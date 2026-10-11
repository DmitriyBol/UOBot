using System;
using Server.Engines.Pathing.Tiered;
using Server.Regions;

namespace Server.BotAI.V2;

/// <summary>
/// <b>The road goes round the danger (29.09.2026, Patrick's second answer of the morning).</b> "Guilds have the right to
/// stay in other towns; if the road is dangerous, through the bog for instance, they can go by the north where the road
/// is not so dangerous." The tiered planner routed by distance alone, so The Needle's bots walked from Cove to Britain
/// through the plague bog every time, and the delve's march went past the orc fort. This is the hook the planners ask
/// at every node (<c>NavigationService.Danger</c>): a node on a quadrant the map reads as bloodied (<c>BotQuad.Muscle</c>)
/// costs <see cref="BloodCost"/> tiles more, one inside barred or quarantined ground <see cref="BarredCost"/> more, so a
/// detour up to that many tiles is taken before the blood is crossed. The knowledge is the same the samplers and the
/// travellers already read; this is the walk reading it too.
///
/// <para>
/// <b>Priced for the walker, not for the median bot (01.10.2026, build 388; Patrick's plan v2, point 5: "bots know how to
/// go round the zones, with a margin or without, to pass without trouble").</b> On build 386, 71 % of 701 deaths fell inside
/// a deadly zone and 124 of 139 prowl deaths more than a hundred tiles from the prowl's target: the road killed, not the
/// goal. The zones' price on a node was read against the median bot (<c>BotZone.VsBot</c>), the same for a novice alone
/// and for a company of eight, and the goal's ground was the only ground any proposer weighed. Now the walk sets who is
/// walking before a route is asked (<see cref="For"/>): a zone's fight is read against that walker's own strength — its
/// company's when it is in one — so a weak bot pays the fringe and keeps the margin while a company whose strength the
/// fight does not reach <see cref="DeadlyVsWalker"/> times is charged the hostile price and may cut through; and the
/// straight road to far work is weighed before the work is offered (<see cref="Road"/>), by the record of deaths along it
/// and by the patrols it crosses. Costs stay bounded: no node costs more than <see cref="BarredCost"/> tiles, and a node
/// the reach map says no walk from home reaches is refused outright (<see cref="NavCost.Unaffordable"/>, which the planners
/// decline to relax) rather than priced — Patrick's plan v2, point 6.
/// </para>
///
/// <para>
/// <b>And where the straight line refuses, the road the planner would draw is judged instead (06.10.2026, road15).</b> The
/// line refused 88 % of all far roads on build 400 for squares nobody would walk through. See <see cref="Judge"/> in
/// <c>BotDangerRoute.cs</c>.
/// </para>
/// </summary>
public static partial class BotDanger
{
    public static bool Running { get; set; } = true;

    public static int BloodCost { get; set; } = 60;

    public static int BarredCost { get; set; } = 800;

    public static bool ZonesPriced { get; set; } = true;

    public static int EdgePerBot { get; set; } = 10;

    public static int EdgeMost { get; set; } = 40;

    public static int HostilePerBot { get; set; } = 30;

    public static int HostileLeast { get; set; } = 20;

    public static int HostileMost { get; set; } = 150;

    public static int DeadlyCost { get; set; } = 300;

    public static bool ByWalker { get; set; } = true;

    public static double DeadlyVsWalker { get; set; } = 3.0;

    public static int BloodBeyond { get; set; } = 300;

    public static bool RefusesNodes { get; set; } = true;

    private static Mobile _walker;

    private static Map _walkerMap;

    private static double _strength;

    private static bool _standsOpen;

    private static bool _afloat;

    private static BotReaches _sighted;

    public static int SightCost { get; set; } = 200;

    public static long InSight { get; private set; }

    public static long ForWalkers { get; private set; }

    public static long CutThrough { get; private set; }

    public static long BeyondWalker { get; private set; }

    public static long RefusedNodes { get; private set; }

    public static void For(Mobile walker)
    {
        _walker = null;
        _sighted = null;

        if (!ByWalker || walker?.Map == null || walker.Map == Map.Internal)
        {
            return;
        }

        _sighted = SightCost > 0 ? (walker as BotMobile)?.Journey?.Sighted : null;

        _walker = walker;
        _walkerMap = walker.Map;
        _strength = Math.Max(1.0, BotQuad.Strength(walker));
        _standsOpen = BotReachMap.Standing(walker.Map, walker.Location, out var want) && want == BotReachClass.Open;
        _afloat = want == BotReachClass.Water;
        ForWalkers++;
    }

    public static void Clear()
    {
        _walker = null;
        _sighted = null;
    }

