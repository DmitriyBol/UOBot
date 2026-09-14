namespace Server.BotAI.V2;

/// <summary>
/// The one bot on the shard that is not trying to make a living.
///
/// <para>
/// <b>Every other class is an answer to "how does this bot get by".</b> A miner digs because ore sells, a
/// tailor sews because armour is wanted, a captain patrols because the auction happens to offer it and it
/// has nothing it would rather do — and all of them are weighed in gold a minute, which is the one honest
/// currency this shard has. The Baron is the deliberate exception, and he is only interesting <em>as</em> an
/// exception: he is paid nothing, keeps nothing, gives away everything a fight drops, and the two pieces of
/// work he will take are the two the arithmetic would never choose. What replaces the wage is
/// <see cref="Grieves"/> — the ground that has killed people and has not been dealt with.
/// </para>
///
/// <para>
/// <b>Sworn, which is a harder statement than "he prefers his own work".</b> Preference is a number, and a
/// number loses: pricing his hunt high enough to beat a rescue would have been a thumb on the auction's
/// scale, and pricing it honestly would have left him mining. So he is not offered ordinary work at all —
/// see <see cref="BotClass.Sworn"/> — and the four trades he may take are named there rather than implied
/// by a score. This is the only class that says it, and it should stay the only one: a shard where several
/// classes cannot be offered work is a shard whose auction has stopped being the thing that decides.
/// </para>
///
/// <para>
/// <b>Born finished, like the captain and for a harder reason.</b> He walks into ground that has already
/// killed somebody, with five bots behind him who came because he asked, and he is the one standing between
/// the ground and them. A Baron learning which end of a halberd to hold would be five bots' worth of funeral.
/// Ninety-five across the four skills that decide a melee fight in this era — the blade, the tactics behind
/// it, the anatomy that makes it hurt and the resistance that keeps a caster from ending him at range — and
/// healing beside them, because the company he raises is going to need patching and he is the one who never
/// leaves.
/// </para>
///
/// <para>
/// <b>He leads, and that flag is nearly inert on him.</b> <see cref="BotClass.Leads"/> is read by a patrol
/// offer, a lectern and a scouting party, and none of the three is ever put to him: all are on the free rung
/// and all are outside his sworn list, so the auction never asks. He walks unknown ground under his own
/// office instead — see <c>BotWarden</c>, which pays nobody and goes alone. It is set because it is true — he calls companies
/// together for places, which is the whole of what the flag means — and leaving it false would have made the
/// one class that most obviously leads the one class that does not claim to.
/// </para>
/// </summary>
public sealed class BotBaron : BotClass
{
    public override string Name => "Baron";

    public override BotRole Role => BotRole.Melee;

    public override SkillName? MainSkill => SkillName.Swords;

    public override bool Leads => true;

    public override bool Seasoned => true;

    public override bool Grieves => true;

    public override bool Unpaid => true;

    public override string[] Sworn => ["Baron", "Warden", "Undertaker", "Stroll", "Shopper", "Mind"];

    protected override void Defaults()
    {
        Str = 95;
        Dex = 65;
        Int = 20;

        Skills =
        [
            (SkillName.Swords, 100.0),
            (SkillName.Tactics, 100.0),
            (SkillName.Anatomy, 100.0),
            (SkillName.MagicResist, 100.0),
            (SkillName.Healing, 100.0)
        ];

        Kit = new BotKit
        {
            Melee = [new BotWeaponOption(typeof(BotBaronHalberd), SkillName.Swords, 95.0)],

            Armour =
            [
                typeof(BotBaronHelm),
                typeof(BotBaronGorget),
                typeof(BotBaronArms),
                typeof(BotBaronGloves),
                typeof(BotBaronChest),
                typeof(BotBaronLegs),
                typeof(BotBaronCloak)
            ],

            Bandages = 100
        };

        PotionLimits[BotPotionKind.Heal] = 3;
        PotionLimits[BotPotionKind.Cure] = 2;

        Stipend = 10000;
    }
}
