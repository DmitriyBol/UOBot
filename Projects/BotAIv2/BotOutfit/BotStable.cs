using System;
using Server.Items;
using Server.Logging;
using Server.Mobiles;

namespace Server.BotAI.V2;

public static class BotStable
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotStable));

    public static int Reach { get; set; } = 8;

    public static int Reserve
    {
        get => _reserve ?? BotPurse.KeepBack;
        set => _reserve = value;
    }

    private static int? _reserve;

    public static int EveryMs { get; set; } = 2000;

    public static long Bought { get; private set; }

    public static long Paid { get; private set; }

    public static long Summons { get; private set; }

    public static long Thrown { get; private set; }

    public static BotSteed Of(Mobile bot) =>
        bot?.Backpack?.FindItemByType<BotChampionSteed>() ?? bot?.Backpack?.FindItemByType<BotSteed>();

    public static bool Wants(BotMobile bot) =>
        bot?.Class is { Rides: true } && Of(bot) == null && !bot.Mounted;

    public static bool Buy(BotMobile bot, Mobile keeper)
    {
        var pack = bot?.Backpack;

        if (pack == null || keeper is not { Deleted: false } || !bot.InRange(keeper.Location, Reach))
        {
            return false;
        }

        if (BotYield.Spendable(bot) - BotSteed.Price < Reserve)
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

        BotYield.Aside(bot, BotSteed.Price);

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

    public static long Unfetched { get; private set; }

    private static void Unfetch(BotMobile bot)
    {
        var journey = bot?.Journey;

        if (journey?.Current is not { Interruption: true, Reason: "a horse" } errand)
        {
            return;
        }

        var keeper = errand.Follow;

        if (Wants(bot) && keeper is { Deleted: false } && !bot.InRange(keeper.Location, Reach))
        {
            return;
        }

        if (Wants(bot) && keeper is { Deleted: false } && BotYield.Spendable(bot) - BotSteed.Price >= Reserve)
        {
            return;
        }

        journey.Complete();
        Unfetched++;
    }

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
        Unfetch(bot);

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

        var wealth = BotYield.Spendable(bot);

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

        if (bot.Resolve?.Deed is { Afoot: true })
        {
            HeldBack++;

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

    public static void Alight(Mobile bot)
    {
        if (bot?.Mount == null)
        {
            return;
        }

        EtherealMount.Dismount(bot);
        Alighted++;
    }

    public static long Alighted { get; private set; }

    public static long HeldBack { get; private set; }

    public static string Describe() =>
        Asked == 0
            ? "no class that rides has been looked at"
            : $"{Asked} looks at a rider with no horse: {Ready} had the price and a stablemaster in reach, {Away} had the price and were too far from one (the closest of them stood {(Nearest == int.MaxValue ? 0 : Nearest)} tiles off, against a reach of {Reach}), {Nowhere} had no stablemaster surveyed at all, {Poor} could not afford {BotSteed.Price}gp and keep {Reserve} (the fattest purse among them held {Richest}gp); "
              + $"{Fetched} sent the last streets to one and {Unfetched} of those walks taken off the road once done; {Bought} horses bought for {Paid}gp out of {Drawn}gp drawn, {Fenced} stablemasters found behind a fence; {Summons} called up, {Thrown} riders put on the ground by a blow, {Alighted} put on foot to mine and {HeldBack} calls held back while they worked";

    public static void Forget()
    {
        Bought = 0;
        Paid = 0;
        Summons = 0;
        Thrown = 0;
        Alighted = 0;
        HeldBack = 0;
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
