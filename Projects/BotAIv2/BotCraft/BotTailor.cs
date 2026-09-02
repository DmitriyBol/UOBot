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

    // ---- Every gate, counted apart. This proposer had none at all. ------------------------------
    //
    // <b>The half of the crafting trade nobody could see.</b> The smith names eight refusals in the shard's
    // own summary; the tailor named none, so "the tailor is not taking orders" and "the tailor is taking
    // orders and you have not been watching" were the same log — and the first turned out to be true, for
    // the life of the project, because this proposer never read the board at all.

    public static long Asked { get; private set; }

    /// <summary>Asked of a bot with no sewing kit. Not a refusal — most answers are this.</summary>
    public static long NoKit { get; private set; }

    /// <summary>Orders taken off the needs board.</summary>
    public static long ToOrder { get; private set; }

    /// <summary>Orders this bot could make and had not the leather for.</summary>
    public static long ShortOfLeather { get; private set; }

    /// <summary>Sewing chosen on the bot's own judgement, with no order behind it.</summary>
    public static long OnSpec { get; private set; }

    /// <summary>Neither an order nor leather anywhere: the cloth route, or nothing.</summary>
    public static long NoLeather { get; private set; }

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
            // Content initialisation builds the craft systems, and anything that asks before that gets null.
            // Said once: a crafter that never works is otherwise indistinguishable from a lazy one.
            if (!_saidNoSystem)
            {
                _saidNoSystem = true;

                logger.Error("The tailoring system does not exist yet, so nobody can sew");
            }

            return null;
        }

        BotShops.Survey(map, body.Location);

        // <b>Leather is offered first, and the order is the point of it.</b> Cloth is the trade that waits on
        // nobody — it is on a shelf, it will be there tomorrow, and a bot sewing it is a bot converting town
        // money into skill. Leather is the other kind entirely: nothing on this shard sells it, so every
        // piece of it in the world came off something a bot killed and skinned. Reaching for it first is what
        // makes a hunter's afternoon worth money to somebody, and it is the only thing here that turns two
        // trades into an economy rather than two hobbies.
        var leather = Leatherwork(bot, body);

        if (leather != null)
        {
            return leather;
        }

        NoLeather++;

        var shop = BotShops.Nearest(bot, typeof(Cloth));

        if (shop == null)
        {
            if (!_saidNoCloth)
            {
                _saidNoCloth = true;

                // <b>Said of this bot, not of the shard.</b> BotShops.Nearest searches from ONE bot's
                // position within ITS reach, and a null from it means that bot has nowhere to buy — not that
                // the map has no cloth. Measured 02.09.2026: this line fired four times in a session during
                // which 36 sewing jobs finished and cloth was bought from Melina and from Phyllis. It is an
                // error-level sentence that was simply untrue, and it is the sort a debugger reads and
                // believes. Same correction as its sister in BotShopper.Missing.
                logger.Error(
                    "{Name} at {Where} on {Map} found no shopkeeper selling cloth within its own reach; it cannot sew from bought cloth here",
                    body?.Name ?? "a bot",
                    body?.Location ?? Point3D.Zero,
                    map
                );
            }

            return null;
        }

        var price = BotShops.Price(shop, typeof(Cloth));

        return price > 0 ? new BotSew(shop, price) : null;
    }

    /// <summary>
    /// A leather chain if there is leather to be had, otherwise nothing.
    ///
    /// <para>
    /// Two sources and they are checked in the order that costs least. What the bot is already carrying is
    /// free and would otherwise sit in a pack or go out to a stall for somebody else to sew — a bot that
    /// skinned a bear an hour ago should be wearing the result, not buying the same thing back. Failing that,
    /// the population's own market, which is the whole point: it is where a hunter's leather went, and buying
    /// it is what pays the hunter for having gone out.
    /// </para>
    ///
    /// <para>
    /// No complaint is logged when there is none. Unlike a missing cloth merchant — which is a shard that
    /// cannot support the trade at all and wants saying once — an empty leather market is the ordinary state
    /// of a morning before anybody has killed anything, and it cures itself.
    /// </para>
    /// </summary>
    private static BotDeed Leatherwork(IBotWilful bot, Mobile body)
    {
        // <b>The board first, exactly as the smith does it.</b> An order is money already in escrow and a
        // piece somebody is waiting for; sewing on spec is a guess about what might sell. Asked before the
        // bot's own judgement so that the piece made is the piece wanted.
        var order = Order(bot, body);
        var recipe = order == null ? BotThread.Choose(body, typeof(Leather)) : BotThread.Recipe(body, typeof(Leather), order.Kind);

        if (recipe == null)
        {
            return null;
        }

        if (order != null)
        {
            ToOrder++;
            Once(body, order);
        }
        else
        {
            OnSpec++;
        }

        var need = BotThread.Units(recipe);
        var carried = BotThread.Amount(body, typeof(Leather));

        // Free, and it would otherwise go out to a stall for somebody else to sew.
        if (carried >= need)
        {
            return new BotSew(body.Map, body.Location, null, 0, 0, need, order);
        }

        var stall = BotAuction.Cheapest(typeof(Leather), bot);

        if (stall == null || stall.IsEmpty)
        {
            return null;
        }

        var take = Math.Min(BotSew.Bolt, stall.Amount);

        // Enough on the stall to finish a piece counting what is already carried, or this is a purchase that
        // buys a bot the right to stand still.
        return carried + take >= need
            ? new BotSew(body.Map, body.Location, stall, stall.Price, take, need, order)
            : null;
    }

    /// <summary>
    /// The most valuable standing order this bot could sew, or null.
    ///
    /// <para>
    /// Worth rather than nearness, like the smith's: everything on this board is within a few minutes' walk
    /// of everything else, and what separates two orders is what they pay. And, like the smith's, the
    /// material is checked here rather than left for the work to discover — <b>skill enough and leather
    /// enough are two different questions</b>, and taking the dearest order on the board is reliably taking
    /// the one that needs the most hide. The smith paid for that lesson with fifty-six failed hauberks in a
    /// night; it is not worth learning twice.
    /// </para>
    /// </summary>
    private static BotWant Order(IBotWilful bot, Mobile body)
    {
        var wants = BotAuction.Wants;

        BotWant best = null;
        var bestWorth = 0;

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
                // The market can still make this up — the stall is read below — so this is only a refusal
                // when there is nothing on the board either. Counted so the two cases stay apart.
                var stall = BotAuction.Cheapest(typeof(Leather), bot);

                if (stall == null || stall.IsEmpty || carried + Math.Min(BotSew.Bolt, stall.Amount) < need)
                {
                    ShortOfLeather++;

                    continue;
                }
            }

            best = want;
            bestWorth = want.Worth;
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
              + $"{OnSpec} sewed on spec, {NoLeather} found no leather anywhere and fell back on cloth";

    /// <summary>Lets the complaints be made again after a world reload.</summary>
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
    }
}
