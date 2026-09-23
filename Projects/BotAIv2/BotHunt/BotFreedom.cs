using System.Collections.Generic;
using Server.Mobiles;

namespace Server.BotAI.V2;

/// <summary>
/// Getting a prisoner out of a camp and home again.
///
/// <para>
/// <b>Ordered by Patrick on 08.09.2026, and the engine does almost all of it.</b> Every one of this era's
/// camps — orc, brigand, ratman, lizardman — puts a <c>Noble</c> or a <c>SeekerOfAdventure</c> in the
/// middle of it with <c>IsPrisoner</c> set and <c>CantWalk</c> true, and has them yell for help whenever a
/// player walks past. Freeing one is not a lockpick or a key: it is
/// <c>BaseEscortable.AcceptEscorter</c>, which clears <c>CantWalk</c>, makes the bot its master and sets
/// it following. From then on the prisoner walks itself, and when it stands inside its destination region
/// the engine pays 500 to 1000 gold into the escorter's pack, awards ten fame and four hundred points of
/// Compassion, and deletes itself.
/// </para>
///
/// <para>
/// <b>So the whole of the work here is the walking, and the whole of the risk is the distance.</b> The
/// destination is a random town or dungeon region on Felucca, which on this map can be four hundred tiles
/// off — further than any road this shard will search for. A bot that accepts an escort it cannot complete
/// has taken a prisoner out of a cage to die of neglect in a field, so the distance is checked before the
/// offer is made and never after, and it is checked against the same number every other walk is:
/// <see cref="BotHunter.Walkable"/>.
/// </para>
///
/// <para>
/// <b>One at a time, and the engine enforces it.</b> <c>EscortDelay</c> is five minutes between escorts for
/// any one <c>PlayerMobile</c>, and bots are PlayerMobiles, so a population that all rushed the same camp
/// would simply be refused. Counted rather than avoided: <see cref="Refused"/> reading high next to a
/// healthy <see cref="Freed"/> is the population wanting more of this work than the rules allow.
/// </para>
/// </summary>
public sealed class BotFreedom : BotDeed
{
    public const string Trade = "liberate";

    public static double Prior { get; set; } = 220.0;

    public static double WorkMinutes { get; set; } = 6.0;

    public static int Reach { get; set; } = 48;

    public static int Roam { get; set; } = 1000;

    public static int Touch { get; set; } = 3;

    public static int Reward { get; set; } = 750;

    public static string Town { get; set; } = "Britain";

    private static Point3D Where_Town
    {
        get
        {
            var region = EscortDestinationInfo.Find(Town)?.Region;

            return region == null ? Point3D.Zero : region.GoLocation;
        }
    }

    public static long Uncaged { get; private set; }

    public static long Freed { get; private set; }

    public static long Refused { get; private set; }

    public static long Lost { get; private set; }

    public static long TooFar { get; private set; }

    private readonly Map _map;

    private readonly BaseEscortable _prisoner;

    private readonly Point3D _cage;

    private bool _taken;

    private bool _delivered;

    public static int ClaimMs { get; set; } = 90000;

    private static readonly Dictionary<Serial, (Serial By, long Tick)> _claims = [];

    public BotFreedom(Map map, BaseEscortable prisoner)
    {
        _map = map;
        _prisoner = prisoner;
        _cage = prisoner?.Location ?? Point3D.Zero;
    }

    public override string Kind => Trade;

    public override void Taken(IBotWilful bot)
    {
        if (bot?.Self is { } body && _prisoner != null)
        {
            _claims[_prisoner.Serial] = (body.Serial, Core.TickCount);
        }
    }

    public override void Drop(IBotWilful bot)
    {
        base.Drop(bot);

        Release(bot?.Self, _prisoner);
    }

    public static bool Claimed(Mobile body, BaseEscortable prisoner)
    {
        if (body == null || prisoner == null || !_claims.TryGetValue(prisoner.Serial, out var claim))
        {
            return false;
        }

        if (Core.TickCount - claim.Tick >= ClaimMs)
        {
            _claims.Remove(prisoner.Serial);

            return false;
        }

        return claim.By != body.Serial;
    }

    private static void Release(Mobile body, BaseEscortable prisoner)
    {
        if (body != null && prisoner != null && _claims.TryGetValue(prisoner.Serial, out var claim) && claim.By == body.Serial)
        {
            _claims.Remove(prisoner.Serial);
        }
    }

    public override bool Summons => true;

    public override Map Map => _map;

    public override Point3D Where => _taken ? Home() : _cage;

    public override double Expects => Prior;

    public override double Minutes => WorkMinutes;

    public override SkillName? Trains => null;

    public override int Outlay => 0;

    public override double Coin => 0.0;

    public override int Made => _delivered ? Reward : 0;

    public override string Stage =>
        _delivered ? "delivered" : _taken ? $"walking {_prisoner?.Name} home" : $"after {_prisoner?.Name}";

