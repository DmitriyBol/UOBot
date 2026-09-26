using System;
using Server.Mobiles;

namespace Server.BotAI.V2;

/// <summary>
/// Getting hold of one spell the book is short of: off a shelf, off somebody's stall, or by asking the
/// population for it and putting the money down.
///
/// <para>
/// <b>Three routes, and which one exists is a fact about the spell rather than a choice.</b> The first three
/// circles are on a shopkeeper's shelf for twelve, twenty-two and thirty-two gold. Above that no shop in the
/// era sells them, so either a scribe has already written one and it is on a stall, or nobody has and the only
/// thing left to do is ask — a standing, funded want that says what this bot will pay and waits for somebody
/// to find it worth writing.
/// </para>
///
/// <para>
/// <b>It is priced as an errand, because that is what it is.</b> Buying creates no wealth: coin becomes goods,
/// and money put down on a want is coin becoming a claim. So the takings are declared as what was spent and
/// the trip comes out at about nothing per minute — never punished, and never preferred over work that
/// produces something. A mage with a pen will always rather write; a healer with no trade at all has this and
/// the shops, which is an honest account of what a healer is on this shard today.
/// </para>
///
/// <para>
/// <b>Nothing here prices a spell.</b> The want's opening offer is what the engine's own shelf charges for
/// that circle, continued one step per circle above the third, and from then on the market moves it. What a
/// fourth-circle scroll is worth is whatever makes somebody write one.
/// </para>
/// </summary>
public sealed class BotAcquire : BotDeed
{
    public const string Trade = "acquire";

    public static double Prior { get; set; } = 12.0;

    public static double WorkMinutes { get; set; } = 2.0;

    public static int Ask { get; set; } = 1;

    private enum Route
    {
        Delivered,

        Counter,

        Stall,

        Board
    }

    private readonly Route _route;

    private readonly Type _kind;

    private readonly int _spell;

    private BaseVendor _shop;

    private int _repicks;

    private readonly BotListing _stall;

    private readonly Map _map;

    private readonly Point3D _where;

    private int _price;

    private int _paid;

    private bool _learned;

    /// <summary>
    /// What the scroll is being bought for, which is two entirely different errands wearing one name.
    ///
    /// <para>
    /// <b>A scroll bought to learn goes into a book; a scroll bought to cast stays in the pack.</b>
    /// <c>BotSeeker</c> fills a caster's book, <c>BotArmoury</c> stocks whatever a bot can throw — and both
    /// were building the same undertaking, which then tried to write every scroll into a book. What that
    /// produced was 233 of 430 rounds in a session reporting "the book would not take it" about warriors who
    /// had no book, had never wanted one, and had in fact got exactly what they set out for. A round that
    /// succeeded, filed as a failure, 233 times, with the ledger pricing the trade off it.
    /// </para>
    ///
    /// <para>
    /// And underneath the mislabelling, a real refusal: the round gives up early when the book already holds
    /// the spell, which is right for learning and backwards for stocking. A mage that knew Magic Arrow could
    /// never buy a Magic Arrow scroll to throw.
    /// </para>
    /// </summary>
    private enum Purpose
    {
        Learn,

        Stock
    }

    private readonly Purpose _purpose;

    private BotAcquire(
        Route route, Type kind, int spell, Map map, Point3D where, int price, BaseVendor shop, BotListing stall,
        Purpose purpose = Purpose.Learn
    )
    {
        _purpose = purpose;
        _route = route;
        _kind = kind;
        _spell = spell;
        _map = map;
        _where = where;
        _price = Math.Max(1, price);
        _shop = shop;
        _stall = stall;
    }

    public static BotAcquire Delivery(Type kind, int spell, Map map, Point3D where, bool toCast = false) =>
        new(Route.Delivered, kind, spell, map, where, 1, null, null, Bought(toCast));

    private static Purpose Bought(bool toCast) => toCast ? Purpose.Stock : Purpose.Learn;

    public static BotAcquire Counter(Type kind, int spell, BaseVendor shop, int price, bool toCast = false) =>
        new(Route.Counter, kind, spell, shop?.Map, shop?.Location ?? Point3D.Zero, price, shop, null, Bought(toCast));

    public static BotAcquire Stalled(Type kind, int spell, BotListing stall, Map map, Point3D where, bool toCast = false) =>
        new(Route.Stall, kind, spell, map, where, stall?.Price ?? 1, null, stall, Bought(toCast));

    public static BotAcquire Board(Type kind, int spell, Map map, Point3D where, int offer, bool toCast = false) =>
        BotAuction.Full ? null : new BotAcquire(Route.Board, kind, spell, map, where, offer, null, null, Bought(toCast));

    public override string Kind => Trade;

    public override bool Steadfast => true;

    public override Map Map => _map;

    public override Point3D Where => _where;

    public override double Expects => Prior;

    public override double Minutes => WorkMinutes;

    public override SkillName? Trains => null;

    public override int Outlay => _route == Route.Delivered ? 0 : Ask * _price;

    public override bool AtCounter => _route == Route.Counter;

