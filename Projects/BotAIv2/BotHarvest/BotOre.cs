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

    public static double Worth(Mobile bot, HarvestResource vein) =>
        IsCommon(vein) || !CanWork(bot, vein.ReqSkill) ? 0.0 : vein.ReqSkill / 10.0;

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

                    var worth = Worth(bot, VeinAt(map, found.X, found.Y));

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
