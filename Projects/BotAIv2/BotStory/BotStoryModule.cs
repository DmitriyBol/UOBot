using System;
using System.Collections.Generic;
using System.IO;
using Server.Json;
using Server.Logging;

namespace Server.BotAI.V2;

public sealed class BotStoryModule : BotModule
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotStoryModule));

    public static int SayEveryMs { get; set; } = 300000;

    private static Timer _clock;

    private static Timer _summary;

    public override string Name => "Story";

    public override BotPhase Phase => BotPhase.World;

    public override string[] Requires => ["Population", "Character"];

    public override void Start()
    {
        BotStoryConfig.Load();

        Stop();

        BotEvents.Heard = BotStory.Heard;
        BotStory.Started(Core.TickCount);

        _clock = Timer.DelayCall(
            TimeSpan.FromMilliseconds(Math.Max(250, BotStory.TickMs)),
            TimeSpan.FromMilliseconds(Math.Max(250, BotStory.TickMs)),
            () => BotStory.Tick(Core.TickCount)
        );

        _summary = Timer.DelayCall(
            TimeSpan.FromMilliseconds(Math.Max(10000, SayEveryMs)),
            TimeSpan.FromMilliseconds(Math.Max(10000, SayEveryMs)),
            () => logger.Information("Stories: {What}", BotStory.Describe())
        );

        logger.Information(
            "The stories are {State}: {Books} books kept; every bot looked at once in {Every}s ({Tick}ms a slice), at most {Notes} notes a bot and {Most} books; "
            + "old age after {Old} days; the model {Voice}; the Chronicle keeps {Days} days of at most {PerDay} entries",
            BotStory.Running ? "on" : "off",
            BotStory.Count,
            BotStory.EveryMs / 1000,
            BotStory.TickMs,
            BotStory.MostNotes,
            BotStory.MostBooks,
            BotStory.OldAgeDays,
            BotStory.Voiced
                ? $"retells at most {BotStory.VoicePerHour} chapters or days an hour (chapters of {BotStory.VoiceLeastFacts} facts or more, {BotStory.VoiceMostWords} words), with {(string.IsNullOrWhiteSpace(BotStory.VoiceModel) ? "the duke's model" : BotStory.VoiceModel)}"
                : "is not asked",
            BotChronicle.MostDays,
            BotChronicle.MostPerDay
        );
    }

    private static void Stop()
    {
        _clock?.Stop();
        _clock = null;
        _summary?.Stop();
        _summary = null;
    }

    public override void Reset()
    {
        logger.Information("Stories, before the reload: {What}", BotStory.Describe());

        Stop();
        BotEvents.Heard = null;
        BotStory.Forget();
        BotStory.Unseal();
    }
}

/// <summary>
/// Keeps the stories across restarts (PLAN-life A3, 13.10.2026): every bot's book — when it was first seen or born, what it was

public sealed class BotStoryStore : GenericPersistence
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotStoryStore));

    private static BotStoryStore _store;

    public static void Open() => _store ??= new BotStoryStore();

    public BotStoryStore() : base("BotStory", 25)
    {
    }

    public override void Serialize(IGenericWriter writer) => BotStory.Save(writer);

    public override void Deserialize(string savePath, Dictionary<ulong, string> typesDb)
    {
        var path = Path.GetFullPath(SaveFilePath, savePath);

        if (Peek(path) is { } shape && shape is not (1 or 2))
        {
            logger.Warning(
                "The saved stories ({Path}) are shape {Found} and this build reads {Wanted}: not read, every book opens again at the start and the next save writes the new shape",
                path,
                shape,
                BotStory.Shape
            );

            return;
        }

        base.Deserialize(savePath, typesDb);
    }

    private static int? Peek(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);

            int value = 0, shift = 0, b;

            do
            {
                b = stream.ReadByte();

                if (b < 0 || shift > 28)
                {
                    return null;
                }

                value |= (b & 0x7F) << shift;
                shift += 7;
            }
            while (b >= 0x80);

            return value;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    public override void Deserialize(IGenericReader reader) => BotStory.Load(reader);
}

public static class BotStoryCore
{

    [CallPriority(47)]
    public static void Configure()
    {
        if (!BotCore.Enabled)
        {
            return;
        }

        BotStoryConfig.Load();

        BotStoryStore.Open();
        BotModules.Register(new BotStoryModule());
    }
}

