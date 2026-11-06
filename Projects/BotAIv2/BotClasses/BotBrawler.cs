namespace Server.BotAI.V2;

public sealed class BotBrawler : BotClass
{
    public override string Name => "Brawler";

    public override BotRole Role => BotRole.Melee;

    public override SkillName? MainSkill => SkillName.Wrestling;

    protected override void Defaults()
    {

        Str = 60;
        Dex = 40;
        Int = 10;

        Skills =
        [
            (SkillName.Wrestling, 100.0),
            (SkillName.Tactics, 100.0),
            (SkillName.Anatomy, 100.0),
            (SkillName.Healing, 100.0)
        ];

        Leanings =
        [
            new BotLeaning("brawler", SkillName.Wrestling, 4, 58.0, 72.0),
            BotLeaning.Even(1)
        ];

        HandsAlwaysFree = true;

        PotionLimits[BotPotionKind.Heal] = 2;

        Kit = new BotKit
        {
            Armour = [typeof(BotBrawlerGloves)]
        };
    }
}
