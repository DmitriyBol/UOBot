using Server.Items;

namespace Server.BotAI.V2;

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

        Leanings =
        [
            new BotLeaning("mender", SkillName.Healing, 4, 58.0, 72.0),
            BotLeaning.Even(1)
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
