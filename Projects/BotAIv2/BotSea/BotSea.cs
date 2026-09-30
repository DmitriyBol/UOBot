using System;
using Server.Guilds;
using Server.Logging;
using Server.Multis;

namespace Server.BotAI.V2;

/// <summary>
/// The sea as the rest of the population sees it: which towns a traveller may sail to, the dock it sets out from, whether
/// it can have a ship, and the one summary line.
///
/// <para>
/// <b>Patrick's point 7 of 29.09.2026: "teach the bots to travel by sea, on boats."</b> The traveller offers towns a road
/// reaches (<see cref="BotTraveller"/>); asked first, this offers — by <see cref="Share"/> for a bot on home's land, always
/// for a bot on an island — a town no road reaches whose dock has a lane from a dock the bot can walk to. On an island the
/// sea is the only way anywhere, so a bot there travels by sea or not at all, and when nothing on the island is worth doing
/// it is offered the way home by sea (<see cref="BotSeafarer"/>).
/// </para>
///
/// <para>
/// <b>A ship costs what the shopkeepers ask: 10,177 gold for a small one, from a shipwright or a provisioner.</b> On a world
/// wiped that morning the middling purse was 66 gold and the fattest 9,850 (18:58 on 29.09.2026), so a guild stands what a
/// member lacks (<see cref="GuildPays"/>, the way supplies are funded — <see cref="BotProvision.Fund"/>), and a ship once
/// bought is kept as the bot's own and sails every voyage after. <see cref="Grant"/> hands one over for nothing, for proving
/// the voyage on a young world; it is off.
/// </para>
/// </summary>
public static class BotSea
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotSea));

    public static bool Running { get; set; } = true;

    public static double Share { get; set; } = 0.25;

    public static int Furthest { get; set; } = 8000;

    public static bool GuildPays { get; set; } = true;

    public static bool Grant { get; set; }

    public static int ListPrice { get; set; } = 10177;

    public static string Order { get; set; } = "";

    public static bool Ready => BotSeaLanes.Ready;

    public static long Asked { get; private set; }

    public static long Offered { get; private set; }

    public static long Nowhere { get; private set; }

    public static long Unafforded { get; private set; }

    public static long Islanders { get; private set; }

    public static long Ordered { get; private set; }

    public static bool Travel(IBotWilful bot, BotTowns.Town here, string from, out BotDeed voyage)
    {
        voyage = null;

        var body = bot?.Self as BotMobile;
        var map = body?.Map;

        if (!Running || !Ready || body == null || map == null || map != BotSeaChart.Map)
        {
            return false;
        }

        if (BotHelm.Of(body) != null)
        {
            return true;
        }

        var ashore = BotGates.Joined(map, body.Location, BotPopulation.Where);

        if (ashore && Utility.RandomDouble() >= Share)
        {
            return false;
        }

        Asked++;

        if (!ashore)
        {
            Islanders++;
        }

        var towns = BotTowns.All;
        Span<double> weights = stackalloc double[towns.Count];
        var plans = new (BotDock From, BotSeaLane Lane)[towns.Count];
        var total = 0.0;

        for (var i = 0; i < towns.Count; i++)
        {
            weights[i] = 0.0;

            var town = towns[i];
            var dock = BotDocks.Of(town);

            if (town == here || dock == null || BotGates.Joined(map, body.Location, dock.Shore))
            {
                continue;
            }

            if (!Plan(map, body.Location, dock, out var start, out var lane, out var tiles) || tiles > Furthest)
            {
                continue;
            }

            plans[i] = (start, lane);
            weights[i] = 1.0 / (1.0 + lane.Sailed / Math.Max(0.1, BotTraveller.Curiosity));
            total += weights[i];
        }

        if (total <= 0.0)
        {
            Nowhere++;

            return !ashore;
        }

        if (!CanSail(body))
        {
            Unafforded++;

            return !ashore;
        }

        var roll = Utility.RandomDouble() * total;
        var picked = -1;

        for (var i = 0; i < towns.Count && picked < 0; i++)
        {
            roll -= weights[i];

            if (weights[i] > 0.0 && roll <= 0.0)
            {
                picked = i;
            }
        }

        if (picked < 0)
        {
            for (var i = towns.Count - 1; i >= 0 && picked < 0; i--)
            {
                if (weights[i] > 0.0)
                {
                    picked = i;
                }
            }
        }

        var target = towns[picked];

        Offered++;
        voyage = new BotVoyage(map, plans[picked].From, BotDocks.Of(target), plans[picked].Lane, target, target.Square, from, false);

        return true;
    }

    public static bool Plan(Map map, Point3D at, BotDock to, out BotDock from, out BotSeaLane lane, out int tiles)
    {
        from = null;
        lane = null;
        tiles = int.MaxValue;

        var docks = BotDocks.All;

        for (var i = 0; i < docks.Count; i++)
        {
            var dock = docks[i];

            if (dock == to || !BotGates.Joined(map, at, dock.Shore))
            {
                continue;
            }

            var way = BotSeaLanes.Between(dock, to);

            if (way == null)
            {
                continue;
            }

            var cost = BotHelm.Away(at, dock.Shore) + way.Tiles;

            if (cost < tiles)
            {
                tiles = cost;
                from = dock;
                lane = way;
            }
        }

        return from != null;
    }

    public static bool CanSail(BotMobile body)
    {
        if (body == null)
        {
            return false;
        }

        if (Grant || BotHelm.ShipOf(body) != null)
        {
            return true;
        }

        var vendor = BotShops.Nearest((Mobile)body, typeof(SmallBoatDeed));
        var price = vendor != null ? BotShops.Price(vendor, typeof(SmallBoatDeed)) : ListPrice;

        if (price <= 0)
        {
            price = ListPrice;
        }

        var have = BotYield.Wealth(body);

        if (have >= price)
        {
            return true;
        }

        if (!GuildPays || body.Guild is not Guild guild)
        {
            return false;
        }

        var could = have + BotDues.Spare(guild);

        for (var i = 0; i < guild.Members.Count && could < price; i++)
        {
            if (guild.Members[i] is BotMobile { Deleted: false } mate && mate != body)
            {
                could += Math.Max(0, BotYield.Wealth(mate) - BotGuilds.Keep);
            }
        }

        return could >= price;
    }

    public static void Obey()
    {
        var order = Order;

        if (string.IsNullOrWhiteSpace(order) || !Ready)
        {
            return;
        }

        Order = "";

        var split = order.IndexOf(" to ", StringComparison.OrdinalIgnoreCase);
        var who = split > 0 ? order[..split].Trim() : order.Split(' ', 2)[0].Trim();
        var where = split > 0 ? order[(split + 4)..].Trim() : order.Split(' ', 2).Length > 1 ? order.Split(' ', 2)[1].Trim() : "";

        BotMobile bot = null;
        var bots = BotPopulation.Bots;

        for (var i = 0; i < bots.Count && bot == null; i++)
        {
            if (bots[i] is { Deleted: false } one && one.Name != null && one.Name.StartsWith(who, StringComparison.OrdinalIgnoreCase))
            {
                bot = one;
            }
        }

        var town = BotTowns.Find(where);
        var dock = town != null ? BotDocks.Of(town) : null;

        if (bot == null || town == null || dock == null)
        {
            logger.Information("Sea: the order \"{Order}\" names {Problem}", order, bot == null ? "no bot" : town == null ? "no town" : $"a town with no dock, {town.Name}");

            return;
        }

        if (!Plan(bot.Map, bot.Location, dock, out var from, out var lane, out var tiles))
        {
            logger.Information("Sea: the order \"{Order}\": no dock {Name} can walk to has a lane to {Town}", order, bot.Name, town.Name);

            return;
        }

        var here = BotTowns.Nearest(bot.Location);
        var voyage = new BotVoyage(bot.Map, from, dock, lane, town, town.Square, here?.Name ?? "the wild", false);

        if (BotWill.Press(bot, voyage, "ordered to sea by hand (BotSea.Order)"))
        {
            Ordered++;

            logger.Information("Sea: {Name} is ordered to sail from {From} to {Town}, {Tiles} tiles of walk and sea", bot.Name, from.Name, town.Name, tiles);
        }
    }

    public static string Describe() =>
        !Running
            ? "the sea is off"
            : $"{BotHelm.Embarked} voyages begun, {BotHelm.Arrived} arrived, {BotHelm.Lost} lost; {BotVoyage.Bought} ships bought for {BotVoyage.BoughtGold}gp, {BotVoyage.Granted} handed over; "
              + $"{BotSeaChart.Describe()}; {BotDocks.Describe()}; {BotSeaLanes.Describe()}; asked {Asked} travellers ({Islanders} on an island): {Offered} offered a voyage, {Nowhere} with nowhere to sail, {Unafforded} without the means of a ship; "
              + $"{BotSeafarer.Describe()}; {Ordered} ordered by hand; {BotVoyage.Describe()}; {BotHelm.Describe()}; {BotSeaCourse.Describe()}";

    public static void Forget()
    {
        Asked = 0;
        Offered = 0;
        Nowhere = 0;
        Unafforded = 0;
        Islanders = 0;
        Ordered = 0;
        Order = "";
        BotSeafarer.Forget();
        BotVoyage.Forget();
        BotHelm.Forget();
        BotSeaLanes.Forget();
        BotSeaCourse.Forget();
        BotDocks.Forget();
        BotSeaChart.Forget();
    }
}

