using System;
using System.Collections.Generic;
using Server.Engines.Harvest;
using Server.Items;
using Server.Logging;
using Server.Targeting;

namespace Server.BotAI.V2;

/// <summary>
/// Ore: what is in a hill, whether this bot can get it out, how it is dug, and what it becomes.
///
/// <para>
/// <b>The work is the engine's, not ours.</b> Digging goes through the shard's own
/// <see cref="HarvestSystem"/> — the same call a player's double-click makes — so the swing, the skill
/// check, the yield and the seam running dry are the real ones. A bot mining is a miner, not a bot being
/// credited with ore. That also makes the thing this project measures real: skill gain here is the
/// engine raising a number, not us deciding a number should go up.
/// </para>
/// </summary>
public static class BotOre
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotOre));

    public static int Reach { get; set; } = 12;

    public static int SwingReach => Mining.System?.OreAndStone?.MaxRange ?? 2;

    public static int FireReach { get; set; } = 2;

    public static int WorthSmelting { get; set; } = 4;

    public static HarvestResource VeinAt(Map map, int x, int y)
    {
        var definition = Mining.System?.OreAndStone;

        if (definition == null || map == null || map == Map.Internal)
        {
            return null;
        }

        return definition
            .GetVeinAt(map, x / definition.BankWidth, y / definition.BankHeight)
            ?.PrimaryResource;
    }

    public static int LastRocks { get; private set; }

    public static int LastEmpty { get; private set; }

    public static int Left(Map map, int x, int y)
    {
        if (map == null || map == Map.Internal)
        {
            return 0;
        }

        return Mining.System?.OreAndStone?.GetBank(map, x, y)?.Current ?? 0;
    }

    public static int StockedLeast { get; set; } = 4;

    public static bool Stocked(Map map, Point3D seam)
    {
        var system = Mining.System;

        if (system == null || map == null || map == Map.Internal)
        {
            return true;
        }

        var rocks = 0;

        for (var radius = 1; radius <= Reach; radius++)
        {
            for (var dx = -radius; dx <= radius; dx++)
            {
                for (var dy = -radius; dy <= radius; dy++)
                {
                    if (Math.Abs(dx) != radius && Math.Abs(dy) != radius)
                    {
                        continue;
                    }

                    var x = seam.X + dx;
                    var y = seam.Y + dy;

                    if (Examine(map, x, y, system) == null)
                    {
                        continue;
                    }

                    if (Left(map, x, y) >= StockedLeast)
                    {
                        return true;
                    }

                    rocks++;
                }
            }
        }

        return rocks == 0;
    }

    public static int RespawnMs =>
        (int)(Mining.System?.OreAndStone?.MaxRespawn ?? System.TimeSpan.FromMinutes(20.0)).TotalMilliseconds;

    public static double Chance(Mobile bot, Map map, int x, int y)
    {
        var def = Mining.System?.OreAndStone;

        if (bot == null || def == null || map == null)
        {
            return 1.0;
        }

        var vein = VeinAt(map, x, y);

        if (vein == null)
        {
            return 1.0;
        }

        var skill = bot.Skills[def.Skill].Base;

        var resource = skill < vein.ReqSkill || skill < vein.MinSkill
            ? def.Resources is { Length: > 0 } ? def.Resources[0] : vein
            : vein;

        var span = resource.MaxSkill - resource.MinSkill;

        if (span <= 0.0)
        {
            return skill >= resource.MinSkill ? 1.0 : 0.0;
        }

        return System.Math.Clamp((skill - resource.MinSkill) / span, 0.0, 1.0);
    }

    public static bool IsCommon(HarvestResource vein) => vein == null || vein.ReqSkill <= 0.0;

    public static bool CanWork(Mobile bot, double required) =>
        bot != null && bot.Skills[SkillName.Mining].Value >= required;

    public static double Worth(Mobile bot, Map map, int x, int y) => Premium(bot, VeinOf(map, x, y), null);

    public static HarvestVein VeinOf(Map map, int x, int y)
    {
        var definition = Mining.System?.OreAndStone;

        if (definition == null || map == null || map == Map.Internal)
        {
            return null;
        }

        return definition.GetVeinAt(map, x / definition.BankWidth, y / definition.BankHeight);
    }

    public static int Batch { get; set; } = 20;

    public static double WorstPremium { get; set; } = -0.9;

    public static int CounterMs { get; set; } = 600000;

    private static readonly Dictionary<Type, Type> _ingots = [];

    private static readonly Dictionary<Type, (int Price, long Tick)> _counters = [];

    public static Type IngotOf(Type ore)
    {
        if (ore == null)
        {
            return null;
        }

        if (_ingots.TryGetValue(ore, out var known))
        {
            return known;
        }

        Type ingot = null;
        var resource = CraftResources.GetFromType(ore);

        if (resource != CraftResource.None && CraftResources.GetInfo(resource)?.ResourceTypes is { Length: > 0 } types
            && typeof(BaseIngot).IsAssignableFrom(types[0]))
        {
            ingot = types[0];
        }

        _ingots[ore] = ingot;

        return ingot;
    }

    public static int CounterPays(Type ingot)
    {
        if (ingot == null)
        {
            return 0;
        }

        var now = Core.TickCount;

        if (_counters.TryGetValue(ingot, out var known) && now - known.Tick < CounterMs)
        {
            return known.Price;
        }

        var best = 0;
        var shops = BotShops.Shops;

        if (shops.Count == 0)
        {
            return 0;
        }

        var sample = ingot.CreateInstance<Item>();

        if (sample != null)
        {

            for (var i = 0; i < shops.Count; i++)
            {
                if (BotShops.Buys(shops[i], sample, out var price) && price > best)
                {
                    best = price;
                }
            }

            sample.Delete();
        }

        _counters[ingot] = (best, now);

        return best;
    }

    public static double IngotWorth(Type ingot)
    {
        if (ingot == null)
        {
            return 1.0;
        }

        var batch = Math.Max(1, Batch);
        var guess = ingot == typeof(IronIngot) ? BotDig.GoldPerIngot : Math.Max(1, BotAnvil.Reckon(ingot));
        var bots = BotGlut.Yield(ingot, batch, guess) / (double)batch;

        return Math.Max(1.0, Math.Max(CounterPays(ingot), bots));
    }

    public static double Premium(Mobile bot, HarvestVein vein, Type seen)
    {
        var metal = vein?.PrimaryResource;

        if (bot == null || IsCommon(metal) || !CanWork(bot, metal.ReqSkill))
        {
            return 0.0;
        }

        var iron = IngotWorth(typeof(IronIngot));
        var fallback = Math.Clamp(vein.ChanceToFallback, 0.0, 1.0);
        var own = IngotWorth(IngotOf(metal.Types is { Length: > 0 } types ? types[0] : null));

        if (seen != null && seen != typeof(IronIngot))
        {
            own = Math.Max(own, IngotWorth(seen));
        }

        var perBar = fallback * iron + (1.0 - fallback) * own;

        return Math.Max(WorstPremium, perBar / iron - 1.0);
    }

    public static double Premium(Mobile bot, Map map, Point3D where, int seenRank)
    {
        Type seen = null;

        if (seenRank > (int)CraftResource.Iron && CraftResources.GetInfo((CraftResource)seenRank)?.ResourceTypes is { Length: > 0 } types
            && typeof(BaseIngot).IsAssignableFrom(types[0]))
        {
            seen = types[0];
        }

        return Premium(bot, VeinOf(map, where.X, where.Y), seen);
    }

    public static long Passed { get; private set; }

    public static long ChoseIron { get; private set; }

    public static long ChoseWanted { get; private set; }

    public static long ChoseUnwanted { get; private set; }

    public static void Chose(double premium, bool passed)
    {
        if (premium > 0.0)
        {
            ChoseWanted++;
        }
        else if (premium < 0.0)
        {
            ChoseUnwanted++;
        }
        else
        {
            ChoseIron++;
        }

        if (passed)
        {
            Passed++;
        }
    }

    private static readonly Dictionary<Type, (long Smelted, long Peddled, long Sold, long Paid)> _bars = [];

    public static void Smelted(Type ingot, int units) => Book(ingot, units, 0, 0, 0);

    public static void Peddled(Type ingot, int units, int gold)
    {
        Book(ingot, 0, units, 0, gold);

        if (Coloured(ingot) && gold > 0)
        {
            Purse();
            _purse -= gold;
            ColouredBars += Math.Max(0, units);
            ColouredGold += gold;
        }
    }

    public static void Sold(Type ingot, int units) => Book(ingot, 0, 0, units, 0);

    private static void Book(Type kind, int smelted, int peddled, int sold, int paid)
    {
        if (kind == null || !typeof(BaseIngot).IsAssignableFrom(kind))
        {
            return;
        }

        var (s, p, b, g) = _bars.GetValueOrDefault(kind);
        _bars[kind] = (s + Math.Max(0, smelted), p + Math.Max(0, peddled), b + Math.Max(0, sold), g + Math.Max(0, paid));
    }

    public static int ColouredPerHour { get; set; } = 10000;

    public static int PurseHoldMs { get; set; } = 600000;

    private static double _purse;

    private static long _purseTick;

    private static bool _pursePrimed;

    private static readonly Dictionary<Serial, (int Gold, long Tick)> _held = [];

    public static long ColouredBars { get; private set; }

    public static long ColouredGold { get; private set; }

    public static long PurseShut { get; private set; }

    public static long PurseCut { get; private set; }

    public static bool Coloured(Type kind) =>
        kind != null && kind != typeof(IronIngot) && typeof(BaseIngot).IsAssignableFrom(kind);

    private static double Purse()
    {
        var most = Math.Max(0, ColouredPerHour);
        var now = Core.TickCount;

        if (!_pursePrimed)
        {
            _pursePrimed = true;
            _purse = most;
            _purseTick = now;

            return _purse;
        }

        var elapsed = now - _purseTick;

        if (elapsed > 0)
        {
            _purse = Math.Min(most, _purse + elapsed * (most / 3600000.0));
            _purseTick = now;
        }

        return _purse;
    }

    public static int CounterRoom(Mobile bot, Type kind, int price)
    {
        if (!Coloured(kind))
        {
            return int.MaxValue;
        }

        if (ColouredPerHour <= 0 || price <= 0 || bot == null)
        {
            return 0;
        }

        var free = Purse();
        var now = Core.TickCount;

        foreach (var (who, (gold, tick)) in _held)
        {
            if (who != bot.Serial && now - tick < PurseHoldMs)
            {
                free -= gold;
            }
        }

        return free < price ? 0 : (int)(free / price);
    }

    public static void Hold(Mobile bot, Type kind, int gold, bool cut)
    {
        if (bot == null || !Coloured(kind))
        {
            return;
        }

        if (_held.Count > 512)
        {
            _held.Clear();
        }

        _held[bot.Serial] = (Math.Max(0, gold), Core.TickCount);

        if (cut)
        {
            PurseCut++;
        }
    }

    public static void Release(Mobile bot)
    {
        if (bot != null)
        {
            _held.Remove(bot.Serial);
        }
    }

    public static void Shut() => PurseShut++;

    private static string PurseLine()
    {
        if (ColouredPerHour <= 0)
        {
            return $"coloured bars to a shopkeeper: {ColouredBars} for {ColouredGold}gp since the load; not offered (the hourly purse is off)";
        }

        var held = 0;
        var now = Core.TickCount;

        foreach (var (_, (gold, tick)) in _held)
        {
            if (now - tick < PurseHoldMs)
            {
                held += gold;
            }
        }

        return $"coloured bars to a shopkeeper: {ColouredBars} for {ColouredGold}gp since the load, at most {ColouredPerHour}gp an hour ({Math.Max(0, (int)Purse())}gp of it free now, {held}gp held by loads on their way); {PurseShut} coloured kinds not offered for a spent purse, {PurseCut} loads cut to what was left of it";
    }

    public static string Describe()
    {
        var say = new System.Text.StringBuilder();

        say.Append(
            $"ore: {ChoseIron + ChoseWanted + ChoseUnwanted} seams chosen, {ChoseIron} iron (or past the miner's skill), {ChoseWanted} a metal worth more than iron, {ChoseUnwanted} a metal worth less; {Passed} coloured veins passed for a metal nobody buys; bars smelted/to a shopkeeper/to a bot/the rest:"
        );

        var any = false;

        foreach (var (kind, (smelted, peddled, sold, paid)) in _bars)
        {
            var name = kind.Name.EndsWith("Ingot", StringComparison.Ordinal) ? kind.Name[..^5] : kind.Name;

            say.Append(
                $" {name} {smelted}/{peddled}/{sold}/{smelted - peddled - sold} (worth {IngotWorth(kind):F1}gp, the counter pays {CounterPays(kind)}gp, {paid}gp paid),"
            );
            any = true;
        }

        if (!any)
        {
            say.Append(" none yet,");
        }

        say.Length--;
        say.Append("; ").Append(PurseLine());

        return say.ToString();
    }

    public static void Forget()
    {
        Passed = 0;
        ChoseIron = 0;
        ChoseWanted = 0;
        ChoseUnwanted = 0;
        _bars.Clear();
        _counters.Clear();
        ColouredBars = 0;
        ColouredGold = 0;
        PurseShut = 0;
        PurseCut = 0;
        _held.Clear();
        _pursePrimed = false;
    }

    public static string NameOf(HarvestResource vein)
    {
        var types = vein?.Types;

        return types is { Length: > 0 } ? types[0].Name : "rock";
    }

    public static Item Tool(Mobile bot)
    {
        var pack = bot?.Backpack;

        if (pack == null)
        {
            return null;
        }

        Item tool = BotOutfit.Oldest<Pickaxe>(pack);

        return tool ?? BotOutfit.Oldest<Shovel>(pack);
    }

    private const int SmallPile = 0x19B7;

    public static bool TooLittle(BaseOre ore) => ore != null && ore.ItemID == SmallPile && ore.Amount < 2;

    public static long Dust { get; private set; }

    public static int Carried(Mobile bot)
    {
        var pack = bot?.Backpack;

        if (pack == null)
        {
            return 0;
        }

        var carried = 0;
        var items = pack.Items;

        for (var i = 0; i < items.Count; i++)
        {
            if (items[i] is BaseOre { Deleted: false } ore && !TooLittle(ore))
            {
                carried += ore.Amount;
            }
        }

        return carried;
    }

    public static int Ingots(Mobile bot) => bot?.Backpack?.GetAmount(typeof(BaseIngot)) ?? 0;

    public static IPoint3D Find(
        Mobile bot,
        out HarvestSystem system,
        List<Point3D> skip = null,
        Point3D anchor = default,
        int leash = 0,
        Point3D from = default
    )
    {
        system = Mining.System;

        var map = bot?.Map;

        if (system == null || map == null || map == Map.Internal)
        {
            return null;
        }

        var origin = from == Point3D.Zero ? bot.Location : from;

        IPoint3D nearest = null;
        IPoint3D richest = null;

        var bestWorth = 0.0;

        LastRocks = 0;
        LastEmpty = 0;

        for (var radius = 1; radius <= Reach; radius++)
        {
            for (var dx = -radius; dx <= radius; dx++)
            {
                for (var dy = -radius; dy <= radius; dy++)
                {
                    if (Math.Abs(dx) != radius && Math.Abs(dy) != radius)
                    {
                        continue;
                    }

                    var x = origin.X + dx;
                    var y = origin.Y + dy;

                    if (leash > 0 && (Math.Abs(x - anchor.X) > leash || Math.Abs(y - anchor.Y) > leash))
                    {
                        continue;
                    }

                    if (Skipped(skip, x, y) || BotFooting.Footless(map, x, y))
                    {
                        continue;
                    }

                    var found = Examine(map, x, y, system);

                    if (found == null)
                    {
                        continue;
                    }

                    LastRocks++;

                    if (Left(map, x, y) <= 0)
                    {
                        LastEmpty++;

                        continue;
                    }

                    nearest ??= found;

                    var worth = Worth(bot, map, found.X, found.Y);

                    if (worth <= bestWorth)
                    {
                        continue;
                    }

                    richest = found;
                    bestWorth = worth;
                }
            }
        }

        return richest ?? nearest;
    }

    private static bool Skipped(List<Point3D> skip, int x, int y)
    {
        if (skip == null)
        {
            return false;
        }

        for (var i = 0; i < skip.Count; i++)
        {
            if (skip[i].X == x && skip[i].Y == y)
            {
                return true;
            }
        }

        return false;
    }

    public static IPoint3D Examine(Map map, int x, int y, HarvestSystem system)
    {
        if (map == null || system == null || x < 0 || y < 0 || x >= map.Width || y >= map.Height)
        {
            return null;
        }

        foreach (var tile in map.Tiles.GetStaticTiles(x, y))
        {
            if (Workable(system, system.GetDefinition(tile.ID & 0x3FFF, false)))
            {
                return new StaticTarget(new Point3D(x, y, tile.Z), tile.ID);
            }
        }

        var land = map.Tiles.GetLandTile(x, y);

        return Workable(system, system.GetDefinition(land.ID & 0x3FFF, true))
            ? new LandTarget(new Point3D(x, y, map.GetAverageZ(x, y)), map)
            : null;
    }

    private static bool Workable(HarvestSystem system, HarvestDefinition definition) =>
        definition != null && (system is not Mining mining || definition == mining.OreAndStone);

    public static bool Swing(Mobile bot, Item tool, HarvestSystem system, IPoint3D target)
    {
        if (bot == null || tool == null || system == null || target == null)
        {
            return false;
        }

        bot.Direction = bot.GetDirectionTo(new Point3D(target.X, target.Y, target.Z));

        system.StartHarvesting(bot, tool, target);

        return true;
    }

    public static int Melt(Mobile bot)
    {
        var pack = bot?.Backpack;

        if (pack == null)
        {
            return 0;
        }

        var forge = Fire(bot, FireReach);

        if (forge == null)
        {
            return 0;
        }

        var before = Ingots(bot);

        List<Item> carried = [.. pack.Items];
        var piles = 0;
        var dust = 0;

        for (var i = 0; i < carried.Count; i++)
        {
            if (carried[i] is not BaseOre ore || ore.Deleted)
            {
                continue;
            }

            if (TooLittle(ore))
            {
                dust++;

                continue;
            }

            ore.OnDoubleClick(bot);

            var target = bot.Target;

            if (target == null)
            {
                continue;
            }

            target.Invoke(bot, forge);
            piles++;
        }

        var made = Ingots(bot) - before;
        Dust += dust;

        if (piles > 0)
        {
            logger.Information(
                "{Name} put {Piles} piles of ore into the fire at {Where} and got {Ingots} ingots, {Dust} single ore left as too little to smelt",
                bot.Name,
                piles,
                bot.Location,
                made,
                dust
            );
        }

        return made > 0 ? made : 0;
    }

    public static object Fire(Mobile bot, int range)
    {
        var map = bot?.Map;

        if (map == null || map == Map.Internal)
        {
            return null;
        }

        foreach (var item in map.GetItemsInRange(bot.Location, range))
        {
            if (BotGround.IsForgeId(item.ItemID))
            {
                return item;
            }
        }

        var origin = bot.Location;

        for (var dx = -range; dx <= range; dx++)
        {
            for (var dy = -range; dy <= range; dy++)
            {
                var x = origin.X + dx;
                var y = origin.Y + dy;

                if (x < 0 || y < 0 || x >= map.Width || y >= map.Height)
                {
                    continue;
                }

                foreach (var tile in map.Tiles.GetStaticAndMultiTiles(x, y))
                {
                    if (BotGround.IsForgeId(tile.ID))
                    {
                        return new StaticTarget(new Point3D(x, y, tile.Z), tile.ID);
                    }
                }
            }
        }

        return null;
    }
}
