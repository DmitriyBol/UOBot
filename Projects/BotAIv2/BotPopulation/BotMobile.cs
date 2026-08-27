using System;
using System.Collections.Generic;
using System.Diagnostics;
using Server.Items;
using Server.Logging;
using Server.Misc;
using Server.Mobiles;
using Server.Text;

namespace Server.BotAI.V2;

/// <summary>
/// An autonomous inhabitant of the shard. <b>The object all seven other subsystems were written against
/// and waiting for.</b>
///
/// <para>
/// <b>It derives from <see cref="PlayerMobile"/> rather than <c>BaseCreature</c>, and that single choice is
/// what makes this project's measure of work possible.</b> Every player system applies without being
/// reimplemented: use-based skill gain, fame and karma, banking, criminal flags, a lootable corpse. Skill
/// gain is the point — the whole decision layer values work by the skill it produces, and skill here is the
/// engine raising a number after its own check, not us deciding a number should go up. On a creature there
/// would be nothing to measure and the metric would have to be invented.
/// </para>
///
/// <para>
/// The cost is that nothing drives it: <see cref="PlayerMobile"/> has no think loop. <see cref="BotBeat"/>
/// supplies one, and <see cref="Beat"/> below is the whole of what a bot does per turn — three calls, in an
/// order that matters.
/// </para>
///
/// <para>
/// <b>Nothing here holds a <c>NetState</c>.</b> The engine tolerates that, and one consequence is load
/// bearing: <see cref="Mobile.Move"/> skips its movement throttle when the net state is null, so a bot's
/// pace is set purely by how often it is beaten. Content that assumes a connected client is the standing
/// hazard of this approach, and the reason the population starts small.
/// </para>
///
/// <para>
/// <b>Saved bots are not reused.</b> The world save will contain these — they are Mobiles like any other —
/// so the population is purged and rebuilt on every world load. See
/// <see cref="BotPopulation.PurgeSaved"/> for why that is the honest choice rather than laziness: half the
/// state a bot needs lives in objects that would have to be rebuilt anyway, and a kit handed out twice is a
/// bot with two of everything.
/// </para>
/// </summary>
public class BotMobile : PlayerMobile, IBotWilful, IBotAside
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotMobile));

    /// <summary>
    /// How much of its health a bot needs to count as help in somebody else's fight.
    ///
    /// A quarter. The first version formed companies whose founder could not fight, found nobody able, and
    /// disbanded in the same tick — over and over, because "is it alive" was the only test.
    /// </summary>
    public static double FitFraction { get; set; } = 0.25;

    /// <summary>
    /// How far a bot reckons the fight it is in when something hits it.
    ///
    /// Ten tiles, which is more than a sword's reach on purpose: a caster strikes from eight and never
    /// closes, so a bot that only counted what was next to it would be assessing the wrong fight.
    /// </summary>
    public static int NoticeRange { get; set; } = 10;

    /// <summary>Whether the population runs rather than walks. Sets the beat as well as the pace.</summary>
    public static bool Runs { get; set; }

    /// <summary>Only so that a bot which came back from a save has something to delete. See the class note.</summary>
    public BotMobile(Serial serial) : base(serial)
    {
    }

    public BotMobile()
    {
    }

    /// <summary>The mobile this bot is — itself. See <see cref="IBotSquadMember.Self"/> for why not "Body".</summary>
    public Mobile Self => this;

    /// <summary>What this bot is. Data and limits; it decides nothing. See <see cref="BotClass"/>.</summary>
    public BotClass Class { get; private set; }

    /// <summary>Where it is going, and what it has put aside to do first.</summary>
    public BotJourney Journey { get; } = new();

    /// <summary>Which quadrant this bot was in when it was last looked at, and whereabouts in it.</summary>
    private (int Map, int X, int Y) _quadWas;

    private Point3D _quadAt;

    private bool _quadSeen;

    /// <summary>
    /// Whether anything has laid a finger on this bot since it entered the quadrant it is standing in.
    ///
    /// <para>
    /// This is what makes a crossing count as evidence about the ground rather than merely as traffic over
    /// it. The order was "three <em>free</em> crossings", and free is the operative word: a bot that walked
    /// across a square while something was chewing on it has learned that the square is dangerous, not that
    /// it is fine. Without this the very ground where the fighting happens would be credited fastest, since
    /// it is also the ground bots walk over most.
    /// </para>
    /// </summary>
    private bool _quadClean;

    /// <summary>What it has resolved, feels and has learned.</summary>
    public BotResolve Resolve { get; } = new();

    /// <summary>
    /// Which squad it belongs to, or null. Held here, set by the registry — never looked up in a table
    /// keyed by serial.
    /// </summary>
    public BotSquad Squad { get; set; }

    /// <summary>What it was given at birth and cannot lose. See <see cref="BotBond"/>.</summary>
    public BotBond Bond { get; private set; }

    /// <summary>Whether it could actually take part in a fight.</summary>
    public bool AbleToFight =>
        !Deleted && Alive && Map != null && Map != Map.Internal && Hits >= HitsMax * FitFraction;

    /// <summary>
    /// When this bot is next due a turn, and whether it has ever had one. Owned by <see cref="BotBeat"/>.
    ///
    /// The flag is not decoration: a tick count of zero is a legitimate reading on hosts that pass the
    /// machine's uptime counter through, so "never beaten" cannot be spelled as "stamp is zero".
    /// </summary>
    public long DueTick { get; internal set; }

    public bool Scheduled { get; internal set; }

    /// <summary>Whether this bot is lying dead, and since when. Read by the population's reviver.</summary>
    public bool Fallen { get; private set; }

    /// <summary>Whether a refused resurrection has already been reported for this death. One line, not one a beat.</summary>
    public bool ReviveComplained { get; set; }

    /// <summary>
    /// The corpse this bot left behind, or null.
    ///
    /// Remembered at the moment of death rather than looked for afterwards: the engine hands the corpse
    /// straight to the death hook, and sweeping the world for one later would be both expensive and unable to
    /// say whose it was.
    /// </summary>
    public Corpse Remains { get; set; }

    /// <summary>
    /// Roads proved impossible since this bot last actually moved. See <see cref="BotPopulation.Rescue"/>.
    ///
    /// Reset by a single step, which is what makes it mean "cannot get anywhere" rather than "picked badly":
    /// a bot that is walking is a bot that is not stranded, whatever it has been refused.
    /// </summary>
    public int Refusals { get; set; }

    public long FellTick { get; private set; }

    /// <summary>
    /// How far along its own trade this bot is, from nought to one.
    ///
    /// <para>
    /// <b>The one number that says whether this population is going anywhere.</b> Money says what a bot has;
    /// this says whether it is becoming something. It is the share of what its class declared it was working
    /// towards that it has actually reached — the weapon it was handed included, because that skill is chosen
    /// by the roll and is as much part of the trade as the ones written down.
    /// </para>
    ///
    /// <para>
    /// Capped per skill at that skill's own target, so a bot cannot make its vector look good by being
    /// enormously better than it needed to be at one thing. A target of forty does not want fifty.
    /// </para>
    /// </summary>
    public double Progress
    {
        get
        {
            var klass = Class;

            if (klass == null)
            {
                return 0.0;
            }

            var wanted = 0.0;
            var reached = 0.0;
            var wants = klass.Skills;

            for (var i = 0; i < wants.Count; i++)
            {
                var (skill, target) = wants[i];

                if (target <= 0.0)
                {
                    continue;
                }

                wanted += target;
                reached += Math.Min(target, Skills[skill].Base);
            }

            var weapon = Bond?.Weapon;

            if (weapon != null && weapon.Value.Target > 0.0)
            {
                wanted += weapon.Value.Target;
                reached += Math.Min(weapon.Value.Target, Skills[weapon.Value.Skill].Base);
            }

            return wanted <= 0.0 ? 0.0 : reached / wanted;
        }
    }

    /// <summary>
    /// How content this bot is, from nought to one: neither bored nor short of money.
    ///
    /// Both halves are already kept by the decision layer and neither is a mood in the usual sense — boredom
    /// is what grows when nothing is happening, and need is a fact about the purse against what this bot was
    /// about to try to do. One number out of the two is for a person reading a table; the bot itself never
    /// uses it.
    /// </summary>
    public double Mood
    {
        get
        {
            // <b>A class may be content by something else, and one is.</b> Both halves below are about this
            // bot's own circumstances — how long since it worked, and whether it can afford what it was about
            // to try — and both read backwards on a bot that is paid nothing on purpose: need is nought
            // for ever because it buys almost nothing, and boredom never falls because relief comes from
            // being paid. See BotClass.Grieves. The question asked instead is about the island, which is
            // the only thing the Baron's contentment was ever meant to be about.
            if (Class is { Grieves: true })
            {
                return Grief();
            }

            var urges = Resolve.Urges;

            return Math.Clamp(1.0 - (urges.Boredom + urges.Need) / 2.0, 0.0, 1.0);
        }
    }

    /// <summary>
    /// Contentment for a class that grieves: one, less what is left standing.
    ///
    /// <para>
    /// Every one of the dead within reach whose ground has not been harrowed off the board takes a share off
    /// it, and a shard where nobody has died anywhere reads as one. The dead never decay and never fall on
    /// their own, so this only ever comes down when somebody deals with the ground — which is why one number
    /// answers both halves of what he grieves over: the deaths of others, and the squares nobody has
    /// cleared.
    /// </para>
    /// </summary>
    private double Grief()
    {
        var map = Map;

        if (map == null || map == Map.Internal)
        {
            return 1.0;
        }

        var standing = BotPeril.Unavenged(map, Location, BotPopulation.Roam);

        return Math.Clamp(1.0 - standing / (double)Math.Max(1, GriefAt), 0.0, 1.0);
    }

    /// <summary>
    /// How many unavenged dead count as complete misery.
    ///
    /// Four, and it is a scale rather than a threshold: it decides how fast contentment falls, not whether
    /// anything happens. Nothing branches on it.
    /// </summary>
    public static int GriefAt { get; set; } = 4;

    /// <summary>
    /// Makes this thing a bot of the given class: a face, a build, a trade it has started rather than
    /// finished, and the kit that goes with it.
    ///
    /// <para>
    /// <b>The kit is handed over before the weapon skill is set, and the order is the point.</b> Which blade
    /// a class ends up with is a roll made by <see cref="BotOutfit"/>, and the skill that swings it comes
    /// back on the bond — so asking the bond afterwards is the only way to train the weapon the bot actually
    /// holds. The first version set the skill from the profile and rolled the weapon separately, which is
    /// how it produced bots trained in swords carrying maces.
    /// </para>
    /// </summary>
    public void Become(BotClass klass, string name, bool female)
    {
        if (klass == null)
        {
            return;
        }

        // <b>Without this a dead bot is not a ghost, it is gone.</b> The engine's own death path reads
        // <c>Mobile.OnDeath</c> as "if this is not a player, delete it" — so a bot with the flag unset leaves a
        // corpse and is removed from the world outright, and the population quietly shrinks every time
        // something wins a fight. Nothing sets it for us: <c>PlayerMobile</c>'s constructor does not, because
        // for a real player it is set when a client attaches, and a bot never has one.
        //
        // It is also what <c>Alive</c> is computed from — <c>!Deleted && (!Player || !Body.IsGhost)</c> — so
        // with it unset a dead bot reads as alive, the revive refuses on that very check without a word, and
        // the clock skips the bot for ever because it is still marked fallen. That is the motionless bot at
        // the graveyard, and it is the same one line.
        Player = true;

        Class = klass;

        Name = name;
        Female = female;
        Body = female ? 0x191 : 0x190;
        Hue = Race.RandomSkinHue();
        HairItemID = Race.RandomHair(female);
        HairHue = Race.RandomHairHue();

        // Before anything is handed over: everything below needs somewhere to put things.
        AddItem(new Backpack { Movable = false });

        Build(klass);
        Learn(klass);

        Bond = BotOutfit.Give(this, klass);

        Clothe();
        LearnWeapon();

        Hits = HitsMax;
        Stam = StamMax;
        Mana = ManaMax;

        // <b>Said once, with the numbers, because a class that claims to be born finished is a claim and not
        // a fact until something reads it back.</b> Everything else about a bot's build can be inferred from
        // watching it work for an hour; "it arrived at Expert" cannot, and a silent failure here would look
        // exactly like a captain having an unlucky day. This shard's own rule is that the thresholds a
        // subsystem actually runs with belong in its startup lines, and a birth meant to be unlike every
        // other birth is a startup line.
        if (klass.Seasoned)
        {
            var sb = ValueStringBuilder.Create(160);

            try
            {
                Recite(ref sb, klass);

                logger.Information(
                    "{Name} was raised a seasoned {Class}, holding its trade already: {Skills}",
                    Name,
                    klass.Name,
                    sb.ToString()
                );
            }
            finally
            {
                sb.Dispose();
            }
        }
    }

    /// <summary>The skills a seasoned bot actually woke up with, read back off the bot rather than off the class.</summary>
    private void Recite(ref ValueStringBuilder sb, BotClass klass)
    {
        var wanted = klass.Skills;
        var said = 0;

        for (var i = 0; i < wanted.Count; i++)
        {
            var skill = Skills[wanted[i].Skill];

            if (skill == null)
            {
                continue;
            }

            if (said++ > 0)
            {
                sb.Append(", ");
            }

            sb.Append(skill.Info.Name);
            sb.Append(' ');
            sb.Append(skill.Base, "F1");
        }

        if (said == 0)
        {
            sb.Append("nothing at all, which is a defect");
        }
    }

    /// <summary>
    /// One turn. Three calls, and the order is the whole of the contract between the subsystems.
    ///
    /// <para>
    /// Decide first, because deciding may put a new destination at the bottom of the journey. Then take one
    /// step towards whatever the journey now holds. Then report what that step did, because two of its
    /// outcomes — proven unreachable, and getting nowhere — are facts the decision layer has to learn from,
    /// and it cannot see them any other way.
    /// </para>
    /// </summary>
    public void Beat()
    {
        if (Deleted || !Alive || Map == null || Map == Map.Internal)
        {
            return;
        }

        // Before deciding: a bot standing at a counter puts away what it is not going to spend. Cheap — a
        // count of the purse and one distance check — and it is the only moment the question can be asked,
        // because nothing here would ever choose to make the trip on its own. See BotPurse.
        BotPurse.Bank(this);

        // The crown's money, for the one class that is given some. Its own clock inside, and it costs a
        // dictionary lookup for everybody else. See BotStipend for why a second faucet is allowed once.
        BotStipend.Keep(this);

        // Goods this bot has already paid for, taken off the board. Here rather than as a piece of work, and
        // that move is the whole fix: collecting costs nothing, takes no time and goes nowhere, so priced as
        // a trade it was 8 gold a minute standing behind a rescue at 140 and lost every auction it ever
        // entered. Nessa ordered a cap and gloves on 26.08.2026, paid for both, and was still wearing cloth
        // when the shard came down. See BotAuction.Fetch.
        BotAuction.Fetch(this);

        // A horse, bought out of a trip this bot was already making and called up whenever it is on foot.
        // Both are conditions rather than events, exactly like being dressed and like banking, and both cost
        // a class flag for everybody who does not ride. See BotStable.
        BotStable.Keep(this);
        BotStable.Ride(this);

        // <b>Wearing the right thing is a condition, not an event, and it was written as an event.</b>
        // BotArms made this exact argument about weapons a week ago — "being armed was treated as an event
        // and it is a condition" — and armour was left on the old footing: a re-arm happened at birth, after
        // a death, after a shopping trip and when a delivery was fetched, and at no other moment. So a
        // cuirass off a corpse, a helm out of a company's share-out and a pair of gloves handed over by any
        // route nobody thought of all sat in the pack until the bot happened to die. Asked on a clock, every
        // one of those closes at once, including the ones not yet invented.
        Dress();

        Trickle();

        Gasp();

        // The rangers' standing order: see Watch. A no-op for every other class on the shard.
        Watch();

        Rank();

        BotWill.Decide(this);

        var result = BotWalk.Advance(this, Journey, Running);

        Cross();

        BotWill.Note(this, result);

        // A refusal is proof that nothing at that destination can be reached from here. Counted, and cleared
        // by any step at all — so this rises only for a bot that is getting nowhere in the literal sense.
        switch (result)
        {
            case BotWalkResult.Refused:
            case BotWalkResult.GaveUp:
                {
                    if (++Refusals >= BotPopulation.StrandedLimit)
                    {
                        BotPopulation.Rescue(this);
                    }

                    break;
                }
            case BotWalkResult.Stepped:
            case BotWalkResult.Arrived:
            case BotWalkResult.Improvised:
                {
                    Refusals = 0;

                    break;
                }
        }
    }

    /// <summary>
    /// The mana a caster's class gives it back, paid out on its own clock.
    ///
    /// <para>
    /// <b>Designed, documented at length, and connected to nothing.</b> <c>BotClass.ManaTrickle</c> carries a
    /// careful ruling about staves and warrior-mages, the mage's staff is hued blue and the healer's green
    /// precisely so that a watcher can tell which trickle is which — and not one line of this project ever
    /// called it. So the only mana a caster on this shard has ever had back is the engine's own regeneration,
    /// which on this era is <c>(Int + Meditation) / 2</c> with no meditation bonus, because nothing makes a
    /// bot meditate either. Cedric stood in a fight with a troll on 25.08.2026 holding <em>two</em> of fifty
    /// mana and one attack spell in a book of seven, and the log said so in as many words while every
    /// number in it was correct.
    /// </para>
    ///
    /// <para>
    /// Only while alive and only up to the pool, and deliberately not while the bot is at full mana — the
    /// point of a trickle is to get a caster back into a fight, not to be a number that ticks.
    /// </para>
    /// </summary>
    private void Trickle()
    {
        var klass = Class;

        if (klass == null || Mana >= ManaMax)
        {
            return;
        }

        if (Core.TickCount - _trickledTick < BotClass.ManaTrickleIntervalMs)
        {
            return;
        }

        _trickledTick = Core.TickCount;

        // Asked of what is actually in the hands, every time, because a caster that has drawn a blade or had
        // its staff knocked about is a caster whose trickle has changed. The class's own ruling decides what
        // holding one is worth; this only reports the fact.
        var given = klass.ManaTrickle(Weapon is BaseStaff);

        if (given > 0)
        {
            Mana = Math.Min(ManaMax, Mana + given);
        }
    }

    private long _trickledTick;

    /// <summary>
    /// The share of health at which a bot swallows a bottle whatever else it is doing.
    ///
    /// Fifteen per cent: two or three more blows. Deliberately far below <c>BotMend.Gulp</c>, which is the
    /// threshold for <em>choosing</em> to drink as part of looking after itself — this is the one for having
    /// run out of choices.
    /// </summary>
    public static double Critical { get; set; } = 0.15;

    /// <summary>
    /// A bottle at death's door, as a reflex rather than as a decision.
    ///
    /// <para>
    /// <b>The rule was already right and it was in the wrong place.</b> <c>BotMend</c> says it plainly — a
    /// potion is the only mending in this game that works while something is hitting you: a cast is destroyed
    /// by a blow, a bandage slips with every one, a bottle cannot be interrupted at all. But the only code
    /// that ever reached for one lived inside the <em>mending</em> undertaking, and mending has to win an
    /// auction on the failing rung against <c>BotFugitive</c>. When flight wins — which it often does, and
    /// should — the bot turns and runs holding two unopened bottles, and one flight in six ends in a corpse.
    /// Aldric was kicked to death by a group on 25.08.2026 without drinking either of them.
    /// </para>
    ///
    /// <para>
    /// So it is not a decision at all. This shard's own rule is that survival belongs to reflexes and not to
    /// the deciding layer — the minds are told as much in their standing instruction — and a swallow is the
    /// most reflexive act there is. It costs a share and a pack lookup on a beat, and it fires only for a bot
    /// that is about to stop existing.
    /// </para>
    /// </summary>
    /// <summary>
    /// Bots that dropped under the bar, and what came of it. Three named outcomes, not one silence.
    ///
    /// <para>
    /// <b>"Nobody drinks their potions" is three completely different faults and they all looked the same.</b>
    /// The reflex was written, correct and called every beat — and there was no way to tell whether it was
    /// never firing, firing and finding an empty pack, or firing and being refused by the engine's own
    /// cooldown. Counted per beat rather than per bot, deliberately: a bot held under the bar for ten
    /// seconds is ten readings of "still dying", which is the thing worth seeing.
    /// </para>
    /// </summary>
    public static long Gasps { get; private set; }

    public static long Drank { get; private set; }

    public static long Dry { get; private set; }

    public static long Refused { get; private set; }

    /// <summary>Swallows that only happened because the weapon was put away first.</summary>
    public static long Freed { get; private set; }

    /// <summary>Clears the potion counters. Called with the rest of the population's own.</summary>
    public static void ForgetGasps()
    {
        Gasps = 0;
        Drank = 0;
        Dry = 0;
        Refused = 0;
        Freed = 0;
    }

    public static string DescribeGasps() =>
        Gasps == 0
            ? "nobody has been under a sixth of their health"
            : $"{Gasps} times a bot fell under {Critical:P0} health: {Drank} bottles swallowed ({Freed} of them after putting the weapon away), {Dry} found no bottle in the pack, {Refused} were refused by the engine";

    /// <summary>Whether this bot is already known to be under the bar. See <see cref="Gasp"/>.</summary>
    private bool _gasping;

    private void Gasp()
    {
        if (HitsMax <= 0 || Hits > HitsMax * Critical)
        {
            _gasping = false;

            return;
        }

        // <b>Counted once per fall rather than once per beat, and the first version of this lied.</b> A beat
        // is a fifth of a second per bot: one bot lying at a tenth of its health for a minute produced three
        // hundred readings and looked, in the summary, like the whole population dying at once — 252 against
        // 17 the window before, with a single death in each. What is worth counting is how often a bot went
        // under the bar, which is an event, not how long it stayed there, which is a duration.
        if (!_gasping)
        {
            _gasping = true;
            Gasps++;
        }

        var bottle = BotMend.Bottle(Backpack, BotPotionKind.Heal);

        if (bottle == null)
        {
            Dry++;

            return;
        }

        // CanDrink carries the engine's own cooldown, so this is safe to ask on every beat: a bot that has
        // just drunk simply gets false until the delay is up.
        if (!BotMend.Swallow(this, bottle))
        {
            // <b>And the commonest refusal is a full pair of hands, which is every bot on this shard.</b>
            // The engine wants a free hand for a bottle — "You must have a free hand to drink a potion" —
            // and a bot holding a blade and a shield, a bow, or a halberd has none, ever. So the reflex fired
            // correctly, found its bottle, and was turned down every single time: 27 beats under fifteen per
            // cent in one window, 27 refusals, nought swallowed. The only class it ever worked for was the
            // brawler, who fights with his fists. Putting the weapon away for the swallow is what a person
            // does, and Rearm picks it back up on the next beat.
            if (Stow() && BotMend.Swallow(this, bottle))
            {
                Drank++;
                Freed++;

                return;
            }

            Refused++;

            return;
        }

        Drank++;

        logger.Information(
            "{Name} was at {Share:P0} and drank a bottle where it stood",
            Name,
            Hits / (double)HitsMax
        );
    }

    /// <summary>
    /// Something hit it. Everything a bot has to be *told* rather than notice happens here.
    ///
    /// <para>
    /// <b>Told, not observed, and this is the rung the first version never fired.</b> A caster strikes from
    /// eight tiles and never closes, so every test of the form "is something next to me" is a test that
    /// never fires: six bots stood in a ring while a lich killed them one at a time, and not one rung of
    /// their survival ladder noticed.
    /// </para>
    ///
    /// <para>
    /// The decision is <see cref="BotThreat"/>'s and it is binary: put the road aside and deal with this, or
    /// keep walking and hit back on the move. Standing still is not an option at any number.
    /// </para>
    /// </summary>
    public override void OnDamage(int amount, Mobile from, bool willKill)
    {
        base.OnDamage(amount, from, willKill);

        if (from == null || from == this || Deleted || !Alive || willKill)
        {
            return;
        }

        BotWill.Hurt(this);
        BotSquads.Note(this, from);

        // Where the island is dangerous, learned from the one place it cannot be guessed at. See BotPeril:
        // this hook and the death hook are the only two facts that map, and a blow costs it a dictionary
        // lookup and two additions.
        BotPeril.Struck(Map, Location);

        // The same blow against the island's standing reputation rather than against this minute's danger.
        // See BotQuad: the two maps answer different questions and are both fed from here, because this hook
        // and the death hook below are the only two places on this shard that know a bot was hurt.
        BotQuad.Struck(Map, Location, Class is BotRanger);

        // And this crossing is no longer a free one. See _quadClean.
        _quadClean = false;

        // On the ground, by order — and the half of the order that matters is the rest of the sentence: a
        // rider that has been set upon fights. Here rather than in any decision, so it happens whether or not
        // anything is thinking about this bot.
        BotStable.Throw(this);

        // And the same blow, remembered against this bot rather than against the ground. See
        // BotResolve.Beaten: it is the only honest answer to "is armour worth anything to you".
        Resolve.Bruise(Core.TickCount);

        // <b>Hitting back is a reflex and not a decision, and that distinction is worth twenty seconds of
        // being beaten.</b> Fighting back used to be a piece of work like any other — the rescue, "hitting
        // back at a harpy" — which meant it had to win an auction on the bot's next decision. For most of the
        // population that is a moment; for a bot with a mind behind it the decisions come every twenty
        // seconds, and an orc that caught one stood there hitting it for exactly that long before it turned
        // round. Nobody waits for a review to swing back.
        //
        // <b>It costs nothing, which is why it may be unconditional.</b> Combat in this engine is its own
        // timer: a bot with a combatant swings while it walks, works, flees or stands, and setting one does
        // not change what the bot is <em>doing</em>. That was the confusion the old ordering rested on — it
        // guarded this line with the same test that guards picking a fight, and the two are not the same act.
        // A bot running for its life still swings at what it passes; BotBolt clears the combatant on its own
        // beat, so flight still wins where flight was chosen.
        Warmode = true;
        Combatant = from;

        // <b>A bot whose health is going does not answer a blow by picking a fight.</b> The ladder has it on
        // Failing and something up there is getting it out — mending or running — and this reflex was quietly
        // undoing both: it squared the bot up to the largest thing in the field on every blow, which is a bot
        // that has stopped moving. That is how one of these ended up standing in a ring of three at a third of
        // its health, and the ladder's own note says why it is wrong in as many words.
        //
        // Everything below this line is <em>choosing</em> a fight, which is a decision. Hitting back at
        // whatever is already hitting you is above it, and always happens.
        // <b>The King's Rangers never do any of this, and that is the whole of their doctrine.</b> They do
        // not decide whether a fight is winnable, do not walk on while something chews on them, and do not
        // run: they turn and kill what is in front of them, and there are five of them so that this is a
        // sound rule rather than a suicidal one. Every branch below is a bot weighing whether to fight, and
        // weighing is exactly what produced a ranger jogging through a crowd being beaten the whole way.
        if (Class is BotRanger)
        {
            Engage(BotThreat.Strongest(this, BotRangers.Sight) ?? from);

            return;
        }

        if (BotLadder.Failing(this))
        {
            return;
        }

        // Already leaving. A blow landing is not news to a bot running from the thing that landed it, and the
        // reflex below would rebase its journey onto the creature — walking it back into the fight it decided
        // to leave, at the one moment it has no health to spare for the round trip.
        if (Resolve.Deed is BotBolt)
        {
            return;
        }

        var stand = BotThreat.Decide(this, NoticeRange);

        if (stand == BotStand.Nothing)
        {
            return;
        }

        // Outmatched keeps the errand: several times over is exactly when walking on beats standing. What
        // gets hit back at is then whatever is actually swinging — squaring up to the worst thing in the
        // field is not hitting back on the move, it is starting the fight that was just refused. The
        // combatant is already this attacker, set above, so there is nothing left to do here.
        if (stand != BotStand.Fight)
        {
            return;
        }

        // The strongest thing here, not whatever hit last: a graveyard's nearest resident is a skeleton, and
        // the first version's parties reliably committed to the skeleton while the lich went on casting.
        // Falling back to the attacker matters too — Strongest only counts creatures, and a bot can be hit by
        // something that is not one.
        Mobile quarry = BotThreat.Strongest(this, NoticeRange) ?? from;

        Engage(quarry);
    }

    /// <summary>
    /// Squares up to something: makes it the target and walks the body to it.
    ///
    /// Only once per quarry. Every blow would otherwise push another errand, and four blows would fill the
    /// queue with the same fight four times over.
    /// </summary>
    private void Engage(Mobile quarry)
    {
        if (quarry is not { Deleted: false, Alive: true } || quarry.Map != Map)
        {
            return;
        }

        Combatant = quarry;
        Warmode = true;

        if (!ReferenceEquals(Journey.Current?.Follow, quarry))
        {
            Journey.Interrupt(Map, quarry, BotArrival.Beside, "fight");
        }
    }

    /// <summary>
    /// A ranger looks up, sees something hostile, and goes for it. No decision, no auction, no weighing.
    ///
    /// <para>
    /// <b>By order, and it is the difference between a patrol and five bots jogging past a war.</b> Every
    /// other bot on this shard picks its fights: it asks whether the thing is worth killing, whether it can
    /// be beaten, whether something better pays. That is right for a miner and ruinous for a company whose
    /// entire duty is to walk into unread ground and find out what lives there — a ranger that walks past a
    /// hostile has failed the errand it is on, because the whole product of the errand is knowing what is
    /// out there, and what is out there is that.
    /// </para>
    ///
    /// <para>
    /// A reflex on the beat rather than an offer to the auction, for the same reason the potion is: the
    /// auction reconsiders every several seconds and a fight starts inside one of them. It costs one spatial
    /// query on the beat, and only for bots of one class.
    /// </para>
    /// </summary>
    /// <summary>
    /// Puts whatever is in the hands into the pack, so that a bottle can be drunk. True when a hand came free.
    ///
    /// <para>
    /// Both hands, because the engine's test is for a free one and a shield occupies the other: a bot that
    /// stowed only its blade would still be refused. Nothing is dropped and nothing is unbound — the items go
    /// into the bot's own pack, and <see cref="Rearm"/> puts them back on the next beat it is allowed to.
    /// </para>
    /// </summary>
    private bool Stow()
    {
        var pack = Backpack;

        if (pack == null)
        {
            return false;
        }

        var freed = false;

        for (var i = 0; i < TwoHands.Length; i++)
        {
            var held = FindItemOnLayer(TwoHands[i]);

            if (held == null)
            {
                continue;
            }

            if (pack.TryDropItem(this, held, false))
            {
                freed = true;
            }
        }

        return freed;
    }

    /// <summary>The two layers a weapon, a shield or a staff can occupy. What the engine calls a full pair of hands.</summary>
    private static readonly Layer[] TwoHands = [Layer.OneHanded, Layer.TwoHanded];

    private void Watch()
    {
        if (Class is not BotRanger || Deleted || !Alive || Map == null || Map == Map.Internal)
        {
            return;
        }

        // <b>Held until it is dead, however far that goes, by order.</b> No leash on the chase and no giving
        // up when it leaves the quadrant: a ranger that breaks off has left something alive behind it in
        // ground it is supposed to be reporting on. Nothing here re-targets while the current quarry lives,
        // which is also what stops a company re-deciding every beat and spreading itself across a field.
        if (Combatant is Mobile { Deleted: false, Alive: true } held && held.Map == Map)
        {
            return;
        }

        // The strongest rather than the nearest, and the squad's own note says why: a graveyard's nearest
        // resident is a skeleton, and companies that commit to the skeleton get killed by the lich behind it.
        var foe = BotThreat.Strongest(this, BotRangers.Sight);

        if (foe == null)
        {
            return;
        }

        // <b>The whole company takes the target, not the bot that saw it.</b> A squad's focus is what its
        // formation is built around — see BotFormation.PressRingFor: the warriors close to contact, the bow
        // holds five tiles, the staff and the surgeon hold seven. Engaging privately would put whoever
        // happened to look up into contact on his own while the other four went on walking, which is the
        // company strung out across a field that this reflex exists to prevent.
        // <b>Both, and the second is not redundant.</b> The squad's engage sets the company's focus, which is
        // what the formation is built around — but it does not make any individual bot swing, and for every
        // other class on this shard the decision layer is what does that. These five have no decision layer,
        // so three rangers stood in contact with a zombie whose "health has not moved" while the company
        // dutifully held formation around it. The company agrees on the target; this bot attacks it.
        Squad?.Engage(foe, this);

        Engage(foe);
    }

    /// <summary>
    /// It died. The bond decides what it keeps, the decision layer counts what the work came to, and the
    /// squad stops counting it as help.
    /// </summary>
    public override void OnDeath(Container c)
    {
        base.OnDeath(c);

        Fallen = true;
        FellTick = Core.TickCount;
        ReviveComplained = false;

        // Everything the bind does not cover is in there: the tools, the herbs, the bandages and the purse.
        // Nothing to go back for when the crown takes the body away. See the ranger branch below.
        Remains = Class is BotRanger ? null : c as Corpse;

        // Said plainly, every time.
        //
        // <b>A death used to be invisible unless the bot happened to be holding work.</b> The decision layer
        // logs what an undertaking came to, so a bot killed between jobs left no line anywhere — which made
        // "nobody died" and "everybody died and none of them got up" the same log. That is not a diagnosis
        // anybody can make, and it cost an argument about whether resurrection worked at all.
        logger.Information(
            "{Name} the {Class} was killed at {Where}; it should rise again in {Wait}s",
            Name,
            Class?.Name,
            Location,
            BotPopulation.ReviveMs / 1000
        );

        // Ammunition first: the corpse is the only place the count can still be trimmed from.
        BotBinding.TrimAmmunition(this, Bond, c);

        BotWill.Died(this);
        BotSquads.Leave(this);

        // The heaviest single reading the peril map ever takes, and the only unambiguous one. A bot that did
        // not come back is the evidence a captain is actually looking for.
        BotPeril.Fell(Map, Location);

        // <b>Whose death it was, because one of them is worth five of the others as evidence.</b> A Baron is
        // the best-armed thing this population can field and is sent to bad ground on purpose; ground that
        // killed him will kill anybody, and BotQuad.BaronWorth is sized so one such death is enough on its
        // own to make a square dire and raise a great hunt for it immediately.
        if (Class is BotRanger)
        {
            // <b>The body goes with them.</b> A ranger is armed and armoured by the crown at nobody's
            // expense; a corpse in gold plate lying in a field is a free suit for whoever walks past, and
            // this shard has thirty bots that walk past everything. Their gear is bound and returns with
            // them anyway, so the corpse holds nothing they need and everything somebody else would want.
            if (c is Corpse body)
            {
                // Deferred by a tick rather than deleted under the engine's feet: this runs inside OnDeath,
                // which is still holding the container it just built. Zero delay is the loop's next pass.
                Timer.DelayCall(TimeSpan.Zero, () => body.Delete());
            }

            // <b>Whether this was the last of them is asked here, while the body is still on the ground.</b>
            // The company is pruned on the population's own clock, which is a beat later and somewhere else,
            // so asking afterwards would mean asking about a square this bot is no longer standing in. One
            // left standing means one is about to die too; none means this death completed the wipe.
            BotQuad.FellRanger(Map, Location, BotRangers.Standing <= 1);
        }
        else
        {
            BotQuad.Fell(Map, Location, Class is BotBaron ? BotQuad.BaronWorth : BotQuad.DeathWorth);
        }

        Journey.Finish();

        Warmode = false;
    }

    /// <summary>
    /// Notices when this bot has walked out of one quadrant and into another, and tells the map about it.
    ///
    /// <para>
    /// Read off where the bot is standing rather than hooked into movement, because a bot changes quadrant by
    /// several routes that have nothing to do with walking — it is raised into one, it is shoved off a tile,
    /// it dies and is put back on its feet somewhere else. One test in the one place every bot passes through
    /// on every turn covers all of them, and costs two divisions and a comparison.
    /// </para>
    /// </summary>
    private void Cross()
    {
        if (Map == null || Map == Map.Internal || Deleted)
        {
            return;
        }

        var now = BotQuad.Key(Map, Location);

        if (!_quadSeen)
        {
            _quadSeen = true;
            _quadWas = now;
            _quadAt = Location;
            _quadClean = true;

            // Raised into it, which is the one arrival nobody walked. The square still counts as stood in.
            BotQuad.Crossed(Map, Location, Location);

            return;
        }

        if (now == _quadWas)
        {
            return;
        }

        // Only a crossing that nothing interrupted teaches the map anything good. A bruised one still marks
        // the square as trodden, which is the other half of what Crossed does.
        if (_quadClean)
        {
            BotQuad.Crossed(Map, _quadAt, Location);
        }
        else
        {
            BotQuad.Seen(Map, Location);
        }

        _quadWas = now;
        _quadAt = Location;
        _quadClean = true;
    }

    /// <summary>
    /// The share of its stamina a bot keeps in hand before it will run.
    ///
    /// A fifth, and there is a gap on purpose: at a bare "any stamina at all" a bot alternates between running
    /// and walking every step as the last point comes and goes.
    /// </summary>
    public static double RunAbove { get; set; } = 0.2;

    /// <summary>
    /// Whether this bot should be running <em>now</em>, as opposed to whether the population runs at all.
    ///
    /// <para>
    /// <b>The distinction was missing, and it cost more than anything else found tonight.</b> This shard is
    /// configured with <c>stamina.cannotRunWhenFatigued</c>, so a bot out of breath is refused a running step
    /// — and every step a bot took was a running step, because running was a population-wide setting read once.
    /// So a tired bot did not slow down; it stopped, permanently, and the only trace was a hundred refused
    /// steps and "made no progress". Two gatherers were found that way carrying well under half their limit
    /// with nothing wrong except zero stamina.
    /// </para>
    ///
    /// <para>
    /// Walking is always allowed here (<c>cannotWalkWhenFatigued</c> is off), so falling back to a walk is
    /// always better than not moving — and stamina comes back while walking, so the bot picks the run up again
    /// by itself.
    /// </para>
    /// </summary>
    /// <remarks>
    /// The undertaking has a say as well as the stamina, and the order is deliberate: an errand may ask to be
    /// taken at a walk — see <see cref="BotDeed.Hurries"/> — but nothing may ask a bot with no breath to run.
    /// </remarks>
    public bool Running =>
        Runs && StamMax > 0 && Stam > StamMax * RunAbove && (Resolve.Deed?.Hurries ?? true);

    /// <summary>How often the title is worked out again. Skills move slowly; this is generous.</summary>
    private const int RankEveryMs = 30000;

    private bool _ranked;

    private long _rankedTick;

    /// <summary>
    /// Keeps the rank under the name honest, the way the engine spells it: "Alden, Grandmaster Miner".
    ///
    /// <para>
    /// <b>Written through <see cref="BotRank"/> rather than into <see cref="Mobile.Title"/>, and the
    /// difference is the whole of this note.</b> The first version put the skill title straight into
    /// <c>Title</c>, which reads as the obvious thing to do and is two bugs at once. <c>Title</c> means
    /// <em>custom</em> title to this engine: <c>AddNameProperties</c> glues it to the name with a bare space,
    /// so a bot in the world read "Perri Apprentice Swordsman" while a player reads "Arold, Grandmaster
    /// Alchemist" — and worse, <c>Titles.ComputeTitle</c> takes a non-empty <c>Title</c> as a signal to
    /// <em>skip</em> the skill-title branch entirely, which is the one branch that writes the comma. Setting
    /// it therefore both mangled the punctuation and switched off the code that would have got it right.
    /// </para>
    ///
    /// <para>
    /// So the rank is no longer stored anywhere the engine will second-guess. A bot is a
    /// <c>PlayerMobile</c>, so leaving <c>Title</c> alone makes it render exactly as a player does — nothing
    /// glued into the world label, and the engine's own comma on the paperdoll — and the rank is kept here,
    /// in the bot's own field, for the dashboard and the log to read.
    /// </para>
    ///
    /// <para>
    /// On a clock rather than every beat: it walks the whole skill list, and a title cannot change faster than
    /// a skill does.
    /// </para>
    /// </summary>
    private void Rank()
    {
        var now = Core.TickCount;

        if (_ranked && now - _rankedTick < RankEveryMs)
        {
            return;
        }

        _ranked = true;
        _rankedTick = now;

        BotRank = Titles.GetSkillTitle(this);
    }

    /// <summary>
    /// What this bot's highest skill would call it — "Grandmaster Miner" — kept apart from
    /// <see cref="Mobile.Title"/> so the engine goes on formatting the name its own way. See <see cref="Rank"/>.
    /// </summary>
    public string BotRank { get; private set; }

    /// <summary>
    /// Whether a language model is choosing this one's work. Set by BotMindAI when it takes a body.
    ///
    /// <para>
    /// <b>A flag rather than a title, and that is the whole point of it.</b> The model's name used to be
    /// written into <see cref="Mobile.Title"/> so that a thinking bot could be told from the other thirteen
    /// — which put "of qwen3.5:9b" over its head in the world, where nothing else wears a maker's mark, and
    /// meanwhile stopped the engine printing the skill title that belongs there. Which bot is thinking is a
    /// fact for the dashboard, not for the field, so it lives here and is shown as "(AI)" in the one place
    /// that asks.
    /// </para>
    ///
    /// <para>
    /// Held on this side of the wall because BotAIv2 knows nothing of BotMindAI and must not: the minds are a
    /// separate assembly that depends on this one, never the other way round.
    /// </para>
    /// </summary>
    public bool Minded { get; set; }

    /// <summary>
    /// When this bot last came back from the woods with herbs. See <see cref="BotClass.HerbIntervalMs"/>.
    ///
    /// A flag beside the tick, never "is the tick zero": on some hosts the counter is the machine's uptime
    /// passed straight through, so zero is a legitimate reading and useless as "never".
    /// </summary>
    public bool Herbed { get; set; }

    public long HerbTick { get; set; }

    /// <summary>
    /// When this bot last got a piece for nothing. See <see cref="BotClass.FreeCraftIntervalMs"/>.
    ///
    /// A flag beside the tick for the same reason as the herbs: on some hosts a tick count starts at the
    /// machine's uptime, so nought is a legitimate reading and useless as "never".
    /// </summary>
    public bool Crafted { get; set; }

    public long CraftTick { get; set; }

    /// <summary>
    /// A bot does not put its weapon down to cast.
    ///
    /// <para>
    /// <b>The engine empties both hands on every cast, and for a person that is right.</b>
    /// <c>Spell.Cast</c> calls this, it drops whatever is held into the pack, and a player picks it back up
    /// afterwards because a player can see that it happened. A bot cannot: the only thing that re-armed it
    /// was a check on a five-second clock, so a caster in a fight spent most of its time standing there with
    /// empty hands, and every one of those re-arms cost an item move and a line in the log. It is also the
    /// same fault the first version had.
    /// </para>
    ///
    /// <para>
    /// <b>Overridden rather than undone afterwards, which is the difference between a rule and a tidy-up.</b>
    /// Putting the staff back the instant the spell was away worked and was still a repair: the hands emptied
    /// several times a minute and something had to notice each time. Nothing empties them now. The engine
    /// clears hands as a courtesy to the caster — nothing later checks that they are empty, and casting
    /// proceeds exactly as it did — and it is overridable precisely so that a mobile which cannot manage its
    /// own kit may decline. <c>Changeling</c> declines it too.
    /// </para>
    /// </summary>
    public override void ClearHands()
    {
    }

    /// <summary>Whether the one line below has been said. Once is evidence; every time is noise.</summary>
    private static bool _saidDisarmed;

    /// <summary>How long between saying what a bot put back on. See Rearm.</summary>
    public static int SayEveryMs { get; set; } = 60000;

    private static bool _saidWorn;

    private static long _wornTick;

    /// <summary>
    /// Something has come off this bot. Said once, with the call stack, when it was a weapon in a hand.
    ///
    /// <para>
    /// <b>A trap rather than a rule, and it is here because the question could not be answered by reading.</b>
    /// Casters keep losing the staff they were born with — the pack has it back a few seconds later and the
    /// re-arm puts it on again, over and over, and it happens in town. Nothing in this project takes a held
    /// weapon off except <see cref="Draw"/>, which only a class that closes ever calls and no caster is. So
    /// the hand is being emptied by the engine, and the one thing that reliably says which of the engine's
    /// several ways it was is the stack at the moment it happens.
    /// </para>
    ///
    /// <para>
    /// Once for the life of the shard. A stack trace is expensive and this is a question, not a measurement:
    /// the first answer is the whole answer, and asking again costs the population nothing but frames.
    /// </para>
    /// </summary>
    public override void OnItemRemoved(Item item)
    {
        base.OnItemRemoved(item);

        // A bot being deleted takes its kit with it, and the engine reports every piece of it through here.
        // That is not a disarming, it is a funeral — and the boot-time purge of last session's bots fired
        // this thirty-three times before anything had happened at all.
        if (Deleted || !Alive || World.Loading)
        {
            return;
        }

        if (_saidDisarmed || item is not BaseWeapon || item.Layer is not (Layer.OneHanded or Layer.TwoHanded))
        {
            return;
        }

        _saidDisarmed = true;

        logger.Information(
            "{Name} has had its {Item} taken out of its hands. Whatever did it is here: {Where}",
            Name,
            item.GetType().Name,
            new StackTrace(false)
        );
    }

    /// <summary>Back on its feet: the kit comes back, and so does the health to use it.</summary>
    public override void OnAfterResurrect()
    {
        base.OnAfterResurrect();

        Fallen = false;

        BotBinding.Restore(this, Bond);

        // What death kept, and what it handed back, are both in the pack. Put them on before walking off.
        Rearm();

        Hits = HitsMax;
        Stam = StamMax;
        Mana = ManaMax;
    }

    /// <summary>
    /// Gone for good. Every subsystem that counts this bot is told, because a count that is never released
    /// does not look wrong — it looks like a busier shard.
    /// </summary>
    public override void OnAfterDelete()
    {
        base.OnAfterDelete();

        BotWill.Forget(this);
        BotSquads.Leave(this);
        BotPopulation.Forget(this);

        Journey.Finish();

        Bond = null;
        Class = null;
        Squad = null;
    }

    /// <summary>
    /// Somebody wants this tile. Whether to give it up is the squad's arithmetic, not this bot's opinion —
    /// a blade holds its place and a mage does not.
    /// </summary>
    public bool StepAsideFor(Mobile asker)
    {
        if (asker == null || Deleted || !Alive || !BotSquads.ShouldYield(this, asker))
        {
            return false;
        }

        var away = BotSquads.YieldAwayFrom(this, asker);

        Direction = away;

        return Move(away);
    }

    /// <summary>
    /// Stats as the class asks for them, held to the cap this character actually has.
    ///
    /// Over-cap raw stats are not an error the engine refuses; they are a character who can never gain
    /// another point and whose sheet does not add up. So the excess is taken back proportionally rather than
    /// clipped off whichever stat happened to be largest.
    /// </summary>
    private void Build(BotClass klass)
    {
        var str = Math.Max(1, klass.Str);
        var dex = Math.Max(1, klass.Dex);
        var pow = Math.Max(1, klass.Int);

        var cap = StatCap;
        var total = str + dex + pow;

        if (cap > 0 && total > cap)
        {
            var scale = cap / (double)total;

            str = Math.Max(1, (int)(str * scale));
            dex = Math.Max(1, (int)(dex * scale));
            pow = Math.Max(1, (int)(pow * scale));
        }

        RawStr = str;
        RawDex = dex;
        RawInt = pow;
    }

    /// <summary>
    /// The hundred points of character creation, spent in the order the class's own skills matter to it.
    ///
    /// <para>
    /// Fifty, thirty and twenty — and never above what the class is aiming for, because a target of forty
    /// does not want fifty. <b>Deliberately well below the targets:</b> the gap is what the bot is motivated
    /// by, and a bot that begins at its target has nothing left to want. The first version's flat "forty per
    /// cent of every target" gave everybody about twenty-five in everything, which is the number at which no
    /// trade works at all — a smith at twenty-six Mining smelts two piles in a hundred and burns the rest.
    /// They were not novices, they were unemployable.
    /// </para>
    /// </summary>
    private void Learn(BotClass klass)
    {
        var wanted = klass.Skills;

        if (wanted == null || wanted.Count == 0)
        {
            return;
        }

        // Paired with the position each skill was declared at, so that equal targets break towards the one
        // the class names first.
        //
        // <b>A tie here is not a curiosity, it is the shape of a measured defect.</b> The first version put
        // Swords and Tactics both at seventy and let a dictionary's enumeration order decide which got the
        // fifty: half the warriors on the shard spent their best points on Tactics and could not hit
        // anybody. The class files have carried warnings about it ever since — and the sort below was still
        // an unstable one, so the warnings were the only protection. Now the order a class writes its
        // skills in is the tie-break, which is the one thing a class can actually say about it.
        List<(SkillName Skill, double Target, int Declared)> ordered = [];

        for (var i = 0; i < wanted.Count; i++)
        {
            ordered.Add((wanted[i].Skill, wanted[i].Target, i));
        }

        ordered.Sort(
            static (a, b) =>
            {
                var byTarget = b.Target.CompareTo(a.Target);

                return byTarget != 0 ? byTarget : a.Declared.CompareTo(b.Declared);
            }
        );

        for (var i = 0; i < ordered.Count; i++)
        {
            var (skill, target, _) = ordered[i];

            // <b>A seasoned class is given what it declares, and the ladder is skipped rather than raised.</b>
            // Handing the captain a bigger allowance would have been the smaller edit and it would have been
            // the wrong one: the allowance is a shape — most of the points to one skill, a little to a
            // second, a token to a third — and a class that has to hold two weapons at the same standing
            // cannot be described by it at any size. See BotClass.Seasoned for why exactly one class is
            // allowed to say this.
            if (klass.Seasoned)
            {
                Skills[skill].Base = target;

                continue;
            }

            var allowance = i < StartingAllowance.Length ? StartingAllowance[i] : 0.0;

            Skills[skill].Base = Math.Min(target, allowance);
        }
    }

    private static readonly double[] StartingAllowance = [50.0, 30.0, 20.0];

    /// <summary>
    /// The skill that swings whatever the roll actually handed this bot, taken from the bond.
    ///
    /// Ten archers in the first version spent their whole lives stabbing skeletons with the daggers they had
    /// been handed first, carrying the bows they had trained for. One fact, asked in one place.
    /// </summary>
    private void LearnWeapon()
    {
        var weapon = Bond?.Weapon;

        if (weapon == null)
        {
            return;
        }

        // The class's own skill list deliberately leaves the weapon skill out — the roll settles it — so
        // there is nothing here to overwrite. Held to the option's own target for the same reason the rest
        // are: a target of forty does not want fifty.
        var chosen = weapon.Value;

        // The same exception, and it has to be made twice because the weapon skill is set from the bond
        // rather than from the class's list. Setting it in one place and not the other is how a captain
        // would have ended up an Expert swordsman carrying a bow it could barely draw — which is the exact
        // shape of the defect this whole method was written to cure.
        Skills[chosen.Skill].Base = Class is { Seasoned: true }
            ? chosen.Target
            : Math.Min(chosen.Target, StartingAllowance[0]);
    }

    /// <summary>
    /// Clothes, bound like everything else the world hands out.
    ///
    /// Bound rather than bought, and not for tidiness: an unbound shirt is lootable, and a population that
    /// dies twice is a population standing about naked. It costs nothing to make the cosmetic layer survive
    /// death, and it saves a whole class of "why does this look broken" question.
    /// </summary>
    /// <summary>
    /// How often a bot looks at what it is carrying against what it is wearing.
    ///
    /// Fifteen seconds. The check walks the pack, which is thirty-odd things, so it is not free — but it is
    /// nothing beside the movement budget, and the alternative is the state this replaces: a piece of armour
    /// bought, paid for, delivered and never worn because the one moment that would have noticed had already
    /// gone by.
    /// </summary>
    public static int DressEveryMs { get; set; } = 15000;

    private bool _dressed;

    private long _dressedTick;

    /// <summary>Puts on anything in the pack that belongs on the body, on a clock. See <see cref="Rearm"/>.</summary>
    private void Dress()
    {
        var now = Core.TickCount;

        // Seeded from a real tick rather than compared against a nought this field never held.
        if (!_dressed)
        {
            _dressed = true;
            _dressedTick = now;

            return;
        }

        if (now - _dressedTick < DressEveryMs)
        {
            return;
        }

        _dressedTick = now;

        Rearm();
    }

    private void Clothe()
    {
        Wear(new Shirt(Utility.RandomDyedHue()));
        Wear(new LongPants(Utility.RandomDyedHue()));
        Wear(new Boots());
    }

    /// <summary>
    /// Puts back on whatever is in the pack and belongs in a hand or on a body.
    ///
    /// <para>
    /// <b>Nothing did this, and death made it matter.</b> Bound gear survives being killed, but it survives
    /// into the <em>pack</em> — the engine keeps it with the owner, not on the owner — and going back for your
    /// own corpse fills the pack with the rest. So a bot rose, walked to its body, picked up its sword,
    /// hammer and bandages, and then went hunting with all of it stowed and its fists out. It lost the next
    /// fight, and the one after that, which is precisely what was seen: bots trotting back and forth and
    /// being killed.
    /// </para>
    ///
    /// <para>
    /// Two passes, and the order is the same one birth uses: the engine refuses a two-handed thing while
    /// anything at all is in the other hand, so a bow or a staff goes on before a blade. Getting that
    /// backwards is what once left ten archers stabbing skeletons with knives while carrying the bows they
    /// had trained for.
    /// </para>
    /// </summary>
    public int Rearm()
    {
        var pack = Backpack;

        if (pack == null || Deleted || !Alive)
        {
            return 0;
        }

        // <b>Never mid-cast.</b> Putting anything on while a spell is going up calls
        // Spell.OnCasterEquipping, which disturbs it — the engine says so in one line — so a re-arm on a
        // fixed clock is a mage's spell broken on that same clock. Orin threw ten spells in thirty-six
        // seconds on 26.08.2026 while this fired every five, and a caster interrupted every five seconds is
        // a caster that never lands the slow ones at all. Whatever is in the pack will still be there when
        // the spell is off; the same reasoning, and nearly the same sentence, is already in BotWalk.
        if (Spell != null)
        {
            return 0;
        }

        var worn = 0;
        var refused = 0;

        // <b>Layers already spoken for, so that two things never contend for one place.</b> This is the whole
        // of the fix: the loop below used to try every wearable thing in the pack against the body, and where
        // two of them wanted the same hand the second was simply refused — which is harmless in itself and
        // was not the problem. The problem was that <em>which one got there first depended on the order of
        // the pack</em>, and the pack is reordered constantly. Orin alternated between a SkinningKnife and a
        // WarMace every five seconds for as long as anybody watched, on 26.08.2026, putting one on and being
        // refused the other, then the reverse. From outside it is a bot that buys a weapon and does not keep
        // it on.
        HashSet<Layer> taken = [];

        for (var i = 0; i < Items.Count; i++)
        {
            taken.Add(Items[i].Layer);
        }

        // What was in the hands before this ran. Said in the line below because "put on QuarterStaff" every
        // five seconds is either a bot that keeps losing its staff or a bot that never had it on, and those
        // want opposite fixes — and the line as written could not tell them apart.
        var held = FindItemOnLayer(Layer.TwoHanded) ?? FindItemOnLayer(Layer.OneHanded);

        // <b>What went on, by name, and what would not.</b> The line used to say "put 1 things back on" and
        // nothing else, which is a count where the only useful fact is an identity: a bot that puts one thing
        // on every five seconds for an hour — Joss did it ninety-two times on 26.08.2026 — is either wearing
        // ninety-two different things or losing the same one ninety-two times, and those are opposite
        // problems. A number cannot tell them apart and a name can.
        using var put = ValueStringBuilder.Create();
        using var left = ValueStringBuilder.Create();

        // A snapshot: equipping moves things out of the pack, which mutates the list being read.
        List<Item> carried = [.. pack.Items];

        // <b>A hand busy with something lesser is a two-handed weapon that can never go on.</b> The engine
        // refuses anything two-handed while the other hand holds anything at all, and Upgrade deliberately
        // will not touch a hand — so a bow, a staff or a halberd in the pack simply stayed there, refused on
        // every pass, for as long as whatever was in the hand stayed there. Nothing in this method could
        // ever have got it out.
        Unhand(taken, carried);


        for (var pass = 0; pass < 2; pass++)
        {
            for (var i = 0; i < carried.Count; i++)
            {
                var item = carried[i];

                if (item is not (BaseWeapon or BaseArmor or BaseClothing) || item.Deleted || item.Parent != pack)
                {
                    continue;
                }

                if (item.Layer == Layer.TwoHanded != (pass == 0))
                {
                    continue;
                }

                // <b>Somewhere already covered, which is not the same as somewhere covered well enough.</b>
                // This used to skip outright, so a bot that bought a leather skirt while wearing cloth
                // trousers carried the leather home and never put it on — the slot was busy, and busy was the
                // whole of the question being asked. Joss did exactly that, and it is the reason a population
                // that orders armour, pays for it and collects it can still be walking about in cloth.
                //
                // Asked properly: is what is in the pack better than what is on the body. Better is the
                // engine's own ArmorRating and nothing of ours — see Guards — so leather beats cloth because
                // leather stops blows and cloth does not, rather than because somebody ranked them.
                if (taken.Contains(item.Layer) && !Upgrade(item.Layer, carried, pass))
                {
                    continue;
                }

                // Both hands are busy. A one-handed thing cannot go on over a two-handed one and never could,
                // so trying is not a refusal to report — it is arithmetic, and putting it in the log as
                // "could not wear" would be inventing a problem for the next reader to chase.
                //
                // <b>The hand, and only the hand, and getting that wrong locked half the population out of
                // armour entirely.</b> This read "anything that is not two-handed", which is true of a helm,
                // a gorget, vambraces, gauntlets, a cuirass, greaves and a cloak — so every bot holding a
                // bow, a staff or a halberd skipped every piece of armour in its own pack, for ever, and the
                // only bots ever seen putting anything on were the ones carrying a sword. Fifteen of the
                // thirty-four on this shard fight two-handed, including the Baron in the plate he was issued
                // and every archer the armourer has ever filled an order for. Nothing looked wrong: the
                // goods arrived, the order was paid, the market said so, and the piece sat in the pack.
                //
                // A guard written for one layer and applied to all of them. It cost from the first day the
                // population had any armour to buy.
                if (item.Layer == Layer.OneHanded && taken.Contains(Layer.TwoHanded))
                {
                    continue;
                }

                // The best of what wants that place, rather than whichever the pack happened to offer first.
                var choice = Pick(carried, item.Layer, pass);

                if (choice == null)
                {
                    continue;
                }

                if (EquipItem(choice))
                {
                    worn++;
                    taken.Add(choice.Layer);

                    if (put.Length > 0)
                    {
                        put.Append(", ");
                    }

                    put.Append(choice.GetType().Name);

                    continue;
                }

                // Refused, and by far the likeliest reason is that the layer is taken — Mobile.CheckEquip
                // turns any conflict into a bare false. Worth counting: a pack full of armour a bot will
                // never wear because it is already wearing something there is a pack full of merchandise,
                // and nothing anywhere said so.
                refused++;

                if (left.Length > 0)
                {
                    left.Append(", ");
                }

                left.Append(choice.GetType().Name);
            }
        }

        // <b>Said at most once a minute for the whole population.</b> It was one line per re-arm, and once
        // casting turned out to disarm its own caster that came to a hundred lines an hour of a bot picking
        // its staff back up — which is not news, it is the trade working. What the line is for is the case
        // where something keeps coming off and nobody knows what; one a minute still shows that and does not
        // bury the log while it does.
        // Counted before the line is throttled, and that order is the point. See BotArms.Dressing.
        BotArms.Dressing(worn, refused);

        var now = Core.TickCount;

        if ((worn > 0 || refused > 0) && (!_saidWorn || now - _wornTick >= SayEveryMs))
        {
            _saidWorn = true;
            _wornTick = now;

            logger.Information(
                "{Name} was holding {Held} and put on {Put}{Left}",
                Name,
                held?.GetType().Name ?? "nothing",
                worn > 0 ? put.ToString() : "nothing",
                refused > 0 ? $" and could not wear {left.ToString()}" : ""
            );
        }

        return worn;
    }

    /// <summary>
    /// Empties the one hand when the pack holds a two-handed weapon that outranks what is in it.
    ///
    /// <para>
    /// <b>Judged by rank and not by damage, which is the whole of why it is safe.</b> A bot fighting with the
    /// weapon its birth roll settled on is holding rank two and nothing displaces it. A bot holding a tool —
    /// rank minus one — is holding something that is not a weapon at all, and a bot holding somebody else's
    /// loot is at rank one. So this only ever fires for a hand that is occupied by something the bot was
    /// never meant to be fighting with, which is exactly the state it was written for.
    /// </para>
    ///
    /// <para>
    /// The old piece goes into the pack rather than on the ground, and nothing happens at all if the pack
    /// will not take it — the same rule <see cref="Draw"/> and <see cref="Upgrade"/> both keep, and for the
    /// same reason: a bot that dropped its knife in a field has been made permanently poorer by a temporary
    /// problem.
    /// </para>
    /// </summary>
    private void Unhand(HashSet<Layer> taken, List<Item> carried)
    {
        if (!taken.Contains(Layer.OneHanded) || taken.Contains(Layer.TwoHanded))
        {
            return;
        }

        var wanted = Pick(carried, Layer.TwoHanded, 0);

        if (wanted == null)
        {
            return;
        }

        var held = FindItemOnLayer(Layer.OneHanded);

        if (held == null || Rank(held) >= Rank(wanted))
        {
            return;
        }

        var pack = Backpack;

        if (pack == null || !pack.TryDropItem(this, held, false))
        {
            return;
        }

        taken.Remove(Layer.OneHanded);

        logger.Information(
            "{Name} put {Held} away so that both hands were free for {Wanted}",
            Name,
            held.GetType().Name,
            wanted.GetType().Name
        );
    }

    /// <summary>
    /// Of everything in the pack that wants one place on the body, the one this bot should actually be
    /// wearing there.
    ///
    /// <para>
    /// <b>The weapon the birth roll settled on outranks everything, and a tool of the trade comes last.</b>
    /// A skinning knife is a <c>BaseWeapon</c> as far as the engine is concerned — every knife is — so a
    /// re-arm that took the first weapon it found would put a butcher's knife in the hand of an archer and
    /// call it armed. The knife is for carving and something else will pick it up when there is something to
    /// carve; what belongs in the hand between times is what the bot fights with.
    /// </para>
    /// </summary>
    private Item Pick(List<Item> carried, Layer layer, int pass)
    {
        Item best = null;
        var bestRank = -1;

        for (var i = 0; i < carried.Count; i++)
        {
            var item = carried[i];

            if (item is not (BaseWeapon or BaseArmor or BaseClothing) || item.Deleted || item.Parent != Backpack)
            {
                continue;
            }

            if (item.Layer != layer || item.Layer == Layer.TwoHanded != (pass == 0))
            {
                continue;
            }

            // <b>Whether this body may wear it at all, asked before anything is weighed.</b> Everything else
            // in this method decides which of several things is best; this decides whether a thing is a
            // candidate. Vance the archer looted a pair of bone vambraces off a skeleton on 27.08.2026 and
            // tried them on every fifteen seconds for the rest of the session: they ask forty Strength and
            // an archer is built with thirty-five. Nothing was wrong anywhere — the engine refused, correctly
            // and quietly, and the only trace was a count I had added an hour earlier.
            if (!Suits(item))
            {
                Misfits++;

                continue;
            }

            var rank = Rank(item);

            // A tool of the trade, and not the thing this bot fights with. It is not a candidate at all —
            // see Rank.
            if (rank < 0 || rank <= bestRank)
            {
                continue;
            }

            best = item;
            bestRank = rank;
        }

        return best;
    }

    /// <summary>
    /// Whether this body may wear this thing at all: the right sex for it, and strong, quick and clever
    /// enough to carry it.
    ///
    /// <para>
    /// <b>Read off the object, never from a list of item names.</b> Every number here is the engine's own,
    /// asked of the very item being considered, so a piece that is re-tempered or made of a different metal
    /// answers for itself and nothing here has to be told. The engine's <c>CanEquip</c> would have been the
    /// tidier call and it cannot be used: its base also refuses anything whose layer is occupied, which is
    /// exactly the case <see cref="Upgrade"/> exists to handle — asked there it would refuse every
    /// improvement a bot ever tried to make.
    /// </para>
    ///
    /// <para>
    /// It is a <em>candidacy</em> test and not a preference. A thing that fails it is not a worse choice than
    /// what the bot is wearing; it is not a choice, and treating it as one is how a bot spends its afternoon
    /// being refused.
    /// </para>
    /// </summary>
    private bool Suits(Item item) =>
        item switch
        {
            BaseArmor armour =>
                (Female ? armour.AllowFemaleWearer : armour.AllowMaleWearer)
                && Str >= armour.StrRequirement
                && Dex >= armour.DexRequirement
                && Int >= armour.IntRequirement,
            BaseClothing clothing =>
                (Female ? clothing.AllowFemaleWearer : clothing.AllowMaleWearer)
                && Str >= clothing.StrRequirement,
            BaseWeapon weapon => Str >= weapon.StrRequirement,
            _ => true
        };

    /// <summary>
    /// Things passed over because this body could not wear them, across the whole population.
    ///
    /// A named number, because "the bot has nothing for its arms" and "the bot is carrying vambraces it will
    /// never be strong enough for" are different facts and were the same silence.
    /// </summary>
    public static long Misfits { get; private set; }

    /// <summary>
    /// How much a thing stops, as the engine reckons it. Cloth stops nothing, which is the point.
    ///
    /// Read off the item rather than off a catalogue of kinds: two pieces of the same sort wear differently,
    /// and the one being compared here is a real object with a real remaining life.
    /// </summary>
    private static double Guards(Item item) => item is BaseArmor armour ? armour.ArmorRating : 0.0;

    /// <summary>
    /// Takes off what is covering a place when the pack holds something that covers it better, so the better
    /// thing can go on.
    ///
    /// <para>
    /// <b>Weapons are not swapped on a number and armour is.</b> What a bot fights with was settled by its
    /// birth roll and by its class; a piece of armour is simply the best one it happens to own, and there is
    /// no reason to prefer the older. So this only ever moves armour and clothing, and only for something
    /// that stops strictly more — which also makes it stable, because the piece that came off stops less and
    /// will not swap itself back.
    /// </para>
    ///
    /// <para>
    /// The old piece goes into the pack rather than to the ground, and nothing happens at all if the pack
    /// will not take it: a bot that dropped its cuirass in a field to put on a better one has been made
    /// permanently worse by a temporary problem. The same rule <see cref="Draw"/> keeps about weapons.
    /// </para>
    /// </summary>
    private bool Upgrade(Layer where, List<Item> carried, int pass)
    {
        if (where is Layer.OneHanded or Layer.TwoHanded)
        {
            return false;
        }

        var worn = FindItemOnLayer(where);

        if (worn is not (BaseArmor or BaseClothing))
        {
            return false;
        }

        var choice = Pick(carried, where, pass);

        if (choice == null || Guards(choice) <= Guards(worn))
        {
            return false;
        }

        var pack = Backpack;

        if (pack == null || !pack.TryDropItem(this, worn, false))
        {
            return false;
        }

        logger.Information(
            "{Name} took off {Old} for {New}, which stops {Guard:F0} against {Was:F0}",
            Name,
            worn.GetType().Name,
            choice.GetType().Name,
            Guards(choice),
            Guards(worn)
        );

        return true;
    }

    /// <summary>
    /// What the bot fights with first, then anything else that goes there; a tool of the trade, never.
    ///
    /// <para>
    /// <b>A skinning knife comes out to skin something and at no other time.</b> Every knife is a
    /// <c>BaseWeapon</c> as far as the engine is concerned, so a re-arm that took the first weapon it found
    /// in the pack armed an archer with a butcher's knife and called the job done. Ranking the tool last was
    /// not enough and would have been the wrong rule anyway: a tool is not a poor weapon, it is not a weapon,
    /// and the carving code takes it out of the pack itself when there is a corpse — it never wanted it in a
    /// hand in the first place.
    /// </para>
    ///
    /// <para>
    /// Unless the birth roll handed this bot that very thing to fight with, which is asked first and settles
    /// it: what a bot fights with is a fact about the bot, not about the item's usual job.
    /// </para>
    /// </summary>
    /// <returns>Two for the bot's own weapon, one for anything else wearable, minus one for a tool.</returns>
    private int Rank(Item item)
    {
        var kind = item.GetType();

        if (Bond?.Weapon?.Weapon == kind)
        {
            return 2;
        }

        var tools = BotOutfit.ToolsFor(Class);

        for (var i = 0; i < tools.Count; i++)
        {
            if (tools[i] == kind)
            {
                return -1;
            }
        }

        return 1;
    }

    /// <summary>
    /// Swaps between the two weapons a bot is carrying: the one it shoots with and the one it swings.
    ///
    /// <para>
    /// <b>Nothing did this, and the sidearm was decorative because of it.</b> Every ranged class on the
    /// shard is handed a second weapon at birth — <c>BotOutfit</c> puts it straight into the pack — and no
    /// line of code has ever taken it out again. An archer with something chewing on it backs away holding a
    /// bow it cannot fire at that distance, and the dagger it was given for exactly this rides in its pack
    /// until it dies. That was survivable for an archer, whose answer is to be elsewhere. It is not
    /// survivable for a class whose answer is to stand.
    /// </para>
    ///
    /// <para>
    /// The old weapon goes into the pack rather than to the ground, so the swap is reversible and costs
    /// nothing but the moment. If the pack will not take it, nothing happens at all: a bot that has dropped
    /// its bow in a field to draw a sword has been made permanently worse by a temporary problem.
    /// </para>
    /// </summary>
    /// <param name="melee">True to draw the blade, false to bring the bow back up.</param>
    /// <returns>Whether the bot is now holding the sort of weapon asked for.</returns>
    public bool Draw(bool melee)
    {
        var pack = Backpack;

        if (pack == null || Deleted || !Alive)
        {
            return false;
        }

        // Fists answer this too, and they answer it wrongly: they are a BaseMeleeWeapon the bot does not
        // own, so they read as "already holding a blade" and would stop an unarmed bot ever drawing one.
        var held = Weapon as Item;

        if (held != null && held.Parent != this)
        {
            held = null;
        }

        if (held is BaseWeapon inHand && inHand is BaseRanged != melee)
        {
            return true;
        }

        BaseWeapon wanted = null;

        // A snapshot: equipping moves things out of the pack, which mutates the list being read. Same reason
        // as Rearm's.
        List<Item> carried = [.. pack.Items];

        // <b>By rank, and never "the first weapon in the pack".</b> This is the same defect Rearm was fixed
        // for and it was left standing here because the two were written apart: a skinning knife is a
        // BaseWeapon as far as the engine is concerned, and a knife is not ranged, so the first thing this
        // found when a captain closed to arm's length was the butcher's tool. Aldric spent 27.08.2026
        // holding one — and because a two-handed bow cannot go on over anything at all, the knife then
        // locked him out of the weapon he exists to shoot, permanently, three refusals every fifteen
        // seconds. See Rank: a tool is not a poor weapon, it is not a weapon.
        var bestRank = 0;

        for (var i = 0; i < carried.Count; i++)
        {
            if (carried[i] is not BaseWeapon weapon || weapon is BaseRanged != melee)
            {
                continue;
            }

            var rank = Rank(weapon);

            if (rank <= 0 || rank <= bestRank)
            {
                continue;
            }

            bestRank = rank;
            wanted = weapon;
        }

        if (wanted == null)
        {
            return false;
        }

        if (held != null && !pack.TryDropItem(this, held, false))
        {
            return false;
        }

        if (EquipItem(wanted))
        {
            return true;
        }

        // Put back what was taken off. Failing to re-equip leaves the bot bare-handed, which BotArms will
        // notice and mend on its own beat — but it should not be this method that caused it.
        if (held != null)
        {
            EquipItem(held);
        }

        return false;
    }

    private void Wear(Item item)
    {
        if (item == null)
        {
            return;
        }

        if (Bond != null)
        {
            BotBinding.Bind(item, Bond);
        }

        if (EquipItem(item))
        {
            return;
        }

        var pack = Backpack;

        if (pack == null)
        {
            item.Delete();

            return;
        }

        pack.DropItem(item);
    }

    public override string ToString() =>
        Class == null ? base.ToString() : $"{Name} the {Class.Name}";

    /// <summary>
    /// Written so a bot that ends up in a save can be read back and deleted, and for nothing else. Nothing
    /// about a bot is worth keeping: the population is rebuilt from its own file every world load, which is
    /// what makes "who exists" a question answered by configuration rather than by a save migration.
    /// </summary>
    public override void Serialize(IGenericWriter writer)
    {
        base.Serialize(writer);

        writer.Write(0);
    }

    public override void Deserialize(IGenericReader reader)
    {
        base.Deserialize(reader);

        reader.ReadInt();
    }
}
