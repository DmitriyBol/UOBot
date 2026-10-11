using System;
using System.Collections.Generic;

namespace Server.BotAI.V2;

/// <summary>
/// What sweeps of each square of the island paid lately: how many sweeps walked it, how many fights they found in it, and what
/// the hunts those finds led to came to. The price a prowl is offered at (<see cref="BotProwl.Expects"/>) is read from here.
///
/// <para>
/// <b>A prowl was offered at a flat eight a minute, and it won the auction for being nothing.</b> Patrick, 06.10.2026 ~10:00:
/// 82 % of the 14 000 prowls a day end "nothing here" and count as finished work; "ideally, when they go on a prowl they are
/// given several quadrants to clear; if they walked them and found nobody, that is fine, that is a success." On build 400
/// (06.10, 06:12–09:42) 2709 prowls were taken, 1761 of the 2669 that ended said "nothing here" (a median of 0.6 minutes),
/// and 274 of them were taken over a real offer. The walk itself pays nothing and never will; what it is worth is what the
/// fights it finds pay, and that is a fact about the squares it walks — so it is measured per square, from the sweeps that
/// walked them, and a square that sweeps keep finding empty prices its sweeps down towards nothing.
/// </para>
///
/// <para>
/// Faded by <see cref="HalfLifeMs"/>: a square cleared an hour ago fills again from its spawner, and a square that paid this
/// morning may be empty now. Not saved: a restart is a fresh look, and the first hour refills it.
/// </para>
/// </summary>
public static class BotSweepRecord
{
    public static int HalfLifeMs { get; set; } = 3600000;

    public static double SquarePrior { get; set; } = 1.0;

    public static double PriorWeight { get; set; } = 2.0;

    public static int CreditMs { get; set; } = 180000;

    public static int ResumeMs { get; set; } = 300000;

    public static int MostSquares { get; set; } = 8192;

    private sealed class Entry
    {
        public double Walked;

        public double Found;

        public double Gold;

        public long Tick;

        public long WaylessTick;
    }

    public static int WaylessMs { get; set; } = 3600000;

    public static long WaylessFiled { get; private set; }

    public static void Wayless(Map map, Point3D where)
    {
        if (WaylessMs <= 0)
        {
            return;
        }

        var entry = Touch(map, where, true);

        if (entry != null)
        {
            entry.WaylessTick = Core.TickCount;
            WaylessFiled++;
        }
    }

    public static bool NoWay(Map map, Point3D where)
    {
        if (WaylessMs <= 0 || map == null || map == Map.Internal)
        {
            return false;
        }

        return _squares.TryGetValue(BotQuad.Key(map, where), out var entry) && entry.WaylessTick != 0
                                                                            && Core.TickCount - entry.WaylessTick < WaylessMs;
    }

    private static readonly Dictionary<(int Map, int X, int Y), Entry> _squares = [];

    private static readonly Dictionary<Serial, (Map Map, Point3D Where, long Tick)> _finds = [];

    private static readonly Dictionary<Serial, (Map Map, Point3D[] Legs, long Tick)> _left = [];

    public static long Credited { get; private set; }

    public static long CreditedWorth { get; private set; }

    public static int Squares => _squares.Count;

    private static Entry Touch(Map map, Point3D where, bool make)
    {
        if (map == null || map == Map.Internal)
        {
            return null;
        }

        var key = BotQuad.Key(map, where);
        var now = Core.TickCount;

        if (!_squares.TryGetValue(key, out var entry))
        {
            if (!make)
            {
                return null;
            }

            if (_squares.Count >= MostSquares)
            {
                Trim(now);
            }

            entry = new Entry { Tick = now };
            _squares[key] = entry;

            return entry;
        }

        Fade(entry, now);

        return entry;
    }

    private static void Fade(Entry entry, long now)
    {
        var age = now - entry.Tick;

        if (age <= 0 || HalfLifeMs <= 0)
        {
            return;
        }

        var keep = Math.Pow(0.5, age / (double)HalfLifeMs);

        entry.Walked *= keep;
        entry.Found *= keep;
        entry.Gold *= keep;
        entry.Tick = now;
    }

    private static void Trim(long now)
    {
        List<(int Map, int X, int Y)> faint = [];

        foreach (var (key, entry) in _squares)
        {
            Fade(entry, now);

            if (entry.Walked + entry.Found < 0.25 && (entry.WaylessTick == 0 || now - entry.WaylessTick >= WaylessMs))
            {
                faint.Add(key);
            }
        }

        for (var i = 0; i < faint.Count; i++)
        {
            _squares.Remove(faint[i]);
        }

        if (_squares.Count >= MostSquares)
        {
            _squares.Clear();
        }
    }

    public static void Walked(Map map, Point3D where)
    {
        var entry = Touch(map, where, true);

        if (entry != null)
        {
            entry.Walked += 1.0;
        }
    }

    public static void Found(Mobile body, Map map, Point3D where)
    {
        var entry = Touch(map, where, true);

        if (entry == null)
        {
            return;
        }

        entry.Found += 1.0;
        entry.Walked += 1.0;

        if (body != null)
        {
            _finds[body.Serial] = (map, where, Core.TickCount);
        }
    }

    public static void Hunted(Mobile body, int worth)
    {
        if (body == null || !_finds.TryGetValue(body.Serial, out var find))
        {
            return;
        }

        _finds.Remove(body.Serial);

        if (Core.TickCount - find.Tick >= CreditMs || worth <= 0)
        {
            return;
        }

        var entry = Touch(find.Map, find.Where, true);

        if (entry == null)
        {
            return;
        }

        entry.Gold += worth;
        Credited++;
        CreditedWorth += worth;
    }

    public static double Worth(Map map, Point3D where)
    {
        var entry = Touch(map, where, false);

        if (entry == null)
        {
            return SquarePrior;
        }

        return (SquarePrior * PriorWeight + entry.Gold) / (PriorWeight + entry.Walked);
    }

    public static double Sweeps(Map map, Point3D where) => Touch(map, where, false)?.Walked ?? 0.0;

    public static void Leave(Mobile body, Map map, List<Point3D> legs, int from)
    {
        if (body == null || map == null || legs == null || from >= legs.Count)
        {
            return;
        }

        var left = new Point3D[legs.Count - from];

        for (var i = from; i < legs.Count; i++)
        {
            left[i - from] = legs[i];
        }

        _left[body.Serial] = (map, left, Core.TickCount);
    }

    public static Point3D[] Left(Mobile body, Map map)
    {
        if (body == null || !_left.TryGetValue(body.Serial, out var left))
        {
            return null;
        }

        if (left.Map != map || Core.TickCount - left.Tick >= ResumeMs)
        {
            _left.Remove(body.Serial);

            return null;
        }

        return left.Legs;
    }

    public static void Taken(Mobile body)
    {
        if (body != null)
        {
            _left.Remove(body.Serial);
        }
    }

    public static string Describe() =>
        $"{_squares.Count} squares with a record of sweeps, {Credited} hunts credited to the square a sweep found them in ({CreditedWorth} coin and goods), {WaylessFiled} squares filed as having no way to their waypoint for {WaylessMs / 60000} minutes";

    public static void Forget()
    {
        _squares.Clear();
        _finds.Clear();
        _left.Clear();
        Credited = 0;
        CreditedWorth = 0;
        WaylessFiled = 0;
    }
}
