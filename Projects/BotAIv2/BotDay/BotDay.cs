using System;
using System.Text.Json;
using Server.Text;

namespace Server.BotAI.V2;

public static class BotDay
{

    public static bool Running { get; set; } = true;

    public static double DawnAt { get; set; } = 5.0;

    public static double MorningAt { get; set; } = 7.0;

    public static double NoonAt { get; set; } = 12.0;

    public static double AfternoonAt { get; set; } = 14.0;

    public static double EveningAt { get; set; } = 18.0;

    public static double NightAt { get; set; } = 22.0;

    public static double Lift { get; set; } = 1.5;

    public static double Least { get; set; } = 0.5;

    public static double Most { get; set; } = 2.0;

    public static int BrokeUnder { get; set; } = 100;

    public static double MealWindowHours { get; set; } = 1.0;

    public static int JourneyTiles { get; set; } = 200;

    public static double OwlShift { get; set; } = 10.0;

    public static double OwlShareFighters { get; set; } = 0.2;

    public static double OwlShareOthers { get; set; } = 0.05;

    public static double BedFrom { get; set; } = 21.0;

    public static double BedSpan { get; set; } = 3.0;

    public static double OwlBedFrom { get; set; } = 4.0;

    public static double OwlBedSpan { get; set; } = 5.0;

    public static bool BedtimeTires { get; set; } = true;

    public static double BedWindowHours { get; set; } = 2.0;

    public static double BedPlayedShare { get; set; } = 0.5;

    public static double WakeWindowHours { get; set; } = 3.0;

    public static long Raised { get; private set; }

    public static long Lowered { get; private set; }

    public static long Floored { get; private set; }

    public static long ToBedOnTime { get; internal set; }

    public static long ToBedEarly { get; internal set; }

    public static long HeldPastBed { get; internal set; }

    public static long WokeOnTime { get; internal set; }

    public static long FieldFirst { get; internal set; }

    public static long FieldFirstMissed { get; internal set; }

    internal enum Group
    {
        Work,
        Leisure,
        Journey,
        Market,
        Night
    }

    private const int Parts = 6;

    private const int Groups = 5;

    private static readonly long[,] _taken = new long[Parts, Groups];

    private static readonly long[] _guests = new long[24];

    private static BotDayPart _part = BotDayPart.Noon;

    private static double _hour = 12.0;

    public static BotDayPart Part => _part;

    public static double Hour => _hour;

    public static void Refresh(DateTime now)
    {
        var local = now.Kind == DateTimeKind.Utc ? now.ToLocalTime() : now;

        _hour = local.TimeOfDay.TotalHours;
        _part = PartAt(_hour);
    }

    public static BotDayPart PartAt(double hour)
    {
        hour = BotTimetables.Wrap(hour);

        if (hour >= NightAt || hour < DawnAt)
        {
            return BotDayPart.Night;
        }

        if (hour < MorningAt)
        {
            return BotDayPart.Dawn;
        }

        if (hour < NoonAt)
        {
            return BotDayPart.Morning;
        }

        if (hour < AfternoonAt)
        {
            return BotDayPart.Noon;
        }

        if (hour < EveningAt)
        {
            return BotDayPart.Afternoon;
        }

        return BotDayPart.Evening;
    }

    public static bool HoursInOrder() =>
        DawnAt >= 0.0 && DawnAt < MorningAt && MorningAt < NoonAt && NoonAt < AfternoonAt && AfternoonAt < EveningAt && EveningAt < NightAt && NightAt <= 24.0;

    internal static bool Owl(BotMobile bot, BotTimetable table)
    {
        if (table.Owl)
        {
            return true;
        }

        var now = Core.TickCount;

        if (!table.ThiefChecked || now - table.ThiefCheckedTick >= 60000)
        {
            table.ThiefChecked = true;
            table.ThiefCheckedTick = now;
            table.Thief = BotCrime.IsThief(bot);
        }

        return table.Thief;
    }

    private static BotDayPart PartFor(bool owl) => owl ? PartAt(_hour - OwlShift) : _part;

    private static double OwnHour(BotTimetable table, bool owl, double hour) =>
        owl && !table.Owl ? BotTimetables.Wrap(hour + OwlShift) : hour;

    private static bool Near(double hour, double at) =>
        Math.Min(BotTimetables.Wrap(hour - at), BotTimetables.Wrap(at - hour)) <= Math.Max(0.0, MealWindowHours);

