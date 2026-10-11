using System;
using System.Collections.Generic;
using Server.Items;
using Server.Logging;
using Server.Mobiles;
using Server.Multis;
using Server.Multis.Deeds;

namespace Server.BotAI.V2;

/// <summary>
/// The traders as the rest of the shard sees them: what neutrality closes, what a haul is allowed to be, the shopkeepers that
/// sell a kind, and the <c>Traders:</c> line.
///
/// <para>
/// <b>Patrick's order of 30.09.2026, point 5, and plan v2 of 01.10.2026, point 3 (build 385).</b> A trader reads what the
/// island has said it wants (<see cref="BotMarket.Wants"/>), buys it where it is to be had — its own stock, a bot's shop, a
/// shopkeeper in any town — only against a want that is open, carries it to the buyer by road, gate or, once well off, by sea,
/// and hands it over at a meeting (<see cref="BotHandover.Exchange"/>). The want is the whole of the demand it answers, so a
/// trader never buys anything nobody asked for and never floods a shelf: with no open want it has no haul, and does what any
/// bot with nothing to sell does — hunts (Patrick, 30.09: poor bots hunt). Its leftovers are sold by its own shift at
/// Britain's bank (<c>BotShopkeeper.Merchant</c>, the producer role).
/// </para>
///
/// <para>
/// <b>Where the margin comes from, and why it is not a handout.</b> A want names the buyer's price. When that price already
/// pays the road (<see cref="LeastMarkup"/> over what the goods cost the trader) the trader takes it as said. When it does not,
/// the trader offers carriage: the two split what carrying saves the buyer (<see cref="CarriageShare"/>) between the trader's
/// least price and the buyer's own alternative — its walk to its nearest counter and back at the shard's own price of a
/// shopping minute (<see cref="BotShopkeep.WalkGold"/>) — never more than <see cref="MostMarkup"/> over cost. A buyer agrees,
/// because it is cheaper than walking; the units are what it can pay for. Every coin a trader earns is a buyer's coin for
/// goods it asked for. The median purse on 384 was 70gp (the 384 trade baseline), so the split is half, not all.
/// </para>
/// </summary>
public static class BotTrading
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotTrading));

    public static bool Running { get; set; } = true;

    public static bool NeutralInWars { get; set; } = true;

    public static int LeastCapital { get; set; } = 60;

    public static int LeastProfit { get; set; } = 12;

    public static double LeastMarkup { get; set; } = 0.15;

    public static double MostMarkup { get; set; } = 1.0;

    public static double CarriageShare { get; set; } = 0.5;

    public static double LeastClaim { get; set; } = 4.0;

    public static double MostClaim { get; set; } = 200.0;

    public static int FreshMs { get; set; } = 60000;

    public static int LookMs { get; set; } = 20000;

    public static int MostLooked { get; set; } = 64;

    public static int MostOrders { get; set; } = 3;

    public static int BatchTiles { get; set; } = 200;

    public static int MostUnits { get; set; } = 80;

    public static int NearTiles { get; set; } = 40;

    public static int ChaseMs { get; set; } = 480000;

    public static int DeadWaitMs { get; set; } = 180000;

    public static int MostHaulMs { get; set; } = 2700000;

    public static double BuyMinutes { get; set; } = 0.5;

    public static double HandMinutes { get; set; } = 0.5;

    public static int GatherMs { get; set; } = 300000;

    public static double GatherYield { get; set; } = 0.5;

    public static int BankStand { get; set; } = 4;

    public static bool Sails { get; set; } = true;

    public static int SailWealth { get; set; } = 12000;

    public static int SayEveryMs { get; set; } = 300000;

    public static int BookHistory { get; set; } = 40;

    public static int SellersMs { get; set; } = 60000;

    public static int ShunMs { get; set; } = 600000;

    public static int GatherShunMs { get; set; } = 1800000;

    public static int RoadStep { get; set; } = 32;

    public static double RoadDare { get; set; } = 1.5;

    public static double RestShare { get; set; } = 0.5;

    public static int RestEarlyMinutes { get; set; } = 180;

    public static int PocketFloat { get; set; } = 100;

    public static int PutAwayLeast { get; set; } = 20;

    public static long PutAways { get; internal set; }

    public static long PutAwayGold { get; internal set; }

    public static long RestHeld { get; internal set; }

    public static long RestEarly { get; internal set; }

    public static bool Trader(Mobile m) => m is BotMobile { Class.Neutral: true };

    public static int PutAway(Mobile body)
    {
        if (PocketFloat <= 0 || !Trader(body) || body is not { Deleted: false, Alive: true } || body.Backpack is not { } pack)
        {
            return 0;
        }

        var excess = pack.GetAmount(typeof(Gold)) - PocketFloat;

        if (excess < Math.Max(1, PutAwayLeast))
        {
            return 0;
        }

        if (body.FindBankNoCreate() == null)
        {
            _ = body.BankBox;
        }

        if (!pack.ConsumeTotal(typeof(Gold), excess))
        {
            return 0;
        }

        if (!Banker.Deposit(body, excess))
        {
            pack.DropItem(new Gold(excess));

            return 0;
        }

        PutAways++;
        PutAwayGold += excess;

        return excess;
    }

    public static long Looks { get; internal set; }

    public static long Poor { get; internal set; }

    public static long Barren { get; internal set; }

    public static long Offered { get; internal set; }

    public static long Fresh { get; internal set; }

    public static long ToTraders { get; internal set; }

    public static long OwnWant { get; internal set; }

    public static long NoSource { get; internal set; }

    public static long Thin { get; internal set; }

    public static long Unjoined { get; internal set; }

    public static long Broke { get; internal set; }

    public static long Perilous { get; internal set; }

    public static long Sealed { get; internal set; }

    public static long Heavy { get; internal set; }

    public static long Glutted { get; internal set; }

    public static long Shunned { get; internal set; }

    public static long DeadlyRoad { get; internal set; }

    public static long Unreached { get; internal set; }

    public static long Stranded { get; internal set; }

    public static long GatherBarren { get; internal set; }

    public static long GatherShunned { get; internal set; }

    public static long Taken { get; internal set; }

    public static long Finished { get; internal set; }

    public static long FailedHauls { get; internal set; }

    public static long DroppedHauls { get; internal set; }

    public static long Deaths { get; internal set; }

    public static long FromStock { get; internal set; }

    public static long FromShops { get; internal set; }

    public static long FromCounters { get; internal set; }

    public static long BoughtUnits { get; internal set; }

    public static long BoughtGold { get; internal set; }

    public static long Repicked { get; internal set; }

    public static long Gathers { get; internal set; }

    public static long GatherOverOwn { get; internal set; }

    public static long GatheredUnits { get; internal set; }

    public static long GatheredGold { get; internal set; }

    public static long GatherEmpty { get; internal set; }

    public static long GatherTopped { get; internal set; }

    public static long Handovers { get; internal set; }

    public static long HandedUnits { get; internal set; }

    public static long HandedGold { get; internal set; }

    public static long HandedCost { get; internal set; }

    public static long Carriage { get; internal set; }

    public static long Agreed { get; internal set; }

    public static long Adopted { get; internal set; }

    public static long Leftover { get; internal set; }

    public static long Sailings { get; internal set; }

    public static long Landed { get; internal set; }

    public static long SeaLost { get; internal set; }

    public static long CompanyRefused { get; private set; }

    public static long CallsPassed { get; private set; }

    public static long TollWaived { get; private set; }

    public static bool Neutral(Mobile m) => NeutralInWars && m is BotMobile { Class: { Neutral: true } };

    public static bool Refuses(Mobile m)
    {
        if (!Neutral(m))
        {
            return false;
        }

        CompanyRefused++;

        return true;
    }

    public static bool Passes(Mobile m)
    {
        if (!Neutral(m))
        {
            return false;
        }

        CallsPassed++;

        return true;
    }

    public static void Waive() => TollWaived++;

    public static void Keep(IBotWilful bot, Dictionary<Type, int> keep)
    {
        if (keep == null || bot?.Resolve is not { } resolve)
        {
            return;
        }

        var haul = resolve.Deed as BotHaul ?? resolve.Paused?.Deed as BotHaul;

        if (haul?.Goods is not { } kind)
        {
            return;
        }

        var owed = haul.Owed;

        if (owed <= 0)
        {
            return;
        }

        var already = keep.GetValueOrDefault(kind);

        if (already < int.MaxValue)
        {
            keep[kind] = Math.Max(already, owed);
        }
    }

    private static readonly Dictionary<Type, (List<BaseVendor> Vendors, long Tick)> _sellers = [];

    private static bool _surveyed;

    public static void Survey(Map map)
    {
        if (_surveyed || map == null || map == Map.Internal || !BotTowns.Ready)
        {
            return;
        }

        _surveyed = true;

        var towns = BotTowns.All;
        var found = 0;

        for (var i = 0; i < towns.Count; i++)
        {
            if (towns[i] is { } town && (town.FromHome || town.BySea))
            {
                found += BotShops.Survey(map, town.Square);
            }
        }

        logger.Information("Traders read the counters of every town they can reach: {Found} shopkeepers added to the list", found);
    }

    public static List<BaseVendor> Sellers(Type kind)
    {
        var now = Core.TickCount;

        if (!_sellers.TryGetValue(kind, out var known) || now - known.Tick >= SellersMs)
        {
            var list = known.Vendors ?? [];
            list.Clear();

            var shops = BotShops.Shops;

            for (var i = 0; i < shops.Count; i++)
            {
                if (shops[i] is { Deleted: false } vendor && BotShops.Sells(vendor, kind, out _))
                {
                    list.Add(vendor);
                }
            }

            known = (list, now);
            _sellers[kind] = known;
        }

        return known.Vendors;
    }

    public static double Alternative(Mobile buyer, Type kind, int units, BotWant want)
    {
        var best = double.MaxValue;
        var vendors = Sellers(kind);
        var walk = BotShopkeep.WalkGold;
        var n = Math.Max(1, units);

        for (var i = 0; i < vendors.Count; i++)
        {
            var vendor = vendors[i];

            if (vendor.Deleted || vendor.Map != buyer?.Map)
            {
                continue;
            }

            var price = BotShops.Price(vendor, kind);

            if (price <= 0)
            {
                continue;
            }

            var per = price + 2.0 * BotAppraisal.Travel(buyer.Location, vendor.Location) * walk / n;

            if (per < best)
            {
                best = per;
            }
        }

        if (best == double.MaxValue)
        {
            best = Math.Max(1, want?.Anchor ?? 1) * BotMarket.MostMultiple;
        }

        return want is { Ceiling: > 0 } ? Math.Min(best, want.Ceiling) : best;
    }

    internal static bool OverItsCeiling(BotWant want, int cost)
    {
        if (want is { CeilingPending: true } && Meat(want.Kind))
        {
            BotMarket.Recap(want);

            if (want.CeilingPending)
            {
                CeilingUnread++;

                return true;
            }
        }

        if (want is not { Ceiling: > 0 } || cost < want.Ceiling)
        {
            return false;
        }

        OverCeiling++;

        return true;
    }

    public static long OverCeiling { get; internal set; }

    public static long CeilingUnread { get; internal set; }

    private static bool Meat(Type kind)
    {
        var raw = BotOven.Raw;

        for (var i = 0; i < raw.Count; i++)
        {
            if (raw[i] == kind)
            {
                return true;
            }
        }

        return false;
    }

    public static int Quote(int cost, int offer, double alternative)
    {
        var least = (int)Math.Ceiling(Math.Max(1, cost) * (1.0 + LeastMarkup));

        if (offer >= least)
        {
            return offer;
        }

        var ceiling = Math.Min((int)Math.Ceiling(Math.Max(1, cost) * (1.0 + MostMarkup)), (int)Math.Floor(alternative));

        if (ceiling <= least)
        {
            return Math.Max(offer, ceiling);
        }

        var price = least + (int)((ceiling - least) * Math.Clamp(CarriageShare, 0.0, 1.0));

        price = BotNight.Carriage(least, price, ceiling);

        return Math.Max(offer, price);
    }

    public static int BookValue(Type kind, int fallback)
    {
        var lowest = BotShops.Lowest(kind);

        return Math.Max(1, lowest > 0 ? lowest : BotMarket.Worth(kind, fallback));
    }

    private static readonly Dictionary<Type, int> _weights = [];

    public static double Weight(Type kind)
    {
        if (kind == null)
        {
            return 1.0;
        }

        if (_weights.TryGetValue(kind, out var tenths))
        {
            return tenths / 10.0;
        }

        var sample = kind.CreateInstance<Item>();
        tenths = 10;

        if (sample != null)
        {
            tenths = Math.Max(0, (int)Math.Round(sample.Weight * 10.0));
            sample.Delete();
        }

        _weights[kind] = tenths;

        return tenths / 10.0;
    }

    public static bool Goods(Type kind) =>
        kind != null && !typeof(Container).IsAssignableFrom(kind) && !typeof(BaseBoatDeed).IsAssignableFrom(kind)
        && !typeof(HouseDeed).IsAssignableFrom(kind) && !typeof(Key).IsAssignableFrom(kind) && !typeof(Spellbook).IsAssignableFrom(kind)
        && !typeof(Runebook).IsAssignableFrom(kind) && kind.Namespace?.StartsWith("Server.BotAI", StringComparison.Ordinal) != true;

    public static bool CanSail(BotMobile body) =>
        Sails && body != null && BotSea.Running && BotSea.Ready && BotSea.Owns(body);

    public static string Describe()
    {
        var traders = 0;
        var hauling = 0;
        var keeping = 0;
        var hunting = 0;
        var other = 0;
        var wealth = 0L;
        var bots = BotPopulation.Bots;

        for (var i = 0; i < bots.Count; i++)
        {
            if (bots[i] is not { Deleted: false } bot || bot.Class is not { Neutral: true })
            {
                continue;
            }

            traders++;
            wealth += BotYield.Wealth(bot);

            var kind = bot.Resolve?.Deed?.Kind;

            if (kind == BotHaul.Trade)
            {
                hauling++;
            }
            else if (kind == "shopkeep")
            {
                keeping++;
            }
            else if (kind is "hunt" or "prowl")
            {
                hunting++;
            }
            else
            {
                other++;
            }
        }

        var ended = Finished + FailedHauls + DroppedHauls + Deaths;
        var share = ended > 0 ? Finished * 100.0 / ended : 0.0;
        var margin = HandedGold - HandedCost;

        return $"{traders} traders worth {wealth}gp ({hauling} hauling, {keeping} keeping shop, {hunting} hunting, {other} at other work); "
               + $"hauls: {Taken} taken, {Finished} finished (at least one order handed over), {FailedHauls} failed, {DroppedHauls} ended from outside (put down for good, or a watchdog), {Deaths} died ({share:F1}% finished); "
               + $"{Looks} looks: {Offered} hauls offered, {Poor} too poor to buy (under {LeastCapital}gp), {Barren} found nothing worth hauling; wants passed over: {Fresh} left to near carriers for their first {FreshMs / 1000}s, {ToTraders} a trader's own, {OwnWant} wanted by the trader itself, {NoSource} nowhere to buy, {Thin} under {LeastProfit}gp of margin, {Broke} the buyer could not pay, {Unjoined} across water with no ship, {Perilous} the buyer on deadly ground, {Sealed} no way to the buyer, {Unreached} the buyer down the dungeon block where a walk or a haul from outside lately found no way, {Stranded} wants or counters out of the dungeon block or across its lands from a trader or a counter standing in it, {Heavy} too heavy to carry, {Glutted} shopkeepers' counters not bought from into a glut, {Shunned} lately struck for the buyer's own doing, {DeadlyRoad} over a road that has killed past the trader's strength, {OverCeiling} sources costing as much as the want's material ceiling or more, {CeilingUnread} wants read back from the save before their ceiling could be read; "
               + $"bought: {FromCounters} lots at shopkeepers, {FromShops} at bots' shops, {FromStock} from its own stock, {BoughtUnits} things for {BoughtGold}gp ({Repicked} counters found empty or out of reach and another tried); "
               + $"gathered from the bots of the trader's own town for buyers no carrier reaches: {Gathers} gatherings ({GatherOverOwn} said over the trader's own standing want), {GatheredUnits} things brought for {GatheredGold}gp, {GatherEmpty} brought nothing, {GatherTopped} topped up at a counter, {GatherBarren} not offered for a kind no bot was seen holding, {GatherShunned} not offered after an empty one; "
               + $"sold: {Handovers} handovers of {HandedUnits} things for {HandedGold}gp against wants, margin {margin}gp ({Carriage}gp of it carriage {Agreed} buyers agreed to above their own offer), {Adopted} orders taken up on the road, {Leftover} things left over for the shop; "
               + $"sea: {Sailings} sailings, {Landed} landed, {SeaLost} lost; "
               + $"book: {BotOrders.Describe()}; "
               + $"neutral in wars: {CompanyRefused} company doors shut (joining or raising one), {CallsPassed} company calls not offered (enlistment, bands), {TollWaived} tolls waived; in no guild by class; "
               + $"capital: {PutAways} times a trader put {PutAwayGold}gp in all into its account above a pocket float of {PocketFloat}gp; rest: {RestHeld} traders past their hours kept in the world so no more than {RestShare:P0} of the traders rest at once, {RestEarly} brought back early for it";
    }

    public static void Forget()
    {
        PutAways = 0;
        PutAwayGold = 0;
        OverCeiling = 0;
        CeilingUnread = 0;
        RestHeld = 0;
        RestEarly = 0;
        Looks = 0;
        Poor = 0;
        Barren = 0;
        Offered = 0;
        Fresh = 0;
        ToTraders = 0;
        OwnWant = 0;
        NoSource = 0;
        Thin = 0;
        Unjoined = 0;
        Broke = 0;
        Perilous = 0;
        Sealed = 0;
        Unreached = 0;
        Stranded = 0;
        Heavy = 0;
        Glutted = 0;
        Shunned = 0;
        DeadlyRoad = 0;
        GatherBarren = 0;
        GatherShunned = 0;
        Taken = 0;
        Finished = 0;
        FailedHauls = 0;
        DroppedHauls = 0;
        Deaths = 0;
        FromStock = 0;
        FromShops = 0;
        FromCounters = 0;
        BoughtUnits = 0;
        BoughtGold = 0;
        Repicked = 0;
        Gathers = 0;
        GatherOverOwn = 0;
        GatheredUnits = 0;
        GatheredGold = 0;
        GatherEmpty = 0;
        GatherTopped = 0;
        Handovers = 0;
        HandedUnits = 0;
        HandedGold = 0;
        HandedCost = 0;
        Carriage = 0;
        Agreed = 0;
        Adopted = 0;
        Leftover = 0;
        Sailings = 0;
        Landed = 0;
        SeaLost = 0;
        CompanyRefused = 0;
        CallsPassed = 0;
        TollWaived = 0;
        _sellers.Clear();
        _surveyed = false;
        BotHauler.Forget();
        BotOrders.Forget();
    }
}
