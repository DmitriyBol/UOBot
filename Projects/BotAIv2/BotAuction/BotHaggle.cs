using System;
using System.Collections.Generic;
using Server.Logging;

namespace Server.BotAI.V2;

/// <summary>
/// A seller looking at what buyers are offering for what it has out, and moving its price towards them.
///
/// <para>
/// <b>Both sides of this market were present, funded, and looking past each other.</b> The board carries an
/// offer and an escrow for every want, so what a buyer will pay is a fact the shard has always known; the
/// stall carried a price nobody consulted it about. The only thing that ever moved a price was the market's
/// own clock — <c>BotAuction.BeatStalls</c>, a tenth off every <c>StaleMs</c> whatever the reason — and the
/// summary said what that costs: 1416 wants finding the thing on a stall dearer than they would pay, against
/// 200 that crossed, at 08:13 on 05.09.2026.
/// </para>
///
/// <para>
/// <b>On the bot's own beat and not on the market's, which is the whole of what "in real time" means here.</b>
/// Patrick's order of 05.09.2026. The blind clock still runs — it is what walks a price down when nobody is
/// asking at all — and this sits beside it for the case where somebody is. It is a condition rather than an
/// errand, like banking and dressing: minding your own shop is not a journey and costs the bot no time it
/// could have spent working. See <c>BotMobile</c>, where the rest of that family is called.
/// </para>
///
/// <para>
/// <b>It reads the board and never the buyer.</b> A want is a public number with money behind it; who raised
/// it is not this file's business, and a seller that could see whose purse was thin would be a seller that
/// could rob it.
/// </para>
/// </summary>
public static class BotHaggle
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotHaggle));

    /// <summary>
    /// How often one bot looks over its own stalls. Five seconds.
    ///
    /// Fast enough to be worth calling real time — a buyer that raises its offer sees a reply within a beat
    /// or two — and slow enough that fifty bots minding shops cost one scan of a short list each per five
    /// seconds. The board is walked once per stall, and a bot with no stalls pays a dictionary lookup.
    /// </summary>
    public static int EveryMs { get; set; } = 5000;

    /// <summary>
    /// How far towards the offer one look moves the price. A quarter of the gap.
    ///
    /// <para>
    /// A share of the distance rather than a share of the price, because the question here is not "is this
    /// dear" but "how far apart are we": two sellers a hundred gold and two gold above the bid should both
    /// take about the same number of looks to arrive, and a fixed tenth of the price would give the cheap one
    /// a lifetime. Four looks is twenty seconds, which is a haggle rather than a capitulation.
    /// </para>
    /// </summary>
    public static double Step { get; set; } = 0.25;

    /// <summary>Prices moved down towards a bid. For the summary.</summary>
    public static long Cut { get; private set; }

    /// <summary>Prices moved up towards a bid that was over the ask. For the summary.</summary>
    public static long Raised { get; private set; }

    /// <summary>Stalls looked at that had no bid on the board at all, and so were left to the blind clock.</summary>
    public static long Unbid { get; private set; }

    private static readonly Dictionary<Serial, long> _looked = new();

    /// <summary>
    /// Looks over this bot's stalls, once per <see cref="EveryMs"/>, and moves each price towards the best
    /// open bid for that kind.
    /// </summary>
    public static void Keep(IBotWilful bot)
    {
        var body = bot?.Self;

        if (body is not { Deleted: false, Alive: true })
        {
            return;
        }

        var now = Core.TickCount;

        // Seeded from a real tick and compared by subtraction, never against a nought default.
        if (_looked.TryGetValue(body.Serial, out var last) && now - last < EveryMs)
        {
            return;
        }

        _looked[body.Serial] = now;

        var stalls = BotAuction.Listings;

        for (var i = 0; i < stalls.Count; i++)
        {
            var stall = stalls[i];

            if (!ReferenceEquals(stall.Seller, bot) || stall.IsEmpty)
            {
                continue;
            }

            // The best open want this bot is actually allowed to fill, which already excludes its own wants
            // and anything it is itself queueing for. One question, asked where it is already answered.
            var want = BotAuction.Demand(bot, stall.Kind);

            if (want == null)
            {
                Unbid++;

                continue;
            }

            var was = stall.Price;

            if (!stall.Meet(want.Offer, Step, BotAuction.LeastMultiple, BotAuction.MostMultiple))
            {
                continue;
            }

            if (stall.Price > was)
            {
                Raised++;
            }
            else
            {
                Cut++;
            }

            // Said at the level the rest of the market says its price moves at, and only when the two sides
            // have actually met — a line per step of every haggle on the shard would be the log this project
            // has twice had to stop writing.
            if (stall.Price == want.Offer)
            {
                logger.Information(
                    "{Name} met {Buyer}'s {Offer}gp for {Item} and is asking that, down from {Was}",
                    body.Name,
                    want.Buyer?.Self?.Name,
                    want.Offer,
                    stall.Label,
                    was
                );
            }
        }
    }

    public static string Describe() =>
        Cut + Raised + Unbid == 0
            ? "nobody has looked at their own prices yet"
            : $"{Cut} prices moved down towards a bid and {Raised} up, {Unbid} stalls had no bid to move towards";

    /// <summary>Forgotten with the world, like every store in this assembly that is keyed by serial.</summary>
    public static void Forget()
    {
        _looked.Clear();
        Cut = 0;
        Raised = 0;
        Unbid = 0;
    }
}
