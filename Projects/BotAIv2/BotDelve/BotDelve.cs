using System;
using System.Collections.Generic;
using Server.Logging;
using Server.Mobiles;
using Server.Regions;

namespace Server.BotAI.V2;

/// <summary>
/// Five bots taken down a dungeon by the maker of their guild, and kept there for twenty minutes or twenty
/// corpses.
///
/// <para>
/// <b>Patrick's order of 11.09.2026.</b> A guild's leader calls a delve, five go down — a fighter, the
/// leader, and three more of whatever answers — they run the tunnels and fight everything that moves, and
/// what they take is divided at the end: half to the leader and the rest evenly between the other four. It
/// is the first thing on this shard that a guild does together for its own sake rather than for a hall or
/// a piece of ground.
/// </para>
///
/// <para>
/// <b>They are put down there rather than walking, and that is a fact about the map rather than a
/// convenience.</b> The dungeon block is cut into its own quarter of the world from x 5120 eastward and has
/// no walkable connection to the island at all — the engine's way in is a teleporter on an entrance, and a
/// bot asked to walk to one is asked for a road that does not exist. This shard has measured what that
/// costs: two and a half thousand walk failures in a session, a third of them at three destinations nothing
/// could reach. So the descent is the door, the way up is the same door, and <see cref="BotDelveParty"/>
/// holds the promise that everybody who went down comes back up.
/// </para>
///
/// <para>
/// <b>The muster is the Baron's, because his is the one on this shard that has been made to work.</b> A
/// call stands in a place while the population walks past it and whoever is free falls in; nobody is asked
/// in advance, because a population that is working well has no volunteers standing about in it. What is
/// different here is who is called first: this guild's own members, before anybody else, which is what
/// makes a delve a thing a band does rather than a thing five strangers do.
/// </para>
///
/// <para>
/// <b>One fighter is a condition and the other three are not.</b> A party of a crafter and four healers is
/// a party that will spend twenty minutes bandaging each other, so the line is required; past that, the
/// order says "three others who answer" and this takes them in the order they answer.
/// </para>
/// </summary>
public sealed class BotDelve : BotDeed
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotDelve));

    public const string Trade = "delve";

    public static int Company { get; set; } = 5;

    public static int Fighters { get; set; } = 1;

    public static int Quota { get; set; } = 20;

    public static int CapMs { get; set; } = 1200000;

    public static int MusterMs { get; set; } = 180000;

    public static int Reach { get; set; } = 200;

    public static int Assembly { get; set; } = 24;

    public static int Station { get; set; } = 2;

    public static int Sight { get; set; } = 18;

    public static int RoomMs { get; set; } = 60000;

    public static int SweepMs { get; set; } = 5000;

    public static int LevyMs { get; set; } = 2000;

    public static int RestMs { get; set; } = 600000;

    public static int MaxBends { get; set; } = 200;

    public static int MaxStrays { get; set; } = 2;

    public static double Prior { get; set; } = 260.0;

    public static double WorkMinutes { get; set; } = 25.0;

    public static long Calls { get; private set; }

    public static long Marches { get; private set; }

    public static long Undermanned { get; private set; }

    public static long Lineless { get; private set; }

    public static long Called { get; private set; }

    public static long Killed { get; private set; }

    public static long Emptied { get; private set; }

    public static long Timedout { get; private set; }

    public static long Broken { get; private set; }

    public static long Bends { get; private set; }

    public static long Shifted { get; private set; }

    public static long Walled { get; private set; }

    public static long Leaderless { get; private set; }

    public static long Strayed { get; private set; }

    private static readonly Dictionary<Serial, long> _resting = [];

    public static bool Resting(Mobile body) =>
        body != null && _resting.TryGetValue(body.Serial, out var when) && Core.TickCount - (when + RestMs) < 0;

    public static void Rest(Mobile body)
    {
        if (body != null)
        {
            _resting[body.Serial] = Core.TickCount;
        }
    }

    private BotDungeon.Deep _deep;

    private readonly Map _map;

    private BotSquad _squad;

    private BotDelveParty _party;

    private Point3D _muster;

    private Point3D _room;

    private bool _calling;

    private bool _down;

    private int _strays;

    private bool _atMuster;

    private long _musteredTick;

    private long _began;

    private long _sweptTick;

    private long _leviedTick;

    private long _roomTick;

    private int _which;

    private int _kills;

    private int _here;

    private int _count;

    private Mobile _quarry;

    private int _said0 = -1;

    private int _said1 = -1;

    private int _bends;

    private BotHalls.Hall _hall;

    public BotDelve(Map map, BotDungeon.Deep deep)
    {
        _map = map;
        _deep = deep;
        _began = Core.TickCount;

        _muster = Rally(null);
    }

    public override string Kind => Trade;

    public override void Taken(IBotWilful bot) => BotDelver.Stamp(bot?.Self, _deep);

    public override bool Braves => true;

    public override Map Map => _map;

    public override Point3D Where => _muster;

    public override double Expects => Prior;

    public override double Minutes => WorkMinutes;

    public override SkillName? Trains => null;

    public override int Outlay => 0;

    public override int Made => _party == null ? 0 : _party.Owed(_party.Leader);

    public override bool Alongside => true;

    public override bool Committed => _down;

    public override bool Still => _down;

    public override string Stage =>
        !_down
            ? $"calling a party for {_deep?.Name}: {_here} of {Company} gathered"
            : $"in {_deep?.Name} with {_count} of us, {_kills} of {Quota} down, {_party?.Left ?? 0} raisings left";

    public override BotDoing Advance(IBotWilful bot)
    {
        var body = bot?.Self;

        if (body == null || _map == null || _map == Map.Internal || _deep == null)
        {
            return BotDoing.Failed("no body, or nowhere to go");
        }

        if (bot is not IBotSquadMember member)
        {
            return BotDoing.Failed("not the sort of thing that leads a party");
        }

        return _down ? Delving(member, body) : Calling(member, body);
    }

    private BotDoing Calling(IBotSquadMember member, Mobile body)
    {
        var squad = member.Squad ?? BotSquads.Form(member);

        if (squad == null)
        {
            return BotDoing.Failed("could not call a party together");
        }

        _squad = squad;
        squad.Ceiling = Company;
        squad.Charged = true;

        var now = Core.TickCount;

        if (!_calling)
        {
            _calling = true;

            if (_muster == Point3D.Zero)
            {
                _muster = Rally(body);
            }

            Calls++;

            logger.Information(
                "{Name} is calling a party at ({X}, {Y}) for a delve into {Deep}, where the worst of them is worth {Worst}",
                body.Name,
                _muster.X,
                _muster.Y,
                _deep.Name,
                _deep.Worst.ToString("F0")
            );
        }

        if (!body.InRange(_muster, Station))
        {
            return BotDoing.Walk(_map, _muster, BotArrival.Within(Station), $"to the muster at ({_muster.X}, {_muster.Y})");
        }

        if (!_atMuster)
        {
            _atMuster = true;
            _musteredTick = now;
        }

        if (now - _leviedTick >= LevyMs)
        {
            _leviedTick = now;

            Levy(squad, body);
        }

        _count = squad.Count;
        _here = Gathered(squad);

        if (_here != _said0 || _count != _said1)
        {
            _said0 = _here;
            _said1 = _count;

            logger.Information(
                "{Name}'s party for {Deep}: {Here} of {Want} standing in it, {Called} in the company, {Left}s left",
                body.Name,
                _deep.Name,
                _here,
                Company,
                _count,
                Math.Max(0, (MusterMs - (now - _musteredTick)) / 1000)
            );
        }

        if (_here < Company && now - _musteredTick < MusterMs)
        {
            return BotDoing.Work($"calling at ({_muster.X}, {_muster.Y}): {_here} of {Company} gathered");
        }

        if (_here < Company)
        {
            Undermanned++;
            Rest(body);
            BotSquads.Leave(member);
            _squad = null;

            return BotDoing.Failed(
                $"only {_here} of the {Company} stood up for {_deep.Name} in {MusterMs / 60000} minutes, of {_count} called"
            );
        }

        if (Line(squad) < Fighters)
        {
            if (now - _musteredTick < MusterMs)
            {
                return BotDoing.Work($"calling for a fighter at ({_muster.X}, {_muster.Y})");
            }

            Lineless++;
            Rest(body);
            BotSquads.Leave(member);
            _squad = null;

            return BotDoing.Failed($"nobody who could hold a line answered the call for {_deep.Name}");
        }

        return Descend(squad, body);
    }

    private BotDoing Descend(BotSquad squad, Mobile body)
    {
        var planned = _deep;

        _deep = BotDelver.Recheck(squad, body, _deep, out var strength) ?? planned;

        if (_deep != planned)
        {
            logger.Information(
                "{Name}'s party for {Planned} is {Strength} of proven strength against its worst ({Wanted} wanted), so it goes down {Deep} instead",
                body.Name,
                planned.Name,
                strength.ToString("F0"),
                (planned.Worst * BotDelver.Odds).ToString("F0"),
                _deep.Name
            );
        }

        _hall = BotHalls.Largest(_deep);
        _which = Utility.Random(Math.Max(1, _hall?.Rooms.Count ?? _deep.Rooms.Count));

        _room = _hall != null
            ? BotHalls.Room(_hall, _which)
            : BotDungeon.Room(_map, _deep, _which);

        if (_room == Point3D.Zero)
        {
            return BotDoing.Failed($"{_deep.Name} has no room anybody could be put down in");
        }

        _party = new BotDelveParty(_deep, body as BotMobile);

        var went = 0;
        var members = squad.Members;

        for (var i = 0; i < members.Count; i++)
        {
            if (members[i]?.Self is not BotMobile { Deleted: false, Alive: true } who)
            {
                continue;
            }

            _party.Add(who);
            Put(who);
            went++;
        }

        if (went == 0)
        {
            _party.Settle("nobody could be put down there");

            return BotDoing.Failed($"nobody could be put down in {_deep.Name}");
        }

        squad.Ceiling = went;
        squad.Quarry = Prey;

        _down = true;
        _count = went;
        _began = Core.TickCount;
        _roomTick = _began;
        _deep.Runs++;
        Marches++;

        logger.Information(
            "{Name} has taken {Count} into {Deep} at ({X}, {Y}): {Quota} of them to kill or {Minutes} minutes, {Raisings} raisings between them",
            body.Name,
            went,
            _deep.Name,
            _room.X,
            _room.Y,
            Quota,
            CapMs / 60000,
            _party.Left
        );

        return BotDoing.Work($"in {_deep.Name}");
    }

    private void Put(BotMobile who)
    {
        var map = _map;

        for (var pass = 0; pass < 2; pass++)
        {
            for (var attempt = 0; attempt < 12; attempt++)
            {
                var x = _room.X + Utility.RandomMinMax(-2, 2);
                var y = _room.Y + Utility.RandomMinMax(-2, 2);

                if (!map.CanSpawnMobile(x, y, _room.Z - 8, _room.Z + 8, false, false, out var z))
                {
                    continue;
                }

                if (pass == 0 && BotStep.Mask(map, x, y, (sbyte)Math.Clamp(z, sbyte.MinValue, sbyte.MaxValue)).WalkMask == 0)
                {
                    continue;
                }

                who.MoveToWorld(new Point3D(x, y, z), map);
                who.Journey?.Finish();

                return;
            }
        }

        who.MoveToWorld(_room, map);
        who.Journey?.Finish();
    }

    private BotDoing Delving(IBotSquadMember member, Mobile body)
    {
        var now = Core.TickCount;

        if (now - _sweptTick >= SweepMs)
        {
            _sweptTick = now;

            _party?.Sweep();
        }

        Count();

        if (_kills >= Quota)
        {
            Emptied++;

            return Finish($"{_kills} of them are down in {_deep.Name}");
        }

        if (now - _began >= CapMs)
        {
            Timedout++;

            return Finish($"{CapMs / 60000} minutes in {_deep.Name} was enough at {_kills} down");
        }

        var down = _party?.Down ?? 0;

        if (down < 2)
        {
            Broken++;

            return Finish($"there was nobody left to delve with in {_deep.Name}");
        }

        if (!_deep.Holds(body.Location))
        {
            if (body.Alive && _strays < MaxStrays)
            {
                _strays++;
                Strayed++;

                logger.Information(
                    "{Name} was leading a delve into {Deep} and was found at ({X}, {Y}); it has been put back with its party",
                    body.Name,
                    _deep.Name,
                    body.Location.X,
                    body.Location.Y
                );

                if (body is BotMobile lead)
                {
                    Put(lead);
                }

                return BotDoing.Work($"back in {_deep.Name}: {_kills} of {Quota} down");
            }

            Leaderless++;

            return Finish($"whoever was leading it was no longer in {_deep.Name}");
        }

        _count = down;

        if (now - _roomTick >= RoomMs)
        {
            _roomTick = now;
            _room = Next(body, ++_which);

            if (_room == Point3D.Zero)
            {
                return Finish($"{_deep.Name} ran out of rooms");
            }

            return BotDoing.Walk(_map, _room, BotArrival.Within(3), $"on to ({_room.X}, {_room.Y}) in {_deep.Name}");
        }

        if (_room == Point3D.Zero)
        {
            return Finish($"{_deep.Name} ran out of rooms");
        }

        if (Prey(body) != null)
        {
            return BotDoing.Work($"fighting in {_deep.Name}: {_kills} of {Quota} down");
        }

        return body.InRange(_room, 3)
            ? BotDoing.Work($"in {_deep.Name}: {_kills} of {Quota} down")
            : BotDoing.Walk(_map, _room, BotArrival.Within(3), $"on to ({_room.X}, {_room.Y}) in {_deep.Name}");
    }

    private Point3D Next(Mobile body, int which)
    {
        var hall = _hall;

        if (body != null && BotHalls.Ready(_deep))
        {
            var standing = BotHalls.Holding(_deep, body.Location);

            if (standing != null)
            {
                hall = _hall = standing;
            }
        }

        return hall != null ? BotHalls.Room(hall, which) : BotDungeon.Room(_map, _deep, which);
    }

    private void Count()
    {
        if (_quarry != null && (_quarry.Deleted || !_quarry.Alive))
        {
            _kills++;
            Killed++;
            _quarry = null;
        }

        var focus = _squad?.Focus;

        if (focus is { Deleted: false, Alive: true } && !ReferenceEquals(focus, _quarry))
        {
            _quarry = focus;
        }
    }

    private BotDoing Finish(string why)
    {
        var pot = _party?.Pot ?? 0;

        _party?.Settle(why);
        Release(_squad);

        return BotDoing.Done($"{why} — {_count} of us, {pot}gp between them");
    }

    private Point3D Rally(Mobile body)
    {
        var where = BotPopulation.Where;

        if (where != Point3D.Zero && BotStep.Settle(_map, where.X, where.Y, out var z))
        {
            return new Point3D(where.X, where.Y, z);
        }

        return body?.Location ?? where;
    }

    private void Levy(BotSquad squad, Mobile body)
    {
        if (squad.Count >= Company)
        {
            return;
        }

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

            if (BotDelveParty.Delving(other))
            {
                continue;
            }

            if (other.Resolve?.Deed is { Alongside: true })
            {
                continue;
            }

            if (!BotProvision.Fit(other, out _))
            {
                continue;
            }

            _called0.Add(other);
        }

        _called0.Sort(
            (a, b) =>
            {
                var mine = Mates(body, b).CompareTo(Mates(body, a));

                return mine != 0 ? mine : Apart(a, body).CompareTo(Apart(b, body));
            }
        );

        Take(squad, BotRole.Melee, Fighters);
        Take(squad, null, Company);
    }

    private static int Mates(Mobile leader, Mobile other) => BotGuilds.Same(leader, other) ? 1 : 0;

    private void Take(BotSquad squad, BotRole? role, int most)
    {
        var taken = 0;

        for (var i = 0; i < _called0.Count && taken < most && squad.Count < Company; i++)
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

    private static int Line(BotSquad squad)
    {
        var line = 0;
        var members = squad.Members;

        for (var i = 0; i < members.Count; i++)
        {
            if (members[i]?.Self is BotMobile { Deleted: false, Alive: true } who && who.Class?.Role == BotRole.Melee)
            {
                line++;
            }
        }

        return line;
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

    private static int Apart(Mobile a, Mobile b) =>
        Math.Max(Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));

    private static readonly List<BotMobile> _called0 = [];

    private BaseCreature Prey(Mobile leader)
    {
        var map = leader?.Map;

        if (map == null || map != _map || _deep == null)
        {
            return null;
        }

        BaseCreature nearest = null;
        var closest = int.MaxValue;

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

            if (!_deep.Holds(creature.Location))
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

            if (BotQuarry.Shunned(creature))
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

    public override bool Bend(IBotWilful bot)
    {
        if (++_bends > MaxBends)
        {
            Walled++;

            return false;
        }

        if (!_down)
        {
            var body = bot?.Self;

            if (body == null || body.Location == _muster)
            {
                return false;
            }

            Shifted++;
            _muster = body.Location;

            return true;
        }

        Bends++;

        _roomTick = Core.TickCount;
        _room = Next(bot?.Self, ++_which);

        return _room != Point3D.Zero;
    }

    public override void Drop(IBotWilful bot)
    {
        var fell = bot?.Self is { Deleted: false, Alive: false };

        _party?.Settle(fell ? "whoever was leading it fell" : "the errand was let go");

        Release(_squad);

        if (bot is IBotSquadMember member && member.Squad != null && ReferenceEquals(member.Squad, _squad))
        {
            BotSquads.Leave(member);
        }

        _squad = null;
        _down = false;
        _calling = false;
    }

    private static void Release(BotSquad squad)
    {
        if (squad == null)
        {
            return;
        }

        squad.Charged = false;
        squad.Quarry = null;
        squad.Ceiling = BotSquad.MaxSize;
    }

    public static string Describe() =>
        Calls == 0
            ? "no delve has been called"
            : $"{Calls} delves called and {Called} bots called up, {Marches} parties went down; {Undermanned} could not raise "
            + $"{Company} in {MusterMs / 60000} minutes and {Lineless} could raise nobody to hold a line; "
            + $"{Emptied} came out on {Quota} corpses, {Timedout} on the {CapMs / 60000} minutes, {Broken} because too few were left, {Leaderless} because whoever led them was no longer down there "
            + $"and {Walled} because the walls would not let them past ({Bends} refused roads shrugged off, {Shifted} musters moved to the caller's own tile, {Strayed} leaders fetched back out of the world); "
            + $"{Killed} things killed down there; {BotHalls.Describe()}; {BotDelveParty.Describe()}";

    public static void Forget()
    {
        Calls = 0;
        Marches = 0;
        Undermanned = 0;
        Lineless = 0;
        Called = 0;
        Killed = 0;
        Emptied = 0;
        Timedout = 0;
        Broken = 0;
        Bends = 0;
        Shifted = 0;
        Walled = 0;
        Leaderless = 0;
        Strayed = 0;
        _resting.Clear();
        _called0.Clear();

        BotDelveParty.Forget();
    }
}
