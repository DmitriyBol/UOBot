using System.Collections.Generic;
using Server.Guilds;
using Server.Mobiles;

namespace Server.BotAI.V2;

public sealed class BotFitter : IBotProposer
{

    public static int ClaimMs { get; set; } = 120000;

    public static long Offered { get; private set; }

    public static long Wanting { get; private set; }

    public static long Furnished { get; private set; }

    public static long Homeless { get; private set; }

    public const string Office = "fitter";

    public string Name => "fitter";

    public BotStanding Rung => BotStanding.Free;

    public BotDeed Propose(IBotWilful bot)
    {
        if (!BotEstate.Running || !BotFittings.Running || bot?.Self is not BotMobile { Deleted: false } body)
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

        var hall = BotEstate.Hall(guild);

        if (hall is not { Deleted: false } || hall.Map != body.Map)
        {
            Homeless++;

            return null;
        }

        if (!BotFittings.Wanting(hall, guild, out var bench))
        {
            Furnished++;

            return null;
        }

        var now = Core.TickCount;

        if (BotOffice.Busy(Office, guild))
        {
            return null;
        }

        if (BotEstate.Fund(guild) < bench.Price)
        {
            Wanting++;

            return null;
        }

        Offered++;
        BotOffice.Offering(Office, guild);

        return new BotBench(guild, hall, bench);
    }

    public static void Hold(Guild guild) => BotOffice.Hold(Office, guild, ClaimMs);

    public static void Release(Guild guild) => BotOffice.Release(Office, guild);

    public static string Describe() =>
        $"the fitter offered a bench {Offered} times, held off {Wanting} times for want of the money, "
        + $"found {Furnished} halls already holding everything their guild wants and {Homeless} bots with no hall at all; {BotFittings.Fittings()}";

    public static void Forget()
    {
        Offered = 0;
        Wanting = 0;
        Furnished = 0;
        Homeless = 0;
    }
}
