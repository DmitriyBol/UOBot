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
/// <b>The condition for coming here is the whole design, and it is the market's own price that states it.</b>
/// A shopkeeper is not where goods go; it is where goods go <em>after the population has been offered them
/// and refused</em>. A stall that has never sold one and has already had its price cut once has stood in front
/// of every bot on the shard for half an hour with nobody interested. That is the shard saying "nobody here
/// wants this", in the only language it has, and it costs no new number to hear.
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

    private BotListing _listing;

    private int _soldBefore;

    private bool _begun;

    public static long SoldOnTheWay { get; private set; }

    public static long HandedBack { get; private set; }

    private readonly bool _fromPack;

    private readonly double _factor;

    private readonly int _passed;

    private int _premium;

    public BotPeddle(BaseVendor shop, Type kind, string label, int units, int price, bool fromPack = false, double factor = 1.0, int passed = 0)
    {
        _fromPack = fromPack;
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

    public override double Expects => Math.Max(1.0, _units * (double)_price * _factor / Math.Max(0.5, WorkMinutes));

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
            _listing = BotAuction.Find(bot, _kind);
            _soldBefore = _listing?.Sold ?? 0;
        }

        if (!body.InRange(_shop.Location, BotShops.CounterReach))
        {
            return BotDoing.Walk(_shop.Map, _shop, BotArrival.Within(BotShops.CounterReach), $"to {_shop.Name} with {_label}");
        }

        var taken = BotAuction.Reclaim(bot, _kind);

        if (taken <= 0 && Gather(body, _kind).Count > 0)
        {
            if (!_fromPack)
            {
                HandedBack++;
            }
        }
        else if (taken <= 0 && _listing != null && _listing.Sold > _soldBefore)
        {
            SoldOnTheWay++;

            return BotDoing.Done($"the stall sold {_listing.Sold - _soldBefore} of them while it walked");
        }
        else if (taken <= 0)
        {
            return BotDoing.Failed(
                BotYield.Pocket(body) ? "the stall was empty by the time it got here" : "the pack had no room to take the goods back off the stall"
            );
        }

        var goods = Gather(body, _kind);

        _earned = BotShops.Sell(bot, _shop, goods, out var units, out _premium);

        if (_earned <= 0)
        {
            for (var i = 0; i < goods.Count; i++)
            {
                BotAuction.List(bot, goods[i], Math.Max(1, _price));
            }

            return BotDoing.Failed("the shopkeeper would not buy it after all");
        }

        _sold = units;

        if (_passed > 0 && BotCapital.In(_shop.Location))
        {
            BotCapital.Delivered(units, _earned + _premium, _passed);
        }

        return _premium > 0
            ? BotDoing.Done($"{units} {_label} for {_earned}gp and {_premium}gp more from the city at the capital's counter")
            : BotDoing.Done($"{units} {_label} for {_earned}gp");
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
