using System;
using System.Collections.Generic;
using System.Diagnostics;
using Server.Engines.Pathing.Tiered;
using Server.Regions;

namespace Server.BotAI.V2;

/// <summary>
/// <b>The road the planner would draw, judged where the straight line refuses (06.10.2026, road15; Patrick's targets of that
/// night: paths 99 %, work 95 %, Needs 95 %, "money gathers very slowly", and his rule of 03.10: "zones are for understanding
/// danger and who can be met, but the actual presence nearby must not be excluded").</b>
///
/// <para>
/// <b>Measured on build 400 (04.10, 11.3 h, 210 bots).</b> <see cref="Road"/> was asked 64.0 million times and refused 56.6
/// million (88 %): 5.0 million refusals an hour, the first veto printed for weave 579 times an hour, prowl 351, envoy 322,
/// deliver 219, errand 201. The square that refused asked a median 5.2 times what the work brought (9000 against 1511), and
/// 91 % of the printed refusals by the record were for a square nobody had died on in the three hours before: the record of a
/// square is one number moved a quarter by every death, whoever died and whatever killed, kept across restarts and bought
/// back only by crossings the veto itself prevents. And the line is not the road: the planner already charges such a square
/// <see cref="BloodBeyond"/> tiles a node and a deadly patrol <see cref="DeadlyCost"/>, and goes round both.
/// </para>
///
/// <para>
/// <b>The rule (second form, 06.10.2026, road16).</b> The line is asked first, as before, and a road it passes is offered
/// as before. A road it refuses is judged: a search over the navigation graph for a road <em>clear</em> of ground beyond
/// the walker — every node further than <see cref="RoadStep"/> tiles from both ends and outside guarded ground is read as
/// the line's samples were (a patrol crossed at <see cref="ZoneRoadShare"/> of its fight; the record of a square somebody
/// died on within <see cref="RoadFreshMs"/>, or where creatures were counted now, or with dead of an age unknown since the
/// boot, against ×<see cref="RoadDare"/>; an older record past ×<see cref="RoadStaleDare"/>), and a node that fails is not
/// walked through — priced for the rest by the planners' own hook (<see cref="Cost"/>) for a walker of that strength, no
/// further than <see cref="RouteDetour"/> tiles outside the line's own box and <see cref="RouteMost"/> nodes. A clear road
/// found is offered only when the planner would draw it: when its price is within <see cref="RouteSlack"/> tiles of the
/// straight distance, since any road through one node beyond the walker costs the planner at least the straight distance
/// and <see cref="BloodBeyond"/> more; a clear road dearer than that is a detour the walk would not take, and the line's
/// refusal stands. Where no clear road is found inside the box, or the question was not asked (more than
/// <see cref="RoutePerSecond"/> searches or <see cref="RouteMsPerSecond"/> ms this second, ends the graph cannot place),
/// the line's answer stands. A verdict is kept <see cref="RouteKeepMs"/> for its origin square of <see cref="Cell"/> tiles,
/// goal square and band of strength, and a kept verdict answers any walker.
/// </para>
///
/// <para>
/// <b>Why the second form (build 405, 3.5 h).</b> The first form priced the whole road and read it after: with a record past
/// the walker charged 300 tiles a node and 1341 squares of the island so priced, the search's estimate was nothing like the
/// cost, it expanded the whole box before the goal and hit its 3000 nodes: 567 080 of 604 629 judgements were "no road
/// within 240 tiles" (2820 nodes a search), while offline 47–67 % of a sample of 120 had a clear road inside the box. And
/// 16.4 M of 21.6 M line refusals were never judged for a walker that had not stood two seconds: 74 % of the road vetoes
/// printed at the moment a bot took work were those, and the bot had come from elsewhere in the minute before — a bot
/// chooses its next work the moment it stops. The standing rule is gone; a walker holding work (<c>busy</c>) searches only
/// while the second's budget is under <see cref="RouteBusyShare"/>, and reads kept verdicts freely.
/// </para>
///
/// <para>
/// <b>What it costs, read directly.</b> A road offered here that the line refused is remembered for its walker; when the
/// walk draws a leg to it the road is counted walked (<see cref="RouteWalked"/>), and a death within
/// <see cref="RouteDeathMs"/> of such a leg is counted (<see cref="RouteDied"/>): deaths on roads the old rule would have
/// refused. <see cref="RouteJudged"/> off is the old rule.
/// </para>
/// </summary>
public static partial class BotDanger
{
    public static bool RouteJudged { get; set; } = true;

