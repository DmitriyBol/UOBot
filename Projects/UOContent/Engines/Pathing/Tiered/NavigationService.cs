using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Server.Engines.Pathing.Cache;
using Server.Logging;

namespace Server.Engines.Pathing.Tiered;

/// <summary>
/// The tiered navigation, as the rest of the shard sees it: one graph a map, drawn on the game loop a slice at a time,
/// kept in step with the houses raised and taken down, and asked for routes.
///
/// <para>
/// <b>The tiers.</b> A walk nearer than a cluster or two is the precise search's alone (Short: the caller's own tile
/// planner, with everything dynamic in it — doors, creatures, items). A longer one is asked of the first level of the
/// graph (<see cref="HierarchicalPlanner"/>, Medium), which answers with one point a cluster for the precise search to
/// walk a leg at a time, <see cref="NavStatus.Direct"/> when the two ends are joined inside one cluster, and
/// <see cref="NavStatus.Unreachable"/> — without searching, from the components — when the ground has no way. The long
/// tier (regions of 128 tiles, teleporters, a route cache) sits above it.
/// </para>
///
/// <para>
/// <b>On the loop, and never off it.</b> The graph is drawn from the map's tiles, which are game state: a slice of at
/// most <see cref="SliceMs"/> every <see cref="EveryMs"/>, nearest the prioritized place first (<see cref="Prepare"/>),
/// so the ground round home is ready within seconds of a boot and the rest follows.
/// </para>
/// </summary>
public static class NavigationService
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(NavigationService));

    public static bool Enabled { get; set; } = true;

    public static Func<int, int, int> Danger { get; set; }

    internal static int DangerAt(NavGraph graph, int node)
    {
        var danger = Danger;

        return danger == null ? 0 : danger(graph.X(node), graph.Y(node));
    }

    public static int SliceMs { get; set; } = 8;

    public static int EveryMs { get; set; } = 50;

    private static readonly Dictionary<int, NavGraph> _graphs = new();

    private static readonly Dictionary<int, long> _begun = new();

    private static readonly Dictionary<int, double> _spent = new();

    private static readonly NavWindow _bakeWindow = new();

    private static readonly NavWindowCache _cache = new();

    private static readonly HierarchicalPlanner _medium = new() { Cache = _cache, MaxExpansions = 12000 };

    private static readonly StrategicPlanner _long = new(_medium, _cache);

    private static readonly NavRouteMemory _memory = new();

    private static readonly List<Point3D> _groundPoints = [];

    public static NavRouteMemory Memory => _memory;

    public static long Remembered { get; private set; }

    public static double RememberedMs { get; private set; }

    public static long Plans { get; private set; }

    public static double PlansMs { get; private set; }

    public static long EligiblePlans { get; private set; }

    public static double EligiblePlansMs { get; private set; }

    public static long GroundPlans { get; private set; }

    public static long RoundPlans { get; private set; }

    public static long RoundFallbacks { get; private set; }

    public static double SavedMs =>
        EligiblePlans <= 0 ? 0.0 : Math.Max(0.0, Remembered * (EligiblePlansMs / EligiblePlans) - RememberedMs);

    public static int RouteWriteEveryMs { get; set; } = 600000;

    public static long RoutesWritten { get; private set; }

    private static volatile bool _routesWriting;

    private static Timer _routeTimer;

    private static readonly Dictionary<int, ulong> _fingerprints = new();

    public static long LongRoutes { get; private set; }

    private static readonly long[] _statuses = new long[Enum.GetValues<NavStatus>().Length];

    public static long Status(NavStatus status) =>
        (int)status >= 0 && (int)status < _statuses.Length ? _statuses[(int)status] : 0;

    private static Timer _timer;

    public static bool Persist { get; set; } = true;

    /// <summary>A graph being read from disk on a worker thread, per map, and what it read.</summary>
    private sealed class Load
    {
        public ulong Fingerprint;

        public Point3D Center;

        public volatile bool Done;

        public volatile NavGraphSnapshot Result;

        public string Error;

        public List<NavRouteRecord> Routes;

        public string RoutesError;
    }

    private static readonly Dictionary<int, Load> _loads = new();

    private static readonly HashSet<int> _saved = new();

    private static volatile bool _writing;

    public static long Routes { get; private set; }

    public static double RouteMs { get; private set; }

    public static double WorstRouteMs { get; private set; }

    public static double SlowRouteMs { get; set; } = 250.0;

    public static long SlowRoutes { get; private set; }

    private static int _routeMediumCalls;
    private static long _routeMediumExpanded;
    private static int _routeLongCalls;
    private static long _routeLongExpanded;

    public static HierarchicalPlanner Medium => _medium;

    public static StrategicPlanner Long => _long;

    public static void Configure()
    {
        Enabled = ServerConfiguration.GetOrUpdateSetting("pathfinding.tiered.enable", true);
        Persist = ServerConfiguration.GetOrUpdateSetting("pathfinding.tiered.persist", true);
        SliceMs = ServerConfiguration.GetOrUpdateSetting("pathfinding.tiered.sliceMs", SliceMs);
        EveryMs = ServerConfiguration.GetOrUpdateSetting("pathfinding.tiered.everyMs", EveryMs);
        _medium.MaxExpansions = ServerConfiguration.GetOrUpdateSetting("pathfinding.medium.maxExpansions", _medium.MaxExpansions);
        _long.MaxExpansions = ServerConfiguration.GetOrUpdateSetting("pathfinding.long.maxExpansions", _long.MaxExpansions);
        _cache.Capacity = ServerConfiguration.GetOrUpdateSetting("pathfinding.medium.windowCache", _cache.Capacity);
        _memory.Enabled = ServerConfiguration.GetOrUpdateSetting("pathfinding.routes.remember", _memory.Enabled);
        _memory.Capacity = ServerConfiguration.GetOrUpdateSetting("pathfinding.routes.capacity", _memory.Capacity);
        _memory.DetourMs = ServerConfiguration.GetOrUpdateSetting("pathfinding.routes.detourMs", _memory.DetourMs);
        RouteWriteEveryMs = ServerConfiguration.GetOrUpdateSetting("pathfinding.routes.writeEveryMs", RouteWriteEveryMs);

        EventSink.Shutdown += () => WriteRoutes(true);
    }

    public static NavGraph Graph(Map map) =>
        map != null && _graphs.TryGetValue(map.MapID, out var graph) ? graph : null;

    public static void Prepare(Map map, Point3D center)
    {
        if (!Enabled || map == null || map == Map.Internal)
        {
            return;
        }

        if (!_graphs.TryGetValue(map.MapID, out var graph))
        {
            graph = new NavGraph(new MapNavTerrain(map), map);
            _graphs[map.MapID] = graph;
            _begun[map.MapID] = Core.TickCount;
            _spent[map.MapID] = 0.0;

            logger.Information(
                "Navigation: drawing the graph of {Map}, {Clusters} clusters of {Side} tiles, outward from {Center}",
                map,
                graph.ClustersX * graph.ClustersY,
                NavGraph.Side,
                center
            );
        }

        graph.Prioritize(center.X, center.Y);

        if (Persist && !_loads.ContainsKey(map.MapID) && graph.Built == 0)
        {
            var load = new Load { Fingerprint = StepCacheFile.ComputeFingerprint(map.MapID), Center = center };
            var path = NavGraphFile.PathFor(map);
            var routes = NavRouteFile.PathFor(map);
            var width = map.Width;
            var height = map.Height;

            _loads[map.MapID] = load;
            _fingerprints[map.MapID] = load.Fingerprint;

            Task.Run(() =>
            {
                try
                {
                    load.Result = NavGraphFile.Read(path, load.Fingerprint, width, height);
                }
                catch (Exception e)
                {
                    load.Error = e.Message;
                }

                load.Routes = NavRouteFile.TryRead(routes, load.Fingerprint, width, height, out load.RoutesError);
                load.Done = true;
            });
        }

        if (Persist && _routeTimer == null && RouteWriteEveryMs > 0)
        {
            var every = TimeSpan.FromMilliseconds(RouteWriteEveryMs);

            _routeTimer = Timer.DelayCall(every, every, () => WriteRoutes(false));
        }

        _timer ??= new DrawTimer();

        if (!_timer.Running)
        {
            _timer.Start();
        }
    }

    public static NavStatus Route(Map map, Point3D from, Point3D to, List<Point3D> points) => Route(map, from, to, points, 0);

    public static NavStatus Route(Map map, Point3D from, Point3D to, List<Point3D> points, int reach)
    {
        points.Clear();

        if (!Enabled || map == null || map == Map.Internal)
        {
            return NavStatus.Unplaced;
        }

        if (!_graphs.TryGetValue(map.MapID, out var graph))
        {
            return NavStatus.Pending;
        }

        var started = Stopwatch.GetTimestamp();

        _routeMediumCalls = 0;
        _routeMediumExpanded = 0;
        _routeLongCalls = 0;
        _routeLongExpanded = 0;

        var recall = _memory.Recall(graph, _cache, _medium.Anchorage, from, to, points, Danger);
        NavStatus status;

        if (recall is NavRecall.Served or NavRecall.Detoured)
        {
            status = NavStatus.Ok;
            Remembered++;
            RememberedMs += (Stopwatch.GetTimestamp() - started) * 1000.0 / Stopwatch.Frequency;
        }
        else
        {
            status = Planned(graph, from, to, points, reach, recall);

            var planMs = (Stopwatch.GetTimestamp() - started) * 1000.0 / Stopwatch.Frequency;

            Plans++;
            PlansMs += planMs;

            if (recall != NavRecall.Skipped && status == NavStatus.Ok)
            {
                EligiblePlans++;
                EligiblePlansMs += planMs;
            }
        }

        var ms = (Stopwatch.GetTimestamp() - started) * 1000.0 / Stopwatch.Frequency;

        Routes++;
        RouteMs += ms;
        WorstRouteMs = Math.Max(WorstRouteMs, ms);
        _statuses[(int)status]++;

        if (ms >= SlowRouteMs)
        {
            SlowRoutes++;
            logger.Information(
                "Navigation: a slow route, {Ms:F0}ms, from ({FX}, {FY}, {FZ}) to ({TX}, {TY}, {TZ}), {Apart} tiles apart: {Status}, memory {Recall}; the medium tier asked {MediumCalls} times over {MediumExpanded} nodes, the long tier {LongCalls} times over {LongExpanded}",
                ms,
                from.X,
                from.Y,
                from.Z,
                to.X,
                to.Y,
                to.Z,
                Math.Max(Math.Abs(from.X - to.X), Math.Abs(from.Y - to.Y)),
                status,
                recall,
                _routeMediumCalls,
                _routeMediumExpanded,
                _routeLongCalls,
                _routeLongExpanded
            );
        }

        return status;
    }

    private static NavStatus Plan(NavGraph graph, Point3D from, Point3D to, List<Point3D> points, int reach)
    {
        var status = _medium.Route(graph, from.X, from.Y, from.Z, to.X, to.Y, to.Z, points, reach);

        _routeMediumCalls++;
        _routeMediumExpanded += _medium.LastExpanded;

        if (status == NavStatus.BudgetExceeded)
        {
            LongRoutes++;

            var goal = _medium.LastGoal;

            status = _long.Route(graph, from.X, from.Y, from.Z, goal.X, goal.Y, goal.Z, points);

            _routeLongCalls++;
            _routeLongExpanded += _long.LastExpanded;
        }

        return status;
    }

    private static NavStatus Planned(NavGraph graph, Point3D from, Point3D to, List<Point3D> points, int reach, NavRecall recall)
    {
        var danger = Danger;

        if (recall == NavRecall.Skipped || !_memory.Enabled)
        {
            return Plan(graph, from, to, points, reach);
        }

        if (recall == NavRecall.Bloodied)
        {
            var groundDanger = _memory.LastGroundDanger;
            var round = Plan(graph, from, to, points, reach);

            RoundPlans++;

            if (round == NavStatus.Ok)
            {
                _memory.OfferDetour(graph, _cache, _medium.Anchorage, from, _medium.LastGoal, _medium.LastPath, groundDanger, danger);

                return round;
            }

            if (_memory.LastGround(points))
            {
                RoundFallbacks++;

                return NavStatus.Ok;
            }

            return round;
        }

        NavStatus ground;

        Danger = null;

        try
        {
            ground = Plan(graph, from, to, points, reach);
        }
        finally
        {
            Danger = danger;
        }

        GroundPlans++;

        if (ground != NavStatus.Ok)
        {
            return ground;
        }

        _memory.Offer(
            graph,
            _cache,
            _medium.Anchorage,
            from,
            _medium.LastGoal,
            _medium.LastPath,
            _medium.Shunning ? _medium.ShunMs : 0
        );

        if (NavRouteMemory.Quiet(graph, _medium.LastPath, danger, out var along))
        {
            return NavStatus.Ok;
        }

        _groundPoints.Clear();
        _groundPoints.AddRange(points);

        var status = Plan(graph, from, to, points, reach);

        RoundPlans++;

        if (status == NavStatus.Ok)
        {
            _memory.OfferDetour(graph, _cache, _medium.Anchorage, from, _medium.LastGoal, _medium.LastPath, along, danger);

            return status;
        }

        RoundFallbacks++;
        points.Clear();
        points.AddRange(_groundPoints);

        return NavStatus.Ok;
    }

    public static int ComponentOf(Map map, Point3D at) =>
        Enabled && map != null && map != Map.Internal && _graphs.TryGetValue(map.MapID, out var graph)
            ? _medium.ComponentAt(graph, at.X, at.Y, at.Z)
            : -1;

    public static int ComponentEpoch(Map map) =>
        map != null && _graphs.TryGetValue(map.MapID, out var graph) ? graph.ComponentEpoch : 0;

    public static bool ComponentsCounted(Map map) =>
        map != null && _graphs.TryGetValue(map.MapID, out var graph) && graph.ComponentsValid;

    public static string Explain(Map map, Point3D from, Point3D to) =>
        map != null && _graphs.TryGetValue(map.MapID, out var graph)
            ? _medium.Explain(graph, from.X, from.Y, from.Z, to.X, to.Y, to.Z)
            : "no graph for that map";

    public static void Shun(Map map, Point3D point)
    {
        if (map != null && _graphs.TryGetValue(map.MapID, out var graph))
        {
            _medium.Shun(graph, point.X, point.Y);
            _memory.Shun(graph, point.X, point.Y);
        }
    }

    public static void GroundChanged(Map map, Rectangle2D bounds)
    {
        if (map == null || !_graphs.TryGetValue(map.MapID, out var graph))
        {
            return;
        }

        graph.MarkDirty(bounds.Start.X, bounds.Start.Y, bounds.End.X, bounds.End.Y);
        _cache.Invalidate(graph, bounds.Start.X, bounds.Start.Y, bounds.End.X, bounds.End.Y);
        _memory.GroundChanged(graph, bounds.Start.X, bounds.Start.Y, bounds.End.X, bounds.End.Y);

        _timer ??= new DrawTimer();

        if (!_timer.Running)
        {
            _timer.Start();
        }
    }

    private static void Draw()
    {
        var idle = true;

        foreach (var (mapId, graph) in _graphs)
        {
            if (_loads.TryGetValue(mapId, out var load) && load != null)
            {
                if (!load.Done)
                {
                    idle = false;

                    continue;
                }

                _loads[mapId] = null;

                if (load.Result != null)
                {
                    var began = Stopwatch.GetTimestamp();

                    graph.Restore(load.Result, load.Center.X, load.Center.Y);

                    var housed = 0;

                    foreach (var c in load.Result.Housed)
                    {
                        graph.MarkDirtyCluster(c);
                        housed++;
                    }

                    foreach (var c in Housed(graph))
                    {
                        graph.MarkDirtyCluster(c);
                        housed++;
                    }

                    _saved.Add(mapId);

                    logger.Information(
                        "Navigation: the graph of {Map} was read from disk: {Graph}; {Housed} clusters by houses drawn again; {Ms:F0}ms of the loop to put it in place",
                        graph.Map,
                        graph.Describe(),
                        housed,
                        (Stopwatch.GetTimestamp() - began) * 1000.0 / Stopwatch.Frequency
                    );
                }
                else if (load.Error != null)
                {
                    logger.Warning("Navigation: the graph of {Map} on disk could not be read ({Error}); drawing it", graph.Map, load.Error);
                }

                if (load.Routes != null)
                {
                    var adopted = _memory.Adopt(graph, load.Routes);

                    logger.Information(
                        "Navigation: {Adopted} remembered roads of {Map} read from disk ({Read} in the file); each is proved against the graph by its clusters' signatures the first time it is asked",
                        adopted,
                        graph.Map,
                        load.Routes.Count
                    );
                }
                else if (load.RoutesError != null)
                {
                    logger.Warning("Navigation: the remembered roads of {Map} could not be read ({Error}); they are found again", graph.Map, load.RoutesError);
                }
            }

            if (graph.Complete && graph.ComponentsValid)
            {
                if (Persist && !_saved.Contains(mapId) && !_writing && !World.Saving)
                {
                    Save(mapId, graph);
                }

                if (!_long.Prebuilt(graph))
                {
                    idle = false;

                    var begun = Stopwatch.GetTimestamp();

                    if (_long.Prebuild(graph, begun + (long)(SliceMs * Stopwatch.Frequency / 1000.0)))
                    {
                        logger.Information(
                            "Navigation: the regions of {Map} are built: {Regions} regions of {Tiles} tiles",
                            graph.Map,
                            _long.Regions,
                            StrategicPlanner.RegionTiles
                        );
                    }

                    _spent[mapId] += (Stopwatch.GetTimestamp() - begun) * 1000.0 / Stopwatch.Frequency;

                    break;
                }

                continue;
            }

            idle = false;

            var started = Stopwatch.GetTimestamp();
            var deadline = started + (long)(SliceMs * Stopwatch.Frequency / 1000.0);
            var wasComplete = graph.Complete;

            graph.Slice(_bakeWindow, deadline);

            _spent[mapId] += (Stopwatch.GetTimestamp() - started) * 1000.0 / Stopwatch.Frequency;

            if (graph.Complete && !wasComplete)
            {
                logger.Information(
                    "Navigation: the graph of {Map} is drawn: {Graph}; {Ms:F0}ms of the loop over {Seconds:F0}s",
                    graph.Map,
                    graph.Describe(),
                    _spent[mapId],
                    (Core.TickCount - _begun[mapId]) / 1000.0
                );
            }

            break;
        }

        if (idle)
        {
            _timer?.Stop();
        }
    }

    private static List<int> Housed(NavGraph graph)
    {
        var housed = new List<int>();
        var map = graph.Map;

        if (map == null)
        {
            return housed;
        }

        for (var cy = 0; cy < graph.ClustersY; cy++)
        {
            for (var cx = 0; cx < graph.ClustersX; cx++)
            {
                if (map.GetRealSector(cx, cy).HasMultis)
                {
                    for (var dy = -1; dy <= 1; dy++)
                    {
                        for (var dx = -1; dx <= 1; dx++)
                        {
                            if (cx + dx >= 0 && cy + dy >= 0 && cx + dx < graph.ClustersX && cy + dy < graph.ClustersY)
                            {
                                housed.Add((cy + dy) * graph.ClustersX + cx + dx);
                            }
                        }
                    }
                }
            }
        }

        return housed;
    }

    private static void Save(int mapId, NavGraph graph)
    {
        var snapshot = graph.Snapshot(StepCacheFile.ComputeFingerprint(mapId), Housed(graph).ToArray());

        if (snapshot == null)
        {
            return;
        }

        _saved.Add(mapId);
        _writing = true;

        var path = NavGraphFile.PathFor(graph.Map);
        var map = graph.Map;

        Task.Run(() =>
        {
            try
            {
                var watch = Stopwatch.StartNew();

                NavGraphFile.Write(path, snapshot);
                logger.Information("Navigation: the graph of {Map} was written to disk in {Ms}ms", map, watch.ElapsedMilliseconds);
            }
            catch (Exception e)
            {
                logger.Warning("Navigation: the graph of {Map} could not be written ({Error})", map, e.Message);
            }

            _writing = false;
        });
    }

    private static void WriteRoutes(bool now)
    {
        if (!Persist || !_memory.Enabled || !_memory.Dirty || _routesWriting || !now && World.Saving)
        {
            return;
        }

        var files = new List<(string Path, ulong Fingerprint, int Width, int Height, string Map, List<NavRouteRecord> Routes)>();

        foreach (var (mapId, graph) in _graphs)
        {
            if (graph.Map != null && _fingerprints.TryGetValue(mapId, out var fingerprint))
            {
                files.Add((NavRouteFile.PathFor(graph.Map), fingerprint, graph.Terrain.Width, graph.Terrain.Height, graph.Map.ToString(), _memory.Records(graph)));
            }
        }

        if (files.Count == 0)
        {
            return;
        }

        _memory.Dirty = false;
        _routesWriting = true;

        void Write()
        {
            foreach (var f in files)
            {
                try
                {
                    var watch = Stopwatch.StartNew();

                    NavRouteFile.Write(f.Path, f.Fingerprint, f.Width, f.Height, f.Routes);
                    logger.Information("Navigation: {Roads} remembered roads of {Map} written to disk in {Ms}ms", f.Routes.Count, f.Map, watch.ElapsedMilliseconds);
                }
                catch (Exception e)
                {
                    logger.Warning("Navigation: the remembered roads of {Map} could not be written ({Error})", f.Map, e.Message);
                }
            }

            _routesWriting = false;
        }

        foreach (var f in files)
        {
            RoutesWritten += f.Routes.Count;
        }

        if (now)
        {
            Write();
        }
        else
        {
            Task.Run(Write);
        }
    }

    public static string Describe()
    {
        if (!Enabled)
        {
            return "tiered navigation is off";
        }

        if (_graphs.Count == 0)
        {
            return "tiered navigation: no graph asked for yet";
        }

        var parts = new List<string>();

        foreach (var (mapId, graph) in _graphs)
        {
            parts.Add($"{graph.Map}: {graph.Describe()}, {_spent[mapId]:F0}ms of the loop");
        }

        return $"tiered navigation: {string.Join("; ", parts)}; {Routes} routes asked in {RouteMs:F0}ms "
            + $"(worst {WorstRouteMs:F1}ms, {SlowRoutes} slower than {SlowRouteMs:F0}ms, {_medium.Expanded} nodes expanded): {_statuses[(int)NavStatus.Ok]} routed, "
            + $"{_statuses[(int)NavStatus.Direct]} direct, {_statuses[(int)NavStatus.Unreachable]} unreachable, "
            + $"{_statuses[(int)NavStatus.Pending]} not drawn yet, {_statuses[(int)NavStatus.BudgetExceeded]} over budget, "
            + $"{_statuses[(int)NavStatus.Unplaced]} unplaced; windows {_cache.Hits} kept and {_cache.Misses} probed; "
            + $"{_medium.Shunned} gates shunned; {_medium.Cycles + _long.Cycles} parent chains found looping and cut (the last at node {_long.LastCycleNode}); the long tier took {LongRoutes} routes over budget ({_long.Expanded} region "
            + $"nodes expanded, {_long.Regions} regions built); routes from memory: {Remembered} served in {RememberedMs:F0}ms against {Plans} planned in {PlansMs:F0}ms "
            + $"({EligiblePlans} of them far enough to remember, {(EligiblePlans > 0 ? EligiblePlansMs / EligiblePlans : 0.0):F2}ms each), about {SavedMs:F0}ms saved; "
            + $"{GroundPlans} planned on the ground, {RoundPlans} round the danger after, {RoundFallbacks} walked on the ground for want of a way round; "
            + $"{_memory.Describe()}; {RoutesWritten} written to disk";
    }

    private sealed class DrawTimer : Timer
    {
        public DrawTimer() : base(TimeSpan.FromMilliseconds(EveryMs), TimeSpan.FromMilliseconds(EveryMs))
        {
        }

        protected override void OnTick() => Draw();
    }
}
