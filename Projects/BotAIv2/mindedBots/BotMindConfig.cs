using System.IO;
using Server.Json;
using Server.Logging;

namespace Server.BotAI.Mind;

/// <summary>
/// What <c>Configuration/bot-mind.json</c> is allowed to say. Everything optional; empty means "keep the
/// numbers the code chose".
///
/// <para>
/// <b>PascalCase, and it is not a style question.</b> The deserialiser matches these names as written, so a
/// key in lower case is not an error and not a warning — it is a value silently left at its default, and a
/// configuration file that appears to have been read is worse than one that fails to load.
/// </para>
/// </summary>
public sealed class BotMindSettings
{
    public string Model { get; set; }

    public string Endpoint { get; set; }

    public string KeepAlive { get; set; }

    public int? TimeoutMs { get; set; }

    public string WarriorName { get; set; }

    public string ArchitectName { get; set; }

    public string SageName { get; set; }

    public string BaronName { get; set; }

    public string[] CrafterNames { get; set; }

    public int? ThinkEveryMs { get; set; }

    public int? ReviewEveryMs { get; set; }

    public int? ChoiceHoldsMs { get; set; }

    public int? MostLessons { get; set; }

    public double? Insistence { get; set; }
}

/// <summary>Reads the mind file and moves the numbers it names.</summary>
public static class BotMindConfig
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotMindConfig));

    private const string ConfigPath = "Configuration/bot-mind.json";

    public static void Load()
    {
        var path = Path.Combine(Core.BaseDirectory, ConfigPath);
        var settings = JsonConfig.Deserialize<BotMindSettings>(path);

        if (settings == null)
        {
            JsonConfig.Serialize(path, new BotMindSettings());

            logger.Information(
                "Wrote a starter mind file to {Path}; every number stays as the code has it",
                ConfigPath
            );

            return;
        }

        BotOllama.Model = settings.Model ?? BotOllama.Model;
        BotOllama.Endpoint = settings.Endpoint ?? BotOllama.Endpoint;
        BotOllama.KeepAlive = settings.KeepAlive ?? BotOllama.KeepAlive;
        BotOllama.TimeoutMs = settings.TimeoutMs ?? BotOllama.TimeoutMs;

        BotMinds.WarriorName = settings.WarriorName ?? BotMinds.WarriorName;
        BotMinds.ArchitectName = settings.ArchitectName ?? BotMinds.ArchitectName;
        BotMinds.SageName = settings.SageName ?? BotMinds.SageName;
        BotMinds.BaronName = settings.BaronName ?? BotMinds.BaronName;

        BotMinds.CrafterNames = settings.CrafterNames ?? BotMinds.CrafterNames;

        BotMind.ThinkEveryMs = settings.ThinkEveryMs ?? BotMind.ThinkEveryMs;
        BotMind.ReviewEveryMs = settings.ReviewEveryMs ?? BotMind.ReviewEveryMs;
        BotMind.ChoiceHoldsMs = settings.ChoiceHoldsMs ?? BotMind.ChoiceHoldsMs;
        BotMind.MostLessons = settings.MostLessons ?? BotMind.MostLessons;

        BotMindDeed.Insistence = settings.Insistence ?? BotMindDeed.Insistence;
    }
}
