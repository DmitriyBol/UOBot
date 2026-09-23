using System;
using Server.Engines.Craft;
using Server.Logging;
using Server.Items;
using Server.Mobiles;

namespace Server.BotAI.V2;

/// <summary>
/// Buy cloth, make something of it, put it on the market. The crafter's chain, and the first work in this
/// project that <b>creates</b> rather than extracts.
///
/// <para>
/// <b>It depends on nobody.</b> A crafter does not have to wait for a miner: cloth is on a shelf in town, and
/// what comes off the needle is worth more than what went into it — in skill certainly, in coin if the market
/// agrees. That is the answer to the question this chain was written for: what does a producer do on a shard
/// where nothing has been produced yet.
/// </para>
///
/// <para>
/// <b>Attempts are not output.</b> Crafting runs on the engine's own timer and fails often at the edge of a
/// skill, so what is counted is what is in the pack afterwards. The first version reported attempts and its
/// tally claimed forty-four things made by a smith that had produced nothing in three minutes.
/// </para>
/// </summary>
public sealed class BotSew : BotDeed
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotSew));

    public static long Jammed { get; private set; }

    public const string Trade = "sew";

    public static double Prior { get; set; } = 55.0;

    public static double WorkMinutes { get; set; } = 6.0;

    public static int Bolt { get; set; } = 20;

    public static int SwingMs { get; set; } = 3000;

    public static int StallMs { get; set; } = SwingMs * 8;

    public static int GoldPerPiece { get; set; } = 12;

    private enum Leg
    {
        Shop,
        Work,
        Counter
    }

    private BaseVendor _shop;

    private int _repicks;

    private BotListing _stall;

    private readonly Type _stuff;

    private readonly int _price;

    private readonly int _take;

    private readonly int _need;

    private Map _map;

    private Point3D _where;

    private Leg _leg;

    private CraftItem _recipe;

    private Type _kind;

    private Point3D _counter;

    private int _had;

    private int _worth;

    private int _pieces;

    private int _made;

    private int _swings;

    private bool _swung;

    private long _swungTick;

    private int _lastLeft = -1;

    private long _stirTick;

    private int _want;

    public BotSew(BaseVendor shop, int price, int need)
    {
        _stuff = typeof(Cloth);
        _shop = shop;
        _map = shop?.Map;
        _where = shop?.Location ?? Point3D.Zero;
        _price = Math.Max(1, price);
        _take = Bolt;
        _need = Math.Max(1, need);
    }

    public BotSew(Map map, Point3D where, BotListing stall, int price, int take, int need)
        : this(map, where, stall, price, take, need, null)
    {
    }

    public BotSew(Map map, Point3D where, BotListing stall, int price, int take, int need, BotWant order)
    {
        _stuff = typeof(Leather);
        _stall = stall;
        _map = map;
        _where = where;
        _price = Math.Max(1, price);
        _take = Math.Max(0, take);
        _need = Math.Max(1, need);
        _order = order;
    }

    private readonly BotWant _order;

    public override string Kind => Trade;

    public override Map Map => _map;

    public override Point3D Where => _where;

    public override double Expects => _order == null ? Prior : Prior * 1.6;

    public override double Minutes => WorkMinutes;

    public override SkillName? Trains => SkillName.Tailoring;

    public override int Outlay => _take * _price;

    public override bool AtCounter => _shop != null;

    public override double Coin => 0.0;

    public override int Made => _made;

    public override string Stage => _leg switch
    {
        Leg.Shop => $"after {_stuff?.Name ?? "material"}",
        Leg.Work => $"sewing {_kind?.Name ?? "something"} ({_swings} attempts, {_pieces} made)",
        _ => $"putting {_pieces} away"
    };

    public override bool Bend(IBotWilful bot)
    {
        if (_shop == null)
        {
            return false;
        }

        bot?.Resolve?.Ledger?.Beware(BotShops.ShopKind, _shop.Map, _shop.Location);

        var other = BotShops.Nearest(bot, _stuff);

        if (other == null || other == _shop)
        {
            return false;
        }

        _shop = other;
        _map = other.Map;
        _where = other.Location;

        return true;
    }

    public override BotDoing Advance(IBotWilful bot)
    {
        var body = bot?.Self;

        if (body == null)
        {
            return BotDoing.Failed("no body");
        }

        for (var guard = 0; guard < 6; guard++)
        {
            var doing = _leg switch
            {
                Leg.Shop => Shopping(bot, body),
                Leg.Work => Sewing(body),
                _ => Selling(bot, body)
            };

            if (doing.Kind != BotDoingKind.None)
            {
                return doing;
            }
        }

        return BotDoing.Failed("could not settle on a next step");
    }

    private BotDoing Shopping(IBotWilful bot, Mobile body)
    {
        if (BotThread.Amount(body, _stuff) >= _need)
        {
            _leg = Leg.Work;

            return default;
        }

        if (BotAuction.Reclaim(bot, _stuff) > 0 && BotThread.Amount(body, _stuff) >= _need)
        {
            _leg = Leg.Work;

            return default;
        }

        if (_stall != null)
        {
            if (_stall.IsEmpty)
            {
                return BotDoing.Failed($"the {_stuff.Name} stall is empty now");
            }

            if (BotAuction.Buy(body, _stall, _take) <= 0)
            {
                return BotDoing.Failed($"could not pay another bot for {_stuff.Name}");
            }

            _leg = Leg.Work;

            return default;
        }

        if (_stall == null && _shop == null)
        {
            return BotDoing.Failed($"the {_stuff.Name} it set out with is gone");
        }

        if (_shop == null || _shop.Deleted || _shop.Map == null || _shop.Map == Map.Internal)
        {
            return BotDoing.Failed($"the {_stuff.Name} merchant is gone");
        }

        if (!body.InRange(_shop.Location, BotShops.CounterReach))
        {
            return BotDoing.Walk(_shop.Map, _shop, BotArrival.Within(BotShops.CounterReach), $"to {_shop.Name} for {_stuff.Name}");
        }

        if (BotShops.Buy(bot, _shop, _stuff, _take, out var refused) <= 0)
        {
            var next = BotShops.Next(bot, _shop, _stuff, ref _repicks);

            if (next != null)
            {
                _shop = next;
                _map = next.Map;
                _where = next.Location;

                return BotDoing.Walk(next.Map, next, BotArrival.Within(BotShops.CounterReach), $"on to {next.Name} for {_stuff.Name}");
            }

            return BotDoing.Failed(refused ?? $"no {_stuff.Name} to be had");
        }

        _leg = Leg.Work;

        return default;
    }

    private BotDoing Sewing(Mobile body)
    {
        var kit = BotThread.Kit(body);

        if (kit == null && _swings == 0)
        {
            return BotDoing.Failed("nothing to sew with");
        }

        if (_recipe == null)
        {
            _recipe = _order == null
                ? BotThread.Choose(body, _stuff)
                : BotThread.Recipe(body, _stuff, _order.Kind);

            if (_recipe == null)
            {
                return BotDoing.Failed($"nothing it knows how to make from {_stuff.Name}");
            }

            _kind = _recipe.ItemType;
            _want = BotThread.Units(_recipe);
            _had = BotThread.Made(body, _kind);

            var floor = BotThread.Units(_recipe) * BotAuction.Worth(_stuff, _price) * 2;

            _worth = BotAuction.Worth(_kind, Math.Max(GoldPerPiece, floor));
        }

        var have = BotThread.Made(body, _kind);

        if (have > _had)
        {
            have += BotCraftwork.Bonus(body, _kind);

            _pieces += have - _had;
            _had = have;
            _made = _pieces * _worth;
        }

        if (kit == null)
        {
            if (_swung && Core.TickCount - _swungTick < SwingMs)
            {
                return BotDoing.Work("sewing");
            }

            if (_pieces <= 0)
            {
                return BotDoing.Failed("nothing to sew with");
            }

            _leg = Leg.Counter;

            return default;
        }

        var left = BotThread.Amount(body, _stuff);

        if (_lastLeft != left)
        {
            _lastLeft = left;
            _stirTick = Core.TickCount;
        }

        if (Core.TickCount - _stirTick >= StallMs)
        {
            Jammed++;

            logger.Information(
                "{Name}'s needle stopped: {Swings} attempts, {Pieces} made, {Left} {Stuff} left untouched for {Stall}s",
                body.Name,
                _swings,
                _pieces,
                left,
                _stuff.Name,
                StallMs / 1000
            );

            if (_pieces <= 0)
            {
                return BotDoing.Failed($"the needle has not moved in {StallMs / 1000}s, with {left} {_stuff.Name} still in the pack");
            }

            _leg = Leg.Counter;

            return default;
        }

        if (left < _want)
        {
            _leg = Leg.Counter;

            return default;
        }

        if (_swung && Core.TickCount - _swungTick < SwingMs)
        {
            return BotDoing.Work("sewing");
        }

        _swings++;
        _swung = true;
        _swungTick = Core.TickCount;

        BotThread.Swing(body, _recipe, _stuff, kit);

        return BotDoing.Work("sewing");
    }

    private BotDoing Selling(IBotWilful bot, Mobile body)
    {
        if (_pieces <= 0)
        {
            return BotDoing.Done($"{_swings} attempts, nothing came of it");
        }

        if (_counter == Point3D.Zero)
        {
            _counter = BotGround.Counter(Map, body.Location);
        }

        if (_counter != Point3D.Zero && !body.InRange(_counter, BotDig.CounterReach))
        {
            return BotDoing.Walk(Map, _counter, BotArrival.Within(BotDig.CounterReach), "to a counter");
        }

        var listed = 0;
        var ordered = 0;
        var made = BotThread.Gather(body, _kind);

        for (var i = 0; i < made.Count; i++)
        {
            var piece = made[i];
            var held = Math.Max(1, piece.Amount);
            var want = _order ?? BotAuction.Demand(bot, _kind);
            var sold = want == null ? 0 : BotAuction.Fill(bot, want, piece);

            if (sold > 0)
            {
                ordered += sold;
                _made -= sold * _worth;

                if (sold >= held)
                {
                    continue;
                }
            }

            if (BotAuction.List(bot, piece, _worth) != null)
            {
                listed++;
            }
        }

        return BotDoing.Done($"{_pieces} {_kind?.Name} in {_swings} attempts, {ordered} to order and {listed} put out to sell");
    }
}
