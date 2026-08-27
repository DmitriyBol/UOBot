using System;
using System.Collections.Generic;
using Server.Logging;

namespace Server.BotAI.V2;

/// <summary>
/// The island cut into squares thirty tiles across, each carrying one number: how safe the population has
/// found it to be.
///
/// <para>
/// <b>This is a different question from <see cref="BotPeril"/>'s and the two must not be merged.</b> Peril
/// answers "where is it dangerous <em>now</em>" — a decaying frequency of blows, right for a captain deciding
/// where a company should be standing this minute, and deliberately forgetful, because a graveyard that was
/// terrible an hour ago and quiet since is not where anybody should be sent. This answers "what sort of
/// ground is that" — a standing reputation that a place earns slowly, in both directions, and does not
/// forget on its own. A quiet meadow and a graveyard nobody has visited since the last massacre read the
/// same on Peril's map and must never read the same here.
/// </para>
///
/// <para>
/// <b>Both directions, which is what makes it a reputation rather than a scar.</b> Blows and deaths push a
/// square down; bots walking through it and coming out the other side push it back up. So ground that was
/// cleared genuinely recovers — by being walked, which is evidence — rather than by a clock running out,
/// which is not. A square nothing has happened in for a day reads exactly what it read yesterday, and that
/// is the point: the population's memory of the island should outlive any one session's worth of walking.
/// </para>
///
/// <para>
/// <b>Shared by everybody, kept nowhere else.</b> There is one map and every bot reads and writes the same
/// squares, the way the squad's stations and the market's prices are shared: nothing is messaged anywhere,
/// and two bots asking the same question of the same ground get the same answer by construction.
/// </para>
/// </summary>
public static class BotQuad
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotQuad));

    /// <summary>
    /// How many tiles across one quadrant is.
    ///
    /// <para>
    /// Thirty, by order. It is the scale at which "that part of the island" means something to a person
    /// looking at a map: wide enough that a bot crossing it is genuinely somewhere, narrow enough that a
    /// company sent to one is not being sent to a county. A screen at this era's resolution is about this
    /// wide, so a quadrant is roughly "what a bot can see the far side of".
    /// </para>
    /// </summary>
    public const int Side = 30;

    /// <summary>The safest a square may ever read. Nothing has gone wrong here in a long while.</summary>
    public const double Safest = 1.0;

    /// <summary>The worst a square may ever read.</summary>
    public const double Bleakest = -1.0;

    /// <summary>Where a square starts before anything is known about it. Neither trusted nor feared.</summary>
    public const double Fresh = 0.0;

    /// <summary>
    /// How many uneventful crossings it takes to earn a square a little credit.
    ///
    /// Three, by order, and counted rather than timed: a crossing is a bot going in and coming out with
    /// nothing having happened to it, which is evidence about the ground. Time passing is not.
    /// </summary>
    public static int PerPass { get; set; } = 3;

    /// <summary>What those crossings are worth.</summary>
    public static double PassWorth { get; set; } = 0.05;

    /// <summary>How many blows landed on bots here before the square is marked down.</summary>
    public static int PerBlows { get; set; } = 5;

    /// <summary>What that many blows costs a square.</summary>
    public static double BlowsWorth { get; set; } = -0.01;

    /// <summary>
    /// What one death costs a square.
    ///
    /// A tenth, which is twenty times what five blows cost and is meant to be. Being hit is what happens all
    /// day to a population that fights for a living. A bot that did not come back is the only unambiguous
    /// evidence that ground is beyond whoever went there — the same reasoning, and very nearly the same
    /// ratio, as <see cref="BotPeril.PerDeath"/>.
    /// </summary>
    public static double DeathWorth { get; set; } = -0.1;

    /// <summary>
    /// What the death of a Baron costs a square.
    ///
    /// <para>
    /// Half, by order, and five times an ordinary death. The reasoning is about evidence rather than about
    /// rank: he is the best-armed and best-trained thing this population can put on a field, he is sent to
    /// ground precisely because it is bad, and he is the one who never leaves. Ground that killed <em>him</em>
    /// is ground that will kill anybody, and one such death should say so at once rather than after five.
    /// </para>
    ///
    /// <para>
    /// It is also sized to clear <see cref="Dire"/> in a single step from a square that read nothing at all,
    /// which is deliberate: the square where a Baron fell may have a great hunt raised for it immediately,
    /// with no further evidence needed and nobody having to wait for the map to catch up.
    /// </para>
    /// </summary>
    public static double BaronWorth { get; set; } = -0.5;

    // ---- What the King's Rangers' own evidence is worth, which is not what anybody else's is. ------
    //
    // <b>They are elite, so their survival proves less and their deaths prove more.</b> Five bots in gold
    // plate with a grandmaster surgeon behind them walking out of a quadrant unharmed says nothing about
    // whether a miner could — and it is exactly that inference an ordinary bot's crossing is credited for.
    // So a clean sweep by rangers moves the square not at all: it is marked read and left where it was.
    // Blows landed on them are worth a fifth of an ordinary bot's, because they are in armour and are hit
    // constantly by design. A whole company dying in one square is the strongest single reading this map
    // takes from anybody.

    /// <summary>What a clean ranger sweep is worth. Nothing, by order: see the note above.</summary>
    public static double SweptWorth { get; set; }

    /// <summary>How many blows landed on rangers before the square is marked down.</summary>
    public static int PerRangerBlows { get; set; } = 50;

    /// <summary>What that many blows on rangers costs a square.</summary>
    public static double RangerBlowsWorth { get; set; } = -0.05;

    /// <summary>What one ranger's death costs a square.</summary>
    public static double RangerDeathWorth { get; set; } = -0.1;

    /// <summary>
    /// What the loss of a whole ranger company in one square costs it.
    ///
    /// Half, and it is applied on top of the deaths themselves rather than instead of them: five bodies is
    /// five readings, and the fact that all five fell on the same ground is a sixth reading of its own. It is
    /// sized, like the Baron's, to carry a square past <see cref="Dire"/> from nothing in a single step.
    /// </summary>
    public static double WipedWorth { get; set; } = -0.5;

    /// <summary>Squares the rangers have swept clean, and squares that took a whole company.</summary>
    public static long Sweeps { get; private set; }

    public static long Wiped { get; private set; }

    /// <summary>
    /// Above this a square is too quiet to hunt in.
    ///
    /// <para>
    /// By order, and it is a rule about the population rather than about the ground: a square that has been
    /// walked through fifty times without incident has nothing living in it worth killing, and a hunter
    /// standing in it is a hunter earning nothing. It is the same idea as the crowding discount in
    /// <c>BotAppraisal</c> — a want whose value does not fall as it is satisfied is a want everybody ends up
    /// having — applied to ground instead of to work.
    /// </para>
    /// </summary>
    public static double TooQuiet { get; set; } = 0.5;

    /// <summary>Below this a square is where hunters should be going first.</summary>
    public static double Wanted { get; set; } = -0.1;

    /// <summary>Below this the Baron raises a great hunt for it. See <c>BotHarrow</c>.</summary>
    public static double Dire { get; set; } = -0.3;

    /// <summary>What a square reads after a great hunt has been through it: neither trusted nor feared.</summary>
    public static double Harrowed { get; set; } = 0.0;

    /// <summary>
    /// Most squares remembered at once.
    ///
    /// Felucca is 6144 by 4096, which is 205 by 137 quadrants — twenty-eight thousand of them, and a
    /// population of thirty will never stand in most of them. The cap is a backstop against a bug, not a
    /// budget: at four thousand it holds every square this shard's bots have ever been near, several times
    /// over, and each is a handful of numbers.
    /// </summary>
    public static int Most { get; set; } = 4096;

    /// <summary>One square of the island, and everything the population knows about it.</summary>
    public sealed class Quad
    {
        public Map Map;

        /// <summary>The square's own coordinates, in quadrants rather than tiles.</summary>
        public int X;

        public int Y;

        /// <summary>How safe it has been found to be, from <see cref="Bleakest"/> to <see cref="Safest"/>.</summary>
        public double Safety;

        /// <summary>Crossings that came to nothing, all told.</summary>
        public int Passes;

        /// <summary>Crossings counted towards the next step up. Reset each time one is awarded.</summary>
        public int Towards;

        /// <summary>Blows landed on bots here, all told.</summary>
        public int Blows;

        /// <summary>Blows counted towards the next step down.</summary>
        public int Bruising;

        /// <summary>Blows landed on the crown's rangers here, counted towards their own coarser step.</summary>
        public int RangerBruising;

        /// <summary>Rangers who died here.</summary>
        public int RangersLost;

        /// <summary>Whether the King's Rangers have swept this square and come out of it clean.</summary>
        public bool Swept;

        /// <summary>Bots that died here.</summary>
        public int Deaths;

        /// <summary>Whether anybody has ever actually stood in it. A square can be talked about before that.</summary>
        public bool Trodden;

        /// <summary>When it was last touched by anything at all.</summary>
        public long Tick;

        /// <summary>When a great hunt last finished here, or nought.</summary>
        public long HarrowedTick;

        /// <summary>The middle of the square, in tiles. The height is settled, never invented.</summary>
        public Point2D Middle => new(X * Side + Side / 2, Y * Side + Side / 2);

        public override string ToString() =>
            $"({Middle.X}, {Middle.Y}) at {Safety:F2} on {Passes} crossings, {Blows} blows and {Deaths} dead";
    }

    private static readonly Dictionary<(int Map, int X, int Y), Quad> _quads = [];

    /// <summary>Every square the population has anything to say about.</summary>
    public static IReadOnlyCollection<Quad> All => _quads.Values;

    public static int Count => _quads.Count;

    // ---- What has happened, for the summary. ------------------------------------------------------

    /// <summary>Squares that have been raised a step by being walked through.</summary>
    public static long Credited { get; private set; }

    /// <summary>Squares marked down for blows.</summary>
    public static long Marked { get; private set; }

    /// <summary>Squares marked down for a death.</summary>
    public static long Mourned { get; private set; }

    /// <summary>Squares set foot in for the very first time.</summary>
    public static long Discovered { get; private set; }

    /// <summary>Squares a great hunt has finished with.</summary>
    public static long Cleansed { get; private set; }

    /// <summary>Which square a tile falls in.</summary>
    public static (int Map, int X, int Y) Key(Map map, Point3D where) =>
        (map?.MapID ?? -1, Floor(where.X), Floor(where.Y));

    /// <summary>
    /// Division that keeps going the same way below nought.
    ///
    /// C# truncates towards zero, so tiles -1 and +1 would both land in quadrant 0 and the square either side
    /// of an axis would be twice as wide as every other. No bot walks negative coordinates on Felucca today,
    /// which is exactly why this would never be noticed until something did.
    /// </summary>
    private static int Floor(int tile) => (int)Math.Floor(tile / (double)Side);

    /// <summary>The square this tile is in, made if it is new to the population.</summary>
    public static Quad At(Map map, Point3D where)
    {
        if (map == null || map == Map.Internal)
        {
            return null;
        }

        var key = Key(map, where);

        if (_quads.TryGetValue(key, out var quad))
        {
            return quad;
        }

        if (_quads.Count >= Most)
        {
            return null;
        }

        quad = new Quad
        {
            Map = map,
            X = key.X,
            Y = key.Y,
            Safety = Fresh,
            Tick = Core.TickCount
        };

        _quads[key] = quad;

        return quad;
    }

    /// <summary>The square this tile is in, or nothing when the population has never touched it.</summary>
    public static Quad Known(Map map, Point3D where) =>
        map == null || map == Map.Internal ? null : _quads.GetValueOrDefault(Key(map, where));

    /// <summary>What this ground reads, or <see cref="Fresh"/> where nothing is known.</summary>
    public static double Safety(Map map, Point3D where) => Known(map, where)?.Safety ?? Fresh;

    /// <summary>Whether a bot has ever actually stood in the square this tile is in.</summary>
    public static bool Trodden(Map map, Point3D where) => Known(map, where)?.Trodden == true;

    /// <summary>
    /// A bot crossed out of one square and into another with nothing having happened to it.
    ///
    /// <para>
    /// Credited to the square it <em>left</em>, and that is the whole of what makes a crossing evidence. A
    /// bot that has just walked into a square knows nothing about it yet; a bot that has walked out of one
    /// has been through it and is still standing. Crediting the square being entered would hand credit to
    /// the very ground a bot is about to be killed on.
    /// </para>
    /// </summary>
    public static void Crossed(Map map, Point3D left, Point3D entered)
    {
        var into = At(map, entered);

        if (into != null && !into.Trodden)
        {
            into.Trodden = true;
            into.Tick = Core.TickCount;

            Discovered++;
        }

        var quad = Known(map, left);

        if (quad == null)
        {
            return;
        }

        quad.Passes++;
        quad.Towards++;
        quad.Tick = Core.TickCount;

        if (quad.Towards < PerPass)
        {
            return;
        }

        quad.Towards = 0;

        Raise(quad, PassWorth);
        Credited++;
    }

    /// <summary>
    /// A bot is standing here, and that is all this says.
    ///
    /// For a crossing that was interrupted: the square becomes known and counts as trodden, but earns no
    /// credit, because a bot that was being hit while it walked has learned nothing good about the ground.
    /// </summary>
    public static void Seen(Map map, Point3D where)
    {
        var quad = At(map, where);

        if (quad == null)
        {
            return;
        }

        quad.Tick = Core.TickCount;

        if (quad.Trodden)
        {
            return;
        }

        quad.Trodden = true;
        Discovered++;
    }

    /// <summary>Something hit a bot here.</summary>
    /// <param name="ranger">Whether the bot struck was one of the crown's rangers. Their blows count coarser.</param>
    public static void Struck(Map map, Point3D where, bool ranger = false)
    {
        var quad = At(map, where);

        if (quad == null)
        {
            return;
        }

        quad.Blows++;
        quad.Tick = Core.TickCount;

        if (ranger)
        {
            quad.RangerBruising++;

            if (quad.RangerBruising < PerRangerBlows)
            {
                return;
            }

            quad.RangerBruising = 0;

            Raise(quad, RangerBlowsWorth);
            Marked++;

            return;
        }

        quad.Bruising++;

        if (quad.Bruising < PerBlows)
        {
            return;
        }

        quad.Bruising = 0;

        Raise(quad, BlowsWorth);
        Marked++;
    }

    /// <summary>
    /// The King's Rangers walked out of this square without losing anybody.
    ///
    /// <para>
    /// Marked read and left exactly where it was, which is <see cref="SweptWorth"/> and is nought by order.
    /// Five bots in gold plate coming through unharmed is not evidence that a miner could, and crediting the
    /// ground for it would quietly raise every square they walk past the bar at which anybody else is
    /// allowed to hunt in it.
    /// </para>
    /// </summary>
    public static void Swept(Map map, Point3D where)
    {
        var quad = At(map, where);

        if (quad == null)
        {
            return;
        }

        quad.Swept = true;
        quad.Tick = Core.TickCount;

        if (!quad.Trodden)
        {
            quad.Trodden = true;
            Discovered++;
        }

        if (SweptWorth != 0.0)
        {
            Raise(quad, SweptWorth);
        }

        Sweeps++;
    }

    /// <summary>
    /// One of the crown's rangers died here.
    ///
    /// <para>
    /// Worth an ordinary death on its own — they are better armed, but a body is a body. What is not ordinary
    /// is <paramref name="wiped"/>: the whole company falling in one square is a separate reading applied on
    /// top of the five, because "five bots died here over an afternoon" and "a company of five was destroyed
    /// here" are different facts about the same ground and only the second one means nobody should go.
    /// </para>
    /// </summary>
    public static void FellRanger(Map map, Point3D where, bool wiped)
    {
        Fell(map, where, RangerDeathWorth);

        var quad = Known(map, where);

        if (quad == null)
        {
            return;
        }

        quad.RangersLost++;

        if (!wiped)
        {
            return;
        }

        Raise(quad, WipedWorth);
        Wiped++;

        logger.Warning(
            "The King's Rangers were destroyed around ({X}, {Y}); the ground now reads {Safety:F2}",
            quad.Middle.X,
            quad.Middle.Y,
            quad.Safety
        );
    }

    /// <summary>A bot died here.</summary>
    /// <param name="worth">What this particular death costs the square. See <see cref="BaronWorth"/>.</param>
    public static void Fell(Map map, Point3D where, double worth)
    {
        var quad = At(map, where);

        if (quad == null)
        {
            return;
        }

        quad.Deaths++;
        quad.Tick = Core.TickCount;

        Raise(quad, worth);
        Mourned++;

        logger.Information(
            "The ground around ({X}, {Y}) has taken somebody and now reads {Safety:F2}, on {Deaths} dead",
            quad.Middle.X,
            quad.Middle.Y,
            quad.Safety,
            quad.Deaths
        );
    }

    /// <summary>The same, at what an ordinary death is worth.</summary>
    public static void Fell(Map map, Point3D where) => Fell(map, where, DeathWorth);

    /// <summary>
    /// A great hunt has finished here. The square is neither trusted nor feared afterwards.
    ///
    /// <para>
    /// Set rather than raised, and set to nothing rather than to safe. What a company killed everything in
    /// is not <em>safe</em> — nobody has walked it since, and whatever wanders back in is unaccounted for.
    /// It is simply no longer known to be dire, which is what a clearance actually establishes. Earning its
    /// way up from there is the same walking every other square does.
    /// </para>
    /// </summary>
    public static void Cleared(Quad quad)
    {
        if (quad == null)
        {
            return;
        }

        var was = quad.Safety;

        quad.Safety = Harrowed;
        quad.Bruising = 0;
        quad.Towards = 0;
        quad.HarrowedTick = Core.TickCount;
        quad.Tick = Core.TickCount;

        Cleansed++;

        logger.Information(
            "The ground around ({X}, {Y}) has been harrowed: it read {Was:F2} and is now {Now:F2}",
            quad.Middle.X,
            quad.Middle.Y,
            was,
            quad.Safety
        );
    }

    private static void Raise(Quad quad, double by) =>
        quad.Safety = Math.Clamp(quad.Safety + by, Bleakest, Safest);

    /// <summary>
    /// The squares that read worst, worst first. For the board and for whoever is deciding where to send
    /// people.
    /// </summary>
    public static List<Quad> Worst(int most, Map map = null)
    {
        List<Quad> found = [];

        foreach (var quad in _quads.Values)
        {
            if (map != null && quad.Map != map)
            {
                continue;
            }

            found.Add(quad);
        }

        found.Sort(static (a, b) => a.Safety.CompareTo(b.Safety));

        if (most > 0 && found.Count > most)
        {
            found.RemoveRange(most, found.Count - most);
        }

        return found;
    }

    /// <summary>
    /// The eight squares around this one, and this one. What a Baron walks and what a great hunt spills into.
    /// </summary>
    public static List<Quad> Around(Quad quad, bool madeIfNew)
    {
        List<Quad> found = [];

        if (quad?.Map == null)
        {
            return found;
        }

        for (var dx = -1; dx <= 1; dx++)
        {
            for (var dy = -1; dy <= 1; dy++)
            {
                var key = (quad.Map.MapID, quad.X + dx, quad.Y + dy);

                if (_quads.TryGetValue(key, out var near))
                {
                    found.Add(near);

                    continue;
                }

                if (!madeIfNew || _quads.Count >= Most)
                {
                    continue;
                }

                near = new Quad
                {
                    Map = quad.Map,
                    X = quad.X + dx,
                    Y = quad.Y + dy,
                    Safety = Fresh,
                    Tick = Core.TickCount
                };

                _quads[key] = near;
                found.Add(near);
            }
        }

        return found;
    }

    /// <summary>
    /// The nearest square on the frontier: never stood in, but next to somewhere that has been.
    ///
    /// <para>
    /// <b>Nearest rather than worst, which is the opposite of how every other errand picks its ground.</b> A
    /// patrol goes where it is most dangerous and a great hunt where it is most dire, because both answer
    /// "where is the trouble". This answers "what do we not know", and unknown ground is all equally
    /// unknown — there is nothing to rank it by, so the tiebreak is the walk. The map then fills outwards
    /// from where the population actually lives, which is also the order in which knowing is worth anything.
    /// </para>
    ///
    /// <para>
    /// <b>Candidates are the ring just past what is known, never a search of the island.</b> This table only
    /// holds squares something has happened in or beside, so every candidate is by construction beside
    /// ground somebody has walked — which means it can be got to, and a square picked off a blank map cannot
    /// promise that. The frontier then advances a ring at a time under its own steam.
    /// </para>
    /// </summary>
    /// <param name="fit">Asked of each candidate. Null takes the first with ground under it.</param>
    public static Point3D Frontier(Map map, Point3D from, int within, Func<Point3D, bool> fit)
    {
        if (map == null || map == Map.Internal)
        {
            return Point3D.Zero;
        }

        var closest = int.MaxValue;
        var best = Point3D.Zero;

        foreach (var quad in _quads.Values)
        {
            if (quad.Map != map || !quad.Trodden)
            {
                continue;
            }

            for (var dx = -1; dx <= 1; dx++)
            {
                for (var dy = -1; dy <= 1; dy++)
                {
                    if (dx == 0 && dy == 0)
                    {
                        continue;
                    }

                    if (_quads.TryGetValue((map.MapID, quad.X + dx, quad.Y + dy), out var near) && near.Trodden)
                    {
                        continue;
                    }

                    var x = (quad.X + dx) * Side + Side / 2;
                    var y = (quad.Y + dy) * Side + Side / 2;
                    var away = Math.Max(Math.Abs(x - from.X), Math.Abs(y - from.Y));

                    if (away > within || away >= closest)
                    {
                        continue;
                    }

                    // The height is settled from the map, never arithmetic. A square's middle is a corner
                    // plus half a side and has no height of its own: invented as nought it works on the
                    // plains north of Britain and is thirty units under the grass on a hill, where no walk
                    // can ever arrive. This project has paid for that once already.
                    if (!BotStep.Settle(map, x, y, out var z))
                    {
                        continue;
                    }

                    var at = new Point3D(x, y, z);

                    if (fit != null && !fit(at))
                    {
                        continue;
                    }

                    closest = away;
                    best = at;
                }
            }
        }

        return best;
    }

    /// <summary>
    /// The worst square within reach that reads at or below <see cref="Dire"/>, or nothing.
    ///
    /// What the Baron is looking for. Ranked by the reading rather than by the dead, which is the whole
    /// difference between this map and <see cref="BotPeril"/>: a square earns its way down here by hurting
    /// people repeatedly and does not climb back out on its own.
    /// </summary>
    /// <param name="fit">Asked of each candidate in turn. Null takes the first with ground under it.</param>
    public static Quad Direst(Map map, Point3D from, int within, Func<Point3D, bool> fit)
    {
        if (map == null || map == Map.Internal)
        {
            return null;
        }

        Quad best = null;
        var lowest = Dire;

        foreach (var quad in _quads.Values)
        {
            if (quad.Map != map || quad.Safety > lowest)
            {
                continue;
            }

            var middle = quad.Middle;

            if (Math.Max(Math.Abs(middle.X - from.X), Math.Abs(middle.Y - from.Y)) > within)
            {
                continue;
            }

            if (!BotStep.Settle(map, middle.X, middle.Y, out var z))
            {
                continue;
            }

            if (fit != null && !fit(new Point3D(middle.X, middle.Y, z)))
            {
                continue;
            }

            lowest = quad.Safety;
            best = quad;
        }

        return best;
    }

    /// <summary>Where a square's middle is, with the ground settled under it. Zero when nothing can stand there.</summary>
    public static Point3D Stand(Quad quad)
    {
        if (quad?.Map == null)
        {
            return Point3D.Zero;
        }

        var middle = quad.Middle;

        return BotStep.Settle(quad.Map, middle.X, middle.Y, out var z)
            ? new Point3D(middle.X, middle.Y, z)
            : Point3D.Zero;
    }

    public static string Describe()
    {
        if (_quads.Count == 0)
        {
            return "no ground has been walked yet";
        }

        var trodden = 0;
        var quiet = 0;
        var wanted = 0;
        var dire = 0;
        Quad worst = null;

        foreach (var quad in _quads.Values)
        {
            if (quad.Trodden)
            {
                trodden++;
            }

            if (quad.Safety > TooQuiet)
            {
                quiet++;
            }

            if (quad.Safety <= Wanted)
            {
                wanted++;
            }

            if (quad.Safety <= Dire)
            {
                dire++;
            }

            if (worst == null || quad.Safety < worst.Safety)
            {
                worst = quad;
            }
        }

        return $"{_quads.Count} quadrants of {Side} tiles, {trodden} of them stood in: {quiet} too quiet to hunt "
               + $"(above {TooQuiet:F2}), {wanted} worth going to (at or below {Wanted:F2}), {dire} dire (at or below {Dire:F2}); "
               + $"worst is {worst}; {Discovered} first set foot in, {Credited} raised for crossings, "
               + $"{Marked} marked for blows, {Mourned} for a death, {Cleansed} harrowed, {Sweeps} swept by rangers, {Wiped} took a whole company";
    }

    /// <summary>
    /// Puts one quadrant back as it was read off disk. Called only by <see cref="BotQuadStore"/>.
    ///
    /// <para>
    /// The facet is looked up here rather than stored: a <c>Map</c> belongs to the world that was replaced,
    /// and a record holding one is a record holding a deleted object. An id that no longer names a facet is
    /// dropped rather than guessed at — a shard reconfigured from Felucca to somewhere else should lose the
    /// old island's reputation, not silently apply it to the new one.
    /// </para>
    /// </summary>
    public static void Restore(
        int facet,
        int x,
        int y,
        double safety,
        int passes,
        int blows,
        int deaths,
        int rangersLost,
        bool trodden,
        bool swept,
        bool harrowed
    )
    {
        var map = Map.Maps is { Length: > 0 } && facet >= 0 && facet < Map.Maps.Length ? Map.Maps[facet] : null;

        if (map == null || map == Map.Internal || _quads.Count >= Most)
        {
            return;
        }

        _quads[(facet, x, y)] = new Quad
        {
            Map = map,
            X = x,
            Y = y,
            Safety = Math.Clamp(safety, Bleakest, Safest),
            Passes = passes,
            Blows = blows,
            Deaths = deaths,
            RangersLost = rangersLost,
            Trodden = trodden,
            Swept = swept,

            // Stamped with a real reading rather than with the one from the last process: these counters can
            // be the machine's uptime, so a tick from yesterday is not a smaller number, it is a nonsense one.
            Tick = Core.TickCount,
            HarrowedTick = harrowed ? Core.TickCount : 0
        };
    }

    /// <summary>A world reload is a different world. The ground is not, but the Maps in these records are.</summary>
    public static void Forget()
    {
        _quads.Clear();

        Credited = 0;
        Marked = 0;
        Mourned = 0;
        Discovered = 0;
        Cleansed = 0;
        Sweeps = 0;
        Wiped = 0;
    }
}
