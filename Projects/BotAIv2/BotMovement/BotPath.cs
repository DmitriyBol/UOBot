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
    Sealed,

    Partial,

    Reached
}

/// <summary>What asking about the far side of a journey concluded.</summary>
public enum BotEnclosure
{
    Enclosed,

    TooBig,

    NoTime,

    NoFooting,

    Deferred
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

    private const int StraightCost = 10;

    private const int DiagonalCost = 14;

    public static int MinMargin { get; set; } = 256;

    public static int MaxMargin { get; set; } = 256;

    public static double CeilingMs { get; set; } = 60.0;

    public static int EnclosureCells { get; set; } = 2500;

    public static double EnclosureCeilingMs { get; set; } = 30.0;

    public static int EnclosureGapMs { get; set; } = 250;

    public static int StrandedCells { get; set; } = 25000;

    public static double StrandedCeilingMs { get; set; } = 150.0;

    public static double FloorMs { get; set; } = 1.0;

    public static double MsPerTile { get; set; } = 0.25;

    public static double ShortMs { get; set; } = 4.0;

    public static double WindowMs { get; set; } = 500.0;

    private const int WindowLengthMs = 1000;

    private const int ClockEvery = 64;

    private const int InitialNodes = 4096;

    private const int MinPocket = 2;

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

    private static readonly List<Point3D> _scratch = [];

    public static int MemoMs { get; set; } = 2000;

    public static long Recalled { get; private set; }

    public static double RecalledMs { get; private set; }

    private const int MemoSweep = 256;

    private readonly record struct MemoKey(int Map, Point3D From, Point3D To, int Tiles, double Ceiling);

    private sealed class Memo
    {
        public readonly List<Point3D> Path = [];

        public long At;

        public BotPathOutcome Outcome;

        public double Ms;
    }

    private static readonly Dictionary<MemoKey, Memo> _memo = [];

    private static readonly Stack<Memo> _memoFree = new();

    private static readonly List<MemoKey> _memoStale = [];

    private static bool Recall(in MemoKey key, List<Point3D> path, out BotPathOutcome outcome)
    {
        outcome = BotPathOutcome.Partial;

        if (!_memo.TryGetValue(key, out var memo) || Core.TickCount - memo.At >= MemoMs)
        {
            return false;
        }

        path.AddRange(memo.Path);
        outcome = memo.Outcome;
        Recalled++;
        RecalledMs += memo.Ms;

        return true;
    }

    private static void Keep(in MemoKey key, BotPathOutcome outcome, List<Point3D> path, double ms)
    {
        if (_memo.Count >= MemoSweep)
        {
            _memoStale.Clear();

            foreach (var (k, m) in _memo)
            {
                if (Core.TickCount - m.At >= MemoMs)
                {
                    _memoStale.Add(k);
                }
            }

            for (var i = 0; i < _memoStale.Count; i++)
            {
                if (_memo.Remove(_memoStale[i], out var gone) && _memoFree.Count < MemoSweep)
                {
                    _memoFree.Push(gone);
                }
            }

            if (_memo.Count >= MemoSweep * 4)
            {
                _memo.Clear();
            }
        }

        if (!_memo.TryGetValue(key, out var memo))
        {
            memo = _memoFree.Count > 0 ? _memoFree.Pop() : new Memo();
            _memo[key] = memo;
        }

        memo.Path.Clear();
        memo.Path.AddRange(path);
        memo.At = Core.TickCount;
        memo.Outcome = outcome;
        memo.Ms = ms;
    }

    private static readonly List<int> _frontier = [];

    private static long _windowEnds;

    private static bool _windowStarted;

    private static double _spentThisWindow;

    public static long Searches { get; private set; }

    public static long TilesExamined { get; private set; }

    public static long Reached { get; private set; }

    public static long PartialRuns { get; private set; }

    public static long LostToClock { get; private set; }

    public static long LostToBox { get; private set; }

    public static long LostToAvoiding { get; private set; }

    public static long LostToDoors { get; private set; }

    public static long LostToSize { get; private set; }

    private static bool Reached2(int reached) => reached >= 0;

    public static long SealedRuns { get; private set; }

    public static double TotalMs { get; private set; }

    public static double WorstMs { get; private set; }

    public static long Starved { get; private set; }

    public static bool LastStarved { get; private set; }

    public static double LastMs { get; private set; }

    public static long LastExpansions { get; private set; }

    public static long Lengthened { get; private set; }

