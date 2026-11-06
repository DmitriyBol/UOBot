using System.Collections.Generic;
using System.IO;
using Server.Json;
using Server.Logging;

namespace Server.BotAI.V2;

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

    public bool? Inns { get; set; }

    public int? InnReach { get; set; }

    public int? InnPricePerHour { get; set; }

    public double? InnBuffShare { get; set; }

    public BotGrowthSettings Growth { get; set; }

    public BotUnloadSettings Unload { get; set; }
}

public sealed class BotUnloadSettings
{

    public int? StuckFails { get; set; }

    public int? StuckWindowMs { get; set; }

    public int? JoinMs { get; set; }

    public bool? Releases { get; set; }
}

public sealed class BotGrowthSettings
{

    public bool? Running { get; set; }

    public int? EveryMinutes { get; set; }

    public int? By { get; set; }

    public int? Most { get; set; }

    public int? FillTo { get; set; }

    public int? BurstBy { get; set; }

    public int? LeastToWant { get; set; }
}

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
        BotInns.Running = settings.Inns ?? BotInns.Running;
        BotInns.Reach = settings.InnReach ?? BotInns.Reach;
        BotInns.PricePerHour = settings.InnPricePerHour ?? BotInns.PricePerHour;
        BotInns.BuffShare = settings.InnBuffShare ?? BotInns.BuffShare;
        BotPopulation.ReviveMs = settings.ReviveMs ?? BotPopulation.ReviveMs;

        BotBeat.IntervalMs = settings.BeatMs ?? BotBeat.IntervalMs;

        BotMobile.Runs = settings.Run ?? BotMobile.Runs;
        BotMobile.NoticeRange = settings.NoticeRange ?? BotMobile.NoticeRange;
        BotMobile.FitFraction = settings.FitFraction ?? BotMobile.FitFraction;

        {
            var c = settings.Growth;

            if (c != null)
            {
                BotGrowth.Running = c.Running ?? BotGrowth.Running;
                BotGrowth.EveryMinutes = c.EveryMinutes ?? BotGrowth.EveryMinutes;
                BotGrowth.By = c.By ?? BotGrowth.By;
                BotGrowth.Most = c.Most ?? BotGrowth.Most;
                BotGrowth.FillTo = c.FillTo ?? BotGrowth.FillTo;
                BotGrowth.BurstBy = c.BurstBy ?? BotGrowth.BurstBy;
                BotGrowth.LeastToWant = c.LeastToWant ?? BotGrowth.LeastToWant;
            }
        }

        {
            var c = settings.Unload;

            if (c != null)
            {
                BotUnload.StuckFails = c.StuckFails ?? BotUnload.StuckFails;
                BotUnload.StuckWindowMs = c.StuckWindowMs ?? BotUnload.StuckWindowMs;
                BotUnload.JoinMs = c.JoinMs ?? BotUnload.JoinMs;
                BotUnload.Releases = c.Releases ?? BotUnload.Releases;
            }

            logger.Information(
                "The porter: {Fails} stand-still passes no lighter in {Window} minutes set a bot aside until its weight changes; piles lying apart joined every {Join}s for a heavy bot; kept things let go when they alone are over the ceiling: {Releases}",
                BotUnload.StuckFails,
                BotUnload.StuckWindowMs / 60000.0,
                BotUnload.JoinMs / 1000,
                BotUnload.Releases
            );
        }

        Mix = Offices(settings.Classes is { Count: > 0 } ? settings.Classes : StarterMix());
    }

    private static Dictionary<string, int> Offices(Dictionary<string, int> classes)
    {
        Dictionary<string, int> held = null;

        foreach (var (name, many) in classes)
        {
            if (many <= 1 || BotClasses.Find(name) is not { Office: true } klass)
            {
                continue;
            }

            logger.Warning(
                "{Class} is an office, held by one bot at any population; the {Many} in {Path} is read as 1",
                klass.Name,
                many,
                ConfigPath
            );

            held ??= new Dictionary<string, int>(classes, classes.Comparer);
            held[name] = 1;
        }

        return held ?? classes;
    }
}
