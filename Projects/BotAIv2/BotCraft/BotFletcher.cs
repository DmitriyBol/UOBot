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

    /// <summary>Every gate apart, with the denominator. There is no bucket called "other".</summary>
    public static long Asked { get; private set; }

    public static long NoKit { get; private set; }

    public static long NoFeathers { get; private set; }

    public static long NoWood { get; private set; }

    public static long ToOrder { get; private set; }

    public static long OnSpec { get; private set; }

    /// <summary>Arrows asked for on the board that nobody could fill for want of feathers. The trade's whole story.</summary>
    /// <summary>
    /// Times a fletcher short of feathers found an arrow order it could not fill.
    ///
    /// <para>
    /// <b>A count of looks and not of orders, and it used to be printed as orders.</b> It is
    /// incremented once per pass through the no-feathers gate, so a single standing order looked at
    /// by twenty fletchers on every beat reads in the hundreds: "419 arrow orders stood on the board"
    /// at 23:42 on 04.09.2026, against no arrow order raised at all in the preceding half hour. A
    /// counter whose sentence promises a different denominator than it counts is worse than no
    /// counter — it is the second lying instrument found in one night.
    /// </para>
    /// </summary>
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

        // Its own feathers back off the market first, exactly as the tailor reclaims its own leather: a bot
        // that skinned a bird has already listed what it took, and it cannot buy from its own stall.
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

        // Enough in the pack already — feathers and shafts or logs to match them. No shopping leg at all.
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

        // Short of wood, which is the half that can simply be bought.
        BotShops.Survey(map, body.Location);

        var shop = BotShops.Nearest(bot, typeof(Log));
        var price = shop == null ? 0 : BotShops.Price(shop, typeof(Log));

        // The population's own wood counts, and it is usually the only wood there is. A stall needs no walk
        // and no shopkeeper; the errand is offered on either, and refused only when there is neither.
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

        // What the wood will actually cost, from whichever source the work will use. Outlay is reckoned from
        // this, so a stall purchase priced at the shopkeeper'''s nought would tell the decision layer the wood
        // was free — and a trade that looks free is a trade the ledger cannot judge.
        if (lotted && (price <= 0 || lot.Price < price))
        {
            price = lot.Price;
        }

        // Bought to match the feathers and never by the armful: the feather is the binding half, and wood
        // beyond it is money spent on arrows that cannot be made.
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

    /// <summary>The most valuable standing order for arrows, or null. Worth rather than nearness, as the smith does.</summary>
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

    /// <summary>Said once, because the first arrow this shard ever made is worth a line and the thousandth is not.</summary>
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
            : $"{Asked} asked to fletch: {ToOrder} took an order off the board, {OnSpec} made some on spec, {NoFeathers} had no feathers and nobody sells one, {NoWood} could not find wood; {Unfilled} times a fletcher with no feathers looked at an arrow order it could not fill";

    public static void Forget()
    {
        Asked = 0;
        NoKit = 0;
        NoFeathers = 0;
        NoWood = 0;
        ToOrder = 0;
        OnSpec = 0;
        Unfilled = 0;
    }
}