    public static bool RouteAlways { get; set; }

    public static int RouteDetour { get; set; } = 240;

    public static int RouteMost { get; set; } = 4000;

    public static int RouteSlack { get; set; } = 300;

    public static double RouteBusyShare { get; set; } = 0.34;

    public static int RoutePerSecond { get; set; } = 12;

    public static double RouteMsPerSecond { get; set; } = 15.0;

    public static int RouteKeepMs { get; set; } = 120000;

    public static int RoadFreshMs { get; set; } = 10800000;

    private static long _bootTick;

    private static bool _booted;

    private static void Booted()
    {
        _bootTick = Core.TickCount;
        _booted = true;
    }

    public static double RoadStaleDare { get; set; } = 4.5;

    public static int RouteDeathMs { get; set; } = 120000;

    public static int RouteWatchMs { get; set; } = 1800000;

    public const int Cell = 32;

    public static string RoadHow { get; private set; }

    public static long LineRefused { get; private set; }

    public static long RouteBudget { get; private set; }

    public static long RouteBusy { get; private set; }

    public static long RouteUnplaced { get; private set; }

    public static long RouteAsked { get; private set; }

    public static long RouteKept { get; private set; }

    public static long RouteSearches { get; private set; }

    public static long RouteExpanded { get; private set; }

    public static long RouteOffered { get; private set; }

    public static long RouteDetours { get; private set; }

    public static long RouteNone { get; private set; }

    public static long RouteExhausted { get; private set; }

    public static long RouteBlocked { get; private set; }

    public static long RouteStricter { get; private set; }

    public static long RouteWalked { get; private set; }

    public static long RouteDied { get; private set; }

    private static long _routeTicks;

    public static double RouteMs => _routeTicks * 1000.0 / Stopwatch.Frequency;

    private sealed class RoadTally
    {
        public long Asked;
        public long Line;
        public long Offered;
        public long Refused;
        public long Unjudged;
        public long Died;
    }

    private static readonly Dictionary<string, RoadTally> _trades = new(StringComparer.OrdinalIgnoreCase);

    private static RoadTally TallyOf(string trade)
    {
        if (!_trades.TryGetValue(trade, out var tally))
        {
            _trades[trade] = tally = new RoadTally();
        }

        return tally;
    }

    private enum RouteVerdict : byte
    {
        None,
        Clear,

        Detour,

        Record,
        Old,
        Zone,

        Unplaced
    }

    private struct Kept
    {
        public long Tick;
        public RouteVerdict Verdict;
        public float Asked;
    }

    private static readonly Dictionary<long, Kept> _kept = [];

    private sealed class Offer
    {
        public Point3D To;
        public long Tick;
        public string Trade;
        public bool Walked;
    }

    private static readonly Dictionary<uint, List<Offer>> _offered = [];

    public const int OfferNear = 48;

    private const int MostOffers = 12;

    private static Offer OfferTo(List<Offer> offers, Point3D to, long now)
    {
        Offer best = null;
        var nearest = OfferNear + 1;

        for (var i = 0; i < offers.Count; i++)
        {
            var offer = offers[i];
            var off = Math.Max(Math.Abs(offer.To.X - to.X), Math.Abs(offer.To.Y - to.Y));

            if (off < nearest && now - offer.Tick < RouteWatchMs)
            {
                nearest = off;
                best = offer;
            }
        }

        return best;
    }

    private static readonly Dictionary<uint, (long Tick, string Trade)> _walking = [];

    private static long _second;

    private static int _searched;

    private static long _spent;

    private static Mobile _judge;

    private static int[] _g = [];

    private static int[] _parent = [];

    private static int[] _seen = [];

    private static int[] _closed = [];

    private static int _search;

    private static readonly PriorityQueue<int, long> _open = new();

