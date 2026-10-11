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

    public static double OrderLift { get; set; } = 1.6;

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

    private readonly BotQuillPlan _plan;

    private readonly Map _map;

    private readonly Point3D _here;

    private bool _started;

    public BotInscribe(BaseVendor shop, int price, BotQuillPlan plan, Mobile scribe)
    {
        _shop = shop;
        _price = Math.Max(1, price);
        _plan = plan;
        _recipe = plan?.Recipe;
        _kind = plan?.Kind;
        _worth = plan?.Worth ?? 0;
        _map = scribe?.Map;
        _here = scribe?.Location ?? Point3D.Zero;
    }

    public override string Kind => Trade;

    private bool Buys => _plan?.Buys ?? true;

    public override Map Map => Buys ? _shop?.Map : _map;

    public override Point3D Where => Buys ? _shop?.Location ?? Point3D.Zero : _here;

    public override double Expects => _plan?.For != null ? Prior * OrderLift : Prior;

    public override double Minutes => WorkMinutes;

    public override SkillName? Trains => SkillName.Inscribe;

    public override int Outlay => _plan?.Outlay ?? Batch * _price;

    public override bool AtCounter => Buys && _shop != null;

    public override double Coin => _plan?.For != null ? 1.0 : 0.0;

    public bool Serves(BotWant want) => want != null && ReferenceEquals(_plan?.For, want) && _leg != Leg.Market;

    public override void Taken(IBotWilful bot)
    {
        BotScribes.Took(_plan);

        if (_plan?.For != null)
        {
            BotScribes.Claim(_plan.For, bot);
        }
    }

    public override int Made => _made + BotGlut.Yield(_kind, Math.Max(0, _scrolls - _placed), _worth);

    public override string Stage => _leg switch
    {
        Leg.Shop => _plan?.Herbs is { Count: > 0 } ? "after paper and herbs" : "after paper",
        Leg.Work => $"writing {_kind?.Name ?? "something"}{For} ({_swings} attempts, {_scrolls} written)",
        _ => $"placing {_scrolls}"
    };

    private string For =>
        _plan?.For?.Buyer?.Self is { } buyer ? $" for {buyer.Name}'s want" : _plan?.Own == true ? " for itself" : "";

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
                Leg.Work => Writing(bot, body),
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
        var paper = PaperWanted(body);

        if (paper <= 0 && !HerbsLacking(body))
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

        if (paper > 0 && BotShops.Buy(bot, _shop, typeof(BlankScroll), paper, out var refused) <= 0)
        {
            var next = BotShops.Next(bot, _shop, typeof(BlankScroll), ref _repicks);

            if (next != null)
            {
                _shop = next;

                return BotDoing.Walk(next.Map, next, BotArrival.Within(BotShops.CounterReach), $"on to {next.Name} for blank scrolls");
            }

            return BotDoing.Failed(refused ?? "no blank scrolls to be had");
        }

        BuyHerbs(bot, body);

        _leg = Leg.Work;

        return default;
    }

    private int PaperWanted(Mobile body)
    {
        var held = BotQuill.Blanks(body);

        if (_plan?.Order != true)
        {
            return held > 0 ? 0 : Math.Max(1, Batch);
        }

        return Math.Max(0, _plan.Attempts - held);
    }

    private bool HerbsLacking(Mobile body)
    {
        var pack = body?.Backpack;
        var resources = _recipe?.Resources;

        if (_plan?.Order != true || pack == null || resources == null)
        {
            return false;
        }

        for (var i = 0; i < resources.Count; i++)
        {
            var res = resources[i];

            if (res.ItemType != null && res.ItemType != typeof(BlankScroll)
                && pack.GetAmount(res.ItemType) < _plan.Attempts * Math.Max(1, res.Amount))
            {
                return true;
            }
        }

        return false;
    }

    private void BuyHerbs(IBotWilful bot, Mobile body)
    {
        var pack = body?.Backpack;
        var resources = _recipe?.Resources;

        if (_plan?.Order != true || pack == null || resources == null)
        {
            return;
        }

        for (var i = 0; i < resources.Count; i++)
        {
            var res = resources[i];

            if (res.ItemType == null || res.ItemType == typeof(BlankScroll))
            {
                continue;
            }

            var lacking = _plan.Attempts * Math.Max(1, res.Amount) - pack.GetAmount(res.ItemType);

            if (lacking > 0)
            {
                BotScribes.Bought(BotShops.Buy(bot, _shop, res.ItemType, lacking));
            }
        }
    }

    private int Allowed(IBotWilful bot) =>
        _kind != null && BotUnload.Keeps(bot).TryGetValue(_kind, out var allowed) ? Math.Max(0, allowed) : 0;

    private bool _worn;

    private BotDoing Writing(IBotWilful bot, Mobile body)
    {
        var pen = BotQuill.Pen(body);

        if (pen == null && _swings == 0)
        {
            return BotDoing.Failed("nothing to write with");
        }

        if (!_started)
        {
            if (_recipe == null)
            {
                var plan = BotQuill.Plan(bot, _shop, _price);

                if (plan == null)
                {
                    return BotDoing.Failed("no spell it can write with the herbs it has");
                }

                _recipe = plan.Recipe;
                _kind = plan.Kind;
                _worth = plan.Worth;
            }

            _started = true;
            _had = BotQuill.Held(body, _kind);
        }

        var have = BotQuill.Held(body, _kind);

        if (have > _had)
        {
            _scrolls += have - _had;
            BotCraftwork.Produced(_kind, have - _had);
            _had = have;
        }

        if (_plan?.Order == true && _scrolls >= Math.Max(1, _plan.Units))
        {
            _leg = Leg.Market;

            return default;
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

        var reserve = _plan?.Order == true && _swings < _plan.Attempts ? 0 : BotQuill.Reserve;

        if (BotQuill.Blanks(body) <= 0 || !BotQuill.Stocked(body, _recipe, reserve))
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

        var keeps = Allowed(bot);
        var order = _plan?.For is { IsOpen: true } open && BotMarket.Stands(open) ? open : null;
        var spare = order == null
            ? _scrolls
            : keeps >= int.MaxValue ? 0 : Math.Max(0, BotHandover.Carried(bot, _kind) - keeps - order.Payable);

        for (var i = 0; i < written.Count; i++)
        {
            var scroll = written[i];

            if (spare > 0 && BotGrimoire.Write(body, scroll))
            {
                spare--;
                _kept++;
                _placed++;
                _made += _worth;

                BotMarket.Withdrawn(bot, _kind, BotWithdraw.Have);
            }

            if (scroll.Deleted || scroll.Amount <= 0)
            {
                continue;
            }

            var left = scroll.Amount;

            _made += BotGlut.Yield(_kind, left, _worth);
            _placed += left;
        }

        var surplus = keeps >= int.MaxValue ? 0 : Math.Max(0, BotHandover.Carried(bot, _kind) - keeps);
        var asked = order ?? BotMarket.Demand(bot, _kind);

        _sold = asked == null ? 0 : Math.Min(surplus, asked.Payable);

        BotScribes.Wrote(_plan, _sold, _scrolls);

        return BotDoing.Done($"{_scrolls} {_kind?.Name} in {_swings} attempts, {_kept} into its own book, {_sold} for somebody's want, to be carried to it{(For.Length > 0 ? "; the session written" + For : "")}{(_worn ? ", the pen worn through" : "")}");
    }
}