/// <summary>
/// Offers a bot on an island — ground no road joins to its home — the way home by sea, once it has had nothing worth doing
/// for as long as the walk home waits (<see cref="BotHomeward.BarrenFor"/>). The walk home cannot be offered there: its
/// place is on another land and the appraisal refuses it.
/// </summary>
public sealed class BotSeafarer : IBotProposer
{
    public static long Asked { get; private set; }

    public static long OnHome { get; private set; }

    public static long Busy { get; private set; }

    public static long NoWay { get; private set; }

    public static long Unafforded { get; private set; }

    public static long Sent { get; private set; }

    public string Name => "Seafarer";

    public BotStanding Rung => BotStanding.Free;

    public BotDeed Propose(IBotWilful bot)
    {
        var body = bot?.Self as BotMobile;
        var map = body?.Map;

        if (!BotSea.Running || !BotSea.Ready || body == null || !body.Alive || map == null || map != BotSeaChart.Map)
        {
            return null;
        }

        if (bot is IBotSquadMember { Squad: not null } || BotDelveParty.Delving(body) || BotDungeon.Under(body.Location) || BotHelm.Of(body) != null)
        {
            return null;
        }

        Asked++;

        var hearth = BotSeat.Home(body);

        if (BotGates.Joined(map, body.Location, hearth))
        {
            OnHome++;

            return null;
        }

        if (bot.Resolve?.Urges?.BarrenMinutes(Core.TickCount) < BotHomeward.BarrenFor)
        {
            Busy++;

            return null;
        }

        BotDock start = null;
        BotDock end = null;
        BotSeaLane lane = null;
        var best = int.MaxValue;
        var docks = BotDocks.All;

        for (var i = 0; i < docks.Count; i++)
        {
            var dock = docks[i];

            if (!BotGates.Joined(map, dock.Shore, hearth) || !BotSea.Plan(map, body.Location, dock, out var from, out var way, out var tiles))
            {
                continue;
            }

            var cost = tiles + BotHelm.Away(dock.Shore, hearth);

            if (cost < best)
            {
                best = cost;
                start = from;
                end = dock;
                lane = way;
            }
        }

        if (end == null)
        {
            NoWay++;

            return null;
        }

        if (!BotSea.CanSail(body))
        {
            Unafforded++;

            return null;
        }

        Sent++;

        return new BotVoyage(map, start, end, lane, null, hearth, BotTowns.Nearest(body.Location)?.Name ?? "the wild", true);
    }

    public static string Describe() =>
        Asked == 0
            ? "nobody has been asked to sail home"
            : $"{Asked} asked to sail home: {OnHome} on home's land, {Busy} not yet out of work, {NoWay} with no dock and lane home, {Unafforded} without the means of a ship, {Sent} sent";

    public static void Forget()
    {
        Asked = 0;
        OnHome = 0;
        Busy = 0;
        NoWay = 0;
        Unafforded = 0;
        Sent = 0;
    }
}
