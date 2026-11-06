using System;
using System.Collections.Generic;
using Server.Logging;

namespace Server.BotAI.V2;

public enum BotReachVerdict
{

    Unknown,

    Connected,

    Sealed
}

public static class BotReach
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotReach));

    private static readonly Dictionary<long, int> _pocketOf = [];

    private static readonly Dictionary<int, int> _merged = [];

    private static readonly Dictionary<int, int> _size = [];

    private static int _next;

    public static int Pockets { get; private set; }

    public static long Refused { get; private set; }

    public static long Healed { get; private set; }

    public static long MapRefused { get; private set; }

    public static bool LastByMap { get; private set; }

    public static long Probes { get; private set; }

    public static void Record(Map map, ICollection<int> cells, Point3D where)
    {
        if (map == null || cells == null || cells.Count == 0)
        {
            return;
        }

        foreach (var cell in cells)
        {
            if (_pocketOf.ContainsKey(Fold(map, cell)))
            {
                return;
            }
        }

        var pocket = _next++;

        foreach (var cell in cells)
        {
            _pocketOf[Fold(map, cell)] = pocket;
        }

        _size[pocket] = cells.Count;
        Pockets++;

        logger.Information(
            "A pocket of {Count} tiles on {Map} around {Where} has been walked to its edges; journeys in or out of it will be refused without a search",
            cells.Count,
            map,
            where
        );
    }

    public static BotReachVerdict Ask(Map map, Point3D from, Point3D goal, BotArrival arrival, bool tally = true)
    {
        LastByMap = false;

        if (map != null && BotReachMap.Refuses(map, from, goal, Math.Max(arrival.Tiles, 2)))
        {
            LastByMap = true;

            if (tally)
            {
                MapRefused++;
            }

            return BotReachVerdict.Sealed;
        }

        if (map == null || _pocketOf.Count == 0)
        {
            return BotReachVerdict.Unknown;
        }

        if (BotReachMap.Joins(map, from, goal))
        {
            if (tally)
            {
                MapJoined++;
            }

            return BotReachVerdict.Unknown;
        }

        var hasHere = _pocketOf.TryGetValue(Fold(map, Cell(from)), out var here);

        if (hasHere)
        {
            here = Root(here);
        }

        var verdict = Sweep(map, goal, arrival, hasHere, here);

        if (verdict == BotReachVerdict.Sealed && !hasHere && Beside(map, from, out var beside))
        {
            NearSide++;
            verdict = Sweep(map, goal, arrival, true, beside);
        }

        if (verdict == BotReachVerdict.Sealed && tally)
        {
            Refused++;
        }

        return verdict;
    }

    private static BotReachVerdict Sweep(Map map, Point3D goal, BotArrival arrival, bool hasHere, int here)
    {

        var reach = Math.Min(arrival.Tiles, MaxSweep);
        var goalZ = (sbyte)Math.Clamp(goal.Z, sbyte.MinValue, sbyte.MaxValue);
        var sealedSeen = false;

        for (var dx = -reach; dx <= reach; dx++)
        {
            for (var dy = -reach; dy <= reach; dy++)
            {
                var x = goal.X + dx;
                var y = goal.Y + dy;
                var goalCell = BotStep.Cell(x, y, goalZ);

                Probes++;

                var verdict = Look(map, goalCell, hasHere, here);

                if (verdict == BotReachVerdict.Connected)
                {
                    return verdict;
                }

                if (verdict == BotReachVerdict.Sealed)
                {
                    sealedSeen = true;

                    continue;
                }

                var open = BotStep.Ground(map, x, y, goalZ, BotArrival.PersonHeight - 1, out var z)
                    || BotStep.Settle(map, x, y, out z);

                if (open && BotStep.Cell(x, y, z) != goalCell)
                {
                    verdict = Look(map, BotStep.Cell(x, y, z), hasHere, here);

                    if (verdict == BotReachVerdict.Connected)
                    {
                        return verdict;
                    }

                    if (verdict == BotReachVerdict.Sealed)
                    {
                        sealedSeen = true;

                        continue;
                    }
                }

                if (!hasHere && open)
                {
                    return BotReachVerdict.Unknown;
                }
            }
        }

        if (!hasHere && !sealedSeen)
        {
            return BotReachVerdict.Unknown;
        }

        return BotReachVerdict.Sealed;
    }

    private static bool Beside(Map map, Point3D at, out int pocket)
    {
        pocket = 0;

        var z = (sbyte)Math.Clamp(at.Z, sbyte.MinValue, sbyte.MaxValue);

        for (var dx = -1; dx <= 1; dx++)
        {
            for (var dy = -1; dy <= 1; dy++)
            {
                var x = at.X + dx;
                var y = at.Y + dy;

                if (!BotStep.Ground(map, x, y, z, BotStep.GroundReach, out var floor) && !BotStep.Settle(map, x, y, out floor))
                {
                    continue;
                }

                if (_pocketOf.TryGetValue(Fold(map, BotStep.Cell(x, y, floor)), out var filed))
                {
                    pocket = Root(filed);

                    return true;
                }
            }
        }

        return false;
    }

    public static long MapJoined { get; private set; }

    public static long NearSide { get; private set; }

    private static BotReachVerdict Look(Map map, int cell, bool hasHere, int here)
    {
        if (!_pocketOf.TryGetValue(Fold(map, cell), out var there))
        {
            return BotReachVerdict.Unknown;
        }

        there = Root(there);

        if (hasHere)
        {
            return there == here ? BotReachVerdict.Connected : BotReachVerdict.Unknown;
        }

        return BotReachVerdict.Sealed;
    }

    private const int MaxSweep = 2;

    public static void Contradict(Map map, Point3D left, Point3D right)
    {
        if (map == null || _pocketOf.Count == 0)
        {
            return;
        }

        if (!_pocketOf.TryGetValue(Fold(map, Cell(left)), out var a)
            || !_pocketOf.TryGetValue(Fold(map, Cell(right)), out var b))
        {
            return;
        }

        a = Root(a);
        b = Root(b);

        if (a == b)
        {
            return;
        }

        _merged[b] = a;
        Pockets--;
        Healed++;

        logger.Information(
            "Two pockets of ground on {Map} turned out to be one; a bot walked from {Left} to {Right}",
            map,
            left,
            right
        );
    }

    public static void Reset()
    {
        _pocketOf.Clear();
        _merged.Clear();
        _size.Clear();
        _next = 0;
        Pockets = 0;
        Refused = 0;
        Healed = 0;
        Probes = 0;
        MapRefused = 0;
        MapJoined = 0;
        NearSide = 0;
    }

    public static string Describe() =>
        $"{Pockets} pockets of ground walked to their edges, {Refused} journeys refused without a search at a cost of {Probes} surface probes, {Healed} pockets that turned out to be one; {MapRefused} refused by the reach map; {MapJoined} asks the reach map answered for the ledger, both ends open ground a walk from home reaches through the gates, and {NearSide} bodies standing off every flood placed in the pocket beside them";

    private static int Root(int pocket)
    {
        while (_merged.TryGetValue(pocket, out var parent))
        {
            pocket = parent;
        }

        return pocket;
    }

    private static long Fold(Map map, int cell) => ((long)map.MapIndex << 32) | (uint)cell;

    private static int Cell(Point3D at) =>
        BotStep.Cell(at.X, at.Y, (sbyte)Math.Clamp(at.Z, sbyte.MinValue, sbyte.MaxValue));
}
