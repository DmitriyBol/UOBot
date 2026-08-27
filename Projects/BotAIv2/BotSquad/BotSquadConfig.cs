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
    /// <summary>Most a squad may hold.</summary>
    public int? MaxSize { get; set; }

    /// <summary>
    /// The longest a single attack can take to come round again. The break-off window is this times
    /// <see cref="Blows"/> — set the parts rather than the product, so the squad's patience and its
    /// restationing clock cannot be tuned apart from one another.
    /// </summary>
    public int? SlowestBlowMs { get; set; }

    /// <summary>How many fruitless swings of the slowest weapon a company sits through before breaking off.</summary>
    public int? Blows { get; set; }

    /// <summary>The hard ceiling on one fight.</summary>
    public int? FightCapMs { get; set; }

    /// <summary>How many bots make a knot while sweeping.</summary>
    public int? KnotSize { get; set; }

    /// <summary>How far a knot goes from the anchor.</summary>
    public int? Spread { get; set; }

    /// <summary>How close a member has to be to be counted in a share-out.</summary>
    public int? Earshot { get; set; }

    /// <summary>How far around a member the squad looks when working out what is attacking it.</summary>
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
        BotScatter.KnotSize = settings.KnotSize ?? BotScatter.KnotSize;
        BotScatter.Spread = settings.Spread ?? BotScatter.Spread;
        BotSpoils.Earshot = settings.Earshot ?? BotSpoils.Earshot;
        BotSquads.Reach = settings.Reach ?? BotSquads.Reach;
    }
}
