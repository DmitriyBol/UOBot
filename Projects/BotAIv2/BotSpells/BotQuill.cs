using System;
using System.Collections.Generic;
using Server.Engines.Craft;
using Server.Items;

namespace Server.BotAI.V2;

/// <summary>
/// Writing scrolls: what can be written, what it takes, and the attempt itself.
///
/// <para>
/// <b>Everything goes through the shard's own <see cref="CraftSystem"/></b>, exactly as sewing does — the same
/// call a player's craft window makes. The skill check, the failure, the blank and the herbs burnt on a bad
/// attempt and the scroll that appears are all the engine's, so the Inscribe a scribe gains here is gained by
/// inscribing rather than by being credited.
/// </para>
///
/// <para>
/// <b>It is the same shape as the needle and it needs no place either.</b> A pen works where the bot is
/// standing, so this is the second trade rather than the third: a smith still needs a forge. What it needs
/// instead is mana — four for the first circle and fifty for the eighth, which at fifty Intelligence is a
/// mage's entire pool — and that is what makes Meditation on a mage's own vector something other than
/// decoration.
/// </para>
///
/// <para>
/// <b>And unlike cloth, what comes off it has no shopkeeper above the third circle.</b> That is the whole
/// reason this trade is worth adding: it is the first work in the project whose output only another bot can
/// buy.
/// </para>
/// </summary>
public static class BotQuill
{
    public static double Margin { get; set; } = 5.0;

    public static double Markup { get; set; } = 0.20;

    public static int Reserve { get; set; } = 10;

    public static int HerbGuess { get; set; } = 3;

    public static CraftSystem System => DefInscription.CraftSystem;

    private static readonly Dictionary<Type, int> _herbage = [];

    public static int Herb(Mobile bot, Type kind)
    {
        if (kind == null)
        {
            return 0;
        }

        if (_herbage.TryGetValue(kind, out var known))
        {
            return known;
        }

        var shop = BotShops.Nearest(bot, kind);
        var price = shop == null ? 0 : BotShops.Price(shop, kind);

        if (price <= 0)
        {
            return HerbGuess;
        }

        _herbage[kind] = price;

        return price;
    }

    public static int Cost(Mobile bot, CraftItem recipe, int paper)
    {
        var resources = recipe?.Resources;

        if (resources == null)
        {
            return 0;
        }

        var bill = Math.Max(0, paper);

        for (var i = 0; i < resources.Count; i++)
        {
            var res = resources[i];

            if (res.ItemType == null || res.ItemType == typeof(BlankScroll))
            {
                continue;
            }

            bill += Math.Max(1, res.Amount) * Herb(bot, res.ItemType);
        }

        return bill;
    }

    public static int Asking(int cost) => cost <= 0 ? 0 : (int)Math.Ceiling(cost * (1.0 + Markup));

    public static ScribesPen Pen(Mobile bot) => BotOutfit.Oldest<ScribesPen>(bot?.Backpack);

    public static int Blanks(Mobile bot) => bot?.Backpack?.GetAmount(typeof(BlankScroll)) ?? 0;

    public static double Requirement(CraftItem recipe)
    {
        var skills = recipe?.Skills;

        if (skills == null)
        {
            return 0.0;
        }

        for (var i = 0; i < skills.Count; i++)
        {
            if (skills[i].SkillToMake == SkillName.Inscribe)
            {
                return skills[i].MinSkill;
            }
        }

        return 0.0;
    }

    public static bool Stocked(Mobile bot, CraftItem recipe)
    {
        var pack = bot?.Backpack;
        var resources = recipe?.Resources;

        if (pack == null || resources == null || resources.Count == 0)
        {
            return false;
        }

        for (var i = 0; i < resources.Count; i++)
        {
            var res = resources[i];

            if (res.ItemType == null || res.ItemType == typeof(BlankScroll))
            {
                continue;
            }

            if (pack.GetAmount(res.ItemType) < Math.Max(1, res.Amount) + Math.Max(0, Reserve))
            {
                return false;
            }
        }

        return true;
    }

    public static CraftItem Choose(Mobile bot, out Type scroll, out int worth) =>
        Choose(bot, 0, out scroll, out worth);

    public static CraftItem Choose(Mobile bot, int paper, out Type scroll, out int worth)
    {
        scroll = null;
        worth = 0;

        var system = System;
        var pack = bot?.Backpack;

        if (system == null || pack == null)
        {
            return null;
        }

        var skill = bot.Skills[SkillName.Inscribe].Value;
        var pool = bot.ManaMax;
        var recipes = system.CraftItems;

        var book = BotGrimoire.Book(bot);

        CraftItem best = null;
        var bestNeeds = -1.0;
        var bestWorth = 0;

        CraftItem asked = null;
        var bestBid = 0;
        Type askedKind = null;
        var askedWorth = 0;

        for (var i = 0; i < recipes.Count; i++)
        {
            var recipe = recipes[i];
            var kind = recipe.ItemType;
            var spell = BotGrimoire.SpellOf(kind);

            if (spell < 0)
            {
                continue;
            }

            var needs = Requirement(recipe);

            if (needs > skill - Margin || recipe.Mana > pool)
            {
                continue;
            }

            var bid = BotAuction.Best(kind);

            if (bid <= 0 && best != null && needs < bestNeeds)
            {
                continue;
            }

            if (bid > 0 && bid <= bestBid)
            {
                continue;
            }

            if (!Stocked(bot, recipe))
            {
                continue;
            }

            if (DefInscription.Knows?.Invoke(bot, spell) != true && book?.HasSpell(spell) != true)
            {
                continue;
            }

            var asking = BotAuction.Worth(kind, BotGrimoire.ShopPrice(BotGrimoire.Circle(spell)));

            var cost = Cost(bot, recipe, paper);
            var revenue = Math.Max(asking, bid);

            if (cost > 0 && revenue < cost)
            {
                continue;
            }

            asking = Math.Max(asking, Asking(cost));

            if (bid > 0)
            {
                asked = recipe;
                bestBid = bid;
                askedKind = kind;
                askedWorth = asking;

                continue;
            }

            if (best != null && !Better(needs, asking, bestNeeds, bestWorth))
            {
                continue;
            }

            best = recipe;
            bestNeeds = needs;
            bestWorth = asking;

            scroll = kind;
            worth = asking;
        }

        if (asked == null)
        {
            return best;
        }

        scroll = askedKind;
        worth = askedWorth;

        return asked;
    }

    private static bool Better(double needs, int worth, double bestNeeds, int bestWorth)
    {
        if (needs > bestNeeds)
        {
            return true;
        }

        if (needs < bestNeeds)
        {
            return false;
        }

        return worth > bestWorth;
    }

    public static bool Swing(Mobile bot, CraftItem recipe, BaseTool pen)
    {
        if (bot == null || recipe == null || pen == null || System == null)
        {
            return false;
        }

        recipe.Craft(bot, System, null, pen);

        return true;
    }

    public static int Held(Mobile bot, Type kind) =>
        kind == null ? 0 : bot?.Backpack?.GetAmount(kind, true) ?? 0;

    public static List<SpellScroll> Gather(Mobile bot, Type kind)
    {
        List<SpellScroll> made = [];

        var pack = bot?.Backpack;

        if (pack == null || kind == null)
        {
            return made;
        }

        List<Item> carried = [.. pack.Items];

        for (var i = 0; i < carried.Count; i++)
        {
            if (carried[i] is SpellScroll scroll && !scroll.Deleted && scroll.Movable && kind.IsInstanceOfType(scroll))
            {
                made.Add(scroll);
            }
        }

        return made;
    }
}
