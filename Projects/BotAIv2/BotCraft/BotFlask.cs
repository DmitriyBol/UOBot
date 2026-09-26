using System;
using System.Collections.Generic;
using Server.Engines.Craft;
using Server.Items;
using Server.Logging;

namespace Server.BotAI.V2;

/// <summary>
/// Brewing: what a potion is made of, and the swing that makes it.
///
/// <para>
/// <b>The whole trade was scaffolded and never built.</b> Three classes on this shard — Healer, Mage and
/// Sage — ask for Alchemy at a hundred; <see cref="BotOutfit"/> hands every one of them a mortar and pestle
/// on the strength of that skill; <c>BotUnload</c> keeps their empty glass out of the market because "glass
/// is stock to an alchemist"; and the word <c>DefAlchemy</c> appeared nowhere in the assembly. The tool was
/// issued, the glass was protected and the skill was trained for a trade that did not exist. Meanwhile the
/// only potion on the island came off an alchemist's shelf at fifteen gold, and on the evening of 27.08.2026
/// two hundred and twenty-one of two hundred and fifty-two moments at death's door were a bot reaching for
/// a bottle it did not have.
/// </para>
///
/// <para>
/// <b>Two materials, which is what makes this its own file rather than another caller of
/// <see cref="BotCraftwork"/>.</b> Every recipe in <c>DefAlchemy</c> declares a reagent and a
/// <see cref="Bottle"/> — <c>AddRes(index, typeof(Bottle), …)</c> on every single one — and
/// <c>BotCraftwork.Simple</c> refuses any recipe with more than one resource, by design and for good
/// reasons on the trades it serves. So the arithmetic of "can I make one" is done here, over both halves.
/// </para>
///
/// <para>
/// <b>The glass is the half that comes back.</b> A drunk potion leaves its bottle in the pack — the engine
/// does it, <c>BasePotion.Drink</c> — so a population that drinks is a population that produces the second
/// material of its own supply. The alchemist's counter sells the rest at five gold a hundred, which makes
/// glass the cheap half and the reagent the dear one, the opposite way round from every other trade here.
/// </para>
/// </summary>
public static class BotFlask
{
    public static CraftSystem System => DefAlchemy.CraftSystem;

    public static BaseTool Kit(Mobile bot) => BotOutfit.Oldest<MortarPestle>(bot?.Backpack);

    public static int Herbs { get; set; } = 5;

    public static double Margin { get; set; } = 5.0;

    public static int LeastBottles { get; set; } = 5;

    public static int Batch { get; set; } = 20;

    public static int Cap { get; set; } = 5;

    public static int RestMs { get; set; } = 600000;

    public static long Capped { get; private set; }

    public static long Rests { get; private set; }

    private static readonly Dictionary<(Serial Who, Type What), long> _resting = new();

    public static int Held(IBotWilful bot, Mobile body, Type kind)
    {
        if (kind == null)
        {
            return 0;
        }

        var shelved = BotAuction.Find(bot, kind);

        return Amount(body, kind) + (shelved is { IsEmpty: false } ? shelved.Amount : 0);
    }

    public static bool AtCap(IBotWilful will, Mobile bot)
    {
        if (bot == null || System == null)
        {
            return false;
        }

        var families = BotArsenal.Draughts;
        var makeable = 0;

        for (var i = 0; i < families.Count; i++)
        {
            var kind = BotArsenal.Potion(families[i]);

            if (kind == null || Recipe(bot, kind) == null)
            {
                continue;
            }

            makeable++;

            if (!Resting(bot, kind) && Held(will, bot, kind) < Cap)
            {
                return false;
            }
        }

        return makeable > 0;
    }

    public static bool Resting(Mobile body, Type kind)
    {
        if (body == null || kind == null || !_resting.TryGetValue((body.Serial, kind), out var until))
        {
            return false;
        }

        if (Core.TickCount - until < 0)
        {
            return true;
        }

        _resting.Remove((body.Serial, kind));

        return false;
    }

