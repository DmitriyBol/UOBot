using System;
using System.Collections.Generic;
using System.Diagnostics;
using Server.Logging;
using CalcMoves = Server.Movement.Movement;

namespace Server.BotAI.V2;

/// <summary>
/// How far each piece of ground around the population's home lies from it by road, rather than in a straight line.
///
/// <para>
/// <b>A dart knew only the straight line, and the straight line lies about ground behind a river.</b> The hunt throws
/// its darts at ground within five hundred tiles of the asker as the crow flies, and on 15.09.2026 the ground north-west
/// of the banks west of Britain — x below 1140, y between 1050 and 1420 — took one arriving prowl in 44 sessions against
/// some 450 failed ones on that day alone. Asked through the door, the pathfinder found no road into it from the banks at
/// (1161, 1345) and (1242, 1239) at five times its ceiling, and a road of 520 tiles from reachable ground in the
/// south-west at (1006, 1641): the way in runs round by the south, about 1,100 tiles from Britain for a place 440 tiles
/// off. No search the shard pays for walks away from its goal for that long, so every such dart ended at a bank.
/// </para>
///
/// <para>
/// <b>Notes about failed places could not stand in for it.</b> Replayed over 14–15.09, reading the quadrant baulk in the
/// darts would have caught 130 of 1,889 prowl failures and turned away 228 walks that arrived or found a fight; a note of
/// failures without arrivals at 30, 60 or 90 tiles did no better. A sample over an area needs a fact about the area.
/// </para>
///
/// <para>
/// <b>What it is.</b> One flood from home, breadth first, stepping exactly as the planner and the far-side look step — the
/// engine's step masks, both flanks of a diagonal clear, no item standing where the step lands — inside a square of
/// <see cref="Reach"/> tiles each way. Each tile keeps the steps of the shortest road to it from home. Heights are kept
/// apart the way the search keeps them, so a floor above a street is its own place.
/// </para>
///
/// <para>
/// <b>What it can say about a bot standing somewhere else.</b> A walk is not the same both ways — the world allows drops
/// it will not allow back up — so from another start this proves one thing: the road from there to a place is at least
/// the road from home to the place less the road from home to the start. That is the case it was built for. Bots live
/// around home, and the ground that fails them is ground whose road from home runs far past its straight line.
/// </para>
///
/// <para>
/// <b>Paid once per world load, on the game loop, in slices.</b> The map is game state, so no thread may read it; a
/// slice of at most <see cref="SliceMs"/> every <see cref="SliceEveryMs"/> keeps the loop's share small, and until the
/// flood is finished every answer is "unknown" and nothing reading it changes behaviour.
/// </para>
/// </summary>
public static class BotRoads
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotRoads));

    public static bool Running { get; set; } = true;

    public static int Reach { get; set; } = 640;

    public static double SliceMs { get; set; } = 8.0;

    public static int SliceEveryMs { get; set; } = 100;

    private const int ClockEvery = 64;

    private const int StartSweep = 8;

    private static Map _map;

    private static Point3D _home;

    public static Point3D Home => _home;

    private static int _x0;

    private static int _y0;

    private static int _side;

    private static ushort[] _steps;

    private static sbyte[] _heights;

    private static readonly Dictionary<int, ushort> _stacked = [];

    private static List<int> _ring = [];

    private static List<int> _next = [];

    private static int _at;

    private static int _depth;

    private static bool _begun;

    private static long _begunTick;

    public static bool Ready { get; private set; }

    public static bool Failed { get; private set; }

    public static long Tiles { get; private set; }

    public static long Slices { get; private set; }

    public static double SpentMs { get; private set; }

    public static long TookMs { get; private set; }

    public static long Asked { get; private set; }

    public static long Unknown { get; private set; }

    public static void Slice()
    {
        if (!Running || Ready || Failed)
        {
            return;
        }

        if (!_begun && !Begin())
        {
            return;
        }

        var started = Stopwatch.GetTimestamp();
        var deadline = started + (long)(SliceMs * Stopwatch.Frequency / 1000.0);
        var map = _map;
        var expansions = 0;
        var done = false;

        while (true)
        {
            if (_at >= _ring.Count)
            {
                if (_next.Count == 0)
                {
                    done = true;

                    break;
                }

                (_ring, _next) = (_next, _ring);
                _next.Clear();
                _at = 0;
                _depth++;

                continue;
            }

            if ((++expansions & (ClockEvery - 1)) == 0 && Stopwatch.GetTimestamp() >= deadline)
            {
                break;
            }

            Expand(map, _ring[_at++]);
        }

        Slices++;
        SpentMs += (Stopwatch.GetTimestamp() - started) * 1000.0 / Stopwatch.Frequency;

        if (done)
        {
            Finish();
        }
    }

    public static bool Covers(Map map, int x, int y) =>
        Ready && map != null && map == _map && x >= _x0 && y >= _y0 && x < _x0 + _side && y < _y0 + _side;

    public static int FromHome(Map map, int x, int y)
    {
        Asked++;

        if (!Ready || map == null || map != _map)
        {
            Unknown++;

            return -1;
        }

        var best = int.MaxValue;

        for (var dy = -1; dy <= 1; dy++)
        {
            for (var dx = -1; dx <= 1; dx++)
            {
                var tx = x + dx - _x0;
                var ty = y + dy - _y0;

                if (tx < 0 || ty < 0 || tx >= _side || ty >= _side)
                {
                    continue;
                }

                var steps = _steps[ty * _side + tx];

                if (steps == 0)
                {
                    continue;
                }

                var road = steps - 1 + (dx == 0 && dy == 0 ? 0 : 1);

                if (road < best)
                {
                    best = road;
                }
            }
        }

        if (best == int.MaxValue)
        {
            Unknown++;

            return -1;
        }

        return best;
    }

    public static int Detour(Map map, Point3D from, Point3D to)
    {
        var road = FromHome(map, to.X, to.Y);

        if (road < 0)
        {
            return -1;
        }

        var own = FromHome(map, from.X, from.Y);

        if (own < 0)
        {
            return -1;
        }

        var straight = Math.Max(Math.Abs(to.X - from.X), Math.Abs(to.Y - from.Y));

        return Math.Max(0, road - own - straight);
    }

    public static string Describe()
    {
        if (!Running)
        {
            return "the road map is off";
        }

        if (Failed)
        {
            return $"the road map could not begin: nothing within {StartSweep} tiles of home at {_home} will take a body";
        }

        if (!Ready)
        {
            return _begun
                ? $"the road map is being drawn: {Tiles} tiles reached, {_depth} steps out, {Slices} slices and {SpentMs:F0}ms of the loop so far"
                : "the road map has not begun";
        }

        return $"the road map from {_home}: {Tiles} tiles reached within {(_side - 1) / 2} tiles each way, the farthest {_depth} steps of road, drawn in {Slices} slices and {SpentMs:F0}ms of the loop over {TookMs / 1000.0:F1}s; {Asked} questions asked of it, {Unknown} without an answer";
    }

    public static void Forget()
    {
        Ready = false;
        Failed = false;
        _begun = false;
        _map = null;
        _stacked.Clear();
        _ring.Clear();
        _next.Clear();
        _at = 0;
        _depth = 0;
        Tiles = 0;
        Slices = 0;
        SpentMs = 0.0;
        TookMs = 0;
        Asked = 0;
        Unknown = 0;
    }

    private static bool Begin()
    {
        var map = BotPopulation.Home;

        if (map == null || map == Map.Internal)
        {
            return false;
        }

        _begun = true;
        _begunTick = Core.TickCount;
        _map = map;
        _home = BotPopulation.Where;

        var reach = Math.Clamp(Reach, 16, 2000);

        _side = 2 * reach + 1;
        _x0 = _home.X - reach;
        _y0 = _home.Y - reach;

        var cells = _side * _side;

        if (_steps == null || _steps.Length != cells)
        {
            _steps = new ushort[cells];
            _heights = new sbyte[cells];
        }
        else
        {
            Array.Clear(_steps);
        }

        _stacked.Clear();
        _ring.Clear();
        _next.Clear();
        _at = 0;
        _depth = 0;
        Tiles = 0;

        if (!Footing(map, _home, out var x, out var y, out var z))
        {
            Failed = true;

            logger.Warning(
                "Roads: nothing within {Sweep} tiles of home at {Home} on {Map} will take a body, so there is no road map and the darts keep the straight line",
                StartSweep,
                _home,
                map
            );

            return false;
        }

        Mark(x, y, z, 0);
        (_ring, _next) = (_next, _ring);

        return true;
    }

    private static bool Footing(Map map, Point3D home, out int x, out int y, out sbyte z)
    {
        var homeZ = (sbyte)Math.Clamp(home.Z, sbyte.MinValue, sbyte.MaxValue);

        for (var r = 0; r <= StartSweep; r++)
        {
            for (var dy = -r; dy <= r; dy++)
            {
                for (var dx = -r; dx <= r; dx++)
                {
                    if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != r)
                    {
                        continue;
                    }

                    x = home.X + dx;
                    y = home.Y + dy;

                    if (!BotStep.OnMap(map, x, y))
                    {
                        continue;
                    }

                    if (BotStep.Mask(map, x, y, homeZ).WalkMask != 0)
                    {
                        z = homeZ;

                        return true;
                    }

                    if (BotStep.Settle(map, x, y, out z) && BotStep.Mask(map, x, y, z).WalkMask != 0)
                    {
                        return true;
                    }
                }
            }
        }

        x = 0;
        y = 0;
        z = 0;

        return false;
    }

    private static bool Seen(int index, int x, int y, sbyte z)
    {
        if (_steps[index] == 0)
        {
            return false;
        }

        var cell = BotStep.Cell(x, y, z);

        return cell == BotStep.Cell(x, y, _heights[index]) || _stacked.ContainsKey(cell);
    }

    private static void Mark(int x, int y, sbyte z, int depth)
    {
        var index = (y - _y0) * _side + (x - _x0);
        var steps = (ushort)Math.Min(depth + 1, ushort.MaxValue);

        if (_steps[index] == 0)
        {
            _steps[index] = steps;
            _heights[index] = z;
        }
        else
        {
            _stacked[BotStep.Cell(x, y, z)] = steps;
        }

        Tiles++;
        _next.Add((index << 8) | (z + 128));
    }

    private static void Expand(Map map, int node)
    {
        var index = node >> 8;
        var z = (sbyte)((node & 0xFF) - 128);
        var cx = _x0 + index % _side;
        var cy = _y0 + index / _side;

        var mask = BotStep.Mask(map, cx, cy, z);
        var walk = mask.WalkMask;

        if (walk == 0)
        {
            return;
        }

        for (var d = 0; d < 8; d++)
        {
            if ((walk & (1 << d)) == 0)
            {
                continue;
            }

            var nx = cx;
            var ny = cy;

            CalcMoves.Offset((Direction)d, ref nx, ref ny);

            if (nx < _x0 || ny < _y0 || nx >= _x0 + _side || ny >= _y0 + _side || nx < 0 || ny < 0 || nx >= map.Width
                || ny >= map.Height)
            {
                continue;
            }

            var nz = mask.GetWalkZ((Direction)d);

            if (Seen((ny - _y0) * _side + (nx - _x0), nx, ny, nz))
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

                if (BotStep.FlankBlocked(map, cx, cy, left, mask) || BotStep.FlankBlocked(map, cx, cy, right, mask))
                {
                    continue;
                }
            }

            if (BotStep.BlockedByItems(map, nx, ny, nz))
            {
                continue;
            }

            Mark(nx, ny, nz, _depth + 1);
        }
    }

    private static void Finish()
    {
        Ready = true;
        TookMs = Core.TickCount - _begunTick;

        _ring.Clear();
        _next.Clear();

        logger.Information(
            "Roads: {Tiles} tiles reached from home at {Home} within {Reach} tiles each way, the farthest {Deepest} steps of road; {Slices} slices, {Ms:F0}ms of the loop over {Seconds:F1}s",
            Tiles,
            _home,
            (_side - 1) / 2,
            _depth,
            Slices,
            SpentMs,
            TookMs / 1000.0
        );

        BotSeat.Audit();
    }
}
