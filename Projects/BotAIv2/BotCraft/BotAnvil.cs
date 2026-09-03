using System;
using System.Collections.Generic;
using Server.Engines.Craft;
using Server.Items;

namespace Server.BotAI.V2;

/// <summary>
/// What the smith's trade knows that no other trade does: its system, its skill, its hammer, its metal, and
/// the one thing about blacksmithing that is genuinely different — it cannot be done just anywhere.
///
/// <para>
/// <b>A smith needs a forge and an anvil, both, within reach.</b> That is the engine's rule and it is
/// checked by the engine — <c>DefBlacksmithy.CheckAnvilAndForge</c> — so a bot standing next to an anvil in a
/// field can swing a hammer all day and produce a message about needing a forge that nobody reads. The good
/// news is that this shard already solved the location half for another reason entirely: <c>BotGround</c>
/// only writes a forge into its list if there is an anvil within a few tiles of it, because the miner needs
/// the same pair to smelt ore. So "where can I smith" is a question that was already being answered, by the
/// people who needed it for something else.
/// </para>
/// </summary>
public static class BotAnvil
{
    /// <summary>The blacksmithing system, or null before content initialisation has built it.</summary>
    public static CraftSystem System => DefBlacksmithy.CraftSystem;

    /// <summary>The skill this trade is measured in.</summary>
    public const SkillName Skill = SkillName.Blacksmith;

    /// <summary>The metal everything here is made of.</summary>
    public static Type Metal => typeof(IronIngot);

    /// <summary>The hammer this bot forges with, if it is carrying one.</summary>
    public static SmithHammer Kit(Mobile bot) => bot?.Backpack?.FindItemByType<SmithHammer>();

    /// <summary>How much of a given metal is in the pack. Iron when nothing else is named.</summary>
    public static int Ingots(Mobile bot, Type metal = null) => bot?.Backpack?.GetAmount(metal ?? Metal) ?? 0;

    /// <summary>
    /// The dearest metal this bot carries enough of and its skill will take, or iron.
    ///
    /// <para>
    /// <b>Every recipe on this shard says "iron ingot" and none of them means it.</b> The engine's smithy
    /// takes eight metals and they make the same items in better material — the recipe names iron because
    /// iron is the <em>base</em> resource, and which metal actually goes in is chosen at the moment of the
    /// swing. Nothing here ever chose: <see cref="Metal"/> was a constant, so a population that dug forty
    /// bronze to five iron in a session could use one ore in nine and piled the rest up.
    /// </para>
    ///
    /// <para>
    /// <b>The skill floor is the engine's, read rather than copied.</b> Every metal carries its own
    /// requirement — sixty-five for dull copper, eighty for bronze, ninety-nine for valorite — and those
    /// numbers already exist in <c>DefBlacksmithy</c>. A second table of them here is the defect this project
    /// has paid for more than any other, so this walks the system's own sub-resources instead.
    /// </para>
    ///
    /// <para>
    /// Dearest first, because a better metal is a better piece for the same swing and the same walk to the
    /// mine. Falling back to iron rather than to nothing: a smith that will not work because it has no
    /// valorite is a smith standing still.
    /// </para>
    /// </summary>
    /// <param name="need">How many the recipe eats. A metal the bot cannot fill a recipe with is no use.</param>
    public static Type Best(Mobile bot, int need)
    {
        var system = System;
        var pack = bot?.Backpack;

        if (system == null || pack == null)
        {
            return Metal;
        }

        var able = bot.Skills[Skill].Value;
        var metals = system.CraftSubRes;

        Type best = null;
        var bestNeeds = -1.0;

        for (var i = 0; i < metals.Count; i++)
        {
            var metal = metals.GetAt(i);

            if (metal?.ItemType == null || metal.RequiredSkill > able || metal.RequiredSkill <= bestNeeds)
            {
                continue;
            }

            if (pack.GetAmount(metal.ItemType) < need)
            {
                continue;
            }

            best = metal.ItemType;
            bestNeeds = metal.RequiredSkill;
        }

        return best ?? Metal;
    }

