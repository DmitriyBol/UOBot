using System;
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

    /// <summary>
    /// How often the timer looks at the population. The resolution of the schedule, not the pace of a bot.
    ///
    /// A tenth of a second: fine enough that a bot due for a step takes it within a quarter of the step's
    /// own delay, coarse enough that the pass costs nothing worth measuring.
    /// </summary>
    public static int IntervalMs { get; set; } = 100;

    private static BeatTimer _timer;

    /// <summary>How many times the clock has looked, and how many turns it has handed out.</summary>
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

    /// <summary>
    /// Everybody who is due. Bots are visited by index over the live list and deleted ones leave holes
    /// rather than shifting it — see <see cref="BotPopulation.Forget"/> — because a bot can be deleted by
    /// its own turn, and a list that shifts underneath the loop skips whoever moved into the gap.
    /// </summary>
    /// <summary>How often the cost of getting about is reported. The same cadence as the decision census.</summary>
    public static int SummaryMs { get; set; } = 300000;

    private static bool _summarised;

    private static long _summaryTick;

    /// <summary>
    /// What movement has cost lately, said out loud on a clock.
    ///
    /// <para>
    /// <b>These numbers existed and were never printed except on a world reload</b>, which is to say never —
    /// so the one budget the whole population shares was invisible while it was being spent. That is how a
    /// speculative reachability check in a proposer could quietly starve every walking bot on the shard:
    /// searches were shrinking to the floor and coming back with nothing, and the only symptom anybody could
    /// see was bots failing to get anywhere.
    /// </para>
    /// </summary>
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

        logger.Information("Getting about: {Paths}; {Walk}; {Reach}", BotPath.Describe(), BotWalk.Describe(), BotReach.Describe());

        // <b>The market was in the same position these were, and for longer.</b> Its own summary existed and
        // went to two places: a gump nobody has open at four in the morning, and the world reload. So the one
        // board the whole population trades on was invisible for a whole night, and how full it was could only
        // be inferred from the errors it printed when it overflowed. Said here rather than on the market's own
        // beat because that beat is every thirty seconds and this is a report, not a heartbeat.
        logger.Information("The market: {What}", BotAuction.Describe());

        // What the population knows between it. On the fifth tab in full; here because a thing that can only
        // be seen by opening a window is a thing nobody sees at four in the morning, which is the lesson the
        // market's own summary taught two hours ago.
        logger.Information("What we know: {What}", BotCommons.Describe());

        // <b>The same lesson a third time, and this one was worse: BotPurse.Describe had no caller at all.</b>
        // Not printed on a reload, not on a gump, not anywhere — a method that existed and was dead. Meanwhile
        // three subsystems spent the afternoon refusing to buy things for want of money and no line on this
        // shard said how much money there was.
        logger.Information("Money: {What}", BotPurse.Describe());

        // The island's own memory of itself, on the same clock as everything else that is read rather than
        // watched. See BotQuad: this is the shard's standing opinion of its ground, and the tab that shows it
        // is a tab nobody has open at four in the morning.
        logger.Information("The island: {What}; {Hunting}", BotQuad.Describe(), BotHunter.Describe());

        logger.Information("At death's door: {What}; {Supplies}", BotMobile.DescribeGasps(), BotShopper.Describe());

        logger.Information("Standing still: {What}", BotStall.Describe());




        // <b>Four more of the same, found by asking the question of the whole assembly at once.</b> A Describe
        // with no caller anywhere is the commonest defect in this project by count — BotPurse, BotGround, the
        // market and the commons had all been in this state — so the assembly was searched for the shape
        // rather than for the next instance of it. These four came back: what the woods and the ground give
        // up, what a bot picks off the things it kills, and what it was handed when it was raised.
        logger.Information(
            "Gathering: {Forage}; {Herbs}; {Pickings}; {Outfit}",
            BotForager.Describe(),
            BotHerbalist.Describe(),
            BotPicker.Describe(),
            BotOutfit.Describe()
        );

        // <b>And three more behind a second wall: a live Describe wrapped in a dead Summarise.</b> Eight
        // modules each carry a facade that gathers their own summaries into one line, and not one of those
        // facades has a caller — so a Describe that looked called from a search was reachable only through a
        // method nothing runs. What that hid is the other half of the money question: what the population
        // buys and sells. "The population has 20903gp" and "the population bought nothing all afternoon" are
        // the two halves of the same answer, and only the first of them was ever printed.
        logger.Information(
            "Trade: {Shops}; {Peddling}; {Quarry}; {Standing}",
            BotShops.Describe(),
            BotPeddler.Describe(),
            BotQuarry.Describe(),
            BotPopulation.Describe()
        );
    }

    private static void Tick()
    {
        Ticks++;

        var bots = BotPopulation.Bots;
        var now = Core.TickCount;

        Summarise(now);

        // <b>The pins, on the population's fast beat rather than on its five-minute summary.</b> They were
        // asked from Summarise, which meant "rewrite every minute" could never mean anything of the sort —
        // the soonest it could fire was five. BotMarkers keeps its own throttle, so this costs a subtraction
        // per tick and delivers the interval it actually names.
        BotMarkers.Tick();

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

            // Whether this bot is getting anywhere at all. See BotStall: standing still is this shard's most
            // expensive defect and the only one that never wrote a line of its own.
            BotStall.Look(bot);

            // Each bot's own pace rather than the population's. One that has run itself out of breath walks,
            // and a walk is four hundred milliseconds against a run's two hundred; scheduling it as though it
            // were still running asks it to move twice as often as the engine will allow.
            bot.DueTick = now + BotWalk.StepDelayMs(bot.Running);

            Turns++;

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
                // Loud and contained: one bot's mistake must not take the population's clock with it, and it
                // must not be quiet either. A silent exception here stops every bot, which reads as a brain
                // that has stopped deciding.
                Faults++;

                logger.Error(e, "{Name} threw on its turn; the rest of the population carries on", bot.Name);
            }
        }
    }

    public static string Describe() =>
        $"{Ticks} looks, {Turns} turns handed out, {Faults} faults; every {IntervalMs}ms, a turn each every {BotWalk.StepDelayMs(BotMobile.Runs)}ms";

    private sealed class BeatTimer : Timer
    {
        public BeatTimer(TimeSpan interval) : base(interval, interval)
        {
        }

        protected override void OnTick() => Tick();
    }
}
