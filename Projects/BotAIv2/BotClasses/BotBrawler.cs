namespace Server.BotAI.V2;

/// <summary>
/// Fights with its hands, and is therefore never holding anything it has to put down.
///
/// <para>
/// <b>Free hands is the talent, and it is the quietest strong thing in the nine.</b> Bandaging takes
/// both hands and several seconds, so every armed bot is forbidden to bandage while something is in
/// contact with it — which left a wounded bot in the first version exactly two options, and both were
/// bad: stand still and die, or run, losing the fight and the training. Drinking and casting have the
/// same problem in a milder form. A bot that fights with its fists has never had it. Nothing new is
/// added to give it this advantage: an existing restriction simply does not apply.
/// </para>
///
/// <para>
/// The rest of "very flexible" is its build — the highest Dexterity of any class and the lowest
/// Intelligence, so it swings often and has stamina left to keep stepping. No dodge chance: there is
/// no such mechanic in this era, and inventing one would put this class in the same position as the
/// mana potion, which exists outside the era and had to be justified item by item.
/// </para>
///
/// <para>
/// Two heal potions rather than one, because healing is what it is built around: it stands in contact
/// longer than anything else on the shard, and the bottle is the only healing available while there.
/// </para>
/// </summary>
public sealed class BotBrawler : BotClass
{
    public override string Name => "Brawler";

    public override BotRole Role => BotRole.Melee;

    public override SkillName? MainSkill => SkillName.Wrestling;

    protected override void Defaults()
    {
        Str = 60;
        Dex = 40;
        Int = 10;

        Skills =
        [
            (SkillName.Wrestling, 100.0),
            (SkillName.Tactics, 100.0),
            (SkillName.Anatomy, 100.0),
            (SkillName.Healing, 100.0)
        ];

        HandsAlwaysFree = true;

        PotionLimits[BotPotionKind.Heal] = 2;

        Kit = new BotKit
        {
            Armour = [typeof(BotBrawlerGloves)]
        };
    }
}
