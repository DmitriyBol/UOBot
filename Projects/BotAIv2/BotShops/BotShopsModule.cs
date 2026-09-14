using Server.Logging;

namespace Server.BotAI.V2;

/// <summary>
/// Trading with the shopkeepers, as a module.
///
/// <para>
/// <see cref="BotPhase.World"/> — a shopkeeper is a mobile standing somewhere. Requires <c>Classes</c> (what
/// a bot is short of comes from its class's kit) and <c>Will</c> (it hands over a proposer).
/// </para>
///
/// <para>
/// This is the module that gives the population an <em>input</em> side. Until it existed the whole assembly
/// could only take things out of the ground: nothing could be bought, so no trade could depend on materials
/// it did not dig up itself.
/// </para>
///
/// <para>
/// <b>And both directions of the world's money live here.</b> Coin handed to a shopkeeper leaves the world;
/// coin taken from one is the only coin that enters it. Bots are born with none and nothing else mints any, so
/// without <see cref="BotPeddler"/> every piece of work that costs money to start fails on its first beat and
/// the only thing that can happen on the shard is digging. Which of the two dominates is the whole health of
/// the economy, and it is one subtraction: see the <c>bought for / sold for</c> line this module prints.
/// </para>
/// </summary>
public sealed class BotShopsModule : BotModule
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotShopsModule));

    public override string Name => "Shops";

    public override BotPhase Phase => BotPhase.World;

    public override string[] Requires => ["Classes", "Will"];

    public override void Start()
    {
        BotShopsConfig.Load();

        BotWill.Offer(new BotShopper());
        BotWill.Offer(new BotPeddler());

        BotWill.Offer(new BotUpkeep());

        BotWill.Offer(new BotBullion());

        logger.Information(
            "Shops ready: shopkeepers swept {Reach} tiles around the first bot to ask, traded with from {Counter} tiles, a supply is restocked once it falls below {Short:P0} of what the bot was born with, and goods the market has ignored for {Peddle} minutes are carried to a counter",
            BotShops.Reach,
            BotShops.CounterReach,
            BotShopper.Short,
            BotPeddler.IgnoredMs / 60000
        );
    }

    public override void Reset()
    {
        logger.Information("Shops, before the reload: {State}", BotShops.Describe());

        BotShops.Reset();
        BotShopper.Forget();
        BotPeddler.Forget();
        BotUpkeep.Forget();
        BotBullion.Forget();
    }

    public static string Summarise() => BotShops.Describe();
}
