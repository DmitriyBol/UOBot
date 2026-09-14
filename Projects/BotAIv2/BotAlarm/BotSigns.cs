using System;
using Server.Logging;

namespace Server.BotAI.V2;

/// <summary>
/// The rules that decide when the shard should speak up: read once a minute, each one comparing a number
/// the shard already keeps against a threshold, over a window it names.
///
/// <para>
/// <b>Six rules, not sixty, and every one of them was checked against a live shard before its threshold was
/// chosen.</b> A watchdog is the easiest thing in this project to get wrong, because a rule that fires
/// wrongly costs more than no rule at all: a night of finding out that the instrument was the fault. The
/// debugger produced ten false alarms against two real defects in its first day, so the thresholds here sit
/// far from what the running shard actually does — errors were 0.3 a minute when this was written and the
/// alarm is at five; four out of five pieces of work were finishing and the alarm is at one in four.
/// </para>
///
/// <para>
/// <b>Everything is a difference over a window, never a total.</b> The counters in this assembly only ever
/// grow, and reading one as the state of this minute is the single most common way this shard has lied to
/// itself: "848 could not afford armour" was true, and was about the first five minutes of a session that
/// had been running for hours. So each rule keeps an anchor — a value and the moment it was taken — and asks
/// its question of the difference.
/// </para>
///
/// <para>
/// <b>A rule that cannot state its denominator is not written.</b> Errors are counted against the minute
/// they were read in, unfinished work against the work taken on, the stuck against the population, deaths
/// against the population, the silent market against the stalls and wants that were standing at the time.
/// </para>
///
/// <para>
/// <b>The thresholds are dials, so they move without a restart.</b> That is the point of tuning a watchdog
/// on a living shard: the first version of a rule is wrong, the evidence for how wrong it is arrives over
/// the following hour, and stopping the shard to change a number would throw that hour away.
/// </para>
/// </summary>
public static class BotSigns
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotSigns));

    public static int TickMs { get; set; } = 60000;

    public static int ErrorsAt { get; set; } = 5;

    public static int WorkMs { get; set; } = 300000;

    public static int WorkLeast { get; set; } = 20;

    public static double WorkFloor { get; set; } = 0.55;

    public static double StandShare { get; set; } = 0.3;

    public static int StandLeast { get; set; } = 10;

    public static int MarketMs { get; set; } = 600000;

    public static int DeathsAt { get; set; } = 10;

    public static int AliveMs { get; set; } = 3600000;

    private static long _errorsAt;

    private static long _workAt;

    private static long _workTaken;

    private static long _workFinished;

    private static long _workDeaths;

    private static long _marketAt;

    private static long _marketSells;

    private static long _marketFills;

    private static long _aliveAt;

    private static long _aliveTaken;

    private static long _aliveFinished;

    private static long _aliveErrors;

    public static long Looks { get; private set; }

    public static void Start()
    {
        var now = Core.TickCount;

        _errorsAt = now;
        _workAt = now;
        _marketAt = now;
        _aliveAt = now;

        _workTaken = BotWill.Taken;
        _workFinished = BotWill.Finished;
        _workDeaths = BotWill.Deaths;

        _marketSells = BotAuction.Sells;
        _marketFills = BotAuction.Fills;

        _aliveTaken = BotWill.Taken;
        _aliveFinished = BotWill.Finished;
        _aliveErrors = BotTail.Errors;

        Looks = 0;
    }

    private static bool Due(long now, long anchor, int every) => now - anchor >= every - TickMs / 2;

    private static string Window(long now, long anchor)
    {
        var seconds = (now - anchor) / 1000;

        return seconds < 90 ? $"{seconds}s" : $"{(seconds + 30) / 60}m";
    }

    private static int Percent(double share) => (int)Math.Round(share * 100);

    public static void Look()
    {
        Looks++;

        var now = Core.TickCount;

        Errors();
        Standing();

        if (Due(now, _workAt, WorkMs))
        {
            Work(now);
        }

        if (Due(now, _marketAt, MarketMs))
        {
            Market(now);
        }

        if (Due(now, _aliveAt, AliveMs))
        {
            Heartbeat(now);
        }
    }

    private static void Errors()
    {
        var now = Core.TickCount;
        var found = BotTail.Since();
        var window = Window(now, _errorsAt);

        _errorsAt = now;

        if (found >= ErrorsAt)
        {
            BotAlarm.Raise(
                "errors",
                $"{found} errors printed in the last {window}; the newest reads: {BotTail.Worst}."
                + " The log is read through a redirect, so this count can lag the world by a buffer",
                found,
                ErrorsAt,
                window
            );

            return;
        }

        if (found == 0)
        {
            BotAlarm.Clear("errors", $"errors have stopped; none in the last {window}", 0, ErrorsAt, window);
        }
    }

    private static void Standing()
    {
        var alive = BotPopulation.Count;

        if (alive < StandLeast)
        {
            return;
        }

        var stuck = BotStall.Stuck;
        var share = (double)stuck / alive;

        if (share >= StandShare)
        {
            BotAlarm.Raise(
                "standing",
                $"{stuck} of {alive} bots are standing still, which is {Percent(share)}% of the population."
                + $" The shard's own worst case right now: {BotStall.Worst ?? "none named"}",
                stuck,
                alive,
                "now"
            );

            return;
        }

        if (share < StandShare / 2)
        {
            BotAlarm.Clear(
                "standing",
                $"the population is moving again: {stuck} of {alive} standing still",
                stuck,
                alive,
                "now"
            );
        }
    }

    private static void Work(long now)
    {
        var taken = BotWill.Taken - _workTaken;
        var finished = BotWill.Finished - _workFinished;
        var deaths = BotWill.Deaths - _workDeaths;
        var alive = BotPopulation.Count;
        var window = Window(now, _workAt);

        _workAt = now;
        _workTaken = BotWill.Taken;
        _workFinished = BotWill.Finished;
        _workDeaths = BotWill.Deaths;

        if (taken >= WorkLeast)
        {
            var share = (double)finished / taken;

            if (share < WorkFloor)
            {
                BotAlarm.Raise(
                    "work-not-finishing",
                    $"{finished} of {taken} pieces of work finished in the last {window}, which is"
                    + $" {Percent(share)}%. Failed and dropped all told: {BotWill.Failed}, {BotWill.Dropped}",
                    finished,
                    taken,
                    window
                );
            }
            else
            {
                BotAlarm.Clear(
                    "work-not-finishing",
                    $"work is finishing again: {finished} of {taken} in the last {window}",
                    finished,
                    taken,
                    window
                );
            }
        }

        if (deaths >= DeathsAt)
        {
            BotAlarm.Raise(
                "dying",
                $"{deaths} bots died at their work in the last {window}, out of {alive} alive",
                deaths,
                alive,
                window
            );
        }
        else if (deaths == 0)
        {
            BotAlarm.Clear("dying", $"nobody has died in the last {window}", 0, alive, window);
        }
    }

    private static void Market(long now)
    {
        var sells = BotAuction.Sells - _marketSells;
        var fills = BotAuction.Fills - _marketFills;
        var stalls = BotAuction.Listings.Count;
        var wants = BotAuction.Wants.Count;
        var window = Window(now, _marketAt);

        _marketAt = now;
        _marketSells = BotAuction.Sells;
        _marketFills = BotAuction.Fills;

        if (stalls == 0 || wants == 0)
        {
            return;
        }

        if (sells + fills == 0)
        {
            BotAlarm.Raise(
                "market-quiet",
                $"nothing has been bought or sold in {window}, with {stalls} stalls standing and"
                + $" {wants} wants on the board",
                0,
                stalls + wants,
                window
            );

            return;
        }

        BotAlarm.Clear(
            "market-quiet",
            $"the market is moving: {sells} sales and {fills} fills in {window}",
            sells + fills,
            stalls + wants,
            window
        );
    }

    private static void Heartbeat(long now)
    {
        var taken = BotWill.Taken - _aliveTaken;
        var finished = BotWill.Finished - _aliveFinished;
        var errors = BotTail.Errors - _aliveErrors;
        var window = Window(now, _aliveAt);

        _aliveAt = now;
        _aliveTaken = BotWill.Taken;
        _aliveFinished = BotWill.Finished;
        _aliveErrors = BotTail.Errors;

        BotAlarm.Alive(
            $"{BotPopulation.Count} bots alive, {finished} of {taken} pieces of work finished, {errors} errors,"
            + $" {BotStall.Stuck} standing still, {BotAuction.Listings.Count} stalls and {BotAuction.Wants.Count} wants."
            + $" Alarms standing: {BotAlarm.Standing}",
            finished,
            taken,
            window
        );
    }

    public static string Describe() =>
        $"{Looks} looks taken, thresholds: {ErrorsAt} errors a minute, work finishing below {Percent(WorkFloor)}% of"
        + $" {WorkLeast}+ taken in {WorkMs / 60000}m, {Percent(StandShare)}% of the population standing still,"
        + $" nothing traded in {MarketMs / 60000}m, {DeathsAt} deaths in {WorkMs / 60000}m";

    public static void Forget()
    {
        Looks = 0;
    }
}
