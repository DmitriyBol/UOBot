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

    public static int EveryMs { get; set; } = 5000;

    public static double Step { get; set; } = 0.25;

    public static long Cut { get; private set; }

    public static long Raised { get; private set; }

    public static long Unbid { get; private set; }

    private static readonly Dictionary<Serial, long> _looked = new();

    public static void Keep(IBotWilful bot)
    {
        var body = bot?.Self;

        if (body is not { Deleted: false, Alive: true })
        {
            return;
        }

        var now = Core.TickCount;

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

    public static void Forget()
    {
        _looked.Clear();
        Cut = 0;
        Raised = 0;
        Unbid = 0;
    }
}