    public override BotDoing Advance(IBotWilful bot)
    {
        var body = bot?.Self;

        if (body == null || _map == null || _map == Map.Internal)
        {
            return BotDoing.Failed("no body");
        }

        if (_prisoner == null || _prisoner.Deleted)
        {
            Release(body, _prisoner);

            if (_taken)
            {
                _delivered = true;
                Freed++;

                return BotDoing.Done("delivered, and paid for it");
            }

            return BotDoing.Failed("the prisoner is gone");
        }

        if (!_prisoner.Alive)
        {
            Lost++;

            return BotDoing.Failed("the prisoner is dead");
        }

        if (!_taken)
        {
            if (!body.InRange(_prisoner.Location, Touch))
            {
                return BotDoing.Walk(_map, _cage, BotArrival.Within(Touch), $"after {_prisoner.Name}");
            }

            if (_prisoner.Destination != Town)
            {
                _prisoner.Destination = Town;
            }

            if (!_prisoner.AcceptEscorter(body))
            {
                Refused++;
                Release(body, _prisoner);

                return BotDoing.Failed("it would not come");
            }

            _taken = true;
            Uncaged++;

            return BotDoing.Work("freeing it");
        }

        if (_prisoner.GetEscorter() != body)
        {
            if (_prisoner.GetDestination() == null)
            {
                Release(body, _prisoner);
                _delivered = true;
                Freed++;

                return BotDoing.Done("delivered, and paid for it");
            }

            Lost++;

            return BotDoing.Failed("it is no longer following");
        }

        var home = Home();

        if (home == Point3D.Zero)
        {
            Lost++;

            return BotDoing.Failed("it no longer knows where it is going");
        }

        return BotDoing.Walk(_map, home, BotArrival.Within(2), $"walking {_prisoner.Name} home");
    }

    private Point3D Home()
    {
        var region = _prisoner?.GetDestination()?.Region;

        return region == null ? Point3D.Zero : region.GoLocation;
    }

    public static BaseEscortable Nearest(Mobile body, int range, out bool spoken)
    {
        spoken = false;

        var map = body?.Map;

        if (map == null || map == Map.Internal)
        {
            return null;
        }

        BaseEscortable best = null;
        var bestAway = double.MaxValue;

        foreach (var near in map.GetMobilesInRange<BaseEscortable>(body.Location, range))
        {
            if (near.Deleted || !near.Alive || !near.IsPrisoner || !near.CantWalk)
            {
                continue;
            }

            if (Claimed(body, near))
            {
                spoken = true;

                continue;
            }

            var town = Where_Town;

            if (town == Point3D.Zero)
            {
                continue;
            }

            if (!Utility.InRange(near.Location, town, Roam))
            {
                TooFar++;

                continue;
            }

            var away = body.GetDistanceToSqrt(near.Location);

            if (away >= bestAway)
            {
                continue;
            }

            best = near;
            bestAway = away;
        }

        return best;
    }

    public static void Forget()
    {
        _claims.Clear();
        Uncaged = 0;
        Freed = 0;
        Refused = 0;
        Lost = 0;
        TooFar = 0;
    }

    public static string Describe() =>
        $"{Uncaged} prisoners taken out of cages and {Freed} walked home, {Lost} lost on the way, "
        + $"{Refused} refused by the engine, {TooFar} passed over for living further than {Roam} tiles from {Town}";
}

/// <summary>
/// Offers a bot the job of walking a prisoner home.
///
/// <para>
/// Rationed to once a minute per bot like every other spatial sweep on this shard, and offered to anybody:
/// a prisoner does not care what trade its escort practises, and the walk is the work.
/// </para>
/// </summary>
public sealed class BotLiberator : IBotProposer
{
    public string Name => "Liberator";

    public BotStanding Rung => BotStanding.Free;

    public static long Asked { get; private set; }

    public static long Soon { get; private set; }

    public static long None { get; private set; }

    public static long Offered { get; private set; }

    public static long Spoken { get; private set; }

    public BotDeed Propose(IBotWilful bot)
    {
        var body = bot?.Self;
        var map = body?.Map;

        if (map == null || map == Map.Internal || !body.Alive)
        {
            return null;
        }

        Asked++;

        if (!BotNeeds.Due(body, "prisoner"))
        {
            Soon++;

            return null;
        }

        var prisoner = BotFreedom.Nearest(body, BotFreedom.Reach, out var spoken);

        if (prisoner == null)
        {
            if (spoken)
            {
                Spoken++;
            }
            else
            {
                None++;
            }

            return null;
        }

        Offered++;

        return new BotFreedom(map, prisoner);
    }

    public static string Describe() =>
        $"{Asked} asked to walk somebody home: {Offered} sent to a cage, {None} heard nobody, {Spoken} heard only prisoners another bot had set out for, {Soon} had listened too recently";

    public static void Reset()
    {
        Asked = 0;
        Soon = 0;
        None = 0;
        Offered = 0;
        Spoken = 0;
    }
}
