using Server.Logging;

namespace Server.BotAI.V2;

/// <summary>
/// Offers a captain the worst square on the island and a company to take there.
///
/// <para>
/// <b>It answers one bot, and that is the whole of what "authority" means on this shard.</b> There is no
/// rank, no chain of command and nothing anybody is obliged to do. A captain gets an offer nobody else gets;
/// the bots it calls on are free, come because they were free, and go back to their own business the moment
/// the company ends. This project has refused to model obedience since the first week — a bot that does what
/// it is told is a bot whose motivation has been replaced by somebody else's — and a proposer that only ever
/// answers one class is the entire mechanism by which one bot can nevertheless lead.
/// </para>
///
/// <para>
/// <b>It refuses far more often than it offers, and every refusal is named.</b> A patrol needs a captain, a
/// quiet captain, volunteers standing near it and somewhere genuinely dangerous to go — and when there is no
/// patrol happening, "which of those four was missing" is the only question worth being able to answer. An
/// unnamed nought is the failure mode this shard has paid for more than any other.
/// </para>
/// </summary>
public sealed class BotPatrol : IBotProposer
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotPatrol));

    /// <summary>
    /// How far a captain will march a company from where it is standing.
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

    public string Name => "Patrol";

    public BotStanding Rung => BotStanding.Free;

    public static long Asked { get; private set; }

    /// <summary>Asked of a bot that is not a captain. Not a refusal — fifteen of sixteen answers are this.</summary>
    public static long NotACaptain { get; private set; }

    public static long Held { get; private set; }

    public static long Unfit { get; private set; }

    public static long Peaceful { get; private set; }

    public static long TooFewNear { get; private set; }

    /// <summary>
    /// Dangerous squares turned down because the ground between here and there is known to be closed.
    ///
    /// A named nought of its own, because "the island is quiet" and "the worst of it is across water" are
    /// different facts about an evening and <see cref="Peaceful"/> would have reported both as the first.
    /// </summary>
    public static long Sealed { get; private set; }

    /// <summary>
    /// Offers made. <b>Not marches</b>, and it used to be printed as though it were: most offers lose the
    /// auction and are thrown away. What actually set out is <see cref="BotSweep.Marches"/>.
    /// </summary>
    public static long Offered { get; private set; }

    public BotDeed Propose(IBotWilful bot)
    {
        var body = bot?.Self;
        var map = body?.Map;

        if (map == null || map == Map.Internal || !body.Alive)
        {
            return null;
        }

        // Counted before the class check so that "nobody is a captain" and "the captain never gets an offer"
        // are different numbers rather than the same silence.
        if (body is not BotMobile { Class.Leads: true })
        {
            NotACaptain++;

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

        // <b>Asked with a refusal in hand, so that one unreachable square is not the whole map.</b> The worst
        // square is an opinion about where blood is being spilt and knows nothing about whether a company can
        // get there; without this the captain is shown that one square, is shown it again the instant the
        // patrol fails, and the next four places on the list are never offered at all.
        //
        // A dictionary lookup and it must stay one — the reach ledger answers from pockets already proved by
        // searches that failed, and a real search per candidate per captain per beat is the price the hunt's
        // proposer paid once and wrote down. See BotHunter.Hunting.
        var square = BotPeril.Worst(
            map,
            body.Location,
            Range,
            out var reading,
            at => Reachable(map, body.Location, at)
        );

        if (square == Point3D.Zero)
        {
            Peaceful++;

            return null;
        }

        if (Free(body, BotSweep.Reach) < BotSweep.Least - 1)
        {
            TooFewNear++;

            return null;
        }

        Offered++;

        return new BotSweep(map, square, reading);
    }

    /// <summary>Whether the ground between the captain and a square is not already known to be closed.</summary>
    private static bool Reachable(Map map, Point3D from, Point3D square)
    {
        if (BotReach.Ask(map, from, square, BotArrival.Within(BotPeril.Side / 3)) != BotReachVerdict.Sealed)
        {
            return true;
        }

        Sealed++;

        return false;
    }

    /// <summary>Bots near enough to be called on, who are able to fight and are not already in a company.</summary>
    private static int Free(Mobile body, int range)
    {
        var map = body.Map;
        var free = 0;

        foreach (var mobile in map.GetMobilesInRange<Mobile>(body.Location, range))
        {
            if (mobile == body || mobile is not IBotSquadMember { Squad: null })
            {
                continue;
            }

            if (mobile is IBotAlly { AbleToFight: true })
            {
                free++;
            }
        }

        return free;
    }

    public static string Describe() =>
        Asked == 0
            ? $"no captain has ever been offered a patrol ({NotACaptain} answers went to bots that are not captains)"
            : $"{Asked} times a captain was asked: {Offered} were offered a square, {Held} were already in a company, {Unfit} were too hurt, {Peaceful} found nowhere dangerous enough, {Sealed} found the worst of it behind something, {TooFewNear} had too few free bots near; {BotSweep.Describe()}; {BotPeril.Describe()}";

    public static void Forget()
    {
        Asked = 0;
        NotACaptain = 0;
        Held = 0;
        Unfit = 0;
        Peaceful = 0;
        Sealed = 0;
        TooFewNear = 0;
        Offered = 0;
    }
}
