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

        BotVigil.Start();
        BotHail.Listen();
        BotConsole.Open();

        // The numbers it is actually running with, said once, because a belief about what the debugger is
        // watching for that was formed from the defaults in the source can be wrong by a factor of two
        // without anything looking odd. This shard's rule, and it applies to the thing that enforces it too.
        logger.Information(
            "{Name} the debugger is watching, thinking with {Model} (the population thinks with something else) held for {Hold} after each answer: measuring every {Sample}ms, moving every {Hover}ms, reporting every {Report}ms and reflecting every {Reflect}ms. Frozen after {Frozen}s of standing while walking, silent work after {Silent}m, no-progress judged after {Settled}m. Its thinking goes to {Log}",
            BotVigil.Name,
            BotVigil.Model,
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
    }

    /// <summary>What it has done, for whoever is reading the session log.</summary>
    public static string Summarise() => BotVigil.Describe();

    public override void Reset()
    {
        BotHail.Forget();
        BotHail.Reset();
        BotVigil.Reset();
    }
}
