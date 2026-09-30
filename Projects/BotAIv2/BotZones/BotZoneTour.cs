using System;
using System.Collections.Generic;
using System.Text.Json;

namespace Server.BotAI.V2;

/// <summary>
/// The debuggers' walk round the zones: each goes to the least-watched zone, wakes the ground round it the way a client would,
/// holds it awake long enough for the patrol to move, and goes on.
///
/// <para>
/// <b>Patrick's order of 30.09.2026: "walk the world with Argus and the other debuggers, look at all the mob zones".</b> The
/// world sleeps where no client is (<see cref="BotWake"/>): a creature nobody is near stands where it was spawned, so the only
/// way to learn a patrol a bot has never been near is to wake the ground there and watch. That is what the stop does:
/// <c>Sector.Activate</c> on the sectors round the zone — the five by five a client wakes, widened to the zone's own box up to
/// <see cref="MostSectors"/> — and a pass of the zone's box every <see cref="FocusMs"/> while it holds
/// (<see cref="BotZoneScan.Focus"/>), for <see cref="HoldMs"/>.
/// </para>
///
/// <para>
/// <b>Which zone next: the least watched, and the nearest among equals.</b> Zones are ranked by the seconds their ground has
/// been seen awake, in buckets of <see cref="Bucket"/> seconds, and within a bucket by distance from the walker — so a walker
/// sweeps a region of never-watched zones before crossing the map, and a zone the bots walk through every hour (already awake
/// by <see cref="BotWake"/>) waits for last. A round ends when every zone has been stood in once; the next begins at once.
/// Resumable: the awake seconds and the stops are kept across a restart (<see cref="BotZoneMemory"/>), so a new session picks
/// up with the zones the last one had not reached.
/// </para>
///
/// <para>
/// <b>It never disturbs the bots' own waking.</b> A sector is put back to sleep only if the tour woke it, no bot needs it
/// (<see cref="BotWake.Needed"/>) and no client is near; and <see cref="BotWake"/> in turn leaves alone a sector the tour holds
/// (<see cref="Holds"/>).
/// </para>
///
/// <para>
/// <b>The walkers are the debugger's, reached through two hooks.</b> The debugger lives in the minds' assembly, which references
/// this one and never the reverse; it fills <see cref="Walkers"/> (the watchers free to walk: a body, not witnessing a meeting,
/// not charting the dungeons) and <see cref="Move"/> (its own teleport) when it starts. With no debugger the tour stands still
/// and says so.
/// </para>
/// </summary>
public static class BotZoneTour
{
    public static bool Running { get; set; } = true;

    public static int TickMs { get; set; } = 1000;

    public static int HoldMs { get; set; } = 75000;

    public static int GraceMs { get; set; } = 30000;

    public static int Reach { get; set; } = 2;

    public static int MostSectors { get; set; } = 81;

    public static int MostWalkers { get; set; } = 4;

    public static int FocusMs { get; set; } = 2000;

    public static bool Water { get; set; }

    public static int Bucket { get; set; } = 60;

    public static Func<IReadOnlyList<(string Name, Mobile Body)>> Walkers { get; set; }

    public static Func<Mobile, Map, Point3D, bool> Move { get; set; }

    public static long Stops { get; private set; }

    public static long Abandoned { get; private set; }

    public static long Refused { get; private set; }

    public static int Round { get; private set; } = 1;

    public static long Woken { get; private set; }

    public static long PutBack { get; private set; }

    public static long LeftAwake { get; private set; }

    public static int Visited { get; private set; }

    public static int Total { get; private set; }

    private sealed class Stop
    {
        public string Name;

        public Mobile Body;

        public int Zone;

        public string Label;

        public Point3D At;

        public Rectangle2D Box;

        public long Began;

        public long Focused;

        public readonly List<(int X, int Y)> Sectors = [];
    }

    private struct Held
    {
        public long Until;

        public bool Woke;
    }

    private static readonly Dictionary<Mobile, Stop> _stops = [];

    private static readonly Dictionary<(int X, int Y), Held> _held = [];

    private static readonly List<(int X, int Y)> _expired = [];

    private static readonly HashSet<int> _visited = [];

