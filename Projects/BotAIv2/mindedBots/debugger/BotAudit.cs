using System;
using System.Collections.Generic;
using Server.BotAI.V2;
using Server.Text;

namespace Server.BotAI.Mind;

/// <summary>
/// The roll-call: every two minutes, three questions asked of every bot, and a hand laid on the ones that
/// answer no to all of them.
///
/// <para>
/// <b>It asks no model anything, and that is the first decision here.</b> The three questions — did it get
/// where it was going, did it finish what it took on, did anything change at all — are arithmetic. A model
/// is worth asking why a bot is stuck; it is not worth asking whether one is, and a check that depends on a
/// model is a check that does not run while the card is busy. This runs on a two-minute clock whatever else
/// is happening, costs one pass over the roll, and cannot fail to fire.
/// </para>
///
/// <para>
/// <b>It is also the point at which the debugger stops being only an observer, and that is worth saying out
/// loud.</b> Everything else in this folder was built so the watcher could not affect what it watched. From
/// here it can: it clears a stale route, and if that does not help it ends the piece of work. So every
/// intervention is written down with what was true before it and what was true two minutes after, and the
/// counts are reported separately from everything else. An intervention nobody measures is not a fix, it is
/// a second source of behaviour nobody can account for — and this shard already knows what that costs.
/// </para>
///
/// <para>
/// <b>Two strengths, escalated, because they mean different things.</b> A remind throws the route away and
/// keeps the destination: the bot still wants the same thing and draws a fresh path to it, which is the
/// whole cure when the plan has gone stale under it. A shake ends the undertaking as a failure, so the
/// ledger learns that place was no good and the auction offers something else. The first is cheap and
/// reversible and is tried first; the second throws away work in hand and is only for a bot that was
/// reminded and did not take it.
/// </para>
/// </summary>
public static class BotAudit
{
    public static int WindowMs { get; set; } = 120000;

    public static int MostTouched { get; set; } = 4;

    public static int RestMs { get; set; } = 300000;

    /// <summary>What was true about one bot when the window opened.</summary>
    private sealed class Mark
    {
        public string Kind;

        public long Takes;

        public Point3D Where;

        public Point3D Goal;

        public bool Going;

        public int Worth;

        public double Progress;

        public double Mood;

        public bool Stuck;

        public bool Touched;

        public long TouchedTick;

        public int Reminds;

        public int Shakes;
    }

    private static readonly Dictionary<Serial, Mark> _marks = [];

    private static long _sweptTick;

    private static bool _opened;

    public static long Windows { get; private set; }

    public static long Stuck { get; private set; }

    public static long Reminded { get; private set; }

    public static long Shaken { get; private set; }

    public static long LeftFighting { get; private set; }

    public static long LeftResting { get; private set; }

    public static long LeftCapped { get; private set; }

    public static long Freed { get; private set; }

    public static long StillStuck { get; private set; }

    public static string Last { get; private set; } = "No window has closed yet.";

    public static void Reset()
    {
        _marks.Clear();
        _opened = false;
        _sweptTick = Core.TickCount;

        Windows = 0;
        Stuck = 0;
        Reminded = 0;
        Shaken = 0;
        LeftFighting = 0;
        LeftResting = 0;
        LeftCapped = 0;
        Freed = 0;
        StillStuck = 0;
        Last = "No window has closed yet.";
    }

    public static bool Due(long now)
    {
        if (!_opened)
        {
            _opened = true;
            _sweptTick = now;

            return false;
        }

        return now - _sweptTick >= WindowMs;
    }

