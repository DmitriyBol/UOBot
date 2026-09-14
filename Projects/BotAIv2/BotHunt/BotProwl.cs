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

    private readonly Map _map;

    private readonly Point3D _where;

    private int _nearest = int.MaxValue;

    private int _stalled;

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
                    foreach (var mobile in _map.GetMobilesInRange<Mobile>(body.Location, BotMuster.Reach))
                    {
                        if (squad.Count >= squad.Ceiling)
                        {
                            break;
                        }

                        if (mobile != body && mobile is IBotSquadMember { Squad: null } other
                            && mobile is IBotAlly { AbleToFight: true })
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

                return BotDoing.Failed($"could not raise enough strength for ({_where.X}, {_where.Y})");
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
            return BotDoing.Done("nothing here");
        }

        var gap = System.Math.Max(System.Math.Abs(body.X - _where.X), System.Math.Abs(body.Y - _where.Y));

        if (gap < _nearest)
        {
            _nearest = gap;
            _stalled = 0;
        }
        else if (++_stalled >= TrekLimit)
        {
            BotPeril.Baulked(_map, _where);

            BotQuad.Baulk(_map, _where);
            Baulked++;

            return BotDoing.Failed($"got no nearer than {gap} tiles to ({_where.X}, {_where.Y})");
        }

        return BotDoing.Walk(_map, _where, BotArrival.Within(ArriveWithin), "looking for a fight");
    }

    public override bool Bend(IBotWilful bot)
    {
        BotPeril.Baulked(_map, _where);
        BotQuad.Baulk(_map, _where);

        return false;
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
