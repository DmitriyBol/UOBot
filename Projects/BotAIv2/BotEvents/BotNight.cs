using System;
using Server.Logging;

namespace Server.BotAI.V2;

/// <summary>
/// The night (07.10.2026, Patrick's fourth idea: "roads are more dangerous at night, night deliveries pay more"). Between
/// <see cref="From"/> and <see cref="To"/> of the shard's own clock — the engine's <c>Clock.GetTime</c>, five real seconds to a
/// game minute, so a game day is two real hours and its night forty minutes — the danger the walk prices on a road is
/// <see cref="Danger"/> times what it is by day (<c>BotDanger.Cost</c> on every node, <c>BotDanger.Road</c> on the straight line
/// before far work is offered), and carriage pays <see cref="Pay"/> times as much: a trader's carriage on a haul
/// (<c>BotTrading.Quote</c>, never above the buyer's own ceiling) and a caravan's premium and guards' wage (<see cref="BotCaravans"/>).
///
/// <para>
/// <b>Read where the population lives.</b> The engine's hour depends on the longitude (<c>x / 16</c> game minutes), so Britain and
/// Moonglow do not agree about midnight by forty game minutes. One hour for the whole island is read at the population's home
/// (<see cref="BotPopulation.Where"/>) once a beat; a road is not dearer at one end than at the other.
/// </para>
/// </summary>
public static class BotNight
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotNight));

    public static bool Running { get; set; } = true;

    public static int From { get; set; } = 22;

    public static int To { get; set; } = 6;

    public static double NightDanger { get; set; } = 1.25;

    public static double NightPay { get; set; } = 1.25;

    public static bool Dark { get; private set; }

    public static int Hour { get; private set; }

    public static double Danger { get; private set; } = 1.0;

    public static double Pay => Running && Dark ? Math.Max(1.0, NightPay) : 1.0;

    public static long Nights { get; private set; }

    public static double DarkMinutes { get; private set; }

    public static long DarkNodes { get; private set; }

    public static long DarkRoads { get; private set; }

    public static long Quotes { get; private set; }

    public static long QuoteGold { get; private set; }

    private static long _beatTick;

    private static bool _beaten;

    public static void Beat(long now)
    {
        var map = BotPopulation.Home;

        if (map == null || map == Map.Internal)
        {
            return;
        }

        Server.Items.Clock.GetTime(map, BotPopulation.Where.X, BotPopulation.Where.Y, out int hours, out int minutes);

        var dark = Running && Within(hours);

        if (_beaten && Dark)
        {
            DarkMinutes += (now - _beatTick) / 60000.0;
        }

        _beatTick = now;
        _beaten = true;
        Hour = hours;

        if (dark == Dark)
        {
            return;
        }

        Dark = dark;
        Danger = dark ? Math.Max(1.0, NightDanger) : 1.0;

        if (dark)
        {
            Nights++;

            logger.Information(
                "Night fell at {Hour:00}:{Minute:00} by the shard's clock: the roads read {Danger:F2} times as dangerous and carriage pays {Pay:F2} times as much until {To:00}:00",
                hours,
                minutes,
                Danger,
                Pay,
                To
            );

            BotSeasons.Announce("night", map, BotPopulation.Where, "Night falls on the roads. Travel in company; carriers are paid a quarter more until dawn.");
        }
        else
        {
            logger.Information(
                "Day broke at {Hour:00}:{Minute:00} by the shard's clock: {Nodes} nodes were priced dearer and {Roads} roads judged in the dark so far; {Quotes} hauls quoted a night's carriage for {Gold}gp more",
                hours,
                minutes,
                DarkNodes,
                DarkRoads,
                Quotes,
                QuoteGold
            );
        }
    }

    private static bool Within(int hour) => From <= To ? hour >= From && hour < To : hour >= From || hour < To;

    public static int Node(int tiles)
    {
        if (tiles <= 0 || !Dark)
        {
            return tiles;
        }

        DarkNodes++;

        return (int)Math.Ceiling(tiles * Danger);
    }

    public static double Dare(double dare)
    {
        if (!Dark || dare <= 0.0)
        {
            return dare;
        }

        DarkRoads++;

        return dare / Danger;
    }

    public static int Carriage(int least, int price, int ceiling)
    {
        if (!Dark || price <= least || ceiling <= price)
        {
            return price;
        }

        var raised = Math.Min(ceiling, least + (int)Math.Ceiling((price - least) * Pay));

        if (raised > price)
        {
            Quotes++;
            QuoteGold += raised - price;
        }

        return raised;
    }

    public static string Describe() =>
        !Running
            ? "off"
            : $"{(Dark ? "night" : "day")} at {Hour:00}:00 by the shard's clock (night {From:00}–{To:00}, roads ×{NightDanger:F2}, carriage ×{NightPay:F2}); {Nights} nights, {DarkMinutes:F0} minutes of them; {DarkNodes} nodes priced dearer, {DarkRoads} roads judged in the dark; {Quotes} hauls quoted a night's carriage for {QuoteGold}gp more";

    public static void Forget()
    {
        Nights = 0;
        DarkMinutes = 0;
        DarkNodes = 0;
        DarkRoads = 0;
        Quotes = 0;
        QuoteGold = 0;
        Dark = false;
        Danger = 1.0;
        _beaten = false;
    }
}
