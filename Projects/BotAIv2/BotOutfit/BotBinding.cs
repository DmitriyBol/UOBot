using System;
using System.Collections.Generic;
using Server.Items;
using Server.Logging;

namespace Server.BotAI.V2;

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

    public static long Refused { get; private set; }

    public static long Forfeited { get; private set; }

    public static bool Refuses(Item item, BotBond bond)
    {
        if (!IsBound(item, bond))
        {
            return false;
        }

        Refused++;

        return true;
    }

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

    public static long Reissued { get; private set; }

    public static int Reissue(BotMobile bot)
    {
        if (bot is not { Deleted: false, Bond: { } bond, Backpack: { } pack })
        {
            return 0;
        }

        var handed = 0;

        if (bond.Weapon is { Weapon: { } kind })
        {
            handed += Reissue(bot, bond, pack, kind);
        }

        if (bot.Class?.Kit?.Sidearm is { Weapon: { } side })
        {
            handed += Reissue(bot, bond, pack, side);
        }

        if (bot.Class?.Kit is { Staff: true })
        {
            handed += Reissue(bot, bond, pack, typeof(BotCasterStaff));
        }

        return handed;
    }

    private static int Reissue(Mobile bot, BotBond bond, Container pack, Type kind)
    {
        if (bond.Issued.Contains(kind))
        {
            return 0;
        }

        if (Holds(bot, kind))
        {
            bond.Issued.Add(kind);

            return 0;
        }

        var weapon = Make(kind, 1);

        if (weapon == null)
        {
            return 0;
        }

        Bind(weapon, bond);
        pack.DropItem(weapon);
        Reissued++;

        logger.Information("{Name} is handed back the {Thing} a catch took off it", bot.Name, kind.Name);

        return 1;
    }

    public static int Forfeit(Mobile bot, BotBond bond, HashSet<Item> spared = null)
    {
        if (bond == null)
        {
            return 0;
        }

        var taken = 0;

        if (bot is { Deleted: false })
        {
            List<Item> gone = [];
            var worn = bot.Items;

            for (var i = 0; i < worn.Count; i++)
            {
                if (IsBound(worn[i], bond) && !(spared != null && worn[i] is BaseWeapon))
                {
                    gone.Add(worn[i]);
                }
            }

            if (bot.Backpack is { } pack)
            {
                for (var i = 0; i < pack.Items.Count; i++)
                {
                    if (IsBound(pack.Items[i], bond) && !(spared != null && pack.Items[i] is BaseWeapon))
                    {
                        gone.Add(pack.Items[i]);
                    }
                }
            }

            for (var i = 0; i < gone.Count; i++)
            {
                gone[i].Delete();
                taken++;
            }
        }

        if (spared != null)
        {
            var owed = bond.Issued.Count;
            HashSet<Serial> kept = [];

            foreach (var item in spared)
            {
                kept.Add(item.Serial);
            }

            bond.Items.RemoveWhere(serial => !kept.Contains(serial) && World.FindItem(serial) is not BaseWeapon { Deleted: false });
            bond.Issued.RemoveAll(type => !typeof(BaseWeapon).IsAssignableFrom(type));

            var struck = owed - bond.Issued.Count;

            Forfeited += taken + struck;

            return taken + struck;
        }

        var entitled = bond.Issued.Count;

        bond.Items.Clear();
        bond.Issued.Clear();

        Forfeited += taken + entitled;

        return taken + entitled;
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
