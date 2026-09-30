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

        BotEstate.Adopt();

        BotWar.Reconcile();

        BotWill.Offer(new BotSteward());

        BotWill.Offer(new BotReeve());

        BotWill.Offer(new BotFitter());

        BotWill.Offer(new BotHirer());

        BotWill.Offer(new BotSupplier());

        BotWill.Offer(new BotBailiff());

        BotWill.Offer(new BotTollman());

        BotWill.Offer(new BotFeuder());

        BotWill.Offer(new BotRemover());

        BotWill.Offer(new BotEnlarger());

        BotAbode.Adopt();
        BotWill.Offer(new BotAbodeBuyer());
        BotWill.Offer(new BotReposer());

        BotOutpost.Adopt();
        BotWill.Offer(new BotOutposter());

        BotWill.Offer(new BotHolder());

        logger.Information(
            "Estate ready: a guild raises a hall at {Price}gp levied off its chest and then its members, each keeping {Keep}gp back, up to {Max} on the island; plots are looked for between {Near} and {Far} tiles of home, {Apart} apart, {Budget} put to the engine at a time; {Dues}",
            BotEstate.Price,
            BotEstate.Keep,
            BotEstate.MaxHalls,
            BotPlot.Near,
            BotPlot.Far,
            BotPlot.Apart,
            BotPlot.Budget,
            BotDues.Running
                ? $"members pay {BotDues.Share:P0} of what their work brings in into their guild's chest, up to {BotDues.Ceiling}gp a chest, and a chest {(BotDues.Saving ? "keeps the price of the hall (or of a house in Britain) back from everyday draws" : "keeps nothing back")}"
                : "no dues are paid"
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

        logger.Information(
            "Wars: won at {Kills} dead or {Loot}gp of plunder, no peace before {Least} minutes, judged at {Longest}, a truce of {Truce} after, one declaration a guild per {Every} minutes and {Most} at a time; an enemy on a guild's own ground is answered from {Defend} tiles at {DefendPrior}/min; opinions forget {Mend} a drift",
            BotWar.Kills,
            BotWar.Loot,
            BotWar.LeastMs / 60000,
            BotWar.LongestMs / 60000,
            BotWar.TruceMs / 60000,
            BotWar.DeclareEveryMs / 60000,
            BotWar.MostWars,
            BotFeud.Defend,
            BotQuarrel.DefendPrior,
            BotRegard.Mend
        );

        logger.Information(
            "Seats: {Seats}; a hall is at home within {Settled} tiles of its seat, members are born and rise beside their hall, and a hall further off is carried to the seat",
            BotSeat.Tell(),
            BotSeat.Settled
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
        BotFeuder.Forget();
        BotQuarrel.Forget();
        BotFeud.Forget();
        BotExile.Forget();
        BotWar.Forget();
        BotSeat.Forget();
        BotClaim.Forget();
        BotHolder.Forget();
        BotReeve.Forget();
        BotChest.Forget();
        BotDues.Forget();
        BotToll.Forget();
        BotTollman.Forget();
        BotHold.Forget();
        BotRemover.Forget();
        BotRemove.Forget();
        BotEnlarger.Forget();
        BotAbode.Forget();
        BotAbodeBuyer.Forget();
        BotReposer.Forget();
        BotOutpost.Forget();
        BotOutposter.Forget();
        BotShelf.Forget();
        BotFittings.Forget();
    }
}
