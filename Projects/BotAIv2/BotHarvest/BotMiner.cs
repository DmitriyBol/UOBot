using Server.Logging;

namespace Server.BotAI.V2;

/// <summary>
/// Offers a mining trip to anybody carrying a pick.
///
/// <para>
/// <b>The preconditions live here rather than in the undertaking, and that is the whole point of a
/// proposer.</b> Without a fire on record the chain would end with a bot holding a pack of rock, and the
/// ledger would file that failure against the <em>seam</em> — so the bot would slowly learn that a
/// perfectly good mine was worthless, for a reason that had nothing to do with the mine. Something that
/// cannot be finished is not offered.
/// </para>
///
/// <para>
/// <b>The tool decides who mines, not the class name.</b> A gatherer is born with a pickaxe, bound and
/// weightless; anybody who buys or loots one is a miner while it holds it. The first version asked which
/// archetypes were permitted to work the land, which meant a list that had to be edited every time a class
/// was added — and adding a class silently excluded it.
/// </para>
/// </summary>
public sealed class BotMiner : IBotProposer
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotMiner));

    private static bool _saidNoFire;

    private static bool _saidNoCounter;

    public string Name => "Miner";

    /// <summary>An ordinary want, weighed against every other ordinary want.</summary>
    public BotStanding Rung => BotStanding.Free;

    public BotDeed Propose(IBotWilful bot)
    {
        var body = bot?.Self;
        var map = body?.Map;

        if (map == null || map == Map.Internal)
        {
            return null;
        }

        if (BotOre.Tool(body) == null)
        {
            return null;
        }

        // Whatever this bot is standing in the middle of, swept once for everybody. The first bot to ask is
        // the one that pays for it; the sweep refuses politely once the ground here is known.
        BotGround.Survey(map, body.Location);

        if (BotGround.Fire(bot, body.Location) == Point3D.Zero)
        {
            Missing(ref _saidNoFire, "a fire", map);

            return null;
        }

        if (BotGround.Counter(bot, body.Location) == Point3D.Zero)
        {
            Missing(ref _saidNoCounter, "a counter", map);

            return null;
        }

        var seam = BotGround.Seam(bot);

        if (seam.Exists)
        {
            return new BotDig(seam);
        }

        // No rock on record, but a pack with ore in it. A seam at the bot's own feet is the honest way to
        // say "there is nothing to dig here": the undertaking finds nothing to swing at, sees what it is
        // already carrying, and goes straight to the fire with it. Ore left in a pack is ore nobody can buy.
        if (BotOre.Carried(body) < BotOre.WorthSmelting)
        {
            return null;
        }

        // <b>Unless carrying it somewhere has just failed here.</b> This is the one offer in the subsystem
        // whose place is the bot's own feet, so it is the one that repeats identically for as long as the bot
        // stands still — and a bot holding ore it cannot melt stands very still indeed. Everything else is
        // spared this by being keyed to a seam somewhere else; without the check, a miner whose forge cannot
        // be reached takes the same trip and fails it three times a second until the shard restarts.
        var ledger = bot.Resolve?.Ledger;

        if (ledger != null && ledger.Cautious(BotDig.Trade, map, body.Location))
        {
            return null;
        }

        return new BotDig(new BotSeam(map, body.Location, "ore", 0.0));
    }

    private static void Missing(ref bool said, string what, Map map)
    {
        if (said)
        {
            return;
        }

        said = true;

        // Once, by name, in the same voice the module loader uses for a subsystem that ought to be running.
        // Silence here would look exactly like a population that did not feel like mining.
        logger.Error(
            "Mining is not offered on {Map}: no {What} has been found that a bot could reach, so the trip could not be finished",
            map,
            what
        );
    }

    /// <summary>
    /// Lets the complaints be made again. Called when the world is reloaded: the next world may well have a
    /// forge in it, and a warning suppressed for ever is a warning about the wrong world.
    /// </summary>
    public static void Forget()
    {
        _saidNoFire = false;
        _saidNoCounter = false;
    }
}
