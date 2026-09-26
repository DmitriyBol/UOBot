using System;
using System.Collections.Generic;

namespace Server.BotAI.V2;

/// <summary>
/// What a bot is: its build, what it is born owning, what it may carry, and the one thing it can do
/// that nobody else can.
///
/// <para>
/// <b>Data and limits, never behaviour.</b> A class does not decide anything. Deciding lives in
/// <c>BotWill/</c>, and it reads classes the way it reads the map — as facts about the world. This is
/// the boundary the first version did not have: talents there were wired straight into whichever
/// subsystem noticed them first, so the mage's staff grew its own timer inside the magic code, and
/// there was no place to look up what a mage <em>was</em>. Nine files and one contract replace that.
/// </para>
///
/// <para>
/// <b>Two kinds of member, and the split is deliberate.</b> Identity — name, role, main skill — is
/// abstract and unchangeable: it is what the class <em>is</em>, and a configuration file that could
/// rename a healer into a smith would be a configuration file that can break every muster on the
/// shard. Everything else is a settable property carrying the class's own default, so
/// <c>bots.json</c> can move any number without a rebuild. That matters more here than it looks:
/// this shard is built on a different machine from the one it is designed on, so a number that needs
/// a compiler to change is a number that needs a person to change.
/// </para>
/// </summary>
public abstract class BotClass
{
    public const int ManaTrickleIntervalMs = 4000;

    public const int DefaultBrewIntervalMs = 600000;

    protected abstract void Defaults();

    public void Reset()
    {
        Str = 0;
        Dex = 0;
        Int = 0;
        Skills = [];
        Kit = new BotKit();
        NeedsMeditation = false;
        PotionLimits.Clear();
        BrewIntervalMs = DefaultBrewIntervalMs;
        IntrinsicManaTrickle = 0;
        StaffManaTrickle = 0;
        StaffHue = 0;
        CritChancePerSkill = 0.0;
        CritMultiplier = 3;
        HandsAlwaysFree = false;
        FreeCraftIntervalMs = 0;
        ForageIntervalMs = 0;
        ForageYieldMin = 0;
        ForageYieldMax = 0;
        Stipend = 0;

        Defaults();
    }

    public abstract string Name { get; }

    public abstract BotRole Role { get; }

    public virtual bool Casts => false;

    public abstract SkillName? MainSkill { get; }

    public virtual bool Seasoned => false;

    public virtual double Seasoning => 1.0;

    public virtual bool Leads => false;

    public virtual bool Closes => false;

    public virtual bool Levies => false;

    public virtual bool Tutors => false;

    public virtual string[] Sworn => [];

    public virtual int[] BookFirst => [];

    public virtual bool Grieves => false;

    public virtual bool Unpaid => false;

    public virtual bool Rides => true;

    public virtual bool Scavenges => true;

    public virtual bool Provisioned => false;

    public virtual bool DefendsOnly => false;

    public virtual bool Bidding => true;

    public int Stipend { get; set; }

    public int Str { get; set; }

    public int Dex { get; set; }

    public int Int { get; set; }

    public IReadOnlyList<(SkillName Skill, double Target)> Skills { get; set; } = [];

    public BotKit Kit { get; set; } = new();

    public bool Wants(SkillName skill)
    {
        if (MainSkill == skill)
        {
            return true;
        }

        var wanted = Skills;

        for (var i = 0; i < wanted.Count; i++)
        {
            if (wanted[i].Skill == skill)
            {
                return true;
            }
        }

        return Offered(Kit.Melee, skill)
            || Offered(Kit.Ranged, skill)
            || Kit.Sidearm.HasValue && Kit.Sidearm.Value.Skill == skill;
    }

    private static bool Offered(IReadOnlyList<BotWeaponOption> options, SkillName skill)
    {
        for (var i = 0; i < options.Count; i++)
        {
            if (options[i].Skill == skill)
            {
                return true;
            }
        }

        return false;
    }

    public bool NeedsMeditation { get; set; }

    public Dictionary<BotPotionKind, int> PotionLimits { get; } = [];

    public int PotionLimit(BotPotionKind kind) =>
        PotionLimits.TryGetValue(kind, out var limit) ? limit : 2;

    public virtual bool CanBrewManaPotion => Role is BotRole.Caster or BotRole.Medic;

    public int BrewIntervalMs { get; set; } = DefaultBrewIntervalMs;

    public int IntrinsicManaTrickle { get; set; }

    public int StaffManaTrickle { get; set; }

    public int StaffHue { get; set; }

    public int ManaTrickle(bool staffInHand) =>
        Math.Max(IntrinsicManaTrickle, staffInHand ? StaffManaTrickle : 0);

    public double CritChancePerSkill { get; set; }

    public int CritMultiplier { get; set; } = 3;

    public double CritChance(double skill) => Math.Clamp(skill * CritChancePerSkill, 0.0, 1.0);

    public bool HandsAlwaysFree { get; set; }

    public int FreeCraftIntervalMs { get; set; }

    public int HerbIntervalMs { get; set; }

    public int ForageIntervalMs { get; set; }

    public int ForageYieldMin { get; set; }

    public int ForageYieldMax { get; set; }
}
