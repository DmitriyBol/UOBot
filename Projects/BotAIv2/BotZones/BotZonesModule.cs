using System;
using System.IO;
using Server.Json;
using Server.Logging;

namespace Server.BotAI.V2;

/// <summary>
/// What <c>Configuration/bot-zones.json</c> may say. Everything optional; an empty file keeps the code's numbers. PascalCase:
/// a key in lower case is silently a default, and the boot line is the proof of what took.
/// </summary>
public sealed class BotZonesSettings
{
    public bool? Enabled { get; set; }

    public int? TickMs { get; set; }

    public int? RefreshMs { get; set; }

    public int? JsonEveryMs { get; set; }

    public double? ScanBudgetMs { get; set; }

    public int? ScanWindowSectors { get; set; }

    public int? ScanMostWindows { get; set; }

    public int? DeclaredMost { get; set; }

    public int? LiveRadius { get; set; }

    public int? LooseRadius { get; set; }

    public int? PassiveRadius { get; set; }

    public int? WaterRadius { get; set; }

    public int? WaterGroup { get; set; }

    public int? ObservedMargin { get; set; }

    public double? ObservedLeast { get; set; }

    public double? ObservedWins { get; set; }

    public int? ConfidentSeconds { get; set; }

    public int? ConfidentSamples { get; set; }

    public int? HalfLifeMinutes { get; set; }

    public int? PerceptionMost { get; set; }

    public int? WaterMeleeReach { get; set; }

    public int? WaterCasterReach { get; set; }

    public int? RootedReach { get; set; }

    public int? ConvergeTiles { get; set; }

    public double? DeadlyVsBot { get; set; }

    public double? EmptyWeight { get; set; }

    public int? DeathsPromote { get; set; }

    public int? DeathsWindowHours { get; set; }

    public int? ClearLeastLive { get; set; }

    public int? MostPoints { get; set; }

    public int? ClearMostPoints { get; set; }

    public int? SeaMostPoints { get; set; }

    public int? MostRings { get; set; }

    public int? MostKinds { get; set; }

    public double? SimplifyTiles { get; set; }

    public int? SmoothPasses { get; set; }

    public bool? OffLoop { get; set; }

    public int? OffLoopCores { get; set; }

    public int? InlineRefreshMs { get; set; }

    public bool? Tour { get; set; }

    public int? TourHoldMs { get; set; }

    public int? TourGraceMs { get; set; }

    public int? TourReach { get; set; }

    public int? TourMostSectors { get; set; }

    public int? TourMostWalkers { get; set; }

    public int? TourFocusMs { get; set; }

    public bool? TourWater { get; set; }

    public bool? Memory { get; set; }

    public int? MemorySaveMs { get; set; }
}

