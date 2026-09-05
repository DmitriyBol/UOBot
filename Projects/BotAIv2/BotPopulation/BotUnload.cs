using System;
using System.Collections.Generic;
using Server.Items;
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

    public override BotDoing Advance(IBotWilful bot)
    {
        var body = bot?.Self;

        if (body == null || _map == null || _map == Map.Internal)
        {
            return BotDoing.Failed("no body");
        }

        if (_counter == Point3D.Zero)
        {
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

            return shed > 0
                ? BotDoing.Done($"{shed} things put on the market from where it stood, too heavy to walk")
                : BotDoing.Failed("too heavy to walk and nothing on it the market would take");
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

    /// <summary>Things listed on the spot by a bot too heavy to walk to a counter. For the summary.</summary>
    public static long Shed { get; private set; }

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

            // Only the part above what the trade keeps for itself is merchandise, which is the same reading
            // Sell and Sellable make. A fletcher's own twenty logs are stock and stay where they are.
            if (keep.TryGetValue(kind, out var allowed) && item.Amount <= allowed)
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
            if (keep.TryGetValue(item.GetType(), out var allowed))
            {
                if (item.Amount <= allowed)
                {
                    kept++;

                    continue;
                }

                if (allowed > 0)
                {
                    // The engine's own way of taking part of a stack: this one becomes the surplus and the
                    // remainder is put back in the pack beside it. Written the other way round it would sell
                    // the bot's own supplies and keep the spare.
                    //
                    // <b>The null is not a formality.</b> LiftItemDupe needs a parameterless constructor and
                    // hands back nothing when the type has none — leaving the stack whole and untouched. A
                    // caller that ignored that would carry on and list the lot, which is a caster's entire
                    // supply of reagents sold out from under it by a failed split.
                    if (Mobile.LiftItemDupe(item, item.Amount - allowed) == null)
                    {
                        kept++;

                        continue;
                    }

                    kept++;
                }
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
    public static int Sellable(IBotWilful bot, Mobile body)
    {
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

            if (keep.TryGetValue(item.GetType(), out var allowed) && item.Amount <= allowed)
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

        // A tool is not a supply and is not sold at any count. One wears through in twenty-five to seventy-five
        // uses and a crafter with none has no trade at all — see BotShopper.Wanting, which calls that the whole
        // of what stands between "tools wear out" and "trades quietly end".
        var tools = BotOutfit.ToolsFor(bot.Class);

        for (var i = 0; i < tools.Count; i++)
        {
            keep[tools[i]] = int.MaxValue;
        }

        var bottles = BotOutfit.PotionsFor(bot.Class);

        for (var i = 0; i < bottles.Count; i++)
        {
            keep[bottles[i].Kind] = int.MaxValue;
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

        if (!laden && !flush && !bespoken)
        {
            return null;
        }

        // Weight is a necessary condition and not a sufficient one. See BotUnload.Sellable.
        var worth = BotUnload.Sellable(bot, body);

        if (worth <= 0)
        {
            return null;
        }

        var counter = BotGround.Counter(bot, body.Location);

        if (counter == Point3D.Zero)
        {
            return null;
        }

        if (bespoken)
        {
            BotUnload.Bespeak();
        }

        return new BotUnload(map, counter, worth);
    }
}
