using Server.Logging;

namespace Server.BotAI.V2;

/// <summary>
/// The way in. Everything this assembly does starts here, and this file does as little as possible.
///
/// The server finds this assembly through <c>Data/assemblies.json</c> and calls the static
/// <see cref="Configure"/> and <see cref="Initialize"/> by reflection, the same way it drives the
/// shard's own content. Nothing in the engine references this project, so it stays clear of the
/// upstream rebase path entirely.
///
/// <para>
/// <b>It registers modules and names the moments. It does not order them.</b> The list below is
/// "what exists", and its order means nothing — each module declares what it needs and
/// <see cref="BotModules"/> works out the sequence. That is the one thing this file is designed to
/// avoid becoming: the first version's equivalent was a run of thirty calls held together by fifteen
/// comments saying "this must come after that", and a mistake in it was undetectable by reading. The
/// worst of them cost eleven nulls in an index that then behaved plausibly for a whole session.
/// </para>
///
/// <para>
/// Three moments, and the third is the one that is easy to get wrong. <see cref="Configure"/> is
/// before the world exists. <see cref="OnWorldLoad"/> is after it is in memory — and it can happen
/// more than once, because a world can be reloaded, which is why modules are reset first rather than
/// asked to start twice. <see cref="Initialize"/> only reports.
/// </para>
/// </summary>
public static class BotCore
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotCore));

    private static bool _loadedOnce;

    /// <summary>
    /// Master switch, read from <c>bots.enabled</c> in modernuo.json and written back on first boot so
    /// it is discoverable in the config file rather than buried in code. Each module has its own switch
    /// besides this one — <c>bots.&lt;name&gt;.enabled</c>.
    /// </summary>
    public static bool Enabled { get; private set; }

    public static void Configure()
    {
        Enabled = ServerConfiguration.GetOrUpdateSetting("bots.enabled", true);

        if (!Enabled)
        {
            return;
        }

        // Before the modules and before the world: a persistence must be registered by the time the save is
        // read, and this one carries the only thing about a bot that is worth keeping across a restart.
        BotProgress.Configure();

        // What the population found out about the island, kept the same way. See BotQuadStore: the ground's
        // reputation is a fact about the world rather than about anybody standing on it, and it took a
        // company most of an evening to learn.
        BotQuadStore.Configure();

        // Everything this assembly is made of. Order here is not meaningful; dependencies are.
        BotModules.Register(new BotClassModule());
        BotModules.Register(new BotMovementModule());
        BotModules.Register(new BotSquadModule());
        BotModules.Register(new BotWillModule());
        BotModules.Register(new BotHarvestModule());
        BotModules.Register(new BotShopsModule());
        BotModules.Register(new BotCraftModule());
        BotModules.Register(new BotAuctionModule());
        BotModules.Register(new BotSpellsModule());
        BotModules.Register(new BotHuntModule());
        BotModules.Register(new BotMendModule());
        BotModules.Register(new BotPopulationModule());

        // After the population, because a captain deciding whether to hold a class reads the whole roster to
        // find out whether anybody on the island is worth teaching.
        BotModules.Register(new BotDrillModule());

        // After the squads, whose module it needs, and it says so itself rather than relying on this order.
        BotModules.Register(new BotBaronModule());
        BotModules.Register(new BotDashboardModule());

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

    /// <summary>
    /// The world is in memory. Anything that had to ask about the map may now do so.
    ///
    /// Reachable more than once. On a reload the modules that are already running are put back to
    /// nothing first, because their counters describe a population that is about to be rebuilt — and a
    /// total that is never reset does not look wrong, it looks like a population twice the size.
    /// </summary>
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
