using System.Collections.Generic;
using System.IO;
using Server.Json;
using Server.Logging;

namespace Server.BotAI.V2;

/// <summary>
/// What <c>Configuration/bot-population.json</c> is allowed to say.
///
/// <para>
/// <b>The one config file in this project that ships with real values rather than empty ones.</b> Everywhere
/// else absent means "keep the number the code chose", which is harmless. Here an absent class mix means no
/// bots exist at all — so the starter file is written with a small working population in it, and the first
/// boot produces a shard with somebody on it rather than a shard with a question.
/// </para>
/// </summary>
public sealed class BotPopulationSettings
{
    /// <summary>Which facet, by name — <c>Felucca</c>, <c>Trammel</c> and so on.</summary>
    public string Map { get; set; }

    /// <summary>Where on it, as <c>[x, y, z]</c>.</summary>
    public int[] Home { get; set; }

    /// <summary>
    /// How many of each class, by the class's own name. Names must match the nine — a typo is reported by
    /// name at boot rather than quietly producing a smaller population.
    /// </summary>
    public Dictionary<string, int> Classes { get; set; }

    /// <summary>How far around home bots are scattered when they are born.</summary>
    public int? Spread { get; set; }

    /// <summary>
    /// Coin every bot is born holding. See <see cref="BotOutfit.Purse"/> for why a population needs a float
    /// at all.
    ///
    /// It lives in this file rather than beside the kit because the kit has no configuration of its own, and
    /// this is the one file that already ships with real values. That makes it the same small debt the
    /// market's file owes the mine: a dial that belongs to one subsystem, read by another's file.
    /// </summary>
    public int? Purse { get; set; }

    /// <summary>How far from home the population may want anything at all.</summary>
    public int? Roam { get; set; }

    /// <summary>How often the population's clock looks at everybody. Not the pace of a bot.</summary>
    public int? BeatMs { get; set; }

    /// <summary>How long a dead bot lies there before it is put back on its feet.</summary>
    public int? ReviveMs { get; set; }

    /// <summary>Whether bots run rather than walk. Sets the beat as well as the pace.</summary>
    public bool? Run { get; set; }

    /// <summary>How far a bot reckons the fight it is in when something hits it.</summary>
    public int? NoticeRange { get; set; }

    /// <summary>How much health a bot needs to count as help in somebody else's fight.</summary>
    public double? FitFraction { get; set; }
}

/// <summary>Reads the population file, or writes a working one.</summary>
public static class BotPopulationConfig
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotPopulationConfig));

    private const string ConfigPath = "Configuration/bot-population.json";

    /// <summary>
    /// The population a fresh shard gets.
    ///
    /// <para>
    /// Four, and the mix is not arbitrary: two gatherers and a crafter are born with a pickaxe, which is what
    /// decides who can mine, and mining is the only work that exists yet. The warrior is there precisely
    /// <em>because</em> it has nothing to do — it is the visible proof that the census's "nothing was worth
    /// doing" counts a fact about the world and not a broken bot.
    /// </para>
    /// </summary>
    private static Dictionary<string, int> StarterMix() =>
        new()
        {
            ["Gatherer"] = 2,
            ["Crafter"] = 1,
            ["Warrior"] = 1
        };

    public static void Load()
    {
        var path = Path.Combine(Core.BaseDirectory, ConfigPath);
        var settings = JsonConfig.Deserialize<BotPopulationSettings>(path);

        if (settings == null)
        {
            settings = new BotPopulationSettings
            {
                Map = "Felucca",
                Home = [BotPopulation.Where.X, BotPopulation.Where.Y, BotPopulation.Where.Z],
                Classes = StarterMix()
            };

            JsonConfig.Serialize(path, settings);

            logger.Information(
                "Wrote a starter population file to {Path}: four bots in Britain. Edit it to change who exists",
                ConfigPath
            );
        }

        Apply(settings);
    }

    /// <summary>What was read, or the working default where nothing was said.</summary>
    public static IReadOnlyDictionary<string, int> Mix { get; private set; } = StarterMix();

    private static void Apply(BotPopulationSettings settings)
    {
        // TryParse rather than Parse: a facet name out of a configuration file is user input, and Parse
        // throws on nonsense. A typo in a config file must produce a named complaint and a working shard, not
        // an exception on the way up.
        Map map = null;

        if (!string.IsNullOrWhiteSpace(settings.Map) && !Map.TryParse(settings.Map, null, out map))
        {
            map = null;
        }

        if (map == null || map == Map.Internal)
        {
            // Named, because "no bots appeared" is not a diagnosis and "Felucca is not a facet on this shard"
            // is. Felucca by default: it is where a population that wants a smithy and a bank should live.
            if (!string.IsNullOrWhiteSpace(settings.Map))
            {
                logger.Error("There is no facet called {Map}; the population falls back to Felucca", settings.Map);
            }

            map = Map.Felucca;
        }

        BotPopulation.Home = map;

        if (settings.Home is { Length: >= 3 })
        {
            BotPopulation.Where = new Point3D(settings.Home[0], settings.Home[1], settings.Home[2]);
        }

        BotPopulation.Spread = settings.Spread ?? BotPopulation.Spread;
        BotOutfit.Purse = settings.Purse ?? BotOutfit.Purse;
        BotPopulation.Roam = settings.Roam ?? BotPopulation.Roam;
        BotPopulation.ReviveMs = settings.ReviveMs ?? BotPopulation.ReviveMs;

        BotBeat.IntervalMs = settings.BeatMs ?? BotBeat.IntervalMs;

        BotMobile.Runs = settings.Run ?? BotMobile.Runs;
        BotMobile.NoticeRange = settings.NoticeRange ?? BotMobile.NoticeRange;
        BotMobile.FitFraction = settings.FitFraction ?? BotMobile.FitFraction;

        Mix = settings.Classes is { Count: > 0 } ? settings.Classes : StarterMix();
    }
}
