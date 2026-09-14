using System;
using System.Collections.Generic;
using Server.Guilds;
using Server.Items;
using Server.Mobiles;
using Server.Multis;

namespace Server.BotAI.V2;

/// <summary>
/// The guild's own counter: finding the merchant in the hall, putting goods on it, buying off it, and
/// carrying its takings back to the guild.
///
/// <para>
/// <b>Why a shop in the hall is worth anything at all, when the bots already have a market.</b> A stall on
/// <see cref="BotAuction"/> holds its stock out of the world and can be bought from anywhere, so bot-to-bot
/// trade has never needed a building. What a stall cannot do is stand still: it belongs to a bot, and this
/// population is rebuilt at every restart. A merchant in a hall is saved with the house, so what is left on
/// its shelf outlives the bots that put it there — and Patrick can walk in and buy it.
/// </para>
///
/// <para>
/// <b>And the errand it removes is a real one.</b> Reagents, bandages, tools and bottles are bought from
/// shopkeepers in Britain, and that walk is where three percent of the population's time was going. One
/// member fetching a batch for the hall spares every other member the journey. The gold still leaves the
/// world at the town counter — it is the same purchase — but it leaves once instead of nine times, and
/// nobody crosses the island for a bandage.
/// </para>
///
/// <para>
/// <b>Everything here goes through the engine's own bookkeeping rather than round it.</b> Dropping into the
/// merchant's pack makes <c>PlayerVendor.OnSubItemAdded</c> open a lot at 999gp, and the price on that lot
/// is public, so a shelf can be stocked without a patch and without a gump. Buying repeats, line for line,
/// what the buy gump's OK button does: pocket first then bank, the goods into the buyer's pack, the price
/// into the shopkeeper's purse. Anything cheaper than that would be minting.
/// </para>
///
/// <para>
/// <b>The shopkeeper has to be paid or it dies.</b> In this era the engine charges a vendor
/// <c>20 + (shelf value - 500) / 500</c> every UO day, which is two real hours, and destroys it the first
/// time the bill is larger than its purse — a hired merchant is 1,252gp of the guild's money walking away
/// while nobody is looking. It is seeded with 1,000gp by the engine, so an empty one lives about four days;
/// a well-stocked one much less. See <see cref="Wage"/>, which is why the supply errand tops it up.
/// </para>
/// </summary>
public static class BotShelf
{
    public static int Reach => BotShops.CounterReach;

    public static double Markup { get; set; } = 1.0;

    public static int Float { get; set; } = 240;

    public static int Wages { get; set; } = 120;

    public static TimeSpan Settling { get; } = TimeSpan.FromMinutes(1.0);

    public static long Stocked { get; private set; }

    public static long StockedWorth { get; private set; }

    public static long Refused { get; private set; }

    public static long Unstacked { get; private set; }

    public static long Slotless { get; private set; }

    public static long Lost { get; private set; }

    public static long Unpriced { get; private set; }

    public static long Taken { get; private set; }

    public static long Paid { get; private set; }

    public static long Gathered { get; private set; }

    public static long Waged { get; private set; }

    public static PlayerVendor Of(Guild guild) => Of(BotEstate.Hall(guild));

    public static PlayerVendor Of(Mobile bot) => Of(BotEstate.Hall(bot?.Guild as Guild));

    public static PlayerVendor Of(BaseHouse hall)
    {
        var standing = hall?.PlayerVendors;

        if (standing == null)
        {
            return null;
        }

        for (var i = 0; i < standing.Count; i++)
        {
            if (standing[i] is { Deleted: false, Map: not null } merchant && merchant.Map != Map.Internal)
            {
                return merchant;
            }
        }

        return null;
    }

    public static int Held(PlayerVendor merchant, Type kind)
    {
        var pack = merchant?.Backpack;

        if (pack == null || kind == null)
        {
            return 0;
        }

        var many = 0;
        var lots = pack.Items;

        for (var i = 0; i < lots.Count; i++)
        {
            var lot = lots[i];

            if (!lot.Deleted && kind.IsInstanceOfType(lot))
            {
                many += Math.Max(1, lot.Amount);
            }
        }

        return many;
    }

