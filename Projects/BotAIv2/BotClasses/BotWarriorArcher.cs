namespace Server.BotAI.V2;

public sealed class BotWarriorArcher : BotClass
{
    public override string Name => "WarriorArcher";

    public override BotRole Role => BotRole.Ranged;

    public override SkillName? MainSkill => SkillName.Archery;

    protected override void Defaults()
    {
        Str = 40;
        Dex = 45;
        Int = 15;

        Skills =
        [
            (SkillName.Tactics, 100.0),
            (SkillName.Anatomy, 100.0),
            (SkillName.Healing, 100.0)
        ];

        Leanings =
        [
            new BotLeaning("marksman", null, 4, 56.0, 70.0, Weapon: true),
            BotLeaning.Even(1)
        ];

        Kit = new BotKit
        {
            Ranged = BotArsenal.BowOrCrossbow(75.0),
            Sidearm = BotArsenal.Sidearm(40.0)
        };
    }
}
