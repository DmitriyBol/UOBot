using System;
using System.Collections.Generic;
using Server.Items;
using Server.Mobiles;

namespace Server.BotAI.V2;

/// <summary>
/// Taking what the population would not buy to somebody who will. The shard's only faucet.
///
/// <para>
/// <b>Every other coin in this world is a coin that already existed.</b> Trade between bots moves gold about;
/// a shopkeeper's purse creates it. Until this undertaking existed there was no gold at all — bots are born
/// with none and nothing minted any — so every piece of work that cost money to start failed on its first
/// beat and only digging, which costs nothing, could happen at all.
/// </para>
///
/// <para>
/// <b>The condition for coming here is the whole design.</b> A shopkeeper is not where goods go; it is where goods go
/// <em>when the population is not asking for them</em>: nobody has said a want for the kind, and nobody has been short of it
/// lately (<see cref="BotShopkeep.Demanded"/>). Under the auction that was a stall that had stood unsold for ten minutes; since
/// the stalls went (01.10.2026) it is read straight off the wants and the shortages, and the goods come out of the pack or, at a
/// bank, out of the bot's own bank box (<see cref="BotMarket.FromBank"/>), where the porter put what it did not keep.
/// </para>
///
/// <para>
/// It matters that this is narrow. The first version pointed every bot at this faucet and lost 110,900 gold
/// in a night in the other direction: 67k mined against 156k spent over counters, a population of 116 traders
/// to 14 fighters, and a median purse of 57 against a poverty line of 800. Selling to a shopkeeper has to be
/// what a bot does with what nobody wanted, not what a bot does.
/// </para>
///
/// <para>
/// And it is deliberately <b>the wrong way to make a living</b>, which the numbers already say without being
/// told to: a blacksmith pays 4 for an iron ingot and a bot asks 6; a tailor pays 6 for a shirt and a bot
/// asks 12. The counter is the floor under the market, not a competitor to it.
/// </para>
/// </summary>
public sealed class BotPeddle : BotDeed
{
    public const string Trade = "peddle";

    public static double WorkMinutes { get; set; } = 3.0;

    private readonly BaseVendor _shop;

    private readonly Type _kind;

    private readonly string _label;

    private readonly int _units;

    private readonly int _price;

    private int _earned;

    private int _sold;

    private bool _begun;

    public static long FromBox { get; private set; }

    public static long FromBoxUnits { get; private set; }

    private readonly bool _fromBox;

    private readonly double _factor;

    private readonly int _passed;

    private int _premium;

    public BotPeddle(BaseVendor shop, Type kind, string label, int units, int price, bool fromBox = false, double factor = 1.0, int passed = 0)
    {
        _fromBox = fromBox;
        _shop = shop;
        _kind = kind;
        _label = label;
        _units = Math.Max(1, units);
        _price = Math.Max(1, price);
        _factor = Math.Max(1.0, factor);
        _passed = Math.Max(0, passed);
    }

    public override string Kind => Trade;

    public override bool Steadfast => true;

    public override Map Map => _shop?.Map;

    public override Point3D Where => _shop?.Location ?? Point3D.Zero;

    public override double Expects => Math.Max(1.0, (_units * (double)_price * _factor + Extra) / Math.Max(0.5, WorkMinutes));

    public int Extra { get; init; }

    public bool BoxedOut { get; init; }

    public int Most { get; init; }

    public override double Minutes => WorkMinutes;

    public override SkillName? Trains => null;

    public override int Outlay => 0;

    public override bool AtCounter => false;

    public override double Coin => 1.0;

    public override int Made => 0;

    public override string Stage =>
        _sold > 0 ? $"sold {_sold} {_label} for {_earned}gp" : $"taking {_units} {_label} to {_shop?.Name}";

    public override bool Bend(IBotWilful bot)
    {
        if (_shop == null)
        {
            return false;
        }

        bot?.Resolve?.Ledger?.Beware(BotShops.ShopKind, _shop.Map, _shop.Location);

        return false;
    }

