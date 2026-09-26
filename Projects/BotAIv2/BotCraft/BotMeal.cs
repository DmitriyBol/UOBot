using System;
using System.Collections.Generic;
using Server.Items;
using Server.Logging;

namespace Server.BotAI.V2;

/// <summary>
/// A bot eating a cooked meal: what it does to the bot, and for how long.
///
/// <para>
/// <b>Patrick's order of 05.09.2026 — cooking lifts a bot's mood and quickens what it recovers.</b> Both
/// halves land here rather than in the cooking, because the cook is not the one who benefits: a trade whose
/// reward went to whoever made the thing would be a trade with no reason to sell any. The cook is paid in
/// coin like every other crafter on this island; the meal is paid for by whoever eats it.
/// </para>
///
/// <para>
/// <b>Regeneration is reached through the engine's own hooks, and this era leaves two of the three
/// empty.</b> <c>Mobile.HitsRegenRateHandler</c>, <c>StamRegenRateHandler</c> and
/// <c>ManaRegenRateHandler</c> are static entry points that content fills in; <c>RegenRates.Configure</c>
/// fills the mana one always and the other two <em>only under AOS</em>, and this shard is Renaissance. So
/// health and stamina were running at the flat defaults with nothing consulted at all, and mana had one
/// handler. Whatever was there is kept and called: this wraps rather than replaces, so a meal is the only
/// thing that changes and every mobile that has not eaten gets exactly the rate it got before.
/// </para>
///
/// <para>
/// <b>Ten minutes, by order, and kept per bot rather than on the item.</b> An effect written onto the food
/// would have to survive being sold, dropped, looted and re-listed; an effect written against the eater is a
/// stamp and a lookup, and it dies with the bot the way every other fact about a bot does.
/// </para>
/// </summary>
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
