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
    /// Takes this bot's own metal back off its own stall, whatever metal it is, and says how much came back.
    ///
    /// <para>
    /// <b>Three gates in a ring, and the smith starved inside it.</b> <c>BotBullion</c> refuses to order iron
    /// from a bot that is selling iron, and says in as many words that the metal is "fetched back at the
    /// anvil, see BotForge" — which is true, <see cref="BotForge"/> reclaims when it runs short mid-work. But
    /// <c>BotSmith</c> will not offer the anvil at all below <c>LeastMetal</c>, so a smith whose ingots were
    /// on its own stall could never reach the errand that would fetch them. The two halves of it printed side
    /// by side in the same sentence every five minutes and never met: at 21:42 on 04.09.2026,
    /// "241 short of metal" beside "160 have their own out on a stall", with nought forged all session.
    /// </para>
    ///
    /// <para>
    /// So the fetch happens where the shortage is noticed, which is the fletcher's rule about its own
    /// feathers and the tailor's about its own leather. Nothing is bought and nothing walks: the market holds
    /// a stall's goods out of the world, so a bot takes its own back from wherever it is standing.
    /// </para>
    /// </summary>
    /// <summary>
    /// Caps every metal this bot could work at <paramref name="amount"/> in a keep-list, rather than iron
    /// alone.
    ///
    /// <para>
    /// <b>Two lists of what counts as metal, and they were about to be set against each other.</b>
    /// <c>BotUnload.Needed</c> named <see cref="Metal"/> — iron and nothing else — so a smith's bronze was
    /// merchandise and went out on a stall, while <see cref="Best"/> and <see cref="Fetch"/> know every
    /// sub-resource the bot's skill allows. Left as it was, the porter would list the bronze and the smith
    /// would fetch it straight back, for ever, on the beat. The same reading, in one place, is the only way
    /// two subsystems can agree about a thing.
    /// </para>
    /// </summary>
    public static void Keep(Mobile body, Dictionary<Type, int> keep, int amount)
    {
        var system = System;

        if (system == null || keep == null)
        {
            keep?.TryAdd(Metal, amount);

            return;
        }

        keep[Metal] = amount;

        if (body == null)
        {
            return;
        }

        var able = body.Skills[Skill].Value;
        var metals = system.CraftSubRes;

        for (var i = 0; i < metals.Count; i++)
        {
            var metal = metals.GetAt(i);

            if (metal?.ItemType != null && metal.RequiredSkill <= able)
            {
                keep[metal.ItemType] = amount;
            }
        }
    }

    public static int Fetch(IBotWilful bot, Mobile body, int need)
    {
        var system = System;
        var pack = body?.Backpack;

        if (system == null || pack == null || bot == null)
        {
            return 0;
        }

        var able = body.Skills[Skill].Value;
        var metals = system.CraftSubRes;
        var back = 0;

        for (var i = 0; i < metals.Count; i++)
        {
            var metal = metals.GetAt(i);

            // Only metal this bot could actually work. Fetching back what it cannot smith would empty a stall
            // that was selling perfectly well to somebody who can.
            if (metal?.ItemType == null || metal.RequiredSkill > able)
            {
                continue;
            }

            back += BotAuction.Reclaim(bot, metal.ItemType);

            if (Ingots(body, metal.ItemType) >= need)
            {
                break;
            }
        }

        return back;
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

    /// <summary>
    /// How many attempts a stint at the anvil is set up to survive. Three.
    ///
    /// <para>
    /// <b>A failed swing is not a free swing: the engine eats half the metal for it.</b>
    /// <c>CraftItem.ConsumeRes</c> takes <c>amounts[i] - amounts[i] / 2</c> on a failure and
    /// <c>CraftSystem.ConsumeOnFailure</c> is true for smithing, so a broadsword that does not come off costs
    /// five of its ten ingots. Every number in this trade was written as though a swing that failed cost
    /// nothing, and the whole of the waste follows from that one assumption: a smith set out holding exactly
    /// what one piece needs, missed twice, and walked home. On 05.09.2026 that was thirty-eight of the
    /// fifty-two stints that ended in nothing, and the log wrote the same sentence every time —
    /// <em>2 attempts, 0 made — out of metal</em>.
    /// </para>
    ///
    /// <para>
    /// <b>What three actually buys, said properly, because the first version of this note said two.</b> A
    /// miss costs half a piece, so <c>3c</c> carries four misses and then the piece — not two, which is
    /// <c>2c</c>. Three is kept because it is what was measured: stints producing something went from three
    /// in ten to eight in ten. Two was never tried, and the note claiming three was the smallest number that
    /// would do was arithmetic that did not match its own number.
    ///
    /// <para>
    /// <b>It is not free.</b> Dividing the pile by it turned 82 smiths in one window into "with metal but not
    /// enough for any recipe they can work", where that bucket used to read nought. The trade is ahead —
    /// more pieces out of fewer stints — but this is a dial that has been chosen once and never tuned, and
    /// the number to watch when tuning it is that bucket against the count of stints that produced something.
    /// </para>
    ///
    /// <para>
    /// A multiplier and not a floor, because what a round eats depends on what is being made, and a number
    /// that did not depend on the recipe is how these two thresholds came to sit on one shelf in the first
    /// place.
    /// </para>
    /// </para>
    /// </summary>
    public static int Tries { get; set; } = 3;

    /// <summary>
    /// The best thing this bot could make and see through, or null.
    ///
    /// The stock is divided by <see cref="Tries"/> so that what is chosen is what the pack can afford to
    /// attempt, rather than what it can afford to attempt once. Choosing on the undivided pile is choosing
    /// the dearest recipe the metal will just barely cover, which is the recipe most likely to end the round
    /// with nothing.
    /// </summary>
    public static CraftItem Choose(Mobile bot) =>
        BotCraftwork.Choose(bot, System, Skill, Metal, Stock(bot) / Math.Max(1, Tries));

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
