using System;
using System.Collections.Generic;
using Server.Logging;

namespace Server.BotAI.V2;

public static class BotRefused
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotRefused));

    public static int Grain { get; set; } = 8;

    public static int RestMs { get; set; } = 120000;

    public static int MostRestMs { get; set; } = 7200000;

    public static int MostPlaces { get; set; } = 4096;

    public static long Marked { get; private set; }

    public static long Skipped { get; private set; }

    public static long Disproved { get; private set; }

    public static long Forgotten { get; private set; }

    private sealed class Row
    {
        public int Refusals;

        public long MarkedTick;

        public long TouchedTick;
    }

    private static readonly Dictionary<(int Map, int X, int Y), Row> _rows = [];

    private static (int Map, int X, int Y) Key(Map map, Point3D where) =>
        (map?.MapID ?? -1, where.X / Grain, where.Y / Grain);

    public static void Refuse(Map map, Point3D where)
    {
        if (map == null || map == Map.Internal)
        {
            return;
        }

        var now = Core.TickCount;
        var key = Key(map, where);

        if (!_rows.TryGetValue(key, out var row))
        {
            Sweep(now);

            row = new Row();
            _rows[key] = row;
        }

        if (row.Refusals < 30)
        {
            row.Refusals++;
        }

        row.MarkedTick = now;
        row.TouchedTick = now;

        Marked++;
    }

    public static bool Refusing(Map map, Point3D where)
    {

        if (BotBarred.Barred(map, where))
        {
            return true;
        }

        if (map == null || _rows.Count == 0)
        {
            return false;
        }

        if (!_rows.TryGetValue(Key(map, where), out var row) || row.Refusals <= 0)
        {
            return false;
        }

        var rest = Math.Min((long)RestMs << Math.Min(row.Refusals - 1, 20), MostRestMs);

        if (Core.TickCount - row.MarkedTick >= rest)
        {
            return false;
        }

        row.TouchedTick = Core.TickCount;
        Skipped++;

        return true;
    }

    public static void Arrived(Map map, Point3D where)
    {
        if (map == null || _rows.Count == 0)
        {
            return;
        }

        if (_rows.Remove(Key(map, where)))
        {
            Disproved++;
        }
    }

    private static void Sweep(long now)
    {
        if (_rows.Count < MostPlaces)
        {
            return;
        }

        var oldestKey = default((int Map, int X, int Y));
        var oldest = long.MaxValue;

        foreach (var (key, row) in _rows)
        {
            if (row.TouchedTick < oldest)
            {
                oldest = row.TouchedTick;
                oldestKey = key;
            }
        }

        if (_rows.Remove(oldestKey))
        {
            Forgotten++;
        }
    }

    public static string Describe() =>
        Marked == 0
            ? $"nowhere has refused anybody yet; {BotBarred.Describe()}"
            : $"{_rows.Count} squares of {Grain} tiles are resting after refusing somebody: {Marked} refusals written, {Skipped} choices steered away from them, {Disproved} cleared by somebody arriving after all, {Forgotten} dropped to stay inside {MostPlaces}; {BotBarred.Describe()}";

    public static void Forget()
    {
        _rows.Clear();
        Marked = 0;
        Skipped = 0;
        Disproved = 0;
        Forgotten = 0;

        BotBarred.Forget();
    }
}
