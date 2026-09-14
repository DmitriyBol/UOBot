using System.Linq;
using Server.Items;

namespace Server.BotAI.V2;

/// <summary>
/// The captain's opposite number, for the half of the population a captain cannot teach.
///
/// <para>
/// <b>A school for fighters existed and a school for casters did not, and the gap was not an oversight so
/// much as an arithmetic one.</b> <c>BotDrill</c> takes students who are <c>Melee or Ranged</c>, because its
/// master is a captain and a captain can only teach what it knows. A mage was therefore the one build on the
/// shard that could never be taught anything by anybody — and it is also the build whose value depends most
/// on what it knows, because everything it can do is rationed: mana, reagents, and a book that has to be
/// bought or written before it holds a single thing worth throwing.
/// </para>
///
/// <para>
/// <b>It teaches up to its own standing and no further, which is one number rather than two.</b> The same
/// rule the captain lives by, and for the same reason stated there: a separate teaching cap is a second
/// number on one shelf, and this project keeps paying for those. What it knows is what it can hand on.
/// </para>
///
/// <para>
/// <b>One lectern, and the captain has first claim on it.</b> The school holds one master at a time, so a
/// sage that finds the field taken does not teach — see <c>BotSchool.Master</c>. That is not deference, it is
/// the field being a place: two masters calling classes on the same plot would have the students standing in
/// two rings and hearing neither.
/// </para>
///
/// <para>
/// <b>Four circles at birth, and that is what makes it a teacher rather than a student.</b> A caster's book
/// is normally three spells and a long youth of buying scrolls; this one arrives knowing the first four
/// circles outright, so it can write any of them for anybody who asks and can demonstrate any of them to
/// anybody it teaches. The book is bound to it like every other book on the shard — a caster that loses its
/// book is not a caster and cannot earn the price of another.
/// </para>
/// </summary>
public sealed class BotSage : BotClass
{
    private const int FourthCircle = 31;

    public override string Name => "Sage";

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
