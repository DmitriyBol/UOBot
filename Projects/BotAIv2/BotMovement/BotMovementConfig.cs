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
    /// <summary>Milliseconds one search may cost before it returns what it has.</summary>
    public double? CeilingMs { get; set; }

    /// <summary>Milliseconds a search still gets when the population's allowance for the second is spent.</summary>
    public double? FloorMs { get; set; }

    /// <summary>Milliseconds the whole population may spend searching per second.</summary>
    public double? WindowMs { get; set; }

    /// <summary>How far off the straight line a short search may look.</summary>
    public int? MinMargin { get; set; }

    /// <summary>How far off it a long one may — what it takes to round a lake rather than a building.</summary>
    public int? MaxMargin { get; set; }

    /// <summary>How long a plan is trusted before being drawn again.</summary>
    public int? PlanStaleMs { get; set; }

    /// <summary>How many fruitless attempts at stepping before the journey is given up.</summary>
    public int? StallAttempts { get; set; }

    /// <summary>How many plans in a row with nowhere to walk before the destination is given up.</summary>
    public int? MaxEmptyPlans { get; set; }

    /// <summary>How many plans in a row may fail to bring the errand any closer before it is given up.</summary>
    public int? MaxPlansWithoutCloser { get; set; }

    /// <summary>How long ground that nearly killed a bot stays out of its plans.</summary>
    public int? DangerAvoidMs { get; set; }

    /// <summary>The largest pocket, in tiles, worth proving from the destination's side.</summary>
    public int? EnclosureCells { get; set; }

    /// <summary>Milliseconds one look at the far side of a journey may cost.</summary>
    public double? EnclosureCeilingMs { get; set; }

    /// <summary>The shortest gap between two such looks, across the whole population.</summary>
    public int? EnclosureGapMs { get; set; }

    /// <summary>Plans in a row that get no closer before the far side is asked about.</summary>
    public int? PlansBeforeAskingTheFarSide { get; set; }
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
        BotWalk.PlansBeforeAskingTheFarSide =
            settings.PlansBeforeAskingTheFarSide ?? BotWalk.PlansBeforeAskingTheFarSide;
    }
}
