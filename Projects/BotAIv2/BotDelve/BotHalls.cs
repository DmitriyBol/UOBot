using System;
using System.Collections.Generic;
using System.IO;
using Server.Json;
using Server.Logging;

namespace Server.BotAI.V2;

/// <summary>
/// Which rooms of a dungeon can actually be walked between, found by walking between them.
///
/// <para>
/// <b>Patrick's order of 11.09.2026: send Argus down and have him map the passable ground, because the bots
/// are supposed to be able to get round obstacles.</b> They are, and they do — the planner routes round a
/// wall perfectly well. What it cannot do is route to somewhere with no route at all, and a dungeon is full
/// of those: a cavern is a handful of separate caves that share a name, and two rooms fifteen tiles apart
/// can have solid rock between them. The party was being sent to a room, refused, sent to another, refused,
/// and the only thing that ended it was a counter running out.
/// </para>
///
/// <para>
/// <b>So this is a connectivity map, and it is measured rather than reasoned about.</b> Each room is probed
/// against one representative of each hall already found, with the shard's own path search — the same one
/// the bots use, with the same rules about diagonals and items and doors — and joins the first hall that
/// answers yes, or starts a hall of its own. What comes out is, per dungeon, the groups of rooms that are
/// mutually reachable. The party then only ever picks a room out of the hall it is standing in.
/// </para>
///
/// <para>
/// <b>Why not a flood fill of my own.</b> Because the expansion rule is delicate — the planner's own notes
/// call getting the diagonal flank rule half-right the most expensive mistake in the first version's
/// pathfinder, and the item half of it is what this world is built out of. A second copy of that logic
/// would be a second thing to get wrong, and it would answer a slightly different question than the one the
/// bots will actually ask. One search per pair is more expensive and it is the real answer.
/// </para>
///
/// <para>
/// <b>One probe at a time, on Argus's own beat.</b> This is heavy work that needs the world, so by the
/// shard's rule it is chunked across ticks rather than threaded: a search every <see cref="EveryMs"/>, each
/// bounded by <see cref="CeilingMs"/>. A dungeon of eighty rooms in three halls costs a couple of hundred
/// searches, which is a few minutes of a watcher who has nothing else to do with those milliseconds.
/// </para>
///
/// <para>
/// <b>And written down, because it is a fact about the map rather than about the population.</b> The ore
/// survey is rebuilt every start and that is right — veins run out. Rock does not move. The finished map
/// goes into <c>Configuration/bot-dungeon-halls.json</c> and is read back at boot, so the survey happens
/// once ever rather than once a restart.
/// </para>
/// </summary>
public static class BotHalls
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotHalls));

    private const string ConfigPath = "Configuration/bot-dungeon-halls.json";

    public static bool Running { get; set; } = true;

    public static int EveryMs { get; set; } = 1000;

    public static int Probes { get; set; } = 2;

    public static double CeilingMs { get; set; } = 30.0;

    public static int Spread { get; set; } = 3;

    public static int Within { get; set; } = 2;

    public static long Asked { get; private set; }

    public static long Joined { get; private set; }

    public static long Opened { get; private set; }

    public static long Footless { get; private set; }

    public static long Remembered { get; private set; }

    public static Point3D Probing { get; private set; }

    public static string Charting { get; private set; }

    public static Action<string> Noted { get; set; }

    /// <summary>One group of rooms that can all be walked between.</summary>
    public sealed class Hall
    {
        public Point3D Rep;

        public readonly List<Point3D> Rooms = [];
    }

    private sealed class Chart
    {
        public BotDungeon.Deep Deep;

        public readonly List<Hall> Halls = [];

        public int Next;

        public int Trying;

        public Point3D Footing;

        public bool Done;

        public long StartedTick;
    }

    private static readonly List<Chart> _charts = [];

    private static int _turn;

    private static long _beatTick;

    public static bool Charted
    {
        get
        {
            if (_charts.Count == 0)
            {
                return false;
            }

            for (var i = 0; i < _charts.Count; i++)
            {
                if (!_charts[i].Done)
                {
                    return false;
                }
            }

            return true;
        }
    }

    public static void Beat(Map map, long now)
    {
        if (!Running || map == null || map == Map.Internal || !BotDungeon.Surveyed)
        {
            return;
        }

        if (_charts.Count == 0)
        {
            Begin(map);
        }

        if (now - (_beatTick + EveryMs) < 0)
        {
            return;
        }

        _beatTick = now;

        for (var i = 0; i < Probes; i++)
        {
            if (!Step(map))
            {
                return;
            }
        }
    }

    private static void Begin(Map map)
    {
        var known = Read();
        var deeps = BotDungeon.All;

        for (var i = 0; i < deeps.Count; i++)
        {
            var deep = deeps[i];

            if (deep.Rooms.Count == 0)
            {
                continue;
            }

            var chart = new Chart { Deep = deep, StartedTick = Core.TickCount };

            if (known != null && Restore(chart, known))
            {
                chart.Done = true;
                chart.Next = deep.Rooms.Count;
                Remembered++;
            }

            _charts.Add(chart);
        }

        logger.Information(
            "The dungeons are being charted: {Deeps} of them, {Remembered} read back off the file, one probe every {Every}ms",
            _charts.Count,
            Remembered,
            EveryMs
        );
    }

    private static bool Step(Map map)
    {
        var chart = Pending();

        if (chart == null)
        {
            return false;
        }

        var rooms = chart.Deep.Rooms;

        if (chart.Next >= rooms.Count)
        {
            Finish(chart);

            return false;
        }

        Charting = chart.Deep.Name;

        if (chart.Footing == Point3D.Zero)
        {
            chart.Footing = Footed(map, rooms[chart.Next]);

            if (chart.Footing == Point3D.Zero)
            {
                Footless++;
                chart.Next++;
                chart.Trying = 0;

                return true;
            }
        }

        Probing = chart.Footing;

        if (chart.Trying < chart.Halls.Count)
        {
            var hall = chart.Halls[chart.Trying];

            Asked++;

            if (BotPath.CanReach(map, hall.Rep, chart.Footing, BotArrival.Within(Within), CeilingMs))
            {
                Joined++;
                hall.Rooms.Add(chart.Footing);

                chart.Next++;
                chart.Trying = 0;
                chart.Footing = Point3D.Zero;

                return true;
            }

            chart.Trying++;

            return true;
        }

        var opened = new Hall { Rep = chart.Footing };
        opened.Rooms.Add(chart.Footing);
        chart.Halls.Add(opened);
        Opened++;

        chart.Next++;
        chart.Trying = 0;
        chart.Footing = Point3D.Zero;

        return true;
    }

    private static Chart Pending()
    {
        for (var i = 0; i < _charts.Count; i++)
        {
            var chart = _charts[(_turn + i) % _charts.Count];

            if (!chart.Done)
            {
                _turn = (_turn + i) % _charts.Count;

                return chart;
            }
        }

        return null;
    }

    private static void Finish(Chart chart)
    {
        chart.Done = true;
        Probing = Point3D.Zero;
        Charting = null;

        var biggest = 0;

        for (var i = 0; i < chart.Halls.Count; i++)
        {
            if (chart.Halls[i].Rooms.Count > biggest)
            {
                biggest = chart.Halls[i].Rooms.Count;
            }
        }

        logger.Information(
            "{Deep} is charted: {Halls} halls out of {Rooms} rooms, the largest holding {Biggest}, in {Minutes} minutes",
            chart.Deep.Name,
            chart.Halls.Count,
            chart.Deep.Rooms.Count,
            biggest,
            (Core.TickCount - chart.StartedTick) / 60000
        );

        Noted?.Invoke(
            $"charted {chart.Deep.Name}: {chart.Halls.Count} halls out of {chart.Deep.Rooms.Count} rooms, "
            + $"the largest holding {biggest}. A party may only be sent to a room in the hall it is standing in."
        );

        Save();
    }

    private static Point3D Footed(Map map, Point3D room)
    {
        for (var ring = 0; ring <= Spread; ring++)
        {
            for (var dx = -ring; dx <= ring; dx++)
            {
                for (var dy = -ring; dy <= ring; dy++)
                {
                    if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != ring)
                    {
                        continue;
                    }

                    var x = room.X + dx;
                    var y = room.Y + dy;

                    if (!BotStep.Settle(map, x, y, out var z))
                    {
                        continue;
                    }

                    if (BotStep.Mask(map, x, y, z).WalkMask == 0)
                    {
                        continue;
                    }

                    return new Point3D(x, y, z);
                }
            }
        }

        return Point3D.Zero;
    }

    public static Hall Holding(BotDungeon.Deep deep, Point3D where)
    {
        var chart = Of(deep);

        if (chart == null)
        {
            return null;
        }

        Hall nearest = null;
        var closest = int.MaxValue;

        for (var i = 0; i < chart.Halls.Count; i++)
        {
            var hall = chart.Halls[i];

            for (var r = 0; r < hall.Rooms.Count; r++)
            {
                var apart = Math.Max(Math.Abs(hall.Rooms[r].X - where.X), Math.Abs(hall.Rooms[r].Y - where.Y));

                if (apart >= closest)
                {
                    continue;
                }

                closest = apart;
                nearest = hall;
            }
        }

        return nearest;
    }

    public static Hall Largest(BotDungeon.Deep deep)
    {
        var chart = Of(deep);

        if (chart == null)
        {
            return null;
        }

        Hall best = null;

        for (var i = 0; i < chart.Halls.Count; i++)
        {
            if (best == null || chart.Halls[i].Rooms.Count > best.Rooms.Count)
            {
                best = chart.Halls[i];
            }
        }

        return best;
    }

    public static Point3D Room(Hall hall, int which) =>
        hall == null || hall.Rooms.Count == 0
            ? Point3D.Zero
            : hall.Rooms[Math.Abs(which) % hall.Rooms.Count];

    public static bool Ready(BotDungeon.Deep deep) => Of(deep) is { Done: true, Halls.Count: > 0 };

    private static Chart Of(BotDungeon.Deep deep)
    {
        if (deep == null)
        {
            return null;
        }

        for (var i = 0; i < _charts.Count; i++)
        {
            if (ReferenceEquals(_charts[i].Deep, deep))
            {
                return _charts[i];
            }
        }

        return null;
    }

    public static string Describe()
    {
        if (!Running)
        {
            return "the dungeons are not being charted";
        }

        if (_charts.Count == 0)
        {
            return "the dungeons have not been charted";
        }

        var done = 0;
        var halls = 0;
        var placed = 0;
        var all = 0;

        for (var i = 0; i < _charts.Count; i++)
        {
            var chart = _charts[i];

            if (chart.Done)
            {
                done++;
            }

            halls += chart.Halls.Count;
            placed += chart.Next;
            all += chart.Deep.Rooms.Count;
        }

        return $"{done} of {_charts.Count} dungeons charted ({Remembered} read back off the file), "
            + $"{placed} of {all} rooms placed into {halls} halls: {Asked} probes, {Joined} found a way, "
            + $"{Opened} opened a hall of their own, {Footless} rooms will take no body at all";
    }

    public sealed class HallsFile
    {
        public DeepChart[] Deeps { get; set; }
    }

    public sealed class DeepChart
    {
        public string Name { get; set; }

        public int Rooms { get; set; }

        public int[][] Halls { get; set; }
    }

    private static HallsFile Read()
    {
        try
        {
            return JsonConfig.Deserialize<HallsFile>(Path.Combine(Core.BaseDirectory, ConfigPath));
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static bool Restore(Chart chart, HallsFile file)
    {
        if (file.Deeps == null)
        {
            return false;
        }

        for (var i = 0; i < file.Deeps.Length; i++)
        {
            var saved = file.Deeps[i];

            if (saved?.Halls == null || !string.Equals(saved.Name, chart.Deep.Name, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (saved.Rooms != chart.Deep.Rooms.Count)
            {
                return false;
            }

            for (var h = 0; h < saved.Halls.Length; h++)
            {
                var flat = saved.Halls[h];

                if (flat == null || flat.Length < 3)
                {
                    continue;
                }

                var hall = new Hall();

                for (var p = 0; p + 2 < flat.Length; p += 3)
                {
                    hall.Rooms.Add(new Point3D(flat[p], flat[p + 1], flat[p + 2]));
                }

                if (hall.Rooms.Count > 0)
                {
                    hall.Rep = hall.Rooms[0];
                    chart.Halls.Add(hall);
                }
            }

            return chart.Halls.Count > 0;
        }

        return false;
    }

    private static void Save()
    {
        List<DeepChart> deeps = [];

        for (var i = 0; i < _charts.Count; i++)
        {
            var chart = _charts[i];

            if (!chart.Done || chart.Halls.Count == 0)
            {
                continue;
            }

            var halls = new int[chart.Halls.Count][];

            for (var h = 0; h < chart.Halls.Count; h++)
            {
                var rooms = chart.Halls[h].Rooms;
                var flat = new int[rooms.Count * 3];

                for (var r = 0; r < rooms.Count; r++)
                {
                    flat[r * 3] = rooms[r].X;
                    flat[r * 3 + 1] = rooms[r].Y;
                    flat[r * 3 + 2] = rooms[r].Z;
                }

                halls[h] = flat;
            }

            deeps.Add(new DeepChart { Name = chart.Deep.Name, Rooms = chart.Deep.Rooms.Count, Halls = halls });
        }

        if (deeps.Count == 0)
        {
            return;
        }

        try
        {
            JsonConfig.Serialize(Path.Combine(Core.BaseDirectory, ConfigPath), new HallsFile { Deeps = [.. deeps] });
        }
        catch (Exception e)
        {
            logger.Warning(e, "The dungeon chart could not be written to {Path}; it will be walked again next start", ConfigPath);
        }
    }

    public static void Forget()
    {
        _charts.Clear();
        _turn = 0;
        _beatTick = 0;
        Asked = 0;
        Joined = 0;
        Opened = 0;
        Footless = 0;
        Remembered = 0;
        Probing = Point3D.Zero;
        Charting = null;
    }
}
