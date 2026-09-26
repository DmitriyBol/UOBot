using System;
using System.Collections.Generic;
using Server.Items;
using Server.Logging;
using Server.Mobiles;
using Server.Text;

namespace Server.BotAI.V2;

/// <summary>
/// The bots' own market. They put goods out, top them up, and move their own prices by what actually sells.
///
/// <para>
/// <b>Prices are the bots', not ours.</b> Nothing here sets a value: a stall opens at whatever the seller
/// asked, goes up when the same thing sells again soon, and comes down when it has sat untouched. That is the
/// only pricing rule in the project that needs no table of worths — and it is the same shape as the decision
/// layer's ledger, for the same reason. A number that is measured survives the world changing under it; a
/// number that is configured has to be re-guessed every time the shard does.
/// </para>
///
/// <para>
/// <b>Money is conserved, and the order of two lines is what guarantees it.</b> The buyer is charged first —
/// coin taken out of the pack, the rest withdrawn from the account — and only then is the seller paid. Doing
/// it the other way round mints gold, because the engine's deposit adds to an account without touching what
/// the depositor is carrying. The first version's economy lost 110,900 in a night with nobody able to say
/// where it went; this market can say where every coin went.
/// </para>
///
/// <para>
/// <b>There are two sides, and they are one system.</b> A stall says "I have"; a want says "I want", with the
/// money already down. They are the same object with the sign turned round, so they share one set of numbers:
/// a stall nobody buys from gets cheaper and a want nobody fills gets dearer, which is the same sentence.
/// The first version spread this across a board, a commissions system, a supply system and this auction —
/// five places that each had to learn prices, and none of which learned any.
/// </para>
///
/// <para>
/// <b>A bot cannot be on both sides of the same kind of thing.</b> One number with a sign: plus is a stall,
/// minus is a want. That is not a check, it is the shape of the data, and it is what kills the defect this
/// design was measured against — the first version's bots passed the same fifteen ginseng and the same
/// seventy-five gold round in a circle, because filling somebody's order dropped the filler below its own
/// threshold and it posted an order of its own.
/// </para>
/// </summary>
public static class BotAuction
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotAuction));

    public static double RaiseStep { get; set; } = 0.15;

    public static double CutStep { get; set; } = 0.10;

    public static int BriskMs { get; set; } = 600000;

    public static int StaleMs { get; set; } = 600000;

    public static int RaiseMs
    {
        get => _raiseMs ?? StaleMs;
        set => _raiseMs = value;
    }

    private static int? _raiseMs;

    public static int Floor { get; set; } = 2;

    public static long Stood { get; private set; }

    public static long Returned { get; private set; }

    public static long Unreclaimed { get; private set; }

    public static double Levy { get; set; } = 0.01;

    public static int LeastLevy { get; set; } = 1;

    public static long Levied { get; private set; }

    public static long Levies { get; private set; }

    public static double MostMultiple { get; set; } = 4.0;

    public static double LeastMultiple { get; set; } = 0.25;

    public static int ForgetMs { get; set; } = 3600000;

    public static int BeatMs { get; set; } = 30000;

    public static int MaxListings { get; set; } = 1024;

    public static int MaxWants { get; set; } = 512;

    public static bool Full => _wants.Count >= MaxWants;

    public static int Slice { get; set; } = 5;

    public static int SliceMs { get; set; } = 60000;

    private static readonly List<BotListing> _listings = [];

    private static readonly List<BotWant> _wants = [];

    private static AuctionTimer _timer;

    public static long Sells { get; private set; }

    private static readonly HashSet<Type> _worthless = [];

    public static bool Worthless(Type kind) => kind != null && _worthless.Contains(kind);

    public static long Recalled { get; private set; }

    public static long Cheap { get; private set; }

    public static long Unpriced { get; private set; }

    public static string Condemned(int most)
    {
        if (_worthless.Count == 0)
        {
            return "none";
        }

        List<string> named = [];

        foreach (var kind in _worthless)
        {
            if (named.Count >= most)
            {
                named.Add("and more");

                break;
            }

            named.Add(kind.Name);
        }

        return string.Join(", ", named);
    }

    public static long Unfunded { get; private set; }

    private static int _nextId;

    private static int _nextWantId;

    public static IReadOnlyList<BotListing> Listings => _listings;

    public static IReadOnlyList<BotWant> Wants => _wants;

    public static int Stalls => _listings.Count;

    public static int Asks => _wants.Count;

    public static long Sales { get; private set; }

    public static long Turnover { get; private set; }

    public static long Fills { get; private set; }

    public static long Filled { get; private set; }

    public static long Posted { get; private set; }

    public static long Abandoned { get; private set; }

    public static long Crossed { get; private set; }

    public static long Dear { get; private set; }

    public static long Raises { get; private set; }

    public static long Cuts { get; private set; }

    public static long Forgotten { get; private set; }

    public static bool Running => _timer != null;

    public static void Start()
    {
        if (_timer != null)
        {
            return;
        }

        var interval = TimeSpan.FromMilliseconds(Math.Max(1000, BeatMs));

        _timer = new AuctionTimer(interval);
        _timer.Start();
    }

    public static void Stop()
    {
        _timer?.Stop();
        _timer = null;
    }

    public static void Reset()
    {
        Stop();

        for (var i = 0; i < _listings.Count; i++)
        {
            _listings[i].Discard();
        }

        _listings.Clear();

        for (var i = 0; i < _wants.Count; i++)
        {
            _wants[i].Discard();
        }

        _wants.Clear();
        _holding.Clear();
        _worthless.Clear();
        Recalled = 0;

        _nextId = 0;
        _nextWantId = 0;
        Sales = 0;
        Turnover = 0;
        Fills = 0;
        Fetches = 0;
        Fetched = 0;
        Filled = 0;
        Posted = 0;
        Abandoned = 0;
        Raises = 0;
        Cuts = 0;
        Forgotten = 0;
        Sells = 0;
        Unfunded = 0;
        Levied = 0;
        Levies = 0;
        Cheap = 0;
        Unpriced = 0;
    }

    public static BotListing List(IBotWilful seller, Item item, int price) => List(seller, item, price, true);

    public static BotListing List(IBotWilful seller, Item item, int price, bool measured)
    {
        if (seller?.Self == null || item == null || item.Deleted)
        {
            return null;
        }

        if (BotBinding.Refuses(item, seller.Bond))
        {
            return null;
        }

        if (price < Floor)
        {
            Cheap++;

            if (measured)
            {
                _worthless.Add(item.GetType());
            }
            else
            {
                Unpriced++;
            }

            return null;
        }

        Withdrawn(seller, item.GetType());

        var stall = Find(seller, item.GetType());

        if (stall != null)
        {
            stall.Add(item);

            return stall;
        }

        if (_listings.Count >= MaxListings)
        {
            Squeeze();
        }

        if (_listings.Count >= MaxListings)
        {
            logger.Error(
                "The market is full at {Count} stalls and every one of them has goods on it, so {Name} could not put out {Item}",
                _listings.Count,
                seller.Self.Name,
                item.GetType().Name
            );

            return null;
        }

        stall = new BotListing(++_nextId, seller, item, price);

        stall.Add(item);

        _listings.Add(stall);

        return stall;
    }

    public static int Stocked(Type kind)
    {
        if (kind == null)
        {
            return 0;
        }

        var held = 0;

        for (var i = 0; i < _listings.Count; i++)
        {
            var lot = _listings[i];

            if (lot is { IsEmpty: false } && lot.Kind == kind)
            {
                held += lot.Amount;
            }
        }

        return held;
    }

    public static BotListing Cheapest(Type kind, IBotWilful except)
    {
        BotListing best = null;

        for (var i = 0; i < _listings.Count; i++)
        {
            var stall = _listings[i];

            if (stall.Kind != kind || stall.IsEmpty || ReferenceEquals(stall.Seller, except))
            {
                continue;
            }

            if (stall.Seller?.Self is not { Deleted: false })
            {
                continue;
            }

            if (best == null || stall.Price < best.Price)
            {
                best = stall;
            }
        }

        return best;
    }

    private static void Settle(Mobile seller, int bill)
    {
        if (seller == null || bill <= 0)
        {
            return;
        }

        var keeper = Keeper();

        if (keeper == null || ReferenceEquals(keeper, seller))
        {
            Banker.Deposit(seller, bill);

            return;
        }

        var cut = Math.Min(bill, Math.Max(LeastLevy, (int)(bill * Levy)));

        Banker.Deposit(seller, bill - cut);
        Banker.Deposit(keeper, cut);

        Levied += cut;
        Levies++;
    }

    private static Mobile Keeper()
    {
        var bots = BotPopulation.Bots;

        for (var i = 0; i < bots.Count; i++)
        {
            if (bots[i] is { Deleted: false, Alive: true, Class.Levies: true } keeper)
            {
                return keeper;
            }
        }

        return null;
    }

    public static bool Selling(IBotWilful seller, Type kind) => Find(seller, kind) is { IsEmpty: false };

    public static BotListing Find(IBotWilful seller, Type kind)
    {
        for (var i = 0; i < _listings.Count; i++)
        {
            var stall = _listings[i];

            if (ReferenceEquals(stall.Seller, seller) && stall.Kind == kind)
            {
                return stall;
            }
        }

        return null;
    }

    public static (int Lots, int Units, int Paid) Crown(int lots, int budget = int.MaxValue, bool stuckFirst = false, double share = 1.0)
    {
        if (lots <= 0 || budget <= 0 || _listings.Count == 0)
        {
            return (0, 0, 0);
        }

        List<BotListing> open = [];
        List<BotListing> stuck = [];
        var now = Core.TickCount;

        for (var i = 0; i < _listings.Count; i++)
        {
            var stall = _listings[i];

            if (stall.IsEmpty)
            {
                continue;
            }

            if (stuckFirst && now - stall.ListedTick >= StuckMs)
            {
                stuck.Add(stall);
            }
            else
            {
                open.Add(stall);
            }
        }

        var taken = 0;
        var units = 0;
        var paid = 0;

        for (var i = 0; i < lots && (stuck.Count > 0 || open.Count > 0); i++)
        {
            var from = stuck.Count > 0 ? stuck : open;
            var pick = Utility.Random(from.Count);
            var stall = from[pick];

            from.RemoveAt(pick);

            var price = stall.Price;

            if (price <= 0 || stall.Amount <= 0)
            {
                continue;
            }

            var each = Math.Max(1, (int)Math.Ceiling(price * share));
            var wanted = Math.Min(stall.Amount, (budget - paid) / each);

            if (wanted <= 0)
            {
                continue;
            }

            var (given, bill) = Purchase(stall, wanted, share);

            if (given <= 0)
            {
                continue;
            }

            taken++;
            units += given;
            paid += bill;
        }

        return (taken, units, paid);
    }

    public static (int Units, int Paid) CrownWant(Type kind, int wanted, int maxPrice, int budget)
    {
        if (kind == null || wanted <= 0 || maxPrice <= 0 || budget <= 0 || _listings.Count == 0)
        {
            return (0, 0);
        }

        List<BotListing> offers = [];

        for (var i = 0; i < _listings.Count; i++)
        {
            var stall = _listings[i];

            if (!stall.IsEmpty && stall.Kind == kind && stall.Price > 0 && stall.Price <= maxPrice)
            {
                offers.Add(stall);
            }
        }

        if (offers.Count == 0)
        {
            return (0, 0);
        }

        offers.Sort(static (a, b) => a.Price.CompareTo(b.Price));

        var units = 0;
        var paid = 0;

        for (var i = 0; i < offers.Count && units < wanted; i++)
        {
            var stall = offers[i];
            var take = Math.Min(Math.Min(stall.Amount, wanted - units), (budget - paid) / stall.Price);

            if (take <= 0)
            {
                break;
            }

            var (given, bill) = Purchase(stall, take);

            if (given <= 0)
            {
                continue;
            }

            units += given;
            paid += bill;
        }

        return (units, paid);
    }

    private static (int Given, int Bill) Purchase(BotListing stall, int wanted, double share = 1.0)
    {
        var price = stall.Price;

        var crate = new Backpack();
        var given = stall.Deliver(wanted, crate);

        crate.Delete();

        if (given <= 0)
        {
            return (0, 0);
        }

        var bill = share >= 1.0 ? given * price : Math.Max(1, (int)Math.Ceiling(given * price * share));
        var seller = stall.Seller?.Self;

        Settle(seller, bill);

        Sales++;
        Turnover += bill;

        stall.Note(given, given * price, BriskMs);

        logger.Information(
            "The city bought {Units} {Item} from {Seller} for {Paid}gp, {Share:P0} of the asking price",
            given,
            stall.Label,
            seller?.Name ?? "nobody",
            bill,
            share
        );

        return (given, bill);
    }

    public static int Buy(Mobile buyer, BotListing stall, int units)
    {
        var pack = buyer?.Backpack;

        if (pack == null || stall == null || units <= 0)
        {
            return 0;
        }

        var stock = stall.Amount;

        if (stock <= 0)
        {
            return 0;
        }

        if (units > stock)
        {
            units = stock;
        }

        var price = stall.Price;
        var bill = price * units;

        if (!Charge(buyer, bill))
        {
            return 0;
        }

        var given = stall.Deliver(units, pack);

        if (given <= 0)
        {
            Refund(buyer, bill);

            return 0;
        }

        if (given < units)
        {
            Refund(buyer, (units - given) * price);

            bill = given * price;
        }

        var seller = stall.Seller?.Self;

        Settle(seller, bill);

        Sales++;
        Turnover += bill;

        BotRegard.Traded((buyer?.Guild as Guilds.Guild)?.Name, (seller?.Guild as Guilds.Guild)?.Name);

        if (stall.Note(given, bill, BriskMs) && stall.Raise(RaiseStep, MostMultiple))
        {
            Raises++;

            logger.Information(
                "{Name} put {Item} up to {Price}gp after selling {Units} again soon",
                seller?.Name,
                stall.Label,
                stall.Price,
                given
            );
        }

        return given;
    }

    public static BotWant Ask(IBotWilful buyer, Type kind, int units, int offer)
    {
        var body = buyer?.Self;

        if (body == null || kind == null || units <= 0 || offer <= 0)
        {
            return null;
        }

        if (Selling(buyer, kind))
        {
            Sells++;

            var took = Reclaim(buyer, kind);

            if (took > 0)
            {
                Recalled++;

                logger.Information(
                    "{Name} wanted {Item} and was selling {Units} of them, so it has taken them back off its own stall",
                    body.Name,
                    kind.Name,
                    took
                );

                (body as BotMobile)?.Rearm();
            }

            return null;
        }

        _worthless.Remove(kind);

        var want = Wanted(buyer, kind);
        var bill = units * (want?.Offer ?? offer);

        if (!Charge(body, bill))
        {
            Unfunded++;

            return null;
        }

        if (want != null)
        {
            want.Top(units, bill);

            return want;
        }

        if (_wants.Count >= MaxWants)
        {
            Refund(body, bill);

            logger.Error(
                "The market already holds {Count} wants, so {Name} could not ask for {Item}",
                _wants.Count,
                body.Name,
                kind.Name
            );

            return null;
        }

        want = new BotWant(++_nextWantId, buyer, kind, units, offer);

        want.Top(0, bill);

        _wants.Add(want);

        Posted++;

        logger.Information(
            "{Name} wants {Units} {Item} and has put {Gold}gp down for them",
            body.Name,
            units,
            kind.Name,
            bill
        );

        return want;
    }

    public static BotWant Wanted(IBotWilful buyer, Type kind)
    {
        for (var i = 0; i < _wants.Count; i++)
        {
            var want = _wants[i];

            if (ReferenceEquals(want.Buyer, buyer) && want.Kind == kind)
            {
                return want;
            }
        }

        return null;
    }

    public static BotWant Demand(IBotWilful supplier, Type kind)
    {
        BotWant best = null;

        if (kind == null || Wanted(supplier, kind) != null)
        {
            return null;
        }

        for (var i = 0; i < _wants.Count; i++)
        {
            var want = _wants[i];

            if (want.Kind != kind || !want.IsOpen || ReferenceEquals(want.Buyer, supplier))
            {
                continue;
            }

            if (want.Buyer?.Self is not { Deleted: false } || !want.Yields(supplier, SliceMs))
            {
                continue;
            }

            if (best == null || want.Offer > best.Offer)
            {
                best = want;
            }
        }

        return best;
    }

    public static int Best(Type kind)
    {
        var best = 0;

        for (var i = 0; i < _wants.Count; i++)
        {
            var want = _wants[i];

            if (want.Kind == kind && want.IsOpen && want.Offer > best)
            {
                best = want.Offer;
            }
        }

        return best;
    }

    public static int Worth(Type kind, int fallback)
    {
        var bid = Best(kind);

        if (bid > 0)
        {
            return fallback > 0 ? Math.Min(bid, (int)Math.Min(int.MaxValue, fallback * (long)Math.Max(1.0, MostMultiple))) : bid;
        }

        for (var i = 0; i < _listings.Count; i++)
        {
            var stall = _listings[i];

            if (stall.Kind == kind && stall.Traded && stall.Sold > 0)
            {
                return Math.Max(1, stall.Earned / stall.Sold);
            }
        }

        return fallback;
    }

    public static int Fill(IBotWilful supplier, BotWant want, Item goods)
    {
        var body = supplier?.Self;

        if (body == null || want == null || goods == null || goods.Deleted)
        {
            return 0;
        }

        if (BotBinding.Refuses(goods, supplier.Bond))
        {
            return 0;
        }

        if (ReferenceEquals(want.Buyer, supplier) || want.Kind != goods.GetType())
        {
            return 0;
        }

        if (!want.IsOpen || !want.Yields(supplier, SliceMs) || Wanted(supplier, want.Kind) != null)
        {
            return 0;
        }

        var held = Math.Max(1, goods.Amount);
        var units = Math.Min(Math.Min(held, want.Payable), Math.Max(1, Slice));

        if (units <= 0)
        {
            return 0;
        }

        if (units < held)
        {
            goods = BotListing.Portion(goods, units);

            if (goods == null)
            {
                return 0;
            }
        }

        var brisk = want.Take(goods, supplier, units, BriskMs);
        var bill = Math.Abs(brisk);

        Settle(body, bill);

        Fills++;
        Filled += units;
        Turnover += bill;

        if (want.Buyer != null)
        {
            _holding.Add(want.Buyer);
        }

        logger.Information(
            "{Name} filled {Buyer}'s want for {Units} {Item} and was paid {Gold}gp",
            body.Name,
            want.Buyer?.Self?.Name,
            units,
            want.Label,
            bill
        );

        if (brisk > 0 && want.Cut(CutStep, LeastMultiple))
        {
            Cuts++;

            logger.Information(
                "{Name} dropped its offer for {Item} to {Offer}gp after being filled again soon",
                want.Buyer?.Self?.Name,
                want.Label,
                want.Offer
            );
        }

        return units;
    }

    public static int Owed(IBotWilful buyer)
    {
        if (buyer == null)
        {
            return 0;
        }

        var owed = 0;

        for (var i = 0; i < _wants.Count; i++)
        {
            if (ReferenceEquals(_wants[i].Buyer, buyer))
            {
                owed += _wants[i].Waiting;
            }
        }

        return owed;
    }

    private static readonly HashSet<IBotWilful> _holding = [];

    public static long Fetches { get; private set; }

    public static long Fetched { get; private set; }

    public static int Fetch(IBotWilful buyer)
    {
        if (buyer?.Self is not { Deleted: false, Alive: true } body || !_holding.Contains(buyer))
        {
            return 0;
        }

        var took = Collect(buyer);

        if (Owed(buyer) <= 0)
        {
            _holding.Remove(buyer);
        }

        if (took <= 0)
        {
            return 0;
        }

        Fetches++;
        Fetched += took;

        (body as BotMobile)?.Rearm();

        return took;
    }

    public static int Collect(IBotWilful buyer)
    {
        var pack = buyer?.Self?.Backpack;

        if (pack == null)
        {
            return 0;
        }

        var taken = 0;

        for (var i = 0; i < _wants.Count; i++)
        {
            if (ReferenceEquals(_wants[i].Buyer, buyer))
            {
                taken += _wants[i].Collect(pack);
            }
        }

        return taken;
    }

    public static int Withdrawn(IBotWilful buyer, Type kind)
    {
        var want = Wanted(buyer, kind);

        if (want == null)
        {
            return 0;
        }

        var owed = want.Close();

        want.Collect(buyer?.Self?.Backpack);

        _wants.Remove(want);

        if (owed > 0)
        {
            Refund(buyer?.Self, owed);
        }

        return owed;
    }

    public static (int Open, int Worth) Asking(IBotWilful buyer)
    {
        var open = 0;
        var worth = 0;

        for (var i = 0; i < _wants.Count; i++)
        {
            var want = _wants[i];

            if (!ReferenceEquals(want.Buyer, buyer) || !want.IsOpen)
            {
                continue;
            }

            open++;
            worth += want.Worth;
        }

        return (open, worth);
    }

    public static int Escrowed(Mobile who)
    {
        if (who == null)
        {
            return 0;
        }

        var down = 0;

        for (var i = 0; i < _wants.Count; i++)
        {
            var want = _wants[i];

            if (want != null && ReferenceEquals(want.Buyer?.Self, who))
            {
                down += want.Escrow;
            }
        }

        return down;
    }

    public static (int Units, int Escrow) Sought()
    {
        var units = 0;
        var escrow = 0;

        for (var i = 0; i < _wants.Count; i++)
        {
            units += _wants[i].Payable;
            escrow += _wants[i].Escrow;
        }

        return (units, escrow);
    }

    public static (int Ordered, int Listed) Offer(IBotWilful seller, Item goods, int fallback)
    {
        if (seller?.Self == null || goods is not { Deleted: false, Movable: true })
        {
            return (0, 0);
        }

        var kind = goods.GetType();
        var held = Math.Max(1, goods.Amount);
        var worth = Worth(kind, fallback);

        var want = Demand(seller, kind);
        var ordered = want == null ? 0 : Fill(seller, want, goods);

        if (ordered >= held || goods.Deleted)
        {
            return (ordered, 0);
        }

        var listed = List(seller, goods, worth) == null ? 0 : held - ordered;

        return (ordered, listed);
    }

    public static int Reclaim(IBotWilful seller, Type kind)
    {
        var pack = seller?.Self?.Backpack;
        var stall = Find(seller, kind);

        return pack == null || stall == null ? 0 : stall.Return(seller.Self);
    }

    public static int Withdraw(IBotWilful seller)
    {
        var box = seller?.Self?.BankBox;
        var closed = 0;

        for (var i = _listings.Count - 1; i >= 0; i--)
        {
            if (!ReferenceEquals(_listings[i].Seller, seller))
            {
                continue;
            }

            _listings[i].Reclaim(box);
            _listings.RemoveAt(i);

            closed++;
        }

        return closed;
    }

    public static int StallsOf(IBotWilful seller)
    {
        var stalls = 0;

        for (var i = 0; i < _listings.Count; i++)
        {
            if (ReferenceEquals(_listings[i].Seller, seller) && !_listings[i].IsEmpty)
            {
                stalls++;
            }
        }

        return stalls;
    }

    public static int UnitsOf(IBotWilful seller)
    {
        var units = 0;

        for (var i = 0; i < _listings.Count; i++)
        {
            if (ReferenceEquals(_listings[i].Seller, seller))
            {
                units += _listings[i].Amount;
            }
        }

        return units;
    }

    public static int WorthOf(IBotWilful seller)
    {
        var worth = 0;

        for (var i = 0; i < _listings.Count; i++)
        {
            if (ReferenceEquals(_listings[i].Seller, seller))
            {
                worth += _listings[i].Worth;
            }
        }

        return worth;
    }

    public static (int Units, int Worth) Offered()
    {
        var units = 0;
        var worth = 0;

        for (var i = 0; i < _listings.Count; i++)
        {
            units += _listings[i].Amount;
            worth += _listings[i].Worth;
        }

        return (units, worth);
    }

    private static void Beat()
    {
        BeatStalls();
        Cross();
        BeatWants();
    }

    private static int Cross()
    {
        var crossed = 0;

        for (var i = 0; i < _wants.Count; i++)
        {
            var want = _wants[i];

            if (!want.IsOpen)
            {
                continue;
            }

            var stall = Cheapest(want.Kind, want.Buyer);

            if (stall == null || stall.IsEmpty)
            {
                continue;
            }

            if (stall.Price > want.Offer)
            {
                Dear++;

                continue;
            }

            var seller = stall.Seller;
            var body = seller?.Self;

            if (body is not { Deleted: false })
            {
                continue;
            }

            if (ReferenceEquals(want.Buyer, seller) || !want.Yields(seller, SliceMs) || Wanted(seller, want.Kind) != null)
            {
                continue;
            }

            var units = Math.Min(Math.Min(stall.Amount, want.Payable), Math.Max(1, Slice));

            if (units <= 0)
            {
                continue;
            }

            var goods = stall.Lift(units);

            if (goods == null)
            {
                continue;
            }

            var filled = Fill(seller, want, goods);

            if (filled <= 0)
            {
                stall.Add(goods);

                continue;
            }

            crossed += filled;
            Crossed += filled;

            if (stall.Note(filled, filled * want.Offer, BriskMs) && stall.Raise(RaiseStep, MostMultiple))
            {
                Raises++;

                logger.Information(
                    "{Name} put {Item} up to {Price}gp after the board took {Units} off the stall at once",
                    body.Name,
                    stall.Label,
                    stall.Price,
                    filled
                );
            }
        }

        return crossed;
    }

    private static void BeatWants()
    {
        var now = Core.TickCount;

        for (var i = _wants.Count - 1; i >= 0; i--)
        {
            var want = _wants[i];
            var buyer = want.Buyer?.Self;

            if (buyer == null || buyer.Deleted)
            {
                want.Discard();
                _wants.RemoveAt(i);

                continue;
            }

            if (want.Waiting > 0)
            {
                want.Collect(buyer.Backpack);
            }

            if (!want.IsOpen)
            {
                if (want.Waiting <= 0)
                {
                    var owed = want.Close();

                    _wants.RemoveAt(i);

                    Refund(buyer, owed);
                    Forgotten++;

                    continue;
                }

                if (now - want.TouchedTick >= ForgetMs)
                {
                    var owed = want.Close();

                    want.Collect(buyer.Backpack);
                    _wants.RemoveAt(i);

                    Refund(buyer, owed);
                    Forgotten++;
                }

                continue;
            }

            if (now - want.TouchedTick < RaiseMs)
            {
                continue;
            }

            var stepped = want.Stepped(RaiseStep, MostMultiple);
            var added = 0;

            if (stepped > 0)
            {
                added = Math.Max(0, want.Amount * stepped - want.Escrow);

                if (added > 0 && !Charge(buyer, added))
                {
                    added = Math.Max(0, stepped - want.Escrow);

                    if (added > 0 && !Charge(buyer, added))
                    {
                        stepped = 0;
                    }
                }
            }

            if (stepped > 0)
            {
                want.Top(0, added);
                want.Lift(stepped);

                Raises++;

                logger.Information(
                    "{Name} raised its offer for {Item} to {Offer}gp and put another {Gold}gp down after {Amount} went unfilled",
                    buyer.Name,
                    want.Label,
                    want.Offer,
                    added,
                    want.Amount
                );

                continue;
            }

            var left = want.Close();

            want.Collect(buyer.Backpack);
            _wants.RemoveAt(i);

            Refund(buyer, left);
            Abandoned++;

            logger.Information(
                "{Name} gave up wanting {Item} at {Offer}gp of a possible {Ceiling} and took back {Gold}gp",
                buyer.Name,
                want.Label,
                want.Offer,
                (int)(want.Anchor * MostMultiple),
                left
            );
        }
    }

    private static void Squeeze()
    {
        var oldest = -1;
        var since = long.MinValue;
        var now = Core.TickCount;

        for (var i = 0; i < _listings.Count; i++)
        {
            if (!_listings[i].IsEmpty)
            {
                continue;
            }

            var idle = now - _listings[i].TouchedTick;

            if (idle > since)
            {
                since = idle;
                oldest = i;
            }
        }

        if (oldest >= 0)
        {
            logger.Information(
                "The market was full, so {Stall} — sold out and untouched for {Idle}s — was forgotten to make room",
                _listings[oldest].Label,
                since / 1000
            );

            _listings.RemoveAt(oldest);
            Forgotten++;

            return;
        }

        Unsold();
    }

    private static void Unsold()
    {
        var now = Core.TickCount;
        var stalest = -1;
        var since = long.MinValue;

        for (var i = 0; i < _listings.Count; i++)
        {
            var stall = _listings[i];

            if (stall.Sold > 0)
            {
                continue;
            }

            var idle = now - stall.TouchedTick;

            if (idle > since)
            {
                since = idle;
                stalest = i;
            }
        }

        if (stalest < 0)
        {
            return;
        }

        var doomed = _listings[stalest];
        var pack = doomed.Seller?.Self?.Backpack;

        if (pack == null)
        {
            return;
        }

        var given = doomed.Reclaim(pack);

        logger.Information(
            "The market was full of unsold pitches, so {Stall} — never once bought from, standing {Idle}s — was taken down and {Given} handed back to {Name}",
            doomed.Label,
            since / 1000,
            given,
            doomed.Seller?.Self?.Name ?? "nobody"
        );

        _listings.RemoveAt(stalest);
        Forgotten++;
    }

    private static void BeatStalls()
    {
        var now = Core.TickCount;

        for (var i = _listings.Count - 1; i >= 0; i--)
        {
            var stall = _listings[i];
            var seller = stall.Seller?.Self;

            if (seller == null || seller.Deleted)
            {
                stall.Discard();
                _listings.RemoveAt(i);

                continue;
            }

            if (stall.IsEmpty)
            {
                if (now - stall.TouchedTick >= ForgetMs)
                {
                    _listings.RemoveAt(i);
                    Forgotten++;
                }

                continue;
            }

            if (now - stall.DealtTick < StaleMs)
            {
                continue;
            }

            if (stall.Cut(CutStep, LeastMultiple))
            {
                Cuts++;

                logger.Information(
                    "{Name} cut {Item} to {Price}gp after {Amount} sat unsold",
                    seller.Name,
                    stall.Label,
                    stall.Price,
                    stall.Amount
                );

                continue;
            }

            if (now - stall.ListedTick < StuckMs)
            {
                continue;
            }

            var pack = seller.Backpack;
            var back = pack == null ? 0 : stall.Return(seller);

            if (back <= 0)
            {
                Unreclaimed++;

                continue;
            }

            Returned += back;
            Stood++;

            logger.Information(
                "{Name} took back {Amount} {Item} after {Minutes} minutes at {Price}gp, its lowest ask",
                seller.Name,
                back,
                stall.Label,
                (now - stall.ListedTick) / 60000,
                stall.Price
            );

            if (stall.IsEmpty)
            {
                _listings.RemoveAt(i);
            }
        }
    }

    private static long _unfundedSaidTick;

    public static int UnfundedSayMs { get; set; } = 60000;

    public static bool Charge(Mobile buyer, int bill)
    {
        if (bill <= 0)
        {
            return true;
        }

        var pack = buyer.Backpack;
        var purse = pack?.GetAmount(typeof(Gold)) ?? 0;
        var taken = 0;

        if (purse > 0)
        {
            taken = Math.Min(purse, bill);

            if (!pack.ConsumeTotal(typeof(Gold), taken))
            {
                taken = 0;
            }
        }

        var rest = bill - taken;

        if (rest <= 0)
        {
            return true;
        }

        if (Banker.Withdraw(buyer, rest))
        {
            return true;
        }

        if (taken > 0)
        {
            pack.DropItem(new Gold(taken));
        }

        var now = Core.TickCount;

        if (now - (_unfundedSaidTick + UnfundedSayMs) >= 0)
        {
            _unfundedSaidTick = now;

            logger.Warning(
                "{Name} could not put {Bill}gp down: the pack held {Purse} of which {Taken} were consumed, the bank held {Balance} and refused {Rest}",
                buyer.Name,
                bill,
                purse,
                taken,
                Banker.GetBalance(buyer),
                rest
            );
        }

        return false;
    }

    private static void Refund(Mobile buyer, int amount)
    {
        if (amount > 0)
        {
            Banker.Deposit(buyer, amount);
        }
    }

    public static string Board(int most = 6)
    {
        Dictionary<Type, int> wanted = [];
        Dictionary<Type, int> stocked = [];

        for (var i = 0; i < _wants.Count; i++)
        {
            var want = _wants[i];

            if (want?.Kind != null && want.IsOpen)
            {
                wanted[want.Kind] = wanted.TryGetValue(want.Kind, out var had) ? had + want.Amount : want.Amount;
            }
        }

        for (var i = 0; i < _listings.Count; i++)
        {
            var stall = _listings[i];

            if (stall?.Kind != null && !stall.IsEmpty)
            {
                stocked[stall.Kind] = stocked.TryGetValue(stall.Kind, out var had) ? had + stall.Amount : stall.Amount;
            }
        }

        return $"most wanted: {Top(wanted, most)}; most stocked: {Top(stocked, most)}";
    }

    private static string Top(Dictionary<Type, int> tally, int most)
    {
        if (tally.Count == 0)
        {
            return "nothing";
        }

        List<(Type Kind, int Amount)> ordered = [];

        foreach (var (kind, amount) in tally)
        {
            ordered.Add((kind, amount));
        }

        ordered.Sort(static (a, b) => b.Amount.CompareTo(a.Amount));

        var say = ValueStringBuilder.Create(160);

        try
        {
            for (var i = 0; i < ordered.Count && i < most; i++)
            {
                if (i > 0)
                {
                    say.Append(", ");
                }

                say.Append(ordered[i].Kind.Name);
                say.Append(' ');
                say.Append(ordered[i].Amount);
            }

            return say.ToString();
        }
        finally
        {
            say.Dispose();
        }
    }

    public static int StuckMs { get; set; } = 1800000;

    public static (int Stalls, int Things, int Worth, int OldestMinutes) Stuck()
    {
        var now = Core.TickCount;
        var stalls = 0;
        var things = 0;
        var worth = 0;
        var oldest = 0;

        for (var i = 0; i < _listings.Count; i++)
        {
            var stall = _listings[i];

            if (stall == null || stall.IsEmpty)
            {
                continue;
            }

            var age = now - stall.ListedTick;

            if (age < StuckMs)
            {
                continue;
            }

            stalls++;
            things += stall.Amount;
            worth += stall.Price * stall.Amount;

            var minutes = (int)(age / 60000);

            if (minutes > oldest)
            {
                oldest = minutes;
            }
        }

        return (stalls, things, worth, oldest);
    }

    public static string Describe()
    {
        var (units, worth) = Offered();
        var (sought, escrow) = Sought();
        var (stuckStalls, stuckThings, stuckWorth, stuckOldest) = Stuck();

        return $"{_listings.Count} of {MaxListings} stalls holding {units} things worth {worth}gp and {_wants.Count} of {MaxWants} wants for {sought} things with {escrow}gp down; {Sales} sales and {Fills} fills for {Turnover}gp, of which {Crossed} things went straight off a stall to a want on the board and {Dear} wants found the thing on a stall dearer than they would pay; {Raises} prices raised, {Cuts} cut, of which {BotHaggle.Describe()}, {Forgotten} forgotten, {Abandoned} given up on; {Sells} orders refused to bots already selling the thing, {Recalled} of them settled by taking it back off the stall and {Unfunded} to bots that could not put the money down; {Cheap} things of {_worthless.Count} kinds were worth less than the {Floor}gp floor and stayed in the pack ({Unpriced} of them because nothing could price them at all, which condemns no kind); the condemned kinds are {Condemned(8)}; {Fetches} deliveries fetched off the board holding {Fetched} things; the levy has taken {Levied}gp over {Levies} sales; {stuckStalls} stalls have stood more than {StuckMs / 60000} minutes holding {stuckThings} things at {stuckWorth}gp, the oldest for {stuckOldest} minutes; {Stood} stalls were taken off the board at their lowest ask and {Returned} things went back to their sellers, {Unreclaimed} could not be handed back";
    }

    private sealed class AuctionTimer : Timer
    {
        public AuctionTimer(TimeSpan interval) : base(interval, interval)
        {
        }

        protected override void OnTick() => Beat();
    }
}
