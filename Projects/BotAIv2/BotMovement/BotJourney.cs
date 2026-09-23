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

    public static int PlanStaleMs { get; set; } = 45000;

    public static int StallAttempts { get; set; } = 100;

    public static int MaxEmptyPlans { get; set; } = 8;

    public static int MaxPlansWithoutCloser { get; set; } = 12;

    public static int DangerAvoidMs { get; set; } = 120000;

    public static int MaxErrands { get; set; } = 4;

    public const int FollowSlack = 2;

    private readonly List<BotErrand> _errands = [];

    private readonly List<Point3D> _plan = [];

    private int _step;

    private long _steppedTick;

    private bool _stepped;

    private BotErrand _planErrand;

    private Point3D _planGoal;

    private long _planBuiltTick;

    private int _planStamp;

    private int _progressStamp;

    private int _bestRemaining;

    private int _attemptsSinceProgress;

    private int _emptyPlans;

    private int _bestAway = int.MaxValue;

    private int _plansSinceCloser;

    private BotErrand _awayErrand;

    private Point3D _avoidTile;

    private Point3D _blockedTile;

    private int _blockedCount;

    private int _dangerX1;
    private int _dangerY1;
    private int _dangerX2;
    private int _dangerY2;

    private bool _dangerous;

    private long _dangerUntil;

    public BotErrand Current => _errands.Count > 0 ? _errands[^1] : null;

    public BotErrand Bottom => _errands.Count > 0 ? _errands[0] : null;

    public bool Active => _errands.Count > 0;

    public int Queued => _errands.Count;

    public Map Map => Current?.Map;

    public Point3D Target => Current?.Target ?? Point3D.Zero;

    public BotArrival Arrival => Current?.Arrival ?? BotArrival.Beside;

    public string Reason => Current?.Reason;

    public bool Walking => _plan.Count > 0 && _step < _plan.Count;

    public bool Moving => _stepped && Core.TickCount - _steppedTick < MovingMs;

    public static int MovingMs { get; set; } = 2000;

    public bool Partial { get; private set; }

    public int Plans { get; private set; }

    public bool Hopeless => _emptyPlans >= MaxEmptyPlans || _plansSinceCloser >= MaxPlansWithoutCloser;

    public int PlansSinceCloser => _plansSinceCloser;

    public bool Probed { get; set; }

    public bool Escalated { get; set; }

    public IReadOnlyList<Point3D> Plan => _plan;

    public int Remaining => _plan.Count - _step;

    public void Begin(Map map, Point3D where, BotArrival arrival, string reason)
    {
        _errands.Clear();

        Push(new BotErrand { Map = map, Where = where, Arrival = arrival, Reason = reason });
    }

    public void Begin(Map map, Mobile follow, BotArrival arrival, string reason)
    {
        _errands.Clear();

        Push(new BotErrand { Map = map, Follow = follow, Arrival = arrival, Reason = reason });
    }

    public void Rebase(Map map, Point3D where, BotArrival arrival, string reason)
    {
        if (map == null)
        {
            return;
        }

        Rebase(new BotErrand { Map = map, Where = where, Arrival = arrival, Reason = reason });
    }

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

        if (wasCurrent)
        {
            Discard();
        }
    }

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

    public void Finish()
    {
        _errands.Clear();

        Discard();

        _dangerous = false;
    }

    public bool Arrived(Point3D at) => Active && Arrival.Reached(at, Target);

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

        if (!ReferenceEquals(_planErrand, errand))
        {
            return true;
        }

        var target = errand.Target;

        var slack = Math.Max(FollowSlack, (_plan.Count - _step) / 4);

        if (Math.Abs(target.X - _planGoal.X) > slack || Math.Abs(target.Y - _planGoal.Y) > slack)
        {
            return true;
        }

        var next = _plan[_step];

        if (Math.Abs(at.X - next.X) > 1 || Math.Abs(at.Y - next.Y) > 1)
        {
            return true;
        }

        return Core.TickCount - _planBuiltTick >= PlanStaleMs;
    }

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

        if (_plan.Count == 0)
        {
            _emptyPlans++;
        }
        else
        {
            _emptyPlans = 0;
        }

        var away = Away(at, Target);

        if (!ReferenceEquals(_awayErrand, Current))
        {
            _awayErrand = Current;
            _bestAway = int.MaxValue;
            _plansSinceCloser = 0;
            Probed = false;
            Escalated = false;
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

        _planStamp++;

        _avoidTile = Point3D.Zero;
    }

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

    public void AvoidTile(Point3D tile) => _avoidTile = tile;

    public int NoteBlocked(Point3D tile)
    {
        if (_blockedTile != tile)
        {
            _blockedTile = tile;
            _blockedCount = 0;
        }

        return ++_blockedCount;
    }

    public void AvoidDanger(int x1, int y1, int x2, int y2)
    {
        _dangerX1 = x1;
        _dangerY1 = y1;
        _dangerX2 = x2;
        _dangerY2 = y2;
        _dangerous = true;
        _dangerUntil = Core.TickCount + DangerAvoidMs;

        Discard();
    }

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

    public void Catch(Point3D at)
    {
        while (_step < _plan.Count && _plan[_step].X == at.X && _plan[_step].Y == at.Y)
        {
            _step++;
        }
    }

    public void Attempted() => _attemptsSinceProgress++;

    public void Stepped(Point3D at)
    {
        _steppedTick = Core.TickCount;
        _stepped = true;

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

    public bool Stalled() => Active && Walking && _attemptsSinceProgress >= StallAttempts;

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
