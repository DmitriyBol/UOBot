using System.Linq;
using Server.Items;

namespace Server.BotAI.V2;

public sealed class BotSage : BotClass
{

    private const int FourthCircle = 31;

    public override string Name => "Sage";

    public override bool Office => true;

    public override BotRole Role => BotRole.Caster;

    public override SkillName? MainSkill => SkillName.Magery;

    public override bool Seasoned => true;

    public override double Seasoning => 0.78;

    public override bool Tutors => true;

    public override bool Casts => true;

    protected override void Defaults()
    {

        Str = 25;
        Dex = 20;
        Int = 55;

        Skills =
        [
            (SkillName.Magery, 100.0),
            (SkillName.EvalInt, 100.0),
            (SkillName.Inscribe, 100.0),
            (SkillName.Alchemy, 100.0),
            (SkillName.Meditation, 100.0)
        ];

        StaffManaTrickle = 5;

        StaffHue = 0x4AA;

        HerbIntervalMs = 1800000;

        Kit = new BotKit
        {

            Armour = [typeof(Robe)],
            Staff = true,

            Reagents = 60,

            Spells = Enumerable.Range(0, FourthCircle + 1).ToArray(),

            Bandages = 20
        };
    }
}
