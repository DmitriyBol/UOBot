using System.Collections.Generic;
using Server.Regions;
using Server.Logging;

namespace Server.BotAI.V2;

/// <summary>
/// Going to look for a fight, when there is nothing to fight where the bot is standing.
///
/// <para>
/// <b>Without this a fighter is only as good as where somebody put it.</b> Hunting begins when something
/// hostile is within sight, and a town forbids fighting outright — so a population raised in a city has
/// warriors that stand about for ever, and a population raised at a graveyard hunts only that graveyard until
/// it is empty. Neither is a bot deciding anything; both are a bot being placed well or badly.
/// </para>
///
/// <para>
/// <b>It is priced at almost nothing on purpose.</b> Walking somewhere produces no coin, no goods and no
/// skill, so it wins only when the auction has nothing else to offer at all — which is exactly when a bot
/// should be out looking rather than standing still. The moment anything worth fighting comes into reach, the
/// hunt itself scores an order of magnitude higher and takes over at the next review.
/// </para>
///
/// <para>
/// And it ends itself the instant it has worked: a bot that walks into sight of a quarry does not finish the
/// walk first. "Stand and complete the errand" is the shape of bug this project keeps finding in itself.
/// </para>
/// </summary>
public sealed class BotProwl : BotDeed
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotProwl));

    public const string Trade = "prowl";

    public static double Prior { get; set; } = 8.0;

    public static double WorkMinutes { get; set; } = 2.0;

    public static int ArriveWithin { get; set; } = 8;

    public static int TrekLimit { get; set; } = 200;

    public static long Baulked { get; private set; }

    public static bool ByRoad { get; set; } = true;

    public static long RoadKept { get; private set; }

    public static int Redarts { get; set; } = 1;

    public static int RedartReach { get; set; } = 40;

    public static int RedartSamples { get; set; } = 8;

    public static int RedartVets { get; set; } = 2;

    public static double RedartVetMs { get; set; } = 300.0;

    public static long Redarted { get; private set; }

    public static long Unredarted { get; private set; }

    public static long Turned { get; private set; }

    public static long Unturned { get; private set; }

    private readonly Map _map;

    private Point3D _where;

    private int _redarts;

    private static readonly List<Point3D> _candidates = [];

    private int _nearest = int.MaxValue;

    private int _plans = -1;

    private int _planLeast = int.MaxValue;

    private bool Along(Mobile body)
    {
        if (!ByRoad || body is not BotMobile { Journey: { Current: { } errand } journey } || errand.Follow != null
            || errand.Where.X != _where.X || errand.Where.Y != _where.Y || journey.Remaining <= 0)
        {
            return false;
        }

        if (journey.Plans != _plans)
        {
            _plans = journey.Plans;
            _planLeast = journey.Remaining;

            return false;
        }

        if (journey.Remaining >= _planLeast)
        {
            return false;
        }

        _planLeast = journey.Remaining;

        return true;
    }

    private int _stalled;

    private Point3D _setOut;

    private static readonly List<Mobile> _recruits = [];

    public BotProwl(Map map, Point3D where) : this(map, where, false)
    {
    }

    public BotProwl(Map map, Point3D where, bool company)
    {
        _map = map;
        _where = where;
        _company = company;
    }

    private readonly bool _company;

    private BotSquad _squad;

    private bool _raised;

    private bool _gated;

    private Point3D _gate;

    public static int GatherWithin { get; set; } = 3;

    public static long Raised { get; private set; }

    public static long Unraised { get; private set; }

    public static int ClaimMs { get; set; } = 60000;

    private static readonly Dictionary<(int Map, int X, int Y), long> _raising = [];

    public static bool Raising(Map map, Point3D where)
    {
        var key = BotQuad.Key(map, where);

        return _raising.TryGetValue(key, out var when) && Core.TickCount - when < ClaimMs;
    }

    private static void Claim(Map map, Point3D where) => _raising[BotQuad.Key(map, where)] = Core.TickCount;

    public override string Kind => Trade;

    public override bool Braves => true;

    public override bool Guess => true;

    public override Map Map => _map;

    public override Point3D Where => _where;

    public override double Expects => Prior;

    public override double Minutes => WorkMinutes;

    public override SkillName? Trains => null;

    public override int Outlay => 0;

    public override double Coin => 0.0;

    public override int Made => 0;

    public override string Stage => $"looking for a fight near {_where}";

    public override BotDoing Advance(IBotWilful bot)
    {
        var body = bot?.Self;

        if (body == null || _map == null || _map == Map.Internal)
        {
            return BotDoing.Failed("no body");
        }

        if (_company && !_raised)
        {
            if (!_gated)
            {
                _gated = true;
                _gate = BotPopulation.Gate(_map, body.Location, _where);

                Claim(_map, _where);
            }

            if (_gate != Point3D.Zero && !body.InRange(_gate, GatherWithin))
            {
                return BotDoing.Walk(_map, _gate, BotArrival.Within(GatherWithin), "to the edge of town to gather");
            }

            _raised = true;

            if (bot is IBotSquadMember { Squad: null } member && BotSquads.Running)
            {
                var squad = BotSquads.Form(member);

                if (squad != null)
                {
                    _recruits.Clear();

                    foreach (var mobile in _map.GetMobilesInRange<Mobile>(body.Location, BotMuster.Reach))
                    {
                        if (mobile != body && mobile is IBotSquadMember { Squad: null } && mobile is IBotAlly { AbleToFight: true }
                            && mobile is { Deleted: false, Alive: true })
                        {
                            _recruits.Add(mobile);
                        }
                    }

                    _recruits.Sort((a, b) => BotThreat.Power(b).CompareTo(BotThreat.Power(a)));

                    for (var i = 0; i < _recruits.Count && squad.Count < squad.Ceiling; i++)
                    {
                        if (_recruits[i] is IBotSquadMember other)
                        {
                            BotSquads.Join(squad, other);
                        }
                    }

                    _squad = squad;
                    squad.Charged = true;
                }
            }

            if (!BotQuad.Dares(body, _map, _where))
            {
                Unraised++;

                return BotDoing.Failed(
                    $"could not raise enough strength for ({_where.X}, {_where.Y}): {BotQuad.Strength(body):F0} of the {BotQuad.Muscle(_map, _where):F0} it asks, gathered at ({body.X}, {body.Y})"
                );
            }

            Raised++;

            logger.Information(
                "{Name} raised a company for ({X}, {Y}), which asks {Muscle:F0} of strength",
                body.Name,
                _where.X,
                _where.Y,
                BotQuad.Muscle(_map, _where)
            );
        }

        var picked = BotThreat.Hunter(body, BotMobile.NoticeRange);

        if (picked != null && !BotQuarry.Crowded(picked) && !BotQuarry.Handed(picked))
        {
            BotQuarry.Hand(picked);

            return BotDoing.Done("something has picked this fight for us");
        }

        if (BotQuarry.Worthwhile(body))
        {
            return BotDoing.Done("something worth fighting");
        }

        if (body.InRange(_where, ArriveWithin))
        {
            BotHunter.FoundEmpty(body, _where);

            return BotDoing.Done("nothing here");
        }

        var gap = System.Math.Max(System.Math.Abs(body.X - _where.X), System.Math.Abs(body.Y - _where.Y));

        var along = Along(body);

        if (gap < _nearest || along)
        {
            if (gap >= _nearest)
            {
                RoadKept++;
            }

            _nearest = System.Math.Min(_nearest, gap);
            _stalled = 0;
        }
        else if (++_stalled >= TrekLimit)
        {
            BotPeril.Baulked(_map, _where);

            BotQuad.Baulk(_map, _where);
            Baulked++;

            if (_setOut != Point3D.Zero && !body.InRange(_setOut, 2 * BotBarrier.Near))
            {
                BotBarrier.Stopped(_map, body.Location, _where);
            }

            if (!_company && _redarts < Redarts)
            {
                _redarts++;

                var next = Redart(body);

                if (next != Point3D.Zero)
                {
                    Redarted++;

                    _where = next;
                    _nearest = int.MaxValue;
                    _plans = -1;
                    _stalled = 0;
                    _setOut = Point3D.Zero;

                    return BotDoing.Walk(_map, _where, BotArrival.Within(ArriveWithin), "looking for a fight on this side instead");
                }

                Unredarted++;
            }

            return BotDoing.Failed($"got no nearer than {gap} tiles to ({_where.X}, {_where.Y}) from ({body.X}, {body.Y}, {body.Z})");
        }

        if (_setOut == Point3D.Zero)
        {
            _setOut = body.Location;
        }

        return BotDoing.Walk(_map, _where, BotArrival.Within(ArriveWithin), "looking for a fight");
    }

    private Point3D Redart(Mobile body)
    {
        _candidates.Clear();

        for (var i = 0; i < RedartSamples; i++)
        {
            var x = body.X + Utility.RandomMinMax(-RedartReach, RedartReach);
            var y = body.Y + Utility.RandomMinMax(-RedartReach, RedartReach);

            if (!BotStep.Settle(_map, x, y, out var z))
            {
                continue;
            }

            var where = new Point3D(x, y, z);

            if (Utility.InRange(body.Location, where, 2 * ArriveWithin)
                || Region.Find(where, _map)?.IsPartOf<TownRegion>() == true
                || BotRefused.Refusing(_map, where)
                || BotReach.Ask(_map, body.Location, where, BotArrival.Within(ArriveWithin)) == BotReachVerdict.Sealed
                || !BotQuad.Dares(body, _map, where))
            {
                continue;
            }

            _candidates.Add(where);
        }

        var map = _map;

        _candidates.Sort(
            (a, b) =>
            {
                var noise = BotPeril.Reading(map, b).CompareTo(BotPeril.Reading(map, a));

                return noise != 0 ? noise : BotQuad.Safety(map, a).CompareTo(BotQuad.Safety(map, b));
            }
        );

        for (var i = 0; i < _candidates.Count && i < RedartVets; i++)
        {
            if (BotPath.CanReach(_map, body.Location, _candidates[i], BotArrival.Within(ArriveWithin), RedartVetMs))
            {
                return _candidates[i];
            }
        }

        return Point3D.Zero;
    }

    public override bool Bend(IBotWilful bot)
    {
        BotPeril.Baulked(_map, _where);
        BotQuad.Baulk(_map, _where);

        var body = bot?.Self;

        if (body == null || _company || _redarts >= Redarts || body.Map != _map)
        {
            return false;
        }

        _redarts++;

        var next = Redart(body);

        if (next == Point3D.Zero)
        {
            Unturned++;

            return false;
        }

        Turned++;

        _where = next;
        _nearest = int.MaxValue;
        _plans = -1;
        _stalled = 0;
        _setOut = Point3D.Zero;

        return true;
    }

    public override void Drop(IBotWilful bot)
    {
        if (_squad is { Disbanded: false })
        {
            _squad.Charged = false;
        }

        _squad = null;
    }
}
