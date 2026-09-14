using System.IO;
using Server.Json;
using Server.Logging;

namespace Server.BotAI.V2;

/// <summary>
/// What <c>Configuration/bot-mend.json</c> is allowed to say. Everything optional.
///
/// <para>
/// What is not here: how much a heal heals, what it costs in mana, how long a bandage takes. All of that is the
/// engine's, and a file able to disagree with it would be a file able to promise a bot a rescue that does not
/// arrive.
/// </para>
/// </summary>
public sealed class BotMendSettings
{
    public double? Hurt { get; set; }

    public double? Mended { get; set; }

    public int? Cast { get; set; }

    public int? UnderFireMs { get; set; }

    public double? Gulp { get; set; }

    public double? Urgency { get; set; }

    public int? Watch { get; set; }

    public int? TryMs { get; set; }

    public double? Expects { get; set; }

    public double? WorkMinutes { get; set; }

    public int? FleeWatch { get; set; }

    public int? FleeBound { get; set; }

    public int? FleeGiveUpMs { get; set; }

    public double? FleeBearable { get; set; }

    public double? FleeExpects { get; set; }
}

/// <summary>Reads the mending file and moves the numbers it names.</summary>
public static class BotMendConfig
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotMendConfig));

    private const string ConfigPath = "Configuration/bot-mend.json";

    public static void Load()
    {
        var path = Path.Combine(Core.BaseDirectory, ConfigPath);
        var settings = JsonConfig.Deserialize<BotMendSettings>(path);

        if (settings == null)
        {
            JsonConfig.Serialize(path, new BotMendSettings());

            logger.Information(
                "Wrote a starter mending file to {Path}; every number stays as the code has it",
                ConfigPath
            );

            return;
        }

        BotMend.Hurt = settings.Hurt ?? BotMend.Hurt;
        BotMend.Mended = settings.Mended ?? BotMend.Mended;
        BotMend.Cast = settings.Cast ?? BotMend.Cast;
        BotMend.UnderFireMs = settings.UnderFireMs ?? BotMend.UnderFireMs;
        BotMend.Gulp = settings.Gulp ?? BotMend.Gulp;

        BotSalve.Urgency = settings.Urgency ?? BotSalve.Urgency;

        BotSalve.TryMs = settings.TryMs ?? BotSalve.TryMs;
        BotSalve.Prior = settings.Expects ?? BotSalve.Prior;
        BotSalve.WorkMinutes = settings.WorkMinutes ?? BotSalve.WorkMinutes;

        BotSurgeon.Reach = settings.Watch ?? BotSurgeon.Reach;

        BotBolt.Watch = settings.FleeWatch ?? BotBolt.Watch;
        BotBolt.Bound = settings.FleeBound ?? BotBolt.Bound;
        BotBolt.GiveUpMs = settings.FleeGiveUpMs ?? BotBolt.GiveUpMs;
        BotBolt.Prior = settings.FleeExpects ?? BotBolt.Prior;

        BotFugitive.Bearable = settings.FleeBearable ?? BotFugitive.Bearable;
    }
}
