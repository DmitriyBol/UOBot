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

        BotDials.Open();

        BotVigil.Start();
        BotHail.Listen();
        BotConsole.Open();

        BotAppraisal.Revelry = BotRevel.Worth;

        BotCrier.Posted = BotRevel.Notice;

        BotCrier.Watchers = BotVigil.Standing;
        BotWill.Completed = BotRevel.Did;

        BotHalls.Noted = BotDebugLog.Write;

        BotWitness.Open();

        BotZoneTour.Walkers = BotVigil.Walkers;
        BotZoneTour.Move = BotVigil.Carry;

        _timer?.Stop();
        _timer = new VigilSummaryTimer(TimeSpan.FromMilliseconds(SayEveryMs));
        _timer.Start();

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

        logger.Information(
            "It may also declare a revel every {Every}m: one trade worth ×{Bonus} for {Holds}m with a prize up to {Prize}gp, out of a treasury of {Treasury}gp for the session",
            BotRevel.EveryMs / 60000,
            BotRevel.Bonus,
            BotRevel.HoldsMs / 60000,
            BotRevel.MostPrize,
            BotRevel.Treasury
        );

        if (BotMarshal.Standing != null)
        {
            logger.Information(
                "{Marshal} is the marshal of events among them, asked every {Every}m for a revel, a camp, a tournament, an errand on the board, a standing order, a bounty, a price on a head or a fair; the revels are its business while it stands",
                BotMarshal.Name,
                BotMarshal.EveryMs / 60000
            );
        }
    }

    public static int SayEveryMs { get; set; } = 300000;

    private static Timer _timer;

    private sealed class VigilSummaryTimer : Timer
    {
        public VigilSummaryTimer(TimeSpan interval) : base(interval, interval)
        {
        }

        protected override void OnTick() => logger.Information("The debugger: {What}", Summarise());
    }

    public static string Summarise() => $"{BotVigil.Describe()}; {BotRevel.Describe()}; {BotWitness.Describe()}";

    public override void Reset()
    {
        BotAppraisal.Revelry = null;
        BotCrier.Posted = null;
        BotCrier.Watchers = null;
        BotWill.Completed = null;
        BotHalls.Noted = null;
        BotZoneTour.Walkers = null;
        BotZoneTour.Move = null;
        BotWitness.Close();
        BotWitness.Forget();
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
