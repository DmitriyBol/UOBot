using System;
using System.Collections.Generic;
using Server.Engines.Craft;
using Server.Items;

namespace Server.BotAI.V2;

public static class BotOven
{

    public static CraftSystem System => DefCooking.CraftSystem;

    public static SkillName Skill => SkillName.Cooking;

    public static BaseTool Kit(Mobile bot) => BotOutfit.Oldest<Skillet>(bot?.Backpack);

    public static IReadOnlyList<Type> Raw { get; } =
    [
        typeof(RawRibs),
        typeof(RawLambLeg),
        typeof(RawBird),
        typeof(RawChickenLeg),
        typeof(RawFishSteak)
    ];

    public static int Reach { get; set; } = 2;

    public static bool AtAHearth(Mobile bot) => CraftItem.NearHeatSource(bot);

    public static int Worthwhile { get; set; } = 2;

    public static int Worth { get; set; } = 6;

    private static readonly Dictionary<Type, Type> _meals = [];

    public static int MealWorth(Type raw)
    {
        if (raw == null)
        {
            return 0;
        }

        if (!_meals.TryGetValue(raw, out var meal))
        {
            var cooked = false;

            for (var i = 0; i < Raw.Count; i++)
            {
                cooked |= Raw[i] == raw;
            }

            var recipes = cooked ? System?.CraftItems : null;

            for (var i = 0; recipes != null && i < recipes.Count && meal == null; i++)
            {
                var recipe = recipes[i];

                if (recipe?.ItemType != null && recipe.Resources is { Count: > 0 } needs && needs[0].ItemType == raw && IsMeal(recipe.ItemType))
                {
                    meal = recipe.ItemType;
                }
            }

            if (meal != null || !cooked || System != null)
            {
                _meals[raw] = meal;
            }
        }

        return meal == null ? 0 : Math.Max(1, BotMarket.Worth(meal, Worth));
    }

    public static int Keeps { get; set; } = 20;

    public static long Spared { get; private set; }

    public static long Sold { get; private set; }

    public static void Forget()
    {
        Spared = 0;
        Sold = 0;
    }

    public static bool Spares(Mobile bot, Item item)
    {
        if (item == null || bot == null || Kit(bot) == null)
        {
            return false;
        }

        var kind = item.GetType();

        for (var i = 0; i < Raw.Count; i++)
        {
            if (Raw[i] != kind)
            {
                continue;
            }

            if (Amount(bot, kind) <= Keeps && BotMarket.Demand(bot as IBotWilful, kind) == null)
            {
                Spared++;

                return true;
            }

            Sold++;

            return false;
        }

        return false;
    }

    public static int Amount(Mobile bot, Type stuff) =>
        stuff == null ? 0 : bot?.Backpack?.GetAmount(stuff) ?? 0;

    public static Type Larder(Mobile bot, out int held)
    {
        held = 0;

        Type best = null;

        for (var i = 0; i < Raw.Count; i++)
        {
            var carried = Amount(bot, Raw[i]);

            if (carried > held)
            {
                held = carried;
                best = carried >= Worthwhile ? Raw[i] : null;
            }
        }

        return best;
    }

    public static CraftItem Choose(Mobile bot, Type raw) =>
        raw == null ? null : BotCraftwork.Choose(bot, System, Skill, raw, Amount(bot, raw));

    public static bool IsMeal(Type kind) =>
        kind == typeof(Ribs)
        || kind == typeof(LambLeg)
        || kind == typeof(CookedBird)
        || kind == typeof(ChickenLeg)
        || kind == typeof(FishSteak);
}