    private static bool Judge(
        Mobile body, Map map, Point3D from, Point3D to, double brought, int step, double dare, bool lineRefused, string trade, bool busy, RoadTally tally,
        ref double asked, ref Point3D at, ref string what, ref bool byZone
    )
    {
        if (lineRefused)
        {
            LineRefused++;

            if (tally != null)
            {
                tally.Line++;
            }
        }

        var now = Core.TickCount;
        var serial = body.Serial.Value;

        var band = Math.Clamp((int)Math.Floor(Math.Log(Math.Max(1.0, brought)) / Math.Log(1.2)), 0, 255);
        var strength = Math.Pow(1.2, band);
        var key = ((long)map.MapID << 56) | ((long)band << 48) | ((long)(from.Y / Cell) << 36) | ((long)(from.X / Cell) << 24)
                  | ((long)(to.Y / Cell) << 12) | (uint)(to.X / Cell);

        if (!_kept.TryGetValue(key, out var kept) || now - kept.Tick >= RouteKeepMs)
        {
            var second = now / 1000;

            if (second != _second)
            {
                _second = second;
                _searched = 0;
                _spent = 0;
            }

            if (_searched >= RoutePerSecond || _spent * 1000.0 / Stopwatch.Frequency >= RouteMsPerSecond)
            {
                return Unjudged(lineRefused, tally, "on the straight line, the second's searches of the planner's road being spent", 1);
            }

            if (busy && (_searched >= RoutePerSecond * RouteBusyShare || _spent * 1000.0 / Stopwatch.Frequency >= RouteMsPerSecond * RouteBusyShare))
            {
                return Unjudged(lineRefused, tally, "on the straight line, the second's searches being kept for walkers choosing", 0);
            }

            _searched++;
            _judge = body;

            var began = Stopwatch.GetTimestamp();

            if (Search(map, from, to, strength, step, dare, out kept))
            {
                RouteSearches++;
            }
            else
            {
                kept.Verdict = RouteVerdict.Unplaced;
            }

            var took = Stopwatch.GetTimestamp() - began;

            _judge = null;
            _routeTicks += took;
            _spent += took;
            kept.Tick = now;

            if (_kept.Count > 16384)
            {
                _kept.Clear();
            }

            _kept[key] = kept;
        }
        else if (kept.Verdict != RouteVerdict.Unplaced)
        {
            RouteKept++;
        }

        if (kept.Verdict == RouteVerdict.Unplaced)
        {
            return Unjudged(lineRefused, tally, "on the straight line, the navigation graph not placing an end of the road", 2);
        }

        RouteAsked++;

        switch (kept.Verdict)
        {
            case RouteVerdict.Clear:
                {
                    if (lineRefused)
                    {
                        RouteOffered++;

                        if (tally != null)
                        {
                            tally.Offered++;
                        }

                        Offered(serial, to, trade, now);
                    }

                    asked = 0.0;
                    at = Point3D.Zero;
                    what = null;
                    byZone = false;
                    RoadHow = null;

                    return false;
                }
            case RouteVerdict.Detour:
                {
                    if (lineRefused)
                    {
                        RouteDetours++;

                        if (tally != null)
                        {
                            tally.Refused++;
                        }

                        RoadHow = "on the straight line, the clear road near it being a detour the planner would not draw";
                    }

                    return lineRefused;
                }
            default:
                {
                    if (lineRefused)
                    {
                        RouteNone++;

                        if (tally != null)
                        {
                            tally.Refused++;
                        }

                        RoadHow = "on the straight line, no clear road being found near it";
                    }

                    return lineRefused;
                }
        }
    }

    private static void Offered(uint serial, Point3D to, string trade, long now)
    {
        if (!_offered.TryGetValue(serial, out var offers))
        {
            if (_offered.Count > 4096)
            {
                _offered.Clear();
            }

            _offered[serial] = offers = [];
        }

        var offer = OfferTo(offers, to, now);

        if (offer != null && Math.Max(Math.Abs(offer.To.X - to.X), Math.Abs(offer.To.Y - to.Y)) <= Cell / 2)
        {
            offer.Tick = now;
            offer.Trade = trade ?? offer.Trade;

            return;
        }

        if (offers.Count >= MostOffers)
        {
            var oldest = 0;

            for (var i = 1; i < offers.Count; i++)
            {
                if (offers[i].Tick - offers[oldest].Tick < 0)
                {
                    oldest = i;
                }
            }

            offer = offers[oldest];
            offer.Walked = false;
        }
        else
        {
            offers.Add(offer = new Offer());
        }

        offer.To = to;
        offer.Tick = now;
        offer.Trade = trade;
    }

