using System.IO;
using Server.Json;
using Server.Logging;

namespace Server.BotAI.V2;

/// <summary>
/// What <c>Configuration/bot-spells.json</c> is allowed to say. Everything optional.
///
/// <para>
/// What is not here, and could not be: which spells exist, what they are made of, how hard they are to write,
/// and what a spell is worth. The first three are the shard's own inscription system and a file able to
/// disagree with it would be a file able to send a scribe swinging at something it cannot make. The fourth is
/// not a number in this project at all — a spell has no price, because filling a book is what happens to a
/// scribe who gets good at writing rather than something it buys.
/// </para>
/// </summary>
public sealed class BotSpellsSettings
{
    public double? Margin { get; set; }

    public double? Markup { get; set; }

    public int? Reserve { get; set; }

    public int? HerbGuess { get; set; }

    public int? Batch { get; set; }

    public int? SwingMs { get; set; }

    public int? PatienceMs { get; set; }

    public double? Expects { get; set; }

    public double? WorkMinutes { get; set; }

    public double? SeekExpects { get; set; }

    public double? SeekMinutes { get; set; }
}

/// <summary>Reads the spells file and moves the numbers it names.</summary>
public static class BotSpellsConfig
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotSpellsConfig));

    private const string ConfigPath = "Configuration/bot-spells.json";

    public static void Load()
    {
        var path = Path.Combine(Core.BaseDirectory, ConfigPath);
        var settings = JsonConfig.Deserialize<BotSpellsSettings>(path);

        if (settings == null)
        {
            JsonConfig.Serialize(path, new BotSpellsSettings());

            logger.Information(
                "Wrote a starter spells file to {Path}; every number stays as the code has it",
                ConfigPath
            );

            return;
        }

        BotQuill.Margin = settings.Margin ?? BotQuill.Margin;
        BotQuill.Markup = settings.Markup ?? BotQuill.Markup;
        BotQuill.Reserve = settings.Reserve ?? BotQuill.Reserve;
        BotQuill.HerbGuess = settings.HerbGuess ?? BotQuill.HerbGuess;

        BotInscribe.Batch = settings.Batch ?? BotInscribe.Batch;
        BotInscribe.SwingMs = settings.SwingMs ?? BotInscribe.SwingMs;
        BotInscribe.PatienceMs = settings.PatienceMs ?? BotInscribe.PatienceMs;
        BotInscribe.Prior = settings.Expects ?? BotInscribe.Prior;
        BotInscribe.WorkMinutes = settings.WorkMinutes ?? BotInscribe.WorkMinutes;

        BotAcquire.Prior = settings.SeekExpects ?? BotAcquire.Prior;
        BotAcquire.WorkMinutes = settings.SeekMinutes ?? BotAcquire.WorkMinutes;
    }
}
