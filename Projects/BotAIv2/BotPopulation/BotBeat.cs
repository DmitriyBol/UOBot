using System;
using System.Diagnostics;
using Server.Logging;

namespace Server.BotAI.V2;

/// <summary>
/// The population's clock. One timer for everybody, and each bot due on its own schedule.
///
/// <para>
/// <b>The pace is the movement subsystem's own number, not a knob here, and that is the fix for the one
/// defect the first version's ticker had.</b> There, the period was <c>interval × phases</c> — two numbers
/// in a config file whose product nobody checked — and it shipped at 800ms against a walking step of 400ms.
/// The bots were not stuck and nothing in the log looked wrong; they simply moved at half a pedestrian's
/// pace for the entire session, because a bot cannot step more often than it is asked to. Here a bot's next
/// turn is set from <see cref="BotWalk.StepDelayMs"/> when it takes one, so the beat cannot be slower than a
/// step by construction. There is no product to get wrong.
/// </para>
///
/// <para>
/// The timer's own interval is therefore only the resolution: how finely due times can be honoured. It is
/// far shorter than a step, so what it costs is one pass over the population per tick — a hundred and fifty
/// comparisons — and what it buys is that nobody is ever late by more than that interval.
/// </para>
///
/// <para>
/// <b>Spread comes from the due times, not from phases.</b> Bots are seeded staggered across one step's
/// worth of turns when they are born, so the work of a step lands evenly across ticks instead of all of it
/// landing on one. That is what phases were for; done this way it needs no configuration and cannot be set
/// to a value that throttles movement.
/// </para>
/// </summary>
public static class BotBeat
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotBeat));

    public static int IntervalMs { get; set; } = 100;

    private static BeatTimer _timer;

    public static long Ticks { get; private set; }

    public static long Turns { get; private set; }

    public static long Faults { get; private set; }

    public static bool Running => _timer != null;

    public static void Start()
    {
        if (_timer != null)
        {
            return;
        }

        var interval = TimeSpan.FromMilliseconds(Math.Max(10, IntervalMs));

        _timer = new BeatTimer(interval);
        _timer.Start();

        logger.Information(
            "The population's clock is running: looked at every {Interval}ms, each bot taking a turn every {Step}ms",
            IntervalMs,
            BotWalk.StepDelayMs(BotMobile.Runs)
        );
    }

    public static void Stop()
    {
        _timer?.Stop();
        _timer = null;
    }

    public static void Reset()
    {
        Stop();

        Ticks = 0;
        Turns = 0;
        Faults = 0;
    }

    public static int SummaryMs { get; set; } = 300000;

    private static bool _summarised;

    private static long _summaryTick;

    private static void Summarise(long now)
    {
        if (!_summarised)
        {
            _summarised = true;
            _summaryTick = now;

            return;
        }

        if (now - _summaryTick < SummaryMs)
        {
            return;
        }

        _summaryTick = now;

        logger.Information("Getting about: {Paths}; {Walk}; {Reach}; {Refused}; {Footing}", BotPath.Describe(), BotWalk.Describe(), BotReach.Describe(), BotRefused.Describe(), BotFooting.Describe());

        logger.Information("The market: {What}", BotAuction.Describe());

        logger.Information("What we know: {What}", BotCommons.Describe());

        logger.Information("Money: {What}", BotPurse.Describe());

        logger.Information("City: {What}", BotCity.Describe());

        logger.Information("Quests: {What}", BotQuests.Describe());

        logger.Information("Kit: {What}", BotTidy.Describe());

        logger.Information("The island: {What}; {Hunting}; {Kept}", BotQuad.Describe(), BotHunter.Describe(), BotKept.Describe());

        logger.Information("At death's door: {What}; {Supplies}", BotMobile.DescribeGasps(), BotShopper.Describe());

        logger.Information("Standing still: {What}; {Home}", BotStall.Describe(), BotHomer.Describe());

        logger.Information(
            "Gathering: {Forage}; {Herbs}; {Pickings}; {Outfit}",
            BotForager.Describe(),
            BotHerbalist.Describe(),
            BotPicker.Describe(),
            BotOutfit.Describe()
        );

        logger.Information("The board: {What}", BotAuction.Board());

        logger.Information(
            "Spoils: {Chests}; {Prisoners}; {Plunderer}; {Liberator}",
            BotPlunder.Describe(),
            BotFreedom.Describe(),
            BotPlunderer.Describe(),
            BotLiberator.Describe()
        );

        logger.Information(
            "Trade: {Shops}; {Peddling}; {Quarry}; {Standing}",
            BotShops.Describe(),
            BotPeddler.Describe(),
            BotQuarry.Describe(),
            BotPopulation.Describe()
        );

        logger.Information("Rest: {Rest}; {Growth}", BotRest.Describe(), BotGrowth.Describe());

        logger.Information("Lessons: {Lessons}", BotTutor.Describe());

        logger.Information("Guilds: {What}", BotGuilds.Describe());

        logger.Information("Estate: {What}", BotEstate.Describe());

        logger.Information("Wars: {Wars}; {Seats}", BotWar.Describe(), BotSeat.Describe());

        logger.Information("The clock: {What}", Describe());
    }

    public static double SpentMs { get; private set; }

    public static double WorstMs { get; private set; }

    private static void Tick()
    {
        Ticks++;

        var began = Stopwatch.GetTimestamp();

        try
        {
            Look(began);
        }
        finally
        {
            var ms = (Stopwatch.GetTimestamp() - began) * 1000.0 / Stopwatch.Frequency;

            SpentMs += ms;

            if (ms > WorstMs)
            {
                WorstMs = ms;
            }
        }
    }

    public static double SlowMs { get; set; } = 100.0;

    public static long Slow { get; private set; }

    private static long _slowSaidTick;

    private static double Since(long mark) => (Stopwatch.GetTimestamp() - mark) * 1000.0 / Stopwatch.Frequency;

    private static void Look(long began)
    {

        var bots = BotPopulation.Bots;
        var now = Core.TickCount;

        var mark = Stopwatch.GetTimestamp();
        var worstMs = 0.0;
        string worst = null;

        void Segment(string name)
        {
            var ms = Since(mark);

            if (ms > worstMs)
            {
                worstMs = ms;
                worst = name;
            }

            mark = Stopwatch.GetTimestamp();
        }

        Summarise(now);
        Segment("the summary");

        BotMarkers.Tick();
        Segment("the pins");

        BotRegard.Drift();
        Segment("the drift of opinions");

        BotClaim.Look();
        Segment("the claims");

        BotToll.Look();
        Segment("the tolls");

        BotWar.Beat();
        BotFeud.Watch();
        Segment("the wars");

        BotGuilds.Review();

        BotGuilds.Gather();
        Segment("the guild reviews");

        BotShops.Keep();
        Segment("the shops");

        var turnWorstMs = 0.0;
        BotMobile turnWorst = null;

        for (var i = 0; i < bots.Count; i++)
        {
            var bot = bots[i];

            if (bot == null || bot.Deleted)
            {
                continue;
            }

            if (bot.Scheduled && now - bot.DueTick < 0)
            {
                continue;
            }

            bot.Scheduled = true;

            BotStall.Look(bot);

            bot.DueTick = now + BotWalk.StepDelayMs(bot, bot.Running);

            Turns++;

            var turnMark = Stopwatch.GetTimestamp();

            try
            {
                if (bot.Fallen)
                {
                    BotPopulation.Revive(bot);

                    continue;
                }

                bot.Beat();
            }
            catch (Exception e)
            {
                Faults++;

                logger.Error(e, "{Name} threw on its turn; the rest of the population carries on", bot.Name);
            }
            finally
            {
                var turnMs = Since(turnMark);

                if (turnMs > turnWorstMs)
                {
                    turnWorstMs = turnMs;
                    turnWorst = bot;
                }
            }
        }

        Segment("the bots' turns");

        var total = (Stopwatch.GetTimestamp() - began) * 1000.0 / Stopwatch.Frequency;

        if (total < SlowMs)
        {
            return;
        }

        Slow++;

        if (_slowSaidTick != 0 && now - (_slowSaidTick + SummaryMs) < 0)
        {
            return;
        }

        _slowSaidTick = now;

        logger.Information(
            "A slow tick: {Total:F0}ms, {WorstMs:F0}ms of it in {Worst}; the costliest turn was {Bot}'s at {TurnMs:F0}ms on {Deed}; {Slow} such ticks so far",
            total,
            worstMs,
            worst ?? "nothing in particular",
            turnWorst?.Name ?? "nobody",
            turnWorstMs,
            turnWorst?.Resolve?.Deed?.Kind ?? "nothing",
            Slow
        );
    }

    public static string Describe() =>
        $"{Ticks} looks, {Turns} turns handed out, {Faults} faults; every {IntervalMs}ms, a turn each every {BotWalk.StepDelayMs(BotMobile.Runs)}ms; "
        + $"{SpentMs:F0}ms of the loop in all ({(Ticks > 0 ? SpentMs / Ticks : 0.0):F2}ms a look, worst {WorstMs:F1}ms), of which deciding {BotWill.SpentMs:F0}ms over {BotWill.Decisions} turns and walking {BotWalk.SpentMs:F0}ms";

    private sealed class BeatTimer : Timer
    {
        public BeatTimer(TimeSpan interval) : base(interval, interval)
        {
        }

        protected override void OnTick() => Tick();
    }
}
