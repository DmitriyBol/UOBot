using Server.BotAI.V2;
using Server.Logging;

namespace Server.BotAI.Mind;

public sealed class BotMindModule : BotModule
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotMindModule));

    public override string Name => "Mind";

    public override BotPhase Phase => BotPhase.World;

    public override string[] Requires => ["Population", "Will", "Classes"];

    public override void Start()
    {
        BotMindConfig.Load();

        BotMinds.Start();

        BotWebHooks.Minds = () => new BotMindsReport(
            BotOllama.Model,
            BotOllama.Endpoint,
            BotMinds.All.Count,
            BotOllama.Reachable,
            BotOllama.Asked,
            BotOllama.Answered,
            BotOllama.Refused,
            BotOllama.LastMs
        );

        BotStoryVoice.Open();

        if (BotMinds.All.Count == 0)
        {
            return;
        }

        BotWill.Offer(new BotMindProposer());

        logger.Information(
            "{Count} minds are awake on {Model} at {Endpoint}: {Who}; asked to choose every {Think}ms while free, reckoning up at most every {Review}ms, holding {Lessons} rules each ({PerTrade} of them about any one trade) and asking with a weight of {Insistence:F2}; {Embodied} of them have bodies. Their thinking is written to {Log}",
            BotMinds.All.Count,
            BotOllama.Model,
            BotOllama.Endpoint,
            Who(),
            BotMind.ThinkEveryMs,
            BotMind.ReviewEveryMs,
            BotMind.MostLessons,
            BotMind.MostPerTrade,
            BotMindDeed.Insistence,
            BotMinds.Embodied,
            BotMindLog.Path ?? "nowhere"
        );
    }

    private static string Who()
    {
        var minds = BotMinds.All;
        var names = new string[minds.Count];

        for (var i = 0; i < minds.Count; i++)
        {
            names[i] = $"{minds[i].Name} the {minds[i].Trade}";
        }

        return string.Join(", ", names);
    }

    public static string Summarise() => $"{BotMinds.Describe()}; {BotOllama.Describe()}";

    public override void Reset()
    {
        BotStoryVoice.Close();
        BotMinds.Stop();
        BotOllama.Forget();
    }
}
