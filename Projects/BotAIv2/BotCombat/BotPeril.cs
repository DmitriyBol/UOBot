using System;
using System.Collections.Generic;
using Server.Logging;

namespace Server.BotAI.V2;

/// <summary>
/// Where the shard is dangerous, learned from the only two facts that actually say so: where bots are being
/// hit, and where they are dying.
///
/// <para>
/// <b>Everything else about danger on this shard is a private opinion, and that is why this exists.</b>
/// <see cref="BotLedger"/> already remembers that a piece of work went badly in a place — but it lives on
/// one bot, it is keyed by trade as well as by ground, and it is a record of <em>disappointment</em> rather
/// than of harm. Fifteen bots each learning separately that the same field kills people is fifteen bots
/// learning it fifteen times, none of them able to tell anybody, and a captain deciding where to take a
/// company has to read a fact about the island rather than a mood of its own. So this is deliberately
/// shard-wide, deliberately not keyed by trade, and deliberately fed from the two hooks that cannot lie:
/// <c>OnDamage</c> and <c>OnDeath</c>.
/// </para>
///
/// <para>
/// <b>A frequency, never a total, and the difference decides everything the captain does.</b> A tally that
/// only rises names the graveyard for ever, because the graveyard has always been the worst place on the
/// map and always will be — which is a fact about history and not about tonight. What is wanted is where
/// blood is being spilt <em>lately</em>, so every reading decays towards nothing on its own clock: a square
/// that has gone quiet for an hour stops being the answer without anybody having to clear it, and a square
/// that has just killed two bots outranks one that killed ten yesterday.
/// </para>
///
/// <para>
/// <b>Squares rather than points, because a patrol cannot stand on a coordinate.</b> Harm arrives at exact
/// tiles and is useless at that resolution — thirty single blows scattered over a wood are one dangerous
/// wood, and a map keyed by tile would report thirty places each worth nothing. <see cref="Side"/> is the
/// one number that decides what "a place" means here.
/// </para>
/// </summary>
public static class BotPeril
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotPeril));

    /// <summary>
    /// How many tiles across one square is.
    /// </summary>
    /// <remarks>
    /// Wide enough that a running fight stays inside one, narrow enough that "go and deal with it" names a
    /// walkable errand rather than a region. Twenty-four is about eight seconds' run corner to corner, which
    /// is also roughly how far a squad's scouting knots spread — so a company standing in the middle of a
    /// square covers it, which is the property that makes the number right rather than merely round.
    /// </remarks>
    public static int Side { get; set; } = 24;

    /// <summary>
    /// How long it takes a square's reading to fall by half with nothing further happening in it.
    ///
    /// Twenty minutes. Long enough that a patrol dispatched to a square finds the trouble still there when it
    /// arrives, short enough that last night's massacre does not rule tonight.
    /// </summary>
    public static int HalfLifeMs { get; set; } = 1200000;

    /// <summary>What one blow landed on a bot adds to a square.</summary>
    public static double PerBlow { get; set; } = 1.0;

    /// <summary>
    /// What one death adds.
    ///
    /// <para>
    /// Twenty-five blows' worth, and the gap is meant to be enormous. Being hit is what happens all day to a
    /// population that fights for a living — every hunt is a hundred blows and none of them is a problem. A
    /// bot that did not come back is the only unambiguous evidence that a place is beyond whoever went
    /// there. Weighted evenly, the reading would be a map of where bots hunt rather than of where they die,
    /// which is nearly the opposite question.
    /// </para>
    /// </summary>
    public static double PerDeath { get; set; } = 25.0;

    /// <summary>
    /// How high a square's reading has to be before it is worth sending a company to at all.
    ///
    /// <para>
    /// <b>Set at one death, this never fired once, and the counter said so in as many words.</b> Twenty-one
    /// offers to the captain in five minutes, twenty-one of them "found nowhere dangerous enough", on
    /// twenty-seven blows across three squares and no deaths at all. The bar was written as "a death has
    /// happened here", which is a fact worth patrolling but a rare one — this population fights well and
    /// mostly wins. Twelve is a sustained beating instead: at a twenty-minute half-life it takes about four
    /// blows every ten minutes in one square to hold, which is somewhere bots are actually being hurt on a
    /// regular basis. A death still counts double it on its own.
    /// </para>
    /// </summary>
    public static double Worrying { get; set; } = 12.0;

    /// <summary>
    /// How far a square's reading is knocked down when a company has swept it.
    ///
    /// <para>
    /// <b>Not to nothing, and this is the difference between a patrol and a loop.</b> Cleared outright, a
    /// square reads as safe the instant the company arrives, the patrol ends, the captain looks again, and
    /// the same square — still full of whatever was killing people — is once more the worst on the map by
    /// the time the walk home is over. Knocked down, a swept square has to earn its way back up by hurting
    /// somebody again, which is the true test of whether the sweep worked.
    /// </para>
    /// </summary>
    public static double SweptTo { get; set; } = 0.25;

    /// <summary>Most squares remembered at once. Beyond this the calmest is forgotten.</summary>
    public static int MostSquares { get; set; } = 256;

    /// <summary>
    /// How long a square that a company could not work is left off the captain's list.
    ///
    /// <para>
    /// <b>This is the difference between a captain with a map and a captain with one square on it.</b>
    /// <see cref="Worst"/> answers with the highest reading, so a square that cannot be walked stays the
    /// answer for as long as it stays dangerous — and a patrol that fails there is offered the identical
    /// square within the second. Four marches on (1380, 1500) in thirty-six seconds on the night of
    /// 25.08.2026, and the rest of the island unpatrolled the whole while. The reading is <em>not</em>
    /// knocked down here: the square really is dangerous, and saying otherwise would be a lie told to
    /// every bot that reads this map. It is simply not offered again for a while.
    /// </para>
    /// </summary>
    public static int BaulkMs { get; set; } = 600000;

    /// <summary>
    /// How far down the list of dangerous squares a caller may be taken before it is told there is nowhere.
    ///
    /// Five, and it is a bound on work rather than a policy: each step past the first is another scan of the
    /// table, and a captain that would not go to any of the five worst places within reach is a captain with
    /// nothing to do.
    /// </summary>
    public static int Tries { get; set; } = 5;

    private sealed class Square
    {
        public Map Map;

        public int X;

        public int Y;

        public double Reading;

        public long Tick;

        public int Blows;

        public int Deaths;

        /// <summary>Whether a company has given this square up. A flag, never "is the tick zero" — see BotJourney.</summary>
        public bool Baulked;

        public long BaulkedTick;

        /// <summary>Whether somewhere in this square has been looked for yet. Terrain does not move, so once.</summary>
        public bool Sounded;

        /// <summary>Whether that search found anywhere at all, and where.</summary>
        public bool Standable;

        public Point3D Foot;
    }

    private static readonly Dictionary<(int Map, int X, int Y), Square> _squares = [];

    /// <summary>
    /// Squares already offered to the caller inside one <see cref="Worst"/> call. Kept between calls rather
    /// than built per call: this runs on the population's beat, and the answer is thrown away every time.
    /// </summary>
    private static readonly HashSet<(int Map, int X, int Y)> _passed = [];

    /// <summary>Blows and deaths ever recorded, for the summary. Not decayed: these are the raw evidence.</summary>
    public static long Blows { get; private set; }

    public static long Deaths { get; private set; }

    public static long Sweeps { get; private set; }

    /// <summary>Squares taken off the board outright by a harrowing. See <see cref="Cleared"/>.</summary>
    public static long Clearances { get; private set; }

    /// <summary>Squares a company set out for and could not work. A named nought: see <see cref="BaulkMs"/>.</summary>
    public static long Baulks { get; private set; }

    /// <summary>
    /// Squares with nowhere in them a body could stand — open water, and very little else.
    ///
    /// <para>
    /// <b>Squares, and counted once each, because the ground is looked at once each.</b> The first version of
    /// this number was times rather than squares and read 1807 in fifty minutes, which is a number about how
    /// often a captain thinks and not about the island at all. Counted so that "the island is quiet" and "the
    /// worst place on it is a coordinate in the sea" stay different facts.
    /// </para>
    /// </summary>
    public static long Unfooted { get; private set; }

    /// <summary>Something hit a bot here.</summary>
    public static void Struck(Map map, Point3D where)
    {
        Blows++;

        Add(map, where, PerBlow, blow: true);
    }

    /// <summary>A bot died here. The only unambiguous evidence this map has.</summary>
    public static void Fell(Map map, Point3D where)
    {
        Deaths++;

        Add(map, where, PerDeath, blow: false);
    }

    private static void Add(Map map, Point3D where, double weight, bool blow)
    {
        if (map == null || map == Map.Internal || weight <= 0.0)
        {
            return;
        }

        var key = Key(map, where);
        var now = Core.TickCount;

        if (!_squares.TryGetValue(key, out var square))
        {
            // Room is made by forgetting the calmest square rather than the oldest. An old square that is
            // still the worst on the map is the one thing this table must never lose.
            if (_squares.Count >= MostSquares)
            {
                Forget(now);
            }

            square = new Square { Map = map, X = key.X, Y = key.Y, Tick = now };
            _squares[key] = square;
        }

        square.Reading = Faded(square, now) + weight;
        square.Tick = now;

        if (blow)
        {
            square.Blows++;
        }
        else
        {
            square.Deaths++;
        }
    }

    /// <summary>
    /// The most dangerous square within reach that the caller will actually take, or nothing when the island
    /// is quiet enough to leave alone.
    /// </summary>
    /// <param name="map">Which facet. Squares never compare across one.</param>
    /// <param name="from">Where the company would be setting out from.</param>
    /// <param name="within">How far it is prepared to go, in tiles.</param>
    /// <param name="reading">What that square's reading came to, or nought when nothing is returned.</param>
    public static Point3D Worst(Map map, Point3D from, int within, out double reading) =>
        Worst(map, from, within, out reading, null);

    /// <summary>
    /// The same, with the caller allowed to refuse a square and be offered the next one down.
    ///
    /// <para>
    /// <b>The answer is a place a body can stand, and it did not used to be.</b> A square's middle is
    /// arithmetic — the cell's corner plus half a side — and arithmetic has no height, so this returned
    /// <c>(x, y, 0)</c> for every square on the map. On the plains north of Britain nought is the real
    /// ground and nothing looked wrong; on a hill it is thirty units under the grass, which is further than
    /// <see cref="BotArrival.PersonHeight"/>, so the walk could never arrive. The company marched, stood on
    /// the very tile it had been sent to, was told it had not arrived, redrew its route twelve times without
    /// getting a tile closer to a place it was standing on, and the patrol was failed with "no way through".
    /// Ten of eighteen patrols on the night of 25.08.2026, every one of them on high ground, while every
    /// patrol that finished was on ground that happens to lie at nought. <see cref="BotStep.Settle"/> is how
    /// the rest of this project turns two numbers into a place, and <c>BotHunter.Noisy</c> — reading this
    /// very table for a prowl — was already calling it. One caller settled the height and the other invented
    /// it, from the same square, on the same night.
    /// </para>
    ///
    /// <para>
    /// <b>And a refusal moves down the list rather than ending it.</b> The worst square is an opinion about
    /// danger and says nothing about whether a company can get there; a caller that can see further — a
    /// sealed pocket, a square already given up on — needs somewhere else to be offered, or the one square
    /// it cannot use is the only square it is ever shown.
    /// </para>
    /// </summary>
    /// <param name="fit">Asked of each candidate in turn. Null accepts the first that has ground under it.</param>
    public static Point3D Worst(Map map, Point3D from, int within, out double reading, Func<Point3D, bool> fit)
    {
        reading = 0.0;

        if (map == null || map == Map.Internal)
        {
            return Point3D.Zero;
        }

        var now = Core.TickCount;

        _passed.Clear();

        for (var tries = 0; tries < Tries; tries++)
        {
            (int Map, int X, int Y) at = default;
            Square worst = null;
            var highest = 0.0;

            foreach (var (key, square) in _squares)
            {
                if (square.Map != map || _passed.Contains(key))
                {
                    continue;
                }

                var faded = Faded(square, now);

                if (faded < Worrying || faded <= highest)
                {
                    continue;
                }

                var middle = Middle(square);

                // The larger of the two axes, which is how everything else on this shard measures a distance.
                if (Math.Max(Math.Abs(middle.X - from.X), Math.Abs(middle.Y - from.Y)) > within)
                {
                    continue;
                }

                highest = faded;
                worst = square;
                at = key;
            }

            if (worst == null)
            {
                return Point3D.Zero;
            }

            // Written down before anything can reject it, or the next turn of the loop finds the same square
            // and the caller is offered it five times instead of five squares.
            _passed.Add(at);

            if (worst.Baulked && now - worst.BaulkedTick < BaulkMs)
            {
                continue;
            }

            if (!Footing(map, worst))
            {
                continue;
            }

            var footed = worst.Foot;

            if (fit != null && !fit(footed))
            {
                continue;
            }

            reading = highest;

            return footed;
        }

        return Point3D.Zero;
    }

    /// <summary>
    /// Fewest deaths on a piece of ground before the Baron will take a company to it.
    ///
    /// <para>
    /// One, by order, and the number moved for a reason worth keeping. It was two, on the argument that one
    /// death is somebody's bad afternoon and two is the ground — which reads well and was wrong in practice
    /// for the same reason <see cref="Worrying"/> had to be lowered a day earlier: this population fights
    /// well and rarely dies, so a bar written in deaths is a bar that never clears. An hour of the evening of
    /// 26.08.2026 recorded 451 blows and no deaths at all.
    /// </para>
    ///
    /// <para>
    /// What makes one death safe to act on is that it is <em>one death on ground nobody has dealt with</em>,
    /// and the dead never fade. A square that killed somebody an hour ago is still a square that killed
    /// somebody; the only thing that takes it off the board is a company going there. That is the whole
    /// design, and at a bar of two it could not start.
    /// </para>
    /// </summary>
    public static int Deadly { get; set; } = 1;

    /// <summary>
    /// The ground that has killed the most people within reach, or nothing when no ground has taken
    /// <see cref="Deadly"/> of them.
    ///
    /// <para>
    /// <b>Ranked by the dead and not by the reading, which is the difference between this errand and a
    /// patrol.</b> <see cref="Worst"/> answers "where is it dangerous now" - a decaying frequency, exactly
    /// right for a captain deciding where a company should be standing before anything happens. This answers
    /// "where has it already gone wrong", which does not decay, cannot be reached by a hundred harmless
    /// scratches, and is not satisfied by the ground going quiet on its own. The two rankings disagree
    /// constantly and both are correct about their own question.
    /// </para>
    ///
    /// <para>
    /// <b>Over a neighbourhood, not over one cell, and that scale decides whether this ever fires at all.</b>
    /// A cell is <see cref="Side"/> tiles across because that is the resolution at which "a place" means
    /// something to a map. The company sent here walks a box three times that. Counted per cell, "two have
    /// died in this square" is very nearly unsatisfiable: this population fights well and mostly wins, and one
    /// hour of the evening of 26.08.2026 recorded 451 blows and not one death, while the nine deaths of the
    /// whole evening fell in five different cells with the two nearest of them twenty-seven tiles apart. Two
    /// bots killed twenty-seven tiles apart died on the same ground by any reading a person would give the
    /// word, and both would be inside the box the Baron walks. So the dead are counted over the ground he
    /// will actually work. The same mistake in the other direction is what the captain's own bar was lowered
    /// for - see <see cref="Worrying"/>, which used to be written as "a death has happened here" and never
    /// once fired.
    /// </para>
    /// </summary>
    /// <param name="spread">Half the width of the ground the caller will work. The dead are summed over it.</param>
    /// <param name="deaths">How many died on the ground returned, or nought when nothing is.</param>
    /// <param name="fit">Asked of each candidate in turn. Null accepts the first that has ground under it.</param>
    public static Point3D Deadliest(
        Map map,
        Point3D from,
        int within,
        int spread,
        out int deaths,
        Func<Point3D, bool> fit
    )
    {
        deaths = 0;

        if (map == null || map == Map.Internal)
        {
            return Point3D.Zero;
        }

        var now = Core.TickCount;

        _passed.Clear();

        for (var tries = 0; tries < Tries; tries++)
        {
            (int Map, int X, int Y) at = default;
            Square worst = null;
            var most = Deadly - 1;

            foreach (var (key, square) in _squares)
            {
                // Only a cell that has taken somebody can be the middle of a candidate. Every other cell is
                // reached as a neighbour, and starting from the empty ones would scan the whole table against
                // itself for no extra answer.
                if (square.Map != map || square.Deaths <= 0 || _passed.Contains(key))
                {
                    continue;
                }

                var middle = Middle(square);

                // The larger of the two axes, which is how everything else on this shard measures a distance.
                if (Math.Max(Math.Abs(middle.X - from.X), Math.Abs(middle.Y - from.Y)) > within)
                {
                    continue;
                }

                var total = Around(map, middle, spread);

                if (total <= most)
                {
                    continue;
                }

                most = total;
                worst = square;
                at = key;
            }

            if (worst == null)
            {
                return Point3D.Zero;
            }

            // Written down before anything can reject it, or the next turn of the loop finds the same ground
            // and the caller is offered it five times instead of five places.
            _passed.Add(at);

            if (worst.Baulked && now - worst.BaulkedTick < BaulkMs)
            {
                continue;
            }

            if (!Footing(map, worst))
            {
                continue;
            }

            var footed = worst.Foot;

            if (fit != null && !fit(footed))
            {
                continue;
            }

            deaths = most;

            return footed;
        }

        return Point3D.Zero;
    }

    /// <summary>How many have died within <paramref name="spread"/> tiles of a place, all cells counted.</summary>
    private static int Around(Map map, Point3D middle, int spread)
    {
        var total = 0;

        foreach (var square in _squares.Values)
        {
            if (square.Map != map || square.Deaths <= 0)
            {
                continue;
            }

            var at = Middle(square);

            if (Math.Max(Math.Abs(at.X - middle.X), Math.Abs(at.Y - middle.Y)) <= spread)
            {
                total += square.Deaths;
            }
        }

        return total;
    }

    /// <summary>
    /// How many of the dead within reach still lie on ground nobody has dealt with.
    ///
    /// <para>
    /// The dead are the one figure on this map that never decays and never falls, so this only ever comes
    /// down when somebody harrows the ground they died on - which makes it the single number that answers
    /// both halves of what the Baron is content by: the deaths of others, and the squares nobody has
    /// cleared. One number rather than two, because they are the same fact counted once.
    /// </para>
    /// </summary>
    public static int Unavenged(Map map, Point3D from, int within)
    {
        if (map == null || map == Map.Internal)
        {
            return 0;
        }

        var total = 0;

        foreach (var square in _squares.Values)
        {
            if (square.Map != map || square.Deaths <= 0)
            {
                continue;
            }

            var middle = Middle(square);

            if (Math.Max(Math.Abs(middle.X - from.X), Math.Abs(middle.Y - from.Y)) <= within)
            {
                total += square.Deaths;
            }
        }

        return total;
    }

    /// <summary>
    /// This square is finished with: forgotten outright, dead and all.
    ///
    /// <para>
    /// <b>The one thing <see cref="Swept"/> deliberately refuses to do, and the reasons do not conflict.</b> A
    /// patrol knocks a square down rather than clearing it because a patrol is a company standing in a place
    /// for a while — it proves nothing about whether the place is still full of what was killing people, so
    /// the square has to earn its way back onto the board by hurting somebody rather than by never having
    /// been visited. A harrowing is the opposite errand: twenty things dead or forty minutes of hunting
    /// inside seventy-five tiles, and what it has actually done is spend the square's contents. Leaving the
    /// dead on the count afterwards would send the Baron back to the same coordinates for ever, because the
    /// dead are exactly the number that never decays.
    /// </para>
    ///
    /// <para>
    /// So the row goes. Anything that happens there afterwards writes a new one, from nothing, which is the
    /// honest state of a place nobody has been hurt in since it was dealt with.
    /// </para>
    /// </summary>
    /// <param name="spread">
    /// Half the width of the ground that was worked. Everything inside it goes, not only the cell at the
    /// middle: the company walked the whole box, and clearing one cell of it would offer the Baron the
    /// neighbouring cell of the ground he has just spent the afternoon on.
    /// </param>
    public static int Cleared(Map map, Point3D where, int spread)
    {
        if (map == null || map == Map.Internal)
        {
            return 0;
        }

        _going.Clear();

        var blows = 0;
        var dead = 0;

        foreach (var (key, square) in _squares)
        {
            if (square.Map != map)
            {
                continue;
            }

            var middle = Middle(square);

            if (Math.Max(Math.Abs(middle.X - where.X), Math.Abs(middle.Y - where.Y)) > spread)
            {
                continue;
            }

            _going.Add(key);
            blows += square.Blows;
            dead += square.Deaths;
        }

        if (_going.Count == 0)
        {
            return 0;
        }

        for (var i = 0; i < _going.Count; i++)
        {
            _squares.Remove(_going[i]);
        }

        Clearances += _going.Count;

        logger.Information(
            "The ground around {Where} has been harrowed: {Count} squares off the board altogether, carrying {Blows} blows and {Deaths} deaths between them",
            where,
            _going.Count,
            blows,
            dead
        );

        return _going.Count;
    }

    /// <summary>
    /// Keys being removed by one clearance. Static and reused, like the passed set above: a dictionary cannot
    /// be written to while it is being walked, and a fresh list per harrowing is garbage for nothing.
    /// </summary>
    private static readonly List<(int Map, int X, int Y)> _going = [];

    /// <summary>
    /// A company has been through this square. Knocked down rather than cleared — see <see cref="SweptTo"/>.
    /// </summary>
    public static void Swept(Map map, Point3D where)
    {
        if (map == null || !_squares.TryGetValue(Key(map, where), out var square))
        {
            return;
        }

        Sweeps++;

        var was = Faded(square, Core.TickCount);

        square.Reading = was * SweptTo;
        square.Tick = Core.TickCount;

        // A company has just worked this ground, so whatever it was that could not be walked last time no
        // longer applies. Cleared here rather than left to time out, or a square swept an hour after it was
        // given up on stays off the list for the remainder of its baulk.
        square.Baulked = false;

        logger.Information(
            "The square around {Where} has been swept: it read {Was:F0} and now reads {Now:F0}, on {Blows} blows and {Deaths} deaths",
            where,
            was,
            square.Reading,
            square.Blows,
            square.Deaths
        );
    }

    /// <summary>
    /// A company set out for this square and could not work it — no way through to it, or nowhere in it that
    /// could be walked.
    ///
    /// <para>
    /// The reading is left exactly as it stands. This is not a sweep and must never read as one: the square
    /// is every bit as dangerous as it was, and the only thing learned is that this errand does not work.
    /// See <see cref="BaulkMs"/>.
    /// </para>
    ///
    /// <para>
    /// <b>Shard-wide, like everything else in this table, and that is on purpose.</b> The mark is left by a
    /// company but it is a fact about ground, so a lone bot reading this map for somewhere to prowl is kept
    /// off it too — five bots could not walk in there and one will not do better. It lapses on its own, and a
    /// prowl has its own sampled candidates to fall back on, so nothing starves waiting for it.
    /// </para>
    /// </summary>
    public static void Baulked(Map map, Point3D where)
    {
        if (map == null || !_squares.TryGetValue(Key(map, where), out var square))
        {
            return;
        }

        Baulks++;

        square.Baulked = true;
        square.BaulkedTick = Core.TickCount;

        logger.Information(
            "The square around {Where} could not be worked and is off the captain's list for {For} minutes: it still reads {Now:F0}, on {Blows} blows and {Deaths} deaths",
            where,
            BaulkMs / 60000,
            Faded(square, Core.TickCount),
            square.Blows,
            square.Deaths
        );
    }

    /// <summary>What a square reads right now, which is what it read when last touched, faded since.</summary>
    public static double Reading(Map map, Point3D where) =>
        map != null && _squares.TryGetValue(Key(map, where), out var square) ? Faded(square, Core.TickCount) : 0.0;

    /// <summary>
    /// Somewhere inside a square that a body can actually stand on, worked out once and kept.
    ///
    /// <para>
    /// <b>The middle of a square is arithmetic, and arithmetic can land on a tree.</b> Asking
    /// <see cref="BotStep.Settle"/> about the middle tile alone and throwing the square away when it says no
    /// puts the fate of five hundred and seventy-six tiles on one of them — a rock, a wall, a trunk, the
    /// corner of somebody's house — and on the first evening it ran, that is what happened: 1807 refusals in
    /// fifty minutes over squares that were perfectly walkable everywhere except the one tile anybody asked
    /// about. This is the same fault as the height that was invented rather than looked up, wearing the
    /// opposite hat, and it was introduced fixing that one.
    /// </para>
    ///
    /// <para>
    /// So the middle first — it is the point the reading is about, and it nearly always answers — and then a
    /// handful of places spread through the square. All of them are well inside it, so what comes back is
    /// still an answer about this square and not about its neighbour.
    /// </para>
    ///
    /// <para>
    /// <b>Looked at once and never again</b>, because terrain does not move. That is what makes the search
    /// affordable and it is also what makes the tally above mean something: a square counts here once, when
    /// it is first considered, rather than on every beat of every captain for the rest of the night.
    /// </para>
    /// </summary>
    private static bool Footing(Map map, Square square)
    {
        if (square.Sounded)
        {
            return square.Standable;
        }

        square.Sounded = true;

        var middle = Middle(square);
        var quarter = Math.Max(1, Side / 4);
        var third = Math.Max(2, Side / 3);

        ReadOnlySpan<(int X, int Y)> offsets =
        [
            (0, 0),
            (-quarter, -quarter),
            (quarter, -quarter),
            (quarter, quarter),
            (-quarter, quarter),
            (-third, 0),
            (third, 0),
            (0, -third),
            (0, third)
        ];

        foreach (var (dx, dy) in offsets)
        {
            var x = middle.X + dx;
            var y = middle.Y + dy;

            if (!BotStep.Settle(map, x, y, out var z))
            {
                continue;
            }

            square.Standable = true;
            square.Foot = new Point3D(x, y, z);

            return true;
        }

        Unfooted++;

        logger.Information(
            "Nowhere in the square around ({X}, {Y}) can be stood on, so nobody will ever be sent there: {Blows} blows and {Deaths} deaths happened in it",
            middle.X,
            middle.Y,
            square.Blows,
            square.Deaths
        );

        return false;
    }

    private static double Faded(Square square, long now)
    {
        var since = now - square.Tick;

        if (since <= 0 || square.Reading <= 0.0)
        {
            return square.Reading;
        }

        // Halving every HalfLifeMs. Written as a power of a half rather than as a subtraction so that a
        // square nobody has touched for a day reads as nearly nothing instead of as a negative number.
        return square.Reading * Math.Pow(0.5, since / (double)HalfLifeMs);
    }

    private static void Forget(long now)
    {
        (int Map, int X, int Y) calmest = default;
        var lowest = double.MaxValue;
        var found = false;

        foreach (var (key, square) in _squares)
        {
            var faded = Faded(square, now);

            if (faded < lowest)
            {
                lowest = faded;
                calmest = key;
                found = true;
            }
        }

        if (found)
        {
            _squares.Remove(calmest);
        }
    }

    private static (int Map, int X, int Y) Key(Map map, Point3D where) =>
        (map.MapID, Cell(where.X), Cell(where.Y));

    /// <summary>
    /// Which square a coordinate falls in.
    ///
    /// Floor division rather than integer division, because the two disagree on negative coordinates and a
    /// map with a negative corner would otherwise fold two squares into one.
    /// </summary>
    private static int Cell(int value) => (int)Math.Floor(value / (double)Side);

    private static Point3D Middle(Square square) =>
        new(square.X * Side + Side / 2, square.Y * Side + Side / 2, 0);

    /// <summary>
    /// The worst squares the map holds, bloodiest first. For the dashboard and nothing else.
    ///
    /// Built fresh on the ask rather than kept in order: it is read when somebody opens a window, and an
    /// ordering nothing looks at between times is bookkeeping the population pays for and never uses.
    /// </summary>
    public static List<(Point3D Where, double Reading, int Blows, int Deaths)> Worst(int most)
    {
        var now = Core.TickCount;

        List<(Point3D Where, double Reading, int Blows, int Deaths)> found = [];

        foreach (var square in _squares.Values)
        {
            var faded = Faded(square, now);

            if (faded <= 0.0)
            {
                continue;
            }

            found.Add((Middle(square), faded, square.Blows, square.Deaths));
        }

        found.Sort((left, right) => right.Reading.CompareTo(left.Reading));

        if (most > 0 && found.Count > most)
        {
            found.RemoveRange(most, found.Count - most);
        }

        return found;
    }

    /// <summary>One line: how much harm this map is built on, and where the worst of it is.</summary>
    public static string Describe()
    {
        if (_squares.Count == 0)
        {
            return "nowhere has hurt anybody yet";
        }

        var now = Core.TickCount;
        Square worst = null;
        var reading = 0.0;

        foreach (var square in _squares.Values)
        {
            var faded = Faded(square, now);

            if (faded > reading)
            {
                reading = faded;
                worst = square;
            }
        }

        // <b>How far under the bar it came, because "nowhere is dangerous enough" is two different facts.</b>
        // A map whose worst square reads 11 against a bar of 12 is a bar set a hair too high; a map whose
        // worst square reads 0.4 is a map that is not filling up, and no amount of moving the bar will help
        // it. This sentence used to end at the bar and could not tell the two apart, so the only way to
        // choose between them was to guess at the code.
        if (worst == null || reading < Worrying)
        {
            var quiet = worst == null
                ? "nothing on it reads at all"
                : $"the worst of them is around ({Middle(worst).X}, {Middle(worst).Y}) at {reading:F1} on {worst.Blows} blows and {worst.Deaths} deaths";

            return
                $"{_squares.Count} squares remembered on {Blows} blows and {Deaths} deaths; none of them reads above {Worrying:F0} now, so nowhere needs a company — {quiet}; {Sweeps} swept, {Clearances} harrowed off the board, {Baulks} given up on, {Unfooted} with nowhere in them to stand";
        }

        var middle = Middle(worst);

        return
            $"{_squares.Count} squares remembered on {Blows} blows and {Deaths} deaths; worst is around ({middle.X}, {middle.Y}) at {reading:F0} — {worst.Blows} blows and {worst.Deaths} deaths there; {Sweeps} swept, {Clearances} harrowed off the board, {Baulks} given up on, {Unfooted} with nowhere in them to stand";
    }

    public static void Forget()
    {
        _squares.Clear();
        _passed.Clear();
        Blows = 0;
        Deaths = 0;
        Sweeps = 0;
        Clearances = 0;
        Baulks = 0;
        Unfooted = 0;
    }
}
