using System;
using Server.Items;
using Server.Logging;

namespace Server.BotAI.V2;

/// <summary>
/// Offers a bot with a fletching tool and a handful of feathers a turn at making arrows, and offers it the
/// board's orders first.
///
/// <para>
/// <b>Every gate here is a skipped candidate and never a failed errand.</b> That distinction has cost this
/// shard five separate loops, most recently a want board that filled up and produced a hundred and
/// seventy-six errands that died on their first beat. A fletcher with no feathers is passed over; it is not
/// sent out to discover that at the bench.
/// </para>
///
/// <para>
/// <b>Feathers are checked before wood, because they are the half nobody sells.</b> Logs are money — the
/// carpenter has them — so a fletcher short of wood is a fletcher with an errand. A fletcher short of
/// feathers has nothing to do until a hunter kills a bird and lists what it took, and telling those two
/// apart is what makes the counters below worth reading.
/// </para>
///
/// <para>
/// <b>And when a hunter has listed them, they are bought (30.09.2026).</b> "No feathers and nobody sells one" was never asked of
/// the stalls: the fletcher looked in its own pack and at its own stall, and put an order on the board only when its purse
/// carried five feathers over the hundred gold it keeps back — which a crafter's seldom does ("1259 cannot afford one, the
/// fattest purse among them held 116gp"). So at 00:18 on build 336 the board read "most stocked: … Feather 257" while 597 of
/// 740 asks by 01:13 were fletchers with no feathers. The feathers are now the first leg of the round, bought off the
/// cheapest stall like the wood (<see cref="BotFletch"/>), placeless and priced as the round's outlay; and a fletcher that
/// finds neither feathers nor any on a stall marks the want for the hunters (<see cref="Starved"/>).
/// </para>
/// </summary>
public sealed class BotFletcher : IBotProposer
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotFletcher));

    private static bool _saidNoSystem;

    private static bool _saidNoWood;

    private static bool _said;

    public static long Asked { get; private set; }

    public static long NoKit { get; private set; }

    public static long NoFeathers { get; private set; }

    public static long NoWood { get; private set; }

    public static long ToOrder { get; private set; }

    public static long OnSpec { get; private set; }

    public static long Unfilled { get; private set; }

    public static int LeastFeathers { get; set; } = 5;

    public static long Poor { get; private set; }

    public static int CallMs { get; set; } = 600000;

    private static long _starvedAt;

    private static bool _starved;

    private static long _woodlessAt;

    private static bool _woodless;

    public static bool Starved => _starved && Core.TickCount - _starvedAt < CallMs;

    public static bool Woodless => _woodless && Core.TickCount - _woodlessAt < CallMs;

    public string Name => "Fletcher";

    public BotStanding Rung => BotStanding.Free;

    public BotDeed Propose(IBotWilful bot)
    {
        var body = bot?.Self;
        var map = body?.Map;

        if (map == null || map == Map.Internal || !body.Alive)
        {
            return null;
        }

        if (BotFletching.Kit(body) == null)
        {
            NoKit++;

            return null;
        }

        Asked++;

        if (BotFletching.System == null)
        {
            if (!_saidNoSystem)
            {
                _saidNoSystem = true;

                logger.Error("The fletching system does not exist yet, so nobody can make arrows");
            }

            return null;
        }

        if (BotFletching.Feathers(body) < LeastFeathers)
        {
            BotMarket.FromBank(bot, typeof(Feather), BotFletching.LeastArrows);
        }

        var feathers = BotFletching.Feathers(body);

        if (feathers <= 0)
        {
            NoFeathers++;
            _starved = true;
            _starvedAt = Core.TickCount;

            if (Order(bot) != null)
            {
                Unfilled++;

                BotCraftFirst.Short(body, typeof(Feather), Math.Max(LeastFeathers, BotFletching.LeastArrows));
            }

            return null;
        }

        var order = Order(bot);

        if (BotFletching.Possible(body) >= BotFletching.LeastArrows)
        {
            Once(body, feathers);

            if (order != null)
            {
                ToOrder++;
                order.Offered();

                return new BotFletch(map, body.Location, null, 0, 0, order);
            }

            OnSpec++;
            BotGlut.Spec(typeof(Arrow));

            return new BotFletch(map, body.Location, null, 0, 0);
        }

        BotShops.Survey(map, body.Location);

        var held = feathers;

        var shop = BotShops.Nearest(bot, typeof(Log));
        var price = shop == null ? 0 : BotShops.Price(shop, typeof(Log));

        if (price <= 0)
        {
            NoWood++;
            _woodless = true;
            _woodlessAt = Core.TickCount;

            if (!_saidNoWood)
            {
                _saidNoWood = true;

                logger.Error(
                    "No shopkeeper within reach of the bots on {Map} sells wood, so arrows wait for a woodcutter's logs brought by hand",
                    map
                );
            }

            return null;
        }

        var take = Math.Max(BotFletching.LeastArrows, held) - BotFletching.Shafts(body) - BotFletching.Logs(body);

        if (take <= 0)
        {
            take = BotFletching.LeastArrows;
        }

        if (BotYield.Spendable(body) < take * price)
        {
            Poor++;

            return null;
        }

        Once(body, held);

        if (order != null)
        {
            ToOrder++;
            order.Offered();

            return new BotFletch(map, body.Location, shop, price, take, order);
        }

        OnSpec++;
        BotGlut.Spec(typeof(Arrow));

        return new BotFletch(map, body.Location, shop, price, take, null);
    }

    private static BotWant Order(IBotWilful bot)
    {
        var wants = BotMarket.Wants;

        BotWant best = null;
        var bestWorth = 0;

        for (var i = 0; i < wants.Count; i++)
        {
            var want = wants[i];

            if (!want.IsOpen || want.Kind != typeof(Arrow) || want.Worth <= bestWorth)
            {
                continue;
            }

            if (!BotCourier.Answerable(bot, want) || BotCourier.Holding(bot, want))
            {
                continue;
            }

            best = want;
            bestWorth = want.Worth;
        }

        return best;
    }

    private static void Once(Mobile body, int feathers)
    {
        if (_said)
        {
            return;
        }

        _said = true;

        logger.Information(
            "{Name} is the first bot on this shard ever to make arrows: it holds {Feathers} feathers, and until now the only arrows on the island were the ones everybody was born with",
            body.Name,
            feathers
        );
    }

    public static string Describe() =>
        Asked == 0
            ? $"nobody has been offered fletching ({NoKit} answers went to bots with no tool)"
            : $"{Asked} asked to fletch: {ToOrder} took an order off the board, {OnSpec} made some on spec, {NoFeathers} had no feathers and waited for a carrier to bring some, {Poor} could not pay for the wood, {NoWood} could not find wood; {Unfilled} times a fletcher with no feathers looked at an arrow order it could not fill; "
              + $"{BotFletching.Spared} stacks of feathers kept back off a corpse against {BotFletching.Sold} sold on past the cap of {BotFletching.Keeps}";

    public static void Forget()
    {
        BotFletching.ForgetTrade();
        Asked = 0;
        NoKit = 0;
        NoFeathers = 0;
        NoWood = 0;
        ToOrder = 0;
        OnSpec = 0;
        Unfilled = 0;
        Poor = 0;
        _starved = false;
        _woodless = false;
        BotFletch.Forget();
    }
}