    public static void Rest(Mobile body, Type kind)
    {
        if (body == null || kind == null || Resting(body, kind))
        {
            return;
        }

        if (_resting.Count > 512)
        {
            Sweep();
        }

        _resting[(body.Serial, kind)] = Core.TickCount + RestMs;
        Rests++;
    }

    private static void Sweep()
    {
        List<(Serial, Type)> lapsed = [];

        foreach (var (key, until) in _resting)
        {
            if (Core.TickCount - until >= 0)
            {
                lapsed.Add(key);
            }
        }

        for (var i = 0; i < lapsed.Count; i++)
        {
            _resting.Remove(lapsed[i]);
        }
    }

    public static void Forget()
    {
        _resting.Clear();
        Capped = 0;
        Rests = 0;
    }

    public static int Worth { get; set; } = 14;

    public static int Amount(Mobile bot, Type stuff) =>
        stuff == null ? 0 : bot?.Backpack?.GetAmount(stuff) ?? 0;

    public static int Bottles(Mobile bot) => Amount(bot, typeof(Bottle));

    public static bool Twofold(CraftItem recipe, out Type reagent, out int units, out int glass)
    {
        reagent = null;
        units = 0;
        glass = 0;

        var resources = recipe?.Resources;

        if (resources == null || resources.Count != 2 || recipe.ItemType == null)
        {
            return false;
        }

        for (var i = 0; i < resources.Count; i++)
        {
            var res = resources[i];

            if (res.ItemType == typeof(Bottle))
            {
                glass = Math.Max(1, res.Amount);

                continue;
            }

            reagent = res.ItemType;
            units = Math.Max(1, res.Amount);
        }

        return reagent != null && glass > 0;
    }

    public static (Type Reagent, int Units, int Glass) Costs(CraftItem recipe) =>
        Twofold(recipe, out var reagent, out var units, out var glass) ? (reagent, units, glass) : (null, 0, 0);

    public static IReadOnlyList<Type> Needs
    {
        get
        {
            if (_needs is { Count: > 0 })
            {
                return _needs;
            }

            return _needs = Wanted();
        }
    }

