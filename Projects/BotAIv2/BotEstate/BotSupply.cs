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
    /// <summary>The ledger key.</summary>
    public const string Trade = "supply";

    /// <summary>What stocking the hall is reckoned at per minute before experience corrects it.</summary>
    public static double Prior { get; set; } = 260.0;

    /// <summary>How long the errand itself takes once the bot is standing where it needs to be.</summary>
    public static double WorkMinutes { get; set; } = 3.0;

    /// <summary>How near the counter the bot must be. The engine's own reach, at both ends.</summary>
    public static int Reach => BotShops.CounterReach;

    private readonly Guild _guild;

    private readonly BaseHouse _hall;

    private readonly BaseVendor _shop;

    private readonly Type _kind;

    private readonly int _batch;

    private readonly int _price;

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

    /// <summary>Nothing out of this bot's own pocket: the guild is buying. See the note on the class.</summary>
    public override int Outlay => 0;

    /// <summary>Not a coin comes back. The guild has bought its members a store cupboard.</summary>
    public override double Coin => 0.0;

    /// <summary>Not about money, and not to be refused for failing to earn any.</summary>
    public override bool Unpaid => true;

    /// <summary>
    /// What the bot's own purse put into this, handed straight back as takings.
    ///
    /// <para>
    /// <b>Every errand a guild pays for was teaching the shard that guild errands lose money.</b> Takings are
    /// <c>coin + made</c>, coin is measured as the change in the bot's own purse, and these errands are paid
    /// for by a levy that comes partly out of that purse — so the ledger recorded "raised The Lantern for
    /// 5000gp: -20 in 0.4 min (-50/min)", "set an oven up: -33", "hired a merchant: -148", "left 20 ash on
    /// the counter: -60". <c>BotCommons.Corrected</c> then drags the trade's whole estimate towards those
    /// numbers, which is why <c>BotHall.Prior</c> carries a note about having to be raised from ninety to
    /// four hundred before anybody would take one.
    /// </para>
    ///
    /// <para>
    /// The fix is the one <c>BotRestock</c> has always used and says in as many words: goods are worth what
    /// they cost. A hall, a bench, a shopkeeper and a shelf of reagents are all worth what was paid for them,
    /// and the bot's share of the price is a contribution to something the guild now owns rather than money
    /// that left the world. So the errand comes out at about nothing a minute — never punished, never
    /// preferred over work that actually produces something, which is the right place for it.
    /// </para>
    /// </summary>
    public override int Made => _mine;

    /// <summary>Coin out of this bot's own purse, measured across the payment rather than assumed.</summary>
    private int _mine;

    /// <summary>
    /// How many beats a leg of this errand may go without getting nearer before it is given up.
    ///
    /// <para>
    /// <b>An errand that only ever answers "walk" is immortal, and this one was.</b> Measured 09.09.2026:
    /// Quill held a supply run for eleven minutes, reported every time as "walking to somewhere 1 tile off",
    /// and never finished, failed or dropped it — because the bot had been taken onto the Bound rung by a
    /// company on the way, and a bound bot's auction is switched off, so nothing was ever going to replace
    /// the errand it was holding. The whole time it held its guild's supplier claim, so no other member could
    /// be sent either. See <c>bot-frozen-work-family</c>: work that answers Work, or the same walk, for ever
    /// is work that never ends.
    /// </para>
    ///
    /// <para>
    /// Measured as progress rather than as attempts, which is the distinction <c>BotDig.TrekLimit</c> pays
    /// for at length: a long walk across the island is legitimate, and what is not legitimate is a walk that
    /// stops getting closer.
    /// </para>
    ///
    /// <para>
    /// <b>Two hundred, and forty was wrong by five times over — the same units mistake this file's neighbour
    /// was opened to fix.</b> A bot is beaten once per step, not once per tick, so forty beats is eight to
    /// sixteen seconds of not gaining ground: an ordinary detour round a building. Measured within ten
    /// minutes of it going in — five supply runs failed with "the walk to Delano stopped closing" while the
    /// shops were perfectly reachable. Two hundred is <c>BotDig.TrekLimit</c>, which carries the reasoning
    /// and the measurement, and taking its number rather than choosing a second one is the point.
    /// </para>
    /// </summary>
    public static int TrekLimit { get; set; } = 200;

    /// <summary>The nearest this errand has got to what it is currently walking at.</summary>
    private int _nearest = int.MaxValue;

    private int _stalled;

    /// <summary>Whether the walk is still closing. Resets when the errand changes what it is walking at.</summary>
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

    /// <summary>A new leg, so the old leg's best distance means nothing. Called when the target changes.</summary>
    private void Fresh()
    {
        _nearest = int.MaxValue;
        _stalled = 0;
    }


    /// <summary>
    /// The guild's money is in this pack as goods. See <see cref="BotDeed.Committed"/>.
    ///
    /// True only between the two legs: before the purchase there is nothing to lose, and once the lot is on
    /// the counter the errand is over anyway.
    /// </summary>
    public override bool Committed => _bought > _shelved;

    public override string Stage =>
        _shelved > 0 ? $"left {_shelved} {_kind?.Name} on the counter of {_guild?.Name}"
            : _bought > 0 ? $"carrying {_bought} {_kind?.Name} to the hall of {_guild?.Name}"
            : $"to {_shop?.Name} for {_batch} {_kind?.Name}";

    /// <summary>
    /// The way turned out not to exist.
    ///
    /// <para>
    /// Nothing to bend to — the batch was priced against this shopkeeper — but the failure is filed under the
    /// <em>place</em>, because that is the word the shop lookup asks in. Filed under the errand's name it
    /// would be written and never read, and the next beat would pick the same unreachable counter on
    /// distance alone. That is the peddler's lesson and it costs one line to keep.
    /// </para>
    /// </summary>
    public override bool Bend(IBotWilful bot)
    {
        if (_bought <= 0 && _shop != null)
        {
            bot?.Resolve?.Ledger?.Beware(BotShops.ShopKind, _shop.Map, _shop.Location);
        }

        return false;
    }

    /// <summary>However it went, the guild is no longer waiting on this bot.</summary>
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

            if (!body.InRange(_shop.Location, Reach))
            {
                if (!Closing(body, _shop.Location))
                {
                    // Filed under the place, because that is the word the shop lookup asks in. See Bend.
                    bot?.Resolve?.Ledger?.Beware(BotShops.ShopKind, _shop.Map, _shop.Location);

                    return BotDoing.Failed($"the walk to {_shop.Name} stopped closing {_nearest} tiles short");
                }

                return BotDoing.Walk(_shop.Map, _shop, BotArrival.Within(Reach), $"to {_shop.Name} for {_kind.Name}");
            }

            // <b>Counted before the purchase, not after it.</b> A bot on this errand is very likely carrying
            // some of the same thing for its own use — a healer fetching bandages has bandages — and putting
            // the pack's whole holding on the shelf would be the guild robbing its own courier. What goes out
            // is what came off this shelf and nothing else.
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

            // What this bot itself put in, across the whole payment: the levy topped its purse up first, so
            // the difference is its own share and nothing else. See Made.
            _mine += Math.Max(0, mine - BotYield.Wealth(body));

            if (got <= 0)
            {
                return BotDoing.Failed(refused ?? "the shopkeeper would not sell it");
            }

            _bought = got;
            _spent = got * _price;

            // A different place to be walking at, so the first leg's best distance says nothing about this
            // one. Without this the second leg starts already believing it has been within a tile.
            Fresh();

            return BotDoing.Work($"{got} {_kind.Name} bought for the hall");
        }

        if (!body.InRange(merchant.Location, Reach))
        {
            if (!Closing(body, merchant.Location))
            {
                // Nothing to file: the hall is the guild's own and its merchant is not going anywhere, so a
                // walk that stopped closing is about this bot's afternoon rather than about the place.
                return BotDoing.Failed(
                    $"the walk to the counter of {_guild.Name} stopped closing {_nearest} tiles short"
                );
            }

            return BotDoing.Walk(merchant.Map, merchant, BotArrival.Within(Reach), $"to the counter of {_guild.Name}");
        }

        // Standing at its own guild's counter with the guild's money in reach: the two things a member is
        // uniquely able to do here, done while it is here rather than made into errands of their own.
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

        // At what the guild paid, times the markup, which is one. A member buying here is meant to be
        // indifferent about the price and better off about the walk. See BotShelf.Markup.
        var asking = Math.Max(1, (int)Math.Round(spare * _price * BotShelf.Markup));

        if (!BotShelf.Put(merchant, lot, asking))
        {
            // Back in the pack rather than on the floor: the goods are the guild's, and a failed errand is
            // not a reason to leave them in a room.
            pack.DropItem(lot);

            return BotDoing.Failed("the merchant would not take any more on its shelf");
        }

        _shelved = spare;

        return BotDoing.Done(
            $"{spare} {_kind.Name} on the counter of {_guild.Name} at {asking}gp the lot, {_spent}gp of guild money"
        );
    }

    /// <summary>
    /// Takes exactly this many of a kind out of a pack as one lot, splitting a stack if it has to.
    ///
    /// <para>
    /// <c>LiftItemDupe</c> is the engine's own way of dividing a stack, and its shape is easy to read
    /// backwards: it leaves the <em>original</em> item holding the amount asked for and puts the remainder in
    /// a new one beside it. So the item handed back here is the one to carry to the shelf, and the leftovers
    /// stay in the pack without anything else being done about them.
    /// </para>
    /// </summary>
    private static Item Lift(Mobile body, Container pack, Type kind, int many)
    {
        List<Item> carried = [.. pack.Items];

        for (var i = 0; i < carried.Count; i++)
        {
            var item = carried[i];

            if (item.Deleted || !item.Movable || !kind.IsInstanceOfType(item))
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

        // Nothing single held enough. Any one lot of the kind is better than failing the errand, and the
        // caller has already worked out that the pack owes the shelf this much.
        for (var i = 0; i < carried.Count; i++)
        {
            var item = carried[i];

            if (!item.Deleted && item.Movable && kind.IsInstanceOfType(item))
            {
                return item;
            }
        }

        return null;
    }
}
