using System;
using System.Collections.Generic;
using Server.Logging;
using Server.Multis;
using Server.Regions;

namespace Server.BotAI.V2;

/// <summary>
/// Finding ground a house will actually stand on.
///
/// <para>
/// <b>The engine is the only authority here and it is asked directly.</b> <c>HousePlacement.Check</c> knows
/// the five rules — nothing impassable touching the foundation, five clear tiles front and back, a flat
/// surface, no road underneath, no no-housing region — and it answers with which one was broken. Guessing
/// any part of that here would be a second opinion about the world, and this project has paid for those:
/// the fastest way to a house that cannot be placed is code that believes it knows where houses go.
/// </para>
///
/// <para>
/// <b>Trodden ground is preferred for the length of one look, and never required.</b> A hall in a corner
/// of the island nobody walks through is a hall whose forge no sweep will ever find, so
/// <see cref="BotQuad.Trodden"/> decides between two valid plots found in the same search. It does not
/// decide whether a plot is valid — that would be a veto dressed as a preference — and it does not
/// outlive the search either: holding the untrodden plot back until the whole spiral had been walked meant
/// holding it for an hour, and the shard spent twenty minutes reporting "1 would take a hall" while
/// nothing was ever offered to anybody.
/// </para>
///
/// <para>
/// The search is a spiral outwards from where the population lives, kept between calls, budgeted per call.
/// A full check costs the engine a 7x7 multi's worth of tile and item queries; forty-nine bots taking their
/// turn at that on one beat is a stall, and a stall is the thing this shard is least willing to pay.
/// </para>
/// </summary>
public static class BotPlot
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotPlot));

    public static int MultiID { get; set; } = 0x0064;

    public static int Near { get; set; } = 14;

    public static int Far { get; set; } = 90;

    public static int Apart { get; set; } = 40;

    public static int Shy { get; set; } = 120;

    public static long Neighboured { get; private set; }

    public static int Clearance { get; set; } = 20;

    public static int Budget { get; set; } = 60;

    public static long Tested { get; private set; }

    public static long Fit { get; private set; }

    public static long Floorless { get; private set; }

    public static long Occupied { get; private set; }

    public static long InTown { get; private set; }

    public static long Shunned { get; private set; }

    public static long Forbidden { get; private set; }

    public static long Uneven { get; private set; }

    public static long BadLand { get; private set; }

    public static long BadStatic { get; private set; }

    public static long BadItem { get; private set; }

    public static long Surfaceless { get; private set; }

    public static long Exhausted { get; private set; }

    private static List<Point2D> _ring;

    private static int _builtNear = -1;

    private static int _builtFar = -1;

    private static List<Rectangle2D> _shunned;

    private static int _hallowedBy = -1;

    /// <summary>
    /// One search, from one origin: how far round the spiral it has got and what it has found.
    ///
    /// <para>
    /// <b>One per origin, because every guild now looks from its own seat.</b> The first cut kept a single
    /// cursor and a single remembered plot for the whole island, which was right while the whole island
    /// looked from one point; with five guilds looking from five seats a shared cursor would be a search
    /// that jumps three hundred tiles between one call and the next and remembers a plot found for the
    /// wrong guild. See <c>BotSeat</c>.
    /// </para>
    /// </summary>
    private sealed class Search
    {
        public int Cursor;

        public Point3D Spare;

        public bool HasSpare;

        public Point3D Kept;

        public bool HasKept;
    }

    private static readonly Dictionary<(int X, int Y), Search> _searches = [];

    public static bool Find(Mobile by, out Point3D centre) => Find(by, Point3D.Zero, Point3D.Zero, 0, out centre);

    public static bool Find(Mobile by, Point3D from, out Point3D centre) => Find(by, from, Point3D.Zero, 0, out centre);

    public static bool Find(Mobile by, Point3D from, Point3D shun, int clear, out Point3D centre)
    {
        centre = Point3D.Zero;

        var map = BotPopulation.Home;

        if (by == null || map == null || map == Map.Internal)
        {
            return false;
        }

        if (from == Point3D.Zero)
        {
            from = BotPopulation.Where;
        }

        if (!_searches.TryGetValue((from.X, from.Y), out var search))
        {
            search = new Search();
            _searches[(from.X, from.Y)] = search;
        }

        if (search.HasKept && Sound(map, search.Kept) && Away(search.Kept, shun, clear))
        {
            centre = search.Kept;

            return true;
        }

        search.HasKept = false;

        Build();

        for (var spent = 0; spent < Budget; spent++)
        {
            if (search.Cursor >= _ring.Count)
            {
                search.Cursor = 0;
                Exhausted++;

                break;
            }

            var offset = _ring[search.Cursor++];
            var x = from.X + offset.X;
            var y = from.Y + offset.Y;

            if (!BotStep.Settle(map, x, y, out _))
            {
                Floorless++;

                continue;
            }

            map.GetAverageZ(x, y, out _, out var avg, out _);

            var at = new Point3D(x, y, avg);

            if (!Away(at, shun, clear))
            {
                Overshadowed++;

                continue;
            }

            if (Crowded(map, at))
            {
                Occupied++;

                continue;
            }

            if (Hallowed(map, at))
            {
                Shunned++;

                continue;
            }

            if (Region.Find(at, map)?.IsPartOf<GuardedRegion>() == true)
            {
                InTown++;

                continue;
            }

            if (!Level(map, at))
            {
                Uneven++;

                continue;
            }

            Tested++;

            var result = HousePlacement.Check(by, MultiID, at, out var toMove, Direction.South);

            if (result != HousePlacementResult.Valid)
            {
                switch (result)
                {
                    case HousePlacementResult.BadRegion:
                    case HousePlacementResult.BadRegionHidden:
                    case HousePlacementResult.BadRegionTemp:
                    case HousePlacementResult.BadRegionRaffle:
                        {
                            Forbidden++;

                            break;
                        }
                    case HousePlacementResult.BadLand:
                        {
                            BadLand++;

                            break;
                        }
                    case HousePlacementResult.BadStatic:
                        {
                            BadStatic++;

                            break;
                        }
                    case HousePlacementResult.BadItem:
                        {
                            BadItem++;

                            break;
                        }
                    default:
                        {
                            Surfaceless++;

                            break;
                        }
                }

                continue;
            }

            if (toMove is { Count: > 0 })
            {
                Occupied++;

                continue;
            }

            Fit++;

            if (BotQuad.Trodden(map, at) && Nearest(map, at) >= Shy)
            {
                centre = at;
                search.Kept = at;
                search.HasKept = true;
                Say(at, from, true);

                return true;
            }

            if (!search.HasSpare || Nearest(map, at) > Nearest(map, search.Spare))
            {
                search.Spare = at;
                search.HasSpare = true;
            }
        }

        if (search.HasSpare)
        {
            if (Nearest(map, search.Spare) < Shy)
            {
                Neighboured++;
            }

            search.HasSpare = false;
            centre = search.Spare;
            search.Kept = centre;
            search.HasKept = true;
            Say(centre, from, false);

            return true;
        }

        return false;
    }

    private static bool Away(Point3D at, Point3D shun, int clear) =>
        clear <= 0 || shun == Point3D.Zero ||
        Math.Max(Math.Abs(at.X - shun.X), Math.Abs(at.Y - shun.Y)) >= clear;

    public static long Overshadowed { get; private set; }

    public static void Spend()
    {
        foreach (var search in _searches.Values)
        {
            search.HasKept = false;
        }
    }

    private static bool Sound(Map map, Point3D at) => !Crowded(map, at) && Level(map, at);

    private static void Say(Point3D at, Point3D from, bool trodden) =>
        logger.Information(
            "Ground a hall could stand on at {X},{Y},{Z}, on {Kind} ground, {Gap} tiles from the seat at {FromX},{FromY}: {Tested} candidates have been put to the engine and {Fit} would take one",
            at.X,
            at.Y,
            at.Z,
            trodden ? "trodden" : "untrodden",
            Math.Max(Math.Abs(at.X - from.X), Math.Abs(at.Y - from.Y)),
            from.X,
            from.Y,
            Tested,
            Fit
        );

    private static bool Level(Map map, Point3D at)
    {
        var mcl = MultiData.GetComponents(MultiID);

        for (var ix = 0; ix < mcl.Width; ix++)
        {
            for (var iy = 0; iy < mcl.Height; iy++)
            {
                var tiles = mcl.Tiles[ix][iy];
                var foundation = false;

                for (var t = 0; t < tiles.Length && !foundation; t++)
                {
                    foundation = tiles[t].Z == 0 &&
                                 TileData.ItemTable[tiles[t].ID & TileData.MaxItemValue].Wall;
                }

                if (!foundation)
                {
                    continue;
                }

                map.GetAverageZ(at.X + mcl.Min.X + ix, at.Y + mcl.Min.Y + iy, out _, out var avg, out _);

                if (avg != at.Z)
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static bool Hallowed(Map map, Point3D at)
    {
        if (Clearance <= 0)
        {
            return false;
        }

        Hallow(map);

        for (var i = 0; i < _shunned.Count; i++)
        {
            var box = _shunned[i];

            if (at.X >= box.X && at.X <= box.X + box.Width && at.Y >= box.Y && at.Y <= box.Y + box.Height)
            {
                return true;
            }
        }

        return false;
    }

    private static void Hallow(Map map)
    {
        if (_shunned != null && _hallowedBy == Clearance)
        {
            return;
        }

        _shunned = [];
        _hallowedBy = Clearance;

        var regions = Region.Regions;

        for (var i = 0; i < regions.Count; i++)
        {
            var region = regions[i];

            if (region?.Map != map || !region.IsPartOf<NoHousingRegion>() || region.Area == null)
            {
                continue;
            }

            for (var a = 0; a < region.Area.Length; a++)
            {
                var box = region.Area[a];

                _shunned.Add(
                    new Rectangle2D(
                        box.Start.X - Clearance,
                        box.Start.Y - Clearance,
                        box.Width + Clearance * 2,
                        box.Height + Clearance * 2
                    )
                );
            }
        }

        logger.Information(
            "{Count} places on this map will not have a hall within {Clearance} tiles of them",
            _shunned.Count,
            Clearance
        );
    }

    private static int Nearest(Map map, Point3D at)
    {
        var nearest = int.MaxValue;

        foreach (var house in BotEstate.Halls)
        {
            if (house is not { Deleted: false } || house.Map != map)
            {
                continue;
            }

            var gap = Math.Max(Math.Abs(house.X - at.X), Math.Abs(house.Y - at.Y));

            if (gap < nearest)
            {
                nearest = gap;
            }
        }

        return nearest;
    }

    private static bool Crowded(Map map, Point3D at)
    {
        foreach (var house in BotEstate.Halls)
        {
            if (house is { Deleted: false } && house.Map == map && house.Location.GetDistanceToSqrt(at) < Apart)
            {
                return true;
            }
        }

        return false;
    }

    private static void Build()
    {
        if (_ring != null && _builtNear == Near && _builtFar == Far)
        {
            return;
        }

        var ring = new List<Point2D>();

        for (var dx = -Far; dx <= Far; dx += 2)
        {
            for (var dy = -Far; dy <= Far; dy += 2)
            {
                var away = Math.Sqrt(dx * dx + dy * dy);

                if (away >= Near && away <= Far)
                {
                    ring.Add(new Point2D(dx, dy));
                }
            }
        }

        ring.Sort(
            (a, b) => (a.X * a.X + a.Y * a.Y).CompareTo(b.X * b.X + b.Y * b.Y)
        );

        _ring = ring;
        _builtNear = Near;
        _builtFar = Far;

        foreach (var search in _searches.Values)
        {
            search.Cursor = 0;
        }
    }

    public static string Describe() =>
        $"plots: {Tested} put to the engine and {Fit} would take a hall; {Uneven} passed over as not level, {Floorless} with no floor at all, {InTown} for standing in a town, {Shunned} for pressing against a graveyard, {Occupied} with something standing on them, {Neighboured} taken within {Shy} tiles of another clan for want of anything further off; the engine refused {Forbidden} on its own regions, {Surfaceless} for resting on nothing, {BadLand} for the land, {BadStatic} for a static or an unclear yard, {BadItem} for something lying there; the spiral has been walked out {Exhausted} times";

    public static void Forget()
    {
        _ring = null;
        _searches.Clear();
        Tested = 0;
        Fit = 0;
        Floorless = 0;
        Occupied = 0;
        InTown = 0;
        Shunned = 0;
        _shunned = null;
        _hallowedBy = -1;
        Forbidden = 0;
        Neighboured = 0;
        Uneven = 0;
        BadLand = 0;
        BadStatic = 0;
        BadItem = 0;
        Surfaceless = 0;
        Exhausted = 0;
        _builtNear = -1;
        _builtFar = -1;
    }
}
