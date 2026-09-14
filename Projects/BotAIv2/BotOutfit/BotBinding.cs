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

    public static void Bind(Item item, BotBond bond)
    {
        if (item == null || bond == null)
        {
            return;
        }

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
        Dye(item);

        bond.Items.Add(item.Serial);

        var type = item.GetType();

        if (!bond.Issued.Contains(type))
        {
            bond.Issued.Add(type);
        }
    }

    public static void BindStack(Item stack, int granted, BotBond bond)
    {
        if (stack == null || bond == null || granted <= 0)
        {
            return;
        }

        Mark(stack);

        bond.Ammunition[stack.GetType()] = granted;
    }

    private static void Mark(Item stack)
    {
        if (!Core.AOS && stack.LootType == LootType.Regular)
        {
            stack.LootType = LootType.Newbied;
        }

        Weightless(stack);
        Dye(stack);
    }

    public static int BoundHue { get; set; } = 1152;

    private static void Dye(Item item)
    {
        if (item is BaseWeapon && BoundHue > 0)
        {
            item.Hue = BoundHue;
        }
    }

    public static bool IsBound(Item item, BotBond bond) =>
        item != null && bond != null && bond.Items.Contains(item.Serial);

    public static int BoundCount(Type type, BotBond bond) =>
        type != null && bond != null && bond.Ammunition.TryGetValue(type, out var granted) ? granted : 0;

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

    private static void Weightless(Item item) => item.Weight = 0.0;

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

    private static int TakeAll(Container container, Type type)
    {
        var items = container.Items;
        var total = 0;

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

    internal static Item Make(Type type, int amount)
    {
        if (type == null)
        {
            return null;
        }

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
