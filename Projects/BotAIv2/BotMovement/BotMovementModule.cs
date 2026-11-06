using System;
using Server.Logging;

namespace Server.BotAI.V2;

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
            "Movement ready: a search is charged {PerTile}ms a tile of distance, never less than {Short}ms and never more than {Ceiling}ms, the population {Window}ms a second, floor {Floor}ms; a plan is trusted {Stale}ms and a journey is given up after {Stall} fruitless attempts at stepping or {NoCloser} plans that get no closer (a reached plan after somebody about its own business starting that count again: {KeepUp}); after {FarSide} of those the far side of the destination is looked at, at most every {Gap}ms, for a pocket of up to {Cells} tiles costing at most {Look}ms",
            BotPath.MsPerTile,
            BotPath.ShortMs,
            BotPath.CeilingMs,
            BotPath.WindowMs,
            BotPath.FloorMs,
            BotJourney.PlanStaleMs,
            BotJourney.StallAttempts,
            BotJourney.MaxPlansWithoutCloser,
            BotJourney.KeepUp,
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

        logger.Information(
            "Sight {State}: each bot looks every {Every}ms within {Tiles} tiles; a creature that attacks on sight and comes, at its kind's price, past ×{Odds} what the bot brings is gone round at its reach plus {Margin} tiles (+{Step} a step in a plan, +{Route} tiles a node of a route), waited out {Wait}ms and then turned back from; work left standing {Standing}",
            BotSight.Running ? "on" : "off",
            BotSight.EveryMs,
            BotSight.SightTiles,
            BotSight.Odds,
            BotSight.Margin,
            BotSight.StepCost,
            BotDanger.SightCost,
            BotSight.WaitMs,
            BotSight.Standing ? "on" : "off"
        );

        BotBarred.Load();
        BotDanger.Hook();
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
        BotGates.Forget();
        BotSight.Reset();
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
