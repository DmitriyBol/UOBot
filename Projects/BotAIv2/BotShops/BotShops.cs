using System;
using System.Collections.Generic;
using Server.Items;
using Server.Logging;
using Server.Mobiles;

namespace Server.BotAI.V2;

/// <summary>
/// Buying things from the shopkeepers. The capability, available to every bot rather than to a trade.
///
/// <para>
/// <b>A purchase goes through the shopkeeper's own <c>OnBuyItems</c>.</b> That is the same call a player's
/// shop window makes: it charges the shard's real prices out of the pack and the account behind it, and
/// hands over real goods. A bot is a customer on exactly the terms a player is, which is worth more than it
/// sounds — every price scalar, every access rule and every stock limit applies without being reimplemented,
/// and nothing here can accidentally invent goods or money.
/// </para>
///
/// <para>
/// <b>Two lines that are not obvious and both were paid for in the first version.</b> Shelves refill on a
/// timer that is only wound when somebody <em>opens</em> the shop window — and bots never open one, so a
/// shop a bot has cleaned out stays empty for good unless the restock is asked for. And prices carry the
/// shard's own scalars, brought up to date only on demand.
/// </para>
/// </summary>
public static class BotShops
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotShops));

    public static int Reach { get; set; } = 160;

    public static int CounterReach { get; set; } = 3;

    public static int MaxShops { get; set; } = 96;

    private static readonly List<BaseVendor> _shops = [];

    private static readonly List<(Map Map, Point3D Where)> _swept = [];

    public static IReadOnlyList<BaseVendor> Shops => _shops;

    public static long Bought { get; private set; }

    public static long Spent { get; private set; }

    public static long Sold { get; private set; }

    public static long Earned { get; private set; }

    public static long Walled { get; private set; }

    public static int RepickLimit { get; set; } = 2;

    public static long Repicked { get; private set; }

    public static BaseVendor Next(IBotWilful bot, BaseVendor emptied, Type wanted, ref int tries)
    {
        if (tries >= RepickLimit || emptied == null || wanted == null || Sells(emptied, wanted, out _))
        {
            return null;
        }

        var next = Nearest(bot, wanted);

        if (next == null || next == emptied)
        {
            return null;
        }

        tries++;
        Repicked++;

        return next;
    }

    public static bool Swept(Map map, Point3D around)
    {
        for (var i = 0; i < _swept.Count; i++)
        {
            if (_swept[i].Map == map && Utility.InRange(_swept[i].Where, around, Reach / 2))
            {
                return true;
            }
        }

        return false;
    }

    public static int Survey(Map map, Point3D around)
    {
        if (map == null || map == Map.Internal || Swept(map, around))
        {
            return 0;
        }

        _swept.Add((map, around));

        var found = 0;

        foreach (var vendor in map.GetMobilesInRange<BaseVendor>(around, Reach))
        {
            if (vendor.Deleted || _shops.Count >= MaxShops || !BotPopulation.Within(map, vendor.Location))
            {
                continue;
            }

            if (_shops.Contains(vendor))
            {
                continue;
            }

            _shops.Add(vendor);
            found++;
        }

        logger.Information(
            "Found {Found} shopkeepers within {Reach} tiles of {Where} on {Map} (now {Total})",
            found,
            Reach,
            around,
            map,
            _shops.Count
        );

        return found;
    }

    private static readonly Dictionary<Type, long> _dry = [];

    public static List<(Type Kind, long Times)> Dry()
    {
        List<(Type Kind, long Times)> found = [];

        foreach (var (kind, times) in _dry)
        {
            found.Add((kind, times));
        }

        found.Sort((a, b) => b.Times.CompareTo(a.Times));

        return found;
    }

    private static string Driest()
    {
        Type worst = null;
        long most = 0;

        foreach (var (kind, times) in _dry)
        {
            if (times > most)
            {
                most = times;
                worst = kind;
            }
        }

        return worst == null ? "nothing" : $"{worst.Name} ({most} times)";
    }

    public static int KeepEveryMs { get; set; } = 60000;

    public static long Refills { get; private set; }

    private static long _kept;

    private static bool _everKept;

    public static void Keep()
    {
        var now = Core.TickCount;

        if (_everKept && now - (_kept + KeepEveryMs) < 0)
        {
            return;
        }

        _kept = now;
        _everKept = true;

        for (var i = 0; i < _shops.Count; i++)
        {
            var vendor = _shops[i];

            if (vendor is not { Deleted: false } || !vendor.IsActiveSeller)
            {
                continue;
            }

            if (Core.Now - vendor.LastRestock <= vendor.RestockDelay)
            {
                continue;
            }

            vendor.Restock();
            Refills++;
        }
    }

    public static bool Sells(BaseVendor vendor, Type wanted, out GenericBuyInfo entry)
    {
        entry = null;

        if (vendor == null || vendor.Deleted || wanted == null || !vendor.IsActiveSeller)
        {
            return false;
        }

        var offered = vendor.GetBuyInfo();

        for (var i = 0; i < offered.Length; i++)
        {
            if (offered[i] is not GenericBuyInfo info || info.Type != wanted || info.Amount <= 0)
            {
                continue;
            }

            entry = info;

            return true;
        }

        return false;
    }

    public const string ShopKind = "shop";

    public static BaseVendor Nearest(IBotWilful bot, Type wanted)
    {
        var body = bot?.Self;
        var ledger = bot?.Resolve?.Ledger;

        if (body == null || ledger == null)
        {
            return Nearest(body, wanted);
        }

        BaseVendor best = null;
        var bestAway = double.MaxValue;
        var map = body.Map;

        if (map == null || map == Map.Internal || wanted == null)
        {
            return null;
        }

        for (var i = 0; i < _shops.Count; i++)
        {
            var vendor = _shops[i];

            if (vendor.Deleted || vendor.Map != map || !BotPopulation.Within(map, vendor.Location))
            {
                continue;
            }

            if (ledger.Cautious(ShopKind, map, vendor.Location) || !Sells(vendor, wanted, out _))
            {
                continue;
            }

            if (BotReach.Ask(map, body.Location, vendor.Location, BotArrival.Within(CounterReach)) == BotReachVerdict.Sealed)
            {
                Walled++;

                continue;
            }

            var away = body.GetDistanceToSqrt(vendor.Location);

            if (away >= bestAway)
            {
                continue;
            }

            best = vendor;
            bestAway = away;
        }

        return best;
    }

    public static BaseVendor Nearest(Mobile bot, Type wanted)
    {
        var map = bot?.Map;

        if (map == null || map == Map.Internal || wanted == null)
        {
            return null;
        }

        BaseVendor best = null;
        var bestAway = double.MaxValue;

        for (var i = 0; i < _shops.Count; i++)
        {
            var vendor = _shops[i];

            if (vendor.Deleted || vendor.Map != map || !BotPopulation.Within(map, vendor.Location))
            {
                continue;
            }

            if (!Sells(vendor, wanted, out _))
            {
                continue;
            }

            if (BotReach.Ask(map, bot.Location, vendor.Location, BotArrival.Within(CounterReach)) == BotReachVerdict.Sealed)
            {
                Walled++;

                continue;
            }

            var away = bot.GetDistanceToSqrt(vendor.Location);

            if (away >= bestAway)
            {
                continue;
            }

            best = vendor;
            bestAway = away;
        }

        return best;
    }

    public static int Price(BaseVendor vendor, Type wanted) => Sells(vendor, wanted, out var entry) ? entry.Price : 0;

    public static int Shelf(IBotWilful bot, Type kind, int fallback, bool survey = false)
    {
        var body = bot?.Self;

        if (body == null || kind == null)
        {
            return fallback;
        }

        if (survey)
        {
            Survey(body.Map, body.Location);
        }

        var shop = Nearest(bot, kind);
        var price = shop == null ? 0 : Price(shop, kind);

        return price > 0 ? price : fallback;
    }

    public static bool Buys(BaseVendor vendor, Item item, out int price)
    {
        price = 0;

        if (vendor == null || vendor.Deleted || item == null || item.Deleted || !vendor.IsActiveBuyer)
        {
            return false;
        }

        var counters = vendor.GetSellInfo();

        for (var i = 0; i < counters.Length; i++)
        {
            var counter = counters[i];

            if (!counter.IsSellable(item))
            {
                continue;
            }

            price = counter.GetSellPriceFor(item);

            return price > 0;
        }

        return false;
    }

    public static BaseVendor Buyer(IBotWilful bot, Item item, out int price)
    {
        price = 0;

        var body = bot?.Self;
        var ledger = bot?.Resolve?.Ledger;

        if (body == null)
        {
            return null;
        }

        if (ledger == null)
        {
            return Buyer(body, item, out price);
        }

        var map = body.Map;

        if (map == null || map == Map.Internal || item == null)
        {
            return null;
        }

        BaseVendor best = null;
        var bestAway = double.MaxValue;

        for (var i = 0; i < _shops.Count; i++)
        {
            var vendor = _shops[i];

            if (vendor.Deleted || vendor.Map != map || !BotPopulation.Within(map, vendor.Location))
            {
                continue;
            }

            if (ledger.Cautious(ShopKind, map, vendor.Location) || !Buys(vendor, item, out var paying))
            {
                continue;
            }

            var away = body.GetDistanceToSqrt(vendor.Location);

            if (away >= bestAway)
            {
                continue;
            }

            best = vendor;
            bestAway = away;
            price = paying;
        }

        return best;
    }

    public static BaseVendor Buyer(Mobile bot, Item item, out int price)
    {
        price = 0;

        var map = bot?.Map;

        if (map == null || map == Map.Internal || item == null)
        {
            return null;
        }

        BaseVendor best = null;
        var bestAway = double.MaxValue;

        for (var i = 0; i < _shops.Count; i++)
        {
            var vendor = _shops[i];

            if (vendor.Deleted || vendor.Map != map || !BotPopulation.Within(map, vendor.Location))
            {
                continue;
            }

            if (!Buys(vendor, item, out var paying))
            {
                continue;
            }

            var away = bot.GetDistanceToSqrt(vendor.Location);

            if (away >= bestAway)
            {
                continue;
            }

            best = vendor;
            bestAway = away;
            price = paying;
        }

        return best;
    }

    public static int Sell(IBotWilful bot, BaseVendor vendor, List<Item> goods) =>
        Sell(bot, vendor, goods, out _);

    public static int Sell(IBotWilful bot, BaseVendor vendor, List<Item> goods, out int units)
    {
        units = 0;

        var body = bot?.Self;
        var pack = body?.Backpack;

        if (pack == null || vendor == null || vendor.Deleted || goods == null || goods.Count == 0)
        {
            return 0;
        }

        if (!body.InRange(vendor.Location, CounterReach) || !vendor.IsActiveBuyer)
        {
            return 0;
        }

        List<SellItemResponse> order = [];

        for (var i = 0; i < goods.Count; i++)
        {
            var item = goods[i];

            if (item == null || item.Deleted || !item.Movable || !item.IsStandardLoot())
            {
                continue;
            }

            if (item.RootParent != body || !Buys(vendor, item, out _))
            {
                continue;
            }

            var amount = Math.Max(1, item.Amount);

            units += amount;

            order.Add(new SellItemResponse(item, amount));
        }

        if (order.Count == 0)
        {
            units = 0;

            return 0;
        }

        var before = BotYield.Wealth(body);

        if (!vendor.OnSellItems(body, order))
        {
            units = 0;

            return 0;
        }

        var earned = BotYield.Wealth(body) - before;

        if (earned <= 0)
        {
            units = 0;

            return 0;
        }

        Sold += order.Count;
        Earned += earned;

        logger.Information(
            "{Name} sold {Units} things to {Vendor} for {Gold}gp",
            body.Name,
            units,
            vendor.Name,
            earned
        );

        return earned;
    }

    public static int Buy(IBotWilful bot, BaseVendor vendor, Type wanted, int amount) =>
        Buy(bot, vendor, wanted, amount, out _);

    public static int Buy(IBotWilful bot, BaseVendor vendor, Type wanted, int amount, out string refused)
    {
        refused = null;

        var body = bot?.Self;
        var pack = body?.Backpack;

        if (pack == null || vendor == null || vendor.Deleted || amount <= 0 || !vendor.IsActiveSeller)
        {
            refused = "there is no shopkeeper to buy from";

            return 0;
        }

        if (!body.InRange(vendor.Location, CounterReach))
        {
            refused = "it is not close enough to the counter";

            return 0;
        }

        if (Core.Now - vendor.LastRestock > vendor.RestockDelay)
        {
            vendor.Restock();
        }

        vendor.UpdateBuyInfo();

        if (!Sells(vendor, wanted, out var entry) || entry.Price <= 0)
        {
            _dry.TryGetValue(wanted, out var turned);
            _dry[wanted] = turned + 1;

            refused = $"the shelf holds no {wanted.Name} at any price";

            return 0;
        }

        if (entry.GetDisplayEntity() is not { Deleted: false } display)
        {
            refused = $"the shopkeeper has no {wanted.Name} to show";

            return 0;
        }

        var price = entry.Price;
        var purse = BotYield.Wealth(body);
        var affordable = Math.Min(Math.Min(amount, entry.Amount), purse / price);

        if (affordable <= 0)
        {
            refused = purse < price
                ? $"{purse}gp will not buy one {wanted.Name} at {price}gp"
                : $"the shelf is down to {entry.Amount} {wanted.Name}";

            return 0;
        }

        var bill = affordable * price;
        var carried = pack.GetAmount(typeof(Gold));

        if (carried < bill)
        {
            var owing = bill - carried;

            if (!Banker.Withdraw(body, owing))
            {
                refused = $"{carried}gp in the pack and the bank would not find the other {owing}gp";

                return 0;
            }

            var drawn = new Gold(owing);

            if (!pack.TryDropItem(body, drawn, false))
            {
                drawn.Delete();
                Banker.Deposit(body, owing);

                refused = $"the pack would not hold the {owing}gp it drew to pay with";

                return 0;
            }
        }

        List<BuyItemResponse> order = [new BuyItemResponse(display.Serial, affordable)];

        if (!vendor.OnBuyItems(body, order))
        {
            refused = $"{vendor.Name} turned down {affordable} {wanted.Name} at {price}gp with {purse}gp to hand";

            return 0;
        }

        Bought += affordable;
        Spent += affordable * price;

        logger.Information(
            "{Name} bought {Amount} {Item} from {Vendor} for {Cost}gp",
            body.Name,
            affordable,
            wanted.Name,
            vendor.Name,
            affordable * price
        );

        return affordable;
    }

    public static void Reset()
    {
        _shops.Clear();
        _swept.Clear();

        Bought = 0;
        Walled = 0;
        Spent = 0;
        Sold = 0;
        Earned = 0;
        Refills = 0;
        _dry.Clear();
        _kept = 0;
        _everKept = false;
    }

    public static string Describe() =>
        $"{_shops.Count} shopkeepers known from {_swept.Count} sweeps; {Bought} things bought for {Spent}gp, {Sold} sold for {Earned}gp, {Walled} counters passed over for having no way through to them, {Refills} shelves refilled on their own hour, {Repicked} errands sent on to another shopkeeper when the shelf emptied before they arrived, {BotPeddle.SoldOnTheWay} loads bought off their stall while they were being carried to one and {BotPeddle.HandedBack} handed back into the pack on the way and sold from it, {BotRestock.FellThrough} errands that found their guild's shelf bought out and went on to one, {BotRestock.Restalled} stall purchases that found the stall bought out and took the next cheapest; the town is oftenest out of {Driest()}";
}
