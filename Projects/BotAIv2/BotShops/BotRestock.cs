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
    /// <summary>The ledger's key. A kind of work, not one shop or one thing.</summary>
    public const string Trade = "restock";

    /// <summary>
    /// What an errand to the shops is reckoned at before experience corrects it. Low on purpose: it produces
    /// nothing, and the only reason to do it is that something has run out.
    /// </summary>
    public static double Prior { get; set; } = 12.0;

    /// <summary>How long the errand itself is expected to take once the bot is there.</summary>
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

    /// <summary>
    /// The same errand, off another bot's stall instead of a shelf.
    ///
    /// <para>
    /// <b>This is where a crafter's living comes from.</b> A blade breaks in somebody's ribs and the bot needs
    /// another; if a smith has one out cheaper than the shopkeeper, the fighter's gold — which came off a
    /// monster — goes to the smith instead of out of the world. Same undertaking, because it is the same
    /// errand: the only difference is whose counter it is, and a bot has no reason to care.
    /// </para>
    ///
    /// <para>
    /// And it needs no walk. The market holds its goods out of the world, so buying from a stall happens from
    /// wherever the bot is standing.
    /// </para>
    /// </summary>
    public BotRestock(BotListing stall, Type wanted, int amount, Map map, Point3D where)
    {
        _stall = stall;
        _wanted = wanted;
        _amount = Math.Max(1, amount);
        _price = Math.Max(1, stall?.Price ?? 1);
        _map = map;
        _where = where;
    }

    private readonly Map _map;

    private readonly Point3D _where;

    public override string Kind => Trade;

    public override Map Map => _shop?.Map ?? _map;

    public override Point3D Where => _shop?.Location ?? _where;

    public override double Expects => Prior;

    public override double Minutes => WorkMinutes;

    /// <summary>Nothing. Handing coin over a counter teaches a bot nothing at all.</summary>
    public override SkillName? Trains => null;

    /// <summary>What it will cost. The one number the decision layer measures need against.</summary>
    public override int Outlay => _amount * _price;

    /// <summary>Not a penny comes back. This is the spending half of a living.</summary>
    public override double Coin => 0.0;

    /// <summary>Goods are worth what they cost, so the errand comes out at about nothing rather than a loss.</summary>
    public override int Made => _paid;

    public override string Stage => _bought > 0 ? $"bought {_bought} {_wanted?.Name}" : $"after {_amount} {_wanted?.Name}";

    /// <summary>
    /// The way to the shopkeeper turned out not to exist.
    ///
    /// <para>
    /// <b>Nothing to bend to here, and something to write down — and it was the writing down that was
    /// missing.</b> What this undertaking carries was priced against <em>this</em> shopkeeper, so swapping in
    /// another one mid-errand would carry a stale price; failing is the honest answer. But the failure has to
    /// be filed under the <em>place's</em> name, because that is the word the shop lookup asks in. Filed under
    /// the undertaking's name — which is all <c>BotWill.Settle</c> can do — it is written and never read, and
    /// the next beat picks the same unreachable shopkeeper on distance alone. Calla walked at Gus thirty-one
    /// times in an hour on 26.08.2026 that way.
    /// </para>
    /// </summary>
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

        if (_shop == null || _shop.Deleted || _shop.Map == null || _shop.Map == Map.Internal)
        {
            return BotDoing.Failed("the shopkeeper is gone");
        }

        if (!body.InRange(_shop.Location, BotShops.CounterReach))
        {
            // The distance the work itself asks for, on the line above. See BotArrival.Beside.
            // Followed rather than aimed at: a shopkeeper wanders. See BotPeddle for the whole reason.
            return BotDoing.Walk(_shop.Map, _shop, BotArrival.Within(BotShops.CounterReach), $"to {_shop.Name}");
        }

        _bought = BotShops.Buy(bot, _shop, _wanted, _amount, out var refused);

        if (_bought <= 0)
        {
            // Standing at the counter with nothing to show for it: sold out, priced out, or refused. All
            // three are the shop's business rather than the bot's — but which of the three it was is this
            // log's business, and lumping them cost a night. See the note on the overload above.
            return BotDoing.Failed(refused ?? "the shop would not sell it");
        }

        _paid = _bought * _price;

        // Straight on, if it is something to wear or wield.
        //
        // <b>The same hole death opened, dug by shopping.</b> A blade wears through and is bought again — and
        // the new one lands in the pack, where the old one never was. Without this a bot walks out of the shop
        // it just spent its takings in and goes back to fighting with its fists, carrying the sword.
        (bot as BotMobile)?.Rearm();

        return BotDoing.Done($"{_bought} {_wanted?.Name} for {_paid}gp");
    }
}
