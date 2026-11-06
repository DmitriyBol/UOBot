namespace Server.BotAI.V2;

public sealed class BotTamer : BotClass
{
    public override string Name => "Tamer";

    public override BotRole Role => BotRole.Ranged;

    public override SkillName? MainSkill => SkillName.AnimalTaming;

    protected override void Defaults()
    {
        Str = 35;
        Dex = 45;
        Int = 25;

        Skills =
        [
            (SkillName.AnimalTaming, 100.0),
            (SkillName.AnimalLore, 100.0),
            (SkillName.Veterinary, 100.0),
            (SkillName.Archery, 100.0),
            (SkillName.Tactics, 100.0),
            (SkillName.Healing, 60.0)
        ];

        Leanings =
        [
            new BotLeaning("beastmaster", SkillName.AnimalTaming, 3, 58.0, 70.0),
            new BotLeaning("marksman", null, 1, 56.0, 66.0, Weapon: true),
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
