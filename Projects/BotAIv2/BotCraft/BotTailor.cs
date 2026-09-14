using System;
using Server.Items;
using Server.Logging;

namespace Server.BotAI.V2;

/// <summary>
/// Offers the needle to anybody carrying a sewing kit.
///
/// <para>
/// <b>The tool decides, as it does with the pickaxe.</b> A crafter is born with a kit; anybody who buys one
/// is a tailor while they hold it. No list of permitted classes, so adding a class cannot silently exclude
/// it.
/// </para>
///
/// <para>
/// The precondition belongs here rather than in the work: without a shop selling cloth the chain would end
/// with a bot standing in a shop it cannot buy from, and the ledger would learn that <em>sewing</em> is
/// worthless for a reason that has nothing to do with sewing.
/// </para>
/// </summary>
public sealed class BotTailor : IBotProposer
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotTailor));

    private static bool _saidNoCloth;

    private static bool _saidNoSystem;

    private static bool _said;

    public static long Asked { get; private set; }

    public static long NoKit { get; private set; }

    public static long ToOrder { get; private set; }

    public static long ShortOfLeather { get; private set; }

    public static long OnSpec { get; private set; }

    public static long NoLeather { get; private set; }

    public static long NoRecipe { get; private set; }

    public string Name => "Tailor";

    public BotStanding Rung => BotStanding.Free;

    public BotDeed Propose(IBotWilful bot)
    {
        var body = bot?.Self;
        var map = body?.Map;

        if (map == null || map == Map.Internal)
        {
            return null;
        }

        if (BotThread.Kit(body) == null)
        {
            NoKit++;

            return null;
        }

        Asked++;

        if (BotThread.System == null)
        {
            if (!_saidNoSystem)
            {
                _saidNoSystem = true;

                logger.Error("The tailoring system does not exist yet, so nobody can sew");
            }

            return null;
        }

        BotShops.Survey(map, body.Location);

        var leather = Leatherwork(bot, body);

        if (leather != null)
        {
            return leather;
        }

        var shop = BotShops.Nearest(bot, typeof(Cloth));

        if (shop == null)
        {
            if (!_saidNoCloth)
            {
                _saidNoCloth = true;

                logger.Error(
                    "{Name} at {Where} on {Map} found no shopkeeper selling cloth within its own reach; it cannot sew from bought cloth here",
                    body?.Name ?? "a bot",
                    body?.Location ?? Point3D.Zero,
                    map
                );
            }

            return null;
        }

        var recipe = BotThread.Choose(body, typeof(Cloth), BotSew.Bolt);

        if (recipe == null)
        {
            NoRecipe++;

            return null;
        }

        var price = BotShops.Price(shop, typeof(Cloth));

        return price > 0 ? new BotSew(shop, price, BotThread.Units(recipe)) : null;
    }

    private static BotDeed Leatherwork(IBotWilful bot, Mobile body)
    {
        var carried = BotThread.Amount(body, typeof(Leather));
        var stall = BotAuction.Cheapest(typeof(Leather), bot);
        var take = stall == null || stall.IsEmpty ? 0 : Math.Min(BotSew.Bolt, stall.Amount);

        var order = Order(bot, body);
        var recipe = order == null
            ? BotThread.Choose(body, typeof(Leather), carried + take)
            : BotThread.Recipe(body, typeof(Leather), order.Kind);

        if (recipe == null)
        {
            NoRecipe++;

            return null;
        }

        var need = BotThread.Units(recipe);

        if (carried >= need)
        {
            Took(body, order);

            return new BotSew(body.Map, body.Location, null, 0, 0, need, order);
        }

        if (take <= 0 || carried + take < need)
        {
            NoLeather++;

            return null;
        }

        Took(body, order);

        return new BotSew(body.Map, body.Location, stall, stall.Price, take, need, order);
    }

    private static void Took(Mobile body, BotWant order)
    {
        if (order == null)
        {
            OnSpec++;

            return;
        }

        ToOrder++;
        Once(body, order);
    }

    private static BotWant Order(IBotWilful bot, Mobile body)
    {
        var wants = BotAuction.Wants;

        BotWant best = null;
        var bestWorth = 0;

        var starved = false;

        for (var i = 0; i < wants.Count; i++)
        {
            var want = wants[i];

            if (!want.IsOpen || ReferenceEquals(want.Buyer, bot))
            {
                continue;
            }

            var recipe = BotThread.Recipe(body, typeof(Leather), want.Kind);

            if (want.Worth <= bestWorth || recipe == null)
            {
                continue;
            }

            var need = BotThread.Units(recipe);
            var carried = BotThread.Amount(body, typeof(Leather));

            if (carried < need)
            {
                var stall = BotAuction.Cheapest(typeof(Leather), bot);

                if (stall == null || stall.IsEmpty || carried + Math.Min(BotSew.Bolt, stall.Amount) < need)
                {
                    starved = true;

                    continue;
                }
            }

            best = want;
            bestWorth = want.Worth;
        }

        if (starved && best == null)
        {
            ShortOfLeather++;
        }

        return best;
    }

    private static void Once(Mobile body, BotWant order)
    {
        if (_said)
        {
            return;
        }

        _said = true;

        logger.Information(
            "{Name} is the first tailor to take an order off the board: {Buyer} wants {Item} and has {Worth}gp down",
            body.Name,
            order.Buyer?.Self?.Name ?? "somebody",
            order.Label,
            order.Worth
        );
    }

    public static string Describe() =>
        Asked == 0
            ? $"nobody has been offered sewing ({NoKit} answers went to bots with no kit)"
            : $"{Asked} asked: {ToOrder} took an order off the board, {ShortOfLeather} passed one over for want of hide, "
              + $"{OnSpec} sewed on spec, {NoRecipe} had nothing they could sew out of hide, {NoLeather} found no leather anywhere and fell back on cloth";

    public static void Forget()
    {
        _saidNoCloth = false;
        _saidNoSystem = false;
        _said = false;
        Asked = 0;
        NoKit = 0;
        ToOrder = 0;
        ShortOfLeather = 0;
        OnSpec = 0;
        NoLeather = 0;
        NoRecipe = 0;
    }
}
