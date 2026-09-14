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

        var order = Order(potion);

        Once(body, potion);

        if (order != null)
        {
            ToOrder++;

            return new BotBrew(map, body.Location, potion, null, 0, 0, order);
        }

        OnSpec++;

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

        var lot = BotAuction.Cheapest(typeof(Bottle), bot);
        var lotted = lot is { IsEmpty: false };

        if (!lotted && price <= 0)
        {
            NoShop++;

            if (!_saidNoGlass)
            {
                _saidNoGlass = true;

                logger.Error(
                    "{Name} at {Where} on {Map} found no counter with an empty bottle in stock within its own reach and no bot with any on a stall; BotShops.Sells refuses a shelf whose Amount has run to nought, so this is as often a drained shelf as a missing shopkeeper",
                    body.Name,
                    body.Location,
                    map
                );
            }

            return null;
        }

        if (lotted && (price <= 0 || lot.Price < price))
        {
            price = lot.Price;
        }

        if (price <= 0)
        {
            NoPrice++;

            return null;
        }

        Sent++;

        return new BotBrew(map, body.Location, potion, shop, price, BotFlask.Batch);
    }

    private static BotWant Order(System.Type potion)
    {
        var wants = BotAuction.Wants;

        BotWant best = null;
        var bestWorth = 0;

        for (var i = 0; i < wants.Count; i++)
        {
            var want = wants[i];

            if (!want.IsOpen || want.Kind != potion || want.Worth <= bestWorth)
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
              + $"{NoGlass} had the herbs but no glass ({Sent} sent to buy some, {NoShop} found no counter with one in stock "
              + $"within reach, {NoPrice} found a counter that named no price)";

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
        ToOrder = 0;
        OnSpec = 0;
    }
}
