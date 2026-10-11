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
///
/// <para>
/// <b>A sweep of named squares, not a look round where the bot stands (06.10.2026, build next).</b> Patrick, 06.10 ~10:00:
/// "Ideally, when they go on a prowl they are given several quadrants to clear; if they walked them and found nobody, that is
/// fine, that is a success." On build 400 (06.10, 06:12–09:42) 1761 of the 2669 prowls that ended said "nothing here" after a
/// walk to one point a median of 0.6 minutes long, and each counted as finished work. A prowl is now a route over two to four
/// squares of <see cref="BotQuad.Side"/> tiles (<c>BotHunter.Sweep</c>), each entered and crossed — its waypoint reached —
/// before the next. A fight found on the way ends this sweep as finished ("something worth fighting", with the square), the
/// hunt is fought by the hunter's own rules, and the squares left are offered again afterwards (<see cref="BotSweepRecord.Left"/>).
/// "Found nothing" is finished only when every square was walked; a sweep that could not reach its squares fails with the
/// reason. Its price is what sweeps of its squares paid lately (<see cref="BotSweepRecord.Worth"/>), not a flat number.
/// </para>
/// </summary>
public sealed class BotProwl : BotDeed
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotProwl));

    public const string Trade = "prowl";

    public static bool Sweeps { get; set; } = true;

    public static int SweepLeast { get; set; } = 2;

    public static int SweepMost { get; set; } = 4;

    public static double MinutesPerSquare { get; set; } = 1.0;

    public static long SweepsOffered { get; internal set; }

    public static long SquaresOffered { get; internal set; }

    public static long ShortSweeps { get; internal set; }

    public static long SquaresNoWay { get; internal set; }

    public static long SquaresNoGround { get; internal set; }

    public static long FirstNoWay { get; internal set; }

    public static long Underfoot { get; internal set; }

    public static long ResumeOffered { get; internal set; }

    public static long SweepsTaken { get; private set; }

    public static long ResumedTaken { get; private set; }

    public static long SquaresWalked { get; private set; }

    public static long FightsFound { get; private set; }

    public static long EndedEmpty { get; private set; }

    public static long EndedFight { get; private set; }

    public static long EndedCompany { get; private set; }

    public static long FailedUnreached { get; private set; }

    public static long FailedUnraised { get; private set; }

    public static long FailedCompany { get; private set; }

    public static long FailedClosed { get; private set; }

    public static long EndedByWill { get; private set; }

    public static long EndedDead { get; private set; }

    public static long SweepsEnded { get; private set; }

    public static double SweepMinutes { get; private set; }

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

    public BotProwl(Map map, Point3D where, bool company, List<Point3D> legs = null, bool resumed = false)
    {
        _map = map;
        _company = company;
        _legs = legs is { Count: > 0 } ? legs : [where];
        _where = _legs[0];
        _resumedSweep = resumed;
        _price = Price(map, _legs);
    }

    private readonly List<Point3D> _legs;

    private int _leg;

    private int _walked;

    private bool _chasing;

    private readonly bool _resumedSweep;

    private readonly double _price;

    private long _began;

    private string _ending;

    private static double Price(Map map, List<Point3D> legs)
    {
        if (!Sweeps || map == null)
        {
            return Prior;
        }

        var worth = 0.0;

        for (var i = 0; i < legs.Count; i++)
        {
            worth += BotSweepRecord.Worth(map, legs[i]);
        }

        return worth / System.Math.Max(0.1, MinutesPerSquare * legs.Count);
    }

    private string Squares()
    {
        var text = "";

        for (var i = 0; i < _legs.Count; i++)
        {
            text += (i == 0 ? "" : " ") + $"({_legs[i].X}, {_legs[i].Y})";
        }

        return text;
    }

    private string Progress => $"{_walked} of {_legs.Count} squares walked";

    private readonly bool _company;

    public int Seek { get; init; }

    public static int Seeks { get; set; } = 2;

    private int _sought;

    public static long ZoneFollowed { get; private set; }

    public static long ZoneEmpty { get; private set; }

    public static long CameApart { get; private set; }

    public static long ClosedOn { get; private set; }

    public static int LookWider { get; set; } = 48;

    public static long Widened { get; private set; }

    public static int CompanyFights { get; set; } = 6;

    public static long CompanyTookOn { get; private set; }

    private int _companyFights;

    private BotSquad _squad;

    private bool _raised;

    public override double Brings(Mobile body) =>
        _company && !_raised ? System.Math.Max(BotQuad.Strength(body), BotThreat.Power(body) * BotSquad.Usual) : BotQuad.Strength(body);

    private bool _gated;

    private Point3D _gate;

    public static int GatherWithin { get; set; } = 3;

    public static long Raised { get; private set; }

    public static long Unraised { get; private set; }

    public static long Regathered { get; private set; }

    public static int ClaimMs { get; set; } = 60000;

    private static readonly Dictionary<(int Map, int X, int Y), long> _raising = [];

    public static bool Raising(Map map, Point3D where)
    {
        var key = BotQuad.Key(map, where);

        if (_short.TryGetValue(key, out var fell))
        {
            if (Core.TickCount - fell < ShortRestMs)
            {
                return true;
            }

            _short.Remove(key);
        }

        return _raising.TryGetValue(key, out var when) && Core.TickCount - when < ClaimMs;
    }

    public static int ShortRestMs { get; set; } = 600000;

    private static readonly Dictionary<(int Map, int X, int Y), long> _short = [];

    public static long ShortRested { get; private set; }

    private static void Claim(Map map, Point3D where) => _raising[BotQuad.Key(map, where)] = Core.TickCount;

    public override string Kind => Trade;

    public override bool Braves => true;

    public override bool Resumes => true;

    public override void Resumed(IBotWilful bot)
    {
        _plans = -1;
        _planLeast = int.MaxValue;
        _nearest = int.MaxValue;
        _stalled = 0;
    }

    public override bool Guess => true;

    public override Map Map => _map;

    public override Point3D Where => _where;

    public override double Expects => _price;

    public override bool Unpaid => Sweeps;

    public override double Minutes => Sweeps ? MinutesPerSquare * _legs.Count : WorkMinutes;

    public override SkillName? Trains => null;

    public override int Outlay => 0;

    public override double Coin => 1.0;

    public override int Made => 0;

    public static long PoorTaken { get; private set; }

    public override void Taken(IBotWilful bot)
    {
        if (bot?.Self is { } body && BotYield.Spendable(body) < BotHunter.PoorBelow)
        {
            PoorTaken++;
        }

        if (_began == 0)
        {
            _began = Core.TickCount;
            SweepsTaken++;

            if (_resumedSweep)
            {
                ResumedTaken++;
            }

            BotSweepRecord.Taken(bot?.Self);
        }
    }

    public override string Stage =>
        _legs.Count > 1 || _walked > 0 ? $"looking for a fight near {_where}, square {System.Math.Min(_leg + 1, _legs.Count)} of {_legs.Count}" : $"looking for a fight near {_where}";

    public override BotDoing Advance(IBotWilful bot)
    {
        var body = bot?.Self;

        if (body == null || _map == null || _map == Map.Internal)
        {
            return BotDoing.Failed("no body");
        }

        if (BotBarred.Holds(_map, _where))
        {
            ClosedOn++;
            _ending = "closed";

            return BotDoing.Failed($"({_where.X}, {_where.Y}) was closed to the population while it walked there; {Progress}");
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

                    var asked = BotQuad.Muscle(_map, _where);
                    var strength = BotQuad.Strength(body);

                    for (var i = 0; i < _recruits.Count && !BotSquads.Enough(squad, strength, asked); i++)
                    {
                        if (_recruits[i] is not IBotSquadMember other)
                        {
                            continue;
                        }

                        var past = squad.Count >= BotSquad.Usual;

                        if (BotSquads.Enrol(squad, other, _map, body.Location, $"called to {body.Name}'s company for ({_where.X}, {_where.Y})"))
                        {
                            strength += BotThreat.Power(_recruits[i]);

                            if (past)
                            {
                                BotSquads.Past();
                            }
                        }
                    }

                    _recruits.Clear();

                    BotBand.LevyMedic(squad, body, _map, body.Location, $"called to {body.Name}'s company for ({_where.X}, {_where.Y}), as its medic");

                    _squad = squad;
                    squad.Charged = true;

                    squad.OnTheMarch = true;
                }
            }

            if (!BotQuad.Dares(body, _map, _where))
            {
                var instead = Redart(body, company: true);

                if (instead != Point3D.Zero)
                {
                    Regathered++;

                    logger.Information(
                        "{Name} raised {Strength:F0} of the {Asked:F0} that ({X}, {Y}) asks, so the company of {Count} goes to ({IX}, {IY}), which asks {Less:F0}, instead",
                        body.Name,
                        BotQuad.Strength(body),
                        BotQuad.Muscle(_map, _where),
                        _where.X,
                        _where.Y,
                        _squad?.Count ?? 1,
                        instead.X,
                        instead.Y,
                        BotQuad.Muscle(_map, instead)
                    );

                    _where = instead;

                    _legs.Clear();
                    _legs.Add(instead);
                    _leg = 0;

                    return BotDoing.Walk(_map, _where, BotArrival.Within(ArriveWithin), $"to ({_where.X}, {_where.Y}) with what was raised");
                }

                Unraised++;
                _ending = "unraised";
                ShortRested++;
                _short[BotQuad.Key(_map, _where)] = Core.TickCount;

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

        if (_company && _raised)
        {
            if (_squad is { Disbanded: false, Stance: BotSquadStance.Fighting } fighting && (bot as IBotSquadMember)?.Squad == fighting)
            {
                return BotDoing.Work($"standing to a fight on the way to ({_where.X}, {_where.Y}) with the company");
            }

            if (_squad is { Disbanded: true, WithdrewFrom: { } withdrewFrom })
            {
                _squad = null;

                _ending = "company";

                return BotDoing.Failed($"the company raised for ({_where.X}, {_where.Y}) withdrew from {withdrewFrom.Name}, which it could not beat");
            }

            if (_squad is { Disbanded: true } && !BotQuad.Dares(body, _map, _where))
            {
                _squad = null;
                CameApart++;
                _ending = "company";

                return BotDoing.Failed($"the company raised for ({_where.X}, {_where.Y}) came apart on the way, and it asks {BotQuad.Muscle(_map, _where):F0} of one bot's {BotQuad.Strength(body):F0}");
            }
        }

        var picked = BotThreat.Hunter(body, BotMobile.NoticeRange);

        if (_company && _raised && _squad is { Disbanded: false } led && bot is IBotSquadMember leading && leading.Squad == led)
        {
            var foe = picked;

            if (foe == null && BotThreat.Decide(body, BotMobile.NoticeRange) != BotStand.Outmatched)
            {
                foe = BotQuarry.Best(body, BotQuarry.Reach);
            }

            if (foe != null && ReferenceEquals(led.Focus, foe))
            {
                return BotDoing.Work($"standing to a fight on the way to ({_where.X}, {_where.Y}) with the company");
            }

            if (foe != null && led.Beyond(foe, 0.0, picked != null ? BotSquad.WithdrawOdds : 0.0) && !BotSquad.Caster(foe))
            {
                var reason = led.Withdraw(foe, BotSquad.Reckon(foe), led.Able());

                _squad = null;
                BotSquads.Disband(led, reason);
                _ending = "company";

                return BotDoing.Failed($"the company raised for ({_where.X}, {_where.Y}) withdrew from {foe.Name}, which it could not beat");
            }

            if (foe != null && _companyFights < CompanyFights)
            {
                _companyFights++;
                CompanyTookOn++;

                led.Engage(foe, leading, picked == null);

                return BotDoing.Work($"the company takes on {foe.Name} on the way to ({_where.X}, {_where.Y})");
            }

            if (foe != null)
            {
                _ending = "companydone";

                return BotDoing.Done($"the company put down {led.Kills} with it, and {CompanyFights} fights is a prowl's worth");
            }
        }

        if (picked != null && !BotQuarry.Crowded(picked) && !BotQuarry.Handed(picked))
        {
            BotQuarry.Hand(picked);

            return Found(body, "something has picked this fight for us");
        }

        if (BotQuarry.Worthwhile(body))
        {
            return Found(body, "something worth fighting");
        }

        if (body.InRange(_where, ArriveWithin))
        {
            if (!_chasing)
            {
                _walked++;
                SquaresWalked++;
                BotSweepRecord.Walked(_map, _legs[_leg]);
            }

            _chasing = false;

            var look = System.Math.Max(Seek, LookWider);
            var seen = look > BotQuarry.Reach && _sought < Seeks
                ? BotQuarry.Best(body, look) ?? (_raised && _squad is { Disbanded: false } ? BotQuarry.Company(body, look) : null)
                : null;

            if (seen != null)
            {
                _sought++;

                if (Seek > BotQuarry.Reach)
                {
                    ZoneFollowed++;
                }
                else
                {
                    Widened++;
                }

                _where = seen.Location;
                _chasing = true;
                _nearest = int.MaxValue;
                _plans = -1;
                _stalled = 0;

                return BotDoing.Walk(_map, _where, BotArrival.Within(ArriveWithin), $"after {seen.Name} in the zone's patrol");
            }

            if (Seek > 0)
            {
                ZoneEmpty++;
            }

            return NextSquare(body);
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
            if (_chasing)
            {
                _chasing = false;

                return NextSquare(body);
            }

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
                    _legs[_leg] = next;
                    _nearest = int.MaxValue;
                    _plans = -1;
                    _stalled = 0;
                    _setOut = Point3D.Zero;

                    return BotDoing.Walk(_map, _where, BotArrival.Within(ArriveWithin), "looking for a fight on this side instead");
                }

                Unredarted++;
            }

            _ending = "unreached";

            return BotDoing.Failed($"got no nearer than {gap} tiles to ({_where.X}, {_where.Y}) from ({body.X}, {body.Y}, {body.Z}); {Progress}");
        }

        if (_setOut == Point3D.Zero)
        {
            _setOut = body.Location;
        }

        return BotDoing.Walk(_map, _where, BotArrival.Within(ArriveWithin), "looking for a fight");
    }

    private BotDoing NextSquare(Mobile body)
    {
        BotHunter.FoundEmpty(body, _legs[_leg]);

        _leg++;

        if (_leg >= _legs.Count)
        {
            _leg = _legs.Count - 1;
            _ending = "empty";

            return BotDoing.Done(
                Sweeps ? $"swept {_walked} square{(_walked == 1 ? "" : "s")} and found nothing: {Squares()}" : "nothing here"
            );
        }

        _where = _legs[_leg];
        _nearest = int.MaxValue;
        _plans = -1;
        _stalled = 0;
        _setOut = Point3D.Zero;

        return BotDoing.Walk(_map, _where, BotArrival.Within(ArriveWithin), "looking for a fight");
    }

    private BotDoing Found(Mobile body, string what)
    {
        FightsFound++;
        _ending = "fight";

        BotSweepRecord.Found(body, _map, body.Location);

        var from = _chasing ? _leg + 1 : _leg;

        var keep = Sweeps && !_company && from < _legs.Count && !(_resumedSweep && _walked == 0 && Core.TickCount - _began < 5000);

        if (keep)
        {
            BotSweepRecord.Leave(body, _map, _legs, from);
        }

        if (!Sweeps || _legs.Count == 1 && _walked == 0)
        {
            return BotDoing.Done(what);
        }

        return BotDoing.Done($"{what} in the square at ({body.X}, {body.Y}), {Progress}{(keep ? "; the rest are left for after it" : "")}");
    }

    private Point3D Redart(Mobile body, bool company = false)
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
                || !BotQuad.Dares(body, _map, where)
                || company && BotQuad.Muscle(_map, where) <= 0.0)
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

        if (!_chasing && bot?.Self is BotMobile { LastWalk: BotWalkResult.Refused or BotWalkResult.GaveUp })
        {
            BotSweepRecord.Wayless(_map, _where);
        }

        var body = bot?.Self;

        if (body == null || _company || _redarts >= Redarts || body.Map != _map)
        {
            _ending = "unreached";

            return false;
        }

        _redarts++;

        var next = Redart(body);

        if (next == Point3D.Zero)
        {
            Unturned++;
            _ending = "unreached";

            return false;
        }

        Turned++;

        _where = next;
        _legs[_leg] = next;
        _chasing = false;
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
            _squad.OnTheMarch = false;
        }

        _squad = null;

        Ended(bot?.Self);
    }

    private bool _counted;

    private void Ended(Mobile body)
    {
        if (_counted)
        {
            return;
        }

        _counted = true;

        var minutes = _began == 0 ? 0.0 : (Core.TickCount - _began) / 60000.0;

        SweepsEnded++;
        SweepMinutes += minutes;

        string how;

        switch (_ending)
        {
            case "empty":
                EndedEmpty++;
                how = "every square walked and nothing found";
                break;
            case "fight":
                EndedFight++;
                how = "a fight found";
                break;
            case "companydone":
                EndedCompany++;
                how = "its company's fights done";
                break;
            case "unreached":
                FailedUnreached++;
                how = "failed: could not reach its square";
                break;
            case "unraised":
                FailedUnraised++;
                how = "failed: could not raise the company its ground asked";
                break;
            case "company":
                FailedCompany++;
                how = "failed: its company came apart or withdrew";
                break;
            case "closed":
                FailedClosed++;
                how = "failed: its ground was closed";
                break;
            default:
                if (body is not { Deleted: false, Alive: true })
                {
                    EndedDead++;
                    how = "the bot died";
                    break;
                }

                EndedByWill++;
                how = "ended by the will (turned back, outbid, put down or stalled: the will's line says which)";

                if (Sweeps && !_company && body.Map == _map && (body.Combatant != null || BotThreat.Hunter(body, BotMobile.NoticeRange) != null))
                {
                    var from = _chasing ? _leg + 1 : _leg;

                    if (from < _legs.Count)
                    {
                        BotSweepRecord.Leave(body, _map, _legs, from);
                        how += "; a fight at hand, the squares left wait for it";
                    }
                }

                break;
        }

        if (Sweeps)
        {
            logger.Information(
                "{Name} ended a sweep of {Count} squares {Squares}{Again}: {Walked} walked, {How}, in {Minutes:F1} min",
                body?.Name,
                _legs.Count,
                Squares(),
                _resumedSweep ? " (taken up again after a fight)" : "",
                _walked,
                how,
                minutes
            );
        }
    }

    public static string DescribeSweeps() =>
        Sweeps
            ? $"prowls as sweeps of {SweepLeast}-{SweepMost} squares: {SweepsOffered} offered naming {SquaresOffered} squares ({ShortSweeps} with one square only, {ResumeOffered} offered again after a fight; {Underfoot} squares passed over for being where the bot stood or had lately found nothing; {SquaresNoWay} looks at a square refused for a sweep's walk to it failing within the hour and {SquaresNoGround} for ground the reach map calls an island or a pocket, {FirstNoWay} prowls not offered for their first square), "
              + $"{SweepsTaken} taken ({ResumedTaken} taken up again), {SquaresWalked} squares walked, {FightsFound} fights found; "
              + $"of {SweepsEnded} ended: {EndedEmpty} every square walked and nothing found, {EndedFight} a fight found, {EndedCompany} a company's fights done; failed {FailedUnreached} could not reach a square, {FailedUnraised} could not raise its company, {FailedCompany} company came apart or withdrew, {FailedClosed} ground closed; "
              + $"{EndedByWill} ended by the will (turned back, outbid, put down, stalled), {EndedDead} died; {(SweepsEnded > 0 ? SweepMinutes / SweepsEnded : 0.0):F1} minutes a sweep; {BotSweepRecord.Describe()}"
            : "prowls as sweeps: off";

    public override bool Alongside => _squad is { Disbanded: false };
}
