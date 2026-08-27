using Server.Items;

namespace Server.BotAI.V2;

/// <summary>
/// What every one of the King's Rangers has in common, whatever it carries.
///
/// <para>
/// <b>A standing company of five kept at the crown's expense, and not a trade.</b> Every other class on this
/// shard earns: it digs, it sews, it hunts, and the auction weighs what it does against what everything else
/// pays. These five are paid nothing, take nothing off a corpse, and have exactly one duty — to walk the
/// quadrants of the island Britain stands on and find out what is on them. They are the instrument by which
/// the map gets filled in, and they are deliberately better at surviving than at anything else.
/// </para>
///
/// <para>
/// <b>Provisioned rather than paid, which is a different thing and matters to the economy.</b> Their
/// bandages, reagents and arrows are replaced by the crown — see <see cref="BotClass.Provisioned"/> — so
/// nothing they do puts gold into the population or takes it out. A ranger that had to buy reagents would be
/// a ranger standing in a shop, and a ranger paid a wage would be a second tap on a shard that has spent a
/// day learning what one tap does.
/// </para>
///
/// <para>
/// <b>They take no share and never loot.</b> <see cref="BotClass.Unpaid"/> keeps them out of the division of
/// spoils, and <see cref="BotClass.Scavenges"/> is false so they will not stoop to a corpse at all. A company
/// that stopped to rob what it killed would be a company strung out across a quadrant with its healer forty
/// tiles from its warriors, which is the one arrangement that gets all five of them killed.
/// </para>
/// </summary>
public abstract class BotRanger : BotClass
{
    /// <summary>Born finished. A ranger learning its trade is five bots' worth of funeral: see BotBaron.</summary>
    public override bool Seasoned => true;

    /// <summary>Takes no share of anything the company kills. The crown keeps them.</summary>
    public override bool Unpaid => true;

    /// <summary>Never stoops to a corpse. See the class note: a strung-out company is a dead one.</summary>
    public override bool Scavenges => false;

    /// <summary>Bandages, reagents and arrows come from the crown and never run out.</summary>
    public override bool Provisioned => true;

    /// <summary>
    /// The only errands a ranger may ever be offered.
    ///
    /// <para>
    /// A whitelist, like the Baron's, and for a harder reason: theirs is the only class on the shard with no
    /// trade at all. There is nothing for the auction to weigh — a ranger cannot mine, cannot sew, cannot
    /// take a commission and has no purse to spend — so leaving them open to ordinary work would produce a
    /// company that wandered off to the shops one bot at a time. See <c>BotWill.Sworn</c>: any office added
    /// to this class in future has to be named here in the same breath, or it will look exactly like code
    /// that does not run.
    /// </para>
    /// </summary>
    /// <para>
    /// <b>"Ranger" alone was a company that could not come to its own aid, and it read exactly like five
    /// bots too stupid to help each other.</b> Coming to a comrade under attack is <c>Rescuer</c>; standing
    /// and fighting what has laid hands on you is <c>Defender</c>; patching a hurt comrade is <c>Medic</c>
    /// and <c>Surgeon</c>; getting a dying one out is <c>Fugitive</c>. Every one of those is an ordinary
    /// proposer on the free rung, and a sworn list that does not name them refuses each of them before it is
    /// ever called — silently, which is the whole danger of this mechanism and the third time in one day it
    /// has bitten. Mutual aid is first for this company by order, so it is first in this list.
    /// </para>
    /// <summary>Nothing at all: this class is not run by the auction. See <see cref="BotClass.Bidding"/>.</summary>
    public override bool Bidding => false;

    // <b>Rescuer, Defender and Fugitive were here for one window and had to come out again.</b> Adding them
    // did fix the company standing about while its members were killed one at a time — but a rescue is a
    // *piece of work*, reckoned at eighty a minute against the sweep's forty, so every skirmish won the
    // auction and threw the sweep away. The round then began again from nothing: three squares of five
    // hundred read in eight minutes, and from outside it looks exactly like five bots standing in a field
    // for an hour, which is what it was.
    //
    // Their fighting is a reflex instead — see BotMobile.Watch, which takes a target for the whole company
    // through the squad and never touches the errand underneath. That is strictly better than the rescue
    // errand was: it engages on sight rather than on being hit, it puts all five on one target, and the
    // sweep survives the fight. Mending stays a piece of work, because it genuinely is one and it is short.
}