/// <summary>
/// The danger and safety zones as a module: the numbers read, the beat started, and one <c>Zones:</c> line every five minutes
/// with every counter the system keeps.
///
/// <para>
/// <see cref="BotPhase.World"/>, after <c>Population</c>: the zones are drawn on the population's home map, and the yardstick
/// of a zone's strength is the population's median bot.
/// </para>
/// </summary>
public sealed class BotZonesModule : BotModule
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotZonesModule));

    private const string ConfigPath = "Configuration/bot-zones.json";

    public override string Name => "Zones";

    public override BotPhase Phase => BotPhase.World;

    public override string[] Requires => ["Population"];

    public static int SayEveryMs { get; set; } = 300000;

    private static Timer _say;

    public override void Start()
    {
        Load();

        if (!BotZones.Running)
        {
            logger.Information("Zones: switched off in {Path}; every query answers clear", ConfigPath);

            return;
        }

        BotZones.Start(BotPopulation.Home ?? Map.Felucca);

        logger.Information(
            "Zones: kept on {Map} in cells of {Cell} tiles. Declared discs up to {Declared} tiles until a haunt's confidence reaches {Wins:F2} ({Seconds}s awake, {Samples} sightings); deadly at ×{Deadly:F1} the median bot; {Promote} deaths in {Hours}h make a hostile zone deadly. The sweep spends {Budget:F1}ms a beat every {Tick}ms; a build every {Refresh}s, {Where}. The tour is {Tour}: {Walkers} walkers, {Hold}s a zone",
            BotZones.Map,
            BotZoneState.Cell,
            BotZones.DeclaredMost,
            BotZones.ObservedWins,
            BotZones.ConfidentSeconds,
            BotZones.ConfidentSamples,
            BotZones.DeadlyVsBot,
            BotZones.DeathsPromote,
            BotZones.DeathsWindowHours,
            BotZoneScan.BudgetMs,
            BotZones.TickMs,
            BotZones.RefreshMs / 1000,
            BotZones.OffLoopNow
                ? $"off the loop ({Environment.ProcessorCount} cores)"
                : $"on the loop every {BotZones.InlineRefreshMs / 1000}s at most ({Environment.ProcessorCount} cores, {BotZones.OffLoopCores} wanted to go off it)",
            BotZoneTour.Running ? "on" : "off",
            BotZoneTour.MostWalkers,
            BotZoneTour.HoldMs / 1000
        );

        _say?.Stop();
        _say = new SayTimer(TimeSpan.FromMilliseconds(SayEveryMs));
        _say.Start();
    }

    private static void Load()
    {
        var path = Path.Combine(Core.BaseDirectory, ConfigPath);
        var settings = JsonConfig.Deserialize<BotZonesSettings>(path);

        if (settings == null)
        {
            JsonConfig.Serialize(path, new BotZonesSettings());

            logger.Information("Wrote a starter zones file to {Path}; every number stays as the code has it", ConfigPath);

            return;
        }

        BotZones.Running = settings.Enabled ?? BotZones.Running;
        BotZones.TickMs = settings.TickMs ?? BotZones.TickMs;
        BotZones.RefreshMs = settings.RefreshMs ?? BotZones.RefreshMs;
        BotZones.JsonEveryMs = settings.JsonEveryMs ?? BotZones.JsonEveryMs;
        BotZoneScan.BudgetMs = settings.ScanBudgetMs ?? BotZoneScan.BudgetMs;
        BotZoneScan.WindowSectors = settings.ScanWindowSectors ?? BotZoneScan.WindowSectors;
        BotZoneScan.MostWindows = settings.ScanMostWindows ?? BotZoneScan.MostWindows;
        BotZones.DeclaredMost = settings.DeclaredMost ?? BotZones.DeclaredMost;
        BotZones.SeaMostPoints = settings.SeaMostPoints ?? BotZones.SeaMostPoints;
        BotZones.LiveRadius = settings.LiveRadius ?? BotZones.LiveRadius;
        BotZones.LooseRadius = settings.LooseRadius ?? BotZones.LooseRadius;
        BotZones.PassiveRadius = settings.PassiveRadius ?? BotZones.PassiveRadius;
        BotZones.WaterRadius = settings.WaterRadius ?? BotZones.WaterRadius;
        BotZones.WaterGroup = settings.WaterGroup ?? BotZones.WaterGroup;
        BotZones.ObservedMargin = settings.ObservedMargin ?? BotZones.ObservedMargin;
        BotZones.ObservedLeast = settings.ObservedLeast ?? BotZones.ObservedLeast;
        BotZones.ObservedWins = settings.ObservedWins ?? BotZones.ObservedWins;
        BotZones.ConfidentSeconds = settings.ConfidentSeconds ?? BotZones.ConfidentSeconds;
        BotZones.ConfidentSamples = settings.ConfidentSamples ?? BotZones.ConfidentSamples;
        BotZoneRaster.HalfLifeMinutes = settings.HalfLifeMinutes ?? BotZoneRaster.HalfLifeMinutes;
        BotZones.PerceptionMost = settings.PerceptionMost ?? BotZones.PerceptionMost;
        BotZones.WaterMeleeReach = settings.WaterMeleeReach ?? BotZones.WaterMeleeReach;
        BotZones.WaterCasterReach = settings.WaterCasterReach ?? BotZones.WaterCasterReach;
        BotZones.RootedReach = settings.RootedReach ?? BotZones.RootedReach;
        BotZones.ConvergeTiles = settings.ConvergeTiles ?? BotZones.ConvergeTiles;
        BotZones.DeadlyVsBot = settings.DeadlyVsBot ?? BotZones.DeadlyVsBot;
        BotZones.EmptyWeight = settings.EmptyWeight ?? BotZones.EmptyWeight;
        BotZones.DeathsPromote = settings.DeathsPromote ?? BotZones.DeathsPromote;
        BotZones.DeathsWindowHours = settings.DeathsWindowHours ?? BotZones.DeathsWindowHours;
        BotZones.ClearLeastLive = settings.ClearLeastLive ?? BotZones.ClearLeastLive;
        BotZones.MostPoints = settings.MostPoints ?? BotZones.MostPoints;
        BotZones.ClearMostPoints = settings.ClearMostPoints ?? BotZones.ClearMostPoints;
        BotZones.MostRings = settings.MostRings ?? BotZones.MostRings;
        BotZones.MostKinds = settings.MostKinds ?? BotZones.MostKinds;
        BotZones.SimplifyTiles = settings.SimplifyTiles ?? BotZones.SimplifyTiles;
        BotZones.SmoothPasses = settings.SmoothPasses ?? BotZones.SmoothPasses;
        BotZones.OffLoop = settings.OffLoop ?? BotZones.OffLoop;
        BotZones.OffLoopCores = settings.OffLoopCores ?? BotZones.OffLoopCores;
        BotZones.InlineRefreshMs = settings.InlineRefreshMs ?? BotZones.InlineRefreshMs;
        BotZoneTour.Running = settings.Tour ?? BotZoneTour.Running;
        BotZoneTour.HoldMs = settings.TourHoldMs ?? BotZoneTour.HoldMs;
        BotZoneTour.GraceMs = settings.TourGraceMs ?? BotZoneTour.GraceMs;
        BotZoneTour.Reach = settings.TourReach ?? BotZoneTour.Reach;
        BotZoneTour.MostSectors = settings.TourMostSectors ?? BotZoneTour.MostSectors;
        BotZoneTour.MostWalkers = settings.TourMostWalkers ?? BotZoneTour.MostWalkers;
        BotZoneTour.FocusMs = settings.TourFocusMs ?? BotZoneTour.FocusMs;
        BotZoneTour.Water = settings.TourWater ?? BotZoneTour.Water;
        BotZoneMemory.Running = settings.Memory ?? BotZoneMemory.Running;
        BotZoneMemory.SaveEveryMs = settings.MemorySaveMs ?? BotZoneMemory.SaveEveryMs;

        BotZoneTour.HoldMs = Math.Max(60000, BotZoneTour.HoldMs);
    }

    private sealed class SayTimer : Timer
    {
        public SayTimer(TimeSpan interval) : base(interval, interval)
        {
        }

        protected override void OnTick() => logger.Information("Zones: {What}", BotZones.Describe());
    }

    public override void Reset()
    {
        logger.Information("Zones, before the reload: {What}", BotZones.Describe());

        _say?.Stop();
        _say = null;

        BotZones.Forget();
    }
}
