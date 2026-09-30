using System;
using System.Collections.Generic;

namespace Server.BotAI.V2;

/// <summary>
/// Where attacking creatures have actually been seen awake: one number per cell of four tiles, fading with time.
///
/// <para>
/// <b>Awake sightings only.</b> A creature on sleeping ground stands where the spawner put it and never moves (see
/// <see cref="BotWake"/>), so its position is a fact about now and nothing about where it goes; counting it would paint the
/// spawn points and call that a patrol. The living creatures' positions reach the build separately, from the register.
/// </para>
///
/// <para>
/// <b>Fading, and slowly.</b> A sighting weighs one, halved every <see cref="HalfLifeMinutes"/>. The decay is lazy — a cell is
/// brought up to date only when it is touched or read — so a raster of 1.8 million cells costs nothing for the ones nobody
/// has seen anything in. The cells ever touched are kept in a list so a build can collect them without walking the map.
/// </para>
/// </summary>
public static class BotZoneRaster
{
    public static int HalfLifeMinutes { get; set; } = 360;

    public static float MostWeight { get; set; } = 64f;

    public static float Forgotten { get; set; } = 0.05f;

    public static Map Map { get; private set; }

    public static int Width { get; private set; }

    public static int Height { get; private set; }

    public static long Recorded { get; private set; }

    public static int Touched => _touched.Count;

    private static float[] _weight;

    private static ushort[] _minute;

    private static readonly List<int> _touched = [];

    private static long _startTick;

    public static void Open(Map map, long now)
    {
        Map = map;
        Width = map.Width >> BotZoneState.Shift;
        Height = map.Height >> BotZoneState.Shift;
        _weight = new float[Width * Height];
        _minute = new ushort[Width * Height];
        _touched.Clear();
        _startTick = now;
        Recorded = 0;
    }

    private static ushort Stamp(long now) => (ushort)((now - _startTick) / 60000 % 65535 + 1);

    private static float Faded(float weight, ushort then, ushort now)
    {
        var minutes = (ushort)(now - then);

        return minutes == 0 ? weight : weight * MathF.Pow(0.5f, minutes / (float)Math.Max(1, HalfLifeMinutes));
    }

    public static void Record(int x, int y, long now)
    {
        if (_weight == null)
        {
            return;
        }

        var cx = x >> BotZoneState.Shift;
        var cy = y >> BotZoneState.Shift;

        if ((uint)cx >= (uint)Width || (uint)cy >= (uint)Height)
        {
            return;
        }

        var i = cy * Width + cx;
        var stamp = Stamp(now);

        if (_minute[i] == 0)
        {
            _touched.Add(i);
            _weight[i] = 1f;
        }
        else
        {
            _weight[i] = Math.Min(MostWeight, Faded(_weight[i], _minute[i], stamp) + 1f);
        }

        _minute[i] = stamp;
        Recorded++;
    }

    public static float WeightAt(int x, int y, long now)
    {
        if (_weight == null)
        {
            return 0f;
        }

        var cx = x >> BotZoneState.Shift;
        var cy = y >> BotZoneState.Shift;

        if ((uint)cx >= (uint)Width || (uint)cy >= (uint)Height)
        {
            return 0f;
        }

        var i = cy * Width + cx;

        return _minute[i] == 0 ? 0f : Faded(_weight[i], _minute[i], Stamp(now));
    }

    public static void Collect(long now, float least, List<int> cells, List<float> weights)
    {
        if (_weight == null)
        {
            return;
        }

        var stamp = Stamp(now);
        var kept = 0;

        for (var k = 0; k < _touched.Count; k++)
        {
            var i = _touched[k];
            var weight = Faded(_weight[i], _minute[i], stamp);

            if (weight < Forgotten)
            {
                _weight[i] = 0f;
                _minute[i] = 0;

                continue;
            }

            _touched[kept++] = i;

            if (weight >= least)
            {
                cells.Add(i);
                weights.Add(weight);
            }
        }

        _touched.RemoveRange(kept, _touched.Count - kept);
    }

    public static void Restore(int i, float weight, long now)
    {
        if (_weight == null || (uint)i >= (uint)_weight.Length || weight < Forgotten)
        {
            return;
        }

        if (_minute[i] == 0)
        {
            _touched.Add(i);
        }

        _weight[i] = Math.Min(MostWeight, Math.Max(_weight[i], weight));
        _minute[i] = Stamp(now);
    }

    public static void Forget()
    {
        Map = null;
        _weight = null;
        _minute = null;
        _touched.Clear();
        Recorded = 0;
    }
}