    public static long PartialNear { get; private set; }

    public static long PartialStill { get; private set; }

    public static long PartialSpan { get; private set; }

    public static long PartialUnfooted { get; private set; }

    public const int Near = 32;

    public static long Probes { get; private set; }

    public static long Enclosed { get; private set; }

    public static long ProbedTooBig { get; private set; }

    public static long ProbedNoTime { get; private set; }

    public static long ProbedNoFooting { get; private set; }

    public static double ProbeMs { get; private set; }

    public static long ProbeTiles { get; private set; }

    public static long ProbeCells { get; private set; }

    private static long _probedAt;

    private static bool _probeStarted;

    public static void Reset()
    {
        _memo.Clear();
        Recalled = 0;
        RecalledMs = 0.0;
        Searches = 0;
        TilesExamined = 0;
        Reached = 0;
        PartialRuns = 0;
        LostToClock = 0;
        LostToBox = 0;
        LostToAvoiding = 0;
        LostToDoors = 0;
        LostToSize = 0;
        SealedRuns = 0;
        TotalMs = 0.0;
        WorstMs = 0.0;
        Starved = 0;
        Lengthened = 0;
        PartialNear = 0;
        PartialStill = 0;
        PartialSpan = 0;
        PartialUnfooted = 0;
        Probes = 0;
        Enclosed = 0;
        ProbedTooBig = 0;
        ProbedNoTime = 0;
        ProbedNoFooting = 0;
        ProbeMs = 0.0;
        ProbeTiles = 0;
        ProbeCells = 0;
        _probeStarted = false;
        _spentThisWindow = 0.0;
        _windowStarted = false;
    }

    public static string Describe() =>
        Searches == 0
            ? "no searches yet"
            : $"{Searches} searches, {TilesExamined} tiles examined, {TotalMs:F0}ms total ({TotalMs / Searches:F2}ms each, worst {WorstMs:F2}ms), {Reached} reached, {PartialRuns} partial, {SealedRuns} refused outright; {Starved} were handed less clock than they asked for and {Lengthened} asked for the whole ceiling because they were not closing; of the partials {PartialNear} were going somewhere within {Near} tiles and {PartialStill} ended no nearer than they started and {PartialUnfooted} never began because the bot's own tile forbids every direction, the average one {(PartialRuns > PartialUnfooted ? PartialSpan / (PartialRuns - PartialUnfooted) : 0)} tiles out; proofs of a pocket lost: {LostToClock} to the clock, {LostToBox} to the box, {LostToAvoiding} to avoiding danger, {LostToDoors} to shut doors, {LostToSize} too small to be one; {Probes} looks at the far side costing {ProbeMs:F0}ms over {ProbeTiles} expansions across {ProbeCells} cells of ground: {Enclosed} found a pocket, {ProbedTooBig} found the world, {ProbedNoTime} ran out of clock, {ProbedNoFooting} found nowhere at all to stand; {Recalled} asked again within {MemoMs / 1000.0:0.#}s of the same search and answered from it ({RecalledMs:F0}ms of searching not done again)";

    public static bool CanReach(Map map, Point3D from, Point3D to, BotArrival arrival, double ceilingMs = 0.0) =>
        Find(map, from, to, arrival, _scratch, ceilingMs: ceilingMs) == BotPathOutcome.Reached;

    public static bool ReachableWithDoorsShut(Map map, Point3D from, Point3D to, BotArrival arrival) =>
        Find(map, from, to, arrival, _scratch, doorsShut: true) == BotPathOutcome.Reached;

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
        LastMs = 0.0;
        LastExpansions = 0;

        if (map == null || map == Map.Internal)
        {
            return BotPathOutcome.Sealed;
        }

        if (arrival.Reached(from, to))
        {
            return BotPathOutcome.Reached;
        }

        if (BotReach.Ask(map, from, to, arrival) == BotReachVerdict.Sealed)
        {
            Searches++;
            SealedRuns++;

            return BotPathOutcome.Sealed;
        }

        var memoKey = new MemoKey(map.MapID, from, to, arrival.Tiles, ceilingMs);
        var memoable = MemoMs > 0 && avoid.Empty && !doorsShut;

        if (memoable && Recall(memoKey, path, out var recalled))
        {
            LastStarved = false;

            return recalled;
        }

        StepCache.Instance.BeginFindGeneration();

        Searches++;

        var started = Stopwatch.GetTimestamp();

