using System;
using Server.Items;
using Server.Logging;

namespace Server.BotAI.V2;

/// <summary>
/// A crafter with money buys its metal instead of going and digging it.
///
/// <para>
/// <b>Digging is the cheapest way to get metal and very nearly the worst.</b> A mining trip is eight minutes
/// of walking to a seam, swinging at it, carrying rock to a fire and the metal to a counter — and at the end
/// of it a crafter has twenty ingots and has spent the eight minutes not crafting. Its skill is in the forge,
/// not in the rock. So once there is coin in the purse the sensible thing is to let somebody whose trade
/// <em>is</em> the rock do the walking, and pay them for it.
/// </para>
///
/// <para>
/// <b>It buys by asking rather than by shopping, and that is the whole elegance of it.</b> An order on the
/// board is read by every miner on the shard before it banks a single ingot — <c>BotDig</c> checks
/// <see cref="BotAuction.Demand"/> ahead of the bank box, and has since it was written. So a crafter putting
/// up money for metal does not merely acquire metal: it redirects the population's miners onto the thing the
/// population is short of, without anybody being told anything. That is the same trick as everything else
/// here — a fact left where others will pass it, and arithmetic on the far side.
/// </para>
///
/// <para>
/// Ordered on 24.08.2026: <em>if a crafter has a decent amount of money, say more than eight hundred to a
/// thousand, let it simply buy the ore rather than walk after it — buying and smelting is faster, and for a
/// crafter that is exactly right.</em>
/// </para>
/// </summary>
public sealed class BotBullion : IBotProposer
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotBullion));

    public static int Reserve { get; set; } = 150;

    public static int Enough { get; set; } = 20;

    public static int Batch { get; set; } = 20;

    public static int Least { get; set; } = 6;

    public static int Guess { get; set; } = 8;

    public static double Prior { get; set; } = 20.0;

    private static bool _said;

    public static long Asked { get; private set; }

    public static long Poor { get; private set; }

    public static long Soon { get; private set; }

    public static long Stocked { get; private set; }

    public static long Standing { get; private set; }

    public static long Shelved { get; private set; }

    public static long Ordered { get; private set; }

    public string Name => "Bullion";

    public BotStanding Rung => BotStanding.Free;

    public BotDeed Propose(IBotWilful bot)
    {
        var body = bot?.Self;
        var map = body?.Map;

        if (map == null || map == Map.Internal || !body.Alive)
        {
            return null;
        }

        if (!Smiths(bot.Class))
        {
            return null;
        }

        Asked++;

        if (!BotNeeds.Due(body, "metal"))
        {
            Soon++;

            return null;
        }

        var kind = BotAnvil.Best(body, Batch);

        var carried = body.Backpack?.GetAmount(kind) ?? 0;

        if (carried >= Enough)
        {
            Stocked++;

            return null;
        }

        if (BotAuction.Selling(bot, kind))
        {
            Shelved++;

            return null;
        }

        if (BotAuction.Wanted(bot, kind) != null)
        {
            Standing++;

            return null;
        }

        var offer = BotAuction.Worth(kind, Guess);

        var wealth = BotYield.Wealth(body);
        var afford = offer <= 0 ? 0 : (wealth - Reserve) / offer;
        var units = Math.Min(Batch, afford);

        if (units < Least)
        {
            Poor++;

            return null;
        }

        Ordered++;
        Once(body);

        return BotOrder.For(map, body.Location, bot, kind, offer, units);
    }

    private static bool Smiths(BotClass klass)
    {
        var tools = klass?.Kit?.Tools;

        for (var i = 0; tools != null && i < tools.Count; i++)
        {
            if (tools[i] == typeof(SmithHammer))
            {
                return true;
            }
        }

        return false;
    }

    private static void Once(Mobile body)
    {
        if (_said)
        {
            return;
        }

        _said = true;

        logger.Information(
            "{Name} has money enough to buy its metal rather than dig it, and has put the order to the miners",
            body.Name
        );
    }

    public static string Describe() =>
        Asked == 0
            ? "no crafter has been asked about metal"
            : $"{Asked} asked: {Ordered} ordered metal, {Standing} already have an order out, {Stocked} have {Enough} ingots already, {Shelved} have their own out on a stall, {Poor} cannot afford to buy it, {Soon} asked again inside the minute";

    public static void Forget()
    {
        _said = false;
        Asked = 0;
        Poor = 0;
        Soon = 0;
        Stocked = 0;
        Shelved = 0;
        Standing = 0;
        Ordered = 0;
    }
}