    private static readonly HashSet<int> _candidates = [];

    private static readonly List<(string Name, Mobile Body)> _walking = [];

    private static readonly List<Mobile> _dropped = [];

    private static Map _map;

    public static void Open(long now)
    {
        _stops.Clear();
        _held.Clear();
        _visited.Clear();
        _walking.Clear();
        _map = null;
    }

    public static bool Holds(Map map, int x, int y) => map != null && map == _map && _held.ContainsKey((x, y));

    public static bool Adopt(Map map, int x, int y)
    {
        if (map == null || map != _map || !_held.TryGetValue((x, y), out var held))
        {
            return false;
        }

        _held[(x, y)] = new Held { Until = held.Until, Woke = true };

        return true;
    }

    public static bool Holding(Mobile body) => body != null && _stops.ContainsKey(body);

    public static bool Beat(Map map, long now)
    {
        _map = map;

        var state = BotZones.State;
        var walkers = Running ? Walkers?.Invoke() : null;

        _walking.Clear();

        if (walkers == null || Move == null || state == null || state.Map != map)
        {
            var had = _stops.Count > 0;

            EndAll();
            LetGo(map, now);

            return had;
        }

        for (var i = 0; i < walkers.Count && _walking.Count < Math.Max(0, MostWalkers); i++)
        {
            if (walkers[i].Body is { Deleted: false })
            {
                _walking.Add(walkers[i]);
            }
        }

        _dropped.Clear();

        foreach (var (body, _) in _stops)
        {
            if (!Offered(body))
            {
                _dropped.Add(body);
            }
        }

        for (var i = 0; i < _dropped.Count; i++)
        {
            _stops.Remove(_dropped[i]);
            Abandoned++;
        }

        Count(state);

        for (var i = 0; i < _walking.Count; i++)
        {
            var (name, body) = _walking[i];

            if (_stops.TryGetValue(body, out var stop))
            {
                if (now - stop.Began < HoldMs)
                {
                    Keep(map, stop, now);

                    continue;
                }

                Finish(stop, now);
                _stops.Remove(body);
            }

            Begin(map, state, name, body, now);
        }

        LetGo(map, now);

        return _walking.Count > 0 || _dropped.Count > 0;
    }

    private static bool Offered(Mobile body)
    {
        for (var i = 0; i < _walking.Count; i++)
        {
            if (_walking[i].Body == body)
            {
                return true;
            }
        }

        return false;
    }

    private static bool Eligible(BotZone zone) => zone.Level >= BotZoneLevel.Hostile && (Water || !zone.Water);

    private static void Count(BotZoneState state)
    {
        _candidates.Clear();

        for (var i = 0; i < state.Zones.Length; i++)
        {
            if (Eligible(state.Zones[i]))
            {
                _candidates.Add(state.Zones[i].Id);
            }
        }

        var visited = 0;

        foreach (var id in _visited)
        {
            if (_candidates.Contains(id))
            {
                visited++;
            }
        }

        Total = _candidates.Count;
        Visited = visited;
    }

    private static void Begin(Map map, BotZoneState state, string name, Mobile body, long now)
    {
        var zone = Pick(state, body);

        if (zone == null && Total > 0 && Visited >= Total)
        {
            Round++;
            _visited.Clear();
            Visited = 0;
            zone = Pick(state, body);
        }

        if (zone == null)
        {
            return;
        }

        var at = zone.Stand;

        if (!zone.StandIsReal)
        {
            at = new Point3D(at.X, at.Y, map.GetAverageZ(at.X, at.Y));
        }

        if (!Move(body, map, at) && (body.Map != map || body.Location != at))
        {
            Refused++;
            _visited.Add(zone.Id);

            return;
        }

        var stop = new Stop
        {
            Name = name,
            Body = body,
            Zone = zone.Id,
            Label = zone.Label,
            At = at,
            Began = now,
            Focused = now
        };

        Sectors(map, zone, at, stop.Sectors);

        int x0 = int.MaxValue, y0 = int.MaxValue, x1 = -1, y1 = -1;

        for (var i = 0; i < stop.Sectors.Count; i++)
        {
            var (sx, sy) = stop.Sectors[i];

            x0 = Math.Min(x0, sx);
            y0 = Math.Min(y0, sy);
            x1 = Math.Max(x1, sx);
            y1 = Math.Max(y1, sy);
        }

        var side = 1 << Map.SectorShift;

        stop.Box = new Rectangle2D(x0 * side, y0 * side, (x1 - x0 + 1) * side, (y1 - y0 + 1) * side);
        _stops[body] = stop;
        Keep(map, stop, now);
    }

