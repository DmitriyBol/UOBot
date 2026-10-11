using System;
using System.Collections.Generic;
using Server.Logging;
using Server.Mobiles;
using Server.Regions;

namespace Server.BotAI.V2;

/// <summary>
/// Offers a fight to any bot healthy enough to want one.
///
/// <para>
/// <b>Nothing decides who is a fighter.</b> No class list, no role check: whoever can beat the thing standing
/// in the field is offered it, and a mage with wrestling at thirty will find that the arithmetic says no. That
/// is the same rule as the pickaxe and the pen — the ability decides, not the name — and it means a crafter
/// caught in a lean patch can go and hit something rather than sitting in a census as "nothing was worth
/// doing".
/// </para>
///
/// <para>
/// <b>Only within the population's own ground, and that is the whole of the answer to the first version's
/// worst night.</b> Four hundred and forty-three deaths, a hundred and four of them one bot resurrecting in
/// the same tile every thirty seconds, all of it in the far zones the population had walked to. A hunt that
/// cannot begin more than a screen and a half from where the bot is standing, in a world already bounded to
/// two hundred tiles around the spawn, cannot build that loop: the bot is never far from where it gets up.
/// </para>
///
/// <para>
/// The cost of this proposer is a real spatial sweep, every time a free bot asks. That is unavoidable and it
/// is the honest exception to "ask the world cheaply": a vein stays where it is and can be remembered, a shop
/// keeper stands still, but a monster walks and respawns, so a remembered one is a lie inside a minute.
/// </para>
/// </summary>
public sealed class BotHunter : IBotProposer
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotHunter));

    public static double FitAt { get; set; } = 0.8;

    private static bool _saidNoQuarry;

    public string Name => "Hunter";

    public BotStanding Rung => BotStanding.Free;

    public BotDeed Propose(IBotWilful bot)
    {
        var body = bot?.Self;
        var map = body?.Map;

        if (map == null || map == Map.Internal || !body.Alive)
        {
            return null;
        }

        if (body.HitsMax <= 0 || body.Hits < body.HitsMax * FitAt)
        {
            return null;
        }

        if (body is BotMobile { Class.DefendsOnly: true })
        {
            Sworn++;

            return null;
        }

        if (BotThreat.Decide(body, BotMobile.NoticeRange) == BotStand.Outmatched)
        {
            var elsewhere = Hunting(bot, body, map, out var outmatched, out var reach, out _);

            return Offer(bot, body, map, elsewhere, outmatched, reach);
        }

        var quarry = BotQuarry.Paying(bot, body);

        if (quarry != null && GrazersLast && BotQuarry.HostileFirst
            && !ReferenceEquals(quarry.Combatant, body) && !BotQuarry.Fights(quarry))
        {
            var ground = Hunting(bot, body, map, out var needsCompany, out var zoneSeek, out var fighter);

            if (ground != Point3D.Zero && fighter is { Deleted: false, Alive: true }
                && BotQuarry.Ours(body, fighter) && !BotQuarry.Shunned(fighter) && !BotQuarry.Crowded(fighter))
            {
                if (needsCompany)
                {
                    GrazerCompanied++;

                    return Offer(bot, body, map, ground, true, zoneSeek);
                }

                GrazerPassed++;
                quarry = fighter;
            }
        }

        if (quarry != null
            && BotThreat.Overrun(body, quarry, BotMobile.NoticeRange))
        {
            BotQuarry.Crowd(quarry);
            Overrun++;

            quarry = null;
        }

        if (quarry == null)
        {
            Missing(map);

            var again = Again(body, map);

            if (again != null)
            {
                return again;
            }

            var ground = Hunting(bot, body, map, out var company, out var seek, out _);

            return Offer(bot, body, map, ground, company, seek);
        }

        BotQuarry.Claim(body, quarry);
        BotQuarry.Chosen(quarry, body);

        var trains = bot.Bond?.Weapon?.Skill ?? SkillName.Wrestling;

        return new BotSlay(quarry, trains);
    }

    private static Point3D Hunting(IBotWilful bot, Mobile body, Map map, out bool company, out int seek, out BaseCreature sighted)
    {
        company = false;
        seek = 0;
        sighted = null;

        var ledger = bot?.Resolve?.Ledger;
        var home = BotPopulation.HomeOf(body);
        var roam = Math.Clamp(BotPopulation.Leash(body) / 2, BotQuarry.Reach, Walkable);

        var best = Point3D.Zero;
        var bestPaid = -1.0;
        var bestKnown = -1.0;
        var bestWanted = false;
        var bestLive = false;

        var refused = 0;

        var needsCompany = false;
        var bestNeedsCompany = false;

        var quietest = Point3D.Zero;
        var quietestSafety = 0.0;

        var noisy = Noisy(body, map, roam);
        var paying = Paying(body, map, roam);

        var feared = Feared(body, map);

        var material = BotCharter.Carved(body);
        var yielding = material == null ? Point3D.Zero : BotQuad.Yielding(map, body.Location, roam, material);
        var bestBoarded = false;

        var mainland = BotGates.Ready ? BotGates.LandOf(map, BotPopulation.Where) : -1;
        var overWater = mainland >= 0 ? BotGates.NearestMoongate(map, body.Location, mainland, Walkable) : null;
        var landing = overWater?.To ?? Point3D.Zero;

        var zoned = Zoned(body, map, home, roam, out var zoneSeek, out var zoneCreature);

        for (var tries = 0; tries <= Samples + 4; tries++)
        {
            Point3D where;

            needsCompany = false;

            if (tries == 0)
            {
                if (noisy == Point3D.Zero)
                {
                    continue;
                }

                where = noisy;
            }
            else if (tries == 1)
            {
                if (paying == Point3D.Zero)
                {
                    continue;
                }

                where = paying;
            }
            else if (tries == 2)
            {
                if (feared == Point3D.Zero)
                {
                    continue;
                }

                where = feared;
            }
            else if (tries == 3)
            {
                if (yielding == Point3D.Zero)
                {
                    continue;
                }

                where = yielding;
            }
            else if (tries == 4)
            {
                if (zoned == Point3D.Zero)
                {
                    continue;
                }

                where = zoned;
            }
            else
            {
                var centre = landing != Point3D.Zero && (tries & 1) == 0 ? landing : home;
                var x = centre.X + Utility.RandomMinMax(-roam, roam);
                var y = centre.Y + Utility.RandomMinMax(-roam, roam);

                if (!BotStep.Settle(map, x, y, out var z))
                {
                    continue;
                }

                where = new Point3D(x, y, z);
            }

            var live = tries == 4;

            var near = live && body.Region?.IsPartOf<TownRegion>() == true ? BotProwl.ArriveWithin : BotQuarry.Reach;

            if (Utility.InRange(body.Location, where, near))
            {
                continue;
            }

            if (!BotPopulation.Within(map, where, body))
            {
                continue;
            }

            if (!BotPopulation.Reachable(map, body.Location, where))
            {
                continue;
            }

            var overland = landing != Point3D.Zero && BotGates.LandOf(map, where) == mainland;

            if (overland ? !Utility.InRange(landing, where, Walkable) : !Utility.InRange(body.Location, where, Walkable))
            {
                Distant++;

                continue;
            }

            if (overland)
            {
                OverWater++;
            }

            if (BotGates.Ready && !BotGates.Joined(map, body.Location, where))
            {
                Unjoined++;

                continue;
            }

            if (BotRoads.Detour(map, body.Location, where) > Detour || BotRoads.Behind(map, body.Location, where) > Detour)
            {
                Roundabout++;

                if (ReadsRoads)
                {
                    continue;
                }
            }

            if (TroddenOnly && !Walked(map, where))
            {
                if (live)
                {
                    ZoneSpared++;
                }
                else
                {
                    Untrodden++;

                    continue;
                }
            }

            if (Region.Find(where, map)?.IsPartOf<TownRegion>() == true)
            {
                continue;
            }

            if (BotRefused.Refusing(map, where) || BotQuad.Baulking(map, where) || NoWay(map, where))
            {
                Darted++;

                continue;
            }

            if (BotReach.Ask(map, body.Location, where, BotArrival.Within(BotQuarry.Reach)) == BotReachVerdict.Sealed)
            {
                continue;
            }

            if (BotBarrier.Beyond(map, body.Location, where))
            {
                continue;
            }

            if (!live && LatelyEmpty(body, where))
            {
                Revisits++;

                continue;
            }

            var dares = BotQuad.Dares(body, map, where);
            var lethal = BotPeril.Overwhelms(map, where, body.Location, BotQuad.Strength(body), out _, count: false);

            if (ReadsZones)
            {
                var level = BotZones.LevelAt(map, where.X, where.Y, out _, out var threat);

                if (level == BotZoneLevel.Deadly && !lethal)
                {
                    lethal = true;
                    ZoneDeadly++;
                }
                else if (level == BotZoneLevel.Hostile && dares && threat > BotQuad.Strength(body) * ZoneDare)
                {
                    dares = false;
                    ZoneTooStrong++;
                }
            }

            if (lethal && dares)
            {
                Deadly++;
            }

            if (dares && !lethal && BotDanger.Road(body, map, body.Location, where, BotQuad.Strength(body), out _, out _, out _))
            {
                dares = false;
                RoadTooStrong++;
            }

            if (lethal || !dares)
            {
                if (overland)
                {
                    Overmatched++;
                    AbroadTooHard++;

                    continue;
                }

                var gate = BotPopulation.Gate(map, body.Location, where, counted: false);

                if (!BotQuad.Together(body, map, where, BotMuster.Reach, gate))
                {
                    Overmatched++;

                    continue;
                }

                if (BotProwl.Raising(map, where))
                {
                    Claimed++;

                    continue;
                }

                needsCompany = true;
            }

            var paid = ledger?.Expect(BotSlay.Trade, map, where, 0.0) ?? 0.0;

            var known = BotPeril.Reading(map, where);

            var safety = BotQuad.Safety(map, where);

            if (safety > BotQuad.TooQuiet && live)
            {
                ZoneSpared++;
            }
            else if (safety > BotQuad.TooQuiet)
            {
                Quiet++;
                refused++;

                if (quietest == Point3D.Zero || safety < quietestSafety)
                {
                    quietest = where;
                    quietestSafety = safety;
                }

                continue;
            }

            var wanted = safety <= BotQuad.Wanted;

            if (wanted && BotLadder.Novice(body))
            {
                Green++;
                refused++;

                continue;
            }

            var boarded = material != null && BotQuad.YieldsAt(map, where, material);

            var better = best == Point3D.Zero
                || (boarded && !bestBoarded)
                || (boarded == bestBoarded
                    && ((ZoneFirst && live && !bestLive)
                        || ((!ZoneFirst || live == bestLive)
                            && ((wanted && !bestWanted)
                                || (wanted == bestWanted && (paid > bestPaid || (paid >= bestPaid && known > bestKnown)))))));

            if (better)
            {
                best = where;
                bestPaid = paid;
                bestKnown = known;
                bestWanted = wanted;
                bestBoarded = boarded;
                bestLive = live;
                bestNeedsCompany = needsCompany;
                company = needsCompany;

                if (wanted)
                {
                    Sought++;
                }

                if (boarded)
                {
                    Boarded++;
                }
            }
        }

        if (best == Point3D.Zero && refused > 0)
        {
            Stranded++;

            return quietest;
        }

        if (zoned != Point3D.Zero && best == zoned)
        {
            ZoneChosen++;
            seek = zoneSeek;

            if (zoneCreature != null && BotQuarry.Fights(zoneCreature))
            {
                sighted = zoneCreature;
            }
        }

        return best;
    }

    private static BotProwl Offer(IBotWilful bot, Mobile body, Map map, Point3D ground, bool company, int seek)
    {
        if (ground == Point3D.Zero)
        {
            return null;
        }

        if (NoWay(map, ground))
        {
            BotProwl.FirstNoWay++;

            return null;
        }

        List<Point3D> legs = null;

        if (BotProwl.Sweeps)
        {
            legs = Sweep(bot, body, map, ground, company);

            BotProwl.SweepsOffered++;
            BotProwl.SquaresOffered += legs.Count;

            if (!company && legs.Count < BotProwl.SweepLeast)
            {
                BotProwl.ShortSweeps++;
            }
        }

        return new BotProwl(map, ground, company, legs) { Seek = seek };
    }

    private static readonly List<(Point3D At, double Score)> _ring = [];

    public static int SweepTries { get; set; } = 3;

    private static List<Point3D> Sweep(IBotWilful bot, Mobile body, Map map, Point3D first, bool company)
    {
        var legs = new List<Point3D>(Math.Max(1, BotProwl.SweepMost)) { first };

        if (company || BotProwl.SweepMost <= 1)
        {
            return legs;
        }

        var ledger = bot?.Resolve?.Ledger;
        var strength = BotQuad.Strength(body);
        var own = BotQuad.Key(map, body.Location);
        var last = BotQuad.Key(map, first);
        var now = Core.TickCount;
        var novice = BotLadder.Novice(body);

        while (legs.Count < BotProwl.SweepMost)
        {
            _ring.Clear();

            for (var dx = -1; dx <= 1; dx++)
            {
                for (var dy = -1; dy <= 1; dy++)
                {
                    if (dx == 0 && dy == 0)
                    {
                        continue;
                    }

                    var qx = last.X + dx;
                    var qy = last.Y + dy;

                    if (own.Map == last.Map && own.X == qx && own.Y == qy)
                    {
                        BotProwl.Underfoot++;

                        continue;
                    }

                    if (Listed(map, legs, qx, qy))
                    {
                        continue;
                    }

                    var mx = qx * BotQuad.Side + BotQuad.Side / 2;
                    var my = qy * BotQuad.Side + BotQuad.Side / 2;
                    var probe = new Point3D(mx, my, 0);
                    var quad = BotQuad.Known(map, probe);

                    if (quad == null || !quad.Trodden || quad.Townbound || quad.Deep)
                    {
                        continue;
                    }

                    if (BotQuad.Baulking(map, probe) || BotRefused.Refusing(map, probe) || BotBarred.Holds(map, probe)
                        || !BotPopulation.Within(map, probe, body) || NoWay(map, probe))
                    {
                        continue;
                    }

                    if (LatelyEmpty(body, probe))
                    {
                        BotProwl.Underfoot++;

                        continue;
                    }

                    var level = BotZoneLevel.Clear;
                    var threat = 0f;

                    if (ReadsZones)
                    {
                        level = BotZones.LevelAt(map, mx, my, out _, out threat);
                    }

                    if (level == BotZoneLevel.Deadly || level is BotZoneLevel.Hostile or BotZoneLevel.Edge && threat > strength * ZoneDare)
                    {
                        continue;
                    }

                    var safety = BotQuad.Safety(map, probe);

                    if (novice && safety <= BotQuad.Wanted)
                    {
                        continue;
                    }

                    var mobs = quad.Mobs > 0 && now - quad.Sighted < BotQuad.SightMs ? Math.Min(quad.Mobs, 5) : 0;

                    var score = (level == BotZoneLevel.Hostile ? 20.0 : level == BotZoneLevel.Edge ? 10.0 : 0.0)
                                + mobs * 3.0
                                + BotSweepRecord.Worth(map, probe)
                                + (ledger?.Expect(BotSlay.Trade, map, probe, 0.0) ?? 0.0) / 10.0
                                - (safety > BotQuad.TooQuiet ? 5.0 : 0.0);

                    _ring.Add((probe, score));
                }
            }

            if (_ring.Count == 0)
            {
                break;
            }

            _ring.Sort(static (a, b) => b.Score.CompareTo(a.Score));

            var next = Point3D.Zero;

            for (var i = 0; i < _ring.Count && i < SweepTries; i++)
            {
                if (!StandIn(map, _ring[i].At, out var stand))
                {
                    continue;
                }

                if (Region.Find(stand, map)?.IsPartOf<TownRegion>() == true
                    || !BotQuad.Dares(body, map, stand)
                    || BotPeril.Overwhelms(map, stand, body.Location, strength, out _, count: false)
                    || BotReach.Ask(map, body.Location, stand, BotArrival.Within(BotProwl.ArriveWithin)) == BotReachVerdict.Sealed
                    || NoWay(map, stand))
                {
                    continue;
                }

                next = stand;

                break;
            }

            if (next == Point3D.Zero)
            {
                break;
            }

            legs.Add(next);
            last = BotQuad.Key(map, next);
        }

        _ring.Clear();

        return legs;
    }

    public static bool NoWay(Map map, Point3D where)
    {
        if (map == null || where == Point3D.Zero)
        {
            return false;
        }

        if (BotSweepRecord.NoWay(map, where))
        {
            BotProwl.SquaresNoWay++;

            return true;
        }

        if (BotReachMap.Class(map, where.X, where.Y, where.Z) is BotReachClass.Isle or BotReachClass.Pocket)
        {
            BotProwl.SquaresNoGround++;

            return true;
        }

        return false;
    }

    private static bool Listed(Map map, List<Point3D> legs, int qx, int qy)
    {
        for (var i = 0; i < legs.Count; i++)
        {
            var key = BotQuad.Key(map, legs[i]);

            if (key.X == qx && key.Y == qy)
            {
                return true;
            }
        }

        return false;
    }

    private static bool StandIn(Map map, Point3D middle, out Point3D stand)
    {
        for (var i = 0; i < 5; i++)
        {
            var x = middle.X + (i == 1 ? 8 : i == 2 ? -8 : 0);
            var y = middle.Y + (i == 3 ? 8 : i == 4 ? -8 : 0);

            if (BotStep.Settle(map, x, y, out var z))
            {
                stand = new Point3D(x, y, z);

                return true;
            }
        }

        stand = Point3D.Zero;

        return false;
    }

    private static BotProwl Again(Mobile body, Map map)
    {
        if (!BotProwl.Sweeps)
        {
            return null;
        }

        var left = BotSweepRecord.Left(body, map);

        if (left == null)
        {
            return null;
        }

        var strength = BotQuad.Strength(body);
        var legs = new List<Point3D>(left.Length);

        for (var i = 0; i < left.Length; i++)
        {
            var at = left[i];

            if (BotBarred.Holds(map, at) || BotQuad.Baulking(map, at) || BotRefused.Refusing(map, at) || !BotQuad.Dares(body, map, at)
                || BotPeril.Overwhelms(map, at, body.Location, strength, out _, count: false))
            {
                continue;
            }

            legs.Add(at);
        }

        if (legs.Count == 0)
        {
            BotSweepRecord.Taken(body);

            return null;
        }

        BotProwl.ResumeOffered++;

        return new BotProwl(map, legs[0], false, legs, resumed: true);
    }

    private static Point3D Paying(Mobile body, Map map, int roam)
    {
        var rich = BotCommons.Richest(BotSlay.Trade, map, body.Location, roam);

        if (rich == Point3D.Zero || !BotStep.Settle(map, rich.X, rich.Y, out var z))
        {
            return Point3D.Zero;
        }

        var where = new Point3D(rich.X, rich.Y, z);

        if (Utility.InRange(body.Location, where, BotQuarry.Reach))
        {
            return Point3D.Zero;
        }

        if (BotQuad.Baulking(map, where) || BotRefused.Refusing(map, where) || NoWay(map, where))
        {
            Rested++;

            return Point3D.Zero;
        }

        return BotReach.Ask(map, body.Location, where, BotArrival.Within(BotQuarry.Reach)) == BotReachVerdict.Sealed
            ? Point3D.Zero
            : where;
    }

    private static Point3D Feared(Mobile body, Map map)
    {
        var middle = BotQuad.WorstNear(map, BotPopulation.HomeOf(body), FearedReach);

        if (middle == Point2D.Zero || !BotStep.Settle(map, middle.X, middle.Y, out var z))
        {
            return Point3D.Zero;
        }

        var where = new Point3D(middle.X, middle.Y, z);

        if (Utility.InRange(body.Location, where, BotQuarry.Reach))
        {
            return Point3D.Zero;
        }

        return BotReach.Ask(map, body.Location, where, BotArrival.Within(BotQuarry.Reach)) == BotReachVerdict.Sealed
            ? Point3D.Zero
            : where;
    }

    private static Point3D Noisy(Mobile body, Map map, int roam)
    {
        var worst = BotPeril.Worst(map, body.Location, roam, out _);

        if (worst == Point3D.Zero || !BotStep.Settle(map, worst.X, worst.Y, out var z))
        {
            return Point3D.Zero;
        }

        var where = new Point3D(worst.X, worst.Y, z);

        if (Utility.InRange(body.Location, where, BotQuarry.Reach)
            || Region.Find(where, map)?.IsPartOf<TownRegion>() == true
            || BotReach.Ask(map, body.Location, where, BotArrival.Within(BotQuarry.Reach)) == BotReachVerdict.Sealed)
        {
            return Point3D.Zero;
        }

        if (BotQuad.Baulking(map, where) || BotRefused.Refusing(map, where) || NoWay(map, where))
        {
            Rested++;

            return Point3D.Zero;
        }

        return where;
    }

    private const int Samples = 8;

    public static int Near { get; set; } = 5;

    public static long Overrun { get; private set; }

    public static long Rested { get; private set; }

    public static bool TroddenOnly { get; set; } = true;

    public static long Untrodden { get; private set; }

    private static bool Walked(Map map, Point3D where)
    {
        if (BotQuad.Trodden(map, where))
        {
            return true;
        }

        for (var dx = -1; dx <= 1; dx++)
        {
            for (var dy = -1; dy <= 1; dy++)
            {
                if ((dx != 0 || dy != 0)
                    && BotQuad.Trodden(map, new Point3D(where.X + dx * BotQuad.Side, where.Y + dy * BotQuad.Side, where.Z)))
                {
                    return true;
                }
            }
        }

        return false;
    }

    public static long Distant { get; private set; }

    public static long Darted { get; private set; }

    public static int EmptyRestMs { get; set; } = 900000;

    public static int EmptyNear { get; set; } = 16;

    public static long Revisits { get; private set; }

    private static readonly Dictionary<Serial, List<(Point3D At, long Tick)>> _empty = [];

    private const int EmptyKept = 8;

    public static void FoundEmpty(Mobile body, Point3D at)
    {
        if (body == null || EmptyRestMs <= 0)
        {
            return;
        }

        if (!_empty.TryGetValue(body.Serial, out var grounds))
        {
            grounds = [];
            _empty[body.Serial] = grounds;
        }

        if (grounds.Count >= EmptyKept)
        {
            grounds.RemoveAt(0);
        }

        grounds.Add((at, Core.TickCount));
    }

    private static bool LatelyEmpty(Mobile body, Point3D where)
    {
        if (EmptyRestMs <= 0 || !_empty.TryGetValue(body.Serial, out var grounds))
        {
            return false;
        }

        var now = Core.TickCount;

        for (var i = 0; i < grounds.Count; i++)
        {
            if (now - grounds[i].Tick < EmptyRestMs && Utility.InRange(grounds[i].At, where, EmptyNear))
            {
                return true;
            }
        }

        return false;
    }

    public static bool ReadsRoads { get; set; } = true;

    public static int Detour { get; set; } = 300;

    public static long Roundabout { get; private set; }

    public static int FearedReach { get; set; } = 800;

    public static long Unjoined { get; private set; }

    public static bool ReadsZones { get; set; } = true;

    public static double ZoneDare { get; set; } = 1.0;

    public static long ZoneOffered { get; private set; }

    public static long ZoneDeadly { get; private set; }

    public static long ZoneChosen { get; private set; }

    public static long ZoneTooStrong { get; private set; }

    public static long RoadTooStrong { get; private set; }

    private static Point3D Zoned(Mobile body, Map map, Point3D home, int roam, out int seek, out BaseCreature creature)
    {
        seek = 0;
        creature = null;

        if (!ReadsZones || BotZones.State is not { } state || state.Zones.Length == 0)
        {
            return Point3D.Zero;
        }

        var strength = BotQuad.Strength(body);

        var together = ZoneCompanies ? Math.Max(strength, BotThreat.OurPower(body, BotMuster.Reach)) : strength;

        _looks.Clear();

        for (var i = 0; i < state.Zones.Length; i++)
        {
            var zone = state.Zones[i];

            if (zone.Water || zone.Live <= 0 || zone.Stand == Point3D.Zero)
            {
                continue;
            }

            if (!Utility.InRange(home, zone.Stand, roam))
            {
                continue;
            }

            double quarry;
            double worth;

            if (zone.Level >= BotZoneLevel.Hostile)
            {
                if (zone.Threat > together * ZoneDare)
                {
                    continue;
                }

                var forCompany = zone.Threat > strength * ZoneDare;

                if (zone.Threat * BotQuarry.ZonePrice(zone.Kinds, forCompany) > together * ZoneDare)
                {
                    BotQuarry.ZoneRefused();

                    continue;
                }

                if (forCompany && zone.Threat * BotQuarry.ZonePrice(zone.Kinds) > together * ZoneDare)
                {
                    BotCompanyRecord.Zoned();
                }

                if (zone.Threat > strength * ZoneDare)
                {
                    ZoneForCompany++;
                }

                quarry = Math.Min(zone.Aggressive, 20);
                worth = strength > 0.0 ? 0.5 + zone.Threat / strength : 1.0;
            }
            else
            {
                quarry = Math.Min(zone.Live, 20) * 0.5;
                worth = 0.5;
            }

            var away = Math.Max(Math.Abs(zone.Stand.X - body.X), Math.Abs(zone.Stand.Y - body.Y));
            var score = quarry * worth * BotQuarry.ZonePays(zone.Kinds, body) / (1.0 + away / 150.0);

            if (score <= 0.0)
            {
                continue;
            }

            var at = _looks.Count;

            while (at > 0 && _looks[at - 1].Score < score)
            {
                at--;
            }

            if (at < ZoneLooks)
            {
                _looks.Insert(at, (zone, score));

                if (_looks.Count > ZoneLooks)
                {
                    _looks.RemoveAt(_looks.Count - 1);
                }
            }
        }

        for (var i = 0; i < _looks.Count; i++)
        {
            var zone = _looks[i].Zone;
            var reach = Math.Clamp(Math.Max(zone.DeclaredRange, zone.ObservedRange), ZoneReachLeast, ZoneReach);

            var company = zone.Level >= BotZoneLevel.Hostile && zone.Threat > strength * ZoneDare;
            var live = Living(body, map, zone, reach, company ? together : strength, company);

            if (live != null)
            {
                ZoneOffered++;
                ZoneLive++;
                seek = reach;
                creature = live;
                _looks.Clear();

                return live.Location;
            }

            ZoneUnpeopled++;
        }

        _looks.Clear();

        return Point3D.Zero;
    }

    private static BaseCreature Living(Mobile body, Map map, BotZone zone, int reach, double strength, bool company = false)
    {
        BaseCreature best = null;
        var bestAway = int.MaxValue;
        var fights = zone.Level >= BotZoneLevel.Hostile;

        foreach (var creature in map.GetMobilesInRange<BaseCreature>(zone.Stand, reach))
        {
            if (creature is not { Deleted: false, Alive: true } || creature.Controlled || creature.Summoned || creature.IsDeadBondedPet
                || creature.Blessed || creature is BaseVendor || creature.CanSwim && !BotStep.Settle(map, creature.X, creature.Y, out _))
            {
                continue;
            }

            if (fights && creature.FightMode is FightMode.None or FightMode.Aggressor)
            {
                continue;
            }

            var away = Math.Max(Math.Abs(creature.X - body.X), Math.Abs(creature.Y - body.Y));

            if (away >= bestAway || BotThreat.Power(creature) > strength * BotQuarry.Daring)
            {
                continue;
            }

            if (Notoriety.Compute(body, creature) == Notoriety.Innocent || !BotQuarry.Ours(body, creature) || BotQuarry.Shunned(creature))
            {
                continue;
            }

            if ((company ? BotCompanyRecord.Priced(creature) : BotQuarry.Priced(creature)) > strength * BotQuarry.Daring)
            {
                BotQuarry.ZoneRefused();

                continue;
            }

            best = creature;
            bestAway = away;
        }

        return best;
    }

    public static int PoorBelow { get; set; } = 100;

    public static bool ZoneFirst { get; set; } = true;

    public static bool GrazersLast { get; set; } = true;

    public static long GrazerPassed { get; private set; }

    public static bool ZoneCompanies { get; set; } = true;

    public static long ZoneForCompany { get; private set; }

    public static long GrazerCompanied { get; private set; }

    public static long ZoneSpared { get; private set; }

    public static int ZoneLooks { get; set; } = 3;

    public static int ZoneReach { get; set; } = 40;

    public static int ZoneReachLeast { get; set; } = 8;

    public static long ZoneLive { get; private set; }

    public static long ZoneUnpeopled { get; private set; }

    private static readonly List<(BotZone Zone, double Score)> _looks = [];

    public static long OverWater { get; private set; }

    public static long AbroadTooHard { get; private set; }

    public static int Walkable
    {
        get => _walkable > 0 ? _walkable : Default;
        set => _walkable = value;
    }

    public static int Default { get; set; } = 500;

    private static int _walkable;

    private static void Missing(Map map)
    {
        if (_saidNoQuarry)
        {
            return;
        }

        _saidNoQuarry = true;

        logger.Error(
            "Nothing within {Reach} tiles of the bots on {Map} is worth fighting, so no gold will enter the world",
            BotQuarry.Reach,
            map
        );
    }

    public static long Sworn { get; private set; }

    public static long Quiet { get; private set; }

    public static long Stranded { get; private set; }

    public static long Overmatched { get; private set; }

    public static long Deadly { get; private set; }

    public static long Green { get; private set; }

    public static long Claimed { get; private set; }

    public static long Sought { get; private set; }

    public static long Boarded { get; private set; }

    public static string Describe() =>
        $"{Sworn} answers went to classes that only defend; {Quiet} hunting grounds passed over as too quiet (above {BotQuad.TooQuiet:F2}), {Sought} picked for having hurt somebody (at or below {BotQuad.Wanted:F2}), {Boarded} picked for a creature the guild's board asks for, {Stranded} hunters left with nowhere to walk at all because every ground they looked at was too quiet, {Overmatched} grounds passed over for asking more strength than whoever looked had, {Deadly} grounds where bots had lately died kept to companies, {Green} grounds that had hurt somebody passed over for a novice (main skill under {BotLadder.NoviceSkill:F0}), {Claimed} for somebody already raising a company for them, {Overrun} quarry passed over for the crowd already round it, {Rested} named grounds passed over as resting after refusing somebody and {Darted} sampled ones, {Revisits} for lying where the asker had lately found nothing, {Distant} further from the asker than {Walkable} tiles, which is as far as a road is ever searched for, {Unjoined} on ground no gate joins to the hunter, {OverWater} weighed over the water beyond a moongate for a hunter living on another land ({AbroadTooHard} of them passed over for asking more than it alone brings), {Roundabout} whose road from home runs more than {Detour} tiles past the straight line from the asker ({(ReadsRoads ? "passed over" : "only counted")}),{Untrodden} on squares nobody has ever stood in or beside, {BotProwl.Baulked} prowls that stopped getting nearer ({BotProwl.RoadKept} beats kept going along a road that was not closing as the crow flies), {BotProwl.Redarted} of them sent on to ground they could walk to on their own side and {BotProwl.Unredarted} given up for finding none ({BotBarrier.Stops} of their stopping places remembered and {BotBarrier.Behind} grounds passed over for lying beyond them), {BotProwl.Turned} refused a road on the way and sent on to ground on their own side and {BotProwl.Unturned} given up for finding none,{BotProwl.Raised} companies raised for ground one bot could not take, {BotProwl.Regathered} that came up short and went to ground they could take instead, {BotProwl.Unraised} given up for raising none they could use anywhere ({BotProwl.ShortRested} of their grounds rested {BotProwl.ShortRestMs / 60000} minutes for it); {BotSlay.Undaring} of {BotSlay.Begun} hunts begun at a quarry on ground asking more strength than the hunter brought (counted, not refused); the zones: {ZoneOffered} zones named as ground the hunter could take ({ZoneLive} at a living creature's place, {ZoneUnpeopled} of the best {ZoneLooks} passed over for holding none the hunter could take within their reach) and {ZoneChosen} of them chosen, {ZoneDeadly} grounds made lethal for lying in a deadly zone and {ZoneTooStrong} too strong for a zone's fight outweighing the hunter ({ZoneForCompany} hostile zones kept as ground for a company, too strong for the hunter alone{(ZoneCompanies ? "" : ", off")}); {RoadTooStrong} grounds kept for a company because the road there asked more than the hunter alone brought; {BotProwl.ZoneFollowed} zone prowls walked on after quarry in the zone's patrol and {BotProwl.ZoneEmpty} found it empty after all; {ZoneSpared} times a zone's living creature was let past a dart's filter ({(ZoneFirst ? "and ranked next after the board's" : "ranked as any dart")}); {GrazerPassed} quarry of the grazing kind at hand passed over for a zone's fighter within the walk and {GrazerCompanied} for a company raised against one whose ground asked it ({(GrazersLast ? "grazers last" : "off")});{BotProwl.Widened} prowls walked on after quarry seen within {BotProwl.LookWider} tiles of their ground on arrival; {BotProwl.CameApart} prowls for company ground given up when the company came apart on the way; {BotProwl.ClosedOn} prowls given up when their ground was closed while they walked there; {BotProwl.CompanyTookOn} fights a prowl's raised company took on for what its leader found (up to {BotProwl.CompanyFights} a prowl); the hunt itself: {BotSlay.Slipped} quarry let go for running {BotSlay.Leash} tiles from where it was found, {BotSlay.Dragged} for drawing the hunter {BotSlay.Leash * BotSlay.FarLeash} past its walk out, {BotSlay.Unleashed} hunts kept past the old leash for fighting the bot or having been fought, {BotSlay.Rebased} leashes measured again after a jump of more than {BotSlay.JumpTiles} tiles, {BotSlay.StaleResumes} hunts not taken up again for a quarry far away by then; {BotQuarry.InTown} quarry passed over for standing in a town, {BotQuarry.OnGuarded} hostile quarry for standing on guarded ground (and {BotHaven.QuarryTaken} taken by the guards all the same); {BotSlay.PoorBegun} of {BotSlay.Begun} hunts begun and {BotProwl.PoorTaken} prowls taken by bots holding under {PoorBelow}gp free of what their wants on the board hold; {BotSlay.Thrifted} hunts left when the casts had spent what the quarry pays (a caster casts at least {BotSlay.CastFloor} times{(BotSlay.CastsByGold ? "" : ", off")}); {BotProwl.DescribeSweeps()}";

    public static void Forget()
    {
        _saidNoQuarry = false;
        BotBarrier.Forget();
        Sworn = 0;
        Rested = 0;
        Darted = 0;
        Distant = 0;
        Untrodden = 0;
        Quiet = 0;
        Stranded = 0;
        Overmatched = 0;
        Deadly = 0;
        Green = 0;
        Claimed = 0;
        Overrun = 0;
        Sought = 0;
        Boarded = 0;
        Revisits = 0;
        GrazerPassed = 0;
        GrazerCompanied = 0;
        ZoneForCompany = 0;
        RoadTooStrong = 0;
        _empty.Clear();
        BotSweepRecord.Forget();
    }
}
