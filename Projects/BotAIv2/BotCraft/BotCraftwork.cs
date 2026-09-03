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

    /// <summary>
    /// How far below its own skill a bot keeps its work.
    ///
    /// At the very edge of what it can do most attempts fail and the material is lost, which is a poor
    /// living; well below it, nothing is learned. Five points under is the band where both happen.
    /// </summary>
    public static double Margin { get; set; } = 5.0;

    /// <summary>
    /// The best thing this bot could make out of that material right now, or null.
    ///
    /// <para>
    /// Best means <b>the hardest thing it can still reliably make</b>, which is the same choice a person
    /// learning a trade makes: the difficulty is where the skill comes from, and an easy piece teaches
    /// nothing. Anything needing a second material is skipped — a bot with iron has iron, and a recipe it
    /// cannot finish is a swing that produces a message nobody reads.
    /// </para>
    /// </summary>
    /// <param name="stock">
    /// How much of the material the bot can actually put on the anvil, or nought when the caller does not
    /// know.
    ///
    /// <para>
    /// <b>Without it this picks the hardest thing the skill allows and never asks whether the pack can pay
    /// for it, and that is a defect of the shape this project has now found five times: two thresholds on
    /// one shelf.</b> BotSmith offers forge work to anybody holding six ingots; this chose plate legs, which
    /// eat far more; the bot walked to the forge and reported "out of metal" a quarter of a minute later.
    /// Godric the architect did that four times in four minutes on 03.09.2026, and would have gone on doing
    /// it, because the refusal happens at the anvil and the offer is made in a field.
    /// </para>
    ///
    /// <para>
    /// Choosing a lesser piece is the right answer rather than a compromise: a smith with eight ingots
    /// making a ring mail sleeve has made something, gained skill and put a thing on the market, and will be
    /// offered the plate legs on the day it has the metal for them.
    /// </para>
    /// </param>
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

            best = recipe;
            bestNeeds = needs;
        }

        return best;
    }

    /// <summary>
    /// The recipe that makes exactly this kind of thing, or null.
    ///
    /// <para>
    /// <b>The half of choosing that only the board needs.</b> Working from material to recipe answers "what
    /// shall I make"; an order on the board asks the opposite question — somebody wants a <em>this</em>, can
    /// I make one — and nothing here could answer it until there was somebody placing orders. It still
    /// refuses recipes needing a second material and recipes beyond the bot's skill, because an order taken
    /// and not filled is worse than an order left alone: the money is already down.
    /// </para>
    /// </summary>
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

            return Requirement(recipe, skill) <= able - Margin ? recipe : null;
        }

        return null;
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

    /// <summary>The skill this recipe asks for, or zero when it asks for none.</summary>
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

    /// <summary>How much material one attempt at this recipe eats.</summary>
    public static int Cost(CraftItem recipe)
    {
        var resources = recipe?.Resources;

        return resources == null || resources.Count == 0 ? 1 : Math.Max(1, resources[0].Amount);
    }

    /// <summary>
    /// One attempt. The engine takes it from here: it rolls, and either an item appears in the pack a moment
    /// later or the material is gone — which is what learning a trade costs.
    ///
    /// <b>Nothing is returned about the outcome, because the outcome is not known yet.</b> Crafting runs on
    /// its own timer; whoever swings has to count what is in the pack afterwards rather than believe an
    /// attempt was an item. The first version reported attempts as output, and its tally said forty-four
    /// things made about a smith that had produced nothing in three minutes.
    /// </summary>
    public static bool Swing(Mobile bot, CraftSystem system, CraftItem recipe, Type material, BaseTool tool)
    {
        if (bot == null || recipe == null || material == null || tool == null || system == null)
        {
            return false;
        }

        recipe.Craft(bot, system, material, tool);

        return true;
    }

    /// <summary>
    /// The free craft: once in a while, a swing that produced something produces one more, for the same
    /// material.
    ///
    /// <para>
    /// <b>It was declared, configured, described at length in the class it belongs to — and never once
    /// asked.</b> <c>BotClass.FreeCraftIntervalMs</c> was set to an hour on the crafter, was readable from
    /// configuration, and no line anywhere in the project read it. A class ability that nothing invokes is
    /// not a weak ability, it is a paragraph: the crafter has been an ordinary smith with a longer comment
    /// since the day it was written. That is this project's most familiar shape — a thing written and never
    /// read — and it is the third one found this week.
    /// </para>
    ///
    /// <para>
    /// <b>Asked at the moment new output is seen, which is the only moment the engine gives.</b> A craft is
    /// fired and forgotten — <see cref="Swing"/> hands the attempt to the engine and the result appears in
    /// the pack a moment later — so there is no "the swing succeeded" to hook. What there is, in every craft
    /// on the shard, is a beat where the count of finished pieces has gone up since the last one. Granting
    /// the extra there means it can only ever follow a real success, which is what the class note promises:
    /// a failed swing still costs its cloth and gives nothing.
    /// </para>
    ///
    /// <para>
    /// <b>Aimed at the bottleneck rather than at the skill</b>, as the crafter's own note says: a pack holds
    /// about twelve ore, which is twenty ingots, which is one or two helmets and then a walk back to the
    /// mine. What this saves is the walk.
    /// </para>
    /// </summary>
    /// <returns>How many extra pieces were granted — one, or nought.</returns>
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

        // Stamped before the drop, so a pack too full to take it still costs the hour. Otherwise the cheapest
        // way to hold the ability open would be to work with a full pack.
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

    /// <summary>How many of that thing are in the pack. The only honest way to count what a swing produced.</summary>
    public static int Made(Mobile bot, Type kind) =>
        kind == null ? 0 : bot?.Backpack?.GetAmount(kind, true) ?? 0;

    /// <summary>Everything of that kind in the pack, so it can be handed over or put on the market.</summary>
    public static List<Item> Gather(Mobile bot, Type kind)
    {
        List<Item> made = [];

        var pack = bot?.Backpack;

        if (pack == null || kind == null)
        {
            return made;
        }

        // A snapshot: handing things over moves them out of the pack, which mutates the list being read.
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
