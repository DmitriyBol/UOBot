using System.IO;
using Server.Json;
using Server.Logging;

namespace Server.BotAI.V2;

public sealed class BotCraftSettings
{

    public double? Margin { get; set; }

    public int? Bolt { get; set; }

    public int? SwingMs { get; set; }

    public int? GoldPerPiece { get; set; }

    public double? Expects { get; set; }

    public double? WorkMinutes { get; set; }

    public double? ForgePrior { get; set; }

    public double? ForgeMinutes { get; set; }

    public int? AnvilTries { get; set; }

    public double? TinkerPrior { get; set; }

    public double? TinkerMinutes { get; set; }

    public int? TinkerBatch { get; set; }

    public int? TinkerLeastOnMarket { get; set; }

    public int? TinkerLeastMetal { get; set; }
}

public static class BotCraftConfig
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotCraftConfig));

    private const string ConfigPath = "Configuration/bot-craft.json";

    public static void Load()
    {
        var path = Path.Combine(Core.BaseDirectory, ConfigPath);
        var settings = JsonConfig.Deserialize<BotCraftSettings>(path);

        if (settings == null)
        {
            JsonConfig.Serialize(path, new BotCraftSettings());

            logger.Information(
                "Wrote a starter craft file to {Path}; every number stays as the code has it",
                ConfigPath
            );

            return;
        }

        BotThread.Margin = settings.Margin ?? BotThread.Margin;

        BotSew.Bolt = settings.Bolt ?? BotSew.Bolt;
        BotSew.SwingMs = settings.SwingMs ?? BotSew.SwingMs;
        BotSew.GoldPerPiece = settings.GoldPerPiece ?? BotSew.GoldPerPiece;
        BotSew.Prior = settings.Expects ?? BotSew.Prior;
        BotSew.WorkMinutes = settings.WorkMinutes ?? BotSew.WorkMinutes;

        BotForge.Prior = settings.ForgePrior ?? BotForge.Prior;
        BotForge.WorkMinutes = settings.ForgeMinutes ?? BotForge.WorkMinutes;

        BotAnvil.Tries = settings.AnvilTries ?? BotAnvil.Tries;

        BotTinker.Prior = settings.TinkerPrior ?? BotTinker.Prior;
        BotTinker.WorkMinutes = settings.TinkerMinutes ?? BotTinker.WorkMinutes;
        BotTinker.Batch = settings.TinkerBatch ?? BotTinker.Batch;
        BotTinkerer.LeastOnMarket = settings.TinkerLeastOnMarket ?? BotTinkerer.LeastOnMarket;
        BotTinkerer.LeastMetal = settings.TinkerLeastMetal ?? BotTinkerer.LeastMetal;
    }
}
