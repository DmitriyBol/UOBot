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

    /// <summary>How far around a bot one sweep looks for shopkeepers.</summary>
    public static int Reach { get; set; } = 160;

    /// <summary>How near a shopkeeper a bot has to stand to trade with it.</summary>
    public static int CounterReach { get; set; } = 3;

    /// <summary>How many shopkeepers the population may remember.</summary>
    public static int MaxShops { get; set; } = 96;

    private static readonly List<BaseVendor> _shops = [];

    private static readonly List<(Map Map, Point3D Where)> _swept = [];

    public static IReadOnlyList<BaseVendor> Shops => _shops;

    public static long Bought { get; private set; }

    public static long Spent { get; private set; }

    public static long Sold { get; private set; }

    /// <summary>Gold this population has brought into the world over a counter. The only faucet there is.</summary>
    public static long Earned { get; private set; }

    /// <summary>Whether this patch of the world has already been swept for shopkeepers.</summary>
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

    /// <summary>
    /// Writes down every shopkeeper within reach. One spatial query — shopkeepers are mobiles, so unlike
    /// forges they answer one.
    /// </summary>
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

    /// <summary>
    /// Whether this shopkeeper sells the thing, and what it is asking.
    ///
    /// The entry is needed rather than only the price, because buying wants the serial of the display object
    /// the shopkeeper matches an order against — the same object a player's shop window shows a picture of.
    /// </summary>
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

    /// <summary>
    /// The ledger's key for "I could not get to this shop".
    ///
    /// <para>
    /// Per bot, for the reason the forges taught: a counter behind a river is behind a river only for whoever
    /// is on the wrong bank. A tailor whose nearest cloth shop cannot be reached used to be handed that same
    /// shop every time it asked — fourteen failed sewing trips in ten minutes, each one a walk that ended in a
    /// refusal.
    /// </para>
    /// </summary>
    public const string ShopKind = "shop";

    /// <summary>
    /// The nearest shopkeeper selling this that this bot has not lately failed to reach.
    /// </summary>
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

    /// <summary>The nearest remembered shopkeeper that sells this, or null.</summary>
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

    /// <summary>What this shopkeeper is asking for one of those, or zero if it does not sell them.</summary>
    public static int Price(BaseVendor vendor, Type wanted) => Sells(vendor, wanted, out var entry) ? entry.Price : 0;

    /// <summary>
    /// Whether this shopkeeper will <em>buy</em> this thing, and what it pays for one.
    ///
    /// <para>
    /// Asked of an actual item rather than of a type, because that is the only question the engine answers:
    /// <c>IsSellable</c> looks at the object, and a shopkeeper that buys daggers is being asked about this
    /// dagger. So a caller with nothing in hand has to find something to show.
    /// </para>
    /// </summary>
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

    /// <summary>
    /// The nearest shopkeeper that buys this and that <b>this bot</b> has not lately failed to reach.
    ///
    /// <para>
    /// <b>The selling side had no such question and the buying side did, which is the whole of why one
    /// shopkeeper could swallow an afternoon.</b> Gus buys iron ingots and stands on a plateau at height
    /// thirty; Calla walked at him thirty-one times in one hour on 26.08.2026 and Alden eight, because the
    /// nearest buyer was chosen on distance alone and nothing about the last thirty attempts entered into
    /// it. <see cref="Nearest(IBotWilful, Type)"/> has asked this since the counters were fixed; this side
    /// simply never grew the same overload.
    /// </para>
    ///
    /// <para>
    /// The bot's own note, not the shard's — another bot on the right side of the hill reaches Gus perfectly
    /// well, and telling everybody otherwise would close the only ingot buyer on the island.
    /// </para>
    /// </summary>
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

    /// <summary>
    /// The nearest remembered shopkeeper that buys this, and what it pays for one.
    ///
    /// <b>Distance only.</b> For asking what a thing is worth, where reachability is beside the point — see
    /// the overload above for choosing somewhere to walk to.
    /// </summary>
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

    /// <summary>
    /// Sells things over a counter and says how much gold came back.
    ///
    /// <para>
    /// <b>This is the only place in the project where gold enters the world.</b> Trade between bots moves coin
    /// about; a shopkeeper's purse creates it. That is why the first version drained its world by 110,900 in a
    /// night — every bot was pointed at the one faucet — and it is why nothing here decides on its own to come
    /// here: see <see cref="BotPeddle"/> for the one condition under which it is allowed, which is that the
    /// population has already been offered the goods and refused them.
    /// </para>
    ///
    /// <para>
    /// The takings are <b>measured rather than added up</b>, because the engine decides how much of the order
    /// it honours: it refuses anything not in the seller's own pack, anything immovable, anything not standard
    /// loot — which is a free guarantee that <em>bound gear cannot be sold</em>, since the bind marks it
    /// <c>Newbied</c> — and anything above its own per-visit limit. Believing the asking prices would be
    /// counting an intention.
    /// </para>
    /// </summary>
    public static int Sell(IBotWilful bot, BaseVendor vendor, List<Item> goods)
    {
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

            order.Add(new SellItemResponse(item, Math.Max(1, item.Amount)));
        }

        if (order.Count == 0)
        {
            return 0;
        }

        var before = BotYield.Wealth(body);

        if (!vendor.OnSellItems(body, order))
        {
            return 0;
        }

        var earned = BotYield.Wealth(body) - before;

        if (earned <= 0)
        {
            return 0;
        }

        Sold += order.Count;
        Earned += earned;

        logger.Information(
            "{Name} sold {Count} things to {Vendor} for {Gold}gp",
            body.Name,
            order.Count,
            vendor.Name,
            earned
        );

        return earned;
    }

    /// <summary>
    /// Buys up to <paramref name="amount"/> of something, and says how many were actually bought.
    ///
    /// <para>
    /// The affordability is worked out here rather than left to the shopkeeper, because the engine takes an
    /// order whole or not at all: an order for ten when the purse holds eight buys nothing and says nothing.
    /// </para>
    /// </summary>
    public static int Buy(IBotWilful bot, BaseVendor vendor, Type wanted, int amount) =>
        Buy(bot, vendor, wanted, amount, out _);

    /// <summary>
    /// The same purchase, and <paramref name="refused"/> says which gate closed when nothing was bought.
    ///
    /// <para>
    /// <b>Six ways to come away empty were printed as one sentence, and it hid the largest fault on the
    /// shard.</b> "The shop would not sell it" was written knowing there were three cases — the comment
    /// beside it says "sold out, priced out, or refused" — and reasoned that all three were the shop's
    /// business rather than the bot's. That is true of what to do next and false of what to log: on the night
    /// of 25.08.2026 this sentence appeared 1929 times in half an hour, against nought successful purchases
    /// of a bandage ever, and there was no way to tell an empty shelf from an empty purse without reading the
    /// source. A branch nobody can count is a branch nobody can fix — the same rule that governs the
    /// heartbeat lines governs this.
    /// </para>
    /// </summary>
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

        // Shelves empty as they are bought from and refill on a timer that is only wound when somebody opens
        // the shop window. Bots never open one, so without this a shop a bot has cleaned out stays empty.
        if (Core.Now - vendor.LastRestock > vendor.RestockDelay)
        {
            vendor.Restock();
        }

        // Prices carry the shard's own scalars and are only brought up to date on demand.
        vendor.UpdateBuyInfo();

        if (!Sells(vendor, wanted, out var entry) || entry.Price <= 0)
        {
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

        // <b>Money in the bank is only money if something fetches it, and nothing did.</b> The engine pays a
        // bill under two thousand gold out of the backpack and never looks at the account — while five places
        // in this project put coin <em>into</em> an account and not one takes any out, the sole withdrawal on
        // the shard being the bot market charging its own buyers. So the population's takings drained one way
        // into the bank and stayed there.
        //
        // <see cref="BotYield.Wealth"/> counts the account and the pocket together, and every affordability
        // judgement a bot makes rests on that sum. Either the sum is a lie or the money is reachable; this
        // makes it reachable, which is the half that keeps a rich bot able to buy a bandage. Withdrawn first
        // and carried second, and put straight back if the pack will not take it — the account is debited by
        // the withdrawal itself, so the other order would mint gold out of nothing.
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

    /// <summary>
    /// Everything forgotten. Shopkeepers are mobiles of a world that is being replaced, and a reference to
    /// one of those is a reference to a deleted object.
    /// </summary>
    public static void Reset()
    {
        _shops.Clear();
        _swept.Clear();

        Bought = 0;
        Spent = 0;
        Sold = 0;
        Earned = 0;
    }

    public static string Describe() =>
        $"{_shops.Count} shopkeepers known from {_swept.Count} sweeps; {Bought} things bought for {Spent}gp, {Sold} sold for {Earned}gp";
}
