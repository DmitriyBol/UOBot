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

    /// <summary>How many times the engine has quietly stopped serving a bot's needle. See StallMs.</summary>
    public static long Jammed { get; private set; }

    /// <summary>The ledger's key.</summary>
    public const string Trade = "sew";

    /// <summary>
    /// What an afternoon at the needle is reckoned at per minute before experience corrects it.
    ///
    /// Higher than a mining trip, and the reason is the whole point of the trade: tailoring is <em>on the
    /// crafter's own vector</em>, so every tenth of a point it gains counts at full rate, while the same
    /// tenth of Mining would count at a third. A crafter that sews is becoming what it is for.
    /// </summary>
    public static double Prior { get; set; } = 55.0;

    /// <summary>How long the sewing itself is expected to take.</summary>
    public static double WorkMinutes { get; set; } = 6.0;

    /// <summary>How much cloth to buy in one go. About a pack's worth of work.</summary>
    public static int Bolt { get; set; } = 20;

    /// <summary>
    /// How often an attempt is made. The engine's craft has its own timer and animation; swinging faster than
    /// this only stacks attempts on top of each other.
    /// </summary>
    public static int SwingMs { get; set; } = 3000;

    /// <summary>
    /// How long the work may go with neither a piece made nor a scrap of material spent before it is given
    /// up as jammed.
    ///
    /// <para>
    /// Eight attempts' worth, derived from <see cref="SwingMs"/> so the two cannot drift: long enough that a
    /// run of ordinary failures never trips it — those still consume material — and short enough that a bot
    /// which the engine has quietly stopped serving is back at work within half a minute instead of standing
    /// at its bench until somebody reads the log.
    /// </para>
    /// </summary>
    public static int StallMs { get; set; } = SwingMs * 8;

    /// <summary>
    /// What a finished piece is taken to be worth, and the price the stall opens at.
    ///
    /// A stand-in like the ingot's, and the same rule applies: it opens a stall and the market moves it from
    /// there. Twelve is a little above what the cloth cost, which is what a trade is.
    /// </summary>
    public static int GoldPerPiece { get; set; } = 12;

    private enum Leg
    {
        Shop,
        Work,
        Counter
    }

    private BaseVendor _shop;

    /// <summary>
    /// The stall this chain is buying its material from, or null when the material comes off a shelf in town
    /// or is already in the pack.
    ///
    /// <para>
    /// A stall is not a place. It holds its goods out of the world and hands them over on payment, so the
    /// market route has no walk in it at all — which is exactly why leather can be a trade here despite there
    /// being no shopkeeper anywhere on the shard who sells it.
    /// </para>
    /// </summary>
    private BotListing _stall;

    /// <summary>
    /// What this chain is sewing with.
    ///
    /// <para>
    /// <b>The trade was written as cloth and had to become a material.</b> Cloth is bought from a shopkeeper
    /// and depends on nobody, which is what made it the right first chain; leather is produced by the
    /// population and by nothing else, which is what makes it the first chain that closes a loop. One bot
    /// skins what it kills, the market carries the hide across, and a tailor turns it into armour somebody
    /// else buys. Same needle, same kit, same skill — see BotCrafter, which noticed years ago that the sewing
    /// kit makes leather armour on this era and then had nothing to say about it.
    /// </para>
    /// </summary>
    private readonly Type _stuff;

    private readonly int _price;

    /// <summary>How many units this chain set out to acquire. Nought when the material is already in hand.</summary>
    private readonly int _take;

    /// <summary>
    /// How much material one piece takes, so the chain can tell "carrying some" from "carrying enough".
    ///
    /// <para>
    /// <b>Some is not enough, and the shortcut into the work could not tell the difference.</b> Choosing a
    /// recipe weighs skill and says nothing whatever about quantity, so a bot holding a single hide would
    /// skip the buying leg — it has leather, after all — pick the hardest thing its skill allows, and swing
    /// at a recipe the engine refuses for want of material. Nothing is spent, so nothing runs out, so the leg
    /// never ends: half a minute of a crafter standing still while every summary on the shard says it is
    /// sewing, until the decision layer swaps the work out for unrelated reasons.
    /// </para>
    /// </summary>
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

    /// <summary>How much material was in the pack when anything last actually happened, and when that was.</summary>
    private int _lastLeft = -1;

    private long _stirTick;

    /// <summary>
    /// What the chosen recipe actually eats per piece — the exit's number, as against <see cref="_need"/>,
    /// which is the proposer's estimate before any recipe exists.
    ///
    /// <para>
    /// <b>The cloth route was given a hard one and it was wrong the whole time.</b> "A bolt is bought
    /// wholesale and covers any recipe on the list" sounded safe and is arithmetic nobody did: a shirt eats
    /// eight, twenty makes two, and four are left over that no recipe can use. The engine then refuses every
    /// swing, refusing costs nothing, the count never falls below one, and the leg cannot end — the very
    /// defect <see cref="_need"/> was added to cure, still standing on the other route because the entry
    /// number was guessed instead of asked.
    /// </para>
    ///
    /// <para>
    /// Taken from the recipe the moment one is chosen, so neither route can be wrong about it again.
    /// </para>
    /// </summary>
    private int _want;

    /// <summary>Cloth, off a shelf in town. The chain that waits on nobody.</summary>
    public BotSew(BaseVendor shop, int price)
    {
        _stuff = typeof(Cloth);
        _shop = shop;
        _map = shop?.Map;
        _where = shop?.Location ?? Point3D.Zero;
        _price = Math.Max(1, price);
        _take = Bolt;

        // One, which is what this leg has always meant by "has cloth": a bolt is bought wholesale and covers
        // any recipe on the list, so the cloth route never had this question to answer.
        _need = 1;
    }

    /// <summary>
    /// Leather, out of the population's own market or out of the pack of whoever skinned it.
    ///
    /// <para>
    /// Where it stands rather than where a shop is, because there is no errand: the material is either
    /// already carried or a stall hands it over on payment. That also keeps the decision layer honest — this
    /// work is as near as work gets, and pretending it is at a counter across town would price it as though
    /// it had a walk in it.
    /// </para>
    /// </summary>
    public BotSew(Map map, Point3D where, BotListing stall, int price, int take, int need)
        : this(map, where, stall, price, take, need, null)
    {
    }

    /// <summary>
    /// The same, made against a standing order off the needs board.
    ///
    /// <para>
    /// <b>The tailor could not read the board at all, and that is where the armour trade stopped.</b> The
    /// smith has taken orders since the day the board existed; this side of the trade only ever sewed
    /// whatever it judged best and happened to fill an order if the thing it chose was one somebody wanted.
    /// The moment armour began to be ordered in quantity, almost every want on the board was leather — 0
    /// orders taken by smiths in one window, because a smith cannot sew — and nobody was reading them.
    /// </para>
    /// </summary>
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

    /// <summary>The order this was taken for, or null when the bot is sewing on its own judgement.</summary>
    private readonly BotWant _order;

    public override string Kind => Trade;

    public override Map Map => _map;

    /// <summary>Where the material is: a counter on the cloth route, and where the bot stands on the other.</summary>
    public override Point3D Where => _where;

    /// <summary>
    /// What this is reckoned at. Half again for work somebody has already put money down for.
    ///
    /// The same multiple the forge uses, and for the same reason: an order is money already in escrow, so it
    /// is worth more than a guess about what might sell, and the crafter should reach for it first. The
    /// ledger corrects both numbers to the truth within a few pieces either way.
    /// </summary>
    public override double Expects => _order == null ? Prior : Prior * 1.6;

    public override double Minutes => WorkMinutes;

    /// <summary>Tailoring, and it is on the crafter's own vector — which is what makes this worth its time.</summary>
    public override SkillName? Trains => SkillName.Tailoring;

    /// <summary>
    /// What the material costs. Nought when it is already in the pack, which is the whole advantage of having
    /// skinned the thing yourself.
    /// </summary>
    public override int Outlay => _take * _price;

    /// <summary>Nothing comes back as coin here either: what is made goes to the market, and the sale is later.</summary>
    public override double Coin => 0.0;

    public override int Made => _made;

    public override string Stage => _leg switch
    {
        Leg.Shop => $"after {_stuff?.Name ?? "material"}",
        Leg.Work => $"sewing {_kind?.Name ?? "something"} ({_swings} attempts, {_pieces} made)",
        _ => $"putting {_pieces} away"
    };

    /// <summary>
    /// The way to the cloth turned out not to exist. Try a different counter before giving the trade up.
    ///
    /// <para>
    /// Written down in this bot's own ledger first, so the shop it could not reach stops being offered to it
    /// for a while — without that, the proposer hands back the same nearest shop on the very next beat and the
    /// tailor walks at it again. Fourteen of sixteen sewing failures in one ten-minute stretch were that one
    /// loop. Another bot standing somewhere else may reach the same counter perfectly well, which is why the
    /// note is the bot's and not the shard's.
    /// </para>
    /// </summary>
    public override bool Bend(IBotWilful bot)
    {
        // Nothing to bend to on the market route: a stall is not somewhere a bot failed to walk to, so a
        // journey cannot have gone wrong on the way to one.
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
        // Enough material already in hand — from a previous chain, from a shop visit that half worked, or,
        // for leather, off the back of something this bot killed itself. Enough and not merely some: see
        // _need for what a single hide used to do to this leg.
        if (BotThread.Amount(body, _stuff) >= _need)
        {
            _leg = Leg.Work;

            return default;
        }

        // <b>Its own goods back off the market before paying anybody for the same thing.</b> Going through a
        // corpse lists what it lifts the moment it lifts it, so a tailor that skinned a bear has already put
        // that leather out for sale — and a seller cannot buy from its own stall, so without this the bot
        // stands next to its own material bidding for somebody else's. Reclaiming leaves the stall standing
        // and empty, which keeps the price it learned.
        //
        // Counted again afterwards rather than trusted: taking a stall back says something came home, not
        // that enough did, and jumping into the work on one reclaimed hide is the same standing-still bug
        // _need exists to prevent. Short of a piece, this falls through and buys the rest.
        if (BotAuction.Reclaim(bot, _stuff) > 0 && BotThread.Amount(body, _stuff) >= _need)
        {
            _leg = Leg.Work;

            return default;
        }

        // The market route. A stall holds its goods out of the world and delivers on payment, so this is the
        // whole errand: no counter, no walk, no shopkeeper who has to exist.
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

        // Neither a stall nor a counter, which on the leather route means the chain was taken on material the
        // bot was already carrying and that material is no longer there. Said as what it is rather than as a
        // missing merchant: nobody sells leather, so a leather chain complaining about a shopkeeper would
        // send the next reader looking for one.
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
            // The distance the work itself asks for, on the line above. See BotArrival.Beside.
            return BotDoing.Walk(_shop.Map, _shop.Location, BotArrival.Within(BotShops.CounterReach), $"to {_shop.Name} for {_stuff.Name}");
        }

        if (BotShops.Buy(bot, _shop, _stuff, _take, out var refused) <= 0)
        {
            // The third counter, named like the other two. "No cloth to be had" was the sentence a tailor
            // printed whether the shelf was bare, the purse was short or the shopkeeper simply said no, and
            // sixteen of them in a morning said nothing anybody could act on.
            return BotDoing.Failed(refused ?? $"no {_stuff.Name} to be had");
        }

        _leg = Leg.Work;

        return default;
    }

    private BotDoing Sewing(Mobile body)
    {
        var kit = BotThread.Kit(body);

        if (kit == null)
        {
            return BotDoing.Failed("nothing to sew with");
        }

        if (_recipe == null)
        {
            // The order's own recipe when there is one, so the piece that gets made is the piece somebody
            // asked for rather than whatever this bot rates highest.
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

            // <b>The opening ask follows what went into the piece.</b> Twelve was a little over what a bolt
            // of cloth cost, which made it the right constant for exactly one material — and leather is
            // dearer, comes off a creature somebody had to kill, and goes into armour that eats several
            // units a piece. Opened at the cloth number, a leather tunic sells below its own materials, and
            // what the ledger learns from that is not "leather is mispriced" but "sewing is worthless".
            //
            // Asked once per chain rather than once per beat: what a piece fetches does not change while a
            // bot is sewing, and walking the market on every decision would be a cost model wearing a hat.
            var floor = BotThread.Units(_recipe) * BotAuction.Worth(_stuff, _price) * 2;

            _worth = BotAuction.Worth(_kind, Math.Max(GoldPerPiece, floor));
        }

        // What the last attempt produced, counted before the next one is made. Failures produce nothing and
        // are supposed to: that is what the cloth is paying for.
        var have = BotThread.Made(body, _kind);

        if (have > _had)
        {
            // The swing worked, which is the one moment the free craft may be asked for. Counted into the
            // same tally, so a second piece is a second piece whichever way it arrived.
            have += BotCraftwork.Bonus(body, _kind);

            _pieces += have - _had;
            _had = have;
            _made = _pieces * _worth;
        }

        // <b>Enough left for another piece, not merely something left.</b> Entering the work asks whether the
        // bot has enough to make one — see _need — and leaving it asked whether the material had run out
        // altogether, which are different questions whenever a piece costs more than one unit. Twenty leather
        // makes three pairs of shoes at six apiece and leaves two over: the engine then refuses every swing,
        // refusing costs nothing, nothing is ever consumed, and the count never reaches nought. Faron swung a
        // hundred and twenty-two times for three pairs and only stopped because the decision layer wanted it
        // digging instead. The same pair of numbers on the same shelf as _need, and the second one was left
        // behind when the first was put in.
        var left = BotThread.Amount(body, _stuff);

        // <b>The needle had no clock at all, and three bots stood at it for two hours.</b> Crafting reports
        // nothing through the decision layer — every beat of it answers Work, and Work is the one answer
        // BotWill deliberately never judges, because a bot at a forge or a vein is standing still on purpose.
        // That is right while something is happening. Nothing here asked whether anything was.
        //
        // The engine can leave a bot unable to craft at all: CraftItem.Craft opens by taking the
        // CraftSystem action lock and returns silently when it is already held, so a lock that never came
        // back makes every later attempt a no-op — no piece, no material spent, no message. On 25.08.2026
        // Edda, Faron and Doran each bought twenty cloth at two minutes to noon and were never heard from
        // again; the summary read "sew 3" for two hours and looked like three bots working.
        //
        // So the leg is given the same kind of exit a fight has: not a time limit on the work, but a limit on
        // the work doing nothing. What is watched is the material, and that one number is enough for both
        // halves — a piece cannot be made without spending some, and tailoring consumes on failure too, so
        // an honest run of bad luck moves this while a jammed lock leaves it exactly where it was.
        //
        // Seeded on the first pass through, never from a zero: _lastLeft starts at minus one, which no
        // amount can equal, so the first beat sets both and there is no sentinel to misread later.
        if (_lastLeft != left)
        {
            _lastLeft = left;
            _stirTick = Core.TickCount;
        }

        if (Core.TickCount - _stirTick >= StallMs)
        {
            Jammed++;

            // Said in its own right, because the way out of a jam is about to hide it. A jam that struck
            // after two shirts were finished is not a failed afternoon — the goods are real and somebody
            // should be sold them — so the work goes to the counter rather than being thrown away, and
            // "finished sew: 2 Shirt" is what the ledger will read. True, and silent about the jam. This
            // line and the counter above it are the only places the jam is countable at all.
            logger.Information(
                "{Name}'s needle stopped: {Swings} attempts, {Pieces} made, {Left} {Stuff} left untouched for {Stall}s",
                body.Name,
                _swings,
                _pieces,
                left,
                _stuff.Name,
                StallMs / 1000
            );

            // Nothing made means nothing to carry, and the failure is the honest answer — it also keeps the
            // reason in the ledger where a reader will meet it.
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
            // The cloth is gone and nothing came of it. Still finished rather than failed: the attempts were
            // real, the skill checks happened, and what it cost is what learning a trade costs.
            return BotDoing.Done($"{_swings} attempts, nothing came of it");
        }

        if (_counter == Point3D.Zero)
        {
            _counter = BotGround.Counter(Map, body.Location);
        }

        // Nowhere to put things away is not a reason to throw the work out — the goods are in the pack and
        // the bot is better at its trade than it was.
        if (_counter != Point3D.Zero && !body.InRange(_counter, BotDig.CounterReach))
        {
            // The distance the work itself asks for, on the line above. See BotArrival.Beside.
            return BotDoing.Walk(Map, _counter, BotArrival.Within(BotDig.CounterReach), "to a counter");
        }

        var listed = 0;
        var ordered = 0;
        var made = BotThread.Gather(body, _kind);

        // A funded want comes first, because it is a buyer that has already put the money down — and what
        // it pays for comes back off Made, since a piece cannot be both goods produced and the coin they
        // fetched.
        for (var i = 0; i < made.Count; i++)
        {
            var piece = made[i];
            var held = Math.Max(1, piece.Amount);
            var want = _order ?? BotAuction.Demand(bot, _kind);
            var sold = want == null ? 0 : BotAuction.Fill(bot, want, piece);

            // Counted in units rather than in objects. Nothing the needle makes stacks today, so the two are
            // the same number — but a want may take a slice of a stack, and a rule that only holds while
            // nothing stacks is a rule waiting for the first thing that does.
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
