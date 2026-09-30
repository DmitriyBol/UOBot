using System;
using System.Collections.Generic;
using Server.Logging;

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
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotBolt));

    public static int StuckMs { get; set; } = 4000;

    public static long Shed { get; private set; }

    public static long Stuck { get; private set; }

    private Point3D _lastAt;

    private long _movedTick;

    private bool _saidStuck;

    private bool _saidShed;

    private int _beats;

    private int _walkBeats;

    private int _leastStam = int.MaxValue;

    public static long Fell { get; private set; }

    public static long FellWalking { get; private set; }

    public void Died(Mobile body, Point3D fellAt)
    {
        if (body == null || !_started)
        {
            return;
        }

        Fell++;

        if (_walkBeats * 2 > _beats)
        {
            FellWalking++;
        }

        logger.Information(
            "{Name} died running from ({FromX}, {FromY}) at ({X}, {Y}), {Tiles} tiles in {Seconds}s: {Walked} of {Beats} beats at a walk, stamina at least {Least} of {StamMax}{Mounted}",
            body.Name,
            _from.X,
            _from.Y,
            fellAt.X,
            fellAt.Y,
            Math.Max(Math.Abs(fellAt.X - _from.X), Math.Abs(fellAt.Y - _from.Y)),
            (Core.TickCount - _begun) / 1000,
            _walkBeats,
            _beats,
            _leastStam == int.MaxValue ? body.Stam : _leastStam,
            body.StamMax,
            body.Mounted ? ", mounted" : ""
        );
    }

    public const string Trade = "flee";

    public static double Prior { get; set; } = 2000.0;

    public static double WorkMinutes { get; set; } = 0.5;

    public static int Watch { get; set; } = 14;

    public static int Bound { get; set; } = 18;

    public static double VetMs { get; set; } = 6.0;

    public static long Vetoed { get; private set; }

    public static double HelpWeight { get; set; } = 0.75;

    public static int HelpReach { get; set; } = 60;

    public static long TowardAllies { get; private set; }

    public static long TowardHome { get; private set; }

    private static bool Help(Map map, Mobile body, bool under, out int hx, out int hy)
    {
        hx = 0;
        hy = 0;

        if (HelpWeight <= 0.0)
        {
            return false;
        }

        Mobile nearest = null;
        var nearestAt = int.MaxValue;

        foreach (var mobile in map.GetMobilesInRange<Mobile>(body.Location, HelpReach))
        {
            if (mobile == body || mobile is not IBotAlly { AbleToFight: true } || !mobile.Alive)
            {
                continue;
            }

            var at = Math.Max(Math.Abs(mobile.X - body.X), Math.Abs(mobile.Y - body.Y));

            if (at <= 3 || at >= nearestAt)
            {
                continue;
            }

            nearest = mobile;
            nearestAt = at;
        }

        int tx, ty;

        if (nearest != null)
        {
            tx = nearest.X;
            ty = nearest.Y;
            TowardAllies++;
        }
        else if (!under)
        {
            var home = BotPopulation.HomeOf(body);

            if (home == Point3D.Zero || Math.Max(Math.Abs(home.X - body.X), Math.Abs(home.Y - body.Y)) <= 3)
            {
                return false;
            }

            tx = home.X;
            ty = home.Y;
            TowardHome++;
        }
        else
        {
            return false;
        }

        hx = Math.Sign(tx - body.X);
        hy = Math.Sign(ty - body.Y);

        return hx != 0 || hy != 0;
    }

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

    public override bool Resumes => true;

    public override bool Repeats(BotDeed other) => other is BotBolt;

    public override Map Map => _map;

    public override Point3D Where => _from;

    public override double Expects => Prior;

    public override double Minutes => WorkMinutes;

    public override SkillName? Trains => null;

    public override int Outlay => 0;

    public override double Coin => 1.0;

    public override int Made => 0;

    public override string Stage => _to == Point3D.Zero ? "getting away" : $"getting away to {_to}";

    public static int ClearCalmMs { get; set; } = 4000;

    public static int Pursuit { get; set; } = 24;

    public static long Pursuits { get; private set; }

    private Server.Mobiles.BaseCreature _pursuer;

    private bool Pursued(IBotWilful bot, Mobile body, long now)
    {
        _pursuer = null;

        foreach (var creature in body.Map.GetMobilesInRange<Server.Mobiles.BaseCreature>(body.Location, Pursuit))
        {
            if (creature is { Deleted: false, Alive: true } && creature.Combatant == body)
            {
                _pursuer = creature;
                Pursuits++;

                return true;
            }
        }

        if (bot?.Resolve is { Struck: true } resolve && now - resolve.HurtTick < ClearCalmMs)
        {
            Pursuits++;

            return true;
        }

        return false;
    }

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
            _lastAt = body.Location;
            _movedTick = now;
        }

        if (body.Location != _lastAt)
        {
            _lastAt = body.Location;
            _movedTick = now;
        }

        _beats++;

        if (body is BotMobile { Running: false })
        {
            _walkBeats++;
        }

        if (body.Stam < _leastStam)
        {
            _leastStam = body.Stam;
        }

        if (body is BotMobile { Journey: { } road })
        {
            var top = road.Current is { Interruption: true } over ? over.Reason : null;

            if (road.Shed() > 0)
            {
                Shed++;

                if (!_saidShed)
                {
                    _saidShed = true;
                    logger.Information(
                        "{Name} drops its {Top} to run: the walk away had been kept under it",
                        body.Name,
                        top ?? "detour"
                    );
                }
            }
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

        if (worst == null && Pursued(bot, body, now))
        {
            worst = _pursuer;
        }

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

        if (!_saidStuck && now - _movedTick >= StuckMs)
        {
            _saidStuck = true;
            Stuck++;

            var mobile = body as BotMobile;
            var queue = mobile?.Journey;

            logger.Information(
                "{Name} has not taken a step in {Seconds}s of running from {Foe} ({Tiles} tiles off) towards {To}: the walker last said {Walk}, the road's top is {Top} ({Errands} errands), stamina {Stam} of {StamMax} ({Gait}){Spell}{Paralyzed}{Frozen}",
                body.Name,
                (now - _movedTick) / 1000,
                worst?.Name ?? "nothing it can see",
                worst == null ? -1 : (int)body.GetDistanceToSqrt(worst),
                _to,
                mobile?.LastWalk.ToString() ?? "nothing",
                queue?.Current?.Reason ?? "nothing",
                queue?.Queued ?? 0,
                body.Stam,
                body.StamMax,
                mobile?.Running == true ? "running" : "walking",
                body.Spell != null ? ", mid-spell" : "",
                body.Paralyzed ? ", paralyzed" : "",
                body.Frozen ? ", frozen" : ""
            );
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

    public static int Clearance { get; set; } = 8;

    public static long Skirted { get; private set; }

    private static bool Occupied(Map map, Mobile body, Point3D landing, Mobile from)
    {
        foreach (var creature in map.GetMobilesInRange<Server.Mobiles.BaseCreature>(landing, Clearance))
        {
            if (creature is { Deleted: false, Alive: true } && !ReferenceEquals(creature, from) && BotThreat.Menacing(body, creature))
            {
                return true;
            }
        }

        return false;
    }

    public static string Describe() =>
        $"{Fell} flights ended in death ({FellWalking} of them mostly at a walk), {Shed} fleeing bots' detours dropped from the road away, {Stuck} flights {StuckMs / 1000}s without a step, {Pursuits} beats a flight went on past Watch for a blow still landing or a pursuer still coming, {Bent} flights turned another way after a refused road, {Skirted} legs taken a lesser way for something else hostile at the better one's end (within {Clearance}), {Vetoed} retreats refused by the look ahead ({VetMs:F0} ms) before anybody walked, {Tended} bandages and bottles taken on the run, "
        + $"{TowardAllies} retreats bent towards allies and {TowardHome} towards home (×{HelpWeight:F2})";

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
            var helped = Help(map, body, under, out var hx, out var hy);

            Span<(int Score, Point3D Back)> ways = stackalloc (int, Point3D)[_ways.Length];
            var found = 0;

            for (var i = 0; i < _ways.Length; i++)
            {
                var (wx, wy) = _ways[i];
                var score = wx * dx + wy * dy;

                if (score <= 0)
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

                if (under ? !BotDungeon.Under(back) : !BotPopulation.Within(map, back) && BotPopulation.Within(map, body.Location) || BotBarred.Barred(map, back))
                {
                    continue;
                }

                var rank = (int)(1000.0 * (score / (double)step + (helped ? HelpWeight * (wx * hx + wy * hy) : 0.0)));
                var at = found;

                while (at > 0 && ways[at - 1].Score < rank)
                {
                    ways[at] = ways[at - 1];
                    at--;
                }

                ways[at] = (rank, back);
                found++;
            }

            var skirting = false;

            for (var pass = 0; pass < 2; pass++)
            {
                for (var i = 0; i < found; i++)
                {
                    var back = ways[i].Back;

                    if (pass == 0 && Occupied(map, body, back, from))
                    {
                        skirting = true;

                        continue;
                    }

                    if (VetMs > 0.0 && !BotPath.CanReach(map, body.Location, back, BotArrival.Within(1), VetMs))
                    {
                        Vetoed++;

                        continue;
                    }

                    if (pass == 0 && skirting)
                    {
                        Skirted++;
                    }

                    return back;
                }
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
        var home = BotPopulation.HomeOf(body);
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
