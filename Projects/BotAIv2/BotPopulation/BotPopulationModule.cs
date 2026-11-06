using Server.Logging;

namespace Server.BotAI.V2;

public sealed class BotPopulationModule : BotModule
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotPopulationModule));

    public override string Name => "Population";

    public override BotPhase Phase => BotPhase.World;

    public override string[] Requires => ["Classes", "Movement", "Will"];

    public override void Start()
    {
        BotPopulationConfig.Load();

        BotRestConfig.Load();

        BotWill.Offer(new BotUndertaker());

        BotWill.Offer(new BotPorter());

        BotWill.Offer(new BotHomer());

        var mix = BotGrowth.Mix(BotPopulationConfig.Mix);
        var deleted = BotPopulation.Reclaim(mix, out var kept);
        var back = 0;

        foreach (var (_, many) in kept)
        {
            back += many;
        }

        if (back > 0 || deleted > 0)
        {
            logger.Information(
                "{Back} bots came back from the world save with their place, their pack, their bank and what they had learned, {Ghosts} of them dead at the save and left for the reviver; {Deleted} were deleted for not being asked for any more",
                back,
                BotMobile.BackAsGhost,
                deleted
            );
        }

        var born = BotPopulation.Raise(mix, kept);

        if (born == 0 && back == 0)
        {

            logger.Error(
                "No bots were raised, so nothing else in this assembly will do anything. Check the class names and the home point in Configuration/bot-population.json"
            );

            return;
        }

        BotBeat.Start();

        logger.Information(
            "Population raised: {Born} bots at {Where} on {Map}; {State}",
            born,
            BotPopulation.Where,
            BotPopulation.Home,
            BotBeat.Describe()
        );

        BotRest.Start();
        BotPeoples.Load();

        BotGuilds.Muster();

        BotUnderworld.Reform();

        logger.Information(
            "Learning carried over: {Restored} of {Remembered} remembered bots picked up where they left off, with {Returned}gp of earlier earnings handed back (savings carry over: {Savings})",
            BotProgress.Restored,
            BotProgress.Remembered,
            BotProgress.Returned,
            BotProgress.Savings
        );
    }

    public override void Reset()
    {
        BotGuilds.Forget();
        BotCharter.Forget();

        logger.Information("The island, before the reload: {State}", BotQuad.Describe());

        logger.Information("Population, before the reload: {State}", BotPopulation.Describe());

        BotRest.Stop();
        BotBeat.Reset();
        BotPopulation.Reset();
    }

    public static string Summarise() => BotPopulation.Describe();
}
