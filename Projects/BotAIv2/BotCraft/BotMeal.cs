using System;
using System.Collections.Generic;
using Server.Items;
using Server.Logging;

namespace Server.BotAI.V2;

public static class BotMeal
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotMeal));

    public static int LastsMs { get; set; } = 600000;

    public static double Quickening { get; set; } = 0.5;

    public static double Cheer { get; set; } = 40.0;

    public static long Eaten { get; private set; }

    public static long Fed { get; private set; }

    public static long Empty { get; private set; }

    public static long Emptied { get; private set; }

    public static long Refused { get; private set; }

    private static readonly Dictionary<Serial, long> _until = new();

    public static bool IsFed(Mobile body)
    {
        if (body == null || !_until.TryGetValue(body.Serial, out var until))
        {
            return false;
        }

        if (Core.TickCount - until < 0)
        {
            return true;
        }

        _until.Remove(body.Serial);

        return false;
    }

    public static void Keep(IBotWilful bot)
    {
        var body = bot?.Self;
        var pack = body?.Backpack;

        if (body is not { Deleted: false, Alive: true } || pack == null)
        {
            return;
        }

        if (IsFed(body))
        {
            Fed++;

            return;
        }

        Item meal = null;

        for (var i = 0; i < pack.Items.Count; i++)
        {
            var item = pack.Items[i];

            if (item is { Deleted: false, Movable: true } && BotOven.IsMeal(item.GetType()))
            {
                meal = item;

                break;
            }
        }

        if (meal == null)
        {
            Empty++;

            return;
        }

        if (body.Hunger >= 20)
        {
            body.Hunger = 0;
            Emptied++;
        }

        if (meal is not Food food)
        {
            Refused++;

            return;
        }

        if (!food.Eat(body))
        {
            Refused++;

            return;
        }

        _until[body.Serial] = Core.TickCount + LastsMs;
        Eaten++;

        bot.Resolve?.Urges?.Paid(Cheer);

        logger.Information(
            "{Name} ate {Meal} and will recover twice as fast for {Minutes} minutes",
            body.Name,
            meal.GetType().Name,
            LastsMs / 60000
        );
    }

    public static void Configure()
    {
        var hits = Mobile.HitsRegenRateHandler;
        var stam = Mobile.StamRegenRateHandler;
        var mana = Mobile.ManaRegenRateHandler;

        Mobile.HitsRegenRateHandler = m => Quicken(m, hits?.Invoke(m) ?? Mobile.DefaultHitsRate);
        Mobile.StamRegenRateHandler = m => Quicken(m, stam?.Invoke(m) ?? Mobile.DefaultStamRate);
        Mobile.ManaRegenRateHandler = m => Quicken(m, mana?.Invoke(m) ?? Mobile.DefaultManaRate);
    }

    private static TimeSpan Quicken(Mobile m, TimeSpan rate) =>
        m is BotMobile && IsFed(m) ? TimeSpan.FromMilliseconds(rate.TotalMilliseconds * Quickening) : rate;

    public static string Describe() =>
        Eaten + Fed + Empty == 0
            ? "nobody has been offered a meal yet"
            : $"{Eaten} meals eaten, {Fed} looks found a bot still fed from the last one, {Empty} had nothing cooked on them, "
              + $"{Emptied} were too full until their last meal wore off, {Refused} were refused by the engine for something else";

    public static void Forget()
    {
        _until.Clear();
        Eaten = 0;
        Fed = 0;
        Empty = 0;
        Emptied = 0;
        Refused = 0;
    }
}
