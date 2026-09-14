using Server.BotAI.V2;
using Server.Logging;

namespace Server.BotAI.Mind;

/// <summary>
/// Two thinking bots, as a module of the shard's own bot system — registered from outside it.
///
/// <para>
/// <b>Nothing in BotAIv2 was touched to make this exist, and that is the requirement rather than a
/// nicety.</b> The seam was already there: modules register themselves, proposers offer themselves, and the
/// server loads whatever <c>Data/assemblies.json</c> names. So this assembly references BotAIv2, BotAIv2
/// references nothing of this, and switching the whole thing off is one line in a file — with the population
/// carrying on exactly as it did, because everything the two of them do is the population's own work.
/// </para>
///
/// <para>
/// <see cref="BotPhase.World"/> and after <c>Population</c> and <c>Will</c>: there is no body to think for
/// until the population has raised one, and nothing to offer work into until the auction exists.
/// </para>
/// </summary>
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
        BotMinds.Stop();
        BotOllama.Forget();
    }
}
