using System.IO;
using Server.Json;
using Server.Logging;

namespace Server.BotAI.Mind;

/// <summary>
/// What <c>Configuration/bot-debugger.json</c> is allowed to say. Everything optional; an empty file means
/// "keep the numbers the code chose", which is what is written on the first boot.
///
/// <para>
/// <b>PascalCase, and it is not a style question on this shard.</b> The deserialiser matches these names as
/// written, so a key in lower case is not an error and not a warning — it is a value silently left at its
/// default, and a configuration file that appears to have been read is worse than one that fails to load.
/// </para>
///
/// <para>
/// <b>Its own file rather than a section of the minds'.</b> The two are switched on and off separately and
/// tuned for opposite reasons: the minds are tuned for how often a bot may change its mind, the debugger for
/// how much of one graphics card a watcher may spend. A single file would mean editing the thinking bots'
/// cadence to change how often the debugger writes a paragraph, which is exactly the mistake the whole
/// project's one-file-per-subsystem rule exists to prevent.
/// </para>
/// </summary>
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
}

/// <summary>Reads the debugger's file and moves the numbers it names.</summary>
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
    }
}
