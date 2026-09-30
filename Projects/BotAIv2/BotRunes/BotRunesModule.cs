using System;
using System.IO;
using Server.Json;
using Server.Logging;

namespace Server.BotAI.V2;

/// <summary>What <c>Configuration/bot-runes.json</c> is allowed to say. A missing key keeps the code's value; keys are PascalCase.</summary>
public sealed class BotRunesSettings
{
    public bool? Running { get; set; }

    public double? LeastMagery { get; set; }

    public double? GateMagery { get; set; }

    public bool? Learn { get; set; }

    public double? LeastChance { get; set; }

    public int? Tries { get; set; }

    public int? Blanks { get; set; }

    public int? Far { get; set; }

    public int? LeastSaved { get; set; }

    public int? GateNear { get; set; }

    public double? CrossSeconds { get; set; }
}

/// <summary>
/// Runes as a module: reads its numbers and the guilds' knowledge, offers the mark and the blanks, and looks at the guilds'
/// libraries on its own clock. The recall and the company's gate are asked by the walks that go far, not offered. See
/// <see cref="BotRunes"/> — Patrick's order of 29.09.2026, point 7.
/// </summary>
public sealed class BotRunesModule : BotModule
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotRunesModule));

    private const string ConfigPath = "Configuration/bot-runes.json";

    private static Timer _timer;

    public override string Name => "Runes";

    public override BotPhase Phase => BotPhase.World;

    public override string[] Requires => ["Population", "Will", "Movement", "Squads"];

    public override void Start()
    {
        Load();
        BotRuneStore.Configure();

        if (BotRunes.Running)
        {
            BotWill.Offer(new BotMarker());
            BotWill.Offer(new BotRuneBuyer());
        }

        logger.Information(
            "Runes are {State}: a mage of Magery {Least}+ marks where its guild holds no rune (its seat, a cave mouth, a town) and is taught Recall and Mark first{Learn}, Gate Travel from {Gate}; a cast is tried at {Chance:P0} of the engine's chance or better, {Tries} times, then the bot walks; a recall is asked of walks over {Far} tiles that a rune shortens by {Saved}; a company is gated to a rune within {Near} of where it goes; {Blanks} blanks kept, a blank a copy at the library",
            BotRunes.Running ? "on" : "off",
            BotRunes.LeastMagery,
            BotRunes.Learn ? "" : " (not taught: off)",
            BotRunes.GateMagery,
            BotRunes.LeastChance,
            BotRunes.Tries,
            BotRunes.Far,
            BotRunes.LeastSaved,
            BotRunes.GateNear,
            BotRunes.Blanks
        );

        _timer?.Stop();
        _timer = Timer.DelayCall(TimeSpan.FromMilliseconds(BotRuneLibrary.EveryMs), TimeSpan.FromMilliseconds(Math.Max(1000, BotRuneLibrary.EveryMs)), BotRuneLibrary.Tick);
    }

    private static void Load()
    {
        var path = Path.Combine(Core.BaseDirectory, ConfigPath);
        var settings = JsonConfig.Deserialize<BotRunesSettings>(path);

        if (settings == null)
        {
            JsonConfig.Serialize(path, new BotRunesSettings());
            logger.Information("Wrote a starter runes file to {Path}; every number stays as the code has it", ConfigPath);

            return;
        }

        BotRunes.Running = settings.Running ?? BotRunes.Running;
        BotRunes.LeastMagery = settings.LeastMagery ?? BotRunes.LeastMagery;
        BotRunes.GateMagery = settings.GateMagery ?? BotRunes.GateMagery;
        BotRunes.Learn = settings.Learn ?? BotRunes.Learn;
        BotRunes.LeastChance = settings.LeastChance ?? BotRunes.LeastChance;
        BotRunes.Tries = settings.Tries ?? BotRunes.Tries;
        BotRunes.Blanks = settings.Blanks ?? BotRunes.Blanks;
        BotRunes.Far = settings.Far ?? BotRunes.Far;
        BotRunes.LeastSaved = settings.LeastSaved ?? BotRunes.LeastSaved;
        BotRunes.GateNear = settings.GateNear ?? BotRunes.GateNear;
        BotPassage.CrossMs = settings.CrossSeconds is { } cross ? (int)(cross * 1000) : BotPassage.CrossMs;
    }

    public override void Reset()
    {
        logger.Information("Runes, before the reload: {Runes}", BotRunes.Describe());

        _timer?.Stop();
        _timer = null;

        BotRunes.Forget();
        BotRuneShelf.Forget();
    }
}
