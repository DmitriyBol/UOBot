using System.Collections.Generic;
using System.IO;
using Server.Json;
using Server.Logging;

namespace Server.BotAI.V2;

/// <summary>
/// What <c>Configuration/bot-movement.json</c> is allowed to say. Everything optional; absent means the
/// number the code chose.
///
/// Only genuine knobs are here. The rules — a step climbs two units, a person is sixteen tall, a diagonal
/// needs both flanks, a floor is found within eight of the terrain — are <b>not</b> configurable, and that
/// is deliberate. They are not preferences, they are what the engine does, and a config file able to
/// disagree with the engine is a config file able to recreate every stuck bot the first version had.
/// </summary>
public sealed class BotMovementSettings
{
    public double? CeilingMs { get; set; }

    public double? FloorMs { get; set; }

    public double? MsPerTile { get; set; }

    public double? ShortMs { get; set; }

    public double? WindowMs { get; set; }

    public int? MinMargin { get; set; }

    public int? MaxMargin { get; set; }

    public int? PlanStaleMs { get; set; }

    public int? StallAttempts { get; set; }

    public int? MaxEmptyPlans { get; set; }

    public int? MaxPlansWithoutCloser { get; set; }

    public int? DangerAvoidMs { get; set; }

    public int? EnclosureCells { get; set; }

    public double? EnclosureCeilingMs { get; set; }

    public int? EnclosureGapMs { get; set; }

    public int? StrandedCells { get; set; }

    public double? StrandedCeilingMs { get; set; }

    public int? PlansBeforeAskingTheFarSide { get; set; }

    public int? RefusalGrain { get; set; }

    public int? RefusalRestMs { get; set; }

    public int? MostRefusalRestMs { get; set; }

    public int? MostRefusedPlaces { get; set; }

    public bool? Barring { get; set; }

    public BotBarSettings[] Barred { get; set; }

    public bool? DangerByWalker { get; set; }

    public double? DeadlyVsWalker { get; set; }

    public int? BloodBeyond { get; set; }

    public bool? RefuseNodes { get; set; }

    public bool? Roads { get; set; }

    public double? RoadDare { get; set; }

    public int? RoadStep { get; set; }

    public double? ZoneRoadShare { get; set; }

    public bool? RouteJudged { get; set; }

    public bool? RouteAlways { get; set; }

    public int? RoutePerSecond { get; set; }

    public double? RoadStaleDare { get; set; }

    public bool? SearchPrune { get; set; }

    public bool? Sight { get; set; }

    public int? SightEveryMs { get; set; }

    public int? SightTiles { get; set; }

    public double? SightOdds { get; set; }

    public int? SightMargin { get; set; }

    public int? SightStepCost { get; set; }

    public int? SightRouteCost { get; set; }

    public int? SightWaitMs { get; set; }

    public bool? SightStanding { get; set; }
}

/// <summary>One rectangle of ground the population may not choose, as a configuration file writes it.</summary>
public sealed class BotBarSettings
{
    public int? X1 { get; set; }

    public int? Y1 { get; set; }

    public int? X2 { get; set; }

    public int? Y2 { get; set; }

    public string Why { get; set; }
}

