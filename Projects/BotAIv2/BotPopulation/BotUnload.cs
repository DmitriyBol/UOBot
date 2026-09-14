using System;
using System.Collections.Generic;
using Server.Items;
using Server.Logging;
using Server.Mobiles;

namespace Server.BotAI.V2;

/// <summary>
/// Going to the counter when the pack is getting heavy: coin into the account, everything spare onto the
/// market.
///
/// <para>
/// <b>Being unable to move is the one failure a bot cannot work its way out of.</b> Past the engine's
/// overweight line every step costs five stamina and more, and with none left the step is refused outright —
/// so the cure for a full pack is a walk to the bank, and a full pack is exactly what stops the walk. Nothing
/// in this project acted on that: the fact was readable and nobody read it.
/// </para>
///
/// <para>
/// So it is caught early, before the line rather than after it. What goes is decided by what a bot is for:
/// coin belongs in an account, loot belongs on the market where somebody may want it, and the kit, the
/// supplies and the tools of the trade stay exactly where they are — a bot that unloaded its own bandages to
/// make room for a rusty sword has not solved anything.
/// </para>
/// </summary>
public sealed class BotUnload : BotDeed
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotUnload));

    public const string Trade = "unload";

    public static double Heavy { get; set; } = 0.7;

    public static int Purse { get; set; } = 250;

    public static int Risk { get; set; } = 8;

    public static double Prior { get; set; } = 120.0;

    public static double WorkMinutes { get; set; } = 2.0;

    public static int Reach => BotDig.CounterReach;

    private readonly Map _map;

    private readonly Point3D _counter;

    private int _banked;

    private int _listed;

    private int _stored;

    private readonly int _expected;

    public BotUnload(Map map, Point3D counter, int expected = 0, bool immobile = false)
    {
        _map = map;
        _counter = counter;
        _expected = expected;
        _immobile = immobile;
    }

    private readonly bool _immobile;

    public override bool Unpaid => _immobile;

    public override string Kind => Trade;

    public override bool Steadfast => true;

    public override Map Map => _map;

    public override Point3D Where => _counter;

    public override double Expects => Prior;

    public override double Minutes => WorkMinutes;

    public override SkillName? Trains => null;

    public override int Outlay => 0;

    public override double Coin => 0.0;

    public override int Made => 0;

    public override string Stage =>
        _banked > 0 || _listed > 0 || _stored > 0
            ? $"put {_banked}gp away, {_listed} things out and {_stored} in the box"
            : "taking a full pack to the counter";

    public override bool Bend(IBotWilful bot)
    {
        if (_counter == Point3D.Zero)
        {
            return false;
        }

        bot?.Resolve?.Ledger?.Beware(BotGround.CounterKind, _map, _counter);

        return false;
    }

    public override bool Standing => true;

    public override BotDoing Advance(IBotWilful bot)
    {
        var body = bot?.Self;

        if (body == null || _map == null || _map == Map.Internal)
        {
            return BotDoing.Failed("no body");
        }

        if (_counter == Point3D.Zero)
        {
            if (BotLadder.Load(body) > BotLadder.Ceiling(body))
            {
                var freed = Sell(bot, body, out _, out _, out _);

                Shed += freed;
                Stranded++;

                freed += Store(bot, body);

                if (freed > 0)
                {
                    return BotDoing.Done(
                        $"{freed} things put on the market from where it stood, too heavy to walk and no counter known"
                    );
                }

                return Ditch(bot, body);
            }

            return BotDoing.Failed("nowhere known to put it");
        }

        var stuck = BotLadder.Load(body) > BotLadder.Ceiling(body);

        if (!stuck && !body.InRange(_counter, Reach))
        {
            return BotDoing.Walk(_map, _counter, BotArrival.Within(Reach), "to the counter with a full pack");
        }

        if (stuck && !body.InRange(_counter, Reach))
        {
            var shed = Sell(bot, body, out _, out _, out _);

            Shed += shed;
            shed += Store(bot, body);

            if (shed > 0)
            {
                return BotDoing.Done($"{shed} things put on the market from where it stood, too heavy to walk");
            }

            return Ditch(bot, body);
        }

        var carried = body.Backpack?.Items.Count ?? 0;

        _banked = Bank(body);
        _listed = Sell(bot, body, out var kept, out var refused, out var skipped);
        _stored = Store(bot, body);

        return _banked > 0 || _listed > 0 || _stored > 0
            ? BotDoing.Done($"{_banked}gp banked, {_listed} things put on the market, {_stored} put away in the bank")
            : BotDoing.Done(
                $"nothing to leave here: {carried} things in the pack — {kept} its own kit or supplies, "
                + $"{skipped} gold or fixed in place, {refused} the market would not take; "
                + $"the porter counted {_expected} worth leaving when it set out"
            );
    }

    private static int Bank(Mobile body)
    {
        var pack = body.Backpack;
        var carried = pack?.GetAmount(typeof(Gold)) ?? 0;

        var purse = carried - BotPurse.Keeps(body);

        if (purse <= 0 || !pack.ConsumeTotal(typeof(Gold), purse))
        {
            return 0;
        }

        if (Banker.Deposit(body, purse))
        {
            return purse;
        }

        pack.DropItem(new Gold(purse));

        return 0;
    }

    private static int Store(IBotWilful bot, Mobile body)
    {
        var pack = body?.Backpack;
        var box = body?.BankBox;

        if (pack == null || box == null || box.Deleted)
        {
            return 0;
        }

        var keep = Needed(bot);
        Dictionary<Type, int> seen = [];
        var put = 0;

        List<Item> carried = [.. pack.Items];

        for (var i = 0; i < carried.Count; i++)
        {
            var item = carried[i];

            if (item == null || item.Deleted || !item.Movable || item is Gold)
            {
                continue;
            }

            if (BotBinding.IsBound(item, bot.Bond))
            {
                continue;
            }

            var over = Over(keep, seen, item);

            if (over <= 0)
            {
                continue;
            }

            var lot = item;

            if (over < Math.Max(1, item.Amount))
            {
                if (Mobile.LiftItemDupe(item, over) == null)
                {
                    continue;
                }
            }

            if (box.TryDropItem(body, lot, false))
            {
                put++;
                Stowed += Math.Max(1, lot.Amount);

                continue;
            }

            Boxless++;
            pack.DropItem(lot);
        }

        Stored += put;

        return put;
    }

    public static long Stored { get; private set; }

    public static long Stowed { get; private set; }

    public static long Boxless { get; private set; }

    public static long Filled { get; private set; }

    public static long Bespoken { get; private set; }

    public static long Stranded { get; private set; }

    public static long Shed { get; private set; }

    public static long Dumped { get; private set; }

    public static long Exposed { get; private set; }

    public static void Expose() => Exposed++;

    public static long Cornered { get; private set; }

    public static long Immovable { get; private set; }

    public static int WedgedMs { get; set; } = 120000;

    private static readonly Dictionary<Serial, long> _wedged = [];

    public static bool Wedged(Mobile body) =>
        body != null && _wedged.TryGetValue(body.Serial, out var when) && Core.TickCount - when < WedgedMs;

    public static void Corner() => Cornered++;

    public static int SpareTools { get; set; } = 2;

    public static int Hoard { get; set; } = 40;

    public static long Hoarding { get; private set; }

    public static void Hoarded() => Hoarding++;

    public static int BoardMs { get; set; } = 2000;

    private static readonly HashSet<Type> _bespoke = [];

    private static long _read;

    private static bool _everRead;

    public static bool Wanted(IBotWilful bot, Mobile body)
    {
        var pack = body?.Backpack;

        if (pack == null || bot == null)
        {
            return false;
        }

        var now = Core.TickCount;

        if (!_everRead || now - _read >= BoardMs)
        {
            _everRead = true;
            _read = now;
            _bespoke.Clear();

            var wants = BotAuction.Wants;

            for (var i = 0; i < wants.Count; i++)
            {
                var want = wants[i];

                if (want.IsOpen)
                {
                    _bespoke.Add(want.Kind);
                }
            }
        }

        if (_bespoke.Count == 0)
        {
            return false;
        }

        Dictionary<Type, int> keep = null;
        Dictionary<Type, int> seen = null;

        for (var i = 0; i < pack.Items.Count; i++)
        {
            var item = pack.Items[i];

            if (item == null || item.Deleted || !item.Movable || item is Gold)
            {
                continue;
            }

            var kind = item.GetType();

            if (!_bespoke.Contains(kind) || BotBinding.IsBound(item, bot.Bond))
            {
                continue;
            }

            keep ??= Needed(bot);
            seen ??= [];

            if (Over(keep, seen, item) <= 0)
            {
                continue;
            }

            return true;
        }

        return false;
    }

    internal static void Bespeak() => Bespoken++;

    private static int Sell(IBotWilful bot, Mobile body, out int kept, out int refused, out int skipped)
    {
        kept = 0;
        refused = 0;
        skipped = 0;

        var pack = body.Backpack;

        if (pack == null)
        {
            return 0;
        }

        var keep = Needed(bot);
        Dictionary<Type, int> seen = [];
        var listed = 0;
        var filled = 0;

        List<Item> carried = [.. pack.Items];

        for (var i = 0; i < carried.Count; i++)
        {
            var item = carried[i];

            if (item == null || item.Deleted || !item.Movable || item is Gold)
            {
                skipped++;

                continue;
            }

            if (BotBinding.IsBound(item, bot.Bond))
            {
                kept++;

                continue;
            }

            var over = Over(keep, seen, item);

            if (over <= 0)
            {
                kept++;

                continue;
            }

            if (over < Math.Max(1, item.Amount))
            {
                if (Mobile.LiftItemDupe(item, over) == null)
                {
                    kept++;

                    continue;
                }

                kept++;
            }

            var buyer = BotShops.Buyer(body, item, out var offered);
            var floor = buyer != null ? offered : BotShops.Shelf(bot, item.GetType(), 0);
            var priced = floor > 0;

            var held = Math.Max(1, item.Amount);
            var want = BotAuction.Demand(bot, item.GetType());
            var sold = want == null ? 0 : BotAuction.Fill(bot, want, item);

            if (sold > 0)
            {
                filled += sold;

                if (sold >= held)
                {
                    continue;
                }
            }

            if (BotAuction.List(bot, item, BotAuction.Worth(item.GetType(), Math.Max(1, floor)), priced) != null)
            {
                listed++;
            }
            else
            {
                refused++;
            }
        }

        Filled += filled;

        return listed + filled;
    }

    public static int Sellable(IBotWilful bot, Mobile body) => Sellable(bot, body, out _);

    public static int Sellable(IBotWilful bot, Mobile body, out double stones)
    {
        stones = 0.0;

        var pack = body?.Backpack;

        if (pack == null)
        {
            return 0;
        }

        var worth = pack.GetAmount(typeof(Gold)) > BotPurse.Keeps(body) ? 1 : 0;

        var keep = Needed(bot);
        Dictionary<Type, int> seen = [];

        for (var i = 0; i < pack.Items.Count; i++)
        {
            var item = pack.Items[i];

            if (item == null || item.Deleted || !item.Movable || item is Gold)
            {
                continue;
            }

            if (BotBinding.IsBound(item, bot.Bond))
            {
                continue;
            }

            var over = Over(keep, seen, item);

            if (over <= 0)
            {
                continue;
            }

            if (BotAuction.Worthless(item.GetType()))
            {
                continue;
            }

            worth++;

            var many = Math.Max(1, item.Amount);

            stones += over * (item.PileWeight + item.TotalWeight) / (double)many;
        }

        return worth;
    }

    private static BotDoing Ditch(IBotWilful bot, Mobile body)
    {
        var dumped = Dump(bot, body);

        if (dumped > 0)
        {
            Dumped += dumped;

            return BotDoing.Done($"{dumped} things nobody would buy left on the ground, to be able to walk again");
        }

        Immovable++;
        _wedged[body.Serial] = Core.TickCount;

        return BotDoing.Failed("too heavy to walk, and nothing on it the market would take or the ground would hold");
    }

    private static int Dump(IBotWilful bot, Mobile body)
    {
        var pack = body?.Backpack;

        if (pack == null)
        {
            return 0;
        }

        var keep = Needed(bot);
        Dictionary<Type, int> seen = [];
        var loose = new List<Item>();

        for (var i = 0; i < pack.Items.Count; i++)
        {
            var item = pack.Items[i];

            if (item == null || item.Deleted || !item.Movable || item is Gold)
            {
                continue;
            }

            if (BotBinding.IsBound(item, bot.Bond))
            {
                continue;
            }

            if (Over(keep, seen, item) <= 0)
            {
                continue;
            }

            loose.Add(item);
        }

        loose.Sort((a, b) => b.TotalWeight.CompareTo(a.TotalWeight));

        var put = 0;

        for (var i = 0; i < loose.Count; i++)
        {
            if (BotLadder.Load(body) <= BotLadder.Ceiling(body))
            {
                break;
            }

            loose[i].MoveToWorld(body.Location, body.Map);
            put++;
        }

        if (put > 0)
        {
            logger.Warning(
                "{Name} could neither walk nor sell, and has put {Put} things on the ground at {X},{Y} to move again",
                body.Name,
                put,
                body.X,
                body.Y
            );
        }

        return put;
    }

    public static int Census { get; set; } = 6;

    public static string Weighed()
    {
        var bots = BotPopulation.Bots;
        Dictionary<Type, double> stones = [];
        Dictionary<Type, double> spare = [];
        var carried = 0.0;
        var surplus = 0.0;
        var looked = 0;

        for (var i = 0; i < bots.Count; i++)
        {
            var body = bots[i];
            var pack = body?.Backpack;

            if (body == null || body.Deleted || pack == null)
            {
                continue;
            }

            looked++;

            var keep = Needed(body);
            Dictionary<Type, int> seen = [];

            for (var j = 0; j < pack.Items.Count; j++)
            {
                var item = pack.Items[j];

                if (item == null || item.Deleted || item is Gold || BotBinding.IsBound(item, body.Bond))
                {
                    continue;
                }

                var kind = item.GetType();
                var many = Math.Max(1, item.Amount);
                var weight = (double)(item.PileWeight + item.TotalWeight);

                carried += weight;
                stones[kind] = (stones.TryGetValue(kind, out var had) ? had : 0.0) + weight;

                var before = seen.TryGetValue(kind, out var already) ? already : 0;

                seen[kind] = before + many;

                var allowed = keep.TryGetValue(kind, out var cap) ? cap : 0;

                if (allowed >= int.MaxValue)
                {
                    continue;
                }

                var over = Math.Min(many, Math.Max(0, before + many - allowed));

                if (over <= 0)
                {
                    continue;
                }

                var each = weight / many;

                surplus += over * each;
                spare[kind] = (spare.TryGetValue(kind, out var was) ? was : 0.0) + over * each;
            }
        }

        if (looked == 0)
        {
            return "no packs to weigh";
        }

        List<(Type Kind, double Stones)> worst = [];

        foreach (var (kind, weight) in stones)
        {
            worst.Add((kind, weight));
        }

        worst.Sort((a, b) => b.Stones.CompareTo(a.Stones));

        List<string> named = [];
        var many2 = Math.Min(Census, worst.Count);

        for (var i = 0; i < many2; i++)
        {
            var (kind, weight) = worst[i];
            var over = spare.TryGetValue(kind, out var s) ? s : 0.0;

            named.Add(over > 0.0 ? $"{kind.Name} {weight:F0} ({over:F0} surplus)" : $"{kind.Name} {weight:F0}");
        }

        return $"{looked} packs hold {carried:F0} stones, {surplus:F0} of it over what the bots are allowed to "
               + $"keep ({surplus / Math.Max(1.0, carried) * 100.0:F0}%); heaviest: {string.Join(", ", named)}";
    }

    public static Dictionary<Type, int> Keeps(IBotWilful bot) => bot == null ? [] : Needed(bot);

    private static int Over(Dictionary<Type, int> keep, Dictionary<Type, int> seen, Item item)
    {
        var kind = item.GetType();
        var many = Math.Max(1, item.Amount);
        var before = seen.TryGetValue(kind, out var had) ? had : 0;

        seen[kind] = before + many;

        if (!keep.TryGetValue(kind, out var allowed))
        {
            return many;
        }

        if (allowed >= int.MaxValue)
        {
            return 0;
        }

        return Math.Clamp(before + many - allowed, 0, many);
    }

    private static Dictionary<Type, int> Needed(IBotWilful bot)
    {
        var body = bot.Self;
        var kit = bot.Class?.Kit;

        var reagents = kit?.Reagents ?? 0;

        Dictionary<Type, int> keep = new()
        {
            [typeof(Bandage)] = kit?.Bandages ?? 0,
            [typeof(SulfurousAsh)] = reagents,
            [typeof(BlackPearl)] = reagents,
            [typeof(Garlic)] = reagents,
            [typeof(Ginseng)] = reagents,
            [typeof(SpidersSilk)] = reagents,
            [typeof(Nightshade)] = reagents,
            [typeof(Bloodmoss)] = reagents,
            [typeof(MandrakeRoot)] = reagents
        };

        keep[typeof(BlankScroll)] = int.MaxValue;

        if (BotOutfit.Brews(bot.Class))
        {
            keep[typeof(Bottle)] = int.MaxValue;
        }

        if (BotFletching.Kit(body) != null)
        {
            keep[typeof(Feather)] = BotFletching.LeastArrows;
            keep[typeof(Log)] = BotFletching.LeastArrows;
            keep[typeof(Shaft)] = BotFletching.LeastArrows;
        }

        if (BotThread.Kit(body) != null)
        {
            keep[typeof(Leather)] = BotSew.Bolt;
        }

        if (BotAnvil.Kit(body) != null)
        {
            BotAnvil.Keep(body, keep, BotBullion.Enough);
        }

        if (BotFlask.Kit(body) != null)
        {
            keep[typeof(Bottle)] = int.MaxValue;
        }

        var tools = BotOutfit.ToolsFor(bot.Class);

        for (var i = 0; i < tools.Count; i++)
        {
            keep[tools[i]] = SpareTools;
        }

        var bottles = BotOutfit.PotionsFor(bot.Class);

        for (var i = 0; i < bottles.Count; i++)
        {
            var (kind, count) = bottles[i];

            keep[kind] = Math.Max(1, count);
        }

        var ammunition = bot.Bond?.Weapon?.Ammunition;

        if (ammunition != null)
        {
            keep[ammunition] = int.MaxValue;
        }

        return keep;
    }
}

