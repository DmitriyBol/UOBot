using System.Collections.Generic;
using System.IO;
using Server.Json;
using Server.Logging;

namespace Server.BotAI.V2;

public sealed class BotClassSettings
{

    public Dictionary<string, BotClassOverride> Classes { get; set; } = [];
}

public static class BotClassConfig
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotClassConfig));

    private const string ConfigPath = "Configuration/bot-classes.json";

    public static void Load()
    {
        var path = Path.Combine(Core.BaseDirectory, ConfigPath);

        var settings = JsonConfig.Deserialize<BotClassSettings>(path);

        if (settings == null)
        {
            WriteStarter(path);
            return;
        }

        BotClasses.Override(settings.Classes);
    }

    private static void WriteStarter(string path)
    {
        var settings = new BotClassSettings();
        var classes = BotClasses.All;

        for (var i = 0; i < classes.Count; i++)
        {
            settings.Classes[classes[i].Name] = new BotClassOverride();
        }

        JsonConfig.Serialize(path, settings);

        logger.Information(
            "Wrote a starter class file naming {Count} classes to {Path}; every number stays as the code has it",
            classes.Count,
            ConfigPath
        );
    }
}
