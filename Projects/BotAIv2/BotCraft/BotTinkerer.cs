using System;
using Server.Logging;

namespace Server.BotAI.V2;

/// <summary>
/// Offers a bot with tinker's tools and some iron a stint at making the tool the island is shortest of, and
/// offers it the board's orders first.
///
/// <para>
/// <b>Demand is read off the stalls, not guessed.</b> A tool is offered only while the market holds fewer than
/// <see cref="LeastOnMarket"/> of it, so the tinker stops when the stalls are stocked and starts again when
/// somebody buys — which is the glass rule (a material with no producer must not be ordered by the armful)
/// turned the other way round: a producer with no reader of demand must not make by the armful.
/// </para>
///
/// <para>
/// <b>The iron is read off the stalls too (30.09.2026).</b> This asked only its own pack, so a tinker was a tinker only on the
/// afternoon it had dug and smelted iron itself — and on that afternoon its forge outbid it for the same ingots. Build 336, its
/// first 105 minutes: 1331 asks, 1211 "had no iron", 112 offered for the stalls and none of them taken ("over tinker" 26
/// times, every time under the forge, at 123–561 a minute against the forge's 288–1370), no tool made — while the market held
/// 59 to 618 iron ingots, most of the time over two hundred, at six gold ("The board: most stocked: IronIngot"), and the population bought 125 scissors, 47 skinning knives, 44 pickaxes and 10
/// hatchets over counters for 3240gp. A tinker short of iron now buys a batch's worth off the cheapest stall as the stint's
/// first leg — placeless, so no walk — priced as the stint's outlay, and counted apart when it cannot pay for it.
/// </para>
/// </summary>
public sealed class BotTinkerer : IBotProposer
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotTinkerer));

    public static int LeastMetal { get; set; } = 8;

    public static int LeastOnMarket { get; set; } = 2;

    private static bool _saidNoSystem;

    private static bool _said;

    public static long Asked { get; private set; }

    public static long NoKit { get; private set; }

    public static long NoMetal { get; private set; }

    public static long ToOrder { get; private set; }

    public static long OnSpec { get; private set; }

    public static long Stocked { get; private set; }

    public static long Buying { get; private set; }

    public static long Poor { get; private set; }

    public static long Richest { get; private set; }

    public string Name => "Tinkerer";

    public BotStanding Rung => BotStanding.Free;

    public BotDeed Propose(IBotWilful bot)
    {
        var body = bot?.Self;
        var map = body?.Map;

        if (map == null || map == Map.Internal || !body.Alive)
        {
            return null;
        }

        if (BotTinkering.Kit(body) == null && BotTinkering.Seed(body))
        {
            logger.Information("{Name} was given its tinker's tools: no shop within its reach sells them, and the class is born with them now", body.Name);
        }

        if (BotTinkering.Kit(body) == null)
        {
            NoKit++;

            return null;
        }

        Asked++;

        if (BotTinkering.System == null)
        {
            if (!_saidNoSystem)
            {
                _saidNoSystem = true;

                logger.Error("The tinkering system does not exist yet, so nobody can make tools");
            }

            return null;
        }

        if (BotTinkering.Ingots(body) < LeastMetal)
        {
            BotAuction.Reclaim(bot, BotTinkering.Metal);
        }

        var ingots = BotTinkering.Ingots(body);
        BotListing lot = null;

        if (ingots < LeastMetal)
        {
            lot = BotAuction.Cheapest(BotTinkering.Metal, bot);

            if (lot is not { IsEmpty: false })
            {
                NoMetal++;

                return null;
            }
        }

        var order = Order(bot, body);
        var tool = order?.Kind ?? BotTinkering.Choose(body, LeastOnMarket, out _);

        if (tool == null)
        {
            Stocked++;

            return null;
        }

        var buy = 0;
        var price = 0;

        if (lot != null)
        {
            var cost = Math.Max(1, BotCraftwork.Cost(BotTinkering.Recipe(body, tool)));
            var need = Math.Max(LeastMetal, cost * (Math.Max(1, BotTinker.Batch) + 1));

            buy = Math.Min(need - ingots, lot.Amount);
            price = Math.Max(1, lot.Price);

            if (ingots + buy < cost * 2)
            {
                NoMetal++;

                return null;
            }

            var wealth = BotYield.Wealth(body);

            if (wealth < buy * price)
            {
                Poor++;

                if (wealth > Richest)
                {
                    Richest = wealth;
                }

                return null;
            }

            Buying++;
        }

        if (order != null)
        {
            ToOrder++;
            Once(body, order.Kind.Name, "to order");

            return new BotTinker(map, body.Location, order.Kind, order, buy, price);
        }

        OnSpec++;
        Once(body, tool.Name, "because the stalls are short of it");

        return new BotTinker(map, body.Location, tool, null, buy, price);
    }

    private static BotWant Order(IBotWilful bot, Mobile body)
    {
        var wants = BotAuction.Wants;
        BotWant best = null;
        var bestWorth = 0;

        for (var i = 0; i < wants.Count; i++)
        {
            var want = wants[i];

            if (!want.IsOpen || ReferenceEquals(want.Buyer, bot) || !BotTinkering.IsTool(want.Kind) || want.Worth <= bestWorth)
            {
                continue;
            }

            if (BotTinkering.Recipe(body, want.Kind) == null)
            {
                continue;
            }

            best = want;
            bestWorth = want.Worth;
        }

        return best;
    }

    private static void Once(Mobile body, string tool, string why)
    {
        if (_said)
        {
            return;
        }

        _said = true;

        logger.Information("{Name} is the first to tinker: {Tool}, {Why}", body.Name, tool, why);
    }

    public static string Describe() =>
        Asked == 0
            ? "nobody with tinker's tools has been asked"
            : $"{Asked} asked to tinker: {ToOrder} to order, {OnSpec} for the stalls ({Buying} of them to buy their iron off a stall first), {Stocked} found every tool stocked, {NoMetal} had no iron and found none on any stall, {Poor} could not pay for a batch's iron off a stall (the fattest purse among them held {Richest}gp); {NoKit} asked without tinker's tools; {BotTinker.Describe()}";

    public static void Forget()
    {
        Asked = 0;
        NoKit = 0;
        NoMetal = 0;
        ToOrder = 0;
        OnSpec = 0;
        Stocked = 0;
        Buying = 0;
        Poor = 0;
        Richest = 0;
        _said = false;
    }
}
