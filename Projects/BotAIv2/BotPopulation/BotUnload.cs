using System;
using System.Collections.Generic;
using Server.Items;
using Server.Logging;
using Server.Mobiles;

namespace Server.BotAI.V2;

/// <summary>
/// Going to the counter when the pack is getting heavy: coin into the account, everything spare into the bank box — except
/// what somebody near has said it wants, which stays in the pack for a carrier's walk.
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
/// coin belongs in an account, surplus in the bank box where the bot's own shop by the bank sells it and a peddle takes what
/// nobody asks for to a shopkeeper, and the kit, the supplies and the tools of the trade stay exactly where they are — a bot
/// that unloaded its own bandages to make room for a rusty sword has not solved anything.
/// </para>
///
/// <para>
/// <b>Nothing goes on a market from here any more (01.10.2026, Patrick's order of 30.09: the auction removed, every purchase
/// hand to hand).</b> The porter used to fill wants and list the rest on the auction's stalls from wherever it stood — 4 things
/// straight to a standing order and 409 trips "because the board wanted something in the pack" in the five minutes to 16:52 on
/// 30.09.2026. A want is answered by a walk now (<see cref="BotCourier"/>), so what somebody within a carrier's reach wants is
/// kept in the pack for that walk (<see cref="Filled"/>) and everything else is put away.
/// </para>
/// </summary>
public sealed class BotUnload : BotDeed
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotUnload));

    public const string Trade = "unload";

    public static double Heavy { get; set; } = 0.7;

    public static int Purse { get; set; } = 250;

    public static int Risk { get; set; } = 8;

    public static double Prior { get; set; } = 30.0;

    public static double Stuck { get; set; } = 120.0;

    public static double WorkMinutes { get; set; } = 2.0;

    public static int Reach => BotDig.CounterReach;

    private readonly Map _map;

    private readonly Point3D _counter;

    private int _banked;

    private int _stored;

    private readonly int _expected;

    public BotUnload(Map map, Point3D counter, int expected = 0, bool immobile = false, bool jammed = false)
    {
        _map = map;
        _counter = counter;
        _expected = expected;
        _immobile = immobile;
        _jammed = jammed;
    }

    private readonly bool _immobile;

    private readonly bool _jammed;

    public override bool Unpaid => _immobile || _jammed;

    public override string Kind => Trade;

    public override bool Steadfast => true;

    public override Map Map => _map;

    public override Point3D Where => _counter;

    public override double Expects => _immobile || _jammed ? Stuck : Prior;

    public override double Minutes => WorkMinutes;

    public override SkillName? Trains => null;

    public override int Outlay => 0;

    public override double Coin => 0.0;

    public override int Made => 0;

    public override string Stage =>
        _banked > 0 || _stored > 0
            ? $"put {_banked}gp away and {_stored} lots in the box"
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
                if (Refilling(body))
                {
                    return BotDoing.Failed(Treadmilled(body));
                }

                var freed = Store(bot, body, true);

                Shed += freed;
                Stranded++;

                if (freed > 0)
                {
                    return BotDoing.Done(
                        $"{freed} lots put in the bank box from where it stood, too heavy to walk and no counter known"
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
            if (Refilling(body))
            {
                return BotDoing.Failed(Treadmilled(body));
            }

            var shed = Store(bot, body, true);

            Shed += shed;

            if (shed > 0)
            {
                return BotDoing.Done($"{shed} lots put in the bank box from where it stood, too heavy to walk");
            }

            return Ditch(bot, body);
        }

        var carried = body.Backpack?.Items.Count ?? 0;
        var held = Filled;

        _banked = Bank(body);
        _stored = Store(bot, body, false);

        var forWants = Filled - held;

        return _banked > 0 || _stored > 0
            ? BotDoing.Done($"{_banked}gp banked, {_stored} lots put away in the bank, {forWants} things kept for a carrier to take to somebody's want")
            : BotDoing.Failed(
                $"nothing to leave here: {carried} things in the pack, {forWants} of them kept for somebody's want and the rest its own "
                + $"kit, supplies, gold or what the box would not take; the porter counted {_expected} worth leaving when it set out"
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
        Unbanked += purse;

        return 0;
    }

    public static long Unbanked { get; private set; }

    private static bool Bankable(Container box)
    {
        if (box == null || box.Deleted || box.MaxItems == 0 || box.TotalItems < box.MaxItems)
        {
            return true;
        }

        var items = box.Items;

        for (var i = 0; i < items.Count; i++)
        {
            if (items[i] is Gold { Amount: < 50000 })
            {
                return true;
            }
        }

        return false;
    }

    private static int Store(IBotWilful bot, Mobile body, bool all)
    {
        var pack = body?.Backpack;
        var box = body?.BankBox;

        if (pack == null || box == null || box.Deleted)
        {
            return 0;
        }

        BotSurplus.TidyIfFull(body);

        var keep = Needed(bot);
        Dictionary<Type, int> seen = [];
        Dictionary<Type, int> held = [];
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

            if (!all)
            {
                var hold = Hold(bot, body, item.GetType(), over, held, true);

                if (hold > 0)
                {
                    Filled += hold;
                    over -= hold;

                    if (over <= 0)
                    {
                        continue;
                    }
                }
            }

            var lot = item;

            if (over < Math.Max(1, item.Amount))
            {
                if (Mobile.LiftItemDupe(item, over) == null)
                {
                    continue;
                }
            }

            var kind = lot.GetType();
            var units = Math.Max(1, lot.Amount);
            var capped = keep.TryGetValue(kind, out var cap) && cap > 0 && cap < int.MaxValue;

            if (capped && BotGuildStore.Take(body, lot))
            {
                put++;

                continue;
            }

            if (box.TryDropItem(body, lot, false))
            {
                put++;
                Stowed += units;

                if (capped)
                {
                    BotSurplus.Count(BotSurplus.Box, kind, units);
                }

                continue;
            }

            Boxless++;
            pack.DropItem(lot);

            if (capped)
            {
                BotSurplus.Count(BotSurplus.Refused, kind, units);
            }
        }

        Stored += put;

        return put;
    }

    public static int HoldMs { get; set; } = 900000;

    public static long Overheld { get; private set; }

    private static readonly Dictionary<(Serial, Type), long> _holdingSince = [];

    private static int Hold(IBotWilful bot, Mobile body, Type kind, int over, Dictionary<Type, int> held, bool storing)
    {
        var hold = BotMarket.Holding(bot, kind, over, held);
        var key = (body.Serial, kind);

        if (hold <= 0)
        {
            _holdingSince.Remove(key);

            return 0;
        }

        var now = Core.TickCount;

        if (!_holdingSince.TryGetValue(key, out var since))
        {
            if (_holdingSince.Count > 8192)
            {
                _holdingSince.Clear();
            }

            _holdingSince[key] = now;

            return hold;
        }

        if (now - since < HoldMs)
        {
            return hold;
        }

        if (storing)
        {
            Overheld += hold;
            _holdingSince.Remove(key);
        }

        return 0;
    }

    public static long Stored { get; private set; }

    public static long Unlotted { get; private set; }

    private static bool Boxable(Container box, Item item) =>
        box == null || box.Deleted || box.MaxItems == 0 || box.TotalItems + item.TotalItems + 1 <= box.MaxItems;

    private static bool Boxable(Container box, Item item, HashSet<(Type, int, int)> piles) =>
        Boxable(box, item) || item.Stackable && piles != null && piles.Contains((item.GetType(), item.ItemID, item.Hue));

    private static HashSet<(Type, int, int)> PilesIfFull(Container box) =>
        box is { Deleted: false, MaxItems: > 0 } && box.TotalItems >= box.MaxItems ? BotSurplus.Piles(box) : null;

    public static long Stowed { get; private set; }

    public static long Boxless { get; private set; }

    public static long Filled { get; private set; }

    public static long Stranded { get; private set; }

    public static long Shed { get; private set; }

    public static long Dumped { get; private set; }

    public static long Exposed { get; private set; }

    public static void Expose() => Exposed++;

    public static long Jammed { get; private set; }

    public static void Jam() => Jammed++;

    public static long Cornered { get; private set; }

    public static long Immovable { get; private set; }

    public static int WedgedMs { get; set; } = 120000;

    private static readonly Dictionary<Serial, long> _wedged = [];

    public static int TreadmillMs { get; set; } = 120000;

    public static int PassMs { get; set; } = 60000;

    public static int MostPasses { get; set; } = 3;

    public static long Treadmills { get; private set; }

    private static readonly Dictionary<Serial, (long Tick, int Load, int Count)> _passes = [];

    private static readonly Dictionary<Serial, long> _treadmill = [];

    public static bool Treadmill(Mobile body) =>
        body != null && _treadmill.TryGetValue(body.Serial, out var when) && Core.TickCount - when < TreadmillMs;

    private static bool Refilling(Mobile body)
    {
        var now = Core.TickCount;
        var load = BotLadder.Load(body);

        var count = 1;

        if (_passes.TryGetValue(body.Serial, out var last) && now - last.Tick < PassMs)
        {
            if (load >= last.Load || last.Count + 1 >= MostPasses)
            {
                _passes.Remove(body.Serial);
                _treadmill[body.Serial] = now;
                Treadmills++;

                return true;
            }

            count = last.Count + 1;
        }

        if (_passes.Count > 4096)
        {
            _passes.Clear();
        }

        _passes[body.Serial] = (now, load, count);

        return false;
    }

    private static string Treadmilled(Mobile body) =>
        $"still too heavy to walk at {BotLadder.Load(body)} of {BotLadder.Ceiling(body)} stones, no lighter than when the last pass "
        + "put things away from where it stood; left alone for a while";

    public static bool Wedged(Mobile body) =>
        body != null && _wedged.TryGetValue(body.Serial, out var when) && Core.TickCount - when < WedgedMs;

    public static void Corner() => Cornered++;

    public static int SpareTools { get; set; } = 2;

    public static int Hoard { get; set; } = 40;

    public static int KitSlack { get; set; } = 3;

    public static int KitNear { get; set; } = 60;

    public static int KitWaitMs { get; set; } = 600000;

    public static int BankBy { get; set; } = 12;

    public static long KitTrips { get; private set; }

    public static void Kitted() => KitTrips++;

    public static long BankTrips { get; private set; }

    public static void AtBank() => BankTrips++;

    private static readonly Dictionary<Serial, long> _kitSince = [];

    public static bool KitLong(Mobile body)
    {
        var now = Core.TickCount;

        if (!_kitSince.TryGetValue(body.Serial, out var since))
        {
            if (_kitSince.Count > 4096)
            {
                _kitSince.Clear();
            }

            _kitSince[body.Serial] = now;

            return false;
        }

        return now - since >= KitWaitMs;
    }

    public static void KitGone(Mobile body) => _kitSince.Remove(body.Serial);

    public static long Hoarding { get; private set; }

    public static void Hoarded() => Hoarding++;

    public static int Sellable(IBotWilful bot, Mobile body) => Sellable(bot, body, out _);

    public static int Sellable(IBotWilful bot, Mobile body, out double stones) => Sellable(bot, body, out stones, out _);

    public static int Sellable(IBotWilful bot, Mobile body, out double stones, out int kit)
    {
        stones = 0.0;
        kit = 0;

        var pack = body?.Backpack;

        if (pack == null)
        {
            return 0;
        }

        var box = body.FindBankNoCreate();

        var worth = pack.GetAmount(typeof(Gold)) > BotPurse.Keeps(body) && Bankable(box) ? 1 : 0;

        var keep = Needed(bot);
        Dictionary<Type, int> seen = [];
        Dictionary<Type, int> held = [];
        var piles = PilesIfFull(box);

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

            over -= Hold(bot, body, item.GetType(), over, held, false);

            if (over <= 0)
            {
                continue;
            }

            if (!Boxable(box, item, piles))
            {
                Unlotted++;

                continue;
            }

            worth++;

            if (keep.TryGetValue(item.GetType(), out var cap) && cap > 0)
            {
                kit += over;
            }

            var many = Math.Max(1, item.Amount);

            stones += over * (item.PileWeight + item.TotalWeight) / (double)many;
        }

        return worth;
    }

    public static int Refused(IBotWilful bot, Mobile body, Dictionary<Type, int> into)
    {
        var pack = body?.Backpack;
        var box = body?.FindBankNoCreate();

        if (pack == null || box == null || box.Deleted || box.MaxItems <= 0 || box.TotalItems < box.MaxItems)
        {
            return 0;
        }

        var keep = Needed(bot);
        Dictionary<Type, int> seen = [];
        var piles = BotSurplus.Piles(box);
        var units = 0;

        for (var i = 0; i < pack.Items.Count; i++)
        {
            var item = pack.Items[i];

            if (item == null || item.Deleted || !item.Movable || item is Gold || BotBinding.IsBound(item, bot.Bond))
            {
                continue;
            }

            var over = Over(keep, seen, item);

            if (over <= 0 || !keep.TryGetValue(item.GetType(), out var cap) || cap <= 0 || cap >= int.MaxValue
                || Boxable(box, item, piles))
            {
                continue;
            }

            into[item.GetType()] = into.GetValueOrDefault(item.GetType()) + over;
            units += over;
        }

        return units;
    }

    public static int Holds(IBotWilful bot, Type kind, int over) =>
        bot?.Self == null ? 0 : Hold(bot, bot.Self, kind, over, [], false);

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

            if (keep.TryGetValue(loose[i].GetType(), out var cap) && cap > 0 && cap < int.MaxValue)
            {
                BotSurplus.Count(BotSurplus.Ground, loose[i].GetType(), Math.Max(1, loose[i].Amount));
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

        Dictionary<Type, int> units = [];
        Dictionary<Type, int> slots = [];
        var overBots = 0;
        var overUnits = 0;
        var worstUnits = 0;
        var worst1 = "";
        var boxes = 0;
        var full = 0;

        Dictionary<Type, int> blocked = [];
        var blockedBots = 0;
        _metal.Clear();

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
            var mine = 0;

            Boxes(body, ref boxes, ref full, slots);

            if (Refused(body, body, blocked) > 0)
            {
                blockedBots++;
            }

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

                if (allowed > 0)
                {
                    mine += over;
                    units[kind] = units.GetValueOrDefault(kind) + over;
                }
            }

            if (mine > 0)
            {
                overBots++;
                overUnits += mine;

                if (mine > worstUnits)
                {
                    worstUnits = mine;
                    worst1 = body.Name;
                }
            }
        }

        if (looked == 0)
        {
            return "no packs to weigh";
        }

        BotAnvil.Boxed(_metal);

        var capped = Capped(overBots, overUnits, units, worst1, worstUnits) + "; " + Boxed(boxes, full, slots) + "; "
                     + Blocked(blockedBots, blocked) + "; " + BotSurplus.Describe() + "; " + Metal();

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
               + $"keep ({surplus / Math.Max(1.0, carried) * 100.0:F0}%); heaviest: {string.Join(", ", named)}; {capped}";
    }

    private static string Capped(int bots, int units, Dictionary<Type, int> byKind, string worst, int worstUnits)
    {
        if (bots == 0)
        {
            return "no bot carries more of a capped kind than it keeps";
        }

        List<(Type Kind, int Units)> kinds = [];

        foreach (var (kind, many) in byKind)
        {
            kinds.Add((kind, many));
        }

        kinds.Sort((a, b) => b.Units.CompareTo(a.Units));

        List<string> named = [];

        for (var i = 0; i < kinds.Count && i < Census; i++)
        {
            named.Add($"{kinds[i].Kind.Name} {kinds[i].Units}");
        }

        return $"over the allowance in a capped kind: {bots} bots, {units} units ({string.Join(", ", named)}), the worst {worst} by {worstUnits}";
    }

    public static int OverMs { get; set; } = 30000;

    private static readonly Dictionary<Serial, (int Units, long Tick)> _overs = [];

    public static int OverUnits(BotMobile bot)
    {
        var pack = bot?.Backpack;

        if (pack == null)
        {
            return 0;
        }

        var now = Core.TickCount;

        if (_overs.TryGetValue(bot.Serial, out var known) && now - known.Tick < OverMs)
        {
            return known.Units;
        }

        var keep = Needed(bot);
        Dictionary<Type, int> seen = [];
        var units = 0;

        for (var i = 0; i < pack.Items.Count; i++)
        {
            var item = pack.Items[i];

            if (item == null || item.Deleted || item is Gold || BotBinding.IsBound(item, bot.Bond))
            {
                continue;
            }

            if (keep.TryGetValue(item.GetType(), out var cap) && cap > 0)
            {
                units += Over(keep, seen, item);
            }
        }

        if (_overs.Count > 4096)
        {
            _overs.Clear();
        }

        _overs[bot.Serial] = (units, now);

        return units;
    }

    private static void Boxes(Mobile body, ref int boxes, ref int full, Dictionary<Type, int> slots)
    {
        var box = body.FindBankNoCreate();

        if (box == null || box.Deleted)
        {
            return;
        }

        boxes++;

        var held = box.Items;

        for (var i = 0; i < held.Count; i++)
        {
            if (held[i] is BaseIngot { Deleted: false } bar)
            {
                _metal[bar.GetType()] = _metal.GetValueOrDefault(bar.GetType()) + bar.Amount;
            }
        }

        if (box.MaxItems <= 0 || box.TotalItems < box.MaxItems)
        {
            return;
        }

        full++;

        var items = box.Items;

        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];

            if (item != null && !item.Deleted)
            {
                var kind = item.GetType();

                slots[kind] = slots.GetValueOrDefault(kind) + 1 + item.TotalItems;
            }
        }
    }

    private static readonly Dictionary<Type, int> _metal = [];

    private static string Blocked(int bots, Dictionary<Type, int> byKind)
    {
        if (bots == 0)
        {
            return "no capped surplus is kept out of a pack's own box by a full box";
        }

        List<(Type Kind, int Units)> kinds = [];
        var units = 0;

        foreach (var (kind, many) in byKind)
        {
            kinds.Add((kind, many));
            units += many;
        }

        kinds.Sort((a, b) => b.Units.CompareTo(a.Units));

        List<string> named = [];

        for (var i = 0; i < kinds.Count && i < Census; i++)
        {
            named.Add($"{kinds[i].Kind.Name} {kinds[i].Units}");
        }

        return $"{bots} bots carry {units} units of capped surplus their full box has no place for ({string.Join(", ", named)})";
    }

    private static string Metal()
    {
        if (_metal.Count == 0)
        {
            return "no bars in any bank box";
        }

        List<(Type Kind, int Units)> kinds = [];
        var units = 0;

        foreach (var (kind, many) in _metal)
        {
            kinds.Add((kind, many));
            units += many;
        }

        kinds.Sort((a, b) => b.Units.CompareTo(a.Units));

        List<string> named = [];

        for (var i = 0; i < kinds.Count; i++)
        {
            named.Add($"{BotAnvil.Named(kinds[i].Kind)} {kinds[i].Units}");
        }

        return $"bars in bank boxes: {units} ({string.Join(", ", named)})";
    }

    private static string Boxed(int boxes, int full, Dictionary<Type, int> slots)
    {
        if (full == 0)
        {
            return $"{boxes} bank boxes, none full";
        }

        List<(Type Kind, int Places)> kinds = [];

        foreach (var (kind, places) in slots)
        {
            kinds.Add((kind, places));
        }

        kinds.Sort((a, b) => b.Places.CompareTo(a.Places));

        List<string> named = [];

        for (var i = 0; i < kinds.Count && i < Census; i++)
        {
            named.Add($"{kinds[i].Kind.Name} {kinds[i].Places}");
        }

        return $"{full} of {boxes} bank boxes full (their places: {string.Join(", ", named)})";
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
            BotAnvil.Keep(body, keep, Math.Max(BotBullion.Enough, BotSmith.Short(body)));
        }

        if (BotFlask.Kit(body) != null)
        {
            keep[typeof(Bottle)] = int.MaxValue;
        }

        if (BotTinkering.Kit(body) != null)
        {
            keep[typeof(TinkerTools)] = Math.Max(keep.GetValueOrDefault(typeof(TinkerTools)), BotTinkering.Spares);
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

            keep[kind] = Math.Max(0, Math.Max(1, count) - BotAdvance.Owing(body, kind));
        }

        var thrown = BotArmoury.Kept(body, out _);

        if (thrown != null)
        {
            keep[thrown] = Math.Max(keep.TryGetValue(thrown, out var already) ? already : 0, BotArmoury.Stock);
        }

        if (BotRaise.Reads(body))
        {
            keep[typeof(ResurrectionScroll)] = Math.Max(keep.GetValueOrDefault(typeof(ResurrectionScroll)), BotRaise.Stock);
        }

        var ammunition = bot.Bond?.Weapon?.Ammunition;

        if (ammunition != null)
        {
            keep[ammunition] = int.MaxValue;
        }

        BotTrading.Keep(bot, keep);

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

        var immobile = BotLadder.Load(body) > BotLadder.Ceiling(body);

        var jammed = !immobile && !BotYield.Pocket(body);

        BotSurplus.TidyIfFull(body);

        var worth = BotUnload.Sellable(bot, body, out var spare, out var kit);

        if (worth <= 0 && !immobile)
        {
            return null;
        }

        if (worth <= 0 && BotUnload.Wedged(body))
        {
            return null;
        }

        if (immobile && BotUnload.Treadmill(body))
        {
            return null;
        }

        var exposed = worth >= BotUnload.Risk;

        var hoarding = spare >= BotUnload.Hoard;

        var counter = Point3D.Zero;
        var looked = false;

        var kitted = false;
        var banked = false;
        var kitLong = false;

        if (kit >= BotUnload.KitSlack)
        {
            kitLong = BotUnload.KitLong(body);
        }
        else
        {
            BotUnload.KitGone(body);
        }

        if (!laden && !flush && !exposed && !hoarding && !immobile && !jammed && worth > 0)
        {
            counter = BotGround.Counter(bot, body.Location);
            looked = true;

            if (counter != Point3D.Zero)
            {
                var tiles = Math.Max(Math.Abs(counter.X - body.X), Math.Abs(counter.Y - body.Y));

                var goods = worth - (body.Backpack.TotalGold > BotPurse.Keeps(body) ? 1 : 0);

                banked = goods > 0 && tiles <= BotUnload.BankBy;

                kitted = !banked && kit >= BotUnload.KitSlack && (tiles <= BotUnload.KitNear || kitLong);
            }
        }

        var roomy = !laden && !flush && !exposed && !hoarding && !immobile && !jammed && !kitted && !banked && BotRoom.Pressing(body);

        if (!laden && !flush && !exposed && !hoarding && !immobile && !jammed && !kitted && !banked && !roomy)
        {
            return null;
        }

        if (!looked)
        {
            counter = BotGround.Counter(bot, body.Location);
        }

        if (counter == Point3D.Zero && !immobile)
        {
            return null;
        }

        if (worth <= 0)
        {
            BotUnload.Corner();
        }

        if (exposed && !laden && !flush)
        {
            BotUnload.Expose();
        }

        if (hoarding && !laden && !flush && !exposed)
        {
            BotUnload.Hoarded();
        }

        if (jammed)
        {
            BotUnload.Jam();
        }

        if (kitted)
        {
            BotUnload.Kitted();
        }

        if (banked)
        {
            BotUnload.AtBank();
        }

        if (roomy)
        {
            BotRoom.Tripped();
        }

        return new BotUnload(map, counter, worth, immobile, jammed);
    }
}
