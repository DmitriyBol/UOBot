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

        if (BotDig.Burdened(body))
        {
            Burdened++;

            return null;
        }

        BotGround.Survey(map, body.Location);

        if (BotGround.Fire(bot, body.Location) == Point3D.Zero)
        {
            Missing(ref _saidNoFire, "fire", body, map);

            return null;
        }

        if (BotGround.Counter(bot, body.Location) == Point3D.Zero)
        {
            Missing(ref _saidNoCounter, "counter", body, map);

            return null;
        }

        var seam = BotGround.Seam(bot);

        if (seam.Exists)
        {
            return new BotDig(seam);
        }

        var frontier = BotGround.Frontier(map, body.Location);

        if (frontier != Point3D.Zero && BotOre.Carried(body) < BotOre.WorthSmelting)
        {
            Sent++;

            return new BotProspect(map, frontier, BotGround.Seams.Count);
        }

        if (BotOre.Carried(body) < BotOre.WorthSmelting)
        {
            return null;
        }

        var ledger = bot.Resolve?.Ledger;

        if (ledger != null && ledger.Cautious(BotDig.Trade, map, body.Location))
        {
            return null;
        }

        return new BotDig(new BotSeam(map, body.Location, "ore", 0.0));
    }

    public static long Sent { get; private set; }

    public static long Burdened { get; private set; }

    private static void Missing(ref bool said, string what, Mobile body, Map map)
    {
        if (said)
        {
            return;
        }

        said = true;

        logger.Error(
            "{Name} at {Where} could not be offered mining on {Map}: no {What} within its own reach, so the trip could not be finished",
            body?.Name ?? "a miner",
            body?.Location ?? Point3D.Zero,
            map,
            what
        );
    }

    public static void Forget()
    {
        Burdened = 0;
        _saidNoFire = false;
        _saidNoCounter = false;
    }
}
