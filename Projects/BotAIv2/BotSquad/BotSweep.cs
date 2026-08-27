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

    /// <summary>The ledger's key.</summary>
    public const string Trade = "sweep";

    /// <summary>
    /// Companies that actually formed and set out.
    ///
    /// <para>
    /// <b>Kept here rather than where the offer is made, because they are not the same event and the shard
    /// has paid for that confusion four times already.</b> <c>BotPatrol.Offered</c> counts offers handed to
    /// the auction, and most offers are thrown away unchosen — 22 of them against 2 real marches in one hour
    /// on 26.08.2026. A march is a captain who won the auction, called, and got enough bodies to go.
    /// </para>
    /// </summary>
    public static long Marches { get; private set; }

    /// <summary>Captains who won a patrol and then could not raise <see cref="Least"/> bodies for it.</summary>
    public static long Undermanned { get; private set; }

    /// <summary>
    /// What a patrol is reckoned at per minute before experience corrects it.
    ///
    /// <para>
    /// <b>Low on purpose, and it is not meant to compete on money.</b> A sweep pays what its fights happen to
    /// drop, which is a hunt's takings spread over a much longer errand, so judged as trade it is poor and
    /// the ledger will say so within a session. It wins its place in the auction because only one bot on the
    /// shard is ever offered it and that bot has almost nothing else it would rather do — see
    /// <see cref="BotPatrol"/>. A number chosen to make it win would be a thumb on the scale, and the auction
    /// is the one thing on this shard that has never needed one.
    /// </para>
    /// </summary>
    public static double Prior { get; set; } = 45.0;

    /// <summary>How long a patrol is expected to be out, walk and all.</summary>
    public static double WorkMinutes { get; set; } = 12.0;

    /// <summary>
    /// How long the company spends walking to one corner of its square before trying the next.
    ///
    /// Long enough to actually arrive across a third of a square, short enough that a patrol that has walked
    /// into a dead end gives up on that corner rather than on the afternoon.
    /// </summary>
    public static int RoundMs { get; set; } = 45000;

    /// <summary>Fewest bodies worth marching anywhere with. A captain and one other is an escort, not a company.</summary>
    public static int Least { get; set; } = 3;

    /// <summary>
    /// How many corners of the square may turn out to be unwalkable before the patrol gives the square up.
    ///
    /// <para>
    /// Four, which is the rest of the ring: a corner over a cliff edge or inside a wall is an ordinary fact
    /// about ground and says nothing about the other three. Counted from the last corner actually reached
    /// rather than over the whole patrol, or half an hour of honest walking accumulates the bends of a
    /// patrol that never worked at all.
    /// </para>
    /// </summary>
    public static int MaxBends { get; set; } = 4;

    /// <summary>How far around itself a captain calls for volunteers.</summary>
    public static int Reach { get; set; } = 40;

    /// <summary>
    /// The longest one patrol may last.
    ///
    /// <para>
    /// Half an hour, which is six times a muster's whole life and is meant to be. The order was for companies
    /// that live a long time; the cap exists because "until the square is quiet" is a condition a square full
    /// of respawning residents can refuse to meet all night, and a company that can never be wrong is the
    /// bot standing in a fight for twenty-two minutes wearing a different hat.
    /// </para>
    /// </summary>
    public static int CapMs { get; set; } = 1800000;

    /// <summary>
    /// How long the company stands in the square before "it is quiet here" is allowed to mean anything.
    ///
    /// <para>
    /// A square reads high because of what happened in it over the last twenty minutes, and a patrol that
    /// checked the reading on arrival would find the trouble it was sent for still on the board and the
    /// square still counted as dangerous — or, once the sweep knocks it down, would find it quiet the instant
    /// it got there and turn round. Neither of those is a patrol. The company holds the ground for this long
    /// before the question is asked at all.
    /// </para>
    /// </summary>
    public static int HoldMs { get; set; } = 300000;

    private readonly Map _map;

    private readonly Point3D _square;

    private readonly double _read;

    private long _began;

    private long _stoodTick;

    private bool _standing;

    private BotSquad _squad;

    private int _called;

    private int _fights;

    private long _steppedTick;

    private int _round;

    private Point3D _post;

    private int _bends;

    /// <summary>Whether the company was in contact last time it was asked. See <see cref="_fights"/>.</summary>
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

    public override Map Map => _map;

    public override Point3D Where => _square;

    public override double Expects => Prior;

    public override double Minutes => WorkMinutes;

    /// <summary>
    /// Nothing. A patrol trains whatever the fights in it happen to train, and claiming a skill here would
    /// be claiming it twice — the fights are the shard's ordinary combat and are already counted.
    /// </summary>
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

            // Let go rather than held: a captain standing about with two volunteers is two bots not working,
            // and the auction will offer this again in fifteen seconds when somebody else is free.
            BotSquads.Leave(member);

            return BotDoing.Failed($"only {_called} were free to march on ({_square.X}, {_square.Y})");
        }

        _squad = squad;

        Marches++;

        // The one thing a patrol needs from the squad that a muster does not. See BotSquad.Charged.
        squad.Charged = true;

        // Re-stamped here rather than trusted from the constructor: a deed is built every time the auction
        // asks and most of them are thrown away, so the clock has to start when the company does. The same
        // lesson the minds' own deed learned the hard way.
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

        // <b>Fights, not beats spent fighting.</b> This counted up once per decision while the company was in
        // contact, so one skirmish that lasted a few minutes was reported as hundreds — "1064 fights" for a
        // single half-hour patrol on 25.08.2026, which is not a number anybody could act on. A fight is the
        // company going from not being in contact to being in contact, and nothing else.
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

        // Still on the road. The squad marches itself: the leader's journey is what puts it in Marching
        // stance, and everybody's station falls out of that.
        //
        // <b>Two different distances, and the gap between them is the whole of what makes a patrol stay
        // put.</b> Arriving is half a side — the company is in its square. <em>Leaving</em> is a whole side,
        // because a captain that walks a corner, or takes three steps after something that hit it, has not
        // left the errand and must not be treated as though it had. Judged on one number this flapped: the
        // patrol of 14:49 on 26.08.2026 crossed back and forth across twelve tiles about twice a second for
        // half an hour, and every crossing did three things — it reset the clock that decides whether the
        // ground has been held long enough for "it is quiet here" to mean anything, so no patrol could ever
        // end that way and all of them ran to the half-hour cap; it threw the corner walk back to the middle,
        // so the square the company was sent to walk was never actually walked; and it said "the company has
        // reached the square" 3007 times, which is how it was found.
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

        // <b>It walks the square rather than standing in the middle of it, and that is two things at once.</b>
        //
        // The first is the order as given: a patrol patrols. A company parked on the centre tile covers
        // whatever wanders past the centre tile, and the squad's own scouting knots spread from wherever the
        // leader happens to be — so moving the leader moves the whole net across the ground.
        //
        // The second is a clock. <c>BotWill.LabourMs</c> fails any undertaking that has answered nothing but
        // <c>Work</c> for fifteen minutes, on the reasoning that such a thing is invisible and immortal —
        // and that reasoning is right, it was bought with three tailors frozen at their benches for two
        // hours. But this patrol's own cap is half an hour, so the two numbers could not both be obeyed: a
        // quiet patrol would have been failed at fifteen minutes, and a failure marks the ground with
        // caution, which would have taught the whole population to avoid the square the captain was sent to
        // make safe. Rather than tune one number against the other — two clocks on one shelf, which is the
        // defect this project keeps paying for — the work now does something a walk can be seen in.
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

    /// <summary>
    /// The next place inside the square to walk to: the four corners and the middle, in turn.
    ///
    /// Corners rather than a random point, so that a watcher can tell a patrol from a wander and so that the
    /// whole square is actually covered rather than sampled. A third of a side out from the middle keeps the
    /// company inside the square it was sent to — a corner at half a side would put half the formation in the
    /// next square along.
    /// </summary>
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

        // The corner's own ground, and not the middle's carried sideways eight tiles. A square is twenty-four
        // tiles across and a hillside crosses it: the middle at ten and the corner at thirty is ordinary
        // terrain, and a walk to a corner at the middle's height is a walk to nowhere — the same fault that
        // put (x, y, 0) on the whole map. See BotPeril.Worst.
        return BotStep.Settle(_map, x, y, out var z) ? new Point3D(x, y, z) : _square;
    }

    private BotDoing Finish(BotSquad squad, string why)
    {
        // Knocked down rather than cleared, so that a square which is still full of trouble earns its way
        // back onto the board by hurting somebody rather than by never having been visited.
        BotPeril.Swept(_map, _square);

        if (squad != null)
        {
            squad.Charged = false;
        }

        return BotDoing.Done($"{why} — {_fights} fights, {_called} of us");
    }

    /// <summary>
    /// The way to the next place in the square turned out not to exist.
    ///
    /// <para>
    /// <b>Somewhere else inside the same square, because the square is the errand and a corner is not.</b>
    /// A patrol that answered nothing here was given up whole the first time one of its five posts could not
    /// be walked to — and since the posts were being asked for at the middle's height, on any ground that is
    /// not flat that was the first one. Ten of eighteen patrols on the night of 25.08.2026 ended this way,
    /// on four squares between them, none of them swept.
    /// </para>
    ///
    /// <para>
    /// <b>On the road there is nowhere else to bend to, and that is a fact worth writing down.</b> The
    /// company has not arrived, so there is no square to try another corner of; the honest answer is to give
    /// up — and to say so on the map, or the captain is offered the identical unreachable square in the next
    /// beat and marches at it again. See <see cref="BotPeril.Baulked"/>.
    /// </para>
    /// </summary>
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

    /// <summary>
    /// The charge is given up whichever way the patrol ended, including the ways that never reach Finish.
    ///
    /// Left set, a company that lost its captain would stand in a field until the world was reloaded: the
    /// quiet clock is the only thing that ever disbands a squad nobody is fighting with, and the charge is
    /// precisely the thing that switches it off.
    /// </summary>
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