    private static bool Unjudged(bool lineRefused, RoadTally tally, string how, int why)
    {
        if (!lineRefused)
        {
            return false;
        }

        switch (why)
        {
            case 0:
                RouteBudget++;
                RouteBusy++;
                break;
            case 1:
                RouteBudget++;
                break;
            default:
                RouteUnplaced++;
                break;
        }

        if (tally != null)
        {
            tally.Unjudged++;
        }

        RoadHow = how;

        return true;
    }

    private static bool Search(Map map, Point3D from, Point3D to, double strength, int step, double dare, out Kept kept)
    {
        kept = default;

        var graph = NavigationService.Graph(map);

        if (graph == null || !graph.Contains(from.X, from.Y) || !graph.Contains(to.X, to.Y))
        {
            return false;
        }

        var sc = graph.ClusterOf(from.X, from.Y);
        var gc = graph.ClusterOf(to.X, to.Y);
        var starts = graph.NodesOf(sc);
        var goals = graph.NodesOf(gc);

        if (starts == null || starts.Count == 0 || goals == null || goals.Count == 0 || sc == gc)
        {
            return false;
        }

        var piece = NavigationService.ComponentOf(map, from);
        var pieceTo = NavigationService.ComponentOf(map, to);

        if (piece >= 0 && pieceTo >= 0 && piece != pieceTo)
        {
            return false;
        }

        if (piece < 0)
        {
            piece = pieceTo;
        }

        var size = graph.Capacity + 1;

        if (_g.Length < size)
        {
            var n = Math.Max(size, _g.Length * 2);

            _g = new int[n];
            _parent = new int[n];
            _seen = new int[n];
            _closed = new int[n];
            _search = 0;
        }

        if (++_search == int.MaxValue)
        {
            Array.Clear(_seen);
            Array.Clear(_closed);
            _search = 1;
        }

        var walker = _walker;
        var walkerMap = _walkerMap;
        var walkerStrength = _strength;
        var standsOpen = _standsOpen;
        var afloat = _afloat;
        var sighted = _sighted;
        var nodes = Asked;
        var bloodied = Bloodied;
        var barred = Barred;
        var fringe = InFringe;
        var hostile = InHostile;
        var deadly = InDeadly;
        var cut = CutThrough;
        var beyond = BeyondWalker;
        var refusedNodes = RefusedNodes;
        var inSight = InSight;

        _walker = ByWalker ? _judge : null;
        _walkerMap = map;
        _strength = Math.Max(1.0, strength);
        _standsOpen = BotReachMap.Standing(map, from, out var want) && want == BotReachClass.Open;
        _afloat = want == BotReachClass.Water;
        _sighted = null;

        var found = -1;
        var bar = Math.Max(1.0, strength) * dare;
        var stale = Math.Max(bar, Math.Max(1.0, strength) * RoadStaleDare);
        var standing = BotZones.ZoneAt(map, from.X, from.Y)?.Id ?? 0;
        var ending = BotZones.ZoneAt(map, to.X, to.Y)?.Id ?? 0;
        var slack = RouteSlack;
        var expanded = 0;

        try
        {
            var x1 = Math.Min(from.X, to.X) - RouteDetour;
            var y1 = Math.Min(from.Y, to.Y) - RouteDetour;
            var x2 = Math.Max(from.X, to.X) + RouteDetour;
            var y2 = Math.Max(from.Y, to.Y) + RouteDetour;

            _open.Clear();

            for (var i = 0; i < starts.Count; i++)
            {
                var a = starts[i];

                if (piece >= 0 && graph.Component(a) >= 0 && graph.Component(a) != piece)
                {
                    continue;
                }

                Relax(a, NavCost.Estimate(from.X, from.Y, graph.X(a), graph.Y(a)), -1, NavCost.Estimate(graph.X(a), graph.Y(a), to.X, to.Y));
            }

            while (_open.TryDequeue(out var node, out _))
            {
                if (_closed[node] == _search)
                {
                    continue;
                }

                _closed[node] = _search;

                if (graph.ClusterOfNode(node) == gc)
                {
                    found = node;

                    break;
                }

                if (++expanded > RouteMost)
                {
                    break;
                }

                if (!graph.EdgesOf(node, out var next, out var weights, out var first, out var until))
                {
                    continue;
                }

                var cost = _g[node];

                for (var e = first; e < until; e++)
                {
                    var b = next[e];

                    if (_closed[b] == _search)
                    {
                        continue;
                    }

                    var x = graph.X(b);
                    var y = graph.Y(b);

                    if (x < x1 || x > x2 || y < y1 || y > y2)
                    {
                        continue;
                    }

                    if (Math.Max(Math.Abs(x - from.X), Math.Abs(y - from.Y)) > step && Math.Max(Math.Abs(x - to.X), Math.Abs(y - to.Y)) > step)
                    {
                        var reads = Reads(map, x, y, bar, stale, standing, ending, out _, out var zone);

                        if (reads != RouteVerdict.Clear)
                        {
                            _closed[b] = _search;
                            RouteBlocked++;

                            if (zone != 0)
                            {
                                slack = Math.Min(slack, Math.Clamp((int)(HostilePerBot * (bar / Math.Max(1.0, strength)) / Math.Max(0.01, ZoneRoadShare)), HostileLeast, HostileMost));
                            }

                            continue;
                        }
                    }

                    Relax(b, cost + weights[e] + Cost(x, y), node, NavCost.Estimate(x, y, to.X, to.Y));
                }
            }

            RouteExpanded += expanded;

            if (found < 0)
            {
                RouteExhausted++;
            }
        }
        finally
        {
            _walker = walker;
            _walkerMap = walkerMap;
            _strength = walkerStrength;
            _standsOpen = standsOpen;
            _afloat = afloat;
            _sighted = sighted;
            Asked = nodes;
            Bloodied = bloodied;
            Barred = barred;
            InFringe = fringe;
            InHostile = hostile;
            InDeadly = deadly;
            CutThrough = cut;
            BeyondWalker = beyond;
            RefusedNodes = refusedNodes;
            InSight = inSight;
        }

        if (found < 0)
        {
            kept.Verdict = RouteVerdict.None;

            return true;
        }

        var straight = NavCost.Estimate(from.X, from.Y, to.X, to.Y);

        kept.Verdict = _g[found] <= straight + slack * NavCost.Step ? RouteVerdict.Clear : RouteVerdict.Detour;
        kept.Asked = (float)(_g[found] / (double)NavCost.Step);

        return true;
    }