    public override BotDoing Advance(IBotWilful bot)
    {
        var body = bot?.Self;

        if (body == null)
        {
            return BotDoing.Failed("no body");
        }

        if (_shop == null || _shop.Deleted || _shop.Map == null || _shop.Map == Map.Internal)
        {
            return BotDoing.Failed("the shopkeeper is gone");
        }

        if (!_begun)
        {
            _begun = true;

            if (_fromBox)
            {
                var drawn = BotMarket.FromBank(bot, _kind, _units);

                if (drawn <= 0 && Gather(body, _kind).Count == 0)
                {
                    return BotDoing.Failed($"could not take the {_label} out of its bank box");
                }

                if (drawn > 0)
                {
                    FromBox++;
                    FromBoxUnits += drawn;
                }
            }
        }

        if (!body.InRange(_shop.Location, BotShops.CounterReach))
        {
            return BotDoing.Walk(_shop.Map, _shop, BotArrival.Within(BotShops.CounterReach), $"to {_shop.Name} with {_label}");
        }

        var goods = Upto(Beyond(bot, Gather(body, _kind), _kind), Most);

        if (goods.Count == 0)
        {
            return BotDoing.Failed($"no {_label} left in the pack by the time it got here");
        }

        _earned = BotShops.Sell(bot, _shop, goods, out var units, out _premium);

        BotOre.Release(body);

        if (_earned <= 0)
        {
            return BotDoing.Failed("the shopkeeper would not buy it after all");
        }

        _sold = units;

        BotSurplus.CountIfCapped(bot, BotSurplus.Shop, _kind, units);

        if (_passed > 0 && BotCapital.In(_shop.Location))
        {
            BotCapital.Delivered(units, _earned + _premium, _passed);
        }

        var extra = BotPeddler.Hunter(body) ? Rest(bot, body, _kind) : null;
        var also = "";

        if (extra is { Count: > 0 })
        {
            var more = BotShops.Sell(bot, _shop, extra, out var moreUnits, out var morePremium);

            if (more > 0)
            {
                AlsoSold++;
                AlsoUnits += moreUnits;
                AlsoGold += more;

                _earned += more;
                _premium += morePremium;
                _sold += moreUnits;

                also = $" and {moreUnits} other things of its loot";
            }
        }

        return _premium > 0
            ? BotDoing.Done($"{units} {_label}{also} for {_earned}gp and {_premium}gp more from the city at the capital's counter")
            : BotDoing.Done($"{units} {_label}{also} for {_earned}gp");
    }

    public static long AlsoSold { get; private set; }

    public static long AlsoUnits { get; private set; }

    public static long AlsoGold { get; private set; }

    public static void ForgetCounts()
    {
        AlsoSold = 0;
        AlsoUnits = 0;
        AlsoGold = 0;
    }

    private static List<Item> Rest(IBotWilful bot, Mobile body, Type but)
    {
        List<Item> found = [];

        var pack = body.Backpack;

        if (pack == null)
        {
            return found;
        }

        var bond = (body as BotMobile)?.Bond;
        var keep = BotUnload.Keeps(bot);
        List<Item> carried = [.. pack.Items];

        for (var i = 0; i < carried.Count; i++)
        {
            var item = carried[i];

            if (item == null || item.Deleted || !item.Movable || item is Gold || !item.IsStandardLoot() || BotBinding.IsBound(item, bond)
                || !BotShopkeep.Merchandise(item, bond))
            {
                continue;
            }

            var kind = item.GetType();

            if (kind == but || keep != null && keep.ContainsKey(kind) || BotCourier.Deliverable(bot, kind) || Supper(kind)
                || BotOre.Coloured(kind))
            {
                continue;
            }

            found.Add(item);
        }

        return found;
    }

    internal static int RestWorth(IBotWilful bot, Mobile body, BaseVendor shop, Type but)
    {
        if (shop == null || body == null)
        {
            return 0;
        }

        var rest = Rest(bot, body, but);
        var worth = 0;

        for (var i = 0; i < rest.Count; i++)
        {
            if (BotShops.Buys(shop, rest[i], out var paying))
            {
                worth += paying * Math.Max(1, rest[i].Amount);
            }
        }

        return worth;
    }

    private static bool Supper(Type kind)
    {
        for (var i = 0; i < BotOven.Raw.Count; i++)
        {
            if (BotOven.Raw[i] == kind)
            {
                return true;
            }
        }

        return false;
    }

    private static List<Item> Beyond(IBotWilful bot, List<Item> goods, Type kind)
    {
        if (goods.Count == 0 || !BotUnload.Keeps(bot).TryGetValue(kind, out var cap) || cap <= 0)
        {
            return goods;
        }

        if (cap >= int.MaxValue)
        {
            return [];
        }

        var left = cap;
        List<Item> sell = [];

        for (var i = 0; i < goods.Count; i++)
        {
            var item = goods[i];
            var many = Math.Max(1, item.Amount);

            if (left <= 0)
            {
                sell.Add(item);

                continue;
            }

            if (many <= left)
            {
                left -= many;

                continue;
            }

            if (item.Stackable && Mobile.LiftItemDupe(item, many - left) != null)
            {
                sell.Add(item);
            }

            left = 0;
        }

        return sell;
    }

    private static List<Item> Upto(List<Item> goods, int most)
    {
        if (most <= 0 || goods.Count == 0)
        {
            return goods;
        }

        var left = most;
        List<Item> sell = [];

        for (var i = 0; i < goods.Count && left > 0; i++)
        {
            var item = goods[i];
            var many = Math.Max(1, item.Amount);

            if (many <= left)
            {
                sell.Add(item);
                left -= many;

                continue;
            }

            if (item.Stackable && Mobile.LiftItemDupe(item, left) != null)
            {
                sell.Add(item);
            }

            left = 0;
        }

        return sell;
    }

    private static List<Item> Gather(Mobile body, Type kind)
    {
        List<Item> found = [];

        var pack = body.Backpack;

        if (pack == null || kind == null)
        {
            return found;
        }

        var bond = (body as BotMobile)?.Bond;
        List<Item> carried = [.. pack.Items];

        for (var i = 0; i < carried.Count; i++)
        {
            var item = carried[i];

            if (!item.Deleted && item.Movable && kind.IsInstanceOfType(item) && !BotBinding.IsBound(item, bond))
            {
                found.Add(item);
            }
        }

        return found;
    }
}
