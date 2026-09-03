using Server.Logging;

namespace Server.BotAI.V2;

/// <summary>
/// Offers a bot with a hammer and some metal a turn at an anvil, and offers it the board's orders first.
///
/// <para>
/// <b>Orders before speculation, and the reason is arithmetic rather than politeness.</b> Something on the
/// board has money already down against it — <see cref="BotAuction.Ask"/> takes the payment when the want is
/// raised — so filling one is a sale that has already happened. Making something on spec is making something
/// that might sell. The auction is told the difference through <see cref="BotForge.Expects"/> and settles it
/// the same way it settles everything else, so a smith with nothing on the board still smiths.
/// </para>
///
/// <para>
/// <b>What it will not do is take an order it cannot fill.</b> A recipe beyond the bot's skill, or one
/// needing a material it has not got, is refused here rather than discovered at the anvil — because a taken
/// order that fails is a bot's coin held in escrow for nothing while the bot that paid it goes on fighting
/// with a broken sword.
/// </para>
/// </summary>
public sealed class BotSmith : IBotProposer
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotSmith));

    /// <summary>How much metal makes a trip to the anvil worth taking at all.</summary>
    public static int LeastMetal { get; set; } = 6;

    private static bool _saidNoSystem;

    private static bool _saidNoForge;

    private static bool _said;

    /// <summary>Every gate apart, with the denominator. There is no bucket called "other".</summary>
    public static long Asked { get; private set; }

    public static long NoHammer { get; private set; }

    public static long NoMetal { get; private set; }

    public static long NoForge { get; private set; }

    public static long ToOrder { get; private set; }

    /// <summary>Orders passed over for want of iron rather than for want of skill. Counted apart on purpose.</summary>
    public static long ShortOfMetal { get; private set; }

    /// <summary>Smiths past the metal floor whose pack could still not fill any recipe their skill allows.</summary>
    public static long NothingAffordable { get; private set; }

    public static long OnSpec { get; private set; }

    public string Name => "Smith";

    public BotStanding Rung => BotStanding.Free;

    public BotDeed Propose(IBotWilful bot)
    {
        var body = bot?.Self;
        var map = body?.Map;

        if (map == null || map == Map.Internal || !body.Alive)
        {
            return null;
        }

        if (BotAnvil.Kit(body) == null)
        {
            NoHammer++;

            return null;
        }

        Asked++;

        if (BotAnvil.System == null)
        {
            // Content initialisation builds the craft systems, and anything that asks before that gets null.
            // Said once: a smith that never works is otherwise indistinguishable from a lazy one.
            if (!_saidNoSystem)
            {
                _saidNoSystem = true;

                logger.Error("The blacksmithing system does not exist yet, so nobody can forge");
            }

            return null;
        }

        // Any metal it can work, not iron alone. A smith standing on forty bronze ingots was being told it
        // had no metal, which is how a population that digs mostly bronze came to forge almost nothing.
        if (BotAnvil.Ingots(body, BotAnvil.Best(body, LeastMetal)) < LeastMetal)
        {
            NoMetal++;

            return null;
        }

        // A forge with an anvil beside it. BotGround only records the pair — the miner needs both to smelt —
        // so this is the same list, already surveyed, already reachable.
        var smithy = BotGround.Fire(bot, body.Location);

        if (smithy == Point3D.Zero)
        {
            NoForge++;

            if (!_saidNoForge)
            {
                _saidNoForge = true;

                logger.Error("No forge with an anvil beside it is known within reach of the bots on {Map}", map);
            }

            return null;
        }

        // The board first. Nearest order this bot could actually make, and the money on it is already down.
        var order = Order(bot, body);

        if (order != null)
        {
            ToOrder++;
            Once(body, order);

            return new BotForge(map, smithy, order);
        }

        // Nothing the pack can pay for. A named nought rather than a silent return: this is the branch that
        // now catches what used to walk to the forge and report "out of metal" there, so if it ever reads
        // high next to a healthy NoMetal the two floors have drifted apart again.
        if (BotAnvil.Choose(body) == null)
        {
            NothingAffordable++;

            return null;
        }

        OnSpec++;

        return new BotForge(map, smithy);
    }

    /// <summary>
    /// The most valuable standing order this bot could fill out of iron, or null.
    ///
    /// Worth rather than nearness: everything on this board is within a few minutes' walk of everything else,
    /// and what separates two orders is what they are paying.
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

            var recipe = BotAnvil.Recipe(body, want.Kind);

            if (want.Worth <= bestWorth || recipe == null)
            {
                continue;
            }

            // <b>Skill enough and metal enough are two different questions, and only the first was asked.</b>
            // Recipe() answers "is this bot good enough to make one"; it says nothing about whether there is
            // any iron in the pack to make it out of. The proposer's own gate is LeastMetal — six ingots,
            // which is enough to be worth walking to an anvil for — and a ringmail tunic wants eighteen. So a
            // smith holding anything between the two took the order, walked to the forge and failed "out of
            // metal", over and over: fifty-six of the hundred and eight attempts at a hauberk on the night of
            // 25.08.2026, against two that were actually made. And because orders are picked by what they are
            // worth, it reliably chose the dearest one on the board — which is the one needing the most iron.
            //
            // Two numbers on one shelf, and the band between them is where the whole armour trade sat all
            // night. The recipe knows what it costs; nothing had to be invented, only asked.
            var cost = BotCraftwork.Cost(recipe);

            if (BotAnvil.Ingots(body, BotAnvil.Best(body, cost)) < cost)
            {
                ShortOfMetal++;

                continue;
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
            "{Name} is the first smith to take an order off the board: {Buyer} wants {Item} and has {Worth}gp down",
            body.Name,
            order.Buyer?.Self?.Name ?? "somebody",
            order.Label,
            order.Worth
        );
    }

    public static string Describe() =>
        Asked == 0
            ? "nobody with a hammer has been asked to forge"
            : $"{Asked} asked: {ToOrder} took an order off the board, {ShortOfMetal} passed one over for want of iron, {OnSpec} forged on spec, "
              + $"{NoMetal} short of metal, {NothingAffordable} with metal but not enough for any recipe they can work, {NoForge} with no forge in reach";

    public static void Forget()
    {
        _said = false;
        _saidNoSystem = false;
        _saidNoForge = false;
        Asked = 0;
        NoHammer = 0;
        NoMetal = 0;
        NoForge = 0;
        ToOrder = 0;
        ShortOfMetal = 0;
        NothingAffordable = 0;
        OnSpec = 0;
    }
}
