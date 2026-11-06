using Server.Logging;

namespace Server.BotAI.V2;

public static class BotCore
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotCore));

    private static bool _loadedOnce;

    public static bool Enabled { get; private set; }

    [CallPriority(40)]
    public static void Configure()
    {
        Enabled = ServerConfiguration.GetOrUpdateSetting("bots.enabled", true);

        if (!Enabled)
        {
            return;
        }

        BotProgress.Configure();

        BotQuadStore.Configure();

        BotCommonsStore.Configure();

        BotOutlawStore.Configure();

        BotCityStore.Configure();

        BotMarketStore.Configure();

        BotChestStore.Configure();

        BotQuestStore.Configure();

        BotClaimStore.Configure();

        BotSeatStore.Configure();

        BotWarStore.Configure();

        BotSeizureStore.Configure();

        BotUnderworldStore.Configure();

        BotCrimeStore.Configure();

        BotMeal.Configure();

        BotModules.Register(new BotClassModule());
        BotModules.Register(new BotMovementModule());
        BotModules.Register(new BotSquadModule());
        BotModules.Register(new BotWillModule());
        BotModules.Register(new BotHarvestModule());
        BotModules.Register(new BotShopsModule());
        BotModules.Register(new BotCraftModule());
        BotModules.Register(new BotMarketModule());
        BotModules.Register(new BotSpellsModule());
        BotModules.Register(new BotHuntModule());
        BotModules.Register(new BotMendModule());
        BotModules.Register(new BotPopulationModule());

        BotModules.Register(new BotDrillModule());

        BotModules.Register(new BotEstateModule());

        BotModules.Register(new BotQuestModule());

        BotModules.Register(new BotBaronModule());

        BotModules.Register(new BotDelveModule());

        BotModules.Register(new BotTravelModule());

        BotModules.Register(new BotResidenceModule());

        BotModules.Register(new BotGuildHouseModule());

        BotModules.Register(new BotShopkeepModule());

        BotModules.Register(new BotTradersModule());

        BotModules.Register(new BotCampModule());

        BotModules.Register(new BotTradesModule());

        BotModules.Register(new BotDiplomacyModule());

        BotModules.Register(new BotPoliticsModule());

        BotModules.Register(new BotRunesModule());

        BotModules.Register(new BotSeaModule());

        BotModules.Register(new BotProvingModule());

        BotModules.Register(new BotZonesModule());

        BotModules.Register(new BotReachModule());
        BotModules.Register(new BotDashboardModule());

        BotModules.Register(new BotAlarmModule());

        BotModules.Register(new BotVoiceModule());

        BotModules.Register(new BotSocialModule());

        BotModules.Register(new BotCrimeModule());

        BotModules.Register(new BotWebModule());

        BotModules.Start(BotPhase.Settings);

        EventSink.WorldLoad += OnWorldLoad;
    }

    public static void Initialize()
    {
        logger.Information(
            "BotAI v2 loaded ({Status}): {Modules}",
            Enabled ? "enabled" : "disabled",
            Enabled ? BotModules.Describe() : "nothing registered"
        );
    }

    private static void OnWorldLoad()
    {
        if (_loadedOnce)
        {
            BotModules.Rewind(BotPhase.World);
        }

        _loadedOnce = true;

        BotModules.Start(BotPhase.World);
    }
}
