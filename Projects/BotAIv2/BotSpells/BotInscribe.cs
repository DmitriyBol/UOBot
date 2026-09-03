using System;
using Server.Engines.Craft;
using Server.Items;
using Server.Mobiles;

namespace Server.BotAI.V2;

/// <summary>
/// Buy paper, write scrolls, keep what the book is short of and sell the rest. The scribe's chain.
///
/// <para>
/// <b>This is the first work in the project whose output only another bot can buy.</b> A mage vendor sells
/// the first three circles and nothing above them, so every scroll from the fourth circle up that exists on
/// this shard was written by somebody. Mining put metal out that nothing needed; sewing bought its cloth from
/// a shelf. This trade sits on both sides of a market made of bots: it buys herbs and paper from shopkeepers
/// and sells the one thing shopkeepers cannot supply.
/// </para>
///
/// <para>
/// <b>Filling its own book costs it nothing and is decided here, not priced anywhere.</b> A scroll it has
/// just written is worth what the market says; whether it keeps it or sells it does not change what it
/// produced. So the first one of a kind goes into its own book if the book lacks it, and every further one is
/// sold — and "collecting all the spells" turns out to be what happens to a scribe who gets good at writing,
/// rather than a goal anybody had to give it a price for.
/// </para>
///
/// <para>
/// <b>Mana is the throttle, and it is the engine's.</b> Four for the first circle, fifty for the eighth. A
/// mage with fifty Intelligence writes one top-circle scroll and then has to sit down, which is why this trade
/// and Meditation are on the same vector.
/// </para>
/// </summary>
public sealed class BotInscribe : BotDeed
{
    /// <summary>The ledger's key.</summary>
    public const string Trade = "inscribe";

    /// <summary>
    /// What a session at the pen is reckoned at per minute before experience corrects it.
    ///
    /// A little above the needle's fifty-five, and for a stated reason rather than a feeling: it is the same
    /// on-vector skill at full rate, the materials cost more, and what comes off it asks more. It is also the
    /// number most likely to be wrong, because the real pace is set by mana coming back — and that is exactly
    /// what the ledger is for.
    /// </summary>
    public static double Prior { get; set; } = 60.0;

    /// <summary>How long a session is expected to take.</summary>
    public static double WorkMinutes { get; set; } = 6.0;

    /// <summary>How many blank scrolls to buy in one go.</summary>
    public static int Batch { get; set; } = 20;

    /// <summary>How often an attempt is made. The engine's craft has its own timer besides this.</summary>
    public static int SwingMs { get; set; } = 3000;

    /// <summary>
    /// How many attempts may pass with nothing whatever changing before the session is given up.
    ///
    /// Generous, because a run of failed skill checks is ordinary and does consume blanks — what this catches
    /// is the other thing entirely: attempts that are not happening at all.
    /// </summary>
    public static int Patience { get; set; } = 10;

    /// <summary>
    /// How long a scribe will sit waiting for mana before it takes what it has written and goes.
    ///
    /// A session that stalls on an empty pool must end rather than wait for ever: the work is still finished
    /// and still measured, and a bot that has run itself dry has learned something the ledger should see.
    /// </summary>
    public static int PatienceMs { get; set; } = 60000;

    private enum Leg
    {
        Shop,
        Work,
        Market
    }

    private readonly BaseVendor _shop;

    private readonly int _price;

    private Leg _leg;

    private CraftItem _recipe;

    private Type _kind;

    private int _worth;

    private int _had;

    private int _scrolls;

    private int _swings;

    /// <summary>Attempts since anything at all changed — a scroll appeared, or a blank was spent.</summary>
    private int _fruitless;

    private int _blanks = -1;

    private int _kept;

    /// <summary>How many written scrolls have been dealt with — booked, sold or listed. See <see cref="Made"/>.</summary>
    private int _placed;

    private int _sold;

    private int _made;

    private bool _swung;

    private long _swungTick;

    private long _restingTick;

    public BotInscribe(BaseVendor shop, int price)
    {
        _shop = shop;
        _price = Math.Max(1, price);
    }

    public override string Kind => Trade;

    public override Map Map => _shop?.Map;

    /// <summary>The shop, for the whole life of the work: the one fixed place in the chain.</summary>
    public override Point3D Where => _shop?.Location ?? Point3D.Zero;

    public override double Expects => Prior;

    public override double Minutes => WorkMinutes;

    /// <summary>Inscribe, and it is on the mage's own vector — which is what makes this worth its time.</summary>
    public override SkillName? Trains => SkillName.Inscribe;