/// <summary>
/// Offers the trip to the counter to any bot whose pack is filling up.
///
/// <para>
/// Only when it is actually heavy, so it costs nothing the rest of the time — and when it is offered it wins,
/// because a bot that cannot walk cannot do anything else either.
/// </para>
/// </summary>
public sealed class BotPorter : IBotProposer
{
    public string Name => "Porter";

    public BotStanding Rung => BotStanding.Free;

    public BotDeed Propose(IBotWilful bot)
    {
        var body = bot?.Self;
        var map = body?.Map;

        if (map == null || map == Map.Internal || !body.Alive)
        {
            return null;
        }

        var laden = BotLadder.Load(body) >= BotLadder.Ceiling(body) * BotUnload.Heavy;
        var flush = (body.Backpack?.TotalGold ?? 0) >= BotUnload.Purse;

        var bespoken = !laden && !flush && BotUnload.Wanted(bot, body);

        var immobile = BotLadder.Load(body) > BotLadder.Ceiling(body);

        var worth = BotUnload.Sellable(bot, body, out var spare);

        if (worth <= 0 && !immobile)
        {
            return null;
        }

        if (worth <= 0 && BotUnload.Wedged(body))
        {
            return null;
        }

        var exposed = worth >= BotUnload.Risk;

        var hoarding = spare >= BotUnload.Hoard;

        if (!laden && !flush && !bespoken && !exposed && !hoarding && !immobile)
        {
            return null;
        }

        var counter = BotGround.Counter(bot, body.Location);

        if (counter == Point3D.Zero && !immobile)
        {
            return null;
        }

        if (worth <= 0)
        {
            BotUnload.Corner();
        }

        if (bespoken)
        {
            BotUnload.Bespeak();
        }

        if (exposed && !laden && !flush)
        {
            BotUnload.Expose();
        }

        if (hoarding && !laden && !flush && !exposed)
        {
            BotUnload.Hoarded();
        }

        return new BotUnload(map, counter, worth, immobile);
    }
}
