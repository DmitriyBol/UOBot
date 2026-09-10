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

    /// <summary>
    /// Which house goes up: the smallest classic one, <c>SmallOldHouse</c> 0x0064.
    ///
    /// Small because ground this engine will accept is the scarce thing, not money — the rules demand five
    /// clear tiles front and back of the foundation, and every tile of extra width is a worse chance of
    /// finding anywhere at all near a town.
    /// </summary>
    public static int MultiID { get; set; } = 0x0064;

    /// <summary>No nearer to the population's home than this. A hall on top of the muster point is in the way.</summary>
    public static int Near { get; set; } = 14;

    /// <summary>And no further, so the walk to it is part of the day rather than an expedition.</summary>
    public static int Far { get; set; } = 90;

    /// <summary>How far apart two halls must stand. Four guilds in a terrace is not a village.</summary>
    public static int Apart { get; set; } = 18;

    /// <summary>
    /// How wide a belt round a no-housing region is kept clear as well.
    ///
    /// <para>
    /// <b>Patrick's order of 08.09.2026, and the reason is what he could see out of the window.</b> The
    /// engine forbids building <em>in</em> Britain's graveyard and says nothing about building against its
    /// railings, so the first hall this shard ever raised went up three tiles from the north fence. Twenty
    /// tiles is his figure. It applies to every <c>NoHousingRegion</c> on the map rather than to that one
    /// graveyard, because the rule he is stating is about what a house should not be pressed up against.
    /// </para>
    /// </summary>
    public static int Clearance { get; set; } = 20;

    /// <summary>How many candidates one call may put through the engine's check.</summary>
    public static int Budget { get; set; } = 60;

    /// <summary>Candidates put through the engine's own check.</summary>
    public static long Tested { get; private set; }

    /// <summary>Candidates the engine would take a house on.</summary>
    public static long Fit { get; private set; }

    /// <summary>Candidates with no floor to stand on — water, a hole, off the edge.</summary>
    public static long Floorless { get; private set; }

    /// <summary>Candidates with something standing on them: an item, a creature, another hall.</summary>
    public static long Occupied { get; private set; }

    /// <summary>Candidates passed over because a town watch stands there. Ours, and it comes before the engine.</summary>
    public static long InTown { get; private set; }

    /// <summary>Candidates passed over for standing too near ground the engine will not build on.</summary>
    public static long Shunned { get; private set; }

    /// <summary>Candidates the engine itself refused on the region: a graveyard, a dungeon, another house.</summary>
    public static long Forbidden { get; private set; }

    /// <summary>Candidates whose ground is not level under the whole footprint. Rule 4, and the one that bites.</summary>
    public static long Uneven { get; private set; }

    /// <summary>Candidates refused for the land: a road, a furrow, a slope against the wall.</summary>
    public static long BadLand { get; private set; }

    /// <summary>Candidates refused for a static: a tree, a rock, a fence, or a yard that is not clear.</summary>
    public static long BadStatic { get; private set; }

    /// <summary>Candidates refused for something immovable lying there.</summary>
    public static long BadItem { get; private set; }

    /// <summary>Candidates where the foundation would rest on nothing. Rule 4, as the engine reports it.</summary>
    public static long Surfaceless { get; private set; }

    /// <summary>How many times the spiral has been walked to its end without a plot.</summary>
    public static long Exhausted { get; private set; }

    /// <summary>The ring of offsets, furthest last, built once — and again if the bounds are dialled.</summary>
    private static List<Point2D> _ring;

    /// <summary>What <see cref="_ring"/> was built with, so a dialled bound is not a dial that does nothing.</summary>
    private static int _builtNear = -1;

    private static int _builtFar = -1;

    /// <summary>How far through <see cref="_ring"/> the search has got.</summary>
    private static int _cursor;

    /// <summary>Ground kept clear round every no-housing region, grown by <see cref="Clearance"/>.</summary>
    private static List<Rectangle2D> _shunned;

    private static int _hallowedBy = -1;

    /// <summary>A valid plot on ground nobody walks, held in case the look ends without a better one.</summary>
    private static Point3D _spare;

    private static bool _hasSpare;

    /// <summary>
    /// The last piece of ground found to be good, kept until something is built on it.
    ///
    /// <para>
    /// <b>Searching and paying are two different clocks and they were blocking each other.</b> A search runs
    /// only when somebody asks, somebody only asks when their guild can pay, and on this shard exactly one
    /// guild could pay — a guild of two — so the island was being examined at a fiftieth of the rate the
    /// population could have managed. Remembering the answer decouples them: whoever is nearest to affording
    /// a hall does the looking, and whoever can actually pay is handed ground that was found minutes ago.
    /// </para>
    /// </summary>
    private static Point3D _kept;

    private static bool _hasKept;

    /// <summary>
    /// The next place a hall could go, or false.
    ///
    /// <paramref name="by"/> is the bot that would place it: the engine's check reads its map and its access
    /// level, and a staff member may build anywhere, which is exactly the answer we do not want.
    /// </summary>
    public static bool Find(Mobile by, out Point3D centre)
    {
        centre = Point3D.Zero;

        var map = BotPopulation.Home;

        if (by == null || map == null || map == Map.Internal)
        {
            return false;
        }

        // Already found, and still good. Re-checked rather than trusted: a woodsman may have dropped a tree
        // across it, or another guild may have built there since.
        if (_hasKept && Sound(map, _kept))
        {
            centre = _kept;

            return true;
        }

        _hasKept = false;

        var from = BotPopulation.Where;

        Build();

        for (var spent = 0; spent < Budget; spent++)
        {
            if (_cursor >= _ring.Count)
            {
                // Round the whole spiral. Begin again rather than stop: the island is not the same place it
                // was an hour ago — trees fall to woodsmen, a hall goes up and moves its neighbours along —
                // and a search that gives up for ever is a search that answers a stale question.
                _cursor = 0;
                Exhausted++;

                break;
            }

            var offset = _ring[_cursor++];
            var x = from.X + offset.X;
            var y = from.Y + offset.Y;

            // A body has to be able to get here at all. Asked first because it is the cheapest of the three
            // questions and the only one about walking rather than about building.
            if (!BotStep.Settle(map, x, y, out _))
            {
                Floorless++;

                continue;
            }

            // <b>The height is the land average, not the floor a bot would stand on, and the difference was
            // a hard zero.</b> HousePlacement rule 4 sets hasSurface only where <c>landAvgZ == center.Z</c>
            // on every foundation tile, so a centre taken from BotStep.Settle — which answers the standable
            // height, and will happily stand a bot on a static two units up — is refused at every tile it is
            // offered. 113 candidates and 113 refusals, before this line read the number the engine actually
            // compares against.
            map.GetAverageZ(x, y, out _, out var avg, out _);

            var at = new Point3D(x, y, avg);

            if (Crowded(map, at))
            {
                Occupied++;

                continue;
            }

            // Not up against a graveyard wall either. See Clearance.
            if (Hallowed(map, at))
            {
                Shunned++;

                continue;
            }

            // Out of town, and this is the engine's rule plus one of ours. The engine refuses housing only
            // inside a NoHousingRegion, which on this island is the graveyard and little else — Britain's
            // own town region would take a house on any square of open ground. A hall in a guarded town is
            // a hall where the watch breaks up anything that happens in it, and what is meant to happen in
            // these is two guildmates knocking each other about for the practice.
            if (Region.Find(at, map)?.IsPartOf<GuardedRegion>() == true)
            {
                InTown++;

                continue;
            }

            // Level under the whole footprint, tested here for a fraction of the price of asking the engine:
            // the full check walks the multi, every static and every item on forty-nine tiles, and the great
            // majority of ground near a town fails on this one rule alone.
            if (!Level(map, at))
            {
                Uneven++;

                continue;
            }

            Tested++;

            var result = HousePlacement.Check(by, MultiID, at, out var toMove, Direction.South);

            if (result != HousePlacementResult.Valid)
            {
                // One bucket per refusal, because a bucket holding four reasons answers none of them. See
                // a-new-gate-needs-a-new-bucket: this counter was written lumped, reported 113 out of 113
                // refused "for the ground", and said nothing whatever about which rule was doing it.
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
                // The engine would shove whatever is there under the house sign. It is entitled to; we are
                // not. Somebody's ore pile is not a building site.
                Occupied++;

                continue;
            }

            Fit++;

            if (BotQuad.Trodden(map, at))
            {
                centre = at;
                _kept = at;
                _hasKept = true;
                Say(at, true);

                return true;
            }

            if (!_hasSpare)
            {
                _spare = at;
                _hasSpare = true;
            }
        }

        // The look is over. Whatever it turned up is handed over now rather than kept for a better one.
        if (_hasSpare)
        {
            _hasSpare = false;
            centre = _spare;
            _kept = centre;
            _hasKept = true;
            Say(centre, false);

            return true;
        }

        return false;
    }

    /// <summary>Something has been built on the remembered ground, or it has been proved bad. Forget it.</summary>
    public static void Spend() => _hasKept = false;

    /// <summary>
    /// Whether remembered ground is still worth walking to: level, empty, and not next door to a hall that
    /// has gone up since. The engine's full check is left to the moment of building, where it must happen
    /// again anyway.
    /// </summary>
    private static bool Sound(Map map, Point3D at) => !Crowded(map, at) && Level(map, at);

    /// <summary>
    /// Says where the ground is, once per piece of it.
    ///
    /// A hall is raised perhaps four times in the life of an island, so the one line that says where it is
    /// going is worth having in the session log rather than only in a five-minute total.
    /// </summary>
    private static void Say(Point3D at, bool trodden) =>
        logger.Information(
            "Ground a hall could stand on at {X},{Y},{Z}, on {Kind} ground: {Tested} candidates have been put to the engine and {Fit} would take one",
            at.X,
            at.Y,
            at.Z,
            trodden ? "trodden" : "untrodden",
            Tested,
            Fit
        );

    /// <summary>
    /// Whether the land is at one height under the whole footprint.
    ///
    /// The footprint is read off the multi itself rather than assumed to be seven by seven, so dialling
    /// <see cref="MultiID"/> to a bigger house changes what is measured as well as what is built.
    /// </summary>
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
                    // The engine's own definition, word for word: a wall tile at the house's own height is
                    // what has to rest on level land. Written as "every tile in the bounding box" first,
                    // which threw away three candidates in five for the sake of the ground under a floor
                    // nothing stands on.
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

    /// <summary>
    /// Whether this spot is inside the belt kept clear round a region the engine forbids housing in.
    ///
    /// <para>
    /// The rectangles are gathered once from the engine's own region list rather than written down here: a
    /// graveyard's bounds are the world's business, and a copy of them in this file would be a second
    /// opinion that goes stale the first time the map is decorated.
    /// </para>
    /// </summary>
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

    /// <summary>Gathers those rectangles, grown by <see cref="Clearance"/>, the first time anybody asks.</summary>
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

    /// <summary>Whether a hall already stands near enough that another would be a terrace.</summary>
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

    /// <summary>
    /// The offsets to try, nearest first.
    ///
    /// Built once and walked in order, so successive searches carry on rather than starting again at the
    /// same refused tile — which is how a budgeted search turns into a budgeted loop.
    /// </summary>
    private static void Build()
    {
        // Rebuilt when the bounds move, because they are dials: a ring built once and kept for ever would
        // make "dial BotPlot.Far 300" a line that is accepted, journalled, and does nothing at all.
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
        _cursor = 0;
    }

    /// <summary>Part of the estate's line in the summary.</summary>
    public static string Describe() =>
        $"plots: {Tested} put to the engine and {Fit} would take a hall; {Uneven} passed over as not level, {Floorless} with no floor at all, {InTown} for standing in a town, {Shunned} for pressing against a graveyard, {Occupied} with something standing on them; the engine refused {Forbidden} on its own regions, {Surfaceless} for resting on nothing, {BadLand} for the land, {BadStatic} for a static or an unclear yard, {BadItem} for something lying there; the spiral has been walked out {Exhausted} times";

    public static void Forget()
    {
        _ring = null;
        _cursor = 0;
        _hasSpare = false;
        Tested = 0;
        Fit = 0;
        Floorless = 0;
        Occupied = 0;
        InTown = 0;
        Shunned = 0;
        _shunned = null;
        _hallowedBy = -1;
        Forbidden = 0;
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