    internal static Group GroupOf(string kind, Mobile body, BotDeed deed)
    {
        switch (kind)
        {
            case "supper":
            case "fireside":
            case "camp":
            case "home":
            case "chat":
            case "meet":
                return Group.Leisure;
            case "travel":
            case "voyage":
            case "venture":
            case "expedition":
            case "caravan":
            case "delve":
            case "scout":
            case "scout-rumour":
            case "prospect":
            case "colony-run":
            case "escort":
            case "guard":
                return Group.Journey;
            case "trade":
            case "peddle":
            case "shopkeep":
            case "restock":
            case "resell":
            case "order":
            case "acquire":
            case "supply":
            case "haul":
            case "deliver":
            case "unload":
            case "fetch":
            case "fair":
            case "keep-store":

            case "tavern":
                return Group.Market;
            case "watch":
            case "guard-duty":
            case "ward":
            case "skulk":
            case "steal":
            case "burgle":
            case "rob":
            case "waylay":
            case "ambush":
            case "lielow":
            case "manhunt":
                return Group.Night;
        }

        if (deed != null && body != null && deed.Map == body.Map && deed.Where != Point3D.Zero
            && Math.Max(Math.Abs(body.Location.X - deed.Where.X), Math.Abs(body.Location.Y - deed.Where.Y)) >= JourneyTiles)
        {
            return Group.Journey;
        }

        return Group.Work;
    }

    private static bool Fits(BotTimetable table, string kind, BotMobile bot)
    {
        var habit = table.Habit;

        if (habit == BotHabit.Home && BotAbode.Of(bot) == null)
        {
            habit = BotHabit.Tavern;
        }

        return habit switch
        {
            BotHabit.Tavern => kind is "supper",
            BotHabit.Fireside => kind is "fireside" or "camp",
            BotHabit.Home => kind is "home",
            _ => false
        };
    }

    private static bool Broke(BotMobile bot) =>
        BotYield.Spendable(bot) < BrokeUnder || (bot.Resolve?.Urges?.Need ?? 0.0) > 0.0;

    public static double Factor(Mobile body, BotDeed deed, out string why)
    {
        why = null;

        if (!Running || deed == null || body is not BotMobile bot)
        {
            return 1.0;
        }

        var table = BotTimetables.Of(bot);

        if (table == null)
        {
            return 1.0;
        }

        var kind = deed.Kind;
        var group = GroupOf(kind, body, deed);
        var owl = Owl(bot, table);
        var own = PartFor(owl);
        var lift = Math.Max(1.0, Lift);
        var half = 1.0 + (lift - 1.0) / 2.0;
        var drop = 1.0 / lift;
        var factor = 1.0;

        switch (own)
        {
            case BotDayPart.Dawn:
                if (group == Group.Market)
                {
                    factor = lift;
                    why = "the dawn market";
                }
                else if (group == Group.Leisure)
                {
                    factor = drop;
                    why = "the dawn, no hour to sit";
                }

                break;
            case BotDayPart.Morning:
                if (group == Group.Leisure)
                {
                    factor = drop;
                    why = "the morning's work";
                }

                break;
            case BotDayPart.Evening:

                if (table.Habit == BotHabit.Prowl && kind is "prowl" or "stroll")
                {
                    factor = lift;
                    why = "the evening (prowl)";
                }
                else if (group == Group.Leisure)
                {
                    var fits = Fits(table, kind, bot);

                    factor = fits ? lift : half;
                    why = fits ? $"the evening ({BotTimetable.Word(table.Habit)})" : "the evening";
                }
                else if (kind == "tavern")
                {

                    factor = lift;
                    why = "the evening's guests";
                }
                else if (group == Group.Journey)
                {
                    factor = drop;
                    why = "the evening, too late to set out";
                }

                break;
            case BotDayPart.Night:
                if (group == Group.Journey)
                {
                    factor = drop;
                    why = "the night, no hour to set out";
                }
                else if (kind is "fireside" or "camp" or "home")
                {
                    factor = lift;
                    why = "the night, by a fire or at home";
                }

                break;
        }

        if (group == Group.Night && _part == BotDayPart.Night && factor < lift)
        {
            factor = lift;
            why = "the night, when the watch walks";
        }

        if (kind == "supper" && factor < lift && (Near(_hour, OwnHour(table, owl, table.Meal)) || Near(_hour, OwnHour(table, owl, table.Supper))))
        {
            factor = lift;
            why = "its meal hour";
        }

        if (factor == 1.0)
        {
            why = null;

            return 1.0;
        }

        if (factor < 1.0 && !deed.Unpaid && Broke(bot))
        {
            Floored++;
            why = null;

            return 1.0;
        }

        factor = Math.Clamp(factor, Math.Min(Least, 1.0), Math.Max(Most, 1.0));

        if (factor > 1.0)
        {
            Raised++;
        }
        else
        {
            Lowered++;
        }

        return factor;
    }

