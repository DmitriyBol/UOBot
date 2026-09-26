using System.Collections.Generic;
using System.IO;
using Server.Json;
using Server.Logging;

namespace Server.BotAI.V2;

/// <summary>
/// What <c>Configuration/bot-population.json</c> is allowed to say.
///
/// <para>
/// <b>The one config file in this project that ships with real values rather than empty ones.</b> Everywhere
/// else absent means "keep the number the code chose", which is harmless. Here an absent class mix means no
/// bots exist at all — so the starter file is written with a small working population in it, and the first
/// boot produces a shard with somebody on it rather than a shard with a question.
/// </para>
/// </summary>
public sealed class BotPopulationSettings
{
    public string Map { get; set; }

    public int[] Home { get; set; }

    public Dictionary<string, int> Classes { get; set; }

    public int? Spread { get; set; }

    public int? Purse { get; set; }

    public bool? KeepEarnings { get; set; }

    public int? Roam { get; set; }

    public int? BeatMs { get; set; }

    public int? ReviveMs { get; set; }

    public bool? Run { get; set; }

    public int? NoticeRange { get; set; }

    public double? FitFraction { get; set; }
}

/// <summary>Reads the population file, or writes a working one.</summary>
public static class BotPopulationConfig
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotPopulationConfig));

    private const string ConfigPath = "Configuration/bot-population.json";

    private static Dictionary<string, int> StarterMix() =>
        new()
        {
            ["Gatherer"] = 2,
            ["Crafter"] = 1,
            ["Warrior"] = 1
        };

    public static void Load()
    {
        var path = Path.Combine(Core.BaseDirectory, ConfigPath);
        var settings = JsonConfig.Deserialize<BotPopulationSettings>(path);

        if (settings == null)
        {
            settings = new BotPopulationSettings
            {
                Map = "Felucca",
                Home = [BotPopulation.Where.X, BotPopulation.Where.Y, BotPopulation.Where.Z],
                Classes = StarterMix()
            };

            JsonConfig.Serialize(path, settings);

            logger.Information(
                "Wrote a starter population file to {Path}: four bots in Britain. Edit it to change who exists",
                ConfigPath
            );
        }

        Apply(settings);
    }

    public static IReadOnlyDictionary<string, int> Mix { get; private set; } = StarterMix();

    private static void Apply(BotPopulationSettings settings)
    {
        Map map = null;

        if (!string.IsNullOrWhiteSpace(settings.Map) && !Map.TryParse(settings.Map, null, out map))
        {
            map = null;
        }

        if (map == null || map == Map.Internal)
        {
            if (!string.IsNullOrWhiteSpace(settings.Map))
            {
                logger.Error("There is no facet called {Map}; the population falls back to Felucca", settings.Map);
            }

            map = Map.Felucca;
        }

        BotPopulation.Home = map;

        if (settings.Home is { Length: >= 3 })
        {
            BotPopulation.Where = new Point3D(settings.Home[0], settings.Home[1], settings.Home[2]);
        }

        BotPopulation.Spread = settings.Spread ?? BotPopulation.Spread;
        BotOutfit.Purse = settings.Purse ?? BotOutfit.Purse;
        BotProgress.Savings = settings.KeepEarnings ?? BotProgress.Savings;
        BotPopulation.Roam = settings.Roam ?? BotPopulation.Roam;
        BotPopulation.ReviveMs = settings.ReviveMs ?? BotPopulation.ReviveMs;

        BotBeat.IntervalMs = settings.BeatMs ?? BotBeat.IntervalMs;

        BotMobile.Runs = settings.Run ?? BotMobile.Runs;
        BotMobile.NoticeRange = settings.NoticeRange ?? BotMobile.NoticeRange;
        BotMobile.FitFraction = settings.FitFraction ?? BotMobile.FitFraction;

        Mix = settings.Classes is { Count: > 0 } ? settings.Classes : StarterMix();
    }
}
