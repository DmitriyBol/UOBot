using System;
using System.Collections.Generic;
using Server.Guilds;
using Server.Logging;
using Server.Multis;

namespace Server.BotAI.V2;

/// <summary>
/// Ground another guild holds, and what may not be put on it.
///
/// <para>
/// <b>Patrick, 06.10.2026 ~10:00: "The Ash has put its residence on The Wolf's lands: how could that happen? It is
/// forbidden."</b> It happened at 01:16:38 that night. The steward's plot search (<c>BotPlot</c>) asked whether a candidate
/// was another guild's ground of its centre tile alone; The Ash's hall went up at 1470,1412, in a square nobody held, and its
/// seven-by-seven walls and its steps (x 1467–1473, y 1409–1416) stood three columns deep in The Wolf's square at 1440,1410
/// and one row deep in its square at 1440,1380, held since before 04.10 22:50. Raising it wrote the hall's square into the
/// claims as The Ash's (<c>BotClaim.Seat</c>), which would have overwritten another guild's holding without a word had the
/// centre been on one; and the seat followed the hall there. Nothing ever looked again when ground round a standing hall
/// changed hands: The Wolf took the square north of it at 07:21:28 and the hall stood on The Wolf's ground on two sides.
/// </para>
///
/// <para>
/// <b>The rule.</b> A guild building — a first hall, a bigger one, an outpost, a hall carried anywhere — stands wholly on
/// ground its guild will hold: the square of its centre must be open or the guild's own, and every other square under its
/// walls and steps must be the guild's own (<see cref="Covers"/>, strict). A seat, a guild's town house and a bare seat
/// are points, and the point must not be another guild's (<see cref="Foreign"/>). A bot's own house may not stand on another
/// guild's ground unless the two guilds are allied (lenient, allies welcome). A hall's yard never swallows a square another
/// guild holds (<c>BotClaim.Seat</c>), and a claim on held ground is only ever the seizure, which is a war
/// (<c>BotSeizure</c>). Held ground is what <see cref="BotLand.Holder"/> answers: claimed squares, halls' squares and
/// outposts' squares.
/// </para>
///
/// <para>
/// <b>And when ground changes hands, the look that was missing.</b> Every <see cref="LookMs"/> each standing hall is asked
/// whether any square under it is now another guild's; one that is owes a move, which the remover carries to the nearest
/// ground of its own or open ground (<c>BotRemover</c>, the errand that carries a hall to its seat), and the seat follows
/// the hall. A bare seat on another guild's ground is moved to the nearest point that is not, within <see cref="SeatReach"/>;
/// a guild's town house on it is left (<c>BotGuildHouses.Leave</c>, the path a seat set at the door takes) and the guild
/// settles again in a building that is not. Nothing within reach: the hall stands and waits, counted every look, said once.
/// </para>
/// </summary>
public static class BotBounds
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotBounds));

    public static bool Running { get; set; } = true;

    public static int LookMs { get; set; } = 30000;

    public static int SeatReach { get; set; } = 90;

    /// <summary>Why a building may not stand where it was asked to.</summary>
    public enum Breach
    {
        None,

        Foreign,

        Straddling
    }

    /// <summary>Placements refused at the moment of building, by what was being built.</summary>
    public enum Placing
    {
        Hall,
        Bigger,
        Outpost,
        Carry,
        Abode
    }

    private static readonly long[] _refused = new long[5];

    public static long SeatsRefused { get; private set; }

    public static long HousesRefused { get; private set; }

    public static long YardsKept { get; private set; }

    public static long Spared { get; private set; }

    public static long HallsFound { get; private set; }

    public static long HallsMoved { get; private set; }

    public static long HallsOffered { get; private set; }

    public static long HallsWaiting { get; private set; }

    public static long SeatsMoved { get; private set; }

    public static long SeatsWaiting { get; private set; }

    public static long HousesLeft { get; private set; }

    public static int AbodesAbroad { get; private set; }

    /// <summary>
    /// A hall's move off another guild's ground, step by step (06.10.2026, build 410). <b>The first cut carried The Ash 112 tiles away
    /// and dropped its square.</b> Its hall at 1470,1412 stood in its own square with only its west and north walls on The Wolf's;
    /// the remover asked <c>BotPlot.Find</c>, whose spiral begins <c>BotPlot.Near</c> (14) tiles out and whose engine check counts
    /// the standing hall as an obstacle, so the shift of a few tiles that would have fitted was never put to anything: 256 offers
    /// found nothing in 17 minutes, the next found 1522,1300, and the square, no longer reaching the hall, fell (10:39:39).
    /// Now: a shift inside the hall's own square with the standing hall's tiles free (<c>BotShift</c>), then the guild's other
    /// squares near it; then open ground within <see cref="OpenReach"/>; then the hall stands, its foreign part giving it nothing.
    /// </summary>
    private sealed class Owing
    {
        public string Whose;

        public bool ShiftTried;

        public int OpenMisses;

        public long StandUntil;
    }

    private static readonly Dictionary<string, Owing> _owed = [];

    public static int ShiftChecks { get; set; } = 150;

    public static int ShiftSquares { get; set; } = 2;

    public static int OpenReach { get; set; } = 90;

    public static int OpenLooks { get; set; } = 40;

    public static int TolerateMs { get; set; } = 3600000;

    public static long ShiftedOwn { get; private set; }

    public static long ShiftedHeld { get; private set; }

    public static long ShiftsFailed { get; private set; }

    public static long OpenTooFar { get; private set; }

    public static long Stood { get; private set; }

    private static readonly HashSet<string> _waitingSaid = [];

    private static long _looked;

    public static bool Foreign(string guild, Map map, Point3D at, out string whose)
    {
        whose = null;

        if (!Running || map == null || map == Map.Internal)
        {
            return false;
        }

        var holder = BotLand.Holder(map, at);

        if (holder == null || holder == guild)
        {
            return false;
        }

        whose = holder;

        return true;
    }

    public static Breach Covers(string guild, Map map, Point3D centre, int multi, bool strict, bool alliesWelcome, out string whose)
    {
        var mcl = MultiData.GetComponents(multi);

        return Covers(guild, map, centre, mcl.Min, mcl.Max, strict, alliesWelcome, out whose);
    }

    public static Breach Covers(string guild, Map map, Point3D centre, Point2D min, Point2D max, bool strict, bool alliesWelcome, out string whose)
    {
        whose = null;

        if (!Running || map == null || map == Map.Internal)
        {
            return Breach.None;
        }

        var home = BotQuad.Key(map, centre);
        var low = BotQuad.Key(map, new Point3D(centre.X + min.X, centre.Y + min.Y, 0));
        var high = BotQuad.Key(map, new Point3D(centre.X + max.X, centre.Y + max.Y, 0));

        for (var qx = low.X; qx <= high.X; qx++)
        {
            for (var qy = low.Y; qy <= high.Y; qy++)
            {
                var holder = BotLand.Holder(map, new Point3D(qx * BotQuad.Side + BotQuad.Side / 2, qy * BotQuad.Side + BotQuad.Side / 2, 0));

                if (holder == guild && guild != null)
                {
                    continue;
                }

                if (holder == null)
                {
                    if (strict && (qx != home.X || qy != home.Y))
                    {
                        whose = "open ground";

                        return Breach.Straddling;
                    }

                    continue;
                }

                if (alliesWelcome && BotRegard.AreAllied(guild, holder))
                {
                    continue;
                }

                whose = holder;

                return Breach.Foreign;
            }
        }

        return Breach.None;
    }

    public static bool Refuse(Placing what, string guild, Map map, Point3D plot, int multi, out string why)
    {
        why = null;

        var abode = what == Placing.Abode;
        var breach = Covers(guild, map, plot, multi, !abode, abode, out var whose);

        if (breach == Breach.None)
        {
            return false;
        }

        _refused[(int)what]++;

        why = breach == Breach.Foreign
            ? $"the ground at {plot.X},{plot.Y} lies partly on {whose}'s held ground"
            : $"the walls at {plot.X},{plot.Y} would reach out of their own square onto open ground";

        logger.Information("{Guild}: no {What} goes up at {X},{Y}: {Why}", guild ?? "a bot of no guild", Name(what), plot.X, plot.Y, why);

        return true;
    }

    private static string Name(Placing what) =>
        what switch
        {
            Placing.Hall => "hall",
            Placing.Bigger => "bigger hall",
            Placing.Outpost => "outpost",
            Placing.Carry => "hall carried",
            _ => "house of its own"
        };

    public static void SeatRefused() => SeatsRefused++;

    public static void HouseRefused() => HousesRefused++;

    public static void Spare() => Spared++;

    public static void YardKept(string guild, string holder, Point3D hall)
    {
        YardsKept++;

        logger.Warning(
            "The hall of {Guild} at {X},{Y} stands in a square {Holder} holds; the square stays {Holder}'s, and the hall is to be carried off it",
            guild,
            hall.X,
            hall.Y,
            holder,
            holder
        );
    }

    public static string HallIn(string guild, Map map, (int Map, int X, int Y) key)
    {
        if (!Running || map == null)
        {
            return null;
        }

        foreach (var (name, hall) in BotEstate.Held)
        {
            if (name == guild || hall is not { Deleted: false } || hall.Map != map || hall.Components == null)
            {
                continue;
            }

            var low = BotQuad.Key(map, new Point3D(hall.X + hall.Components.Min.X, hall.Y + hall.Components.Min.Y, 0));
            var high = BotQuad.Key(map, new Point3D(hall.X + hall.Components.Max.X, hall.Y + hall.Components.Max.Y, 0));

            if (key.X >= low.X && key.X <= high.X && key.Y >= low.Y && key.Y <= high.Y)
            {
                return name;
            }
        }

        return null;
    }

    public static bool Owes(Guild guild, out BaseHouse hall, out string whose)
    {
        hall = null;
        whose = null;

        if (!Running || guild == null || !_owed.TryGetValue(guild.Name, out var owing))
        {
            return false;
        }

        hall = BotEstate.Hall(guild);

        if (hall is not { Deleted: false })
        {
            _owed.Remove(guild.Name);

            return false;
        }

        if (owing.StandUntil != 0)
        {
            if (Core.TickCount - owing.StandUntil < 0)
            {
                return false;
            }

            owing.StandUntil = 0;
            owing.ShiftTried = false;
            owing.OpenMisses = 0;
        }

        whose = owing.Whose;

        return true;
    }

    public static bool Shifting(string guild) => guild != null && _owed.TryGetValue(guild, out var owing) && !owing.ShiftTried;

    public static void Offered() => HallsOffered++;

    public static List<(Point3D At, bool Own)> Shifts(string guild, Map map, BaseHouse old, int multi)
    {
        var found = new List<(Point3D At, bool Own, int Gap)>();

        if (guild == null || map == null || old == null)
        {
            return [];
        }

        var home = BotQuad.Key(map, old.Location);

        foreach (var (holder, facet, qx, qy, _) in BotClaim.Holdings())
        {
            if (holder != guild || facet != map.MapID || Math.Max(Math.Abs(qx - home.X), Math.Abs(qy - home.Y)) > ShiftSquares)
            {
                continue;
            }

            var own = qx == home.X && qy == home.Y;
            var step = own ? 1 : 2;

            for (var dx = 0; dx < BotQuad.Side; dx += step)
            {
                for (var dy = 0; dy < BotQuad.Side; dy += step)
                {
                    var x = qx * BotQuad.Side + dx;
                    var y = qy * BotQuad.Side + dy;

                    if (x == old.X && y == old.Y)
                    {
                        continue;
                    }

                    if (Covers(guild, map, new Point3D(x, y, 0), multi, true, false, out _) != Breach.None)
                    {
                        continue;
                    }

                    map.GetAverageZ(x, y, out _, out var avg, out _);

                    var at = new Point3D(x, y, avg);

                    if (!BotPlot.Suits(map, at, multi, old))
                    {
                        continue;
                    }

                    found.Add((at, own, Math.Max(Math.Abs(x - old.X), Math.Abs(y - old.Y))));
                }
            }
        }

        found.Sort((a, b) => a.Own != b.Own ? (a.Own ? -1 : 1) : a.Gap.CompareTo(b.Gap));

        var list = new List<(Point3D At, bool Own)>(found.Count);

        for (var i = 0; i < found.Count; i++)
        {
            list.Add((found[i].At, found[i].Own));
        }

        return list;
    }

    public static void ShiftFailed(string guild, string why)
    {
        ShiftsFailed++;

        if (guild != null && _owed.TryGetValue(guild, out var owing))
        {
            owing.ShiftTried = true;
        }

        logger.Warning("The hall of {Guild} could not be shifted onto ground of its own: {Why}; open ground within {Reach} tiles is looked for next", guild, why, OpenReach);
    }

    public static void Shifted(string guild, Point3D from, Point3D at, bool own)
    {
        if (own)
        {
            ShiftedOwn++;
        }
        else
        {
            ShiftedHeld++;
        }

        Owing owing = null;

        if (guild != null)
        {
            _owed.Remove(guild, out owing);
            _waitingSaid.Remove(guild);
        }

        logger.Warning(
            "The hall of {Guild} has been shifted off {Whose}'s ground from {FX},{FY} to {X},{Y}, {Where}; its seat follows it",
            guild,
            owing?.Whose ?? "another guild",
            from.X,
            from.Y,
            at.X,
            at.Y,
            own ? "within its own square" : "onto another square it holds"
        );
    }

    public static void Missed(string guild, string whose, bool tooFar)
    {
        HallsWaiting++;

        if (tooFar)
        {
            OpenTooFar++;
        }

        if (guild == null || !_owed.TryGetValue(guild, out var owing))
        {
            return;
        }

        if (!tooFar && ++owing.OpenMisses < OpenLooks)
        {
            return;
        }

        var rest = Math.Max(60000, TolerateMs);

        owing.StandUntil = Core.TickCount + rest;
        Stood++;

        logger.Warning(
            "The hall of {Guild} stands with part of its walls on {Whose}'s ground: no ground of its own fits it and no open ground lies within {Reach} tiles; it stands, the part on {Whose}'s ground giving it no yard, no land and no toll, and the move is tried again in {Minutes} minutes",
            guild,
            whose,
            OpenReach,
            whose,
            rest / 60000
        );
    }

    public static void Paid(string guild, Point3D at)
    {
        if (guild == null || !_owed.Remove(guild, out var owing))
        {
            return;
        }

        HallsMoved++;
        _waitingSaid.Remove(guild);

        logger.Warning("The hall of {Guild} has been carried off {Whose}'s ground to {X},{Y}; its seat follows it", guild, owing.Whose, at.X, at.Y);
    }

    public static void Look()
    {
        if (!Running || !BotEstate.HallsAdopted)
        {
            return;
        }

        var now = Core.TickCount;

        if (now - (_looked + LookMs) < 0)
        {
            return;
        }

        _looked = now;

        LookHalls();
        LookSeats();
        LookAbodes();
    }

    private static readonly List<string> _cleared = [];

    private static void LookHalls()
    {
        _cleared.Clear();

        foreach (var name in _owed.Keys)
        {
            if (!BotEstate.Held.TryGetValue(name, out var gone) || gone is not { Deleted: false })
            {
                _cleared.Add(name);
            }
        }

        foreach (var (name, hall) in BotEstate.Held)
        {
            if (hall is not { Deleted: false } || hall.Map == null || hall.Map == Map.Internal || hall.Components == null)
            {
                continue;
            }

            var breach = Covers(name, hall.Map, hall.Location, hall.Components.Min, hall.Components.Max, false, false, out var whose);

            if (breach == Breach.Foreign)
            {
                if (_owed.TryAdd(name, new Owing { Whose = whose }))
                {
                    HallsFound++;

                    logger.Warning(
                        "The hall of {Guild} at {X},{Y} stands on {Whose}'s held ground; it is to be carried to the nearest ground of its own or open ground",
                        name,
                        hall.X,
                        hall.Y,
                        whose
                    );
                }
                else
                {
                    _owed[name].Whose = whose;
                }

                continue;
            }

            if (_owed.ContainsKey(name))
            {
                _cleared.Add(name);
            }
        }

        for (var i = 0; i < _cleared.Count; i++)
        {
            if (_owed.Remove(_cleared[i]))
            {
                _waitingSaid.Remove(_cleared[i]);
                logger.Information("The hall of {Guild} no longer stands on another guild's ground; it owes no move", _cleared[i]);
            }
        }
    }

    private static readonly List<Guild> _guilds = [];

    private static void LookSeats()
    {
        var map = BotPopulation.Home;

        if (map == null)
        {
            return;
        }

        _guilds.Clear();
        _guilds.AddRange(BotGuilds.Standing);

        for (var i = 0; i < _guilds.Count; i++)
        {
            var name = _guilds[i]?.Name;

            if (name == null)
            {
                continue;
            }

            if (BotGuildHouses.Of(name) is { } house && Foreign(name, map, house.Heart, out var owner))
            {
                if (BotGuildHouses.Leave(name, $"its house stands on {owner}'s held ground"))
                {
                    HousesLeft++;

                    logger.Warning("{Guild} leaves its house in {Town} at {X},{Y}: it stands on {Whose}'s held ground", name, house.Town, house.Heart.X, house.Heart.Y, owner);
                }

                continue;
            }

            var bare = BotSeat.Bare(name);

            if (bare == Point3D.Zero || !Foreign(name, map, bare, out var whose))
            {
                _waitingSaid.Remove("seat:" + name);

                continue;
            }

            if (_owed.ContainsKey(name))
            {
                continue;
            }

            if (Nearest(name, map, bare, out var at))
            {
                SeatsMoved++;
                _waitingSaid.Remove("seat:" + name);
                BotSeat.Shift(name, at, $"its seat at {bare.X},{bare.Y} lay on {whose}'s held ground");

                continue;
            }

            SeatsWaiting++;

            if (_waitingSaid.Add("seat:" + name))
            {
                logger.Warning(
                    "The seat of {Guild} at {X},{Y} lies on {Whose}'s held ground and no ground of its own or open ground lies within {Reach} tiles; it waits",
                    name,
                    bare.X,
                    bare.Y,
                    whose,
                    SeatReach
                );
            }
        }
    }

    private static bool Nearest(string guild, Map map, Point3D from, out Point3D at)
    {
        at = Point3D.Zero;

        for (var ring = 2; ring <= SeatReach; ring += 2)
        {
            for (var dx = -ring; dx <= ring; dx += 2)
            {
                for (var dy = -ring; dy <= ring; dy += 2)
                {
                    if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != ring)
                    {
                        continue;
                    }

                    var x = from.X + dx;
                    var y = from.Y + dy;

                    if (Foreign(guild, map, new Point3D(x, y, 0), out _) || !BotStep.Settle(map, x, y, out var z))
                    {
                        continue;
                    }

                    at = new Point3D(x, y, z);

                    return true;
                }
            }
        }

        return false;
    }

    private static void LookAbodes()
    {
        var abroad = 0;

        foreach (var house in BotAbode.Houses)
        {
            if (house is not { Deleted: false } || house.Map == null || house.Components == null)
            {
                continue;
            }

            var guild = (house.Owner?.Guild as Guild)?.Name;

            if (Covers(guild, house.Map, house.Location, house.Components.Min, house.Components.Max, false, true, out _) != Breach.None)
            {
                abroad++;
            }
        }

        AbodesAbroad = abroad;
    }

    public static string Describe() =>
        !Running
            ? "ground another guild holds is not kept from placement"
            : $"held ground kept: a guild building stands wholly on its own square or ground it holds, a seat or a house never on another guild's ground, a bot's house only on its own or an ally's; "
            + $"refused at the building: {_refused[(int)Placing.Hall]} halls, {_refused[(int)Placing.Bigger]} bigger halls, {_refused[(int)Placing.Outpost]} outposts, {_refused[(int)Placing.Carry]} halls carried, {_refused[(int)Placing.Abode]} bots' houses; "
            + $"{SeatsRefused} seats refused, {HousesRefused} looks passed over a town building on another guild's ground, {YardsKept} yards not written over another guild's square, {Spared} open squares not claimed for another guild's hall standing in them; "
            + $"ground changing hands: {HallsFound} halls found on another guild's ground ({_owed.Count} owing a move now), {ShiftedOwn} shifted within their own square, {ShiftedHeld} onto another square of their guild's, {ShiftsFailed} shifts that found nothing and put the hall back, {HallsOffered} carries offered, {HallsMoved} carried to open ground, {HallsWaiting} looks found no open ground within {OpenReach} tiles ({OpenTooFar} found it further), {Stood} halls let stand for {TolerateMs / 60000} minutes with their foreign part giving nothing; "
            + $"{SeatsMoved} seats moved off, {SeatsWaiting} looks found nowhere for one; {HousesLeft} town houses left; {AbodesAbroad} bots' houses on a guild's ground not their own nor allied (kept, not moved)";

    public static void Forget()
    {
        Array.Clear(_refused);
        SeatsRefused = 0;
        HousesRefused = 0;
        YardsKept = 0;
        Spared = 0;
        HallsFound = 0;
        HallsMoved = 0;
        HallsOffered = 0;
        HallsWaiting = 0;
        ShiftedOwn = 0;
        ShiftedHeld = 0;
        ShiftsFailed = 0;
        OpenTooFar = 0;
        Stood = 0;
        SeatsMoved = 0;
        SeatsWaiting = 0;
        HousesLeft = 0;
        AbodesAbroad = 0;
        _owed.Clear();
        _waitingSaid.Clear();
        _looked = 0;
    }
}
