namespace Server.BotAI.V2;

public sealed class BotCrafter : BotClass
{
    public override string Name => "Crafter";

    public override BotRole Role => BotRole.Producer;

    public override SkillName? MainSkill => SkillName.Blacksmith;

    protected override void Defaults()
    {
        Str = 50;
        Dex = 30;
        Int = 20;

        Skills =
        [
            (SkillName.Mining, 100.0),
            (SkillName.Blacksmith, 100.0),
            (SkillName.Tailoring, 100.0),
            (SkillName.Tinkering, 100.0),

            (SkillName.Fletching, 100.0),

            (SkillName.Lumberjacking, 100.0),
            (SkillName.Tactics, 100.0),
            (SkillName.Healing, 100.0)
        ];

        Leanings =
        [
            new BotLeaning("smith", SkillName.Blacksmith, 6, 65.0, 75.0, Tier: BotTier.Metal, Keeps: [(SkillName.Mining, 40.0)]),
            new BotLeaning(
                "tailor",
                SkillName.Tailoring,
                1,
                65.0,
                75.0,
                Tier: BotTier.Leather,
                Keeps: [(SkillName.Mining, 40.0), (SkillName.Blacksmith, 10.0)]
            ),
            new BotLeaning("bowyer", SkillName.Fletching, 2, 55.0, 65.0, Keeps: [(SkillName.Mining, 40.0), (SkillName.Blacksmith, 10.0)]),
            new BotLeaning("tinker", SkillName.Tinkering, 1, 45.0, 55.0, Keeps: [(SkillName.Mining, 40.0), (SkillName.Blacksmith, 10.0)])
        ];

        FreeCraftIntervalMs = 3600000;

        Kit = new BotKit
        {

            Melee = BotArsenal.Melee(100.0),

            Tools =
            [
                typeof(Server.Items.SmithHammer),
                typeof(Server.Items.Pickaxe),
                typeof(Server.Items.SewingKit),
                typeof(Server.Items.FletcherTools),
                typeof(Server.Items.Hatchet),

                typeof(Server.Items.TinkerTools)
            ]
        };
    }
}
