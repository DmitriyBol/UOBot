using System;
using System.Collections.Generic;

namespace Server.BotAI.V2;

public sealed class BotLedger
{

    public static int BandSize { get; set; } = 64;

    public static int MaxPlaces { get; set; } = 48;

    public static double PriorWeight { get; set; } = 2.0;

    public static int Confidence { get; set; } = 12;

    public static double Smoothing { get; set; } = 0.35;

    public static int SpinHalfLifeMs { get; set; } = 900000;

    public static int CautionMs { get; set; } = 300000;

    public static int MostCautionMs { get; set; } = 3600000;

    private sealed class Tally
    {
        public double Measured;

        public int Settled;

        public double Spins;

        public long TouchedTick;

        public bool Cautioned;

        public long CautiousUntil;

        public int Cautions;
    }

    private readonly Dictionary<(string Kind, int Map, int X, int Y), Tally> _tallies = [];

    public int Places => _tallies.Count;

    public void Save(IGenericWriter writer)
    {
        writer.Write(_tallies.Count);

        foreach (var (key, tally) in _tallies)
        {
            writer.Write(key.Kind ?? "");
            writer.Write(key.Map);
            writer.Write(key.X);
            writer.Write(key.Y);
            writer.Write(tally.Measured);
            writer.Write(tally.Settled);
            writer.Write(tally.Spins);
            writer.Write(tally.Cautions);
        }
    }

    public void Load(IGenericReader reader)
    {
        var many = reader.ReadInt();
        var now = Core.TickCount;

        for (var i = 0; i < many; i++)
        {
            var kind = reader.ReadString();
            var map = reader.ReadInt();
            var x = reader.ReadInt();
            var y = reader.ReadInt();

            var tally = new Tally
            {
                Measured = reader.ReadDouble(),
                Settled = reader.ReadInt(),
                Spins = reader.ReadDouble(),
                Cautions = reader.ReadInt(),
                TouchedTick = now
            };

            if (!string.IsNullOrEmpty(kind) && _tallies.Count < MaxPlaces)
            {
                _tallies[(kind, map, x, y)] = tally;
            }
        }
    }

    public double Expect(string kind, Map map, Point3D where, double prior)
    {
        if (prior <= 0.0)
        {
            return 0.0;
        }

        if (!_tallies.TryGetValue(Key(kind, map, where), out var tally) || tally.Settled == 0)
        {

            return BotCommons.Expect(kind, map, where, prior);
        }

        var settled = Math.Min(tally.Settled, Confidence);

        return (prior * PriorWeight + tally.Measured * settled) / (PriorWeight + settled);
    }

    public double Spins(string kind, Map map, Point3D where) =>
        _tallies.TryGetValue(Key(kind, map, where), out var tally) ? Faded(tally, Core.TickCount) : 0.0;

    public bool Cautious(string kind, Map map, Point3D where) =>
        _tallies.TryGetValue(Key(kind, map, where), out var tally)
        && tally.Cautioned
        && Core.TickCount - tally.CautiousUntil < 0;

    public void Note(string kind, Map map, Point3D where, double perMinute)
    {
        var now = Core.TickCount;
        var tally = Row(kind, map, where, now);

        tally.Measured = tally.Settled == 0
            ? perMinute
            : tally.Measured + (perMinute - tally.Measured) * Smoothing;

        if (tally.Settled < int.MaxValue)
        {
            tally.Settled++;
        }

        tally.Spins = Faded(tally, now) + 1.0;
        tally.TouchedTick = now;

        Forget();
    }

    public void Beware(string kind, Map map, Point3D where)
    {
        var now = Core.TickCount;
        var tally = Row(kind, map, where, now);

        if (tally.Cautions < 30)
        {
            tally.Cautions++;
        }

        var span = Math.Min((long)CautionMs << Math.Min(tally.Cautions - 1, 20), MostCautionMs);

        tally.Cautioned = true;
        tally.CautiousUntil = now + span;
        tally.TouchedTick = now;

        Forget();
    }

    public void Worked(string kind, Map map, Point3D where)
    {
        if (!_tallies.TryGetValue(Key(kind, map, where), out var tally))
        {
            return;
        }

        tally.Cautions = 0;
        tally.Cautioned = false;
    }

    private Tally Row(string kind, Map map, Point3D where, long now)
    {
        var key = Key(kind, map, where);

        if (_tallies.TryGetValue(key, out var tally))
        {
            return tally;
        }

        tally = new Tally { TouchedTick = now };
        _tallies[key] = tally;

        return tally;
    }

    private static (string Kind, int Map, int X, int Y) Key(string kind, Map map, Point3D where) =>
        (kind, map?.MapID ?? -1, where.X / BandSize, where.Y / BandSize);

    private static double Faded(Tally tally, long now)
    {
        if (tally.Spins <= 0.0)
        {
            return 0.0;
        }

        var elapsed = now - tally.TouchedTick;

        return elapsed <= 0 ? tally.Spins : tally.Spins * Math.Pow(0.5, elapsed / (double)SpinHalfLifeMs);
    }

    private void Forget()
    {
        while (_tallies.Count > MaxPlaces)
        {
            var oldest = 0L;
            (string Kind, int Map, int X, int Y) worst = default;
            var found = false;

            foreach (var (key, tally) in _tallies)
            {

                if (found && tally.TouchedTick - oldest >= 0)
                {
                    continue;
                }

                oldest = tally.TouchedTick;
                worst = key;
                found = true;
            }

            if (!found)
            {
                return;
            }

            _tallies.Remove(worst);
        }
    }

    public double Best(int least, out string kind)
    {
        kind = null;
        var best = 0.0;

        foreach (var (key, tally) in _tallies)
        {
            if (tally.Settled >= least && tally.Measured > best)
            {
                best = tally.Measured;
                kind = key.Kind;
            }
        }

        return best;
    }

    public override string ToString() => $"{_tallies.Count} places remembered";
}
