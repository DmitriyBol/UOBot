using System;
using System.Collections.Generic;
using Server.Logging;

namespace Server.BotAI.V2;

public static class BotUnreached
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotUnreached));

    public static bool Running { get; set; } = true;

    public static int RestMs { get; set; } = 1800000;

    public static int MostRestMs { get; set; } = 7200000;

    public static int Grain { get; set; } = 512;

    public static int MostKept { get; set; } = 1024;

    public static long Marked { get; private set; }

    public static long Refused { get; private set; }

    public static long ByBuyer { get; private set; }

    public static long Cleared { get; private set; }

    public static long Surfaced { get; private set; }

    private sealed class Row
    {
        public int Fails;

        public long Until;

        public long MarkedTick;
    }

    public static int SameFailureMs { get; set; } = 60000;

    private static readonly Dictionary<(int Map, string Deep, int X, int Y), Row> _ground = [];

    private static readonly Dictionary<Serial, Row> _buyers = [];

    public static bool Deep(Point3D at) => at.X >= BotGates.Edge;

    private static (int Map, string Deep, int X, int Y) Key(Map map, Point3D at) =>
        BotDungeon.Holding(at) is { } deep ? (map.MapID, deep.Name, 0, 0) : (map.MapID, null, at.X / Math.Max(16, Grain), at.Y / Math.Max(16, Grain));

    public static void NoWay(Map map, Mobile buyer, Point3D setOut, Mobile walker)
    {
        if (!Running || map == null || map == Map.Internal || buyer is not { Deleted: false } || !Deep(buyer.Location) || Deep(setOut))
        {
            return;
        }

        if (walker is BotMobile { LastWalk: BotWalkResult.Stalled })
        {
            return;
        }

        if (walker is { Deleted: false } && Deep(walker.Location))
        {
            var land = BotGates.LandOf(map, buyer.Location);

            if (Key(map, walker.Location) == Key(map, buyer.Location) || land >= 0 && land == BotGates.LandOf(map, walker.Location))
            {
                return;
            }
        }

        var now = Core.TickCount;
        var key = Key(map, buyer.Location);
        var rest = Mark(_ground, key, now);

        Mark(_buyers, buyer.Serial, now);
        Marked++;

        logger.Information(
            "No way was found to {Buyer} at {Where} in {Ground}: carriers and traders leave the want for {Minutes} minutes, or until it comes up or something is handed over there",
            buyer.Name,
            buyer.Location,
            key.Deep ?? $"the dungeon block's square at ({key.X * Math.Max(16, Grain)}, {key.Y * Math.Max(16, Grain)})",
            rest / 60000
        );
    }

    private static long Mark<TKey>(Dictionary<TKey, Row> rows, TKey key, long now)
    {
        if (!rows.TryGetValue(key, out var row))
        {
            if (rows.Count >= Math.Max(16, MostKept))
            {
                rows.Clear();
            }

            row = new Row();
            rows[key] = row;
        }

        if (row.Fails > 0 && now - row.Until >= Math.Max(1000L, MostRestMs))
        {
            row.Fails = 0;
        }

        if (row.Fails == 0 || now - row.MarkedTick >= SameFailureMs)
        {
            row.Fails = Math.Min(row.Fails + 1, 16);
        }

        row.MarkedTick = now;

        var first = Math.Max(1000L, RestMs);
        var rest = Math.Min(first << (row.Fails - 1), Math.Max(first, MostRestMs));

        row.Until = now + rest;

        return rest;
    }

    public static bool Refuses(Map map, Mobile buyer, Point3D from)
    {
        if (!Running || map == null || buyer == null || _ground.Count == 0 && _buyers.Count == 0)
        {
            return false;
        }

        if (!Deep(buyer.Location))
        {
            if (_buyers.Remove(buyer.Serial))
            {
                Surfaced++;
            }

            return false;
        }

        if (Deep(from) && !Shut(map, from, buyer.Location))
        {
            return false;
        }

        var now = Core.TickCount;

        if (_buyers.TryGetValue(buyer.Serial, out var mine) && now - mine.Until < 0)
        {
            Refused++;
            ByBuyer++;

            return true;
        }

        if (_ground.TryGetValue(Key(map, buyer.Location), out var row) && now - row.Until < 0)
        {
            Refused++;

            return true;
        }

        return false;
    }

    public static void Reached(Map map, Mobile buyer, Point3D from)
    {
        if (map == null || buyer == null || !Deep(buyer.Location) || Deep(from) || from == Point3D.Zero)
        {
            return;
        }

        var cleared = _ground.Remove(Key(map, buyer.Location));

        cleared |= _buyers.Remove(buyer.Serial);

        if (cleared)
        {
            Cleared++;
        }
    }

    public static bool Shut(Map map, Point3D from, Point3D to)
    {

        if (!Running || map == null || map == Map.Internal || !Deep(from) || !BotGates.Ready)
        {
            return false;
        }

        var a = FromLand(map, from);

        return a < 0 || a != BotGates.LandOf(map, to);
    }

    private static int _fromMap = -1;

    private static Point3D _from;

    private static int _fromLand;

    private static long _fromTick;

    private static int FromLand(Map map, Point3D from)
    {
        var now = Core.TickCount;

        if (map.MapID != _fromMap || from != _from || now - _fromTick >= 2000)
        {
            _fromMap = map.MapID;
            _from = from;
            _fromLand = BotGates.LandOf(map, from);
            _fromTick = now;
        }

        return _fromLand;
    }

    private static (int Buyers, int Grounds) Resting()
    {
        var now = Core.TickCount;
        var buyers = 0;
        var grounds = 0;

        foreach (var row in _buyers.Values)
        {
            if (now - row.Until < 0)
            {
                buyers++;
            }
        }

        foreach (var row in _ground.Values)
        {
            if (now - row.Until < 0)
            {
                grounds++;
            }
        }

        return (buyers, grounds);
    }

    public static string Describe()
    {
        var (buyers, grounds) = Resting();

        return $"the dungeon block: {Marked} walks and hauls from outside found no way to a buyer down there and marked it and its ground for {RestMs / 60000}–{MostRestMs / 60000} minutes ({buyers} buyers and {grounds} grounds resting now), {Refused} looks passed such a want over ({ByBuyer} of them by the buyer's own mark), {Cleared} marks cleared by something handed over there from outside, {Surfaced} buyers' marks dropped for coming up";
    }

    public static void Forget()
    {
        _ground.Clear();
        _buyers.Clear();
        Marked = 0;
        Refused = 0;
        ByBuyer = 0;
        Cleared = 0;
        Surfaced = 0;
        _fromMap = -1;
    }
}
