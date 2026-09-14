using System.IO;
using Server.Json;
using Server.Logging;

namespace Server.BotAI.V2;

/// <summary>
/// What <c>Configuration/bot-squad.json</c> may say. Everything optional.
///
/// Numbers only. The rules are not configurable and should not be: that blades stand in front of casters, that
/// a fight is judged by whether the target's health is falling, that nobody stands still — those are not
/// preferences, they are what four separate defects taught, and a file able to disagree with them is a file
/// able to reinstate them.
/// </summary>
public sealed class BotSquadSettings
{
    public int? MaxSize { get; set; }

    public int? SlowestBlowMs { get; set; }

    public int? Blows { get; set; }

    public int? FightCapMs { get; set; }

    public int? BlindMs { get; set; }

    public int? RestCapMs { get; set; }

    public int? IdleCapMs { get; set; }

    public int? KnotSize { get; set; }

    public int? Spread { get; set; }

    public int? Earshot { get; set; }

    public int? Reach { get; set; }
}

/// <summary>Reads the squad file and moves the numbers it names.</summary>
public static class BotSquadConfig
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotSquadConfig));

    private const string ConfigPath = "Configuration/bot-squad.json";

    public static void Load()
    {
        var path = Path.Combine(Core.BaseDirectory, ConfigPath);
        var settings = JsonConfig.Deserialize<BotSquadSettings>(path);

        if (settings == null)
        {
            JsonConfig.Serialize(path, new BotSquadSettings());

            logger.Information(
                "Wrote a starter squad file to {Path}; every number stays as the code has it",
                ConfigPath
            );

            return;
        }

        BotSquad.MaxSize = settings.MaxSize ?? BotSquad.MaxSize;
        BotSquad.SlowestBlowMs = settings.SlowestBlowMs ?? BotSquad.SlowestBlowMs;
        BotSquad.Blows = settings.Blows ?? BotSquad.Blows;
        BotSquad.FightCapMs = settings.FightCapMs ?? BotSquad.FightCapMs;
        BotSquad.BlindMs = settings.BlindMs ?? BotSquad.BlindMs;
        BotSquad.RestCapMs = settings.RestCapMs ?? BotSquad.RestCapMs;
        BotSquad.IdleCapMs = settings.IdleCapMs ?? BotSquad.IdleCapMs;
        BotScatter.KnotSize = settings.KnotSize ?? BotScatter.KnotSize;
        BotScatter.Spread = settings.Spread ?? BotScatter.Spread;
        BotSpoils.Earshot = settings.Earshot ?? BotSpoils.Earshot;
        BotSquads.Reach = settings.Reach ?? BotSquads.Reach;
    }
}
