using Server.Logging;

namespace Server.BotAI.V2;

public sealed class BotClassModule : BotModule
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotClassModule));

    public override string Name => "Classes";

    public override BotPhase Phase => BotPhase.Settings;

    public override void Start()
    {
        BotClassConfig.Load();

        logger.Information(
            "Classes: {Count} — {Melee} melee, {Ranged} ranged, {Casters} caster, {Medics} medic, {Producers} producing; {Casting} of them cast",
            BotClasses.All.Count,
            BotClasses.Count(BotRole.Melee),
            BotClasses.Count(BotRole.Ranged),
            BotClasses.Count(BotRole.Caster),
            BotClasses.Count(BotRole.Medic),
            BotClasses.Count(BotRole.Producer),
            BotClasses.Casting
        );
    }
}