/// <summary>
/// The two who stand in front. Plate, a shield and a blade, and no expectation of doing anything else.
/// </summary>
public sealed class BotRangerWarrior : BotRanger
{
    public override string Name => "Ranger Warrior";

    public override BotRole Role => BotRole.Melee;

    public override SkillName? MainSkill => SkillName.Swords;

    protected override void Defaults()
    {
        // The Baron's build, because it is the same job: stand in contact, take the blows the rest cannot,
        // and still be standing at the end of it. Ninety-five carries gold plate — the engine asks sixty of a
        // cuirass in this era — and sixty-five of dexterity is what is left of a fighter's speed once a full
        // suit has taken its points off.
        Str = 95;
        Dex = 65;
        Int = 20;

        Skills =
        [
            (SkillName.Swords, 95.0),
            (SkillName.Tactics, 95.0),
            (SkillName.Anatomy, 95.0),
            (SkillName.MagicResist, 95.0),
            (SkillName.Parry, 95.0),
            (SkillName.Healing, 60.0)
        ];

        Kit = new BotKit
        {
            // One option rather than a roll: what a ranger carries is livery, not luck. Declared here and on
            // the skill list both — the kit's roll sets the skill of what the bot holds, the class list is
            // what the birth line and the title read back, and both numbers have nowhere to drift apart to.
            Melee = [new BotWeaponOption(typeof(BotRangerBlade), SkillName.Swords, 95.0)],

            Armour =
            [
                typeof(BotRangerHelm),
                typeof(BotRangerGorget),
                typeof(BotRangerArms),
                typeof(BotRangerGloves),
                typeof(BotRangerChest),
                typeof(BotRangerLegs),
                typeof(BotRangerShield),
                typeof(BotRangerCloak)
            ],

            Bandages = 100
        };

        PotionLimits[BotPotionKind.Heal] = 3;
        PotionLimits[BotPotionKind.Cure] = 2;
    }
}

/// <summary>The one who shoots. Expert with the bow, and armoured to survive being reached.</summary>
public sealed class BotRangerArcher : BotRanger
{
    public override string Name => "Ranger Archer";

    public override BotRole Role => BotRole.Ranged;

    public override SkillName? MainSkill => SkillName.Archery;

    protected override void Defaults()
    {
        // Dexterity over strength, which is what a bow is drawn with — but not a scout's build: this one is
        // walking into quadrants nobody has read, and being reached is a certainty rather than a risk.
        Str = 70;
        Dex = 95;
        Int = 20;

        Skills =
        [
            (SkillName.Archery, 95.0),
            (SkillName.Tactics, 90.0),
            (SkillName.Anatomy, 85.0),
            (SkillName.MagicResist, 85.0),
            (SkillName.Healing, 60.0)
        ];

        // A thousandth per point, as every archer on this shard: 10% at a hundred.
        CritChancePerSkill = 0.001;
        CritMultiplier = 3;

        Kit = new BotKit
        {
            Ranged = [new BotWeaponOption(typeof(BotRangerBow), SkillName.Archery, 95.0, typeof(Arrow))],

            // Studded rather than plate. The engine's own rule for this era is that a bow wants a free arm,
            // and a full suit on an archer is an archer who cannot draw — this is the armour a shooting build
            // can actually wear, in the same livery.
            Armour =
            [
                typeof(BotRangerArcherCap),
                typeof(BotRangerArcherGorget),
                typeof(BotRangerArcherArms),
                typeof(BotRangerArcherGloves),
                typeof(BotRangerArcherChest),
                typeof(BotRangerArcherLegs),
                typeof(BotRangerCloak)
            ],

            Bandages = 100
        };

        PotionLimits[BotPotionKind.Heal] = 3;
        PotionLimits[BotPotionKind.Cure] = 2;
    }
}

