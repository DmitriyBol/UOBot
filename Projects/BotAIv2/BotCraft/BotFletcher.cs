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

        if (BotFletching.Feathers(body) <= 0)
        {
            BotAuction.Reclaim(bot, typeof(Feather));
        }

        var feathers = BotFletching.Feathers(body);

        if (feathers <= 0)
        {
            NoFeathers++;

            if (Order() != null)
            {
                Unfilled++;
            }

            return null;
        }

        var order = Order();

        if (BotFletching.Possible(body) >= BotFletching.LeastArrows)
        {
            Once(body, feathers);

            if (order != null)
            {
                ToOrder++;

                return new BotFletch(map, body.Location, null, 0, 0, order);
            }

            OnSpec++;

            return new BotFletch(map, body.Location, null, 0, 0);
        }

        BotShops.Survey(map, body.Location);

        var shop = BotShops.Nearest(bot, typeof(Log));
        var price = shop == null ? 0 : BotShops.Price(shop, typeof(Log));

        var lot = BotAuction.Cheapest(typeof(Log), bot);
        var lotted = lot is { IsEmpty: false };

        if (!lotted && price <= 0)
        {
            NoWood++;

            if (!_saidNoWood)
            {
                _saidNoWood = true;

                logger.Error(
                    "No shopkeeper within reach of the bots on {Map} sells wood and no bot has any on a stall, so no arrows can be made",
                    map
                );
            }

            return null;
        }

        if (lotted && (price <= 0 || lot.Price < price))
        {
            price = lot.Price;
        }

        var take = Math.Max(BotFletching.LeastArrows, feathers) - BotFletching.Shafts(body) - BotFletching.Logs(body);

        if (take <= 0)
        {
            take = BotFletching.LeastArrows;
        }

        Once(body, feathers);

        if (order != null)
        {
            ToOrder++;

            return new BotFletch(map, body.Location, shop, price, take, order);
        }

        OnSpec++;

        return new BotFletch(map, body.Location, shop, price, take);
    }

    private static BotWant Order()
    {
        var wants = BotAuction.Wants;

        BotWant best = null;
        var bestWorth = 0;

        for (var i = 0; i < wants.Count; i++)
        {
            var want = wants[i];

            if (!want.IsOpen || want.Kind != typeof(Arrow) || want.Worth <= bestWorth)
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
            : $"{Asked} asked to fletch: {ToOrder} took an order off the board, {OnSpec} made some on spec, {NoFeathers} had no feathers and nobody sells one, {NoWood} could not find wood; {Unfilled} times a fletcher with no feathers looked at an arrow order it could not fill; "
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
    }
}
