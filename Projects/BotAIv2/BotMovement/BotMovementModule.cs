using Server.Logging;

namespace Server.BotAI.V2;

/// <summary>
/// Movement as a module: reads its numbers, lets the population walk, and puts its counters back on a
/// world reload.
///
/// <para>
/// <see cref="BotPhase.World"/>, because everything under it asks the map questions — which tile has a
/// floor, what is standing on it, where the doors are — and none of that can be answered before the world
/// is in memory. Requires nothing: a path does not care what class the bot is.
/// </para>
///
/// <para>
/// <b>Its switch is the useful one.</b> Turned off, every bot stands where it is and everything else goes
/// on running. Half the first version's investigations were the same question — is this navigation, or is
/// navigation covering for something else? — and the honest answer took hours of watching a live shard.
/// Four of the things eventually found underneath it were not navigation at all: bots blocking each other
/// in a doorway, a stuck-detector that punished progress, a caster whose own spell read as a wall, and a
/// bot at a counter being fined for standing at a counter.
/// </para>
/// </summary>
public sealed class BotMovementModule : BotModule
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotMovementModule));

    public override string Name => "Movement";

    public override BotPhase Phase => BotPhase.World;

    public override void Start()
    {
        BotMovementConfig.Load();

        BotWalk.Walking = true;

        // Every number that decides behaviour is in this line, because the config file silently wins over the
        // code and a threshold nobody can read is a threshold nobody can argue with.
        logger.Information(
            "Movement ready: one search may cost {Ceiling}ms, the population {Window}ms a second, floor {Floor}ms; a plan is trusted {Stale}ms and a journey is given up after {Stall} fruitless attempts at stepping or {NoCloser} plans that get no closer; after {FarSide} of those the far side of the destination is looked at, at most every {Gap}ms, for a pocket of up to {Cells} tiles costing at most {Look}ms",
            BotPath.CeilingMs,
            BotPath.WindowMs,
            BotPath.FloorMs,
            BotJourney.PlanStaleMs,
            BotJourney.StallAttempts,
            BotJourney.MaxPlansWithoutCloser,
            BotWalk.PlansBeforeAskingTheFarSide,
            BotPath.EnclosureGapMs,
            BotPath.EnclosureCells,
            BotPath.EnclosureCeilingMs
        );
    }

    /// <summary>
    /// A world reload is a different world. The reach ledger describes ground that may not be there any
    /// more, and the counters describe a population that is about to be rebuilt.
    /// </summary>
    public override void Reset()
    {
        BotWalk.Walking = false;

        logger.Information("Movement, before the reload: {Paths}; {Walk}; {Reach}",
            BotPath.Describe(),
            BotWalk.Describe(),
            BotReach.Describe()
        );

        BotPath.Reset();
        BotWalk.Reset();
        BotReach.Reset();
    }

    /// <summary>Everything the summary wants to say about getting about, in three clauses.</summary>
    public static string Summarise() => $"{BotPath.Describe()}; {BotWalk.Describe()}; {BotReach.Describe()}";
}
