using System;
using Server.Engines.Craft;
using Server.Items;
using Server.Logging;

namespace Server.BotAI.V2;

/// <summary>
/// Beating iron into something, at a forge, and handing it to whoever asked for it.
///
/// <para>
/// <b>The trade that was missing, and its absence is why ore went nowhere.</b> Mining has worked for days:
/// dig, smelt, carry the metal to a counter. What happened to the metal after that was nothing at all —
/// ingots went into bank boxes and onto stalls, and no bot on the shard could turn one into a thing. The
/// crafter class has carried a smith's hammer since it was written, and the comment beside it says in as
/// many words what a smith without work becomes: <em>a bot with an opinion about metal</em>. This is the
/// work.
/// </para>
///
/// <para>
/// <b>An order off the board comes first, and that is the whole point of building it now.</b>
/// <see cref="BotUpkeep"/> puts a bot's worn-out sword on the board with the money already down, and until
/// this existed there was nobody who could fill it — the order would stand until it timed out and the coin
/// would come back. A smith that reads the board turns "somebody needs a blade" into a blade, which is the
/// one loop this economy has never closed.
/// </para>
///
/// <para>
/// Three stages, and the third is the one the first version always forgot: go to a forge, beat the metal,
/// <em>hand the thing over</em>. A hauberk in the maker's own backpack has helped nobody.
/// </para>
/// </summary>
public sealed class BotForge : BotDeed
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotForge));

    public const string Trade = "forge";

    public static double Prior { get; set; } = 120.0;

    public static double WorkMinutes { get; set; } = 6.0;

    public static int SwingMs { get; set; } = 1300;

    public static int MaxSwings { get; set; } = 24;

    public static int Guess { get; set; } = 45;

    public static long Stints { get; private set; }

    public static long Wrought { get; private set; }

    public static long Pieces { get; private set; }

    public static long RanOut { get; private set; }

    public static long Fruitless { get; private set; }

    public static long NoPlace { get; private set; }

    public static long Rounded { get; private set; }

    private enum Leg
    {
        Walk,
        Work,
        Hand
    }

    private readonly Map _map;

    private readonly Point3D _smithy;

    private readonly BotWant _order;

    private Leg _leg;

    private bool _rounded;

    private Point3D _stand;

    private CraftItem _recipe;

    private Type _kind;

    private int _swings;

    private Type _metal;

    private int _made;

    private int _had = -1;

    public static long Preowned { get; private set; }

    private int _handed;

    private bool _swung;

    private long _swungTick;

    public BotForge(Map map, Point3D smithy, BotWant order = null)
    {
        _map = map;
        _smithy = smithy;
        _order = order;
        _kind = order?.Kind;
    }

    public override string Kind => Trade;

    public override Map Map => _map;

    public override Point3D Where => _smithy;

    public override double Expects => _order == null ? Prior : Prior * 1.6;

    public override double Minutes => WorkMinutes;

    public override SkillName? Trains => BotAnvil.Skill;

    public override double Coin => _order == null ? 0.4 : 1.0;

    public override int Made => _made * BotAuction.Worth(_kind, Guess);

    public override string Stage =>
        _leg switch
        {
            Leg.Walk => _order == null ? "off to a forge" : $"off to a forge to make {_kind?.Name}",
            Leg.Work => $"beating out {_kind?.Name ?? "iron"} ({_swings} attempts, {_made} made)",
            _ => $"handing over {_kind?.Name}"
        };

    public override BotDoing Advance(IBotWilful bot)
    {
        var body = bot?.Self;

        if (body == null || _map == null || _map == Map.Internal)
        {
            return BotDoing.Failed("no body");
        }

        return _leg switch
        {
            Leg.Walk => Walking(bot, body),
            Leg.Work => Working(bot, body),
            _ => Handing(bot, body)
        };
    }

    private BotDoing Walking(IBotWilful bot, Mobile body)
    {
        if (BotAnvil.AtASmithy(body))
        {
            _leg = Leg.Work;

            return BotDoing.Work("at the anvil");
        }

        if (!_rounded && body.InRange(_smithy, 1))
        {
            _rounded = true;
            _stand = BotAnvil.Between(_map, _smithy, body.Location);

            if (_stand != Point3D.Zero && (_stand.X != body.X || _stand.Y != body.Y))
            {
                Rounded++;
            }
            else
            {
                _stand = Point3D.Zero;
            }
        }

        if (_stand != Point3D.Zero && (_stand.X != body.X || _stand.Y != body.Y))
        {
            return BotDoing.Walk(_map, _stand, BotArrival.Exactly, "round to the anvil side of the forge");
        }

        if (_stand != Point3D.Zero || body.InRange(_smithy, 1))
        {
            Refuse(bot);

            BotGround.Unfit(_smithy);

            Stints++;
            NoPlace++;

            return BotDoing.Failed(
                $"no anvil the engine will accept within {BotAnvil.Reach} of the forge at ({_smithy.X}, {_smithy.Y})"
            );
        }

        return BotDoing.Walk(_map, _smithy, BotArrival.Beside, "to a forge");
    }

    private void Refuse(IBotWilful bot) => bot?.Resolve?.Ledger?.Beware(BotGround.FireKind, _map, _smithy);

    public override bool Bend(IBotWilful bot)
    {
        Refuse(bot);

        return false;
    }

    private BotDoing Working(IBotWilful bot, Mobile body)
    {
        var tool = BotAnvil.Kit(body);

        if (tool == null)
        {
            return _made > 0
                ? BotDoing.Done($"the hammer wore out after {_made} made")
                : BotDoing.Failed("no hammer");
        }

        if (!BotAnvil.AtASmithy(body))
        {
            _leg = Leg.Walk;

            return Walking(bot, body);
        }

        _recipe ??= _order == null ? BotAnvil.Choose(body) : BotAnvil.Recipe(body, _order.Kind);

        if (_recipe == null)
        {
            return BotDoing.Failed(_order == null ? "nothing worth making" : $"cannot make {_kind?.Name}");
        }

        _kind ??= _recipe.ItemType;

        var had = _made;

        if (_had < 0)
        {
            _had = BotAnvil.Made(body, _kind);

            if (_had > 0)
            {
                Preowned++;
            }
        }

        _made = Math.Max(0, BotAnvil.Made(body, _kind) - _had);

        if (_made > had)
        {
            BotCraftwork.Produced(_kind, _made - had);
            _made += BotCraftwork.Bonus(body, _kind);
        }

        if (_made > 0 && (_order != null || _swings >= MaxSwings))
        {
            _leg = Leg.Hand;

            return Handing(bot, body);
        }

        var cost = BotCraftwork.Cost(_recipe);

        _metal = BotAnvil.Best(body, cost * BotAnvil.Tries);

        if (BotAnvil.Ingots(body, _metal) < cost * BotAnvil.Tries)
        {
            _metal = BotAnvil.Best(body, cost);
        }

        if (_swings < MaxSwings && BotAnvil.Ingots(body, _metal) < cost && BotAuction.Reclaim(bot, _metal) > 0)
        {
            logger.Information(
                "{Name} took its own {Metal} back off the market to make {Item}",
                body.Name,
                _metal.Name,
                _kind?.Name
            );
        }

        if (_swings >= MaxSwings || BotAnvil.Ingots(body, _metal) < cost)
        {
            if (_made > 0)
            {
                _leg = Leg.Hand;

                return Handing(bot, body);
            }

            Stints++;

            if (_swings >= MaxSwings)
            {
                Fruitless++;
            }
            else
            {
                RanOut++;
            }

            return BotDoing.Failed((_swings >= MaxSwings ? "nothing came of the iron" : "out of metal") + BotCraftEar.Why(body));
        }

        if (_swung && Core.TickCount - _swungTick < SwingMs)
        {
            return BotDoing.Work($"beating out {_kind?.Name}");
        }

        _swung = true;
        _swungTick = Core.TickCount;
        _swings++;

        BotAnvil.Swing(body, _recipe, tool, _metal);

        return BotDoing.Work($"beating out {_kind?.Name}");
    }

    private BotDoing Handing(IBotWilful bot, Mobile body)
    {
        var goods = BotAnvil.Gather(body, _kind);

        goods.RemoveAll(item => BotBinding.IsBound(item, bot?.Bond));

        if (goods.Count == 0)
        {
            Stints++;
            Fruitless++;

            return BotDoing.Done($"{_swings} attempts at {_kind?.Name} and nothing to show");
        }

        var filled = 0;

        var want = _order ?? BotAuction.Demand(bot, _kind);

        for (var i = 0; i < goods.Count && want != null; i++)
        {
            filled += BotAuction.Fill(bot, want, goods[i]);
        }

        var listed = 0;

        if (BotDig.ListGoods)
        {
            var left = BotAnvil.Gather(body, _kind);

            left.RemoveAll(item => BotBinding.IsBound(item, bot?.Bond));

            for (var i = 0; i < left.Count; i++)
            {
                if (BotAuction.List(bot, left[i], BotAuction.Worth(_kind, Guess)) != null)
                {
                    listed++;
                }
            }
        }

        logger.Information(
            "{Name} beat out {Made} {Item} in {Swings} attempts: {Filled} to order, {Listed} to the market",
            body.Name,
            _made,
            _kind?.Name ?? "iron",
            _swings,
            filled,
            listed
        );

        _handed = filled + listed;

        Stints++;
        Wrought++;
        Pieces += _made;

        return BotDoing.Done($"{_made} {_kind?.Name} made, {filled} to order and {listed} on the stall");
    }

    public static string Describe() =>
        Stints == 0
            ? "no stint at an anvil has ended yet"
            : $"{Stints} stints at an anvil ended: {Wrought} with {Pieces} pieces beaten out, {RanOut} out of metal, "
              + $"{Fruitless} that used every swing and made nothing, {NoPlace} that found no anvil the engine would take, "
              + $"{Rounded} that walked round the forge to its anvil's side, {Preowned} that began with pieces of the kind already in the pack and left them out of what they made; {BotCraftEar.Describe()}";

    public static void Forget()
    {
        Stints = 0;
        Wrought = 0;
        Pieces = 0;
        RanOut = 0;
        Fruitless = 0;
        NoPlace = 0;
        Rounded = 0;
    }
}
