using Server.Logging;

namespace Server.BotAI.V2;

/// <summary>
/// Fighting for a living, as a module.
///
/// <para>
/// <see cref="BotPhase.World"/> and requires <c>Classes</c>, <c>Will</c> and <c>Auction</c> — the last
/// because what comes off a corpse goes onto the market before it goes to a counter.
/// </para>
///
/// <para>
/// <b>This is the module that gives the world money.</b> Before it, every coin on the shard was a coin that
/// already existed: bots are born with none, trade between them only moves it about, and a shopkeeper's
/// counter was a place where it left. So every piece of work that cost something to begin failed on its first
/// beat, and the only thing that could happen was digging, which is free. A monster's purse is where gold
/// comes from, and everything else — the crafter paid for a blade, the scribe paid for a scroll, the miner
/// paid for ore — is that same gold moving one step further from the field.
/// </para>
/// </summary>
public sealed class BotHuntModule : BotModule
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotHuntModule));

    public override string Name => "Hunt";

    public override BotPhase Phase => BotPhase.World;

    public override string[] Requires => ["Classes", "Will", "Auction", "Spells"];

    public override void Start()
    {
        BotHuntConfig.Load();

        BotWill.Offer(new BotHunter());

        BotWill.Offer(new BotGleaner());

        BotWill.Offer(new BotPicker());

        BotWill.Offer(new BotPlunderer());

        BotWill.Offer(new BotLiberator());

        BotWill.Offer(new BotMuster());

        BotWill.Offer(new BotRescuer());
        BotWill.Offer(new BotDefender());

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
            "A cry for help carries {Carries} tiles and stands {Holds}ms; anybody above {Fit:P0} health within {Reach} tiles may answer, and a bot hits back at whatever is on it inside {Near} tiles",
            BotCry.Carries,
            BotCry.HoldsMs,
            BotRescuer.FitAt,
            BotRescuer.Reach,
            BotDefender.Reach
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
