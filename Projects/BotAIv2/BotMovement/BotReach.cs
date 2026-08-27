using System;
using System.Collections.Generic;
using Server.Logging;

namespace Server.BotAI.V2;

/// <summary>What the reach ledger can say about a journey before anybody searches for it.</summary>
public enum BotReachVerdict
{
    /// <summary>Nothing is known. Search.</summary>
    Unknown,

    /// <summary>Both ends are in the same sealed pocket of ground. There is a way; search for it.</summary>
    Connected,

    /// <summary>
    /// One end sits in a pocket of ground that has been walked to its edges, and the other end is not in
    /// it. There is no way, and no search will find one.
    /// </summary>
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

    /// <summary>Standing cell, map folded in, to the pocket it belongs to.</summary>
    private static readonly Dictionary<long, int> _pocketOf = [];

    /// <summary>
    /// Pocket to pocket, for pockets that turned out to be one. A four-line union-find, and it exists
    /// because of <see cref="Contradict"/> — geometry changes when somebody builds a house.
    /// </summary>
    private static readonly Dictionary<int, int> _merged = [];

    /// <summary>How many cells each pocket holds, for the summary. A yard is dozens; a crypt is hundreds.</summary>
    private static readonly Dictionary<int, int> _size = [];

    private static int _next;

    /// <summary>Pockets known. One line in the summary, and the only measure of what this is saving.</summary>
    public static int Pockets { get; private set; }

    /// <summary>Journeys refused outright, without a search.</summary>
    public static long Refused { get; private set; }

    /// <summary>Times two pockets turned out to be one because a bot walked between them.</summary>
    public static long Healed { get; private set; }

    /// <summary>
    /// Surface computations spent answering questions. Counted because they are the real cost of this
    /// ledger and they appear in none of the search figures — an unmeasured saving is a claim, not a
    /// measurement.
    /// </summary>
    public static long Probes { get; private set; }

