using System;
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

    private static RoadsTimer _roads;

    public override string Name => "Movement";

    public override BotPhase Phase => BotPhase.World;

    public override void Start()
    {
        BotMovementConfig.Load();

        BotWalk.Walking = true;

        logger.Information(
            "Movement ready: a search is charged {PerTile}ms a tile of distance, never less than {Short}ms and never more than {Ceiling}ms, the population {Window}ms a second, floor {Floor}ms; a plan is trusted {Stale}ms and a journey is given up after {Stall} fruitless attempts at stepping or {NoCloser} plans that get no closer; after {FarSide} of those the far side of the destination is looked at, at most every {Gap}ms, for a pocket of up to {Cells} tiles costing at most {Look}ms",
            BotPath.MsPerTile,
            BotPath.ShortMs,
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

        logger.Information(
            "Refused ground: one entry per {Grain} tiles, resting {RestMs}ms doubling to {MostRestMs}ms, at most {MostPlaces} squares remembered; arriving anywhere clears one",
            BotRefused.Grain,
            BotRefused.RestMs,
            BotRefused.MostRestMs,
            BotRefused.MostPlaces
        );

        BotBarred.Announce();

        _roads?.Stop();
        _roads = new RoadsTimer(TimeSpan.FromMilliseconds(Math.Max(10, BotRoads.SliceEveryMs)));
        _roads.Start();
    }

    public override void Reset()
    {
        BotWalk.Walking = false;

        logger.Information("Movement, before the reload: {Paths}; {Walk}; {Reach}; {Refused}; {Roads}",
            BotPath.Describe(),
            BotWalk.Describe(),
            BotReach.Describe(),
            BotRefused.Describe(),
            BotRoads.Describe()
        );

        _roads?.Stop();
        _roads = null;

        BotPath.Reset();
        BotWalk.Reset();
        BotReach.Reset();
        BotRefused.Forget();
        BotRoads.Forget();
        BotChart.Forget();
    }

    public static string Summarise() =>
        $"{BotPath.Describe()}; {BotWalk.Describe()}; {BotReach.Describe()}; {BotRefused.Describe()}; {BotChart.Describe()}";

    private sealed class RoadsTimer : Timer
    {
        public RoadsTimer(TimeSpan interval) : base(interval, interval)
        {
        }

        protected override void OnTick()
        {
            if (!BotRoads.Ready && !BotRoads.Failed)
            {
                BotRoads.Slice();

                return;
            }

            if (BotRoads.Failed || !BotChart.Running || BotChart.Slice())
            {
                Stop();
            }
        }
    }
}
