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

    public static long OwnCloth { get; private set; }

    public static bool BuysCloth { get; set; }

    public static long NoBoughtCloth { get; private set; }

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

        if (BotThread.Amount(body, typeof(Cloth)) >= BotSew.Bolt && !BotRoom.SpecShut(body))
        {
            var own = BotThread.Choose(body, typeof(Cloth), BotSew.Bolt);

            if (own != null)
            {
                OwnCloth++;
                BotGlut.Spec(own.ItemType);

                return new BotSew(map, body.Location, BotThread.Units(own), own.ItemType);
            }
        }

        if (!BuysCloth)
        {
            NoBoughtCloth++;

            return null;
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

        if (price <= 0)
        {
            return null;
        }

        BotGlut.Spec(recipe.ItemType);

        return new BotSew(shop, price, BotThread.Units(recipe), recipe.ItemType);
    }

    private static BotDeed Leatherwork(IBotWilful bot, Mobile body)
    {
        if (BotThread.Amount(body, typeof(Leather)) < BotSew.Bolt)
        {
            BotMarket.FromBank(bot, typeof(Leather), BotSew.Bolt);
        }

        var carried = BotThread.Amount(body, typeof(Leather));

        var order = Order(bot, body);
        var recipe = order == null
            ? BotThread.Choose(body, typeof(Leather), carried)
            : BotThread.Recipe(body, typeof(Leather), order.Kind);

        if (recipe == null)
        {
            NoRecipe++;

            return null;
        }

        var need = BotThread.Units(recipe);

        if (order == null && BotRoom.SpecShut(body))
        {
            return null;
        }

        if (carried >= need)
        {
            Took(body, order, recipe.ItemType);

            return new BotSew(body.Map, body.Location, need, order, recipe.ItemType);
        }

        NoLeather++;

        return null;
    }

    private static void Took(Mobile body, BotWant order, Type kind)
    {
        if (order == null)
        {
            OnSpec++;
            BotGlut.Spec(kind);

            return;
        }

        ToOrder++;
        Once(body, order);
    }

    private static BotWant Order(IBotWilful bot, Mobile body)
    {
        var wants = BotMarket.Wants;

        BotWant best = null;
        var bestWorth = 0;

        var starved = false;
        var starvedNeed = 0;
        var starvedWorth = 0;

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

            if (!BotCourier.Answerable(bot, want) || BotCourier.Holding(bot, want))
            {
                continue;
            }

            var need = BotThread.Units(recipe);
            var carried = BotThread.Amount(body, typeof(Leather));

            if (carried < need)
            {
                starved = true;
                want.Passed(BotPass.Material);

                if (want.Worth > starvedWorth)
                {
                    starvedWorth = want.Worth;
                    starvedNeed = need;
                }

                continue;
            }

            best = want;
            bestWorth = want.Worth;
        }

        if (starved && best == null)
        {
            ShortOfLeather++;

            BotCraftFirst.Short(body, typeof(Leather), starvedNeed);
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
              + $"{OnSpec} sewed on spec, {NoRecipe} had nothing they could sew out of hide, {NoLeather} found no leather anywhere and fell back on cloth, "
              + $"{NoBoughtCloth} refused cloth off a counter for spec work ({OwnCloth} sewed the pack's own)";

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
        NoBoughtCloth = 0;
    }
}
