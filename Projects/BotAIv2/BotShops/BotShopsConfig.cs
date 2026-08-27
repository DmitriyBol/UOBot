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
    /// <summary>How far around a bot one sweep looks for shopkeepers.</summary>
    public int? Reach { get; set; }

    /// <summary>How near a shopkeeper a bot must stand to trade.</summary>
    public int? CounterReach { get; set; }

    /// <summary>How many shopkeepers the population may remember.</summary>
    public int? MaxShops { get; set; }

    /// <summary>How far below its birth allowance a supply falls before the bot goes shopping.</summary>
    public double? Short { get; set; }

    /// <summary>What a trip to the shops is reckoned at per minute before experience corrects it.</summary>
    public double? Expects { get; set; }

    /// <summary>How long the errand itself is expected to take.</summary>
    public double? WorkMinutes { get; set; }

    /// <summary>How long a stall sits unsold before its owner takes the goods to a shopkeeper.</summary>
    public int? PeddleAfterMs { get; set; }
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
    }
}
