using System;
using System.Collections.Generic;
using Server.Items;
using Server.Logging;
using Server.Mobiles;

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

    /// <summary>How much a bot puts its price up when the same goods sell again soon.</summary>
    public static double RaiseStep { get; set; } = 0.15;

    /// <summary>How much it comes down when nothing has happened for a while.</summary>
    public static double CutStep { get; set; } = 0.10;

    /// <summary>
    /// How soon after a sale another sale counts as brisk. Ten minutes.
    ///
    /// <b>Brisk is "again, soon", not "a lot".</b> A big single purchase says somebody wanted a lot at the
    /// price already asked; two purchases close together are what say the price was too low.
    /// </summary>
    public static int BriskMs { get; set; } = 600000;

    /// <summary>
    /// How long a stall may sit untouched before the price comes down. Ten minutes.
    ///
    /// <para>
    /// <b>Lowered from half an hour on Patrick's order of 05.09.2026, against a market that had grown faster
    /// than its own markdown.</b> A night of opening the supply side — see <c>BotUnload.Wanted</c> — took the
    /// stalls from 1558 things to 3584 in the same forty minutes, and at half an hour a step the pace works
    /// out at one cut per listing per half hour: a price set at twice what anybody will pay needs seven of
    /// them, which is three and a half hours. Over that window the population's own turnover fell from
    /// 7288gp to 4898gp while its takings from shopkeepers rose — the surplus was leaving through the
    /// counters rather than moving between bots, which is the one thing this market exists to prevent.
    /// </para>
    ///
    /// <para>
    /// The cut is measured from the last <em>sale</em> rather than the last touch — see
    /// <c>BotAuction.BeatStalls</c>, which carries the reason — so shortening it does not punish a stall
    /// that is selling. It only reaches the ones nobody is buying from, which are exactly the ones whose
    /// price is wrong.
    /// </para>
    /// </summary>
    public static int StaleMs { get; set; } = 600000;

    /// <summary>
    /// The least a thing may be offered for, and therefore the least it may fall to.
    ///
    /// <para>
    /// <b>A stall at one gold is a stall that will never empty, and an empty stall is the only way this
    /// market makes room.</b> Prices fall on their own when nothing sells, all the way to a quarter of the
    /// opening ask, so raw ribs and old boots settled at the floor and then sat there for ever holding a
    /// pitch that iron and leather wanted. Two is where a thing stops being worth the walk to fetch it: below
    /// that the seller is not trading, it is storing.
    /// </para>
    ///
    /// <para>
    /// Read in both directions — nothing is listed below it and no cut may take a price under it — because a
    /// floor enforced on the way in and not on the way down is not a floor.
    /// </para>
    /// </summary>
    public static int Floor { get; set; } = 2;

    /// <summary>
    /// The levy taken on every sale this market settles, as a share, with a minimum of one gold.
    ///
    /// <para>
    /// <b>Out of the seller's share, never minted.</b> See <see cref="BotClass.Levies"/>: this shard has one
    /// faucet and it is a monster's purse. A hundredth of every trade is small enough that no seller changes
    /// what it does because of it and large enough that the bot it goes to is paid by the health of the
    /// market rather than by any errand — which is the whole idea of the office.
    /// </para>
    ///
    /// <para>
    /// The minimum is what makes it real at this scale: a hundredth of a twenty-gold cap is nothing at all,
    /// and a levy that rounds to nothing on most of the shard's trade would be a rule that exists only in
    /// the summary. With <see cref="Floor"/> at two, one gold is never more than half a sale.
    /// </para>
    /// </summary>
    public static double Levy { get; set; } = 0.01;

    public static int LeastLevy { get; set; } = 1;

    /// <summary>What the levy has taken, and for whom. Nought when nobody on the shard holds the office.</summary>
    public static long Levied { get; private set; }

    public static long Levies { get; private set; }

    /// <summary>The most, and the least, a price may become as a multiple of what the stall first asked.</summary>
    public static double MostMultiple { get; set; } = 4.0;

    public static double LeastMultiple { get; set; } = 0.25;

    /// <summary>
    /// How long an empty stall is kept before it is forgotten.
    ///
    /// An empty stall is not nothing: it is the bot's remembered price and its sales history, and topping it
    /// up is how a second load inherits both. An hour is long enough to survive the walk back to the mine.
    /// </summary>
    public static int ForgetMs { get; set; } = 3600000;

    /// <summary>How often the market looks at itself: stale prices come down, empty stalls are forgotten.</summary>
    public static int BeatMs { get; set; } = 30000;

    /// <summary>
    /// How many stalls the market may hold at once.
    ///
    /// <para>
    /// <b>A backstop against a leak, and never a shelf to run out of.</b> A stall is one seller and one kind
    /// of thing, so the honest ceiling is the population times the number of kinds it deals in — twenty bots
    /// across the thirty-seven kinds that reached this market on the night of 25.08.2026 is seven hundred
    /// odd, and the cap was two hundred and fifty-six. It was reached, and then it was reached again: 302
    /// refusals in ten hours, and what could not be put out was <c>IronIngot</c> fifty times and
    /// <c>Leather</c> forty-one — the two things armour is made of, kept off the market by raw ribs and old
    /// boots. A market that is full stops being a market and becomes a queue.
    /// </para>
    ///
    /// <para>
    /// A thousand and twenty-four, which is above that ceiling with the population doubled. The number is
    /// still here because an unbounded list with no eviction is how a shard leaks a night's memory, not
    /// because there is any virtue in a small market — and the two evictions below (<see cref="Squeeze"/>
    /// and the never-sold sweep) are what keep it honest if it is ever reached again. Every lookup on this
    /// list is a scan, so it is not free: at four times the old size it is four times the work, on a list
    /// walked a handful of times per bot per beat — tens of thousands of comparisons a second at worst,
    /// which is nothing beside one path search.
    /// </para>
    /// </summary>
    public static int MaxListings { get; set; } = 1024;

    /// <summary>
    /// How many wants the market may hold at once.
    ///
    /// <para>
    /// <b>Five hundred and twelve, and the old hundred and twenty-eight was a ceiling this shard grew
    /// through on the night the population went from thirty-four to fifty-four.</b> The board filled, and
    /// every bot that wanted anything then took an errand which failed on its first beat and was offered
    /// again immediately: 176 orders and 125 purchases in one half-hour window, 85 of them one bot asking
    /// for one scroll. The reasoning is <see cref="MaxListings"/>'s, which says it plainly — the number is
    /// there so an unbounded list cannot leak a night's memory, not because a small market is a virtue, and
    /// every lookup is a scan on a short list walked a few times per bot per beat.
    /// </para>
    /// </summary>
    public static int MaxWants { get; set; } = 512;

    /// <summary>
    /// Whether the want board has no room left.
    ///
    /// <para>
    /// <b>Public because the check belongs in whoever is choosing, not in the work.</b> This shard has paid
    /// for that lesson twice now — a guard put inside an errand fails on the first beat, the errand is
    /// offered again on the next, and the result is a bot doing nothing at eight decisions a second while
    /// the log fills with a line apiece. Passing a candidate over costs nothing; failing an errand is a loop.
    /// </para>
    /// </summary>
    public static bool Full => _wants.Count >= MaxWants;

    /// <summary>
    /// The most units one supplier may deliver against one want at a time.
    ///
    /// A speed rather than a price, like everything else in this file. See <see cref="BotWant.Yields"/> for
    /// what it is for: without it the first bot to own a pile owns every want for that pile.
    /// </summary>
    public static int Slice { get; set; } = 5;

    /// <summary>How long a want holds a supplier off before it will take from the same one again.</summary>
    public static int SliceMs { get; set; } = 60000;

    private static readonly List<BotListing> _listings = [];

    private static readonly List<BotWant> _wants = [];

    private static AuctionTimer _timer;

    /// <summary>
    /// Orders the board turned down because the buyer has that very thing out on a stall of its own.
    ///
    /// <para>
    /// <b>Three ways of refusing an order used to be one silence.</b> <c>Ask</c> answered null and the
    /// caller printed "the board would not take an order for IronIngot" — twenty-four times in an hour on
    /// 26.08.2026, with no way to tell "it is already selling them", "it cannot pay for them" and "there is
    /// no room" apart. A refusal that is not named is a question that looks answered.
    /// </para>
    /// </summary>
    public static long Sells { get; private set; }

    /// <summary>
    /// Kinds of thing the market has refused as worth less than <see cref="Floor"/>.
    ///
    /// <para>
    /// <b>The refusal existed and was never written down, and that one omission cost 279,067 walks to a bank
    /// counter in eight hours.</b> A porter decides to set out by counting what in its pack is <em>its own to
    /// sell</em>; the market decides on arrival by asking what the thing is <em>worth</em>. Those are two
    /// different questions and a rusty dagger answers yes to the first and no to the second — for ever. The
    /// log had both numbers side by side in every line of it — "22 the market would not take; the porter
    /// counted 22 worth leaving when it set out" — and they disagreed a quarter of a million times without
    /// anything being able to act on it, because nothing kept the answer.
    /// </para>
    ///
    /// <para>
    /// Shard-wide and by kind rather than by item: what a rusty dagger is worth is a fact about the market,
    /// not about the dagger in this bot's pack, and fifteen bots each discovering it separately is fifteen
    /// bots each making the trip. Cleared the moment anybody wants one — see <see cref="Ask"/> — because a
    /// want is the market saying the thing has a price after all.
    /// </para>
    /// </summary>
    private static readonly HashSet<Type> _worthless = [];

    /// <summary>Whether the market has already refused this kind of thing as below the floor.</summary>
    public static bool Worthless(Type kind) => kind != null && _worthless.Contains(kind);

    /// <summary>Times a bot took its own goods back rather than ordering what it was already selling.</summary>
    public static long Recalled { get; private set; }

    /// <summary>Things not put out at all because they were worth less than <see cref="Floor"/>.</summary>
    public static long Cheap { get; private set; }

    /// <summary>Orders turned down because the buyer could not put the money down. See <see cref="Selling"/>.</summary>
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

    /// <summary>Units that went straight off a stall to a want on the board. See <see cref="Cross"/>.</summary>
    public static long Crossed { get; private set; }

    /// <summary>
    /// Wants that found the thing on a stall and would not pay the asking price.
    ///
    /// <para>
    /// Not a failure of anything and counted so that it cannot be mistaken for one: a want raises its own
    /// offer every beat, so this is the market at work rather than the market stuck. It is here because
    /// "nobody is selling one" and "somebody is selling one dearer than I will pay" are different facts and
    /// were producing the same silence.
    /// </para>
    /// </summary>
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

    /// <summary>
    /// The whole market cleared, goods destroyed.
    ///
    /// Destroyed rather than returned, and it is not carelessness: this runs when the world is being replaced,
    /// and every seller in the list belongs to the world going away. Goods handed back to a bot that is about
    /// to be deleted are goods on a corpse nobody will ever loot.
    /// </summary>
    public static void Reset()
    {
        Stop();

        for (var i = 0; i < _listings.Count; i++)
        {
            _listings[i].Discard();
        }

        _listings.Clear();

        // The escrow is not handed back for the same reason the goods are not: every buyer in this list
        // belongs to the world going away, and gold deposited to a bot about to be deleted is gold on a
        // corpse nobody will ever loot.
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
    }

    /// <summary>
    /// Puts goods out, or adds them to what this bot already has out. Returns the stall.
    ///
    /// <para>
    /// <b>One stall per bot per kind.</b> The asking price of an existing stall is <em>not</em> overwritten by
    /// whatever the caller suggests: the price on it is what that bot has learned, and a fresh load of the
    /// same thing does not unlearn it. <paramref name="price"/> is therefore only the opening ask of a stall
    /// that does not exist yet.
    /// </para>
    /// </summary>
    public static BotListing List(IBotWilful seller, Item item, int price)
    {
        if (seller?.Self == null || item == null || item.Deleted)
        {
            return null;
        }

        // Nothing goes out below the market's floor. See BotAuction.Floor: a pitch held by something nobody
        // will ever walk across the map for is a pitch that never empties, and an empty stall is the only
        // way this market makes room for the next thing.
        if (price < Floor)
        {
            Cheap++;

            // Written down, so the fifteenth bot carrying one of these does not walk to a counter to find
            // out. See _worthless: the refusal was already being counted and was never being kept.
            _worthless.Add(item.GetType());

            return null;
        }

        // The sign flips before anything else happens. A bot that turns out to have the thing it was asking
        // for stops asking for it — and it gets its money back rather than being told no, because it is not
        // a rule being enforced against it, it is the same fact read the other way round.
        Withdrawn(seller, item.GetType());

        var stall = Find(seller, item.GetType());

        if (stall != null)
        {
            stall.Add(item);

            return stall;
        }

        if (_listings.Count >= MaxListings)
        {
            // <b>A sold-out stall must never be what keeps a full one off the market.</b> An empty stall is
            // kept for an hour on purpose — it is a seller's remembered price, and a second load inherits it
            // — but that is a convenience, and standing between a bot and a sale is not what it was for. The
            // longest-untouched empty goes, which is the one least likely to be topped up.
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

    /// <summary>
    /// The cheapest stall holding this kind of thing that is not this bot's own, or null.
    ///
    /// Cheapest rather than nearest, because this market is placeless: a stall holds its goods out of the
    /// world, so distance is not a fact about buying from one.
    /// </summary>
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

    /// <summary>
    /// Pays a seller what a sale came to, less the levy, and hands the levy to whoever holds that office.
    ///
    /// <para>
    /// <b>One place, because there are three moments money changes hands and a rule applied at two of them
    /// is not a rule.</b> A stall bought from, a lot the city takes, and a want filled by a supplier are the
    /// same event as far as this is concerned: somebody sold something, and a hundredth of it is the market's
    /// own keeper's.
    /// </para>
    ///
    /// <para>
    /// Nothing is created. The seller is paid the remainder, so the two deposits always add to exactly the
    /// bill — and when nobody on the shard holds the office, the seller is paid all of it and the levy simply
    /// does not exist.
    /// </para>
    /// </summary>
    private static void Settle(Mobile seller, int bill)
    {
        if (seller == null || bill <= 0)
        {
            return;
        }

        var keeper = Keeper();

        if (keeper == null || ReferenceEquals(keeper, seller))
        {
            // Its own sale. Taking a cut of itself would be arithmetic pretending to be an income.
            Banker.Deposit(seller, bill);

            return;
        }

        var cut = Math.Min(bill, Math.Max(LeastLevy, (int)(bill * Levy)));

        Banker.Deposit(seller, bill - cut);
        Banker.Deposit(keeper, cut);

        Levied += cut;
        Levies++;
    }

    /// <summary>The bot whose class takes the levy, or null when the population has none.</summary>
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

    /// <summary>
    /// Whether this bot has this kind of thing out on a stall of its own right now.
    ///
    /// <para>
    /// <b>One question, asked by both sides, and it had to become one because it was two.</b> <see cref="Ask"/>
    /// refuses an order from a bot that is selling the same thing — any of it, one ingot or fifty — and
    /// <c>BotBullion</c> was written to hold back only when the stall carried <em>enough</em>. Between one and
    /// enough sat a band where the smith ordered and the market refused, which is the shard's oldest defect
    /// shape wearing a new coat: two thresholds on one shelf. Thirty-one failed orders in an hour became
    /// eight, then two, and would never have reached nought while the two ends counted differently. Now
    /// neither end owns the rule.
    /// </para>
    /// </summary>
    public static bool Selling(IBotWilful seller, Type kind) => Find(seller, kind) is { IsEmpty: false };

    /// <summary>This bot's stall for this kind of thing, or null.</summary>
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

    /// <summary>
    /// Buys up to <paramref name="units"/> from a stall, and says how many were actually bought.
    ///
    /// <para>
    /// The order below is the whole of the money safety. Charge, then deliver, then pay — and refund whatever
    /// could not be delivered. Every one of those four steps can fail on its own, and none of them may leave
    /// the world with more gold than it started with.
    /// </para>
    /// </summary>
    /// <summary>
    /// The city sends for goods from far away: whole lots bought off the population's own stalls, paid for
    /// out of nothing. Returns how many lots were taken and what was paid for them.
    ///
    /// <para>
    /// <b>This prints money, deliberately and by order.</b> Every other coin on this shard comes out of a
    /// monster's purse, which makes the population's total wealth a slowly rising line with one source and
    /// several drains; a market with no outside demand is sixteen bots trading the same coins in a circle
    /// while their stalls silently fill with things none of them wants. This is the outside. It is wired to
    /// one button on a dashboard that only an administrator can open, and it is not offered to anything a bot
    /// can reach.
    /// </para>
    ///
    /// <para>
    /// Booked as a real sale — the seller is paid into the bank, the takings count, and the stall learns from
    /// it exactly as it learns from a bot buying — because the whole value of the thing is price discovery.
    /// A purchase the market does not notice would move goods and teach nobody anything.
    /// </para>
    ///
    /// <para>
    /// The goods leave the world. They were bought by somewhere that is not here, so there is no container
    /// they end up in, and a pile of city-bought cloth sitting in a chest would be a second problem.
    /// </para>
    /// </summary>
    public static (int Lots, int Units, int Paid) Crown(int lots)
    {
        if (lots <= 0 || _listings.Count == 0)
        {
            return (0, 0, 0);
        }

        // A snapshot of what is actually for sale, so the shuffle below cannot pick the same empty stall
        // twice and so removing stock mid-loop cannot disturb the walk.
        List<BotListing> open = [];

        for (var i = 0; i < _listings.Count; i++)
        {
            if (!_listings[i].IsEmpty)
            {
                open.Add(_listings[i]);
            }
        }

        var taken = 0;
        var units = 0;
        var paid = 0;

        for (var i = 0; i < lots && open.Count > 0; i++)
        {
            var pick = Utility.Random(open.Count);
            var stall = open[pick];

            open.RemoveAt(pick);

            var price = stall.Price;
            var wanted = stall.Amount;

            if (price <= 0 || wanted <= 0)
            {
                continue;
            }

            // Somewhere for Deliver to put them, and then nowhere: the city is not a place on this map.
            var crate = new Backpack();
            var given = stall.Deliver(wanted, crate);

            crate.Delete();

            if (given <= 0)
            {
                continue;
            }

            var bill = given * price;
            var seller = stall.Seller?.Self;

            Settle(seller, bill);

            Sales++;
            Turnover += bill;

            stall.Note(given, bill, BriskMs);

            taken++;
            units += given;
            paid += bill;

            logger.Information(
                "The city bought {Units} {Item} from {Seller} for {Paid}gp",
                given,
                stall.Label,
                seller?.Name ?? "nobody",
                bill
            );
        }

        return (taken, units, paid);
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
            // Nothing could be handed over — a type that cannot be split, or stock that vanished. The buyer
            // gets everything back.
            Refund(buyer, bill);

            return 0;
        }

        if (given < units)
        {
            Refund(buyer, (units - given) * price);

            bill = given * price;
        }

        var seller = stall.Seller?.Self;

        // Straight into the account: this is a market rather than a hand-off, and a seller standing in a
        // mine cannot be handed coin. Less the levy — see Settle.
        Settle(seller, bill);

        Sales++;
        Turnover += bill;

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

    /// <summary>
    /// Asks the population for something, with the money down. Returns the want.
    ///
    /// <para>
    /// <b>One want per buyer per kind, and it persists.</b> A bot decides what it wants many times a minute
    /// and the answer does not change between one beat and the next — the first version's boards filled with
    /// six hundred and eighty-eight identical lines in six minutes because posting was an event rather than a
    /// standing position. Here there is nothing to repost: asking again tops up the same want, and the offer
    /// already on it is not overwritten, because that offer is what this bot has learned.
    /// </para>
    ///
    /// <para>
    /// The gold is taken now, out of purse and account, or the want does not exist. Everything the demand
    /// side of this market can be trusted about follows from that one line.
    /// </para>
    /// </summary>
    public static BotWant Ask(IBotWilful buyer, Type kind, int units, int offer)
    {
        var body = buyer?.Self;

        if (body == null || kind == null || units <= 0 || offer <= 0)
        {
            return null;
        }

        // <b>The other half of the sign rule, and it used to be half a rule.</b> A bot with the thing on a
        // stall is not short of it, whatever it thinks, and a market that let it be both would be the ginseng
        // carousel with extra bookkeeping. That much was right. What was missing is what happens next: the
        // want was simply refused, the bot had no idea why, and it asked again on its next review — Edda
        // asked the board for LeatherBustierArms 849 times in twenty-five minutes on 27.08.2026, and every
        // one of them was refused because she was selling a pair.
        //
        // So it is settled rather than refused, exactly as the mirror of this rule in List settles it: a bot
        // that turns out to have the thing it was asking for stops asking, and a bot that turns out to be
        // selling the thing it needs takes it back off its own stall. Both directions now end with the bot
        // holding the item, which is the state it was trying to reach either way.
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

        // Somebody is prepared to pay for one, so whatever the market decided about this kind of thing when
        // it was last offered a rusty example of it is out of date. See _worthless: the mark is a shortcut
        // past a walk to the counter, not a verdict, and a want is the one thing that overturns it.
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
            // The money goes straight back: a want that does not exist has not been funded.
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

    /// <summary>This bot's want for this kind of thing, or null.</summary>
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

    /// <summary>
    /// The best open want for this kind that this supplier is allowed to fill, or null.
    ///
    /// Its own wants are skipped, and so is anything it is itself short of: a bot does not sell what it is
    /// queueing for. That second rule is the ginseng carousel, closed at the only place where both facts are
    /// visible at once.
    /// </summary>
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

    /// <summary>The best price anybody is currently offering for this kind, funded, or zero.</summary>
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

    /// <summary>
    /// What the shard reckons one of these is worth, and the number a producer should count its output at.
    ///
    /// <para>
    /// <b>This is how a shortage reaches the decision layer, and it needed no new mechanism to do it.</b>
    /// Demand first: what somebody will actually pay, with the money down. Then what one of these has really
    /// changed hands for on a stall. Only then the caller's stand-in — the hardcoded six a gold ingot was
    /// worth because nothing else could say. A producer counts what it made at this price, the takings go
    /// into the ledger, and the ledger raises its estimate of that work next time round. The market moves
    /// labour by being measured rather than by being consulted, which is one trip of latency and no new
    /// machinery at all.
    /// </para>
    /// </summary>
    public static int Worth(Type kind, int fallback)
    {
        var bid = Best(kind);

        if (bid > 0)
        {
            return bid;
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

    /// <summary>
    /// Delivers goods against a want, and says how many units went. The supplier is paid out of the money the
    /// buyer already put down.
    ///
    /// <para>
    /// No gold is created here and none can be: the escrow was taken out of the buyer's purse when the want
    /// was posted, so this is a transfer of a number that already exists. What is refused, and why, is the
    /// whole of the fairness of this market — its own want, a want for something it is itself queueing for,
    /// and more than a slice at a time.
    /// </para>
    /// </summary>
    public static int Fill(IBotWilful supplier, BotWant want, Item goods)
    {
        var body = supplier?.Self;

        if (body == null || want == null || goods == null || goods.Deleted)
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

        // Only part of the stack is wanted: the rest stays with the supplier, which is what a slice means.
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

        // The buyer now has something waiting. See Fetch: this is what keeps the reflex from walking the
        // whole board on every beat of every bot.
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

        // Filled again inside the brisk window: the offer was generous, so the buyer asks for less.
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

    /// <summary>
    /// How much is sitting on the board already made and paid for, waiting for this bot to come and take it.
    ///
    /// Asked before anything is ordered, so that a bot with a sword waiting for it goes and fetches that
    /// rather than ordering a second one. See <see cref="BotUpkeep"/>.
    /// </summary>
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

    /// <summary>
    /// Buyers with something sitting on the board waiting to be picked up.
    ///
    /// <para>
    /// A set rather than a walk of every want, because the question is asked on the population's beat: at ten
    /// looks a second for thirty-three bots against a board of several hundred wants, <see cref="Owed"/> is
    /// a hundred thousand comparisons a second to answer "no" almost every time. Written when goods are
    /// taken in and cleared when they are handed over, so the walk only ever happens for a bot that really
    /// has something.
    /// </para>
    /// </summary>
    private static readonly HashSet<IBotWilful> _holding = [];

    /// <summary>Times goods were fetched off the board by the reflex, and how many things came.</summary>
    public static long Fetches { get; private set; }

    public static long Fetched { get; private set; }

    /// <summary>
    /// Hands a bot whatever it has already paid for, and puts on anything it can wear.
    ///
    /// <para>
    /// <b>A reflex on the beat, and moving it here is the whole of the fix.</b> This used to be a piece of
    /// work — an undertaking called <c>order</c> with nothing in it, offered by <c>BotUpkeep</c> and weighed
    /// in gold a minute like a dig or a hunt. It is not work by any reading: it costs nothing, takes no time,
    /// and sends the bot nowhere. Priced anyway, it came out at eight a minute, and eight a minute never
    /// wins: a rescue is a hundred and forty, a hunt is fifty, and the only bot on the shard that ever
    /// collected anything in twenty-six minutes was one that had nothing else on the board at all. So a
    /// population that wants armour, orders it, pays for it and has it made walks about in cloth with the
    /// armour sitting in escrow — which is exactly what Nessa did on 26.08.2026 with a cap and a pair of
    /// gloves, and what made the "wear the better thing" code look broken when it had never once been given
    /// anything to compare.
    /// </para>
    ///
    /// <para>
    /// Nothing about the world changes by moving it: the collection was already instant and already happened
    /// wherever the bot was standing. What changes is that it is no longer in a competition it cannot win.
    /// </para>
    /// </summary>
    public static int Fetch(IBotWilful buyer)
    {
        if (buyer?.Self is not { Deleted: false, Alive: true } body || !_holding.Contains(buyer))
        {
            return 0;
        }

        var took = Collect(buyer);

        // Cleared only when the board really is empty for this bot. A pack that was too full to take
        // everything must not be told the shelf is bare, or the rest sits there until something else is
        // delivered.
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

    /// <summary>Everything delivered against this bot's wants, handed over. Returns units collected.</summary>
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

    /// <summary>
    /// Drops this bot's want for a kind of thing and returns whatever escrow was left. Silent when there was
    /// none, which is the ordinary case.
    /// </summary>
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

    /// <summary>How many open wants this bot has out, and what they are worth to a supplier.</summary>
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

    /// <summary>Everything being asked for, in units and in the gold already put down for it.</summary>
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

    /// <summary>
    /// Takes one stall's goods back off the market and into the bot's own hands. Returns units recovered.
    ///
    /// <para>
    /// The stall itself is kept and left empty, which is the point: it holds the price this bot learned and
    /// the record of whether anybody ever bought one. A bot that gives up on selling ingots to the population
    /// and takes them to a shopkeeper should not have to relearn what ingots are worth when it next has some.
    /// </para>
    /// </summary>
    public static int Reclaim(IBotWilful seller, Type kind)
    {
        var pack = seller?.Self?.Backpack;
        var stall = Find(seller, kind);

        return pack == null || stall == null ? 0 : stall.Reclaim(pack);
    }

    /// <summary>Closes every stall this bot has, putting the goods in its bank box. Returns stalls closed.</summary>
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

    /// <summary>How many stalls this bot has out.</summary>
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

    /// <summary>How many units this bot has on the market, across everything it is selling.</summary>
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

    /// <summary>What this bot has on the market, at its own asking prices.</summary>
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

    /// <summary>Everything on offer, in units and in gold at the asking prices.</summary>
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

    /// <summary>
    /// The market's own turn: prices that have sat come down, stalls whose seller is gone are cleared, and
    /// empty stalls are eventually forgotten.
    ///
    /// Idleness is measured from the <em>last thing that happened</em> rather than from the last sale, which
    /// is what makes a price ratchet down once per <see cref="StaleMs"/> instead of once per beat.
    /// </summary>
    private static void Beat()
    {
        BeatStalls();
        Cross();
        BeatWants();
    }

    /// <summary>
    /// The one thing this market never did: put a stall and a want for the same thing together.
    ///
    /// <para>
    /// <b>Supply and demand could sit side by side for the life of the shard and never meet.</b> Every route
    /// into <see cref="Fill"/> came from somebody holding the goods in a pack at that moment — a smith
    /// finishing a hauberk, a hunter emptying a pack at a counter — so goods that had already been listed
    /// were invisible to every want on the board, and a want was invisible to every stall. Nothing anywhere
    /// crossed the two.
    /// </para>
    ///
    /// <para>
    /// Measured on 04.09.2026 at 10:53, on the trade it was written to fix: Calla stood with 60gp down for
    /// twenty feathers while Alden, Vesna, Wulfric, Neriah and Merrick each carried feathers to Missy the
    /// shopkeeper — the peddler's ten-minute rule doing exactly what it was built to do with goods the
    /// population was funding an order for. Two hundred and sixty-one fletchers were passed over for want of
    /// feathers in the same half hour. The same shape sits under every "raised its offer after 1 went
    /// unfilled" line in the log.
    /// </para>
    ///
    /// <para>
    /// A crossing pays the want's offer and not the stall's ask, which is the rule everywhere else in this
    /// market: a smith filling a want for a hauberk is paid what the buyer put down whatever the iron cost
    /// it. The stall is told what it fetched, so a price that clears instantly ratchets up on its own.
    /// </para>
    /// </summary>
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

            // A shopkeeper is the ceiling on what a bot may charge; a want's offer is the ceiling on what it
            // will pay. Above it there is no sale and nothing to record: the want raises its own offer every
            // beat and will reach the ask on its own if it is worth reaching.
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

            // The market's own rules about who may fill a want, asked before anything moves rather than
            // after — Fill refuses on all three, and a refusal after the goods have been lifted off the
            // stall is goods in nobody's hands.
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
                // Nothing went. The goods go back on the stall they came off, which is where their owner
                // left them: a market that could drop a stack on the floor of a refusal would be a market
                // that quietly eats its members' property.
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

    /// <summary>
    /// The demand side's turn: an offer nobody has taken up goes up, a buyer that is gone is cleared, and a
    /// want that has run out of room to rise gives up and hands the money back.
    ///
    /// <para>
    /// <b>Giving up is information, not failure.</b> An offer at four times what it opened at, still unfilled
    /// after half an hour, is the shard saying that nobody on it can make this thing — and a want that says
    /// so while holding its buyer's money for ever would be saying it at the buyer's expense. The buyer keeps
    /// its ledger entry for the attempt and will ask again later, knowing what the last one cost.
    /// </para>
    /// </summary>
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

            // Anything delivered goes over now rather than waiting to be fetched.
            //
            // <b>Holding it was a state nothing was guaranteed to leave.</b> Collecting is worth nothing per
            // minute — the goods are already bought and paid for — so a bot with a trade would always rather
            // do its trade, and a scroll somebody wrote to order would sit in the market for the life of the
            // shard. The market keeps it only while a pack will not take it, which is what the holding was
            // ever for.
            if (want.Waiting > 0)
            {
                want.Collect(buyer.Backpack);
            }

            if (!want.IsOpen)
            {
                // <b>Nothing left to ask for and nothing left waiting: off the board at once.</b> This used
                // to be kept for a full ForgetMs — an hour — on the reasoning an empty stall is kept by: the
                // price on it is what this bot learned the thing costs. That reasoning is true of a stall and
                // false here, and the difference is one line in Best(), which reads open wants only. A filled
                // want teaches nobody anything; it is a row of noughts sitting on a board of a hundred and
                // twenty-eight rows. And a full board on this shard does not push the oldest row off — it
                // refuses the new one, which is how the cave survey once filled the market and shut it.
                if (want.Waiting <= 0)
                {
                    var owed = want.Close();

                    _wants.RemoveAt(i);

                    Refund(buyer, owed);
                    Forgotten++;

                    continue;
                }

                // Goods bought and paid for that the buyer's pack would not take. The board holds those until
                // there is room for them, which is what the holding was always for.
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

            if (now - want.TouchedTick < StaleMs)
            {
                continue;
            }

            // A raise has to be funded, and the buyer's own purse is the only place the money can come from.
            // The whole order first; failing that, one unit of it — a bot that can afford to outbid nobody
            // must not be able to say it has.
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

            // Two different sentences reach this line and both are worth saying plainly: nobody here can make
            // the thing, or this bot cannot afford to ask any louder. The offer against the ceiling tells them
            // apart at a glance.
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

    /// <summary>Makes room by forgetting the emptiest, stalest pitch. Nothing with goods on it is touched.</summary>
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

    /// <summary>
    /// Every stall has goods on it and the market is still full: take down the one nobody has ever bought
    /// from, and give its owner the goods back.
    ///
    /// <para>
    /// <b>A full market is not a busy market, and on the night of 25.08.2026 it was a blockage.</b> Two
    /// hundred and fifty-six stalls, every one of them stocked, and three hundred and twenty-seven refusals
    /// to put anything else out — of which the two most refused things were <c>IronIngot</c> and
    /// <c>Leather</c>, which is to say the two materials the whole armour trade is made of. Meanwhile the
    /// pitches holding the places were raw ribs, feathers, candles and old boots: loot nobody has ever
    /// wanted, cut to its price floor months of shard-time ago and sitting there for ever, because the only
    /// thing that has ever cleared a stall was running out of stock.
    /// </para>
    ///
    /// <para>
    /// <b>Never sold anything is the test, not cheapest or oldest.</b> A stall that has traded is a stall
    /// somebody wants something from, however slowly; a stall that has never once traded is a bot's opinion
    /// that has been refuted by everybody for hours. And the goods go back into the seller's pack rather
    /// than into the bin — it is still its property, it can carry it to a shopkeeper, and a market that
    /// destroys what it cannot sell is a worse thing than a full one.
    /// </para>
    /// </summary>
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

        // Handed back rather than destroyed. If there is nowhere to hand it to, the pitch stays: a bot's
        // goods are not the market's to throw away.
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

            // <b>Since the last sale, not since the last touch.</b> A seller adding to its own pitch counts
            // as touching it — see BotListing.DealtTick — so the stall that most needs a markdown, restocked
            // every few minutes and bought from never, was the one this clock could never reach. Two and
            // three quarter hours on 03.09.2026: 1902 things listed at 13066gp, 18 prices raised, none cut.
            if (now - stall.DealtTick < StaleMs || !stall.Cut(CutStep, LeastMultiple))
            {
                continue;
            }

            Cuts++;

            logger.Information(
                "{Name} cut {Item} to {Price}gp after {Amount} sat unsold",
                seller.Name,
                stall.Label,
                stall.Price,
                stall.Amount
            );
        }
    }

    /// <summary>
    /// Takes the money, purse first and the account for the rest, all or nothing.
    ///
    /// The pack is emptied with <c>ConsumeTotal</c> — which actually destroys the coin — before the account is
    /// touched, and if the account cannot cover the rest the coin is handed straight back. Any other order,
    /// or any early return between them, is a way to make gold out of nothing.
    /// </summary>
    /// <remarks>
    /// Public because it is the shard's one correct answer to "take this bot's money", and a second copy of
    /// it somewhere else would be a second chance to get the order wrong. The drill charges its fee through
    /// this for exactly that reason.
    /// </remarks>
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

        return false;
    }

    /// <summary>
    /// Gives money back, into the account rather than the pack: a refund must not fail because somebody is
    /// carrying too much.
    /// </summary>
    private static void Refund(Mobile buyer, int amount)
    {
        if (amount > 0)
        {
            Banker.Deposit(buyer, amount);
        }
    }

    public static string Describe()
    {
        var (units, worth) = Offered();
        var (sought, escrow) = Sought();

        return $"{_listings.Count} of {MaxListings} stalls holding {units} things worth {worth}gp and {_wants.Count} of {MaxWants} wants for {sought} things with {escrow}gp down; {Sales} sales and {Fills} fills for {Turnover}gp, of which {Crossed} things went straight off a stall to a want on the board and {Dear} wants found the thing on a stall dearer than they would pay; {Raises} prices raised, {Cuts} cut, of which {BotHaggle.Describe()}, {Forgotten} forgotten, {Abandoned} given up on; {Sells} orders refused to bots already selling the thing, {Recalled} of them settled by taking it back off the stall and {Unfunded} to bots that could not put the money down; {Cheap} things of {_worthless.Count} kinds were worth less than the {Floor}gp floor and stayed in the pack; {Fetches} deliveries fetched off the board holding {Fetched} things; the levy has taken {Levied}gp over {Levies} sales";
    }

    private sealed class AuctionTimer : Timer
    {
        public AuctionTimer(TimeSpan interval) : base(interval, interval)
        {
        }

        protected override void OnTick() => Beat();
    }
}
