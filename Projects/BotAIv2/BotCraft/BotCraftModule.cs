using Server.Logging;

namespace Server.BotAI.V2;

/// <summary>
/// Making things, as a module.
///
/// <para>
/// <see cref="BotPhase.World"/> and requires <c>Classes</c>, <c>Will</c> and <c>Shops</c> — the last because
/// the chain begins at a counter: a crafter with no way to buy cloth is a crafter with an opinion about
/// cloth.
/// </para>
///
/// <para>
/// <b>This is the module that makes the market a market.</b> Mining puts metal out; nothing bought it,
/// because nothing needed it. Sewing is the first trade that <em>buys</em> — and the moment a smith's chain
/// exists, the same shape turns a miner's ingots into somebody's input rather than somebody's pile.
/// </para>
/// </summary>
public sealed class BotCraftModule : BotModule
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotCraftModule));

    public override string Name => "Craft";

    public override BotPhase Phase => BotPhase.World;

    public override string[] Requires => ["Classes", "Will", "Shops"];

    public override void Start()
    {
        BotCraftConfig.Load();

        BotCraftEar.Listen();

        BotWill.Offer(new BotTailor());

        BotWill.Offer(new BotSmith());

        BotWill.Offer(new BotFletcher());

        BotWill.Offer(new BotAlchemist());

        BotWill.Offer(new BotCook());

        logger.Information(
            "Craft ready: a tailor buys {Bolt} cloth at a time, works {Margin} points below its own skill, attempts every {Swing}ms, and asks {Price}gp a piece; a fletcher makes at least {Least} arrows at a time and opens them at {Arrow}gp, buying wood to match the feathers it holds because nobody anywhere sells a feather",
            BotSew.Bolt,
            BotThread.Margin,
            BotSew.SwingMs,
            BotSew.GoldPerPiece,
            BotFletching.LeastArrows,
            BotFletching.Worth
        );

        logger.Information(
            "Brewing ready: a brewer works {Margin} points below its own Alchemy, sets up once it holds {Least} bottles, buys {Batch} empties at a time and opens a draught at {Worth}gp against the alchemist's fifteen; it brews only what the population drinks",
            BotFlask.Margin,
            BotFlask.LeastBottles,
            BotFlask.Batch,
            BotFlask.Worth
        );

    }

    public override void Reset()
    {
        BotTailor.Forget();
        BotSmith.Forget();
        BotFletcher.Forget();
        BotAlchemist.Forget();
        BotCook.Forget();
        BotBake.Forget();
        BotMeal.Forget();
        BotStores.Forget();
    }
}
