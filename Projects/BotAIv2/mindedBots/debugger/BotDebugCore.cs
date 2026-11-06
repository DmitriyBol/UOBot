using Server.BotAI.V2;
using Server.Logging;

namespace Server.BotAI.Mind;

public static class BotDebugCore
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotDebugCore));

    public static bool Enabled { get; private set; }

    public static void Configure()
    {
        Enabled = ServerConfiguration.GetOrUpdateSetting("bots.debugger.enabled", true);

        if (!Enabled)
        {
            return;
        }

        if (!BotCore.Enabled)
        {
            Enabled = false;

            logger.Information("The debugger is switched on but the bot system is not, so there is nobody to watch");

            return;
        }

        BotModules.Register(new BotDebugModule());

        BotTourneyStore.Configure();
    }

    public static void Initialize()
    {
        logger.Information(
            Enabled
                ? "The debugger is loaded: one invisible watcher, no work, no wage and no opinions about anybody's trade"
                : "The debugger is loaded but switched off; nothing is watching the population"
        );
    }
}
