using System;
using System.Collections.Generic;

namespace Server.BotAI.V2;

/// <summary>
/// Getting away from whatever is killing it. The one piece of work in this project whose whole product is
/// that the bot still exists afterwards.
///
/// <para>
/// <b>The rung it answers has had a proposer for a while and it was only half the answer.</b>
/// <c>Failing</c> is the rung for a bot whose health is going, and the only thing offering work on it was
/// <see cref="BotMedic"/> — mending, standing still, where it stands. That is right when a bot is simply
/// hurt and wrong in the one case the rung exists for: three creatures on one bot, the bot at a third of
/// its health, winding a bandage that takes several seconds while all three go on hitting it. Watched from
/// a client it reads as a bot that has decided to die politely, and it is the same defect the ladder's own
/// notes describe twice — <em>standing still is not an option at any number</em> — reappearing as the thing
/// the rung does instead of standing still.
/// </para>
///
/// <para>
/// <b>It walks away from the worst of it and no further than that.</b> There is no clever route and there
/// must not be: a bot computing an escape path is a bot spending the population's whole path-search budget
/// at the moment it can least afford to stand about. Straight back along the line the creature came in on,
/// recomputed only on arrival, and towards home when straight back would leave the ground the population
/// lives on.
/// </para>
/// </summary>
public sealed class BotBolt : BotDeed
{
    public const string Trade = "flee";

    public static double Prior { get; set; } = 2000.0;

    public static double WorkMinutes { get; set; } = 0.5;

    public static int Watch { get; set; } = 14;

    public static int Bound { get; set; } = 18;

    public static int GiveUpMs { get; set; } = 30000;

    private readonly Map _map;

    private readonly Point3D _from;

    private Point3D _to;

    private long _begun;

    private bool _started;

    private int _legs;

    private readonly int _least;

    public BotBolt(Map map, Point3D from)
    {
        _map = map;
        _from = from;
    }

    public BotBolt(Map map, Point3D from, int leastMs) : this(map, from)
    {
        _least = Math.Max(0, leastMs);
    }

    public override string Kind => Trade;

    public override bool Braves => true;

    public override Map Map => _map;

    public override Point3D Where => _from;

    public override double Expects => Prior;

    public override double Minutes => WorkMinutes;

    public override SkillName? Trains => null;

    public override int Outlay => 0;

    public override double Coin => 1.0;

    public override int Made => 0;

    public override string Stage => _to == Point3D.Zero ? "getting away" : $"getting away to {_to}";

    public override BotDoing Advance(IBotWilful bot)
    {
        var body = bot?.Self;

        if (body == null || _map == null || _map == Map.Internal || !body.Alive)
        {
            return BotDoing.Failed("no body");
        }

        var now = Core.TickCount;

        if (!_started)
        {
            _started = true;
            _begun = now;
        }

        body.Combatant = null;
        body.Warmode = false;

        if (BotMend.Share(body) < BotMend.Hurt || body.Poisoned)
        {
            if (!BotMend.Winding(body) && BotMend.Wind(body, body))
            {
                Tended++;
            }

            if (BotMend.Draught(body) is { } bottle && BotMend.Swallow(body, bottle))
            {
                Tended++;
            }
        }

        var worst = BotThreat.Strongest(body, Watch);

        if (worst == null && now - _begun >= _least)
        {
            BotFugitive.Cleared(body);

            return BotDoing.Done(_legs > 0 ? $"clear of it after {_legs} legs" : "nothing following");
        }

        if (now - _begun >= GiveUpMs)
        {
            return BotDoing.Done(worst == null ? $"ran for {(now - _begun) / 1000}s" : $"could not shake {worst.Name}");
        }

        if (_to == Point3D.Zero || body.InRange(_to, 1))
        {
            _to = Retreat(_map, body, worst);
            _legs++;
        }

        if (_to == Point3D.Zero)
        {
            return BotDoing.Failed($"cornered by {worst.Name}");
        }

        return BotDoing.Walk(_map, _to, BotArrival.Within(1), $"away from {worst.Name}");
    }

    private readonly List<Point3D> _refused = [];

    public override bool Bend(IBotWilful bot)
    {
        var body = bot?.Self;

        if (body == null)
        {
            return false;
        }

        if (_to != Point3D.Zero)
        {
            _refused.Add(_to);
        }

        var worst = BotThreat.Strongest(body, Watch);
        var next = worst == null ? Point3D.Zero : Retreat(_map, body, worst, _refused);

        if (next == Point3D.Zero)
        {
            next = Homeward(_map, body);

            if (next == Point3D.Zero || _refused.Contains(next))
            {
                return false;
            }
        }

        _to = next;
        Bent++;

        return true;
    }

    public static long Bent { get; private set; }

    public static long Tended { get; private set; }

    public static Point3D Retreat(Map map, Mobile body, Mobile from) => Retreat(map, body, from, null);

    private static readonly (int X, int Y)[] _ways =
    [
        (1, 0), (1, 1), (0, 1), (-1, 1), (-1, 0), (-1, -1), (0, -1), (1, -1)
    ];

    public static Point3D Retreat(Map map, Mobile body, Mobile from, List<Point3D> not)
    {
        if (map == null || map == Map.Internal || body == null || from == null)
        {
            return Point3D.Zero;
        }

        var dx = body.X - from.X;
        var dy = body.Y - from.Y;
        var step = Math.Max(Math.Abs(dx), Math.Abs(dy));

        if (step > 0)
        {
            var under = BotDungeon.Under(body.Location);

            var best = Point3D.Zero;
            var bestScore = 0;

            for (var i = 0; i < _ways.Length; i++)
            {
                var (wx, wy) = _ways[i];
                var score = wx * dx + wy * dy;

                if (score <= 0 || score <= bestScore)
                {
                    continue;
                }

                var x = body.X + wx * Bound;
                var y = body.Y + wy * Bound;

                if (!BotStep.Settle(map, x, y, out var z))
                {
                    continue;
                }

                var back = new Point3D(x, y, z);

                if (not != null && not.Contains(back))
                {
                    continue;
                }

                if (under ? !BotDungeon.Under(back) : !BotPopulation.Within(map, back) || BotBarred.Barred(map, back))
                {
                    continue;
                }

                best = back;
                bestScore = score;
            }

            if (best != Point3D.Zero)
            {
                return best;
            }

            if (under)
            {
                return Point3D.Zero;
            }
        }

        var home = Homeward(map, body);

        return not != null && not.Contains(home) ? Point3D.Zero : home;
    }

    private static Point3D Homeward(Map map, Mobile body)
    {
        var home = BotPopulation.Where;
        var dx = home.X - body.X;
        var dy = home.Y - body.Y;
        var step = Math.Max(Math.Abs(dx), Math.Abs(dy));

        if (step <= 0)
        {
            return Point3D.Zero;
        }

        if (step <= Bound)
        {
            return BotStep.Settle(map, home.X, home.Y, out var atHome)
                ? new Point3D(home.X, home.Y, atHome)
                : Point3D.Zero;
        }

        var x = body.X + dx * Bound / step;
        var y = body.Y + dy * Bound / step;

        return BotStep.Settle(map, x, y, out var z) ? new Point3D(x, y, z) : Point3D.Zero;
    }
}
