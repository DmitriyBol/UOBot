using System;
using System.Collections.Generic;
using Server.Items;
using Server.Logging;
using Server.Mobiles;

namespace Server.BotAI.V2;

/// <summary>
/// Offers a trip to a counter to any bot holding a stall the population has ignored.
///
/// <para>
/// <b>The market decides who comes here, and it decides with a number it was already keeping.</b> A stall that
/// has never sold one and has already had its price cut has been in front of every bot on the shard for a full
/// stale period with nobody interested. Nothing new had to be invented to know that — no "is this junk" test,
/// no table of worthless things, no threshold anybody chose. A price that fell and a sales count of zero say
/// it between them.
/// </para>
///
/// <para>
/// <b>And it can only ever offer what the bot itself decided to sell.</b> That is what keeps this from becoming
/// the first version's disaster, where two bots sold the same shopkeeper the same reagents four thousand times
/// because nothing distinguished "goods" from "the things I need to do my job". Here the question cannot be
/// asked about a pack at all: a bot's tools, herbs, paper and bandages are never on a stall, so they are never
/// candidates.
/// </para>
/// </summary>
public sealed class BotPeddler : IBotProposer
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotPeddler));

    private static bool _saidNoBuyer;

    public static int IgnoredMs { get; set; } = 600000;

    public static long Asked { get; private set; }

    public static long Stallless { get; private set; }

    public static long Wanted { get; private set; }

    public static long Fresh { get; private set; }

    public static long NoBuyer { get; private set; }

    public static long Spoken { get; private set; }

    public static long Offered { get; private set; }

    public static string Describe() =>
        Asked == 0
            ? "nobody has been offered a trip to a counter with goods"
            : $"{Asked} looks for something to peddle: {Offered} trips to a counter offered, {Stallless} had nothing of their own on the market, {Spoken} were left alone because the board has money down for that kind, "
              + $"{Wanted} held stalls somebody here still wants, {Fresh} held stalls not yet ignored for {IgnoredMs / 60000} minutes, "
              + $"{NoBuyer} found no shopkeeper in reach who buys the thing, {Petty} were worth less than the walk (under {PettyGold}gp or {PettyPerTile:F1}gp a tile); {FromPack} of the trips took goods out of a crowded pack that the bots' market would not take from it";

    public string Name => "Peddler";

    public BotStanding Rung => BotStanding.Free;

    public static int PettyGold { get; set; } = 15;

    public static double PettyPerTile { get; set; } = 0.1;

    public static long Petty { get; private set; }

    public BotDeed Propose(IBotWilful bot)
    {
        var body = bot?.Self;
        var map = body?.Map;

        if (map == null || map == Map.Internal)
        {
            return null;
        }

        Asked++;

        var stalls = BotAuction.Listings;
        var mine = 0;

        for (var i = 0; i < stalls.Count; i++)
        {
            var stall = stalls[i];

            if (!ReferenceEquals(stall.Seller, bot) || stall.IsEmpty)
            {
                continue;
            }

            mine++;

            if (stall.Traded)
            {
                Wanted++;

                continue;
            }

            if (Core.TickCount - stall.ListedTick < IgnoredMs)
            {
                Fresh++;

                continue;
            }

            if (BotAuction.Demand(bot, stall.Kind) != null)
            {
                Spoken++;

                continue;
            }

            var sample = stall.Sample;

            if (sample == null)
            {
                continue;
            }

            BotShops.Survey(map, body.Location);

            var shop = BotShops.Buyer(bot, sample, stall.Amount, true, out var price, out var factor, out var nearer);

            if (shop == null)
            {
                NoBuyer++;

                Missing(stall.Label, map);

                continue;
            }

            var worth = stall.Amount * price * factor;
            var tiles = Math.Max(Math.Abs(shop.X - body.X), Math.Abs(shop.Y - body.Y));

            if (worth < Math.Max(PettyGold, tiles * PettyPerTile))
            {
                Petty++;

                continue;
            }

            Offered++;

            var passed = factor > 1.0 && nearer >= 0 && tiles > nearer ? tiles - nearer : 0;

            if (passed > 0)
            {
                BotCapital.Offer();
            }

            return new BotPeddle(shop, stall.Kind, stall.Label, stall.Amount, price, false, factor, passed);
        }

        if (mine == 0)
        {
            Stallless++;
        }

        return FromThePack(bot, body, map);
    }

    public static double CrowdedShare { get; set; } = 0.5;

    public static int PackLookMs { get; set; } = 60000;

    public static long FromPack { get; private set; }

    private static readonly Dictionary<Serial, long> _looked = [];

    private static BotDeed FromThePack(IBotWilful bot, Mobile body, Map map)
    {
        var pack = body.Backpack;

        if (pack == null || pack.MaxItems <= 0 || pack.TotalItems < pack.MaxItems * CrowdedShare)
        {
            return null;
        }

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

        var keep = BotUnload.Keeps(bot);
        var atLots = BotAuction.AtLots(bot);
        Dictionary<Type, (Item Sample, int Amount)> kinds = [];

        for (var i = 0; i < pack.Items.Count; i++)
        {
            var item = pack.Items[i];

            if (item == null || item.Deleted || !item.Movable || item is Gold || !item.IsStandardLoot()
                || BotBinding.IsBound(item, bot.Bond))
            {
                continue;
            }

            var kind = item.GetType();

            if (keep.ContainsKey(kind) || BotAuction.Selling(bot, kind))
            {
                continue;
            }

            if (!BotAuction.Worthless(kind) && !atLots)
            {
                continue;
            }

            kinds[kind] = kinds.TryGetValue(kind, out var had)
                ? (had.Sample, had.Amount + Math.Max(1, item.Amount))
                : (item, Math.Max(1, item.Amount));
        }

        if (kinds.Count == 0)
        {
            return null;
        }

        BotShops.Survey(map, body.Location);

        foreach (var (kind, (sample, amount)) in kinds)
        {
            if (BotAuction.Demand(bot, kind) != null)
            {
                continue;
            }

            var shop = BotShops.Buyer(bot, sample, amount, false, out var price, out _, out _);

            if (shop == null)
            {
                continue;
            }

            var tiles = Math.Max(Math.Abs(shop.X - body.X), Math.Abs(shop.Y - body.Y));

            if (amount * price < Math.Max(PettyGold, tiles * PettyPerTile))
            {
                continue;
            }

            FromPack++;
            Offered++;

            return new BotPeddle(shop, kind, BotListing.Name(sample), amount, price, true);
        }

        return null;
    }

    private static void Missing(string label, Map map)
    {
        if (_saidNoBuyer)
        {
            return;
        }

        _saidNoBuyer = true;

        logger.Error(
            "No shopkeeper within reach of the bots on {Map} buys {Item}, and no bot wants it either; it will sit on the market",
            map,
            label
        );
    }

    public static void Forget()
    {
        _saidNoBuyer = false;
        Asked = 0;
        FromPack = 0;
        _looked.Clear();
        Stallless = 0;
        Wanted = 0;
        Fresh = 0;
        NoBuyer = 0;
        Offered = 0;
        Spoken = 0;
    }
}
