namespace Server.BotAI.V2;

public sealed class BotGatherer : BotClass
{
    public override string Name => "Gatherer";

    public override BotRole Role => BotRole.Producer;

    public override SkillName? MainSkill => SkillName.Mining;

    public override bool Rides => true;

    protected override void Defaults()
    {
        Str = 55;
        Dex = 30;
        Int = 15;

        Skills =
        [
            (SkillName.Mining, 100.0),
            (SkillName.Lumberjacking, 100.0),
            (SkillName.Tactics, 100.0),
            (SkillName.Healing, 100.0)
        ];

        Leanings =
        [
            new BotLeaning("digger", SkillName.Mining, 4, 65.0, 80.0, Tier: BotTier.Ore),
            new BotLeaning("woodsman", SkillName.Lumberjacking, 1, 45.0, 60.0),
            BotLeaning.Even(1)
        ];

        ForageIntervalMs = 900000;
        HerbIntervalMs = 900000;
        ForageYieldMin = 3;
        ForageYieldMax = 6;

        Kit = new BotKit
        {
            Melee = BotArsenal.Melee(100.0),
            Tools = [typeof(Server.Items.Pickaxe), typeof(Server.Items.Hatchet)]
        };
    }
}
