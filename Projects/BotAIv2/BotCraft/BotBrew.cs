using System;
using System.Collections.Generic;
using Server.Items;
using Server.Logging;
using Server.Mobiles;

namespace Server.BotAI.V2;

/// <summary>
/// Brewing: buy the glass if it is short of it, work the mortar, and hand the bottles to whoever put the
/// money down.
///
/// <para>
/// <b>Two legs and no station.</b> A mortar and pestle works wherever the bot is standing, so the only
/// walking here is to a counter for empty bottles. See <see cref="BotFlask"/> for why this trade exists.
/// </para>
///
/// <para>
/// <b>The reagent is never bought here, and that is the point of the chain.</b> A caster short of herbs has
/// a shopping errand of its own, a gatherer walks into a wood for them and a hunter turns them up on the
/// ground. What this leg buys is glass, which is five gold a hundred and never the reason a potion does not
/// get made.
/// </para>
/// </summary>
public sealed class BotBrew : BotDeed
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotBrew));

    public const string Trade = "brew";

    public static double Prior { get; set; } = 90.0;

    public static double WorkMinutes { get; set; } = 1.5;

    public static int SwingMs { get; set; } = 3000;

    public static int StallMs { get; set; } = SwingMs * 8;

    private enum Leg
    {
        Glass,
        Work,
        Sell
    }

    private readonly Map _map;

    private readonly Point3D _where;

    private readonly BaseVendor _shop;

    private readonly int _price;

    private readonly int _take;

    private readonly BotWant _order;

    private readonly Type _potion;

    private Leg _leg;

    private int _bottled;

    private int _swings;

    private int _made;

    private int _had;

    private bool _counting;

    private bool _swung;

    private long _swungTick;

    private int _lastLeft = -1;

    private long _stirTick;

    public BotBrew(Map map, Point3D where, Type potion, BaseVendor shop, int price, int take, BotWant order = null)
    {
        _map = map;
        _where = where;
        _potion = potion;
        _shop = shop;
        _price = price;
        _take = take;
        _order = order;

        _leg = take > 0 ? Leg.Glass : Leg.Work;
    }

    public override string Kind => Trade;

    public override Map Map => _map;

    public override Point3D Where => _where;

    public override double Expects => _order == null ? Prior : Prior * 1.6;

    public override double Minutes => WorkMinutes;

    public override SkillName? Trains => SkillName.Alchemy;

    public override int Outlay => _take * _price;

    public override bool AtCounter => _shop != null;

    public override double Coin => 0.0;

    public override int Made => _made;

    public override string Stage =>
        _leg switch
        {
            Leg.Glass => $"after {_take} Bottle to brew with",
            Leg.Work  => $"brewing ({_swings} attempts, {_bottled} made)",
            _         => $"putting {_bottled} bottles out"
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
                Leg.Glass => Glass(bot, body),
                Leg.Work  => Brewing(body),
                _         => Selling(bot, body)
            };

            if (doing.Kind != BotDoingKind.None)
            {
                return doing;
            }
        }

        return BotDoing.Failed("could not settle on a next step");
    }

    private BotDoing Glass(IBotWilful bot, Mobile body)
    {
        if (BotFlask.Bottles(body) >= BotFlask.LeastBottles)
        {
            _leg = Leg.Work;

            return default;
        }

        if (BotAuction.Reclaim(bot, typeof(Bottle)) > 0 && BotFlask.Bottles(body) >= BotFlask.LeastBottles)
        {
            _leg = Leg.Work;

            return default;
        }

        var lot = BotAuction.Cheapest(typeof(Bottle), bot);

        if (lot is { IsEmpty: false } && BotAuction.Buy(body, lot, Math.Min(_take, lot.Amount)) > 0)
        {
            _leg = Leg.Work;

            return default;
        }

        if (_shop == null || _shop.Deleted || _shop.Map == null || _shop.Map == Map.Internal)
        {
            return BotDoing.Failed("no glass on any stall and no merchant selling it");
        }

        if (!body.InRange(_shop.Location, BotShops.CounterReach))
        {
            return BotDoing.Walk(_shop.Map, _shop, BotArrival.Within(BotShops.CounterReach), $"to {_shop.Name} for glass");
        }

        if (BotShops.Buy(bot, _shop, typeof(Bottle), _take, out var refused) <= 0)
        {
            return BotDoing.Failed(refused ?? "no glass to be had");
        }

        _leg = Leg.Work;

        return default;
    }

    private BotDoing Brewing(Mobile body)
    {
        var tool = BotFlask.Kit(body);

        if (tool == null)
        {
            return BotDoing.Failed("nothing to brew with");
        }

        if (!_counting)
        {
            _counting = true;
            _had = BotFlask.Made(body, _potion);
        }

        var have = BotFlask.Made(body, _potion);

        if (have > _had)
        {
            _bottled += have - _had;
            _had = have;
            _made = _bottled * BotFlask.Worth;
        }

        var recipe = BotFlask.Recipe(body, _potion);

        if (recipe == null)
        {
            return Finish(BotFlask.Bottles(body) <= 0 ? "no glass left to brew into" : "no herbs left to brew with");
        }

        var (reagent, _, _) = BotFlask.Costs(recipe);

        var left = BotFlask.Amount(body, reagent) + BotFlask.Bottles(body);

        if (_lastLeft != left)
        {
            _lastLeft = left;
            _stirTick = Core.TickCount;
        }

        if (Core.TickCount - _stirTick >= StallMs)
        {
            logger.Information(
                "{Name}'s mortar stopped: {Swings} attempts, {Bottles} made, {Left} of herb and glass left untouched for {Stall}s",
                body.Name,
                _swings,
                _bottled,
                left,
                StallMs / 1000
            );

            return Finish($"the mortar has not moved in {StallMs / 1000}s, with {left} of herb and glass in the pack");
        }

        if (_swung && Core.TickCount - _swungTick < SwingMs)
        {
            return BotDoing.Work($"brewing, {_bottled} bottles so far");
        }

        _swings++;
        _swung = true;
        _swungTick = Core.TickCount;

        BotFlask.Swing(body, recipe, reagent, tool);

        return BotDoing.Work($"brewing, {_bottled} bottles so far");
    }

    private BotDoing Finish(string why)
    {
        if (_bottled <= 0)
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

        var keep = Keeps(bot);

        List<Item> made = BotCraftwork.Gather(body, _potion);

        for (var i = 0; i < made.Count; i++)
        {
            var stack = made[i];
            var held = Math.Max(1, stack.Amount);

            if (keep > 0)
            {
                if (held <= keep)
                {
                    keep -= held;

                    continue;
                }

                if (Mobile.LiftItemDupe(stack, held - keep) == null)
                {
                    continue;
                }

                keep = 0;
                held = Math.Max(1, stack.Amount);
            }

            var want = _order ?? BotAuction.Demand(bot, _potion);
            var sold = want == null ? 0 : BotAuction.Fill(bot, want, stack);

            if (sold > 0)
            {
                ordered += sold;
                _made -= sold * BotFlask.Worth;

                if (sold >= held)
                {
                    continue;
                }
            }

            if (BotAuction.List(bot, stack, BotAuction.Worth(_potion, BotFlask.Worth)) != null)
            {
                listed++;
            }
        }

        if (ordered > 0)
        {
            logger.Information(
                "{Name} brewed {Bottles} bottles of {Potion} and {Ordered} of them went straight to an order",
                body.Name,
                _bottled,
                _potion?.Name,
                ordered
            );
        }

        return BotDoing.Done($"{_bottled} bottles in {_swings} attempts, {ordered} to order and {listed} put out to sell");
    }

    private int Keeps(IBotWilful bot)
    {
        var bottles = BotOutfit.PotionsFor(bot?.Class);

        for (var i = 0; i < bottles.Count; i++)
        {
            if (bottles[i].Kind == _potion)
            {
                return bottles[i].Count;
            }
        }

        return 0;
    }
}
