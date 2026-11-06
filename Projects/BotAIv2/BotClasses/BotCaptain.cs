namespace Server.BotAI.V2;

public sealed class BotCaptain : BotClass
{
    public override string Name => "Captain";

    public override bool Office => true;

    public override BotRole Role => BotRole.Ranged;

    public override SkillName? MainSkill => SkillName.Archery;

    public override bool Leads => true;

    public override bool Seasoned => true;

    public override double Seasoning => 0.78;

    public override bool Closes => true;

    protected override void Defaults()
    {

        Str = 80;
        Dex = 75;
        Int = 25;

        Skills =
        [
            (SkillName.Archery, 100.0),
            (SkillName.Swords, 100.0),
            (SkillName.Tactics, 100.0),
            (SkillName.Anatomy, 100.0),
            (SkillName.Healing, 100.0)
        ];

        Kit = new BotKit
        {
            Ranged = BotArsenal.Bow(100.0),

            Sidearm = new BotWeaponOption(typeof(Server.Items.Broadsword), SkillName.Swords, 77.0),

            Bandages = 50
        };
    }
}
