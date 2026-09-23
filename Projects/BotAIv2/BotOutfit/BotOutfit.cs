using System;
using System.Collections.Generic;
using Server.Items;
using Server.Logging;

namespace Server.BotAI.V2;

/// <summary>
/// Turns a class's kit into things a bot is actually holding.
///
/// <para>
/// One step, run once at birth, and it is the only place in v2 that creates a bot's belongings. The
/// first version had this as a switch statement inside the bot itself, which meant that "what does a
/// smith own" was a question you answered by reading control flow, and that the ordering rules below —
/// every one of them a bug that was paid for — were scattered through it as comments.
/// </para>
///
/// <para>
/// <b>Order is not cosmetic here.</b> A two-handed weapon is refused by the engine while anything at
/// all is in the other hand, so whatever needs both hands goes on first. Handing out the dagger before
/// the bow cost the first version ten archers who spent their entire lives stabbing skeletons with
/// knives while carrying the bows they had trained for — and it was invisible, because the bow was in
/// the pack, exactly where a spare weapon belongs.
/// </para>
/// </summary>
public static class BotOutfit
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotOutfit));

    private const int StartingBottles = 8;

    private const int StartingScrolls = 20;

    public static int Purse { get; set; } = 400;

    public static int Outfitted { get; private set; }

    public static int Bound { get; private set; }

    public static BotBond Give(Mobile bot, BotClass klass)
    {
        var bond = new BotBond();

        if (bot?.Backpack == null || klass == null)
        {
            logger.Warning("Asked to outfit a bot with no pack or no class; nothing handed over");
            return bond;
        }

        var kit = klass.Kit;

        GiveStaff(bot, klass, bond);
        GiveWeapon(bot, kit.Ranged, bond);

        GiveWeapon(bot, kit.Melee, bond);

        if (kit.Sidearm.HasValue)
        {
            Hand(bot, BotBinding.Make(kit.Sidearm.Value.Weapon, 1), bond, pack: true);
        }

        GiveTools(bot, klass);
        GiveArmour(bot, kit, bond);
        GiveBook(bot, kit, bond);
        GiveSupplies(bot, klass);

        Outfitted++;

        return bond;
    }

    private static void GiveWeapon(Mobile bot, IReadOnlyList<BotWeaponOption> options, BotBond bond)
    {
        if (options == null || options.Count == 0 || bond.Weapon.HasValue)
        {
            return;
        }

        var start = Utility.Random(options.Count);
        var chosen = options[start];
        Item weapon = null;
        Item lightest = null;
        var lightestOption = chosen;
        var least = int.MaxValue;

        for (var i = 0; i < options.Count; i++)
        {
            var option = options[(start + i) % options.Count];
            var made = BotBinding.Make(option.Weapon, 1);

            if (made == null)
            {
                continue;
            }

            var need = made is BaseWeapon arm ? arm.StrRequirement : 0;

            if (bot.Str >= need)
            {
                weapon = made;
                chosen = option;

                if (i > 0)
                {
                    Refitted++;
                }

                break;
            }

            if (need < least)
            {
                lightest?.Delete();
                lightest = made;
                lightestOption = option;
                least = need;
            }
            else
            {
                made.Delete();
            }
        }

        if (weapon == null)
        {
            if (lightest == null)
            {
                return;
            }

            weapon = lightest;
            chosen = lightestOption;
            lightest = null;
            Overborn++;

            if (bot.RawStr < least)
            {
                bot.RawStr = least;
            }
        }

        lightest?.Delete();

        bond.Weapon = chosen;

        Hand(bot, weapon, bond, pack: false);

        if (chosen.Ammunition == null || chosen.AmmunitionCount <= 0)
        {
            return;
        }

        var ammunition = BotBinding.Make(chosen.Ammunition, chosen.AmmunitionCount);

        if (ammunition == null)
        {
            return;
        }

        bot.Backpack.DropItem(ammunition);

        BotBinding.BindStack(ammunition, chosen.AmmunitionCount, bond);
        Bound++;
    }

    private static void GiveStaff(Mobile bot, BotClass klass, BotBond bond)
    {
        if (!klass.Kit.Staff)
        {
            return;
        }

        var staff = new BotCasterStaff { Hue = klass.StaffHue };

        Hand(bot, staff, bond, pack: false);
    }

    public static List<Type> ToolsFor(BotClass klass)
    {
        List<Type> tools = [];

        if (klass == null)
        {
            return tools;
        }

        var kit = klass.Kit;

        for (var i = 0; i < kit.Tools.Count; i++)
        {
            tools.Add(kit.Tools[i]);
        }

        tools.Add(typeof(Scissors));

        tools.Add(typeof(SkinningKnife));

        if (Wants(klass, SkillName.Alchemy))
        {
            tools.Add(typeof(MortarPestle));
        }

        tools.Add(typeof(Skillet));

        if (Wants(klass, SkillName.Inscribe))
        {
            tools.Add(typeof(ScribesPen));
        }

        return tools;
    }

    private static void GiveTools(Mobile bot, BotClass klass)
    {
        var pack = bot.Backpack;
        var tools = ToolsFor(klass);

        for (var i = 0; i < tools.Count; i++)
        {
            var tool = tools[i].CreateInstance<Item>();

            if (tool == null)
            {
                continue;
            }

            pack.DropItem(tool);
        }
    }

    private static void GiveBook(Mobile bot, BotKit kit, BotBond bond)
    {
        var spells = kit.Spells;

        if (spells.Count == 0)
        {
            return;
        }

        var book = new Spellbook();

        for (var i = 0; i < spells.Count; i++)
        {
            book.Content |= 1ul << spells[i];
        }

        Hand(bot, book, bond, pack: true);
    }

    private static void GiveSupplies(Mobile bot, BotClass klass)
    {
        var pack = bot.Backpack;
        var kit = klass.Kit;

        if (Purse > 0)
        {
            pack.DropItem(new Gold(Purse));
        }

        if (kit.Bandages > 0)
        {
            pack.DropItem(new Bandage(kit.Bandages));
        }

        if (kit.Reagents > 0)
        {
            var each = kit.Reagents;

            pack.DropItem(new SulfurousAsh(each));
            pack.DropItem(new BlackPearl(each));
            pack.DropItem(new Garlic(each));
            pack.DropItem(new Ginseng(each));
            pack.DropItem(new SpidersSilk(each));
            pack.DropItem(new Nightshade(each));
            pack.DropItem(new Bloodmoss(each));
            pack.DropItem(new MandrakeRoot(each));
        }

        if (Wants(klass, SkillName.Alchemy))
        {
            pack.DropItem(new Bottle(StartingBottles));
        }

        if (Wants(klass, SkillName.Inscribe))
        {
            pack.DropItem(new BlankScroll(StartingScrolls));
        }

        var bottles = PotionsFor(klass);

        for (var i = 0; i < bottles.Count; i++)
        {
            var (kind, count) = bottles[i];

            for (var made = 0; made < count; made++)
            {
                var bottle = kind.CreateInstance<Item>();

                if (bottle != null)
                {
                    pack.DropItem(bottle);
                }
            }
        }
    }

    public static List<(Type Kind, int Count)> PotionsFor(BotClass klass)
    {
        List<(Type, int)> bottles = [];

        if (klass == null)
        {
            return bottles;
        }

        var families = BotArsenal.Draughts;

        for (var i = 0; i < families.Count; i++)
        {
            var type = BotArsenal.Potion(families[i]);
            var count = klass.PotionLimit(families[i]);

            if (type != null && count > 0)
            {
                bottles.Add((type, count));
            }
        }

        return bottles;
    }

    public static bool Brews(BotClass klass) => klass != null && Wants(klass, SkillName.Alchemy);

    private static bool Wants(BotClass klass, SkillName skill)
    {
        var skills = klass.Skills;

        for (var i = 0; i < skills.Count; i++)
        {
            if (skills[i].Skill == skill)
            {
                return true;
            }
        }

        return false;
    }

    private static void GiveArmour(Mobile bot, BotKit kit, BotBond bond)
    {
        var armour = kit.Armour;

        for (var i = 0; i < armour.Count; i++)
        {
            Hand(bot, BotBinding.Make(armour[i], 1), bond, pack: false);
        }
    }

    private static void Hand(Mobile bot, Item item, BotBond bond, bool pack)
    {
        if (item == null)
        {
            return;
        }

        BotBinding.Bind(item, bond);
        Bound++;

        if (!pack && bot.EquipItem(item))
        {
            return;
        }

        var backpack = bot.Backpack;

        if (backpack != null)
        {
            backpack.DropItem(item);
            return;
        }

        item.Delete();
    }

    public static void Reset()
    {
        Outfitted = 0;
        Bound = 0;
        Refitted = 0;
        Overborn = 0;
    }

    public static string Describe() =>
        $"{Outfitted} bots outfitted, {Bound} things bound to their owners, {Refitted} born to a lighter weapon than the roll for want of strength, {Overborn} given the strength for the lightest";

    public static long Refitted { get; private set; }

    public static long Overborn { get; private set; }
}
