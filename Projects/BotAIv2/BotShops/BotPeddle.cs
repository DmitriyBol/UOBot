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
    /// <summary>The ledger's key.</summary>
    public const string Trade = "peddle";

    /// <summary>How long the errand is expected to take once the bot is there.</summary>
    public static double WorkMinutes { get; set; } = 3.0;

    private readonly BaseVendor _shop;

    private readonly Type _kind;

    private readonly string _label;

    private readonly int _units;

    private readonly int _price;

    private int _earned;

    private int _sold;

    public BotPeddle(BaseVendor shop, Type kind, string label, int units, int price)
    {
        _shop = shop;
        _kind = kind;
        _label = label;
        _units = Math.Max(1, units);
        _price = Math.Max(1, price);
    }

    public override string Kind => Trade;

    public override Map Map => _shop?.Map;

    public override Point3D Where => _shop?.Location ?? Point3D.Zero;

    /// <summary>
    /// What the load is actually worth over that counter, per minute — not a flat prior.
    ///
    /// The proposer knows both numbers exactly, which almost nothing else in this project does: how many
    /// there are, and what this shopkeeper pays for one. A guess would be strictly worse than the truth, and
    /// twenty ingots outranking three is the behaviour anybody would want.
    /// </summary>
    public override double Expects => Math.Max(1.0, _units * (double)_price / Math.Max(0.5, WorkMinutes));

    public override double Minutes => WorkMinutes;

    /// <summary>Nothing. Handing goods over a counter teaches a bot nothing at all.</summary>
    public override SkillName? Trains => null;

    /// <summary>Nothing to pay. This is the earning half of a living, and the only one that makes new coin.</summary>
    public override int Outlay => 0;

    /// <summary>All of it, and this is the one undertaking in the project of which that is true.</summary>
    public override double Coin => 1.0;

    /// <summary>Nothing is produced here. The takings are the coin, counted by the brain as it always is.</summary>
    public override int Made => 0;

    public override string Stage =>
        _sold > 0 ? $"sold {_sold} {_label} for {_earned}gp" : $"taking {_units} {_label} to {_shop?.Name}";

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

        if (_shop == null || _shop.Deleted || _shop.Map == null || _shop.Map == Map.Internal)
        {
            return BotDoing.Failed("the shopkeeper is gone");
        }

        if (!body.InRange(_shop.Location, BotShops.CounterReach))
        {
            // The distance the work itself asks for, on the line above. See BotArrival.Beside.
            // <b>Followed rather than aimed at, because a shopkeeper wanders.</b> A walk order to a point is
            // matched by that point, so a vendor that shuffles one tile behind its counter makes every beat a
            // fresh order: the journey is replaced, and with it every counter that would have said this was
            // going nowhere — MaxEmptyPlans, MaxPlansWithoutCloser and StallAttempts all reset before any of
            // them can fire. That is why a stuck errand of this kind is silent. On 03.09.2026 at 04:16 the
            // roll-call had Doran the Crafter on peddle for 210 seconds and Calla for 182, neither arriving,
            // neither failing, and nothing in the log at all. BotDoing.Walk's follow form matches on the
            // mobile itself, so the order stands still while the shopkeeper does not. Every errand that walks
            // to a counter is changed with it — sew, restock, acquire and inscribe have the same shape.
            return BotDoing.Walk(_shop.Map, _shop, BotArrival.Within(BotShops.CounterReach), $"to {_shop.Name} with {_label}");
        }

        // The goods stay in the market until the bot is standing at the counter, and that is not tidiness. A
        // stall holds its stock out of the world, so nothing can be dropped, killed for or double-sold on the
        // way — take it out early and a failed walk leaves a bot wandering with a pack full of what nobody
        // wanted.
        var taken = BotAuction.Reclaim(bot, _kind);

        if (taken <= 0)
        {
            return BotDoing.Failed("the stall was empty by the time it got here");
        }

        var goods = Gather(body, _kind);

        // <b>What is handed over is the stall's stock and the pack's together, so the stall's share is not
        // the denominator of the price.</b> Reporting `taken` here made a four-gold ingot read as seventy-nine.
        // See BotShops.Sell, which counts the units as it builds the order.
        _earned = BotShops.Sell(bot, _shop, goods, out var units);

        if (_earned <= 0)
        {
            // Standing at the counter holding goods it cannot sell. The stall is gone, so they go back out on
            // the market at the price it remembered — this is a failed errand, not lost property.
            for (var i = 0; i < goods.Count; i++)
            {
                BotAuction.List(bot, goods[i], Math.Max(1, _price));
            }

            return BotDoing.Failed("the shopkeeper would not buy it after all");
        }

        _sold = units;

        return BotDoing.Done($"{units} {_label} for {_earned}gp");
    }

    /// <summary>Everything of that kind in the pack, as objects, so the counter can be shown all of it.</summary>
    private static List<Item> Gather(Mobile body, Type kind)
    {
        List<Item> found = [];

        var pack = body.Backpack;

        if (pack == null || kind == null)
        {
            return found;
        }

        List<Item> carried = [.. pack.Items];

        for (var i = 0; i < carried.Count; i++)
        {
            var item = carried[i];

            if (!item.Deleted && item.Movable && kind.IsInstanceOfType(item))
            {
                found.Add(item);
            }
        }

        return found;
    }
}
