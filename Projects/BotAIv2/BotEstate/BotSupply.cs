using System;
using System.Collections.Generic;
using Server.Guilds;
using Server.Items;
using Server.Mobiles;
using Server.Multis;

namespace Server.BotAI.V2;

/// <summary>
/// Fetching a batch of something the population keeps running out of and leaving it on the guild's own
/// counter, so that the other nine members do not each walk to Britain for it.
///
/// <para>
/// <b>One journey instead of nine, and that is the whole of the argument.</b> The gold leaves the world at
/// the town counter either way — this buys at the same shelf, at the same price, that a shopper would have
/// bought at — so nothing here is a faucet and nothing here is a saving in coin. What it saves is the
/// island: three percent of the population's waking minutes were the walk to Britain and back, one bot at a
/// time, for a packet of bandages.
/// </para>
///
/// <para>
/// <b>What it fetches is measured rather than listed.</b> <see cref="BotShopper"/> has been keeping a tally
/// of what bots turn out to be short of since the day it was given counters; this reads that tally and
/// stocks the top of it. So a guild of archers fills its hall with arrows and a guild of casters fills it
/// with reagents, without anybody writing down which is which, and a class added tomorrow is provided for
/// the day after.
/// </para>
///
/// <para>
/// <b>The guild pays and the guild owns it.</b> Like the hall, the benches and the contract, the money comes
/// out of the members' purses through <c>BotGuilds.Stand</c> and the goods belong to all of them —
/// <see cref="Outlay"/> is nothing so that a buyer is not made to feel poor by an errand it is not paying
/// for, which is the mistake the Baron's rounds cost twelve minutes of standing still.
/// </para>
/// </summary>
public sealed class BotSupply : BotDeed
{
    public const string Trade = "supply";

    public static double Prior { get; set; } = 260.0;

    public static double WorkMinutes { get; set; } = 3.0;

    public static int Reach => BotShops.CounterReach;

    private readonly Guild _guild;

    private readonly BaseHouse _hall;

    private BaseVendor _shop;

    private int _repicks;

    private readonly Type _kind;

    private readonly int _batch;

    private int _price;

    private int _before = -1;

    private int _bought;

    private int _shelved;

    private int _spent;

    public BotSupply(Guild guild, BaseHouse hall, BaseVendor shop, Type kind, int batch, int price)
    {
        _guild = guild;
        _hall = hall;
        _shop = shop;
        _kind = kind;
        _batch = Math.Max(1, batch);
        _price = Math.Max(1, price);
    }

    public override string Kind => Trade;

    public override Map Map => _bought > 0 ? _hall?.Map : _shop?.Map;

    public override Point3D Where => _bought > 0 ? _hall?.Location ?? Point3D.Zero : _shop?.Location ?? Point3D.Zero;

    public override double Expects => Prior;

    public override double Minutes => WorkMinutes;

    public override SkillName? Trains => null;

    public override int Outlay => 0;

    public override bool AtCounter => _shop != null && _bought <= 0;

    public override double Coin => 0.0;

    public override bool Unpaid => true;

    public override int Made => _mine;

    private int _mine;

    public static int TrekLimit { get; set; } = 200;

    private int _nearest = int.MaxValue;

    private int _stalled;

    private bool Closing(Mobile body, Point3D at)
    {
        var gap = System.Math.Max(System.Math.Abs(body.X - at.X), System.Math.Abs(body.Y - at.Y));

        if (gap < _nearest)
        {
            _nearest = gap;
            _stalled = 0;

            return true;
        }

        return ++_stalled < TrekLimit;
    }

    private void Fresh()
    {
        _nearest = int.MaxValue;
        _stalled = 0;
    }

    public override bool Committed => _bought > _shelved;

    public override string Stage =>
        _shelved > 0 ? $"left {_shelved} {_kind?.Name} on the counter of {_guild?.Name}"
            : _bought > 0 ? $"carrying {_bought} {_kind?.Name} to the hall of {_guild?.Name}"
            : $"to {_shop?.Name} for {_batch} {_kind?.Name}";

    public override bool Bend(IBotWilful bot)
    {
        if (_bought <= 0 && _shop != null)
        {
            bot?.Resolve?.Ledger?.Beware(BotShops.ShopKind, _shop.Map, _shop.Location);
        }

        return false;
    }

    public override void Drop(IBotWilful bot) => BotSupplier.Release(_guild);

