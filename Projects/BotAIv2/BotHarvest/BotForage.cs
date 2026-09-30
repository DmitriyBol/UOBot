using System;
using System.Collections.Generic;
using Server.Items;
using Server.Logging;

namespace Server.BotAI.V2;

/// <summary>
/// Picking up the reagents the world leaves lying about, and putting them on the board.
///
/// <para>
/// <b>Free money the population has been walking past since the day it was raised.</b> The world spawns
/// nightshade, sulphurous ash, garlic and the rest on the ground and refills them on their own clock; every
/// bot on this shard has stepped over them for a fortnight. It is the cheapest income there is — no fight,
/// no tool, no material, no skill — and unlike a vein or a shopkeeper it costs nobody anything to take.
/// </para>
///
/// <para>
/// <b>Asked of the engine as a family, not written out as a list of eight herbs.</b> Everything the world
/// calls a reagent derives from <c>BaseReagent</c>, so one type check covers all of them and covers any that
/// are ever added — which is the same reason the armoury reads the craft systems instead of keeping a table
/// of hauberks. A list of names here would be right today and quietly wrong the first time somebody adds a
/// herb.
/// </para>
///
/// <para>
/// <b>And it ends on the board rather than in a pocket.</b> Two of these are worth pennies to the bot that
/// picked them up and a great deal to the mage two fields away who cannot cast without them — this shard's
/// scribes and casters buy reagents constantly. A gathering errand that ended at the backpack would be a bot
/// hoarding somebody else's spellbook.
/// </para>
/// </summary>
public sealed class BotForage : BotDeed
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotForage));

    public const string Trade = "forage";

    public static double Prior { get; set; } = 20.0;

    public static double WorkMinutes { get; set; } = 1.0;

    public static int Reach { get; set; } = 18;

    public static int Touch { get; set; } = 2;

    public static double FillFraction { get; set; } = 0.8;

    public static int Guess { get; set; } = 5;

    public static bool ListGoods { get; set; } = true;

    public static long Unreachable { get; private set; }

    public static void ForgetGround() => Unreachable = 0;

    private readonly Map _map;

    private readonly Point3D _where;

    private readonly List<Item> _taken = [];

    public static int ApproachBeats { get; set; } = 20;

    public static int ShunMs { get; set; } = 600000;

    public static long Shunned { get; private set; }

    public static int MostDoublings { get; set; } = 4;

    public static int MostPiles { get; set; } = 2;

    public static long WentOn { get; private set; }

    private static readonly Dictionary<Serial, (long Since, int Times)> _shunned = [];

    private int _piles;

    private int _approach;

    private int _nearestSeen = int.MaxValue;

    private static bool IsShunned(Item item)
    {
        if (item == null || !_shunned.TryGetValue(item.Serial, out var shun))
        {
            return false;
        }

        return Core.TickCount - shun.Since < (long)ShunMs << Math.Min(Math.Max(0, shun.Times - 1), MostDoublings);
    }

    private int _gathered;

    private int _worth;

    private Item _lying;

    public BotForage(Map map, Point3D where)
    {
        _map = map;
        _where = where;
    }

    public override string Kind => Trade;

    public override Map Map => _map;

    public override Point3D Where => _where;

    public override double Expects => Prior;

    public override double Minutes => WorkMinutes;

    public override SkillName? Trains => null;

    public override int Outlay => 0;

    public override double Coin => 0.0;

    public override int Made => _worth;

    public override string Stage =>
        _gathered > 0 ? $"gathered {_gathered} reagents" : "after reagents lying about";

    public override bool Bend(IBotWilful bot)
    {
        bot?.Resolve?.Ledger?.Beware(Trade, _map, _where);

        return false;
    }

    public override BotDoing Advance(IBotWilful bot)
    {
        var body = bot?.Self;

        if (body == null || _map == null || _map == Map.Internal)
        {
            return BotDoing.Failed("no body");
        }

        var pack = body.Backpack;

        if (pack == null)
        {
            return BotDoing.Failed("nowhere to put it");
        }

        if (BotLadder.Load(body) >= BotLadder.Ceiling(body) * FillFraction)
        {
            return Finish(bot, body, "the pack is too full to stoop");
        }

        if (_lying is not { Deleted: false, Movable: true } || !body.InRange(_lying.GetWorldLocation(), Reach))
        {
            _lying = Nearest(body, Reach);
        }

        var lying = _lying;

        if (lying == null)
        {
            return Finish(bot, body, "nothing left lying about");
        }

        if (!body.InRange(lying.GetWorldLocation(), Touch))
        {
            var at = lying.GetWorldLocation();
            var away = Math.Max(Math.Abs(body.X - at.X), Math.Abs(body.Y - at.Y));

            if (away < _nearestSeen)
            {
                _nearestSeen = away;
                _approach = 0;
            }
            else if (++_approach >= ApproachBeats)
            {
                if (_shunned.Count > 2048)
                {
                    _shunned.Clear();
                }

                var times = _shunned.TryGetValue(lying.Serial, out var before) ? before.Times + 1 : 1;

                _shunned[lying.Serial] = (Core.TickCount, times);
                Shunned++;
                BotRefused.Refuse(_map, at);
                _lying = null;
                _approach = 0;
                _nearestSeen = int.MaxValue;

                if (++_piles <= MostPiles)
                {
                    WentOn++;

                    return BotDoing.Work($"giving up the reagents at ({at.X}, {at.Y}), {away} tiles off, for another pile");
                }

                return BotDoing.Failed($"could not get nearer than {away} tiles to the reagents at ({at.X}, {at.Y}) in {ApproachBeats} beats; the pile is shunned for {ShunMs / 60000} minutes");
            }

            return BotDoing.Walk(_map, at, BotArrival.Within(Touch), "after reagents");
        }

        _approach = 0;
        _nearestSeen = int.MaxValue;

        _lying = null;

        var amount = Math.Max(1, lying.Amount);

        var kind = lying.GetType();

        if (!pack.TryDropItem(body, lying, false))
        {
            return Finish(bot, body, "the pack would not take it");
        }

        _gathered += amount;

        BotQuad.Harvested(body.Map, body.Location);
        _worth += amount * BotAuction.Worth(kind, Guess);

        _taken.Add(lying);

        return BotDoing.Work($"gathered {_gathered}");
    }

    private BotDoing Finish(IBotWilful bot, Mobile body, string why)
    {
        if (_gathered == 0 && _piles > 0)
        {
            return BotDoing.Failed($"{why}, after giving up {_piles} pile(s) nobody could get to");
        }

        if (_gathered == 0)
        {
            return BotDoing.Done(why);
        }

        var put = 0;

        if (ListGoods)
        {
            put = Put(bot, body);
        }

        _taken.Clear();

        logger.Information(
            "{Name} gathered {Count} reagents worth about {Worth}gp and put {Put} of them on the board",
            body.Name,
            _gathered,
            _worth,
            put
        );

        return BotDoing.Done($"{_gathered} reagents gathered, {put} put out — {why}");
    }

    private int Put(IBotWilful bot, Mobile body)
    {
        var pack = body.Backpack;

        if (pack == null)
        {
            return 0;
        }

        var put = 0;

        List<Item> carried = [.. pack.Items];

        for (var i = 0; i < carried.Count; i++)
        {
            var item = carried[i];

            if (item is not BaseReagent { Deleted: false })
            {
                continue;
            }

            var amount = Math.Max(1, item.Amount);

            if (BotAuction.List(bot, item, BotAuction.Worth(item.GetType(), BotShops.Shelf(bot, item.GetType(), Guess))) != null)
            {
                put += amount;
            }
        }

        return put;
    }

    public override void Drop(IBotWilful bot) => _taken.Clear();

    public static Item Nearest(Mobile bot, int range)
    {
        var map = bot?.Map;

        if (map == null || map == Map.Internal)
        {
            return null;
        }

        Item best = null;
        var bestAway = double.MaxValue;

        foreach (var item in map.GetItemsInRange<BaseReagent>(bot.Location, range))
        {
            if (item.Deleted || item.Parent != null || !item.Movable || IsShunned(item))
            {
                continue;
            }

            if (BotReach.Ask(map, bot.Location, item.Location, BotArrival.Beside) == BotReachVerdict.Sealed)
            {
                Unreachable++;

                continue;
            }

            var away = bot.GetDistanceToSqrt(item.Location);

            if (away < bestAway)
            {
                bestAway = away;
                best = item;
            }
        }

        return best;
    }
}

