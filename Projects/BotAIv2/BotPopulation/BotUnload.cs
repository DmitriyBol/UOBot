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

        if (!body.InRange(_counter, Reach))
        {
            // The distance the work itself asks for, on the line above. See BotArrival.Beside.
            return BotDoing.Walk(_map, _counter, BotArrival.Within(Reach), "to the counter with a full pack");
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
            if (BotBinding.IsBound(item, bot.Bond) || keep.Contains(item.GetType()))
            {
                kept++;

                continue;
            }

            // Priced at what a shopkeeper would pay rather than at a flat coin — the same correction the
            // corpse-rifling side got, and the same reason. Worth answers with the market's own price the
            // moment anybody has traded the kind; until then it hands back the caller's guess, and a guess of
            // one gold for everything is what made leather worthless and would do the same to glass.
            var floor = BotShops.Buyer(body, item, out var offered) != null ? offered : 1;

            if (BotAuction.List(bot, item, BotAuction.Worth(item.GetType(), Math.Max(1, floor))) != null)
            {
                listed++;
            }
            else
            {
                refused++;
            }
        }

        return listed;
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

            if (BotBinding.IsBound(item, bot.Bond) || keep.Contains(item.GetType()))
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
    private static HashSet<Type> Needed(IBotWilful bot)
    {
        HashSet<Type> keep =
        [
            typeof(Bandage), typeof(BlankScroll),
            typeof(SulfurousAsh), typeof(BlackPearl), typeof(Garlic), typeof(Ginseng),
            typeof(SpidersSilk), typeof(Nightshade), typeof(Bloodmoss), typeof(MandrakeRoot)
        ];

        // <b>Glass is stock to an alchemist and rubbish to everybody else, and it was kept by everybody.</b>
        // A potion leaves its bottle behind, so a bot that drinks all day accumulates empties it will never
        // have a use for — protected from sale by a list that was written for the one class that fills them.
        // Fifteen bots carrying somebody else's raw material is weight nobody is paid for, and a brewer short
        // of glass buying more while fifteen packs hold it is the market failing at the one thing it is for.
        if (BotOutfit.Brews(bot.Class))
        {
            keep.Add(typeof(Bottle));
        }

        var tools = BotOutfit.ToolsFor(bot.Class);

        for (var i = 0; i < tools.Count; i++)
        {
            keep.Add(tools[i]);
        }

        var bottles = BotOutfit.PotionsFor(bot.Class);

        for (var i = 0; i < bottles.Count; i++)
        {
            keep.Add(bottles[i].Kind);
        }

        var ammunition = bot.Bond?.Weapon?.Ammunition;

        if (ammunition != null)
        {
            keep.Add(ammunition);
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

        if (!laden && !flush)
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

        return counter == Point3D.Zero ? null : new BotUnload(map, counter, worth);
    }
}