    public static Item Offer(PlayerVendor merchant, Type kind, out int price)
    {
        price = 0;

        var pack = merchant?.Backpack;

        if (pack == null || kind == null)
        {
            return null;
        }

        Item best = null;
        var lots = pack.Items;

        for (var i = 0; i < lots.Count; i++)
        {
            var lot = lots[i];

            if (lot.Deleted || !kind.IsInstanceOfType(lot))
            {
                continue;
            }

            var vi = merchant.GetVendorItem(lot);

            if (vi is not { Valid: true, IsForSale: true } || vi.Created + Settling > Core.Now)
            {
                continue;
            }

            if (best == null || vi.Price < price)
            {
                best = lot;
                price = vi.Price;
            }
        }

        return best;
    }

    public static bool Put(PlayerVendor merchant, Item goods, int price)
    {
        var pack = merchant?.Backpack;

        if (pack == null || goods is not { Deleted: false } || price < 0)
        {
            return false;
        }

        var kind = goods.GetType();
        var owner = merchant.Owner is { Deleted: false } held ? held : merchant;

        var standing = Lot(merchant, kind);
        var already = standing == null ? 0 : merchant.GetVendorItem(standing)?.Price ?? 0;

        if (!pack.TryDropItem(owner, goods, false))
        {
            Refused++;

            if (Standing(merchant, goods))
            {
                Unstacked++;
            }
            else
            {
                Slotless++;
            }

            return false;
        }

        var merged = goods.Deleted || goods.Parent != pack;
        var lot = merged ? Lot(merchant, kind) : goods;

        if (lot == null)
        {
            Lost++;

            return false;
        }

        var vi = merchant.GetVendorItem(lot);

        if (vi == null)
        {
            Unpriced++;

            return false;
        }

        vi.Price = merged && ReferenceEquals(lot, standing) ? already + price : price;
        vi.Description = "";
        lot.InvalidateProperties();

        Stocked++;
        StockedWorth += price;

        return true;
    }

    public static bool Standing(PlayerVendor merchant, Item goods)
    {
        var pack = merchant?.Backpack;

        if (pack == null || goods == null)
        {
            return false;
        }

        var lots = pack.Items;

        for (var i = 0; i < lots.Count; i++)
        {
            if (lots[i] is not Container && !lots[i].Deleted && lots[i].CanStackWith(goods))
            {
                return true;
            }
        }

        return false;
    }

    public static bool Room(PlayerVendor merchant, Type kind)
    {
        var pack = merchant?.Backpack;

        if (pack == null || kind == null)
        {
            return false;
        }

        if (Lot(merchant, kind) is { Deleted: false, Stackable: true })
        {
            return true;
        }

        var most = pack.MaxItems;

        return most <= 0 || pack.TotalItems < most;
    }

    private static Item Lot(PlayerVendor merchant, Type kind)
    {
        var lots = merchant.Backpack.Items;

        for (var i = 0; i < lots.Count; i++)
        {
            if (!lots[i].Deleted && kind.IsInstanceOfType(lots[i]))
            {
                return lots[i];
            }
        }

        return null;
    }

    public static int Take(Mobile buyer, PlayerVendor merchant, Item lot, out string refused)
    {
        refused = null;

        var pack = buyer?.Backpack;

        if (pack == null || merchant is not { Deleted: false } || lot is not { Deleted: false })
        {
            refused = "there is no shopkeeper to buy from";

            return 0;
        }

        if (!buyer.InRange(merchant.Location, Reach))
        {
            refused = "it is not close enough to the counter";

            return 0;
        }

        var vi = merchant.GetVendorItem(lot);

        if (vi is not { Valid: true, IsForSale: true } || !lot.IsChildOf(merchant.Backpack))
        {
            refused = "the shopkeeper would not sell that";

            return 0;
        }

        if (vi.Created + Settling > Core.Now)
        {
            refused = "the lot has only just gone out";

            return 0;
        }

        var price = vi.Price;
        var purse = BotYield.Wealth(buyer);

        if (purse < price)
        {
            refused = $"{purse}gp will not buy a lot at {price}gp";

            return 0;
        }

        var many = Math.Max(1, lot.Amount);

        var fromPack = pack.ConsumeUpTo(typeof(Gold), price);
        var owing = price - fromPack;

        if (owing > 0 && !Banker.Withdraw(buyer, owing))
        {
            Refund(buyer, pack, fromPack);

            refused = $"{fromPack}gp in the pack and the bank would not find the other {owing}gp";

            return 0;
        }

        if (!buyer.PlaceInBackpack(lot))
        {
            Refund(buyer, pack, price);

            refused = "there is no room in the pack for it";

            return 0;
        }

        merchant.HoldGold += price;

        Taken++;
        Paid += price;

        return many;
    }