    public static void Sweep(long now, IReadOnlyList<BotWatch> roll)
    {
        _sweptTick = now;
        Windows++;

        var sb = ValueStringBuilder.Create(1024);

        try
        {
            var seen = 0;
            var fresh = 0;
            var arrived = 0;
            var short_ = 0;
            var noGoal = 0;
            var movedOn = 0;
            var chasing = 0;
            var fighting = 0;
            var finished = 0;
            var holding = 0;
            var idle = 0;
            var moved = 0;
            var rooted = 0;
            var richer = 0;
            var flat = 0;
            var sadder = 0;

            List<(BotWatch Watch, Mark Mark, string Why)> stuck = [];

            for (var i = 0; i < roll.Count; i++)
            {
                var watch = roll[i];
                var bot = watch.Bot;

                if (bot is not { Deleted: false })
                {
                    continue;
                }

                if (!_marks.TryGetValue(bot.Serial, out var mark))
                {
                    fresh++;
                    _marks[bot.Serial] = Take(watch, now);

                    continue;
                }

                seen++;

                var over = mark.Takes != watch.Takes;

                if (mark.Kind == "-")
                {
                    idle++;
                }
                else if (over)
                {
                    finished++;
                }
                else
                {
                    holding++;
                }

                var reached = false;

                if (mark.Kind == "-")
                {
                }
                else if (over)
                {
                    movedOn++;
                }
                else if (watch.Following)
                {
                    chasing++;
                }
                else if (!mark.Going)
                {
                    noGoal++;
                }
                else if (bot.Map != null && Math.Max(Math.Abs(bot.X - mark.Goal.X), Math.Abs(bot.Y - mark.Goal.Y)) <= Math.Max(1, watch.Slack))
                {
                    arrived++;
                    reached = true;
                }
                else
                {
                    short_++;
                }

                var stirred = Math.Max(Math.Abs(bot.X - mark.Where.X), Math.Abs(bot.Y - mark.Where.Y)) > BotWatch.PacingSpan;

                if (stirred)
                {
                    moved++;
                }
                else
                {
                    rooted++;
                }

                var gained = watch.Worth > mark.Worth || watch.Progress > mark.Progress + 0.0005;

                if (gained)
                {
                    richer++;
                }
                else
                {
                    flat++;
                }

                if (watch.Mood < mark.Mood - 0.01)
                {
                    sadder++;
                }

                if (watch.Fighting)
                {
                    fighting++;
                }

                var wedged = !reached && !over && !stirred && !gained && !watch.Fighting && !watch.Following
                             && mark.Kind != "-";

                var current = Take(watch, now, mark);

                _marks[bot.Serial] = current;

                Was(watch, current, now, wedged, ref stuck, current.Touched);
            }

            Say(ref sb, seen, fresh, arrived, short_, noGoal, movedOn, chasing, fighting, finished, holding, idle, moved, rooted, richer, flat, sadder, stuck.Count);

            Touch(now, stuck, ref sb);

            Last = sb.ToString();

            BotDebugLog.Rule();
            BotDebugLog.Block($"ROLL-CALL {Windows} — every bot, over the last {WindowMs / 1000} seconds", Last);
            BotDebugLog.Rule();
        }
        finally
        {
            sb.Dispose();
        }
    }

    private static void Was(
        BotWatch watch,
        Mark mark,
        long now,
        bool wedged,
        ref List<(BotWatch, Mark, string)> stuck,
        bool touched
    )
    {
        var was = mark.Stuck;

        mark.Stuck = wedged;

        if (wedged)
        {
            Stuck++;

            if (touched)
            {
                StillStuck++;
            }

            var why = watch.Kind == "-"
                ? "it holds nothing and has not moved"
                : $"held {watch.Kind} for {watch.HeldMs / 1000}s, did not arrive, did not finish, did not leave a {BotWatch.PacingSpan}-tile patch at {watch.Where.X},{watch.Where.Y}, and is no better off than {mark.Worth}gp";

            stuck.Add((watch, mark, why));

            return;
        }

        if (touched && was)
        {
            Freed++;

            BotDebugLog.Write(
                $"    {watch.Name} was {(mark.Shakes > 0 ? "shaken" : "reminded")} and is going again: it now holds {watch.Kind} at {watch.Where.X},{watch.Where.Y}, worth {watch.Worth}gp against {mark.Worth}gp"
            );
        }
    }

    private static void Touch(long now, List<(BotWatch Watch, Mark Mark, string Why)> stuck, ref ValueStringBuilder sb)
    {
        if (stuck.Count == 0)
        {
            return;
        }

        stuck.Sort((a, b) => b.Watch.Suspicion.CompareTo(a.Watch.Suspicion));

        var touched = 0;
        var fighting = 0;
        var resting = 0;
        var capped = 0;

        for (var i = 0; i < stuck.Count; i++)
        {
            var (watch, mark, why) = stuck[i];
            var bot = watch.Bot;

            if (bot is not { Deleted: false, Alive: true })
            {
                continue;
            }

            var standing = bot.Resolve?.Standing ?? BotStanding.Dead;

            if (standing is not (BotStanding.Free or BotStanding.Busy))
            {
                fighting++;

                continue;
            }

            if (mark.Touched && now - mark.TouchedTick < RestMs)
            {
                resting++;

                continue;
            }

            if (touched >= MostTouched)
            {
                capped++;

                continue;
            }

            touched++;

            mark.Touched = true;
            mark.TouchedTick = now;

            if (mark.Reminds == 0)
            {
                mark.Reminds++;
                Reminded++;

                bot.Journey?.Discard();

                sb.AppendLine($"  REMINDED {watch.Name} the {watch.Class} of where it was going — {why}. Its route is thrown away; the destination stands.");

                continue;
            }

            mark.Shakes++;
            Shaken++;

            var work = bot.Resolve?.Deed;

            if (work != null && work.Bend(bot))
            {
                sb.AppendLine($"  BENT {watch.Name} the {watch.Class} onto somewhere else — {why}. Its work found another place to go, so nothing was ended.");

                continue;
            }

            BotWill.Abandon(bot, "the debugger found it stuck and ended it");
            bot.Journey?.Finish();
            bot.Refusals = 0;

            sb.AppendLine($"  SHOOK {watch.Name} the {watch.Class} — {why}. It had already been reminded once. Its {mark.Kind} is ended as a failure so the ledger marks that ground down.");
        }

        LeftFighting += fighting;
        LeftResting += resting;
        LeftCapped += capped;

        if (fighting + resting + capped > 0)
        {
            sb.AppendLine(
                $"  Left alone: {fighting} were in a fight or with a company, {resting} had been touched inside the last {RestMs / 60000} minutes, {capped} were past the cap of {MostTouched} a window."
            );
        }
    }

