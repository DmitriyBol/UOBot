using Server.Logging;

namespace Server.BotAI.V2;

/// <summary>
/// Offers a bot whose health is going the one thing that was missing from that rung: leaving.
///
/// <para>
/// <b>It shares <c>Failing</c> with <see cref="BotMedic"/> and the two do not overlap.</b> This one offers
/// nothing at all unless something hostile is standing there, so an ordinarily hurt bot is offered a bandage
/// and nothing else, exactly as before. When something <em>is</em> standing there the two compete on the
/// same arithmetic as everything else on this shard, and flight is priced to win — see
/// <see cref="BotBolt.Prior"/> for why that price is honest rather than a thumb on the scale.
/// </para>
///
/// <para>
/// <b>Not every hostile is worth running from</b>, and the test is what is left rather than what the bot
/// started with. A bot at a third of its health has a third of its fighting power, and the question is
/// whether what is here can finish that third — a rat cannot and an ogre plainly can. Judging by full
/// strength would have said "you can take these three" of a bot that had already lost the ability to.
/// </para>
/// </summary>
public sealed class BotFugitive : IBotProposer
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotFugitive));

    /// <summary>
    /// How much of what a bot has left the opposition may come to before running beats staying.
    ///
    /// Half. Not one — a bot on this rung is by definition already losing, and something worth half of what
    /// remains is something that finishes the job in the time a bandage takes to wind. Not a tenth either:
    /// a bot that bolts from a rat while bleeding never gets patched up at all.
    /// </summary>
    public static double Bearable { get; set; } = 0.5;

    /// <summary>
    /// How long between saying that somebody had nowhere to run.
    ///
    /// It is worth saying and it is not worth saying eighty-one times in half a minute, which is what the
    /// undertaking's own failure line did before this was asked here instead. One a minute for the whole
    /// population is enough to see it happening and cheap enough to leave on.
    /// </summary>
    public static int CorneredSayMs { get; set; } = 60000;

    /// <summary>
    /// Bots that were losing, wanted to run, and had nowhere to go. A named nought: without it, the whole
    /// case simply stops appearing anywhere.
    /// </summary>
    public static long Cornered { get; private set; }

    private static bool _saidRunning;

    private static bool _saidCornered;

    private static long _saidCorneredTick;

    public string Name => "Fugitive";

    /// <summary>The rung this was written for, shared with the medic. See the class note.</summary>
    public BotStanding Rung => BotStanding.Failing;

    public BotDeed Propose(IBotWilful bot)
    {
        var body = bot?.Self;
        var map = body?.Map;

        if (map == null || map == Map.Internal || !body.Alive || body.HitsMax <= 0)
        {
            return null;
        }

        var threat = BotThreat.ThreatPower(body, BotBolt.Watch);

        if (threat <= 0.0)
        {
            // Hurt, and nothing is doing it. That is the medic's case and always was.
            return null;
        }

        // What this bot can still bring, not what it could bring at full health. See the class note.
        var left = BotThreat.Power(body) * ((double)body.Hits / body.HitsMax);

        if (threat <= left * Bearable)
        {
            return null;
        }

        // <b>Asked before the offer is made, not discovered after it is taken.</b> See BotBolt.Retreat: an
        // undertaking that fails the instant it begins is offered again in the same beat, for ever. Two tile
        // probes, and only for a bot that is already losing a fight to something standing next to it.
        if (BotBolt.Retreat(map, body, BotThreat.Strongest(body, BotBolt.Watch)) == Point3D.Zero)
        {
            Cornered++;

            Trapped(body);

            // Nothing offered, so the medic and the fighting on this rung are what is left — which is the
            // honest answer for a bot with its back to the water.
            return null;
        }

        Running(body, threat, left);

        return new BotBolt(map, body.Location);
    }

    /// <summary>Says that somebody is cornered, at most once a minute for the whole population.</summary>
    private static void Trapped(Mobile body)
    {
        var now = Core.TickCount;

        if (_saidCornered && now - _saidCorneredTick < CorneredSayMs)
        {
            return;
        }

        _saidCornered = true;
        _saidCorneredTick = now;

        logger.Information(
            "{Name} is losing and has nowhere to run to, so it is not being offered flight; {Count} times so far",
            body.Name,
            Cornered
        );
    }

    private static void Running(Mobile body, double threat, double left)
    {
        if (_saidRunning)
        {
            return;
        }

        _saidRunning = true;

        // Once, with the arithmetic in it. "A bot ran away" and "a bot ran away from something it could have
        // beaten" look identical in a log, and the second is the one that costs the population its evening.
        logger.Information(
            "{Name} is running: {Threat:F0} of hostile against the {Left:F0} it has left at {Hits} of {Pool} health",
            body.Name,
            threat,
            left,
            body.Hits,
            body.HitsMax
        );
    }

    /// <summary>Lets the line be said again after a world reload.</summary>
    public static void Forget()
    {
        _saidRunning = false;
        _saidCornered = false;
        Cornered = 0;
    }
}
