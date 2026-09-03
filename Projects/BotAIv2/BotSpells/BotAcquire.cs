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
    /// <summary>The ledger's key. A kind of work — getting a spell — not one spell.</summary>
    public const string Trade = "acquire";

    /// <summary>
    /// What it is reckoned at before experience corrects it. The same as an errand to the shops, deliberately:
    /// it produces nothing, and the only reason to do it is that the book is short.
    /// </summary>
    public static double Prior { get; set; } = 12.0;

    /// <summary>How long it is expected to take once the bot is there.</summary>
    public static double WorkMinutes { get; set; } = 2.0;

    /// <summary>How many of a scroll a want asks for at a time. One: a book needs one of each.</summary>
    public static int Ask { get; set; } = 1;

    private enum Route
    {
        /// <summary>Something has already been delivered against a standing want. Collect it.</summary>
        Delivered,

        /// <summary>A shopkeeper sells it. Walk over and buy.</summary>
        Counter,

        /// <summary>Another bot has one out. Buy it off the market, from wherever this bot is standing.</summary>
        Stall,

        /// <summary>Nobody has one. Ask, with the money down.</summary>
        Board
    }

    private readonly Route _route;

    private readonly Type _kind;

    private readonly int _spell;

    private readonly BaseVendor _shop;

    private readonly BotListing _stall;

    private readonly Map _map;

    private readonly Point3D _where;

    private readonly int _price;

    private int _paid;

    private bool _learned;

    private BotAcquire(
        Route route, Type kind, int spell, Map map, Point3D where, int price, BaseVendor shop, BotListing stall
    )
    {
        _route = route;
        _kind = kind;
        _spell = spell;
        _map = map;
        _where = where;
        _price = Math.Max(1, price);
        _shop = shop;
        _stall = stall;
    }

    /// <summary>Collecting what a standing want has already been filled with.</summary>
    public static BotAcquire Delivery(Type kind, int spell, Map map, Point3D where) =>
        new(Route.Delivered, kind, spell, map, where, 1, null, null);

    /// <summary>Off a shopkeeper's shelf.</summary>
    public static BotAcquire Counter(Type kind, int spell, BaseVendor shop, int price) =>
        new(Route.Counter, kind, spell, shop?.Map, shop?.Location ?? Point3D.Zero, price, shop, null);

    /// <summary>Off another bot's stall. No walk: the market holds its goods out of the world.</summary>
    public static BotAcquire Stalled(Type kind, int spell, BotListing stall, Map map, Point3D where) =>
        new(Route.Stall, kind, spell, map, where, stall?.Price ?? 1, null, stall);

    /// <summary>By asking the population, with the money down.</summary>
    public static BotAcquire Board(Type kind, int spell, Map map, Point3D where, int offer) =>
        new(Route.Board, kind, spell, map, where, offer, null, null);

    public override string Kind => Trade;

    public override Map Map => _map;

    public override Point3D Where => _where;

    public override double Expects => Prior;

    public override double Minutes => WorkMinutes;

    /// <summary>Nothing. Putting a scroll into a book teaches a bot nothing at all; casting from it would.</summary>
    public override SkillName? Trains => null;

    /// <summary>What it will cost. The one number the decision layer measures need against.</summary>
    public override int Outlay => _route == Route.Delivered ? 0 : Ask * _price;

    /// <summary>Not a penny comes back. This is the spending half of a living.</summary>
    public override double Coin => 0.0;

    /// <summary>
    /// Goods are worth what they cost, so the errand comes out at about nothing rather than at a loss. Money
    /// put down on a want counts the same way: it has not been lost, it has become a claim on a scroll.
    /// </summary>
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

    /// <summary>
    /// The way to the shopkeeper turned out not to exist.
    ///
    /// <para>
    /// <b>Nothing to bend to here, and something to write down — and it was the writing down that was
    /// missing.</b> What this undertaking carries was priced against <em>this</em> shopkeeper, so swapping in
    /// another one mid-errand would carry a stale price; failing is the honest answer. But the failure has to
    /// be filed under the <em>place's</em> name, because that is the word the shop lookup asks in. Filed under
    /// the undertaking's name — which is all <c>BotWill.Settle</c> can do — it is written and never read, and
    /// the next beat picks the same unreachable shopkeeper on distance alone. Calla walked at Gus thirty-one
    /// times in an hour on 26.08.2026 that way.
    /// </para>
    /// </summary>
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

        if (BotGrimoire.Holds(body, _spell))
        {
            // Somebody else's delivery, a corpse, or a scroll bought a moment ago: whatever the reason, the
            // book has it now and there is nothing left to do.
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

    /// <summary>
    /// Taking delivery. The scroll may already be in the pack — the market hands over on its own beat — so
    /// this asks the market once and then looks where the answer would have put it either way.
    /// </summary>
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
            // The distance the work itself asks for, on the line above. See BotArrival.Beside.
            // Followed rather than aimed at: a shopkeeper wanders. See BotPeddle for the whole reason.
            return BotDoing.Walk(_shop.Map, _shop, BotArrival.Within(BotShops.CounterReach), $"to {_shop.Name} for a scroll");
        }

        var bought = BotShops.Buy(bot, _shop, _kind, Ask, out var refused);

        if (bought <= 0)
        {
            // The second counter in the project, and it was left saying the old sentence when the first one
            // was taught to name its reason — so scroll buying went on being the one shop errand nobody could
            // diagnose. Two call sites, one message: whichever gets instrumented, the other has to follow.
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

    /// <summary>
    /// Puts the want up, or tops up the one already there, and finishes.
    ///
    /// <para>
    /// <b>Finishing here rather than waiting is the point.</b> A want is a standing position, not an errand:
    /// it sits on the market with the money behind it, raises its own offer while nobody fills it, and gets
    /// collected by a later piece of work when somebody does. A bot that stood still until its want was
    /// filled would be a bot doing nothing for half an hour, which is the one thing no state in this project
    /// is allowed to be.
    /// </para>
    /// </summary>
    private BotDoing Asking(IBotWilful bot, Mobile body)
    {
        var want = BotAuction.Ask(bot, _kind, Ask, _price);

        if (want == null)
        {
            return BotDoing.Failed("could not put the money down for it");
        }

        _paid = Ask * want.Offer;

        return BotDoing.Done($"asked for {_kind.Name} at {want.Offer}gp, {want.Escrow}gp down");
    }

    private BotDoing Learn(IBotWilful bot, Mobile body)
    {
        var scrolls = BotQuill.Gather(body, _kind);

        for (var i = 0; i < scrolls.Count; i++)
        {
            if (!BotGrimoire.Write(body, scrolls[i]))
            {
                continue;
            }

            _learned = true;

            // The spell is in the book, so whatever was still standing on the market asking for it is asking
            // for nothing. The money comes back. Without this a caster that bought a scroll off a stall would
            // leave its own want up, holding its gold until the market gave up on it half an hour later.
            BotAuction.Withdrawn(bot, _kind);

            return BotDoing.Done($"learned {_kind.Name} for {_paid}gp");
        }

        // It is in the pack and the book would not take it. Not a failure worth a caution on the place: the
        // scroll is real, it is worth money, and the population has one more of them than it did.
        return BotDoing.Done($"has {_kind.Name} but the book would not take it");
    }
}
