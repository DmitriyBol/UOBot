using System;
using System.Collections.Generic;
using Server.Items;
using Server.Logging;
using Server.Mobiles;

namespace Server.BotAI.V2;

/// <summary>
/// A captain taking a paid party out to ground nobody has ever stood in.
///
/// <para>
/// <b>This is the one errand on the shard whose product is knowledge rather than goods.</b> Everything else
/// a bot does ends in coin, in a made thing or in a dead monster; this ends in a square on the map changing
/// from "never stood in" to a reading. The population cannot hunt where it has not been, cannot be sent
/// where nothing is known, and — because <see cref="BotQuad"/> credits ground for being walked — cannot even
/// tell safe ground from unvisited ground without somebody going and looking.
/// </para>
///
/// <para>
/// <b>Paid, and paid out of the captain's own pocket, by order.</b> Fifty gold split between whoever comes.
/// It is a small sum on purpose: scouting is not meant to compete with hunting on takings, it is meant to be
/// worth doing when nothing better is going. What the money really buys is a reason for the other bots to
/// come at all — the auction weighs every want in gold a minute, and an unpaid walk into the unknown scores
/// exactly nothing against a hunt. The captain is kept from ruining himself by a floor on his own purse: see
/// <see cref="Solvent"/>.
/// </para>
/// </summary>
public sealed class BotScout : BotDeed
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotScout));

    public const string Trade = "scout";

    public static int Wage { get; set; } = 50;

    public static int Solvent { get; set; } = 300;

    public static int Range
    {
        get => _range > 0 ? _range : BotPopulation.Roam;
        set => _range = value;
    }

    private static int _range;

    public static int Reach { get; set; } = 30;

    public static int Least { get; set; } = 2;

    public static int CapMs { get; set; } = 600000;

    public static double Prior { get; set; } = 40.0;

    public static double WorkMinutes { get; set; } = 6.0;

    public static long Parties { get; private set; }

    public static long Undermanned { get; private set; }

    public static long Surveyed { get; private set; }

    public static long Timedout { get; private set; }

    public static long Wages { get; private set; }

    public static long Paid { get; private set; }

    private readonly Map _map;

    private Point3D _where;

    private readonly int _wage;

    private readonly int _least;

    private readonly int _rounds;

    private readonly bool _kin;

    private readonly Rectangle2D _bounds;

    private int _read;

    private BotSquad _squad;

    private int _called;

    private long _began;

    public BotScout(Map map, Point3D where) : this(map, where, Wage, Least, 1)
    {
    }

    public BotScout(Map map, Point3D where, int wage, int least, int rounds)
        : this(map, where, wage, least, rounds, false, default)
    {
    }

    public BotScout(Map map, Point3D where, int wage, int least, int rounds, bool kin, Rectangle2D bounds)
    {
        _map = map;
        _where = where;
        _wage = Math.Max(0, wage);
        _least = Math.Max(1, least);
        _rounds = Math.Max(1, rounds);
        _kin = kin;
        _bounds = bounds;
        _began = Core.TickCount;
    }

    public override string Kind => Trade;

    public override bool Braves => true;

    public override Map Map => _map;

    public override Point3D Where => _where;

    public override double Expects => Prior;

    public override double Minutes => WorkMinutes;

    public override SkillName? Trains => null;

    public override int Outlay => _wage;

    public override double Coin => 1.0;

    public override bool Unpaid => true;

    public override bool Alongside => true;

    public override string Stage =>
        _squad == null
            ? $"raising a party for the ground at ({_where.X}, {_where.Y})"
            : _rounds > 1
                ? $"scouting ({_where.X}, {_where.Y}) with {_called} of us, {_read} of {_rounds} squares read"
                : $"scouting ({_where.X}, {_where.Y}) with {_called} of us";

    public override BotDoing Advance(IBotWilful bot)
    {
        var body = bot?.Self;

        if (body == null || _map == null || _map == Map.Internal)
        {
            return BotDoing.Failed("no body");
        }

        if (bot is not IBotSquadMember member)
        {
            return BotDoing.Failed("not the sort of thing that leads parties");
        }

        return _squad == null ? Calling(member, body) : Walking(member, body);
    }

    private BotDoing Calling(IBotSquadMember member, Mobile body)
    {
        var squad = member.Squad;

        if (squad != null && !ReferenceEquals(squad.Leader, member))
        {
            return BotDoing.Done("somebody else is leading this company");
        }

        if (_kin)
        {
            if (squad == null)
            {
                return BotDoing.Failed("the company has not been mustered");
            }
        }
        else
        {
            squad ??= BotSquads.Form(member);

            if (squad == null)
            {
                return BotDoing.Failed("could not call a party together");
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
        }

        _called = squad.Count;

        if (_called < _least)
        {
            Undermanned++;

            BotSquads.Leave(member);

            return BotDoing.Failed($"only {_called} would come to look at ({_where.X}, {_where.Y})");
        }

        _squad = squad;

        squad.Charged = true;

        Parties++;

        _began = Core.TickCount;

        body.Say(_wage > 0 ? "Ground nobody has walked. Who is coming? There is coin in it." : "Ground nobody has walked. I am going to look at it.");

        logger.Information(
            "{Name} is taking {Count} of them to look at ({X}, {Y}), which nobody has stood in",
            body.Name,
            _called,
            _where.X,
            _where.Y
        );

        return BotDoing.Walk(_map, _where, BotArrival.Within(BotQuad.Side / 3), $"scouting ({_where.X}, {_where.Y})");
    }

    private BotDoing Walking(IBotSquadMember member, Mobile body)
    {
        var squad = member.Squad;

        if (squad == null || !ReferenceEquals(squad, _squad))
        {
            return BotDoing.Done("the party broke up on the road");
        }

        _called = squad.Count;

        if (Core.TickCount - _began >= CapMs)
        {
            Timedout++;

            BotQuad.Seen(_map, _where);
            Unreached++;

            Disband(member);

            return BotDoing.Done($"gave up on reaching ({_where.X}, {_where.Y})");
        }

        if (!body.InRange(_where, BotQuad.Side / 2))
        {
            return BotDoing.Walk(_map, _where, BotArrival.Within(BotQuad.Side / 3), $"scouting ({_where.X}, {_where.Y})");
        }

        Surveyed++;
        _read++;

        if (_kin)
        {
            BotQuad.Swept(_map, body.Location);
        }
        else
        {
            BotQuad.Seen(_map, body.Location);
        }

        if (_read < _rounds)
        {
            var next = NextSquare(body);

            if (next != Point3D.Zero)
            {
                _where = next;

                _began = Core.TickCount;

                return BotDoing.Walk(_map, _where, BotArrival.Within(BotQuad.Side / 3), $"scouting ({_where.X}, {_where.Y})");
            }
        }

        var paid = _wage > 0 ? Pay(body, squad, _wage) : 0;

        Disband(member);

        return BotDoing.Done(
            paid > 0
                ? $"read {_read} squares and paid {paid}gp for it"
                : $"read {_read} squares, the last at ({_where.X}, {_where.Y})"
        );
    }

    private static int Pay(Mobile captain, BotSquad squad, int wage)
    {
        var members = squad?.Members;

        if (members == null || captain?.Backpack == null)
        {
            return 0;
        }

        List<Mobile> owed = [];

        for (var i = 0; i < members.Count; i++)
        {
            var body = members[i]?.Self;

            if (body != null && body != captain && !body.Deleted && body.Backpack != null)
            {
                owed.Add(body);
            }
        }

        if (owed.Count == 0)
        {
            return 0;
        }

        var each = Math.Max(1, wage / owed.Count);
        var total = each * owed.Count;

        if (BotYield.Wealth(captain) - total < Solvent)
        {
            return 0;
        }

        var pack = captain.Backpack;
        var carried = pack.GetAmount(typeof(Gold));
        var fromPack = Math.Min(carried, total);
        var fromBank = total - fromPack;

        if (fromPack > 0 && !pack.ConsumeTotal(typeof(Gold), fromPack))
        {
            return 0;
        }

        if (fromBank > 0 && !Banker.Withdraw(captain, fromBank))
        {
            if (fromPack > 0)
            {
                pack.DropItem(new Gold(fromPack));
            }

            return 0;
        }

        for (var i = 0; i < owed.Count; i++)
        {
            owed[i].Backpack.DropItem(new Gold(each));
        }

        Wages += total;
        Paid += owed.Count;

        logger.Information(
            "{Name} paid {Total}gp to {Count} of them for walking into the unknown, {Each}gp apiece",
            captain.Name,
            total,
            owed.Count,
            each
        );

        return total;
    }

    public override bool Bend(IBotWilful bot)
    {
        var body = bot?.Self;

        if (body == null || _map == null || _squad == null)
        {
            return false;
        }

        BotQuad.Seen(_map, _where);
        Baulked++;

        if (_read >= _rounds)
        {
            return false;
        }

        var next = NextSquare(body);

        if (next == Point3D.Zero || next == _where)
        {
            return false;
        }

        _where = next;
        _began = Core.TickCount;

        return true;
    }

    public static long Baulked { get; private set; }

    public static long Unreached { get; private set; }

    public override void Drop(IBotWilful bot)
    {
        var body = bot?.Self;

        if (body == null || _map == null || _squad == null)
        {
            LetGo(bot);

            return;
        }

        if (body.InRange(_where, BotQuad.Side / 2))
        {
            LetGo(bot);

            return;
        }

        BotQuad.Seen(_map, _where);
        Unreached++;

        logger.Information(
            "{Name} never stood in ({X}, {Y}) and it has been marked read, so it will not be offered again",
            body.Name,
            _where.X,
            _where.Y
        );

        LetGo(bot);
    }

    private void LetGo(IBotWilful bot)
    {
        if (_squad != null && bot is IBotSquadMember member)
        {
            Disband(member);
        }
    }

    private bool Within(Point3D at) =>
        _bounds.Width <= 0 || _bounds.Height <= 0 || _bounds.Contains(new Point2D(at.X, at.Y));

    private static bool Reachable(Map map, Point3D from, Point3D at) =>
        BotReach.Ask(map, from, at, BotArrival.Within(BotQuad.Side / 3)) != BotReachVerdict.Sealed;

    public static int RoadLimit { get; set; } = 600;

    public static long Roadless { get; private set; }

    internal static bool Roadworthy(Map map, Point3D at)
    {
        if (!BotRoads.Ready)
        {
            return true;
        }

        if (!BotRoads.Covers(map, at.X, at.Y))
        {
            Roadless++;

            return false;
        }

        var road = BotRoads.FromHome(map, at.X, at.Y);

        if (road >= 0 && road <= RoadLimit)
        {
            return true;
        }

        Roadless++;

        return false;
    }

    private Point3D NextSquare(Mobile body)
    {
        for (var tries = 0; tries < Vets; tries++)
        {
            var next = BotQuad.Frontier(
                _map,
                body.Location,
                Range,
                at => Within(at) && Roadworthy(_map, at) && Reachable(_map, body.Location, at)
            );

            if (next == Point3D.Zero)
            {
                return Point3D.Zero;
            }

            var cramped = BotPath.Starved;

            if (BotPath.CanReach(_map, body.Location, next, BotArrival.Within(BotQuad.Side / 3), BotScoutmaster.VetMs))
            {
                return next;
            }

            if (BotPath.Starved != cramped)
            {
                return Point3D.Zero;
            }

            BotQuad.Seen(_map, next);
            Unreached++;
        }

        return Point3D.Zero;
    }

    public static int Vets { get; set; } = 3;

    private void Disband(IBotSquadMember member)
    {
        if (_kin)
        {
            _squad = null;

            return;
        }

        var squad = _squad ?? member?.Squad;

        if (squad != null)
        {
            squad.Charged = false;

            BotSquads.Disband(squad, "the errand that raised it is over");
        }

        BotSquads.Leave(member);

        _squad = null;
    }

    public static string Describe() =>
        $"{Parties} scouting parties set out, {Undermanned} could not raise {Least} bodies, {Surveyed} squares read, {Baulked} stepped past as unreachable, "
        + $"{Unreached} never stood in and retired off the frontier, {Roadless} passed over for lying past {RoadLimit} tiles of road from home, {Timedout} ran out of time; {Wages}gp paid to {Paid} volunteers";

    public static void Forget()
    {
        Parties = 0;
        Undermanned = 0;
        Surveyed = 0;
        Baulked = 0;
        Unreached = 0;
        Roadless = 0;
        Timedout = 0;
        Wages = 0;
        Paid = 0;
    }
}