    private static IReadOnlyList<Type> _needs;

    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotFlask));

    private static bool _saidNeeds;

    /// <summary>
    /// Why the last look at the mortar found nothing, split apart because one bucket was holding three
    /// answers.
    ///
    /// <para>
    /// <b>"Had the glass but no herbs" was everything Choose refused for.</b> It refuses on three quite
    /// different grounds — the skill will not carry any draught, the reagent is not in the pack, or the
    /// recipe table has nothing that fits — and the alchemist wrote all three into one number. Measured on
    /// 05.09.2026 the share of new asks landing there rose from 14% to 37% inside one run *after* the
    /// reagents were fixed, which is a time pattern rather than a supply one and could not be read at all
    /// while the three were together.
    /// </para>
    /// </summary>
    public enum Refusal
    {
        None,

        Unskilled,

        Reagentless,

        Full
    }

    public static Refusal Why { get; private set; }

    private static IReadOnlyList<Type> Wanted()
    {
        List<Type> want = [];
        var families = BotArsenal.Draughts;

        var system = System;
        var recipes = system?.CraftItems;

        if (recipes == null)
        {
            return want;
        }

        for (var i = 0; i < families.Count; i++)
        {
            var kind = BotArsenal.Potion(families[i]);

            for (var j = 0; j < recipes.Count; j++)
            {
                if (recipes[j].ItemType != kind || !Twofold(recipes[j], out var reagent, out _, out _))
                {
                    continue;
                }

                if (!want.Contains(reagent))
                {
                    want.Add(reagent);
                }

                break;
            }
        }

        if (!_saidNeeds && want.Count > 0)
        {
            _saidNeeds = true;

            logger.Information(
                "A brewer is sent after {Count} reagents, {Herbs} of each, read off the recipes: {Names}",
                want.Count,
                Herbs,
                string.Join(", ", want.ConvertAll(t => t.Name))
            );
        }

        return want;
    }

    public static double Requirement(CraftItem recipe)
    {
        var skills = recipe?.Skills;

        return skills == null || skills.Count == 0 ? 0.0 : skills[0].MinSkill;
    }

    public static CraftItem Recipe(Mobile bot, Type wanted)
    {
        var system = System;

        if (bot == null || system == null || wanted == null)
        {
            return null;
        }

        var able = bot.Skills[SkillName.Alchemy].Value;
        var recipes = system.CraftItems;

        for (var i = 0; i < recipes.Count; i++)
        {
            var recipe = recipes[i];

            if (recipe.ItemType != wanted || !Twofold(recipe, out var reagent, out var units, out var glass))
            {
                continue;
            }

            if (Requirement(recipe) > able - Margin)
            {
                continue;
            }

            if (Amount(bot, reagent) < units || Bottles(bot) < glass)
            {
                continue;
            }

            return recipe;
        }

        return null;
    }

    public static CraftItem Choose(IBotWilful will, Mobile bot, out Type made)
    {
        made = null;
        Why = Refusal.None;

        if (bot == null || System == null)
        {
            return null;
        }

        var anySkilled = false;
        var anyFull = false;

        CraftItem best = null;
        var bestStock = 0;

        CraftItem asked = null;
        var bestBid = 0;
        Type askedKind = null;

        var families = BotArsenal.Draughts;

        for (var i = 0; i < families.Count; i++)
        {
            var kind = BotArsenal.Potion(families[i]);
            var recipe = Recipe(bot, kind);

            if (recipe == null)
            {
                continue;
            }

            anySkilled = true;

            if (Resting(bot, kind))
            {
                Capped++;
                anyFull = true;

                continue;
            }

            if (Held(will, bot, kind) >= Cap)
            {
                Capped++;
                anyFull = true;
                Rest(bot, kind);

                continue;
            }

            var bid = BotAuction.Best(kind);

            if (bid > bestBid)
            {
                bestBid = bid;
                asked = recipe;
                askedKind = kind;
            }

            var (reagent, units, _) = Costs(recipe);
            var stock = units <= 0 ? 0 : Amount(bot, reagent) / units;

            if (stock <= bestStock)
            {
                continue;
            }

            bestStock = stock;
            best = recipe;
            made = kind;
        }

        if (best == null && asked == null)
        {
            Why = anyFull ? Refusal.Full : anySkilled ? Refusal.Reagentless : Refusal.Unskilled;
        }

        if (asked == null)
        {
            return best;
        }

        made = askedKind;

        return asked;
    }

    public static int Possible(Mobile bot, CraftItem recipe)
    {
        var (reagent, units, glass) = Costs(recipe);

        if (reagent == null || units <= 0 || glass <= 0)
        {
            return 0;
        }

        return Math.Min(Amount(bot, reagent) / units, Bottles(bot) / glass);
    }

    public static Type Likeliest(IBotWilful will, Mobile bot)
    {
        var system = System;

        if (bot == null || system == null)
        {
            return null;
        }

        var able = bot.Skills[SkillName.Alchemy].Value;
        var recipes = system.CraftItems;
        var families = BotArsenal.Draughts;

        Type best = null;
        var bestStock = -1;

        for (var f = 0; f < families.Count; f++)
        {
            var kind = BotArsenal.Potion(families[f]);

            if (kind == null)
            {
                continue;
            }

            if (Resting(bot, kind) || Held(will, bot, kind) >= Cap)
            {
                continue;
            }

            for (var i = 0; i < recipes.Count; i++)
            {
                var recipe = recipes[i];

                if (recipe.ItemType != kind || !Twofold(recipe, out var reagent, out var units, out _))
                {
                    continue;
                }

                if (Requirement(recipe) > able - Margin)
                {
                    continue;
                }

                var stock = units <= 0 ? 0 : Amount(bot, reagent) / units;

                if (stock > bestStock)
                {
                    bestStock = stock;
                    best = kind;
                }

                break;
            }
        }

        return bestStock > 0 ? best : null;
    }

    public static bool Swing(Mobile bot, CraftItem recipe, Type reagent, BaseTool tool) =>
        BotCraftwork.Swing(bot, System, recipe, reagent, tool);

    public static int Made(Mobile bot, Type kind) => BotCraftwork.Made(bot, kind);
}
