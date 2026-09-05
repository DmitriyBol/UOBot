using System;
using System.Collections.Generic;
using Server.Items;
using Server.Logging;
using Server.Regions;

namespace Server.BotAI.V2;

/// <summary>
/// A walk into the woods that comes back with herbs.
///
/// <para>
/// <b>The only thing on this shard that makes a reagent.</b> In this era herbs are shop goods and no skill
/// picks them, so every reagent in the world arrived across a counter — and a shard whose shopkeepers do not
/// stock sulphurous ash is a shard where casting ends, quietly, with one line at boot to say so. That is not
/// a hypothetical: it is in the logs. A sage who can walk out and gather is the population's own answer, and
/// the rationing is the whole of what keeps it an answer rather than a tap — see
/// <see cref="BotClass.HerbIntervalMs"/>.
/// </para>
///
/// <para>
/// <b>What it brings back is not chosen.</b> A gatherer that returned exactly what was short would be a
/// vending machine with a walk attached, and the shortage would stop being a fact the market has to solve.
/// It comes back with what the woods had: a random few kinds, a random amount of each. What is surplus goes
/// on the board like anything else, and what is still missing is still missing.
/// </para>
/// </summary>
public sealed class BotHerbs : BotDeed
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotHerbs));

    /// <summary>The ledger's key.</summary>
    public const string Trade = "herbs";

    /// <summary>What a trip to the woods is reckoned at per minute before experience corrects it.</summary>
    public static double Prior { get; set; } = 40.0;

    public static double WorkMinutes { get; set; } = 3.0;

    /// <summary>How near the ground the sage has to get before it counts as being in the woods.</summary>
    public static int ArriveWithin { get; set; } = 4;

    /// <summary>Fewest and most kinds of herb one trip may turn up.</summary>
    public static int LeastKinds { get; set; } = 2;

    public static int MostKinds { get; set; } = 5;

    /// <summary>What a herb is reckoned at before the shard has ever traded one. BotForage's figure.</summary>
    public static int Guess { get; set; } = 5;

    /// <summary>Fewest and most of any one kind.</summary>
    public static int LeastEach { get; set; } = 5;

    public static int MostEach { get; set; } = 20;

    /// <summary>
    /// How many of one reagent a picker keeps for itself before the rest goes to the population.
    ///
    /// Five, which is a caster's own working handful. A picker that sold everything and then bought the same
    /// reagent back off a counter would be paying twice to carry what it was already holding — the rule the
    /// archer's arrows and the cook's meat are both kept by.
    ///
    /// <para>
    /// And kept only by a bot that has some use for them: a spellbook to cast from or a mortar to brew with.
    /// Most pickers are gatherers and are neither, and a handful held back by somebody who will never spend it
    /// is a handful the population cannot reach.
    /// </para>
    /// </summary>
    public static int Keeps { get; set; } = 5;

    /// <summary>How much this bot keeps: the handful if it can cast or brew, nothing if it can do neither.</summary>
    private static int KeptBy(Mobile bot) =>
        BotGrimoire.Book(bot) != null || BotFlask.Kit(bot) != null ? Keeps : 0;

    /// <summary>Reagents put into somebody's standing order. For the summary.</summary>
    public static long Ordered { get; private set; }

    /// <summary>The same, onto a stall. For the summary.</summary>
    public static long Listed { get; private set; }

    /// <summary>The eight, in the order the world lists them. What the woods may have is any of these.</summary>
    private static readonly Type[] Kinds =
    [
        typeof(SulfurousAsh), typeof(BlackPearl), typeof(Garlic), typeof(Ginseng),
        typeof(SpidersSilk), typeof(Nightshade), typeof(Bloodmoss), typeof(MandrakeRoot)
    ];

    private readonly Map _map;

    private readonly Point3D _where;

    private int _found;

    /// <summary>What the haul is worth at the market's own price. See <see cref="Made"/>.</summary>
    private int _worth;

    public BotHerbs(Map map, Point3D where)
    {
        _map = map;
        _where = where;
    }

    public override string Kind => Trade;

    public override Map Map => _map;

    public override Point3D Where => _where;

    public override double Expects => Prior;

    public override double Minutes => WorkMinutes;

    /// <summary>Nothing. Walking into a wood teaches nobody anything, whatever comes back in the bag.</summary>
    public override SkillName? Trains => null;

    public override int Outlay => 0;

    /// <summary>
    /// Counted as coin without producing any, for the reason <c>BotBolt</c> spells out: work that pays in
    /// anything but money is discounted by how badly a bot needs money, and a discount that reaches nought is
    /// a veto. A sage with an empty purse is exactly the sage that should be out picking herbs.
    /// </summary>
    public override double Coin => 1.0;

    /// <summary>
    /// What came back out of the woods, priced the way the shard prices it.
    ///
    /// <para>
    /// <b>This declared nought, and declaring nought taught the whole shard that gathering is worthless.</b>
    /// It is the identical defect <see cref="BotOrder.Made"/> records paying for, on the trade the shard can
    /// least afford it: over the four hours to 09:26 on 04.09.2026 the population spent 25,770gp of its
    /// 48,685gp at counters on reagents — fifty-three pence in every pound — and in the same four hours
    /// sixty-five trips into the woods were taken, each one settling in the ledger as "0 in 1.0 min (0/min)".
    /// A trade that reports nothing per minute is a trade the auction stops offering, so the only reagent
    /// route the population had was the one that takes money out of the world.
    /// </para>
    ///
    /// <para>
    /// Goods and not coin, exactly as <see cref="BotForage"/>, <c>BotDig</c> and <c>BotChop</c> reckon what
    /// they bring back — every other gathering trade on the shard already prices its haul this way and this
    /// one was simply never given the line. The <see cref="Coin"/> factor above is left as it was: what a
    /// trip is worth and whether a bot short of money should take it are two questions, and only the first
    /// was being answered wrongly.
    /// </para>
    /// </summary>
    public override int Made => _worth;

    public override string Stage =>
        _found > 0 ? $"back from the woods with {_found} herbs" : $"out to the woods near {_where}";

    public override BotDoing Advance(IBotWilful bot)
    {
        var body = bot?.Self;

        if (body is not BotMobile sage || _map == null || _map == Map.Internal)
        {
            return BotDoing.Failed("no body");
        }

        if (!body.InRange(_where, ArriveWithin))
        {
            return BotDoing.Walk(_map, _where, BotArrival.Within(ArriveWithin), "out to the woods for herbs");
        }

        var pack = body.Backpack;

        if (pack == null)
        {
            return BotDoing.Failed("nothing to carry them in");
        }

        // Stamped before anything is picked, so a trip that ends badly still costs the half hour. Otherwise
        // the cheapest way to gather would be to fail on purpose.
        sage.Herbed = true;
        sage.HerbTick = Core.TickCount;

        var klass = sage.Class;

        // <b>A class may name its own handful, and one did with nothing reading it.</b> BotGatherer sets
        // ForageIntervalMs, ForageYieldMin and ForageYieldMax, and its own documentation calls the forage
        // "the point of this class existing — this is the tap. A handful of one kind every quarter of an
        // hour, deliberately less than the fifteen a caster orders at a time, so the gatherer becomes a
        // supplier rather than a one-off answer." On 02.09.2026 all three of those numbers were assigned,
        // bound to configuration, and read by nothing anywhere on the shard: the tap had never been
        // plumbed in, which is half of why a caster out of reagents had only a counter to go to.
        //
        // One kind, in the amount the class asks for. A class that names no amount gets the Sage's trip,
        // which is what this file was written for and is left exactly as it was.
        // Once, before anything is priced. Shelf asks which counters are known and does not sweep for them.
        BotShops.Survey(body.Map, body.Location);

        var handful = klass is { ForageYieldMax: > 0 };
        var kinds = handful ? 1 : Utility.RandomMinMax(LeastKinds, MostKinds);
        var picked = 0;

        for (var i = 0; i < kinds; i++)
        {
            var kind = Kinds[Utility.Random(Kinds.Length)];

            var amount = handful
                ? Utility.RandomMinMax(Math.Max(1, klass.ForageYieldMin), klass.ForageYieldMax)
                : Utility.RandomMinMax(LeastEach, MostEach);
            var herb = kind.CreateInstance<Item>();

            if (herb == null)
            {
                continue;
            }

            herb.Amount = amount;

            if (!pack.TryDropItem(body, herb, false))
            {
                herb.Delete();

                break;
            }

            picked += amount;

            // Priced as it is picked and at the market's own price, so a reagent the population is bidding
            // hard for makes the trip that fetched it worth what it really was.
            //
            // <b>Valued at the same number it will be sold at, and it has to be the same call.</b> The
            // fallback here was Guess while Store came to open at the shopkeeper's shelf price — five against
            // three for garlic — so the trip reported takings it could not get and the ledger would have
            // learned to over-price this trade by two thirds. Both ends ask Shelf now, which reaches Guess
            // only where no shopkeeper within reach stocks the thing at all.
            _worth += amount * BotAuction.Worth(kind, Shelf(bot, kind));

            // Ground that paid while a bot stood still on it. See BotQuad.Harvested.
            BotQuad.Harvested(body.Map, body.Location);
        }

        _found = picked;

        if (picked <= 0)
        {
            return BotDoing.Failed("the woods had nothing, or the pack was full");
        }

        // <b>Picking had no ending, and that is the whole reason the alchemist had nothing to work with.</b>
        // "herbs" was the second commonest thing on this shard — 1982 rounds of it — while the brewer read
        // "470 had the glass but no herbs, 633 had neither". Neither trade was broken; there was no edge
        // between them. A funded order first and a stall second, the way the miner has always finished.
        var (ordered, listed) = Store(bot);

        logger.Information(
            "{Name} came back from the woods with {Count} herbs worth about {Worth}gp, {Ordered} of them straight into somebody's order and {Listed} onto a stall",
            body.Name,
            picked,
            _worth,
            ordered,
            listed
        );

        return BotDoing.Done(
            $"{picked} herbs out of the woods worth about {_worth}gp, {ordered} to order and {listed} put out to sell"
        );
    }

    /// <summary>
    /// Puts the picked reagents where the trades that want them can see them.
    ///
    /// Only the kinds this trade picks, so a caster's own spellbook reagents bought over a counter are not
    /// swept out with them, and only what is above <see cref="Keeps"/>.
    /// </summary>
    private static (int Ordered, int Listed) Store(IBotWilful bot)
    {
        var body = bot?.Self;
        var pack = body?.Backpack;

        if (pack == null)
        {
            return (0, 0);
        }

        BotShops.Survey(body.Map, body.Location);

        var ordered = 0;
        var listed = 0;

        // A snapshot: offering a stack moves it out of the pack.
        List<Item> carried = [.. pack.Items];

        for (var i = 0; i < carried.Count; i++)
        {
            var stack = carried[i];

            if (stack is not { Deleted: false, Movable: true } || Array.IndexOf(Kinds, stack.GetType()) < 0)
            {
                continue;
            }

            var held = Math.Max(1, stack.Amount);
            var spare = held - KeptBy(body);

            if (spare <= 0)
            {
                continue;
            }

            var goods = spare >= held ? stack : Mobile.LiftItemDupe(stack, held - spare);

            if (goods == null)
            {
                continue;
            }

            var (went, out_) = BotAuction.Offer(bot, goods, Shelf(bot, stack.GetType()));

            ordered += went;
            listed += out_;
        }

        Ordered += ordered;
        Listed += listed;

        return (ordered, listed);
    }

    /// <summary>
    /// What one of these opens at: the shopkeeper's own asking price, and only then a guess.
    ///
    /// <para>
    /// <b>Opening above the shelf is opening at a price nobody on this island can rationally pay.</b>
    /// <c>BotShopper</c> takes whichever of stall and counter is cheaper and gives a tie to one of ours, so a
    /// reagent listed at five when a herbalist sells garlic at three is a reagent that will never move: 1986
    /// of them went onto stalls in one window and every caster that wanted one walked to a shopkeeper and
    /// paid the world instead of paying a bot. The same fault the fletcher already documents about arrows,
    /// on the trade that produces the most goods per hour of anything here.
    /// </para>
    ///
    /// <para>
    /// Measured rather than declared, like the loot floor in <c>BotSlay.Rifle</c>: the engine knows what a
    /// shopkeeper charges and there is no table here to go stale. The guess is only ever reached where no
    /// shopkeeper within reach stocks the thing at all — which for the deeper reagents is most of them, and
    /// is exactly where a bot's stall is the only supply there is.
    /// </para>
    /// </summary>
    /// <param name="bot">Whose reach decides which counters count. The survey is the caller's to do once —
    /// see the two call sites, both of which sweep before their loop rather than inside it.</param>
    private static int Shelf(IBotWilful bot, Type kind) => BotShops.Shelf(bot, kind, Guess);

    /// <summary>Forgotten with the world.</summary>
    public static void ForgetTrade()
    {
        Ordered = 0;
        Listed = 0;
    }
}

