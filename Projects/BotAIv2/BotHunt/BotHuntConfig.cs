using System.IO;
using Server.Json;
using Server.Logging;

namespace Server.BotAI.V2;

/// <summary>
/// What <c>Configuration/bot-hunt.json</c> is allowed to say. Everything optional.
///
/// <para>
/// What is not here: what monsters exist, what they carry, how hard they hit, and where they stand. All of
/// that is the shard's own content, and a file able to disagree with it would be a file able to send a bot at
/// something it cannot beat. What a fight is worth is not here either — that is the ledger's, measured per
/// patch of ground.
/// </para>
/// </summary>
public sealed class BotHuntSettings
{
    public int? Reach { get; set; }

    public int? Notice { get; set; }

    public double? Daring { get; set; }

    public int? MusterReach { get; set; }

    public int? MusterLeast { get; set; }

    public double? BandExpects { get; set; }

    public int? SquadIdleCapMs { get; set; }

    public double? FleeAt { get; set; }

    public double? FitAt { get; set; }

    public int? FearedReach { get; set; }

    public double? Expects { get; set; }

    public double? WorkMinutes { get; set; }

    public double? FillFraction { get; set; }

    public int? ProwlArriveWithin { get; set; }

    public int? SlayNoProgressMs { get; set; }

    public int? SlayCapMs { get; set; }
}

/// <summary>Reads the hunt file and moves the numbers it names.</summary>
public static class BotHuntConfig
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotHuntConfig));

    private const string ConfigPath = "Configuration/bot-hunt.json";

    public static void Load()
    {
        var path = Path.Combine(Core.BaseDirectory, ConfigPath);
        var settings = JsonConfig.Deserialize<BotHuntSettings>(path);

        if (settings == null)
        {
            JsonConfig.Serialize(path, new BotHuntSettings());

            logger.Information(
                "Wrote a starter hunt file to {Path}; every number stays as the code has it",
                ConfigPath
            );

            return;
        }

        BotQuarry.Reach = settings.Reach ?? BotQuarry.Reach;
        BotQuarry.Notice = settings.Notice ?? BotQuarry.Notice;
        BotQuarry.Daring = settings.Daring ?? BotQuarry.Daring;

        BotMuster.Reach = settings.MusterReach ?? BotMuster.Reach;
        BotMuster.Least = settings.MusterLeast ?? BotMuster.Least;
        BotBand.Prior = settings.BandExpects ?? BotBand.Prior;
        BotSquad.IdleCapMs = settings.SquadIdleCapMs ?? BotSquad.IdleCapMs;

        BotSlay.FleeAt = settings.FleeAt ?? BotSlay.FleeAt;
        BotSlay.Prior = settings.Expects ?? BotSlay.Prior;
        BotSlay.WorkMinutes = settings.WorkMinutes ?? BotSlay.WorkMinutes;
        BotSlay.FillFraction = settings.FillFraction ?? BotSlay.FillFraction;

        BotHunter.FitAt = settings.FitAt ?? BotHunter.FitAt;
        BotHunter.FearedReach = settings.FearedReach ?? BotHunter.FearedReach;

        BotProwl.ArriveWithin = settings.ProwlArriveWithin ?? BotProwl.ArriveWithin;

        BotSlay.NoProgressMs = settings.SlayNoProgressMs ?? BotSlay.NoProgressMs;
        BotSlay.CapMs = settings.SlayCapMs ?? BotSlay.CapMs;
    }
}
