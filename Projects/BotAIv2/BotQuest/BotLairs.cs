using System;
using System.Collections.Generic;
using Server.Engines.Spawners;
using Server.Logging;
using Server.Mobiles;

namespace Server.BotAI.V2;

public static class BotLairs
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotLairs));

    public static int IslandEdge { get; set; } = 5120;

    public static int Spawners { get; private set; }

    public static int Unknown { get; private set; }

    public static long Roadless { get; private set; }

    public static long Emptied { get; private set; }

    public static long Stopped { get; private set; }

    public static long Asked { get; private set; }

    private sealed class Lair
    {
        public BaseSpawner Spawner;

        public int Entry;

        public Point3D At;

        public int Roam;

        public int Road = Unasked;
    }

    private const int Unasked = -2;

    private const int NoRoad = -1;

    private const int Unmapped = int.MaxValue;

    private static Map _map;

    private static readonly Dictionary<Type, List<Lair>> _byType = [];

    private static readonly Dictionary<Type, List<Lair>> _byQuarry = [];

    private static readonly List<Lair> _none = [];

    public static void Survey(Map map)
    {
        if (map == null || map == Map.Internal || _map == map)
        {
            return;
        }

        _map = map;
        _byType.Clear();
        _byQuarry.Clear();
        Spawners = 0;
        Unknown = 0;

        var watch = System.Diagnostics.Stopwatch.StartNew();
        Dictionary<string, Type> named = new(StringComparer.OrdinalIgnoreCase);
        var bounds = new Rectangle2D(0, 0, Math.Min(IslandEdge, map.Width), map.Height);

        foreach (var spawner in map.GetItemsInBounds<BaseSpawner>(bounds))
        {
            if (spawner is not { Deleted: false } || spawner.Entries == null)
            {
                continue;
            }

            Spawners++;

            for (var i = 0; i < spawner.Entries.Count; i++)
            {
                var name = spawner.Entries[i]?.SpawnedName;

                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                if (!named.TryGetValue(name, out var type))
                {
                    type = AssemblyHandler.FindTypeByName(name.Trim());

                    if (type != null && !typeof(BaseCreature).IsAssignableFrom(type))
                    {
                        type = null;
                    }

                    if (type == null)
                    {
                        Unknown++;
                    }

                    named[name] = type;
                }

                if (type == null)
                {
                    continue;
                }

                if (!_byType.TryGetValue(type, out var lairs))
                {
                    lairs = [];
                    _byType[type] = lairs;
                }

                lairs.Add(new Lair { Spawner = spawner, Entry = i, At = spawner.Location, Roam = Math.Max(0, spawner.WalkingRange) });
            }
        }

        logger.Information(
            "The island's lairs were read off {Spawners} spawners west of x {Edge} in {Ms}ms: {Kinds} kinds of creature kept, {Unknown} names that are no creature",
            Spawners,
            IslandEdge,
            watch.ElapsedMilliseconds,
            _byType.Count,
            Unknown
        );
    }

    public static int Count(Map map, Type quarry) => Of(map, quarry).Count;

    public static double MetFight(Map map, Point3D where, int sight, out int kept, out double wide)
    {
        kept = 0;
        wide = 0.0;

        if (map == null || map == Map.Internal)
        {
            return 0.0;
        }

        Survey(map);

        if (_map != map)
        {
            return 0.0;
        }

        var worst = 0.0;
        var total = 0.0;
        var metWorst = 0.0;
        var metTotal = 0.0;

        foreach (var lairs in _byType.Values)
        {
            for (var i = 0; i < lairs.Count; i++)
            {
                var lair = lairs[i];
                var roam = lair.Roam;
                var reach = roam + sight;

                if (Math.Abs(lair.At.X - where.X) > reach || Math.Abs(lair.At.Y - where.Y) > reach)
                {
                    continue;
                }

                var across = Math.Min(lair.At.X + roam, where.X + sight) - Math.Max(lair.At.X - roam, where.X - sight) + 1;
                var down = Math.Min(lair.At.Y + roam, where.Y + sight) - Math.Max(lair.At.Y - roam, where.Y - sight) + 1;

                if (across <= 0 || down <= 0)
                {
                    continue;
                }

                var side = 2.0 * roam + 1;
                var share = Math.Min(1.0, across * (double)down / (side * side));

                var spawner = lair.Spawner;

                if (spawner is not { Deleted: false, Running: true } || spawner.Entries == null || lair.Entry >= spawner.Entries.Count)
                {
                    continue;
                }

                var spawned = spawner.Entries[lair.Entry]?.Spawned;

                if (spawned == null)
                {
                    continue;
                }

                var alive = 0;
                var strongest = 0.0;
                var sum = 0.0;

                for (var j = 0; j < spawned.Count; j++)
                {
                    if (spawned[j] is not BaseCreature { Deleted: false, Alive: true, Controlled: false, Summoned: false } creature
                        || creature is BaseVendor || creature.Map != map
                        || creature.FightMode is FightMode.None or FightMode.Aggressor or FightMode.Evil)
                    {
                        continue;
                    }

                    var power = BotThreat.Power(creature);

                    alive++;
                    sum += power;
                    strongest = Math.Max(strongest, power);
                }

                if (alive == 0)
                {
                    continue;
                }

                kept += alive;
                total += sum;
                worst = Math.Max(worst, strongest);
                metTotal += sum * share;
                metWorst = Math.Max(metWorst, strongest * Math.Min(1.0, alive * share));
            }
        }

        wide = worst + (total - worst) * BotThreat.Secondary;

        return (1 - BotThreat.Secondary) * metWorst + BotThreat.Secondary * metTotal;
    }

    public static List<string> Kinds(Map map)
    {
        List<string> names = [];

        if (map == null || map == Map.Internal)
        {
            return names;
        }

        Survey(map);

        if (_map != map)
        {
            return names;
        }

        foreach (var type in _byType.Keys)
        {
            if (!typeof(BaseVendor).IsAssignableFrom(type))
            {
                names.Add(type.Name);
            }
        }

        names.Sort(StringComparer.Ordinal);

        return names;
    }

    public static Point3D Closest(Map map, Type quarry, Point3D to, out int reach)
    {
        reach = int.MaxValue;

        var lairs = Of(map, quarry);
        Lair best = null;

        for (var i = 0; i < lairs.Count; i++)
        {
            var lair = lairs[i];
            var away = Math.Max(Math.Abs(lair.At.X - to.X), Math.Abs(lair.At.Y - to.Y)) - lair.Roam;

            if (away < reach)
            {
                reach = away;
                best = lair;
            }
        }

        return best?.At ?? Point3D.Zero;
    }

    public static Point3D Nearest(Map map, Type quarry, Point3D from, IReadOnlyList<Point3D> passed, out int roam)
    {
        Asked++;
        roam = 0;

        var lairs = Of(map, quarry);
        Lair best = null;
        var closest = int.MaxValue;

        for (var i = 0; i < lairs.Count; i++)
        {
            var lair = lairs[i];
            var away = Math.Max(Math.Abs(lair.At.X - from.X), Math.Abs(lair.At.Y - from.Y));

            if (away >= closest || Passed(passed, lair.At))
            {
                continue;
            }

            var cost = away + Math.Max(0, BotRoads.Behind(map, from, lair.At));

            if (cost >= closest || Unreached(map, lair) || !Holds(lair, quarry))
            {
                continue;
            }

            closest = cost;
            best = lair;
        }

        if (best == null)
        {
            return Point3D.Zero;
        }

        roam = best.Roam;

        return best.At;
    }

    private static bool Holds(Lair lair, Type quarry)
    {
        var spawner = lair.Spawner;

        if (spawner is not { Deleted: false, Running: true } || spawner.Entries == null || lair.Entry >= spawner.Entries.Count)
        {
            Stopped++;

            return false;
        }

        var spawned = spawner.Entries[lair.Entry]?.Spawned;

        if (spawned != null)
        {
            for (var i = 0; i < spawned.Count; i++)
            {
                if (spawned[i] is BaseCreature { Deleted: false, Alive: true, Controlled: false } creature && quarry.IsInstanceOfType(creature))
                {
                    return true;
                }
            }
        }

        Emptied++;

        return false;
    }

    private static List<Lair> Of(Map map, Type quarry)
    {
        if (map == null || map == Map.Internal || quarry == null)
        {
            return _none;
        }

        Survey(map);

        if (_map != map)
        {
            return _none;
        }

        if (_byQuarry.TryGetValue(quarry, out var gathered))
        {
            return gathered;
        }

        gathered = [];

        foreach (var (type, lairs) in _byType)
        {
            if (quarry.IsAssignableFrom(type))
            {
                gathered.AddRange(lairs);
            }
        }

        _byQuarry[quarry] = gathered;

        return gathered;
    }

    private static bool Unreached(Map map, Lair lair)
    {
        if (lair.Road == Unasked && BotRoads.Ready)
        {
            lair.Road = BotRoads.Covers(map, lair.At.X, lair.At.Y) ? BotRoads.FromHome(map, lair.At.X, lair.At.Y) : Unmapped;
        }

        if (lair.Road != NoRoad)
        {
            return false;
        }

        Roadless++;

        return true;
    }

    private static bool Passed(IReadOnlyList<Point3D> passed, Point3D at)
    {
        if (passed == null)
        {
            return false;
        }

        for (var i = 0; i < passed.Count; i++)
        {
            if (passed[i] == at)
            {
                return true;
            }
        }

        return false;
    }

    public static string Describe() =>
        _map == null
            ? "the island's lairs have not been read"
            : $"{Spawners} spawners read, {_byType.Count} kinds kept, {Asked} questions; lairs passed over: {Emptied} with nothing of theirs alive, {Stopped} stopped, {Roadless} with no road";

    public static void Forget()
    {
        _map = null;
        _byType.Clear();
        _byQuarry.Clear();
        Spawners = 0;
        Unknown = 0;
        Roadless = 0;
        Emptied = 0;
        Stopped = 0;
        Asked = 0;
    }
}
