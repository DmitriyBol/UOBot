using System.IO;
using Server.Json;
using Server.Logging;

namespace Server.BotAI.V2;

public sealed class BotDaySettings
{

    public bool? Running { get; set; }

    public double? DawnAt { get; set; }

    public double? MorningAt { get; set; }

    public double? NoonAt { get; set; }

    public double? AfternoonAt { get; set; }

    public double? EveningAt { get; set; }

    public double? NightAt { get; set; }

    public double? Lift { get; set; }

    public double? Least { get; set; }

    public double? Most { get; set; }

    public int? BrokeUnder { get; set; }

    public double? MealWindowHours { get; set; }

    public int? JourneyTiles { get; set; }

    public double? OwlShift { get; set; }

    public double? OwlShareFighters { get; set; }

    public double? OwlShareOthers { get; set; }

    public double? BedFrom { get; set; }

    public double? BedSpan { get; set; }

    public double? OwlBedFrom { get; set; }

    public double? OwlBedSpan { get; set; }

    public bool? BedtimeTires { get; set; }

    public double? BedWindowHours { get; set; }

    public double? BedPlayedShare { get; set; }

    public double? WakeWindowHours { get; set; }
}

public static class BotDayConfig
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotDayConfig));

    private const string ConfigPath = "Configuration/bot-day.json";

    private static BotDaySettings Starter() =>
        new()
        {
            Running = BotDay.Running,
            DawnAt = BotDay.DawnAt,
            MorningAt = BotDay.MorningAt,
            NoonAt = BotDay.NoonAt,
            AfternoonAt = BotDay.AfternoonAt,
            EveningAt = BotDay.EveningAt,
            NightAt = BotDay.NightAt,
            Lift = BotDay.Lift,
            Least = BotDay.Least,
            Most = BotDay.Most,
            BrokeUnder = BotDay.BrokeUnder,
            MealWindowHours = BotDay.MealWindowHours,
            JourneyTiles = BotDay.JourneyTiles,
            OwlShift = BotDay.OwlShift,
            OwlShareFighters = BotDay.OwlShareFighters,
            OwlShareOthers = BotDay.OwlShareOthers,
            BedFrom = BotDay.BedFrom,
            BedSpan = BotDay.BedSpan,
            OwlBedFrom = BotDay.OwlBedFrom,
            OwlBedSpan = BotDay.OwlBedSpan,
            BedtimeTires = BotDay.BedtimeTires,
            BedWindowHours = BotDay.BedWindowHours,
            BedPlayedShare = BotDay.BedPlayedShare,
            WakeWindowHours = BotDay.WakeWindowHours
        };

    public static void Load()
    {
        var path = Path.Combine(Core.BaseDirectory, ConfigPath);
        var settings = JsonConfig.Deserialize<BotDaySettings>(path);

        if (settings == null)
        {
            JsonConfig.Serialize(path, Starter());

            logger.Information("Wrote a starter day file to {Path} with the hours of the day and every dial in it", ConfigPath);

            return;
        }

        BotDay.Running = settings.Running ?? BotDay.Running;

        var dawn = BotDay.DawnAt;
        var morning = BotDay.MorningAt;
        var noon = BotDay.NoonAt;
        var afternoon = BotDay.AfternoonAt;
        var evening = BotDay.EveningAt;
        var night = BotDay.NightAt;

        BotDay.DawnAt = settings.DawnAt ?? dawn;
        BotDay.MorningAt = settings.MorningAt ?? morning;
        BotDay.NoonAt = settings.NoonAt ?? noon;
        BotDay.AfternoonAt = settings.AfternoonAt ?? afternoon;
        BotDay.EveningAt = settings.EveningAt ?? evening;
        BotDay.NightAt = settings.NightAt ?? night;

        if (!BotDay.HoursInOrder())
        {
            logger.Warning("The hours in {Path} do not stand in order inside the day (dawn < morning < noon < afternoon < evening < night, nought to twenty-four); the code's own stand", ConfigPath);

            BotDay.DawnAt = dawn;
            BotDay.MorningAt = morning;
            BotDay.NoonAt = noon;
            BotDay.AfternoonAt = afternoon;
            BotDay.EveningAt = evening;
            BotDay.NightAt = night;
        }

        BotDay.Lift = settings.Lift ?? BotDay.Lift;
        BotDay.Most = settings.Most ?? BotDay.Most;

        var least = settings.Least ?? BotDay.Least;

        if (least < 0.1)
        {
            logger.Warning("Least in {Path} is {Least}, under the floor of 0.1 that keeps the hour from being a veto; 0.1 stands", ConfigPath, least);
            least = 0.1;
        }

        BotDay.Least = least;
        BotDay.BrokeUnder = settings.BrokeUnder ?? BotDay.BrokeUnder;
        BotDay.MealWindowHours = settings.MealWindowHours ?? BotDay.MealWindowHours;
        BotDay.JourneyTiles = settings.JourneyTiles ?? BotDay.JourneyTiles;
        BotDay.OwlShift = settings.OwlShift ?? BotDay.OwlShift;
        BotDay.OwlShareFighters = settings.OwlShareFighters ?? BotDay.OwlShareFighters;
        BotDay.OwlShareOthers = settings.OwlShareOthers ?? BotDay.OwlShareOthers;
        BotDay.BedFrom = settings.BedFrom ?? BotDay.BedFrom;
        BotDay.BedSpan = settings.BedSpan ?? BotDay.BedSpan;
        BotDay.OwlBedFrom = settings.OwlBedFrom ?? BotDay.OwlBedFrom;
        BotDay.OwlBedSpan = settings.OwlBedSpan ?? BotDay.OwlBedSpan;
        BotDay.BedtimeTires = settings.BedtimeTires ?? BotDay.BedtimeTires;
        BotDay.BedWindowHours = settings.BedWindowHours ?? BotDay.BedWindowHours;
        BotDay.BedPlayedShare = settings.BedPlayedShare ?? BotDay.BedPlayedShare;
        BotDay.WakeWindowHours = settings.WakeWindowHours ?? BotDay.WakeWindowHours;
    }
}
