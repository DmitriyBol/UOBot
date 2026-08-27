using Server.Logging;

namespace Server.BotAI.V2;

/// <summary>
/// The market as a module: reads how fast bots may change their minds, and starts its own beat.
///
/// <para>
/// <see cref="BotPhase.World"/> and requires nothing. It is a place rather than a participant — goods can be
/// put out before anybody exists to put them out, and a market with nothing in it is a market, not an error.
/// </para>
///
/// <para>
/// Registered before the population on purpose. A reload resets modules in registration order, and clearing
/// the market first means the stalls are emptied while their sellers still exist; the other way round the
/// market spends a moment holding goods belonging to bots that have already gone.
/// </para>
/// </summary>
public sealed class BotAuctionModule : BotModule
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotAuctionModule));

    public override string Name => "Auction";

    public override BotPhase Phase => BotPhase.World;

    public override void Start()
    {
        BotAuctionConfig.Load();

        BotAuction.Start();

        logger.Information(
            "The market is open on both sides: prices rise {Raise:P0} when the same goods sell inside {Brisk}ms, fall {Cut:P0} after {Stale}ms untouched, and stay between ×{Least} and ×{Most} of the opening ask — a want moves the same way with the sign turned round, and one supplier may fill at most {Slice} units of it at a time; it has room for {Stalls} stalls and {Wants} wants; produced goods {Listed}",
            BotAuction.RaiseStep,
            BotAuction.BriskMs,
            BotAuction.CutStep,
            BotAuction.StaleMs,
            BotAuction.LeastMultiple,
            BotAuction.MostMultiple,
            BotAuction.Slice,
            BotAuction.MaxListings,
            BotAuction.MaxWants,
            BotDig.ListGoods ? "go to the market" : "go to the bank box"
        );
    }

    /// <summary>
    /// A world reload is a different world, and every stall in this one belongs to a seller about to stop
    /// existing. Goods are destroyed rather than handed back, because a bank box belonging to a bot that is
    /// being deleted is not somewhere anything survives.
    /// </summary>
    public override void Reset()
    {
        logger.Information("The market, before the reload: {State}", BotAuction.Describe());

        BotAuction.Reset();
    }

    public static string Summarise() => BotAuction.Describe();
}
