using System;
using System.Collections.Generic;
using System.Diagnostics;
using Server.Engines.Pathing.Cache;
using Server.Logging;
using CalcMoves = Server.Movement.Movement;

namespace Server.BotAI.V2;

/// <summary>What a search concluded. Three answers, and the difference between two of them is load-bearing.</summary>
public enum BotPathOutcome
{
    /// <summary>
    /// Every tile the bot can reach from here was examined, and the goal is not among them.
    ///
    /// <b>A statement about the world, not about the search.</b> There is no way, and nothing whatever is
    /// gained by walking a little way and asking again — the nearest reachable tile to a goal behind a
    /// wall is a tile <em>against the wall</em>, and going there to ask again is exactly the fence-hugging
    /// this whole design exists to end. Returned only when the search can prove it: the open set emptied
    /// and nothing was ever clipped by the search box.
    /// </summary>
    Sealed,

    /// <summary>
    /// The search ran out of its allowance, or out of ground inside its own box, and the path leads to the
    /// best tile it did reach. Walking it is real progress: the next search starts from further along,
    /// with the box re-centred and a fresh allowance.
    /// </summary>
    Partial,

    /// <summary>A walkable path to the goal, tile by tile.</summary>
    Reached
}

/// <summary>
/// Tile-by-tile A* over the engine's own step masks, bounded by a clock.
///
/// <para>
/// <b>Why a clock and not a tile count.</b> The first version budgeted expansions — twelve thousand of
/// them. Its own measurements say what that costs: 90 006 tiles across 538 searches at 215 ms total is
/// 167 tiles per search at 0.40 ms, so about 2.4 µs a tile, so a search that spent its whole allowance
/// cost something like <b>thirty milliseconds</b>. The average was honest and the worst case was two
/// orders of magnitude worse, which is the shape of a frame that stutters when fifty bots decide to cross
/// the continent in the same tick. Worse, a tile is not a fixed price: on a warm <see cref="StepCache"/>
/// it is a lookup, on a cold one it is a full recompute. Budget the thing that was promised.
/// </para>
///
/// <para>
/// <b>Two properties matter more than speed.</b> A failed search still answers — it has established
/// exactly which tiles it can reach, so finding the gate out of a walled graveyard is not a mechanism
/// with its own ring sweeps and gate memory, it is the ordinary search read correctly. And a refusal is
/// trustworthy, which is what turns "stuck" from a state a bot occupies into an answer it receives.
/// </para>
/// </summary>
public static class BotPath
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotPath));

    /// <summary>Cardinal cost, matching the engine's own A* so the two agree on what is shortest.</summary>
    private const int StraightCost = 10;

    private const int DiagonalCost = 14;

    /// <summary>
    /// How far outside the straight line between start and goal the search may look. Wide enough to walk
    /// right round a walled graveyard rather than merely to sidestep a tree, because going the wrong way
    /// first is exactly what leaving an enclosure requires.
    /// </summary>
    public static int MinMargin { get; set; } = 28;

    /// <summary>
    /// The widest that box may get, on a long journey.
    ///
    /// <para>
    /// <b>Raised from ninety-six once the cost was actually measured.</b> Ninety-six tiles off the straight
    /// line is enough to round a building and not enough to round a lake, so a bot whose way lay round one
    /// hit the edge of its own search box, came back with a partial plan, walked to the water and asked
    /// again. The box was never the thing keeping the cost down — the clock is, and the clock was idle: 1675
    /// searches came to 256ms over five minutes, which is under one per cent of what the population is
    /// allowed. A wider box does not make a search slower; it makes the time it is given buy a better answer.
    /// </para>
    /// </summary>
    public static int MaxMargin { get; set; } = 256;

    /// <summary>
    /// What one search may cost. The number asked for, and now the number that is actually enforced.
    /// </summary>
    public static double CeilingMs { get; set; } = 20.0;

    /// <summary>
    /// The smallest search worth running, for when the population's allowance is spent. Below this a bot
    /// cannot see round anything, and it is better to creep than to stop.
    /// </summary>
    public static double FloorMs { get; set; } = 1.0;

    /// <summary>
    /// What the whole population may spend on searching, per second.
    ///
    /// Not a correctness bound — a governor, and a generous one: the first version measured 215 ms of
    /// searching across ninety seconds, about 2.4 ms a second for fifty bots. Twenty-five times that is
    /// still under a tenth of the game loop, and when it does run out searches shrink to
    /// <see cref="FloorMs"/> and come back <see cref="BotPathOutcome.Partial"/> more often, so the
    /// population walks in shorter hops instead of the world stuttering.
    /// </summary>
    public static double WindowMs { get; set; } = 250.0;

    private const int WindowLengthMs = 1000;

    /// <summary>
    /// How many expansions between glances at the clock. Reading a timestamp is not free, and sixty-four
    /// tiles is a few hundred microseconds at most — fine granularity against a two millisecond ceiling.
    /// </summary>
    private const int ClockEvery = 64;

    private const int InitialNodes = 4096;

    /// <summary>
    /// The smallest pocket worth writing down. Two, because one tile is never useful knowledge and is the
    /// shape an invalid start takes — and a wrong entry in the reach ledger is permanent and silent.
    /// </summary>
    private const int MinPocket = 2;

    // ---- Search state. Reused across searches: one game thread, and a search that allocates is a
    // ---- search that costs more in collection than it does in work.

    private static int[] _nodeX = new int[InitialNodes];
    private static int[] _nodeY = new int[InitialNodes];
    private static sbyte[] _nodeZ = new sbyte[InitialNodes];
    private static int[] _nodeCost = new int[InitialNodes];
    private static int[] _nodeTotal = new int[InitialNodes];
    private static int[] _nodeParent = new int[InitialNodes];
    private static bool[] _nodeClosed = new bool[InitialNodes];

    private static int _nodeCount;

    private static readonly Dictionary<int, int> _lookup = [];

    private static readonly PriorityQueue<int, int> _open = new();

    private static readonly Dictionary<long, bool> _blockedByItems = [];

    private static readonly List<Point3D> _reversed = [];

    /// <summary>A scratch path, for callers who only want a yes or no.</summary>
    private static readonly List<Point3D> _scratch = [];

    // ---- The population's allowance.

    private static long _windowEnds;

    private static bool _windowStarted;

    private static double _spentThisWindow;

    // ---- What it has all cost. For the summary.

    public static long Searches { get; private set; }

    public static long TilesExamined { get; private set; }

    public static long Reached { get; private set; }

    public static long PartialRuns { get; private set; }

    public static long SealedRuns { get; private set; }

    public static double TotalMs { get; private set; }

    public static double WorstMs { get; private set; }

    public static void Reset()
    {
        Searches = 0;
        TilesExamined = 0;
        Reached = 0;
        PartialRuns = 0;
        SealedRuns = 0;
        TotalMs = 0.0;
        WorstMs = 0.0;
        _spentThisWindow = 0.0;
        _windowStarted = false;
    }

    public static string Describe() =>
        Searches == 0
            ? "no searches yet"
            : $"{Searches} searches, {TilesExamined} tiles examined, {TotalMs:F0}ms total ({TotalMs / Searches:F2}ms each, worst {WorstMs:F2}ms), {Reached} reached, {PartialRuns} partial, {SealedRuns} refused outright";

    /// <summary>
    /// Whether there is a way at all, without keeping the path. For vetting a candidate before committing
    /// to it — a place to wander to, a rally point, a spot to retreat onto.
    /// </summary>
    public static bool CanReach(Map map, Point3D from, Point3D to, BotArrival arrival) =>
        Find(map, from, to, arrival, _scratch) == BotPathOutcome.Reached;

    /// <summary>
    /// Whether there is a way there <em>without going through a door</em> — which is how "is that spot out
    /// on the street" gets answered.
    ///
    /// Without it, a caller asking for somewhere outdoors is handed a point in somebody's pantry: a shut
    /// door is passable to the planner by design, so the inside of every building in the world is
    /// reachable and therefore a candidate. The distinction has to be a separate question rather than a
    /// different rule, because both answers are wanted, about the same tile, by different callers.
    ///
    /// Nothing calls this yet. It exists now because the alternative was a parameter on
    /// <see cref="BotStep.BlockedByItems"/> with no way to reach it from a search — plumbing that looks
    /// finished and is not.
    /// </summary>
    public static bool ReachableWithDoorsShut(Map map, Point3D from, Point3D to, BotArrival arrival) =>
        Find(map, from, to, arrival, _scratch, doorsShut: true) == BotPathOutcome.Reached;

    /// <summary>
    /// Finds a way from <paramref name="from"/> to <paramref name="to"/> and writes it into
    /// <paramref name="path"/> as the tiles to walk, the bot's own tile excluded.
    ///
    /// On <see cref="BotPathOutcome.Partial"/> the path leads to the nearest point to the goal that
    /// <em>can</em> be reached, so a caller that simply walks whatever comes back behaves correctly either
    /// way: it arrives, or it gets as close as the world permits and asks again from there — which, from
    /// further out, may well succeed.
    /// </summary>
    public static BotPathOutcome Find(
        Map map,
        Point3D from,
        Point3D to,
        BotArrival arrival,
        List<Point3D> path,
        BotAvoid avoid = default,
        double ceilingMs = 0.0,
        bool doorsShut = false
    )
    {
        path.Clear();

        if (map == null || map == Map.Internal)
        {
            return BotPathOutcome.Sealed;
        }

        if (arrival.Reached(from, to))
        {
            return BotPathOutcome.Reached;
        }

        // Free refusal, from a pocket somebody else already walked to its edges. The most expensive
        // question there is, answered by a dictionary lookup.
        if (BotReach.Ask(map, from, to, arrival) == BotReachVerdict.Sealed)
        {
            // Counted as a search, because it answered one. Left out, the summary could report more
            // refusals than searches and divide the total time by the wrong denominator — and a system
            // whose entire premise is that measurement beats guessing cannot afford a lying counter.
            Searches++;
            SealedRuns++;

            return BotPathOutcome.Sealed;
        }

        // One pathfind, as far as the step cache's promotion gate is concerned. It counts distinct
        // searches rather than lookups, so without this a single search's thousands of probes read as
        // thousands of separate visits and the gate builds chunks nothing revisits.
        StepCache.Instance.BeginFindGeneration();

        Searches++;

        var started = Stopwatch.GetTimestamp();
        var allowanceMs = Allowance(ceilingMs > 0.0 ? ceilingMs : CeilingMs);
        var deadline = started + (long)(allowanceMs * Stopwatch.Frequency / 1000.0);

        var span = Math.Max(Math.Abs(to.X - from.X), Math.Abs(to.Y - from.Y));
        var margin = Math.Clamp(span / 2, MinMargin, MaxMargin);

        var minX = Math.Max(0, Math.Min(from.X, to.X) - margin);
        var minY = Math.Max(0, Math.Min(from.Y, to.Y) - margin);
        var maxX = Math.Min(map.Width - 1, Math.Max(from.X, to.X) + margin);
        var maxY = Math.Min(map.Height - 1, Math.Max(from.Y, to.Y) + margin);

        _lookup.Clear();
        _open.Clear();
        _blockedByItems.Clear();
        _nodeCount = 0;

        var startZ = (sbyte)Math.Clamp(from.Z, sbyte.MinValue, sbyte.MaxValue);

        // Where the bot is standing has to be somewhere it could stand, and the caller is not obliged to
        // have checked.
        //
        // A start whose mask forbids all eight directions makes the search empty its open set on the first
        // expansion — which looks exactly like a pocket of ground walked to its edges, and would be written
        // into the reach ledger as one. A one-tile pocket is never knowledge worth having, and this one
        // would be poison: it seals that cell, and every other height sharing its band, against every
        // journey for the rest of the shard's life. So an immovable start is reported as no progress rather
        // than as a fact about the world, and the walker asks the engine directly instead.
        if (BotStep.Mask(map, from.X, from.Y, startZ).WalkMask == 0)
        {
            PartialRuns++;

            return BotPathOutcome.Partial;
        }

        var start = AddNode(from.X, from.Y, startZ, 0, -1);

        _nodeTotal[start] = Heuristic(from.X, from.Y, to);
        _lookup[BotStep.Cell(from.X, from.Y, startZ)] = start;
        _open.Enqueue(start, _nodeTotal[start]);

        // The best thing found short of the goal, kept as the search runs. A* explores towards the goal,
        // so this is genuinely the closest reachable point rather than the first one tried.
        var nearest = -1;
        var nearestScore = int.MaxValue;

        var reached = -1;
        var expansions = 0;
        var outOfTime = false;

        // Whether the search box ever stopped the search from looking somewhere.
        //
        // The single most important flag in this file. Without it, "the open set emptied" is ambiguous
        // between "the bot can reach nothing else in the world" and "the bot can reach nothing else
        // inside this rectangle" — and recording the second as a sealed pocket would wall off half a
        // continent, permanently, with nothing in any log to say so.
        var clipped = false;

        while (_open.Count > 0)
        {
            if ((expansions & (ClockEvery - 1)) == 0 && Stopwatch.GetTimestamp() >= deadline)
            {
                outOfTime = true;
                break;
            }

            if (!_open.TryDequeue(out var current, out var priority))
            {
                break;
            }

            // Lazily deleted duplicate: a cheaper route to this cell was found after it was queued.
            if (_nodeClosed[current] || _nodeTotal[current] != priority)
            {
                continue;
            }

            _nodeClosed[current] = true;
            expansions++;

            var cx = _nodeX[current];
            var cy = _nodeY[current];
            var cz = _nodeZ[current];

            if (arrival.Reached(new Point3D(cx, cy, cz), to))
            {
                reached = current;
                break;
            }

            var score = Heuristic(cx, cy, to);

            if (score < nearestScore)
            {
                nearestScore = score;
                nearest = current;
            }

            var mask = BotStep.Mask(map, cx, cy, cz);
            var walk = mask.WalkMask;

            if (walk == 0)
            {
                continue;
            }

            for (var d = 0; d < 8; d++)
            {
                if ((walk & (1 << d)) == 0)
                {
                    continue;
                }

                // The diagonal rule, and it needs both of its halves.
                //
                // The engine will not let a player cut a corner unless BOTH flanking tiles pass the whole
                // movement check — terrain and items. The first version calls getting this half-right the
                // most expensive mistake in its pathfinder, and it can be got half-right in either
                // direction: check only the terrain and every squeeze past a headstone fails at the moment
                // of stepping; check only the items and the planner emits diagonals through wall corners
                // that the engine then refuses. Both gates, in this order.
                if ((d & 1) == 1)
                {
                    var left = (d + 7) & 7;
                    var right = (d + 1) & 7;

                    // Mask bits first, and not merely for speed: GetWalkZ for a direction the mask forbids
                    // is a landing height for a step that does not exist, and FlankBlocked would then be
                    // asking about the wrong tile at the wrong height.
                    if ((walk & (1 << left)) == 0 || (walk & (1 << right)) == 0)
                    {
                        continue;
                    }

                    if (FlankBlocked(map, cx, cy, left, mask, doorsShut) || FlankBlocked(map, cx, cy, right, mask, doorsShut))
                    {
                        continue;
                    }
                }

                var nx = cx;
                var ny = cy;

                CalcMoves.Offset((Direction)d, ref nx, ref ny);

                if (nx < minX || ny < minY || nx > maxX || ny > maxY)
                {
                    clipped = true;
                    continue;
                }

                if (avoid.Blocks(nx, ny))
                {
                    continue;
                }

                var nz = mask.GetWalkZ((Direction)d);

                // Items, which the mask does not know about. This world is built out of them — the
                // graveyard railing, the headstones, the crates are all items — so a planner without this
                // draws confident straight lines through the very fences it exists to see.
                if (Blocked(map, nx, ny, nz, doorsShut))
                {
                    continue;
                }

                var cell = BotStep.Cell(nx, ny, nz);
                var stepCost = (d & 1) == 1 ? DiagonalCost : StraightCost;
                var cost = _nodeCost[current] + stepCost;

                if (_lookup.TryGetValue(cell, out var existing))
                {
                    if (_nodeClosed[existing] || cost >= _nodeCost[existing])
                    {
                        continue;
                    }

                    _nodeCost[existing] = cost;
                    _nodeParent[existing] = current;
                    _nodeTotal[existing] = cost + Heuristic(nx, ny, to);
                    _open.Enqueue(existing, _nodeTotal[existing]);

                    continue;
                }

                var node = AddNode(nx, ny, nz, cost, current);

                _nodeTotal[node] = cost + Heuristic(nx, ny, to);
                _lookup[cell] = node;
                _open.Enqueue(node, _nodeTotal[node]);
            }
        }

        var elapsedMs = (Stopwatch.GetTimestamp() - started) * 1000.0 / Stopwatch.Frequency;

        Spend(elapsedMs);

        TotalMs += elapsedMs;
        TilesExamined += expansions;

        if (elapsedMs > WorstMs)
        {
            WorstMs = elapsedMs;
        }

        if (reached >= 0)
        {
            Rebuild(reached, path);
            Reached++;

            return BotPathOutcome.Reached;
        }

        // Ground genuinely exhausted: the open set emptied, the clock had time left, the box never got in
        // the way, and no tactical exclusion was distorting the answer. Only then is this knowledge.
        if (!outOfTime && !clipped && avoid.Empty)
        {
            // A search run with the doors treated as walls has mapped a room, not a pocket of the world.
            // Recording it would seal every building on the shard against everybody outside it.
            if (!doorsShut && _lookup.Count >= MinPocket)
            {
                BotReach.Record(map, _lookup.Keys);
            }

            SealedRuns++;

            return BotPathOutcome.Sealed;
        }

        if (nearest >= 0 && nearest != start)
        {
            Rebuild(nearest, path);
        }

        PartialRuns++;

        return BotPathOutcome.Partial;
    }

    /// <summary>
    /// What this search is allowed, given what the population has already spent this second.
    ///
    /// The window is seeded from a real tick the first time round rather than left at zero: tick counts
    /// can start enormous or negative depending on the host, and a deadline field that defaults to zero is
    /// either already past or unreachably far — the first shuts the window for ever, the second never
    /// closes it.
    /// </summary>
    private static double Allowance(double requested)
    {
        var now = Core.TickCount;

        if (!_windowStarted || now - _windowEnds >= 0)
        {
            _windowStarted = true;
            _windowEnds = now + WindowLengthMs;
            _spentThisWindow = 0.0;
        }

        var left = WindowMs - _spentThisWindow;

        if (left <= 0.0)
        {
            return FloorMs;
        }

        return Math.Clamp(Math.Min(requested, left), FloorMs, CeilingMs);
    }

    private static void Spend(double ms) => _spentThisWindow += ms;

    /// <summary>Octile distance in the same units as the step costs, so the estimate never overshoots.</summary>
    private static int Heuristic(int x, int y, Point3D goal)
    {
        var dx = Math.Abs(x - goal.X);
        var dy = Math.Abs(y - goal.Y);

        return dx > dy
            ? DiagonalCost * dy + StraightCost * (dx - dy)
            : DiagonalCost * dx + StraightCost * (dy - dx);
    }

    /// <summary>
    /// <see cref="BotStep.BlockedByItems"/>, remembered for the length of one search.
    ///
    /// Without this the diagonal rule costs two item lookups per diagonal candidate — eight a tile, where
    /// the terrain mask costs one — and a tile is flanked by up to four diagonals from four different
    /// neighbours, so nearly all of it would be the same answer fetched again.
    ///
    /// <para>
    /// <b>Keyed on the exact height, not the height band.</b> <see cref="BotStep.Cell"/> folds Z into
    /// twenty-unit bands, which is right for naming a place and wrong for this: whether an item is in the
    /// way is decided by whether its own height overlaps the body standing there, and two heights thirteen
    /// units apart share a band while disagreeing about a crate. Keyed by band, the first query to arrive
    /// would answer for the second — silently, and differently depending on the order the search happened
    /// to reach them. The first version keyed this by band. It is the same planner-versus-engine
    /// disagreement the whole file exists to prevent, hiding in the cache rather than the rules.
    /// </para>
    /// </summary>
    private static bool Blocked(Map map, int x, int y, sbyte z, bool doorsShut)
    {
        var key = ((long)(z + 128) << 26) | ((long)x << 13) | (uint)y;

        if (_blockedByItems.TryGetValue(key, out var blocked))
        {
            return blocked;
        }

        blocked = BotStep.BlockedByItems(map, x, y, z, doorsShut);
        _blockedByItems[key] = blocked;

        return blocked;
    }

    private static bool FlankBlocked(Map map, int x, int y, int dir, in StepMask mask, bool doorsShut)
    {
        var fx = x;
        var fy = y;

        CalcMoves.Offset((Direction)dir, ref fx, ref fy);

        return Blocked(map, fx, fy, mask.GetWalkZ((Direction)dir), doorsShut);
    }

    private static int AddNode(int x, int y, sbyte z, int cost, int parent)
    {
        if (_nodeCount == _nodeX.Length)
        {
            Grow();
        }

        var node = _nodeCount++;

        _nodeX[node] = x;
        _nodeY[node] = y;
        _nodeZ[node] = z;
        _nodeCost[node] = cost;
        _nodeParent[node] = parent;
        _nodeClosed[node] = false;

        return node;
    }

    private static void Grow()
    {
        var size = _nodeX.Length * 2;

        Array.Resize(ref _nodeX, size);
        Array.Resize(ref _nodeY, size);
        Array.Resize(ref _nodeZ, size);
        Array.Resize(ref _nodeCost, size);
        Array.Resize(ref _nodeTotal, size);
        Array.Resize(ref _nodeParent, size);
        Array.Resize(ref _nodeClosed, size);

        logger.Information("Path search grew its node pool to {Size}", size);
    }

    private static void Rebuild(int node, List<Point3D> path)
    {
        _reversed.Clear();

        // The bot's own tile is the root and is not a step, so the walk stops before it.
        while (_nodeParent[node] >= 0)
        {
            _reversed.Add(new Point3D(_nodeX[node], _nodeY[node], _nodeZ[node]));
            node = _nodeParent[node];
        }

        for (var i = _reversed.Count - 1; i >= 0; i--)
        {
            path.Add(_reversed[i]);
        }
    }
}
