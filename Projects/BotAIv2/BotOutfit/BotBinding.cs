using System;
using System.Collections.Generic;
using Server.Items;
using Server.Logging;

namespace Server.BotAI.V2;

/// <summary>
/// What "bound" means, in one place.
///
/// Bound is two promises and a ceiling: the thing weighs nothing, death does not take it, and nobody
/// may sell it. Each of the three answers a failure the first version measured, and none of them is
/// decoration.
///
/// <para>
/// <b>Weightless.</b> Past <c>40 + 3.5 × Str</c> stones the engine charges five stamina and more for
/// every single step, and refuses the step outright once stamina reaches zero — so an overloaded bot
/// drains itself flat in a dozen paces and then stands there for the rest of the shard's life. Three
/// bots spent an entire session exactly that way, and the log insisted six hundred times over that the
/// ground was clear and the engine approved of the step. It did. Stamina was not in the message. For a
/// gatherer, whose whole job is to fill a pack, tools that weigh are ore that cannot be carried.
/// </para>
///
/// <para>
/// <b>Kept through death.</b> A bot's working tools are what let it start again after being killed. The
/// first version's smith who lost its hammer was not a smith any more — it could not forge, could not
/// take commissions, and quietly spent the rest of its life hitting skeletons like everybody else. An
/// entire mechanism existed there to soften this: spare tools kept in a bank box, and a trip across
/// Britain to fetch one. Binding retires that mechanism rather than improving it.
/// </para>
///
/// <para>
/// <b>Not merchandise.</b> A bound item is never sold, auctioned, scrapped or posted. Without this the
/// weightlessness becomes an exploit — a bot would carry a free anvil to market — and worse, the
/// scrapper would eat the hammer, since "destroy it" was the first version's final answer for anything
/// nobody would buy.
/// </para>
///
/// <para>
/// <b>The engine does two thirds of this and the ledger does the rest.</b> <c>LootType.Newbied</c> is
/// how this era keeps a thing with its owner through death, and it works on whole objects. It cannot
/// express a partial stack, which is why ammunition is counted instead — see
/// <see cref="TrimAmmunition"/>.
/// </para>
/// </summary>
public static class BotBinding
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotBinding));

    private static bool _warnedAboutEra;

    /// <summary>
    /// Binds one indivisible thing: weightless, kept through death, recorded so nothing sells it.
    /// </summary>
    public static void Bind(Item item, BotBond bond)
    {
        if (item == null || bond == null)
        {
            return;
        }

        // Newbied is how the pre-AOS engine keeps a thing with its owner. On an AOS-era shard the flag
        // is inert — insurance replaced it — and the ledger below would then be the only thing standing
        // between a bot and losing its trade to one bad fight. Said once, loudly, rather than
        // discovered from a population of smiths with no hammers.
        if (Core.AOS)
        {
            if (!_warnedAboutEra)
            {
                _warnedAboutEra = true;

                logger.Warning(
                    "This shard runs AOS rules, where LootType.Newbied does nothing; bound gear survives death only by being handed back on resurrection"
                );
            }
        }
        else if (item.LootType == LootType.Regular)
        {
            item.LootType = LootType.Newbied;
        }

        Weightless(item);

        bond.Items.Add(item.Serial);

        var type = item.GetType();

        if (!bond.Issued.Contains(type))
        {
            bond.Issued.Add(type);
        }
    }

    /// <summary>
    /// Binds a stack by <em>count</em>: this much of it, and no more, is the bot's own.
    ///
    /// The flag goes on as well, so the ordinary case costs the ledger nothing at death — but the count
    /// is what is authoritative, because the flag on a merged stack is a coin toss.
    /// </summary>
    public static void BindStack(Item stack, int granted, BotBond bond)
    {
        if (stack == null || bond == null || granted <= 0)
        {
            return;
        }

        Mark(stack);

        bond.Ammunition[stack.GetType()] = granted;
    }

    /// <summary>
    /// The flag and the weight, without touching the ledger.
    ///
    /// Split out for one narrow reason: <see cref="TrimAmmunition"/> re-marks a stack while walking the
    /// ledger, and a helper that also wrote to it would be mutating a dictionary mid-enumeration.
    /// </summary>
    private static void Mark(Item stack)
    {
        if (!Core.AOS && stack.LootType == LootType.Regular)
        {
            stack.LootType = LootType.Newbied;
        }

        Weightless(stack);
    }

    /// <summary>
    /// Whether this exact item is one the bot was given. Asked by anything that would part it from it.
    /// </summary>
    public static bool IsBound(Item item, BotBond bond) =>
        item != null && bond != null && bond.Items.Contains(item.Serial);

    /// <summary>
    /// Whether this type of ammunition is bound to the bot at all, and up to what count.
    /// </summary>
    public static int BoundCount(Type type, BotBond bond) =>
        type != null && bond != null && bond.Ammunition.TryGetValue(type, out var granted) ? granted : 0;

    /// <summary>
    /// The death rule for ammunition: the bot keeps <c>min(carried, granted)</c> and the corpse gets
    /// the rest.
    ///
    /// <para>
    /// Called from the bot's own death hook, where the corpse already exists and already holds whatever
    /// the engine decided to take — so both halves of "how much did it have" are readable, and the
    /// arithmetic does not depend on which way a stack merge happened to set the loot flag. That
    /// independence is the point: it is the one thing about stacks that cannot be relied upon.
    /// </para>
    ///
    /// <para>
    /// An archer born with a hundred and fifty arrows who has spent all but one rises with one, because
    /// bound is a ceiling and not a refill. One who bought two hundred more rises with a hundred and
    /// fifty and leaves the rest on the ground for whoever walks past, because a quiver is something an
    /// archer is supposed to have to think about.
    /// </para>
    /// </summary>
    public static void TrimAmmunition(Mobile bot, BotBond bond, Container corpse)
    {
        var pack = bot?.Backpack;

        if (pack == null || bond == null || bond.Ammunition.Count == 0)
        {
            return;
        }

        foreach (var (type, granted) in bond.Ammunition)
        {
            var inPack = TakeAll(pack, type);
            var inCorpse = corpse is { Deleted: false } ? TakeAll(corpse, type) : 0;
            var carried = inPack + inCorpse;

            if (carried <= 0)
            {
                continue;
            }

            var keep = Math.Min(carried, granted);
            var lost = carried - keep;

            if (keep > 0)
            {
                var kept = Make(type, keep);

                if (kept != null)
                {
                    pack.DropItem(kept);

                    // Marked, not re-registered: the granted count is already in the ledger and this
                    // loop is reading it.
                    Mark(kept);
                }
            }

            if (lost > 0 && corpse is { Deleted: false })
            {
                var dropped = Make(type, lost);

                if (dropped != null)
                {
                    corpse.DropItem(dropped);
                }
            }

            if (lost > 0)
            {
                logger.Information(
                    "{Name} died holding {Carried} {Ammo} and keeps {Kept}; {Lost} were not its own",
                    bot.Name,
                    carried,
                    type.Name,
                    keep,
                    lost
                );
            }
        }
    }

    /// <summary>
    /// Hands back anything bound that no longer exists on the bot. Run on resurrection.
    ///
    /// The check is "does the bot hold one of these", not "is that serial still alive", and the
    /// simplification is safe because of what bound means: a bound thing weighs nothing and cannot be
    /// sold, so there is no reason for a bot ever to put one down. If it is not on the bot, it is gone.
    ///
    /// Mostly a safety net — the engine keeps these through death by itself in this era — and the net
    /// is worth having because the alternative failure is invisible: a bot that quietly stopped being
    /// able to do its trade goes on hitting skeletons and looks like every other bot doing that.
    /// </summary>
    public static int Restore(Mobile bot, BotBond bond)
    {
        var pack = bot?.Backpack;

        if (pack == null || bond == null)
        {
            return 0;
        }

        var handed = 0;

        for (var i = 0; i < bond.Issued.Count; i++)
        {
            var type = bond.Issued[i];

            if (Holds(bot, type))
            {
                continue;
            }

            var replacement = Make(type, 1);

            if (replacement == null)
            {
                continue;
            }

            pack.DropItem(replacement);
            Bind(replacement, bond);
            handed++;

            logger.Information("{Name} rose without its {Thing} and was handed another", bot.Name, type.Name);
        }

        return handed;
    }

    /// <summary>
    /// Zero weight, stated in one place so the single uncertain engine call in this folder is also in
    /// one place. If <c>Item.Weight</c> turns out not to be settable in this fork, this is the only
    /// line that has to change.
    /// </summary>
    private static void Weightless(Item item) => item.Weight = 0.0;

    /// <summary>Whether the bot has one of these on it — worn, wielded or in the pack.</summary>
    private static bool Holds(Mobile bot, Type type)
    {
        var worn = bot.Items;

        for (var i = 0; i < worn.Count; i++)
        {
            if (worn[i].GetType() == type)
            {
                return true;
            }
        }

        var pack = bot.Backpack;

        if (pack == null)
        {
            return false;
        }

        var carried = pack.Items;

        for (var i = 0; i < carried.Count; i++)
        {
            if (carried[i].GetType() == type)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Removes every item of this type from the container and returns how many there were in total.
    ///
    /// Counting and taking in one pass because the caller always wants both, and because a stack that
    /// is counted and then looked up again is a stack that can change between the two.
    /// </summary>
    private static int TakeAll(Container container, Type type)
    {
        var items = container.Items;
        var total = 0;

        // Backwards: removing from a live list.
        for (var i = items.Count - 1; i >= 0; i--)
        {
            var item = items[i];

            if (item.GetType() != type)
            {
                continue;
            }

            total += item.Amount;
            item.Delete();
        }

        return total;
    }

    /// <summary>
    /// One item of this type, of this amount, or null if the type cannot be built.
    ///
    /// Null rather than throwing: a kit naming a type the shard does not have is a configuration
    /// mistake, and it should cost that one item rather than the bot.
    /// </summary>
    internal static Item Make(Type type, int amount)
    {
        if (type == null)
        {
            return null;
        }

        // <b>The engine's activator, not the framework's, and this line was the whole of an archer's
        // problem.</b> <c>Activator.CreateInstance</c> looks for a genuinely parameterless constructor, and
        // ammunition has none: <c>Arrow(int amount = 1)</c> and <c>Bolt(int amount = 1)</c> declare an
        // optional parameter instead. So every archer ever born threw the same warning and went out with an
        // empty quiver — twice a boot, in every log, for a whole evening. The market's own splitter was fixed
        // for exactly this and the kit was not. <c>Type.CreateInstance&lt;T&gt;()</c> fills optional
        // parameters with <c>Type.Missing</c> and returns the object.
        Item item;

        try
        {
            item = type.CreateInstance<Item>();
        }
        catch (Exception e)
        {
            logger.Warning(e, "Could not make a {Thing} for a bot's kit", type.Name);
            return null;
        }

        if (item == null)
        {
            logger.Warning("A bot's kit names {Thing}, which is not an item", type.Name);
            return null;
        }

        if (amount > 1)
        {
            item.Amount = amount;
        }

        return item;
    }
}
