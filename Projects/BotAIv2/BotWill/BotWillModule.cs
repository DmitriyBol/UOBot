using Server.Logging;

namespace Server.BotAI.V2;

public sealed class BotWillModule : BotModule
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotWillModule));

    public override string Name => "Will";

    public override BotPhase Phase => BotPhase.World;

    public override string[] Requires => ["Classes"];

    public override void Start()
    {
        BotWillConfig.Load();

        BotWill.Deciding = true;

        logger.Information(
            "Will ready: a point of skill is worth {Gold} gold, dying costs {Death} minutes; work is reviewed every {Review}ms, held for what it reckons it needs between {Dwell}ms and {DwellCap}ms, and replaced only above ×{Margin} against ×{Inertia} for the work in hand; steadfast work is held for ×{Stretch} its own reckoning up to {CommitCap}ms unless its walk stops closing for {Trouble} of the watchdog, and is taken up again after what would not wait: {Resume} (not below {ResumeHealth} health); a bot's own trade is worth ×{Own} and another class's ×{Other}; a crowd bites {Crowd}, repetition {Repeat}",
            BotYield.GoldPerSkillPoint,
            BotYield.DeathMinutes,
            BotWill.ReviewMs,
            BotWill.DwellMs,
            BotWill.DwellCapMs,
            BotWill.SwitchMargin,
            BotAppraisal.Inertia,
            BotWill.CommitStretch,
            BotWill.CommitCapMs,
            BotWill.TroubleShare,
            BotWill.Resume,
            BotWill.ResumeHealth,
            BotCalling.OwnTrade,
            BotCalling.OtherTrade,
            BotAppraisal.CrowdBite,
            BotAppraisal.RepetitionBite
        );
    }

    public override void Reset()
    {
        logger.Information("Will, before the reload: {State}", BotWill.Describe());

        BotWill.Deciding = false;
        BotWill.Reset();
    }

    public static string Summarise() => BotWill.Describe();
}
