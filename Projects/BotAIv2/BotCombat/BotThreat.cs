using System;
using Server.Items;
using Server.Mobiles;
using Server.Regions;

namespace Server.BotAI.V2;

public interface IBotAlly
{

    bool AbleToFight { get; }
}

public enum BotStand
{

    Nothing,

    Fight,

    Outmatched
}

public static class BotThreat
{

    public static double Tolerance { get; set; } = 1.5;

    private const double SecondaryWeight = 0.4;

    public static double Secondary => SecondaryWeight;

    public static double Power(Mobile m)
    {
        if (m == null || m.Deleted)
        {
            return 0.0;
        }

        return Math.Max(1, m.HitsMax) * AverageDamage(m);
    }

    public static double Now(Mobile m)
    {
        if (m == null || m.Deleted || m.HitsMax <= 0)
        {
            return Power(m);
        }

        return Power(m) * Math.Clamp(m.Hits / (double)m.HitsMax, 0.1, 1.0);
    }

    public static bool SkillWeighs { get; set; } = true;

    public static double SkillFloor { get; set; } = 0.25;

    private static double AverageDamage(Mobile m)
    {
        var melee = 1.0;

        if (m is BaseCreature creature && creature.DamageMax > 0)
        {
            melee = (creature.DamageMin + creature.DamageMax) / 2.0;

            if (SkillWeighs)
            {
                var skill = Math.Max(
                    Math.Max(creature.Skills[SkillName.Wrestling].Value, creature.Skills[SkillName.Archery].Value),
                    Math.Max(
                        Math.Max(creature.Skills[SkillName.Swords].Value, creature.Skills[SkillName.Macing].Value),
                        creature.Skills[SkillName.Fencing].Value
                    )
                );

                melee *= Math.Clamp((skill + 20.0) / 120.0, SkillFloor, 1.0);
            }
        }
        else if (m.Weapon is BaseWeapon weapon)
        {
            melee = (weapon.MinDamage + weapon.MaxDamage) / 2.0;

            if (SkillWeighs)
            {
                var skill = m.Skills[weapon.Skill].Value;

                melee *= Math.Clamp((skill + 20.0) / 120.0, SkillFloor, 1.0);
            }
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

    public static bool Menacing(Mobile bot, BaseCreature creature)
    {
        if (!Hostile(bot, creature))
        {
            return false;
        }

        return creature.FightMode is not (FightMode.None or FightMode.Aggressor) || creature.Combatant != null;
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
            if (!Menacing(bot, creature))
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
            if (!Menacing(bot, creature))
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

    public static double OurPower(Mobile bot, int range, Mobile foe)
    {
        var map = bot?.Map;

        if (map == null || map == Map.Internal)
        {
            return 0.0;
        }

        var total = Power(bot);

        foreach (var mobile in map.GetMobilesInRange<Mobile>(bot.Location, range))
        {
            if (mobile == bot || mobile is not IBotAlly { AbleToFight: true } || Held(mobile, foe))
            {
                continue;
            }

            total += Power(mobile);
        }

        return total;
    }

    public static bool Held(Mobile ally, Mobile foe) =>
        ally?.Combatant is { Deleted: false, Alive: true } own && own != foe && own != ally;

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
            if (!Menacing(bot, creature))
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

    public static double ThreatAt(Mobile bot, IPoint3D where, int range)
    {
        var map = bot?.Map;

        if (map == null || map == Map.Internal || where == null)
        {
            return 0.0;
        }

        var total = 0.0;
        var worst = 0.0;

        foreach (var creature in map.GetMobilesInRange<BaseCreature>(new Point3D(where), range))
        {
            if (!Menacing(bot, creature))
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

        return worst <= 0.0 ? 0.0 : worst + (total - worst) * SecondaryWeight;
    }

    public static bool OverrunThere(Mobile bot, Mobile beside, IPoint3D where, int range)
    {
        var map = bot?.Map;

        if (map == null || map == Map.Internal || where == null || beside == null)
        {
            return false;
        }

        var total = 0.0;
        var worst = 0.0;

        foreach (var creature in map.GetMobilesInRange<BaseCreature>(new Point3D(where), range))
        {
            if (!Menacing(bot, creature))
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
        var ours = Power(bot) + OurPower(beside, range);

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
