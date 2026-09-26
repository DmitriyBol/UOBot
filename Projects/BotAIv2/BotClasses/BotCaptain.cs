namespace Server.BotAI.V2;

/// <summary>
/// The one bot on the shard that exists for the others rather than for itself.
///
/// <para>
/// <b>Born finished, and that is the whole of what makes it different at birth.</b> Every other class starts
/// a trade rather than holding one — <c>BotMobile.Learn</c> hands out fifty, thirty and twenty points to a
/// class's top three skills and nothing else, on purpose, because a population that begins at grandmaster
/// has nothing left to do and nothing to show a watcher. The captain is the exception the shard needs: it
/// cannot lead a company through ground that killed somebody while it is still learning which end of a bow
/// to hold, and it cannot teach a skill it does not have. So it arrives at Expert — see
/// <see cref="BotClass.Seasoned"/> — and that single fact is what buys it both of its offices.
/// </para>
///
/// <para>
/// <b>Both ranges, and the second one is not an apology.</b> <see cref="BotWarriorArcher"/> shoots and keeps
/// a dagger for when that stops working; the dagger is trained forty points below the bow and is, in its own
/// words, "an admission, not a second trade". The captain carries a broadsword at the same Expert standing
/// as its bow, and the difference shows in one moment: when something closes, an archer's whole case for
/// existing is that it is somewhere else, and a captain's is that it is exactly here. See
/// <see cref="Closes"/> — that flag is the class's entire combat identity, and everything else about the way
/// it fights is the shard's ordinary code.
/// </para>
///
/// <para>
/// <b>Expert and no further, which is a ceiling on its teaching before it is a ceiling on itself.</b> A
/// captain may only train a student up to its own standing in the skill, so the two numbers are one number:
/// there is no separate "teaching cap" constant to drift out of step with what the captain actually knows.
/// That is deliberate — a rule with two numbers on the same shelf is the defect this project keeps finding,
/// and the cheapest way not to have it is not to have the second number.
/// </para>
/// </summary>
public sealed class BotCaptain : BotClass
{
    public override string Name => "Captain";

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
