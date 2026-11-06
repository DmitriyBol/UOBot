using System.IO;
using Server.Json;
using Server.Logging;

namespace Server.BotAI.Mind;

public sealed class BotDebugSettings
{

    public string Name { get; set; }

    public string Model { get; set; }

    public string KeepAlive { get; set; }

    public int? TimeoutMs { get; set; }

    public int? RobeHue { get; set; }

    public string[] Helpers { get; set; }

    public int[] Hues { get; set; }

    public double? TaxShare { get; set; }

    public int? SampleMs { get; set; }

    public int? HoverMs { get; set; }

    public int? ReportMs { get; set; }

    public int? ReflectMs { get; set; }

    public int? Rows { get; set; }

    public int? FrozenMs { get; set; }

    public int? ImmortalMs { get; set; }

    public int? SettledMs { get; set; }

    public int? WindowMs { get; set; }

    public int? MostTouched { get; set; }

    public int? RestMs { get; set; }

    public int? TitleWaitMs { get; set; }

    public bool? Venues { get; set; }

    public bool? Arena { get; set; }

    public int? MinEntrants { get; set; }

    public int? StartHourLocal { get; set; }
}

public static class BotDebugConfig
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotDebugConfig));

    private const string ConfigPath = "Configuration/bot-debugger.json";

    public static void Load()
    {
        var path = Path.Combine(Core.BaseDirectory, ConfigPath);
        var settings = JsonConfig.Deserialize<BotDebugSettings>(path);

        if (settings == null)
        {
            JsonConfig.Serialize(path, new BotDebugSettings());

            logger.Information(
                "Wrote a starter debugger file to {Path}; every number stays as the code has it",
                ConfigPath
            );

            return;
        }

        BotVigil.Name = settings.Name ?? BotVigil.Name;
        BotVigil.Model = settings.Model ?? BotVigil.Model;
        BotVigil.KeepAlive = settings.KeepAlive ?? BotVigil.KeepAlive;
        BotVigil.TimeoutMs = settings.TimeoutMs ?? BotVigil.TimeoutMs;
        BotVigil.SampleMs = settings.SampleMs ?? BotVigil.SampleMs;
        BotVigil.HoverMs = settings.HoverMs ?? BotVigil.HoverMs;
        BotVigil.ReportMs = settings.ReportMs ?? BotVigil.ReportMs;
        BotVigil.ReflectMs = settings.ReflectMs ?? BotVigil.ReflectMs;
        BotVigil.Rows = settings.Rows ?? BotVigil.Rows;

        BotDebugger.RobeHue = settings.RobeHue ?? BotDebugger.RobeHue;
        BotVigil.Helpers = settings.Helpers ?? BotVigil.Helpers;
        BotVigil.Hues = settings.Hues ?? BotVigil.Hues;
        BotRevel.TaxShare = settings.TaxShare ?? BotRevel.TaxShare;

        BotWatch.FrozenMs = settings.FrozenMs ?? BotWatch.FrozenMs;
        BotWatch.ImmortalMs = settings.ImmortalMs ?? BotWatch.ImmortalMs;
        BotWatch.SettledMs = settings.SettledMs ?? BotWatch.SettledMs;

        BotAudit.WindowMs = settings.WindowMs ?? BotAudit.WindowMs;
        BotAudit.MostTouched = settings.MostTouched ?? BotAudit.MostTouched;
        BotAudit.RestMs = settings.RestMs ?? BotAudit.RestMs;

        BotTourney.TitleWaitMs = settings.TitleWaitMs ?? BotTourney.TitleWaitMs;
        BotVenues.Running = settings.Venues ?? BotVenues.Running;
        BotTourney.Arena = settings.Arena ?? BotTourney.Arena;
        BotTourney.MinEntrants = settings.MinEntrants ?? BotTourney.MinEntrants;
        BotTourney.StartHourLocal = settings.StartHourLocal ?? BotTourney.StartHourLocal;
    }
}
