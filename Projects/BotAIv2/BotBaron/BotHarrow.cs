using System;
using System.Collections.Generic;
using Server.Items;
using Server.Logging;
using Server.Mobiles;
using Server.Regions;

namespace Server.BotAI.V2;

/// <summary>
/// Six bots taken to ground that has killed people, and kept there until it has been emptied or the
/// afternoon is gone.
///
/// <para>
/// <b>This is the shard's third reason for a company, and it is not a bigger patrol.</b> A muster forms
/// against one creature and ends when that creature is dead. A patrol — see <see cref="BotSweep"/> — is sent
/// to a square that reads dangerous <em>lately</em> and finishes when the reading falls, which is a
/// statement about frequency and can be satisfied by the trouble simply wandering off. A harrowing is
/// answerable to neither: it goes where people have actually died, it is finished by a count of corpses or
/// by a clock, and when it ends the square comes off the board altogether. Nothing else on this shard clears
/// anything — everything else knocks a number down and lets it climb back.
/// </para>
///
/// <para>
/// <b>Everything alive inside the box, and the exclusions are the interesting half.</b> Not "hostile", which
/// is what every other fight on the shard asks: hostility is a notoriety judgement and it lets a field of
/// harmless things stand between a company and the thing it came for. Bots are excluded structurally rather
/// than by a rule — they are players as far as the engine is concerned and this only ever looks at
/// creatures — and guarded ground is excluded outright, so a chase that leads into a town ends at the gate.
/// </para>
///
/// <para>
/// <b>It is worth what it hands to the five who came.</b> The Baron takes no share, so measured the ordinary
/// way — coin in his own pack — this work pays nothing and the ledger would have learned, correctly by its
/// own arithmetic, that leading companies into deadly ground is worthless. See <see cref="BotSquad.Won"/>:
/// the company keeps the figure, and this reads it.
/// </para>
/// </summary>
public sealed class BotHarrow : BotDeed
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotHarrow));

    public const string Trade = "harrow";

    public static int Side { get; set; } = 75;

    public static int Company { get; set; } = 6;

    public static int Grandmasters { get; set; } = 15;

    public static double Might { get; set; } = 4000.0;

    public static int Reagents { get; set; } = 10;

    public static double GrandmasterAt { get; set; } = 100.0;

    public static int Least { get; set; } = 3;

    public static int MusterMs { get; set; } = 300000;

    public static int RestMs { get; set; } = 600000;

    public static long Rested { get; private set; }

    private static readonly Dictionary<Serial, long> _resting = new();

    public static bool Resting(Mobile body)
    {
        if (body == null || !_resting.TryGetValue(body.Serial, out var until))
        {
            return false;
        }

        if (Core.TickCount - until < 0)
        {
            return true;
        }

        _resting.Remove(body.Serial);

        return false;
    }

    public static void Rest(Mobile body)
    {
        if (body == null)
        {
            return;
        }

        _resting[body.Serial] = Core.TickCount + RestMs;
        Rested++;
    }

    public static Point3D Square { get; set; }

    public static int Station { get; set; } = 2;

    public static int Assembly { get; set; } = 24;

    public static int Quota { get; set; } = 20;

    public static int CapMs { get; set; } = 1800000;

    public static int Reach { get; set; } = 200;

    public static int SweepMs { get; set; } = 2000;

    public static int Melee { get; set; } = 2;

    public static int Ranged { get; set; } = 2;

    public static int Medics { get; set; } = 1;

    public static int Sight { get; set; } = 40;

    public static int RoundMs { get; set; } = 90000;

    public static int MaxBends { get; set; } = 4;

    public static double Prior { get; set; } = 150.0;

    public static double WorkMinutes { get; set; } = 25.0;

    public static long Marches { get; private set; }

    public static long Undermanned { get; private set; }

    public static long Musters { get; private set; }

    public static long Emptied { get; private set; }

    public static long Timedout { get; private set; }

    public static long Killed { get; private set; }

    private readonly Map _map;

    private readonly Point3D _square;

    private readonly int _dead;

    private BotSquad _squad;

    private long _won;

    private long _began;

    private int _called;

    private int _kills;

    private bool _standing;

    private Mobile _fighting;

    private long _steppedTick;

    private int _round;

    private Point3D _post;

    private int _bends;

    private Mobile _quarry;

    private bool _marching;

    private int _wanted;

    private int Wanted => _wanted > 0 ? _wanted : Company;

    private int _marched;

    private int _here;

    private bool _mustering;

    private long _musteredTick;

    private bool _atMuster;

    private int _said0 = -1;

    private int _said1 = -1;

    private Point3D _muster;

    private long _sweptTick;

    public BotHarrow(Map map, Point3D square, int dead)
    {
        _map = map;
        _square = square;
        _dead = dead;
        _began = Core.TickCount;
    }

    public static string Describe() =>
        $"{Musters} musters called and {Called} bots called up, {Marches} of them marched, {Undermanned} could not raise the company asked for in {MusterMs / 60000} minutes of calling ({Rested} times the idea was then left alone for {RestMs / 60000} minutes), {Emptied} grounds emptied and {Timedout} run out of time, {Killed} things killed on them, {FellBack} times the leader put the harrow down and the company fell back with him, {TookUp} of them taken up again";

    public override string Kind => Trade;

    public override bool Still => !_marching;

    public override bool Braves => true;

    public override bool Steadfast => true;

    public override Map Map => _map;

    public override Point3D Where => _square;

    public override double Expects => Prior;

    public override double Minutes => WorkMinutes;

    public override SkillName? Trains => null;

    public override int Outlay => 0;

    public override int Made => _squad == null ? 0 : (int)Math.Max(0, _squad.Won - _won);

    public override bool Alongside => true;

    public override string Stage =>
        !_marching
            ? $"calling for volunteers to harrow ({_square.X}, {_square.Y}), where {_dead} have died: {_here} of {Wanted} gathered, {_called} called"
            : !_standing
                ? $"marching {_called} of us on ({_square.X}, {_square.Y}), where {_dead} have died"
                : $"harrowing ({_square.X}, {_square.Y}) with {_called} of us, {_kills} of {Quota} down";

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

        return _marching ? Harrowing(member, body) : Calling(member, body);
    }

    private BotDoing Calling(IBotSquadMember member, Mobile body)
    {
        var squad = member.Squad ?? BotSquads.Form(member);

        if (squad == null)
        {
            return BotDoing.Failed("could not call a company together");
        }

        var now = Core.TickCount;

        if (!ReferenceEquals(squad, _squad))
        {
            _squad = squad;
            _won = squad.Won;
        }

        _wanted = BotQuad.Levy(_map, _square, Company);

        squad.Ceiling = _wanted;
        squad.Charged = true;

        if (!_mustering)
        {
            _mustering = true;

            _muster = Rally(body);

            Musters++;

            logger.Information(
                "{Name} is calling for volunteers at ({MX}, {MY}) to harrow ({X}, {Y}), where {Dead} have died",
                body.Name,
                _muster.X,
                _muster.Y,
                _square.X,
                _square.Y,
                _dead
            );
        }

        if (now - _sweptTick >= SweepMs)
        {
            _sweptTick = now;

            Levy(squad, body);
        }

        _called = squad.Count;
        _here = Gathered(squad);

        if (!body.InRange(_muster, Station))
        {
            return BotDoing.Walk(_map, _muster, BotArrival.Within(Station), $"to the muster at ({_muster.X}, {_muster.Y})");
        }

        if (!_atMuster)
        {
            _atMuster = true;
            _musteredTick = now;

            logger.Information(
                "{Name} has reached the muster at ({MX}, {MY}) and is calling for {Wanted} to harrow ({X}, {Y}), where {Dead} have died",
                body.Name,
                _muster.X,
                _muster.Y,
                _wanted,
                _square.X,
                _square.Y,
                _dead
            );
        }

        if (_here != _said0 || _called != _said1)
        {
            _said0 = _here;
            _said1 = _called;

            logger.Information(
                "{Name}'s muster at ({MX}, {MY}): {Here} of {Wanted} standing in it, {Called} in the company, ceiling {Ceiling}, {Left}s left",
                body.Name,
                _muster.X,
                _muster.Y,
                _here,
                _wanted,
                _called,
                squad.Ceiling,
                Math.Max(0, (MusterMs - (now - _musteredTick)) / 1000)
            );
        }

        if (_here < _wanted && now - _musteredTick < MusterMs)
        {
            return BotDoing.Work($"calling at ({_muster.X}, {_muster.Y}): {_here} of {_wanted} gathered");
        }

        if (_here < _wanted)
        {
            Undermanned++;

            Rest(body);

            BotSquads.Leave(member);

            _squad = null;

            return BotDoing.Failed(
                $"only {_here} of the {_wanted} were standing in the square for ({_square.X}, {_square.Y}) after"
                + $" {MusterMs / 60000} minutes, of {_called} called"
            );
        }

        if (BotQuad.Damning(_map, _square) && !Fit(squad, out var ready, out var unarmed))
        {
            Unfit++;

            if (now - _musteredTick < MusterMs)
            {
                return body.InRange(_muster, Station)
                    ? BotDoing.Work($"calling for grandmasters at ({_muster.X}, {_muster.Y}): {ready} of {Grandmasters} fit")
                    : BotDoing.Walk(_map, _muster, BotArrival.Within(Station), $"calling for grandmasters at ({_muster.X}, {_muster.Y})");
            }

            Rest(body);

            BotSquads.Leave(member);

            _squad = null;

            return BotDoing.Failed(
                $"({_square.X}, {_square.Y}) is damned ground and {ready} of the {Grandmasters} answered fit for it,"
                + $" each needing a skill at {GrandmasterAt:F0} and {Might:F0} of strength"
                + (unarmed > 0 ? $"; {unarmed} more were casters with fewer than {Reagents} reagents" : "")
            );
        }

        _marching = true;
        _marched = _here;

        Marches++;

        squad.Quarry = Prey;

        _began = now;

        logger.Information(
            "{Name} is marching {Count} of the {Called} called on ({X}, {Y}), where {Dead} have died, after {Waited:F1} minutes of calling",
            body.Name,
            _here,
            _called,
            _square.X,
            _square.Y,
            _dead,
            (now - _musteredTick) / 60000.0
        );

        return BotDoing.Walk(_map, _square, BotArrival.Within(Side / 3), $"marching on ({_square.X}, {_square.Y})");
    }

    private BotDoing Harrowing(IBotSquadMember member, Mobile body)
    {
        var squad = member.Squad;

        if (squad == null || !ReferenceEquals(squad, _squad))
        {
            return BotDoing.Done("the company broke up on the road");
        }

        _called = squad.Count;

        if (_called < 2)
        {
            if (_marched > 0)
            {
                BotQuad.LostCompany(_map, _square, _marched);
            }

            return Finish(squad, "there was nobody left to harrow with", cleared: false);
        }

        Count(squad);

        if (_kills >= Quota)
        {
            Emptied++;

            return Finish(squad, $"{_kills} of them are dead and ({_square.X}, {_square.Y}) is done", cleared: true);
        }

        var now = Core.TickCount;

        if (now - _began >= CapMs)
        {
            Timedout++;

            return Finish(squad, $"{CapMs / 60000} minutes on ({_square.X}, {_square.Y}) was enough at {_kills} down", cleared: true);
        }

        var away = _standing ? Side : Side / 2;

        if (!body.InRange(_square, away))
        {
            _standing = false;

            return BotDoing.Walk(_map, _square, BotArrival.Within(Side / 3), $"marching on ({_square.X}, {_square.Y})");
        }

        if (!_standing)
        {
            _standing = true;
            _steppedTick = now;
            _round = 0;
            _post = Post(_round);

            logger.Information(
                "{Name}'s company is on ({X}, {Y}) and has begun walking it",
                body.Name,
                _square.X,
                _square.Y
            );
        }

        if (squad.Stance == BotSquadStance.Fighting && squad.Focus is { Deleted: false, Alive: true } fighting)
        {
            if (!ReferenceEquals(fighting, _fighting))
            {
                _fighting = fighting;

                if (member is IBotWilful wilful && wilful.Resolve != null)
                {
                    wilful.Resolve.StirredTick = now;
                }
            }

            return BotDoing.Work($"fighting {fighting.Name} with the company on ({_square.X}, {_square.Y}), {_kills} of {Quota} down");
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
            $"harrowing ({_square.X}, {_square.Y}), {_kills} of {Quota} down"
        );
    }

    private void Count(BotSquad squad)
    {
        if (_quarry != null && (_quarry.Deleted || !_quarry.Alive))
        {
            _kills++;
            Killed++;
            _quarry = null;
        }

        var focus = squad.Focus;

        if (focus is { Deleted: false, Alive: true } && !ReferenceEquals(focus, _quarry))
        {
            _quarry = focus;
        }
    }

    private Point3D Rally(Mobile body)
    {
        var gate = BotPopulation.Gate(_map, body.Location, _square);

        if (gate != Point3D.Zero)
        {
            return gate;
        }

        var named = Square != Point3D.Zero ? Square : BotPopulation.Where;

        if (BotStep.Settle(_map, named.X, named.Y, out var z))
        {
            return new Point3D(named.X, named.Y, z);
        }

        return named != Point3D.Zero ? named : body.Location;
    }

    private void Levy(BotSquad squad, Mobile body)
    {
        if (squad.Count >= Wanted)
        {
            return;
        }

        _muster = _muster != Point3D.Zero ? _muster : Rally(body);

        _called0.Clear();

        foreach (var mobile in _map.GetMobilesInRange<Mobile>(_muster, Reach))
        {
            if (mobile == body || mobile is not BotMobile other)
            {
                continue;
            }

            if (other.Squad != null || other is not IBotAlly { AbleToFight: true })
            {
                continue;
            }

            if (other.Class is not { } klass || klass.Role == BotRole.Producer)
            {
                continue;
            }

            _called0.Add(other);
        }

        _called0.Sort((a, b) => Apart(a, body).CompareTo(Apart(b, body)));

        Take(squad, BotRole.Melee, Melee);
        Take(squad, BotRole.Ranged, Ranged);
        Take(squad, BotRole.Medic, Medics);
        Take(squad, null, Wanted);
    }

    public static int Musterable()
    {
        var bots = BotPopulation.Bots;
        var ready = 0;

        for (var i = 0; i < bots.Count; i++)
        {
            var body = bots[i];

            if (body is not { Deleted: false, Alive: true })
            {
                continue;
            }

            if (!Master(body) || BotThreat.Power(body) < Might)
            {
                continue;
            }

            if (BotGrimoire.Book(body) != null && Herbs(body) < Reagents)
            {
                continue;
            }

            ready++;
        }

        return ready;
    }

    private static bool Fit(BotSquad squad, out int ready, out int unarmed)
    {
        ready = 0;
        unarmed = 0;

        if (squad == null)
        {
            return false;
        }

        var members = squad.Members;

        for (var i = 0; i < members.Count; i++)
        {
            var body = members[i]?.Self;

            if (body is not { Deleted: false, Alive: true })
            {
                continue;
            }

            if (!Master(body) || BotThreat.Power(body) < Might)
            {
                continue;
            }

            if (BotGrimoire.Book(body) != null && Herbs(body) < Reagents)
            {
                unarmed++;

                continue;
            }

            ready++;
        }

        return ready >= Grandmasters;
    }

    private int Gathered(BotSquad squad)
    {
        if (squad == null || _muster == Point3D.Zero)
        {
            return 0;
        }

        var here = 0;
        var members = squad.Members;

        for (var i = 0; i < members.Count; i++)
        {
            var body = members[i]?.Self;

            if (body is { Deleted: false, Alive: true } && body.Map == _map && body.InRange(_muster, Assembly))
            {
                here++;
            }
        }

        return here;
    }

    private static bool Master(Mobile body)
    {
        var skills = body?.Skills;

        if (skills == null)
        {
            return false;
        }

        for (var i = 0; i < skills.Length; i++)
        {
            if (skills[i].Base >= GrandmasterAt && !BotMobile.Granted(skills[i].SkillName))
            {
                return true;
            }
        }

        return false;
    }

    private static int Herbs(Mobile body)
    {
        var pack = body?.Backpack;

        if (pack == null)
        {
            return 0;
        }

        var held = 0;

        foreach (var item in pack.Items)
        {
            if (item is BaseReagent { Deleted: false } herb)
            {
                held += herb.Amount;
            }
        }

        return held;
    }

    public static long Unfit { get; private set; }

    private void Take(BotSquad squad, BotRole? role, int most)
    {
        var taken = 0;

        for (var i = 0; i < _called0.Count && taken < most && squad.Count < Wanted; i++)
        {
            var other = _called0[i];

            if (other.Squad != null || role != null && other.Class?.Role != role)
            {
                continue;
            }

            if (BotSquads.Join(squad, other))
            {
                taken++;
                Called++;
            }
        }
    }

    private static int Apart(Mobile a, Mobile b) =>
        Math.Max(Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));

    private static readonly System.Collections.Generic.List<BotMobile> _called0 = [];

    public static long Called { get; private set; }

    private BaseCreature Prey(Mobile leader)
    {
        var map = leader?.Map;

        if (map == null || map == Map.Internal || map != _map)
        {
            return null;
        }

        BaseCreature nearest = null;
        var closest = int.MaxValue;
        var edge = Side / 2;

        foreach (var creature in map.GetMobilesInRange<BaseCreature>(leader.Location, Sight))
        {
            if (creature is not { Deleted: false, Alive: true } or BaseVendor)
            {
                continue;
            }

            if (creature.Controlled || creature.Summoned || creature.IsDeadBondedPet)
            {
                continue;
            }

            if (Math.Abs(creature.X - _square.X) > edge || Math.Abs(creature.Y - _square.Y) > edge)
            {
                continue;
            }

            if (creature.Region?.IsPartOf<GuardedRegion>() == true)
            {
                continue;
            }

            if (!BotThreat.Hostile(leader, creature))
            {
                continue;
            }

            var apart = Math.Max(Math.Abs(creature.X - leader.X), Math.Abs(creature.Y - leader.Y));

            if (apart >= closest)
            {
                continue;
            }

            closest = apart;
            nearest = creature;
        }

        return nearest;
    }

    private Point3D Post(int round)
    {
        var reach = Math.Max(2, Side / 3);

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

    private BotDoing Finish(BotSquad squad, string why, bool cleared)
    {
        if (cleared)
        {
            BotPeril.Cleared(_map, _square, Side / 2);

            var quad = BotQuad.Known(_map, _square);

            if (quad != null)
            {
                var around = BotQuad.Around(quad, madeIfNew: false);

                for (var i = 0; i < around.Count; i++)
                {
                    BotQuad.Cleared(around[i]);
                }
            }

            BotCity.Claim(_map, _square, squad.Members);
        }

        Release(squad);

        return BotDoing.Done($"{why} — {_called} of us, {Made}gp between them");
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

        return true;
    }

    public static long FellBack { get; private set; }

    public static long TookUp { get; private set; }

    public override void Paused(IBotWilful bot)
    {
        FellBack++;

        if (_squad == null)
        {
            return;
        }

        _squad.Disengage("the leader has put the harrow down");

        logger.Information(
            "{Name} put the harrow of ({X}, {Y}) down; the company of {Count} falls back with him",
            bot?.Self?.Name,
            _square.X,
            _square.Y,
            _squad.Count
        );
    }

    public override void Resumed(IBotWilful bot)
    {
        TookUp++;

        var now = Core.TickCount;

        _steppedTick = now;
        _sweptTick = now;

        if (_mustering)
        {
            _musteredTick = now;
        }

        logger.Information(
            "{Name} took the harrow of ({X}, {Y}) up again with {Count} in the company",
            bot?.Self?.Name,
            _square.X,
            _square.Y,
            _squad?.Count ?? 0
        );
    }

    public override void Drop(IBotWilful bot)
    {
        Release(_squad);

        if (bot is IBotSquadMember member && member.Squad != null && ReferenceEquals(member.Squad, _squad))
        {
            BotSquads.Leave(member);
        }

        _squad = null;
        _marching = false;
        _mustering = false;
    }

    private void Release(BotSquad squad)
    {
        if (squad == null)
        {
            return;
        }

        squad.Charged = false;
        squad.Quarry = null;
        squad.Ceiling = BotSquad.MaxSize;
    }

    public static void Forget()
    {
        Marches = 0;
        Musters = 0;
        Rested = 0;
        _resting.Clear();
        Called = 0;
        Undermanned = 0;
        Emptied = 0;
        Timedout = 0;
        Killed = 0;
        FellBack = 0;
        TookUp = 0;
    }
}
