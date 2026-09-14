using System;
using System.Collections.Generic;
using Server.Logging;

namespace Server.BotAI.V2;

/// <summary>What the reach ledger can say about a journey before anybody searches for it.</summary>
public enum BotReachVerdict
{
    Unknown,

    Connected,

    Sealed
}

/// <summary>
/// Which pockets of ground are closed, learned for nothing out of searches that failed.
///
/// <para>
/// <b>The idea.</b> Proving that somewhere cannot be reached is the most expensive question a bot ever
/// asks — the cheap searches run almost straight at their goal, while a refusal has to examine every tile
/// the bot can reach before it can say no. But a search that runs out of <em>ground</em> has, by
/// definition, just enumerated an entire connected pocket. So the expensive proof is paid <b>once per
/// pocket for the life of the shard</b>, written down, and shared by everybody. A walled yard, a crypt,
/// an island, somebody's back garden: each bills the population exactly once.
/// </para>
///
/// <para>
/// This is why there is no precomputation pass and no lattice. Felucca is 6144 × 4096 — twenty-five
/// million tiles, about a minute of work at the measured cost per tile — and that is a bad price for a
/// boot. It is also unnecessary: the questions worth refusing for free are "behind that railing" and
/// "across that water", and both are pockets. The mainland never becomes one, and should not.
/// </para>
///
/// <para>
/// <b>What makes a pocket trustworthy.</b> Only a search whose open set emptied <em>without ever being
/// clipped by its own search box</em> may record one. A search bounded by a box that ran out of tiles
/// inside the box has learned nothing about the world — and recording that would seal off half a
/// continent. See <see cref="BotPath"/>.
/// </para>
/// </summary>
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
        if (map == null || _pocketOf.Count == 0)
        {
            return BotReachVerdict.Unknown;
        }

        var hasHere = _pocketOf.TryGetValue(Fold(map, Cell(from)), out var here);

        if (hasHere)
        {
            here = Root(here);
        }

        var reach = Math.Min(arrival.Tiles, MaxSweep);
        var goalZ = (sbyte)Math.Clamp(goal.Z, sbyte.MinValue, sbyte.MaxValue);

        for (var dx = -reach; dx <= reach; dx++)
        {
            for (var dy = -reach; dy <= reach; dy++)
            {
                var x = goal.X + dx;
                var y = goal.Y + dy;

                Probes++;

                var settled = BotStep.Settle(map, x, y, out var z);
                var verdict = Look(map, BotStep.Cell(x, y, goalZ), hasHere, here, tally);

                if (verdict != BotReachVerdict.Unknown)
                {
                    return verdict;
                }

                if (settled && z != goalZ)
                {
                    verdict = Look(map, BotStep.Cell(x, y, z), hasHere, here, tally);

                    if (verdict != BotReachVerdict.Unknown)
                    {
                        return verdict;
                    }
                }

                if (!hasHere && settled)
                {
                    return BotReachVerdict.Unknown;
                }
            }
        }

        if (!hasHere)
        {
            return BotReachVerdict.Unknown;
        }

        if (tally)
        {
            Refused++;
        }

        return BotReachVerdict.Sealed;
    }

    private static BotReachVerdict Look(Map map, int cell, bool hasHere, int here, bool tally)
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

        if (tally)
        {
            Refused++;
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
    }

    public static string Describe() =>
        $"{Pockets} pockets of ground walked to their edges, {Refused} journeys refused without a search at a cost of {Probes} surface probes, {Healed} pockets that turned out to be one";

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
