using Server.Logging;

namespace Server.BotAI.V2;

public sealed class BotHarvestModule : BotModule
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotHarvestModule));

    public override string Name => "Harvest";

    public override BotPhase Phase => BotPhase.World;

    public override string[] Requires => ["Classes", "Will", "Population"];

    public override void Start()
    {
        BotHarvestConfig.Load();

        BotGround.Prospect(BotPopulation.Home);

        BotWill.Offer(new BotMiner());

        BotWill.Offer(new BotForager());

        BotWill.Offer(new BotHerbalist());

        BotWill.Offer(new BotWoodsman());

        logger.Information(
            "Harvest ready: a trip is reckoned at {Expects} a minute over {Minutes} minutes, an ingot at {Ingot} gold; the ground is swept {Reach} tiles around the first bot to ask, at most {Sweeps} times",
            BotDig.Prior,
            BotDig.WorkMinutes,
            BotDig.GoldPerIngot,
            BotGround.Reach,
            BotGround.MaxSurveys
        );

        BotHeard.Listen();

        logger.Information(
            "A worked-out seam rests {Rest}s, which is the engine's own longest refill for an ore bank plus a minute; a rest shorter than that hands the next miner a barren hole",
            BotGround.RestFromEngine() / 1000
        );

        logger.Information(
            "Foraging ready: anything the world calls a reagent, picked up from {Reach} tiles away while a pack is under {Full:P0} full, reckoned at {Expects} a minute and put on the board at {Guess}gp a piece",
            BotForage.Reach,
            BotForage.FillFraction,
            BotForage.Prior,
            BotForage.Guess
        );
    }

    public override void Reset()
    {
        logger.Information("Harvest, before the reload: {State}", BotGround.Describe());

        BotHeard.Forget();
        BotGround.Reset();
        BotMiner.Forget();
        BotWoodsman.Forget();

        BotHerbalist.Forget();
    }

    public static string Summarise() => BotGround.Describe();
}
