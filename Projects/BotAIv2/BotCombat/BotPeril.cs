using System;
using System.Collections.Generic;
using Server.Logging;

namespace Server.BotAI.V2;

/// <summary>
/// Where the shard is dangerous, learned from the only two facts that actually say so: where bots are being
/// hit, and where they are dying.
///
/// <para>
/// <b>Everything else about danger on this shard is a private opinion, and that is why this exists.</b>
/// <see cref="BotLedger"/> already remembers that a piece of work went badly in a place — but it lives on
/// one bot, it is keyed by trade as well as by ground, and it is a record of <em>disappointment</em> rather
/// than of harm. Fifteen bots each learning separately that the same field kills people is fifteen bots
/// learning it fifteen times, none of them able to tell anybody, and a captain deciding where to take a
/// company has to read a fact about the island rather than a mood of its own. So this is deliberately
/// shard-wide, deliberately not keyed by trade, and deliberately fed from the two hooks that cannot lie:
/// <c>OnDamage</c> and <c>OnDeath</c>.
/// </para>
///
/// <para>
/// <b>A frequency, never a total, and the difference decides everything the captain does.</b> A tally that
/// only rises names the graveyard for ever, because the graveyard has always been the worst place on the
/// map and always will be — which is a fact about history and not about tonight. What is wanted is where
/// blood is being spilt <em>lately</em>, so every reading decays towards nothing on its own clock: a square
/// that has gone quiet for an hour stops being the answer without anybody having to clear it, and a square
/// that has just killed two bots outranks one that killed ten yesterday.
/// </para>
///
/// <para>
/// <b>Squares rather than points, because a patrol cannot stand on a coordinate.</b> Harm arrives at exact
/// tiles and is useless at that resolution — thirty single blows scattered over a wood are one dangerous
/// wood, and a map keyed by tile would report thirty places each worth nothing. <see cref="Side"/> is the
/// one number that decides what "a place" means here.
/// </para>
/// </summary>
public static class BotPeril
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotPeril));

    public static int Side { get; set; } = 24;

    public static int HalfLifeMs { get; set; } = 2400000;

    public static double PerBlow { get; set; } = 1.0;

    public static double PerDeath { get; set; } = 25.0;

    public static double Worrying { get; set; } = 12.0;

    public static double SweptTo { get; set; } = 0.25;

    public static int MostSquares { get; set; } = 256;

    public static int BaulkMs { get; set; } = 600000;

    public static int Tries { get; set; } = 5;

    private sealed class Square
    {
        public Map Map;

        public int X;

        public int Y;

        public double Reading;

        public long Tick;

        public int Blows;

        public int Deaths;

        public double Dying;

        public long DyingTick;

        public bool HasDied;

        public bool Baulked;

        public long BaulkedTick;

        public bool Sounded;

        public bool Standable;

        public Point3D Foot;
    }

    private static readonly Dictionary<(int Map, int X, int Y), Square> _squares = [];

    private static readonly HashSet<(int Map, int X, int Y)> _passed = [];

    public static long Blows { get; private set; }

    public static long Deaths { get; private set; }

    public static long Sweeps { get; private set; }

    public static long Clearances { get; private set; }

    public static long Baulks { get; private set; }

    public static long Unfooted { get; private set; }

    public static void Struck(Map map, Point3D where)
    {
        Blows++;

        Add(map, where, PerBlow, blow: true);
    }

    public static void Fell(Map map, Point3D where)
    {
        Deaths++;

        Add(map, where, PerDeath, blow: false);
    }

    private static void Add(Map map, Point3D where, double weight, bool blow)
    {
        if (map == null || map == Map.Internal || weight <= 0.0)
        {
            return;
        }

        var key = Key(map, where);
        var now = Core.TickCount;

        if (!_squares.TryGetValue(key, out var square))
        {
            if (_squares.Count >= MostSquares)
            {
                Forget(now);
            }

            square = new Square { Map = map, X = key.X, Y = key.Y, Tick = now };
            _squares[key] = square;
        }

        square.Reading = Faded(square, now) + weight;
        square.Tick = now;

        if (blow)
        {
            square.Blows++;
        }
        else
        {
            square.Deaths++;

            square.Dying = DeadLately(square, now) + 1.0;
            square.DyingTick = now;
            square.HasDied = true;
        }
    }

    private static double DeadLately(Square square, long now)
    {
        if (!square.HasDied)
        {
            return 0.0;
        }

        var since = now - square.DyingTick;

        return since <= 0 ? square.Dying : square.Dying * Math.Pow(0.5, since / (double)HalfLifeMs);
    }

    public static Point3D Worst(Map map, Point3D from, int within, out double reading) =>
        Worst(map, from, within, out reading, null);

    public static Point3D Worst(Map map, Point3D from, int within, out double reading, Func<Point3D, bool> fit)
    {
        reading = 0.0;

        if (map == null || map == Map.Internal)
        {
            return Point3D.Zero;
        }

        var now = Core.TickCount;

        _passed.Clear();

        for (var tries = 0; tries < Tries; tries++)
        {
            (int Map, int X, int Y) at = default;
            Square worst = null;
            var highest = 0.0;

            foreach (var (key, square) in _squares)
            {
                if (square.Map != map || _passed.Contains(key))
                {
                    continue;
                }

                var faded = Faded(square, now);

                if (faded < Worrying || faded <= highest)
                {
                    continue;
                }

                var middle = Middle(square);

                if (Math.Max(Math.Abs(middle.X - from.X), Math.Abs(middle.Y - from.Y)) > within)
                {
                    continue;
                }

                highest = faded;
                worst = square;
                at = key;
            }

            if (worst == null)
            {
                return Point3D.Zero;
            }

            _passed.Add(at);

            if (worst.Baulked && now - worst.BaulkedTick < BaulkMs)
            {
                continue;
            }

            if (!Footing(map, worst))
            {
                continue;
            }

            var footed = worst.Foot;

            if (fit != null && !fit(footed))
            {
                continue;
            }

            reading = highest;

            return footed;
        }

        return Point3D.Zero;
    }

    public static int Deadly { get; set; } = 1;

    public static Point3D Deadliest(
        Map map,
        Point3D from,
        int within,
        int spread,
        out int deaths,
        Func<Point3D, bool> fit
    )
    {
        deaths = 0;

        if (map == null || map == Map.Internal)
        {
            return Point3D.Zero;
        }

        var now = Core.TickCount;

        _passed.Clear();

        for (var tries = 0; tries < Tries; tries++)
        {
            (int Map, int X, int Y) at = default;
            Square worst = null;
            var most = Deadly - 1;

            foreach (var (key, square) in _squares)
            {
                if (square.Map != map || square.Deaths <= 0 || _passed.Contains(key))
                {
                    continue;
                }

                var middle = Middle(square);

                if (Math.Max(Math.Abs(middle.X - from.X), Math.Abs(middle.Y - from.Y)) > within)
                {
                    continue;
                }

                var total = Around(map, middle, spread);

                if (total <= most)
                {
                    continue;
                }

                most = total;
                worst = square;
                at = key;
            }

            if (worst == null)
            {
                return Point3D.Zero;
            }

            _passed.Add(at);

            if (worst.Baulked && now - worst.BaulkedTick < BaulkMs)
            {
                continue;
            }

            if (!Footing(map, worst))
            {
                continue;
            }

            var footed = worst.Foot;

            if (fit != null && !fit(footed))
            {
                continue;
            }

            deaths = most;

            return footed;
        }

        return Point3D.Zero;
    }

    private static int Around(Map map, Point3D middle, int spread)
    {
        var total = 0;

        foreach (var square in _squares.Values)
        {
            if (square.Map != map || square.Deaths <= 0)
            {
                continue;
            }

            var at = Middle(square);

            if (Math.Max(Math.Abs(at.X - middle.X), Math.Abs(at.Y - middle.Y)) <= spread)
            {
                total += square.Deaths;
            }
        }

        return total;
    }

    public static int Unavenged(Map map, Point3D from, int within)
    {
        if (map == null || map == Map.Internal)
        {
            return 0;
        }

        var total = 0;

        foreach (var square in _squares.Values)
        {
            if (square.Map != map || square.Deaths <= 0)
            {
                continue;
            }

            var middle = Middle(square);

            if (Math.Max(Math.Abs(middle.X - from.X), Math.Abs(middle.Y - from.Y)) <= within)
            {
                total += square.Deaths;
            }
        }

        return total;
    }

    public static int Cleared(Map map, Point3D where, int spread)
    {
        if (map == null || map == Map.Internal)
        {
            return 0;
        }

        _going.Clear();

        var blows = 0;
        var dead = 0;

        foreach (var (key, square) in _squares)
        {
            if (square.Map != map)
            {
                continue;
            }

            var middle = Middle(square);

            if (Math.Max(Math.Abs(middle.X - where.X), Math.Abs(middle.Y - where.Y)) > spread)
            {
                continue;
            }

            _going.Add(key);
            blows += square.Blows;
            dead += square.Deaths;
        }

        if (_going.Count == 0)
        {
            return 0;
        }

        for (var i = 0; i < _going.Count; i++)
        {
            _squares.Remove(_going[i]);
        }

        Clearances += _going.Count;

        logger.Information(
            "The ground around {Where} has been harrowed: {Count} squares off the board altogether, carrying {Blows} blows and {Deaths} deaths between them",
            where,
            _going.Count,
            blows,
            dead
        );

        return _going.Count;
    }

    private static readonly List<(int Map, int X, int Y)> _going = [];

    public static void Swept(Map map, Point3D where)
    {
        if (map == null || !_squares.TryGetValue(Key(map, where), out var square))
        {
            return;
        }

        Sweeps++;

        var was = Faded(square, Core.TickCount);

        square.Reading = was * SweptTo;
        square.Tick = Core.TickCount;

        square.Baulked = false;

        logger.Information(
            "The square around {Where} has been swept: it read {Was:F0} and now reads {Now:F0}, on {Blows} blows and {Deaths} deaths",
            where,
            was,
            square.Reading,
            square.Blows,
            square.Deaths
        );
    }

    public static void Baulked(Map map, Point3D where)
    {
        if (map == null || !_squares.TryGetValue(Key(map, where), out var square))
        {
            return;
        }

        Baulks++;

        square.Baulked = true;
        square.BaulkedTick = Core.TickCount;

        logger.Information(
            "The square around {Where} could not be worked and is off the captain's list for {For} minutes: it still reads {Now:F0}, on {Blows} blows and {Deaths} deaths",
            where,
            BaulkMs / 60000,
            Faded(square, Core.TickCount),
            square.Blows,
            square.Deaths
        );
    }

    public static double Reading(Map map, Point3D where) =>
        map != null && _squares.TryGetValue(Key(map, where), out var square) ? Faded(square, Core.TickCount) : 0.0;

    public static bool KeepsOut { get; set; } = true;

    public static double KeepOutDeaths { get; set; } = 2.0;

    public static long KeptOut { get; private set; }

    public static bool Lethal(Map map, Point3D where, Point3D from, out double dead)
    {
        dead = KeepsOut ? DeadAround(map, where, from) : 0.0;

        if (dead < KeepOutDeaths)
        {
            return false;
        }

        KeptOut++;

        return true;
    }

    public static double CloseDeaths { get; set; } = 3.0;

    public static long Closed { get; private set; }

    public static bool Closes(Map map, Point3D where, Point3D from, out double dead)
    {
        dead = KeepsOut ? DeadAround(map, where, from) : 0.0;

        if (dead < CloseDeaths)
        {
            return false;
        }

        Closed++;

        return true;
    }

    private static double DeadAround(Map map, Point3D where, Point3D from)
    {
        if (map == null || map == Map.Internal || _squares.Count == 0)
        {
            return 0.0;
        }

        var goal = Key(map, where);

        if (goal == Key(map, from))
        {
            return 0.0;
        }

        var now = Core.TickCount;
        var dead = 0.0;

        for (var dx = -1; dx <= 1; dx++)
        {
            for (var dy = -1; dy <= 1; dy++)
            {
                if (_squares.TryGetValue((goal.Map, goal.X + dx, goal.Y + dy), out var square))
                {
                    dead += DeadLately(square, now);
                }
            }
        }

        return dead;
    }

    private static bool Footing(Map map, Square square)
    {
        if (square.Sounded)
        {
            return square.Standable;
        }

        square.Sounded = true;

        var middle = Middle(square);
        var quarter = Math.Max(1, Side / 4);
        var third = Math.Max(2, Side / 3);

        ReadOnlySpan<(int X, int Y)> offsets =
        [
            (0, 0),
            (-quarter, -quarter),
            (quarter, -quarter),
            (quarter, quarter),
            (-quarter, quarter),
            (-third, 0),
            (third, 0),
            (0, -third),
            (0, third)
        ];

        foreach (var (dx, dy) in offsets)
        {
            var x = middle.X + dx;
            var y = middle.Y + dy;

            if (!BotStep.Settle(map, x, y, out var z))
            {
                continue;
            }

            square.Standable = true;
            square.Foot = new Point3D(x, y, z);

            return true;
        }

        Unfooted++;

        logger.Information(
            "Nowhere in the square around ({X}, {Y}) can be stood on, so nobody will ever be sent there: {Blows} blows and {Deaths} deaths happened in it",
            middle.X,
            middle.Y,
            square.Blows,
            square.Deaths
        );

        return false;
    }

    private static double Faded(Square square, long now)
    {
        var since = now - square.Tick;

        if (since <= 0 || square.Reading <= 0.0)
        {
            return square.Reading;
        }

        return square.Reading * Math.Pow(0.5, since / (double)HalfLifeMs);
    }

    private static void Forget(long now)
    {
        (int Map, int X, int Y) calmest = default;
        var lowest = double.MaxValue;
        var found = false;

        foreach (var (key, square) in _squares)
        {
            var faded = Faded(square, now);

            if (faded < lowest)
            {
                lowest = faded;
                calmest = key;
                found = true;
            }
        }

        if (found)
        {
            _squares.Remove(calmest);
        }
    }

    private static (int Map, int X, int Y) Key(Map map, Point3D where) =>
        (map.MapID, Cell(where.X), Cell(where.Y));

    private static int Cell(int value) => (int)Math.Floor(value / (double)Side);

    private static Point3D Middle(Square square) =>
        new(square.X * Side + Side / 2, square.Y * Side + Side / 2, 0);

    public static List<(Point3D Where, double Reading, int Blows, int Deaths)> Worst(int most)
    {
        var now = Core.TickCount;

        List<(Point3D Where, double Reading, int Blows, int Deaths)> found = [];

        foreach (var square in _squares.Values)
        {
            var faded = Faded(square, now);

            if (faded <= 0.0)
            {
                continue;
            }

            found.Add((Middle(square), faded, square.Blows, square.Deaths));
        }

        found.Sort((left, right) => right.Reading.CompareTo(left.Reading));

        if (most > 0 && found.Count > most)
        {
            found.RemoveRange(most, found.Count - most);
        }

        return found;
    }

    public static string Tell(Map map, Point3D where)
    {
        if (map == null || map == Map.Internal)
        {
            return "no map to read";
        }

        var now = Core.TickCount;
        var key = Key(map, where);
        var dead = 0.0;
        var heaviest = 0.0;
        var heaviestAt = Point3D.Zero;

        for (var dx = -1; dx <= 1; dx++)
        {
            for (var dy = -1; dy <= 1; dy++)
            {
                if (!_squares.TryGetValue((key.Map, key.X + dx, key.Y + dy), out var near))
                {
                    continue;
                }

                var lately = DeadLately(near, now);
                dead += lately;

                if (lately > heaviest)
                {
                    heaviest = lately;
                    heaviestAt = Middle(near);
                }
            }
        }

        var own = _squares.TryGetValue(key, out var square)
            ? $"its square of {Side} reads {Faded(square, now):F1} on {square.Blows} blows and {square.Deaths} dead, {DeadLately(square, now):F1} of them lately"
            : $"its square of {Side} has no record since the shard came up";

        var rule = (KeepsOut, dead) switch
        {
            (false, _) => "the rules that read it are off",
            (_, var d) when d >= CloseDeaths => $"closed to every kind of work but running ({d:F1} dead lately in and round it, against {CloseDeaths:F1})",
            (_, var d) when d >= KeepOutDeaths => $"kept from ordinary work ({d:F1} dead lately in and round it, against {KeepOutDeaths:F1}) and open to fighting work and calls",
            (_, var d) => $"open to every kind of work ({d:F1} dead lately in and round it)"
        };

        var worst = heaviestAt == Point3D.Zero ? "" : $"; the most lately dead round it at {heaviestAt}, {heaviest:F1}";

        return $"({where.X}, {where.Y}): {own}; {rule}{worst}; the dead fade by half every {HalfLifeMs / 60000} minutes, and none of this survives a restart";
    }

    public static string Describe()
    {
        if (_squares.Count == 0)
        {
            return "nowhere has hurt anybody yet";
        }

        var now = Core.TickCount;
        Square worst = null;
        var reading = 0.0;

        foreach (var square in _squares.Values)
        {
            var faded = Faded(square, now);

            if (faded > reading)
            {
                reading = faded;
                worst = square;
            }
        }

        if (worst == null || reading < Worrying)
        {
            var quiet = worst == null
                ? "nothing on it reads at all"
                : $"the worst of them is around ({Middle(worst).X}, {Middle(worst).Y}) at {reading:F1} on {worst.Blows} blows and {worst.Deaths} deaths";

            return
                $"{_squares.Count} squares remembered on {Blows} blows and {Deaths} deaths; none of them reads above {Worrying:F0} now, so nowhere needs a company — {quiet}; {Sweeps} swept, {Clearances} harrowed off the board, {Baulks} given up on, {Unfooted} with nowhere in them to stand";
        }

        var middle = Middle(worst);

        return
            $"{_squares.Count} squares remembered on {Blows} blows and {Deaths} deaths; worst is around ({middle.X}, {middle.Y}) at {reading:F0} — {worst.Blows} blows and {worst.Deaths} deaths there; {Sweeps} swept, {Clearances} harrowed off the board, {Baulks} given up on, {Unfooted} with nowhere in them to stand";
    }

    public static void Forget()
    {
        _squares.Clear();
        _passed.Clear();
        Blows = 0;
        Deaths = 0;
        Sweeps = 0;
        Clearances = 0;
        Baulks = 0;
        Unfooted = 0;
    }
}
