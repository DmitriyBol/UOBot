using System;
using System.Collections.Generic;
using Server.Engines.Pathing.Tiered;
using Server.Logging;
using Server.Mobiles;
using Server.Regions;
using Server.Text;

namespace Server.BotAI.V2;

/// <summary>
/// A party taken down a dungeon by a member of its guild — two for every creature of the dungeon's worst room, five at the
/// fewest — and kept there for twenty minutes or twenty corpses.
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
///
/// <para>
/// <b>Five became the fewest, not the number (30.09.2026, build 359; Patrick: "a multiple above the mobs in the dungeon").</b>
/// A party calls two for every creature of its dungeon's worst room and goes down only outnumbering it. See <see cref="PartyFor"/>.
/// </para>
/// </summary>
public sealed class BotDelve : BotDeed
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotDelve));

    public const string Trade = "delve";

    public static int Company { get; set; } = 5;

    public static int PartyFor(Map map, BotDungeon.Deep deep)
    {
        var want = Math.Max(Company, RoomOf(map, deep) * Math.Max(1, BotProving.PartyMultiple));

        return Math.Min(want, Math.Max(Company, BotDelver.HeadsPerParty));
    }

    public static int Least(Map map, BotDungeon.Deep deep) =>
        Math.Min(PartyFor(map, deep), Math.Max(Fewest, RoomOf(map, deep) + 1));

    private static readonly Dictionary<string, (int Most, int Count)> _worstRooms = [];

    public static int RoomOf(Map map, BotDungeon.Deep deep)
    {
        if (map == null || map == Map.Internal || deep == null)
        {
            return 0;
        }

        var most = BotProving.MostFoes;

        if (_worstRooms.TryGetValue(deep.Name, out var had) && had.Most == most)
        {
            return had.Count;
        }

        var count = BotRoomTrial.Worst(map, deep, most).Count;

        if (count > 0 || BotDungeon.Surveyed)
        {
            _worstRooms[deep.Name] = (most, count);
        }

        return count;
    }

    private static string Sizes()
    {
        if (_worstRooms.Count == 0)
        {
            return "no dungeon's worst room read yet";
        }

        var say = ValueStringBuilder.Create(256);

        try
        {
            foreach (var (name, room) in _worstRooms)
            {
                if (say.Length > 0)
                {
                    say.Append(", ");
                }

                var want = Math.Min(Math.Max(Company, room.Count * Math.Max(1, BotProving.PartyMultiple)), Math.Max(Company, BotDelver.HeadsPerParty));
                var least = Math.Min(want, Math.Max(Fewest, room.Count + 1));

                say.Append($"{name} {want} against {room.Count} ({least} at the fewest)");
            }

            return say.ToString();
        }
        finally
        {
            say.Dispose();
        }
    }

    public static int Fighters { get; set; } = 1;

    public static int Fewest { get; set; } = 3;

    public static int LineReach { get; set; } = 400;

    public static long LinesFar { get; private set; }

    public static long LineAway { get; private set; }

    public static long Heads { get; private set; }

    public static int Quota { get; set; } = 20;

    public static int CapMs { get; set; } = 1200000;

    public static int MusterMs { get; set; } = 180000;

    public static int Reach { get; set; } = 320;

    public static int Assembly { get; set; } = 24;

    public static int MarchMs { get; set; } = 900000;

    public static int ArriveMs { get; set; } = 120000;

    public static int LeaveMs { get; set; } = 300000;

    public static long Walked { get; private set; }

    public static long Carried { get; private set; }

    public static long Unreached { get; private set; }

    public static long WalkedOut { get; private set; }

    public static long LeftBelow { get; private set; }

    public static int Station { get; set; } = 2;

    public static int MusterBack { get; set; } = 48;

    public static int MarchSpread { get; set; } = 8;

    public static int MarchLost { get; set; } = 60;

    public static int HoldMs { get; set; } = 90000;

    public static int MouthHoldMs { get; set; } = 180000;

    public static int MouthTiles { get; set; } = 12;

    public static double GoOnShare { get; set; } = 0.6;

    public static int MarchFightMs { get; set; } = 0;

    public static int MarchBends { get; set; } = 2;

    public static long Stood { get; private set; }

    public static long WentOn { get; private set; }

    public static long TurnedBack { get; private set; }

    public static long BroughtBack { get; private set; }

    public static long MarchRefused { get; private set; }

    public static long MusteredForward { get; private set; }

    public static long Held { get; private set; }

    public static long LeftBehind { get; private set; }

    public static long Pressed { get; private set; }

    public static long Ungathered { get; private set; }

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

    private bool _marching;

    private static readonly System.Collections.Generic.List<BotDelve> _callsNow = [];

    private Mobile _caller;

    private long _calledTick;

    private static readonly System.Collections.Generic.List<(Mobile Leader, Map Map, Point3D At, long Tick)> _offersNow = [];

    public static int OfferHoldMs { get; set; } = 15000;

    public static void Offering(Mobile leader)
    {
        if (leader?.Map != null)
        {
            _offersNow.Add((leader, leader.Map, leader.Location, Core.TickCount));
        }
    }

    public static bool CallingNear(Mobile body, int reach)
    {
        if (body == null)
        {
            return false;
        }

        var now = Core.TickCount;

        for (var i = _offersNow.Count - 1; i >= 0; i--)
        {
            var offer = _offersNow[i];

            if (now - offer.Tick > OfferHoldMs || offer.Leader is not { Deleted: false, Alive: true })
            {
                _offersNow.RemoveAt(i);

                continue;
            }

            if (offer.Map == body.Map && offer.Leader != body && Utility.InRange(offer.At, body.Location, reach)
                && !BotRegard.Hostile(offer.Leader, body))
            {
                return true;
            }
        }

        for (var i = _callsNow.Count - 1; i >= 0; i--)
        {
            var call = _callsNow[i];

            if (!call._calling || call._marching || call._caller is not { Deleted: false, Alive: true }
                || now - call._calledTick > MusterMs * 2L)
            {
                _callsNow.RemoveAt(i);

                continue;
            }

            if (call._map == body.Map && call._caller != body && Utility.InRange(call._muster, body.Location, reach)
                && !BotRegard.Hostile(call._caller, body))
            {
                return true;
            }
        }

        return false;
    }

    private long _marchTick;

    private long _holdTick;

    private long _crossTick;

    private long _fightTick;

    private long _fought;

    private Mobile _foe;

    private double _setOut;

    private int _setOutCount;

    private int _judged;

    private Point3D _mouth;

    private bool _atMouth;

    private bool _returning;

    private int _marchBends;

    private bool OtherLand(Point3D a, Point3D b, int apart)
    {
        var la = BotGates.LandOf(_map, a);
        var lb = BotGates.LandOf(_map, b);

        return la >= 0 && lb >= 0 ? la != lb : apart > 1000;
    }

    private BotPassage _passage;

    private bool _leaving;

    private long _leaveTick;

    private string _why;

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

    private int _want;

    private int _least;

    private void Size()
    {
        _want = PartyFor(_map, _deep);
        _least = Least(_map, _deep);
    }

    public BotDelve(Map map, BotDungeon.Deep deep)
    {
        _map = map;
        _deep = deep;
        _began = Core.TickCount;

        Size();

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
        _leaving
            ? $"out of {_deep?.Name} by its mouth: {_why}"
            : _returning
                ? $"turning back from {_deep?.Name} with {_squad?.Count ?? 0}: {_why}"
            : _marching
                ? $"marching {_count} to the mouth of {_deep?.Name}"
                : !_down
                    ? $"calling a party for {_deep?.Name}: {_here} of {_want} gathered"
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

        return _leaving
            ? Leaving(member, body)
            : _down
                ? Delving(member, body)
                : _marching
                    ? Marching(member, body)
                    : Calling(member, body);
    }

    private BotDoing Marching(IBotSquadMember member, Mobile body)
    {
        var now = Core.TickCount;
        var squad = member.Squad;

        if (squad == null)
        {
            return BotDoing.Failed("the party came apart on the way to the mouth");
        }

        if (_passage != null)
        {
            if (_passage.Lead(body, out var through))
            {
                return through;
            }

            _passage = null;
        }

        if (!_returning && _deep.Holds(body.Location))
        {
            return Arrived(squad, body);
        }

        if (squad.Stance == BotSquadStance.Fighting && squad.Focus is { Deleted: false, Alive: true } foe)
        {
            return StandTo(member, squad, body, foe, now);
        }

        if (_fightTick != 0)
        {
            _fought += now - _fightTick;
            _fightTick = 0;
            _foe = null;
        }

        if (now - _marchTick - (MarchFightMs > 0 ? Math.Min(_fought, MarchFightMs) : _fought) >= MarchMs)
        {
            Unreached++;
            Release(squad);
            BotSquads.Leave(member);
            _squad = null;
            _marching = false;

            return BotDoing.Failed(
                _returning
                    ? $"could not bring the party back to the muster at ({_muster.X}, {_muster.Y}) in {MarchMs / 60000} minutes, having turned back from {_deep.Name}: {_why}"
                    : $"could not reach the mouth of {_deep.Name} in {MarchMs / 60000} minutes, from the muster at ({_muster.X}, {_muster.Y})"
            );
        }

        if (_room == Point3D.Zero)
        {
            return BotDoing.Failed($"{_deep.Name} has no room anybody could walk to");
        }

        if (_returning && body.Map == _map && body.InRange(_muster, Assembly))
        {
            return Home(member, squad, body);
        }

        if (!_returning)
        {
            var mouth = !_atMouth && _mouth != Point3D.Zero && body.Map == _map && body.InRange(_mouth, MouthTiles)
                        && squad.Straggler(MarchSpread, out _) == null;

            if (mouth || squad.Count < _judged)
            {
                _atMouth |= mouth;
                _judged = squad.Count;

                var where = mouth ? $"at the mouth of {_deep.Name}" : $"on the way to {_deep.Name}";

                if (Short(squad, body, out var why, out var worth))
                {
                    return TurnBack(member, squad, body, why, where);
                }

                if (mouth)
                {
                    WentOn++;

                    logger.Information(
                        "{Name} brings {Living} of the {SetOut} that set out to the mouth of {Deep}: {Worth} of strength of the {Had} that marched, {Wanted} wanted by its worst; they go in",
                        body.Name,
                        Living(squad),
                        _setOutCount,
                        _deep.Name,
                        worth.ToString("F0"),
                        _setOut.ToString("F0"),
                        (_deep.Worst * BotDelver.Odds).ToString("F0")
                    );
                }
            }
        }

        var straggler = squad.Straggler(MarchSpread, out var behind);

        if (straggler?.Self is { } crossing && OtherLand(body.Location, crossing.Location, behind))
        {
            if (_returning)
            {
                _crossTick = 0;
                _holdTick = 0;

                return BotDoing.Walk(_map, _muster, BotArrival.Within(Station), $"back to the muster at ({_muster.X}, {_muster.Y}) from the way to {_deep.Name}");
            }

            if (_crossTick == 0)
            {
                _crossTick = now;
            }

            if (now - _crossTick < MouthHoldMs)
            {
                member.Journey?.Finish();

                return BotDoing.Work($"holding at the mouth of {_deep.Name} for {crossing.Name} to come through");
            }

            LeftBehind++;
            logger.Information("{Name} leaves {Who} behind at the mouth of {Deep}: it did not come through in {Minutes} minutes", body.Name, crossing.Name, _deep.Name, MouthHoldMs / 60000);
            BotSquads.Leave(straggler);
            _crossTick = 0;

            return BotDoing.Work($"at the mouth of {_deep.Name}");
        }

        _crossTick = 0;

        if (straggler != null)
        {
            if (_holdTick == 0)
            {
                _holdTick = now;
                Held++;
            }

            if ((behind <= MarchLost || BotSquad.Coming(straggler, behind)) && now - _holdTick < HoldMs)
            {
                member.Journey?.Finish();

                return BotDoing.Work($"holding for {straggler.Self?.Name ?? "a member"}, {behind} tiles behind, on the way {(_returning ? "back from" : "to")} {_deep.Name}");
            }

            LeftBehind++;

            var stragglerDeed = (straggler as IBotWilful)?.Resolve?.Deed;
            var stragglerJourney = straggler.Journey;
            logger.Information(
                "{Name} leaves {Who} behind on the march to {Deep}: {Behind} tiles back after {Seconds}s — in squad {Squad}, holding {Deed} ({Stage}), journey {Moving} towards {Target} ({Reason})",
                body.Name,
                straggler.Self?.Name ?? "a member",
                _deep.Name,
                behind,
                (now - _holdTick) / 1000,
                straggler.Squad?.Id.ToString() ?? "none",
                stragglerDeed?.Kind ?? "nothing",
                stragglerDeed?.Stage ?? "",
                stragglerJourney is { Moving: true } ? "moving" : stragglerJourney is { Active: true } ? "active, not moving" : "idle",
                stragglerJourney?.Target ?? Point3D.Zero,
                stragglerJourney?.Reason ?? ""
            );
            BotSquads.Leave(straggler);
            _holdTick = 0;

            if (squad.Count < _least)
            {
                _judged = squad.Count;

                return TurnBack(
                    member,
                    squad,
                    body,
                    $"{squad.Count} left of the {_setOutCount} that set out once {straggler.Self?.Name ?? "a member"} was left behind",
                    _returning ? $"on the way back from {_deep.Name}" : $"on the way to {_deep.Name}"
                );
            }
        }
        else
        {
            _holdTick = 0;
        }

        return _returning
            ? BotDoing.Walk(_map, _muster, BotArrival.Within(Station), $"back to the muster at ({_muster.X}, {_muster.Y}) from the way to {_deep.Name}")
            : BotDoing.Walk(_map, _room, BotArrival.Within(3), $"into {_deep.Name} by its mouth");
    }

    private BotDoing StandTo(IBotSquadMember member, BotSquad squad, Mobile body, Mobile foe, long now)
    {
        if (_fightTick == 0)
        {
            _fightTick = now;
            _holdTick = 0;
            _crossTick = 0;
            Stood++;

            member.Journey?.Finish();

            logger.Information(
                "{Name} halts the march {Way} {Deep}: the company of {Count} turns on {Foe}, which set upon {Who}",
                body.Name,
                _returning ? "back from" : "to",
                _deep.Name,
                squad.Count,
                foe.Name,
                squad.Contact?.Self?.Name ?? "one of us"
            );
        }

        if (!ReferenceEquals(foe, _foe))
        {
            _foe = foe;

            if (member is IBotWilful wilful && wilful.Resolve != null)
            {
                wilful.Resolve.StirredTick = now;
            }
        }

        return BotDoing.Work($"standing with the company against {foe.Name} on the way {(_returning ? "back from" : "to")} {_deep.Name}");
    }

    private int Living(BotSquad squad)
    {
        var living = 0;
        var members = squad.Members;

        for (var i = 0; i < members.Count; i++)
        {
            if (members[i]?.Self is { Deleted: false, Alive: true } self && self.Map == _map)
            {
                living++;
            }
        }

        return living;
    }

    private bool Short(BotSquad squad, Mobile body, out string why, out double worth)
    {
        worth = 0.0;

        var living = Living(squad);

        if (living < _least)
        {
            why = $"{living} of the {_setOutCount} that set out are standing, fewer than the {_least} that outnumber the worst room of {_deep.Name}";

            return true;
        }

        if (Line(squad) < Fighters)
        {
            why = "nobody is left who could hold a line";

            return true;
        }

        worth = BotDelver.Worth(squad, body, _deep);

        if (_setOut > 0.0 && worth < _setOut * GoOnShare && worth < _deep.Worst * BotDelver.Odds)
        {
            why = $"{worth:F0} of strength is left of the {_setOut:F0} that set out, {living} of {_setOutCount} standing";

            return true;
        }

        why = null;

        return false;
    }

    private BotDoing TurnBack(IBotSquadMember member, BotSquad squad, Mobile body, string why, string where)
    {
        var living = Living(squad);

        if (living < 2)
        {
            TurnedBack++;

            logger.Information("{Name} gives up the delve into {Deep} {Where}: {Why}, and nobody is left to walk back with", body.Name, _deep.Name, where, why);

            Release(squad);
            BotSquads.Leave(member);
            _squad = null;
            _marching = false;
            _returning = false;

            return BotDoing.Failed($"nobody left to march with {where}: {why}");
        }

        if (_returning)
        {
            return BotDoing.Work($"turning back from {_deep.Name}: {_why}");
        }

        TurnedBack++;

        _returning = true;
        _why = why;
        _marchTick = Core.TickCount;
        _fought = 0;
        _fightTick = 0;
        _holdTick = 0;
        _crossTick = 0;

        member.Journey?.Finish();

        logger.Information(
            "{Name} turns the party for {Deep} back {Where}: {Why}; the {Living} of them walk back to the muster at ({X}, {Y}) together",
            body.Name,
            _deep.Name,
            where,
            why,
            living,
            _muster.X,
            _muster.Y
        );

        return BotDoing.Work($"turning back from {_deep.Name}: {why}");
    }

    private BotDoing Home(IBotSquadMember member, BotSquad squad, Mobile body)
    {
        BroughtBack++;

        var living = Living(squad);

        logger.Information(
            "{Name} has brought {Living} back to the muster at ({X}, {Y}) from the way to {Deep}: {Why}",
            body.Name,
            living,
            _muster.X,
            _muster.Y,
            _deep.Name,
            _why
        );

        Release(squad);
        BotSquads.Leave(member);
        _squad = null;
        _marching = false;
        _returning = false;

        return BotDoing.Failed($"turned back from {_deep.Name} and brought {living} back to the muster: {_why}");
    }

    private BotDoing Arrived(BotSquad squad, Mobile body)
    {
        var now = Core.TickCount;

        _party = new BotDelveParty(_deep, body as BotMobile);
        _marching = false;
        _down = true;
        squad.OnTheMarch = false;
        _began = now;
        _roomTick = now;
        _deep.Runs++;
        Marches++;
        Heads += Living(squad);

        Adopt(squad);

        logger.Information(
            "{Name} has led {Count} into {Deep} by its mouth after {Minutes:F1} minutes' march: {Quota} of them to kill or {Cap} minutes, {Raisings} raisings between them",
            body.Name,
            _count,
            _deep.Name,
            (now - _marchTick) / 60000.0,
            Quota,
            CapMs / 60000,
            _party.Left
        );

        return BotDoing.Work($"in {_deep.Name}");
    }

    private void Adopt(BotSquad squad)
    {
        if (_party == null || squad == null)
        {
            return;
        }

        var members = squad.Members;

        for (var i = 0; i < members.Count; i++)
        {
            if (members[i]?.Self is BotMobile { Deleted: false, Alive: true } who && _deep.Holds(who.Location) && !_party.Holds(who))
            {
                _party.Add(who);
            }
        }

        _count = _party.Down;
    }

    private BotDoing Leaving(IBotSquadMember member, Mobile body)
    {
        var now = Core.TickCount;
        var pot = _party?.Pot ?? 0;

        if (!_deep.Holds(body.Location))
        {
            WalkedOut++;
            Release(_squad);

            return BotDoing.Done($"{_why} — {_count} of us, {pot}gp between them; out by the mouth");
        }

        if (now - _leaveTick >= LeaveMs)
        {
            LeftBelow++;
            Release(_squad);

            return BotDoing.Done($"{_why} — {_count} of us, {pot}gp between them; the leader could not reach the mouth in {LeaveMs / 60000} minutes");
        }

        return BotDoing.Walk(_map, _muster, BotArrival.Within(Station), $"out of {_deep.Name} by its mouth");
    }

    private BotDoing Calling(IBotSquadMember member, Mobile body)
    {
        var squad = member.Squad ?? BotSquads.Form(member);

        if (squad == null)
        {
            return BotDoing.Failed("could not call a party together");
        }

        _squad = squad;
        squad.Ceiling = _want;
        squad.Charged = true;

        var now = Core.TickCount;

        if (!_calling)
        {
            _calling = true;

            _muster = Muster(body);

            Calls++;

            _caller = body;
            _calledTick = now;
            _callsNow.Add(this);

            logger.Information(
                "{Name} is calling a party of {Want} at ({X}, {Y}) for a delve into {Deep}, where the worst of them is worth {Worst} and the worst room holds {Room}; it goes down with {Least} at the fewest",
                body.Name,
                _want,
                _muster.X,
                _muster.Y,
                _deep.Name,
                _deep.Worst.ToString("F0"),
                RoomOf(_map, _deep),
                _least
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

        var line = LineHere(squad);

        if (_here != _said0 || _count != _said1)
        {
            _said0 = _here;
            _said1 = _count;

            logger.Information(
                "{Name}'s party for {Deep}: {Here} of {Want} standing in it, {Called} in the company, {Line} of them able to hold a line at the muster, {Left}s left",
                body.Name,
                _deep.Name,
                _here,
                _want,
                _count,
                line,
                Math.Max(0, (MusterMs - (now - _musteredTick)) / 1000)
            );
        }

        if ((_here < _want || line < Fighters) && now - _musteredTick < MusterMs)
        {
            return _here >= _want
                ? BotDoing.Work($"calling for a fighter at ({_muster.X}, {_muster.Y})")
                : BotDoing.Work($"calling at ({_muster.X}, {_muster.Y}): {_here} of {_want} gathered");
        }

        if (line < Fighters)
        {
            var away = Line(squad) > line;

            Lineless++;

            if (away)
            {
                LineAway++;
            }

            Rest(body);
            BotSquads.Leave(member);
            _squad = null;

            return BotDoing.Failed(
                away
                    ? $"nobody who could hold a line stood at the muster for {_deep.Name}: the one called did not come in {MusterMs / 60000} minutes"
                    : $"nobody who could hold a line answered the call for {_deep.Name}"
            );
        }

        if (_here < _least)
        {
            Undermanned++;
            Rest(body);
            BotSquads.Leave(member);
            _squad = null;

            return BotDoing.Failed(
                $"only {_here} of the {_want} stood up for {_deep.Name} in {MusterMs / 60000} minutes, of {_count} called, fewer than the {_least} that outnumber its worst room; {PassedOver()}"
            );
        }

        if (_here < _want)
        {
            Undersized++;

            logger.Information("{Name}'s party for {Deep} goes down {Here} strong, short of {Want}: the call ran out", body.Name, _deep.Name, _here, _want);
        }

        return Descend(member, squad, body);
    }

    private BotDoing Descend(IBotSquadMember leader, BotSquad squad, Mobile body)
    {
        for (var i = squad.Members.Count - 1; i >= 0; i--)
        {
            var member = squad.Members[i];

            if (ReferenceEquals(member, squad.Leader) || member?.Self is not { } self)
            {
                continue;
            }

            if (self is not { Deleted: false, Alive: true } || self.Map != _map || !self.InRange(_muster, Assembly))
            {
                BotSquads.Leave(member);
                Ungathered++;
            }
        }

        var planned = _deep;

        _deep = BotDelver.Recheck(squad, body, _deep, out var strength, out var shortfall) ?? planned;

        if (_deep != planned)
        {
            logger.Information(
                "{Name}'s party for {Planned} is {Strength} of proven strength against its worst ({Why}), so it goes down {Deep} instead",
                body.Name,
                planned.Name,
                strength.ToString("F0"),
                shortfall ?? $"{planned.Worst * BotDelver.Odds:F0} wanted",
                _deep.Name
            );

            Size();
        }

        if (squad.Count < _least)
        {
            Undermanned++;
            Rest(body);
            BotSquads.Leave(leader);
            _squad = null;

            return BotDoing.Failed(
                $"only {squad.Count} stood at the muster for {_deep.Name}, fewer than the {_least} that outnumber its worst room"
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

        if (BotGates.Reaches(_map, body.Location, _deep))
        {
            _marching = true;
            _marchTick = Core.TickCount;
            _count = squad.Count;
            squad.Ceiling = Math.Max(1, squad.Count);
            squad.Quarry = Prey;
            Walked++;

            squad.OnTheMarch = true;
            _setOut = BotDelver.Worth(squad, body, _deep);
            _setOutCount = squad.Count;
            _judged = squad.Count;
            _mouth = BotGates.Mouth(_map, body.Location, _room)?.From ?? Point3D.Zero;
            _atMouth = false;
            _returning = false;
            _fought = 0;
            _fightTick = 0;
            _marchBends = 0;

            var mouth = BotGates.Next(_map, body.Location, _room, out var gate) ? gate.ToString() : "a mouth the graph knows";

            logger.Information(
                "{Name} leads {Count} from the muster at ({X}, {Y}) to {Deep} by {Gate}",
                body.Name,
                _count,
                _muster.X,
                _muster.Y,
                _deep.Name,
                mouth
            );

            _passage = gate != null ? BotPassage.Open(squad, body, _map, gate.From, $"the mouth of {_deep.Name}", false) : null;

            if (_passage != null && _passage.Lead(body, out var through))
            {
                return through;
            }

            return BotDoing.Walk(_map, _room, BotArrival.Within(3), $"into {_deep.Name} by its mouth");
        }

        Carried++;
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
        Heads += went;

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
        Adopt(member.Squad);

        if (_kills >= Quota)
        {
            Emptied++;

            return Finish($"{_kills} of them are down in {_deep.Name}", body);
        }

        if (now - _began >= CapMs)
        {
            Timedout++;

            return Finish($"{CapMs / 60000} minutes in {_deep.Name} was enough at {_kills} down", body);
        }

        var down = _party?.Down ?? 0;

        if (down < 2 && now - _began >= ArriveMs)
        {
            Broken++;

            return Finish($"there was nobody left to delve with in {_deep.Name}", body);
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

                if (BotGates.Reaches(_map, body.Location, _deep))
                {
                    return BotDoing.Walk(_map, _room, BotArrival.Within(3), $"back into {_deep.Name} by its mouth");
                }

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
                return Finish($"{_deep.Name} ran out of rooms", body);
            }

            return BotDoing.Walk(_map, _room, BotArrival.Within(3), $"on to ({_room.X}, {_room.Y}) in {_deep.Name}");
        }

        if (_room == Point3D.Zero)
        {
            return Finish($"{_deep.Name} ran out of rooms", body);
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

    private BotDoing Finish(string why) => Finish(why, null);

    private BotDoing Finish(string why, Mobile body)
    {
        var pot = _party?.Pot ?? 0;

        if (_down && body is { Deleted: false, Alive: true } && _deep.Holds(body.Location) && BotGates.Joined(_map, body.Location, _muster))
        {
            _leaving = true;
            _leaveTick = Core.TickCount;
            _why = why;
            _party?.Settle(why, lift: false);

            return BotDoing.Work($"leaving {_deep.Name} by its mouth: {why}");
        }

        _party?.Settle(why);
        Release(_squad);

        return BotDoing.Done($"{why} — {_count} of us, {pot}gp between them");
    }

    public static long Undersized { get; private set; }

    private Point3D Muster(Mobile body)
    {
        var seat = Rally(body);

        if (MusterBack <= 0 || _deep == null || _map == null || _map == Map.Internal)
        {
            return seat;
        }

        var land = BotGates.LandOf(_map, seat);
        var gate = land < 0 ? null : BotGates.First(land, seat, _deep);

        if (gate == null)
        {
            return seat;
        }

        List<Point3D> points = [];

        if (NavigationService.Route(_map, seat, gate.From, points) is not (NavStatus.Ok or NavStatus.Direct) || points.Count < 2)
        {
            return seat;
        }

        var end = points.Count - 1;

        for (var i = 0; i < points.Count; i++)
        {
            if (BotQuad.Muscle(_map, points[i]) > 0.0 || BotBarred.Barred(_map, points[i]))
            {
                end = i;

                break;
            }
        }

        var anchor = points[end];
        var pick = -1;

        for (var i = end - 1; i >= 0; i--)
        {
            if (Math.Max(Math.Abs(points[i].X - anchor.X), Math.Abs(points[i].Y - anchor.Y)) >= MusterBack)
            {
                pick = i;

                break;
            }
        }

        if (pick <= 0)
        {
            return seat;
        }

        var at = points[pick];

        if (!BotStep.Settle(_map, at.X, at.Y, out var z))
        {
            return seat;
        }

        MusteredForward++;
        logger.Information(
            "{Name}'s muster for {Deep} is set forward to ({X}, {Y}), {Back} tiles before {What} at ({AX}, {AY})",
            body?.Name ?? "a leader",
            _deep.Name,
            at.X,
            at.Y,
            MusterBack,
            end == points.Count - 1 ? "the mouth" : "the first bloodied ground on the road",
            anchor.X,
            anchor.Y
        );

        return new Point3D(at.X, at.Y, z);
    }

    private Point3D Rally(Mobile body)
    {
        var where = BotPopulation.HomeOf(body);

        if (where == Point3D.Zero)
        {
            where = BotPopulation.Where;
        }

        if (where != Point3D.Zero && BotStep.Settle(_map, where.X, where.Y, out var z))
        {
            return new Point3D(where.X, where.Y, z);
        }

        return body?.Location ?? where;
    }

    public static long Hostiles { get; private set; }

    private readonly int[] _passed = new int[9];

    private static readonly string[] _passedFor =
    [
        "in a company already", "unable to fight", "producers", "not holding a line and beyond reach", "down a dungeon",
        "answering to a company or class", "without supplies", "of a hostile guild", "no bot"
    ];

    private string PassedOver()
    {
        var said = "";

        for (var i = 0; i < _passed.Length - 1; i++)
        {
            if (_passed[i] > 0)
            {
                said += (said.Length == 0 ? "" : ", ") + $"{_passed[i]} {_passedFor[i]}";
            }
        }

        return said.Length == 0 ? "nobody else within reach" : $"passed over within reach: {said}";
    }

    private void Levy(BotSquad squad, Mobile body)
    {
        if (squad.Count >= _want)
        {
            return;
        }

        _called0.Clear();
        Array.Clear(_passed);

        var lined = Line(squad) >= Fighters;
        var reach = lined ? Reach : Math.Max(Reach, Math.Min(LineReach, BotSquads.JoinReach - Station));

        foreach (var mobile in _map.GetMobilesInRange<Mobile>(_muster, reach))
        {
            if (mobile == body || mobile is not BotMobile other)
            {
                continue;
            }

            if (other.Squad != null)
            {
                _passed[0]++;

                continue;
            }

            if (other is not IBotAlly { AbleToFight: true })
            {
                _passed[1]++;

                continue;
            }

            if (other.Class is not { } klass || klass.Role == BotRole.Producer)
            {
                _passed[2]++;

                continue;
            }

            if (klass.Role != BotRole.Melee && !other.InRange(_muster, Reach))
            {
                _passed[3]++;

                continue;
            }

            if (BotDelveParty.Delving(other))
            {
                _passed[4]++;

                continue;
            }

            if (other.Resolve?.Deed is { Alongside: true })
            {
                _passed[5]++;

                continue;
            }

            if (!BotProvision.Fit(other, out _))
            {
                _passed[6]++;

                continue;
            }

            if (BotRegard.Hostile(body, other))
            {
                Hostiles++;
                _passed[7]++;

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

        Take(squad, BotRole.Melee, Fighters, _want, false);

        if (!lined && Line(squad) < Fighters)
        {
            Take(squad, BotRole.Melee, Fighters - Line(squad), _want, true);
        }

        Take(squad, null, _want, _want - Math.Max(0, Fighters - Line(squad)), false);
    }

    private static int Mates(Mobile leader, Mobile other) => BotGuilds.Same(leader, other) ? 1 : 0;

    private void Take(BotSquad squad, BotRole? role, int most, int upTo, bool afar)
    {
        var taken = 0;

        for (var i = 0; i < _called0.Count && taken < most && squad.Count < upTo; i++)
        {
            var other = _called0[i];

            if (other.Squad != null || role != null && other.Class?.Role != role)
            {
                continue;
            }

            var far = !other.InRange(_muster, Reach);

            if (far && !afar)
            {
                continue;
            }

            if (BotSquads.Join(squad, other))
            {
                taken++;
                Called++;

                if (far)
                {
                    LinesFar++;
                }

                if (BotWill.Press(other, new BotEnlist(squad, _map, _muster, true), $"called to {squad.Leader?.Self?.Name ?? "a leader"}'s party for {_deep?.Name}"))
                {
                    Pressed++;
                }
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

    private int LineHere(BotSquad squad)
    {
        var line = 0;
        var members = squad.Members;

        for (var i = 0; i < members.Count; i++)
        {
            if (members[i]?.Self is BotMobile { Deleted: false, Alive: true } who && who.Class?.Role == BotRole.Melee
                && who.Map == _map && who.InRange(_muster, Assembly))
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

        if (_marching || _returning)
        {
            MarchRefused++;

            var walker = bot?.Self;

            if (walker != null)
            {
                var floor = BotStep.Ground(_map, walker.X, walker.Y, walker.Z, BotStep.GroundReach, out var under) ? under.ToString() : "none within reach";
                var goal = _returning ? _muster : _room;

                logger.Information(
                    "{Name}'s road {Way} {Deep} was refused at ({X}, {Y}, {Z}) — floor {Floor}, land {Land}; the gates from there: {Chain}",
                    walker.Name,
                    _returning ? "back from" : "to",
                    _deep.Name,
                    walker.X,
                    walker.Y,
                    walker.Z,
                    floor,
                    BotGates.LandOf(_map, walker.Location),
                    BotGates.Chain(_map, walker.Location, goal)
                );
            }

            return ++_marchBends <= MarchBends;
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

        _passage?.Abort("the delve was let go");
        _passage = null;

        Release(_squad);

        if (bot is IBotSquadMember member && member.Squad != null && ReferenceEquals(member.Squad, _squad))
        {
            BotSquads.Leave(member);
        }

        _squad = null;
        _down = false;
        _calling = false;
        _marching = false;
        _leaving = false;
        _returning = false;
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
        squad.OnTheMarch = false;
    }

    public static string Describe() =>
        Calls == 0
            ? "no delve has been called"
            : $"{Calls} delves called and {Called} bots called up ({LinesFar} of them line-holders called from beyond {Reach} tiles for a company that had none; {Hostiles} passed over for a guild hostile to the leader's), {Marches} parties went down with {Heads} bots between them ({Undersized} of them short of what they called, with enough to outnumber the worst room; {Walked} marched to a cave mouth ({MusteredForward} musters set forward on the road, {Pressed} levied bots put their errand down for it, {Ungathered} let go at the descent for not standing at the muster, {Held} holds for stragglers, {LeftBehind} left behind, {Stood} fights stood to on the road with the leader, {MarchRefused} roads refused on the march; {WentOn} counted at the mouth and went in, {TurnedBack} turned back together and {BroughtBack} of those reached the muster), {Carried} were put down by hand, {Unreached} never reached the mouth in {MarchMs / 60000} minutes; {WalkedOut} walked out by the mouth, {LeftBelow} leaders gave up the walk out); {Undermanned} could not raise "
            + $"enough to outnumber their dungeon's worst room in {MusterMs / 60000} minutes and {Lineless} could raise nobody to hold a line ({LineAway} of them with one called who never reached the muster); "
            + $"a party calls {BotProving.PartyMultiple} for each creature of its dungeon's worst room, {Company} at the fewest and {Math.Max(Company, BotDelver.HeadsPerParty)} at the most: {Sizes()}; "
            + $"{Emptied} came out on {Quota} corpses, {Timedout} on the {CapMs / 60000} minutes, {Broken} because too few were left, {Leaderless} because whoever led them was no longer down there "
            + $"and {Walled} because the walls would not let them past ({Bends} refused roads shrugged off, {Shifted} musters moved to the caller's own tile, {Strayed} leaders fetched back out of the world); "
            + $"{Killed} things killed down there; {BotHalls.Describe()}; {BotDelveParty.Describe()}";

    public static void Forget()
    {
        Calls = 0;
        Undersized = 0;
        Walked = 0;
        Carried = 0;
        Unreached = 0;
        WalkedOut = 0;
        LeftBelow = 0;
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
        Stood = 0;
        WentOn = 0;
        TurnedBack = 0;
        BroughtBack = 0;
        MarchRefused = 0;
        LinesFar = 0;
        LineAway = 0;
        Heads = 0;
        _resting.Clear();
        _called0.Clear();
        _worstRooms.Clear();

        BotDelveParty.Forget();
    }
}
