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

    /// <summary>The ledger key.</summary>
    public const string Trade = "unload";

    /// <summary>
    /// The share of what it can carry at which a bot heads for the counter.
    ///
    /// Seven tenths, so the walk begins while the bot can still walk briskly. Waiting for the line itself
    /// means starting the journey in the state the journey exists to end.
    /// </summary>
    public static double Heavy { get; set; } = 0.7;

    /// <summary>
    /// How much coin in the pocket is by itself a reason to walk to a counter.
    ///
    /// <para>
    /// The order of 24.08.2026 was "over a hundred goes to the bank", and the pair of numbers that carries it
    /// out is two hundred and fifty here against a hundred kept back — see <c>BotPurse.Float</c>. Setting the
    /// trip at a hundred and one instead would be literal and useless: a bot would walk across the map to
    /// deposit a single coin and be entitled to do it again immediately. Going at two hundred and fifty means
    /// every trip banks at least a hundred and fifty and the bot comes away with the hundred it works on.
    /// </para>
    ///
    /// <para>
    /// The two numbers are one decision and must be read together: a threshold to go that is lower than what
    /// is kept back is a bot that walks to a counter, banks nothing, and sets out again.
    /// </para>
    /// </summary>
    public static int Purse { get; set; } = 250;

    /// <summary>
    /// How much a bot is willing to have on it that somebody could take off its corpse.
    ///
    /// <para>
    /// <b>Weight is about walking, coin is about the market, and this is about risk — the third question,
    /// and it had never been asked.</b> Patrick's order of 08.09.2026: guilds are to fall out, declare war
    /// and loot each other's dead, so "the bots must stop carrying everything they own" stops being a
    /// tidiness point and becomes the difference between a skirmish and a shard-wide redistribution of
    /// everything anybody has made all evening.
    /// </para>
    ///
    /// <para>
    /// Deliberately far below <see cref="Heavy"/>. Seven tenths of a carrying ceiling is a great deal of
    /// loot: a bot could be at a third of its weight, perfectly comfortable, and be walking about with two
    /// thousand gold of ingots that a single lost fight hands to somebody else. This is the number that says
    /// how much of an evening's work one death may cost.
    /// </para>
    ///
    /// <para>
    /// It is measured with <see cref="Sellable"/> — the same reading the counter itself makes — so what the
    /// bot is keeping for its own trade does not count against it. A smith carrying its own hammer and the
    /// ingots for the order it is working is not exposed; a smith carrying forty ingots nobody has asked for
    /// is.
    /// </para>
    /// </summary>
    public static int Risk { get; set; } = 400;

    /// <summary>
    /// What emptying the pack is reckoned at per minute. High, and it should be: a bot that cannot move earns
    /// nothing at all, so this is worth more than whatever it interrupted.
    /// </summary>
    public static double Prior { get; set; } = 120.0;

    public static double WorkMinutes { get; set; } = 2.0;

    /// <summary>
    /// How near the counter the bot has to stand. One question, asked once — see
    /// <see cref="BotDig.CounterReach"/>, which is where the reasoning and the engine fact live.
    /// </summary>
    public static int Reach => BotDig.CounterReach;

    private readonly Map _map;

    private readonly Point3D _counter;

    private int _banked;

    private int _listed;

    /// <summary>What the porter counted as worth leaving when it decided to set out. See <see cref="Sellable"/>.</summary>
    private readonly int _expected;

    public BotUnload(Map map, Point3D counter, int expected = 0)
    {
        _map = map;
        _counter = counter;
        _expected = expected;
    }

    public override string Kind => Trade;

    public override Map Map => _map;

    public override Point3D Where => _counter;

    public override double Expects => Prior;

    public override double Minutes => WorkMinutes;

    public override SkillName? Trains => null;

    public override int Outlay => 0;

    /// <summary>Nothing is earned by putting things away; what it buys is the ability to go on working.</summary>
    public override double Coin => 0.0;

    /// <summary>
    /// Nothing produced. What goes on the market was counted as produced when it was picked up, and counting
    /// it again here would pay the bot twice for one rusty sword.
    /// </summary>
    public override int Made => 0;

    public override string Stage =>
        _banked > 0 || _listed > 0
            ? $"put {_banked}gp away and {_listed} things out"
            : "taking a full pack to the counter";

    /// <summary>
    /// The way to the counter turned out not to exist.
    ///
    /// <para>
    /// <b>Written under the counter's name, which is the word <c>BotGround.Counter</c> asks in.</b>
    /// <c>BotWill.Settle</c> files every failure under the undertaking's name — "unload" — and the counter
    /// lookup has never asked that question, so the nearest counter was chosen on distance alone however many
    /// times it had just been missed. One counter in Britain collected 56 of those in an hour.
    /// </para>
    /// </summary>
    public override bool Bend(IBotWilful bot)
    {
        if (_counter == Point3D.Zero)
        {
            return false;
        }

        bot?.Resolve?.Ledger?.Beware(BotGround.CounterKind, _map, _counter);

        return false;
    }

    /// <summary>
    /// The one errand a bot too heavy to move can still finish: a stall holds its goods wherever the seller
    /// is standing, so nothing here needs a step. See BotDeed.Standing.
    /// </summary>
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
            // Offered without a counter only to a bot too heavy to walk (see the proposal), so the only
            // thing left to do is the thing that needs no counter.
            if (BotLadder.Load(body) > BotLadder.Ceiling(body))
            {
                var freed = Sell(bot, body, out _, out _, out _);

                Shed += freed;
                Stranded++;

                if (freed > 0)
                {
                    return BotDoing.Done(
                        $"{freed} things put on the market from where it stood, too heavy to walk and no counter known"
                    );
                }

                // <b>And the dumping belongs here too, which it did not.</b> The branch below reaches it and
                // this one did not, so the same bot in the same state was cured or abandoned according to
                // whether the shard happened to have surveyed a counter - which is a fact about how far
                // other bots have walked, not about this one. Both branches now end the same way.
                return Ditch(bot, body);
            }

            return BotDoing.Failed("nowhere known to put it");
        }

        // <b>Past the line the walk cannot happen, so the goods go on the market from where the bot is
        // standing.</b> This file's own opening says it: "the cure for a full pack is a walk to the bank, and
        // a full pack is exactly what stops the walk". Heavy starts the journey at seven tenths so it should
        // never come to this — and it does. Calla the Crafter sat at 293 of 222 stones from 00:36 to 00:42 on
        // 05.09.2026, taking this errand and failing it over and over: "no way through", "it got no nearer
        // than 212 tiles", "it had stopped getting anywhere", then the same errand again a quarter of a
        // minute later. An errand that is offered because the bot cannot move and then requires the bot to
        // move is a treadmill, and this one had no way off it.
        //
        // Listing needs no counter. A stall holds its goods out of the world, which is why BotFletch buys
        // from one wherever it stands — so the market is the one place an immobilised bot can still reach.
        // Only the coin needs the counter, and coin is not what is holding it down.
        var stuck = BotLadder.Load(body) > BotLadder.Ceiling(body);

        if (!stuck && !body.InRange(_counter, Reach))
        {
            // The distance the work itself asks for, on the line above. See BotArrival.Beside.
            return BotDoing.Walk(_map, _counter, BotArrival.Within(Reach), "to the counter with a full pack");
        }

        if (stuck && !body.InRange(_counter, Reach))
        {
            var shed = Sell(bot, body, out _, out _, out _);

            Shed += shed;

            if (shed > 0)
            {
                return BotDoing.Done($"{shed} things put on the market from where it stood, too heavy to walk");
            }

            // <b>Nothing sellable, and it cannot move: the one state on this shard with no way out of
            // itself.</b> Failing here puts the errand straight back on offer — the porter's own test still
            // says the pack holds something worth leaving — and the bot takes it, fails in a fifth of a
            // minute, and takes it again. Quill did that 143 times in five minutes on 09.09.2026 and dragged
            // the whole shard's completion band from 75% to 47% on its own, which is how the alarm found it.
            //
            // So it puts something down. A bot that cannot walk is worth more than the ore nobody will buy,
            // and everything the sale would have taken is exactly what may be dropped: never the kit, never
            // what its trade is bound to, never coin.
            return Ditch(bot, body);
        }

        var carried = body.Backpack?.Items.Count ?? 0;

        _banked = Bank(body);
        _listed = Sell(bot, body, out var kept, out var refused, out var skipped);

        return _banked > 0 || _listed > 0
            ? BotDoing.Done($"{_banked}gp banked, {_listed} things put on the market")
            : BotDoing.Done(
                $"nothing to leave here: {carried} things in the pack — {kept} its own kit or supplies, "
                + $"{skipped} gold or fixed in place, {refused} the market would not take; "
                + $"the porter counted {_expected} worth leaving when it set out"
            );
    }

    /// <summary>
    /// The surplus over the working float, and <b>not</b> every coin.
    ///
    /// <para>
    /// <b>"Coin is the one thing exactly as useful in an account as in a pocket" is false, and it cost the
    /// shard its whole economy.</b> <c>BaseVendor.OnBuyItems</c> pays for a purchase out of the backpack and
    /// only reaches for the bank when the bill comes to two thousand gold or more — and a bot's purchases are
    /// bandages at five, garlic at three, cloth at a few coins the yard. So a bot that banked every coin on
    /// its first trip to a counter could never buy anything again, however rich it was.
    /// </para>
    ///
    /// <para>
    /// Nothing said so. <see cref="BotYield.Wealth"/> counts the account and the pocket together, so the bot
    /// believed it could afford what it was refused, and the shop's answer was logged as the shop's business.
    /// On the night of 25.08.2026 that read as 1929 failed restocking trips in half an hour, not one bandage
    /// bought in a whole session, and a population that fought, got hurt, could not heal and died — with four
    /// hundred gold apiece in the bank. Three reasonable decisions in three files: bank it all, count both,
    /// spend only the pocket.
    /// </para>
    ///
    /// <para>
    /// The float left behind is <see cref="BotPurse.Float"/> — the same hundred that <see cref="BotPurse"/>
    /// already keeps back on the other path to a counter, asked for here rather than written again, so the
    /// two cannot drift apart.
    /// </para>
    ///
    /// <para>
    /// The coin leaves the pack before the account is credited and comes straight back if the deposit is
    /// refused. The engine's deposit adds to an account without touching what the depositor carries, so any
    /// other order makes gold out of nothing.
    /// </para>
    /// </summary>
    private static int Bank(Mobile body)
    {
        var pack = body.Backpack;
        var carried = pack?.GetAmount(typeof(Gold)) ?? 0;

        // Asked of BotPurse rather than of its float, because "what a bot keeps on it" is a question with
        // more than one answer now and this is the second place that asks it. See BotPurse.Keeps.
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

    /// <summary>
    /// Everything that is not the bot's own kit, supplies or trade, put out for sale.
    ///
    /// <para>
    /// The market rather than the floor, and rather than a shopkeeper: a stall holds goods out of the world at
    /// a price the seller sets, and whatever nobody buys in half an hour is carried to a counter by the peddler
    /// anyway. Dropping it would be throwing away somebody else's materials.
    /// </para>
    /// </summary>
    /// <summary>Things handed straight to a standing order rather than listed. For the summary.</summary>
    public static long Filled { get; private set; }

    /// <summary>Trips to a counter offered because the board wanted something in the pack. For the summary.</summary>
    public static long Bespoken { get; private set; }

    /// <summary>
    /// Times the errand was finished with no counter known at all, by a bot too heavy to walk.
    ///
    /// Its own number rather than being folded into <c>Shed</c>: those are bots that could not reach a
    /// counter, these are bots for whom the shard could not name one, and a shard that starts naming none
    /// is a different fault from bots that keep getting stuck out of reach.
    /// </summary>
    public static long Stranded { get; private set; }

    /// <summary>Things listed on the spot by a bot too heavy to walk to a counter. For the summary.</summary>
    public static long Shed { get; private set; }

    /// <summary>Things dropped on the ground by a bot that could neither walk nor sell. See <see cref="Dump"/>.</summary>
    public static long Dumped { get; private set; }

    /// <summary>Trips begun because the pack was worth more than the bot should be carrying. See <see cref="Risk"/>.</summary>
    public static long Exposed { get; private set; }

    /// <summary>Counts one such trip.</summary>
    public static void Expose() => Exposed++;

    /// <summary>
    /// Trips offered to a bot with nothing the market wants, purely so that the dumping could be reached.
    ///
    /// <para>
    /// <b>The cure was locked behind a gate meant for a different bot.</b> <see cref="Sellable"/> skips
    /// anything <c>BotAuction.Worthless</c> refuses, and <see cref="Dump"/> is written to drop exactly that
    /// - so a pack full of heavy junk answered "nothing worth a trip" at the proposal and never reached the
    /// one method on this shard able to do something about it. Garrow the Mage sat at 173 stones of 131 with
    /// no stamina on 09.09.2026, having sold six and eight things at a time from where it stood, and spent
    /// the gaps taking peddling errands it could not walk. The gate is right for a bot that can walk, since
    /// an empty trip to a counter is the thing this file spent an hour being cured of, and wrong for one
    /// that cannot, since for that bot the errand is not a trip at all.
    /// </para>
    /// </summary>
    public static long Cornered { get; private set; }

    /// <summary>
    /// Of those, the ones where nothing could be dropped either. The genuinely wedged.
    ///
    /// Reading above a handful means bots are reaching a state neither selling nor dropping can undo -
    /// worn kit alone over the ceiling, most likely - and it is the stall watch that has to carry them out,
    /// not this file.
    /// </summary>
    public static long Immovable { get; private set; }

    /// <summary>
    /// How long a bot that could neither sell nor drop is left alone by the porter.
    ///
    /// <b>Not decoration: without it this errand is a treadmill with the brake taken off.</b> The load now
    /// outranks price rather than discounting it (see <c>BotWill.Auction</c>), so an errand that fails
    /// instantly is an errand that wins again instantly. Quill took one 143 times in five minutes on
    /// 09.09.2026 and pulled the whole shard's completion band from 75% to 47%, and that was while the load
    /// was still only a discount. Two minutes is long enough that the loop cannot form and short enough that
    /// a bot which picks up something saleable is served on its next review but one.
    /// </summary>
    public static int WedgedMs { get; set; } = 120000;

    /// <summary>When each bot last proved it could neither sell nor drop. Absence means never.</summary>
    private static readonly Dictionary<Serial, long> _wedged = [];

    /// <summary>Whether this bot lately proved there is nothing this errand can do for it.</summary>
    public static bool Wedged(Mobile body) =>
        body != null && _wedged.TryGetValue(body.Serial, out var when) && Core.TickCount - when < WedgedMs;

    /// <summary>Counts one trip offered only because the bot cannot walk.</summary>
    public static void Corner() => Cornered++;

    /// <summary>
    /// How many of a tool a bot keeps. One to work with and one to take over when it wears through.
    ///
    /// <para>
    /// <b>This was <c>int.MaxValue</c>, and that is not a cap, it is a promise never to let go.</b> A tool is
    /// bought whenever the pack holds none of a kind and is never sold at any count, so every kit a bot ever
    /// picked up off a corpse, was handed at birth, or bought because the one it held was momentarily
    /// elsewhere stayed with it for the life of the shard. Patrick found a bot on 09.09.2026 carrying ten
    /// sewing kits. Ten kits is five hundred acts of tailoring in one pack, twenty stones of it, and nine of
    /// them were invisible to the market because this list said they were not merchandise.
    /// </para>
    ///
    /// <para>
    /// Two rather than one, because a tool that wears through mid-errand with no spare is a trade that stops
    /// until somebody walks to town, and that is the failure this list was written against. The surplus is
    /// not destroyed - it becomes sellable, which puts it on a stall where the next bot short of one buys it
    /// instead of walking to Britain.
    /// </para>
    /// </summary>
    public static int SpareTools { get; set; } = 2;

    /// <summary>
    /// How many stones of things a bot is not allowed to keep is by itself a reason to walk to a counter.
    ///
    /// <para>
    /// <b>The fifth reason to set out, and its absence was worth seven hundred stones of ore.</b> The other
    /// four ask whether the bot is uncomfortable (<see cref="Heavy"/>), rich (<see cref="Purse"/>), wanted
    /// (<c>Wanted</c>) or exposed (<see cref="Risk"/>). None of them asks the plainest question there is:
    /// how much of what it is carrying is not its own. Ore is heavy and cheap, so it trips none of the
    /// value thresholds, and a miner holds it until seven tenths of its ceiling - which on 10.09.2026 was
    /// 652 stones of iron and 122 of copper spread through the population, every ounce of it surplus by the
    /// shard's own rules, sitting between "enough to be worth selling" and "heavy enough to have to".
    /// </para>
    ///
    /// <para>
    /// Forty, which is a load nobody carries by accident: the whole population averaged nineteen stones of
    /// surplus apiece when this was written, and the miners carrying five times that are exactly who it is
    /// for. Measured in stones and not in items on purpose - three hundred reagents and eight ingots are the
    /// same sentence and not the same fault.
    /// </para>
    /// </summary>
    public static int Hoard { get; set; } = 40;

    /// <summary>Trips begun because the pack held more than <see cref="Hoard"/> stones of somebody else's goods.</summary>
    public static long Hoarding { get; private set; }

    /// <summary>Counts one such trip.</summary>
    public static void Hoarded() => Hoarding++;

    /// <summary>How long the list of what the board is asking for is held before it is read again.</summary>
    public static int BoardMs { get; set; } = 2000;

    /// <summary>The kinds anybody has money down for, read at most once every <see cref="BoardMs"/>.</summary>
    private static readonly HashSet<Type> _bespoke = [];

    private static long _read;

    private static bool _everRead;

    /// <summary>
    /// Whether the pack holds a surplus of something somebody has money down for on the board.
    ///
    /// <para>
    /// <b>The third reason to walk to a counter, and its absence was a whole trade standing still.</b> Weight
    /// and coin were the only two, and both are facts about the bot rather than about the shard. So a
    /// woodsman cut wood to <c>BotTimber.Worthwhile</c> — twenty logs, forty stones, nowhere near the weight
    /// gate — stopped, and held them. Twenty logs is also the number at which the woodsman refuses to cut any
    /// more, so it parks exactly between "enough to stop" and "heavy enough to sell" and stays there. At
    /// 20:09 on 04.09.2026 the woodsman's own line read "476 were carrying enough already" while the
    /// fletcher's read "243 could not find wood", with open orders for logs on the board the entire time.
    /// Two numbers on one shelf again, and nothing in the world crossing the gap between them.
    /// </para>
    ///
    /// <para>
    /// The rule is the same one <see cref="Purse"/> already argues for coin — "money in a pocket is money the
    /// market cannot see" — said about goods, which was always the half that mattered more. Nothing is given
    /// away: the trip lists the surplus on a stall or hands it to the standing order at the order's own
    /// price, exactly as a heavy pack's trip does.
    /// </para>
    /// </summary>
    public static bool Wanted(IBotWilful bot, Mobile body)
    {
        var pack = body?.Backpack;

        if (pack == null || bot == null)
        {
            return false;
        }

        // Built at most every couple of seconds and shared by the whole population, because this is asked of
        // every bot on every beat and the board is one list for all of them.
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

        // Built only once something in the pack has actually been asked for, so the ordinary answer — a bot
        // carrying nothing anybody wants — costs a hash lookup per item and no allocation at all.
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

            // Only the part above what the trade keeps for itself is merchandise, which is the same reading
            // Sell and Sellable make. A fletcher's own twenty logs are stock and stay where they are.
            // Counted across the pack rather than stack by stack: see Over.
            if (Over(keep, seen, item) <= 0)
            {
                continue;
            }

            return true;
        }

        return false;
    }

    /// <summary>Notes one such trip. The porter is the only thing that may begin one.</summary>
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

        // A snapshot: listing takes things out of the pack, which mutates the list being read.
        List<Item> carried = [.. pack.Items];

        for (var i = 0; i < carried.Count; i++)
        {
            var item = carried[i];

            if (item == null || item.Deleted || !item.Movable || item is Gold)
            {
                // Counted, so that the sentence below adds up. A pack holds its gold as one of these, and a
                // bound-in-place oddity now and then; a total that does not reconcile sends the next reader
                // hunting for a fault that is not there.
                skipped++;

                continue;
            }

            // The kit is never merchandise, and the engine agrees: bound things are marked so that a
            // shopkeeper refuses them outright.
            if (BotBinding.IsBound(item, bot.Bond))
            {
                kept++;

                continue;
            }

            // <b>Supplies are kept by the armful the class asks for, and the surplus is merchandise.</b> The
            // list this reads used to hold types and not numbers, so a bot kept every reagent it ever touched
            // for ever, without limit and whatever its trade. A warrior with no spellbook hoarded sulphurous
            // ash off a corpse until it died; a sage came back from the woods with sixty and kept all sixty.
            // Reagents were 25,770gp of the population's 48,685gp of spending in the four hours to 09:26 on
            // 04.09.2026 — fifty-three pence in every pound, every coin of it across a counter and out of the
            // world — while the same reagents sat in packs that had no use for them. Bot-to-bot trade over
            // those four hours came to 2,890gp against 85,482gp earned from shopkeepers: three per cent.
            //
            // The kit already says how many: Kit.Reagents is thirty for a mage, sixty for a sage and nought
            // for everybody else, and Kit.Bandages the same. Keeping to that number and listing the rest is
            // the whole of the difference between a population of hoarders and a market.
            // How many of this one are merchandise, counted across the whole pack rather than within the
            // stack. See Over for the ten sewing kits that were the cost of asking it the other way.
            var over = Over(keep, seen, item);

            if (over <= 0)
            {
                kept++;

                continue;
            }

            if (over < Math.Max(1, item.Amount))
            {
                // The engine's own way of taking part of a stack: this one becomes the surplus and the
                // remainder is put back in the pack beside it. Written the other way round it would sell
                // the bot's own supplies and keep the spare.
                //
                // <b>The null is not a formality.</b> LiftItemDupe needs a parameterless constructor and
                // hands back nothing when the type has none — leaving the stack whole and untouched. A
                // caller that ignored that would carry on and list the lot, which is a caster's entire
                // supply of reagents sold out from under it by a failed split.
                if (Mobile.LiftItemDupe(item, over) == null)
                {
                    kept++;

                    continue;
                }

                kept++;
            }

            // Priced at what a shopkeeper would pay rather than at a flat coin — the same correction the
            // corpse-rifling side got, and the same reason. Worth answers with the market's own price the
            // moment anybody has traded the kind; until then it hands back the caller's guess, and a guess of
            // one gold for everything is what made leather worthless and would do the same to glass.
            var floor = BotShops.Buyer(body, item, out var offered) != null ? offered : 1;

            // <b>Somebody's standing order before the open market, and its absence was the hole in every
            // chain that starts with a corpse.</b> A want on the board has money already down against it —
            // <c>BotAuction.Ask</c> takes the payment when it is raised — so filling one is a sale that has
            // already happened, at a price the buyer chose, with no waiting and no stall fee. Until now only
            // a crafter's own finished work ever looked: the tailor and the smith both fill a want before
            // listing, and the bot walking in from a field with a pack full of what somebody asked for did
            // not. So an archer could put "arrows" on the board, a fletcher could stand ready, and the
            // feathers to make them sat in a hunter's pack going to a shopkeeper for a copper.
            //
            // This is the line that makes a Need reach the whole population rather than the crafters: what
            // anybody is carrying, anybody may be asked for.
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

            if (BotAuction.List(bot, item, BotAuction.Worth(item.GetType(), Math.Max(1, floor))) != null)
            {
                listed++;
            }
            else
            {
                refused++;
            }
        }

        Filled += filled;

        // Counted with what was listed, because to the porter they are the same act — the pack is lighter
        // either way — and counted apart in the summary, because to the shard they are not: one is a sale
        // that somebody was waiting for and the other is goods put on a shelf to see if anybody wants them.
        return listed + filled;
    }

    /// <summary>
    /// Whether there is anything in this pack the counter could actually take.
    ///
    /// <para>
    /// <b>The porter weighed the whole pack and the counter can only take part of it, and those are two
    /// different numbers on one shelf.</b> A warrior in plate with a spellbook, bandages and reagents sits at
    /// seventy per cent of what it can carry all day with nothing whatever to sell — so it was told its pack
    /// was full, walked across Britain, put nothing down and came back: 23 of 45 trips to a counter on
    /// 26.08.2026 ended "0gp banked, 0 things put on the market". Not a loop that runs away, because the
    /// ledger notices a trade that pays nothing — but half of every porter's afternoon, spent on nothing.
    /// </para>
    ///
    /// <para>
    /// Asked only after the cheap weight test has already passed, which is this project's usual order: the
    /// cheap necessary condition first, the pack walk only for the bots it lets through.
    /// </para>
    /// </summary>
    public static int Sellable(IBotWilful bot, Mobile body) => Sellable(bot, body, out _);

    /// <summary>
    /// The same count, and what it weighs.
    ///
    /// The weight comes out of the same walk of the pack rather than a second one: the porter asks both
    /// questions on every proposal, and the pack is the expensive part of asking either.
    /// </summary>
    public static int Sellable(IBotWilful bot, Mobile body, out double stones)
    {
        stones = 0.0;

        var pack = body?.Backpack;

        if (pack == null)
        {
            return 0;
        }

        // The same question again, and the third place to ask it. A bot saving for a horse carries seven
        // hundred that it is not going to bank, and read against the plain float that is a porter setting out
        // for a counter to put down nothing — the empty walk this file spent an hour being cured of.
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

            // The same reading the sale itself makes, and it has to be the same reading or the porter sets
            // out for a counter with nothing to put down: a stack over the kit's number is merchandise here
            // too, and only the part above the number counts.
            if (BotBinding.IsBound(item, bot.Bond))
            {
                continue;
            }

            var over = Over(keep, seen, item);

            if (over <= 0)
            {
                continue;
            }

            // <b>The same question the counter will ask on arrival, asked before setting out.</b> This used
            // to count anything the bot was free to sell, and the market decides by price — two different
            // questions, and a rusty dagger answers yes to the first and no to the second every time. The log
            // printed both numbers side by side in every line ("22 the market would not take; the porter
            // counted 22 worth leaving when it set out") and they disagreed 279,067 times in eight hours on
            // 27.08.2026, which is roughly ten walks to a bank counter every second, for ever.
            if (BotAuction.Worthless(item.GetType()))
            {
                continue;
            }

            worth++;

            // Only the part above the allowance weighs against the bot, exactly as only that part is for
            // sale. PileWeight rather than TotalWeight - see BotUnload.Weighed for the night that cost.
            var many = Math.Max(1, item.Amount);

            stones += over * (item.PileWeight + item.TotalWeight) / (double)many;
        }

        return worth;
    }

    /// <summary>
    /// What a bot must not sell out from under itself: the tools of its trade, what it shoots, and the
    /// supplies every bot lives on.
    ///
    /// Taken from the same lists birth issued from and shopping restocks from, so a class that changes is
    /// right here without anybody remembering to come back.
    /// </summary>
    /// <summary>
    /// What the last <see cref="Sell"/> turned down, and why.
    ///
    /// <para>
    /// <b>"0gp banked, 0 things put on the market" is three different sentences and it said none of them.</b>
    /// A porter that arrives and puts nothing down has either brought nothing but its own kit, or brought
    /// things the market would not take, or brought a pack that emptied itself on the road — and 20 of 59
    /// trips on 26.08.2026 ended that way with no means of telling which. Weighing only what a counter can
    /// take (see <see cref="Sellable"/>) cut it from half to a third and stopped there, which is exactly
    /// the point at which guessing has to stop and the nought has to be named.
    /// </para>
    /// </summary>
    /// <summary>
    /// Puts things on the ground until the bot can walk again, heaviest first.
    ///
    /// <para>
    /// <b>The last resort, and it only ever runs for a bot that is over its carrying ceiling with nothing the
    /// market will take.</b> Everything it may drop is what the sale would have sold: the kit stays, whatever
    /// the trade is bound to stays, coin stays — coin weighs almost nothing and is not what is holding it
    /// down. What goes is merchandise nobody wants, and it goes because a frozen bot earns nothing at all
    /// while a dropped ingot is worth what somebody would have paid for it, which by construction is nothing.
    /// </para>
    ///
    /// <para>
    /// Stops the moment the bot is under the ceiling rather than emptying the pack: this is a cure for being
    /// stuck, not a policy about carrying things.
    /// </para>
    /// </summary>
    /// <summary>
    /// The last thing an immobilised bot can try, and the note it leaves if even that fails.
    ///
    /// <para>
    /// One method rather than two copies because there are two ways into this state - a counter is known and
    /// out of reach, or none is known at all - and they are the same state. What differed between them was
    /// only which of the two had been written most recently.
    /// </para>
    /// </summary>
    private static BotDoing Ditch(IBotWilful bot, Mobile body)
    {
        var dumped = Dump(bot, body);

        if (dumped > 0)
        {
            Dumped += dumped;

            return BotDoing.Done($"{dumped} things nobody would buy left on the ground, to be able to walk again");
        }

        // Neither sold nor dropped: there is nothing this errand can do for this bot, and saying so once is
        // worth more than saying it four times a minute for the rest of the night. See WedgedMs.
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

        // Heaviest first, so the fewest things go down for the most relief.
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

    /// <summary>
    /// How many kinds the pack census names before it stops. The heaviest, so the tail is the cheap tail.
    /// </summary>
    public static int Census { get; set; } = 6;

    /// <summary>
    /// What the whole population is carrying, in stones, and how much of it is surplus.
    ///
    /// <para>
    /// <b>Nothing on this shard has ever printed what is in the packs, and "the bots are carrying a ton of
    /// rubbish" was therefore an impression rather than a number.</b> Every other question about weight has
    /// an instrument - the carrying ceiling has one, the unloading has five - and all of them are about the
    /// bots that are already in trouble. This one is about the ones that are not: weight below the ceiling
    /// costs nothing and is invisible, and it is exactly where a pack quietly fills with things nobody will
    /// ever want.
    /// </para>
    ///
    /// <para>
    /// Reported in stones rather than in items, because that is the unit the problem is in: three hundred
    /// reagents and eight ingots are the same sentence and not the same fault. Surplus is measured against
    /// the same allowance the sale uses, so a line reading "40 of 54 stones surplus" is a claim that can be
    /// checked by selling and watching the number fall.
    /// </para>
    ///
    /// <para>
    /// Walks every pack, so it is on the five-minute summary and nowhere else. Coin and bound kit are
    /// skipped for the same reason <c>BotHand</c>'s reader skips them: they are not merchandise at any
    /// count, and counting them as surplus is an instrument raising an alarm about nothing.
    /// </para>
    /// </summary>
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
                // <b>PileWeight and not TotalWeight, and getting that wrong made this print noughts.</b>
                // Item.TotalWeight is what an item *contains* - it is nought for everything that is not a
                // container - while PileWeight is ceil(Weight x Amount), the stack's own weight. The first
                // census reported "49 packs hold 0 stones" beside bots visibly carrying tools and reagents,
                // which is the instrument lying before the shard does, twice in one night. Both are added,
                // so a bag of something inside a pack is weighed with what is in it.
                var weight = (double)(item.PileWeight + item.TotalWeight);

                carried += weight;
                stones[kind] = (stones.TryGetValue(kind, out var had) ? had : 0.0) + weight;

                // Counted against a running total per kind, because an allowance is about the pack and not
                // about the stack: five bolts of ten under a cap of twenty is thirty over, and asking the
                // question stack by stack would answer nought every time.
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

    /// <summary>
    /// What this bot is allowed to keep, by kind, for whoever wants to look rather than to sell.
    ///
    /// <b>The list that decides what a pack fills up with had no reader outside this file.</b> "Why is that
    /// bot carrying ten sewing kits" was a question about this table, and answering it meant reading the
    /// source rather than the shard - so a number that was wrong for a month looked exactly like a number
    /// that was right. See <c>BotHand</c>'s "pack" verb, which is the whole reason this is public.
    /// </summary>
    public static Dictionary<Type, int> Keeps(IBotWilful bot) => bot == null ? [] : Needed(bot);

    /// <summary>
    /// How many of this stack are surplus, counting what has already been passed over in the same walk.
    ///
    /// <para>
    /// <b>The allowance is about the pack and every place that read it asked about the stack.</b> The check
    /// was <c>item.Amount &lt;= allowed</c>, item by item — so a bot with ten sewing kits kept all ten,
    /// because a sewing kit does not stack and each one is an amount of one against an allowance of two;
    /// and a tailor with five bundles of ten leather kept all fifty against an allowance of twenty, because
    /// no single bundle was over it. The intent is written three lines above the sale in this same file —
    /// "keeping to that number and listing the rest is the whole of the difference between a population of
    /// hoarders and a market" — and the code had never done it.
    /// </para>
    ///
    /// <para>
    /// Found on 10.09.2026 by the pack census disagreeing with the seller: <c>Weighed</c> counts a running
    /// total per kind and printed "Pickaxe 253 (121 surplus)" while <c>Sell</c> passed over every one of
    /// them. Two readings of one allowance, and the instrument happened to be the one that was right.
    /// </para>
    ///
    /// <para>
    /// <c>seen</c> is the running total and this method advances it, so it must be called exactly once per
    /// item per walk of a pack. Returns nought for anything kept whole, and never more than the stack holds.
    /// </para>
    /// </summary>
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

        // How many of each, and not merely which. A cap of nought means the bot has no use for the thing at
        // all and every one of them is merchandise; anything left uncapped below is kept whole as before.
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

        // Paper is left uncapped on purpose: a scribe's stock is the one supply here whose right quantity is
        // "as much as it can write", the kit names no number for it, and a guess would be a threshold nobody
        // could defend. It is cheap and it is on a shelf.
        keep[typeof(BlankScroll)] = int.MaxValue;

        // <b>Glass is stock to an alchemist and rubbish to everybody else, and it was kept by everybody.</b>
        // A potion leaves its bottle behind, so a bot that drinks all day accumulates empties it will never
        // have a use for — protected from sale by a list that was written for the one class that fills them.
        // Fifteen bots carrying somebody else's raw material is weight nobody is paid for, and a brewer short
        // of glass buying more while fifteen packs hold it is the market failing at the one thing it is for.
        if (BotOutfit.Brews(bot.Class))
        {
            keep[typeof(Bottle)] = int.MaxValue;
        }

        // <b>A crafter's raw material is stock, not merchandise, and it was being sold out from under every
        // one of them.</b> This list held supplies and tools and knew nothing about what a trade eats, so a
        // smith walked to a counter and put its own iron on a stall, a fletcher its own wood and feathers, a
        // tailor its own hide. The market's own counters named it and nobody was reading them: at 13:31 on
        // 04.09.2026 the smith's ordering line read "157 have their own out on a stall" — a hundred and
        // fifty-seven refusals to order iron because the bot was already selling iron it needed — and the
        // materials board read "153 have their own out on a stall" beside it.
        //
        // Kept to a working quantity and no further, exactly as the supplies above are: what is over a
        // batch is genuinely surplus and belongs on the market, which is where another crafter will find it.
        // Asked of the tool in the pack, so a bot that takes up a trade tomorrow keeps its stock tomorrow.
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
            // Every metal the smith could work, not iron alone — see BotAnvil.Keep for why the two lists had
            // to become one before BotSmith was allowed to fetch its own stock back.
            BotAnvil.Keep(body, keep, BotBullion.Enough);
        }

        if (BotFlask.Kit(body) != null)
        {
            keep[typeof(Bottle)] = int.MaxValue;
        }

        // A tool is not a supply, and a crafter with none has no trade at all — see BotShopper.Wanting, which
        // calls that the whole of what stands between "tools wear out" and "trades quietly end". So it is
        // kept, and it is kept to a number: see SpareTools for the ten sewing kits that were the cost of not
        // having one.
        var tools = BotOutfit.ToolsFor(bot.Class);

        for (var i = 0; i < tools.Count; i++)
        {
            keep[tools[i]] = SpareTools;
        }

        // <b>The class already says how many of each draught it wants and this threw the number away.</b>
        // BotOutfit.PotionsFor returns the kind and the count together, and the count is klass.PotionLimit -
        // the same number a bot is born with and the same one BotArsenal tops it up to. Writing MaxValue over
        // it meant every potion a bot ever bought, brewed or looted was kept for ever: eight heals in one
        // pack, against a class limit that had said what it wanted all along.
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

        // <b>Two reasons to walk to a counter, and only the first one existed.</b> A heavy pack is the
        // obvious one and it is what this measured — but a coin weighs next to nothing, so a bot could carry
        // eight hundred gold across the map for hours and never once qualify as loaded. The population's
        // whole takings sat in fifteen backpacks, went into the ground with whoever died, and the bank line
        // in every summary read nought banked while gold was plainly arriving. Money in a pocket is money one
        // bad fight from being gone, and money in a pocket is also money the market cannot see.
        var laden = BotLadder.Load(body) >= BotLadder.Ceiling(body) * BotUnload.Heavy;
        var flush = (body.Backpack?.TotalGold ?? 0) >= BotUnload.Purse;

        // <b>And a third, which is a fact about the shard rather than about the bot.</b> Both of the reasons
        // above ask whether the bot is uncomfortable; neither asks whether anybody wants what it is carrying.
        // A woodsman that stops cutting at twenty logs is neither heavy nor rich and never comes, while a
        // fletcher two fields away has money on the board for exactly those logs. See BotUnload.Wanted.
        var bespoken = !laden && !flush && BotUnload.Wanted(bot, body);

        // <b>Whether the bot can take a step at all, and it is read here rather than further down now.</b>
        // Every gate below this line was written to spare a walking bot a pointless trip; not one of them
        // makes sense for a bot that is not going to walk anywhere, because for that bot this errand is not
        // a trip. See BotUnload.Cornered.
        var immobile = BotLadder.Load(body) > BotLadder.Ceiling(body);

        // Weight is a necessary condition and not a sufficient one. See BotUnload.Sellable - and it is asked
        // before the gate now rather than after it, because the fourth reason to set out is a fact about what
        // the pack is worth. Costs a walk of the backpack per proposal; the alternative is a number that
        // cannot be read until one of the other three has already fired, which is the same as not having it.
        var worth = BotUnload.Sellable(bot, body, out var spare);

        // A bot that cannot walk is offered this with an empty answer here, because the thing it needs is
        // not the sale. Sellable counts what the market would take and Dump exists to put down exactly what
        // the market would not, so for this one bot the gate excluded precisely the case the errand was
        // last extended to cover.
        if (worth <= 0 && !immobile)
        {
            return null;
        }

        // ...unless it has just proved there is nothing to put down either. Without this the errand fails in
        // a fifth of a minute and wins the next review outright, for ever. See BotUnload.WedgedMs.
        if (worth <= 0 && BotUnload.Wedged(body))
        {
            return null;
        }

        // <b>The fourth reason, and the only one that is about what a death would cost.</b> See BotUnload.Risk.
        var exposed = worth >= BotUnload.Risk;

        // <b>The fifth, and the only one that asks how much of the load is not the bot's own.</b> See
        // BotUnload.Hoard: ore is heavy and cheap, so it slips past every threshold above and rides around
        // in a miner's pack until sheer weight forces the trip.
        var hoarding = spare >= BotUnload.Hoard;

        if (!laden && !flush && !bespoken && !exposed && !hoarding && !immobile)
        {
            return null;
        }

        var counter = BotGround.Counter(bot, body.Location);

        // <b>A bot that cannot walk is offered this even when no counter is known, and the alternative was a
        // monument.</b> Joss the Gatherer stood on one tile for thirteen minutes on 07.09.2026 at 247 of 236
        // stones with no stamina, outside any named region, taking and dropping thirty-two errands - every
        // one of which needed a step it could not take. The counter list is built by bots walking past
        // counters and is not saved, so a shard fresh from a restart can easily know none; and the errand
        // that cures an unwalkable pack was refused for want of somewhere to walk to. Selling on the spot
        // needs no counter at all - a stall holds goods wherever it stands - so the one bot that most needs
        // this errand was the only one that could not have it.
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

        return new BotUnload(map, counter, worth);
    }
}
