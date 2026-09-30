using System;
using System.Collections.Generic;
using Server.Logging;
using Server.Mobiles;

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

    public static int MostRepeats { get; set; } = 2;

    public static int RepeatMs { get; set; } = 120000;

    public static long Winded { get; private set; }

    public static long Companied { get; private set; }

    private static readonly Dictionary<Serial, (long First, int Count)> _runs = [];

    private static readonly Dictionary<Serial, long> _cleared = [];

    public static void Cleared(Mobile body)
    {
        if (body != null)
        {
            _cleared[body.Serial] = Core.TickCount;
        }
    }

    private static bool _saidCornered;

    private static long _saidCorneredTick;

    public BotFugitive(bool early = false) => _early = early;

    private readonly bool _early;

    public static double EarlyHealth { get; set; } = 0.7;

    public static double EarlyBar { get; set; } = 1.5;

    public static long StoodToCasters { get; private set; }

    public static long Early { get; private set; }

    public string Name => _early ? "Outmatched" : "Fugitive";

    public BotStanding Rung => _early ? BotStanding.Hunted : BotStanding.Failing;

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

        if (_early && body.Hits >= body.HitsMax * EarlyHealth)
        {
            return null;
        }

        if (threat <= left * (_early ? EarlyBar : Bearable))
        {
            return null;
        }

        var ours = BotThreat.OurPower(body, BotBolt.Watch) - BotThreat.Power(body) + left;

        if (ours > left && threat <= ours * BotThreat.Tolerance)
        {
            Companied++;

            return null;
        }

        if (BotThreat.Strongest(body, BotBolt.Watch) is Server.Mobiles.BaseCreature { AI: Server.Mobiles.AIType.AI_Mage })
        {
            StoodToCasters++;

            return null;
        }

        var now = Core.TickCount;

        if (_runs.TryGetValue(body.Serial, out var runs) && now - runs.First < RepeatMs && runs.Count >= MostRepeats)
        {
            Winded++;

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

        Running(body, threat, left, BotThreat.Strongest(body, BotBolt.Watch), _early);

        if (_early)
        {
            Early++;
        }

        _runs[body.Serial] = _runs.TryGetValue(body.Serial, out var ran) && now - ran.First < RepeatMs
            ? (ran.First, ran.Count + 1)
            : (now, 1);

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

    public static long FromCasters { get; private set; }

    public static long FromArchers { get; private set; }

    public static long FromMelee { get; private set; }

    private static void Running(Mobile body, double threat, double left, Mobile foe, bool early)
    {
        var kind = foe is BaseCreature { AI: AIType.AI_Mage } ? "a caster"
            : foe is BaseCreature { AI: AIType.AI_Archer } ? "an archer"
            : foe == null ? "nothing seen" : "hand to hand";

        switch (kind)
        {
            case "a caster":
                FromCasters++;
                break;
            case "an archer":
                FromArchers++;
                break;
            case "hand to hand":
                FromMelee++;
                break;
        }

        var off = foe == null ? -1 : Math.Max(Math.Abs(foe.X - body.X), Math.Abs(foe.Y - body.Y));
        var ours = BotThreat.OurPower(body, BotBolt.Watch) - BotThreat.Power(body);

        logger.Information(
            "{Name} runs{Early} from ({X}, {Y}) at {Hits} of {Pool} health: {Threat:F0} of hostile against the {Left:F0} it has left ({Ours:F0} of ours near), from {Foe} ({Kind}, {Off} tiles off); stamina {Stam} of {StamMax}{Mounted}",
            body.Name,
            early ? " early" : "",
            body.X,
            body.Y,
            body.Hits,
            body.HitsMax,
            threat,
            left,
            Math.Max(0.0, ours),
            foe?.Name ?? "nothing it could see",
            kind,
            off,
            body.Stam,
            body.StamMax,
            body.Mounted ? ", mounted" : ""
        );
    }

    public static void Forget()
    {
        _saidCornered = false;
        Cornered = 0;
        Calmed = 0;
        _cleared.Clear();
    }

    public static string Describe() =>
        $"flights from {FromCasters} casters, {FromArchers} archers and {FromMelee} hand to hand, {StoodToCasters} not offered because the worst of it was a caster (fought to the end); {Early} flights offered early to a bot plainly outmatched (under {EarlyHealth:P0} of its health, the opposition past {EarlyBar:F1} times what it had left), {Cornered} bots losing with nowhere to run, {Calmed} flights not offered to a bot that had just got clear and had not been hit since, {Companied} not offered because the company round the bot could take it, {Winded} not offered to a bot that had run {MostRepeats} times in {RepeatMs / 60000} minutes and stands instead, "
        + $"{BotBolt.Tended} bandages and bottles taken on the run, {BotBolt.Bent} flights turned another way for a refused road, {BotBolt.Vetoed} retreats refused by the look ahead before anybody walked";
}