    private static void Refund(Mobile buyer, Container pack, int coins)
    {
        if (coins <= 0 || buyer == null)
        {
            return;
        }

        if (Banker.Deposit(buyer, coins))
        {
            return;
        }

        var back = new Gold(coins);

        if (pack == null || !pack.TryDropItem(buyer, back, false))
        {
            back.MoveToWorld(buyer.Location, buyer.Map);
        }
    }

    public static int Collect(Mobile member, PlayerVendor merchant)
    {
        if (member == null || merchant is not { Deleted: false })
        {
            return 0;
        }

        var spare = merchant.HoldGold - Float;

        if (spare <= 0)
        {
            return 0;
        }

        var given = merchant.GiveGold(member, spare);

        Gathered += given;

        return given;
    }

    public static int Wage(BotMobile member, PlayerVendor merchant)
    {
        if (member == null || merchant is not { Deleted: false })
        {
            return 0;
        }

        var purse = merchant.BankAccount + merchant.HoldGold;

        if (purse >= Wages)
        {
            return 0;
        }

        var owing = Float - purse;
        var pack = member.Backpack;

        if (pack == null || owing <= 0)
        {
            return 0;
        }

        var wealth = BotYield.Wealth(member);

        if (wealth < owing && !BotGuilds.Stand(member, owing - wealth))
        {
            return 0;
        }

        var carried = pack.GetAmount(typeof(Gold));

        if (carried < owing && !Banker.Withdraw(member, owing - carried))
        {
            return 0;
        }

        if (!pack.ConsumeTotal(typeof(Gold), owing))
        {
            return 0;
        }

        merchant.BankAccount += owing;

        Waged += owing;

        return owing;
    }

    public static (int Shops, int Lots, int Worth, int Purse) Standing()
    {
        var shops = 0;
        var lots = 0;
        var worth = 0;
        var purse = 0;

        foreach (var hall in BotEstate.Halls)
        {
            var merchant = Of(hall);

            if (merchant == null)
            {
                continue;
            }

            shops++;
            purse += merchant.BankAccount + merchant.HoldGold;

            var pack = merchant.Backpack;

            if (pack == null)
            {
                continue;
            }

            var held = pack.Items;

            for (var i = 0; i < held.Count; i++)
            {
                var vi = merchant.GetVendorItem(held[i]);

                if (vi is not { Valid: true, IsForSale: true })
                {
                    continue;
                }

                lots++;
                worth += vi.Price;
            }
        }

        return (shops, lots, worth, purse);
    }

    public static string Describe()
    {
        var (shops, lots, worth, purse) = Standing();

        return $"{shops} guild counters holding {lots} lots worth {worth}gp with {purse}gp in their tills; "
            + $"{Stocked} lots put out for {StockedWorth}gp, {Refused} turned away for want of room, "
            + $"{Lost} that went in and could not be found again, {Unpriced} the engine opened no lot for "
            + $"({Unstacked} of the refusals had a lot standing that the engine says would have taken them, {Slotless} had neither lot nor slot), "
            + $"{Taken} bought back off them for {Paid}gp, {Gathered}gp carried into the guilds and {Waged}gp paid in wages";
    }

    public static void Forget()
    {
        Stocked = 0;
        StockedWorth = 0;
        Refused = 0;
        Lost = 0;
        Unpriced = 0;
        Unstacked = 0;
        Slotless = 0;
        Taken = 0;
        Paid = 0;
        Gathered = 0;
        Waged = 0;
    }

    public static void Kinds(PlayerVendor merchant, HashSet<Type> into)
    {
        var pack = merchant?.Backpack;

        if (pack == null || into == null)
        {
            return;
        }

        var lots = pack.Items;

        for (var i = 0; i < lots.Count; i++)
        {
            if (!lots[i].Deleted)
            {
                into.Add(lots[i].GetType());
            }
        }
    }
}