    public static void Hook()
    {
        NavigationService.Danger = Cost;
        Booted();
    }

    public static long InFringe { get; private set; }

    public static long InHostile { get; private set; }

    public static long InDeadly { get; private set; }

    public static long Asked { get; private set; }

    public static long Bloodied { get; private set; }

    public static long Barred { get; private set; }

    public static int Cost(int x, int y)
    {
        if (!Running)
        {
            return 0;
        }

        var walker = _walker;
        var map = walker != null ? _walkerMap : BotPopulation.Home ?? Map.Felucca;

        if (map == null)
        {
            return 0;
        }

        Asked++;

        if (RefusesNodes && walker != null && (_standsOpen || _afloat))
        {
            var kind = BotReachMap.Class(map, x, y);

            if (kind != BotReachClass.Unknown && (_afloat ? kind != BotReachClass.Water : kind != BotReachClass.Open))
            {
                RefusedNodes++;

                return NavCost.Unaffordable;
            }
        }

        var at = new Point3D(x, y, 0);

        if (BotBarred.Holds(map, at))
        {
            Barred++;

            return BarredCost * NavCost.Step;
        }

        var tiles = 0;
        var record = BotQuad.Muscle(map, at);

        if (record > 0.0)
        {
            Bloodied++;
            tiles = BloodCost;

            if (walker != null && record > _strength * RoadDare)
            {
                BeyondWalker++;
                tiles = Math.Max(tiles, BloodBeyond);
            }
        }

        if (ZonesPriced)
        {
            var level = BotZones.LevelAt(map, x, y, out var vsBot, out var threat);

            var vs = walker != null ? threat / _strength : vsBot;

            var zoned = level switch
            {
                BotZoneLevel.Deadly => walker != null && vs < DeadlyVsWalker ? Hostile(vs) : DeadlyCost,
                BotZoneLevel.Hostile => Hostile(vs),
                BotZoneLevel.Edge => Math.Clamp((int)(EdgePerBot * vs), 0, EdgeMost),
                _ => 0
            };

            if (zoned > 0)
            {
                switch (level)
                {
                    case BotZoneLevel.Deadly:
                        InDeadly++;

                        if (zoned < DeadlyCost)
                        {
                            CutThrough++;
                        }

                        break;
                    case BotZoneLevel.Hostile:
                        InHostile++;
                        break;
                    default:
                        InFringe++;
                        break;
                }

                tiles = Math.Max(tiles, zoned);
            }
        }

        if (_sighted != null && walker != null && _sighted.Within(x, y, 0))
        {
            InSight++;
            tiles = Math.Max(tiles, SightCost);
        }

        tiles = BotNight.Node(tiles);

        return Math.Min(tiles, BarredCost) * NavCost.Step;
    }

    private static int Hostile(double vs) => Math.Clamp((int)(HostilePerBot * vs), HostileLeast, HostileMost);

    public static bool Roads { get; set; } = true;

    public static double RoadDare { get; set; } = 1.5;

    public static int RoadStep { get; set; } = 32;

    public static double ZoneRoadShare { get; set; } = 0.5;

    public static long RoadsAsked { get; private set; }

    public static long RoadsRefused { get; private set; }

    public static long RoadsByRecord { get; private set; }

    public static long RoadsByZone { get; private set; }

    private static long _roadTicks;

    public static long RoadSamples { get; private set; }

    public static double RoadMs => _roadTicks * 1000.0 / System.Diagnostics.Stopwatch.Frequency;

    public static bool Road(Mobile body, Map map, Point3D from, Point3D to, double brought, out double asked, out Point3D at, out string what, string trade = null, bool busy = false)
    {
        asked = 0.0;
        at = Point3D.Zero;
        what = null;
        RoadHow = null;

        var step = Math.Max(8, RoadStep);

        var dare = BotNight.Dare(RoadDare);

        if (!Running || !Roads || dare <= 0.0 || body == null || map == null || map == Map.Internal)
        {
            return false;
        }

        var dx = to.X - from.X;
        var dy = to.Y - from.Y;
        var length = Math.Max(Math.Abs(dx), Math.Abs(dy));

        if (length <= step)
        {
            return false;
        }

        RoadsAsked++;

        var tally = trade == null ? null : TallyOf(trade);

        if (tally != null)
        {
            tally.Asked++;
        }

        var began = System.Diagnostics.Stopwatch.GetTimestamp();
        var refused = Line(map, from, to, brought, step, dare, out asked, out at, out what, out var byZone);

        _roadTicks += System.Diagnostics.Stopwatch.GetTimestamp() - began;

        if (RouteJudged && (refused || RouteAlways))
        {
            refused = Judge(body, map, from, to, brought, step, dare, refused, trade, busy, tally, ref asked, ref at, ref what, ref byZone);
        }
        else if (refused)
        {
            LineRefused++;
            RoadHow = "on the straight line";

            if (tally != null)
            {
                tally.Line++;
            }
        }

        if (refused)
        {
            RoadsRefused++;

            if (byZone)
            {
                RoadsByZone++;
            }
            else
            {
                RoadsByRecord++;
            }
        }

        return refused;
    }

