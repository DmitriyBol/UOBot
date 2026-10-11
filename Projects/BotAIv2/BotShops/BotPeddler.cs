using System;
using System.Collections.Generic;
using Server.Items;
using Server.Logging;
using Server.Mobiles;

namespace Server.BotAI.V2;

/// <summary>
/// Offers a trip to a counter with goods the population is not asking for: out of the bot's own bank box while it stands at a
/// bank, or out of a crowded pack.
///
/// <para>
/// <b>The island decides who comes here, and it decides with facts it was already keeping.</b> Under the auction the test was a
/// stall that had never sold one in ten minutes. The stalls went on 01.10.2026 (Patrick's order of 30.09: the auction removed,
/// every purchase hand to hand), and the same sentence — "nobody here wants this" — is now read where buyers say it: no bot has a
/// want out for the kind (<see cref="BotMarket.Bespoke"/>) and, for a bot that keeps shop, none has been short of it lately
/// (<see cref="BotShopkeep.Demanded"/>). What the island does ask for is left for a shop by the bank or a carrier; what it does
/// not goes to a shopkeeper, the world's own shops, which stay. A fighter keeps no shop, so its goods the island is merely short
/// of go over a counter too, where the bots short of them buy.
/// </para>
///
/// <para>
/// <b>And it can only ever offer what the bot itself put aside.</b> The bank box holds nothing but the porter's surplus
/// (<c>BotUnload.Store</c>), and the pack is read against the bot's own keep list, so a bot's tools, herbs, paper and bandages
/// are never candidates — the first version sold the same shopkeeper the same reagents four thousand times because nothing
/// distinguished "goods" from "the things I need to do my job". The box is opened only at a bank, where a player opens it.
/// </para>
/// </summary>
public sealed class BotPeddler : IBotProposer
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotPeddler));

    private static bool _saidNoBuyer;

    public static long Asked { get; private set; }

    public static long AtBank { get; private set; }

    public static long Spoken { get; private set; }

    public static long NoBuyer { get; private set; }

    public static long Offered { get; private set; }

    public static long FromBox { get; private set; }

    public static long FromPack { get; private set; }

    public static string Describe() =>
        Asked == 0
            ? "nobody has been offered a trip to a counter with goods"
            : $"{Asked} looks for something to peddle: {Offered} trips to a counter offered, {FromBox} of them out of a bank box at a bank ({AtBank} boxes looked through, {FullLoads} loads out of a box at least {FullShare:P0} full) and {FromPack} out of a pack ({BoxedOutLoads} of them capped surplus its full bank box had no place for); "
              + $"{Spoken} kinds left alone because somebody asks for them, {NoBuyer} found no shopkeeper in reach who buys the thing, {Petty} were worth less than the walk (under {PettyGold}gp or {PettyPerTile:F1}gp a tile); "
              + $"box loads: {WeightCut} cut to what the pack carries under {BotUnload.Heavy:P0} of its weight, {BarCut} cut to {BarLoad} bars, {TooHeavy} kinds left in the box for a pack already at that line; "
              + $"hunters' loot{(HunterSells ? "" : " (off)")}: {LootInTown} loads offered standing in a town and {LootLaden} outside one with {LootLoad}gp or more of it; {BotPeddle.AlsoSold} visits sold other loot the same shopkeeper buys ({BotPeddle.AlsoUnits} things for {BotPeddle.AlsoGold}gp)";

    public string Name => "Peddler";

    public BotStanding Rung => BotStanding.Free;

    public static int PettyGold { get; set; } = 15;

    public static double PettyPerTile { get; set; } = 0.1;

    public static long Petty { get; private set; }

    public static double CrowdedShare { get; set; } = 0.5;

    public static int PackLookMs { get; set; } = 60000;

    private static readonly Dictionary<Serial, long> _looked = [];

    public BotDeed Propose(IBotWilful bot)
    {
        var body = bot?.Self;
        var map = body?.Map;

        if (map == null || map == Map.Internal || !body.Alive)
        {
            return null;
        }

        Asked++;

        var now = Core.TickCount;

        if (_looked.TryGetValue(body.Serial, out var at) && now - at < PackLookMs)
        {
            return null;
        }

        if (_looked.Count > 1024)
        {
            _looked.Clear();
        }

        _looked[body.Serial] = now;

        if (BotMarket.AtBank(body) && body.FindBankNoCreate() is { } box && box.Items.Count > 0)
        {
            AtBank++;

            var full = box.MaxItems > 0 && box.TotalItems >= box.MaxItems * FullShare;
            var drawn = Weigh(bot, body, map, box, BotUnload.Keeps(bot), true, full);

            if (drawn != null && full)
            {
                FullLoads++;
            }

            if (drawn != null)
            {
                return drawn;
            }
        }

        var pack = body.Backpack;

        if (pack == null || pack.MaxItems <= 0)
        {
            return null;
        }

        var crowded = pack.TotalItems >= pack.MaxItems * CrowdedShare;
        var keep = BotUnload.Keeps(bot);

        var room = BotRoom.Pressing(body);

        var packed = !room && BotRoom.Packed(body);

        room = room || packed;
        crowded = crowded || room;
        var hunter = false;
        var town = false;

        if (!crowded && HunterSells && Hunter(body))
        {
            town = BotTowns.Of(body.Location) != null;
            hunter = town || Loot(bot, body, pack, keep) >= LootLoad;
        }

        _refused.Clear();

        var refused = BotUnload.Refused(bot, body, _refused);
        var boxedOut = refused >= BotUnload.KitSlack;

        if (!crowded && !hunter && !boxedOut)
        {
            return null;
        }

        var deed = Weigh(bot, body, map, pack, keep, false, false, refused > 0 ? _refused : null, room);

        if (deed != null && packed)
        {
            BotRoom.PackedLoad();
        }
        else if (deed != null && room)
        {
            BotRoom.Peddled();
        }

        if (deed is BotPeddle { BoxedOut: true })
        {
            BoxedOutLoads++;
        }

        if (deed != null && hunter)
        {
            if (town)
            {
                LootInTown++;
            }
            else
            {
                LootLaden++;
            }
        }

        return deed;
    }

    public static double FullShare { get; set; } = 0.9;

    public static long FullLoads { get; private set; }

    public static long BoxedOutLoads { get; private set; }

    private static readonly Dictionary<Type, int> _refused = [];

    public static bool HunterSells { get; set; } = true;

    public static int LootLoad { get; set; } = 60;

    public static long LootInTown { get; private set; }

    public static long LootLaden { get; private set; }

    public static bool Hunter(Mobile body) =>
        body is BotMobile { Class: not null } && !BotShopkeeper.Merchant(body) && !BotTrading.Trader(body);

    private static int Loot(IBotWilful bot, Mobile body, Container pack, Dictionary<Type, int> keep)
    {
        var worth = 0;
        var items = pack.Items;

        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];

            if (item == null || item.Deleted || !item.Movable || item is Gold || !item.IsStandardLoot() || !BotShopkeep.Merchandise(item, bot.Bond))
            {
                continue;
            }

            var kind = item.GetType();

            if (keep != null && keep.ContainsKey(kind) || BotCourier.Deliverable(bot, kind))
            {
                continue;
            }

            worth += BotMarket.Worth(kind, 1) * Math.Max(1, item.Amount);
        }

        return worth;
    }

    private static BotDeed Weigh(
        IBotWilful bot, Mobile body, Map map, Container from, Dictionary<Type, int> keep, bool box, bool full = false,
        Dictionary<Type, int> refused = null, bool room = false
    )
    {
        Dictionary<Type, (Item Sample, int Amount, int Lots)> kinds = [];
        var items = from.Items;

        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];

            if (item == null || item.Deleted || !item.Movable || item is Gold || !item.IsStandardLoot()
                || !BotShopkeep.Merchandise(item, bot.Bond))
            {
                continue;
            }

            var kind = item.GetType();

            if (keep != null && keep.TryGetValue(kind, out var cap) && (!full || cap >= int.MaxValue)
                && (refused == null || !refused.ContainsKey(kind)))
            {
                continue;
            }

            kinds[kind] = kinds.TryGetValue(kind, out var had)
                ? (had.Sample, had.Amount + Math.Max(1, item.Amount), had.Lots + 1)
                : (item, Math.Max(1, item.Amount), 1);
        }

        if (kinds.Count == 0)
        {
            return null;
        }

        BotShops.Survey(map, body.Location);

        var keeps = !full && !room && (BotShopkeeper.FightersKeepShop || BotShopkeeper.Merchant(body));

        List<(Type Kind, Item Sample, int Amount, int Lots)> order = [];

        foreach (var (kind, (sample, amount, lots)) in kinds)
        {
            var units = amount;

            if (full && keep != null && keep.TryGetValue(kind, out var reserve))
            {
                units -= reserve;
            }

            if (refused != null && refused.TryGetValue(kind, out var over))
            {
                units = Math.Min(units, over) - BotUnload.Holds(bot, kind, over);
            }

            if (units > 0)
            {
                order.Add((kind, sample, units, lots));
            }
        }

        if (full || room)
        {
            order.Sort((a, b) => b.Lots.CompareTo(a.Lots));
        }

        for (var o = 0; o < order.Count; o++)
        {
            var (kind, sample, amount, _) = order[o];

            var letGo = refused != null && refused.ContainsKey(kind);
            var trimmed = false;

            if (!letGo && room && !box && BotCourier.Deliverable(bot, kind))
            {
                amount -= BotUnload.Holds(bot, kind, amount);
                trimmed = true;

                if (amount <= 0)
                {
                    Spoken++;

                    continue;
                }
            }
            else if (!letGo && (BotCourier.Deliverable(bot, kind) || keeps && BotShopkeep.Demanded(kind)))
            {
                Spoken++;

                continue;
            }

            var shop = BotShops.Buyer(bot, sample, amount, box, out var price, out var factor, out var nearer);

            if (shop == null)
            {
                NoBuyer++;
                Missing(BotMarket.Label(sample), map);

                if (letGo)
                {
                    BotSurplus.CountOnce(BotSurplus.Stranded, body, kind, amount);
                }

                continue;
            }

            var cut = trimmed;

            if (box)
            {
                var carry = Carryable(body, sample);

                if (carry <= 0)
                {
                    TooHeavy++;

                    continue;
                }

                if (amount > carry)
                {
                    amount = carry;
                    WeightCut++;
                    cut = true;
                }

                if (sample is BaseIngot && BarLoad > 0 && amount > BarLoad)
                {
                    amount = BarLoad;
                    BarCut++;
                    cut = true;
                }
            }

            var purse = BotOre.CounterRoom(body, kind, price);

            if (purse <= 0)
            {
                BotOre.Shut();

                continue;
            }

            var purseCut = amount > purse;

            if (purseCut)
            {
                amount = purse;
                cut = true;
            }

            var tiles = Math.Max(Math.Abs(shop.X - body.X), Math.Abs(shop.Y - body.Y));

            if (amount * price * factor < Math.Max(PettyGold, tiles * PettyPerTile))
            {
                Petty++;

                continue;
            }

            Offered++;

            if (box)
            {
                FromBox++;
            }
            else
            {
                FromPack++;
            }

            var passed = factor > 1.0 && nearer >= 0 && tiles > nearer ? tiles - nearer : 0;

            if (passed > 0)
            {
                BotCapital.Offer();
            }

            var extra = Hunter(body) ? BotPeddle.RestWorth(bot, body, shop, kind) : 0;

            BotOre.Hold(body, kind, amount * price, purseCut);

            return new BotPeddle(shop, kind, BotMarket.Label(sample), amount, price, box, factor, passed)
            {
                Extra = extra, BoxedOut = letGo, Most = letGo || cut ? amount : 0
            };
        }

        return null;
    }

    public static int BarLoad { get; set; } = 300;

    public static long WeightCut { get; private set; }

    public static long BarCut { get; private set; }

    public static long TooHeavy { get; private set; }

    private static int Carryable(Mobile body, Item sample)
    {
        var weight = sample?.Weight ?? 0.0;

        if (body == null || weight <= 0.0)
        {
            return int.MaxValue;
        }

        var spare = BotLadder.Ceiling(body) * BotUnload.Heavy - BotLadder.Load(body);

        if (spare <= 0.0)
        {
            return 0;
        }

        return (int)Math.Min(int.MaxValue, Math.Floor(spare / weight));
    }

    private static void Missing(string label, Map map)
    {
        if (_saidNoBuyer)
        {
            return;
        }

        _saidNoBuyer = true;

        logger.Error(
            "No shopkeeper within reach of the bots on {Map} buys {Item}, and no bot wants it either; it will stay where it is",
            map,
            label
        );
    }

    public static void Forget()
    {
        _saidNoBuyer = false;
        Asked = 0;
        AtBank = 0;
        Spoken = 0;
        NoBuyer = 0;
        Offered = 0;
        FromBox = 0;
        FullLoads = 0;
        BoxedOutLoads = 0;
        FromPack = 0;
        Petty = 0;
        LootInTown = 0;
        LootLaden = 0;
        WeightCut = 0;
        BarCut = 0;
        TooHeavy = 0;
        BotPeddle.ForgetCounts();
        _looked.Clear();
    }
}
