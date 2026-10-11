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
public sealed class BotBrew : BotDeed, IBotWantCarrier
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
        Fund,
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

    private readonly Type _potion;

    private readonly bool _funded;

    private readonly int _fundUnits;

    private readonly int _fundGlass;

    private readonly int _fundHerbs;

    private readonly Type _reagent;

    private readonly int _fundCost;

    private bool _released;

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

    public BotBrew(Map map, Point3D where, Type potion, BaseVendor shop, BotWant order, int units, int glass, int herbs, Type reagent, int cost)
    {
        _map = map;
        _where = where;
        _potion = potion;
        _shop = shop;
        _order = order;
        _funded = true;
        _fundUnits = Math.Max(1, units);
        _fundGlass = Math.Max(0, glass);
        _fundHerbs = Math.Max(0, herbs);
        _reagent = reagent;
        _fundCost = Math.Max(0, cost);
        _leg = Leg.Fund;
    }

    public BotWant Order => _order;

    public bool Carries(BotWant want) => _funded && !_released && ReferenceEquals(_order, want);

    public bool Stale => false;

    public Type Goods => _potion;

    public override void Taken(IBotWilful bot)
    {
        if (_funded && _order != null && BotMarket.Stands(_order))
        {
            _order.Reserve(bot, _fundUnits);
        }
    }

    public override string Kind => Trade;

    public override Map Map => _map;

    public override Point3D Where => _where;

    public override double Expects => _order != null ? Prior * 1.6 : BotGlut.Claim(_potion, Prior);

    public override double Minutes => WorkMinutes;

    public override SkillName? Trains => SkillName.Alchemy;

    public override int Outlay => _funded ? 0 : _take * _price;

    public override bool AtCounter => _shop != null;

    public override double Coin => 0.0;

    public override int Made => _made;

    public override string Stage =>
        _leg switch
        {
            Leg.Glass => $"after {_take} Bottle to brew with",
            Leg.Fund  => $"after {_fundGlass} Bottle and {_fundHerbs} {_reagent?.Name} for {_order?.Buyer?.Self?.Name ?? "a buyer"}'s {_fundUnits} {_potion?.Name}, on its money ({_fundCost}gp)",
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
                Leg.Fund  => Fund(bot, body),
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

        if (BotMarket.FromBank(bot, typeof(Bottle), _take) > 0 && BotFlask.Bottles(body) >= BotFlask.LeastBottles)
        {
            _leg = Leg.Work;

            return default;
        }

        if (_shop == null || _shop.Deleted || _shop.Map == null || _shop.Map == Map.Internal)
        {
            return BotDoing.Failed("no merchant selling glass");
        }

        if (!body.InRange(_shop.Location, BotShops.CounterReach))
        {
            return BotDoing.Walk(_shop.Map, _shop, BotArrival.Within(BotShops.CounterReach), $"to {_shop.Name} for glass");
        }

        if (BotShops.Buy(bot, _shop, typeof(Bottle), _take, out var refused) <= 0)
        {
            var next = BotShops.Next(bot, _shop, typeof(Bottle), ref _repicks);

            if (next != null)
            {
                _shop = next;

                return BotDoing.Walk(next.Map, next, BotArrival.Within(BotShops.CounterReach), $"on to {next.Name} for glass");
            }

            return BotDoing.Failed(refused ?? "no glass to be had");
        }

        _leg = Leg.Work;

        return default;
    }

    private BotDoing Fund(IBotWilful bot, Mobile body)
    {
        var buyer = _order?.Buyer?.Self;

        if (!BotMarket.Stands(_order) || buyer is not { Deleted: false, Alive: true })
        {
            return BotDoing.Failed("the want it meant to brew for is gone");
        }

        if (_shop == null || _shop.Deleted || _shop.Map == null || _shop.Map == Map.Internal)
        {
            return BotDoing.Failed("no counter selling glass and herbs");
        }

        if (!body.InRange(_shop.Location, BotShops.CounterReach))
        {
            return BotDoing.Walk(_shop.Map, _shop, BotArrival.Within(BotShops.CounterReach), $"to {_shop.Name} for glass and herbs on {buyer.Name}'s money");
        }

        var recipe = BotAdvance.Able(body, _potion, out _);

        if (recipe == null)
        {
            return BotDoing.Failed("its Alchemy no longer carries the draught");
        }

        var (reagent, per, glass) = BotFlask.Costs(recipe);
        var lackGlass = Math.Max(0, _fundUnits * glass - BotFlask.Bottles(body));
        var lackHerbs = Math.Max(0, _fundUnits * per - BotFlask.Amount(body, reagent));

        if (lackGlass + lackHerbs <= 0)
        {
            _leg = Leg.Work;

            return default;
        }

        var factor = Math.Max(1.0, BotBurgh.Factor(body, _shop));
        var glassPrice = lackGlass > 0 ? (int)Math.Ceiling(BotShops.Price(_shop, typeof(Bottle)) * factor) : 0;
        var herbPrice = lackHerbs > 0 ? (int)Math.Ceiling(BotShops.Price(_shop, reagent) * factor) : 0;

        if (lackGlass > 0 && glassPrice <= 0 || lackHerbs > 0 && herbPrice <= 0)
        {
            return BotDoing.Failed($"{_shop.Name} sells no glass or no {reagent?.Name} now");
        }

        var cost = lackGlass * glassPrice + lackHerbs * herbPrice;

        if (cost > _fundUnits * _order.Offer)
        {
            return BotDoing.Failed($"{buyer.Name}'s {_order.Offer}gp a draught no longer pays for its glass and herbs at {cost}gp");
        }

        if (!BotAdvance.Lend(bot, _order, _fundUnits, cost))
        {
            return BotDoing.Failed($"{buyer.Name}'s money would not put {cost}gp down for its glass and herbs");
        }

        var before = BotYield.Wealth(body);
        var bottles = lackGlass > 0 ? BotShops.Buy(bot, _shop, typeof(Bottle), lackGlass, out _) : 0;
        var herbs = lackHerbs > 0 ? BotShops.Buy(bot, _shop, reagent, lackHerbs, out _) : 0;

        BotAdvance.Spent(bot, Math.Max(0, before - BotYield.Wealth(body)), bottles, herbs);

        logger.Information(
            "{Name} bought {Bottles} bottles and {Herbs} {Reagent} on {Buyer}'s money for its want of {Units} {Potion} (want #{Id})",
            body.Name,
            bottles,
            herbs,
            reagent?.Name,
            buyer.Name,
            _fundUnits,
            _potion?.Name,
            _order.Id
        );

        _leg = Leg.Work;

        return default;
    }

    private BotDoing Brewing(Mobile body)
    {
        var tool = BotFlask.Kit(body);

        if (tool == null && _swings == 0)
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
            BotCraftwork.Produced(_potion, have - _had);
            _had = have;

            _made = BotGlut.Yield(_potion, _bottled, BotFlask.Worth);
        }

        if (tool == null)
        {
            if (_swung && Core.TickCount - _swungTick < SwingMs)
            {
                return BotDoing.Work($"brewing, {_bottled} bottles so far");
            }

            return Finish("the mortar wore through");
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
        if (_funded && !_released)
        {
            _released = true;
            _order?.Release(bot);
        }

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

            var want = _order ?? BotMarket.Demand(bot, _potion);
            var answer = want == null ? 0 : Math.Min(held, Math.Max(0, want.Payable - ordered));

            ordered += answer;
            listed += held - answer;
        }

        if (ordered > 0)
        {
            logger.Information(
                "{Name} brewed {Bottles} bottles of {Potion} and {Ordered} of them are for an order, to be carried to it",
                body.Name,
                _bottled,
                _potion?.Name,
                ordered
            );
        }

        return BotDoing.Done($"{_bottled} bottles in {_swings} attempts, {ordered} for an order and {listed} kept to sell");
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
