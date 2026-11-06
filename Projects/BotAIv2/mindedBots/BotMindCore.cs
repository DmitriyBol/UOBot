using Server.BotAI.V2;
using Server.Logging;

namespace Server.BotAI.Mind;

public static class BotMindCore
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotMindCore));

    public static bool Enabled { get; private set; }

    public static void Configure()
    {
        Enabled = ServerConfiguration.GetOrUpdateSetting("bots.mind.thinking", true);

        if (!Enabled)
        {
            return;
        }

        if (!BotCore.Enabled)
        {
            Enabled = false;

            logger.Information("Thinking bots are switched on but the bot system is not, so nothing is registered");

            return;
        }

        BotModules.Register(new BotMindModule());
    }

    public static void Initialize()
    {
        if (!Enabled)
        {
            logger.Information("BotMindAI loaded (disabled): the fifteen carry on without anybody thinking about it");

            return;
        }

        logger.Information("BotMindAI loaded (enabled): two of the population will be thought about, the rest are unchanged");
    }
}