    private static BotZone Pick(BotZoneState state, Mobile body)
    {
        BotZone best = null;
        var bestBucket = long.MaxValue;
        var bestDistance = long.MaxValue;
        var bucket = Math.Max(1, Bucket);

        for (var i = 0; i < state.Zones.Length; i++)
        {
            var zone = state.Zones[i];

            if (!Eligible(zone) || _visited.Contains(zone.Id) || Taken(zone.Id))
            {
                continue;
            }

            var b = (long)(zone.AwakeSeconds / bucket);
            var d = body.Map == state.Map ? Math.Max(Math.Abs(body.X - zone.CenterX), Math.Abs(body.Y - zone.CenterY)) : int.MaxValue;

            if (b < bestBucket || b == bestBucket && d < bestDistance)
            {
                best = zone;
                bestBucket = b;
                bestDistance = d;
            }
        }

        return best;
    }

    private static bool Taken(int id)
    {
        foreach (var (_, stop) in _stops)
        {
            if (stop.Zone == id)
            {
                return true;
            }
        }

        return false;
    }

    private static void Sectors(Map map, BotZone zone, Point3D at, List<(int X, int Y)> into)
    {
        var width = map.Width >> Map.SectorShift;
        var height = map.Height >> Map.SectorShift;
        var cx = at.X >> Map.SectorShift;
        var cy = at.Y >> Map.SectorShift;
        var x0 = Math.Min(cx - Reach, (zone.X1 - zone.AggroRange) >> Map.SectorShift);
        var y0 = Math.Min(cy - Reach, (zone.Y1 - zone.AggroRange) >> Map.SectorShift);
        var x1 = Math.Max(cx + Reach, (zone.X2 + zone.AggroRange) >> Map.SectorShift);
        var y1 = Math.Max(cy + Reach, (zone.Y2 + zone.AggroRange) >> Map.SectorShift);
        List<(int X, int Y, int D)> all = [];

        for (var y = Math.Max(0, y0); y <= Math.Min(height - 1, y1); y++)
        {
            for (var x = Math.Max(0, x0); x <= Math.Min(width - 1, x1); x++)
            {
                all.Add((x, y, Math.Max(Math.Abs(x - cx), Math.Abs(y - cy))));
            }
        }

        all.Sort((a, b) => a.D.CompareTo(b.D));

        for (var i = 0; i < all.Count && i < Math.Max(1, MostSectors); i++)
        {
            into.Add((all[i].X, all[i].Y));
        }
    }

    private static void Keep(Map map, Stop stop, long now)
    {
        var until = now + GraceMs;

        for (var i = 0; i < stop.Sectors.Count; i++)
        {
            var (x, y) = stop.Sectors[i];
            var sector = map.GetRealSector(x, y);
            var woke = false;

            if (!sector.Active)
            {
                sector.Activate();
                woke = true;
                Woken++;
            }

            _held[(x, y)] = _held.TryGetValue((x, y), out var held)
                ? new Held { Until = until, Woke = held.Woke || woke }
                : new Held { Until = until, Woke = woke };
        }

        if (now - stop.Focused >= FocusMs)
        {
            stop.Focused = now;
            BotZoneScan.Focus(map, stop.Box, now);
        }
    }

    private static void Finish(Stop stop, long now)
    {
        Stops++;
        _visited.Add(stop.Zone);

        var zone = BotZones.Find(stop.Zone);

        if (zone == null)
        {
            return;
        }

        var haunts = BotHaunts.All;
        var at = DateTime.UtcNow;

        for (var i = 0; i < haunts.Count; i++)
        {
            var haunt = haunts[i];

            if (haunt.At.X >= zone.X1 && haunt.At.X <= zone.X2 && haunt.At.Y >= zone.Y1 && haunt.At.Y <= zone.Y2)
            {
                haunt.Toured++;
                haunt.TouredAt = at;
            }
        }
    }