/// <summary>
/// Offers any bot the reagents lying within sight of it.
///
/// <para>
/// <b>Offered to everybody, because stooping is not a trade.</b> Mining wants a pickaxe, forging wants an
/// anvil, and scribing wants a book — this wants a free hand, so restricting it to one class would be
/// inventing a guild for picking things up. The auction settles whether it is worth a given bot's minute,
/// which is exactly the sort of question it exists to answer.
/// </para>
/// </summary>
public sealed class BotForager : IBotProposer
{
    public string Name => "Forager";

    public BotStanding Rung => BotStanding.Free;

    public static long Asked { get; private set; }

    public static long Laden { get; private set; }

    public static long Bare { get; private set; }

    public static long Sent { get; private set; }

    public BotDeed Propose(IBotWilful bot)
    {
        var body = bot?.Self;
        var map = body?.Map;

        if (map == null || map == Map.Internal || !body.Alive)
        {
            return null;
        }

        Asked++;

        if (BotLadder.Load(body) >= BotLadder.Ceiling(body) * BotForage.FillFraction)
        {
            Laden++;

            return null;
        }

        var lying = BotForage.Nearest(body, BotForage.Reach);

        if (lying == null)
        {
            Bare++;

            return null;
        }

        Sent++;

        return new BotForage(map, lying.GetWorldLocation());
    }

    public static string Describe() =>
        Asked == 0
            ? "nobody has been offered anything lying about"
            : $"{Asked} asked: {Sent} sent after reagents on the ground, {Bare} had none in sight, {Laden} were carrying too much to stoop, {BotForage.Unreachable} were lying somewhere already known to be shut off, {BotForage.Shunned} piles given up as unreachable and shunned ({BotForage.WentOn} trips went on to another pile)";

    public static void Forget()
    {
        Asked = 0;
        BotForage.ForgetGround();
        Laden = 0;
        Bare = 0;
        Sent = 0;
    }
}
