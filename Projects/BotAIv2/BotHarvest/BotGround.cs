using System;
using System.Collections.Generic;
using System.Diagnostics;
using Server.Engines.Craft;
using Server.Engines.Harvest;
using Server.Logging;
using Server.Mobiles;
using Server.Regions;
using Server.Text;

namespace Server.BotAI.V2;

/// <summary>One remembered patch of workable rock, and what is in it.</summary>
public readonly struct BotSeam
{
    public BotSeam(Map map, Point3D where, string ore, double required)
    {
        Map = map;
        Where = where;
        Ore = ore;
        Required = required;
    }

    public Map Map { get; }

    public Point3D Where { get; }

    public string Ore { get; }

    public double Required { get; }

    public bool Exists => Map != null;

    public override string ToString() => $"{Ore} at {Where}";
}

/// <summary>
/// What the population knows about the ground: where the rock is, where metal can be melted, and where
/// money and goods can be put away.
///
/// <para>
/// <b>Found by one bounded sweep, kept for everybody.</b> A bot standing in a town cannot see a mine and a
/// bot standing in a mine cannot see a forge, so knowledge of places cannot come from looking around: it
/// has to be swept for once and remembered. The first version reached the same conclusion the expensive
/// way — every workshop on this shard is part of the map rather than an object in it, so no spatial query
/// finds one, and a smith could only discover a forge by standing within two tiles of a forge it had no
/// reason to walk to. It never happened, all night.
/// </para>
///
/// <para>
/// <b>Swept where bots actually are, rather than around a list of towns.</b> The first version swept
/// vendor clusters and missed the only town that mattered: Britain's cluster centre is the average of its
/// shopkeepers' spawn points, which lands inside a wall 246 tiles from the smithy, so it recorded eight
/// forges on four facets and <em>none</em> on Felucca, where the whole population lived. A bot asking
/// where it may work triggers the sweep around itself, which cannot miss the place bots are.
/// </para>
///
/// <para>
/// This is not a persistent map and is not written to disk. It is rebuilt when the world is, because
/// everything in it is a fact about a world that has just been replaced.
/// </para>
/// </summary>
public static class BotGround
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotGround));

    public static int Reach { get; set; } = 160;

    public static int Stride { get; set; } = 4;

    public static int SeamSpacing { get; set; } = 16;

    public static int Patience { get; set; } = 80;

    public static Point3D Lode { get; set; } = new(1446, 1227, 0);

    public static int Prospect(Map map)
    {
        if (map == null || map == Map.Internal || Lode == Point3D.Zero)
        {
            logger.Error(
                "The lode at ({X}, {Y}) could not be prospected: there is no facet to sweep it on, so no ore outside the walls has been recorded",
                Lode.X,
                Lode.Y
            );

            return 0;
        }

        var found = Survey(map, Lode);

        logger.Information(
            "Prospected the lode at ({X}, {Y}): {Found} seams, and now {Total} on the board",
            Lode.X,
            Lode.Y,
            found,
            _seams.Count
        );

        return found;
    }

    public static int PlaceSpacing { get; set; } = 12;

    public static int AnvilReach { get; set; } = 3;

    public static int Storey { get; set; } = BotStep.StandingReach;

    public static long Upstairs { get; private set; }

    public static long Refused { get; private set; }

    public static int MaxSeams { get; set; } = 2048;

    public static int MaxPlaces { get; set; } = 48;

    public static int MaxSurveys { get; set; } = 64;

    private static readonly List<BotSeam> _seams = [];

    private static readonly List<(Map Map, Point3D Where)> _fires = [];

    private static readonly List<(Map Map, Point3D Where)> _hearths = [];

    private static readonly List<(Map Map, Point3D Where)> _counters = [];

    private static readonly List<(Map Map, Point3D Where)> _surveyed = [];

    private static bool _saidCapped;

    private static bool _saidFullOfSeams;

    public static IReadOnlyList<BotSeam> Seams => _seams;

    public static IReadOnlyList<(Map Map, Point3D Where)> Fires => _fires;

    public static IReadOnlyList<(Map Map, Point3D Where)> Hearths => _hearths;

    public static IReadOnlyList<(Map Map, Point3D Where)> Counters => _counters;

    public static int Surveys => _surveyed.Count;

    public static long Walled { get; private set; }

    public static bool IsForgeId(int id) => id is 4017 or (>= 6522 and <= 6569) or 11736;

    public static bool IsHearthId(int id) => CraftItem.IsHeatSource(id);

    public static bool IsAnvilId(int id) => id is 4015 or 4016 or 11733 or 11734;

    public static Point3D Frontier(Map map, Point3D from)
    {
        if (map == null || map == Map.Internal || Lode == Point3D.Zero)
        {
            return Point3D.Zero;
        }

        var dx = Lode.X - from.X;
        var dy = Lode.Y - from.Y;
        var span = Math.Max(1, (int)Math.Sqrt(dx * dx + dy * dy));

        for (var step = 1; step <= Rings; step++)
        {
            var out0 = span + step * Reach;
            var x = from.X + dx * out0 / span;
            var y = from.Y + dy * out0 / span;

            if (x < 8 || y < 8 || x >= map.Width - 8 || y >= map.Height - 8)
            {
                break;
            }

            if (!BotStep.Settle(map, x, y, out var z))
            {
                continue;
            }

            var where = new Point3D(x, y, z);

            if (BotRefused.Refusing(map, where))
            {
                continue;
            }

            if (!Surveyed(map, where))
            {
                return where;
            }
        }

        return Point3D.Zero;
    }

    public static int Rings { get; set; } = 12;

    public static bool Surveyed(Map map, Point3D around)
    {
        for (var i = 0; i < _surveyed.Count; i++)
        {
            if (_surveyed[i].Map == map && Utility.InRange(_surveyed[i].Where, around, Reach / 2))
            {
                return true;
            }
        }

        return false;
    }

    public static int Survey(Map map, Point3D around)
    {
        if (map == null || map == Map.Internal || Surveyed(map, around))
        {
            return 0;
        }

        if (_surveyed.Count >= MaxSurveys)
        {
            if (!_saidCapped)
            {
                _saidCapped = true;

                logger.Error(
                    "The ground has been swept {Count} times, which is the limit; bots further out will find no seams, fires or counters",
                    _surveyed.Count
                );
            }

            return 0;
        }

        _surveyed.Add((map, around));

        var clock = Stopwatch.StartNew();
        var system = Mining.System;

        var seams = 0;
        var fires = 0;
        var hearths = 0;

        for (var x = around.X - Reach; x <= around.X + Reach; x++)
        {
            if (x < 0 || x >= map.Width)
            {
                continue;
            }

            for (var y = around.Y - Reach; y <= around.Y + Reach; y++)
            {
                if (y < 0 || y >= map.Height)
                {
                    continue;
                }

                if (NoteFire(map, x, y))
                {
                    fires++;
                }

                if (NoteHearth(map, x, y))
                {
                    hearths++;
                }

                if (system == null || x % Stride != 0 || y % Stride != 0)
                {
                    continue;
                }

                if (NoteSeam(map, x, y, system))
                {
                    seams++;
                }
            }
        }

        fires += NoteItemFires(map, around);
        hearths += NoteItemHearths(map, around);

        var counters = NoteCounters(map, around);

        clock.Stop();

        logger.Information(
            "Swept {Reach} tiles around {Where} on {Map} in {Elapsed}ms: {Seams} seams, {Fires} fires, {Hearths} hearths, {Counters} counters (now {AllSeams}, {AllFires}, {AllHearths}, {AllCounters})",
            Reach,
            around,
            map,
            clock.ElapsedMilliseconds,
            seams,
            fires,
            hearths,
            counters,
            _seams.Count,
            _fires.Count,
            _hearths.Count,
            _counters.Count
        );

        if (fires > 0)
        {
            var where = ValueStringBuilder.Create(256);

            try
            {
                for (var i = 0; i < _fires.Count; i++)
                {
                    if (i > 0)
                    {
                        where.Append(", ");
                    }

                    where.Append('(');
                    where.Append(_fires[i].Where.X);
                    where.Append(", ");
                    where.Append(_fires[i].Where.Y);
                    where.Append(", ");
                    where.Append(_fires[i].Where.Z);
                    where.Append(')');
                }

                logger.Information(
                    "The fires now known, by address, against a population standing at z {Feet}: {Fires}",
                    BotPopulation.Where.Z,
                    where.ToString()
                );
            }
            finally
            {
                where.Dispose();
            }
        }

        return seams;
    }

    private static bool NoteSeam(Map map, int x, int y, HarvestSystem system)
    {
        if (_seams.Count >= MaxSeams)
        {
            if (!_saidFullOfSeams)
            {
                _saidFullOfSeams = true;

                logger.Error(
                    "The seam list is full at {Max} and no more rock will be recorded anywhere on the island",
                    MaxSeams
                );
            }

            return false;
        }

        if (BotOre.Examine(map, x, y, system) == null)
        {
            return false;
        }

        var where = new Point3D(x, y, map.GetAverageZ(x, y));

        if (Region.Find(where, map)?.IsPartOf<GuardedRegion>() == true)
        {
            Townbound++;

            return false;
        }

        for (var i = 0; i < _seams.Count; i++)
        {
            if (_seams[i].Map == map && Utility.InRange(_seams[i].Where, where, SeamSpacing))
            {
                return false;
            }
        }

        var vein = BotOre.VeinAt(map, x, y);

        _seams.Add(new BotSeam(map, where, BotOre.NameOf(vein), vein?.ReqSkill ?? 0.0));

        return true;
    }

    private static bool NoteFire(Map map, int x, int y)
    {
        if (_fires.Count >= MaxPlaces)
        {
            return false;
        }

        foreach (var tile in map.Tiles.GetStaticAndMultiTiles(x, y))
        {
            if (!IsForgeId(tile.ID))
            {
                continue;
            }

            var where = new Point3D(x, y, tile.Z);

            if (!HasAnvil(map, x, y) || Known(_fires, map, where) || !Footed(map, where))
            {
                return false;
            }

            _fires.Add((map, where));

            return true;
        }

        return false;
    }

    private static int NoteItemFires(Map map, Point3D around)
    {
        var found = 0;

        foreach (var item in map.GetItemsInRange(around, Reach))
        {
            if (item.Deleted || _fires.Count >= MaxPlaces || !IsForgeId(item.ItemID))
            {
                continue;
            }

            var where = item.GetWorldLocation();

            if (Known(_fires, map, where) || !HasAnvil(map, where.X, where.Y) || !Footed(map, where))
            {
                continue;
            }

            _fires.Add((map, where));
            found++;
        }

        return found;
    }

    private static bool NoteHearth(Map map, int x, int y)
    {
        if (_hearths.Count >= MaxPlaces)
        {
            return false;
        }

        foreach (var tile in map.Tiles.GetStaticAndMultiTiles(x, y))
        {
            if (!IsHearthId(tile.ID))
            {
                continue;
            }

            var where = new Point3D(x, y, tile.Z);

            if (Known(_hearths, map, where) || !Footed(map, where))
            {
                return false;
            }

            _hearths.Add((map, where));

            return true;
        }

        return false;
    }

    private static int NoteItemHearths(Map map, Point3D around)
    {
        var found = 0;

        foreach (var item in map.GetItemsInRange(around, Reach))
        {
            if (item.Deleted || _hearths.Count >= MaxPlaces || !IsHearthId(item.ItemID))
            {
                continue;
            }

            var where = item.GetWorldLocation();

            if (Known(_hearths, map, where) || !Footed(map, where))
            {
                continue;
            }

            _hearths.Add((map, where));
            found++;
        }

        return found;
    }

    private static int NoteCounters(Map map, Point3D around)
    {
        var found = 0;

        foreach (var banker in map.GetMobilesInRange<Banker>(around, Reach))
        {
            if (banker.Deleted || _counters.Count >= MaxPlaces)
            {
                continue;
            }

            var where = banker.Location;

            if (Known(_counters, map, where))
            {
                continue;
            }

            _counters.Add((map, where));
            found++;
        }

        return found;
    }

    private static bool Footed(Map map, Point3D where)
    {
        if (!NeedFooting)
        {
            return true;
        }

        var z = (sbyte)Math.Clamp(where.Z, sbyte.MinValue, sbyte.MaxValue);

        if (BotStep.Mask(map, where.X, where.Y, z).WalkMask != 0)
        {
            return true;
        }

        if (BotStep.Settle(map, where.X, where.Y, out var under)
            && Math.Abs(under - where.Z) <= BotArrival.PersonHeight)
        {
            return true;
        }

        for (var dx = -FootingSweep; dx <= FootingSweep; dx++)
        {
            for (var dy = -FootingSweep; dy <= FootingSweep; dy++)
            {
                if ((dx != 0 || dy != 0)
                    && BotStep.Settle(map, where.X + dx, where.Y + dy, out var rz)
                    && Math.Abs(rz - where.Z) <= BotArrival.PersonHeight / 2)
                {
                    return true;
                }
            }
        }

        Unfooted++;

        return false;
    }

    public static int FootingSweep { get; set; } = 1;

    public static bool NeedFooting { get; set; } = true;

    public static long Unfooted { get; private set; }

    private static bool HasAnvil(Map map, int x, int y)
    {
        for (var dx = -AnvilReach; dx <= AnvilReach; dx++)
        {
            for (var dy = -AnvilReach; dy <= AnvilReach; dy++)
            {
                foreach (var tile in map.Tiles.GetStaticAndMultiTiles(x + dx, y + dy))
                {
                    if (IsAnvilId(tile.ID))
                    {
                        return true;
                    }
                }
            }
        }

        foreach (var item in map.GetItemsInRange(new Point3D(x, y, 0), AnvilReach))
        {
            if (IsAnvilId(item.ItemID))
            {
                return true;
            }
        }

        return false;
    }

    private static bool Known(List<(Map Map, Point3D Where)> places, Map map, Point3D where)
    {
        for (var i = 0; i < places.Count; i++)
        {
            if (places[i].Map == map && Utility.InRange(places[i].Where, where, PlaceSpacing))
            {
                return true;
            }
        }

        return false;
    }

    public static BotSeam Seam(IBotWilful bot) => Seam(bot, Point3D.Zero);

    public static int AskEveryMs { get; set; } = 2500;

    private static readonly Dictionary<Serial, (long Tick, Point3D Except, BotSeam Seam)> _told = [];

    public static long Spared { get; private set; }

    public static void Forget(Mobile bot)
    {
        if (bot != null)
        {
            _told.Remove(bot.Serial);
        }
    }

    private static bool Told(Mobile body, Point3D except, out BotSeam seam)
    {
        seam = default;

        if (!_told.TryGetValue(body.Serial, out var last) || last.Except != except)
        {
            return false;
        }

        if (Core.TickCount - last.Tick >= AskEveryMs)
        {
            return false;
        }

        seam = last.Seam;
        Spared++;

        return true;
    }

    public static BotSeam Seam(IBotWilful bot, Point3D except)
    {
        var body = bot?.Self;
        var map = body?.Map;

        if (map == null || map == Map.Internal)
        {
            return default;
        }

        if (Told(body, except, out var lately))
        {
            return lately;
        }

        var ledger = bot.Resolve?.Ledger;
        var best = default(BotSeam);
        var bestScore = 0.0;

        for (var i = 0; i < _seams.Count; i++)
        {
            var seam = _seams[i];

            if (seam.Map != map || (except != Point3D.Zero && seam.Where == except))
            {
                continue;
            }

            if (!BotPopulation.Within(map, seam.Where))
            {
                continue;
            }

            if (ledger != null && ledger.Cautious(BotDig.Trade, map, seam.Where))
            {
                continue;
            }

            if (Draining(seam.Where))
            {
                continue;
            }

            if (!Free(body, seam.Where))
            {
                continue;
            }

            if (BotReach.Ask(map, body.Location, seam.Where, BotArrival.Within(BotOre.Reach)) == BotReachVerdict.Sealed)
            {
                Walled++;

                continue;
            }

            var worth = BotOre.CanWork(body, seam.Required) ? seam.Required / 10.0 : 0.0;

            worth += BotCommons.Richest(map, seam.Where);
            var away = body.GetDistanceToSqrt(seam.Where);
            var score = (1.0 + worth) / (1.0 + away / Patience);

            if (score <= bestScore)
            {
                continue;
            }

            best = seam;
            bestScore = score;
        }

        _told[body.Serial] = (Core.TickCount, except, best);

        return best;
    }

    public static Point3D Fire(Map map, Point3D from) => Nearest(_fires, map, from);

    public static Point3D Fire(Map map, Point3D from, Point3D except) => Nearest(_fires, map, from, except);

    public static Point3D Counter(Map map, Point3D from) => Nearest(_counters, map, from);

    public static Point3D Counter(Map map, Point3D from, Point3D except) => Nearest(_counters, map, from, except);

    public const string FireKind = "fire";

    public const string CounterKind = "counter";

    public const string HearthKind = "hearth";

    public static int DigClaimMs { get; set; } = 90000;

    private static readonly Dictionary<Point3D, (Serial Miner, long Tick)> _digging = [];

    public static void Working(Mobile miner, Point3D seam)
    {
        if (miner != null && seam != Point3D.Zero && Free(miner, seam))
        {
            _digging[seam] = (miner.Serial, Core.TickCount);
        }
    }

    public static bool Free(Mobile miner, Point3D seam)
    {
        if (miner == null || !_digging.TryGetValue(seam, out var held))
        {
            return true;
        }

        if (Core.TickCount - held.Tick >= DigClaimMs)
        {
            _digging.Remove(seam);

            return true;
        }

        return held.Miner == miner.Serial;
    }

    public static void Leave(Point3D seam) => _digging.Remove(seam);

    public static int DrainedMs { get; set; } = 600000;

    public static int RestFromEngine()
    {
        DrainedMs = BotOre.RespawnMs + 60000;

        return DrainedMs;
    }

    public static long Dry { get; private set; }

    private static readonly Dictionary<Point3D, long> _drained = [];

    public static void Drained(Point3D where)
    {
        _drained[where] = Core.TickCount;
        Dry++;
    }

    public static bool Draining(Point3D where) =>
        _drained.TryGetValue(where, out var when) && Core.TickCount - when < DrainedMs;

    public static bool Barren(Point3D where)
    {
        for (var i = 0; i < _seams.Count; i++)
        {
            if (_seams[i].Where != where)
            {
                continue;
            }

            _seams.RemoveAt(i);
            _digging.Remove(where);
            Emptied++;

            return true;
        }

        return false;
    }

    public static long Emptied { get; private set; }

    public static long Prospected { get; private set; }

    public static long Fruitless { get; private set; }

    internal static void Found(int seams) => Prospected += seams;

    internal static void FoundNothing() => Fruitless++;

    public static long Townbound { get; private set; }

    public static Point3D Fire(IBotWilful bot, Point3D from, Point3D except = default) =>
        Nearest(_fires, bot?.Self?.Map, from, except, bot, FireKind);

    public static Point3D Counter(IBotWilful bot, Point3D from, Point3D except = default) =>
        Nearest(_counters, bot?.Self?.Map, from, except, bot, CounterKind);

    public static Point3D Hearth(IBotWilful bot, Point3D from, Point3D except = default) =>
        Nearest(_hearths, bot?.Self?.Map, from, except, bot, HearthKind);

    public static Point3D Hearth(Map map, Point3D from) => Nearest(_hearths, map, from);

    private static Point3D Nearest(
        List<(Map Map, Point3D Where)> places, Map map, Point3D from, Point3D except = default,
        IBotWilful bot = null, string kind = null
    )
    {
        var ledger = kind == null ? null : bot?.Resolve?.Ledger;

        for (var pass = 0; pass < 2; pass++)
        {
            var found = Pick(places, map, from, except, ledger, kind, pass == 0, bot?.Self);

            if (found != Point3D.Zero)
            {
                if (pass > 0)
                {
                    Anyway++;
                }

                return found;
            }
        }

        return Point3D.Zero;
    }

    public static long Anyway { get; private set; }

    private static Point3D Pick(
        List<(Map Map, Point3D Where)> places, Map map, Point3D from, Point3D except, BotLedger ledger, string kind,
        bool choosy, Mobile who = null
    )
    {
        var best = Point3D.Zero;
        var bestAway = double.MaxValue;

        for (var i = 0; i < places.Count; i++)
        {
            var (on, where) = places[i];

            if (on != map || !BotPopulation.Within(on, where))
            {
                continue;
            }

            if (except != Point3D.Zero && where == except)
            {
                continue;
            }

            if (who != null && !BotEstate.MayUse(who, on, where))
            {
                if (choosy)
                {
                    BotEstate.Bar();
                }

                continue;
            }

            if (BotBarred.Barred(on, where))
            {
                continue;
            }

            if (ledger?.Cautious(kind, on, where) == true)
            {
                continue;
            }

            if (BotReach.Ask(on, from, where, BotArrival.Within(1)) == BotReachVerdict.Sealed)
            {
                continue;
            }

            if (Math.Abs(where.Z - from.Z) > Storey)
            {
                Upstairs++;

                continue;
            }

            if (choosy && BotRefused.Refusing(on, where))
            {
                Refused++;

                continue;
            }

            var away = Math.Sqrt(
                (double)(where.X - from.X) * (where.X - from.X) + (double)(where.Y - from.Y) * (where.Y - from.Y)
            );

            if (away >= bestAway)
            {
                continue;
            }

            best = where;
            bestAway = away;
        }

        return best;
    }

    public static void Reset()
    {
        _seams.Clear();
        _fires.Clear();
        _hearths.Clear();
        _counters.Clear();
        _surveyed.Clear();
        _digging.Clear();
        _told.Clear();
        _drained.Clear();
        Dry = 0;
        Spared = 0;
        Upstairs = 0;
        Unfooted = 0;
        Refused = 0;
        Anyway = 0;

        _saidCapped = false;
    }

    public static string Describe() =>
        $"{_surveyed.Count} sweeps: {_seams.Count} seams, {_fires.Count} fires, {_hearths.Count} hearths, {_counters.Count} counters; {Walled} seams passed over with no way through, {Townbound} for being inside the walls, {Emptied} struck off as barren and {Dry} rested after being worked out, {BotMiner.Sent} sent out past the frontier and {Prospected} seams found there over {Fruitless} empty walks, {BotMiner.Burdened} seams not offered to a pack with no room for ore, {BotDig.Unwalkable} struck off for nobody getting nearer to them, {BotDig.Drained} rocks given up with the engine's bank under them empty against {BotDig.Fumbled} still holding ore the miner kept missing and {BotDig.Allowanced} trips that stopped on their own allowance of eight (the miners that gave up on full rock were being given {(BotDig.Fumbled > 0 ? BotDig.FumbledChance / BotDig.Fumbled : 0.0):P0} a swing by the engine, {BotDig.Locked} swings were taken with the pickaxe still locked by the one before, and {BotDig.Stirred} quiet swings were taken by a bot that had moved since the last one, {BotDig.Adrift} swings the engine cancelled for that and {BotDig.Laden} whose ore was lost to a full pack), {Spared} asks answered out of the last scan, {Unfooted} workshops never filed for having no floor at them, {Upstairs} passed over for standing on another floor and {Refused} for ground that has refused the population ({Anyway} choices then had to be made with the resting rule off, or there would have been nowhere at all), patience {Patience} tiles; the lode is at ({Lode.X}, {Lode.Y}); {BotHeard.Describe()}";
}
