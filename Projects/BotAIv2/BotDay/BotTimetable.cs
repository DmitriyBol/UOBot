using System;
using System.Collections.Generic;

namespace Server.BotAI.V2;

public sealed class BotTimetable
{
    public BotTimetable(string name) => Name = name;

    public string Name { get; }

    public double Wake;

    public double Bed;

    public double Meal;

    public double Supper;

    public BotHabit Habit;

    public bool Owl;

    public BotBedPlace Place;

    public bool Drawn;

    public bool Thief;

    internal bool ThiefChecked;

    internal long ThiefCheckedTick;

    public static string Word(BotHabit habit) =>
        habit switch
        {
            BotHabit.Tavern => "tavern",
            BotHabit.Fireside => "fireside",
            BotHabit.Home => "home",
            _ => "prowl"
        };

    public static string Word(BotBedPlace place) =>
        place switch
        {
            BotBedPlace.Home => "home",
            BotBedPlace.Inn => "inn",
            BotBedPlace.Field => "field",
            _ => "ground"
        };

    public override string ToString() =>
        $"wakes {Wake:0.0}h, to bed {Bed:0.0}h, eats {Meal:0.0}h and {Supper:0.0}h, evenings at the {Word(Habit)}, sleeps at the {Word(Place)}{(Owl ? ", a night owl" : "")}";
}

public enum BotDayPart
{
    Dawn,
    Morning,
    Noon,
    Afternoon,
    Evening,
    Night
}

public enum BotHabit
{
    Tavern,
    Fireside,
    Home,
    Prowl
}

public enum BotBedPlace
{
    Home,
    Inn,
    Field,
    Ground
}

public static class BotTimetables
{
    private static readonly Dictionary<string, BotTimetable> _byName = new(StringComparer.Ordinal);

    private static readonly Dictionary<Serial, BotTimetable> _bySerial = [];

    public static IEnumerable<BotTimetable> All => _byName.Values;

    public static int Count => _byName.Count;

    public static long Drawn { get; private set; }

    private static bool Fighter(string className) =>
        className is "Warrior" or "Archer" or "Brawler" or "Tamer" or "WarriorArcher" or "WarriorMage";

    public static BotTimetable Of(Mobile m)
    {
        if (m is not BotMobile bot || string.IsNullOrEmpty(bot.Name))
        {
            return null;
        }

        if (_bySerial.TryGetValue(bot.Serial, out var table) && string.Equals(table.Name, bot.Name, StringComparison.Ordinal))
        {
            return table;
        }

        if (!_byName.TryGetValue(bot.Name, out table))
        {
            table = new BotTimetable(bot.Name);
            _byName[bot.Name] = table;
        }

        if (!table.Drawn)
        {
            Draw(table, bot.Class?.Name, bot.Class?.Office ?? false, BotTraits.Of(bot));
            Drawn++;
        }

        _bySerial[bot.Serial] = table;

        return table;
    }

    public static BotTimetable Find(string name) =>
        name != null && _byName.TryGetValue(name, out var table) ? table : null;

    public static void Put(BotTimetable table)
    {
        if (table?.Name != null)
        {
            _byName[table.Name] = table;
        }
    }

    public static void Draw(BotTimetable table, string className, bool office, BotTrait traits)
    {
        var hash = 2166136261u;
        var name = table.Name ?? "";

        for (var i = 0; i < name.Length; i++)
        {
            hash ^= name[i];
            hash *= 16777619u;
        }

        var rng = new System.Random((int)((hash ^ 0x5A17A9E3u) & 0x7FFFFFFF));

        var homebody = (traits & BotTrait.Homebody) != 0;
        var wanderer = (traits & BotTrait.Wanderer) != 0;
        var sociable = (traits & BotTrait.Sociable) != 0;
        var solitary = (traits & BotTrait.Solitary) != 0;

        var owlShare = office ? 0.0 : Fighter(className) ? BotDay.OwlShareFighters : BotDay.OwlShareOthers;

        table.Owl = rng.NextDouble() < Math.Clamp(owlShare, 0.0, 1.0);

        if (table.Owl)
        {

            table.Bed = BotDay.OwlBedFrom + rng.NextDouble() * Math.Max(0.0, BotDay.OwlBedSpan);
        }
        else
        {
            var bed = BotDay.BedFrom + rng.NextDouble() * Math.Max(0.0, BotDay.BedSpan);

            if (homebody)
            {
                bed -= 1.0;
            }
            else if (wanderer)
            {
                bed += 0.5;
            }

            if (sociable)
            {
                bed += 0.5;
            }
            else if (solitary)
            {
                bed -= 0.5;
            }

            table.Bed = bed;
        }

        table.Wake = Wrap(table.Bed + 6.5 + rng.NextDouble() * 1.5);
        table.Bed = Wrap(table.Bed);
        table.Meal = table.Owl ? Wrap(table.Wake + 1.0 + rng.NextDouble()) : 11.5 + rng.NextDouble() * 2.0;
        table.Supper = Wrap(table.Bed - 1.5 - rng.NextDouble() * 1.5);

        var tavern = 1.0;
        var fireside = 1.0;
        var home = 1.0;
        var prowl = 0.5;

        if (sociable)
        {
            tavern *= 2.0;
            fireside *= 1.5;
            home *= 0.5;
        }
        else if (solitary)
        {
            tavern *= 0.5;
            home *= 1.5;
            prowl *= 1.5;
        }

        if (homebody)
        {
            home *= 2.0;
            prowl *= 0.3;
        }
        else if (wanderer)
        {
            fireside *= 1.5;
            prowl *= 2.0;
            home *= 0.5;
        }

        if (table.Owl)
        {
            prowl *= 3.0;
        }

        var roll = rng.NextDouble() * (tavern + fireside + home + prowl);

        table.Habit = roll < tavern ? BotHabit.Tavern
            : roll < tavern + fireside ? BotHabit.Fireside
            : roll < tavern + fireside + home ? BotHabit.Home
            : BotHabit.Prowl;

        var field = wanderer || solitary ? 0.6 : sociable || homebody ? 0.15 : 0.3;

        table.Place = rng.NextDouble() < field ? BotBedPlace.Field : BotBedPlace.Inn;
        table.Drawn = true;
    }

    public static double Wrap(double hour)
    {
        hour %= 24.0;

        return hour < 0.0 ? hour + 24.0 : hour;
    }

    public static void Refresh()
    {
        if (_bySerial.Count > BotPopulation.Bots.Count * 2 + 64)
        {
            _bySerial.Clear();
        }
    }

    public static void Forget()
    {
        _byName.Clear();
        _bySerial.Clear();
        Drawn = 0;
    }

    public static int Wipe()
    {
        var count = _byName.Count;

        Forget();
        BotDay.Forget();

        return count;
    }
}
