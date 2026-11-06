using System;
using Server.Logging;

namespace Server.BotAI.V2;

public sealed class BotDayStore : GenericPersistence
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotDayStore));

    private const int Shape = 1;

    private static BotDayStore _store;

    public static void Configure() => _store ??= new BotDayStore();

    public BotDayStore() : base("BotDay", 24)
    {
    }

    public override void Serialize(IGenericWriter writer)
    {
        writer.WriteEncodedInt(Shape);
        writer.WriteEncodedInt(BotTimetables.Count);

        foreach (var table in BotTimetables.All)
        {
            writer.Write(table.Name);
            writer.Write(table.Drawn);
            writer.Write(table.Wake);
            writer.Write(table.Bed);
            writer.Write(table.Meal);
            writer.Write(table.Supper);
            writer.WriteEncodedInt((int)table.Habit);
            writer.Write(table.Owl);
            writer.WriteEncodedInt((int)table.Place);
        }
    }

    public override void Deserialize(IGenericReader reader)
    {
        var shape = reader.ReadEncodedInt();

        if (shape != Shape)
        {
            logger.Warning("The saved timetables are shape {Found} and this build reads {Wanted}; every timetable is drawn again from the names", shape, Shape);

            return;
        }

        var count = reader.ReadEncodedInt();
        var owls = 0;

        for (var n = 0; n < count; n++)
        {
            var table = new BotTimetable(reader.ReadString())
            {
                Drawn = reader.ReadBool(),
                Wake = reader.ReadDouble(),
                Bed = reader.ReadDouble(),
                Meal = reader.ReadDouble(),
                Supper = reader.ReadDouble(),

                Habit = (BotHabit)Math.Clamp(reader.ReadEncodedInt(), (int)BotHabit.Tavern, (int)BotHabit.Prowl),
                Owl = reader.ReadBool(),
                Place = (BotBedPlace)Math.Clamp(reader.ReadEncodedInt(), (int)BotBedPlace.Home, (int)BotBedPlace.Ground)
            };

            if (string.IsNullOrEmpty(table.Name))
            {
                continue;
            }

            if (table.Owl)
            {
                owls++;
            }

            BotTimetables.Put(table);
        }

        logger.Information("Timetables read back for {Count} bots, {Owls} of them night owls", count, owls);
    }
}
