using System;
using Server.Items;
using Server.Mobiles;
using Server.Regions;

namespace Server.BotAI.V2;

/// <summary>
/// One of this population, counted when adding up our side of a fight. Implemented by the bot.
///
/// A marker rather than a list, and deliberately so: the first version summed allies by walking the whole
/// registry — a hundred and fifty entries, per bot, per assessment. Asking the map what is nearby costs what
/// is nearby.
/// </summary>
public interface IBotAlly
{
    bool AbleToFight { get; }
}

/// <summary>What to do about being attacked on the way somewhere.</summary>
public enum BotStand
{
    Nothing,

    Fight,

    Outmatched
}

/// <summary>
/// How dangerous a thing is, how dangerous the situation is, and therefore whether a bot walking somewhere
/// should stop and deal with what just hit it.
///
/// <para>
/// <b>Power is endurance times output.</b> Judging by health alone rates an ogre and a lich as near-equals —
/// 108 against 111 — when one of them hits two and a half times harder and casts. The measured reference
/// values, from the first version: a bot 1116, an ogre 1080, a lich 6937.
/// </para>
///
/// <para>
/// <b>Maximum health, never current, and this is not a detail.</b> The question is "can we win this fight",
/// which does not become a different question because somebody has taken a few hits. Whether one bot should
/// personally back out is a separate question with its own answer. Mixing the two made groups talk
/// themselves out of fights they were winning, the moment they started winning them: the party took damage,
/// its "power" collapsed, and it disbanded mid-battle.
/// </para>
/// </summary>
public static class BotThreat
{
    public static double Tolerance { get; set; } = 1.5;

    private const double SecondaryWeight = 0.4;

    public static double Power(Mobile m)
    {
        if (m == null || m.Deleted)
        {
            return 0.0;
        }

        return Math.Max(1, m.HitsMax) * AverageDamage(m);
    }

    private static double AverageDamage(Mobile m)
    {
        var melee = 1.0;

        if (m is BaseCreature creature && creature.DamageMax > 0)
        {
            melee = (creature.DamageMin + creature.DamageMax) / 2.0;
        }
        else if (m.Weapon is BaseWeapon weapon)
        {
            melee = (weapon.MinDamage + weapon.MaxDamage) / 2.0;
        }

        var magery = m.Skills[SkillName.Magery].Base;

        return Math.Max(1.0, melee + magery / 2.0);
    }

    public static bool Hostile(Mobile bot, BaseCreature creature)
    {
        if (bot?.Region?.IsPartOf<TownRegion>() == true)
        {
            return false;
        }

        if (creature == null || creature.Deleted || !creature.Alive)
        {
            return false;
        }

        if (creature.Controlled || creature.Summoned || creature.IsDeadBondedPet)
        {
            return false;
        }

        if (creature is BaseVendor)
        {
            return false;
        }

        if (Notoriety.Compute(bot, creature) == Notoriety.Innocent)
        {
            return false;
        }

        return bot.CanBeHarmful(creature, false);
    }

    public static double ThreatPower(Mobile bot, int range)
    {
        var map = bot?.Map;

        if (map == null || map == Map.Internal)
        {
            return 0.0;
        }

        var total = 0.0;
        var worst = 0.0;

        foreach (var creature in map.GetMobilesInRange<BaseCreature>(bot.Location, range))
        {
            if (!Hostile(bot, creature))
            {
                continue;
            }

            var power = Power(creature);

            total += power;

            if (power > worst)
            {
                worst = power;
            }
        }

        if (worst <= 0.0)
        {
            return 0.0;
        }

        return worst + (total - worst) * SecondaryWeight;
    }

    public static BaseCreature Strongest(Mobile bot, int range)
    {
        var map = bot?.Map;

        if (map == null || map == Map.Internal)
        {
            return null;
        }

        BaseCreature worst = null;
        var worstPower = 0.0;

        foreach (var creature in map.GetMobilesInRange<BaseCreature>(bot.Location, range))
        {
            if (!Hostile(bot, creature))
            {
                continue;
            }

            var power = Power(creature);

            if (power <= worstPower)
            {
                continue;
            }

            worst = creature;
            worstPower = power;
        }

        return worst;
    }

    public static BaseCreature Hunter(Mobile bot, int range)
    {
        var map = bot?.Map;

        if (map == null || map == Map.Internal)
        {
            return null;
        }

        BaseCreature worst = null;
        var worstPower = 0.0;

        foreach (var creature in map.GetMobilesInRange<BaseCreature>(bot.Location, range))
        {
            if (!Hostile(bot, creature) || creature.Combatant != bot)
            {
                continue;
            }

            var power = Power(creature);

            if (power <= worstPower)
            {
                continue;
            }

            worst = creature;
            worstPower = power;
        }

        return worst;
    }

    public static bool Anything(Mobile bot, int range)
    {
        var map = bot?.Map;

        if (map == null || map == Map.Internal)
        {
            return false;
        }

        foreach (var creature in map.GetMobilesInRange<BaseCreature>(bot.Location, range))
        {
            if (Hostile(bot, creature))
            {
                return true;
            }
        }

        return false;
    }

    public static double OurPower(Mobile bot, int range)
    {
        var map = bot?.Map;

        if (map == null || map == Map.Internal)
        {
            return 0.0;
        }

        var total = Power(bot);

        foreach (var mobile in map.GetMobilesInRange<Mobile>(bot.Location, range))
        {
            if (mobile == bot || mobile is not IBotAlly { AbleToFight: true })
            {
                continue;
            }

            total += Power(mobile);
        }

        return total;
    }

    public static bool Overrun(Mobile bot, IPoint3D where, int range)
    {
        var map = bot?.Map;

        if (map == null || map == Map.Internal || where == null)
        {
            return false;
        }

        var total = 0.0;
        var worst = 0.0;

        foreach (var creature in map.GetMobilesInRange<BaseCreature>(new Point3D(where), range))
        {
            if (!Hostile(bot, creature))
            {
                continue;
            }

            var power = Power(creature);

            total += power;

            if (power > worst)
            {
                worst = power;
            }
        }

        if (worst <= 0.0)
        {
            return false;
        }

        var threat = worst + (total - worst) * SecondaryWeight;
        var ours = OurPower(bot, range);

        return ours > 0.0 && threat / ours > Tolerance;
    }

    public static double Danger(Mobile bot, int range)
    {
        var ours = OurPower(bot, range);

        return ours <= 0.0 ? 0.0 : ThreatPower(bot, range) / ours;
    }

    public static BotStand Decide(Mobile bot, int range)
    {
        if (bot == null || !bot.Alive)
        {
            return BotStand.Nothing;
        }

        var threat = ThreatPower(bot, range);

        if (threat <= 0.0)
        {
            return BotStand.Nothing;
        }

        var ours = OurPower(bot, range);

        return ours > 0.0 && threat / ours <= Tolerance ? BotStand.Fight : BotStand.Outmatched;
    }
}
