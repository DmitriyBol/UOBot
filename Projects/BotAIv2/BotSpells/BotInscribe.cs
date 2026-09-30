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
    public const string Trade = "inscribe";

    public static double Prior { get; set; } = 60.0;

    public static double WorkMinutes { get; set; } = 6.0;

    public static int Batch { get; set; } = 20;

    public static int SwingMs { get; set; } = 3000;

    public static int Patience { get; set; } = 10;

    public static int PatienceMs { get; set; } = 60000;

    private enum Leg
    {
        Shop,
        Work,
        Market
    }

    private BaseVendor _shop;

    private int _repicks;

    private readonly int _price;

    private Leg _leg;

    private CraftItem _recipe;

    private Type _kind;

    private int _worth;

    private int _had;

    private int _scrolls;

    private int _swings;

    private int _fruitless;

    private int _blanks = -1;

    private int _kept;

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

    public override Point3D Where => _shop?.Location ?? Point3D.Zero;

    public override double Expects => Prior;

    public override double Minutes => WorkMinutes;

    public override SkillName? Trains => SkillName.Inscribe;

    public override int Outlay => Batch * _price;

    public override bool AtCounter => _shop != null;

    public override double Coin => 0.0;

    public override int Made => _made + Math.Max(0, _scrolls - _placed) * _worth;

    public override string Stage => _leg switch
    {
        Leg.Shop => "after paper",
        Leg.Work => $"writing {_kind?.Name ?? "something"} ({_swings} attempts, {_scrolls} written)",
        _ => $"placing {_scrolls}"
    };

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
            return BotDoing.Walk(_shop.Map, _shop, BotArrival.Within(BotShops.CounterReach), $"to {_shop.Name} for blank scrolls");
        }

        if (BotShops.Buy(bot, _shop, typeof(BlankScroll), Batch, out var refused) <= 0)
        {
            var next = BotShops.Next(bot, _shop, typeof(BlankScroll), ref _repicks);

            if (next != null)
            {
                _shop = next;

                return BotDoing.Walk(next.Map, next, BotArrival.Within(BotShops.CounterReach), $"on to {next.Name} for blank scrolls");
            }

            return BotDoing.Failed(refused ?? "no blank scrolls to be had");
        }

        _leg = Leg.Work;

        return default;
    }

    private bool _worn;

    private BotDoing Writing(Mobile body)
    {
        var pen = BotQuill.Pen(body);

        if (pen == null && _swings == 0)
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

        var have = BotQuill.Held(body, _kind);

        if (have > _had)
        {
            _scrolls += have - _had;
            BotCraftwork.Produced(_kind, have - _had);
            _had = have;
        }

        if (pen == null)
        {
            if (_swung && Core.TickCount - _swungTick < SwingMs)
            {
                return BotDoing.Work("writing");
            }

            _worn = true;
            _leg = Leg.Market;

            return default;
        }

        if (BotQuill.Blanks(body) <= 0 || !BotQuill.Stocked(body, _recipe))
        {
            _leg = Leg.Market;

            return default;
        }

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

    private BotDoing Placing(IBotWilful bot, Mobile body)
    {
        if (_scrolls <= 0)
        {
            return BotDoing.Done($"{_swings} attempts, nothing came of it{(_worn ? ", the pen worn through" : "")}");
        }

        var written = BotQuill.Gather(body, _kind);

        for (var i = 0; i < written.Count; i++)
        {
            var scroll = written[i];

            if (BotGrimoire.Write(body, scroll))
            {
                _kept++;
                _placed++;
                _made += _worth;

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

        return BotDoing.Done($"{_scrolls} {_kind?.Name} in {_swings} attempts, {_kept} into its own book, {_sold} sold to order{(_worn ? ", the pen worn through" : "")}");
    }
}
