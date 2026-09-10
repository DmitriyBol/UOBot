using Server.Logging;

namespace Server.BotAI.V2;

/// <summary>
/// The guilds' halls, as a module.
///
/// <para>
/// <see cref="BotPhase.World"/> and after <c>Population</c>, and that order is not a preference: the first
/// thing this module does is hand every hall in the world back to the guild that owns it, and a guild has no
/// leader until the population has been built and enrolled. Started earlier it would adopt nothing, every
/// hall on the island would stay ownerless, and this era condemns an ownerless house.
/// </para>
/// </summary>
public sealed class BotEstateModule : BotModule
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotEstateModule));

    public override string Name => "Estate";

    public override BotPhase Phase => BotPhase.World;

    public override string[] Requires => ["Classes", "Will", "Population"];

    public override void Start()
    {
        BotEstateConfig.Load();

        // The world first: whatever was raised on a previous evening is standing out there right now with a
        // deleted owner, and every minute before this runs is a minute it is decaying.
        BotEstate.Adopt();

        BotWill.Offer(new BotSteward());

        // The second half of an estate: a hall is bought once, and then fitted out one bench at a time for
        // as long as the guild can afford it. See BotFitter.
        BotWill.Offer(new BotFitter());

        // And the third: a shopkeeper of the guild's own, once the workshop is finished. See BotHirer.
        BotWill.Offer(new BotHirer());

        // And the fourth, which is what makes the third worth 1,252gp: something on the shopkeeper's shelf.
        // See BotSupplier.
        BotWill.Offer(new BotSupplier());

        // And the yard round the hall, which is what a hall makes of the ground. See BotLand and BotBailiff:
        // the claim is a preference and the eviction it makes possible is deliberately almost nothing.
        BotWill.Offer(new BotBailiff());

        logger.Information(
            "Estate ready: a guild raises a hall at {Price}gp levied off its members, each keeping {Keep}gp back, up to {Max} on the island; plots are looked for between {Near} and {Far} tiles of home, {Apart} apart, {Budget} put to the engine at a time",
            BotEstate.Price,
            BotEstate.Keep,
            BotEstate.MaxHalls,
            BotPlot.Near,
            BotPlot.Far,
            BotPlot.Apart,
            BotPlot.Budget
        );

        logger.Information(
            "Halls found in the world: {Adopted} taken back and handed to their guilds; fittings {Fittings}, and a guild's benches are its own — nobody outside it is offered one",
            BotEstate.Adopted,
            BotFittings.Running ? "on" : "off"
        );

        logger.Information(
            "Land and standing: a hall claims {Reach} tiles, worth x{Home} to its own and x{Abroad} to anybody else; guilds fall out below {Enmity} and make peace above {Amity}, and war is {War}",
            BotLand.Reach,
            BotLand.Home,
            BotLand.Abroad,
            BotRegard.Enmity,
            BotRegard.Amity,
            BotRegard.Warring ? "ON — bots of guilds at war may strike and loot each other" : "off, so a bad enough quarrel is counted and nothing more"
        );
    }

    public override void Reset()
    {
        BotEstate.Forget();
        BotPlot.Forget();
        BotSteward.Forget();
        BotFitter.Forget();
        BotHirer.Forget();
        BotSupplier.Forget();
        BotOffice.Forget();
        BotLand.Forget();
        BotRegard.Forget();
        BotBailiff.Forget();
        BotShelf.Forget();
        BotFittings.Forget();
    }
}