    /// <summary>
    /// Records a pocket of ground that has been walked to its edges.
    ///
    /// <paramref name="cells"/> must be every standing cell the search closed, and the search must have
    /// emptied its open set with nothing clipped. Anything less and this seals off ground that is
    /// perfectly reachable — the one failure here that would be worse than the problem it solves.
    /// </summary>
    public static void Record(Map map, ICollection<int> cells)
    {
        if (map == null || cells == null || cells.Count == 0)
        {
            return;
        }

        // Already known: the same pocket proved again from a different tile inside it. Nothing to learn,
        // and re-numbering it would orphan the cells recorded the first time.
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
            "A pocket of {Count} tiles on {Map} has been walked to its edges; nothing outside it will be attempted from inside again",
            cells.Count,
            map
        );
    }

    /// <summary>
    /// Whether a journey is already known to be impossible, before a tile is examined.
    ///
    /// <para>
    /// Every cell within the arrival tolerance is tried rather than the goal alone. The goal's own Z is
    /// often not a floor at all — it is where a creature is standing, or the middle of a market — and a
    /// verdict of <see cref="BotReachVerdict.Sealed"/> is far too expensive to be wrong. Nine lookups for
    /// the ordinary case is a rounding error against a search.
    /// </para>
    /// </summary>
    public static BotReachVerdict Ask(Map map, Point3D from, Point3D goal, BotArrival arrival)
    {
        if (map == null || _pocketOf.Count == 0)
        {
            return BotReachVerdict.Unknown;
        }

        // The Z that comes in is used as it stands rather than looked up. Callers ask this about somewhere
        // a body is standing or could stand, so its height is already the standing height — and probing
        // for it again would put a surface computation on the hot path for no new information.
        var hasHere = _pocketOf.TryGetValue(Fold(map, Cell(from)), out var here);

        if (hasHere)
        {
            here = Root(here);
        }

        // Both directions are asked, and the second one is not symmetry for its own sake.
        //
        // Only checking "the bot is in a sealed pocket and the goal is not" leaves the commoner case
        // unanswered: a bot out on the mainland with a goal inside a sealed crypt. There, the near side is
        // unknown, so the ledger says nothing, the search runs against the whole continent, hits its time
        // ceiling, comes back partial — and the bot walks hopefully towards a place it can never enter, for
        // as long as it will put up with getting nowhere. The pocket was already paid for. It should
        // answer.
        var reach = Math.Min(arrival.Tiles, MaxSweep);

        for (var dx = -reach; dx <= reach; dx++)
        {
            for (var dy = -reach; dy <= reach; dy++)
            {
                var x = goal.X + dx;
                var y = goal.Y + dy;

                Probes++;

                if (!BotStep.Settle(map, x, y, out var z))
                {
                    continue;
                }

                if (!_pocketOf.TryGetValue(Fold(map, BotStep.Cell(x, y, z)), out var there))
                {
                    // Somewhere near the goal is ground nobody has filed. Whatever else is true, this
                    // journey is not provably impossible.
                    if (!hasHere)
                    {
                        return BotReachVerdict.Unknown;
                    }

                    continue;
                }

                there = Root(there);

                if (hasHere && there == here)
                {
                    return BotReachVerdict.Connected;
                }

                if (!hasHere)
                {
                    // The far side is sealed and the near side is not in it. Nothing can get in.
                    Refused++;

                    return BotReachVerdict.Sealed;
                }
            }
        }

        if (!hasHere)
        {
            return BotReachVerdict.Unknown;
        }

        Refused++;

        return BotReachVerdict.Sealed;
    }

    /// <summary>
    /// How wide a sweep around the goal is worth doing.
    ///
    /// The sweep is <c>(2n+1)²</c> cells and each one is a <see cref="BotStep.Settle"/> — a terrain height
    /// plus one or two surface computations, not a dictionary lookup. Nine of those for
    /// <see cref="BotArrival.Beside"/> is a rounding error against a search; four hundred and forty-one for
    /// <c>Within(10)</c> is not, and sixteen hundred for <c>Within(20)</c> would cost more than the search
    /// it is trying to avoid. Past this the goal tile alone is asked about, which can only make the ledger
    /// answer <see cref="BotReachVerdict.Unknown"/> more often — never wrongly.
    /// </summary>
    private const int MaxSweep = 2;

    /// <summary>
    /// Two cells that the ledger thought were in different pockets, and a bot has just walked from one to
    /// the other. The ledger was wrong; it is corrected.
    ///
    /// <para>
    /// Cheap insurance rather than a common event. A pocket is only sealed on evidence, but the evidence
    /// has a shelf life: somebody builds a house, a wall comes down, a gate is added. Without this the
    /// ledger would be a permanent hole in the population's behaviour with no way to notice — the worst
    /// shape a bug can take, and one this project has already paid for in other forms. With it, the world
    /// itself is the correction, and it arrives the first time a bot proves the ledger wrong by doing the
    /// thing the ledger says is impossible.
    /// </para>
    /// </summary>
    public static void Contradict(Map map, Point3D left, Point3D right)
    {
        if (map == null || _pocketOf.Count == 0)
        {
            return;
        }

        // Both points are places a bot has just been standing, so their own Z is the standing Z. No probe.
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

    /// <summary>
    /// Everything forgotten.
    ///
    /// <para>
    /// <b>This knowledge is deliberately not written to disk yet</b>, unlike the map of ground and the map
    /// of shops in the first version. The argument for persisting is the same and it is a good one: a
    /// proof thrown away at restart is a proof bought twice. The argument against is that a wrong
    /// "impossible" is invisible and permanent — a bot simply never goes somewhere, for ever, and nothing
    /// in any log says so. One shard run's worth of savings is already the whole point; the file can come
    /// later, once this has been watched behaving.
    /// </para>
    /// </summary>
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

    /// <summary>
    /// Cell plus map in one long. A cell fits in thirty bits — thirteen each for x and y, four for the
    /// height band — so the map index has the top of the word to itself.
    /// </summary>
    private static long Fold(Map map, int cell) => ((long)map.MapIndex << 32) | (uint)cell;

    /// <summary>The cell a point names, taking its own height as the standing height.</summary>
    private static int Cell(Point3D at) =>
        BotStep.Cell(at.X, at.Y, (sbyte)Math.Clamp(at.Z, sbyte.MinValue, sbyte.MaxValue));
}
