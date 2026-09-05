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

    /// <summary>Every gate apart, with the denominator. There is no bucket called "other".</summary>
    public static long Asked { get; private set; }

    public static long NoKit { get; private set; }

    public static long NoHerbs { get; private set; }

    public static long NoGlass { get; private set; }

    /// <summary>
    /// Bots holding neither herb nor glass, which used to be counted as holding herbs.
    ///
    /// <para>
    /// <b>The biggest number in the whole economy summary was measuring something else.</b> The gate above
    /// asks for glass first and answers "no glass" whenever the recipe cannot be filled, so a bot carrying
    /// nothing at all was reported as a brewer standing over its reagents waiting for a bottle. At 20:09 on
    /// 04.09.2026 the line read "846 had the herbs but no glass" — the largest single shortage on the board
    /// — and there was no way to tell from it how many of the eight hundred were a glass problem at all.
    /// A bucket that swallows a second cause is worse than no bucket: it names a cure and points it at the
    /// wrong half of the shard.
    /// </para>
    /// </summary>
    public static long Bare { get; private set; }

    /// <summary>Bots holding the cap of every draught they can make. Not the same as holding nothing.</summary>
    public static long AtCap { get; private set; }

    /// <summary>Of those genuinely short of glass, how many were sent to buy some.</summary>
    public static long Sent { get; private set; }

    /// <summary>How many found no counter within their own reach that sells a bottle.</summary>
    public static long NoShop { get; private set; }

    /// <summary>How many found a counter that named no price for one.</summary>
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
            // Choose only ever answers with a recipe both halves are paid for, so a null here is one of two
            // shortages. Named apart: which one it is decides whether the answer is a gatherer or an errand.
            // Which of the two halves is missing is settled inside Glassware, where the answer is actually
            // known: a bot with no bottle may still have no herb either, and counting it here made the two
            // indistinguishable. See BotAlchemist.Bare.
            if (BotFlask.Bottles(body) <= 0)
            {
                return Glassware(bot, body, map);
            }

            NoHerbs++;

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

    /// <summary>
    /// A trip for empty bottles, when that is the only thing standing between the bot and its trade.
    ///
    /// <para>
    /// Offered only when the pack still holds herbs to brew with, which <see cref="Propose"/> has already
    /// established: buying glass to stand next to no reagents is money spent on nothing.
    /// </para>
    /// </summary>
    private BotDeed Glassware(IBotWilful bot, Mobile body, Map map)
    {
        // Which draught the herbs in the pack are for, settled before the walk rather than after it.
        var potion = BotFlask.Likeliest(bot, body);

        if (potion == null)
        {
            // <b>Full is not empty, and telling them apart is the whole of what this bucket is for.</b> A bot
            // holding five of every draught it can make answers null from Likeliest exactly as a bot holding
            // nothing does. See BotFlask.Cap, and see Bare below for the first time this file was corrected
            // for a bucket that swallowed a second cause.
            if (BotFlask.AtCap(bot, body))
            {
                AtCap++;

                return null;
            }

            // No glass and no herbs either. Not this trade's errand — the shopper and the gatherer both
            // answer for herbs — and counted apart, because a bot with nothing in its pack is not a brewer
            // waiting on a bottle and a cure aimed at the glass will never reach it.
            Bare++;

            return null;
        }

        NoGlass++;

        BotShops.Survey(map, body.Location);

        var shop = BotShops.Nearest(bot, typeof(Bottle));
        var price = shop == null ? 0 : BotShops.Price(shop, typeof(Bottle));

        // <b>The population's own glass counts, and it is the glass that is actually there.</b> Every bot
        // that drinks a draught leaves an empty, and BotUnload lists it because glass is merchandise to
        // anyone who does not brew — so the stalls hold the island's whole supply while this asked only
        // shopkeepers. Same shape as the fletcher's wood and the tailor's leather, both opened before it.
        var lot = BotAuction.Cheapest(typeof(Bottle), bot);
        var lotted = lot is { IsEmpty: false };

        if (!lotted && price <= 0)
        {
            NoShop++;

            if (!_saidNoGlass)
            {
                _saidNoGlass = true;

                // Said of this bot and not of the shard — BotShops.Nearest searches from ONE bot's position.
                // Same correction as its sisters in BotTailor and BotShopper.
                logger.Error(
                    "{Name} at {Where} on {Map} found no counter with an empty bottle in stock within its own reach and no bot with any on a stall; BotShops.Sells refuses a shelf whose Amount has run to nought, so this is as often a drained shelf as a missing shopkeeper",
                    body.Name,
                    body.Location,
                    map
                );
            }

            return null;
        }

        // What the glass will actually cost, from whichever source the work will use. Outlay is reckoned
        // from this, so a stall purchase priced at the shopkeeper's nought would tell the decision layer the
        // glass was free — and a trade that looks free is a trade the ledger cannot judge.
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

    /// <summary>The most valuable standing order for this potion, or null. Worth rather than nearness.</summary>
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

    /// <summary>Said once. The first potion this shard ever brewed is worth a line and the thousandth is not.</summary>
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
              + $"{NoHerbs} had the glass but no herbs, {Bare} had neither, {AtCap} were at the cap of {BotFlask.Cap} on everything they can make ({BotFlask.Capped} draughts passed over for it, {BotFlask.Rests} stood off for {BotFlask.RestMs / 60000} minutes), "
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
