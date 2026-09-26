using Server.Logging;

namespace Server.BotAI.V2;

/// <summary>
/// Offers a trip to a counter to any bot holding a stall the population has ignored.
///
/// <para>
/// <b>The market decides who comes here, and it decides with a number it was already keeping.</b> A stall that
/// has never sold one and has already had its price cut has been in front of every bot on the shard for a full
/// stale period with nobody interested. Nothing new had to be invented to know that — no "is this junk" test,
/// no table of worthless things, no threshold anybody chose. A price that fell and a sales count of zero say
/// it between them.
/// </para>
///
/// <para>
/// <b>And it can only ever offer what the bot itself decided to sell.</b> That is what keeps this from becoming
/// the first version's disaster, where two bots sold the same shopkeeper the same reagents four thousand times
/// because nothing distinguished "goods" from "the things I need to do my job". Here the question cannot be
/// asked about a pack at all: a bot's tools, herbs, paper and bandages are never on a stall, so they are never
/// candidates.
/// </para>
/// </summary>
public sealed class BotPeddler : IBotProposer
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotPeddler));

    private static bool _saidNoBuyer;

    public static int IgnoredMs { get; set; } = 600000;

    public static long Asked { get; private set; }

    public static long Stallless { get; private set; }

    public static long Wanted { get; private set; }

    public static long Fresh { get; private set; }

    public static long NoBuyer { get; private set; }

    public static long Spoken { get; private set; }

    public static long Offered { get; private set; }

    public static string Describe() =>
        Asked == 0
            ? "nobody has been offered a trip to a counter with goods"
            : $"{Asked} looks for something to peddle: {Offered} trips to a counter offered, {Stallless} had nothing of their own on the market, {Spoken} were left alone because the board has money down for that kind, "
              + $"{Wanted} held stalls somebody here still wants, {Fresh} held stalls not yet ignored for {IgnoredMs / 60000} minutes, "
              + $"{NoBuyer} found no shopkeeper in reach who buys the thing";

    public string Name => "Peddler";

    public BotStanding Rung => BotStanding.Free;

    public BotDeed Propose(IBotWilful bot)
    {
        var body = bot?.Self;
        var map = body?.Map;

        if (map == null || map == Map.Internal)
        {
            return null;
        }

        Asked++;

        var stalls = BotAuction.Listings;
        var mine = 0;

        for (var i = 0; i < stalls.Count; i++)
        {
            var stall = stalls[i];

            if (!ReferenceEquals(stall.Seller, bot) || stall.IsEmpty)
            {
                continue;
            }

            mine++;

            if (stall.Traded)
            {
                Wanted++;

                continue;
            }

            if (Core.TickCount - stall.ListedTick < IgnoredMs)
            {
                Fresh++;

                continue;
            }

            if (BotAuction.Demand(bot, stall.Kind) != null)
            {
                Spoken++;

                continue;
            }

            var sample = stall.Sample;

            if (sample == null)
            {
                continue;
            }

            BotShops.Survey(map, body.Location);

            var shop = BotShops.Buyer(bot, sample, out var price);

            if (shop == null)
            {
                NoBuyer++;

                Missing(stall.Label, map);

                continue;
            }

            Offered++;

            return new BotPeddle(shop, stall.Kind, stall.Label, stall.Amount, price);
        }

        if (mine == 0)
        {
            Stallless++;
        }

        return null;
    }

    private static void Missing(string label, Map map)
    {
        if (_saidNoBuyer)
        {
            return;
        }

        _saidNoBuyer = true;

        logger.Error(
            "No shopkeeper within reach of the bots on {Map} buys {Item}, and no bot wants it either; it will sit on the market",
            map,
            label
        );
    }

    public static void Forget()
    {
        _saidNoBuyer = false;
        Asked = 0;
        Stallless = 0;
        Wanted = 0;
        Fresh = 0;
        NoBuyer = 0;
        Offered = 0;
        Spoken = 0;
    }
}
