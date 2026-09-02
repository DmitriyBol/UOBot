namespace Server.BotAI.V2;

/// <summary>
/// Plate, a blade, and spells anyway.
///
/// The one class that is defined by what it gives up. Meditation is how a caster gets mana back, and
/// the engine refuses it to anybody in metal — so a fighter who wanted to cast had to choose between
/// armour and being able to cast twice. This class chooses armour and is paid back with mana that
/// arrives on its own, in plate, in a fight, without sitting down.
///
/// Its trickle is worth the same as a mage's staff and does not stack with one: holding a mage's staff
/// buys it nothing. The advantage is not the quantity, it is that its hands are free for a sword while
/// it happens — and a mage that wants a sword has no answer to that at all.
///
/// It is the only class allowed two mana potions, for the same reason it is the only one with the
/// trickle: mana is what it runs out of, and the bottle is the only mana available to anybody who is
/// being hit.
/// </summary>
public sealed class BotWarriorMage : BotClass
{
    public override string Name => "WarriorMage";

    /// <summary>Melee, not caster: it holds the line. Its spells are reported through <see cref="Casts"/>.</summary>
    public override BotRole Role => BotRole.Melee;

    public override bool Casts => true;

    /// <summary>Settled by the roll, like the plain warrior. It is a fighter first.</summary>
    public override SkillName? MainSkill => null;

    protected override void Defaults()
    {
        Str = 40;
        Dex = 25;
        Int = 35;

        Skills =
        [
            (SkillName.Magery, 100.0),
            (SkillName.Tactics, 100.0),
            (SkillName.Anatomy, 100.0),
            (SkillName.Healing, 100.0)
        ];

        // Explicit rather than left at the default, because this is the one class for which the answer
        // is interesting: it wears plate, it therefore cannot meditate, and that is the whole premise.
        NeedsMeditation = false;

        IntrinsicManaTrickle = 2;

        PotionLimits[BotPotionKind.Mana] = 2;


        // <b>The one thing it burns that it could not go and get.</b> Reagents are shop goods in this era —
        // nothing grows them — so until 02.09.2026 the only route into a mage's pack was a counter, and a
        // mage with an empty purse was a mage with no way back: the debugger found Quill, Perri and Rowan
        // standing on 2, 8 and 24 gold with the only offer any of thirty-one proposers made being another
        // purchase they could not afford. The Sage has had the woods since it was written and the argument
        // was never about the Sage: it is that a caster short of herbs should have a day's walk as an
        // alternative to a shelf. Longer than the Sage's half hour, because the Sage's trip is its trade and
        // this one is a fallback.
        HerbIntervalMs = 2700000;

        Kit = new BotKit
        {
            Melee = BotArsenal.MeleeWithStaff(70.0),
            Reagents = 30,
            Spells = [BotArsenal.SpellHeal, BotArsenal.SpellMagicArrow]
        };
    }
}