/// <summary>Reads the movement file and moves the numbers it names.</summary>
public static class BotMovementConfig
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotMovementConfig));

    private const string ConfigPath = "Configuration/bot-movement.json";

    public static void Load()
    {
        var path = Path.Combine(Core.BaseDirectory, ConfigPath);
        var settings = JsonConfig.Deserialize<BotMovementSettings>(path);

        if (settings == null)
        {
            JsonConfig.Serialize(path, new BotMovementSettings());

            logger.Information(
                "Wrote a starter movement file to {Path}; every number stays as the code has it",
                ConfigPath
            );

            return;
        }

        BotPath.CeilingMs = settings.CeilingMs ?? BotPath.CeilingMs;
        BotPath.FloorMs = settings.FloorMs ?? BotPath.FloorMs;
        BotPath.MsPerTile = settings.MsPerTile ?? BotPath.MsPerTile;
        BotPath.ShortMs = settings.ShortMs ?? BotPath.ShortMs;
        BotPath.WindowMs = settings.WindowMs ?? BotPath.WindowMs;
        BotPath.MinMargin = settings.MinMargin ?? BotPath.MinMargin;
        BotPath.MaxMargin = settings.MaxMargin ?? BotPath.MaxMargin;
        BotJourney.PlanStaleMs = settings.PlanStaleMs ?? BotJourney.PlanStaleMs;
        BotJourney.StallAttempts = settings.StallAttempts ?? BotJourney.StallAttempts;
        BotJourney.MaxEmptyPlans = settings.MaxEmptyPlans ?? BotJourney.MaxEmptyPlans;
        BotJourney.MaxPlansWithoutCloser = settings.MaxPlansWithoutCloser ?? BotJourney.MaxPlansWithoutCloser;
        BotJourney.DangerAvoidMs = settings.DangerAvoidMs ?? BotJourney.DangerAvoidMs;
        BotPath.EnclosureCells = settings.EnclosureCells ?? BotPath.EnclosureCells;
        BotPath.EnclosureCeilingMs = settings.EnclosureCeilingMs ?? BotPath.EnclosureCeilingMs;
        BotPath.EnclosureGapMs = settings.EnclosureGapMs ?? BotPath.EnclosureGapMs;
        BotPath.StrandedCells = settings.StrandedCells ?? BotPath.StrandedCells;
        BotPath.StrandedCeilingMs = settings.StrandedCeilingMs ?? BotPath.StrandedCeilingMs;
        BotWalk.PlansBeforeAskingTheFarSide =
            settings.PlansBeforeAskingTheFarSide ?? BotWalk.PlansBeforeAskingTheFarSide;
        BotRefused.Grain = settings.RefusalGrain ?? BotRefused.Grain;
        BotRefused.RestMs = settings.RefusalRestMs ?? BotRefused.RestMs;
        BotRefused.MostRestMs = settings.MostRefusalRestMs ?? BotRefused.MostRestMs;
        BotRefused.MostPlaces = settings.MostRefusedPlaces ?? BotRefused.MostPlaces;
        BotBarred.Running = settings.Barring ?? BotBarred.Running;

        BotDanger.ByWalker = settings.DangerByWalker ?? BotDanger.ByWalker;
        BotDanger.DeadlyVsWalker = settings.DeadlyVsWalker ?? BotDanger.DeadlyVsWalker;
        BotDanger.BloodBeyond = settings.BloodBeyond ?? BotDanger.BloodBeyond;
        BotDanger.RefusesNodes = settings.RefuseNodes ?? BotDanger.RefusesNodes;
        BotDanger.Roads = settings.Roads ?? BotDanger.Roads;
        BotDanger.RoadDare = settings.RoadDare ?? BotDanger.RoadDare;
        BotDanger.RoadStep = settings.RoadStep ?? BotDanger.RoadStep;
        BotDanger.ZoneRoadShare = settings.ZoneRoadShare ?? BotDanger.ZoneRoadShare;
        BotDanger.RouteJudged = settings.RouteJudged ?? BotDanger.RouteJudged;
        BotDanger.RouteAlways = settings.RouteAlways ?? BotDanger.RouteAlways;
        BotDanger.RoutePerSecond = settings.RoutePerSecond ?? BotDanger.RoutePerSecond;
        BotDanger.RoadStaleDare = settings.RoadStaleDare ?? BotDanger.RoadStaleDare;
        BotReachMap.PrunesSearch = settings.SearchPrune ?? BotReachMap.PrunesSearch;

        BotSight.Running = settings.Sight ?? BotSight.Running;
        BotSight.EveryMs = settings.SightEveryMs ?? BotSight.EveryMs;
        BotSight.SightTiles = settings.SightTiles ?? BotSight.SightTiles;
        BotSight.Odds = settings.SightOdds ?? BotSight.Odds;
        BotSight.Margin = settings.SightMargin ?? BotSight.Margin;
        BotSight.StepCost = settings.SightStepCost ?? BotSight.StepCost;
        BotDanger.SightCost = settings.SightRouteCost ?? BotDanger.SightCost;
        BotSight.WaitMs = settings.SightWaitMs ?? BotSight.WaitMs;
        BotSight.Standing = settings.SightStanding ?? BotSight.Standing;

        if (settings.Barred != null)
        {
            List<BotBarred.Bar> bars = [];

            for (var i = 0; i < settings.Barred.Length; i++)
            {
                var bar = settings.Barred[i];

                if (bar?.X1 == null || bar.Y1 == null || bar.X2 == null || bar.Y2 == null)
                {
                    logger.Warning(
                        "Barred entry {Index} in {Path} is missing one of X1, Y1, X2, Y2 and is ignored",
                        i,
                        ConfigPath
                    );

                    continue;
                }

                bars.Add(new BotBarred.Bar(bar.X1.Value, bar.Y1.Value, bar.X2.Value, bar.Y2.Value, bar.Why ?? "no reason given"));
            }

            BotBarred.Set(bars);
        }
    }
}
