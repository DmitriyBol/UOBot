using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Server.Logging;
using Server.Mobiles;

namespace Server.BotAI.V2;

/// <summary>The dashboard's copy of the zones: the JSON and the tag a browser may send back to be told nothing changed.</summary>
public sealed record BotZonePage(string Json, string ETag);

/// <summary>
/// The danger and safety zones of the bots' home map: where creatures that attack on sight patrol, how far their sight
/// reaches, how strong they are against a bot, and where only passive animals live. The answer the planners, the hunt and the
/// dashboard read.
///
/// <para>
/// <b>Patrick's order of 30.09.2026, evening:</b> walk the world with Argus and the other debuggers, mark every mob zone on the
/// map with how many live there and how strong they are; put the zones on the dashboard as separate areas that live in
/// memory, update, have smoothed corners and grow more precise; where no mobs are, bots may route safe paths through; where
/// mobs are and their patrol or aggro could touch someone, mark the ground potentially dangerous — "maximum precision, it
/// drives safe paths and the quality of the hunting".
/// </para>
///
/// <para>
/// <b>How the precision grows.</b> At boot every spawner is a declared haunt: a disc of its walking range round it
/// (<see cref="BotHaunts"/>). The sweep (<see cref="BotZoneScan"/>) passes every living creature every few seconds and records
/// where attackers are seen <em>awake</em> (<see cref="BotZoneRaster"/>); when a haunt has been watched awake long enough
/// (<see cref="BotHaunt.Confidence"/> at <see cref="ObservedWins"/>) its declared disc shrinks to the farthest its creatures
/// were seen from home and the observed patrol draws the rest. The ground only wakes where a client or a bot is (<see cref="BotWake"/>), so the debuggers walk the
/// least-watched zones and wake them (<see cref="BotZoneTour"/>). Bot deaths are counted in the zone they fall in, and a zone
/// that keeps killing is read one level deadlier than its arithmetic (<see cref="DeathsPromote"/>).
/// </para>
///
/// <para>
/// <b>Cost.</b> On the loop: a strip of the spawner survey a beat, the sweep within <see cref="BotZoneScan.BudgetMs"/> a beat,
/// the tour once a second, and a gather of plain values every <see cref="RefreshMs"/>; every one of them is measured and
/// printed in the <c>Zones:</c> line. The build — painting, labelling, fringes, outlines and the zones' JSON — is pure
/// arithmetic on that gathered copy (<see cref="BotZoneBuild"/>) and runs off the loop when the machine has
/// <see cref="OffLoopCores"/> cores, published through the one volatile reference <see cref="State"/> (CLAUDE.md rules 3, 10).
/// </para>
///
/// <para>
/// <b>The queries are one array read.</b> <see cref="LevelAt(Map, int, int)"/> is asked per node by a path planner, millions of
/// times: it reads the published state once, indexes the raster by <c>(y &gt;&gt; 2) * width + (x &gt;&gt; 2)</c>, and at most
/// reads one zone out of an array. Nothing in it allocates, locks or walks anything.
/// </para>
/// </summary>
public static class BotZones
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotZones));

    public static bool Running { get; set; } = true;

    public static int TickMs { get; set; } = 250;

    public static int RefreshMs { get; set; } = 20000;

    public static int JsonEveryMs { get; set; } = 4000;

    public static int ReferenceEveryMs { get; set; } = 120000;

    public static double ReferenceFloor { get; set; } = 1116;

    public static int DeclaredMost { get; set; } = 64;

    public static int LiveRadius { get; set; } = 4;

    public static int LooseRadius { get; set; } = 12;

    public static int PassiveRadius { get; set; } = 8;

    public static int WaterRadius { get; set; } = 8;

    public static int WaterGroup { get; set; } = 40;

    public static int ObservedMargin { get; set; } = 1;

    public static double ObservedLeast { get; set; } = 0.5;

    public static double ObservedWins { get; set; } = 0.8;

    public static int ConfidentSeconds { get; set; } = 600;

    public static int ConfidentSamples { get; set; } = 60;

    public static int AwakeGapMs { get; set; } = 45000;

    public static int PerceptionMost { get; set; } = 20;

    public static int WaterMeleeReach { get; set; } = 3;

    public static int WaterCasterReach { get; set; } = 12;

    public static int RootedReach { get; set; } = 3;

    public static int ConvergeTiles { get; set; } = 20;

    public static double DeadlyVsBot { get; set; } = 6.0;

    public static double EmptyWeight { get; set; } = 0.5;

    public static int DeathsPromote { get; set; } = 6;

    public static int DeathsWindowHours { get; set; } = 24;

    public static int ClearLeastLive { get; set; } = 3;

    public static int MostPoints { get; set; } = 64;

    public static int ClearMostPoints { get; set; } = 32;

    public static int SeaMostPoints { get; set; } = 16;

    public static int MostRings { get; set; } = 4;

    public static int MostKinds { get; set; } = 8;

    public static double SimplifyTiles { get; set; } = 2.0;

    public static int SmoothPasses { get; set; } = 2;

    public static bool OffLoop { get; set; } = true;

    public static int OffLoopCores { get; set; } = 4;

    public static int InlineRefreshMs { get; set; } = 120000;

    public static int BuildLostMs { get; set; } = 120000;

    public static BotZoneState State => _state;

    private static volatile BotZoneState _state;

    private static volatile BotZonePage _page = new(Empty(), "\"z0\"");

    public static string Json => _page.Json;

    public static BotZonePage Page => _page;

    public static Map Map { get; private set; }

    public static long Builds { get; private set; }

    public static long BuildsOffLoop { get; private set; }

    public static long BuildFailures { get; private set; }

    public static double GatherMs { get; private set; }

    public static double GatherMsTotal { get; private set; }

    public static double BuildMsTotal { get; private set; }

    public static double BuildMsWorst { get; private set; }

    public static double InlineMsTotal { get; private set; }

    public static long Ticks { get; private set; }

    public static double SpentMs { get; private set; }

    public static double WorstTickMs { get; private set; }

    public static double TourSpentMs { get; private set; }

    public static long JsonWrites { get; private set; }

    public static double JsonMsTotal { get; private set; }

    public static int JsonBytes { get; private set; }

    public static long DeathsHeard { get; private set; }

    public static double Reference { get; private set; }

    private static BotZoneState _seen;

    private static bool _building;

    private static bool _lostSaid;

    private static int _generation;

    private static long _dispatchedTick;

    private static long _refreshedTick;

    private static long _jsonTick;

    private static long _referenceTick;

    private static long _touredTick;

    private static long _startedTick;

    private static bool _jsonDirty = true;

    private static long _version;

    private static readonly ushort[][] _buffers = new ushort[2][];

    private static readonly List<(int X, int Y, long Tick)> _deaths = [];

    public static int MostDeaths { get; set; } = 20000;

    private static Timer _timer;

    public static BotZoneLevel LevelAt(Map map, int x, int y)
    {
        var state = _state;

        if (state == null || map != state.Map)
        {
            return BotZoneLevel.Clear;
        }

        var cx = x >> BotZoneState.Shift;
        var cy = y >> BotZoneState.Shift;

        if ((uint)cx >= (uint)state.Width || (uint)cy >= (uint)state.Height)
        {
            return BotZoneLevel.Clear;
        }

        var v = state.Cells[cy * state.Width + cx];

        if (v == 0)
        {
            return BotZoneLevel.Clear;
        }

        return (v & BotZoneState.Fringe) != 0 ? BotZoneLevel.Edge : state.Zones[(v & BotZoneState.IndexMask) - 1].Level;
    }

    public static BotZoneLevel LevelAt(Map map, int x, int y, out float vsBot, out float threat)
    {
        vsBot = 0f;
        threat = 0f;

        var state = _state;

        if (state == null || map != state.Map)
        {
            return BotZoneLevel.Clear;
        }

        var cx = x >> BotZoneState.Shift;
        var cy = y >> BotZoneState.Shift;

        if ((uint)cx >= (uint)state.Width || (uint)cy >= (uint)state.Height)
        {
            return BotZoneLevel.Clear;
        }

        var v = state.Cells[cy * state.Width + cx];

        if (v == 0)
        {
            return BotZoneLevel.Clear;
        }

        var zone = state.Zones[(v & BotZoneState.IndexMask) - 1];
        var level = (v & BotZoneState.Fringe) != 0 ? BotZoneLevel.Edge : zone.Level;

        if (level != BotZoneLevel.Clear)
        {
            vsBot = (float)zone.VsBot;
            threat = (float)zone.Threat;
        }

        return level;
    }

    public static BotZone ZoneAt(Map map, int x, int y)
    {
        var state = _state;

        return state == null || map != state.Map ? null : state.Owner(x, y);
    }

    public static bool IsFringe(Map map, int x, int y) => LevelAt(map, x, y) == BotZoneLevel.Edge;

    public static BotZone Find(int id)
    {
        var zones = _state?.Zones;

        if (zones == null)
        {
            return null;
        }

        for (var i = 0; i < zones.Length; i++)
        {
            if (zones[i].Id == id)
            {
                return zones[i];
            }
        }

        return null;
    }

    public static string Word(BotZoneLevel level) =>
        level switch
        {
            BotZoneLevel.Edge => "edge",
            BotZoneLevel.Hostile => "hostile",
            BotZoneLevel.Deadly => "deadly",
            _ => "clear"
        };

    public static void Fell(Map map, Point3D at)
    {
        if (!Running || map == null || map != Map)
        {
            return;
        }

        DeathsHeard++;
        _deaths.Add((at.X, at.Y, Core.TickCount));

        if (_deaths.Count > Math.Max(100, MostDeaths))
        {
            _deaths.RemoveRange(0, _deaths.Count - MostDeaths);
        }
    }

    public static void Start(Map map)
    {
        Stop();

        if (map == null || map == Map.Internal)
        {
            logger.Warning("Zones: the population has no home map; nothing is kept");

            return;
        }

        var now = Core.TickCount;

        Map = map;
        _startedTick = now;
        _refreshedTick = now - RefreshMs;
        _jsonTick = now - JsonEveryMs;
        _referenceTick = now - ReferenceEveryMs;
        _touredTick = now;
        _dispatchedTick = now;
        _jsonDirty = true;

        BotHaunts.Open(map, now);
        BotZoneRaster.Open(map, now);
        BotZoneScan.Open(map, now);
        BotZoneTour.Open(now);
        BotZoneMemory.Load(map);

        _timer = new ZoneTimer(TimeSpan.FromMilliseconds(Math.Max(50, TickMs)));
        _timer.Start();
    }

    public static void Stop()
    {
        _timer?.Stop();
        _timer = null;
    }

    public static void Tick()
    {
        var map = Map;

        if (map == null)
        {
            return;
        }

        var began = Stopwatch.GetTimestamp();
        var now = Core.TickCount;

        if (!Running)
        {
            BotZoneTour.Release(now);

            return;
        }

        BotHaunts.Step(now);
        BotHaunts.Measure();

        if (BotHaunts.Surveyed)
        {
            BotZoneScan.Step(now);
            BotZoneMemory.Apply(now);
        }

        if (now - _touredTick >= BotZoneTour.TickMs)
        {
            _touredTick = now;

            var tourBegan = Stopwatch.GetTimestamp();

            if (BotZoneTour.Beat(map, now))
            {
                _jsonDirty = true;
            }

            TourSpentMs += Stopwatch.GetElapsedTime(tourBegan).TotalMilliseconds;
        }

        if (now - _referenceTick >= ReferenceEveryMs)
        {
            _referenceTick = now;
            Reference = Median();
        }

        var state = _state;

        if (!ReferenceEquals(state, _seen) && state != null)
        {
            if (state.Generation == _generation)
            {
                Published(state);
            }
            else
            {
                _seen = null;
                _state = null;
                _building = false;
            }
        }

        if (_building && !_lostSaid && now - _dispatchedTick > BuildLostMs)
        {
            _lostSaid = true;
            BuildFailures++;

            logger.Warning("Zones: a build dispatched {Seconds}s ago has not published; no other will start until it does", (now - _dispatchedTick) / 1000);
        }

        var every = OffLoopNow ? RefreshMs : Math.Max(RefreshMs, InlineRefreshMs);

        if (!_building && now - _refreshedTick >= every && Ready() && !SaveUnderWay())
        {
            _refreshedTick = now;
            Dispatch(map, now);
        }

        if (_jsonDirty && now - _jsonTick >= JsonEveryMs)
        {
            _jsonTick = now;
            _jsonDirty = false;
            WriteJson();
        }

        BotZoneMemory.Beat(now);

        var ms = Stopwatch.GetElapsedTime(began).TotalMilliseconds;

        Ticks++;
        SpentMs += ms;

        if (ms > WorstTickMs)
        {
            WorstTickMs = ms;
        }
    }

    public static bool SaveUnderWay() => World.WorldState is WorldState.Saving or WorldState.PendingSave;

    public static bool OffLoopNow => OffLoop && Environment.ProcessorCount >= Math.Max(2, OffLoopCores);

    private static bool Ready() =>
        BotHaunts.Surveyed && BotZoneScan.Sweeps > 0 && (BotHaunts.Pending == 0 || Core.TickCount - _startedTick > 120000);

    private static void Published(BotZoneState state)
    {
        _seen = state;
        _building = false;
        _lostSaid = false;
        _jsonDirty = true;
        Builds++;
        BuildMsTotal += state.BuildMs;

        if (state.BuildMs > BuildMsWorst)
        {
            BuildMsWorst = state.BuildMs;
        }

        if (state.OffLoop)
        {
            BuildsOffLoop++;
        }

        if (Builds == 1)
        {
            logger.Information(
                "Zones: the first map is built: {Zones} zones ({Clear} clear, {Hostile} hostile, {Deadly} deadly, {Water} at sea) off {Spawners} spawners and {Creatures} creatures, in {Ms:F1}ms {Where}",
                state.Zones.Length,
                state.Clear,
                state.Hostile,
                state.Deadly,
                state.WaterZones,
                state.Spawners,
                state.Creatures,
                state.BuildMs,
                state.OffLoop ? "off the loop" : "on the loop"
            );
        }
    }

    private static void Dispatch(Map map, long now)
    {
        var began = Stopwatch.GetTimestamp();
        var input = Gather(map, now);

        GatherMs = Stopwatch.GetElapsedTime(began).TotalMilliseconds;
        GatherMsTotal += GatherMs;

        _building = true;
        _dispatchedTick = now;

        if (input.OffLoop)
        {
            _ = Task.Run(
                () =>
                {
                    try
                    {
                        _state = BotZoneBuild.Build(input);
                    }
                    catch (Exception e)
                    {
                        Core.LoopContext.Post(
                            () =>
                            {
                                _building = false;
                                BuildFailures++;
                                logger.Error(e, "Zones: a build off the loop threw; the last map stands");
                            }
                        );
                    }
                }
            );

            return;
        }

        var inline = Stopwatch.GetTimestamp();

        try
        {
            _state = BotZoneBuild.Build(input);
        }
        catch (Exception e)
        {
            _building = false;
            BuildFailures++;
            logger.Error(e, "Zones: a build threw; the last map stands");
        }

        InlineMsTotal += Stopwatch.GetElapsedTime(inline).TotalMilliseconds;
    }

    private static BotZoneInput Gather(Map map, long now)
    {
        Dictionary<BotZoneKind, int> kindIndex = [];
        List<BotZoneKindIn> kinds = [];

        int IndexOf(BotZoneKind kind)
        {
            if (!kindIndex.TryGetValue(kind, out var index))
            {
                index = kinds.Count;
                kindIndex[kind] = index;
                kinds.Add(new BotZoneKindIn(kind.Name, kind.Power, kind.Aggressive, kind.Caster, kind.Perception, kind.Water, kind.Rooted));
            }

            return index;
        }

        var all = BotHaunts.All;
        List<BotZoneHauntIn> haunts = new(all.Count);
        List<BotHauntEntry> entries = [];

        for (var i = 0; i < all.Count; i++)
        {
            var haunt = all[i];
            var weights = 0;

            haunt.Index = -1;
            entries.Clear();

            for (var e = 0; e < haunt.Entries.Length; e++)
            {
                if (!haunt.Entries[e].Kind.Townsfolk)
                {
                    entries.Add(haunt.Entries[e]);
                    weights += haunt.Entries[e].Probability;
                }
            }

            if (entries.Count == 0)
            {
                continue;
            }

            var kindsOf = new int[entries.Count];
            var expected = new double[entries.Count];

            for (var e = 0; e < entries.Count; e++)
            {
                kindsOf[e] = IndexOf(entries[e].Kind);

                expected[e] = Math.Min(entries[e].Max, haunt.Count * entries[e].Probability / (double)Math.Max(1, weights));
            }

            haunt.Index = haunts.Count;

            haunts.Add(
                new BotZoneHauntIn
                {
                    X = haunt.At.X,
                    Y = haunt.At.Y,
                    Z = haunt.At.Z,
                    Radius = haunt.Radius,
                    Count = haunt.Count,
                    Running = haunt.Running,
                    InTown = haunt.InTown,
                    Kinds = kindsOf,
                    Expected = expected,
                    Confidence = haunt.Confidence,
                    AwakeSeconds = haunt.AwakeSeconds,
                    Samples = haunt.Samples,
                    ObservedRange = haunt.ObservedRange,
                    LastAwake = haunt.LastAwake
                }
            );
        }

        List<BotZoneLiveIn> living = [];

        BotHaunts.Living(
            map,
            now,
            (c, seen) => living.Add(
                new BotZoneLiveIn(
                    c.X,
                    c.Y,
                    IndexOf(seen.Kind),
                    seen.Haunt?.Index ?? -1,
                    seen.Awake,
                    seen.Power,
                    BotHaunts.AttacksFirst(c.FightMode),
                    c.AI == AIType.AI_Mage,
                    c.RangePerception,

                    c.CanSwim && (c.CantWalk || Wet(map, c.X, c.Y)),
                    c.CantWalk && !c.CanSwim
                )
            )
        );

        List<int> cells = [];
        List<float> weightsOf = [];

        BotZoneRaster.Collect(now, (float)ObservedLeast, cells, weightsOf);

        var window = (long)Math.Max(1, DeathsWindowHours) * 3600000L;
        var firstKept = 0;

        while (firstKept < _deaths.Count && now - _deaths[firstKept].Tick > window)
        {
            firstKept++;
        }

        if (firstKept > 0)
        {
            _deaths.RemoveRange(0, firstKept);
        }

        var deaths = new (int X, int Y)[_deaths.Count];

        for (var i = 0; i < _deaths.Count; i++)
        {
            deaths[i] = (_deaths[i].X, _deaths[i].Y);
        }

        List<BotZonePlaceIn> towns = [];

        foreach (var town in BotTowns.All)
        {
            if (town?.Name != null)
            {
                towns.Add(new BotZonePlaceIn(town.Name, town.Square.X, town.Square.Y, town.Square.X, town.Square.Y));
            }
        }

        List<BotZonePlaceIn> deeps = [];

        foreach (var deep in BotDungeon.All)
        {
            deeps.Add(new BotZonePlaceIn(deep.Name, deep.Bounds.Start.X, deep.Bounds.Start.Y, deep.Bounds.End.X, deep.Bounds.End.Y));
        }

        var width = BotZoneRaster.Width;
        var height = BotZoneRaster.Height;

        return new BotZoneInput
        {
            Map = map,
            Width = width,
            Height = height,
            Settings = Settings(),
            Kinds = kinds.ToArray(),
            Haunts = haunts.ToArray(),
            Live = living.ToArray(),
            Observed = cells.ToArray(),
            ObservedWeight = weightsOf.ToArray(),
            Deaths = deaths,
            Towns = towns.ToArray(),
            Deeps = deeps.ToArray(),
            RefBotPower = Math.Max(ReferenceFloor, Reference),
            Previous = _state,
            Buffer = BackBuffer(width * height),
            At = DateTime.Now,
            OffLoop = OffLoopNow,
            Generation = _generation
        };
    }

    private static bool Wet(Map map, int x, int y)
    {
        var tiles = map.Tiles;

        foreach (var tile in tiles.GetStaticTiles(x, y))
        {
            if (BotSeaChart.WaterStatic(tile.ID))
            {
                return true;
            }
        }

        return BotSeaChart.WaterLand(tiles.GetLandTile(x, y).ID);
    }

    private static ushort[] BackBuffer(int length)
    {
        var current = _state?.Cells;

        for (var b = 0; b < _buffers.Length; b++)
        {
            if (_buffers[b] == null || _buffers[b].Length != length)
            {
                _buffers[b] = new ushort[length];
            }

            if (!ReferenceEquals(_buffers[b], current))
            {
                return _buffers[b];
            }
        }

        return new ushort[length];
    }

    private static BotZoneSettings Settings() =>
        new()
        {
            DeclaredMost = DeclaredMost,
            LiveRadius = LiveRadius,
            LooseRadius = LooseRadius,
            PassiveRadius = PassiveRadius,
            WaterRadius = WaterRadius,
            WaterGroup = WaterGroup,
            ObservedMargin = Math.Max(0, ObservedMargin),
            ObservedWins = ObservedWins,
            PerceptionMost = PerceptionMost,
            WaterMeleeReach = WaterMeleeReach,
            WaterCasterReach = WaterCasterReach,
            RootedReach = RootedReach,
            ConvergeTiles = ConvergeTiles,
            Secondary = BotThreat.Secondary,
            DeadlyVsBot = DeadlyVsBot,
            EmptyWeight = EmptyWeight,
            DeathsPromote = DeathsPromote,
            DeathsWindowHours = DeathsWindowHours,
            ConfidentSamples = ConfidentSamples,
            ClearLeastLive = ClearLeastLive,
            MostPoints = MostPoints,
            ClearMostPoints = ClearMostPoints,
            SeaMostPoints = SeaMostPoints,
            MostRings = MostRings,
            MostKinds = MostKinds,
            SimplifyTiles = SimplifyTiles,
            SmoothPasses = SmoothPasses
        };

    private static double Median()
    {
        List<double> powers = [];
        var bots = BotPopulation.Bots;

        for (var i = 0; i < bots.Count; i++)
        {
            var bot = bots[i];

            if (bot is { Deleted: false, Alive: true } && bot.Map != null && bot.Map != Map.Internal)
            {
                powers.Add(BotThreat.Power(bot));
            }
        }

        if (powers.Count == 0)
        {
            return Math.Max(ReferenceFloor, Reference);
        }

        powers.Sort();

        return Math.Max(ReferenceFloor, powers[powers.Count / 2]);
    }

    private static string Empty() =>
        "{\"at\":null,\"map\":null,\"cell\":4,\"refBotPower\":0,\"counts\":{\"zones\":0,\"clear\":0,\"edge\":0,\"hostile\":0,\"deadly\":0,\"spawners\":0,\"creatures\":0},\"tour\":{\"running\":false,\"visited\":0,\"total\":0,\"walkers\":[]},\"zones\":[]}";

    private static void WriteJson()
    {
        var began = Stopwatch.GetTimestamp();
        var state = _state;
        var buffer = new ArrayBufferWriter<byte>((state?.ZonesUtf8.Length ?? 0) + 8192);

        using (var w = new Utf8JsonWriter(buffer))
        {
            w.WriteStartObject();

            if (state == null)
            {
                w.WriteNull("at");
            }
            else
            {
                w.WriteString("at", state.At.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture));
            }

            w.WriteString("map", Map?.Name);
            w.WriteNumber("cell", BotZoneState.Cell);
            w.WriteNumber("refBotPower", Math.Round(state?.RefBotPower ?? 0));

            w.WriteStartObject("counts");
            w.WriteNumber("zones", state?.Zones.Length ?? 0);
            w.WriteNumber("clear", state?.Clear ?? 0);
            w.WriteNumber("edge", state?.Edge ?? 0);
            w.WriteNumber("hostile", state?.Hostile ?? 0);
            w.WriteNumber("deadly", state?.Deadly ?? 0);
            w.WriteNumber("water", state?.WaterZones ?? 0);
            w.WriteNumber("spawners", BotHaunts.Spawners);
            w.WriteNumber("creatures", BotZoneScan.Creatures);
            w.WriteNumber("observedCells", BotZoneRaster.Touched);
            w.WriteEndObject();

            w.WritePropertyName("tour");
            BotZoneTour.Write(w);

            w.WritePropertyName("zones");

            if (state == null)
            {
                w.WriteStartArray();
                w.WriteEndArray();
            }
            else
            {
                w.WriteRawValue(state.ZonesUtf8, true);
            }

            w.WriteEndObject();
        }

        var json = Encoding.UTF8.GetString(buffer.WrittenSpan);

        _version++;
        _page = new BotZonePage(json, $"\"z{_version}\"");

        JsonWrites++;
        JsonBytes = buffer.WrittenCount;
        JsonMsTotal += Stopwatch.GetElapsedTime(began).TotalMilliseconds;
    }

    public static string Describe()
    {
        var state = _state;
        var zones = state == null
            ? "no zones built yet"
            : $"{state.Zones.Length} zones ({state.Clear} clear, {state.Hostile} hostile, {state.Deadly} deadly; {state.WaterZones} of them at sea; {Emptied(state)} hostile ones empty now), built {Builds} times ({BuildsOffLoop} off the loop, {BuildFailures} failed), {state.Kept} kept their number and {state.Born} new in the last; the median bot {state.RefBotPower:F0}";

        var deaths = state == null
            ? ""
            : $"; deaths in {DeathsWindowHours}h by where they fell: {state.DeathsDeadly} deadly, {state.DeathsHostile} hostile, {state.DeathsEdge} in a fringe, {state.DeathsClear} on clear ground ({DeathsHeard} heard since the start)";

        var build = Builds == 0
            ? "no build yet"
            : $"gather {GatherMs:F1}ms on the loop (last), build {BuildMsTotal / Builds:F1}ms average and {BuildMsWorst:F1}ms worst ({state?.Phases}, the last); {InlineMsTotal:F0}ms of builds ran inline on the loop";

        return $"{zones}{deaths}. {BotHaunts.Describe()}. The sweep: {BotZoneScan.Describe()}. Observed: {BotZoneRaster.Touched} cells hold awake sightings ({BotZoneRaster.Recorded} recorded), {state?.ObservedCells ?? 0} of them patrol. "
            + $"The tour: {BotZoneTour.Describe()}. The loop: {Ticks} beats, {(Ticks == 0 ? 0 : SpentMs / Ticks):F3}ms a beat, {WorstTickMs:F1}ms worst, {SpentMs:F0}ms in all (the tour {TourSpentMs:F0}ms); {build}. "
            + $"JSON {JsonBytes / 1024}KB, written {JsonWrites} times in {(JsonWrites == 0 ? 0 : JsonMsTotal / JsonWrites):F2}ms each. {BotZoneMemory.Describe()}";
    }

    private static int Emptied(BotZoneState state)
    {
        var n = 0;

        for (var i = 0; i < state.Zones.Length; i++)
        {
            if (state.Zones[i].Empty)
            {
                n++;
            }
        }

        return n;
    }

    public static string Tell()
    {
        var state = _state;

        if (state == null)
        {
            return $"the zones are not built yet: {BotHaunts.Describe()}; {BotZoneScan.Describe()}.";
        }

        List<BotZone> worst = [];

        for (var i = 0; i < state.Zones.Length; i++)
        {
            if (state.Zones[i].Level >= BotZoneLevel.Hostile)
            {
                worst.Add(state.Zones[i]);
            }
        }

        worst.Sort((a, b) => b.VsBot.CompareTo(a.VsBot));

        var lines = new List<string>();

        for (var i = 0; i < worst.Count && i < 10; i++)
        {
            var z = worst[i];

            lines.Add($"#{z.Id} {Word(z.Level)} ×{z.VsBot:F1} at ({z.CenterX}, {z.CenterY}) — {z.Label}: {z.Aggressive} of {z.Live} alive attack, confidence {z.Confidence:F2}, {z.Deaths} deaths");
        }

        return $"{state.Zones.Length} zones: {state.Clear} clear, {state.Hostile} hostile, {state.Deadly} deadly ({state.WaterZones} at sea); the median bot {state.RefBotPower:F0}. The strongest: {string.Join("; ", lines)}. The tour: {BotZoneTour.Describe()}.";
    }

    public static string TellAt(int x, int y)
    {
        var map = Map;
        var state = _state;

        if (state == null || map == null)
        {
            return "the zones are not built yet.";
        }

        var level = LevelAt(map, x, y);
        var zone = ZoneAt(map, x, y);
        var seen = BotZoneRaster.WeightAt(x, y, Core.TickCount);

        if (zone == null)
        {
            return $"({x}, {y}) is clear: no zone's patrol or fringe covers it; awake sightings here weigh {seen:F2}.";
        }

        var kinds = new List<string>();

        for (var i = 0; i < zone.Kinds.Length; i++)
        {
            var k = zone.Kinds[i];

            kinds.Add($"{k.Kind} {k.Live}/{k.Max} ({k.Power:F0}{(k.Aggressive ? ", attacks" : "")}{(k.Caster ? ", casts" : "")})");
        }

        var last = zone.LastSeen == DateTime.MinValue ? "never" : zone.LastSeen.ToLocalTime().ToString("HH:mm:ss");

        return $"({x}, {y}) is {Word(level)}: {(level == BotZoneLevel.Edge ? "in the aggro fringe of" : "inside")} zone #{zone.Id}, {Word(zone.Level)} — {zone.Label}. {zone.Why}. "
            + $"{zone.Live} alive of {zone.Max} kept: {string.Join(", ", kinds)}{(zone.OtherKinds > 0 ? $" and {zone.OtherKinds} more kinds" : "")}. "
            + $"Threat {zone.Threat:F0} (worst {zone.Worst:F0}, all {zone.PowerTotal:F0}) = ×{zone.VsBot:F2} the median bot's {state.RefBotPower:F0}. "
            + $"Patrol declared {zone.DeclaredRange} tiles, observed {zone.ObservedRange}, aggro reach {zone.AggroRange}; {zone.CoreCells} cells of patrol and {zone.AggroCells} of reach. "
            + $"Confidence {zone.Confidence:F2}: {zone.Samples} awake sightings over {zone.AwakeSeconds:F0}s, last {last}; this cell's sightings weigh {seen:F2}. {zone.Deaths} bots killed here in {DeathsWindowHours}h; {zone.Haunts} spawners feed it; the tour stands at ({zone.Stand.X}, {zone.Stand.Y}).";
    }

    public static void Forget()
    {
        Stop();
        BotZoneTour.Forget(Core.TickCount);
        BotZoneMemory.Save();
        BotHaunts.Forget();
        BotZoneRaster.Forget();
        BotZoneScan.Forget();
        _state = null;
        _seen = null;
        _page = new BotZonePage(Empty(), "\"z0\"");
        _buffers[0] = null;
        _buffers[1] = null;
        _deaths.Clear();
        Map = null;
        Builds = BuildsOffLoop = BuildFailures = 0;
        GatherMs = GatherMsTotal = BuildMsTotal = BuildMsWorst = InlineMsTotal = 0.0;
        Ticks = 0;
        SpentMs = WorstTickMs = TourSpentMs = 0.0;
        JsonWrites = 0;
        JsonMsTotal = 0.0;
        JsonBytes = 0;
        DeathsHeard = 0;

        _generation++;
    }

    private sealed class ZoneTimer : Timer
    {
        public ZoneTimer(TimeSpan interval) : base(interval, interval)
        {
        }

        protected override void OnTick()
        {
            try
            {
                Tick();
            }
            catch (Exception e)
            {
                logger.Error(e, "Zones: the beat threw");
            }
        }
    }
}
