using Server.SkillHandlers;

namespace Server.BotAI.V2;

/// <summary>
/// Hiding, moving unseen and searching for the hidden, as a bot does them: the engine's own three skills, used without a client.
///
/// <para>
/// <b>Patrick's order of 17.09.2026, evening: criminals act unseen.</b> They hide, train their hiding, and stalk their
/// victims hidden; and a bot that finds a body searches for the killer with Detect Hidden and tells the Baron. Nothing here
/// bends the era. The skills are the engine's (<c>Hiding</c>, <c>Stealth</c>, <c>DetectHidden</c>), used through
/// <c>Skills.UseSkill</c> as a player's macro would, and gained at the engine's own rate — Patrick chose the honest rate
/// over a faster one for bots. The Renaissance rules bind them: a hidden mobile's first step reveals it unless Stealth has
/// granted it quiet steps (<c>Mobile.OnMove</c>), and running or riding reveals it anyway; Stealth wants Hiding 80 and
/// armour under 26 (<c>Stealth.HidingRequirement</c>, <c>Stealth.GetArmorRating</c>) and grants a step for every ten
/// points; Detect Hidden searches a tenth of its skill in tiles, half that on a failed roll, and a player-mobile with any of
/// it notices a stealther within four tiles by itself (<c>PlayerMobile.OnMovement</c>). Every use costs the ten seconds of
/// the skill clock.
/// </para>
/// </summary>
public static class BotShadow
{
    public static bool Running { get; set; } = true;

    public static double RobHiding { get; set; } = 30.0;

    public static double StalkStealth { get; set; } = 30.0;

    public static long HideTries { get; private set; }

    public static long Sheathed { get; private set; }

    public static long Hid { get; private set; }

    public static long QuietTries { get; private set; }

    public static long WentQuiet { get; private set; }

    public static long Searches { get; private set; }

    public static bool Ready(Mobile m) =>
        m is { Deleted: false, Alive: true } && Core.TickCount - m.NextSkillTime >= 0 && m.Spell == null;

    public static bool CanHide(Mobile m) => m != null && m.Skills.Hiding.Base >= RobHiding;

    public static bool MayStealth(Mobile m) =>
        m != null && m.Skills.Hiding.Base >= Stealth.HidingRequirement && Stealth.GetArmorRating(m) < (Core.AOS ? 42 : 26);

    public static bool CanStalk(Mobile m) => MayStealth(m) && m.Skills.Stealth.Value >= StalkStealth;

    public static bool Hide(Mobile m)
    {
        if (!Ready(m))
        {
            return m?.Hidden == true;
        }

        if (m.Combatant != null || m.Warmode)
        {
            m.Combatant = null;
            m.Warmode = false;
            Sheathed++;
        }

        HideTries++;
        global::Server.Skills.UseSkill(m, SkillName.Hiding);

        if (m.Hidden)
        {
            Hid++;
        }

        return m.Hidden;
    }

    public static bool Quiet(Mobile m)
    {
        if (!Ready(m) || !m.Hidden)
        {
            return m is { Hidden: true, AllowedStealthSteps: > 0 };
        }

        QuietTries++;
        global::Server.Skills.UseSkill(m, SkillName.Stealth);

        var quiet = m is { Hidden: true, AllowedStealthSteps: > 0 };

        if (quiet)
        {
            WentQuiet++;
        }

        return quiet;
    }

    public static bool Search(Mobile m)
    {
        if (!Ready(m) || !global::Server.Skills.UseSkill(m, SkillName.DetectHidden) || m.Target == null)
        {
            return false;
        }

        Searches++;
        m.Target.Invoke(m, m);

        return true;
    }

    public static string Describe() =>
        $"hiding used {HideTries} times and hid {Hid} ({Sheathed} had to put up a weapon first), stealth used {QuietTries} times and moved quietly {WentQuiet}, {Searches} searches with Detect Hidden";

    public static void Forget()
    {
        HideTries = 0;
        Sheathed = 0;
        Hid = 0;
        QuietTries = 0;
        WentQuiet = 0;
        Searches = 0;
    }
}
