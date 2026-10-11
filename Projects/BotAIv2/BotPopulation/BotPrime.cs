using System;
using System.Collections.Generic;
using Server.Logging;
using Server.Text;

namespace Server.BotAI.V2;

/// <summary>
/// Prime time: how much of the island is meant to be in the world at each hour of the day.
///
/// <para>
/// <b>Patrick's order of 02.10.2026, in his words translated:</b> "Add a prime time for the bots: at night, from 00 to 08 in
/// the morning, 100 % of the bots play; from 08 to 16, 75 %; from 16 to 24, 50 %. This is done so that we collect as much data
/// as possible at night and focus on the improvement work during the day." Until this the island kept one floor the clock
/// round (<see cref="BotRest.FloorShare"/>, a half) and whoever was past their hours above it left: 43 of 86 in the world through
/// the afternoon of 01.10.2026, night and day alike.
/// </para>
///
/// <para>
/// <b>A schedule of bands, each a share of the island, and the share is the floor.</b> The rest clock (<c>BotRest.Look</c>)
/// reads <see cref="Share"/> where it read the fixed half: under the target nobody past their hours leaves, and whoever is
/// resting comes back, soonest due first, whatever rest it still owed; over it the ones past their hours go as they come free,
/// longest over first, exactly as before. Rest and return follow one number, so the night fills and the day empties by the
/// same rule.
/// </para>
///
/// <para>
/// <b>Gradual by construction.</b> The target does not jump at the hour: it moves from the old band's share to the new one over
/// <see cref="RampMinutes"/>, so 200 bots at 100 % become 150 at 75 % at about two a minute rather than fifty in one look, and
/// the night's return fills at the same pace. Behind that, <see cref="TirePerLook"/> and <see cref="ReturnPerLook"/> cap what
/// any one look may do. Nothing here pulls a bot out of a fight, a company or a delve: a bot the target lets go only tires,
/// and leaves when it is free, by the rest clock's own rules (<c>BotRest.Holds</c>).
/// </para>
///
/// <para>
/// Local server time, read off <c>Core.Now</c>. Off, the floor is <see cref="BotRest.FloorShare"/> round the clock, as on build
/// 388. Configured in <c>bot-rest.json</c> under <c>PrimeTime</c>.
/// </para>
/// </summary>
public static class BotPrime
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotPrime));

    public static bool Running { get; set; } = true;

    public static int RampMinutes { get; set; } = 30;

    public static int TirePerLook { get; set; } = 5;

    public static int ReturnPerLook { get; set; } = 5;

    /// <summary>One band of the day: from an hour, to an hour, and the share of the island to be in the world between them.</summary>
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