    public override double Coin => 0.0;

    public override int Made => _paid;

    public override string Stage
    {
        get
        {
            var name = _kind?.Name ?? "a spell";

            if (_learned)
            {
                return $"learned {name}";
            }

            return _route switch
            {
                Route.Delivered => $"collecting {name}",
                Route.Counter => $"buying {name}",
                Route.Stall => $"buying {name} off the market",
                _ => $"asking for {name} at {_price}gp"
            };
        }
    }

    public override bool Bend(IBotWilful bot)
    {
        if (_shop == null)
        {
            return false;
        }

        bot?.Resolve?.Ledger?.Beware(BotShops.ShopKind, _shop.Map, _shop.Location);

        return false;
    }

    public override BotDoing Advance(IBotWilful bot)
    {
        var body = bot?.Self;

        if (body == null)
        {
            return BotDoing.Failed("no body");
        }

        if (_kind == null || _spell < 0)
        {
            return BotDoing.Failed("no such spell");
        }

        if (_purpose == Purpose.Learn && BotGrimoire.Holds(body, _spell))
        {
            return BotDoing.Done($"already knows {_kind.Name}");
        }

        return _route switch
        {
            Route.Delivered => Collecting(bot, body),
            Route.Counter => Buying(bot, body),
            Route.Stall => Taking(bot, body),
            _ => Asking(bot, body)
        };
    }

    private BotDoing Collecting(IBotWilful bot, Mobile body)
    {
        BotAuction.Collect(bot);

        return Learn(bot, body);
    }

    private BotDoing Buying(IBotWilful bot, Mobile body)
    {
        if (_shop == null || _shop.Deleted || _shop.Map == null || _shop.Map == Map.Internal)
        {
            return BotDoing.Failed("the shopkeeper is gone");
        }

        if (!body.InRange(_shop.Location, BotShops.CounterReach))
        {
            return BotDoing.Walk(_shop.Map, _shop, BotArrival.Within(BotShops.CounterReach), $"to {_shop.Name} for a scroll");
        }

        var bought = BotShops.Buy(bot, _shop, _kind, Ask, out var refused);

        if (bought <= 0 && BotShops.Next(bot, _shop, _kind, ref _repicks) is { } next)
        {
            _shop = next;
            _price = Math.Max(1, BotShops.Price(next, _kind));

            return BotDoing.Walk(next.Map, next, BotArrival.Within(BotShops.CounterReach), $"on to {next.Name} for a scroll");
        }

        if (bought <= 0)
        {
            return BotDoing.Failed(refused ?? "the shop would not sell it");
        }

        _paid = bought * _price;

        return Learn(bot, body);
    }

    private BotDoing Taking(IBotWilful bot, Mobile body)
    {
        if (_stall == null || _stall.IsEmpty)
        {
            return BotDoing.Failed("that stall is empty now");
        }

        var price = _stall.Price;

        if (BotAuction.Buy(body, _stall, Ask) <= 0)
        {
            return BotDoing.Failed("could not pay for it");
        }

        _paid = Ask * price;

        return Learn(bot, body);
    }

    private BotDoing Asking(IBotWilful bot, Mobile body)
    {
        if (BotAuction.Selling(bot, _kind))
        {
            var took = BotAuction.Reclaim(bot, _kind);
            (body as BotMobile)?.Rearm();

            return took > 0
                ? BotDoing.Done($"took {took} {_kind.Name} back off its own stall")
                : BotDoing.Failed($"selling {_kind.Name} itself with nothing on the stall to take back");
        }

        var want = BotAuction.Ask(bot, _kind, Ask, _price);

        if (want == null)
        {
            return BotDoing.Failed(
                BotAuction.Full
                    ? $"the board is full at {BotAuction.MaxWants} wants"
                    : "could not put the money down for it"
            );
        }

        _paid = Ask * want.Offer;

        return BotDoing.Done($"asked for {_kind.Name} at {want.Offer}gp, {want.Escrow}gp down");
    }

    private BotDoing Learn(IBotWilful bot, Mobile body)
    {
        var scrolls = BotQuill.Gather(body, _kind);

        if (_purpose == Purpose.Stock)
        {
            if (scrolls.Count == 0)
            {
                return BotDoing.Failed($"paid for {_kind.Name} and it is not in the pack");
            }

            _learned = true;
            BotAuction.Withdrawn(bot, _kind);

            return BotDoing.Done($"stocked {_kind.Name} for {_paid}gp");
        }

        string refused = null;

        for (var i = 0; i < scrolls.Count; i++)
        {
            if (!BotGrimoire.Write(body, scrolls[i], out var why))
            {
                refused ??= why;

                continue;
            }

            _learned = true;

            BotAuction.Withdrawn(bot, _kind);

            return BotDoing.Done($"learned {_kind.Name} for {_paid}gp");
        }

        return BotDoing.Done(
            scrolls.Count == 0
                ? $"paid for {_kind.Name} and it is not in the pack"
                : $"has {_kind.Name} but the book would not take it: {refused ?? "no reason given"}"
        );
    }
}