/// <summary>
/// Offers the woods to whoever may walk into them, which on this shard is one bot.
///
/// <para>
/// Refuses more often than it offers and every refusal is named, for the reason the patrol's proposer states
/// at length: an unnamed nought is the failure mode this shard has paid for more than any other.
/// </para>
/// </summary>
public sealed class BotHerbalist : IBotProposer
{
    /// <summary>How far out the woods may be looked for.</summary>
    public static int Range { get; set; } = 200;

    /// <summary>How many places to try before giving up on finding a wood at all.</summary>
    public static int Samples { get; set; } = 6;

    public static long Asked { get; private set; }

    /// <summary>Asked of a bot whose class has no such trip. Not a refusal — nearly every answer is this.</summary>
    public static long NotAGatherer { get; private set; }

    public static long TooSoon { get; private set; }

    public static long NoWood { get; private set; }

    public static long Offered { get; private set; }

    public string Name => "Herbalist";

    public BotStanding Rung => BotStanding.Free;

    public BotDeed Propose(IBotWilful bot)
    {
        var body = bot?.Self;
        var map = body?.Map;

        if (map == null || map == Map.Internal || !body.Alive)
        {
            return null;
        }

        if (body is not BotMobile { Class.HerbIntervalMs: > 0 } sage)
        {
            NotAGatherer++;

            return null;
        }

        Asked++;

        if (sage.Herbed && Core.TickCount - sage.HerbTick < sage.Class.HerbIntervalMs)
        {
            TooSoon++;

            return null;
        }

        var where = Wood(body, map);

        if (where == Point3D.Zero)
        {
            NoWood++;

            return null;
        }

        Offered++;

        return new BotHerbs(map, where);
    }

