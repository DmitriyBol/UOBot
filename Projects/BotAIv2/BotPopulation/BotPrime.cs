using System;
using System.Collections.Generic;
using Server.Logging;
using Server.Text;

namespace Server.BotAI.V2;

public static class BotPrime
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotPrime));

    public static bool Running { get; set; } = true;

    public static int RampMinutes { get; set; } = 30;

    public static int TirePerLook { get; set; } = 5;

    public static int ReturnPerLook { get; set; } = 5;

    public readonly record struct Band(int From, int To, double Share)
    {

        public bool Holds(double hour) => hour >= From && hour < To;

        public override string ToString() => $"{From:00}–{To:00} at {Share:P0}";
    }

    private static readonly List<Band> _bands = [new(0, 8, 1.0), new(8, 16, 0.75), new(16, 24, 0.5)];

    public static IReadOnlyList<Band> Bands => _bands;

    public static long Returned { get; internal set; }

    public static long TiresPaced { get; internal set; }

    public static long ReturnsPaced { get; internal set; }

    private static Band? _band;
    private static double _share;
    private static bool _ramping;
    private static double _rampedFrom;
    private static double _minutesIn;
    private static int _target;
    private static int _present;
    private static int _island;

    public static int Set(IReadOnlyList<Band> bands)
    {
        if (bands == null || bands.Count == 0)
        {
            return _bands.Count;
        }

        List<Band> kept = [];

        for (var i = 0; i < bands.Count; i++)
        {
            var band = bands[i];

            if (band.From < 0 || band.To > 24 || band.From >= band.To || double.IsNaN(band.Share))
            {
                logger.Warning("The prime time band {Band} does not run from an hour to a later hour inside the day and is dropped", band);
                continue;
            }

            var overlaps = false;

            for (var k = 0; k < kept.Count; k++)
            {
                if (band.From < kept[k].To && kept[k].From < band.To)
                {
                    overlaps = true;
                    break;
                }
            }

            if (overlaps)
            {
                logger.Warning("The prime time band {Band} overlaps an earlier one and is dropped", band);
                continue;
            }

            kept.Add(new Band(band.From, band.To, Math.Clamp(band.Share, 0.0, 1.0)));
        }

        if (kept.Count == 0)
        {
            logger.Warning("No prime time band could be read; the three written into the code stand");

            return _bands.Count;
        }

        kept.Sort((a, b) => a.From.CompareTo(b.From));
        _bands.Clear();
        _bands.AddRange(kept);

        return _bands.Count;
    }

    private static Band? At(double hour)
    {
        for (var i = 0; i < _bands.Count; i++)
        {
            if (_bands[i].Holds(hour))
            {
                return _bands[i];
            }
        }

        return null;
    }

    public static double Share(DateTime utcNow, out Band? band, out bool ramping)
    {
        band = null;
        ramping = false;

        if (!Running || _bands.Count == 0)
        {
            return Math.Clamp(BotRest.FloorShare, 0.0, 1.0);
        }

        var local = utcNow.Kind == DateTimeKind.Utc ? utcNow.ToLocalTime() : utcNow;
        var hour = local.TimeOfDay.TotalHours;
        var now = At(hour);

        if (now == null)
        {
            return Math.Clamp(BotRest.FloorShare, 0.0, 1.0);
        }

        band = now;

        var share = now.Value.Share;

        if (RampMinutes > 0)
        {
            var minutesIn = (hour - now.Value.From) * 60.0;

            if (minutesIn < RampMinutes)
            {

                var before = At((now.Value.From == 0 ? 24.0 : now.Value.From) - 1.0 / 3600.0);

                if (before != null && before.Value.Share != share)
                {
                    share = before.Value.Share + (share - before.Value.Share) * Math.Clamp(minutesIn / RampMinutes, 0.0, 1.0);
                    ramping = true;
                    _rampedFrom = before.Value.Share;
                    _minutesIn = minutesIn;
                }
            }
        }

        return Math.Clamp(share, 0.0, 1.0);
    }

    internal static void Read(Band? band, double share, bool ramping, int island, int target, int present)
    {
        _band = band;
        _share = share;
        _ramping = ramping;
        _island = island;
        _target = target;
        _present = present;
    }

    public static string Schedule()
    {
        using var line = ValueStringBuilder.Create(96);

        for (var i = 0; i < _bands.Count; i++)
        {
            if (i > 0)
            {
                line.Append(", ");
            }

            line.Append(_bands[i].ToString());
        }

        return line.ToString();
    }

    public static string Describe()
    {
        if (!Running)
        {
            return $"prime time OFF (the floor is {BotRest.FloorShare:P0} round the clock)";
        }

        var band = _band == null ? "no band holds this hour" : $"band {_band.Value}";
        var ramp = _ramping ? $", ramping from {_rampedFrom:P0} with {_minutesIn:0} of {RampMinutes} minutes in" : "";
        var gap = _present >= _target ? "none short" : $"{_target - _present} short";

        return $"prime time: {band}{ramp}, target {_share:P0} = {_target} of {_island} in the world against {_present} there ({gap}); "
               + $"bands {Schedule()}, the target moving over {RampMinutes} minutes at a band's start; this session {Returned} brought back by it "
               + $"(up to {ReturnPerLook} a look, {ReturnsPaced} looks held more), {TiresPaced} tires put off by the pace of {TirePerLook} a look";
    }
}
