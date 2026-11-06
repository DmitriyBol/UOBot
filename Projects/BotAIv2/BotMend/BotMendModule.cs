using Server.Logging;

namespace Server.BotAI.V2;

public sealed class BotMendModule : BotModule
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotMendModule));

    public override string Name => "Mend";

    public override BotPhase Phase => BotPhase.World;

    public override string[] Requires => ["Classes", "Will"];

    public override void Start()
    {
        BotMendConfig.Load();

        BotWill.Offer(new BotMedic());
        BotWill.Offer(new BotSurgeon());

        BotWill.Offer(new BotAttendant());

        BotWill.Offer(new BotHouseCalls());

        BotWill.Offer(new BotFugitive());

        BotWill.Offer(new BotFugitive(early: true));

        logger.Information(
            "Mending ready: a bot looks after itself below {Hurt:P0} health and stops at {Mended:P0}, spell before cloth; a caster watches {Watch} tiles for somebody worse off",
            BotMend.Hurt,
            BotMend.Mended,
            BotSurgeon.Reach
        );

        logger.Information(
            "Flight ready: on the same rung, a hurt bot runs when what is within {Watch} tiles comes to more than {Bearable:P0} of the strength it has left, and gives up after {GiveUp}s",
            BotBolt.Watch,
            BotFugitive.Bearable,
            BotBolt.GiveUpMs / 1000
        );
    }

    public override void Reset()
    {
        BotMedic.Forget();
        BotMend.Forget();
        BotSurgeon.Forget();
        BotAttendant.Forget();
        BotHouseCalls.Forget();
        BotSalve.Forget();
        BotRetainer.Forget();
        BotFugitive.Forget();
    }
}
