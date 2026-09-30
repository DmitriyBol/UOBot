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

        BotWill.Offer(new BotTinkerer());

        BotWill.Offer(new BotBowyer());

        BotWill.Offer(new BotWeaver());

        BotWill.Offer(new BotFletcher());

        BotAuction.Staple(typeof(Server.Items.Feather));

        BotWill.Offer(new BotAlchemist());

        BotWill.Offer(new BotCook());

        BotWill.Offer(new BotTutor());

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
            "Brewing ready: a brewer works {Margin} points below its own Alchemy, sets up once it holds {Least} bottles, buys as many empties as its herbs can fill (at least {Floor}, at most {Batch}) and opens a draught at {Worth}gp against the alchemist's fifteen; it brews only what the population drinks",
            BotFlask.Margin,
            BotFlask.LeastBottles,
            BotFlask.LeastBottles,
            BotFlask.Batch,
            BotFlask.Worth
        );

        logger.Information(
            "Supplies from our own benches: a weaver walks to the flock with the least road per sheep within {Pasture} tiles and its leash, gathers up to {Wool} wool, and puts cloth over {Keep} out at no more than the tailor asks; a tinker short of iron buys a batch's worth off the cheapest stall; a fletcher short of feathers buys them off a stall; what a maker makes goes out past the auction's lot cap: {Uncapped}",
            BotWeaver.PastureReach,
            BotWeave.WoolAfield,
            BotSew.Bolt * 2,
            BotAuction.MadeUncapped
        );

    }

    public override void Reset()
    {
        BotLooms.Forget();
        BotPastures.Forget();
        BotWeaver.Forget();
        BotTinkerer.Forget();
        BotTinker.Forget();
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