    private static void Relax(int node, int cost, int parent, int estimate)
    {
        if (cost < 0 || cost > NavCost.Unaffordable)
        {
            return;
        }

        if (_seen[node] == _search && _g[node] <= cost)
        {
            return;
        }

        _seen[node] = _search;
        _g[node] = cost;
        _parent[node] = parent;

        _open.Enqueue(node, ((long)(cost + estimate + estimate / 1000) << 32) | (uint)(int.MaxValue - cost));
    }

    private static RouteVerdict Reads(Map map, int x, int y, double bar, double stale, int standing, int ending, out double asks, out int zone)
    {
        asks = 0.0;
        zone = 0;

        var here = new Point3D(x, y, 0);
        var record = BotQuad.Muscle(map, here);

        if (record > bar && Region.Find(here, map)?.IsPartOf<GuardedRegion>() == true)
        {
            return RouteVerdict.Clear;
        }

        if (record > bar)
        {
            var quad = BotQuad.Known(map, here);
            var now = Core.TickCount;
            var fresh = quad != null
                        && (quad.DiedTick != 0 && now - quad.DiedTick < RoadFreshMs
                            || quad.Mobs > 0 && now - quad.Sighted < BotQuad.SightMs
                            || quad.DiedTick == 0 && quad.Deaths > 0 && (!_booted || now - _bootTick < RoadFreshMs));

            if (fresh || record > stale)
            {
                asks = record;

                return fresh ? RouteVerdict.Record : RouteVerdict.Old;
            }
        }

        if (ZoneRoadShare <= 0.0)
        {
            return RouteVerdict.Clear;
        }

        var level = BotZones.LevelAt(map, x, y, out _, out var threat);

        if (level is not (BotZoneLevel.Deadly or BotZoneLevel.Hostile))
        {
            return RouteVerdict.Clear;
        }

        var id = BotZones.ZoneAt(map, x, y)?.Id ?? 0;

        if (id == 0 || id == standing || id == ending)
        {
            return RouteVerdict.Clear;
        }

        var fight = threat * ZoneRoadShare;

        if (fight > bar && Region.Find(here, map)?.IsPartOf<GuardedRegion>() != true)
        {
            asks = fight;
            zone = id;

            return RouteVerdict.Zone;
        }

        return RouteVerdict.Clear;
    }

