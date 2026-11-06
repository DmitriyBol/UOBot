using Server.Logging;

namespace Server.BotAI.V2;

public sealed class BotHuntModule : BotModule
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotHuntModule));

    public override string Name => "Hunt";

    public override BotPhase Phase => BotPhase.World;

    public override string[] Requires => ["Classes", "Will", "Market", "Spells"];

    public override void Start()
    {
        BotHuntConfig.Load();

        BotCombatConfig.Load();

        BotWill.Offer(new BotHunter());

        BotWill.Offer(new BotTamerProposer());

        BotWill.Offer(new BotGleaner());

        BotWill.Offer(new BotPicker());

        BotWill.Offer(new BotPlunderer());

        BotWill.Offer(new BotLiberator());

        BotWill.Offer(new BotMuster());

        BotWill.Offer(new BotRescuer());
        BotWill.Offer(new BotDefender());

        BotWill.Offer(new BotDefender(last: true));

        BotOutlaw.Start();

        logger.Information(

            "The hunt is on: quarry looked for {Reach} tiles out and taken up to ×{Daring} of our own power, anything inside {Notice} tiles worth dropping other work for, set out above {Fit:P0} health and given up below {Flee:P0} or when outnumbered; ground to look over is picked beyond {Beyond} tiles and walked to within {Arrive}",
            BotQuarry.Reach,
            BotQuarry.Daring,
            BotQuarry.Notice,
            BotHunter.FitAt,
            BotSlay.FleeAt,
            BotQuarry.Reach,
            BotProwl.ArriveWithin
        );

        logger.Information(
            "One bot leaves a fight it is not winning: {NoProgress}ms without the quarry's health falling, and {Cap}ms all told from taking it on",
            BotSlay.NoProgressMs,
            BotSlay.CapMs
        );

        logger.Information(
            "A cry for help carries {Carries} tiles and stands {Holds}ms; anybody above {Fit:P0} health within {Reach} tiles may answer, and a bot hits back at whatever is on it inside {Near} tiles; a dire cry (under {Dire:P0}) summons up to {Most} at once{Summons}, a steady one its nearest free neighbour within {NearReach} tiles when the thing is {NearOdds:P0} of its own strength or more{NearCalls}; a bot refused the fight for the odds with no flight on offer takes the stand{Stands}; one unanswered cry in {Sample} is said with who was in earshot",
            BotCry.Carries,
            BotCry.HoldsMs,
            BotRescuer.FitAt,
            BotRescuer.Reach,
            BotDefender.Reach,
            BotCry.DireAt,
            BotCry.MostCalled,
            BotCry.Summons ? "" : " (off)",
            BotCry.NearReach,
            BotCry.NearOdds,
            BotCry.NearCalls ? "" : " (off)",
            BotDefender.Stands ? "" : " (off)",
            BotReflex.SampleEvery
        );

        logger.Information(
            "Companies may be called: anything beyond one bot but within ×{Tolerance} of everybody inside {Reach} tiles, when at least {Least} others are free",
            BotThreat.Tolerance,
            BotMuster.Reach,
            BotMuster.Least
        );

        logger.Information(
            "Chests are worth going through: looked for {Reach} tiles out once a minute, reached into from {Touch} tiles, broken off when something living is within {Danger}; locked ones, trapped ones and any standing inside a town are passed over, and an emptied one is left alone {Emptied}ms",
            BotPlunder.Reach,
            BotPlunder.Touch,
            BotPlunder.Danger,
            BotPlunder.EmptiedMs
        );

        logger.Information(
            "Prisoners are worth freeing: heard {Reach} tiles out once a minute, accepted from {Touch} tiles, and walked to {Town} whatever town the engine picked for them — passed over only when the cage is further than {Roam} tiles from it; the escort turns back for a prisoner more than {Lag} tiles behind until it is within {Rejoin}, and gives up on one that gets no nearer home in {Stuck}ms; the engine pays around {Reward}gp, and refuses anybody who escorted somebody else inside five minutes",
            BotFreedom.Reach,
            BotFreedom.Touch,
            BotFreedom.Town,
            BotFreedom.Roam,
            BotFreedom.Lag,
            BotFreedom.Rejoin,
            BotFreedom.StuckMs,
            BotFreedom.Reward
        );
    }

    public static string Summarise() =>
        $"{BotQuarry.Describe()}; {BotPlunder.Describe()}; {BotFreedom.Describe()}";

    public override void Reset()
    {
        BotHunter.Forget();
        BotMuster.Forget();
        BotBand.Forget();
        BotRescuer.Forget();
        BotOutlaw.Stop();
        BotOutlaw.Forget();
        BotRobber.Forget();
        BotManhunt.Forget();
        BotLawful.Forget();
        BotRaid.Forget();
        BotInquest.Forget();
        BotBrawl.Forget();
        BotDuel.Forget();
        BotArms.Forget();
        BotSlay.ForgetBows();

        BotCry.Forget();

        BotQuarry.Forget();

        BotPlunder.Forget();
        BotPlunderer.Reset();

        BotFreedom.Forget();
        BotLiberator.Reset();
    }
}
