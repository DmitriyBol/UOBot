using System;
using System.Collections.Generic;
using Server.Engines.Craft;
using Server.Items;
using Server.Mobiles;

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

    public static bool Stocked(Mobile bot, CraftItem recipe) => Stocked(bot, recipe, Reserve);

    public static bool Stocked(Mobile bot, CraftItem recipe, int reserve)
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

            if (pack.GetAmount(res.ItemType) < Math.Max(1, res.Amount) + Math.Max(0, reserve))
            {
                return false;
            }
        }

        return true;
    }

    public static double Chance(Mobile bot, CraftItem recipe)
    {
        if (bot == null || recipe == null || System == null)
        {
            return 0.0;
        }

        var chance = recipe.GetSuccessChance(bot, null, System, false, out var able);

        return able ? Math.Clamp(chance, 0.0, 1.0) : 0.0;
    }

    private static bool Supply(
        Mobile bot, BaseVendor shop, CraftItem recipe, int units, double chance, int paper, out int attempts, out int blanks,
        out List<(Type Herb, int Amount)> herbs, out int outlay
    )
    {
        attempts = Math.Clamp((int)Math.Ceiling(Math.Max(1, units) / Math.Max(0.05, chance)), 1, Math.Max(1, BotInscribe.Batch));
        blanks = Math.Max(0, attempts - Blanks(bot));
        outlay = blanks * Math.Max(0, paper);
        herbs = null;

        var pack = bot?.Backpack;
        var resources = recipe?.Resources;

        if (pack == null || resources == null)
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

            var lacking = attempts * Math.Max(1, res.Amount) - pack.GetAmount(res.ItemType);

            if (lacking <= 0)
            {
                continue;
            }

            var price = shop == null ? 0 : BotShops.Price(shop, res.ItemType);

            if (price <= 0)
            {
                return false;
            }

            herbs ??= [];
            herbs.Add((res.ItemType, lacking));
            outlay += lacking * price;
        }

        return true;
    }

    public static bool OwnPen(IBotWilful wilful, Type kind)
    {
        var bot = wilful?.Self;

        if (bot == null || System == null || Pen(bot) == null || BotGrimoire.Book(bot) == null)
        {
            return false;
        }

        var recipe = BotScribes.Recipe(kind);
        var spell = BotGrimoire.SpellOf(kind);

        if (recipe == null || spell < 0 || Requirement(recipe) > bot.Skills[SkillName.Inscribe].Value - Margin || recipe.Mana > bot.ManaMax)
        {
            return false;
        }

        var chance = Chance(bot, recipe);

        if (chance <= 0.0)
        {
            return false;
        }

        var shop = BotShops.Nearest(wilful, typeof(BlankScroll));
        var paper = shop == null ? 0 : BotShops.Price(shop, typeof(BlankScroll));

        if (paper <= 0 || !Supply(bot, shop, recipe, 1, chance, paper, out _, out _, out _, out var outlay) || BotYield.Spendable(bot) < outlay)
        {
            return false;
        }

        var offer = BotMarket.Worth(kind, BotGrimoire.ShopPrice(BotGrimoire.Circle(spell)));

        return Cost(bot, recipe, paper) / chance <= offer;
    }

    public static BotQuillPlan Plan(IBotWilful wilful, BaseVendor shop, int paper)
    {
        var bot = wilful?.Self;
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

        var gap = OwnGap(bot);
        var kept = BotArmoury.Kept(bot, out _);
        var keptShort = kept == null ? 0 : Math.Max(0, BotArmoury.Stock - pack.GetAmount(kept));

        var keep = BotUnload.Keeps(wilful);

        BotQuillPlan line = null;
        BotQuillPlan own = null;

        var free = BotYield.Spendable(bot);

        CraftItem best = null;
        var bestNeeds = -1.0;
        var bestWorth = 0;
        Type bestKind = null;

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

            var funded = BotMarket.Best(kind) > 0;
            var mine = kind == gap || kind == kept && keptShort > 0;

            if (!funded && !mine && best != null && needs < bestNeeds)
            {
                continue;
            }

            if (DefInscription.Knows?.Invoke(bot, spell) != true && book?.HasSpell(spell) != true)
            {
                continue;
            }

            var chance = Chance(bot, recipe);

            if (chance <= 0.0)
            {
                continue;
            }

            var cost = Cost(bot, recipe, paper);
            var asking = Math.Max(BotMarket.Worth(kind, BotGrimoire.ShopPrice(BotGrimoire.Circle(spell))), Asking(cost));

            if (funded && BotScribes.Order(wilful, kind) is { } want && want.Offer > (line?.For?.Offer ?? 0)
                && !BotCourier.Holding(wilful, want))
            {
                keep.TryGetValue(kind, out var allowed);
                var units = allowed >= int.MaxValue
                    ? 0
                    : want.Payable + Math.Max(0, allowed - BotHandover.Carried(wilful, kind));

                if (units <= 0)
                {
                    BotScribes.Keeping();
                }
                else if (cost > 0 && want.Offer < cost / chance)
                {
                    BotScribes.Under();
                }
                else if (!Supply(bot, shop, recipe, units, chance, paper, out var attempts, out var blanks, out var herbs, out var outlay))
                {
                    BotScribes.Herbless();
                }
                else if (outlay > free)
                {
                    BotScribes.Poor();
                }
                else
                {
                    line = new BotQuillPlan
                    {
                        Recipe = recipe, Kind = kind, Worth = Math.Max(asking, want.Offer), For = want, Units = units,
                        Chance = chance, Attempts = attempts, Paper = blanks, Herbs = herbs, Outlay = outlay
                    };
                }
            }

            if (mine && own == null && (cost <= 0 || cost / chance <= BotMarket.Worth(kind, BotGrimoire.ShopPrice(BotGrimoire.Circle(spell))))
                && Supply(bot, shop, recipe, 1, chance, paper, out var tries, out var sheets, out var stock, out var bill) && bill <= free)
            {
                own = new BotQuillPlan
                {
                    Recipe = recipe, Kind = kind, Worth = asking, Own = true, Units = 1, Chance = chance, Attempts = tries,
                    Paper = sheets, Herbs = stock, Outlay = bill
                };
            }

            if (best != null && needs < bestNeeds || !Stocked(bot, recipe))
            {
                continue;
            }

            if (cost > 0 && BotMarket.Worth(kind, BotGrimoire.ShopPrice(BotGrimoire.Circle(spell))) < cost)
            {
                continue;
            }

            if (best != null && !Better(needs, asking, bestNeeds, bestWorth))
            {
                continue;
            }

            best = recipe;
            bestNeeds = needs;
            bestWorth = asking;
            bestKind = kind;
        }

        if (line != null || own != null)
        {
            return line ?? own;
        }

        if (best == null)
        {
            return null;
        }

        var practicePaper = Blanks(bot) > 0 ? 0 : Math.Max(1, BotInscribe.Batch);

        return new BotQuillPlan
        {
            Recipe = best, Kind = bestKind, Worth = bestWorth, Chance = Chance(bot, best), Attempts = Math.Max(1, BotInscribe.Batch),
            Paper = practicePaper, Outlay = practicePaper * Math.Max(0, paper)
        };
    }

    private static Type OwnGap(Mobile bot)
    {
        var spell = BotGrimoire.Missing(bot);

        if (spell < 0 || spell == BotRaise.Spell && BotRaise.KeepsScroll(bot))
        {
            return null;
        }

        var kind = BotGrimoire.ScrollFor(spell);

        return kind == null || Held(bot, kind) > 0 ? null : kind;
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

/// <summary>
/// What one session at the pen is for, and what it has to buy before it starts. Made by <see cref="BotQuill.Plan"/>, carried by
/// <see cref="BotInscribe"/> from the proposal to the last scroll, so the work writes what it was chosen for.
/// </summary>
public sealed class BotQuillPlan
{
    public CraftItem Recipe { get; init; }

    public Type Kind { get; init; }

    public int Worth { get; init; }

    public BotWant For { get; init; }

    public bool Own { get; init; }

    public int Units { get; init; }

    public double Chance { get; init; }

    public int Attempts { get; init; }

    public int Paper { get; init; }

    public List<(Type Herb, int Amount)> Herbs { get; init; }

    public int Outlay { get; init; }

    public bool Order => For != null || Own;

    public bool Buys => Paper > 0 || Herbs is { Count: > 0 };
}
