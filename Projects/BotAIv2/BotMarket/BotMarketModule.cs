using Server.Logging;

namespace Server.BotAI.V2;

public sealed class BotMarketModule : BotModule
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotMarketModule));

    public override string Name => "Market";

    public override BotPhase Phase => BotPhase.World;

    public override string[] Requires => ["Will"];

    public override void Start()
    {
        BotMarketConfig.Load();

        BotMarket.Start();

        BotCity.Start();

        BotWill.Offer(new BotCourier());

        BotWill.Offer(new BotMeeter());

        logger.Information(
            "Meetings {State}: a carrier within {Call} tiles of its buyer calls the meeting; the buyer walks to meet it, or both to the nearest guarded ground within {Safe} tiles when either stands in a hostile or deadly patrol or on a square asking more than it brings; the buyer gives the meeting {Meet} minutes and stands {Wait}s beside the carrier for the hand-over; a buyer that walks on about its work stands where it is for a carrier within {Stand} tiles of it, {StandMs}ms a beat and at most {StandMost}s a walk, and a carrier that comes no nearer to its buyer in {Losing}s of walking after it lets it go",
            BotRendezvous.Running ? "on" : "off",
            BotRendezvous.CallTiles,
            BotRendezvous.SafeReach,
            BotRendezvous.MeetMs / 60000,
            BotRendezvous.WaitMs / 1000,
            BotRendezvous.StandTiles,
            BotRendezvous.StandMs,
            BotRendezvous.StandMostMs / 1000,
            BotDeliver.LosingMs / 1000
        );

        logger.Information(
            "The market is open by the bank in {Town}, hand to hand: wants are said aloud and hold no gold; a want unanswered for {Raise}ms asks {Step:P0} louder up to ×{Most} of its opening, if its buyer could pay the louder price, and gives up there; a carrier holding what is wanted walks up to {Reach} tiles to the buyer for anything worth {Least}gp or more and hands it over within {Meet} tiles; nothing changes hands between two bots that are not standing together",
            BotCapital.Name,
            BotMarket.RaiseMs,
            BotMarket.RaiseStep,
            BotMarket.MostMultiple,
            BotCourier.Reach,
            BotCourier.LeastGold,
            BotHandover.MeetTiles
        );

        logger.Information(
            "Wants reserve their buyer's money: a want is said only if what the buyer's other wants hold leaves enough for it, and everything else a bot spends is read off what is left; a dead buyer keeps its wants, asked again {Grace} minutes after it rises; a raise the buyer cannot reserve leaves the want at its price for up to {Age} minutes; a line is met if every unit arrives within {Window} minutes, and taken back or unpaid inside its first {Kept} minutes it is kept out; the board is {Restored}",
            BotMarket.RisenGraceMs / 60000,
            BotMarket.MostAgeMs / 60000,
            BotLines.WindowMs / 60000,
            BotLines.GraceMs / 60000,
            BotMarket.RestoredWants > 0 ? $"read back from the save with {BotMarket.RestoredWants} wants" : "new"
        );
    }

    public override void Reset()
    {
        logger.Information("The market, before the reload: {State}", BotMarket.Describe());

        BotMarket.Reset();
        BotCity.Stop();
        BotCity.Forget();
    }
}
