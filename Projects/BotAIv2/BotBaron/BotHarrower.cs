using Server.Logging;

namespace Server.BotAI.V2;

/// <summary>
/// Offers the Baron the ground that has killed the most people, and five bots to take there.
///
/// <para>
/// <b>It reads a different column of the same table the captain reads, and the difference is the whole
/// point.</b> <see cref="BotPatrol"/> asks <c>BotPeril.Worst</c> — a decaying frequency, which answers
/// "where is blood being spilt lately" and is exactly right for deciding where a company should be standing
/// before anything happens. This asks <c>BotPeril.Deadliest</c>, a count that does not fade, which answers
/// "where has it already gone wrong". A square can be top of one list and absent from the other, and both
/// lists are correct.
/// </para>
///
/// <para>
/// <b>Every refusal is named, and there is no bucket called "other".</b> A harrowing needs a Baron, a
/// Baron who is not already leading one, one who is fit to walk into it, ground with the dead on it and a
/// way through to that ground. When none is happening, which of those five was missing is the only
/// question worth being able to answer — an unnamed nought is the failure this shard has paid for more
/// than any other.
/// </para>
///
/// <para>
/// <b>It does not count volunteers, and it used to.</b> The offer was refused outright unless five free
/// bots were standing within forty tiles at the instant it was weighed, which is arithmetic about one
/// second of a working population and produced a Baron who never left town. Raising a company is the
/// errand's own business now — he goes to the square and calls for five minutes; see
/// <see cref="BotHarrow.MusterMs"/>. A muster that comes to nothing is counted there, by name, where it
/// actually happened.
/// </para>
/// </summary>
public sealed class BotHarrower : IBotProposer
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotHarrower));

    /// <summary>
    /// How far a Baron will march a company from where he is standing.
    ///
    /// <para>
    /// <b>It follows how far the population may go, and pinning it to a number of its own is what stopped
    /// this working.</b> Bots die where they roam; a rescuer that cannot reach as far as the population
    /// wanders is a rescuer with a map of somewhere else. On 27.08.2026 <c>Roam</c> was raised to five
    /// hundred to spread the hunting out, and within the hour every death on the island was at x≈1865 —
    /// four hundred and twenty tiles from home — while this said three hundred. The Baron was asked a
    /// hundred times in a row and answered "nowhere has taken anybody" a hundred times, with three bodies on
    /// the board and two of them in one square. Two thresholds on one shelf, and the second one nobody moved.
    /// </para>
    ///
    /// <para>
    /// Nought means "as far as the population goes", which is the default and the only value that cannot go
    /// stale. Configuration may still pin a real number when a shorter leash is actually wanted.
    /// </para>
    /// </summary>
    public static int Range
    {
        get => _range > 0 ? _range : BotPopulation.Roam;
        set => _range = value;
    }

    private static int _range;

    public string Name => "Baron";

    public BotStanding Rung => BotStanding.Free;

    public static long Asked { get; private set; }

    /// <summary>Asked of somebody who is not a Baron. Not a refusal — nearly every answer is this.</summary>
    public static long NotABaron { get; private set; }

    public static long Held { get; private set; }

    public static long Unfit { get; private set; }

    /// <summary>Nowhere within reach has taken <c>BotPeril.Deadly</c> people.</summary>
    public static long Quiet { get; private set; }

    /// <summary>The deadliest ground is behind something the company cannot walk through.</summary>
    public static long Sealed { get; private set; }

    /// <summary>
    /// Dire ground with nowhere in it a body could stand — open water, and very little else.
    ///
    /// A named nought of its own, because "the island is quiet" and "the worst of it is a coordinate in the
    /// sea" are different facts about an evening and <see cref="Quiet"/> would report both as the first.
    /// </summary>
    public static long Unfooted { get; private set; }

    public static long Offered { get; private set; }

    private static bool _said;

    public BotDeed Propose(IBotWilful bot)
    {
        var body = bot?.Self;
        var map = body?.Map;

        if (map == null || map == Map.Internal || !body.Alive)
        {
            return null;
        }

        // Counted before the class check, so that "nobody is a Baron" and "the Baron is never offered
        // anything" stay different numbers instead of the same silence.
        if (body is not BotMobile { Class: BotBaron })
        {
            NotABaron++;

            return null;
        }

        Asked++;

        if (!BotSquads.Running)
        {
            return null;
        }

        if (bot is not IBotSquadMember { Squad: null })
        {
            Held++;

            return null;
        }

        if (body.HitsMax <= 0 || body.Hits < body.HitsMax * BotHunter.FitAt)
        {
            Unfit++;

            return null;
        }

        // Asked with a refusal in hand, so one unreachable square is not the whole map. The reach ledger
        // answers from pockets already proved by searches that failed, so this stays a dictionary lookup: a
        // real path search per candidate per beat is the price the hunt's proposer paid once and wrote down.
        // Over the ground the company will actually walk, not over one bookkeeping cell of it. See
        // BotPeril.Deadliest: cells are twenty-four tiles across and the box is seventy-five, and counting
        // the dead per cell is what made this offer nearly unsatisfiable.
        // <b>Chosen off the quadrant map rather than off the peril map, by order, and the two rank ground
        // by genuinely different things.</b> Peril counts the dead and forgets on a clock — right for a
        // captain deciding where a company should stand this minute, and wrong for this: a square that
        // killed nobody but has ground people down for an hour never rises on it, and a square that had one
        // bad night an hour ago quietly falls off it. BotQuad is a standing reputation that does not decay,
        // so "dire" means the island itself has been found wanting, which is what a great hunt answers.
        var quad = BotQuad.Direst(map, body.Location, Range, at => Reachable(map, body.Location, at));

        if (quad == null)
        {
            Quiet++;

            return null;
        }

        var square = BotQuad.Stand(quad);

        if (square == Point3D.Zero)
        {
            Unfooted++;

            return null;
        }

        Offered++;

        Once(body, square, quad.Deaths);

        return new BotHarrow(map, square, quad.Deaths);
    }

    /// <summary>Whether the ground between the Baron and the square is not already known to be closed.</summary>
    private static bool Reachable(Map map, Point3D from, Point3D square)
    {
        if (BotReach.Ask(map, from, square, BotArrival.Within(BotHarrow.Side / 3)) != BotReachVerdict.Sealed)
        {
            return true;
        }

        Sealed++;

        return false;
    }

    /// <summary>
    /// Said the first time only. An offer is not a march — most offers are weighed and thrown away — so a
    /// line per offer would be a log full of a company that never set out. What actually marched says so
    /// itself, in <see cref="BotHarrow"/>.
    /// </summary>
    private static void Once(Mobile body, Point3D square, int dead)
    {
        if (_said)
        {
            return;
        }

        _said = true;

        logger.Information(
            "{Name} has been offered the first harrowing on this shard: ({X}, {Y}), where {Dead} have died",
            body.Name,
            square.X,
            square.Y,
            dead
        );
    }

    public static string Describe() =>
        Asked == 0
            ? $"no Baron has ever been offered a harrowing ({NotABaron} answers went to bots that are not Barons)"
            : $"{Asked} times a Baron was asked: {Offered} were offered ground, {Held} were already leading a company, {Unfit} were too hurt, {Quiet} found nowhere reading at or below {BotQuad.Dire:F2}, {Sealed} found the worst of it behind something, {Unfooted} found nowhere in it to stand; {BotHarrow.Describe()}";

    public static void Forget()
    {
        _said = false;
        Asked = 0;
        NotABaron = 0;
        Held = 0;
        Unfit = 0;
        Quiet = 0;
        Sealed = 0;
        Unfooted = 0;
        Offered = 0;

        BotHarrow.Forget();
    }
}
