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

    public override SkillName? MainSkill => SkillName.Healing;

    public override bool DefendsOnly => Defends;

    public static bool Defends { get; set; } = true;

    private static readonly int[] _bookFirst =
    [
        BotArsenal.SpellCure,
        BotArsenal.SpellProtection,
        BotArsenal.SpellGreaterHeal,
        BotArsenal.SpellArchCure,
        BotArsenal.SpellArchProtection,
        BotArsenal.SpellResurrection
    ];

    public override int[] BookFirst => _bookFirst;

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

        HerbIntervalMs = 1800000;

        StaffManaTrickle = 4;
        StaffHue = 0x48F;

        BrewIntervalMs = 420000;

        Kit = new BotKit
        {
            Armour = [typeof(Robe)],
            Staff = true,
            Reagents = 30,
            Spells = [BotArsenal.SpellHeal, BotArsenal.SpellCure, BotArsenal.SpellMagicArrow]
        };
    }
}
