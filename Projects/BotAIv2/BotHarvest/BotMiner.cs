using Server.Logging;

namespace Server.BotAI.V2;

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

        if (BotGround.Sweeping(map, body.Location))
        {
            Awaiting++;

            return null;
        }

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

    public static long Awaiting { get; private set; }

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
        Awaiting = 0;
        _saidNoFire = false;
        _saidNoCounter = false;
    }
}
