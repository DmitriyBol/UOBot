using System;
using Server.Engines.Craft;
using Server.Items;

namespace Server.BotAI.V2;

/// <summary>
/// What a fletcher needs to know: the craft system, the tool, and the two-step chain that turns a log and a
/// feather into an arrow.
///
/// <para>
/// <b>Why this trade exists at all.</b> Arrows were the only consumable on this shard with no source. The
/// population is born with a hundred and fifty apiece and thirteen bots shoot them; the provisioner keeps
/// twenty at a time and the variety dealer thirty to sixty; gleaning brings back one or two off the ground
/// and the rest break. So the arrow supply could only ever fall, and on the morning of 04.09.2026 it hit
/// bottom: five archers failed sixty-seven fights in ten minutes, every one of them reading "100% of it left
/// and not a scratch in 45s" — a bow being swung with nothing in it. The blade in the pack (see
/// <c>BotArms.Quiver</c>) is what stops that being a dead bot; this is what stops it happening.
/// </para>
///
/// <para>
/// <b>Two steps and no station, which is what makes this the shortest craft on the shard.</b> A smith needs
/// a forge with an anvil beside it and a miner to bring it metal. Fletching needs a tool in the pack and
/// nothing else: <c>Log → Shaft</c>, then <c>Shaft + Feather → Arrow</c>, both at Fletching 0–40, which is
/// to say anybody who owns the tool can do it. And every recipe in the chain is <c>SetUseAllRes</c>, so one
/// action spends everything the pack holds — a hundred logs become a hundred shafts in a single swing.
/// </para>
///
/// <para>
/// <b>Where the two materials come from is the whole point of the trade.</b> Logs are sold by carpenters, so
/// they are money. Feathers are sold by <em>nobody</em> — there is not one <c>typeof(Feather)</c> in any
/// vendor's stock on this shard — and come off birds, twenty-five to a chicken and thirty-six to an eagle,
/// which the hunters kill all day and list on the population's own market. So the arrow is the first thing
/// on this island that cannot be made without something another bot went out and killed.
/// </para>
/// </summary>
public static class BotFletching
{
    public static CraftSystem System => DefBowFletching.CraftSystem;

    public static BaseTool Kit(Mobile bot) => bot?.Backpack?.FindItemByType<FletcherTools>();

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
