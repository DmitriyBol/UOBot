using System;
using Server.Items;
using Server.Logging;
using Server.Mobiles;

namespace Server.BotAI.V2;

/// <summary>
/// Buying a horse, and calling it up.
///
/// <para>
/// <b>Why a miner and not a warrior.</b> A gatherer's whole day is the walk: out to the rock, back to the
/// forge, back out again, with a pack heavy enough that stamina is the thing it actually runs out of. It is
/// the one trade on this shard whose takings are limited by distance rather than by skill or by what it
/// meets — which is exactly the bot a horse is worth five hundred gold to. Everything else here is written
/// so that the next class to be given one needs no code at all: see <see cref="BotClass.Rides"/>.
/// </para>
///
/// <para>
/// <b>Not an errand, and it was one for an hour.</b> Buying a horse was written as a piece of work with a
/// price on it, and a piece of work is measured: every ending, including being outbid halfway, writes what
/// it came to per minute into the bot's ledger. What this comes to per minute is <em>nought</em> — always,
/// even when it succeeds, because it does not earn five hundred gold, it spends it. So the forecast decayed
/// towards <c>prior × 2 / (2 + tries)</c> and the errand poisoned itself: Bryn took it at 115 a minute on
/// 27.08.2026, was outbid by his own copper seam half a minute later, and thereafter it was offered to him
/// at 51, then 36, then 33, against mining at 260. It could never win again.
/// </para>
///
/// <para>
/// A number that is both a forecast and a bid, on a thing the measure cannot see: the horse pays back inside
/// every <em>other</em> errand the bot ever runs, and there is nowhere in takings-per-minute to say that.
/// This project already has the answer written down, in <c>BotPurse</c>, about the identical shape — moving
/// coin to a bank "produces nothing by that measure, so the trip would score zero and never be chosen,
/// however sensible it is. What it is instead is something a bot does <em>while it happens to be at a
/// counter</em>." A horse is bought the same way: on the beat, out of trips the bot was making anyway.
/// </para>
///
/// <para>
/// <b>The five hundred gold is destroyed, and that is worth saying out loud.</b> This shard has one faucet —
/// a monster's purse — and everything else moves coin about rather than making or unmaking it. A stablemaster
/// is an ordinary shopkeeper as far as the world is concerned, so paying one is a sink, and a sink is the
/// half of an economy this population has almost none of. It is the same shape as the crown's stipend read
/// backwards, and it is the direction that needs no argument.
/// </para>
/// </summary>
public static class BotStable
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotStable));

    public static int Reach { get; set; } = 8;

    public static int Reserve { get; set; } = 200;

    public static int EveryMs { get; set; } = 2000;

    public static long Bought { get; private set; }

    public static long Paid { get; private set; }

    public static long Summons { get; private set; }

    public static long Thrown { get; private set; }

    public static BotSteed Of(Mobile bot) => bot?.Backpack?.FindItemByType<BotSteed>();

    public static bool Wants(BotMobile bot) =>
        bot?.Class is { Rides: true } && Of(bot) == null && !bot.Mounted;

    public static bool Buy(BotMobile bot, Mobile keeper)
    {
        var pack = bot?.Backpack;

        if (pack == null || keeper is not { Deleted: false } || !bot.InRange(keeper.Location, Reach))
        {
            return false;
        }

        if (BotYield.Wealth(bot) - BotSteed.Price < Reserve)
        {
            return false;
        }

        var carried = pack.GetAmount(typeof(Gold));
        var fromPack = Math.Min(carried, BotSteed.Price);
        var fromBank = BotSteed.Price - fromPack;

        if (fromPack > 0 && !pack.ConsumeTotal(typeof(Gold), fromPack))
        {
            return false;
        }

        if (fromBank > 0 && !Banker.Withdraw(bot, fromBank))
        {
            if (fromPack > 0)
            {
                pack.DropItem(new Gold(fromPack));
            }

            return false;
        }

        var steed = new BotSteed();

        if (!pack.TryDropItem(bot, steed, false))
        {
            steed.Delete();

            if (fromPack > 0)
            {
                pack.DropItem(new Gold(fromPack));
            }

            if (fromBank > 0)
            {
                Banker.Deposit(bot, fromBank);
            }

            return false;
        }

        BotBinding.Bind(steed, bot.Bond);

        Bought++;
        Paid += BotSteed.Price;

        logger.Information(
            "{Name} bought a horse from {Keeper} for {Price}gp, {Pack} of it out of its pocket and {Bank} out of its account",
            bot.Name,
            keeper.Name,
            BotSteed.Price,
            fromPack,
            fromBank
        );

        return true;
    }

    public static bool Draw(BotMobile bot)
    {
        var pack = bot?.Backpack;

        if (pack == null)
        {
            return false;
        }

        var have = pack.GetAmount(typeof(Gold));
        var want = BotSteed.Price + Reserve - have;

        if (want <= 0)
        {
            return true;
        }

        if (Banker.GetBalance(bot) < want || !Banker.Withdraw(bot, want))
        {
            return false;
        }

        var coins = new Gold(want);

        if (!pack.TryDropItem(bot, coins, false))
        {
            coins.Delete();
            Banker.Deposit(bot, want);

            return false;
        }

        Drawn += want;

        return true;
    }

    public static long Drawn { get; private set; }

    public static long Fenced { get; private set; }

    public static long Asked { get; private set; }

    public static long Poor { get; private set; }

    public static long Richest { get; private set; }

    public static long Nowhere { get; private set; }

    public static long Away { get; private set; }

    public static int Fetch { get; set; } = 60;

    public static long Fetched { get; private set; }

    public static int Nearest { get; private set; } = int.MaxValue;

    public static long Ready { get; private set; }

    public static Mobile Keeper(Mobile bot, Mobile except = null)
    {
        var map = bot?.Map;

        if (map == null || map == Map.Internal)
        {
            return null;
        }

        var shops = BotShops.Shops;
        Mobile best = null;
        var closest = int.MaxValue;

        for (var i = 0; i < shops.Count; i++)
        {
            var shop = shops[i];

            if (shop is not AnimalTrainer || shop.Deleted || shop.Map != map || ReferenceEquals(shop, except))
            {
                continue;
            }

            if (BotReach.Ask(map, bot.Location, shop.Location, BotArrival.Within(Reach)) == BotReachVerdict.Sealed)
            {
                Fenced++;

                continue;
            }

            var away = System.Math.Max(
                System.Math.Abs(shop.X - bot.X),
                System.Math.Abs(shop.Y - bot.Y)
            );

            if (away >= closest)
            {
                continue;
            }

            closest = away;
            best = shop;
        }

        return best;
    }

    private static readonly System.Collections.Generic.Dictionary<Serial, long> _looked = [];

    public static void Keep(BotMobile bot)
    {
        if (bot?.Class is not { Rides: true } || bot.Deleted || !bot.Alive || !Wants(bot))
        {
            return;
        }

        var pack = bot.Backpack;

        if (pack == null)
        {
            return;
        }

        var now = Core.TickCount;

        if (_looked.TryGetValue(bot.Serial, out var last) && now - last < EveryMs)
        {
            return;
        }

        _looked[bot.Serial] = now;

        Asked++;

        var wealth = BotYield.Wealth(bot);

        if (wealth - BotSteed.Price < Reserve)
        {
            Poor++;

            if (wealth > Richest)
            {
                Richest = wealth;
            }

            return;
        }

        var keeper = Keeper(bot);

        if (keeper == null)
        {
            Nowhere++;

            return;
        }

        if (!bot.InRange(keeper.Location, Reach))
        {
            Away++;

            var gap = System.Math.Max(
                System.Math.Abs(keeper.X - bot.X),
                System.Math.Abs(keeper.Y - bot.Y)
            );

            if (gap < Nearest)
            {
                Nearest = gap;
            }

            if (gap > Fetch)
            {
                return;
            }

            if (!ReferenceEquals(bot.Journey.Current?.Follow, keeper))
            {
                bot.Journey.Interrupt(bot.Map, keeper, BotArrival.Within(Reach), "a horse");
                Fetched++;
            }

            return;
        }

        Ready++;

        Buy(bot, keeper);
    }

    public static void Ride(BotMobile bot)
    {
        if (bot is not { Deleted: false, Alive: true } || bot.Mounted || bot.Spell != null)
        {
            return;
        }

        var steed = Of(bot);

        if (steed == null || steed.Rider != null)
        {
            return;
        }

        if (BotLadder.Standing(bot) < BotStanding.Busy)
        {
            return;
        }

        Summons++;

        steed.OnDoubleClick(bot);
    }

    public static void Throw(Mobile bot)
    {
        if (bot?.Mount == null)
        {
            return;
        }

        EtherealMount.Dismount(bot);
        Thrown++;
    }

    public static string Describe() =>
        Asked == 0
            ? "no class that rides has been looked at"
            : $"{Asked} looks at a rider with no horse: {Ready} had the price and a stablemaster in reach, {Away} had the price and were too far from one (the closest of them stood {(Nearest == int.MaxValue ? 0 : Nearest)} tiles off, against a reach of {Reach}), {Nowhere} had no stablemaster surveyed at all, {Poor} could not afford {BotSteed.Price}gp and keep {Reserve} (the fattest purse among them held {Richest}gp); "
              + $"{Fetched} sent the last streets to one; {Bought} horses bought for {Paid}gp out of {Drawn}gp drawn, {Fenced} stablemasters found behind a fence; {Summons} called up, {Thrown} riders put on the ground by a blow";

    public static void Forget()
    {
        Bought = 0;
        Paid = 0;
        Summons = 0;
        Thrown = 0;
        Drawn = 0;
        Fenced = 0;
        Asked = 0;
        Poor = 0;
        Richest = 0;
        Nowhere = 0;
        Away = 0;
        Nearest = int.MaxValue;
        Fetched = 0;
        Ready = 0;
        _looked.Clear();
    }
}
