using System.Linq;
using Server.Items;

namespace Server.BotAI.V2;

public sealed class BotArchitect : BotClass
{
    public override string Name => "Architect";

    public override bool Office => true;

    public override BotRole Role => BotRole.Producer;

    public override SkillName? MainSkill => SkillName.Blacksmith;

    public override bool Seasoned => true;

    public override double Seasoning => 0.78;

    public override bool Rides => true;

    protected override void Defaults()
    {

        Str = 65;
        Dex = 20;
        Int = 15;

        Skills =
        [
            (SkillName.Mining, 100.0),
            (SkillName.Blacksmith, 100.0),
            (SkillName.Tailoring, 100.0),
            (SkillName.Tinkering, 100.0),
            (SkillName.Tactics, 100.0),
            (SkillName.Healing, 100.0)
        ];

        Kit = new BotKit
        {

            Melee = BotArsenal.Melee(100.0),

            Tools = [typeof(SmithHammer), typeof(Pickaxe), typeof(SewingKit), typeof(TinkerTools)],

            Bandages = 30
        };
    }
}
