using Server.Logging;

namespace Server.BotAI.V2;

public sealed class BotSquadModule : BotModule
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotSquadModule));

    public override string Name => "Squads";

    public override BotPhase Phase => BotPhase.World;

    public override string[] Requires => ["Classes"];

    public override void Start()
    {
        BotSquadConfig.Load();
        BotSquads.Start();

        BotWill.Offer(new BotPatrol());

        BotWill.Offer(new BotEnlister());

        BotWill.Offer(new BotClearer());

        BotWill.Offer(new BotLifeline());

        logger.Information(
            "Clearings ready: any fighter of a main skill at {Skill:F0} or more may call a company for a square within {Reach} tiles where {Deaths} or more died in the last {Recent} minutes, the ones on a road between towns first, when the bots within {Call} tiles of the muster bring ×{Fit:F2} the strength the square asks (what has killed there, or what stands there); a company cleared of its ground lifts the quarantines and the closed boxes on it ({Running})",
            BotClearer.LeastSkill,
            BotClearer.Reach,
            BotClearer.LeastDeaths,
            BotClearer.RecentMs / 60000,
            BotHarrow.Reach,
            BotHarrow.ClearFit,
            BotClearer.Running ? "on" : "off"
        );

        logger.Information(
            "Squads ready: up to {Max} to a squad beating every {Beat}ms; knots of {Knot} at {Spread} tiles while sweeping; a fight is broken off after {Stall}ms without the target's health falling, after {Blind}ms of everybody who has arrived being unable to land one at all, after {Close}ms of nobody arriving at all, and capped at {Cap}ms; a company with nothing to fight looks for one every {Look}ms and disbands after {Idle}ms of finding nothing, and lets go of anybody idle in it after {Rest}ms",
            BotSquad.MaxSize,
            BotSquads.BeatMs,
            BotScatter.KnotSize,
            BotScatter.Spread,
            BotSquad.NoProgressMs,
            BotSquad.BlindMs,
            BotSquad.CloseMs,
            BotSquad.FightCapMs,
            BotSquad.HuntEveryMs,
            BotSquad.IdleCapMs,
            BotSquad.RestCapMs
        );

        logger.Information(
            "Patrols ready: a captain may march {Least} or more up to {Range} tiles onto a square of {Side} tiles reading {Worry:F0} or worse — a blow counts {Blow:F0} and a death {Death:F0}, halving every {Half}ms; it holds the ground {Hold}ms before quiet means anything and comes home after {Cap}ms; it is shown the {Tries} worst squares in turn, will try {Bends} corners of one before giving it up, and a square given up on is off the list for {Baulk}ms",
            BotSweep.Least,
            BotPatrol.Range,
            BotPeril.Side,
            BotPeril.Worrying,
            BotPeril.PerBlow,
            BotPeril.PerDeath,
            BotPeril.HalfLifeMs,
            BotSweep.HoldMs,
            BotSweep.CapMs,
            BotPeril.Tries,
            BotSweep.MaxBends,
            BotPeril.BaulkMs
        );
    }

    public override void Reset()
    {
        logger.Information("Squads, before the reload: {State}", BotSquads.Describe());

        BotSquads.Stop();
        BotSquads.Reset();
        BotSquad.ForgetSpares();
    }

    public static string Summarise() => $"{BotSquads.Describe()}; {BotEnlister.Describe()}; {BotPatrol.Describe()}";
}
