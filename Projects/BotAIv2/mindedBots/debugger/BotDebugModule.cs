using System;
using Server.BotAI.V2;
using Server.Logging;

namespace Server.BotAI.Mind;

/// <summary>
/// The debugger as a module of the shard's own bot system, registered from outside it.
///
/// <para>
/// <see cref="BotPhase.World"/>, and after <c>Population</c>: there is nobody to watch until the population
/// has been raised, and the debugger's body is placed relative to where the population lives. It does not
/// require <c>Will</c> — a population that decides nothing is a legitimate state of the shard, and it is
/// precisely the state somebody would most want a debugger for.
/// </para>
///
/// <para>
/// <b>It offers nothing into the auction and nothing offers anything to it.</b> Every other module here
/// either proposes work or answers for a bot's body; this one only reads. That is what lets it be switched
/// off with no effect on anything — and switched on with no effect either, which is the harder half and the
/// reason the observer is not a <c>BotMobile</c>. See <see cref="BotDebugger"/>.
/// </para>
/// </summary>
public sealed class BotDebugModule : BotModule
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotDebugModule));

    public override string Name => "Debugger";

    public override BotPhase Phase => BotPhase.World;

    public override string[] Requires => ["Population"];

    public override void Start()
    {
        BotDebugConfig.Load();
        BotDebugMemory.Load();
        BotDebugLog.Open(BotVigil.Name);
        BotHand.Open(BotVigil.Name);

        // After every other module has loaded its configuration, which is what makes the values this reads
        // the ones the shard is actually running with rather than the ones the source is written with.
        BotDials.Open();

        BotVigil.Start();
        BotHail.Listen();
        BotConsole.Open();

        // The two seams through which a revel touches the population. Both live in BotAIv2 and are filled
        // from here, never called from there: the minds depend on the bots and must not be depended on. See
        // BotAppraisal.Revelry.
        BotAppraisal.Revelry = BotRevel.Worth;

        // The other half of the same seam: the price goes out through Revelry, and what the notice says goes
        // out through here, so the dashboard can show a revel without this assembly being referenced back.
        BotCrier.Posted = BotRevel.Notice;
        BotWill.Completed = BotRevel.Did;

        _timer?.Stop();
        _timer = new VigilSummaryTimer(TimeSpan.FromMilliseconds(SayEveryMs));
        _timer.Start();

        // The numbers it is actually running with, said once, because a belief about what the debugger is
        // watching for that was formed from the defaults in the source can be wrong by a factor of two
        // without anything looking odd. This shard's rule, and it applies to the thing that enforces it too.
        // Whether anything else is thinking is read rather than asserted. The line used to say "the
        // population thinks with something else", which stopped being true the moment the minds were stood
        // down — and a fact that goes stale in the first line anybody reads is worse than no fact.
        logger.Information(
            "{Name} the debugger is watching, thinking with {Model} ({Others}) held for {Hold} after each answer: measuring every {Sample}ms, moving every {Hover}ms, reporting every {Report}ms and reflecting every {Reflect}ms. Frozen after {Frozen}s of standing while walking, silent work after {Silent}m, no-progress judged after {Settled}m. Its thinking goes to {Log}",
            BotVigil.Name,
            BotVigil.Model,
            BotMinds.All.Count == 0
                ? "nothing else on this shard is thinking, so the card is its own"
                : $"the population's {BotMinds.All.Count} minds think with something else",
            BotVigil.KeepAlive,
            BotVigil.SampleMs,
            BotVigil.HoverMs,
            BotVigil.ReportMs,
            BotVigil.ReflectMs,
            BotWatch.FrozenMs / 1000,
            BotWatch.ImmortalMs / 60000,
            BotWatch.SettledMs / 60000,
            BotDebugLog.Path ?? "nowhere"
        );

        // What it may do to the population as well as what it watches for. A revel moves prices and puts
        // camps on the ground, so the numbers behind it belong in a startup line like every other threshold
        // on this shard.
        logger.Information(
            "It may also declare a revel every {Every}m: one trade worth ×{Bonus} for {Holds}m with a prize up to {Prize}gp, out of a treasury of {Treasury}gp for the session",
            BotRevel.EveryMs / 60000,
            BotRevel.Bonus,
            BotRevel.HoldsMs / 60000,
            BotRevel.MostPrize,
            BotRevel.Treasury
        );
    }

    /// <summary>
    /// How often the debugger says what it has been doing, in its own line in the shard's log.
    ///
    /// <para>
    /// <b>It never said any of it.</b> <see cref="Summarise"/> has existed since the debugger did, composing
    /// a line that carries the only numbers by which this observer can be judged - how many bots it reminded
    /// and shook, and of those, how many were going again by the next window. Nothing called it. The Baron
    /// and the captain print theirs on a clock; the thing whose whole purpose is measurement printed nothing,
    /// so eleven hundred interventions accumulated with no record anywhere of whether one of them helped.
    /// A number nobody prints is a number nobody has.
    /// </para>
    /// </summary>
    public static int SayEveryMs { get; set; } = 300000;

    private static Timer _timer;

    private sealed class VigilSummaryTimer : Timer
    {
        public VigilSummaryTimer(TimeSpan interval) : base(interval, interval)
        {
        }

        protected override void OnTick() => logger.Information("The debugger: {What}", Summarise());
    }

    /// <summary>What it has done, for whoever is reading the session log.</summary>
    public static string Summarise() => $"{BotVigil.Describe()}; {BotRevel.Describe()}";

    public override void Reset()
    {
        // The population is about to be replaced, so nothing it did counts towards a contest.
        BotAppraisal.Revelry = null;
        BotCrier.Posted = null;
        BotWill.Completed = null;
        BotRevel.Forget();
        BotWaves.Forget();

        logger.Information("The debugger, before the reload: {What}", Summarise());

        _timer?.Stop();
        _timer = null;

        BotHail.Forget();
        BotHail.Reset();
        BotVigil.Reset();
    }
}
