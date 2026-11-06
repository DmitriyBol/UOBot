using System.Collections.Generic;

namespace Server.BotAI.V2;

public static class BotFooting
{

    public static int RestMs { get; set; } = 1800000;

    public static int Most { get; set; } = 2048;

    private static readonly Dictionary<(Map Map, int X, int Y), long> _tiles = [];

    public static long Noted { get; private set; }

    public static long Skipped { get; private set; }

    public static void Note(Map map, Point3D where)
    {
        if (map == null || map == Map.Internal || where == Point3D.Zero)
        {
            return;
        }

        if (_tiles.Count >= Most)
        {
            Sweep(true);
        }

        _tiles[(map, where.X, where.Y)] = Core.TickCount;
        Noted++;
    }

    public static bool Footless(Map map, int x, int y)
    {
        if (_tiles.Count == 0 || !_tiles.TryGetValue((map, x, y), out var at))
        {
            return false;
        }

        if (Core.TickCount - at >= RestMs)
        {
            _tiles.Remove((map, x, y));

            return false;
        }

        Skipped++;

        return true;
    }

    private static readonly List<(Map, int, int)> _stale = [];

    private static void Sweep(bool full)
    {
        var now = Core.TickCount;
        (Map, int, int) oldest = default;
        var oldestAt = long.MaxValue;

        foreach (var (key, at) in _tiles)
        {
            if (now - at >= RestMs)
            {
                _stale.Add(key);
            }
            else if (at < oldestAt)
            {
                oldestAt = at;
                oldest = key;
            }
        }

        for (var i = 0; i < _stale.Count; i++)
        {
            _tiles.Remove(_stale[i]);
        }

        if (_stale.Count == 0 && full && oldestAt != long.MaxValue)
        {
            _tiles.Remove(oldest);
        }

        _stale.Clear();
    }

    public static string Describe() => $"{Noted} tiles found with nowhere to stand, {_tiles.Count} written off now, {Skipped} rocks passed over for it";
}
