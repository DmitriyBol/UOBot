using System.Collections.Generic;
using System.IO;
using Server.Json;
using Server.Logging;

namespace Server.BotAI.V2;

/// <summary>
/// What <c>Configuration/bot-voice.json</c> may say. Everything optional; the starter file the shard writes
/// carries the whole phrase bank so that it can be edited rather than discovered.
/// </summary>
public sealed class BotVoiceSettings
{
    public bool? Enabled { get; set; }

    public bool? World { get; set; }

    public bool? Guild { get; set; }

    public bool? Local { get; set; }

    public bool? Mood { get; set; }

    public bool? Cry { get; set; }

    public int? SayEveryMs { get; set; }

    public int? CryEveryMs { get; set; }

    public int? MoodEveryMs { get; set; }

    public double? WorkChance { get; set; }

    public double? FailChance { get; set; }

    public double? DropChance { get; set; }

    public int? MostLetters { get; set; }

    public int? WorldHue { get; set; }

    public int? GuildHue { get; set; }

    public int? CryHue { get; set; }

    public int? MoodHue { get; set; }

    public bool? Broadcast { get; set; }

    public Dictionary<string, string[]> Phrases { get; set; }
}

public static class BotVoiceConfig
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotVoiceConfig));

    private const string ConfigPath = "Configuration/bot-voice.json";

    public static void Load()
    {
        var path = Path.Combine(Core.BaseDirectory, ConfigPath);
        var settings = JsonConfig.Deserialize<BotVoiceSettings>(path);

        if (settings == null)
        {
            JsonConfig.Serialize(
                path,
                new BotVoiceSettings
                {
                    Enabled = true,
                    World = true,
                    Guild = true,
                    Local = true,
                    Mood = true,
                    Cry = true,
                    SayEveryMs = BotVoice.SayEveryMs,
                    CryEveryMs = BotVoice.CryEveryMs,
                    MoodEveryMs = BotVoice.MoodEveryMs,
                    WorkChance = BotVoice.WorkChance,
                    FailChance = BotVoice.FailChance,
                    DropChance = BotVoice.DropChance,
                    MostLetters = BotVoice.MostLetters,
                    WorldHue = BotVoice.WorldHue,
                    GuildHue = BotVoice.GuildHue,
                    CryHue = BotVoice.CryHue,
                    MoodHue = BotVoice.MoodHue,
                    Broadcast = BotVoice.Broadcast,
                    Phrases = BotPhrases.Defaults()
                }
            );

            logger.Information("Wrote a starter voice file with the whole phrase bank to {Path}", ConfigPath);

            return;
        }

        BotVoice.Enabled = settings.Enabled ?? BotVoice.Enabled;
        BotVoice.WorldOn = settings.World ?? BotVoice.WorldOn;
        BotVoice.GuildOn = settings.Guild ?? BotVoice.GuildOn;
        BotVoice.LocalOn = settings.Local ?? BotVoice.LocalOn;
        BotVoice.MoodOn = settings.Mood ?? BotVoice.MoodOn;
        BotVoice.CryOn = settings.Cry ?? BotVoice.CryOn;
        BotVoice.SayEveryMs = settings.SayEveryMs ?? BotVoice.SayEveryMs;
        BotVoice.CryEveryMs = settings.CryEveryMs ?? BotVoice.CryEveryMs;
        BotVoice.MoodEveryMs = settings.MoodEveryMs ?? BotVoice.MoodEveryMs;
        BotVoice.WorkChance = settings.WorkChance ?? BotVoice.WorkChance;
        BotVoice.FailChance = settings.FailChance ?? BotVoice.FailChance;
        BotVoice.DropChance = settings.DropChance ?? BotVoice.DropChance;
        BotVoice.MostLetters = settings.MostLetters ?? BotVoice.MostLetters;
        BotVoice.WorldHue = settings.WorldHue ?? BotVoice.WorldHue;
        BotVoice.GuildHue = settings.GuildHue ?? BotVoice.GuildHue;
        BotVoice.CryHue = settings.CryHue ?? BotVoice.CryHue;
        BotVoice.MoodHue = settings.MoodHue ?? BotVoice.MoodHue;
        BotVoice.Broadcast = settings.Broadcast ?? BotVoice.Broadcast;

        if (settings.Phrases != null)
        {
            BotVoice.Phrases(settings.Phrases);
        }
    }
}