    private static void LetGo(Map map, long now)
    {
        if (_held.Count == 0)
        {
            return;
        }

        _expired.Clear();

        foreach (var (key, held) in _held)
        {
            if (now - held.Until >= 0)
            {
                _expired.Add(key);
            }
        }

        for (var i = 0; i < _expired.Count; i++)
        {
            var key = _expired[i];
            var held = _held[key];

            _held.Remove(key);

            if (!held.Woke || map == null)
            {
                continue;
            }

            if (BotWake.Needed(map, key.X, key.Y) || BotWake.ClientNear(map, key.X, key.Y))
            {
                LeftAwake++;

                continue;
            }

            var sector = map.GetRealSector(key.X, key.Y);

            if (sector.Active)
            {
                sector.Deactivate();
                PutBack++;
            }
        }

        _expired.Clear();
    }

    private static void EndAll()
    {
        Abandoned += _stops.Count;
        _stops.Clear();
    }

    public static void Release(long now)
    {
        EndAll();
        LetGo(_map, now);
    }

    public static void Write(Utf8JsonWriter w)
    {
        var now = Core.TickCount;

        w.WriteStartObject();
        w.WriteBoolean("running", Running && Walkers != null);
        w.WriteNumber("round", Round);
        w.WriteNumber("visited", Visited);
        w.WriteNumber("total", Total);
        w.WriteNumber("stops", Stops);
        w.WriteStartArray("walkers");

        for (var i = 0; i < _walking.Count; i++)
        {
            var (name, body) = _walking[i];

            if (body is not { Deleted: false })
            {
                continue;
            }

            w.WriteStartObject();
            w.WriteString("name", name);
            w.WriteStartArray("at");
            w.WriteNumberValue(body.X);
            w.WriteNumberValue(body.Y);
            w.WriteEndArray();

            if (_stops.TryGetValue(body, out var stop))
            {
                w.WriteStartArray("target");
                w.WriteNumberValue(stop.At.X);
                w.WriteNumberValue(stop.At.Y);
                w.WriteEndArray();
                w.WriteNumber("zone", stop.Zone);
                w.WriteString("state", $"holding {Math.Min(HoldMs, now - stop.Began) / 1000}/{HoldMs / 1000} s at zone {stop.Zone} ({stop.Label})");
            }
            else
            {
                w.WriteNull("target");
                w.WriteNull("zone");
                w.WriteString("state", Running ? "choosing the next zone" : "the tour is off");
            }

            w.WriteEndObject();
        }

        w.WriteEndArray();
        w.WriteEndObject();
    }

    public static string Describe()
    {
        var who = Walkers == null ? "no debugger to walk it" : Move == null ? "no way to move the debuggers" : $"{_walking.Count} walking";
        var now = Core.TickCount;
        var stops = new List<string>();

        foreach (var (_, stop) in _stops)
        {
            stops.Add($"{stop.Name} at zone {stop.Zone} {(now - stop.Began) / 1000}/{HoldMs / 1000}s");
        }

        return $"{(Running ? "on" : "off")}, round {Round}: {Visited} of {Total} zones stood in this round, {Stops} stops in all ({Abandoned} cut short, {Refused} refused moves); {who}{(stops.Count > 0 ? $" ({string.Join(", ", stops)})" : "")}; "
            + $"{_held.Count} sectors held now, {Woken} woken, {PutBack} put back to sleep, {LeftAwake} left awake for a bot or a client";
    }

    public static void Forget(long now)
    {
        EndAll();

        foreach (var key in new List<(int X, int Y)>(_held.Keys))
        {
            _held[key] = new Held { Until = now, Woke = _held[key].Woke };
        }

        LetGo(_map, now);
        _held.Clear();
        _visited.Clear();
        _walking.Clear();
        Stops = Abandoned = Refused = 0;
        Woken = PutBack = LeftAwake = 0;
        Round = 1;
        Visited = Total = 0;
        _map = null;
    }
}