    public static void Took(Mobile body, BotDeed deed)
    {
        if (!Running || deed == null)
        {
            return;
        }

        _taken[(int)_part, (int)GroupOf(deed.Kind, body, deed)]++;
    }

    public static void Guest()
    {
        if (Running)
        {
            _guests[Math.Clamp((int)_hour, 0, 23)]++;
        }
    }

    public static bool AtBed(BotMobile bot, double playedShare, out double pastBed)
    {
        pastBed = 0.0;

        if (!Running || !BedtimeTires || BotTimetables.Of(bot) is not { } table)
        {
            return false;
        }

        var past = BotTimetables.Wrap(_hour - OwnHour(table, Owl(bot, table), table.Bed));

        if (past >= Math.Max(0.0, BedWindowHours) || playedShare < BedPlayedShare)
        {
            return false;
        }

        pastBed = past;

        return true;
    }

    public static bool Awake(BotMobile bot)
    {
        if (!Running || BotTimetables.Of(bot) is not { } table)
        {
            return false;
        }

        return BotTimetables.Wrap(_hour - OwnHour(table, Owl(bot, table), table.Wake)) < Math.Max(0.0, WakeWindowHours);
    }

    public static bool PrefersField(BotMobile bot) =>
        Running && BotTimetables.Of(bot) is { Place: BotBedPlace.Field };

    private static readonly string[] PartWords = ["dawn", "morning", "noon", "afternoon", "evening", "night"];

    private static readonly string[] GroupWords = ["work", "leisure", "journey", "market", "night"];

    public static string Word(BotDayPart part) => PartWords[(int)part];

    public static string Schedule() =>
        $"dawn {DawnAt:00}–{MorningAt:00}, morning {MorningAt:00}–{NoonAt:00}, noon {NoonAt:00}–{AfternoonAt:00}, afternoon {AfternoonAt:00}–{EveningAt:00}, evening {EveningAt:00}–{NightAt:00}, night {NightAt:00}–{DawnAt:00}";

    private static string Clock(double hour) => $"{(int)hour:00}:{(int)((hour - Math.Floor(hour)) * 60.0):00}";

    private static void Mix(out int owls, out int thieves, Span<int> habits, Span<int> places)
    {
        owls = 0;
        thieves = 0;

        foreach (var table in BotTimetables.All)
        {
            if (table.Owl)
            {
                owls++;
            }
            else if (table.Thief)
            {
                thieves++;
            }

            habits[(int)table.Habit]++;
            places[(int)table.Place]++;
        }
    }

    public static string Describe()
    {
        if (!Running)
        {
            return "the daily cycle is OFF: every hour is one";
        }

        Span<int> habits = stackalloc int[4];
        Span<int> places = stackalloc int[4];

        Mix(out var owls, out var thieves, habits, places);
        BotRest.Sleepers(out var home, out var inn, out var field, out var ground);

        using var line = ValueStringBuilder.Create(768);

        line.Append($"{Word(_part)} at {Clock(_hour)} ({Schedule()}); {BotTimetables.Count} timetables ({owls} night owls");

        if (thieves > 0)
        {
            line.Append($" and {thieves} thieves living by night");
        }

        line.Append($"; evenings: tavern {habits[0]}, fireside {habits[1]}, home {habits[2]}, prowl {habits[3]}; beds: inn {places[1]}, field {places[2]}); ");
        line.Append($"offers raised {Raised} and lowered {Lowered} (×{1.0 / Math.Max(1.0, Lift):F2}–×{Lift:F2}, held in ×{Least:F2}–×{Most:F2}), {Floored} markdowns withheld from broke bots (under {BrokeUnder}gp or in need); taken by part:");

        for (var p = 0; p < Parts; p++)
        {
            var total = 0L;

            for (var g = 0; g < Groups; g++)
            {
                total += _taken[p, g];
            }

            line.Append(p == 0 ? " " : ", ");
            line.Append($"{PartWords[p]} {total} (");

            for (var g = 0; g < Groups; g++)
            {
                line.Append(g == 0 ? "" : " ");
                line.Append($"{GroupWords[g]} {_taken[p, g]}");
            }

            line.Append(')');
        }

        line.Append("; tavern guests by hour:");

        var any = false;

        for (var h = 0; h < 24; h++)
        {
            if (_guests[h] > 0)
            {
                line.Append(any ? ", " : " ");
                line.Append($"{h:00}h {_guests[h]}");
                any = true;
            }
        }

        if (!any)
        {
            line.Append(" none");
        }

        line.Append($"; asleep now: {home} at home, {inn} at an inn, {field} by a fire, {ground} on the open ground; ");
        line.Append($"this session {ToBedOnTime} went to bed at their bedtime ({ToBedEarly} before their hours, {HeldPastBed} held past it by the floor), {WokeOnTime} woke at their hour, {FieldFirst} chose a fire over an inn and {FieldFirstMissed} found none and went to the inn");

        return line.ToString();
    }

