using Server.Items;

namespace Server.BotAI.V2;

public sealed class BotMage : BotClass
{
    public override string Name => "Mage";

    public override BotRole Role => BotRole.Caster;

    public override bool Casts => true;

    public override SkillName? MainSkill => SkillName.Magery;

    private static readonly int[] _bookFirst =
    [
        BotArsenal.SpellHarm,
        BotArsenal.SpellFireball,
        BotArsenal.SpellLightning,
        BotArsenal.SpellProtection,
        BotArsenal.SpellEnergyBolt
    ];

    public override int[] BookFirst => _bookFirst;

    protected override void Defaults()
    {
        Str = 25;
        Dex = 25;
        Int = 50;

        Skills =
        [
            (SkillName.Magery, 100.0),
            (SkillName.Inscribe, 100.0),
            (SkillName.Meditation, 100.0),
            (SkillName.Alchemy, 100.0),
            (SkillName.Wrestling, 100.0)
        ];

        Leanings =
        [
            new BotLeaning("archmage", SkillName.Magery, 4, 58.0, 72.0),
            new BotLeaning("scribe", SkillName.Inscribe, 1, 36.0, 40.0),
            BotLeaning.Even(1)
        ];

        NeedsMeditation = true;

        StaffManaTrickle = 2;
        StaffHue = 0x48D;

        HerbIntervalMs = 2700000;

        ForageYieldMin = 5;
        ForageYieldMax = 12;

        Kit = new BotKit
        {

            Armour = [typeof(Robe)],
            Staff = true,
            Reagents = 30,
            Spells = [BotArsenal.SpellHeal, BotArsenal.SpellMagicArrow, BotArsenal.SpellWeaken]
        };
    }
}
