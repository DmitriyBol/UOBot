using System;
using System.Collections.Generic;
using Server.Engines.Craft;
using Server.Items;

namespace Server.BotAI.V2;

/// <summary>
/// Sewing: what can be made out of cloth, and the swing that makes it.
///
/// <para>
/// <b>Everything goes through the shard's own <see cref="CraftSystem"/></b> — the same call a player's craft
/// window makes. The skill check, the failure, the material burnt on a bad attempt and the item that appears
/// are all the engine's, which is what makes the gain real: a tailor here trains Tailoring by tailoring,
/// not by being credited.
/// </para>
///
/// <para>
/// <b>Tailoring is the trade to start a crafter on because it needs no place.</b> Smithing wants a forge and
/// an anvil, so a smith without a workshop is a bot with an opinion about metal; a sewing kit works wherever
/// the bot is standing. That is the whole reason the first crafting chain is cloth and not ore.
/// </para>
/// </summary>
public static class BotThread
{
    public static double Margin { get; set; } = 5.0;

    public static CraftSystem System => DefTailoring.CraftSystem;

    public static SewingKit Kit(Mobile bot) => bot?.Backpack?.FindItemByType<SewingKit>();

    public static int Amount(Mobile bot, Type stuff) =>
        stuff == null ? 0 : bot?.Backpack?.GetAmount(stuff) ?? 0;

    public static int Units(CraftItem recipe)
    {
        var resources = recipe?.Resources;

        return resources == null || resources.Count == 0 ? 1 : Math.Max(1, resources[0].Amount);
    }

    public static CraftItem Recipe(Mobile bot, Type material, Type wanted) =>
        BotCraftwork.Recipe(bot, System, SkillName.Tailoring, material, wanted);

    public static CraftItem Choose(Mobile bot, Type material) => Choose(bot, material, -1);

    public static CraftItem Choose(Mobile bot, Type material, int budget)
    {
        var system = System;

        if (bot == null || system == null || material == null)
        {
            return null;
        }

        var skill = bot.Skills[SkillName.Tailoring].Value;
        var recipes = system.CraftItems;

        var held = budget >= 0 ? budget : bot.Backpack?.GetAmount(material) ?? 0;

        CraftItem best = null;
        var bestNeeds = -1.0;

        CraftItem asked = null;
        var bestBid = 0;

        for (var i = 0; i < recipes.Count; i++)
        {
            var recipe = recipes[i];

            if (!Simple(recipe, material))
            {
                continue;
            }

            var needs = Requirement(recipe);

            if (needs > skill - Margin)
            {
                continue;
            }

            if (BotCraftwork.Cost(recipe) > held)
            {
                continue;
            }

            var bid = BotAuction.Best(recipe.ItemType);

            if (bid > bestBid)
            {
                bestBid = bid;
                asked = recipe;
            }

            if (needs > bestNeeds)
            {
                best = recipe;
                bestNeeds = needs;
            }
        }

        return asked ?? best;
    }

    public static bool Simple(CraftItem recipe, Type material)
    {
        var resources = recipe?.Resources;

        if (resources == null || resources.Count != 1 || recipe.ItemType == null)
        {
            return false;
        }

        var only = resources[0];

        return only.ItemType == material && only.Amount > 0;
    }

    public static double Requirement(CraftItem recipe)
    {
        var skills = recipe?.Skills;

        if (skills == null)
        {
            return 0.0;
        }

        for (var i = 0; i < skills.Count; i++)
        {
            if (skills[i].SkillToMake == SkillName.Tailoring)
            {
                return skills[i].MinSkill;
            }
        }

        return 0.0;
    }

    public static bool Swing(Mobile bot, CraftItem recipe, Type material, BaseTool tool)
    {
        if (bot == null || recipe == null || material == null || tool == null || System == null)
        {
            return false;
        }

        recipe.Craft(bot, System, material, tool);

        return true;
    }

    public static int Made(Mobile bot, Type kind) =>
        kind == null ? 0 : bot?.Backpack?.GetAmount(kind, true) ?? 0;

    public static List<Item> Gather(Mobile bot, Type kind)
    {
        List<Item> made = [];

        var pack = bot?.Backpack;

        if (pack == null || kind == null)
        {
            return made;
        }

        List<Item> carried = [.. pack.Items];

        for (var i = 0; i < carried.Count; i++)
        {
            var item = carried[i];

            if (!item.Deleted && item.Movable && kind.IsInstanceOfType(item))
            {
                made.Add(item);
            }
        }

        return made;
    }
}
