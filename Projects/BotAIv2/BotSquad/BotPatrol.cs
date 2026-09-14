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

    public static int Range
    {
        get => _range > 0 ? _range : BotPopulation.Roam;
        set => _range = value;
    }

    private static int _range;

    public string Name => "Patrol";

    public BotStanding Rung => BotStanding.Free;

    public static long Asked { get; private set; }

    public static long NotACaptain { get; private set; }

    public static long Held { get; private set; }

    public static long Unfit { get; private set; }

    public static long Peaceful { get; private set; }

    public static long TooFewNear { get; private set; }

    public static long Sealed { get; private set; }

    public static long Offered { get; private set; }

    public BotDeed Propose(IBotWilful bot)
    {
        var body = bot?.Self;
        var map = body?.Map;

        if (map == null || map == Map.Internal || !body.Alive)
        {
            return null;
        }

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

    private static bool Reachable(Map map, Point3D from, Point3D square)
    {
        if (BotReach.Ask(map, from, square, BotArrival.Within(BotPeril.Side / 3)) != BotReachVerdict.Sealed)
        {
            return true;
        }

        Sealed++;

        return false;
    }

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
