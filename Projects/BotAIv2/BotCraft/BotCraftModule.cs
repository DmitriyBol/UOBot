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

        BotWill.Offer(new BotTailor());

        // Iron into things, and the board's orders before anything speculative. Until this the whole mining
        // chain ended at a bank box: nothing on the shard could turn an ingot into an object.
        BotWill.Offer(new BotSmith());

        logger.Information(
            "Craft ready: a tailor buys {Bolt} cloth at a time, works {Margin} points below its own skill, attempts every {Swing}ms, and asks {Price}gp a piece",
            BotSew.Bolt,
            BotThread.Margin,
            BotSew.SwingMs,
            BotSew.GoldPerPiece
        );
    }

    public override void Reset()
    {
        BotTailor.Forget();
        BotSmith.Forget();
    }
}
