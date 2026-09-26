using System;
using Server.Logging;

namespace Server.BotAI.V2;

/// <summary>
/// A company called together for a place rather than for a creature, and kept together until the place stops
/// killing people.
///
/// <para>
/// <b>This is the shard's second reason for a group to exist, and it is a different reason.</b>
/// <see cref="BotBand"/> musters against one thing that is too big for one bot: it forms when the thing is
/// in sight, it ends when the thing is dead, and everybody goes back to their own business — which is
/// exactly right, and is why the squads that ran all day were over in a minute or two. A patrol has no
/// quarry at all. It is dispatched to a square that has been hurting people, and its work is finished when
/// the square is quiet, which is a condition that cannot be met by killing any particular thing.
/// </para>
///
/// <para>
/// <b>Almost none of the marching is written here, and that is the point of putting it on a squad.</b> The
/// company follows its leader by arithmetic — <c>BotSquad.Station</c> re-forms whenever the anchor drifts,
/// and the stance falls out of whether the leader is walking: on the road they hold formation, and the
/// moment the captain stops in the middle of the square they scatter into scouting knots and cover it. Being
/// hit anywhere in that spread pulls the whole company onto the attacker through <c>BotSquads.Note</c>. So
/// "sweep a wood" needed no sweeping code: it needed somewhere to stand and a reason not to go home.
/// </para>
///
/// <para>
/// <b>The reason not to go home is the one piece of the squad this had to change.</b> A company disbands
/// after five quiet minutes, which is the right rule for a muster and the wrong one for a patrol — the whole
/// value of standing in a dangerous wood is being there before anything happens. See
/// <see cref="BotSquad.Charged"/>.
/// </para>
/// </summary>
public sealed class BotSweep : BotDeed
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotSweep));

    public const string Trade = "sweep";

    public static long Marches { get; private set; }

    public static long Undermanned { get; private set; }

    public static double Prior { get; set; } = 45.0;

    public static double WorkMinutes { get; set; } = 12.0;

    public static int RoundMs { get; set; } = 45000;

    public static int Least { get; set; } = 3;

    public static int MaxBends { get; set; } = 4;

    public static int Reach { get; set; } = 40;

    public static int CapMs { get; set; } = 1800000;

    public static int HoldMs { get; set; } = 300000;

    private readonly Map _map;

    private readonly Point3D _square;

    private readonly double _read;

    private long _began;

    private long _stoodTick;

    private bool _standing;

    private Mobile _focus;

    private BotSquad _squad;

    private int _called;

    private int _fights;

    private long _steppedTick;

    private int _round;

    private Point3D _post;

    private int _bends;

    private bool _fighting;

    public BotSweep(Map map, Point3D square, double reading)
    {
        _map = map;
        _square = square;
        _read = reading;
        _began = Core.TickCount;
    }

    public static string Describe() =>
        $"{Marches} companies actually marched, {Undermanned} could not raise {Least} bodies once chosen";

    public override string Kind => Trade;

    public override bool Braves => true;

    public override Map Map => _map;

    public override Point3D Where => _square;

    public override double Expects => Prior;

    public override double Minutes => WorkMinutes;

    public override SkillName? Trains => null;

    public override int Outlay => 0;

    public override double Coin => 1.0;

    public override bool Alongside => true;

    public override string Stage =>
        !_standing
            ? $"marching {_called} of us on the square at ({_square.X}, {_square.Y}), which reads {_read:F0}"
            : $"walking the square at ({_square.X}, {_square.Y}) with {_called} of us, {_fights} fights so far";

    public override BotDoing Advance(IBotWilful bot)
    {
        var body = bot?.Self;

        if (body == null || _map == null || _map == Map.Internal)
        {
            return BotDoing.Failed("no body");
        }

        if (bot is not IBotSquadMember member)
        {
            return BotDoing.Failed("not the sort of thing that leads companies");
        }

        if (_squad == null)
        {
            return Calling(member, body);
        }

        return Patrolling(member, body);
    }

    private BotDoing Calling(IBotSquadMember member, Mobile body)
    {
        var squad = member.Squad ?? BotSquads.Form(member);

        if (squad == null)
        {
            return BotDoing.Failed("could not call a company together");
        }

        foreach (var mobile in _map.GetMobilesInRange<Mobile>(body.Location, Reach))
        {
            if (squad.Count >= squad.Ceiling)
            {
                break;
            }

            if (mobile == body || mobile is not IBotSquadMember { Squad: null } other)
            {
                continue;
            }

            if (mobile is not IBotAlly { AbleToFight: true })
            {
                continue;
            }

            BotSquads.Join(squad, other);
        }

        _called = squad.Count;

        if (_called < Least)
        {
            Undermanned++;

            BotSquads.Leave(member);

            return BotDoing.Failed($"only {_called} were free to march on ({_square.X}, {_square.Y})");
        }

        _squad = squad;

        Marches++;

        squad.Charged = true;

        _began = Core.TickCount;

        logger.Information(
            "{Name} is marching {Count} of them on the square at ({X}, {Y}), which reads {Read:F0}",
            body.Name,
            _called,
            _square.X,
            _square.Y,
            _read
        );

        return BotDoing.Walk(_map, _square, BotArrival.Within(BotPeril.Side / 3), $"marching on ({_square.X}, {_square.Y})");
    }

    private BotDoing Patrolling(IBotSquadMember member, Mobile body)
    {
        var squad = member.Squad;

        if (squad == null || !ReferenceEquals(squad, _squad))
        {
            return BotDoing.Done("the company broke up on the road");
        }

        _called = squad.Count;

        if (_called < 2)
        {
            return Finish(squad, "there was nobody left to patrol with");
        }

        var fighting = squad.Stance == BotSquadStance.Fighting;

        if (fighting && !_fighting)
        {
            _fights++;
        }

        _fighting = fighting;

        var now = Core.TickCount;

        if (now - _began >= CapMs)
        {
            return Finish(squad, $"half an hour on ({_square.X}, {_square.Y}) was enough");
        }

        var away = _standing ? BotPeril.Side : BotPeril.Side / 2;

        if (!body.InRange(_square, away))
        {
            _standing = false;

            return BotDoing.Walk(_map, _square, BotArrival.Within(BotPeril.Side / 3), $"marching on ({_square.X}, {_square.Y})");
        }

        if (!_standing)
        {
            _standing = true;
            _stoodTick = now;

            _steppedTick = now;
            _round = 0;
            _post = Post(_round);

            logger.Information(
                "{Name}'s company has reached the square at ({X}, {Y}) and is walking it",
                body.Name,
                _square.X,
                _square.Y
            );
        }

        if (now - _stoodTick >= HoldMs && BotPeril.Reading(_map, _square) < BotPeril.Worrying)
        {
            return Finish(squad, $"({_square.X}, {_square.Y}) has gone quiet");
        }

        if (fighting && squad.Focus is { Deleted: false, Alive: true } focus)
        {
            if (!ReferenceEquals(focus, _focus))
            {
                _focus = focus;

                if (member is IBotWilful wilful && wilful.Resolve != null)
                {
                    wilful.Resolve.StirredTick = now;
                }
            }

            return BotDoing.Work($"fighting {focus.Name} with the company on ({_square.X}, {_square.Y}), {_fights} fights so far");
        }

        if (now - _steppedTick >= RoundMs || body.InRange(_post, 1))
        {
            _steppedTick = now;
            _bends = 0;
            _post = Post(++_round);
        }

        return BotDoing.Walk(
            _map,
            _post,
            BotArrival.Within(1),
            $"walking the square at ({_square.X}, {_square.Y}), {_fights} fights so far"
        );
    }

    private Point3D Post(int round)
    {
        var reach = Math.Max(2, BotPeril.Side / 3);

        var (dx, dy) = (round % 5) switch
        {
            0 => (0, 0),
            1 => (-reach, -reach),
            2 => (reach, -reach),
            3 => (reach, reach),
            _ => (-reach, reach)
        };

        var x = _square.X + dx;
        var y = _square.Y + dy;

        return BotStep.Settle(_map, x, y, out var z) ? new Point3D(x, y, z) : _square;
    }

    private BotDoing Finish(BotSquad squad, string why)
    {
        BotPeril.Swept(_map, _square);

        if (squad != null)
        {
            squad.Charged = false;
        }

        return BotDoing.Done($"{why} — {_fights} fights, {_called} of us");
    }

    public override bool Bend(IBotWilful bot)
    {
        if (!_standing || ++_bends > MaxBends)
        {
            BotPeril.Baulked(_map, _square);

            return false;
        }

        _steppedTick = Core.TickCount;
        _post = Post(++_round);

        logger.Information(
            "{Name}'s company could not reach that corner of ({X}, {Y}) and is trying the next",
            bot?.Self?.Name,
            _square.X,
            _square.Y
        );

        return true;
    }

    public override void Drop(IBotWilful bot)
    {
        if (_squad != null)
        {
            _squad.Charged = false;
        }

        if (bot is IBotSquadMember member && member.Squad != null && ReferenceEquals(member.Squad, _squad))
        {
            BotSquads.Leave(member);
        }

        _squad = null;
    }
}
