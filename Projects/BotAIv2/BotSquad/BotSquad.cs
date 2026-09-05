using System;
using System.Collections.Generic;
using Server.Items;
using Server.Logging;
using Server.Mobiles;

namespace Server.BotAI.V2;

/// <summary>What a squad is doing. Three states, and none of them is "stand and wait".</summary>
public enum BotSquadStance
{
    /// <summary>Going somewhere together, holding formation on the leader.</summary>
    Marching,

    /// <summary>Ground with nothing on it. Spread into small knots and cover it. See <see cref="BotScatter"/>.</summary>
    Scouting,

    /// <summary>Something is being dealt with. The formation anchors on whoever is in contact with it.</summary>
    Fighting
}

/// <summary>
/// A standing company of bots: a leader, a few followers, a formation and a share-out.
///
/// <para>
/// <b>One kind of group, not two.</b> The first version had a <em>warband</em> — spontaneous, formed the
/// moment a bot met something it could not take alone — and separately a <em>band</em> raised once a minute
/// by the system, exactly five strong, a medic compulsory, aimed at a square of the map. Both were called
/// "the group", including in the documentation, and the two answered different questions with different
/// lifetimes. This is one thing: it forms, it lives, it walks, it scouts, it fights, it splits what it took,
/// and it goes on.
/// </para>
///
/// <para>
/// <b>No state here may be "stand and wait", and that is a rule paid for in bodies.</b> The first version's
/// mustering and loot-settling states both opened by setting <c>Warmode = false</c> and standing still — and
/// a rally point was almost always inside assembly range, so "go and assemble" degenerated into "stand and
/// look at the enemy". A lich strikes from eight tiles and never closes, so not one rung of the survival
/// ladder ever fired: six bots waited politely in a ring while it killed them one at a time. Every state
/// below is a state in which the bot is going somewhere.
/// </para>
///
/// <para>
/// <b>The collective mind is arithmetic, not messages.</b> Stations, scouting patches and shares are all
/// derived by every member from the same facts in the same order. What passes between bots is only what is
/// genuinely an event: <em>I am being attacked</em>, and <em>get off this tile</em>.
/// </para>
/// </summary>
public sealed class BotSquad
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotSquad));

    /// <summary>
    /// How long the squad keeps at a target that is not dying.
    ///
    /// <para>
    /// <b>Measured in damage, never in time or distance.</b> The first version's engaged state had no exit at
    /// all beyond victory or the death of everybody — so anything that could not be killed held its whole
    /// company for ever, and assisting outranks every personal errand. Twelve bots out of twenty were
    /// permanently "assisting": not idle, bound. The economy worked fine; there was nobody left to take part
    /// in it.
    /// </para>
    ///
    /// <para>
    /// And the measure is the <b>lowest health seen this fight</b>, not the current health, or a creature
    /// regenerating between blows reads as progress.
    /// </para>
    ///
    /// <para>
    /// <b>Ninety seconds was a number, not a measurement, and it read as a crowd standing still.</b> Nothing
    /// a bot carries takes anywhere near that long to come round again: the slowest weapon in any kit is the
    /// crossbow, and the engine gives it <c>15000/((Stam+100) × 18)</c> — four seconds at full stamina, five
    /// and a half at half — while the longest spell in the book has a base cast of four. So ninety seconds is
    /// something like twenty chances to land a blow, times every member, all of them coming to nothing while
    /// the whole company stands on the Bound rung with its own trades set aside. Watched from a client it is
    /// simply a knot of bots doing nothing for a minute.
    /// </para>
    ///
    /// <para>
    /// Reckoned from the engine's own slowest cycle instead — see <see cref="SlowestBlowMs"/> — so that the
    /// question being asked is the honest one: has anybody managed to hurt it in the time it takes the
    /// slowest thing here to swing, three times over. For a company of five that is upwards of twenty blows,
    /// and any one of them landing resets the clock, because the measure is the lowest health seen and not
    /// the current one.
    /// </para>
    /// </summary>
    public static int NoProgressMs => SlowestBlowMs * Blows;

    /// <summary>
    /// The longest a single attack can take to come round again, taken from the engine rather than chosen.
    ///
    /// <para>
    /// A crossbow is the slowest thing in any bot's kit at <c>OldSpeed</c> 18, which the pre-AOS formula
    /// <c>15000/((Stam+100) × 18)</c> turns into four seconds at full stamina and five and a half at half;
    /// the deepest circle in the grimoire has a four-second base cast. Four is the common case, because a bot
    /// that has been standing in a fight failing to hurt anything is not the one that is out of breath. Bots
    /// carry no heavy crossbow — if one ever enters a kit, this is the number that has to move.
    /// </para>
    /// </summary>
    public static int SlowestBlowMs { get; set; } = 4000;

    /// <summary>
    /// How many fruitless swings of the slowest weapon a company will sit through before it accepts that this
    /// is not a fight.
    ///
    /// <para>
    /// <b>Three, and the walk from ninety down to here was worth writing out.</b> Ninety seconds was a
    /// number nobody had measured. Fifteen came from the arithmetic and Patrick's answer on watching it was
    /// that it still reads as a crowd standing about, so it went to two cycles — eight seconds — which held
    /// for three windows and then showed its price: on the fourth, companies broke off seventeen times
    /// against six kills, with the kill rate itself down by a third. A window that ends fights the squad was
    /// winning is worse than one that looks slow, because the second costs patience and the first costs the
    /// fight.
    /// </para>
    ///
    /// <para>
    /// Twelve seconds is the middle nobody had tried: shorter than the fifteen he judged too long, and long
    /// enough that a company of five gets some thirty swings at the thing, any one of which landing resets
    /// the clock. If it reads slow again from the client, this is the one number to move — and the thing to
    /// watch when moving it is the kill rate, not the break-off count, because only the first says whether a
    /// change cost anything.
    /// </para>
    /// </summary>
    public static int Blows { get; set; } = 3;

    /// <summary>
    /// The hard ceiling on one fight. Something healing faster than it is being hit is not a fight, it is a
    /// way of life.
    /// </summary>
    public static int FightCapMs { get; set; } = 240000;

    /// <summary>Most a squad may hold. Checked in one place only — see <see cref="BotSquads.Join"/>.</summary>
    public static int MaxSize { get; set; } = 5;

    /// <summary>
    /// How long a squad may go with nothing to fight before it is let go.
    ///
    /// <para>
    /// <b>A squad with no end is a squad that eats the population.</b> Being in one puts a bot on the
    /// <c>Bound</c> rung, where its own wants are set aside — which is right while there is a fight on and
    /// ruinous the moment there is not: five bots that came together for one ogre would otherwise sweep
    /// empty ground for the rest of the session, mining nothing, crafting nothing and buying nothing, and
    /// the first version has the measurement for what that does — twelve of twenty bots permanently
    /// "assisting", an economy in perfect health with nobody left in it.
    /// </para>
    ///
    /// <para>
    /// Two minutes, which is long enough to look for a second fight where the first one was — that is what
    /// the scouting stance is for — and short enough that a quiet company is back at work before anybody
    /// notices it was gone.
    /// </para>
    /// </summary>
    /// <remarks>
    /// <b>Raised to five minutes on 24.08.2026, by order and against the reasoning above.</b> Two minutes was
    /// chosen to protect the economy from bots bound to a company with nothing to fight — a real risk, and the
    /// first version's bill for it is quoted above. What that number could not know is how much ground a
    /// company covers once it is sweeping properly: two minutes is barely one leg of a sweep, so a squad was
    /// disbanded before it had finished looking at the first patch, and every fight had to start by forming a
    /// company from scratch. A company that lasts long enough to work a graveyard through, then a field, then
    /// the next field, is the thing being asked for — and the quiet clock still tears itself up the moment a
    /// fight starts, so a working company never ages at all.
    /// </remarks>
    /// <summary>
    /// How long a company that is not fighting and cannot find anything to fight stays together.
    ///
    /// <para>
    /// <b>Five minutes was written when a standing company was a company between fights, and it turned out
    /// to be a company that had stopped working.</b> A member is Bound and a Bound bot takes no work of its
    /// own — so every second a quiet squad exists is a second four or five bots are contributing nothing,
    /// and the only thing the clock was buying was the chance that something would wander past. Now that a
    /// standing company goes looking (see <see cref="Hunt"/>), the clock means what it should: nothing worth
    /// a company has been found anywhere near, so give everybody their own afternoon back.
    /// </para>
    ///
    /// <para>
    /// <b>Eight seconds, cut from forty-five on 25.08.2026 after watching it.</b> Forty-five was already six
    /// times better than what it replaced and it was still far too long to look at: a company that has just
    /// killed the last thing in the field stands there for the best part of a minute, four or five bots
    /// deep, and from outside there is no way to tell that from a company that has broken. The sweep for a
    /// new quarry runs every <see cref="HuntEveryMs"/>, so eight seconds is still three honest attempts to
    /// find something before the company gives up on the field — and the clock is torn up the instant
    /// anything is engaged or anybody is hit, so a fight in progress can never age it.
    /// </para>
    /// </summary>
    public static int IdleCapMs { get; set; } = 8000;

    /// <summary>How often a company with no focus sweeps for one. A spatial search on a once-a-second beat.</summary>
    public static int HuntEveryMs { get; set; } = 3000;

    /// <summary>
    /// How far from the focus a member is told to set about it.
    ///
    /// Wide, because a bow carries ten tiles and a spell eight, and a member pointed at the fight from
    /// where it is standing shoots from there. Being pointed at something out of reach costs nothing: the
    /// engine swings when the range is right and not before.
    /// </summary>
    public static int PressReach { get; set; } = 12;

    /// <summary>
    /// How far the anchor may drift before everybody is re-stationed.
    ///
    /// Not every tick: working out a station ends in a path search, and a squad that redraws five of them
    /// every beat is five searches per beat for as long as it is walking. Two tiles of drift is a formation
    /// that visibly holds and costs a fraction of that.
    /// </summary>
    public const int ReformDistance = 2;

    /// <summary>
    /// Least time between two rounds of stationing. Each round is a path search per member who has to move,
    /// and a fight re-stations on two triggers rather than one — the enemy walking, and anybody who stopped
    /// short of it. Something over a second keeps a company of five inside the movement budget the whole
    /// population shares.
    /// </summary>
    public static int RestationMs { get; set; } = 1500;

    /// <summary>
    /// How long the focus may go unhurt before the squad tries standing somewhere else.
    ///
    /// <para>
    /// One slow blow's worth, so a fight that is going nowhere gets a different arrangement tried for each of
    /// the chances <see cref="Blows"/> allows before it is given up on — and a fight that is merely slow,
    /// where blows land every few seconds, never turns at all.
    /// </para>
    ///
    /// <para>
    /// <b>Derived rather than written down, because written down it was already wrong.</b> The line above
    /// used to say "a sixth of NoProgressMs" beside a hard fifteen thousand, which was true only while
    /// NoProgressMs happened to be ninety — and the moment that number came down to where the engine says it
    /// belongs, the two would have met and the squad would have given up at the very instant it first tried
    /// moving. A rule stated in a comment is a rule waiting to be broken by the next edit.
    /// </para>
    /// </summary>
    public static int RotateAfterMs => Math.Max(RestationMs, SlowestBlowMs);

    private readonly List<IBotSquadMember> _members = [];

    private long _stationedTick;

    private int _focusLowest;

    private long _focusProgressTick;

    private long _focusSinceTick;

    private Point3D _stationedAt;

    private BotSquadStance _stationedStance;

    private int _stationedCount;

    /// <summary>Where the focus was standing the last time it was alive, so its corpse can be found.</summary>
    private Point3D _fell;

    private long _quietTick;

    private long _huntedTick;

    private bool _quiet;

    public BotSquad(int id, IBotSquadMember leader)
    {
        Id = id;
        Leader = leader;
        Stance = BotSquadStance.Marching;

        // Seeded from a real tick rather than left at zero: these counters can start enormous and wrap, and a
        // zero here would hold the first stationing off until the clock caught up with it.
        _stationedTick = Core.TickCount;
    }

    public int Id { get; }

    /// <summary>
    /// Whoever called the squad together, whatever class they are. On their death the strongest survivor
    /// inherits.
    ///
    /// The formation is built from here even when the leader is the last person who ought to be in front: an
    /// archer leading means the blades form up ahead of <em>the archer</em> and wait to be led, which is the
    /// intended behaviour rather than a defect to be corrected.
    /// </summary>
    public IBotSquadMember Leader { get; private set; }

    public IReadOnlyList<IBotSquadMember> Members => _members;

    public int Count => _members.Count;

    /// <summary>
    /// Gold-equivalent this company has divided among its members since it formed.
    ///
    /// <para>
    /// <b>What a company is worth, kept where the company is, because some work is worth exactly what it
    /// hands to other people.</b> An errand is judged on takings per minute and takings are read off the
    /// bot that did it — which is right for every trade on the shard and wrong for one: a leader who stands
    /// out of the share-out produces the whole of a corpse for five other bots and measures at nought.
    /// Left that way the ledger would learn, correctly by its own arithmetic and disastrously in fact, that
    /// leading companies pays nothing. Written by <see cref="BotSpoils"/> at the one moment the number is
    /// known, and read by whatever gave the charge.
    /// </para>
    /// </summary>
    public long Won { get; set; }

    public BotSquadStance Stance { get; private set; }

    /// <summary>
    /// Whether this company has been taken apart. A dead company is not a company anybody may join.
    ///
    /// <para>
    /// <b>Nothing marked one, and a dissolved company is still a live object with a live leader.</b>
    /// BotSquads.Dissolve clears the Squad of everybody in it and drops it out of the list the squad timer
    /// walks — so it is never thought about again, and therefore can never disband again. Join looked at the
    /// count, the ceiling and the map, all of which a dead company still answers perfectly well, and
    /// attached new members to it. They sit on the Bound rung with the auction switched off, in a company
    /// that no longer exists and so can never let them go.
    /// </para>
    ///
    /// <para>
    /// The log says it in two lines one second apart on 03.09.2026: "Squad 20 is no more: it went a while
    /// with nothing to fight and nobody holding it together" at 17:41:35, and "Doran 2 fell in with company
    /// 20, now 4 strong" at 17:41:35. Doran 2 was still standing there with its errand reading "nothing"
    /// forty-five minutes later, its own barren clock reading nought the whole time because a Bound bot is
    /// never asked what it wants to do.
    /// </para>
    /// </summary>
    public bool Disbanded { get; private set; }

    /// <summary>Marks this company dead. Called only by <c>BotSquads.Dissolve</c>, which owns the list.</summary>
    internal void Bury() => Disbanded = true;

    /// <summary>
    /// Whether this company is out on a standing charge and is therefore not disbanded for being quiet.
    ///
    /// <para>
    /// Set by whatever gave the charge, and it takes on the duty of ending the company along with it — a
    /// flag that switches off the only clock that ever disbands an unfought squad is a licence to stand in a
    /// field for ever if nobody takes that duty seriously. See <see cref="BotSweep"/>.
    /// </para>
    /// </summary>
    public bool Charged { get; set; }

    /// <summary>
    /// Most this particular company may hold, which is <see cref="MaxSize"/> unless whatever called it
    /// together asked for something else.
    ///
    /// <para>
    /// <b>Per company rather than a second global number, because the global one is a statement about
    /// crowding and this is a statement about one errand.</b> Five is what a muster is worth: more bots on
    /// one ogre is more bots not working. A harrowing was ordered at six, and raising <see cref="MaxSize"/>
    /// to get it would have quietly widened every muster on the shard — the shape of change this project
    /// keeps paying for, where one number is moved and something nobody was looking at moves with it.
    /// </para>
    /// </summary>
    public int Ceiling { get; set; } = MaxSize;

    /// <summary>
    /// What this company looks for when it has nothing to fight, or null for the ordinary rule.
    ///
    /// <para>
    /// <b>A hook rather than a flag, because the answer belongs to whoever gave the charge.</b> The ordinary
    /// rule — see <see cref="Hunt"/> — deliberately refuses anything one bot could handle alone, and it is
    /// right to: a company of five falling on a rat is four bots doing nothing. A harrowing is the case where
    /// that is precisely the point, so the errand supplies its own finder and this file learns nothing about
    /// what a Baron is.
    /// </para>
    /// </summary>
    public Func<Mobile, BaseCreature> Quarry { get; set; }

    /// <summary>What the squad is dealing with, or null.</summary>
    public Mobile Focus { get; private set; }

    /// <summary>
    /// Whoever the formation is built around: the leader ordinarily, and whoever is in contact when there is
    /// a fight.
    ///
    /// That second case is the whole of "if one of us is attacked, go to them". It is not a rescue mechanism —
    /// there is no rescue mechanism. The anchor moves onto the member who was hit, every station is derived
    /// from the anchor, and so everybody is already walking towards the trouble before anything has been
    /// decided.
    /// </summary>
    public IBotSquadMember Contact { get; private set; }

    public Map Map => Leader?.Self?.Map;

    public Point3D Anchor
    {
        get
        {
            // <b>In a fight the formation is built round the creature, not round one of ours.</b> Anchoring on
            // the member in contact is the right answer to "who do we gather on" and the wrong answer to
            // "where do we stand", and it was answering both: a healer who called a company anchored it on
            // himself, stood still because a healer's own station is behind himself, and so nobody was ever
            // re-stationed and nobody ever closed. What it was reaching for survives untouched — the member
            // under attack is where the fight is, because the focus is whatever is attacking him.
            if (Stance == BotSquadStance.Fighting && Focus is { Deleted: false })
            {
                return Focus.Location;
            }

            // Contact can be pruned out from under this — it is a member like any other and members die — so
            // the leader is always the fallback. An anchor of zero would put the whole formation in the corner
            // of the map.
            var on = Contact?.Self is { Deleted: false } && Stance == BotSquadStance.Fighting ? Contact : Leader;

            return on?.Self?.Location ?? Point3D.Zero;
        }
    }

    /// <summary>
    /// How many times the squad has had to send somebody to its station again during this fight.
    ///
    /// Read by <see cref="BotFormation"/> to turn the ring of tiles round the enemy, so that a blade whose
    /// place is behind a fence tries a different one instead of the same one until the fight times out.
    /// </summary>
    public int Attempt { get; private set; }

    /// <summary>
    /// The line to the threat, as a unit offset. Towards the focus when there is one, otherwise wherever the
    /// leader is facing.
    /// </summary>
    public (int X, int Y) Axis
    {
        get
        {
            // From the creature towards us, now that the ranks are measured from the creature: the axis has to
            // point back down the line the squad is standing on, or the blades are stationed on the far side
            // of the thing and walk round it to get there.
            if (Stance == BotSquadStance.Fighting && Focus is { Deleted: false })
            {
                var side = (Contact?.Self is { Deleted: false } ? Contact : Leader)?.Self;

                var offset = side == null ? (0, 0) : Unit(Focus.Location, side.Location);

                if (offset != (0, 0))
                {
                    return offset;
                }
            }
            else if (Focus is { Deleted: false })
            {
                var offset = Unit(Anchor, Focus.Location);

                if (offset != (0, 0))
                {
                    return offset;
                }
            }

            var leader = Leader?.Self;

            return leader == null ? (0, -1) : Facing(leader.Direction);
        }
    }

    internal void Attach(IBotSquadMember member)
    {
        if (member != null && !_members.Contains(member))
        {
            _members.Add(member);
        }
    }

    internal bool Detach(IBotSquadMember member) => _members.Remove(member);

    internal void Promote(IBotSquadMember member) => Leader = member;

    /// <summary>Whether this bot belongs here.</summary>
    public bool Holds(IBotSquadMember member) => _members.Contains(member);

    /// <summary>
    /// Deals with this. Resets the damage bookkeeping, because a new target is a new fight.
    /// </summary>
    public void Engage(Mobile focus, IBotSquadMember contact)
    {
        if (focus is not { Deleted: false, Alive: true })
        {
            return;
        }

        if (ReferenceEquals(Focus, focus))
        {
            // Already on it. Only the point of contact may have changed, which it does whenever somebody else
            // gets hit — and that is worth honouring, because it is where the formation should be built from.
            Contact = contact ?? Contact;

            return;
        }

        Focus = focus;
        Contact = contact ?? Leader;
        Stance = BotSquadStance.Fighting;
        Attempt = 0;
        _focusLowest = focus.Hits;
        _focusProgressTick = Core.TickCount;
        _focusSinceTick = Core.TickCount;
        _ableTick = Core.TickCount;
        _closedTick = Core.TickCount;

        logger.Information(
            "Squad {Id} of {Count} is dealing with {What} ({Power:F0})",
            Id,
            Count,
            focus.Name,
            BotThreat.Power(focus)
        );
    }

    /// <summary>Done with the fight, whatever the reason. Back to walking.</summary>
    public void Disengage(string why, bool hopeless = false)
    {
        if (Focus == null)
        {
            return;
        }

        // <b>The numbers that would have told the whole story in one line and were not there.</b> "Its health
        // has not moved" is true of a company that cannot hurt the thing and equally true of one that never
        // got near it, and those want opposite fixes. How many of us were standing in reach of it separates
        // them at a glance — and the denominator has to be there, or none is indistinguishable from all.
        //
        // <b>Said only where it answers something, which is the correction to its first evening.</b> Printed
        // on every break-off it read "0 of 3 in reach, nearest -1" over three clean kills in a row: a dead
        // focus is a deleted mobile off on the internal map, so the count had nobody to count and the honest
        // answer to a question nobody was asking looked exactly like the failure it was written to detect.
        if (hopeless)
        {
            var reach = InReach(out var nearest, out var blind, out var refused, out var unsteady);

            // <b>"In reach" meant a distance, and a distance is not the question the engine answers.</b>
            // Every swing goes through <c>InLOS</c>, so a blade on the tile that touches a wraith through a
            // crypt wall counted as in reach, in the fight, and doing nothing — and this line said so in the
            // most reassuring words available. The two ways a bot standing on the right tile still cannot
            // hurt anything are counted apart, exactly as BotSlay counts them, because a broken line is
            // cured by another tile and a refusal is not.
            logger.Information(
                "Squad {Id} broke off from {What}: {Why} — {Reach} of {Count} able to strike ({Blind} with no line to it, {Refused} refused by the engine, {Unsteady} moved too recently to shoot), nearest {Nearest} tiles off, {Tries} tries at standing right",
                Id,
                Focus.Name,
                why,
                reach,
                Count,
                blind,
                refused,
                unsteady,
                nearest,
                Attempt
            );
        }
        else
        {
            logger.Information("Squad {Id} broke off from {What}: {Why}", Id, Focus.Name, why);
        }

        // Nobody could hurt it, whatever the reason. Left alone long enough that the population stops
        // rediscovering it: the same wraith was called a company against every two minutes for an evening,
        // thirty-five times, because breaking off taught nothing to anybody.
        if (hopeless)
        {
            BotQuarry.Shun(Focus, BotQuarry.HopelessMs);
        }

        Focus = null;
        Contact = null;
        Attempt = 0;
        Stance = BotSquadStance.Marching;
    }

    /// <summary>
    /// What stops this member landing a blow on the focus from exactly where it stands, if anything.
    ///
    /// <para>
    /// <b>One rule, asked from the three places that were each answering it their own way.</b> The press set
    /// a combatant on anybody inside twelve tiles, the break-off line counted anybody inside its own ring,
    /// and the stationing asked neither — so a company could report itself fully engaged while not one arrow
    /// left it. Every one of the four refusals below is the engine's, taken from the engine, and each has a
    /// different cure: distance wants a walk, a broken line wants another tile, a refusal wants nothing at
    /// all, and stillness wants to be left alone.
    /// </para>
    /// </summary>
    private enum BotBlow
    {
        /// <summary>Not near enough for its own rank's ring.</summary>
        Far,

        /// <summary>Near enough, and no line to it. See BotFormation.Sighted — the engine drops the swing silently.</summary>
        Blind,

        /// <summary>Near enough and in plain sight, and the engine refuses the blow anyway.</summary>
        Refused,

        /// <summary>
        /// Near enough, in sight, holding a bow — and it has moved too recently to loose an arrow.
        ///
        /// <para>
        /// <b>This is the one the company never knew about, and it is the answer to a whole evening.</b>
        /// <c>BaseRanged.OnSwing</c> refuses outright unless the archer has stood still for a second on this
        /// era, and the formation re-stations everybody every <see cref="RestationMs"/> whenever the thing
        /// being fought drifts two tiles — which a creature in a melee does constantly. So the shooters were
        /// walked back and forth over one tile for the whole fight and never once fired, while every measure
        /// this file had said they were in the fight. The lone hunter has known the number since 25.08.2026
        /// and says so in as many words: "a kite that does not know this number is a bot that moves for ever
        /// and never fires". <c>BotSlay.StillMs</c> is that number, and it is read from there rather than
        /// copied, because two copies of an engine constant is how they come apart.
        /// </para>
        /// </summary>
        Unsteady,

        /// <summary>Swinging.</summary>
        Able
    }

    /// <summary>
    /// How far this member can actually hurt the focus from — which is not the same question as where the
    /// formation means it to stand, and answering the first with the second put the archers on a treadmill.
    ///
    /// <para>
    /// <b>A ring is a place to walk to; a reach is what the engine will let a blow cross.</b> The shooters'
    /// ring is five tiles and a bow carries ten, so an archer that was hitting the thing perfectly well from
    /// seven was judged out of the fight, marched two tiles back in, and lost its shot to the second of
    /// stillness <c>BaseRanged.OnSwing</c> demands — and then the creature drifted and it happened again.
    /// Measured on the first window that could see it: 96 beats of "moved too recently to fire" in sixteen
    /// minutes, against six of every other refusal put together.
    /// </para>
    ///
    /// <para>
    /// The ring stays the floor, because a blade's reach is one tile and its ring is one tile, and nothing
    /// should be able to make a rank <em>narrower</em> than the formation drew it. Casters are given the
    /// spell range rather than the stick in their hands, which is the thing they are actually fighting with;
    /// asked of the role rather than of the pack, because <c>BotStrike.Can</c> walks the whole backpack and
    /// this is asked three times a second for every member of every company.
    /// </para>
    /// </summary>
    private static int Reach(IBotSquadMember member, Mobile body)
    {
        var role = BotFormation.RoleOf(member);
        var arm = body.Weapon?.MaxRange ?? 1;

        if (role is BotRole.Caster or BotRole.Medic)
        {
            arm = Math.Max(arm, BotStrike.Range);
        }

        return Math.Max(BotFormation.PressRingFor(role), arm);
    }

    /// <summary>The verdict above, with how far off this member is on the way out.</summary>
    private BotBlow Strike(IBotSquadMember member, out int away)
    {
        away = int.MaxValue;

        var focus = Focus;
        var body = member?.Self;

        if (body is not { Deleted: false, Alive: true } || focus is not { Deleted: false } || body.Map != focus.Map)
        {
            return BotBlow.Far;
        }

        away = Math.Max(
            Math.Abs(body.Location.X - focus.Location.X),
            Math.Abs(body.Location.Y - focus.Location.Y)
        );

        if (away > Reach(member, body))
        {
            return BotBlow.Far;
        }

        if (!body.InLOS(focus))
        {
            return BotBlow.Blind;
        }

        if (!body.CanBeHarmful(focus, false))
        {
            return BotBlow.Refused;
        }

        if (body.Weapon is BaseRanged && Core.TickCount - body.LastMoveTime < BotSlay.StillMs)
        {
            return BotBlow.Unsteady;
        }

        return BotBlow.Able;
    }

    /// <summary>
    /// How many members could actually land a blow on the focus, how far off the nearest is, and — of the
    /// ones near enough — how many are stopped by a broken line, by the engine's refusal, and by having
    /// moved too recently to shoot.
    /// </summary>
    private int InReach(out int nearest, out int blind, out int refused, out int unsteady)
    {
        nearest = int.MaxValue;
        blind = 0;
        refused = 0;
        unsteady = 0;

        var reach = 0;

        for (var i = 0; i < _members.Count; i++)
        {
            var verdict = Strike(_members[i], out var away);

            if (away < nearest)
            {
                nearest = away;
            }

            switch (verdict)
            {
                case BotBlow.Blind:
                    blind++;

                    break;

                case BotBlow.Refused:
                    refused++;

                    break;

                case BotBlow.Unsteady:
                    unsteady++;

                    break;

                case BotBlow.Able:
                    reach++;

                    break;
            }
        }

        if (nearest == int.MaxValue)
        {
            nearest = -1;
        }

        return reach;
    }

    /// <summary>
    /// One beat of the squad's own life. Called from the squad tick, not from a bot's.
    ///
    /// Returns false when the squad should cease to exist — too few left, or nobody to lead it.
    /// </summary>
    /// <summary>
    /// One beat of the company's own thinking. Null while it lives; otherwise why it is over.
    ///
    /// <para>
    /// <b>A reason rather than a false, because the three ways a company ends are three different things
    /// and read as one.</b> Every disbanding on this shard was logged "there is nothing left of it" — the
    /// caller's own words for a bare <c>false</c> — so a company that lost its last member, a company of one
    /// that nobody had charged, and a company whose leader died with nobody fit to inherit were the same
    /// line. Five of them in seventeen minutes on 27.08.2026 and no way to tell which had happened.
    /// </para>
    /// </summary>
    internal string Update()
    {
        Prune();
        Release();

        // <b>A company of one is nothing, unless somebody is standing in a square calling for the second.</b>
        // Every company on this shard is born at one — <c>BotSquads.Form</c> makes a squad out of its leader
        // and members join afterwards — and for a muster that gap is a single beat, so this never mattered.
        // A harrowing calls for five minutes, and for those five minutes it is a company of one: dissolved on
        // the next squad beat, re-formed on the next Baron beat, dissolved again. 720 companies formed and
        // 720 dissolved in eight hours on 27.08.2026, and every march that came out of it reported "the
        // company broke up on the road" within the second, because the squad the errand was holding had been
        // thrown away and replaced while it was still calling.
        //
        // The charge is exactly the right thing to ask, and it is already the answer to the same question one
        // line below in Quiet(): whoever set it owns ending the company. Nought members is still nothing —
        // there is nobody left to own anything.
        if (_members.Count == 0)
        {
            return "the last of them is gone";
        }

        // <b>A charge is held by an undertaking, and when the undertaking is gone nobody is holding it.</b>
        // This is the fourth time on this shard that a company has outlived whoever raised it: BotScout,
        // BotSweep and BotHarrow each learned to put the flag down, and BotProwl was written without a
        // matching release at all — so a company raised for a walk to a hunting ground stood for ever,
        // because a charged company does not age (see Quiet) and a leader is never let go of (see Release).
        // Ninety-four of the hundred and thirty-six stall reports of 04.09.2026 were that, some nineteen
        // minutes long, every one of them ending in the words "in a company".
        //
        // Rather than a fifth copy of the same two lines, the invariant is asserted here where it can be
        // seen: whoever charged it did so from inside a running errand, and that errand is <c>Alongside</c>
        // by construction — it is the only kind a Bound bot may hold. A leader holding anything else, or
        // nothing, is a leader who is not charging this company with anything.
        if (Charged && Leader?.Self is BotMobile head && head.Resolve?.Deed is not { Alongside: true })
        {
            Charged = false;
            Unowned++;
        }

        if (_members.Count < 2 && !Charged)
        {
            return "one bot was left in it and nobody had charged it with anything";
        }

        if (Leader?.Self is not { Deleted: false, Alive: true } || !_members.Contains(Leader))
        {
            if (!Inherit())
            {
                return "it lost its leader and nobody in it could take over";
            }
        }

        if (Stance == BotSquadStance.Fighting)
        {
            Judge();
        }

        Settle();
        Station();

        // Where everybody stands is one half of a fight and it was the only half there was. Nothing anywhere
        // told a member to swing: the formation walked five bots into a ring around an ogre and left them
        // watching it, and the only ones that ever fought back were the one or two it happened to hit, by
        // way of their own reflex. A company that has agreed on a target attacks the target.
        if (Stance == BotSquadStance.Fighting)
        {
            Press();
        }
        else
        {
            Hunt();
        }

        return Quiet() ? null : "it went a while with nothing to fight and nobody holding it together";
    }

    /// <summary>
    /// A company with nothing to fight goes and finds something.
    ///
    /// <para>
    /// <b>Five bots stood in a graveyard for ninety seconds and this is the line that was missing.</b> A
    /// squad member is <c>Bound</c>, and a Bound bot's own auction is skipped on purpose — the company owns
    /// where it stands and what it hits, so letting each member shop for work would be five bots walking
    /// five different ways. The consequence nobody had followed through is that a company with no focus is
    /// five bots with <em>no source of work at all</em>: they hold formation, they scatter prettily when the
    /// leader stops, and not one of them will ever start a fight. Every muster on this shard ended that way
    /// — the quarry died, the focus cleared, and the company stood about until the quiet clock disbanded it
    /// five minutes later. On 25.08.2026 that was Squad 1 at 20:22:06, five bots, ninety seconds and
    /// counting, while the miners around them worked.
    /// </para>
    ///
    /// <para>
    /// Asked of the same finder a muster is called by, so a company hunts exactly what a company is for:
    /// things big enough to be worth more than one bot. It is deliberately not <c>BotQuarry.Nearest</c> —
    /// a squad of five falling on a rat is five bots doing one bot's work, which is the crowding this
    /// project spends most of its arithmetic avoiding.
    /// </para>
    /// </summary>
    private void Hunt()
    {
        var leader = Leader?.Self;

        if (leader is not { Deleted: false, Alive: true } || leader.Map != Map)
        {
            return;
        }

        // Throttled: this is a spatial sweep and the squad beats once a second. A company that has just
        // failed to find anything will not find anything a tick later either.
        var now = Core.TickCount;

        if (now - _huntedTick < HuntEveryMs)
        {
            return;
        }

        _huntedTick = now;

        // The charge's own finder when it brought one, and a company is never given both.
        var quarry = Quarry != null ? Quarry(leader) : BotQuarry.Company(leader, BotMuster.Reach);

        if (quarry == null)
        {
            return;
        }

        Engage(quarry, Leader);
    }

    /// <summary>Everybody near enough sets about the focus. Said every beat; the setter is a no-op unchanged.</summary>
    private void Press()
    {
        var focus = Focus;

        if (focus is not { Deleted: false, Alive: true })
        {
            return;
        }

        for (var i = 0; i < _members.Count; i++)
        {
            var body = _members[i].Self;

            if (body is not { Deleted: false, Alive: true } || body.Map != focus.Map)
            {
                continue;
            }

            if (!body.InRange(focus.Location, PressReach))
            {
                continue;
            }

            // Counted here and nowhere else, because this is the beat on which a bot either fights or does
            // not. Everyone in reach is still put in warmode and still given the combatant whatever the
            // verdict: the engine retries the swing every tick, so the moment the line opens or the bot has
            // stood still long enough the blow lands without waiting for anything of ours.
            switch (Strike(_members[i], out _))
            {
                case BotBlow.Blind:
                    Blinded++;

                    break;

                case BotBlow.Refused:
                    Refused++;

                    continue;

                case BotBlow.Unsteady:
                    Unsteadied++;

                    break;
            }

            // Nothing goes into a company's fight with its fists but the brawler. A squad member's own
            // undertaking is set aside while it is Bound, so the check that lives in BotSlay never runs for
            // any of them — this is the only place it can be asked of a bot fighting as part of a company.
            BotArms.Check(body, _members[i].Class);

            // And a captain fighting in its own company draws the same steel it would draw hunting alone.
            // The ring rather than the reach: a bot pressing a focus from the far edge of PressReach is
            // still shooting, and only the ones actually standing on it are in a blade fight.
            BotArms.Suit(body, focus, BotFormation.PressRingFor(BotFormation.RoleOf(_members[i])));

            body.Warmode = true;

            if (!ReferenceEquals(body.Combatant, focus))
            {
                body.Combatant = focus;
            }

            // Only the ranks that stand off. A blow disturbs a cast whenever the caster is a player and every
            // bot is one, so a blade casting from the contact ring is a bot doing neither thing: its spell is
            // broken by the first swing against it and its own swing is spent waiting for a spell. The
            // formation already draws that line — casters and medics are stationed at seven tiles and blades
            // at one — so the rank is the answer and no new rule is needed. A warrior-mage is filed under
            // Melee on purpose (see BotRole) and holds the line; hunting alone it casts, through BotSlay.
            switch (BotFormation.RoleOf(_members[i]))
            {
                case BotRole.Medic:
                    // A medic mends first and throws spells only when there is nobody to mend. See Mend.
                    if (!Mend(body))
                    {
                        Conjure(body, focus);
                    }

                    break;

                case BotRole.Caster:
                    Conjure(body, focus);

                    break;
            }
        }
    }

    /// <summary>
    /// A medic in a company mends whoever in it is worst hurt. Returns whether it is busy doing so.
    ///
    /// <para>
    /// <b>The same hole as the casting, found in the same hour, and worse.</b> <c>BotSurgeon</c> — the only
    /// thing on this shard that offers "go and patch somebody up" — is a proposer on the <c>Free</c> rung,
    /// and a squad member is <c>Bound</c>, where <c>BotWill</c> skips the auction entirely. So a healer that
    /// joined a company stopped healing: not slowly, not badly — at all, for as long as the company lasted.
    /// Five of the fifty-four bots on this shard are healers and every muster is written on the assumption
    /// that one of them is behind the line.
    /// </para>
    ///
    /// <para>
    /// <b>Mending outranks throwing, and that ordering is the whole of the rule.</b> Given a spellbook and
    /// an enemy, the attack ladder would happily spend a medic's whole pool on magic arrows while the blade
    /// in front of it bled out — which is a strictly worse use of the same mana. So the medic is asked for a
    /// patient first and only reaches for the attack ladder when the company is whole.
    /// </para>
    ///
    /// <para>
    /// Cloth is deliberately not attempted here. A bandage is a nine-second undertaking with its own timer
    /// and its own failure modes, and that is <c>BotSalve</c>'s business on the Free rung; what this does is
    /// the two-second spell, which is the thing a medic behind a line is for. A medic with no book and no
    /// mana falls through to the attack ladder and then to its stick, exactly as it did before.
    /// </para>
    /// </summary>
    private bool Mend(Mobile body)
    {
        // The click, and the same guard as the attack cast: a Bound bot's own undertaking is set aside, so
        // the only cursor up is one of ours.
        // <b>Worked out again rather than remembered, and with five healers on this shard that is the
        // difference between a rule and a race.</b> A field on the company holding "who is being healed"
        // is one field for however many medics are in it: the second one to begin a cast overwrites it, and
        // the first one's click then lands on the second one's patient. Recomputing costs a walk of at most
        // five members and cannot be wrong — if the worst-hurt has changed since the cast began, the new
        // worst-hurt is the right place to put the heal anyway.
        if (body.Target != null)
        {
            var at = Worst(body) ?? body;

            if (BotMend.Aim(body, at))
            {
                Mended++;
            }

            return true;
        }

        if (body.Spell != null)
        {
            return true;
        }

        var patient = Worst(body);

        if (patient == null)
        {
            return false;
        }

        var spell = BotMend.Spell(body, patient);

        if (spell < 0)
        {
            return false;
        }

        return BotMend.Begin(body, spell);
    }

    /// <summary>
    /// The worst-hurt member of this company within the medic's own reach, itself included.
    ///
    /// <para>
    /// Itself included for the reason <c>BotSurgeon</c> gives: below its own failing mark a bot is on a
    /// higher rung and looking after itself already, and above it nothing else in the world would offer.
    /// </para>
    /// </summary>
    private Mobile Worst(Mobile medic)
    {
        Mobile worst = null;
        var lowest = MendAbove;

        for (var i = 0; i < _members.Count; i++)
        {
            var body = _members[i].Self;

            if (body is not { Deleted: false, Alive: true } || body.Map != medic.Map || body.HitsMax <= 0)
            {
                continue;
            }

            if (!medic.InRange(body.Location, BotSurgeon.Reach) || !medic.InLOS(body))
            {
                continue;
            }

            var share = body.Hits / (double)body.HitsMax;

            if (share >= lowest)
            {
                continue;
            }

            lowest = share;
            worst = body;
        }

        return worst;
    }

    /// <summary>
    /// How hurt somebody has to be before a company's medic spends a cast on them.
    ///
    /// <para>
    /// Four fifths, which is the engine's own opinion of "genuinely hurt" put in one number: healing a bot
    /// that has taken one scratch is the training dummy with a friend in it, and that is the shape the whole
    /// ledger exists to refuse. See BotSurgeon, which says the same thing on the other side of the rung.
    /// </para>
    /// </summary>
    public static double MendAbove { get; set; } = 0.8;

    /// <summary>
    /// A caster in a company casts, and until 04.09.2026 it did not.
    ///
    /// <para>
    /// <b>The same shape as the archers, found in the same hour and for the same reason.</b> <c>BotStrike</c>
    /// was written because "a caster's book filled up, its Inscribe climbed, its reagents were bought and
    /// spent — on writing; in a fight it walked up and hit things with a stick". It is called from exactly
    /// two places, <c>BotSlay</c> and the Baron's harrowing, and both of them are a bot fighting <em>alone</em>.
    /// A member of a company is Bound, its own undertaking is set aside, and the only thing anything did for
    /// it was set a combatant — which is the engine's word for "swing whatever is in your hands". So every
    /// mage, every healer and every warrior-mage that joined a company went straight back to hitting things
    /// with a stick, which is the one thing its whole build is arranged to avoid.
    /// </para>
    ///
    /// <para>
    /// <b>Melee goes on underneath, deliberately</b> — the engine swings on its own timer whatever else the
    /// mobile is doing, so a caster loses nothing by also being in a fight, and a caster out of mana is
    /// simply a bot with a stick again. That is BotSlay's own reasoning and it is repeated rather than
    /// referenced only because the two live on opposite sides of the rung.
    /// </para>
    ///
    /// <para>
    /// <b>Stateless, unlike the hunt's version, and that is a decision.</b> BotSlay keeps three fields to
    /// throttle itself; here the engine's own two are enough — a cast in flight sets <c>Spell</c>, a cast
    /// waiting for its target sets <c>Target</c> — and <c>Begin</c> refuses politely during recovery. A
    /// company beats once a second against a recovery of <see cref="BotStrike.CastMs"/>, so the wasted ask
    /// is at most one a cycle, and the alternative is a per-member record on an object that is rebuilt every
    /// world load.
    /// </para>
    /// </summary>
    private static void Conjure(Mobile body, Mobile focus)
    {
        if (!BotStrike.Can(body))
        {
            return;
        }

        // The click a bot has no client to make. Only ever our own cursor: a Bound bot's own undertaking is
        // set aside, so nothing else of ours is putting one up.
        if (body.Target != null)
        {
            BotStrike.Aim(body, focus);

            return;
        }

        // Mid-cast. The engine holds the delay, and the formation has already been told to leave a member
        // that can strike where it stands.
        if (body.Spell != null)
        {
            return;
        }

        var spell = BotStrike.Best(body);

        if (spell < 0)
        {
            // It holds a book or a scroll and cannot pay for anything in it this instant. Counted, because
            // "the casters are casting" and "the casters are out of mana" look identical from every other
            // number on this shard, and the cure for the second is reagents rather than arithmetic.
            Dry++;

            return;
        }

        if (BotStrike.Begin(body, spell))
        {
            Conjured++;
        }
    }

    /// <summary>
    /// Whether this company still has a reason to exist. See <see cref="IdleCapMs"/>.
    ///
    /// The clock starts when the fighting stops and is torn up when it starts again, so a squad working
    /// through a graveyard one resident at a time never ages, and one standing in an empty field does.
    /// </summary>
    private bool Quiet()
    {
        // <b>A company with a standing charge does not age.</b> The quiet clock is the right rule for a
        // muster — five bots who gathered against one troll and killed it have no further reason to be a
        // company, and holding them together would be fifteen bots' worth of shard doing four bots' worth of
        // work. It is exactly the wrong rule for a patrol, whose entire value is being in a dangerous place
        // *before* anything happens there: measured by this clock, the better a patrol is working the sooner
        // it is disbanded for having nothing to fight. Whoever sets this owns ending the company; see
        // BotSweep, which fences itself with two clocks of its own for that reason.
        if (Charged)
        {
            _quiet = false;

            return true;
        }

        if (Stance == BotSquadStance.Fighting)
        {
            _quiet = false;

            return true;
        }

        if (!_quiet)
        {
            _quiet = true;
            _quietTick = Core.TickCount;

            return true;
        }

        return Core.TickCount - _quietTick < IdleCapMs;
    }

    /// <summary>
    /// How long a company may go without a fight before it stops holding members who are doing nothing for it.
    ///
    /// <para>
    /// Half a minute, which is well short of the four the stall watch waits and well past any gap between two
    /// undertakings, so a bot is let go before anybody reports it standing still and not for merely being
    /// between jobs.
    /// </para>
    /// </summary>
    public static int RestCapMs { get; set; } = 30000;

    /// <summary>Members let go for doing nothing for a company that was doing nothing. For the summary.</summary>
    public static long Released { get; private set; }

    /// <summary>Beats on which a member stood near enough to fight and had no line to the thing. For the summary.</summary>
    public static long Blinded { get; private set; }

    /// <summary>Beats on which the engine itself refused the blow. Counted apart: a different cure.</summary>
    public static long Refused { get; private set; }

    /// <summary>Beats on which a shooter was in place and had moved too recently to fire. See BotBlow.Unsteady.</summary>
    public static long Unsteadied { get; private set; }

    /// <summary>Spells a company's back ranks actually got off. Nought before 04.09.2026: see Conjure.</summary>
    public static long Conjured { get; private set; }

    /// <summary>Heals a company's medics landed on their own. Nought before 04.09.2026: see Mend.</summary>
    public static long Mended { get; private set; }

    /// <summary>Charges taken back because the errand holding them had ended. See Update.</summary>
    public static long Unowned { get; private set; }

    /// <summary>Beats a caster in a company held a book it could not pay a single spell out of.</summary>
    public static long Dry { get; private set; }

    /// <summary>Fights given up because not one member could land a blow from anywhere it could reach.</summary>
    public static long Blindfights { get; private set; }

    public static void Forget()
    {
        Released = 0;
        Blinded = 0;
        Refused = 0;
        Unsteadied = 0;
        Conjured = 0;
        Mended = 0;
        Unowned = 0;
        Dry = 0;
        Blindfights = 0;
    }

    /// <summary>
    /// Lets go of anybody who is neither doing something of its own nor going anywhere for the company.
    ///
    /// <para>
    /// <b>An uncharged company already dies of this in eight seconds — see <see cref="IdleCapMs"/> — and a
    /// charged one never does, by design and rightly: whoever set the charge owns ending it.</b> But the
    /// charge was also holding everybody else. A bot in a company sits on the Bound rung, and
    /// <c>BotWill</c> skips the auction entirely for Bound — so a member of a captain's party that is walking
    /// somewhere five hundred tiles away, and has no station to walk to and nothing of its own in hand, is
    /// offered nothing at all. Not even the way home: <c>BotHomer</c> is a Free-rung errand.
    /// </para>
    ///
    /// <para>
    /// Patrick's decision of 03.09.2026, put as "release the bound from an idle company" — the other way
    /// round would have been to exempt the way home from the rung, which fixes the symptom and leaves the
    /// bot bound to something that has no use for it.
    /// </para>
    ///
    /// <para>
    /// Three guards, and each of them is what keeps this from breaking a working company. A company in a
    /// fight lets nobody go, and the clock is torn up the moment it engages. A member that has moved in the
    /// last two seconds is walking to a station and is doing exactly what the company asked. A member
    /// holding an undertaking of its own is not idle whatever it looks like — an <c>Alongside</c> errand is
    /// allowed to run while Bound. And the leader is never let go of, because a company without one is a
    /// separate rule two screens up.
    /// </para>
    /// </summary>
    private void Release()
    {
        if (Stance == BotSquadStance.Fighting)
        {
            _resting = false;

            return;
        }

        if (!_resting)
        {
            _resting = true;
            _restTick = Core.TickCount;

            return;
        }

        if (Core.TickCount - _restTick < RestCapMs)
        {
            return;
        }

        for (var i = _members.Count - 1; i >= 0; i--)
        {
            var member = _members[i];

            if (ReferenceEquals(member, Leader) || member.Self is not BotMobile bot)
            {
                continue;
            }

            // <b>The comment two screens up said <c>Alongside</c> and the code said "any errand at all", and
            // the difference is the longest freeze on this shard.</b> A Bound bot's own auction is skipped
            // (<c>BotWill.cs</c>), and an errand that is not <c>Alongside</c> is set aside rather than
            // advanced — so a member holding one is doing nothing, can do nothing, and cannot be offered
            // anything, and this line was reading exactly that state as "busy, leave it alone". It sat there
            // until <c>AsideCapMs</c> gave the errand up: ten minutes. The log of 03.09.2026 has twenty-four
            // of them in a day, eleven of them <c>unload</c>, and forty-four of sixty-five stall reports
            // carrying the words "in a company".
            //
            // A parked errand is not a claim on the bot. It is the reason to let go of it: released, the bot
            // is Free on the next beat and the errand it was holding advances instead of expiring.
            if (bot.Journey is { Moving: true } || bot.Resolve?.Deed is { Alongside: true })
            {
                continue;
            }

            _members.RemoveAt(i);
            member.Squad = null;
            Released++;

            logger.Information(
                "Squad {Id} let {Name} go: it had nothing of its own and nowhere of ours to walk to",
                Id,
                bot.Name
            );
        }
    }

    /// <summary>When this company last had a fight to be in. See <see cref="Release"/>.</summary>
    private long _restTick;

    private bool _resting;

    /// <summary>Members who are gone, dead, or somewhere else entirely.</summary>
    private void Prune()
    {
        for (var i = _members.Count - 1; i >= 0; i--)
        {
            var body = _members[i].Self;

            if (body is { Deleted: false } && body.Map == Map && body.Map != Map.Internal)
            {
                continue;
            }

            _members.RemoveAt(i);
        }
    }

    /// <summary>
    /// The leader has fallen. The strongest survivor takes over — power by the same measure everything else
    /// uses, so the answer is the same one the squad would give about an enemy.
    /// </summary>
    private bool Inherit()
    {
        IBotSquadMember heir = null;
        var best = 0.0;

        for (var i = 0; i < _members.Count; i++)
        {
            var candidate = _members[i];

            if (candidate.Self is not { Deleted: false, Alive: true })
            {
                continue;
            }

            var power = BotThreat.Power(candidate.Self);

            if (power <= best)
            {
                continue;
            }

            heir = candidate;
            best = power;
        }

        if (heir == null)
        {
            return false;
        }

        Leader = heir;

        logger.Information("Squad {Id} lost its leader; {Name} has it now", Id, heir.Self.Name);

        return true;
    }

    /// <summary>Whether the fight is still a fight. See <see cref="NoProgressMs"/>.</summary>
    private void Judge()
    {
        var focus = Focus;

        if (focus is not { Deleted: false, Alive: true })
        {
            // Before the focus is let go, because letting it go loses the only reference to what was killed
            // and therefore to whose corpse this is. A company that kills something and walks away from the
            // purse has done the one piece of work on this shard that brings new gold into the world, and
            // then declined the gold.
            Spoils(focus);

            Disengage("it is down");

            return;
        }

        // Kept while it is alive: a corpse lies where the thing fell, and by the time anybody notices it is
        // dead the mobile itself may already be off the map.
        _fell = focus.Location;

        if (focus.Hits < _focusLowest)
        {
            _focusLowest = focus.Hits;
            _focusProgressTick = Core.TickCount;
        }

        if (Core.TickCount - _focusSinceTick >= FightCapMs)
        {
            Disengage("four minutes is long enough", true);

            return;
        }

        // <b>Twelve seconds is the patience a fight deserves; a fight nobody is in deserves none of it.</b>
        // "Its health has not moved" is the right clock for a company that is swinging and missing, and the
        // wrong one for a company that is not swinging at all — and until the line was asked of the engine
        // rather than of the distance, those two were the same reading. A company that cannot land a blow
        // from anywhere it has been able to reach has tested its hypothesis and failed it; sitting out the
        // rest of the window costs every member of it the difference, on top of the quiet clock afterwards.
        //
        // The clock is torn up by anybody at all becoming able to strike, so a company working its way round
        // an obstacle is never cut off — only one that has been standing blind throughout.
        // <b>And the clock must not start before it can mean anything.</b> Its first evening cut two fights
        // off at four seconds with the break-off line reading "0 of 5 able to strike (0 with no line to it,
        // 0 refused), nearest 14 tiles off" — nobody was blind, nobody was refused, nobody had arrived. A
        // company still walking to a fight is not a company failing at one, and "able to strike" is false of
        // both. Only the members standing on their own ring can say anything about whether the arrangement
        // works, so with none of them there yet the clock is torn up along with the rest.
        var able = InReach(out _, out var blind, out var refused, out var unsteady);
        var arrived = able + blind + refused + unsteady;

        // <b>Both of the clocks below judge a fight, and neither of them may run before there is one.</b>
        // A company engages whatever the finder hands it, and the finder reaches forty tiles; the walk to a
        // spectre twenty tiles off is a fair part of a minute. Started at the moment of engaging, "its
        // health has not moved" was being said of a creature nobody had reached — Squad 63 said it twice in
        // one window at seventeen and twenty tiles, and every one of those break-offs shuns the creature and
        // then hands it straight back through the defender's path, which is where the forty-one rebuffs in
        // the same window came from. The outer bound stays: a company that can never close gives up on the
        // clock below rather than standing there for the whole FightCapMs.
        if (arrived == 0)
        {
            _focusProgressTick = Core.TickCount;
            _ableTick = Core.TickCount;

            if (Core.TickCount - _closedTick >= CloseMs)
            {
                Disengage("we never got near it", true);
            }

            return;
        }

        _closedTick = Core.TickCount;

        if (able > 0 || unsteady > 0 || blind + refused == 0)
        {
            _ableTick = Core.TickCount;
        }
        else if (Core.TickCount - _ableTick >= BlindMs)
        {
            Blindfights++;

            Disengage(
                blind > refused
                    ? "we are standing on it and there is no line to it"
                    : "we are standing on it and the engine refuses every blow",
                true
            );

            return;
        }

        if (Core.TickCount - _focusProgressTick >= NoProgressMs)
        {
            Disengage("its health has not moved", true);
        }
    }

    /// <summary>
    /// How long a company may stand round something none of it can hit before it gives the thing up.
    ///
    /// <para>
    /// Four seconds: long enough for the ring to be turned once and walked to — the formation re-stations at
    /// <see cref="RestationMs"/> and a blade covers a couple of tiles in that time — and short enough that a
    /// creature inside a crypt wall costs a company one third of what it used to.
    /// </para>
    /// </summary>
    public static int BlindMs { get; set; } = 4000;

    /// <summary>When somebody in this company was last able to land a blow on the focus. See <see cref="BlindMs"/>.</summary>
    private long _ableTick;

    /// <summary>
    /// How long a company may spend walking at something without one member of it arriving, before it gives
    /// the thing up.
    ///
    /// <para>
    /// Half a minute, which is a generous crossing of the forty tiles the company finder reaches, and a
    /// sixth of the outright cap on a fight. What it replaces is the wrong sentence rather than a missing
    /// one: before this, a company that never arrived broke off after twelve seconds saying the target's
    /// health had not moved — true, unhelpful, and it taught the whole population to shun a creature nobody
    /// had touched.
    /// </para>
    /// </summary>
    public static int CloseMs { get; set; } = 30000;

    /// <summary>When anybody in this company was last within reach of the focus at all. See <see cref="CloseMs"/>.</summary>
    private long _closedTick;

    /// <summary>
    /// What the company killed, divided among whoever is standing there for it.
    ///
    /// <para>
    /// The share-out itself is <see cref="BotSpoils"/>'s and has been ready the whole time — it was written
    /// for the hunter's own corpse-rifling, which hands it a corpse when it finds one. A squad kill has
    /// nobody rifling anything: no member holds an undertaking about this creature, so the corpse was
    /// nobody's business. This is the one line that makes it somebody's.
    /// </para>
    ///
    /// <para>
    /// What it carried is priced for the whole population on the way past, exactly as a lone kill is, or the
    /// only creatures anybody ever learns the worth of are the small ones a bot can take by itself — which
    /// is precisely backwards.
    /// </para>
    /// </summary>
    private void Spoils(Mobile fallen)
    {
        var collector = Contact ?? Leader;
        var map = Map;

        if (fallen == null || collector?.Self is not { Deleted: false, Alive: true } || map == null)
        {
            return;
        }

        var corpse = BotQuarry.Remains(map, _fell, fallen);

        BotQuarry.Release(fallen);

        if (corpse == null)
        {
            return;
        }

        var coin = 0;
        var lying = corpse.Items;

        for (var i = 0; i < lying.Count; i++)
        {
            if (lying[i] is Gold gold)
            {
                coin += gold.Amount;
            }
        }

        BotSpoils.Share(this, collector, corpse);

        BotQuarry.Paid(fallen.GetType(), coin);
    }

    /// <summary>
    /// Works out which of the three states the squad is in.
    ///
    /// Scouting is entered from the one situation the first version described exactly: arrived somewhere and
    /// met nobody. Eight bots sent to the same coordinate stand in a heap, see the same eight tiles as each
    /// other, and the corner of the ground where the spawner is quietly refilling is watched by nobody — for
    /// the five to ten minutes a spawn timer takes.
    /// </summary>
    private void Settle()
    {
        if (Focus is { Deleted: false, Alive: true })
        {
            Stance = BotSquadStance.Fighting;

            return;
        }

        var leader = Leader?.Self;

        Stance = leader != null && Leader.Journey?.Active != true
            ? BotSquadStance.Scouting
            : BotSquadStance.Marching;
    }

    /// <summary>
    /// Puts everybody where they belong, when where they belong has changed.
    ///
    /// Guarded on three things rather than run every beat, because each station ends in a path search: the
    /// anchor drifting, the state changing, and the roster changing. Between those, everybody is already
    /// walking to the right place and there is nothing to say.
    /// </summary>
    private void Station()
    {
        var map = Map;

        if (map == null)
        {
            return;
        }

        var anchor = Anchor;

        var moved = Math.Max(Math.Abs(anchor.X - _stationedAt.X), Math.Abs(anchor.Y - _stationedAt.Y));

        var all = moved >= ReformDistance || _stationedStance != Stance || _stationedCount != _members.Count;

        // Each round of this ends in a path search per member, and the fighting stance now re-stations
        // whoever has stopped short as well as whoever the anchor has walked away from — so it is worth
        // saying how often at most, once, here.
        if (Core.TickCount - _stationedTick < RestationMs)
        {
            return;
        }

        if (all)
        {
            _stationedAt = anchor;
            _stationedStance = Stance;
            _stationedCount = _members.Count;
        }

        var sent = false;

        for (var i = 0; i < _members.Count; i++)
        {
            var member = _members[i];
            var journey = member.Journey;

            if (journey == null)
            {
                continue;
            }

            // <b>The half of stationing that was missing: somebody who has stopped and is not there yet.</b>
            // Stations were worked out only when the anchor moved, so a member whose walk to its place was
            // dropped — a blocked tile, a plan that got no closer — simply stood where it gave up for the rest
            // of the fight, and nothing ever asked it again. That is a bot in a fight doing nothing, which
            // reads in the log exactly like a bot in a fight.
            if (!all && !Stranded(member, journey))
            {
                continue;
            }

            // The leader is not stationed on itself. It goes where it was going, and everybody else is
            // arranged around wherever that turns out to be.
            if (ReferenceEquals(member, Leader) && Stance != BotSquadStance.Fighting)
            {
                continue;
            }

            // <b>A member that can already hit the thing is standing in a good place, and the arithmetic has
            // nothing better to offer it.</b> The wholesale re-station fires whenever the focus drifts two
            // tiles, which a creature in a melee does every second or so — and every one of those walks
            // resets a shooter's stillness clock, which <c>BaseRanged.OnSwing</c> requires a full second of
            // on this era. So the archers were marched back and forth across one tile for the length of the
            // fight and never fired once, while the company reported them in the fight the whole time:
            // Squad 7 broke off from three creatures in a row on 04.09.2026 with "2 of 3 able to strike,
            // 0 blind, 0 refused, nearest 5 tiles off" and the target's health exactly where it started.
            //
            // Unsteady counts as a good place too, and that is the point rather than an oversight: a bot
            // that has just moved wants the one thing a re-station cannot give it, which is to be left where
            // it is. Anybody the ring has to fix — too far, or with no line — is untouched by this and is
            // moved as before.
            if (Stance == BotSquadStance.Fighting && Strike(member, out _) is BotBlow.Able or BotBlow.Unsteady)
            {
                continue;
            }

            var where = Stance == BotSquadStance.Scouting
                ? BotScatter.PatchFor(this, member)
                : BotFormation.StationFor(this, member);

            if (where == Point3D.Zero)
            {
                continue;
            }

            // <b>A fighting station is a tile, and it was being delivered with a tile of slack.</b> The eight
            // places round a creature are worked out one to a blade precisely so that no two of them collide;
            // <c>BotArrival.Beside</c> then says "anywhere within one tile of that will do", and one tile of
            // slack on a ring one tile across means two blades can both be satisfied standing on the same
            // ground. Watched from a client it is unmistakable: the second man stands behind the first,
            // swinging at his back. Marching and sweeping keep the slack, where a tile either way is genuinely
            // nothing and exactness would cost a search per member per beat.
            var precision = Stance == BotSquadStance.Fighting ? BotArrival.Exactly : BotArrival.Beside;

            // Rebase, never Begin. A station is what the bot fundamentally is doing; whatever is happening to
            // it right now sits on top, and it has to survive the formation shifting — which is precisely the
            // moment a fight starts. Beginning here would cancel the fight in order to tell the bot where to
            // stand for it.
            journey.Rebase(map, where, precision, Stance == BotSquadStance.Scouting ? "sweep" : "station");
            sent = true;
        }

        if (!all && !sent)
        {
            return;
        }

        _stationedTick = Core.TickCount;

        // <b>The ring turns when the fight is not working, and tying it to anything else left it never
        // turning at all.</b> It used to turn only on the rounds that chased a stopped member up — and those
        // rounds cannot happen while the creature is moving, because a moving focus drifts two tiles every
        // beat, which re-stations everybody wholesale before anyone is ever judged to have stopped. Three
        // stalemates in a row said so in the same words: <c>nearest 2 tiles off, 0 tries at standing right</c>
        // — five blades a step short of a wraith, each sent back to the identical blocked tile every second
        // and a half for ninety seconds, and the counter honestly reporting that nothing else was ever tried.
        //
        // The right trigger was already being measured for another purpose: health that will not move. If the
        // thing is not being hurt, where everybody is standing is a hypothesis that has been tested and
        // failed, so try the next arrangement. When blows are landing, nothing turns.
        if (Stance == BotSquadStance.Fighting && Core.TickCount - _focusProgressTick >= RotateAfterMs)
        {
            Attempt++;
        }
    }

    /// <summary>
    /// A member that has stopped walking and is not near enough to be part of the fight.
    ///
    /// <para>
    /// Judged against its own rank's distance rather than one number for everybody, or an archer holding its
    /// station at five tiles counts as stranded and is sent to it again every couple of seconds for the whole
    /// fight. Only bots that have stopped are asked: one that is still walking is already dealing with it.
    /// </para>
    /// </summary>
    private bool Stranded(IBotSquadMember member, BotJourney journey)
    {
        if (Stance != BotSquadStance.Fighting || Focus is not { Deleted: false, Alive: true })
        {
            return false;
        }

        // <b>Not merely "has stopped": also "is still trying and getting nowhere".</b> The first evening of
        // this rule produced a company that stood two tiles off a wraith for ninety seconds with the counter
        // reading nought tries at standing right — because every member's walk was still live, replanning at
        // a place it could not finish, so not one of them ever counted as having stopped. A journey that has
        // run out of plans that get closer already knows it is beaten; this is the one caller that can do
        // anything about it.
        if (journey.Active && !journey.Hopeless)
        {
            return false;
        }

        var body = member.Self;

        if (body is not { Deleted: false, Alive: true } || body.Map != Focus.Map)
        {
            return false;
        }

        // <b>Near enough and unable to swing is the same problem as too far away, and only one of the two
        // was ever asked about.</b> A member with no line to the focus is standing where the engine will not
        // let it fight from — see BotFormation.Sighted — so it wants another tile exactly as a member who
        // stopped short does. Without this, the one arrangement the formation has already proved useless is
        // the one it holds: the station is satisfied, so nobody re-stations it, for the whole fight.
        //
        // A shooter that has merely moved too recently is <em>not</em> stranded: the cure for that is to
        // stand still, and sending it anywhere is the disease. See BotBlow.Unsteady.
        return Strike(member, out _) is BotBlow.Far or BotBlow.Blind;
    }

    /// <summary>A unit step from one point towards another, on the eight compass lines.</summary>
    private static (int X, int Y) Unit(Point3D from, Point3D to) =>
        (Math.Sign(to.X - from.X), Math.Sign(to.Y - from.Y));

    /// <summary>Which way a mobile is looking, as a unit offset.</summary>
    private static (int X, int Y) Facing(Direction direction) =>
        (direction & Direction.Mask) switch
        {
            Direction.North => (0, -1),
            Direction.Right => (1, -1),
            Direction.East => (1, 0),
            Direction.Down => (1, 1),
            Direction.South => (0, 1),
            Direction.Left => (-1, 1),
            Direction.West => (-1, 0),
            _ => (-1, -1)
        };

    public override string ToString() =>
        $"squad {Id}: {Count} under {Leader?.Self?.Name ?? "nobody"}, {Stance}{(Focus != null ? $" vs {Focus.Name}" : "")}";
}