        var span = Math.Max(Math.Abs(to.X - from.X), Math.Abs(to.Y - from.Y));

        if (ceilingMs > 0.0)
        {
            Lengthened++;
        }

        var wanted = ceilingMs > 0.0 ? ceilingMs : Math.Clamp(span * MsPerTile, ShortMs, CeilingMs);
        var allowanceMs = Allowance(wanted);
        var deadline = started + (long)(allowanceMs * Stopwatch.Frequency / 1000.0);

        LastStarved = allowanceMs < wanted;

        if (LastStarved)
        {
            Starved++;
        }

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

        if (BotStep.Mask(map, from.X, from.Y, startZ).WalkMask == 0)
        {
            PartialRuns++;
            PartialUnfooted++;

            return BotPathOutcome.Partial;
        }

        var start = AddNode(from.X, from.Y, startZ, 0, -1);

        _nodeTotal[start] = Heuristic(from.X, from.Y, to);
        _lookup[BotStep.Cell(from.X, from.Y, startZ)] = start;
        _open.Enqueue(start, _nodeTotal[start]);

        var nearest = -1;
        var nearestScore = int.MaxValue;

        var reached = -1;
        var expansions = 0;
        var outOfTime = false;

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

                if ((d & 1) == 1)
                {
                    var left = (d + 7) & 7;
                    var right = (d + 1) & 7;

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
        LastMs = elapsedMs;
        LastExpansions = expansions;

        if (elapsedMs > WorstMs)
        {
            WorstMs = elapsedMs;
        }

        if (reached >= 0)
        {
            Rebuild(reached, path);
            Reached++;

            if (memoable && !LastStarved)
            {
                Keep(memoKey, BotPathOutcome.Reached, path, elapsedMs);
            }

            return BotPathOutcome.Reached;
        }

        if (!Reached2(reached))
        {
            if (outOfTime)
            {
                LostToClock++;
            }
            else if (clipped)
            {
                LostToBox++;
            }
            else if (!avoid.Empty)
            {
                LostToAvoiding++;
            }
            else if (doorsShut)
            {
                LostToDoors++;
            }
            else if (_lookup.Count < MinPocket)
            {
                LostToSize++;
            }
        }

        if (!outOfTime && !clipped && avoid.Empty)
        {
            if (!doorsShut && _lookup.Count >= MinPocket)
            {
                BotReach.Record(map, _lookup.Keys, from);
            }

            SealedRuns++;

            return BotPathOutcome.Sealed;
        }

        if (nearest >= 0 && nearest != start)
        {
            Rebuild(nearest, path);
        }

        PartialRuns++;
        PartialSpan += span;

        if (span <= Near)
        {
            PartialNear++;
        }

        if (nearest < 0 || nearestScore >= Heuristic(from.X, from.Y, to))
        {
            PartialStill++;
        }

        if (memoable && !LastStarved)
        {
            Keep(memoKey, BotPathOutcome.Partial, path, elapsedMs);
        }

        return BotPathOutcome.Partial;
    }

