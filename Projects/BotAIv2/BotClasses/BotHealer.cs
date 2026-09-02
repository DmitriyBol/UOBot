using Server.Items;

namespace Server.BotAI.V2;

/// <summary>
/// The green staff. Spends its mana on other people, and is equipped accordingly.
///
/// Its staff gives back twice what a mage's does, and the reason is the difference between the two
/// jobs rather than generosity: a mage that runs dry has stopped fighting, and a healer that runs dry
/// has stopped being the reason anybody else is still standing. It gets the better staff on the same
/// argument that gives a smith the better hammer.
///
/// It brews faster than everybody else — seven minutes against ten — and that is its only advantage
/// at the mortar. Brewing itself is open to anyone with the skill and the reagents; what the healer has
/// is the shorter wait and the reason to care.
///
/// Like the mage it refuses metal, and for the same mechanical reason: meditation is where most of its
/// mana comes from and the engine will not allow it in plate.
/// </summary>
public sealed class BotHealer : BotClass
{
    public override string Name => "Healer";

    public override BotRole Role => BotRole.Medic;

    public override bool Casts => true;

    /// <summary>Healing, not Magery. Its spells support the trade; the bandages are the trade.</summary>
    public override SkillName? MainSkill => SkillName.Healing;

    protected override void Defaults()
    {
        Str = 25;
        Dex = 30;
        Int = 45;

        Skills =
        [
            (SkillName.Healing, 100.0),
            (SkillName.Anatomy, 100.0),
            (SkillName.Magery, 100.0),
            (SkillName.Meditation, 100.0),
            (SkillName.Alchemy, 100.0)
        ];

        NeedsMeditation = true;

        StaffManaTrickle = 4;
        StaffHue = 0x48F;

        BrewIntervalMs = 420000;

        Kit = new BotKit
        {
            // <b>The robe, which is dress and not armour, and that is why it had to be issued here.</b>
            // BotHarness surveys the craft systems for BaseArmor and covers six layers, none of them the
            // outer torso — so a robe cannot enter the catalogue and cannot be ordered from it, whatever a
            // caster is willing to pay. It stops nothing and is not meant to: this era gives a robe no
            // armour rating at all. What it is is the one garment that reads as a mage from across a
            // street, worn from birth and bound like everything else issued, so nobody sells it at a
            // counter with a full pack.
            Armour = [typeof(Robe)],
            Staff = true,
            Reagents = 30,
            Spells = [BotArsenal.SpellHeal, BotArsenal.SpellCure, BotArsenal.SpellMagicArrow]
        };
    }
}
