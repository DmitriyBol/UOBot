using Server.Items;
using Server.Spells;

namespace Server.BotAI.V2;

/// <summary>
/// Casting at something, as opposed to casting at somebody who is hurt.
///
/// <para>
/// <b>The half of magic this project never wrote.</b> A caster's book filled up, its Inscribe climbed, its
/// reagents were bought and spent — on writing. In a fight it walked up and hit things with a stick, which is
/// what a mage is worst at and what its whole build is arranged to avoid. Watched from a client it reads
/// exactly as it is: mages and healers being beaten while holding a full spellbook.
/// </para>
///
/// <para>
/// <b>Distance is not a nicety here, it is the mechanic.</b> A blow disturbs a cast whenever the caster is a
/// player, and every bot is one — so a caster in contact does not cast slowly, it does not cast at all. That
/// is why this pairs with the stand-off: a caster fights at the range its spells reach and gives ground when
/// something closes, exactly as an archer does, and for a sharper reason.
/// </para>
///
/// <para>
/// What it does not do is choose cleverly. The strongest thing the book holds and the pool can pay for, and
/// nothing else — no resistances, no target types, no combinations. Those are worth having and they are worth
/// having <em>after</em> a caster stops losing fights it should win.
/// </para>
/// </summary>
public static class BotStrike
{
    private static readonly int[] Ladder =
    [
        BotArsenal.SpellMagicArrow,
        SpellHarm,
        SpellFireball,
        SpellLightning,
        SpellMindBlast,
        SpellEnergyBolt
    ];

    public static int Strongest(Mobile bot)
    {
        for (var i = Ladder.Length - 1; i >= 0; i--)
        {
            if (BotGrimoire.Holds(bot, Ladder[i]))
            {
                return Ladder[i];
            }
        }

        return -1;
    }

    public const int SpellHarm = 11;

    public const int SpellFireball = 17;

    public const int SpellLightning = 29;

    public const int SpellMindBlast = 36;

    public const int SpellEnergyBolt = 41;

    public static int Range { get; set; } = 8;

    public static int CastMs { get; set; } = 1500;

    private static readonly int[] ManaByCircle = [4, 6, 9, 11, 14, 20, 40, 50];

    private static readonly double[] ScrollSkillByCircle = [-50.0, -30.0, 0.0, 10.0, 20.0, 30.0, 40.0, 50.0];

    public static double SkillMargin { get; set; } = 20.0;

    public static int PoolCasts { get; set; } = 2;

    public static int Stock(Mobile bot)
    {
        if (bot == null)
        {
            return -1;
        }

        var magery = bot.Skills[SkillName.Magery].Value;

        for (var i = Ladder.Length - 1; i >= 0; i--)
        {
            var spell = Ladder[i];
            var circle = BotGrimoire.Circle(spell);

            if (circle < 1 || circle > ManaByCircle.Length || circle > ScrollSkillByCircle.Length)
            {
                continue;
            }

            if (bot.ManaMax < ManaByCircle[circle - 1] * PoolCasts)
            {
                continue;
            }

            if (magery >= ScrollSkillByCircle[circle - 1] + SkillMargin)
            {
                return spell;
            }
        }

        return -1;
    }

    public static bool Can(Mobile bot)
    {
        var booked = BotGrimoire.Book(bot) != null;

        for (var i = 0; i < Ladder.Length; i++)
        {
            if (booked && BotGrimoire.Holds(bot, Ladder[i]))
            {
                return true;
            }

            if (Scroll(bot, Ladder[i]) != null)
            {
                return true;
            }
        }

        return false;
    }

    public static int Best(Mobile bot)
    {
        for (var i = Ladder.Length - 1; i >= 0; i--)
        {
            if (Ready(bot, Ladder[i]))
            {
                return Ladder[i];
            }
        }

        return -1;
    }

    public static SpellScroll Scroll(Mobile bot, int spell)
    {
        var pack = bot?.Backpack;
        var kind = BotGrimoire.ScrollFor(spell);

        if (pack == null || kind == null)
        {
            return null;
        }

        return pack.FindItemByType(kind) as SpellScroll;
    }

    public static bool Ready(Mobile bot, int spell)
    {
        var scroll = Scroll(bot, spell);

        if (scroll == null && !BotGrimoire.Holds(bot, spell))
        {
            return false;
        }

        var circle = BotGrimoire.Circle(spell);

        if (circle < 1 || circle > ManaByCircle.Length || bot.Mana < ManaByCircle[circle - 1])
        {
            return false;
        }

        if (scroll != null)
        {
            return true;
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

    public static string Why(Mobile bot)
    {
        if (bot == null)
        {
            return "no body";
        }

        var owned = 0;
        var affordable = 0;

        for (var i = 0; i < Ladder.Length; i++)
        {
            var spell = Ladder[i];

            if (!BotGrimoire.Holds(bot, spell))
            {
                continue;
            }

            owned++;

            var circle = BotGrimoire.Circle(spell);

            if (circle < 1 || circle > ManaByCircle.Length || bot.Mana < ManaByCircle[circle - 1])
            {
                continue;
            }

            affordable++;
        }

        if (owned == 0)
        {
            return $"its book holds none of the {Ladder.Length} attack spells";
        }

        if (affordable == 0)
        {
            return $"it owns {owned} attack spells and has the mana for none of them";
        }

        return $"it owns {owned} attack spells, can pay for {affordable}, and is out of reagents for every one";
    }

    public static bool Begin(Mobile bot, int spell)
    {
        if (bot == null || bot.Spell != null)
        {
            return false;
        }

        var made = SpellRegistry.NewSpell(spell, bot, Scroll(bot, spell));

        return made != null && made.Cast();
    }

    public static bool Aim(Mobile bot, Mobile at)
    {
        var aiming = bot?.Target;

        if (aiming == null || at == null || at.Deleted)
        {
            return false;
        }

        aiming.Invoke(bot, at);

        return true;
    }
}