    private static void Say(
        ref ValueStringBuilder sb,
        int seen,
        int fresh,
        int arrived,
        int short_,
        int noGoal,
        int movedOn,
        int chasing,
        int fighting,
        int finished,
        int holding,
        int idle,
        int moved,
        int rooted,
        int richer,
        int flat,
        int sadder,
        int stuck
    )
    {
        sb.Append(seen);
        sb.AppendLine(" bots could be asked three questions. Every case is counted below and none of them is a leftover.");

        if (fresh > 0)
        {
            sb.Append(fresh);
            sb.AppendLine(" more were seen for the first time this window, so there is nothing yet to compare them against. They are counted in nothing below.");
        }

        if (seen == 0)
        {
            sb.AppendLine("Nobody has been watched for a whole window yet, so every answer below would be nought and none of them would mean it.");

            return;
        }

        sb.Append("Did it finish what it took on? ");
        sb.Append(finished);
        sb.Append(" ended a piece of work, ");
        sb.Append(holding);
        sb.Append(" are holding the same one they held two minutes ago, ");
        sb.Append(idle);
        sb.AppendLine(" held nothing to finish.");

        sb.Append("Of the ");
        sb.Append(arrived + short_ + noGoal + chasing);
        sb.Append(" still on the same piece of work, did it get where it was going? ");
        sb.Append(arrived);
        sb.Append(" arrived, ");
        sb.Append(short_);
        sb.Append(" are still short of it, ");
        sb.Append(noGoal);
        sb.Append(" were not going anywhere, ");
        sb.Append(chasing);
        sb.Append(" were chasing something that moves, so there is no fixed place to have reached. The other ");
        sb.Append(movedOn);
        sb.AppendLine(" ended their work in this window, so the destination they had two minutes ago is not a question about them.");

        sb.Append("In a fight this moment: ");
        sb.Append(fighting);
        sb.AppendLine(". None of them can be called stuck by these questions — a bot trading blows fails all four of them while doing exactly what it should.");

        sb.Append("Did anything change? ");
        sb.Append(moved);
        sb.Append(" left the ground they stood on, ");
        sb.Append(rooted);
        sb.Append(" did not; ");
        sb.Append(richer);
        sb.Append(" are better off, ");
        sb.Append(flat);
        sb.Append(" are not; ");
        sb.Append(sadder);
        sb.AppendLine(" are less content than they were.");

        sb.Append("Answered no to all of it: ");
        sb.Append(stuck);
        sb.AppendLine(".");
    }

    private static Mark Take(BotWatch watch, long now, Mark was = null) =>
        new()
        {
            Kind = watch.Kind,
            Takes = watch.Takes,
            Where = watch.Where,
            Goal = watch.Wants,
            Going = watch.WantsAway > 0,
            Worth = watch.Worth,
            Progress = watch.Progress,
            Mood = watch.Mood,
            Stuck = was?.Stuck ?? false,
            Touched = was?.Touched ?? false,
            TouchedTick = was?.TouchedTick ?? 0,
            Reminds = was?.Reminds ?? 0,
            Shakes = was?.Shakes ?? 0
        };

    public static string Describe() =>
        Windows == 0
            ? "no roll-call has run yet"
            : $"{Windows} roll-calls, {Stuck} times a bot answered no to all three questions; "
              + $"{Reminded} reminded, {Shaken} shaken, {LeftFighting} left fighting, {LeftResting} resting, {LeftCapped} over the cap; "
              + $"{Freed} were going again by the next window and {StillStuck} were not";
}
