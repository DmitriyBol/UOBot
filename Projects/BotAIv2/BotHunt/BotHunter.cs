using System;
using System.Collections.Generic;
using Server.Logging;
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
            var elsewhere = Hunting(bot, body, map, out var outmatched);

            return elsewhere == Point3D.Zero ? null : new BotProwl(map, elsewhere, outmatched);
        }

        var quarry = BotQuarry.Best(body, BotQuarry.Reach);

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

            var ground = Hunting(bot, body, map, out var company);

            return ground == Point3D.Zero ? null : new BotProwl(map, ground, company);
        }

        BotQuarry.Claim(body, quarry);

        var trains = bot.Bond?.Weapon?.Skill ?? SkillName.Wrestling;

        return new BotSlay(quarry, trains);
    }

    private static Point3D Hunting(IBotWilful bot, Mobile body, Map map, out bool company)
    {
        company = false;

        var ledger = bot?.Resolve?.Ledger;
        var home = BotPopulation.HomeOf(body);
        var roam = Math.Clamp(BotPopulation.Leash(body) / 2, BotQuarry.Reach, Walkable);

        var best = Point3D.Zero;
        var bestPaid = -1.0;
        var bestKnown = -1.0;
        var bestWanted = false;

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

        var zoned = Zoned(body, map, home, roam);

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

            if (Utility.InRange(body.Location, where, BotQuarry.Reach))
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
                Untrodden++;

                continue;
            }

            if (Region.Find(where, map)?.IsPartOf<TownRegion>() == true)
            {
                continue;
            }

            if (BotRefused.Refusing(map, where) || BotQuad.Baulking(map, where))
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

            if (LatelyEmpty(body, where))
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

            if (safety > BotQuad.TooQuiet)
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
                    && ((wanted && !bestWanted)
                        || (wanted == bestWanted && (paid > bestPaid || (paid >= bestPaid && known > bestKnown)))));

            if (better)
            {
                best = where;
                bestPaid = paid;
                bestKnown = known;
                bestWanted = wanted;
                bestBoarded = boarded;
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
        }

        return best;
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

        if (BotQuad.Baulking(map, where) || BotRefused.Refusing(map, where))
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

        if (BotQuad.Baulking(map, where) || BotRefused.Refusing(map, where))
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

    private const int EmptyKept = 4;

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

    private static Point3D Zoned(Mobile body, Map map, Point3D home, int roam)
    {
        if (!ReadsZones || BotZones.State is not { } state || state.Zones.Length == 0)
        {
            return Point3D.Zero;
        }

        var strength = BotQuad.Strength(body);
        var best = Point3D.Zero;
        var bestScore = 0.0;

        for (var i = 0; i < state.Zones.Length; i++)
        {
            var zone = state.Zones[i];

            if (zone.Water || zone.Live <= 0 || zone.Level == BotZoneLevel.Deadly || zone.Stand == Point3D.Zero)
            {
                continue;
            }

            if (!Utility.InRange(home, zone.Stand, roam))
            {
                continue;
            }

            double quarry;
            double worth;

            if (zone.Level == BotZoneLevel.Hostile)
            {
                if (zone.Threat > strength * ZoneDare)
                {
                    continue;
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
            var score = quarry * worth / (1.0 + away / 150.0);

            if (score > bestScore)
            {
                bestScore = score;
                best = zone.Stand;
            }
        }

        if (best != Point3D.Zero)
        {
            ZoneOffered++;
        }

        return best;
    }

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
        $"{Sworn} answers went to classes that only defend; {Quiet} hunting grounds passed over as too quiet (above {BotQuad.TooQuiet:F2}), {Sought} picked for having hurt somebody (at or below {BotQuad.Wanted:F2}), {Boarded} picked for a creature the guild's board asks for, {Stranded} hunters left with nowhere to walk at all because every ground they looked at was too quiet, {Overmatched} grounds passed over for asking more strength than whoever looked had, {Deadly} grounds where bots had lately died kept to companies, {Green} grounds that had hurt somebody passed over for a novice (main skill under {BotLadder.NoviceSkill:F0}), {Claimed} for somebody already raising a company for them, {Overrun} quarry passed over for the crowd already round it, {Rested} named grounds passed over as resting after refusing somebody and {Darted} sampled ones, {Revisits} for lying where the asker had lately found nothing, {Distant} further from the asker than {Walkable} tiles, which is as far as a road is ever searched for, {Unjoined} on ground no gate joins to the hunter, {OverWater} weighed over the water beyond a moongate for a hunter living on another land ({AbroadTooHard} of them passed over for asking more than it alone brings), {Roundabout} whose road from home runs more than {Detour} tiles past the straight line from the asker ({(ReadsRoads ? "passed over" : "only counted")}),{Untrodden} on squares nobody has ever stood in or beside, {BotProwl.Baulked} prowls that stopped getting nearer ({BotProwl.RoadKept} beats kept going along a road that was not closing as the crow flies), {BotProwl.Redarted} of them sent on to ground they could walk to on their own side and {BotProwl.Unredarted} given up for finding none ({BotBarrier.Stops} of their stopping places remembered and {BotBarrier.Behind} grounds passed over for lying beyond them), {BotProwl.Turned} refused a road on the way and sent on to ground on their own side and {BotProwl.Unturned} given up for finding none,{BotProwl.Raised} companies raised for ground one bot could not take, {BotProwl.Regathered} that came up short and went to ground they could take instead, {BotProwl.Unraised} given up for raising none they could use anywhere; {BotSlay.Undaring} of {BotSlay.Begun} hunts begun at a quarry on ground asking more strength than the hunter brought (counted, not refused); the zones: {ZoneOffered} zones named as ground the hunter could take and {ZoneChosen} of them chosen, {ZoneDeadly} grounds made lethal for lying in a deadly zone and {ZoneTooStrong} too strong for a zone's fight outweighing the hunter";

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
        _empty.Clear();
    }
}