    public override int Outlay => Batch * _price;

    /// <summary>
    /// Nothing is promised in coin. A scroll may end up filling somebody's want, which pays at once, but at
    /// the moment the work is chosen that is a possibility rather than a plan.
    /// </summary>
    public override double Coin => 0.0;

    /// <summary>
    /// What was produced and <em>not</em> sold: scrolls kept for the book and scrolls left on a stall, and
    /// the ones written but not yet placed.
    ///
    /// A scroll that filled a want is paid for in coin the moment it is handed over, and the coin is already
    /// in the takings — counting it here as well would pay the scribe twice for one scroll.
    ///
    /// <para>
    /// <b>The last clause is why a dropped session used to read as a catastrophe.</b> Everything was counted
    /// in the placing leg, so a scribe whose work the auction chose against mid-page had bought paper, made
    /// scrolls, and declared nothing for them: on 02.09.2026 Calla 2 settled a dropped inscribe at "-100 in
    /// 1.3 min (-79/min): -100 coin, 0 made" with four scrolls in its pack, and Cedric at -198/min with
    /// four more. The scrolls were real and still in hand; the ledger was taught that writing loses money.
    /// Unplaced work is counted here at the price it will be asked for and taken back out as it is placed,
    /// so nothing is counted twice.
    /// </para>
    /// </summary>
    public override int Made => _made + Math.Max(0, _scrolls - _placed) * _worth;

    public override string Stage => _leg switch
    {
        Leg.Shop => "after paper",
        Leg.Work => $"writing {_kind?.Name ?? "something"} ({_swings} attempts, {_scrolls} written)",
        _ => $"placing {_scrolls}"
    };

    /// <summary>
    /// The way to the shopkeeper turned out not to exist.
    ///
    /// <para>
    /// <b>Nothing to bend to here, and something to write down — and it was the writing down that was
    /// missing.</b> What this undertaking carries was priced against <em>this</em> shopkeeper, so swapping in
    /// another one mid-errand would carry a stale price; failing is the honest answer. But the failure has to
    /// be filed under the <em>place's</em> name, because that is the word the shop lookup asks in. Filed under
    /// the undertaking's name — which is all <c>BotWill.Settle</c> can do — it is written and never read, and
    /// the next beat picks the same unreachable shopkeeper on distance alone. Calla walked at Gus thirty-one
    /// times in an hour on 26.08.2026 that way.
    /// </para>
    /// </summary>
    public override bool Bend(IBotWilful bot)
    {
        if (_shop == null)
        {
            return false;
        }

        bot?.Resolve?.Ledger?.Beware(BotShops.ShopKind, _shop.Map, _shop.Location);

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
                Leg.Shop => Shopping(bot, body),
                Leg.Work => Writing(body),
                _ => Placing(bot, body)
            };

