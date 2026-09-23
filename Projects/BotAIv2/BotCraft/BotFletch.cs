using System;
using System.Collections.Generic;
using Server.Items;
using Server.Logging;
using Server.Mobiles;

namespace Server.BotAI.V2;

/// <summary>
/// Making arrows: buy the wood if it is short of it, cut the shafts, feather them, and hand them to whoever
/// put the money down.
///
/// <para>
/// <b>Three legs and no station.</b> The smith walks to a forge and the tailor to a counter; a fletcher works
/// wherever it is standing, so the only walking here is to a carpenter for logs and to a counter to sell.
/// See <see cref="BotFletching"/> for why this trade exists and why the feather is the half that matters.
/// </para>
///
/// <para>
/// <b>Feathers are never bought from a shopkeeper, because no shopkeeper on this shard has one.</b> They
/// come out of the pack — a hunter that went through a bird's corpse is carrying them — or off the
/// population's own market, where that hunter listed them. That is the whole loop Patrick asked for, and it
/// is the same shape as the leather one: something is killed, the market moves the parts, a crafter turns
/// them into a thing the killer needs.
/// </para>
/// </summary>
public sealed class BotFletch : BotDeed
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotFletch));

    public const string Trade = "fletch";

    public static double Prior { get; set; } = 90.0;

    public static double WorkMinutes { get; set; } = 1.5;

    public static int SwingMs { get; set; } = 3000;

    public static int StallMs { get; set; } = SwingMs * 8;

    private enum Leg
    {
        Wood,
        Work,
        Sell
    }

    private readonly Map _map;

    private readonly Point3D _where;

    private BaseVendor _shop;

    private int _repicks;

    private readonly int _price;

    private readonly int _take;

    private readonly BotWant _order;

    private Leg _leg;

    private int _arrows;

    private int _swings;

    private int _made;

    private int _had;

    private bool _counting;

    private bool _swung;

    private long _swungTick;

    private int _lastLeft = -1;

    private long _stirTick;

    public BotFletch(Map map, Point3D where, BaseVendor shop, int price, int take, BotWant order = null)
    {
        _map = map;
        _where = where;
        _shop = shop;
        _price = price;
        _take = take;
        _order = order;
        _leg = take > 0 ? Leg.Wood : Leg.Work;
    }

    public override string Kind => Trade;

    public override Map Map => _map;

    public override Point3D Where => _where;

    public override double Expects => _order == null ? Prior : Prior * 1.6;

    public override double Minutes => WorkMinutes;

    public override SkillName? Trains => SkillName.Fletching;

    public override int Outlay => _take * _price;

    public override bool AtCounter => _shop != null;

    public override double Coin => 0.0;

    public override int Made => _made;

    public override string Stage =>
        _leg switch
        {
            Leg.Wood => $"after {_take} Log for arrows",
            Leg.Work => $"fletching ({_arrows} arrows in {_swings} attempts)",
            _ => $"putting {_arrows} arrows out"
        };

    public override bool Bend(IBotWilful bot)
    {
        if (_shop is { Deleted: false })
        {
            bot?.Resolve?.Ledger?.Beware(BotGround.CounterKind, _map, _shop.Location);
        }

        return false;
    }

    public override BotDoing Advance(IBotWilful bot)
    {
        var body = bot?.Self;

        if (body == null)
        {
            return BotDoing.Failed("no body");
        }

        for (var guard = 0; guard < 6; guard++)
        {
            var doing = _leg switch
            {
                Leg.Wood => Wood(bot, body),
                Leg.Work => Fletching(body),
                _ => Selling(bot, body)
            };

            if (doing.Kind != BotDoingKind.None)
            {
                return doing;
            }
        }

        return BotDoing.Failed("could not settle on a next step");
    }

    private BotDoing Wood(IBotWilful bot, Mobile body)
    {
        if (BotFletching.Possible(body) >= BotFletching.LeastArrows)
        {
            _leg = Leg.Work;

            return default;
        }

        if (BotAuction.Reclaim(bot, typeof(Log)) > 0 && BotFletching.Possible(body) >= BotFletching.LeastArrows)
        {
            _leg = Leg.Work;

            return default;
        }

        var lot = BotAuction.Cheapest(typeof(Log), bot);

        if (lot is { IsEmpty: false } && BotAuction.Buy(body, lot, Math.Min(_take, lot.Amount)) > 0)
        {
            _leg = Leg.Work;

            return default;
        }

        if (_shop == null || _shop.Deleted || _shop.Map == null || _shop.Map == Map.Internal)
        {
            return BotDoing.Failed("no wood on any stall and no merchant selling it");
        }

        if (!body.InRange(_shop.Location, BotShops.CounterReach))
        {
            return BotDoing.Walk(_shop.Map, _shop, BotArrival.Within(BotShops.CounterReach), $"to {_shop.Name} for wood");
        }

        if (BotShops.Buy(bot, _shop, typeof(Log), _take, out var refused) <= 0)
        {
            var next = BotShops.Next(bot, _shop, typeof(Log), ref _repicks);

            if (next != null)
            {
                _shop = next;

                return BotDoing.Walk(next.Map, next, BotArrival.Within(BotShops.CounterReach), $"on to {next.Name} for wood");
            }

            return BotDoing.Failed(refused ?? "no wood to be had");
        }

        _leg = Leg.Work;

        return default;
    }

    private BotDoing Fletching(Mobile body)
    {
        var tool = BotFletching.Kit(body);

        if (tool == null)
        {
            return BotDoing.Failed("nothing to fletch with");
        }

        if (!_counting)
        {
            _counting = true;
            _had = BotFletching.Made(body, typeof(Arrow));
        }

        var have = BotFletching.Made(body, typeof(Arrow));

        if (have > _had)
        {
            _arrows += have - _had;
            _had = have;
            _made = _arrows * BotFletching.Worth;
        }

        var feathers = BotFletching.Feathers(body);

        if (feathers <= 0)
        {
            return Finish("no feathers left to fletch with");
        }

        var left = feathers + BotFletching.Shafts(body) + BotFletching.Logs(body);

        if (_lastLeft != left)
        {
            _lastLeft = left;
            _stirTick = Core.TickCount;
        }

        if (Core.TickCount - _stirTick >= StallMs)
        {
            logger.Information(
                "{Name}'s fletching stopped: {Swings} attempts, {Arrows} made, {Left} of feather and wood left untouched for {Stall}s",
                body.Name,
                _swings,
                _arrows,
                left,
                StallMs / 1000
            );

            return Finish($"the tool has not moved in {StallMs / 1000}s, with {left} of feather and wood in the pack");
        }

        if (_swung && Core.TickCount - _swungTick < SwingMs)
        {
            return BotDoing.Work($"fletching, {_arrows} arrows so far");
        }

        var cutting = BotFletching.Shafts(body) <= 0;

        if (cutting && BotFletching.Logs(body) <= 0)
        {
            return Finish("no wood left to cut");
        }

        var material = cutting ? typeof(Log) : typeof(Shaft);

        var recipe = cutting
            ? BotFletching.Recipe(body, typeof(Log), typeof(Shaft))
            : BotFletching.Feathering(body);

        if (recipe == null)
        {
            return BotDoing.Failed(cutting ? "it does not know how to cut a shaft" : "it does not know how to feather a shaft");
        }

        _swings++;
        _swung = true;
        _swungTick = Core.TickCount;

        BotFletching.Swing(body, recipe, material, tool);

        return BotDoing.Work(cutting ? "cutting shafts" : $"fletching, {_arrows} arrows so far");
    }

    private BotDoing Finish(string why)
    {
        if (_arrows <= 0)
        {
            return BotDoing.Failed(why);
        }

        _leg = Leg.Sell;

        return default;
    }

    private BotDoing Selling(IBotWilful bot, Mobile body)
    {
        var ordered = 0;
        var listed = 0;

        List<Item> made = BotThread.Gather(bot?.Self, typeof(Arrow));

        for (var i = 0; i < made.Count; i++)
        {
            var stack = made[i];
            var held = Math.Max(1, stack.Amount);
            var want = _order ?? BotAuction.Demand(bot, typeof(Arrow));
            var sold = want == null ? 0 : BotAuction.Fill(bot, want, stack);

            if (sold > 0)
            {
                ordered += sold;
                _made -= sold * BotFletching.Worth;

                if (sold >= held)
                {
                    continue;
                }
            }

            if (BotAuction.List(bot, stack, BotAuction.Worth(typeof(Arrow), BotFletching.Worth)) != null)
            {
                listed++;
            }
        }

        if (ordered > 0)
        {
            logger.Information(
                "{Name} fletched {Arrows} arrows and {Ordered} of them went straight to an order",
                body.Name,
                _arrows,
                ordered
            );
        }

        return BotDoing.Done($"{_arrows} arrows in {_swings} attempts, {ordered} to order and {listed} put out to sell");
    }
}