    private static bool Line(Map map, Point3D from, Point3D to, double brought, int step, double dare, out double asked, out Point3D at, out string what, out bool byZone)
    {
        asked = 0.0;
        at = Point3D.Zero;
        what = null;
        byZone = false;

        var dx = to.X - from.X;
        var dy = to.Y - from.Y;
        var length = Math.Max(Math.Abs(dx), Math.Abs(dy));

        var bar = Math.Max(1.0, brought) * dare;
        var standing = BotZones.ZoneAt(map, from.X, from.Y)?.Id ?? 0;
        var ending = BotZones.ZoneAt(map, to.X, to.Y)?.Id ?? 0;
        var samples = length / step;

        for (var i = 1; i < samples; i++)
        {
            var here = new Point3D(from.X + dx * i / samples, from.Y + dy * i / samples, 0);

            RoadSamples++;

            if (Region.Find(here, map)?.IsPartOf<GuardedRegion>() == true)
            {
                continue;
            }

            var record = BotQuad.Muscle(map, here);

            if (record > bar)
            {
                asked = record;
                at = here;
                what = "the record of what has killed there";

                return true;
            }

            if (ZoneRoadShare <= 0.0)
            {
                continue;
            }

            var level = BotZones.LevelAt(map, here.X, here.Y, out _, out var threat);

            if (level is not (BotZoneLevel.Deadly or BotZoneLevel.Hostile))
            {
                continue;
            }

            var zone = BotZones.ZoneAt(map, here.X, here.Y)?.Id ?? 0;

            if (zone == 0 || zone == standing || zone == ending)
            {
                continue;
            }

            var fight = threat * ZoneRoadShare;

            if (fight > bar)
            {
                asked = fight;
                at = here;
                what = $"the patrol of zone {zone}";
                byZone = true;

                return true;
            }
        }

        return false;
    }

    public static string Describe() =>
        !Running
            ? "the roads do not read the danger"
            : $"the roads read the danger: {Asked} nodes asked, {Bloodied} bloodied (+{BloodCost} tiles each, {BeyondWalker} of them +{BloodBeyond} for a record past the walker's strength ×{RoadDare:F1}) and {Barred} barred (+{BarredCost}); "
              + (ZonesPriced
                  ? $"the zones priced {InFringe} in an aggro fringe (+{EdgePerBot} a bot, at most {EdgeMost}), {InHostile} in a hostile patrol (+{HostilePerBot} a bot, {HostileLeast}–{HostileMost}) and {InDeadly} deadly (+{DeadlyCost}; {CutThrough} of them at the hostile price for a walker the fight did not reach ×{DeadlyVsWalker:F0}); "
                  : "the zones are not priced; ")
              + $"{InSight} nodes in the reach of a creature the walker had seen and could not beat (+{SightCost}); "
              + $"{ForWalkers} routes priced for the walker that asked ({(ByWalker ? "on" : "off")}), {RefusedNodes} nodes refused outright on ground the reach map says the walker cannot reach ({(RefusesNodes ? "on" : "off")}); "
              + $"roads weighed before far work was offered: {RoadsAsked} asked, {RoadsRefused} refused ({RoadsByRecord} by the record of deaths on a square, {RoadsByZone} by a patrol crossed at ×{ZoneRoadShare:F2} of its fight, against ×{RoadDare:F1} of what the work brings{(Roads ? "" : "; off")}), {RoadSamples} samples read in {RoadMs:F0}ms of the loop ({(RoadsAsked == 0 ? 0 : RoadMs * 1000.0 / RoadsAsked):F1}µs an ask); "
              + DescribeRoutes();

    public static void Forget()
    {
        Asked = 0;
        Bloodied = 0;
        Barred = 0;
        InFringe = 0;
        InHostile = 0;
        InDeadly = 0;
        ForWalkers = 0;
        CutThrough = 0;
        BeyondWalker = 0;
        RefusedNodes = 0;
        InSight = 0;
        RoadsAsked = 0;
        RoadsRefused = 0;
        RoadsByRecord = 0;
        RoadsByZone = 0;
        RoadSamples = 0;
        _roadTicks = 0;
        ForgetRoutes();
    }
}
