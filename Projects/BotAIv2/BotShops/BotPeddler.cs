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

    /// <summary>
    /// How long a stall must sit unsold before its owner will carry the goods to a shopkeeper.
    ///
    /// <para>
    /// <b>Its own number, and it had to become one: this used to read <c>BotAuction.StaleMs</c>, which is a
    /// different question wearing the same clothes.</b> That number answers "when should a price come down",
    /// and thirty minutes is right for it — a price that ratchets every five would be a population
    /// undercutting itself all afternoon. This one answers "when has the market plainly said no", and thirty
    /// minutes is far too long for that: no session on this shard had ever lived long enough to reach it, so
    /// the one road by which gold enters this population from outside had never been walked once. The
    /// counters said it in as many words — 1429 of 1452 looks refused for this reason and not one refused
    /// for want of a buyer.
    /// </para>
    ///
    /// <para>
    /// Ten minutes, reckoned from how often the goods are actually looked at rather than chosen for feeling
    /// about right. Thirty bots reconsider their wants every twenty seconds or so, so a stall standing for
    /// ten minutes has been in front of the whole population something like nine hundred times. Anything
    /// that has been refused nine hundred times is not going to sell here, and the shopkeeper's lower price
    /// is better than the nothing it is earning now. Still three times longer than the market needs to show
    /// the thing around, so a stall is never whisked away before the population has had its look.
    /// </para>
    /// </summary>
    public static int IgnoredMs { get; set; } = 600000;

    // ---- Every gate, counted apart. This proposer had none at all. ------------------------------
    //
    // <b>The one road by which gold enters this population from outside, and it was completely silent.</b>
    // On the afternoon of 27.08.2026 the shops line read "473 things bought for 3172gp, 0 sold for 0gp"
    // while 1707gp came off corpses in the same ten minutes — a population losing money at twice the rate
    // it earns it, with a median purse of 340gp and every price on the shard being refused for want of
    // funds. Nothing said whether this proposer was being asked and refusing, or never being asked. It
    // turned out to be neither exactly: a stall must sit unsold for BotAuction.StaleMs — thirty minutes —
    // before it may be taken to a counter, and no session that week had lived that long.

    /// <summary>Bots looked at for something to peddle.</summary>
    public static long Asked { get; private set; }

    /// <summary>Bots holding no stall of their own at all.</summary>
    public static long Stallless { get; private set; }

    /// <summary>Stalls that have sold before, so somebody here does want them.</summary>
    public static long Wanted { get; private set; }

    /// <summary>Stalls not yet old enough to count as ignored. The thirty-minute gate.</summary>
    public static long Fresh { get; private set; }

    /// <summary>Ignored stalls with no shopkeeper in reach who buys the thing.</summary>
    public static long NoBuyer { get; private set; }

    /// <summary>Trips to a counter actually offered.</summary>
    public static long Offered { get; private set; }

    public static string Describe() =>
        Asked == 0
            ? "nobody has been offered a trip to a counter with goods"
            : $"{Asked} looks for something to peddle: {Offered} trips to a counter offered, {Stallless} had nothing of their own on the market, "
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

            // Never sold one, and it has been on offer for a full stale period. Both halves matter: a stall
            // that has sold before has a buyer somewhere worth waiting for, and a stall that went up a minute
            // ago has not been in front of anybody long enough to know.
            //
            // Measured from when the stall opened rather than from the last thing that happened to it. A price
            // cut counts as something happening, so the other clock is never more than one beat old and this
            // test would never fire — and it would fire even less for goods opened at a gold apiece, whose
            // price has no room to fall at all.
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

        // Once, by name. Goods that no bot wants and no shopkeeper buys are goods that will sit on a stall for
        // the life of the shard, and a population slowly filling the market with them looks exactly like a
        // population that is trading.
        logger.Error(
            "No shopkeeper within reach of the bots on {Map} buys {Item}, and no bot wants it either; it will sit on the market",
            map,
            label
        );
    }

    /// <summary>Lets the complaint be made again after a world reload.</summary>
    public static void Forget()
    {
        _saidNoBuyer = false;
        Asked = 0;
        Stallless = 0;
        Wanted = 0;
        Fresh = 0;
        NoBuyer = 0;
        Offered = 0;
    }
}