    /// <summary>
    /// How near an anvil and a forge a body has to be for the engine to let it smith.
    ///
    /// <para>
    /// <b>Two, because <c>DefBlacksmithy.CanCraft</c> says two.</b> It is written here rather than borrowed
    /// because the engine writes it as a literal and there is nothing to borrow — so what this constant owes
    /// the reader is the line it mirrors, and that line is the only reason it may ever change.
    /// </para>
    /// </summary>
    public static int Reach { get; set; } = 2;

    /// <summary>
    /// Whether this bot is standing where a hammer is any use. Asked of the engine, not of a list.
    ///
    /// <para>
    /// <b>Asking the right authority the wrong question is not asking it.</b> This called the engine's own
    /// test — and handed it <c>BotGround.AnvilReach</c>, three, which is a number about how far an anvil may
    /// sit from a forge for the pair to be worth remembering. The engine judges by two. So the gate was
    /// looser than the authority behind it, and a gate looser than its authority passes exactly the cases
    /// that go on to fail: the bot stood two tiles the far side of the forge, was told it was at a smithy,
    /// swung twenty-four times, and the engine refused every one — silently, because a refusal is a message
    /// to a client and a bot has none. "24 attempts, 0 made", over and over, at an eighty-seven per cent
    /// failure rate for as long as anybody has been watching, and not one line in the log to say why.
    /// </para>
    ///
    /// <para>
    /// The engine also wants line of sight and the same floor, which no list of coordinates could ever have
    /// promised. That is the whole argument for asking it rather than modelling it — and the argument only
    /// works if it is asked its own question.
    /// </para>
    /// </summary>
    public static bool AtASmithy(Mobile bot)
    {
        if (bot == null)
        {
            return false;
        }

        DefBlacksmithy.CheckAnvilAndForge(bot, Reach, out var anvil, out var forge);

        return anvil && forge;
    }

    /// <summary>The hardest thing this bot can still reliably beat out of iron, or null.</summary>
    /// <summary>
    /// The most of any one metal this smith could actually swing with.
    ///
    /// <para>
    /// A recipe is paid for out of a single metal — <see cref="Best"/> picks which — so what decides whether
    /// a piece can be made is the largest single pile, not the total. A bot holding eight iron and eight
    /// bronze can make an eight-ingot piece and not a sixteen-ingot one, and adding them up would say
    /// otherwise.
    /// </para>
    /// </summary>
    public static int Stock(Mobile bot)
    {
        var system = System;
        var pack = bot?.Backpack;

        if (system == null || pack == null)
        {
            return 0;
        }

        var able = bot.Skills[Skill].Value;
        var metals = system.CraftSubRes;
        var most = pack.GetAmount(Metal);

        for (var i = 0; i < metals.Count; i++)
        {
            var metal = metals.GetAt(i);

            if (metal?.ItemType == null || metal.RequiredSkill > able)
            {
                continue;
            }

            var held = pack.GetAmount(metal.ItemType);

            if (held > most)
            {
                most = held;
            }
        }

        return most;
    }

    public static CraftItem Choose(Mobile bot) => BotCraftwork.Choose(bot, System, Skill, Metal, Stock(bot));

    /// <summary>The recipe for exactly this thing, if this bot could make one. For orders off the board.</summary>
    public static CraftItem Recipe(Mobile bot, Type wanted) =>
        BotCraftwork.Recipe(bot, System, Skill, Metal, wanted);

    /// <summary>
    /// One attempt at the anvil, in the metal named. The metal is what the engine calls the sub-resource, and
    /// passing anything but iron is the whole of how a bronze helm comes to exist. See <see cref="Best"/>.
    /// </summary>
    public static bool Swing(Mobile bot, CraftItem recipe, BaseTool tool, Type metal = null) =>
        BotCraftwork.Swing(bot, System, recipe, metal ?? Metal, tool);

    /// <summary>How many of that thing are in the pack.</summary>
    public static int Made(Mobile bot, Type kind) => BotCraftwork.Made(bot, kind);

    /// <summary>Everything of that kind in the pack.</summary>
    public static List<Item> Gather(Mobile bot, Type kind) => BotCraftwork.Gather(bot, kind);
}
