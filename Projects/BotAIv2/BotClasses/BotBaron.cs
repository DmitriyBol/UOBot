namespace Server.BotAI.V2;

public sealed class BotBaron : BotClass
{
    public override string Name => "Baron";

    public override bool Office => true;

    public override BotRole Role => BotRole.Melee;

    public override SkillName? MainSkill => SkillName.Swords;

    public override bool Leads => true;

    public override bool Seasoned => true;

    public override bool Grieves => true;

    public override bool Unpaid => true;

    public override string[] Sworn => ["Baron", "Warden", "Undertaker", "Stroll", "Shopper", "Mind"];

    protected override void Defaults()
    {

        Str = 95;
        Dex = 65;
        Int = 20;

        Skills =
        [
            (SkillName.Swords, 100.0),
            (SkillName.Tactics, 100.0),
            (SkillName.Anatomy, 100.0),
            (SkillName.MagicResist, 100.0),
            (SkillName.Healing, 100.0)
        ];

        Kit = new BotKit
        {

            Melee = [new BotWeaponOption(typeof(BotBaronHalberd), SkillName.Swords, 95.0)],

            Armour =
            [
                typeof(BotBaronHelm),
                typeof(BotBaronGorget),
                typeof(BotBaronArms),
                typeof(BotBaronGloves),
                typeof(BotBaronChest),
                typeof(BotBaronLegs),
                typeof(BotBaronCloak)
            ],

            Bandages = 100
        };

        PotionLimits[BotPotionKind.Heal] = 3;
        PotionLimits[BotPotionKind.Cure] = 2;

        Stipend = 10000;
    }
}
