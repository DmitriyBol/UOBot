namespace Server.BotAI.V2;

/// <summary>
/// Shoots, and has a knife for when that stops working.
///
/// The hybrid of the two ranges rather than a lesser archer: it trains the bow five points below one
/// and spends the difference on the blade, so the moment something closes it is still in a fight
/// instead of retreating from one. The pure archer answers that moment by not being there.
///
/// Rolls bow or crossbow, and the roll decides what it eats — arrows or bolts. That pairing is why
/// ammunition belongs to the weapon and not to the kit: a kit that named arrows in advance would hand
/// bolts to nobody and leave every crossbowman on the shard unable to shoot.
/// </summary>
public sealed class BotWarriorArcher : BotClass
{
    public override string Name => "WarriorArcher";

    public override BotRole Role => BotRole.Ranged;

    /// <summary>Archery outright. The dagger is an admission, not a second trade.</summary>
    public override SkillName? MainSkill => SkillName.Archery;

    protected override void Defaults()
    {
        Str = 40;
        Dex = 45;
        Int = 15;

        Skills =
        [
            (SkillName.Tactics, 65.0),
            (SkillName.Anatomy, 55.0),
            (SkillName.Healing, 50.0)
        ];

        Kit = new BotKit
        {
            Ranged = BotArsenal.BowOrCrossbow(75.0),
            Sidearm = BotArsenal.Sidearm(40.0)
        };
    }
}
