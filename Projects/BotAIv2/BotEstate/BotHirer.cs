using System.Collections.Generic;
using Server.Guilds;
using Server.Items;
using Server.Mobiles;

namespace Server.BotAI.V2;

/// <summary>
/// Whether the guild wants a merchant in its hall, can pay for one, and who is going to fetch it.
///
/// <para>
/// The third of the estate's proposers and the same shape as the other two — any member may go, one at a
/// time per guild, and what the guild can afford is asked here rather than inside the work. A hall must
/// exist first, which makes this the second piece of work on the shard that exists because the population
/// built something.
/// </para>
///
/// <para>
/// It is asked for last of the three: a hall, then the tools its trade needs, then a shopkeeper to sell what
/// those tools make. That order is not enforced anywhere — it falls out of what each costs.
/// </para>
/// </summary>
public sealed class BotHirer : IBotProposer
{
    public static int ClaimMs { get; set; } = 240000;

    public static long Offered { get; private set; }

    public static long Wanting { get; private set; }

    public static int Short { get; private set; }

    public static int Nearest { get; private set; }

    public static long Unsold { get; private set; }

    public static long Staffed { get; private set; }

    public static long Fitting { get; private set; }

    public static long Claimed { get; private set; }

    public const string Office = "hirer";

    public string Name => "hirer";

    public BotStanding Rung => BotStanding.Free;

    public BotDeed Propose(IBotWilful bot)
    {
        if (!BotEstate.Running || !BotEstate.Merchanting || bot?.Self is not BotMobile { Deleted: false } body)
        {
            return null;
        }

        if (body.Map == null || body.Map == Map.Internal || body.Guild is not Guild guild)
        {
            return null;
        }

        var hall = BotEstate.Hall(guild);

        if (hall is not { Deleted: false } || hall.Map != body.Map)
        {
            return null;
        }

        if (BotEstate.Merchants(hall) >= BotEstate.MostMerchants)
        {
            Staffed++;

            return null;
        }

        if (BotFittings.Wanting(hall, guild, out _))
        {
            Fitting++;

            return null;
        }

        var now = Core.TickCount;

        if (BotOffice.Busy(Office, guild))
        {
            Claimed++;

            return null;
        }

        var shop = BotShops.Nearest(bot, typeof(ContractOfEmployment));

        if (shop == null)
        {
            Unsold++;

            return null;
        }

        var price = BotShops.Price(shop, typeof(ContractOfEmployment));

        if (price <= 0)
        {
            Unsold++;

            return null;
        }

        var fund = BotEstate.Fund(guild);

        if (fund < price)
        {
            Wanting++;

            if (fund > Nearest)
            {
                Nearest = fund;
                Short = price - fund;
            }

            return null;
        }

        Offered++;
        BotOffice.Offering(Office, guild);

        return new BotHire(guild, hall, shop, price);
    }

    public static void Hold(Guild guild) => BotOffice.Hold(Office, guild, ClaimMs);

    public static void Release(Guild guild) => BotOffice.Release(Office, guild);

    public static string Describe() =>
        $"the hirer offered a merchant {Offered} times, held off {Wanting} times for want of the money"
        + (Wanting > 0 ? $" (the best-off guild that wanted one had {Nearest}gp free and was {Short} short)" : "")
        + $" and {Unsold} times for want of anybody selling a contract; "
        + $"{Staffed} halls already had their merchant, {Fitting} guilds were still fitting out a workshop first, {Claimed} already had somebody fetching one";

    public static void Forget()
    {
        Offered = 0;
        Wanting = 0;
        Unsold = 0;
        Short = 0;
        Nearest = 0;
        Staffed = 0;
        Fitting = 0;
        Claimed = 0;
    }
}
