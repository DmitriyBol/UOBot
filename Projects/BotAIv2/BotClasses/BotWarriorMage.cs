namespace Server.BotAI.V2;

public sealed class BotWarriorMage : BotClass
{
    public override string Name => "WarriorMage";

    public override BotRole Role => BotRole.Melee;

    public override bool Casts => true;

    public override SkillName? MainSkill => null;

    private static readonly int[] _bookFirst =
    [
        BotArsenal.SpellHarm,
        BotArsenal.SpellProtection,
        BotArsenal.SpellFireball,
        BotArsenal.SpellLightning
    ];

    public override int[] BookFirst => _bookFirst;

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

        Leanings =
        [
            new BotLeaning("blade", null, 2, 56.0, 66.0, Weapon: true),
            new BotLeaning("spellsword", SkillName.Magery, 2, 56.0, 68.0),
            BotLeaning.Even(1)
        ];

        NeedsMeditation = false;

        IntrinsicManaTrickle = 2;

        PotionLimits[BotPotionKind.Mana] = 2;

        HerbIntervalMs = 2700000;

        ForageYieldMin = 5;
        ForageYieldMax = 12;

        Kit = new BotKit
        {
            Melee = BotArsenal.MeleeWithStaff(70.0),
            Reagents = 30,
            Spells = [BotArsenal.SpellHeal, BotArsenal.SpellMagicArrow]
        };
    }
}
