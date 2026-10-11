using System;
using Server.Mobiles;

namespace Server.BotAI.V2;

/// <summary>
/// Getting hold of one spell the book is short of: off a shelf, at a bot's shop by the bank, or by asking the
/// population for it aloud and waiting for a scribe to bring one.
///
/// <para>
/// <b>Three routes, and which one exists is a fact about the spell rather than a choice.</b> The first three
/// circles are on a shopkeeper's shelf for twelve, twenty-two and thirty-two gold. Above that no shop in the
/// era sells them, so either a scribe has already written one and keeps shop with it by the bank, or nobody has and
/// the only thing left to do is ask — a spoken want that says what this bot will pay and waits for somebody to find
/// it worth writing and carrying over (01.10.2026: the stall and the escrow went with the auction).
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

        Shop,

        Board
    }

    private readonly Route _route;

    private readonly Type _kind;

    private readonly int _spell;

    private BaseVendor _shop;

    private int _repicks;

    private readonly BotStorefront _front;

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
        Route route, Type kind, int spell, Map map, Point3D where, int price, BaseVendor shop, BotStorefront front,
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
        _front = front;
    }

    public static BotAcquire Delivery(Type kind, int spell, Map map, Point3D where, bool toCast = false) =>
        new(Route.Delivered, kind, spell, map, where, 1, null, null, Bought(toCast));

    private static Purpose Bought(bool toCast) => toCast ? Purpose.Stock : Purpose.Learn;

    public static BotAcquire Counter(Type kind, int spell, BaseVendor shop, int price, bool toCast = false) =>
        new(Route.Counter, kind, spell, shop?.Map, shop?.Location ?? Point3D.Zero, price, shop, null, Bought(toCast));

    public static BotAcquire AtShop(Type kind, int spell, BotStorefront front, int price, bool toCast = false) =>
        new(Route.Shop, kind, spell, front?.Map, front?.Location ?? Point3D.Zero, price, null, front, Bought(toCast));

    public static BotAcquire Board(Type kind, int spell, Map map, Point3D where, int offer, bool toCast = false) =>
        BotMarket.Full || !BotMarket.Running ? null : new BotAcquire(Route.Board, kind, spell, map, where, offer, null, null, Bought(toCast));

    public override string Kind => Trade;

    public override bool Steadfast => true;

    public override Map Map => _map;

    public override Point3D Where => _where;

    public override double Expects => Prior;

    public override double Minutes => WorkMinutes;

    public override SkillName? Trains => null;

    public override int Outlay => _route == Route.Delivered ? 0 : Ask * _price;

    public override bool AtCounter => _route is Route.Counter or Route.Shop;

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
                Route.Shop => $"buying {name} at {_front?.Keeper?.Name ?? "a bot"}'s shop",
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
            Route.Shop => Visiting(bot, body),
            _ => Asking(bot, body)
        };
    }

    private BotDoing Collecting(IBotWilful bot, Mobile body) => Learn(bot, body);

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

    private BotDoing Visiting(IBotWilful bot, Mobile body)
    {
        var visit = BotShopkeep.Visit(bot, _front, _kind, Ask, _price, false, out var got, out var cost, out var onward);

        if (onward != null)
        {
            _shop = onward;
            _price = Math.Max(1, BotShops.Price(onward, _kind));

            return Buying(bot, body);
        }

        if (got <= 0)
        {
            return visit;
        }

        _paid = cost;

        return Learn(bot, body);
    }

    private BotDoing Asking(IBotWilful bot, Mobile body)
    {
        if (BotMarket.FromBank(bot, _kind, Ask) > 0)
        {
            (body as BotMobile)?.Rearm();

            return Learn(bot, body);
        }

        var want = BotMarket.Ask(bot, _kind, Ask, _price);

        if (want == null)
        {
            return BotDoing.Failed(
                BotMarket.Full
                    ? $"the market already holds {BotMarket.MaxWants} wants"
                    : BotMarket.Reserved(body) > 0
                        ? $"could not pay for it if it came, with {BotMarket.Reserved(body)}gp of its {BotYield.Wealth(body)}gp held for its other wants"
                        : "could not pay for it if it came"
            );
        }

        return BotDoing.Done($"asked aloud for {_kind.Name} at {want.Offer}gp");
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
            BotMarket.Withdrawn(bot, _kind, BotWithdraw.Have);

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

            BotMarket.Withdrawn(bot, _kind, BotWithdraw.Have);

            return BotDoing.Done($"learned {_kind.Name} for {_paid}gp");
        }

        return BotDoing.Done(
            scrolls.Count == 0
                ? $"paid for {_kind.Name} and it is not in the pack"
                : $"has {_kind.Name} but the book would not take it: {refused ?? "no reason given"}"
        );
    }
}
