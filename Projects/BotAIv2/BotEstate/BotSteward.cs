using System.Collections.Generic;
using Server.Guilds;
using Server.Mobiles;

namespace Server.BotAI.V2;

public sealed class BotSteward : IBotProposer
{

    public static int LookMs { get; set; } = 2000;

    public static double LookAt { get; set; } = 0.5;

    public static int ClaimMs { get; set; } = 180000;

    public static long Offered { get; private set; }

    public static long Groundless { get; private set; }

    public static long Waiting { get; private set; }

    private static long _next;

    private static bool _looked;

    public string Name => "steward";

    public BotStanding Rung => BotStanding.Free;

    public BotDeed Propose(IBotWilful bot)
    {
        if (!BotEstate.Running || bot?.Self is not BotMobile { Deleted: false } body)
        {
            return null;
        }

        if (body.Map == null || body.Map == Map.Internal || body.Guild is not Guild guild)
        {
            return null;
        }

        if (BotUnderworld.Band(guild))
        {
            return null;
        }

        if (BotEstate.Hall(guild) != null)
        {
            return null;
        }

        if (BotEstate.Standing >= BotEstate.MaxHalls)
        {
            return null;
        }

        var fund = BotEstate.Fund(guild);

        if (fund < BotEstate.Price * LookAt)
        {
            return null;
        }

        var now = Core.TickCount;

        if (Claimed(guild, now))
        {
            return null;
        }

        if (_looked && now - _next < 0)
        {
            return null;
        }

        _looked = true;
        _next = now + LookMs;

        if (!BotPlot.Find(body, BotSeat.Of(guild), out var plot))
        {
            Groundless++;

            return null;
        }

        if (fund < BotEstate.Price)
        {
            Waiting++;

            return null;
        }

        Offered++;
        BotOffice.Offering(Office, guild);

        return new BotHall(guild, body.Map, plot);
    }

    public const string Office = "steward";

    private static bool Claimed(Guild guild, long now) => BotOffice.Busy(Office, guild);

    public static void Hold(Guild guild) => BotOffice.Hold(Office, guild, ClaimMs);

    public static void Release(Guild guild) => BotOffice.Release(Office, guild);

    public static string Describe() =>
        $"the steward offered a hall {Offered} times, held off {Waiting} times with the ground in hand and the money not, and found nothing to build on {Groundless} times";

    public static void Forget()
    {
        Offered = 0;
        Groundless = 0;
        Waiting = 0;
        _next = 0;
        _looked = false;
    }
}
