using System;
using System.Collections.Generic;
using Server.Engines.Craft;
using Server.Items;
using Server.Logging;

namespace Server.BotAI.V2;

/// <summary>
/// The part of making things that is the same whatever is being made.
///
/// <para>
/// <b>Lifted out of the tailor when the smith arrived, rather than copied.</b> Choosing the hardest recipe a
/// bot can still manage, reading a recipe's skill requirement, refusing anything that needs a second
/// material, taking a swing, counting what is actually in the pack afterwards — none of that knows or cares
/// whether the material is cloth or iron. Written twice it would be two sets of the same four decisions, and
/// this project already has the note about what that costs: a second list of the same facts is a list that
/// disagrees with the first one the day somebody edits either.
/// </para>
///
/// <para>
/// What stays with each trade is what genuinely differs: which craft system, which skill, which tool, which
/// material. Those are arguments here and properties on <see cref="BotThread"/> and <see cref="BotAnvil"/>.
/// </para>
/// </summary>
public static class BotCraftwork
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotCraftwork));

    public static double Margin { get; set; } = 5.0;

    public static double LeastChance { get; set; } = 0.35;

    public static double CookingLeastChance { get; set; } = 0.15;

    public static long Unlikely { get; private set; }

    private static bool Likely(Mobile bot, CraftSystem system, CraftItem recipe, Type material)
    {
        var least = system?.MainSkill == SkillName.Cooking ? CookingLeastChance : LeastChance;

        if (least <= 0.0)
        {
            return true;
        }

        var chance = recipe.GetSuccessChance(bot, material, system, false, out var all);

        if (all && chance >= least)
        {
            return true;
        }

        Unlikely++;

        return false;
    }

    public static CraftItem Choose(Mobile bot, CraftSystem system, SkillName skill, Type material, int stock = 0)
    {
        if (bot == null || system == null || material == null)
        {
            return null;
        }

        var able = bot.Skills[skill].Value;
        var recipes = system.CraftItems;

        CraftItem best = null;
        var bestNeeds = -1.0;

        for (var i = 0; i < recipes.Count; i++)
        {
            var recipe = recipes[i];

            if (!Simple(recipe, material))
            {
                continue;
            }

            var needs = Requirement(recipe, skill);

            if (needs > able - Margin || needs <= bestNeeds)
            {
                continue;
            }

            if (stock > 0 && Cost(recipe) > stock)
            {
                continue;
            }

            if (!Likely(bot, system, recipe, material))
            {
                continue;
            }

            best = recipe;
            bestNeeds = needs;
        }

        return best;
    }

    public static CraftItem Recipe(Mobile bot, CraftSystem system, SkillName skill, Type material, Type wanted)
    {
        if (bot == null || system == null || wanted == null || material == null)
        {
            return null;
        }

        var able = bot.Skills[skill].Value;
        var recipes = system.CraftItems;

        for (var i = 0; i < recipes.Count; i++)
        {
            var recipe = recipes[i];

            if (recipe.ItemType != wanted || !Simple(recipe, material))
            {
                continue;
            }

            return Requirement(recipe, skill) <= able - Margin && Likely(bot, system, recipe, material) ? recipe : null;
        }

        return null;
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

    public static double Requirement(CraftItem recipe, SkillName skill)
    {
        var skills = recipe?.Skills;

        if (skills == null)
        {
            return 0.0;
        }

        for (var i = 0; i < skills.Count; i++)
        {
            if (skills[i].SkillToMake == skill)
            {
                return skills[i].MinSkill;
            }
        }

        return 0.0;
    }

    public static int Cost(CraftItem recipe)
    {
        var resources = recipe?.Resources;

        return resources == null || resources.Count == 0 ? 1 : Math.Max(1, resources[0].Amount);
    }

    public static bool Swing(Mobile bot, CraftSystem system, CraftItem recipe, Type material, BaseTool tool)
    {
        if (bot == null || recipe == null || material == null || tool == null || system == null)
        {
            return false;
        }

        recipe.Craft(bot, system, material, tool);

        return true;
    }

    public static int Bonus(Mobile bot, Type kind)
    {
        if (bot is not BotMobile maker || kind == null)
        {
            return 0;
        }

        var every = maker.Class?.FreeCraftIntervalMs ?? 0;

        if (every <= 0)
        {
            return 0;
        }

        if (maker.Crafted && Core.TickCount - maker.CraftTick < every)
        {
            return 0;
        }

        var pack = maker.Backpack;
        var extra = kind.CreateInstance<Item>();

        if (pack == null || extra == null)
        {
            extra?.Delete();

            return 0;
        }

        maker.Crafted = true;
        maker.CraftTick = Core.TickCount;

        if (!pack.TryDropItem(maker, extra, false))
        {
            extra.Delete();

            return 0;
        }

        logger.Information(
            "{Name} got a second {Item} out of the same materials, as its trade allows once every {Every} minutes",
            maker.Name,
            kind.Name,
            every / 60000
        );

        return 1;
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
