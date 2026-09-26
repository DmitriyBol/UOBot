using System.IO;
using Server.Json;
using Server.Logging;

namespace Server.BotAI.V2;

/// <summary>
/// What <c>Configuration/bot-harvest.json</c> is allowed to say. Everything optional; absent means the
/// number the code chose.
///
/// <para>
/// What is deliberately <b>not</b> here: which tiles are ore, what a vein contains, how much a swing
/// yields, and how many ingots a pile of ore becomes. Those are the engine's, and a configuration file able
/// to disagree with the engine about them is a file able to send a population to dig a beach — which the
/// first version did, all night, because two of its tests said sand was workable and the engine did not
/// agree.
/// </para>
/// </summary>
public sealed class BotHarvestSettings
{
    public int? SweepReach { get; set; }

    public int? SweepStride { get; set; }

    public int? SeamSpacing { get; set; }

    public int? PlaceSpacing { get; set; }

    public int? AnvilReach { get; set; }

    public int? MaxSeams { get; set; }

    public int? SeamAskEveryMs { get; set; }

    public int? MaxPlaces { get; set; }

    public int? MaxSurveys { get; set; }

    public int? LookReach { get; set; }

    public int? FireReach { get; set; }

    public int? WorthSmelting { get; set; }

    public double? Expects { get; set; }

    public double? WorkMinutes { get; set; }

    public int? GoldPerIngot { get; set; }

    public double? FillFraction { get; set; }

    public int? TargetOre { get; set; }

    public int? DryLimit { get; set; }

    public int? CounterReach { get; set; }

    public int? MaxBends { get; set; }

    public int? MaxSpent { get; set; }
}

/// <summary>Reads the harvest file and moves the numbers it names.</summary>
public static class BotHarvestConfig
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotHarvestConfig));

    private const string ConfigPath = "Configuration/bot-harvest.json";

    public static void Load()
    {
        var path = Path.Combine(Core.BaseDirectory, ConfigPath);
        var settings = JsonConfig.Deserialize<BotHarvestSettings>(path);

        if (settings == null)
        {
            JsonConfig.Serialize(path, new BotHarvestSettings());

            logger.Information(
                "Wrote a starter harvest file to {Path}; every number stays as the code has it",
                ConfigPath
            );

            return;
        }

        BotGround.Reach = settings.SweepReach ?? BotGround.Reach;
        BotGround.Stride = settings.SweepStride ?? BotGround.Stride;
        BotGround.SeamSpacing = settings.SeamSpacing ?? BotGround.SeamSpacing;
        BotGround.PlaceSpacing = settings.PlaceSpacing ?? BotGround.PlaceSpacing;
        BotGround.AnvilReach = settings.AnvilReach ?? BotGround.AnvilReach;
        BotGround.MaxSeams = settings.MaxSeams ?? BotGround.MaxSeams;
        BotGround.AskEveryMs = settings.SeamAskEveryMs ?? BotGround.AskEveryMs;
        BotGround.MaxPlaces = settings.MaxPlaces ?? BotGround.MaxPlaces;
        BotGround.MaxSurveys = settings.MaxSurveys ?? BotGround.MaxSurveys;

        BotOre.Reach = settings.LookReach ?? BotOre.Reach;
        BotOre.FireReach = settings.FireReach ?? BotOre.FireReach;
        BotOre.WorthSmelting = settings.WorthSmelting ?? BotOre.WorthSmelting;

        BotDig.Prior = settings.Expects ?? BotDig.Prior;
        BotDig.WorkMinutes = settings.WorkMinutes ?? BotDig.WorkMinutes;
        BotDig.GoldPerIngot = settings.GoldPerIngot ?? BotDig.GoldPerIngot;
        BotDig.FillFraction = settings.FillFraction ?? BotDig.FillFraction;
        BotDig.TargetOre = settings.TargetOre ?? BotDig.TargetOre;
        BotDig.DryLimit = settings.DryLimit ?? BotDig.DryLimit;
        BotDig.CounterReach = settings.CounterReach ?? BotDig.CounterReach;
        BotDig.MaxBends = settings.MaxBends ?? BotDig.MaxBends;
        BotDig.MaxSpent = settings.MaxSpent ?? BotDig.MaxSpent;
    }
}
