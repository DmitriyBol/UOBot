using System.Collections.Generic;
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

    public static double Bearable { get; set; } = 0.5;

    public static int CorneredSayMs { get; set; } = 60000;

    public static long Cornered { get; private set; }

    public static int CalmMs { get; set; } = 8000;

    public static long Calmed { get; private set; }

    private static readonly Dictionary<Serial, long> _cleared = [];

    public static void Cleared(Mobile body)
    {
        if (body != null)
        {
            _cleared[body.Serial] = Core.TickCount;
        }
    }

    private static bool _saidRunning;

    private static bool _saidCornered;

    private static long _saidCorneredTick;

    public string Name => "Fugitive";

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
            return null;
        }

        var left = BotThreat.Power(body) * ((double)body.Hits / body.HitsMax);

        if (threat <= left * Bearable)
        {
            return null;
        }

        if (_cleared.TryGetValue(body.Serial, out var clearedTick) && Core.TickCount - clearedTick < CalmMs
            && (bot.Resolve == null || bot.Resolve.HurtTick - clearedTick <= 0))
        {
            Calmed++;

            return null;
        }

        if (BotBolt.Retreat(map, body, BotThreat.Strongest(body, BotBolt.Watch)) == Point3D.Zero)
        {
            Cornered++;

            Trapped(body);

            return null;
        }

        Running(body, threat, left);

        return new BotBolt(map, body.Location);
    }

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

        logger.Information(
            "{Name} is running: {Threat:F0} of hostile against the {Left:F0} it has left at {Hits} of {Pool} health",
            body.Name,
            threat,
            left,
            body.Hits,
            body.HitsMax
        );
    }

    public static void Forget()
    {
        _saidRunning = false;
        _saidCornered = false;
        Cornered = 0;
        Calmed = 0;
        _cleared.Clear();
    }

    public static string Describe() =>
        $"{Cornered} bots losing with nowhere to run, {Calmed} flights not offered to a bot that had just got clear and had not been hit since, "
        + $"{BotBolt.Tended} bandages and bottles taken on the run, {BotBolt.Bent} flights turned another way for a refused road";
}