/// <summary>The one who casts. Four circles, and every one of them free.</summary>
public sealed class BotRangerMage : BotRanger
{
    public override string Name => "Ranger Mage";

    public override BotRole Role => BotRole.Caster;

    public override SkillName? MainSkill => SkillName.Magery;

    public override bool Casts => true;

    protected override void Defaults()
    {
        Str = 45;
        Dex = 40;
        Int = 95;

        Skills =
        [
            (SkillName.Magery, 95.0),
            (SkillName.EvalInt, 95.0),
            (SkillName.MagicResist, 95.0),
            (SkillName.Meditation, 90.0),
            (SkillName.Wrestling, 60.0)
        ];

        NeedsMeditation = true;

        StaffManaTrickle = 4;
        StaffHue = BotRegalia.RoyalRed;

        Kit = new BotKit
        {
            Staff = true,

            // The reagents are the crown's and are replaced as they are spent — see BotRanger.Provisioned —
            // so this is a starting stock rather than a budget.
            Reagents = 100,

            // <b>The first five circles, by order, and the attack ladder in full.</b> A book with two attack
            // spells in it is a mage that casts magic arrow all evening — BotStrike walks the ladder from the
            // strongest down and can only throw what the book holds, so the circles it is missing are simply
            // never seen. Through mind blast and no further: the sixth is where a caster starts making the
            // other four rangers scenery, which is the line this company is built against.
            // <b>The book is issued whole rather than assembled from this list.</b> See BotRangerBook: it
            // holds every spell of the first five circles the moment it is made, so nothing here has to
            // remember to name the next one, and BotStrike can always walk its ladder down from the
            // strongest the mana will pay for. The list stays empty on purpose — a second, half-filled book
            // built from it would be the wrong one in the pack.
            Armour = [typeof(BotRangerRobe)],

            Tools = [typeof(BotRangerBook)],

            Bandages = 50
        };

        PotionLimits[BotPotionKind.Heal] = 2;
        PotionLimits[BotPotionKind.Cure] = 2;
    }
}

/// <summary>
/// The one who keeps the other four alive, and fights only when something insists.
///
/// <para>
/// <b>Grandmaster in Healing, and the fighting skills are deliberately poor.</b> A medic who can hold his own
/// is a medic who joins in, and a company whose healer is in the front rank is a company about to lose all
/// five. See <see cref="BotClass.Role"/>: the medic role already keeps him at the back of the formation;
/// this makes it true of his instincts as well as of his station.
/// </para>
/// </summary>
public sealed class BotRangerHealer : BotRanger
{
    public override string Name => "Ranger Healer";

    public override BotRole Role => BotRole.Medic;

    public override SkillName? MainSkill => SkillName.Healing;

    public override bool Casts => true;

    /// <summary>Only when something has laid hands on him. The whole of what "he is the healer" means.</summary>
    public override bool DefendsOnly => true;

    protected override void Defaults()
    {
        Str = 60;
        Dex = 60;
        Int = 80;

        Skills =
        [
            (SkillName.Healing, 100.0),
            (SkillName.Anatomy, 100.0),
            (SkillName.Magery, 80.0),
            (SkillName.Meditation, 80.0),
            (SkillName.MagicResist, 85.0),
            (SkillName.Wrestling, 50.0)
        ];

        NeedsMeditation = true;

        StaffManaTrickle = 4;
        StaffHue = BotRegalia.RoyalRed;

        Kit = new BotKit
        {
            Staff = true,
            Reagents = 100,

            // Healing and curing only. He is not a second mage and must not become one: every point of mana
            // he spends on an attack is a bandage somebody else does not get — and BotRangers.Mend only ever
            // asks him for a mending spell, so the book being fuller than his orders costs nothing.
            Tools = [typeof(BotRangerBook)],

            Armour = [typeof(BotRangerHealerRobe)],

            // Two hundred, and they are the crown's. A grandmaster with a short supply is a surgeon with no
            // thread — the same note the Baron's hundred carries, doubled because this is his whole office.
            Bandages = 200
        };

        PotionLimits[BotPotionKind.Heal] = 3;
        PotionLimits[BotPotionKind.Cure] = 3;
    }
}