public sealed class BotStorySettings
{
    public bool? Running { get; set; }

    public int? TickMs { get; set; }

    public int? EveryMs { get; set; }

    public int? PublishEveryMs { get; set; }

    public int? SettleMs { get; set; }

    public int? MostNotes { get; set; }

    public int? MostBooks { get; set; }

    public int? OldAgeDays { get; set; }

    public bool? Voiced { get; set; }

    public int? VoicePerHour { get; set; }

    public string VoiceModel { get; set; }

    public int? VoiceLeastFacts { get; set; }

    public int? VoiceMostChars { get; set; }

    public int? VoiceMostWords { get; set; }

    public int? MostWanted { get; set; }

    public int? ChronicleDays { get; set; }

    public int? ChroniclePerDay { get; set; }

    public int? ChronicleEveryMs { get; set; }

    public int? ChronicleVoiceLeast { get; set; }

    public int? ChronicleVoiceMostFacts { get; set; }

    public int? ChronicleRepeatMinutes { get; set; }

    public double? ChronicleRepeatDayBelow { get; set; }

    public int? SayEveryMs { get; set; }
}

public static class BotStoryConfig
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotStoryConfig));

    private const string ConfigPath = "Configuration/bot-story.json";

    public static void Load()
    {
        var path = Path.Combine(Core.BaseDirectory, ConfigPath);
        var settings = JsonConfig.Deserialize<BotStorySettings>(path);

        if (settings == null)
        {
            JsonConfig.Serialize(path, new BotStorySettings());

            logger.Information("Wrote a starter story file to {Path}; every number stays as the code has it", ConfigPath);

            return;
        }

        BotStory.Running = settings.Running ?? BotStory.Running;
        BotStory.TickMs = settings.TickMs ?? BotStory.TickMs;
        BotStory.EveryMs = settings.EveryMs ?? BotStory.EveryMs;
        BotStory.PublishEveryMs = settings.PublishEveryMs ?? BotStory.PublishEveryMs;
        BotStory.SettleMs = settings.SettleMs ?? BotStory.SettleMs;
        BotStory.MostNotes = settings.MostNotes ?? BotStory.MostNotes;
        BotStory.MostBooks = settings.MostBooks ?? BotStory.MostBooks;
        BotStory.OldAgeDays = settings.OldAgeDays ?? BotStory.OldAgeDays;
        BotStory.Voiced = settings.Voiced ?? BotStory.Voiced;
        BotStory.VoicePerHour = settings.VoicePerHour ?? BotStory.VoicePerHour;
        BotStory.VoiceModel = settings.VoiceModel ?? BotStory.VoiceModel;
        BotStory.VoiceModel = BotStory.VoiceModel?.Trim() ?? "";
        BotStory.VoiceLeastFacts = settings.VoiceLeastFacts ?? BotStory.VoiceLeastFacts;
        BotStory.VoiceMostChars = settings.VoiceMostChars ?? BotStory.VoiceMostChars;
        BotStory.VoiceMostWords = settings.VoiceMostWords ?? BotStory.VoiceMostWords;
        BotStory.MostWanted = settings.MostWanted ?? BotStory.MostWanted;

        BotChronicle.MostDays = settings.ChronicleDays ?? BotChronicle.MostDays;
        BotChronicle.MostPerDay = settings.ChroniclePerDay ?? BotChronicle.MostPerDay;
        BotChronicle.EveryMs = settings.ChronicleEveryMs ?? BotChronicle.EveryMs;
        BotChronicle.VoiceLeast = settings.ChronicleVoiceLeast ?? BotChronicle.VoiceLeast;
        BotChronicle.VoiceMostFacts = settings.ChronicleVoiceMostFacts ?? BotChronicle.VoiceMostFacts;
        BotChronicle.RepeatMinutes = settings.ChronicleRepeatMinutes ?? BotChronicle.RepeatMinutes;
        BotChronicle.RepeatDayBelow = settings.ChronicleRepeatDayBelow ?? BotChronicle.RepeatDayBelow;

        BotStoryModule.SayEveryMs = settings.SayEveryMs ?? BotStoryModule.SayEveryMs;
    }
}
