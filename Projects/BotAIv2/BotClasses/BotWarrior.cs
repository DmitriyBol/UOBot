namespace Server.BotAI.V2;

public sealed class BotWarrior : BotClass
{
    public override string Name => "Warrior";

    public override BotRole Role => BotRole.Melee;

    public override SkillName? MainSkill => null;

    protected override void Defaults()
    {
        Str = 50;
        Dex = 35;
        Int = 15;

        Skills =
        [
            (SkillName.Tactics, 100.0),
            (SkillName.Anatomy, 100.0),
            (SkillName.Healing, 100.0)
        ];

        Leanings =
        [
            new BotLeaning("blade", null, 5, 56.0, 72.0, Weapon: true),
            BotLeaning.Even(1)
        ];

        Kit = new BotKit
        {
            Melee = BotArsenal.Melee(100.0)
        };
    }
}
