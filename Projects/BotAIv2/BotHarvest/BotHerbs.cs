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

    public const string Trade = "herbs";

    public static double Prior { get; set; } = 40.0;

    public static double WorkMinutes { get; set; } = 3.0;

    public static int ArriveWithin { get; set; } = 4;

    public static int LeastKinds { get; set; } = 2;

    public static int MostKinds { get; set; } = 5;

    public static int Guess { get; set; } = 5;

    public static int LeastEach { get; set; } = 5;

    public static int MostEach { get; set; } = 20;

    public static int Keeps { get; set; } = 5;

    public static readonly Type[] Healing = [typeof(Garlic), typeof(Ginseng), typeof(SpidersSilk), typeof(MandrakeRoot)];

    public static int HealerKeeps { get; set; } = 25;

    public static long ForHealing { get; private set; }

    private static bool Medic(Mobile bot) => bot is BotMobile { Class.Role: BotRole.Medic };

    private static int KeptBy(Mobile bot, Type kind)
    {
        if (Medic(bot) && Array.IndexOf(Healing, kind) >= 0)
        {
            return HealerKeeps;
        }

        return BotGrimoire.Book(bot) != null || BotFlask.Kit(bot) != null ? Keeps : 0;
    }

    private static Type Fewest(Container pack, Type[] kinds)
    {
        Type worst = null;
        var least = int.MaxValue;
        var seen = 0;

        for (var i = 0; i < kinds.Length; i++)
        {
            var held = pack?.GetAmount(kinds[i]) ?? 0;

            if (held > least)
            {
                continue;
            }

            if (held < least)
            {
                least = held;
                seen = 1;
                worst = kinds[i];

                continue;
            }

            seen++;

            if (Utility.Random(seen) == 0)
            {
                worst = kinds[i];
            }
        }

        return worst ?? kinds[Utility.Random(kinds.Length)];
    }

    public static long Ordered { get; private set; }

    public static long Listed { get; private set; }

    private static readonly Type[] Kinds =
    [
        typeof(SulfurousAsh), typeof(BlackPearl), typeof(Garlic), typeof(Ginseng),
        typeof(SpidersSilk), typeof(Nightshade), typeof(Bloodmoss), typeof(MandrakeRoot)
    ];

    private readonly Map _map;

    private readonly Point3D _where;

    private int _found;

    private int _worth;

    public BotHerbs(Map map, Point3D where)
    {
        _map = map;
        _where = where;
    }

    public override string Kind => Trade;

    public override bool Steadfast => true;

    public override Map Map => _map;

    public override Point3D Where => _where;

    public override double Expects => Prior;

    public override double Minutes => WorkMinutes;

    public override SkillName? Trains => null;

    public override int Outlay => 0;

    public override double Coin => 1.0;

    public override int Made => _worth;

    public override string Stage =>
        _found > 0 ? $"back from the woods with {_found} herbs" : $"out to the woods near {_where}";

    public override bool Bend(IBotWilful bot)
    {
        bot?.Resolve?.Ledger?.Beware(Trade, _map, _where);

        return false;
    }

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

        sage.Herbed = true;
        sage.HerbTick = Core.TickCount;

        var klass = sage.Class;

        BotShops.Survey(body.Map, body.Location);

        var medic = Medic(body);
        var handful = klass is { ForageYieldMax: > 0 };
        var kinds = handful ? 1 : medic ? 2 : Utility.RandomMinMax(LeastKinds, MostKinds);
        var picked = 0;

        if (medic)
        {
            ForHealing++;
        }

        for (var i = 0; i < kinds; i++)
        {
            var kind = medic ? Fewest(pack, Healing) : Scarcest();

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

            _worth += amount * BotAuction.Worth(kind, Shelf(bot, kind));

            BotQuad.Harvested(body.Map, body.Location);
        }

        _found = picked;

        if (picked <= 0)
        {
            return BotDoing.Failed("the woods had nothing, or the pack was full");
        }

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

        List<Item> carried = [.. pack.Items];

        for (var i = 0; i < carried.Count; i++)
        {
            var stack = carried[i];

            if (stack is not { Deleted: false, Movable: true } || Array.IndexOf(Kinds, stack.GetType()) < 0)
            {
                continue;
            }

            var held = Math.Max(1, stack.Amount);
            var spare = held - KeptBy(body, stack.GetType());

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

    private static int Shelf(IBotWilful bot, Type kind) => BotShops.Shelf(bot, kind, Guess);

    private static Type Scarcest()
    {
        Type worst = null;
        var least = int.MaxValue;
        var seen = 0;

        for (var i = 0; i < Kinds.Length; i++)
        {
            var held = BotAuction.Stocked(Kinds[i]);

            if (held > least)
            {
                continue;
            }

            if (held < least)
            {
                least = held;
                seen = 1;
                worst = Kinds[i];

                continue;
            }

            seen++;

            if (Utility.Random(seen) == 0)
            {
                worst = Kinds[i];
            }
        }

        return worst ?? Kinds[Utility.Random(Kinds.Length)];
    }

    public static void ForgetTrade()
    {
        Ordered = 0;
        Listed = 0;
        ForHealing = 0;
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
    public static int Range { get; set; } = 200;

    public static int Samples { get; set; } = 6;

    public static long Asked { get; private set; }

    public static long Refused { get; private set; }

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

        var where = Wood(body, map, bot?.Resolve?.Ledger);

        if (where == Point3D.Zero)
        {
            NoWood++;

            return null;
        }

        Offered++;

        return new BotHerbs(map, where);
    }

    private static Point3D Wood(Mobile body, Map map, BotLedger ledger)
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

            if (BotRefused.Refusing(map, where))
            {
                Refused++;

                continue;
            }

            if (ledger != null && ledger.Cautious(BotHerbs.Trade, map, where))
            {
                Refused++;

                continue;
            }

            return where;
        }

        return Point3D.Zero;
    }

    public static string Describe() =>
        Asked == 0
            ? $"nobody on this shard may go looking for herbs ({NotAGatherer} answers went to bots that may not)"
            : $"{Asked} looks at the woods: {Offered} trips offered, {TooSoon} came round too soon, {NoWood} found nowhere out of town to go, {Refused} patches passed over as already refused; "
              + $"{BotHerbs.Ordered} reagents went straight into somebody's order and {BotHerbs.Listed} onto a stall, above the {BotHerbs.Keeps} of each kind a picker that can cast or brew keeps back; "
              + $"{BotHerbs.ForHealing} of the trips were healers' own, for the four herbs a heal spends, of which each keeps {BotHerbs.HealerKeeps}";

    public static void Forget()
    {
        Asked = 0;
        NotAGatherer = 0;
        TooSoon = 0;
        NoWood = 0;
        Refused = 0;
        Offered = 0;
        BotHerbs.ForgetTrade();
    }
}