            if (doing.Kind != BotDoingKind.None)
            {
                return doing;
            }
        }

        return BotDoing.Failed("could not settle on a next step");
    }

    private BotDoing Shopping(IBotWilful bot, Mobile body)
    {
        if (BotQuill.Blanks(body) > 0)
        {
            _leg = Leg.Work;

            return default;
        }

        if (_shop == null || _shop.Deleted || _shop.Map == null || _shop.Map == Map.Internal)
        {
            return BotDoing.Failed("the mage's shop is gone");
        }

        if (!body.InRange(_shop.Location, BotShops.CounterReach))
        {
            // The distance the work itself asks for, on the line above. See BotArrival.Beside.
            // Followed rather than aimed at: a shopkeeper wanders. See BotPeddle for the whole reason.
            return BotDoing.Walk(_shop.Map, _shop, BotArrival.Within(BotShops.CounterReach), $"to {_shop.Name} for blank scrolls");
        }

        if (BotShops.Buy(bot, _shop, typeof(BlankScroll), Batch, out var refused) <= 0)
        {
            // The fourth and last counter in the project. All four said something different and none of them
            // said which of the six ways a purchase can fail had happened.
            return BotDoing.Failed(refused ?? "no blank scrolls to be had");
        }

        _leg = Leg.Work;

        return default;
    }

    private BotDoing Writing(Mobile body)
    {
        var pen = BotQuill.Pen(body);

        if (pen == null)
        {
            return BotDoing.Failed("nothing to write with");
        }

        if (_recipe == null)
        {
            _recipe = BotQuill.Choose(body, _price, out _kind, out _worth);

            if (_recipe == null)
            {
                return BotDoing.Failed("no spell it can write with the herbs it has");
            }

            _had = BotQuill.Held(body, _kind);
        }

        // What the last attempt produced, counted before the next one is made. A failure produces nothing and
        // is supposed to: that is what the blank and the herbs are paying for.
        var have = BotQuill.Held(body, _kind);

        if (have > _had)
        {
            _scrolls += have - _had;
            _had = have;
        }

        if (BotQuill.Blanks(body) <= 0 || !BotQuill.Stocked(body, _recipe))
        {
            _leg = Leg.Market;

            return default;
        }

        // The engine refuses the attempt outright below the cost, so this is asked before swinging rather
        // than discovered from a message nobody reads.
        if (body.Mana < _recipe.Mana)
        {
            if (_restingTick == 0)
            {
                _restingTick = Core.TickCount;
            }
            else if (Core.TickCount - _restingTick >= PatienceMs)
            {
                _leg = Leg.Market;

                return default;
            }

            return BotDoing.Work("resting for mana");
        }

        _restingTick = 0;

        if (_swung && Core.TickCount - _swungTick < SwingMs)
        {
            return BotDoing.Work("writing");
        }

        // Nothing is changing hands.
        //
        // <b>An unbounded "still writing" is the one shape of state this project refuses to have, and it had
        // one.</b> The engine's craft can decline silently — the action lock is held, the skill check comes
        // out at nil, a resource is not where it expected — and it declines by returning, with a message to a
        // client that a bot does not have. So a mage born with twenty blanks never walks to a shop, sits down
        // to write, and stays there: no scroll is produced, no blank is spent, no mana is drawn, and there is
        // nothing in the log at all. Two mages spent sixteen minutes doing that in plain sight.
        var blanks = BotQuill.Blanks(body);

        if (_blanks >= 0 && blanks == _blanks && have == _had)
        {
            if (++_fruitless >= Patience)
            {
                _leg = Leg.Market;

                return BotDoing.Done(
                    $"wrote nothing in {_swings} attempts — {blanks} blanks, {body.Mana} mana, {_recipe.Mana} needed"
                );
            }
        }
        else
        {
            _fruitless = 0;
        }

        _blanks = blanks;

        _swings++;
        _swung = true;
        _swungTick = Core.TickCount;

        BotQuill.Swing(body, _recipe, pen);

        return BotDoing.Work("writing");
    }

    /// <summary>
    /// Where each scroll goes, in the order that makes the trade worth having.
    ///
    /// <para>
    /// Its own book first, and only for the first one of a kind — after that the book already has the spell
    /// and the question does not arise. Then somebody's standing want, because a funded want is a buyer who
    /// has already put the money down and it pays at once. Then a stall, which is the market being told what
    /// exists at what price even though nobody has asked for it yet.
    /// </para>
    /// </summary>
    private BotDoing Placing(IBotWilful bot, Mobile body)
    {
        if (_scrolls <= 0)
        {
            // The paper is gone and nothing came of it. Finished rather than failed: the attempts were real,
            // the skill checks happened, and what it cost is what learning a trade costs.
            return BotDoing.Done($"{_swings} attempts, nothing came of it");
        }

        var written = BotQuill.Gather(body, _kind);

        for (var i = 0; i < written.Count; i++)
        {
            var scroll = written[i];

            // Writing one into the book takes <em>one</em> off the stack. The rest of the stack is still a
            // stack of scrolls and still has to be placed — falling through to the market here rather than
            // going round the loop is the difference between selling two and quietly carrying them for ever.
            if (BotGrimoire.Write(body, scroll))
            {
                _kept++;
                _placed++;
                _made += _worth;

                // It wrote the thing it was queueing for. Whatever was standing on the market asking for it
                // is asking for nothing, and the money behind it comes back.
                BotAuction.Withdrawn(bot, _kind);
            }

            if (scroll.Deleted || scroll.Amount <= 0)
            {
                continue;
            }

            var left = scroll.Amount;
            var want = BotAuction.Demand(bot, _kind);
            var sold = want == null ? 0 : BotAuction.Fill(bot, want, scroll);

            if (sold > 0)
            {
                _sold += sold;
                _placed += sold;

                if (sold >= left)
                {
                    continue;
                }

                left -= sold;
            }

            if (BotAuction.List(bot, scroll, _worth) != null)
            {
                _made += left * _worth;
                _placed += left;
            }
        }

        return BotDoing.Done($"{_scrolls} {_kind?.Name} in {_swings} attempts, {_kept} into its own book, {_sold} sold to order");
    }
}
