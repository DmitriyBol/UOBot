using Server.Logging;

namespace Server.BotAI.V2;

/// <summary>
/// Getting a living out of the ground, as a module: reads its numbers and offers the trade to the decision
/// layer.
///
/// <para>
/// <see cref="BotPhase.World"/>, because everything it knows is a place. Requires <c>Will</c>, which it
/// hands a proposer, and <c>Classes</c>, because a gatherer's pickaxe comes from its kit.
/// </para>
///
/// <para>
/// <b>This is the first module that gives the population something to want</b>, and it is deliberately the
/// gatherer's chain rather than the fighter's: dig, melt, bank exercises every part of the machinery at
/// once — stages that survive an interruption, a named skill that only counts when the work finishes, goods
/// that are worth something before they are sold, and a definition of finished that is somewhere other than
/// where the work happened. A hunt would have exercised none of the last three.
/// </para>
///
/// <para>
/// Its switch is worth having for the plainest reason: with it off, the population has nothing to do and
/// says so in the census. That is the cheapest way to tell "the brain is not choosing" from "there is
/// nothing to choose".
/// </para>
/// </summary>
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
