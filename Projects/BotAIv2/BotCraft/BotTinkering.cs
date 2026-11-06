using System;
using System.Collections.Generic;
using Server.Engines.Craft;
using Server.Items;

namespace Server.BotAI.V2;

public static class BotTinkering
{
    public const SkillName Skill = SkillName.Tinkering;

    public static CraftSystem System => DefTinkering.CraftSystem;

    public static readonly Type Metal = typeof(IronIngot);

    public static readonly Type[] Tools =
    [
        typeof(Pickaxe),
        typeof(Hatchet),
        typeof(SmithHammer),
        typeof(SewingKit),
        typeof(Scissors),
        typeof(SkinningKnife),
        typeof(Skillet),
        typeof(MortarPestle),
        typeof(ScribesPen),
        typeof(FletcherTools),
        typeof(TinkerTools),
        typeof(Tongs)
    ];

    private static readonly HashSet<Type> _tools = new(Tools);

    public static bool IsTool(Type kind) => kind != null && _tools.Contains(kind);

    public static TinkerTools Kit(Mobile bot) => BotOutfit.Oldest<TinkerTools>(bot?.Backpack);

    public static int Spares { get; set; } = 2;

    public static long Seeded { get; private set; }

    private static readonly HashSet<Serial> _seeded = [];

    public static bool Seed(Mobile bot)
    {
        if (bot is not BotMobile { Deleted: false, Alive: true } body || body.Backpack == null || Kit(body) != null
            || body.Class?.Wants(Skill) != true || !_seeded.Add(body.Serial))
        {
            return false;
        }

        var tool = new TinkerTools();

        if (!body.Backpack.TryDropItem(body, tool, false))
        {
            tool.Delete();

            return false;
        }

        BotBinding.Bind(tool, body.Bond);
        Seeded++;

        return true;
    }

    public static int Ingots(Mobile bot) => bot?.Backpack?.GetAmount(Metal) ?? 0;

    public static double Able(Mobile bot) => bot?.Skills[Skill].Base ?? 0.0;

    public static CraftItem Recipe(Mobile bot, Type wanted)
    {
        var system = System;

        if (system?.CraftItems == null || wanted == null || bot == null)
        {
            return null;
        }

        var able = Able(bot);
        var recipes = system.CraftItems;

        for (var i = 0; i < recipes.Count; i++)
        {
            var recipe = recipes[i];

            if (recipe?.ItemType != wanted || !BotCraftwork.Simple(recipe, Metal))
            {
                continue;
            }

            return BotCraftwork.Requirement(recipe, Skill) <= able - BotThread.Margin ? recipe : null;
        }

        return null;
    }

    public static Type Choose(Mobile bot, int floor, out int stocked)
    {
        stocked = 0;

        Dictionary<Type, int> stock = [];
        var box = (bot as BotMobile)?.FindBankNoCreate();

        for (var i = 0; i < Tools.Length; i++)
        {
            var kind = Tools[i];
            var held = BotShopkeep.Showing(kind) + (bot?.Backpack?.GetAmount(kind) ?? 0) + (box?.GetAmount(kind) ?? 0);

            if (held > 0)
            {
                stock[kind] = held;
            }
        }

        if ((bot?.Backpack?.GetAmount(typeof(TinkerTools)) ?? 0) < Spares && Recipe(bot, typeof(TinkerTools)) != null)
        {
            stocked = 0;

            return typeof(TinkerTools);
        }

        Type best = null;
        var shortest = int.MaxValue;

        for (var i = 0; i < Tools.Length; i++)
        {
            var tool = Tools[i];
            var have = stock.TryGetValue(tool, out var n) ? n : 0;

            if (have >= floor || have >= shortest)
            {
                continue;
            }

            if (Recipe(bot, tool) == null)
            {
                continue;
            }

            best = tool;
            shortest = have;
        }

        stocked = best == null ? 0 : shortest;

        return best;
    }
}
