using System;
using System.Collections.Generic;
using Server.Logging;

namespace Server.BotAI.V2;

/// <summary>
/// What a bot is trying to get to, what it has put aside to do first, and the plan it is walking now.
///
/// <para>
/// <b>Owned by the bot</b>, like <see cref="BotBond"/>, and for the same reason: a bot that is deleted takes
/// its journey with it, and answering "where is this bot going" means asking the bot.
/// </para>
///
/// <para>
/// <b>A queue, not a single destination — and that is what makes an interruption something other than a
/// loss.</b> A bot walking to market that gets hit has three honest answers, and in the first version it only
/// had one. There, fleeing was a <em>goal</em>: it overwrote the errand, so the bot forgot where it had been
/// going, and half an hour of walking became a bot standing in a field wondering what it was for. Here, being
/// attacked pushes a second errand on top of the first. Deal with what is in front of you; the road is still
/// underneath, and it resumes by itself.
/// </para>
///
/// <para>
/// So the three answers become: the threat is several times over — keep walking, the errand never changed;
/// it is manageable — put the destination aside, kill the thing, then carry on; it killed you — the queue
/// dies with the bot. Movement implements the putting-aside. <b>Which of the three</b> is the decision
/// layer's, and nothing here has an opinion about combat.
/// </para>
///
/// <para>
/// <b>What counts as progress is the part that has to be got right</b>, and the first version got it wrong
/// three separate ways. It measured distance to the goal — true only while the only way to walk is straight,
/// and false the moment a bot leaves an enclosure, because the gate is twenty tiles the wrong way. It
/// compared a fresh plan's length against the finished one's, so a bot crossing a continent correctly, one
/// leg at a time, was told off every twenty-five seconds for going backwards. And it judged bots that were
/// not walking at all: a bot standing at a counter trading was measured against the distance to a home it had
/// no intention of visiting, and after twenty-five seconds was declared stuck, had its errand cancelled and
/// was barred from trading for five minutes — for trading.
/// </para>
///
/// <para>
/// So: progress is measured <b>along the plan</b>, in <b>attempts rather than seconds</b>, it restarts
/// whenever the plan does, and a journey with no plan is not judged at all.
/// </para>
/// </summary>
public sealed class BotJourney
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotJourney));

    /// <summary>
    /// How long a plan is trusted before it is drawn again from scratch. The world moves: a house goes up, a
    /// gate is shut, a corpse is dropped in a doorway. Nothing depends on this — a plan that stops working is
    /// noticed the moment a step fails — but a bot on a long walk should not be following a picture of the
    /// world from two minutes ago.
    /// </summary>
    public static int PlanStaleMs { get; set; } = 45000;

    /// <summary>
    /// How many attempts at stepping a bot may make, without getting anywhere, before the errand is given up.
    ///
    /// <para>
    /// <b>Attempts, not seconds, and this is the whole reason it is not a clock.</b> A bot held up by a fight
    /// is not failing to walk — nobody is asking it to walk. A clock would run out mid-fight and abandon the
    /// journey for the crime of being attacked, which is the opposite of the rule this design exists to
    /// state. Counting attempts makes the measure indifferent to time: a ten-minute battle costs zero
    /// attempts, and a hundred refused steps means a hundred refused steps whenever they happened.
    /// </para>
    ///
    /// <para>
    /// A hundred is generous — at a step every four hundred milliseconds, about forty seconds of continuous
    /// fruitless walking. It is a backstop and not a policy: the honest ways out are arriving, dying and
    /// being refused. But a bot getting nowhere and unable to admit it is a bot lost until the shard
    /// restarts, and the first version had one wander into a guarded city, where it could neither fight nor
    /// loot nor train, and stay for the rest of the session looking busy.
    /// </para>
    /// </summary>
    public static int StallAttempts { get; set; } = 100;

    /// <summary>
    /// How many plans in a row may come back with nowhere to walk before the errand is given up.
    ///
    /// The first version capped this and v2's first draft did not, which quietly recreated the defect the
    /// whole subsystem exists to end. A bot in a walled yard too large to be sealed inside the search's time
    /// ceiling gets a partial result with an empty path, every tick, for ever — no progress, no refusal, no
    /// complaint. That is fence-hugging again, wearing a new hat.
    /// </summary>
    public static int MaxEmptyPlans { get; set; } = 8;

    /// <summary>
    /// How many plans in a row an errand may draw without ever getting closer to where it is going.
    ///
    /// <para>
    /// <b>The backstop for a journey that is busy and going nowhere, which is a different failure from every
    /// other one here and was the one left uncovered.</b> <see cref="MaxEmptyPlans"/> catches a bot with
    /// nowhere to walk and <see cref="StallAttempts"/> catches a bot whose steps are refused — but a bot on a
    /// spit of land in the water has neither symptom. It gets a perfectly good plan to the tip of the spit,
    /// walks it, makes real progress along it, replans, walks back, and does that for ever: every individual
    /// plan succeeds, every step is allowed, and the destination never comes closer. Three bots spent
    /// twenty-three minutes that way and nothing in the log looked wrong, because from the inside it is
    /// indistinguishable from walking somewhere.
    /// </para>
    ///
    /// <para>
    /// The measure is the <b>best distance reached over the whole errand</b>, not distance within one plan.
    /// That distinction is what keeps it from repeating the first version's mistake: leaving a walled yard
    /// means walking away from the goal first, and a long road is walked in dashes that each start further
    /// out — both of those keep improving the best, so neither is punished. What cannot improve it is a bot
    /// oscillating between the same two places.
    /// </para>
    /// </summary>
    public static int MaxPlansWithoutCloser { get; set; } = 12;

    /// <summary>
    /// How long ground that nearly killed the bot stays out of its plans. Long enough for whatever it was to
    /// move on, short enough that the world does not fill up with ground nobody will cross.
    /// </summary>
    public static int DangerAvoidMs { get; set; } = 120000;

    /// <summary>
    /// How deep the queue may get.
    ///
    /// Four, and the reason to have a limit at all is that an evening of ambushes should not leave a bot with
    /// a to-do list. Something has to give when a fight is interrupted by a fight that is interrupted by a
    /// fight; the thing that gives is the <b>oldest ordinary errand</b>, because the deepest thing in the
    /// queue is the one whose reason is least likely to still be true.
    /// </summary>
    public static int MaxErrands { get; set; } = 4;

    /// <summary>
    /// How far a followed target may drift from where the plan was drawn before the plan is redrawn.
    ///
    /// Not zero, and this is the difference between chasing and thrashing: a monster moves every few hundred
    /// milliseconds, and replanning on each of its steps is one search per tick per pursuing bot. Two tiles
    /// of slack costs a step of overshoot and saves almost all of that.
    /// </summary>
    public const int FollowSlack = 2;

    /// <summary>The queue. The last entry is what the bot is doing now; earlier ones are waiting.</summary>
    private readonly List<BotErrand> _errands = [];

    private readonly List<Point3D> _plan = [];

    private int _step;

    /// <summary>Which errand this plan was drawn for, so resuming an older one always redraws.</summary>
    private BotErrand _planErrand;

    private Point3D _planGoal;

    private long _planBuiltTick;

    /// <summary>
    /// Which plan the progress figures belong to. Bumped on every replan, which is what stops a new leg from
    /// reading as movement backwards.
    /// </summary>
    private int _planStamp;

    private int _progressStamp;

    private int _bestRemaining;

    private int _attemptsSinceProgress;

    private int _emptyPlans;

    /// <summary>The closest this errand has ever managed to get, and how many plans ago that was.</summary>
    private int _bestAway = int.MaxValue;

    private int _plansSinceCloser;

    /// <summary>
    /// Which errand those two figures describe.
    ///
    /// <b>Tied to the errand rather than to the plan, and that distinction is the whole fix.</b> The first
    /// version of this check reset with the plan — and a plan is thrown away constantly: every improvised step
    /// discards one, and improvising is exactly what a bot does when it is wedged. So the bot that most needed
    /// the count was the one bot that could never accumulate it, and it went on walking in circles while
    /// everything else in the population gave up correctly.
    /// </summary>
    private BotErrand _awayErrand;

    private Point3D _avoidTile;

    private Point3D _blockedTile;

    private int _blockedCount;

    private int _dangerX1;
    private int _dangerY1;
    private int _dangerX2;
    private int _dangerY2;

    /// <summary>
    /// Whether there is a dangerous square to keep out of at all.
    ///
    /// A flag rather than "is the deadline still zero", which is this shard's rule for anything in the tick
    /// domain: on some hosts the counter is the physical machine's uptime passed straight through, so it
    /// starts enormous and can wrap negative — zero is a legitimate reading and useless as "never". See
    /// <c>dev-docs/tick-counts.md</c>.
    /// </summary>
    private bool _dangerous;

    private long _dangerUntil;

    /// <summary>What the bot is doing now, or null when it is not going anywhere.</summary>
    public BotErrand Current => _errands.Count > 0 ? _errands[^1] : null;

    /// <summary>Whether there is anything to do at all.</summary>
    public bool Active => _errands.Count > 0;

    /// <summary>How many errands are stacked up, the current one included.</summary>
    public int Queued => _errands.Count;

    public Map Map => Current?.Map;

    /// <summary>Where to walk this instant. Moves by itself when the current errand is following something.</summary>
    public Point3D Target => Current?.Target ?? Point3D.Zero;

    public BotArrival Arrival => Current?.Arrival ?? BotArrival.Beside;

    public string Reason => Current?.Reason;

    /// <summary>Whether a plan is being walked. An errand without one is not judged for progress.</summary>
    public bool Walking => _plan.Count > 0 && _step < _plan.Count;

    /// <summary>True when the last plan led as close as the world allows rather than to the goal.</summary>
    public bool Partial { get; private set; }

    /// <summary>Plans drawn for the current errand. High and rising is a bot fighting the world.</summary>
    public int Plans { get; private set; }

    /// <summary>
    /// Whether the searches have stopped offering anywhere to walk. The destination is not refused — that is
    /// a separate and provable thing — it simply cannot be worked towards from here.
    /// </summary>
    public bool Hopeless => _emptyPlans >= MaxEmptyPlans || _plansSinceCloser >= MaxPlansWithoutCloser;

    /// <summary>
    /// Plans in a row that have not got this errand any closer than it has already been.
    ///
    /// <see cref="Hopeless"/> is what this eventually becomes; exposed on its own because there is something
    /// worth doing well before then. A journey that is not closing is the one situation in which asking about
    /// the far side of it is worth the tiles.
    /// </summary>
    public int PlansSinceCloser => _plansSinceCloser;

    /// <summary>
    /// Whether the far side of this errand has already been looked at.
    ///
    /// Once per errand, not once per plan. What the look finds is a fact about the world, filed where the
    /// whole population reads it, so asking again about the same destination would buy nothing and pay twice.
    /// </summary>
    public bool Probed { get; set; }

    /// <summary>The tiles left to walk on the current plan, in order.</summary>
    public IReadOnlyList<Point3D> Plan => _plan;

    public int Remaining => _plan.Count - _step;

    /// <summary>
    /// Sets out afresh. Everything queued is dropped: this is the bot choosing what its evening is about, not
    /// adding to a list.
    /// </summary>
    public void Begin(Map map, Point3D where, BotArrival arrival, string reason)
    {
        _errands.Clear();

        Push(new BotErrand { Map = map, Where = where, Arrival = arrival, Reason = reason });
    }

    /// <summary>The same, after something that walks — an escort, a debtor, somebody being followed home.</summary>
    public void Begin(Map map, Mobile follow, BotArrival arrival, string reason)
    {
        _errands.Clear();

        Push(new BotErrand { Map = map, Follow = follow, Arrival = arrival, Reason = reason });
    }

    /// <summary>
    /// Replaces what the bot is fundamentally doing, <b>without disturbing anything stacked on top of it</b>.
    ///
    /// <para>
    /// This exists because a squad tells its members where to stand, and telling somebody where to stand must
    /// not cancel the fight they are in the middle of. <see cref="Begin"/> clears the queue — it is the bot
    /// choosing what its evening is about — and using it for a station would have wiped every interruption the
    /// moment the formation shifted, which is the moment a fight starts. The bottom of the queue is where the
    /// bot belongs; the top is what is happening to it.
    /// </para>
    /// </summary>
    public void Rebase(Map map, Point3D where, BotArrival arrival, string reason)
    {
        if (map == null)
        {
            return;
        }

        Rebase(new BotErrand { Map = map, Where = where, Arrival = arrival, Reason = reason });
    }

    /// <summary>
    /// The same, after something that walks.
    ///
    /// Added for the decision layer, which puts a bot's own undertaking at the bottom of the queue the way a
    /// squad puts a station there — and some undertakings are a creature rather than a place: quarry, an
    /// escort, somebody being followed home. Without it those could only be pushed as interruptions, and an
    /// interruption is by definition the thing happening now, not the thing the bot is fundamentally doing.
    /// </summary>
    public void Rebase(Map map, Mobile follow, BotArrival arrival, string reason)
    {
        if (map == null || follow == null)
        {
            return;
        }

        Rebase(new BotErrand { Map = map, Follow = follow, Arrival = arrival, Reason = reason });
    }

    private void Rebase(BotErrand errand)
    {
        if (_errands.Count == 0)
        {
            Push(errand);

            return;
        }

        var wasCurrent = _errands.Count == 1;

        _errands[0] = errand;

        // Only when the thing replaced was also the thing being walked. Otherwise an interruption is in
        // progress and its plan is still perfectly good.
        if (wasCurrent)
        {
            Discard();
        }
    }

    /// <summary>
    /// Puts the current errand aside and does this first. What the road already knew is not lost.
    ///
    /// The whole mechanism the design asks for: point B is remembered, the fight goes on top, and when the
    /// fight ends the bot is already pointed at B again with nobody having had to store it anywhere.
    /// </summary>
    public void Interrupt(Map map, Mobile follow, BotArrival arrival, string reason)
    {
        Push(
            new BotErrand
            {
                Map = map,
                Follow = follow,
                Arrival = arrival,
                Reason = reason,
                Interruption = true
            }
        );
    }

    /// <summary>The same, for an interruption that is a place rather than a creature.</summary>
    public void Interrupt(Map map, Point3D where, BotArrival arrival, string reason)
    {
        Push(
            new BotErrand
            {
                Map = map,
                Where = where,
                Arrival = arrival,
                Reason = reason,
                Interruption = true
            }
        );
    }

    /// <summary>
    /// This errand is done with, however it ended. Returns whether anything was waiting underneath.
    ///
    /// One method for all three endings — the market was reached, the monster is dead, the place turned out to
    /// be unreachable — because from movement's side they are the same event: the thing on top is finished,
    /// and whatever it was covering is next. Which of the three it was belongs in the log, not in the control
    /// flow.
    /// </summary>
    public bool Complete()
    {
        if (_errands.Count == 0)
        {
            return false;
        }

        _errands.RemoveAt(_errands.Count - 1);

        Discard();

        return _errands.Count > 0;
    }

    /// <summary>
    /// Drops errands whose reason has evaporated — a followed creature that died or vanished — from anywhere
    /// in the queue, and says how many went.
    ///
    /// From anywhere rather than just the top, because a queued errand can lapse while something else is
    /// happening: a bot that put aside chasing one skeleton to deal with another may find the first one dead
    /// by the time it looks up, killed by somebody else.
    /// </summary>
    public int Prune()
    {
        var dropped = 0;

        for (var i = _errands.Count - 1; i >= 0; i--)
        {
            if (!_errands[i].Lapsed)
            {
                continue;
            }

            var top = i == _errands.Count - 1;

            _errands.RemoveAt(i);
            dropped++;

            if (top)
            {
                Discard();
            }
        }

        return dropped;
    }

    /// <summary>Everything forgotten. Death, or the bot being told to do something else entirely.</summary>
    public void Finish()
    {
        _errands.Clear();

        Discard();

        _dangerous = false;
    }

    /// <summary>Whether standing at <paramref name="at"/> is arriving at what the bot is doing now.</summary>
    public bool Arrived(Point3D at) => Active && Arrival.Reached(at, Target);

    /// <summary>
    /// Whether a fresh plan is needed before the next step: there is none, it belongs to another errand, the
    /// target has walked off, the bot is no longer on it, or it has gone stale.
    /// </summary>
    public bool NeedsPlan(Point3D at)
    {
        var errand = Current;

        if (errand == null)
        {
            return false;
        }

        if (_plan.Count == 0 || _step >= _plan.Count)
        {
            return true;
        }

        // A different errand entirely — an interruption pushed, or an older one resumed.
        if (!ReferenceEquals(_planErrand, errand))
        {
            return true;
        }

        // The target has moved. Slack rather than exact equality, because a plan redrawn on every step of a
        // fleeing skeleton is one search per tick.
        var target = errand.Target;

        if (Math.Abs(target.X - _planGoal.X) > FollowSlack || Math.Abs(target.Y - _planGoal.Y) > FollowSlack)
        {
            return true;
        }

        // Off the plan. A plan is a list of adjacent tiles, so if the next one is not adjacent then the bot is
        // not where the plan thinks it is — it was teleported, shoved, or it improvised its way round
        // something. Walking on regardless means aiming at a tile two or more away and failing the step for
        // ever, which reads as a wall and is not one.
        var next = _plan[_step];

        if (Math.Abs(at.X - next.X) > 1 || Math.Abs(at.Y - next.Y) > 1)
        {
            return true;
        }

        return Core.TickCount - _planBuiltTick >= PlanStaleMs;
    }

    /// <summary>
    /// Takes the outcome of a search. The plan's own bookkeeping and the progress count both restart here,
    /// together, because they are the same event.
    /// </summary>
    public void Planned(BotPathOutcome outcome, List<Point3D> path, Point3D at)
    {
        _plan.Clear();

        if (path != null)
        {
            _plan.AddRange(path);
        }

        _step = 0;
        _planErrand = Current;
        _planGoal = Target;
        _planBuiltTick = Core.TickCount;
        Partial = outcome == BotPathOutcome.Partial;

        Plans++;

        // A plan with nowhere to walk. On its own unremarkable — the bot is boxed in for the moment — and in a
        // row it is the signature of a bot that will never get out.
        if (_plan.Count == 0)
        {
            _emptyPlans++;
        }
        else
        {
            _emptyPlans = 0;
        }

        // Has this errand ever been closer than it is now? Measured here, once per plan, because this is the
        // only moment at which "the last plan achieved nothing" can be told from "the last plan is still
        // being walked".
        var away = Away(at, Target);

        // A different errand entirely — pushed, resumed or rebased. Its own history starts here.
        if (!ReferenceEquals(_awayErrand, Current))
        {
            _awayErrand = Current;
            _bestAway = int.MaxValue;
            _plansSinceCloser = 0;
            Probed = false;
        }

        if (away < _bestAway)
        {
            _bestAway = away;
            _plansSinceCloser = 0;
        }
        else
        {
            _plansSinceCloser++;
        }

        // A new plan, so what counts as progress starts again. This single line is the fix for the second of
        // the three progress bugs: without it every leg of a long journey looks like going backwards.
        _planStamp++;

        // One tile that somebody was standing on when the last step was refused, kept off this plan and this
        // plan only. Consumed here, so a person who has since walked away stops being avoided.
        _avoidTile = Point3D.Zero;
    }

    /// <summary>
    /// What the next search should keep out of: a person in the way, and ground that nearly killed the bot.
    ///
    /// <para>
    /// The dangerous square is dropped in two cases, and both are the difference between a route that bends
    /// and a bot that cannot move. If the <b>target</b> is inside it, honouring it makes the errand
    /// unplannable for two minutes — and the destination is the one thing nothing here may touch. If the
    /// <b>bot</b> is inside it, every neighbouring tile is excluded too and the search has nowhere to begin:
    /// a bot that was nearly killed is standing where it was nearly killed, which is exactly when it most
    /// needs to be able to leave.
    /// </para>
    /// </summary>
    public BotAvoid Avoid(Point3D at)
    {
        var avoid = BotAvoid.None;

        if (_dangerous && Core.TickCount - _dangerUntil < 0 && !Inside(Target) && !Inside(at))
        {
            avoid = BotAvoid.Square(_dangerX1, _dangerY1, _dangerX2, _dangerY2);
        }

        if (_avoidTile != Point3D.Zero)
        {
            avoid = avoid.And(_avoidTile);
        }

        return avoid;
    }

    /// <summary>Somebody is standing on the next tile. Keep it off the next plan only.</summary>
    public void AvoidTile(Point3D tile) => _avoidTile = tile;

    /// <summary>
    /// How many attempts in a row this same tile has refused the bot.
    ///
    /// The reason to count rather than react at once: most things in the way are leaving anyway. A wandering
    /// monster is gone in a step, and redrawing the route for it spends a search to avoid a tile that will be
    /// empty before the plan is finished. Something still there on the second attempt is worth planning
    /// around — that is the difference between a bot standing at an auction and a skeleton walking past.
    /// </summary>
    public int NoteBlocked(Point3D tile)
    {
        if (_blockedTile != tile)
        {
            _blockedTile = tile;
            _blockedCount = 0;
        }

        return ++_blockedCount;
    }

    /// <summary>
    /// This patch of ground nearly killed the bot. Route around it and keep going to the same place.
    ///
    /// The destination is never touched here, and that is the whole of "the goal is untouchable, the way
    /// bends". A bot driven off a road still arrives; it just arrives by another road.
    /// </summary>
    public void AvoidDanger(int x1, int y1, int x2, int y2)
    {
        _dangerX1 = x1;
        _dangerY1 = y1;
        _dangerX2 = x2;
        _dangerY2 = y2;
        _dangerous = true;
        _dangerUntil = Core.TickCount + DangerAvoidMs;

        // The plan in hand was drawn without knowing this, so it may well walk straight back in.
        Discard();
    }

    /// <summary>The next tile to step onto, or false when the plan is done.</summary>
    public bool TryNextTile(out Point3D tile)
    {
        if (_step < _plan.Count)
        {
            tile = _plan[_step];

            return true;
        }

        tile = Point3D.Zero;

        return false;
    }

    /// <summary>
    /// Tiles already behind the bot are dropped. Keeps a plan valid after the bot has been moved by something
    /// other than itself — a diagonal that cut a corner, a shove that happened to help.
    /// </summary>
    public void Catch(Point3D at)
    {
        while (_step < _plan.Count && _plan[_step].X == at.X && _plan[_step].Y == at.Y)
        {
            _step++;
        }
    }

    /// <summary>
    /// An attempt at stepping was made. Called once per try, whether or not it worked, and nowhere else — so
    /// time spent fighting, standing or waiting costs the errand nothing.
    /// </summary>
    public void Attempted() => _attemptsSinceProgress++;

    /// <summary>A step was taken. The only place progress is recorded.</summary>
    public void Stepped(Point3D at)
    {
        Catch(at);

        var remaining = Remaining;

        if (_progressStamp != _planStamp || remaining < _bestRemaining)
        {
            _progressStamp = _planStamp;
            _bestRemaining = remaining;
            _attemptsSinceProgress = 0;
            _blockedTile = Point3D.Zero;
            _blockedCount = 0;
        }
    }

    /// <summary>
    /// Whether this errand has stopped going anywhere.
    ///
    /// <b>Only a walking bot is judged.</b> An errand with no plan is a bot standing still on purpose — at a
    /// counter, at a forge, waiting out a cast — and measuring it against a destination it is not currently
    /// travelling to is how the first version banned bots from trading as a punishment for trading.
    /// </summary>
    public bool Stalled() => Active && Walking && _attemptsSinceProgress >= StallAttempts;

    /// <summary>Throws the plan away, keeping the queue. The next tick draws a new one.</summary>
    public void Discard()
    {
        _plan.Clear();
        _step = 0;
        _planErrand = null;
        _planGoal = Point3D.Zero;
        _planBuiltTick = 0;
        _planStamp++;
        _progressStamp = -1;
        _bestRemaining = int.MaxValue;
        _attemptsSinceProgress = 0;
        _blockedTile = Point3D.Zero;
        _blockedCount = 0;
        _emptyPlans = 0;

        // <b>This line was missing, and its absence made a bot inherit the last errand's despair.</b> Hopeless
        // reads two counters and only one of them was cleared here, so a bot that had spent twelve plans
        // failing to close on somewhere unreachable was declared hopeless about its *next* destination on the
        // first plan it drew — "could not get one tile closer in 1 plans", 148 times in two minutes, on
        // stations and corpses that were perfectly walkable. A reset list is exactly as good as its shortest
        // omission, and nothing about the symptom pointed at the reset.
        _plansSinceCloser = 0;

        Plans = 0;
        Partial = false;
    }

    private void Push(BotErrand errand)
    {
        if (errand?.Map == null)
        {
            return;
        }

        _errands.Add(errand);

        Discard();

        if (_errands.Count <= MaxErrands)
        {
            return;
        }

        // Full. The oldest ordinary errand goes, not the oldest of any kind: an interruption is by definition
        // the thing happening now, and dropping one would leave a bot walking away from a fight it had already
        // decided to have.
        for (var i = 0; i < _errands.Count - 1; i++)
        {
            if (_errands[i].Interruption)
            {
                continue;
            }

            logger.Information("An errand was forgotten to make room: {Errand}", _errands[i]);

            _errands.RemoveAt(i);

            return;
        }

        logger.Information("An errand was forgotten to make room: {Errand}", _errands[0]);

        _errands.RemoveAt(0);
    }

    /// <summary>Distance the way the engine measures adjacency: the larger of the two axis distances.</summary>
    private static int Away(Point3D from, Point3D to)
    {
        var dx = Math.Abs(from.X - to.X);
        var dy = Math.Abs(from.Y - to.Y);

        return dx > dy ? dx : dy;
    }

    private bool Inside(Point3D where) =>
        where.X >= _dangerX1 && where.X <= _dangerX2 && where.Y >= _dangerY1 && where.Y <= _dangerY2;

    public override string ToString() =>
        !Active
            ? "going nowhere"
            : $"{Current}, {Remaining} tiles left of plan {Plans}{(Partial ? ", partial" : "")}{(Queued > 1 ? $", {Queued - 1} waiting" : "")}";
}
