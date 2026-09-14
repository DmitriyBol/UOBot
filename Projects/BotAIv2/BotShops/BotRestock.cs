using System;
using Server.Mobiles;

namespace Server.BotAI.V2;

/// <summary>
/// Going to a shop and buying what the bot has run out of.
///
/// <para>
/// <b>Maintenance is the one kind of work a takings-per-minute measure prices badly, and this is how it is
/// handled honestly.</b> Buying creates no wealth: coin becomes goods. So the takings are declared as what
/// was paid — <see cref="Made"/> equals the bill — which makes the trip come out at roughly nothing per
/// minute rather than at a loss. It is therefore never <em>punished</em> and never preferred over work that
/// actually produces something, which is exactly the right place for an errand to the shops.
/// </para>
///
/// <para>
/// The one number that does real work here is <see cref="Outlay"/>: it is what the decision layer measures
/// need against, so a bot that cannot afford its own bandages is a bot that feels short of money — and stops
/// feeling short the moment it can.
/// </para>
/// </summary>
public sealed class BotRestock : BotDeed
{
    public const string Trade = "restock";

    public static double Prior { get; set; } = 12.0;

    public static double WorkMinutes { get; set; } = 2.0;

    private readonly BaseVendor _shop;

    private readonly BotListing _stall;

    private readonly Type _wanted;

    private readonly int _amount;

    private readonly int _price;

    private int _bought;

    private int _paid;

    public BotRestock(BaseVendor shop, Type wanted, int amount, int price)
    {
        _shop = shop;
        _wanted = wanted;
        _amount = Math.Max(1, amount);
        _price = Math.Max(1, price);
    }

    public BotRestock(BotListing stall, Type wanted, int amount, Map map, Point3D where)
    {
        _stall = stall;
        _wanted = wanted;
        _amount = Math.Max(1, amount);
        _price = Math.Max(1, stall?.Price ?? 1);
        _map = map;
        _where = where;
    }

    public BotRestock(PlayerVendor merchant, Type wanted, int amount, int price)
    {
        _merchant = merchant;
        _wanted = wanted;
        _amount = Math.Max(1, amount);
        _price = Math.Max(1, price);
    }

    private readonly PlayerVendor _merchant;

    private readonly Map _map;

    private readonly Point3D _where;

    public override string Kind => Trade;

    public override Map Map => _shop?.Map ?? _merchant?.Map ?? _map;

    public override Point3D Where => _shop?.Location ?? _merchant?.Location ?? _where;

    public override double Expects => Prior;

    public override double Minutes => WorkMinutes;

    public override SkillName? Trains => null;

    public override int Outlay => _amount * _price;

    public override bool AtCounter => _shop != null;

    public override double Coin => 0.0;

    public override bool Unpaid => true;

    public override int Made => _paid;

    public override string Stage => _bought > 0 ? $"bought {_bought} {_wanted?.Name}" : $"after {_amount} {_wanted?.Name}";

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

        if (_stall != null)
        {
            if (_stall.IsEmpty)
            {
                return BotDoing.Failed("that stall is empty now");
            }

            var price = _stall.Price;

            _bought = BotAuction.Buy(body, _stall, _amount);

            if (_bought <= 0)
            {
                return BotDoing.Failed("could not pay another bot for it");
            }

            _paid = _bought * price;

            return BotDoing.Done($"{_bought} {_wanted?.Name} off the market for {_paid}gp");
        }

        if (_merchant != null)
        {
            if (_merchant.Deleted || _merchant.Map == null || _merchant.Map == Map.Internal)
            {
                return BotDoing.Failed("the guild's merchant is gone");
            }

            if (!body.InRange(_merchant.Location, BotShelf.Reach))
            {
                return BotDoing.Walk(
                    _merchant.Map,
                    _merchant,
                    BotArrival.Within(BotShelf.Reach),
                    $"to the counter of {body.Guild?.Name ?? "the guild"}"
                );
            }

            var lot = BotShelf.Offer(_merchant, _wanted, out var asking);

            if (lot == null)
            {
                return BotDoing.Failed($"the guild's shelf has no {_wanted?.Name} left on it");
            }

            _bought = BotShelf.Take(body, _merchant, lot, out var declined);

            if (_bought <= 0)
            {
                return BotDoing.Failed(declined ?? "the guild's merchant would not sell it");
            }

            _paid = asking;

            (bot as BotMobile)?.Rearm();

            return BotDoing.Done($"{_bought} {_wanted?.Name} off the guild's own counter for {_paid}gp");
        }

        if (_shop == null || _shop.Deleted || _shop.Map == null || _shop.Map == Map.Internal)
        {
            return BotDoing.Failed("the shopkeeper is gone");
        }

        if (!body.InRange(_shop.Location, BotShops.CounterReach))
        {
            return BotDoing.Walk(_shop.Map, _shop, BotArrival.Within(BotShops.CounterReach), $"to {_shop.Name}");
        }

        _bought = BotShops.Buy(bot, _shop, _wanted, _amount, out var refused);

        if (_bought <= 0)
        {
            return BotDoing.Failed(refused ?? "the shop would not sell it");
        }

        _paid = _bought * _price;

        (bot as BotMobile)?.Rearm();

        return BotDoing.Done($"{_bought} {_wanted?.Name} for {_paid}gp");
    }
}
