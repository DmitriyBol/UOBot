using System;
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

    /// <summary>Fewest and most of any one kind.</summary>
    public static int LeastEach { get; set; } = 5;

    public static int MostEach { get; set; } = 20;

    /// <summary>The eight, in the order the world lists them. What the woods may have is any of these.</summary>
    private static readonly Type[] Kinds =
    [
        typeof(SulfurousAsh), typeof(BlackPearl), typeof(Garlic), typeof(Ginseng),
        typeof(SpidersSilk), typeof(Nightshade), typeof(Bloodmoss), typeof(MandrakeRoot)
    ];

    private readonly Map _map;

    private readonly Point3D _where;

    private int _found;

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

    public override int Made => 0;

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

            // Ground that paid while a bot stood still on it. See BotQuad.Harvested.
            BotQuad.Harvested(body.Map, body.Location);
        }

        _found = picked;

        if (picked <= 0)
        {
            return BotDoing.Failed("the woods had nothing, or the pack was full");
        }

        logger.Information("{Name} came back from the woods with {Count} herbs", body.Name, picked);

        return BotDoing.Done($"{picked} herbs out of the woods");
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
            : $"{Asked} looks at the woods: {Offered} trips offered, {TooSoon} came round too soon, {NoWood} found nowhere out of town to go";

    public static void Forget()
    {
        Asked = 0;
        NotAGatherer = 0;
        TooSoon = 0;
        NoWood = 0;
        Offered = 0;
    }
}
