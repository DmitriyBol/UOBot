using System.IO;
using Server.Json;
using Server.Logging;

namespace Server.BotAI.V2;

/// <summary>
/// What <c>Configuration/bot-web.json</c> may say. Everything optional; an empty file keeps the code's numbers.
/// Keys are PascalCase and a wrong key is silent — the boot line is the proof of what took.
/// </summary>
public sealed class BotWebSettings
{
    public bool? Enabled { get; set; }

    public string Bind { get; set; }

    public int? Port { get; set; }

    public int? SnapshotMs { get; set; }

    public int? PagesMs { get; set; }

    public int? HistoryMs { get; set; }

    public int? KeepHistory { get; set; }

    public int? KeepEvents { get; set; }

    public int? KeepPaths { get; set; }

    public bool? MapImage { get; set; }

    public int? MapScale { get; set; }

    public string Folder { get; set; }
}

public static class BotWebConfig
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotWebConfig));

    private const string ConfigPath = "Configuration/bot-web.json";

    public static bool Enabled { get; private set; } = true;

    public static void Load()
    {
        var path = Path.Combine(Core.BaseDirectory, ConfigPath);
        var settings = JsonConfig.Deserialize<BotWebSettings>(path);

        if (settings == null)
        {
            JsonConfig.Serialize(path, new BotWebSettings { Enabled = true, Bind = "127.0.0.1", Port = 2599 });

            logger.Information("Wrote a starter web file to {Path}; every number stays as the code has it", ConfigPath);

            return;
        }

        Enabled = settings.Enabled ?? Enabled;
        BotWebServer.Bind = string.IsNullOrWhiteSpace(settings.Bind) ? BotWebServer.Bind : settings.Bind.Trim();
        BotWebServer.Port = settings.Port ?? BotWebServer.Port;
        BotWebServer.Folder = string.IsNullOrWhiteSpace(settings.Folder) ? BotWebServer.Folder : settings.Folder.Trim();
        BotWebSnapshot.SnapshotMs = settings.SnapshotMs ?? BotWebSnapshot.SnapshotMs;
        BotWebSnapshot.PagesMs = settings.PagesMs ?? BotWebSnapshot.PagesMs;
        BotWebHistory.EveryMs = settings.HistoryMs ?? BotWebHistory.EveryMs;
        BotWebHistory.Keep = settings.KeepHistory ?? BotWebHistory.Keep;
        BotEvents.Keep = settings.KeepEvents ?? BotEvents.Keep;
        BotEvents.KeepPaths = settings.KeepPaths ?? BotEvents.KeepPaths;
        BotWebMap.Enabled = settings.MapImage ?? BotWebMap.Enabled;
        BotWebMap.Scale = settings.MapScale ?? BotWebMap.Scale;
    }
}
