using Server.Logging;

namespace Server.BotAI.V2;

/// <summary>
/// The population as a module: reads who should exist, deletes whoever came back from the save, raises the
/// rest, and starts the clock.
///
/// <para>
/// <b>This is the module that turns six written subsystems into a running shard.</b> Everything else has been
/// waiting for an object that holds a bond, a journey and a resolve, and gets asked to act — and until this
/// existed, none of it ran at all.
/// </para>
///
/// <para>
/// <see cref="BotPhase.World"/>, and this one genuinely needs it: a bot is placed on a map. Requires
/// <c>Classes</c> (what a bot is), <c>Movement</c> (the pace of the clock comes from a step's delay) and
/// <c>Will</c> (a turn is a decision followed by a step). <b>Not</b> <c>Harvest</c> — a population with
/// nothing to do is a legitimate state, and it is the state that proves the census is telling the truth
/// rather than covering for a missing subsystem.
/// </para>
///
/// <para>
/// Turned off, the shard is exactly what it was before this file existed: seven subsystems loaded, nobody to
/// use them. That is the cleanest possible A/B for any question of the form "is this the bots or is this the
/// shard".
/// </para>
/// </summary>
public sealed class BotPopulationModule : BotModule
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotPopulationModule));

    public override string Name => "Population";

    public override BotPhase Phase => BotPhase.World;

    public override string[] Requires => ["Classes", "Movement", "Will"];

    public override void Start()
    {
        BotPopulationConfig.Load();

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
