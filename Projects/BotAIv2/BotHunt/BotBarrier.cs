using System;
using System.Collections.Generic;

namespace Server.BotAI.V2;

public static class BotBarrier
{

    public static bool Running { get; set; } = false;

    public static int Near { get; set; } = 16;

    public static int Enough { get; set; } = 2;

    public static int KeepMs { get; set; } = 2400000;

    public static int Most { get; set; } = 128;

    public static double SameWay { get; set; } = 0.7;

    public static long Stops { get; private set; }

    public static long Behind { get; private set; }

    private readonly record struct Stop(Map Map, int X, int Y, double Dx, double Dy, long Tick);

    private static readonly List<Stop> _stops = [];

    public static void Stopped(Map map, Point3D at, Point3D toward)
    {
        if (map == null || map == Map.Internal)
        {
            return;
        }

        var dx = toward.X - at.X;
        var dy = toward.Y - at.Y;
        var length = Math.Sqrt((double)dx * dx + (double)dy * dy);

        if (length < 1.0)
        {
            return;
        }

        if (_stops.Count >= Math.Max(1, Most))
        {
            _stops.RemoveAt(0);
        }

        _stops.Add(new Stop(map, at.X, at.Y, dx / length, dy / length, Core.TickCount));
        Stops++;
    }

    public static bool Beyond(Map map, Point3D from, Point3D to)
    {
        if (!Running || map == null || _stops.Count == 0)
        {
            return false;
        }

        var dx = to.X - from.X;
        var dy = to.Y - from.Y;
        var length = Math.Sqrt((double)dx * dx + (double)dy * dy);

        if (length <= Near)
        {
            return false;
        }

        var ux = dx / length;
        var uy = dy / length;
        var now = Core.TickCount;

        for (var i = 0; i < _stops.Count; i++)
        {
            var stop = _stops[i];

            if (stop.Map != map || now - stop.Tick > KeepMs || stop.Dx * ux + stop.Dy * uy < SameWay)
            {
                continue;
            }

            var along = (stop.X - from.X) * ux + (stop.Y - from.Y) * uy;

            if (along <= 0.0 || along >= length - Near)
            {
                continue;
            }

            var across = Math.Abs((stop.X - from.X) * uy - (stop.Y - from.Y) * ux);

            if (across > Near || Neighbours(stop, now) < Enough)
            {
                continue;
            }

            Behind++;

            return true;
        }

        return false;
    }

    private static int Neighbours(Stop stop, long now)
    {
        var found = 0;

        for (var i = 0; i < _stops.Count; i++)
        {
            var other = _stops[i];

            if (other.Map != stop.Map || now - other.Tick > KeepMs || other.Dx * stop.Dx + other.Dy * stop.Dy < SameWay)
            {
                continue;
            }

            if (Math.Abs(other.X - stop.X) <= Near && Math.Abs(other.Y - stop.Y) <= Near)
            {
                found++;
            }
        }

        return found;
    }

    public static void Forget()
    {
        _stops.Clear();
        Stops = 0;
        Behind = 0;
    }
}
