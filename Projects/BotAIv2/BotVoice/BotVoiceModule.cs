using System;
using Server.Logging;

namespace Server.BotAI.V2;

/// <summary>
/// The voices as a module: reads the file, opens the speech log, and hooks the occasions.
///
/// After the population and the will, because every occasion here is one of theirs. The mood clock runs on
/// its own timer, once every few seconds over the roster, and each bot decides for itself whether it is due.
/// </summary>
public sealed class BotVoiceModule : BotModule
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotVoiceModule));

    public static int TickMs { get; set; } = 5000;

    private static MoodTimer _timer;

    public override string Name => "Voice";

    public override BotPhase Phase => BotPhase.World;

    public override string[] Requires => ["Population", "Will"];

    public override void Start()
    {
        BotVoiceConfig.Load();

        if (!BotVoice.Enabled)
        {
            logger.Information("The bots are silent: bot-voice.json says Enabled is false");

            return;
        }

        BotVoice.Open();

        BotWill.Started += BotVoice.Took;
        BotWill.Ended += BotVoice.Ended;

        _timer ??= new MoodTimer();
        _timer.Start();

        logger.Information(
            "The bots speak: world {World}, guild {Guild}, local {Local}, mood {Mood}, cry {Cry}; one line per bot every {Every}s, a mood every {MoodMin} min, {Chance:P0} of work said aloud; the transcript is {Log}",
            BotVoice.WorldOn,
            BotVoice.GuildOn,
            BotVoice.LocalOn,
            BotVoice.MoodOn,
            BotVoice.CryOn,
            BotVoice.SayEveryMs / 1000,
            BotVoice.MoodEveryMs / 60000,
            BotVoice.WorkChance,
            BotVoice.LogPath ?? "nowhere"
        );
    }

    public override void Reset()
    {
        BotVoice.Forget();
    }

    private static void Tick()
    {
        if (!BotVoice.Enabled || !BotVoice.MoodOn)
        {
            return;
        }

        var now = Core.TickCount;
        var bots = BotPopulation.Bots;

        for (var i = 0; i < bots.Count; i++)
        {
            var bot = bots[i];

            if (bot is { Deleted: false, Alive: true } && bot.Map != Map.Internal)
            {
                BotVoice.Mood(bot, now);
            }
        }
    }

    private sealed class MoodTimer : Timer
    {
        public MoodTimer() : base(TimeSpan.FromMilliseconds(TickMs), TimeSpan.FromMilliseconds(TickMs))
        {
        }

        protected override void OnTick() => Tick();
    }
}