    public static void Walks(Mobile walker, Point3D target)
    {
        if (_offered.Count == 0 || walker == null || target == Point3D.Zero || !_offered.TryGetValue(walker.Serial.Value, out var offers))
        {
            return;
        }

        var now = Core.TickCount;
        var offer = OfferTo(offers, target, now);

        if (offer == null)
        {
            return;
        }

        if (!offer.Walked)
        {
            offer.Walked = true;
            RouteWalked++;
        }

        _walking[walker.Serial.Value] = (now, offer.Trade);
    }

    public static void Fell(Mobile bot)
    {
        if (_walking.Count == 0 || bot == null || !_walking.Remove(bot.Serial.Value, out var walking))
        {
            return;
        }

        if (Core.TickCount - walking.Tick >= RouteDeathMs)
        {
            return;
        }

        RouteDied++;

        if (walking.Trade != null)
        {
            TallyOf(walking.Trade).Died++;
        }
    }

    private static string DescribeRoutes()
    {
        if (!RouteJudged)
        {
            return $"the planner's road is not judged where the line refuses (off): {LineRefused} refused by the line";
        }

        var trades = "";

        if (_trades.Count > 0)
        {
            var rows = new List<(string Trade, RoadTally Tally)>(_trades.Count);

            foreach (var (trade, tally) in _trades)
            {
                if (tally.Line > 0)
                {
                    rows.Add((trade, tally));
                }
            }

            rows.Sort((a, b) => b.Tally.Line.CompareTo(a.Tally.Line));

            for (var i = 0; i < rows.Count && i < 14; i++)
            {
                var t = rows[i].Tally;

                trades += $"{(i == 0 ? "" : ", ")}{rows[i].Trade} {t.Line} → {t.Offered} offered, {t.Refused} still refused, {t.Unjudged} not judged, {t.Died} died";
            }
        }

        return $"a clear road judged where the line refused: {LineRefused} refused by the line, {RouteBudget} of them left so past {RoutePerSecond} searches or {RouteMsPerSecond:F0}ms a second "
               + $"({RouteBusy} by a walker holding work past its {RouteBusyShare:P0}) and {RouteUnplaced} for an end the graph could not place; {RouteAsked} judged "
               + $"({RouteKept} from a verdict kept {RouteKeepMs / 1000}s, {RouteSearches} searched in {RouteMs:F0}ms over {RouteExpanded} nodes, {RouteBlocked} not walked through, {RouteExhausted} searches finding no clear road): "
               + $"{RouteOffered} offered on a clear road the planner would draw, {RouteDetours} with a clear road dearer than the straight distance by {RouteSlack} tiles, "
               + $"{RouteNone} with no clear road within {RouteDetour} tiles of the line and {RouteMost} nodes"
               + (RouteAlways ? $"; {RouteStricter} roads the line passed refused on the planner's road" : "")
               + $"; {RouteWalked} such roads walked and {RouteDied} deaths within {RouteDeathMs / 1000}s of a leg on one"
               + (trades.Length > 0 ? $"; by trade, of the appraisal's asks (refused by the line → …): {trades}" : "");
    }

    private static void ForgetRoutes()
    {
        LineRefused = 0;
        RouteBudget = 0;
        RouteUnplaced = 0;
        RouteAsked = 0;
        RouteKept = 0;
        RouteSearches = 0;
        RouteExpanded = 0;
        RouteOffered = 0;
        RouteDetours = 0;
        RouteExhausted = 0;
        RouteBlocked = 0;
        RouteBusy = 0;
        RouteNone = 0;
        RouteStricter = 0;
        RouteWalked = 0;
        RouteDied = 0;
        _routeTicks = 0;
        _trades.Clear();
        _kept.Clear();
        _offered.Clear();
        _walking.Clear();
    }
}
