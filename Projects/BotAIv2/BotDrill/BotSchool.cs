using System;
using System.Collections.Generic;
using Server.Logging;

namespace Server.BotAI.V2;

/// <summary>
/// The training field: where it is, who is standing on it, where each of them stands, and what an hour of
/// being shouted at is actually worth.
///
/// <para>
/// <b>One session at a time, held by one captain, and everybody else derives their place from it.</b> This
/// is the squad's own rule applied to a different problem: the shared facts are the ground, the roster and
/// the order the roster is in, and every student's station falls out of those three identically for
/// everybody who asks. Nobody is told where to stand. Two students cannot be given the same tile, a student
/// that dies and is replaced does not orphan an assignment, and there is no message anywhere.
/// </para>
///
/// <para>
/// <b>The teaching is a rate, not an event, and that is what makes the captain's walking matter.</b> A
/// lesson that granted its points on arrival would be a shop that sells skill, and the captain pacing the
/// ranks would be scenery. Points are handed out per beat, to whoever the captain is near <em>at that
/// beat</em> — so a student at the far corner of the block genuinely learns less than the one he is standing
/// over, and he genuinely has to walk to fix it. See <see cref="Teach"/>.
/// </para>
/// </summary>
public static class BotSchool
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotSchool));

    /// <summary>
    /// Where the field is, in the population's own facet.
    ///
    /// <para>
    /// Ordered as (1479, 1629, 20). The facet is the population's rather than a number of its own: this era
    /// is Renaissance, which has Felucca and Trammel and nothing else, and the bots all live on one of them.
    /// </para>
    /// </summary>
    public static Point3D Ground { get; set; } = new(1479, 1629, 20);

    /// <summary>
    /// Tiles between one student and the next.
    ///
    /// <para>
    /// Two, which is what makes the block a chessboard rather than a huddle: every station lands on a tile of
    /// the same parity as the ground, so the occupied tiles are exactly the light squares and the dark ones
    /// are the aisles the captain walks down. It is also the smallest spacing at which a bot can be walked
    /// past without being shoved off its tile.
    /// </para>
    /// </summary>
    public static int Pace { get; set; } = 2;

    /// <summary>How many students stand in one rank before a new rank is started behind it.</summary>
    public static int Rank { get; set; } = 3;

    /// <summary>Most students one captain will take at once.</summary>
    public static int Most { get; set; } = 6;

    /// <summary>How long a captain waits on the field for people to arrive before teaching begins.</summary>
    public static int GatherMs { get; set; } = 90000;

    /// <summary>How long one lesson runs once it has started.</summary>
    public static int LessonMs { get; set; } = 600000;

    /// <summary>How often points are handed out, and how often the captain moves to a new place to stand.</summary>
    public static int BeatMs { get; set; } = 15000;

    /// <summary>How close the captain has to be to a student for that student to get the whole of a beat.</summary>
    public static int Voice { get; set; } = 4;

    /// <summary>
    /// What a beat is worth to a student the captain is nowhere near.
    ///
    /// Not nought. A bot swinging at a post learns something on this shard whether or not anybody is
    /// watching, and a zero here would make the far corner of the block worthless rather than worse.
    /// </summary>
    public static double Distant { get; set; } = 0.3;

    /// <summary>Skill points a beat, at the very start of a skill, with the captain standing over you.</summary>
    public static double Rate { get; set; } = 0.4;

    /// <summary>
    /// The bottom of the curve: where "you know nothing about this" is reckoned from.
    ///
    /// Thirty, because that is where the engine's own titles start — below it a skill has no name at all —
    /// and reckoning the curve from nought would make the first twenty points, which every bot is born
    /// holding, count as progress nobody made.
    /// </summary>
    public static double Floor { get; set; } = 30.0;

    /// <summary>
    /// The least a beat is ever worth, as a share of the full rate, however close to the ceiling a student is.
    ///
    /// <para>
    /// Without it the curve is an asymptote and the last point before Expert takes all night — which reads,
    /// from outside, exactly like a lesson that has silently stopped working. A floor turns "never arrives"
    /// into "arrives slowly", and those are different things to watch.
    /// </para>
    /// </summary>
    public static double LeastRoom { get; set; } = 0.12;

    /// <summary>
    /// What a lesson costs before the student's own standing is taken into account.
    ///
    /// <para>
    /// <b>Forty was giving it away, and the arithmetic that says so is the shard's own.</b> A class hands out
    /// points, and this island has always valued a skill point at <c>BotYield.GoldPerSkillPoint</c> — five
    /// hundred gold. A lesson that delivers three or four of them for forty is not a price, it is a subsidy,
    /// and the whole population would rationally do nothing else. It should still be a good bargain — that is
    /// the entire reason a bot would stand still for ten minutes — but it has to be an <em>investment</em>,
    /// and the money has to be real, because the captain living off fees is the loop that was asked for.
    /// </para>
    /// </summary>
    /// <para>
    /// <b>Lowered from 150 to 60 on 02.09.2026, and the cooldown below is what makes that safe.</b> The
    /// paragraph above is right that a cheap lesson the whole population can take all day is a subsidy
    /// rather than a price. The cure it chose was an expensive lesson; the cheaper and truer one is a lesson
    /// nobody may take twice in a day. With <see cref="RestMs"/> in place the price no longer has to do the
    /// rationing, so it can go back to being what a bot can actually afford out of an afternoon's work —
    /// measured that day, the captain was drawing 1687 gold from a handful of lessons in half an hour while
    /// every other trade on the shard earned in the hundreds.
    /// </para>
    /// </summary>
    public static int Fee { get; set; } = 60;

    /// <summary>
    /// What each point of the student's existing skill adds to the bill.
    ///
    /// <para>
    /// The better you already are, the dearer the next point — which is true of the teaching as well, since
    /// the curve gives less the higher you stand. Ordered up from eight to fifty-five on 25.08.2026: at
    /// eight, a journeyman's lesson came to three hundred gold for points this island values at five hundred
    /// apiece, and half the shard would have reached Expert for pocket change. At fifty-five, a
    /// fifty-skill warrior pays about eleven hundred — a real investment out of real earnings, which is what
    /// makes the captain's income real too.
    /// </para>
    /// </summary>
    public static int FeePerPoint { get; set; } = 20;

    /// <summary>
    /// What a lesson in magic costs as a multiple of the same lesson in arms. See <see cref="Bill"/>.
    ///
    /// Half again. Enough that a sage earns visibly more per class than a captain does and that a mage has to
    /// have worked for it; not so much that the one build already short of money is priced out of the only
    /// school it can attend.
    /// </summary>
    public static double MagicFee { get; set; } = 1.5;

    /// <summary>
    /// How long a student must wait before it may be taught again. A day.
    ///
    /// <para>
    /// <b>A cooldown rather than a price, because a price only rations the poor.</b> A bot that can afford
    /// the fee could take a lesson every time the field opened, and the ones with money did: the captain's
    /// income was the largest single flow on the shard and it came out of the same few pockets over and
    /// over. One lesson a day makes teaching a thing that happens to a bot rather than a thing a bot does
    /// for a living, and it lets the fee come down to something an ordinary afternoon pays for.
    /// </para>
    ///
    /// <para>
    /// Held in memory and therefore per run of the shard, which on this shard is a good deal shorter than a
    /// day. That is the intended reading: not "once per calendar day" but "once, and then get back to work".
    /// </para>
    /// </summary>
    public static int RestMs { get; set; } = 86400000;

    private static readonly Dictionary<Serial, long> _taught = [];

    /// <summary>Whether this student may be taught at all yet. Asked before a lesson is ever offered.</summary>
    public static bool Rested(BotMobile student) =>
        student == null
        || !_taught.TryGetValue(student.Serial, out var when)
        // By subtraction, never by comparison: on some hosts the tick counter is the machine's uptime and
        // can wrap. See dev-docs/tick-counts.md.
        || Core.TickCount - when >= RestMs;

    /// <summary>This student has had its lesson. Called where the fee is actually paid, not where it is offered.</summary>
    public static void Learned(BotMobile student)
    {
        if (student != null)
        {
            _taught[student.Serial] = Core.TickCount;
        }
    }

    /// <summary>Students turned away because they had already been taught today.</summary>
    public static long Rested_Away { get; internal set; }

    /// <summary>Sessions opened, students taught, points handed out and fees taken.</summary>
    public static long Sessions { get; private set; }

    public static long Empty { get; private set; }

    public static long Taught { get; private set; }

    public static double Points { get; private set; }

    public static long Fees { get; private set; }

    // ---- Every beat that taught nothing, counted apart. ------------------------------------------
    //
    // <b>A fee is paid up front and the teaching is what is bought, so a beat that gives nothing is the
    // one thing this office must be able to explain.</b> Six lessons paid for at 1585gp each and 1.7
    // points handed out between them, on the afternoon of 27.08.2026 — a fortieth of what the auction
    // was promised when it chose the work. <see cref="Teach"/> had four separate ways to return nought
    // and not one of them left a mark, so "the captain is a fraud", "the students are already as good as
    // he is" and "they wandered out of earshot" were the same log.

    /// <summary>Beats where the student had nothing left this captain could teach.</summary>
    public static long Nothing { get; private set; }

    /// <summary>Beats where the student already stood at or above the captain in the named skill.</summary>
    public static long Levelled { get; private set; }

    /// <summary>Beats taught at arm's length, at <see cref="Distant"/> of the gain.</summary>
    public static long Shouted { get; private set; }

    /// <summary>Beats that did teach something.</summary>
    public static long Beats { get; private set; }

    /// <summary>The captain holding the field, or null when nobody is teaching.</summary>
    public static BotMobile Master { get; private set; }

    /// <summary>Whether the field is open to be joined right now.</summary>
    public static bool Gathering { get; private set; }

    private static readonly List<BotMobile> _students = [];

    public static IReadOnlyList<BotMobile> Students => _students;

    /// <summary>
    /// The field as the shard can actually stand on it, which is not always the field as it was written down.
    ///
    /// <para>
    /// <b>A coordinate read off a world map is a place a person is standing; it is not necessarily a place a
    /// path exists to.</b> The ordered ground is at height twenty with earth all round it — a platform — and
    /// a captain that walked up to the side with a rise on it got there, while one that ended up beneath it
    /// reported "could not get one tile closer" from four tiles away and gave the class up. Two classes held
    /// against six refusals, and every number in the failure was correct.
    /// </para>
    ///
    /// <para>
    /// So the height is asked of the map rather than trusted from the file. <c>BotStep.Settle</c> is the same
    /// question the hunt puts to every patch of ground it thinks about walking to, and it answers with the
    /// height a body would actually stand at. If that disagrees with what was ordered, the disagreement is
    /// said once, plainly, because a training field silently ten feet below the one somebody chose is exactly
    /// the sort of thing that looks like nothing at all.
    /// </para>
    /// </summary>
    public static Point3D Standing(Map map)
    {
        if (_settled || map == null || map == Map.Internal)
        {
            return Ground;
        }

        _settled = true;

        if (!BotStep.Settle(map, Ground.X, Ground.Y, out var z))
        {
            logger.Warning(
                "The training field at ({X}, {Y}) is not ground a bot can stand on at all; it is left as ordered",
                Ground.X,
                Ground.Y
            );

            return Ground;
        }

        if (z == Ground.Z)
        {
            logger.Information("The training field at ({X}, {Y}, {Z}) is standable as ordered", Ground.X, Ground.Y, Ground.Z);

            return Ground;
        }

        logger.Information(
            "The training field was ordered at ({X}, {Y}, {Was}) but a bot stands there at {Is}; the field is moved to where feet actually go",
            Ground.X,
            Ground.Y,
            Ground.Z,
            z
        );

        Ground = new Point3D(Ground.X, Ground.Y, z);

        return Ground;
    }

    private static bool _settled;

    /// <summary>A captain has taken the field. Anybody who wants teaching may now offer to come.</summary>
    public static bool Open(BotMobile master)
    {
        if (master is not { Deleted: false, Alive: true })
        {
            return false;
        }

        if (Master is { Deleted: false } && !ReferenceEquals(Master, master))
        {
            return false;
        }

        Master = master;
        Gathering = true;
        _students.Clear();

        Sessions++;

        logger.Information(
            "{Name} has opened the training field at ({X}, {Y}) and is calling for warriors and archers",
            master.Name,
            Ground.X,
            Ground.Y
        );

        return true;
    }

    /// <summary>The joining window has closed; whoever is here is who is being taught.</summary>
    public static void Begin()
    {
        Gathering = false;

        if (_students.Count == 0)
        {
            Empty++;
        }
    }

    /// <summary>The field is clear again.</summary>
    public static void Close()
    {
        Master = null;
        Gathering = false;
        _students.Clear();
    }

    /// <summary>Whether this bot could be taught anything at all by whoever is holding the field.</summary>
    public static bool Teachable(BotMobile student) => Teachable(Master, student);

    /// <summary>
    /// Whether this bot could be taught anything by <em>this</em> captain, asked without a field being open.
    ///
    /// <para>
    /// <b>The master is a parameter rather than the one on the field, and that is not tidiness.</b> A captain
    /// deciding whether to open a school has to ask this about twenty bots while nobody holds the field at
    /// all. Answering that by briefly installing itself as the master — which is how this was first written
    /// — is a query that mutates the thing it is querying: any throw between the two assignments leaves the
    /// shard believing a class is running that nobody is teaching, and every student on the island can see
    /// it. A question should not be able to leave a mark. See <c>BotUpkeep</c> in <c>BotResolve.Offered</c>
    /// for the same lesson learned on the other side of the shard the same day.
    /// </para>
    /// </summary>
    public static bool Teachable(BotMobile master, BotMobile student) =>
        student is { Deleted: false, Alive: true }
        && Suits(master, student.Class)
        && !ReferenceEquals(student, master)
        && Lacking(master, student) != null;

    /// <summary>
    /// Whether this sort of bot is one this master's office is for.
    ///
    /// <para>
    /// <b>One place, because it was two and they were copies.</b> The rule "a student is Melee or Ranged"
    /// was written out in the proposer and again in <see cref="Teachable"/>, which is this project's most
    /// expensive recurring shape — and it had to be touched the moment a second kind of master existed. Asked
    /// of the master's office rather than named as a list: a captain teaches what a captain knows, a sage
    /// teaches what a sage knows, and neither needs a table.
    /// </para>
    ///
    /// <para>
    /// The medic is on the sage's side of the line and not on nobody's. A healer carries a book, spends mana
    /// and lives by what it knows, and leaving it out would have left the shard exactly one class that no
    /// master anywhere may teach — which is the gap this whole office was added to close.
    /// </para>
    /// </summary>
    public static bool Suits(BotMobile master, BotClass klass)
    {
        if (klass == null || master?.Class == null)
        {
            return false;
        }

        // A master is not a student, of its own school or of the other one.
        if (klass.Leads || klass.Tutors)
        {
            return false;
        }

        return master.Class.Tutors
            ? klass.Role is BotRole.Caster or BotRole.Medic
            : klass.Role is BotRole.Melee or BotRole.Ranged;
    }

    /// <summary>
    /// The skill this student would be taught: the one it is furthest behind the captain in.
    ///
    /// <para>
    /// <b>Chosen by the gap rather than named in a list, and the ceiling comes out of the same question.</b>
    /// A captain teaches up to its own standing and no further — so "what may be taught" and "how far" are
    /// one fact asked once, and there is no second constant anywhere saying "Expert" that could drift away
    /// from what the captain actually knows. It has to be a skill the student's own class wants, or the
    /// field turns into a place where archers are taught to swing swords they will never carry.
    /// </para>
    /// </summary>
    public static SkillName? Lacking(BotMobile student) => Lacking(Master, student);

    /// <summary>The same question put to a named captain, so it can be asked before a field is opened.</summary>
    public static SkillName? Lacking(BotMobile master, BotMobile student)
    {
        var klass = student?.Class;

        if (master is not { Deleted: false } || klass == null)
        {
            return null;
        }

        var teachable = master.Class?.Skills;

        if (teachable == null)
        {
            return null;
        }

        SkillName? chosen = null;
        var gap = 0.0;
        var chosenTrade = false;

        for (var i = 0; i < teachable.Count; i++)
        {
            var skill = teachable[i].Skill;

            if (!klass.Wants(skill))
            {
                continue;
            }

            var behind = master.Skills[skill].Base - student.Skills[skill].Base;

            // A tenth of a point is not a gap, and teaching one would charge a fee for nothing.
            if (behind < 0.1)
            {
                continue;
            }

            var trade = Trade(student, skill);

            // <b>The trade first, and the widest gap only among equals.</b> Ranked on the gap alone — which
            // is how this was first written — the captain taught every single pupil <em>Healing</em>: it
            // holds seventy-two of it and a young warrior holds almost none, so the arithmetic was right and
            // the answer was absurd. Six warriors and archers stood on a drill field on 25.08.2026 paying to
            // be taught first aid by a man with a bow. What a fighter is short of is not what it is worst at.
            if (chosen == null || (trade && !chosenTrade) || (trade == chosenTrade && behind > gap))
            {
                chosen = skill;
                gap = behind;
                chosenTrade = trade;
            }
        }

        return chosen;
    }

    /// <summary>
    /// Whether this skill is what the student actually fights with.
    ///
    /// The weapon in its hand first and its class's declared trade second, because the weapon is a roll and
    /// the class's answer can be null — a plain warrior has no trade beyond fighting and which blade it
    /// swings is genuinely decided at birth.
    /// </summary>
    private static bool Trade(BotMobile student, SkillName skill) =>
        student.Bond?.Weapon?.Skill == skill || student.Class?.MainSkill == skill;

    /// <summary>What this student is charged for the lesson, from what it already knows.</summary>
    public static int Bill(BotMobile student) => Bill(Master, student);

    /// <summary>The same bill from a named captain, for a captain working out whether a class is worth calling.</summary>
    public static int Bill(BotMobile master, BotMobile student)
    {
        var skill = Lacking(master, student);

        if (skill == null)
        {
            return 0;
        }

        var held = Math.Max(Floor, student.Skills[skill.Value].Base);

        // <b>A magic lesson costs more, and the reason is the student rather than the teacher.</b> What a
        // point of Magery is worth to a mage is not what a point of Tactics is worth to a warrior: a caster's
        // whole output is rationed by what it knows, so the same point buys more and is worth paying more
        // for. The multiplier lives on the office rather than on the skill, because it is a fact about which
        // school this is.
        var rate = master.Class is { Tutors: true } ? MagicFee : 1.0;

        return (int)((Fee + (held - Floor) * FeePerPoint) * rate);
    }

    /// <summary>Puts a student on the roster if there is room and the field is still open.</summary>
    public static bool Enrol(BotMobile student)
    {
        if (!Gathering || _students.Count >= Most || student == null || _students.Contains(student))
        {
            return false;
        }

        _students.Add(student);

        // Sorted so that every bot deriving a station from this list derives the same one, whatever order
        // they happened to arrive in and whatever order the list is read in later.
        _students.Sort(static (a, b) => a.Serial.Value.CompareTo(b.Serial.Value));

        return true;
    }

    public static void Leave(BotMobile student) => _students.Remove(student);

    /// <summary>Whether this bot is on the roster of the session being held.</summary>
    public static bool Holds(BotMobile student) => _students.Contains(student);

    /// <summary>
    /// Where one student stands: a chessboard, centred on the ground, filled rank by rank.
    ///
    /// <para>
    /// Derived from the roster's order and nothing else, so it is the same answer whoever asks and whenever.
    /// A student's own index is its place — there is no assignment to lose, no message to miss, and two of
    /// them can never be sent to the same tile.
    /// </para>
    /// </summary>
    public static Point3D Station(BotMobile student)
    {
        var index = _students.IndexOf(student);

        return index < 0 ? Ground : Station(index, _students.Count);
    }

    /// <summary>The station at one index of a block of this size. Pure arithmetic; no state is read.</summary>
    public static Point3D Station(int index, int count)
    {
        var ranks = Math.Max(1, (count + Rank - 1) / Rank);

        var row = index / Rank;
        var column = index % Rank;

        // How many stand in this rank, so that a short last rank is centred under the ones above it rather
        // than hanging off one end.
        var wide = Math.Min(Rank, count - row * Rank);

        var x = Ground.X + (column - (wide - 1) / 2.0) * Pace;
        var y = Ground.Y + (row - (ranks - 1) / 2.0) * Pace;

        return new Point3D((int)Math.Round(x), (int)Math.Round(y), Ground.Z);
    }

    /// <summary>
    /// Where the captain stands at this point in the lesson: a circuit of the block, one place per beat.
    ///
    /// <para>
    /// A ring rather than a path through the middle, and the two are not the same lesson. Walking between
    /// the ranks would put the captain on a station, shove a student off its tile, and re-form the whole
    /// block behind him. Walking round the outside keeps every student where it was put and still brings him
    /// within earshot of each rank in turn, which is the thing that has to be true for the arithmetic below
    /// to mean anything.
    /// </para>
    /// </summary>
    public static Point3D Post(int turn, int count)
    {
        var ranks = Math.Max(1, (count + Rank - 1) / Rank);

        var wide = Math.Min(Math.Max(1, count), Rank);

        // Half the block plus a pace of clearance, so the ring never lands on a station.
        var out_x = (int)Math.Round((wide - 1) / 2.0 * Pace) + Pace;
        var out_y = (int)Math.Round((ranks - 1) / 2.0 * Pace) + Pace;

        // Eight places round the ring, taken in order, so the captain circles rather than jumping about.
        var (dx, dy) = (turn % 8) switch
        {
            0 => (-out_x, -out_y),
            1 => (0, -out_y),
            2 => (out_x, -out_y),
            3 => (out_x, 0),
            4 => (out_x, out_y),
            5 => (0, out_y),
            6 => (-out_x, out_y),
            _ => (-out_x, 0)
        };

        return new Point3D(Ground.X + dx, Ground.Y + dy, Ground.Z);
    }

    /// <summary>
    /// One beat of teaching for one student, and the whole formula is here.
    ///
    /// <para>
    /// Three factors on a base rate, and each of them answers a different question a person would ask about
    /// a lesson. <b>Room</b> is how much there is left to learn: full at the bottom of a skill, tapering as
    /// the student closes on the captain's own standing, and floored so the last point arrives slowly rather
    /// than never. <b>Attention</b> is whether the captain is actually near enough to be teaching this one
    /// right now — the reason he walks. And the ceiling is not a factor at all but a hard stop: a captain
    /// cannot take anybody past what he knows himself, which on this shard means Expert, because that is
    /// what he is.
    /// </para>
    ///
    /// <para>
    /// The gain is put on the bot with <c>Skills[...].Base</c> directly rather than through a use check. That
    /// is a deliberate departure from how every other point on this shard is earned — the engine's own
    /// gain-on-use is what makes a bot's progress real — and it is what being <em>taught</em> means: the
    /// hour is spent, the fee is paid, and the points are the thing bought. A use check here would make the
    /// fee a lottery ticket.
    /// </para>
    /// </summary>
    /// <returns>Points actually added, which is nought when there is nothing left to give.</returns>
    public static double Teach(BotMobile student)
    {
        var master = Master;

        if (master is not { Deleted: false, Alive: true } || student is not { Deleted: false, Alive: true })
        {
            return 0.0;
        }

        var which = Lacking(student);

        if (which == null)
        {
            Nothing++;

            return 0.0;
        }

        var skill = student.Skills[which.Value];
        var ceiling = master.Skills[which.Value].Base;

        if (skill.Base >= ceiling)
        {
            Levelled++;

            return 0.0;
        }

        var span = Math.Max(1.0, ceiling - Floor);
        var room = Math.Clamp((ceiling - skill.Base) / span, LeastRoom, 1.0);

        var near = master.InRange(student.Location, Voice);
        var attention = near ? 1.0 : Distant;

        if (!near)
        {
            Shouted++;
        }

        // Never past the captain's own standing, whatever the arithmetic came to.
        var gain = Math.Min(Rate * room * attention, ceiling - skill.Base);

        if (gain <= 0.0)
        {
            Levelled++;

            return 0.0;
        }

        Beats++;

        skill.Base += gain;

        Points += gain;

        // <b>Both of them are the better for it, and the student's half is not the fee coming back.</b>
        // Contentment on this shard is boredom and need — see BotMobile.Mood — and boredom falls when work
        // pays. A lesson pays the student in skill, which the shard already values at a known rate, and pays
        // the captain in coin. Valuing the student's hour with the shard's own number rather than a new one
        // is the point: if a point of skill is worth five hundred gold to every other piece of work, it is
        // worth five hundred here.
        student.Resolve.Urges.Paid(gain * BotYield.GoldPerSkillPoint);

        return gain;
    }

    /// <summary>A lesson has been paid for. Counted here so the fee and the teaching are read together.</summary>
    public static void Paid(int fee)
    {
        Fees += fee;
        Taught++;
    }

    public static string Describe() =>
        Sessions == 0
            ? "no class has ever been held"
            : $"{Sessions} classes held at ({Ground.X}, {Ground.Y}), {Empty} of them with nobody; {Taught} lessons paid for at {Fees}gp, {Points:F1} points handed out over {Beats} beats that taught something ({Nothing} found nothing left to teach, {Levelled} found the student level with the master, {Shouted} taught out of earshot)"
              + (Master is { Deleted: false } ? $"; {Master.Name} is on the field now with {_students.Count}" : "; the field is empty");

    public static void Forget()
    {
        Close();

        Sessions = 0;
        Empty = 0;
        Taught = 0;
        Points = 0.0;
        Fees = 0;
        Nothing = 0;
        Levelled = 0;
        Shouted = 0;
        Beats = 0;
    }
}
