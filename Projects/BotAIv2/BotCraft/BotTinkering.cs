using System;
using System.Collections.Generic;
using Server.Engines.Craft;
using Server.Items;

namespace Server.BotAI.V2;

/// <summary>
/// What a tinker needs to know: the craft system, the tool, and which of the things it can make are the
/// tools the rest of the population wears out.
///
/// <para>
/// <b>The trade that closes the loop on tools.</b> Every other trade on this shard makes its goods from what
/// the island produces — ore, hides, logs, herbs, meat — and buys one thing over a counter that no bot ever
/// made: the tool in its hand. A pickaxe, a hatchet, a smith's hammer, a sewing kit, a skillet, a mortar, a
/// pen: all iron, all in <c>DefTinkering</c>, all bought from an NPC until 28.09.2026. A tinker makes them
/// from the smelter's ingots and puts them on the market, where the restock already looks first
/// (<c>BotShopper</c>), so the counter becomes the fallback rather than the source.
/// </para>
///
/// <para>
/// <b>Only the tools, and only the ones somebody uses.</b> Tinkering makes clocks, sextants, jewellery and
/// cutlery too. None of that has a buyer here, and a maker with no reader of demand fills the stalls with
/// goblets — the shape <c>DECISIONS</c> calls C12. The list below is the tools the classes are issued and
/// restock, in the order the population wears them out.
/// </para>
/// </summary>
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
        var listings = BotAuction.Listings;

        for (var i = 0; i < listings.Count; i++)
        {
            var listing = listings[i];

            if (listing?.Kind == null || listing.Amount <= 0 || !_tools.Contains(listing.Kind))
            {
                continue;
            }

            stock[listing.Kind] = stock.TryGetValue(listing.Kind, out var n) ? n + listing.Amount : listing.Amount;
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
