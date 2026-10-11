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

    public static int CorneredRestMs { get; set; } = 20000;

    public static long CorneredRested { get; private set; }

    private static readonly Dictionary<Serial, long> _corneredAt = [];

    public static void CorneredRun(Mobile body)
    {
        if (body == null)
        {
            return;
        }

        if (_corneredAt.Count > 1024)
        {
            _corneredAt.Clear();
        }

        _corneredAt[body.Serial] = Core.TickCount;
    }

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

    public static double PoisonedHealth { get; set; } = 0.9;

    public static long PoisonedEarly { get; private set; }

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

        var poisoned = body.Poisoned;

        var withdrawing = BotSquad.Withdrawing(body);
        var contact = false;

        var forewarned = _early && bot.Squad == null && bot.Resolve?.Deed?.Foe == null && BotLadder.Forewarned(bot.Resolve);

        var losing = _early && BotTreadmill.Losing(body);

        if (_early && !losing && body.Hits >= body.HitsMax * (poisoned ? PoisonedHealth : EarlyHealth))
        {
            if (!withdrawing && !forewarned && !AtContact(bot, body, threat))
            {
                return null;
            }

            contact = !forewarned;
        }

        if (_early && poisoned && body.Hits >= body.HitsMax * EarlyHealth)
        {
            PoisonedEarly++;
        }

        if (!withdrawing && threat <= left * (_early && !losing ? EarlyBar : Bearable))
        {
            return null;
        }

        var ours = BotThreat.OurPower(body, BotBolt.Watch) - BotThreat.Power(body) + left;

        if (!withdrawing && ours > left && threat <= ours * BotThreat.Tolerance)
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

        if (CorneredRestMs > 0 && _corneredAt.TryGetValue(body.Serial, out var corneredTick) && now - corneredTick < CorneredRestMs)
        {
            CorneredRested++;

            return null;
        }

        if (_cleared.TryGetValue(body.Serial, out var clearedTick) && Core.TickCount - clearedTick < CalmMs
            && (bot.Resolve == null || bot.Resolve.HurtTick - clearedTick <= 0))
        {
            Calmed++;

            return null;
        }

        var strongest = BotThreat.Strongest(body, BotBolt.Watch);

        if (bot.Squad == null && BotHaven.Choose(body, strongest, out var haven, out var town, out var tiles))
        {
            return new BotBolt(map, body.Location, haven, town, tiles)
            {
                Began = () =>
                {
                    Running(body, threat, left, strongest, _early, town, contact, withdrawing, forewarned);
                    Offered(contact, withdrawing, forewarned);
                    Ran(body, Core.TickCount);

                    if (losing)
                    {
                        BotTreadmill.FlightsAfterLosing++;
                    }
                }
            };
        }

        if (BotBolt.Retreat(map, body, strongest) == Point3D.Zero)
        {
            Cornered++;

            Trapped(body);

            return null;
        }

        return new BotBolt(map, body.Location)
        {
            Began = () =>
            {
                Running(body, threat, left, strongest, _early, null, contact, withdrawing, forewarned);
                Offered(contact, withdrawing, forewarned);
                Ran(body, Core.TickCount);

                if (losing)
                {
                    BotTreadmill.FlightsAfterLosing++;
                }
            }
        };
    }

    public static long Forewarned { get; private set; }

    public static bool Calm(Mobile body, BotResolve resolve) =>
        body != null && _cleared.TryGetValue(body.Serial, out var cleared) && Core.TickCount - cleared < CalmMs
        && (resolve == null || resolve.HurtTick - cleared <= 0);

    public static double ContactOdds { get; set; } = 3.0;

    public static long Contacted { get; private set; }

    public static long Withdrawn { get; private set; }

    private void Offered(bool contact, bool withdrawing, bool forewarned = false)
    {
        if (forewarned)
        {
            Forewarned++;
        }
        else if (withdrawing)
        {
            Withdrawn++;
        }
        else if (contact)
        {
            Contacted++;
        }
        else if (_early)
        {
            Early++;
        }
    }

    private static bool AtContact(IBotWilful bot, Mobile body, double threat) =>
        ContactOdds > 0.0 && bot.Resolve?.Deed?.Foe == null && threat >= ContactOdds * Math.Max(1.0, BotThreat.OurPower(body, BotBolt.Watch));

    private static void Ran(Mobile body, long now)
    {
        _runs[body.Serial] = _runs.TryGetValue(body.Serial, out var ran) && now - ran.First < RepeatMs
            ? (ran.First, ran.Count + 1)
            : (now, 1);
    }

    public static long Outrun { get; private set; }

    public static BotDeed ToTown(IBotWilful bot, Mobile body, BaseCreature foe)
    {
        var map = body?.Map;

        if (bot == null || map == null || map == Map.Internal || !body.Alive || foe == null || bot.Squad != null)
        {
            return null;
        }

        if (bot.Resolve?.Deed is BotBolt)
        {
            return null;
        }

        if (foe is { AI: AIType.AI_Mage })
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

        if (!BotHaven.Choose(body, foe, out var haven, out var town, out var tiles))
        {
            return null;
        }

        var left = BotThreat.Power(body) * (body.HitsMax > 0 ? (double)body.Hits / body.HitsMax : 1.0);

        return new BotBolt(map, body.Location, haven, town, tiles)
        {
            Began = () =>
            {
                Running(body, BotThreat.ThreatPower(body, BotBolt.Watch), left, foe, false, town);
                Ran(body, Core.TickCount);
                Outrun++;
            }
        };
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

    private static void Running(
        Mobile body, double threat, double left, Mobile foe, bool early, string town, bool contact = false, bool withdrawing = false,
        bool forewarned = false
    )
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
            "{Name} runs{Early}{Town} from ({X}, {Y}) at {Hits} of {Pool} health: {Threat:F0} of hostile against the {Left:F0} it has left ({Ours:F0} of ours near), from {Foe} ({Kind}, {Off} tiles off); stamina {Stam} of {StamMax}{Mounted}{Contact}",
            body.Name,
            early ? " early" : "",
            town == null ? "" : $" for {town}",
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
            body.Mounted ? ", mounted" : "",
            forewarned ? "; seen coming, before its first blow"
            : withdrawing ? "; its company withdrew from it"
            : contact ? $"; at the first blows, the opposition past {ContactOdds:F1} times what it and ours near bring" : ""
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
        $"flights from {FromCasters} casters, {FromArchers} archers and {FromMelee} hand to hand, {StoodToCasters} not offered because the worst of it was a caster (fought to the end); {Early} flights offered early to a bot plainly outmatched (under {EarlyHealth:P0} of its health, the opposition past {EarlyBar:F1} times what it had left; {PoisonedEarly} looks taken for a poisoned bot above that gate, under {PoisonedHealth:P0}), {Contacted} at the first blows above that gate, the opposition past {ContactOdds:F1} times what the bot and ours near bring and the bot in no fight of its own choosing, {Withdrawn} to a bot whose company had just withdrawn from what was on it, {Outrun} runs for a town offered by the defender to a bot that could not win where it stood, {Cornered} bots losing with nowhere to run, {Calmed} flights not offered to a bot that had just got clear and had not been hit since, {Companied} not offered because the company round the bot could take it, {Winded} not offered to a bot that had run {MostRepeats} times in {RepeatMs / 60000} minutes and stands instead, every run counted when its flight was taken and not when it was offered, {Forewarned} flights taken before the first blow from something seen coming, "
        + $"{BotBolt.Tended} bandages and bottles taken on the run, {BotBolt.Bent} flights turned another way for a refused road, {BotBolt.Vetoed} retreats refused by the look ahead before anybody walked";
}
