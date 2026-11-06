using System;
using Server.Engines.Craft;
using Server.Items;

namespace Server.BotAI.V2;

public static class BotFletching
{

    public static CraftSystem System => DefBowFletching.CraftSystem;

    public static BaseTool Kit(Mobile bot) => BotOutfit.Oldest<FletcherTools>(bot?.Backpack);

    public const int PerArrow = 1;

    public const int PerShaft = 1;

    public static int LeastArrows { get; set; } = 20;

    public static int Worth { get; set; } = 2;

    public static int Amount(Mobile bot, Type stuff) =>
        stuff == null ? 0 : bot?.Backpack?.GetAmount(stuff) ?? 0;

    public static int Logs(Mobile bot) => Amount(bot, typeof(Log));

    public static int Shafts(Mobile bot) => Amount(bot, typeof(Shaft));

    public static int Feathers(Mobile bot) => Amount(bot, typeof(Feather));

    public static int Keeps { get; set; } = 20;

    public static long Spared { get; private set; }

    public static long Sold { get; private set; }

    public static bool Spares(Mobile bot, Item item)
    {
        if (item == null || bot == null || item.GetType() != typeof(Feather) || Kit(bot) == null)
        {
            return false;
        }

        if (Feathers(bot) <= Keeps)
        {
            Spared++;

            return true;
        }

        Sold++;

        return false;
    }

    public static void ForgetTrade()
    {
        Spared = 0;
        Sold = 0;
    }

    public static int Possible(Mobile bot)
    {
        var shafts = Shafts(bot) + Logs(bot) / PerShaft;

        return Math.Min(shafts, Feathers(bot)) / PerArrow;
    }

    public static CraftItem Recipe(Mobile bot, Type material, Type wanted) =>
        BotCraftwork.Recipe(bot, System, SkillName.Fletching, material, wanted);

    public static CraftItem Feathering(Mobile bot)
    {
        var system = System;

        if (bot == null || system == null)
        {
            return null;
        }

        var able = bot.Skills[SkillName.Fletching].Value;
        var recipes = system.CraftItems;

        for (var i = 0; i < recipes.Count; i++)
        {
            var recipe = recipes[i];
            var resources = recipe.Resources;

            if (recipe.ItemType != typeof(Arrow) || resources == null || resources.Count != 2)
            {
                continue;
            }

            var shafts = 0;
            var feathers = 0;

            for (var r = 0; r < resources.Count; r++)
            {
                var res = resources[r];

                if (res.ItemType == typeof(Shaft))
                {
                    shafts = Math.Max(1, res.Amount);
                }
                else if (res.ItemType == typeof(Feather))
                {
                    feathers = Math.Max(1, res.Amount);
                }
            }

            if (shafts <= 0 || feathers <= 0)
            {
                continue;
            }

            if (BotCraftwork.Requirement(recipe, SkillName.Fletching) > able - BotCraftwork.Margin)
            {
                continue;
            }

            if (Shafts(bot) < shafts || Feathers(bot) < feathers)
            {
                continue;
            }

            return recipe;
        }

        return null;
    }

    public static bool Swing(Mobile bot, CraftItem recipe, Type material, BaseTool tool) =>
        BotCraftwork.Swing(bot, System, recipe, material, tool);

    public static int Made(Mobile bot, Type kind) =>
        kind == null ? 0 : bot?.Backpack?.GetAmount(kind, true) ?? 0;
}