    public static BotEnclosure Enclose(Map map, Point3D goal, BotArrival arrival, bool urgent = false)
    {
        if (map == null || map == Map.Internal)
        {
            return BotEnclosure.Deferred;
        }

        var now = Core.TickCount;

        if (!urgent && _probeStarted && now - _probedAt < EnclosureGapMs)
        {
            return BotEnclosure.Deferred;
        }

        _probeStarted = true;
        _probedAt = now;

        Probes++;

        if (!Footing(map, goal, arrival, out var start))
        {
            ProbedNoFooting++;

            return BotEnclosure.NoFooting;
        }

        var started = Stopwatch.GetTimestamp();

        var ceiling = urgent ? StrandedCeilingMs : EnclosureCeilingMs;
        var cells = urgent ? StrandedCells : EnclosureCells;
        var deadline = started + (long)(ceiling * Stopwatch.Frequency / 1000.0);

        StepCache.Instance.BeginFindGeneration();

        _lookup.Clear();
        _blockedByItems.Clear();
        _frontier.Clear();
        _nodeCount = 0;

        var startZ = (sbyte)Math.Clamp(start.Z, sbyte.MinValue, sbyte.MaxValue);
        var root = AddNode(start.X, start.Y, startZ, 0, -1);

        _lookup[BotStep.Cell(start.X, start.Y, startZ)] = root;
        _frontier.Add(root);

        var expansions = 0;
        var outcome = BotEnclosure.Enclosed;

        while (_frontier.Count > 0)
        {
            if ((expansions & (ClockEvery - 1)) == 0 && Stopwatch.GetTimestamp() >= deadline)
            {
                outcome = BotEnclosure.NoTime;

                break;
            }

            if (_lookup.Count > cells)
            {
                outcome = BotEnclosure.TooBig;

                break;
            }

            var current = _frontier[^1];

            _frontier.RemoveAt(_frontier.Count - 1);
            expansions++;

            var cx = _nodeX[current];
            var cy = _nodeY[current];
            var cz = _nodeZ[current];

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

                if ((d & 1) == 1)
                {
                    var left = (d + 7) & 7;
                    var right = (d + 1) & 7;

                    if ((walk & (1 << left)) == 0 || (walk & (1 << right)) == 0)
                    {
                        continue;
                    }

                    if (FlankBlocked(map, cx, cy, left, mask, doorsShut: false)
                        || FlankBlocked(map, cx, cy, right, mask, doorsShut: false))
                    {
                        continue;
                    }
                }

                var nx = cx;
                var ny = cy;

                CalcMoves.Offset((Direction)d, ref nx, ref ny);

                if (nx < 0 || ny < 0 || nx >= map.Width || ny >= map.Height)
                {
                    continue;
                }

                var nz = mask.GetWalkZ((Direction)d);

                if (Blocked(map, nx, ny, nz, doorsShut: false))
                {
                    continue;
                }

                var cell = BotStep.Cell(nx, ny, nz);

                if (_lookup.ContainsKey(cell))
                {
                    continue;
                }

                var node = AddNode(nx, ny, nz, 0, current);

                _lookup[cell] = node;
                _frontier.Add(node);
            }
        }

        var elapsedMs = (Stopwatch.GetTimestamp() - started) * 1000.0 / Stopwatch.Frequency;

        Spend(elapsedMs);

        ProbeMs += elapsedMs;
        ProbeTiles += expansions;
        ProbeCells += _lookup.Count;

        if (outcome == BotEnclosure.TooBig)
        {
            ProbedTooBig++;

            return outcome;
        }

        if (outcome == BotEnclosure.NoTime)
        {
            ProbedNoTime++;

            return outcome;
        }

        if (_lookup.Count < MinPocket)
        {
            ProbedTooBig++;

            return BotEnclosure.TooBig;
        }

        BotReach.Record(map, _lookup.Keys, start);
        Enclosed++;

        return BotEnclosure.Enclosed;
    }

    private static bool Footing(Map map, Point3D goal, BotArrival arrival, out Point3D at)
    {
        var goalZ = (sbyte)Math.Clamp(goal.Z, sbyte.MinValue, sbyte.MaxValue);

        if (BotStep.Mask(map, goal.X, goal.Y, goalZ).WalkMask != 0)
        {
            at = new Point3D(goal.X, goal.Y, goalZ);

            return true;
        }

        if (BotStep.Settle(map, goal.X, goal.Y, out var z) && Math.Abs(z - goal.Z) <= BotArrival.PersonHeight)
        {
            at = new Point3D(goal.X, goal.Y, z);

            return true;
        }

        var reach = Math.Min(arrival.Tiles, MaxFootingSweep);

        for (var r = 1; r <= reach; r++)
        {
            for (var dx = -r; dx <= r; dx++)
            {
                for (var dy = -r; dy <= r; dy++)
                {
                    if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != r)
                    {
                        continue;
                    }

                    if (BotStep.Settle(map, goal.X + dx, goal.Y + dy, out var rz)
                        && Math.Abs(rz - goal.Z) <= BotArrival.PersonHeight)
                    {
                        at = new Point3D(goal.X + dx, goal.Y + dy, rz);

                        return true;
                    }
                }
            }
        }

        at = Point3D.Zero;

        return false;
    }

    private const int MaxFootingSweep = 2;

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

        return Math.Clamp(Math.Min(requested, left), FloorMs, Math.Max(CeilingMs, requested));
    }

    private static void Spend(double ms) => _spentThisWindow += ms;

    private static int Heuristic(int x, int y, Point3D goal)
    {
        var dx = Math.Abs(x - goal.X);
        var dy = Math.Abs(y - goal.Y);

        return dx > dy
            ? DiagonalCost * dy + StraightCost * (dx - dy)
            : DiagonalCost * dx + StraightCost * (dy - dx);
    }

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
