using System;
using System.Collections.Generic;
using Server.Engines.Craft;
using Server.Items;
using Server.Logging;

namespace Server.BotAI.V2;

/// <summary>
/// Who on the island can write which circle, what a scroll no shelf sells may be asked for, which scribe is writing which want,
/// and the scribes' counts for the <c>Needs:</c> line.
///
/// <para>
/// <b>Measured before it (03.10.2026, build 395, Patrick: "the focus of tonight's work is 99 % fulfilment of Needs").</b> Scroll
/// lines failed in every session of 02.10: Greater Heal 0 met of 5, 4 and 2 lines, Lightning 0 of 9, Harm 1 of 6. The failure
/// named was "passed over: trader found nowhere to have it" (Lightning: passed over 1252 times, 1089 of them for that), which is
/// true and says nothing: no shelf sells a circle above the third, so only a bot scribe can answer one, and the scribes did not
/// write — "took on inscribe" 0 times in 85 minutes at 15:26, 0 in 60 at 16:51, once at 17:54. Meanwhile the wants climbed:
/// each new Greater Heal want opened at the dearest one standing (63, 82, 142gp) and raised four times over that, so Cirdan's
/// reached 326gp for a scroll whose paper and herbs cost 17gp, and at 03:12:25 on 03.10 its want for Raw Ribs stood down because
/// "329gp will not pay for one at 5gp".
/// </para>
///
/// <para>
/// <b>What a scroll may be asked for is what writing it costs here, and a fifth on top</b> (<see cref="Ceiling"/>): the paper and
/// herbs of one attempt at the island's lowest counter prices, over the success chance of the best scribe on the island who can
/// write the circle, with <see cref="BotQuill.Markup"/> — never below the shop step the want opens at. Raising past it buys
/// nothing: a scribe takes a paid scroll before practice whatever the price (<see cref="BotQuill.Plan"/>), so a louder offer only
/// holds more of the buyer's money away from its other lines. A circle nobody on the island can write is said once in the log and
/// its wants are held at the price they open at.
/// </para>
/// </summary>
public static class BotScribes
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotScribes));

    public static int CensusMs { get; set; } = 60000;

    public static int PaperGuess { get; set; } = 5;

    private const int Circles = 8;

    private static readonly double[] _min = new double[Circles + 1];

    private static readonly double[] _max = new double[Circles + 1];

    private static readonly int[] _mana = new int[Circles + 1];

    private static readonly Dictionary<Type, CraftItem> _recipes = [];

    public static CraftItem Recipe(Type scroll)
    {
        Table();

        return scroll != null && _recipes.TryGetValue(scroll, out var recipe) ? recipe : null;
    }

    private static void Table()
    {
        if (_recipes.Count > 0 || BotQuill.System?.CraftItems is not { } recipes)
        {
            return;
        }

        for (var i = 0; i < recipes.Count; i++)
        {
            var recipe = recipes[i];
            var spell = BotGrimoire.SpellOf(recipe?.ItemType);

            if (spell < 0)
            {
                continue;
            }

            var circle = BotGrimoire.Circle(spell);

            if (circle < 1 || circle > Circles)
            {
                continue;
            }

            _recipes[recipe.ItemType] = recipe;

            var skills = recipe.Skills;

            for (var s = 0; s < skills.Count; s++)
            {
                if (skills[s].SkillToMake == SkillName.Inscribe)
                {
                    _min[circle] = skills[s].MinSkill;
                    _max[circle] = skills[s].MaxSkill;
                }
            }

            _mana[circle] = recipe.Mana;
        }
    }

    private static double LeastChance(int circle) =>
        _max[circle] > _min[circle] ? Math.Clamp(BotQuill.Margin / (_max[circle] - _min[circle]), 0.01, 1.0) : 1.0;

    private static readonly int[] _able = new int[Circles + 1];

    private static readonly double[] _best = new double[Circles + 1];

    private static int _scanned;

    private static long _censusTick;

    private static bool _censused;

    private static void Census()
    {
        var now = Core.TickCount;

        if (_censused && now - _censusTick < CensusMs)
        {
            return;
        }

        Table();

        if (_recipes.Count == 0)
        {
            return;
        }

        _censused = true;
        _censusTick = now;
        _scanned = 0;
        Array.Clear(_able);
        Array.Clear(_best);

        Count(BotPopulation.Bots);

        if (_scanned > 0 && !_capped)
        {
            _capped = true;
            CapStanding();
        }
    }

    private static bool _capped;

    private static void CapStanding()
    {
        var wants = BotMarket.Wants;

        for (var i = 0; i < wants.Count; i++)
        {
            var want = wants[i];

            if (want.Ceiling > 0 || !Writ(want.Kind))
            {
                continue;
            }

            var writ = Ceiling(want.Kind, want.Offer, out _);

            if (writ <= 0)
            {
                continue;
            }

            if (want.Offer > writ)
            {
                Cut(want.Amount, want.Offer - writ);

                logger.Information(
                    "{Name}'s want for {Kind} stood at {Offer}gp and is cut to {Writ}gp, what writing one costs on the island with a fifth on top (want #{Id})",
                    want.Buyer?.Self?.Name ?? "somebody",
                    want.Label,
                    want.Offer,
                    writ,
                    want.Id
                );
            }

            want.CutTo(writ, writ);
        }
    }

    private static void Count(IReadOnlyList<BotMobile> bots)
    {
        var lowest = _min[BotGrimoire.ShopCircles + 1];

        for (var i = 0; i < bots.Count; i++)
        {
            var bot = bots[i];

            if (bot == null || bot.Deleted)
            {
                continue;
            }

            _scanned++;

            var skill = bot.Skills[SkillName.Inscribe].Value;

            if (skill - BotQuill.Margin < lowest || !Writes(bot))
            {
                continue;
            }

            for (var c = BotGrimoire.ShopCircles + 1; c <= Circles; c++)
            {
                if (_max[c] <= _min[c] || skill - BotQuill.Margin < _min[c] || bot.ManaMax < _mana[c])
                {
                    continue;
                }

                _able[c]++;

                var chance = Math.Clamp((skill - _min[c]) / (_max[c] - _min[c]), 0.0, 1.0);

                if (chance > _best[c])
                {
                    _best[c] = chance;
                }
            }
        }
    }

    private static bool Writes(BotMobile bot)
    {
        var skills = bot.Class?.Skills;

        if (skills != null)
        {
            for (var i = 0; i < skills.Count; i++)
            {
                if (skills[i].Skill == SkillName.Inscribe)
                {
                    return true;
                }
            }
        }

        return BotQuill.Pen(bot) != null;
    }

    public static long CappedAsks { get; private set; }

    public static long CappedGold { get; private set; }

    public static long CutOnRestore { get; private set; }

    public static long CutGold { get; private set; }

    public static long HeldAtPrice { get; private set; }

    public static long Beyond { get; private set; }

    private static readonly HashSet<Type> _saidBeyond = [];

    public static bool Writ(Type kind)
    {
        var spell = BotGrimoire.SpellOf(kind);

        return spell >= 0 && !BotGrimoire.Sold(spell);
    }

    public static int Ceiling(Type kind, int opening, out bool beyond)
    {
        beyond = false;

        if (!Writ(kind) || Recipe(kind) is not { } recipe)
        {
            return 0;
        }

        var circle = BotGrimoire.Circle(BotGrimoire.SpellOf(kind));

        Census();

        double chance;

        if (_scanned == 0)
        {
            chance = LeastChance(circle);
        }
        else if (_able[circle] == 0)
        {
            beyond = true;

            return Math.Max(1, opening);
        }
        else
        {
            chance = Math.Max(LeastChance(circle), _best[circle]);
        }

        var fair = BotQuill.Asking((int)Math.Ceiling(Attempt(recipe) / chance));

        return Math.Max(BotGrimoire.ShopPrice(circle), fair);
    }

    private static double Attempt(CraftItem recipe)
    {
        var paper = BotShops.Lowest(typeof(BlankScroll));
        var bill = (double)(paper > 0 ? paper : PaperGuess);
        var resources = recipe.Resources;

        for (var i = 0; i < resources.Count; i++)
        {
            var res = resources[i];

            if (res.ItemType == null || res.ItemType == typeof(BlankScroll))
            {
                continue;
            }

            var herb = BotShops.Lowest(res.ItemType);

            bill += Math.Max(1, res.Amount) * (herb > 0 ? herb : BotQuill.HerbGuess);
        }

        return bill;
    }

    internal static void SaidBeyond(Type kind, int offer)
    {
        Beyond++;

        if (kind == null || !_saidBeyond.Add(kind))
        {
            return;
        }

        var circle = BotGrimoire.Circle(BotGrimoire.SpellOf(kind));

        logger.Information(
            "Nobody on the island can write {Kind}: circle {Circle} wants Inscribe {Need:F1} and a pool of {Mana} mana, and no bot that writes has both; its wants are held at the price they open at ({Offer}gp) and not raised",
            kind.Name,
            circle,
            _min[circle] + BotQuill.Margin,
            _mana[circle],
            offer
        );
    }

    internal static void Capped(int units, int over)
    {
        CappedAsks++;
        CappedGold += (long)Math.Max(1, units) * Math.Max(0, over);
    }

    private static void Cut(int units, int over)
    {
        CutOnRestore++;
        CutGold += (long)Math.Max(0, units) * Math.Max(0, over);
    }

    internal static void Held() => HeldAtPrice++;

    private static readonly Dictionary<int, IBotWilful> _claims = [];

    private static readonly Dictionary<int, long> _carrying = [];

    internal static void Claim(BotWant want, IBotWilful scribe)
    {
        if (want == null || scribe == null)
        {
            return;
        }

        if (_claims.Count > 1024)
        {
            _claims.Clear();
            _carrying.Clear();
        }

        _claims[want.Id] = scribe;
        _carrying.Remove(want.Id);
    }

    public static bool ClaimedByOther(BotWant want, IBotWilful scribe)
    {
        if (want == null || !_claims.TryGetValue(want.Id, out var holder) || ReferenceEquals(holder, scribe))
        {
            return false;
        }

        var resolve = holder?.Resolve;
        var deed = resolve?.Deed as BotInscribe;

        if (deed == null && resolve?.Paused is { Deed: BotInscribe paused } pause && Core.TickCount - pause.At < BotWill.AsideCapMs)
        {
            deed = paused;
        }

        if (holder?.Self is { Deleted: false })
        {
            if (deed != null && deed.Serves(want))
            {
                _carrying.Remove(want.Id);

                return true;
            }

            if (holder.Self.Alive && Carries(holder, want))
            {
                var now = Core.TickCount;

                if (!_carrying.TryGetValue(want.Id, out var since))
                {
                    _carrying[want.Id] = since = now;
                }

                if (now - since < BotWill.AsideCapMs)
                {
                    return true;
                }
            }
        }

        _claims.Remove(want.Id);
        _carrying.Remove(want.Id);

        return false;
    }

    private static bool Carries(IBotWilful bot, BotWant want)
    {
        var have = BotHandover.Carried(bot, want.Kind);

        if (have <= 0)
        {
            return false;
        }

        if (BotUnload.Keeps(bot).TryGetValue(want.Kind, out var allowed))
        {
            have = allowed >= int.MaxValue ? 0 : Math.Max(0, have - allowed);
        }

        return have >= want.Payable;
    }

    public static BotWant Order(IBotWilful scribe, Type kind)
    {
        if (kind == null || scribe?.Self == null || BotMarket.Wanted(scribe, kind) != null)
        {
            return null;
        }

        BotWant best = null;
        var bestServed = false;
        var bestNeed = false;
        var wants = BotMarket.Wants;

        for (var i = 0; i < wants.Count; i++)
        {
            var want = wants[i];

            if (want.Kind != kind || !want.IsOpen || ReferenceEquals(want.Buyer, scribe) || want.Buyer?.Self is not { Deleted: false } buyer)
            {
                continue;
            }

            if (ClaimedByOther(want, scribe))
            {
                Claimed++;

                continue;
            }

            if (!BotCourier.Answerable(scribe, want))
            {
                continue;
            }

            var served = BotPact.Serves(scribe.Self, buyer);
            var need = !BotTrading.Trader(buyer);

            if (best == null || served && !bestServed
                || served == bestServed && (need && !bestNeed || need == bestNeed && want.Offer > best.Offer))
            {
                best = want;
                bestServed = served;
                bestNeed = need;
            }
        }

        return best;
    }

    public static long Looks { get; private set; }

    public static long NoBook { get; private set; }

    public static long NoPaper { get; private set; }

    public static long Nothing { get; private set; }

    public static long ForLine { get; private set; }

    public static long ForOwn { get; private set; }

    public static long ForPractice { get; private set; }

    public static long Short { get; private set; }

    public static long Claimed { get; private set; }

    public static long UnderCost { get; private set; }

    public static long NoHerb { get; private set; }

    public static long Broke { get; private set; }

    public static long TookLine { get; private set; }

    public static long TookOwn { get; private set; }

    public static long TookPractice { get; private set; }

    public static long WroteLine { get; private set; }

    public static long WroteOwn { get; private set; }

    public static long WrotePractice { get; private set; }

    public static long Handed { get; private set; }

    public static long HandedByScribe { get; private set; }

    public static long KeptWhole { get; private set; }

    public static long HerbsBought { get; private set; }

    public static long OwnPen { get; private set; }

    private static readonly HashSet<(Serial, Type)> _leftToPen = [];

    internal static void Looked() => Looks++;

    internal static void Bookless() => NoBook++;

    internal static void Paperless() => NoPaper++;

    internal static void Idle() => Nothing++;

    internal static void Under() => UnderCost++;

    internal static void Herbless() => NoHerb++;

    internal static void Poor() => Broke++;

    internal static void Keeping() => KeptWhole++;

    internal static void Handover(BotWant want, int units, Mobile seller)
    {
        if (units <= 0 || !Writ(want?.Kind))
        {
            return;
        }

        Handed += units;

        if (seller is BotMobile scribe && Writes(scribe))
        {
            HandedByScribe += units;
        }
    }

    internal static void Offered(BotQuillPlan plan, bool shortOfIt)
    {
        if (plan?.For != null)
        {
            ForLine++;
        }
        else if (plan?.Own == true)
        {
            ForOwn++;
        }
        else
        {
            ForPractice++;
        }

        if (shortOfIt)
        {
            Short++;
        }
    }

    internal static void Took(BotQuillPlan plan)
    {
        if (plan?.For != null)
        {
            TookLine++;
        }
        else if (plan?.Own == true)
        {
            TookOwn++;
        }
        else
        {
            TookPractice++;
        }
    }

    internal static void Wrote(BotQuillPlan plan, int forLine, int scrolls)
    {
        if (scrolls <= 0)
        {
            return;
        }

        var line = Math.Clamp(forLine, 0, scrolls);

        WroteLine += line;

        if (plan?.Own == true)
        {
            WroteOwn += scrolls - line;
        }
        else
        {
            WrotePractice += scrolls - line;
        }
    }

    internal static void Bought(int herbs) => HerbsBought += Math.Max(0, herbs);

    internal static void LeftToPen(Mobile bot, Type kind)
    {
        if (bot == null || kind == null)
        {
            return;
        }

        if (_leftToPen.Count > 4096)
        {
            _leftToPen.Clear();
        }

        if (_leftToPen.Add((bot.Serial, kind)))
        {
            OwnPen++;
        }
    }

    public static string Describe()
    {
        Census();

        List<string> census = [];

        for (var c = BotGrimoire.ShopCircles + 1; c <= Circles; c++)
        {
            census.Add(_able[c] == 0 ? $"circle {c} none" : $"circle {c} {_able[c]} (best {_best[c]:P0})");
        }

        return $"scribes able to write: {string.Join(", ", census)}; "
               + $"{Looks} looks by bots with a pen: {NoBook} with no book, {NoPaper} with no counter for paper, {Nothing} with nothing it could write, "
               + $"{ForLine} sessions offered for somebody's want, {ForOwn} for its own book or scrolls to throw, {ForPractice} for practice ({Short} of them dearer than what it had free); "
               + $"funded wants passed over by a scribe able to write them: {Claimed} for another scribe writing it, {UnderCost} for paying under its cost at that scribe's chance, {NoHerb} for a herb the paper's counter does not sell, {Broke} for paper and herbs dearer than the scribe had free, {KeptWhole} for a kind it keeps whole (all counted per look); "
               + $"sessions taken: {TookLine} for a want, {TookOwn} own, {TookPractice} practice; scrolls written: {WroteLine} for a want and over the writer's own keep, {WroteOwn} own, {WrotePractice} practice, {HerbsBought} herbs bought for them; {Handed} scrolls no shelf sells handed over against wants, {HandedByScribe} of them by a bot that writes; "
               + $"{OwnPen} scrolls (bot and kind) left to the asker's own pen rather than said as a want, for costing it less; "
               + $"scroll wants at the price of writing them: {CappedAsks} opened at it instead of above ({CappedGold}gp less asked), {CutOnRestore} cut to it when read back ({CutGold}gp), {HeldAtPrice} raises not taken at it, {Beyond} said for a circle nobody here can write";
    }

    public static void Forget()
    {
        _claims.Clear();
        _carrying.Clear();
        _saidBeyond.Clear();
        _leftToPen.Clear();
        _recipes.Clear();
        _censused = false;
        _capped = false;
        _scanned = 0;
        CappedAsks = 0;
        CappedGold = 0;
        CutOnRestore = 0;
        CutGold = 0;
        HeldAtPrice = 0;
        Beyond = 0;
        Looks = 0;
        NoBook = 0;
        NoPaper = 0;
        Nothing = 0;
        ForLine = 0;
        ForOwn = 0;
        ForPractice = 0;
        Short = 0;
        Claimed = 0;
        UnderCost = 0;
        NoHerb = 0;
        Broke = 0;
        TookLine = 0;
        TookOwn = 0;
        TookPractice = 0;
        WroteLine = 0;
        Handed = 0;
        HandedByScribe = 0;
        KeptWhole = 0;
        WroteOwn = 0;
        WrotePractice = 0;
        HerbsBought = 0;
        OwnPen = 0;
    }
}
