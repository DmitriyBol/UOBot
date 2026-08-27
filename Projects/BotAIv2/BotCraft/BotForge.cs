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

    /// <summary>The ledger's key.</summary>
    public const string Trade = "forge";

    /// <summary>What smithing is reckoned at per minute before the ledger corrects it.</summary>
    public static double Prior { get; set; } = 55.0;

    /// <summary>How long a stint at the anvil is expected to take.</summary>
    public static double WorkMinutes { get; set; } = 6.0;

    /// <summary>
    /// How long between attempts.
    ///
    /// A second, matching what mining was given on the same day and rather faster than the three the tailor
    /// and the scribe take. A swing at an anvil is one swing; there is no reason for it to be a stream.
    /// </summary>
    public static int SwingMs { get; set; } = 1000;

    /// <summary>How many attempts one stint may make before it takes what it has and stops.</summary>
    public static int MaxSwings { get; set; } = 24;

    /// <summary>What a piece of ironwork is offered at when nothing on the shard has priced one.</summary>
    public static int Guess { get; set; } = 45;

    private enum Leg
    {
        Walk,
        Work,
        Hand
    }

    private readonly Map _map;

    private readonly Point3D _smithy;

    /// <summary>What was asked for, when this stint is filling somebody's order. Null when it is speculative.</summary>
    private readonly BotWant _order;

    private Leg _leg;

    private CraftItem _recipe;

    private Type _kind;

    private int _swings;

    /// <summary>The metal this piece is being beaten out of. See BotAnvil.Best.</summary>
    private Type _metal;

    private int _made;

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

    /// <summary>
    /// An order is worth more than speculation, and by exactly the amount somebody has already put down.
    ///
    /// Not a bonus invented here: the money is real, it is in escrow, and it is the difference between making
    /// something that will certainly be bought and making something that might be.
    /// </summary>
    public override double Expects => _order == null ? Prior : Prior * 1.6;

    public override double Minutes => WorkMinutes;

    public override SkillName? Trains => BotAnvil.Skill;

    /// <summary>Mostly goods until they are sold, which is what Made is for.</summary>
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
        // Asked of the engine rather than of the distance to the remembered point: what matters is whether a
        // hammer will work here, and only the engine knows that. See BotAnvil.AtASmithy.
        if (BotAnvil.AtASmithy(body))
        {
            _leg = Leg.Work;

            return BotDoing.Work("at the anvil");
        }

        // <b>Standing at the remembered forge, and the engine still says no.</b> That is the end of the road
        // for this forge and it has to be said out loud, because the alternative is what used to happen: the
        // walk was aimed two tiles out, arriving satisfied it, the smithy test did not, and the undertaking
        // answered the identical walk order for ever — a piece of work that is both immortal and invisible,
        // which is the failure this project has paid for more than once.
        //
        // Written down under the <em>place's</em> name, "fire", which is the word BotGround.Fire asks in.
        // Filed under the undertaking's name it would be written and never read, and the next beat would
        // choose this same unusable forge again on distance alone.
        if (body.InRange(_smithy, 1))
        {
            Refuse(bot);

            return BotDoing.Failed(
                $"no anvil the engine will accept within {BotAnvil.Reach} of the forge at ({_smithy.X}, {_smithy.Y})"
            );
        }

        // Beside it, not two tiles off. The engine wants an anvil <em>and</em> a forge within
        // BotAnvil.Reach of the body, with line of sight to both; aiming at the far edge of that was how a
        // bot came to be told it was at a smithy while standing where no hammer would work.
        return BotDoing.Walk(_map, _smithy, BotArrival.Beside, "to a forge");
    }

    /// <summary>This forge is no use to this bot. Kept off its list for a while — see BotLedger.Beware.</summary>
    private void Refuse(IBotWilful bot) => bot?.Resolve?.Ledger?.Beware(BotGround.FireKind, _map, _smithy);

    /// <summary>
    /// The way to the forge turned out not to exist. Nothing to bend to — a forge is picked as the nearest
    /// known one, so choosing again would choose the same one — but the refusal is filed under the place's
    /// name so that the next choice is a different forge rather than this one again.
    /// </summary>
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
            return BotDoing.Failed("no hammer");
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

        // Counted out of the pack rather than believed from the attempts. See BotCraftwork.Swing: an attempt
        // is not an item, and the first version's tally said forty-four made about a smith that had made
        // nothing.
        // Counted before and after, because the difference is the only thing on the shard that says a swing
        // just worked — and that is the one moment the free craft may be asked for. See BotCraftwork.Bonus.
        var had = _made;

        _made = BotAnvil.Made(body, _kind);

        if (_made > had)
        {
            _made += BotCraftwork.Bonus(body, _kind);
        }

        if (_made > 0 && (_order != null || _swings >= MaxSwings))
        {
            _leg = Leg.Hand;

            return Handing(bot, body);
        }

        // <b>Its own iron back off the market before giving up for want of iron.</b> A smith lists the metal
        // it is not using, and then a recipe wants more than the pack holds — so the bot that owns exactly
        // the ingots it needs stands at the anvil and reports "out of metal", while the order it raises to
        // replace them is refused by the market on the grounds that it is selling them. This is the tailor's
        // leather, one trade along: see the note in BotSew.Buying. Reclaiming leaves the stall standing and
        // empty, so the price it learned survives.
        var cost = BotCraftwork.Cost(_recipe);

        // <b>Which metal, asked every beat rather than once.</b> A smith may start a piece in iron and finish
        // it in bronze because a delivery arrived — and it should, because the same swing in a better metal
        // is a better piece for nothing. See BotAnvil.Best: dearest the skill will take and the pack can
        // fill, falling back to iron.
        _metal = BotAnvil.Best(body, cost);

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

            return BotDoing.Failed(_swings >= MaxSwings ? "nothing came of the iron" : "out of metal");
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

    /// <summary>
    /// Handing over what was made: into the order it was made for, and whatever is left onto the market.
    ///
    /// <para>
    /// The order first and always. Its money is already down, and a smith that made a blade to somebody's
    /// order and then sold it to a shopkeeper has taken payment for a thing it did not deliver — which the
    /// market would eventually notice and nobody would enjoy.
    /// </para>
    /// </summary>
    private BotDoing Handing(IBotWilful bot, Mobile body)
    {
        var goods = BotAnvil.Gather(body, _kind);

        if (goods.Count == 0)
        {
            return BotDoing.Done($"{_swings} attempts at {_kind?.Name} and nothing to show");
        }

        var filled = 0;

        // Whoever wanted it — the order this was made for if there is one, and otherwise anybody who has
        // asked the board for the same thing while the hammer was going.
        var want = _order ?? BotAuction.Demand(bot, _kind);

        for (var i = 0; i < goods.Count && want != null; i++)
        {
            filled += BotAuction.Fill(bot, want, goods[i]);
        }

        var listed = 0;

        if (BotDig.ListGoods)
        {
            var left = BotAnvil.Gather(body, _kind);

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

        return BotDoing.Done($"{_made} {_kind?.Name} made, {filled} to order and {listed} on the stall");
    }
}
