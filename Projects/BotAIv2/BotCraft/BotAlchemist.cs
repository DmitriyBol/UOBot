using System;
using Server.Items;
using Server.Logging;

namespace Server.BotAI.V2;

/// <summary>
/// Offers the mortar to anybody carrying one, and offers the board's orders first.
///
/// <para>
/// <b>Every gate here is a skipped candidate and never a failed errand.</b> The same rule the fletcher's
/// proposer states at length, and for the same reason: a brewer with no herbs is passed over, not sent out
/// to discover that at the mortar. This shard has paid for the other arrangement twice.
/// </para>
///
/// <para>
/// <b>Herbs are checked before glass, because they are the half nobody hands back.</b> Glass is five gold a
/// hundred on the alchemist's shelf and comes back off every potion anybody drinks, so a brewer short of
/// bottles is a brewer with a short errand. A brewer short of reagents is waiting on a gatherer, and
/// telling those two apart is what makes the counters below worth reading.
/// </para>
/// </summary>
public sealed class BotAlchemist : IBotProposer
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotAlchemist));

    private static bool _saidNoSystem;

    private static bool _saidNoGlass;

    private static bool _said;

    public static long Asked { get; private set; }

    public static long NoKit { get; private set; }

    public static long NoHerbs { get; private set; }

    public static long Unskilled { get; private set; }

    public static long Reagentless { get; private set; }

    public static long Stocked { get; private set; }

    public static long NoGlass { get; private set; }

    public static long Bare { get; private set; }

    public static long AtCap { get; private set; }

    public static long Sent { get; private set; }

    public static long NoShop { get; private set; }

    public static long NoPrice { get; private set; }

    public static long Unaffordable { get; private set; }

    public static long Richest { get; private set; }

    public static long SentBottles { get; private set; }

    public static long ToOrder { get; private set; }

    public static long OnSpec { get; private set; }

    public string Name => "Alchemist";

    public BotStanding Rung => BotStanding.Free;

    public BotDeed Propose(IBotWilful bot)
    {
        var body = bot?.Self;
        var map = body?.Map;

        if (map == null || map == Map.Internal || !body.Alive)
        {
            return null;
        }

        if (BotFlask.Kit(body) == null)
        {
            NoKit++;

            return null;
        }

        Asked++;

        if (BotFlask.System == null)
        {
            if (!_saidNoSystem)
            {
                _saidNoSystem = true;

                logger.Error("The alchemy system does not exist yet, so nobody can brew");
            }

            return null;
        }

        var recipe = BotFlask.Choose(bot, body, out var potion);

        if (recipe == null)
        {
            var funded = Funded(bot, body, map);

            if (funded != null)
            {
                return funded;
            }

            if (BotFlask.Bottles(body) <= 0)
            {
                return Glassware(bot, body, map);
            }

            switch (BotFlask.Why)
            {
                case BotFlask.Refusal.Unskilled:
                    Unskilled++;

                    break;

                case BotFlask.Refusal.Full:
                    Stocked++;

                    break;

                default:
                    NoHerbs++;
                    Reagentless++;

                    break;
            }

            return null;
        }

        var order = Order(bot, potion);

        Once(body, potion);

        if (order != null)
        {
            ToOrder++;
            order.Offered();

            return new BotBrew(map, body.Location, potion, null, 0, 0, order);
        }

        if (BotRoom.SpecShut(body))
        {
            return null;
        }

        OnSpec++;
        BotGlut.Spec(potion);

        return new BotBrew(map, body.Location, potion, null, 0, 0);
    }

    private BotDeed Glassware(IBotWilful bot, Mobile body, Map map)
    {
        var potion = BotFlask.Likeliest(bot, body);

        if (potion == null)
        {
            if (BotFlask.AtCap(bot, body))
            {
                AtCap++;

                return null;
            }

            Bare++;

            return null;
        }

        NoGlass++;

        BotShops.Survey(map, body.Location);

        var shop = BotShops.Nearest(bot, typeof(Bottle));
        var price = shop == null ? 0 : BotShops.Price(shop, typeof(Bottle));

        if (shop == null)
        {
            NoShop++;

            if (!_saidNoGlass)
            {
                _saidNoGlass = true;

                logger.Error(
                    "{Name} at {Where} on {Map} found no counter with an empty bottle in stock within its own reach; BotShops.Sells refuses a shelf whose Amount has run to nought, so this is as often a drained shelf as a missing shopkeeper",
                    body.Name,
                    body.Location,
                    map
                );
            }

            return null;
        }

        if (price <= 0)
        {
            NoPrice++;

            return null;
        }

        var fill = BotFlask.GlassFor(bot, body, potion);
        var take = Math.Clamp(Math.Max(fill, BotFlask.LeastBottles) - BotFlask.Bottles(body), 1, Math.Max(1, BotFlask.Batch));
        var least = Math.Max(1, Math.Min(fill, BotFlask.LeastBottles) - BotFlask.Bottles(body));
        var wealth = BotYield.Spendable(body);

        if (wealth < take * price)
        {
            take = wealth / price;
        }

        if (take < least)
        {
            Unaffordable++;

            if (wealth > Richest)
            {
                Richest = wealth;
            }

            return null;
        }

        Sent++;
        SentBottles += take;

        return new BotBrew(map, body.Location, potion, shop, price, take);
    }

    private static BotDeed Funded(IBotWilful bot, Mobile body, Map map)
    {
        if (!BotAdvance.Running || BotAdvance.Of(body) != null || BotMarket.Reserved(body) > BotYield.Wealth(body))
        {
            return null;
        }

        if (!BotAdvance.Due(body))
        {
            return null;
        }

        var wants = BotMarket.Wants;

        BotWant best = null;
        var bestWorth = 0;
        var units = 0;
        var glass = 0;
        var herbs = 0;
        System.Type reagent = null;

        for (var i = 0; i < wants.Count; i++)
        {
            var want = wants[i];

            if (!want.IsOpen || want.Worth <= bestWorth || !BotAdvance.Draught(want.Kind) || ReferenceEquals(want.Buyer, bot)
                || BotAdvance.On(want) > 0 || BotMarket.Wanted(bot, want.Kind) != null)
            {
                continue;
            }

            if (want.Buyer?.Self is not { Deleted: false, Alive: true } asker || asker.Map != map
                || !Utility.InRange(body.Location, asker.Location, BotCourier.Reach) || BotFlask.Resting(body, want.Kind))
            {
                continue;
            }

            var room = BotFlask.Cap - BotFlask.Held(bot, body, want.Kind);

            if (room <= 0)
            {
                continue;
            }

            var able = BotAdvance.Able(body, want.Kind, out var chance);

            if (able == null)
            {
                continue;
            }

            if (!BotCourier.Answerable(bot, want) || BotCourier.Holding(bot, want))
            {
                continue;
            }

            if (chance < BotAdvance.LeastChance)
            {
                BotAdvance.PassedUnlikely();

                continue;
            }

            if (Core.TickCount - want.PostedTick < BotAdvance.WaitMs)
            {
                BotAdvance.PassedYoung();

                continue;
            }

            var (herb, per, bottle) = BotFlask.Costs(able);
            var n = Math.Min(Math.Min(want.Payable, Math.Max(1, BotAdvance.MostUnits)), room);
            var lackGlass = Math.Max(0, n * bottle - BotFlask.Bottles(body));
            var lackHerbs = Math.Max(0, n * per - BotFlask.Amount(body, herb));

            if (herb == null || n <= 0 || lackGlass + lackHerbs <= 0)
            {
                continue;
            }

            best = want;
            bestWorth = want.Worth;
            units = n;
            glass = lackGlass;
            herbs = lackHerbs;
            reagent = herb;
        }

        if (best == null)
        {
            return null;
        }

        var kind = best.Kind;
        var buyer = best.Buyer?.Self;

        BotShops.Survey(map, body.Location);

        var shop = BotShops.Nearest(bot, glass > 0 ? typeof(Bottle) : reagent);

        if (shop != null && glass > 0 && herbs > 0 && !BotShops.Sells(shop, reagent, out _))
        {
            shop = BotShops.Nearest(bot, reagent);

            if (shop != null && !BotShops.Sells(shop, typeof(Bottle), out _))
            {
                shop = null;
            }
        }

        if (shop == null)
        {
            BotAdvance.PassedNoCounter();

            return null;
        }

        var factor = Math.Max(1.0, BotBurgh.Factor(body, shop));
        var cost = (glass > 0 ? glass * (int)Math.Ceiling(BotShops.Price(shop, typeof(Bottle)) * factor) : 0)
                   + (herbs > 0 ? herbs * (int)Math.Ceiling(BotShops.Price(shop, reagent) * factor) : 0);

        if (cost <= 0 || cost > units * best.Offer)
        {
            BotAdvance.PassedUnderpaid();

            return null;
        }

        if (buyer == null || BotMarket.Affords(best, best.Offer) < units
            || (long)BotYield.Spendable(buyer) + BotMarket.ReservedFor(buyer, kind) < cost)
        {
            BotAdvance.PassedUnfunded();

            return null;
        }

        BotAdvance.Looked(units);
        best.Offered();

        return new BotBrew(map, body.Location, kind, shop, best, units, glass, herbs, reagent, cost);
    }

    private static BotWant Order(IBotWilful bot, System.Type potion)
    {
        var wants = BotMarket.Wants;

        BotWant best = null;
        var bestWorth = 0;

        for (var i = 0; i < wants.Count; i++)
        {
            var want = wants[i];

            if (!want.IsOpen || want.Kind != potion || want.Worth <= bestWorth)
            {
                continue;
            }

            if (!BotCourier.Answerable(bot, want) || BotCourier.Holding(bot, want))
            {
                continue;
            }

            best = want;
            bestWorth = want.Worth;
        }

        return best;
    }

    private static void Once(Mobile body, System.Type potion)
    {
        if (_said)
        {
            return;
        }

        _said = true;

        logger.Information(
            "{Name} is the first bot on this shard ever to brew: a {Potion}, out of herbs and a bottle it had already, and until now every draught on the island came off a shelf at 15gp",
            body.Name,
            potion?.Name
        );
    }

    public static string Describe() =>
        Asked == 0
            ? $"nobody has been offered the mortar ({NoKit} answers went to bots with no pestle)"
            : $"{Asked} asked to brew: {ToOrder} took an order off the board, {OnSpec} brewed on spec, "
              + $"{NoHerbs} had the glass and not the reagent, {Stocked} had both and every draught already at its cap, {Unskilled} could not carry a single recipe yet, {Bare} had neither, {AtCap} were at the cap of {BotFlask.Cap} on everything they can make ({BotFlask.Capped} draughts passed over for it, {BotFlask.Rests} stood off for {BotFlask.RestMs / 60000} minutes), "
              + $"{NoGlass} had the herbs but no glass ({Sent} sent to buy some, {SentBottles} bottles between them, {Unaffordable} could not pay for the glass their herbs could fill (the fattest purse among them held {Richest}gp), {NoShop} found no counter with one in stock "
              + $"within reach, {NoPrice} found a counter that named no price); {BotAdvance.Describe()}";

    public static void Forget()
    {
        _saidNoSystem = false;
        _saidNoGlass = false;
        _said = false;
        Asked = 0;
        NoKit = 0;
        NoHerbs = 0;
        Unskilled = 0;
        Reagentless = 0;
        Stocked = 0;
        NoGlass = 0;
        Bare = 0;
        AtCap = 0;
        Sent = 0;
        NoShop = 0;
        NoPrice = 0;
        Unaffordable = 0;
        Richest = 0;
        SentBottles = 0;
        ToOrder = 0;
        OnSpec = 0;
    }
}