    public static void Write(Utf8JsonWriter w)
    {
        w.WritePropertyName("day");
        w.WriteStartObject();
        w.WriteBoolean("running", Running);
        w.WriteString("part", Word(_part));
        w.WriteNumber("hour", Math.Round(_hour, 2));

        w.WritePropertyName("parts");
        w.WriteStartObject();
        w.WriteNumber("dawn", DawnAt);
        w.WriteNumber("morning", MorningAt);
        w.WriteNumber("noon", NoonAt);
        w.WriteNumber("afternoon", AfternoonAt);
        w.WriteNumber("evening", EveningAt);
        w.WriteNumber("night", NightAt);
        w.WriteEndObject();

        Span<int> habits = stackalloc int[4];
        Span<int> places = stackalloc int[4];

        Mix(out var owls, out var thieves, habits, places);

        w.WriteNumber("timetables", BotTimetables.Count);
        w.WriteNumber("owls", owls);
        w.WriteNumber("thieves", thieves);

        w.WritePropertyName("habits");
        w.WriteStartObject();
        w.WriteNumber("tavern", habits[0]);
        w.WriteNumber("fireside", habits[1]);
        w.WriteNumber("home", habits[2]);
        w.WriteNumber("prowl", habits[3]);
        w.WriteEndObject();

        w.WritePropertyName("beds");
        w.WriteStartObject();
        w.WriteNumber("inn", places[1]);
        w.WriteNumber("field", places[2]);
        w.WriteEndObject();

        w.WritePropertyName("taken");
        w.WriteStartArray();

        for (var p = 0; p < Parts; p++)
        {
            w.WriteStartObject();
            w.WriteString("part", PartWords[p]);

            for (var g = 0; g < Groups; g++)
            {
                w.WriteNumber(GroupWords[g], _taken[p, g]);
            }

            w.WriteEndObject();
        }

        w.WriteEndArray();

        w.WritePropertyName("guestsByHour");
        w.WriteStartArray();

        for (var h = 0; h < 24; h++)
        {
            w.WriteNumberValue(_guests[h]);
        }

        w.WriteEndArray();

        BotRest.Sleepers(out var home, out var inn, out var field, out var ground);

        w.WritePropertyName("asleep");
        w.WriteStartObject();
        w.WriteNumber("home", home);
        w.WriteNumber("inn", inn);
        w.WriteNumber("field", field);
        w.WriteNumber("ground", ground);
        w.WriteEndObject();

        w.WriteNumber("raised", Raised);
        w.WriteNumber("lowered", Lowered);
        w.WriteNumber("floored", Floored);
        w.WriteNumber("toBedOnTime", ToBedOnTime);
        w.WriteNumber("toBedEarly", ToBedEarly);
        w.WriteNumber("heldPastBed", HeldPastBed);
        w.WriteNumber("wokeOnTime", WokeOnTime);
        w.WriteNumber("fieldFirst", FieldFirst);
        w.WriteNumber("fieldFirstMissed", FieldFirstMissed);
        w.WriteEndObject();
    }

    public static void WriteBot(Utf8JsonWriter w, BotMobile bot)
    {
        if (bot?.Name == null || BotTimetables.Find(bot.Name) is not { Drawn: true } table)
        {
            return;
        }

        w.WritePropertyName("day");
        w.WriteStartObject();
        w.WriteNumber("wake", Math.Round(table.Wake, 1));
        w.WriteNumber("bed", Math.Round(table.Bed, 1));
        w.WriteNumber("meal", Math.Round(table.Meal, 1));
        w.WriteNumber("supper", Math.Round(table.Supper, 1));
        w.WriteString("habit", BotTimetable.Word(table.Habit));
        w.WriteBoolean("owl", table.Owl || table.Thief);
        w.WriteString("bedPlace", BotTimetable.Word(table.Place));
        w.WriteEndObject();
    }

    public static void Forget()
    {
        Raised = 0;
        Lowered = 0;
        Floored = 0;
        ToBedOnTime = 0;
        ToBedEarly = 0;
        HeldPastBed = 0;
        WokeOnTime = 0;
        FieldFirst = 0;
        FieldFirstMissed = 0;
        Array.Clear(_taken);
        Array.Clear(_guests);
    }
}