    public override BotDoing Advance(IBotWilful bot)
    {
        var body = bot?.Self;

        if (body == null || _guild == null || _hall is not { Deleted: false })
        {
            return BotDoing.Failed("the hall is gone");
        }

        var pack = body.Backpack;

        if (pack == null)
        {
            return BotDoing.Failed("no pack to carry supplies in");
        }

        BotSupplier.Hold(_guild);

        var merchant = BotShelf.Of(_hall);

        if (merchant == null)
        {
            return BotDoing.Failed($"{_guild.Name} has no merchant to stock");
        }

        if (_bought <= 0)
        {
            if (_shop is not { Deleted: false } || _shop.Map == null)
            {
                return BotDoing.Failed("the shopkeeper is gone");
            }

            if (!BotShelf.Room(merchant, _kind))
            {
                BotSupplier.Shut();

                return BotDoing.Failed(
                    $"the counter of {_guild.Name} had no room left for {_kind.Name} before anything was bought"
                );
            }

            if (!body.InRange(_shop.Location, Reach))
            {
                if (!Closing(body, _shop.Location))
                {
                    bot?.Resolve?.Ledger?.Beware(BotShops.ShopKind, _shop.Map, _shop.Location);

                    return BotDoing.Failed($"the walk to {_shop.Name} stopped closing {_nearest} tiles short");
                }

                return BotDoing.Walk(_shop.Map, _shop, BotArrival.Within(Reach), $"to {_shop.Name} for {_kind.Name}");
            }

            if (_before < 0)
            {
                _before = pack.GetAmount(_kind);
            }

            var bill = _batch * _price;
            var wealth = BotYield.Wealth(body);
            var mine = wealth;

            if (wealth < bill && !BotGuilds.Stand(body as BotMobile, bill - wealth))
            {
                return BotDoing.Failed($"{_guild.Name} could not raise {bill}gp to stock its counter");
            }

            var got = BotShops.Buy(bot, _shop, _kind, _batch, out var refused);

            _mine += Math.Max(0, mine - BotYield.Wealth(body));

            if (got <= 0)
            {
                var next = BotShops.Next(bot, _shop, _kind, ref _repicks);

                if (next != null)
                {
                    _shop = next;
                    _price = Math.Max(1, BotShops.Price(next, _kind));
                    Fresh();

                    return BotDoing.Walk(next.Map, next, BotArrival.Within(Reach), $"on to {next.Name} for {_kind.Name}");
                }

                return BotDoing.Failed(refused ?? "the shopkeeper would not sell it");
            }

            _bought = got;
            _spent = got * _price;

            Fresh();

            return BotDoing.Work($"{got} {_kind.Name} bought for the hall");
        }

        if (!body.InRange(merchant.Location, Reach))
        {
            if (!Closing(body, merchant.Location))
            {
                return BotDoing.Failed(
                    $"the walk to the counter of {_guild.Name} stopped closing {_nearest} tiles short"
                );
            }

            return BotDoing.Walk(merchant.Map, merchant, BotArrival.Within(Reach), $"to the counter of {_guild.Name}");
        }

        BotShelf.Wage(body as BotMobile, merchant);
        BotShelf.Collect(body, merchant);

        var carrying = pack.GetAmount(_kind);
        var spare = Math.Min(_bought, Math.Max(0, carrying - Math.Max(0, _before)));

        if (spare <= 0)
        {
            return BotDoing.Failed($"the {_kind.Name} was gone out of the pack before it got to the counter");
        }

        var lot = Lift(body, pack, _kind, spare);

        if (lot == null)
        {
            return BotDoing.Failed($"could not get the {_kind.Name} out of the pack");
        }

        var asking = Math.Max(1, (int)Math.Round(spare * _price * BotShelf.Markup));

        if (!BotShelf.Put(merchant, lot, asking))
        {
            pack.DropItem(lot);

            return BotDoing.Failed("the merchant would not take any more on its shelf");
        }

        _shelved = spare;

        return BotDoing.Done(
            $"{spare} {_kind.Name} on the counter of {_guild.Name} at {asking}gp the lot, {_spent}gp of guild money"
        );
    }

    private static Item Lift(Mobile body, Container pack, Type kind, int many)
    {
        var bond = (body as BotMobile)?.Bond;
        List<Item> carried = [.. pack.Items];

        for (var i = 0; i < carried.Count; i++)
        {
            var item = carried[i];

            if (item.Deleted || !item.Movable || !kind.IsInstanceOfType(item) || BotBinding.IsBound(item, bond))
            {
                continue;
            }

            var held = Math.Max(1, item.Amount);

            if (held < many)
            {
                continue;
            }

            if (held > many)
            {
                Mobile.LiftItemDupe(item, many);
            }

            return item;
        }

        for (var i = 0; i < carried.Count; i++)
        {
            var item = carried[i];

            if (!item.Deleted && item.Movable && kind.IsInstanceOfType(item) && !BotBinding.IsBound(item, bond))
            {
                return item;
            }
        }

        return null;
    }
}
