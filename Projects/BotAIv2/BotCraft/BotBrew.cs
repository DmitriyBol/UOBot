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

    /// <summary>The ledger's key.</summary>
    public const string Trade = "brew";

    /// <summary>What a brewing round is reckoned at before the ledger knows better. The fletcher's figure.</summary>
    public static double Prior { get; set; } = 90.0;

    /// <summary>How long one is expected to take.</summary>
    public static double WorkMinutes { get; set; } = 1.5;

    /// <summary>
    /// How often an attempt is made.
    ///
    /// <para>
    /// <b>Not every beat, and this is the difference between a trade and a jam.</b> <c>CraftItem.Craft</c>
    /// opens by taking the <c>CraftSystem</c> action lock and returns silently when it is already held, so a
    /// bot that swings on every beat spends its afternoon being told to wait for itself. The needle's figure,
    /// for the needle's reason — see <c>BotSew.SwingMs</c>.
    /// </para>
    /// </summary>
    public static int SwingMs { get; set; } = 3000;

    /// <summary>How long the mortar may produce nothing and spend nothing before the round is given up.</summary>
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

    /// <summary>What was in the pack when this round began, so a healer's own bottles are not counted as made.</summary>
    private int _had;

    /// <summary>Seeded on the first pass through the work, because the pack is not empty when it starts.</summary>
    private bool _counting;

    private bool _swung;

    private long _swungTick;

    /// <summary>Material left at the last look. Minus one, which no amount can equal — see BotSew.</summary>
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

        // <b>The leg is chosen by whether glass is wanted, not by whether a shopkeeper was found.</b> Word
        // for word the fletcher's correction about wood, on the material this trade needs most: "no shop"
        // must not mean "skip the buying leg", because the population's own stalls are a source and every
        // bot that drinks a draught puts an empty on one. See Glass, which reads them first.
        _leg = take > 0 ? Leg.Glass : Leg.Work;
    }

    public override string Kind => Trade;

    public override Map Map => _map;

    public override Point3D Where => _where;

    /// <summary>An order is worth more than speculation: the money for it is already down. See BotSmith.</summary>
    public override double Expects => _order == null ? Prior : Prior * 1.6;

    public override double Minutes => WorkMinutes;

    public override SkillName? Trains => SkillName.Alchemy;

    public override int Outlay => _take * _price;

    /// <summary>Nothing here is coin. What it produces is bottles, and those are counted as goods.</summary>
    public override double Coin => 0.0;

    public override int Made => _made;

    public override string Stage =>
        _leg switch
        {
            Leg.Glass => $"after {_take} Bottle to brew with",
            Leg.Work  => $"brewing ({_swings} attempts, {_bottled} made)",
            _         => $"putting {_bottled} bottles out"
        };

    /// <summary>The counter could not be reached. Written under the counter's name, as the fletcher does.</summary>
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

    /// <summary>Empty glass off a counter, and its own back off the market before it pays anybody.</summary>
    private BotDoing Glass(IBotWilful bot, Mobile body)
    {
        if (BotFlask.Bottles(body) >= BotFlask.LeastBottles)
        {
            _leg = Leg.Work;

            return default;
        }

        // A seller cannot buy from its own stall. The tailor's rule about leather and the fletcher's about
        // wood, on the one material this trade gets back for nothing every time somebody drinks.
        if (BotAuction.Reclaim(bot, typeof(Bottle)) > 0 && BotFlask.Bottles(body) >= BotFlask.LeastBottles)
        {
            _leg = Leg.Work;

            return default;
        }

        // <b>Another bot's glass before a shopkeeper's, and it needs no walk: the market holds its goods out
        // of the world, so a stall is bought from wherever the bot is standing.</b> This leg only ever knew
        // about counters — the twin of the hole the fletcher's wood leg had, on the one material this island
        // produces for nothing. A potion leaves its bottle behind and BotUnload lists it, because glass is
        // merchandise to everybody who does not brew; so every draught drunk on this shard put an empty on a
        // stall that no brewer could reach. Patrick's order of 04.09.2026, in as many words: the counter and
        // another bot's stall, and never the board — an order for glass freezes escrow nobody can fill.
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
            // Followed rather than aimed at: a shopkeeper wanders. See BotPeddle.
            return BotDoing.Walk(_shop.Map, _shop, BotArrival.Within(BotShops.CounterReach), $"to {_shop.Name} for glass");
        }

        if (BotShops.Buy(bot, _shop, typeof(Bottle), _take, out var refused) <= 0)
        {
            return BotDoing.Failed(refused ?? "no glass to be had");
        }

        _leg = Leg.Work;

        return default;
    }

    /// <summary>
    /// One attempt at a time, counted out of the pack <b>before</b> the next one is made.
    ///
    /// <para>
    /// <b>Crafting is asynchronous, and reading the pack in the same breath as the swing reads it too
    /// early.</b> <c>CraftItem.Craft</c> ends at <c>new InternalTimer(...).Start()</c> — the bottle appears a
    /// second or so later — so a leg that swung and then counted saw no change every single time, decided
    /// nothing had come of it, and gave the round up: 0 finished against 34 failed and 20 dropped in the
    /// twenty minutes to 11:31 on 04.09.2026, on a trade that was in fact brewing. The needle got this right
    /// long ago and says so in as many words; this is its shape, and <c>BotFletch</c> had the same fault.
    /// </para>
    /// </summary>
    private BotDoing Brewing(Mobile body)
    {
        var tool = BotFlask.Kit(body);

        if (tool == null)
        {
            return BotDoing.Failed("nothing to brew with");
        }

        if (!_counting)
        {
            // Seeded rather than started from nought: a healer walks into this carrying its own draughts,
            // and counting those as made would price the round at what it did not do.
            _counting = true;
            _had = BotFlask.Made(body, _potion);
        }

        // What the last attempt produced, counted before the next one is made. Failures produce nothing and
        // are supposed to: that is what the herbs are paying for.
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
            // Not a fault of this bot's, and the two halves are worth telling apart for whoever reads a run
            // of these: glass comes off a counter and the herb has to be walked into a wood for.
            return Finish(BotFlask.Bottles(body) <= 0 ? "no glass left to brew into" : "no herbs left to brew with");
        }

        var (reagent, _, _) = BotFlask.Costs(recipe);

        // Both halves on one number, because a swing spends both and either running dry ends the round. The
        // needle watches its one material for exactly this: a craft that consumes on failure too moves this
        // through an honest run of bad luck, and leaves it stock still when the action lock has jammed.
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

    /// <summary>
    /// The order first, because its money is already down, and the rest onto the market under the
    /// alchemist's own price.
    /// </summary>
    private BotDoing Selling(IBotWilful bot, Mobile body)
    {
        var ordered = 0;
        var listed = 0;

        // What the class itself carries stays in the pack: a healer that sells the heal potion it is
        // standing up with has made the shard poorer, not richer.
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
                    // The split could not be made, so the whole stack stays rather than being sold out from
                    // under a bot that needs one of them. See the twin of this line in BotUnload.
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

    /// <summary>How many of what it just brewed this bot is meant to be carrying itself.</summary>
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