    /// <summary>
    /// Somewhere out of town a body can stand.
    ///
    /// <para>
    /// Sampled rather than searched, and the test is the one this project already uses for ground: it is not
    /// a town, and feet go there. "Woods" is what the population's own range happens to be made of once the
    /// town is excluded — asking the world for tree tiles would be a spatial sweep per beat for one bot, and
    /// the proposer contract says in as many words that the question may be real but must not be expensive.
    /// </para>
    /// </summary>
    private static Point3D Wood(Mobile body, Map map)
    {
        var home = BotPopulation.Where;
        var roam = Math.Min(Range, BotPopulation.Roam);

        for (var tries = 0; tries < Samples; tries++)
        {
            var x = home.X + Utility.RandomMinMax(-roam, roam);
            var y = home.Y + Utility.RandomMinMax(-roam, roam);

            if (!BotStep.Settle(map, x, y, out var z))
            {
                continue;
            }

            var where = new Point3D(x, y, z);

            if (Region.Find(where, map)?.IsPartOf<TownRegion>() == true)
            {
                continue;
            }

            if (BotReach.Ask(map, body.Location, where, BotArrival.Within(BotHerbs.ArriveWithin))
                == BotReachVerdict.Sealed)
            {
                continue;
            }

            return where;
        }

        return Point3D.Zero;
    }

    public static string Describe() =>
        Asked == 0
            ? $"nobody on this shard may go looking for herbs ({NotAGatherer} answers went to bots that may not)"
            : $"{Asked} looks at the woods: {Offered} trips offered, {TooSoon} came round too soon, {NoWood} found nowhere out of town to go; "
              + $"{BotHerbs.Ordered} reagents went straight into somebody's order and {BotHerbs.Listed} onto a stall, above the {BotHerbs.Keeps} of each kind a picker that can cast or brew keeps back";

    public static void Forget()
    {
        Asked = 0;
        NotAGatherer = 0;
        TooSoon = 0;
        NoWood = 0;
        Offered = 0;
        BotHerbs.ForgetTrade();
    }
}
