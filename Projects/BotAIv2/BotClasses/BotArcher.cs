namespace Server.BotAI.V2;

public sealed class BotArcher : BotClass
{
    public override string Name => "Archer";

    public override BotRole Role => BotRole.Ranged;

    public override SkillName? MainSkill => SkillName.Archery;

    protected override void Defaults()
    {
        Str = 35;
        Dex = 50;
        Int = 15;

        Skills =
        [
            (SkillName.Tactics, 100.0),
            (SkillName.Anatomy, 100.0),
            (SkillName.Healing, 100.0)
        ];

        Leanings =
        [
            new BotLeaning("marksman", null, 5, 58.0, 74.0, Weapon: true),
            BotLeaning.Even(1)
        ];

        CritChancePerSkill = 0.001;
        CritMultiplier = 3;

        Kit = new BotKit
        {
            Ranged = BotArsenal.Bow(100.0),

            Sidearm = BotArsenal.Sidearm(40.0)
        };
    }
}
