using System.Collections.Generic;
using System.IO;
using Server.Json;
using Server.Logging;

namespace Server.BotAI.V2;

/// <summary>One band of the prime time schedule as the file writes it: from an hour, to an hour, the share of the island in the world.</summary>
public sealed class BotPrimeBandSettings
{
    public int? From { get; set; }

    public int? To { get; set; }

    public double? Share { get; set; }
}

/// <summary>What <c>bot-rest.json</c> may say under <c>PrimeTime</c>. See <see cref="BotPrime"/>.</summary>
public sealed class BotPrimeTimeSettings
{
    public bool? Running { get; set; }

    public int? RampMinutes { get; set; }

    public int? TirePerLook { get; set; }

    public int? ReturnPerLook { get; set; }

    public List<BotPrimeBandSettings> Bands { get; set; }
}

/// <summary>What <c>bot-rest.json</c> may say under <c>Critical</c>. See <see cref="BotCritical"/>.</summary>
public sealed class BotCriticalSettings
{
    public bool? Running { get; set; }

    public int? EarlyMinutes { get; set; }

    public Dictionary<string, int> Floors { get; set; }
}

/// <summary>
/// What <c>Configuration/bot-rest.json</c> is allowed to say: the rest clock's own dials, which had no file until
/// 02.10.2026, the prime time schedule and the critical floors.
/// </summary>
public sealed class BotRestSettings
{
    public bool? Running { get; set; }

    public int? LookMs { get; set; }

    public double? LeastRestHours { get; set; }

    public double? MostRestHours { get; set; }

    public int? GraceMs { get; set; }

    public double? FirstShare { get; set; }

    public double? FloorShare { get; set; }

    public bool? Calls { get; set; }

    public int? CallMost { get; set; }

    public int? CallMostBig { get; set; }

    public int? CallGraceMs { get; set; }

    public double? CalledLeastRestHours { get; set; }

    public int? EarlyMinutes { get; set; }

    public BotPrimeTimeSettings PrimeTime { get; set; }

    public BotCriticalSettings Critical { get; set; }
}

/// <summary>Reads the rest file, or writes one with the schedule and the floors in it.</summary>
public static class BotRestConfig
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotRestConfig));

    private const string ConfigPath = "Configuration/bot-rest.json";

    private static BotRestSettings Starter()
    {
        var settings = new BotRestSettings
        {
            PrimeTime = new BotPrimeTimeSettings
            {
                Running = BotPrime.Running,
                RampMinutes = BotPrime.RampMinutes,
                TirePerLook = BotPrime.TirePerLook,
                ReturnPerLook = BotPrime.ReturnPerLook,
                Bands = []
            },
            Critical = new BotCriticalSettings
            {
                Running = BotCritical.Running,
                EarlyMinutes = BotCritical.EarlyMinutes,
                Floors = new Dictionary<string, int>()
            }
        };

        foreach (var band in BotPrime.Bands)
        {
            settings.PrimeTime.Bands.Add(new BotPrimeBandSettings { From = band.From, To = band.To, Share = band.Share });
        }

        foreach (var post in BotCritical.Posts)
        {
            settings.Critical.Floors[post] = BotCritical.Floor(post);
        }

        return settings;
    }

    public static void Load()
    {
        var path = Path.Combine(Core.BaseDirectory, ConfigPath);
        var settings = JsonConfig.Deserialize<BotRestSettings>(path);

        if (settings == null)
        {
            JsonConfig.Serialize(path, Starter());

            logger.Information("Wrote a starter rest file to {Path} with the prime time schedule and the critical floors in it; every other number stays as the code has it", ConfigPath);

            return;
        }

        BotRest.Running = settings.Running ?? BotRest.Running;
        BotRest.LookMs = settings.LookMs ?? BotRest.LookMs;
        BotRest.LeastRestHours = settings.LeastRestHours ?? BotRest.LeastRestHours;
        BotRest.MostRestHours = settings.MostRestHours ?? BotRest.MostRestHours;
        BotRest.GraceMs = settings.GraceMs ?? BotRest.GraceMs;
        BotRest.FirstShare = settings.FirstShare ?? BotRest.FirstShare;
        BotRest.FloorShare = settings.FloorShare ?? BotRest.FloorShare;
        BotRest.Calls = settings.Calls ?? BotRest.Calls;
        BotRest.CallMost = settings.CallMost ?? BotRest.CallMost;
        BotRest.CallMostBig = settings.CallMostBig ?? BotRest.CallMostBig;
        BotRest.CallGraceMs = settings.CallGraceMs ?? BotRest.CallGraceMs;
        BotRest.CalledLeastRestHours = settings.CalledLeastRestHours ?? BotRest.CalledLeastRestHours;
        BotRest.EarlyMinutes = settings.EarlyMinutes ?? BotRest.EarlyMinutes;

        {
            var c = settings.PrimeTime;

            if (c != null)
            {
                BotPrime.Running = c.Running ?? BotPrime.Running;
                BotPrime.RampMinutes = c.RampMinutes ?? BotPrime.RampMinutes;
                BotPrime.TirePerLook = c.TirePerLook ?? BotPrime.TirePerLook;
                BotPrime.ReturnPerLook = c.ReturnPerLook ?? BotPrime.ReturnPerLook;

                if (c.Bands is { Count: > 0 })
                {
                    List<BotPrime.Band> bands = [];

                    for (var i = 0; i < c.Bands.Count; i++)
                    {
                        var band = c.Bands[i];

                        if (band?.From == null || band.To == null || band.Share == null)
                        {
                            logger.Warning("A prime time band in {Path} is missing From, To or Share and is dropped", ConfigPath);
                            continue;
                        }

                        bands.Add(new BotPrime.Band(band.From.Value, band.To.Value, band.Share.Value));
                    }

                    BotPrime.Set(bands);
                }
            }
        }

        {
            var c = settings.Critical;

            if (c != null)
            {
                BotCritical.Running = c.Running ?? BotCritical.Running;
                BotCritical.EarlyMinutes = c.EarlyMinutes ?? BotCritical.EarlyMinutes;

                if (c.Floors != null)
                {
                    BotCritical.Set(c.Floors);
                }
            }
        }
    }
}
