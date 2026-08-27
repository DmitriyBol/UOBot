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
    /// <summary>
    /// How far below its own skill a bot keeps its work.
    ///
    /// At the very edge of what it can do most attempts fail and the cloth is lost, which is a poor living;
    /// well below it, nothing is learned. Five points under is the band where both happen.
    /// </summary>
    public static double Margin { get; set; } = 5.0;

    /// <summary>The tailoring system, or null before content initialisation has built it.</summary>
    public static CraftSystem System => DefTailoring.CraftSystem;

    /// <summary>The kit this bot sews with, if it is carrying one.</summary>
    public static SewingKit Kit(Mobile bot) => bot?.Backpack?.FindItemByType<SewingKit>();

    /// <summary>
    /// How much of a material is in the pack.
    ///
    /// <para>
    /// <b>Asked by material rather than named cloth, because the needle stopped being about cloth.</b> On
    /// this era one sewing kit makes cloth goods and leather goods alike, and the second material is the only
    /// one on the shard that another bot had to produce — so the count has to take a type or the whole
    /// leather half of the trade has nowhere to ask its question.
    /// </para>
    /// </summary>
    public static int Amount(Mobile bot, Type stuff) =>
        stuff == null ? 0 : bot?.Backpack?.GetAmount(stuff) ?? 0;

    /// <summary>
    /// How much material one piece of this recipe eats, or one when the recipe does not say.
    ///
    /// <para>
    /// Only ever asked of a recipe <see cref="Simple"/> has already passed, so there is exactly one resource
    /// and it is the material in hand. It exists so that what a finished piece opens at on the market can
    /// follow what went into it: a constant learned from cloth prices a leather tunic below its own
    /// materials, and a trade that sells at a loss teaches the ledger that the trade is worthless.
    /// </para>
    /// </summary>
    public static int Units(CraftItem recipe)
    {
        var resources = recipe?.Resources;

        return resources == null || resources.Count == 0 ? 1 : Math.Max(1, resources[0].Amount);
    }

    /// <summary>
    /// The best thing this bot could make out of that material right now, or null.
    ///
    /// <para>
    /// <b>What somebody is paying for, and only failing that, the hardest thing it can still reliably
    /// make.</b> The second half was the whole rule and it quietly closed the armour trade. Difficulty is
    /// where skill comes from, so a tailor always reached for the most advanced leather piece it could
    /// manage — and every piece of armour this population actually asks for is at the <em>bottom</em> of that
    /// ladder. A leather cap needs Tailoring 6, which makes it the last thing this method would ever have
    /// picked. On 26.08.2026 the board carried four standing, funded, twice-raised orders for leather caps
    /// through an entire hour: seventeen finished pieces of tailoring, nought fills, and the wants sat there
    /// raising their offers at nobody. The market had done its whole job — the demand was written down, with
    /// the money down beside it — and the one method that decides what gets made never asked.
    /// </para>
    ///
    /// <para>
    /// Demand does not become a permanent diet: a want is for a number of things and closes when it is met,
    /// so the tailor makes the cap, the order clears, and the next choice is the hardest thing again. That is
    /// the difference between serving a market and being trapped by it, and it needs no second threshold —
    /// the want's own life is the limit.
    /// </para>
    ///
    /// <para>
    /// Anything needing a second material is skipped — a bot with cloth has cloth, and a recipe it cannot
    /// finish is a swing that produces a message nobody reads.
    /// </para>
    /// </summary>
    /// <summary>
    /// The recipe for one named thing, if this bot is good enough to sew it out of that material.
    ///
    /// The tailor's twin of <c>BotAnvil.Recipe</c>, and it exists for the same reason: a crafter reading the
    /// needs board is asked "can you make <em>this</em>", which is a different question from "what is the
    /// best thing you could make", and only the second had an answer here.
    /// </summary>
    public static CraftItem Recipe(Mobile bot, Type material, Type wanted) =>
        BotCraftwork.Recipe(bot, System, SkillName.Tailoring, material, wanted);

    public static CraftItem Choose(Mobile bot, Type material)
    {
        var system = System;

        if (bot == null || system == null || material == null)
        {
            return null;
        }

        var skill = bot.Skills[SkillName.Tailoring].Value;
        var recipes = system.CraftItems;

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

            // Somebody has put money down for one of these. The highest such bid wins outright, ahead of
            // anything chosen for its difficulty: this is paid work and the rest is practice.
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

    /// <summary>Whether this recipe is made of nothing but the one material.</summary>
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

    /// <summary>The tailoring skill this recipe asks for, or zero when it asks for none.</summary>
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

    /// <summary>
    /// One attempt. The engine takes it from here: it rolls, and either an item appears in the pack a moment
    /// later or the cloth is gone — which is what learning a trade costs.
    ///
    /// <b>Nothing is returned about the outcome, because the outcome is not known yet.</b> Crafting runs on
    /// its own timer; whoever swings has to count what is in the pack afterwards rather than believe an
    /// attempt was an item. The first version reported attempts as output, and its tally said forty-four
    /// things made about a smith that had produced nothing in three minutes.
    /// </summary>
    public static bool Swing(Mobile bot, CraftItem recipe, Type material, BaseTool tool)
    {
        if (bot == null || recipe == null || material == null || tool == null || System == null)
        {
            return false;
        }

        recipe.Craft(bot, System, material, tool);

        return true;
    }

    /// <summary>How many of that thing are in the pack. The only honest way to count what a swing produced.</summary>
    public static int Made(Mobile bot, Type kind) =>
        kind == null ? 0 : bot?.Backpack?.GetAmount(kind, true) ?? 0;

    /// <summary>Everything of that kind in the pack, so it can be put on the market.</summary>
    public static List<Item> Gather(Mobile bot, Type kind)
    {
        List<Item> made = [];

        var pack = bot?.Backpack;

        if (pack == null || kind == null)
        {
            return made;
        }

        // A snapshot: listing moves things out of the pack, which mutates the list being read.
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
