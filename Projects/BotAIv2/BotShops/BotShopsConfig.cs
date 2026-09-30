using System.IO;
using Server.Json;
using Server.Logging;

namespace Server.BotAI.V2;

/// <summary>
/// What <c>Configuration/bot-shops.json</c> is allowed to say. Everything optional.
///
/// No prices here either: what a shopkeeper charges is the shard's own business, scalars and all, and this
/// file only says how far a bot will walk and how empty a supply has to get.
/// </summary>
public sealed class BotShopsSettings
{
    public int? Reach { get; set; }

    public int? CounterReach { get; set; }

    public int? MaxShops { get; set; }

    public double? Short { get; set; }

    public double? Expects { get; set; }

    public double? WorkMinutes { get; set; }

    public int? PeddleAfterMs { get; set; }

    public bool? CapitalRunning { get; set; }

    public string Capital { get; set; }

    public double? CapitalPremium { get; set; }

    public int? CapitalReach { get; set; }
}

/// <summary>Reads the shops file and moves the numbers it names.</summary>
public static class BotShopsConfig
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotShopsConfig));

    private const string ConfigPath = "Configuration/bot-shops.json";

    public static void Load()
    {
        var path = Path.Combine(Core.BaseDirectory, ConfigPath);
        var settings = JsonConfig.Deserialize<BotShopsSettings>(path);

        if (settings == null)
        {
            JsonConfig.Serialize(path, new BotShopsSettings());

            logger.Information(
                "Wrote a starter shops file to {Path}; every number stays as the code has it",
                ConfigPath
            );

            return;
        }

        BotShops.Reach = settings.Reach ?? BotShops.Reach;
        BotShops.CounterReach = settings.CounterReach ?? BotShops.CounterReach;
        BotShops.MaxShops = settings.MaxShops ?? BotShops.MaxShops;

        BotShopper.Short = settings.Short ?? BotShopper.Short;

        BotRestock.Prior = settings.Expects ?? BotRestock.Prior;
        BotPeddler.IgnoredMs = settings.PeddleAfterMs ?? BotPeddler.IgnoredMs;
        BotRestock.WorkMinutes = settings.WorkMinutes ?? BotRestock.WorkMinutes;

        BotCapital.Running = settings.CapitalRunning ?? BotCapital.Running;
        BotCapital.Name = string.IsNullOrWhiteSpace(settings.Capital) ? BotCapital.Name : settings.Capital;
        BotCapital.Premium = settings.CapitalPremium ?? BotCapital.Premium;
        BotCapital.Reach = settings.CapitalReach ?? BotCapital.Reach;
    }
}
