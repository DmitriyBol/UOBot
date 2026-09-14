using System;
using System.Collections.Generic;
using Server.Items;
using Server.Spells;

namespace Server.BotAI.V2;

/// <summary>
/// Mending: what a bot can heal with, who needs it, and the two ways of doing it.
///
/// <para>
/// <b>Spell before bandage, for anybody who can cast at all.</b> Not a preference — an ordering with three
/// reasons behind it. A spell lands in a couple of seconds where a bandage on yourself takes nine or ten; mana
/// comes back on its own where bandages cost money at a counter; and the herbs a heal spends are the ones a
/// caster is already walking to town for. So the cloth is what a caster falls back to when the pool is empty,
/// and what everybody else has instead.
/// </para>
///
/// <para>
/// <b>Every judgement about whether healing is possible belongs to the engine.</b> A bandage refuses an
/// undamaged patient by itself — "That being is not damaged!" — which is the anti-exploit this project would
/// otherwise have had to invent, because "bandage a healthy friend for ever" is the training dummy in a
/// different coat. A spell consumes its own reagents and mana in its own sequence. Nothing here simulates a
/// heal.
/// </para>
/// </summary>
public static class BotMend
{
    public static int BeyondMs { get; set; } = 10000;

    private static readonly Dictionary<Serial, long> _beyond = [];

    public static void Beyond(Mobile patient)
    {
        if (patient != null)
        {
            _beyond[patient.Serial] = Core.TickCount + BeyondMs;
        }
    }

    public static bool OutOfReach(Mobile patient)
    {
        if (patient == null || !_beyond.TryGetValue(patient.Serial, out var until))
        {
            return false;
        }

        if (Core.TickCount - until < 0)
        {
            return true;
        }

        _beyond.Remove(patient.Serial);

        return false;
    }

    private static readonly int[] ManaByCircle = [4, 6, 9, 11, 14, 20, 40, 50];

    public static double Hurt { get; set; } = 0.7;

    public static int Touch => Bandage.Range;

    public static int Cast { get; set; } = 8;

    public static int Peril { get; set; } = 6;

    public static int UnderFireMs { get; set; } = 3000;

    public static double Gulp { get; set; } = 0.4;

    public static double Mended { get; set; } = 0.95;

    public static double Share(Mobile m) =>
        m == null || m.HitsMax <= 0 ? 1.0 : Math.Clamp(m.Hits / (double)m.HitsMax, 0.0, 1.0);

    public static bool Wants(Mobile m) =>
        m is { Deleted: false, Alive: true } && m.Map != null && m.Map != Map.Internal
        && (Share(m) < Hurt || m.Poisoned);

    public static bool Whole(Mobile m) => m == null || Share(m) >= Mended;

    public static int Cloth(Mobile bot) => bot?.Backpack?.GetAmount(typeof(Bandage)) ?? 0;

    public static bool Winding(Mobile bot) => bot != null && BandageContext.GetContext(bot) != null;

    public static bool UnderFire(IBotWilful bot)
    {
        var resolve = bot?.Resolve;

        return resolve is { Struck: true } && Core.TickCount - resolve.HurtTick < UnderFireMs;
    }

    public static BasePotion Draught(Mobile bot)
    {
        var pack = bot?.Backpack;

        if (pack == null)
        {
            return null;
        }

        if (bot.Poisoned)
        {
            var cure = Bottle(pack, BotPotionKind.Cure);

            if (cure != null)
            {
                return cure;
            }
        }

        return Share(bot) < Gulp ? Bottle(pack, BotPotionKind.Heal) : null;
    }

    public static bool Swallow(Mobile bot, BasePotion potion)
    {
        if (bot == null || potion == null || potion.Deleted || !potion.CanDrink(bot))
        {
            return false;
        }

        potion.Drink(bot);

        return true;
    }

    public static int Bottles(Mobile bot, BotPotionKind kind)
    {
        var type = BotArsenal.Potion(kind);

        return type == null ? 0 : bot?.Backpack?.GetAmount(type) ?? 0;
    }

    public static BasePotion Bottle(Container pack, BotPotionKind kind)
    {
        var type = BotArsenal.Potion(kind);

        if (type == null)
        {
            return null;
        }

        var found = pack.FindItemByType(type);

        return found as BasePotion;
    }

    public static int Spell(Mobile bot, Mobile patient)
    {
        if (bot == null || patient == null || BotGrimoire.Book(bot) == null)
        {
            return -1;
        }

        if (patient.Poisoned && Ready(bot, BotArsenal.SpellCure))
        {
            return BotArsenal.SpellCure;
        }

        if (Ready(bot, BotArsenal.SpellGreaterHeal))
        {
            return BotArsenal.SpellGreaterHeal;
        }

        return Ready(bot, BotArsenal.SpellHeal) ? BotArsenal.SpellHeal : -1;
    }

    public static bool Ready(Mobile bot, int spell)
    {
        if (!BotGrimoire.Holds(bot, spell))
        {
            return false;
        }

        var circle = BotGrimoire.Circle(spell);

        if (circle < 1 || circle > ManaByCircle.Length || bot.Mana < ManaByCircle[circle - 1])
        {
            return false;
        }

        var made = SpellRegistry.NewSpell(spell, bot, null);
        var herbs = made?.Reagents;

        if (made == null)
        {
            return false;
        }

        var pack = bot.Backpack;

        for (var i = 0; herbs != null && i < herbs.Length; i++)
        {
            if ((pack?.GetAmount(herbs[i]) ?? 0) < 1)
            {
                return false;
            }
        }

        return true;
    }

    public static bool Herbs(Mobile bot, int spell)
    {
        if (bot == null || !BotGrimoire.Holds(bot, spell))
        {
            return false;
        }

        var made = SpellRegistry.NewSpell(spell, bot, null);

        if (made == null)
        {
            return false;
        }

        var herbs = made.Reagents;
        var pack = bot.Backpack;

        for (var i = 0; herbs != null && i < herbs.Length; i++)
        {
            if ((pack?.GetAmount(herbs[i]) ?? 0) < 1)
            {
                return false;
            }
        }

        return true;
    }

    public static bool Begin(Mobile bot, int spell)
    {
        if (bot == null || bot.Spell != null)
        {
            return false;
        }

        var made = SpellRegistry.NewSpell(spell, bot, null);

        return made != null && made.Cast();
    }

    public static bool Aim(Mobile bot, Mobile patient)
    {
        var aiming = bot?.Target;

        if (aiming == null || patient == null)
        {
            return false;
        }

        aiming.Invoke(bot, patient);

        return true;
    }

    public static bool Wind(Mobile bot, Mobile patient)
    {
        var pack = bot?.Backpack;
        var cloth = pack?.FindItemByType<Bandage>();

        if (cloth == null || patient == null || Winding(bot))
        {
            return false;
        }

        if (BandageContext.BeginHeal(bot, patient) == null)
        {
            return false;
        }

        cloth.Consume();

        return true;
    }
}
