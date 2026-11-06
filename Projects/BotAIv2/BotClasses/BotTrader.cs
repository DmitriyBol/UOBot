namespace Server.BotAI.V2;

public sealed class BotTrader : BotClass
{
    public override string Name => "Trader";

    public override BotRole Role => BotRole.Producer;

    public override SkillName? MainSkill => SkillName.ArmsLore;

    public override bool Neutral => true;

    protected override void Defaults()
    {
        Str = 45;
        Dex = 40;
        Int = 15;

        Skills =
        [
            (SkillName.Tactics, 100.0),
            (SkillName.ArmsLore, 100.0),
            (SkillName.Healing, 100.0),
            (SkillName.Anatomy, 100.0)
        ];

        Kit = new BotKit
        {
            Melee = BotArsenal.Melee(100.0)
        };
    }
}
