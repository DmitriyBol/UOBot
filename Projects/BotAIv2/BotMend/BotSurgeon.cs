using System;
using Server.Logging;

namespace Server.BotAI.V2;

public sealed class BotSurgeon : IBotProposer
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotSurgeon));

    public static int Reach { get; set; } = 20;

    public static int MovingMs { get; set; } = 1500;

    public static long Moving { get; private set; }

    public static int Upstairs { get; set; } = 8;

    public static long Floors { get; private set; }

    private static bool _said;

    public static bool Calls { get; set; } = true;

    public static long Called { get; private set; }

    public static long CalledNobody { get; private set; }

    public static void Call(BotMobile patient, bool failing = false)
    {
        var map = patient?.Map;

        if (!Calls || map == null || map == Map.Internal || !patient.Alive)
        {
            return;
        }

        if (patient.Squad != null)
        {
            CalledCompany++;

            BotSquads.Hurry(patient.Squad);
        }

        BotMobile best = null;
        var nearest = int.MaxValue;
        var seen = 0;
        BotSquad nurse = null;
        var nurseOff = int.MaxValue;

        foreach (var m in map.GetMobilesInRange<BotMobile>(patient.Location, Reach))
        {
            if (m == patient || m.Deleted || !m.Alive || m.Resolve is not { } resolve)
            {
                continue;
            }

            seen++;

            if (m.Squad != null)
            {

                if (BotSquad.Nurses && m.Squad != patient.Squad && m.Class?.Role == BotRole.Medic
                    && m.Squad.Focus is not { Deleted: false, Alive: true } && !m.Squad.OnTheMarch
                    && Math.Abs(m.Z - patient.Z) < Upstairs && !BotRegard.Hostile(m, patient)
                    && (BotMend.Spell(m, patient) >= 0 || BotMend.Cloth(m) > 0))
                {
                    var away = Math.Max(Math.Abs(m.X - patient.X), Math.Abs(m.Y - patient.Y));

                    if (away < nurseOff)
                    {
                        nurse = m.Squad;
                        nurseOff = away;
                    }

                    continue;
                }

                WhyCompany++;

                continue;
            }

            if (m.Tired || BotLadder.Failing(m) || BotLadder.Hunted(resolve))
            {
                WhyFighting++;

                continue;
            }

            if (resolve.Deed is BotSalve || resolve.Deed is { Committed: true })
            {
                WhyBusy++;

                continue;
            }

            if (Math.Abs(m.Z - patient.Z) >= Upstairs || BotRegard.Hostile(m, patient))
            {
                WhyApart++;

                continue;
            }

            if (BotMend.Spell(m, patient) < 0 && BotMend.Cloth(m) <= 0)
            {
                WhyNoMeans++;

                continue;
            }

            var off = Math.Max(Math.Abs(m.X - patient.X), Math.Abs(m.Y - patient.Y));

            if (off >= nearest)
            {
                continue;
            }

            best = m;
            nearest = off;
        }

        if (best == null || nurse != null && nurseOff < nearest)
        {
            if (nurse != null)
            {
                nurse.Nurse(patient);
                CalledNurse++;

                return;
            }

            if (patient.Squad != null)
            {
                return;
            }

            CalledNobody++;

            if (seen == 0)
            {
                CalledAlone++;
            }

            return;
        }

        Called++;

        if (Presses && failing && best.Resolve is { } free
            && (free.Deed == null || free.Deed.Resumes && (free.Paused == null || BotCry.StackCalls && free.Under == null)))
        {
            var trains = BotMend.Spell(best, patient) >= 0 ? SkillName.Magery : SkillName.Healing;

            if (BotWill.Press(best, new BotSalve(patient, map, onSelf: false, trains), $"called to {patient.Name} under its failing line as its nearest healer, {nearest} tiles off", BotCry.StackCalls))
            {
                Pressed++;

                return;
            }
        }

        BotWill.Alert(best);
    }

    public static bool Presses { get; set; } = true;

    public static long Pressed { get; private set; }

    public static long CalledNurse { get; private set; }

    public static long CalledCompany { get; private set; }

    public static long CalledAlone { get; private set; }

    public static long WhyCompany { get; private set; }

    public static long WhyFighting { get; private set; }

    public static long WhyBusy { get; private set; }

    public static long WhyApart { get; private set; }

    public static long WhyNoMeans { get; private set; }

    public static string Describe2() =>
        $"{Called} bots that became patients had the nearest of ours able to mend within {Reach} tiles look up at once ({Pressed} of them under the failing line pressed to it, their work put down{(Presses ? "" : "; off")}), {CalledCompany} were members whose company was asked to answer at once, {CalledNurse} were brought to the medics of a company near with no enemy, {CalledNobody} lone ones had nobody free and able there ({CalledAlone} of them nobody of ours at all; bots near passed over: {WhyCompany} in a company, {WhyFighting} in a fight, failing or leaving, {WhyBusy} mending already or on work that may not be left, {WhyApart} on another floor or hostile, {WhyNoMeans} with neither a heal nor cloth)";

    public string Name => "Surgeon";

    public BotStanding Rung => BotStanding.Free;

    public BotDeed Propose(IBotWilful bot)
    {
        var body = bot?.Self;
        var map = body?.Map;

        if (map == null || map == Map.Internal || !body.Alive)
        {
            return null;
        }

        var onMe = BotThreat.Hunter(body, BotDefender.Reach);

        if (onMe != null)
        {
            BotCry.Raise(body, onMe);

            return null;
        }

        var patient = Worst(body, map);

        if (patient == null)
        {
            return null;
        }

        var spell = BotMend.Spell(body, patient);

        if (spell < 0 && BotMend.Cloth(body) <= 0)
        {
            return null;
        }

        var onSelf = patient == body;

        if (!onSelf && !_said)
        {
            _said = true;

            logger.Information("{Name} has started patching up the rest of them", body.Name);
        }

        return new BotSalve(patient, map, onSelf, spell >= 0 ? SkillName.Magery : SkillName.Healing);
    }

    private static Mobile Worst(Mobile bot, Map map)
    {
        Mobile worst = BotMend.Wants(bot) ? bot : null;
        var lowest = worst == null ? 1.0 : BotMend.Share(bot);

        foreach (var mobile in map.GetMobilesInRange<Mobile>(bot.Location, Reach))
        {

            if (mobile == bot || mobile is not IBotAlly || !BotMend.Wants(mobile) || BotMend.OutOfReach(mobile))
            {
                continue;
            }

            if (Math.Abs(mobile.Z - bot.Z) >= Upstairs)
            {
                Floors++;

                continue;
            }

            var share = BotMend.Share(mobile);

            if (share >= lowest)
            {
                continue;
            }

            if (BotMend.Abetting(bot, mobile) != null)
            {
                continue;
            }

            if (OnTheMove(bot, mobile))
            {
                Moving++;

                continue;
            }

            worst = mobile;
            lowest = share;
        }

        return worst;
    }

    private static bool OnTheMove(Mobile healer, Mobile patient) =>
        MovingMs > 0
        && Core.TickCount - patient.LastMoveTime < MovingMs
        && !healer.InRange(patient.Location, BotMend.Cast)
        && !BotMend.Embattled(patient);

    public static string Describe() => $"{Moving} times the worst-hurt was passed over for walking on out of a heal's reach and {Floors} for standing on another floor";

    public static void Forget()
    {
        _said = false;
        Moving = 0;
        Floors = 0;
        Called = 0;
        Pressed = 0;
        CalledNobody = 0;
        CalledCompany = 0;
        CalledNurse = 0;
        CalledAlone = 0;
        WhyCompany = 0;
        WhyFighting = 0;
        WhyBusy = 0;
        WhyApart = 0;
        WhyNoMeans = 0;
    }
}
