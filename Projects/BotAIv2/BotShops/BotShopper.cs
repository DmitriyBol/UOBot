using System;
using System.Collections.Generic;
using Server.Engines.Craft;
using Server.Items;
using Server.Logging;

namespace Server.BotAI.V2;

/// <summary>
/// Offers a trip to the shops to any bot that has run out of something its class needs.
///
/// <para>
/// <b>Every bot, not a trade.</b> A caster with no reagents is not a caster, a bot with no bandages cannot
/// mend itself, and both facts are true of whoever happens to be standing there. What each one needs comes
/// from its own class's kit — the same list the world handed it at birth — so this needs no table of who
/// buys what and gains a class the moment one is added.
/// </para>
///
/// <para>
/// Supplies are deliberately not bound: they are meant to run out. That is what makes a shop worth walking
/// to, and later what makes another bot's production worth buying.
/// </para>
/// </summary>
public sealed class BotShopper : IBotProposer
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotShopper));

    public static double Short { get; set; } = 0.5;

    private static readonly Type[] Reagents =
    [
        typeof(SulfurousAsh), typeof(BlackPearl), typeof(Garlic), typeof(Ginseng),
        typeof(SpidersSilk), typeof(Nightshade), typeof(Bloodmoss), typeof(MandrakeRoot)
    ];

    public static int Guess { get; set; } = 5;

    public static int Reserve { get; set; } = 100;

    public static long Asked { get; private set; }

    private static bool _saidNoShops;

    public string Name => "Shopper";

    public BotStanding Rung => BotStanding.Free;

    public static long Looks { get; private set; }

    public static long Stocked { get; private set; }

    public static long ToCounter { get; private set; }

    public static long ToStall { get; private set; }

    public static long ToHall { get; private set; }

    public static long ToBoard { get; private set; }

    public static long Broke { get; private set; }

    public static long Unboarded { get; private set; }

    public static long Richest { get; private set; }

    private static readonly System.Collections.Generic.Dictionary<Type, long> _short = [];

    public static int ForgetMs { get; set; } = 900000;

    private static long _forgot;

    private static bool _everForgot;

    private static void Fade()
    {
        var now = Core.TickCount;

        if (!_everForgot)
        {
            _everForgot = true;
            _forgot = now;

            return;
        }

        if (now - _forgot < ForgetMs)
        {
            return;
        }

        _forgot = now;

        List<Type> gone = null;

        foreach (var (kind, times) in _short)
        {
            var left = times / 2;

            if (left <= 0)
            {
                (gone ??= []).Add(kind);
            }
            else
            {
                _short[kind] = left;
            }
        }

        if (gone == null)
        {
            return;
        }

        for (var i = 0; i < gone.Count; i++)
        {
            _short.Remove(gone[i]);
        }
    }

    public BotDeed Propose(IBotWilful bot)
    {
        var body = bot?.Self;
        var map = body?.Map;
        var klass = bot?.Class;
        var pack = body?.Backpack;

        if (map == null || map == Map.Internal || klass == null || pack == null)
        {
            return null;
        }

        BotShops.Survey(map, body.Location);

        Looks++;

        if (!Wanting(bot, klass, pack, out var wanted, out var amount))
        {
            Stocked++;

            return null;
        }

        Fade();

        _short.TryGetValue(wanted, out var seen);
        _short[wanted] = seen + 1;

        var shop = BotShops.Nearest(bot, wanted);
        var counter = shop == null ? 0 : BotShops.Price(shop, wanted);
        var stall = BotAuction.Cheapest(wanted, bot);

        if (stall != null && (counter <= 0 || stall.Price <= counter))
        {
            ToStall++;

            return new BotRestock(stall, wanted, Math.Min(amount, stall.Amount), map, body.Location);
        }

        var merchant = BotShelf.Of(body);
        var lotPrice = 0;
        var ours = merchant == null ? null : BotShelf.Offer(merchant, wanted, out lotPrice);

        if (ours != null)
        {
            var unit = (int)Math.Ceiling(lotPrice / (double)Math.Max(1, ours.Amount));

            if ((counter <= 0 || unit <= counter) && BotYield.Wealth(body) >= lotPrice)
            {
                ToHall++;

                return new BotRestock(merchant, wanted, Math.Max(1, ours.Amount), unit);
            }
        }

        if (counter <= 0)
        {
            Missing(wanted, map, body);

            var order = Board(bot, body, map, wanted, amount);

            if (order != null)
            {
                ToBoard++;
            }
            else
            {
                var wealth = BotYield.Wealth(body);

                if (wealth - BotAuction.Worth(wanted, Guess) * amount <= Reserve)
                {
                    Broke++;

                    if (wealth > Richest)
                    {
                        Richest = wealth;
                    }
                }
                else
                {
                    Unboarded++;
                }
            }

            return order;
        }

        ToCounter++;

        return new BotRestock(shop, wanted, amount, counter);
    }

    public static List<(Type Kind, long Times)> Shortages()
    {
        Fade();

        List<(Type Kind, long Times)> found = [];

        foreach (var (kind, times) in _short)
        {
            found.Add((kind, times));
        }

        found.Sort((a, b) => b.Times.CompareTo(a.Times));

        return found;
    }

    private static string Commonest()
    {
        Type worst = null;
        long most = 0;

        foreach (var (kind, times) in _short)
        {
            if (times > most)
            {
                most = times;
                worst = kind;
            }
        }

        return worst == null ? "nothing" : $"{worst.Name} ({most} times)";
    }

    public static string Describe() =>
        Looks == 0
            ? "nobody has been looked at for supplies"
            : $"{Looks} looks for supplies: {Stocked} were short of nothing, {ToCounter} sent to a shopkeeper, "
              + $"{ToStall} to a cheaper stall, {ToHall} to their own guild's counter, {ToBoard} put an order on the board, {Unmakeable} were left off it because nothing on this shard makes the thing, "
              + $"{Broke} wanted something nobody sells and could not afford one made (the fattest purse among them held {Richest}gp); "
              + $"most often short of {Commonest()} lately";

    public static void ForgetCounts()
    {
        Looks = 0;
        Stocked = 0;
        ToCounter = 0;
        ToStall = 0;
        ToHall = 0;
        ToBoard = 0;
        Unmakeable = 0;
        Broke = 0;
        Richest = 0;
        _short.Clear();
        _everForgot = false;
    }

    private static bool Wanting(IBotWilful bot, BotClass klass, Container pack, out Type wanted, out int amount)
    {
        var kit = klass.Kit;
        var tools = BotOutfit.ToolsFor(klass);
        var body = bot.Self;
        var bond = bot.Bond;

        var rolled = bond?.Weapon;

        if (rolled?.Weapon != null && pack.GetAmount(rolled.Value.Weapon) <= 0 && !Held(body, rolled.Value.Weapon))
        {
            wanted = rolled.Value.Weapon;
            amount = 1;

            return true;
        }

        if (rolled?.Ammunition != null && bond != null)
        {
            var quiver = rolled.Value.Ammunition;
            var granted = BotBinding.BoundCount(quiver, bond);

            if (granted > 0 && Lacking(pack, quiver, granted, out amount))
            {
                wanted = quiver;

                return true;
            }
        }

        for (var i = 0; i < tools.Count; i++)
        {
            if (pack.GetAmount(tools[i]) > 0 || Held(pack.Parent as Mobile, tools[i]))
            {
                continue;
            }

            wanted = tools[i];
            amount = 1;

            return true;
        }

        if (Lacking(pack, typeof(Bandage), kit.Bandages, out amount))
        {
            wanted = typeof(Bandage);

            return true;
        }

        var bottles = BotOutfit.PotionsFor(klass);

        for (var i = 0; i < bottles.Count; i++)
        {
            var (kind, count) = bottles[i];
            var held = pack.GetAmount(kind);

            if (held >= count)
            {
                continue;
            }

            wanted = kind;
            amount = count - held;

            return true;
        }

        if (kit.Reagents > 0)
        {
            for (var i = 0; i < Reagents.Length; i++)
            {
                if (!Lacking(pack, Reagents[i], kit.Reagents, out amount))
                {
                    continue;
                }

                wanted = Reagents[i];

                return true;
            }
        }

        if (BotFlask.Kit(body) == null)
        {
            wanted = null;

            return false;
        }

        var brewing = BotFlask.Needs;

        for (var i = 0; i < brewing.Count; i++)
        {
            if (!Lacking(pack, brewing[i], BotFlask.Herbs, out amount))
            {
                continue;
            }

            wanted = brewing[i];

            return true;
        }

        wanted = null;
        amount = 0;

        return false;
    }

    private static bool Held(Mobile bot, Type kind)
    {
        if (bot == null)
        {
            return false;
        }

        var worn = bot.Items;

        for (var i = 0; i < worn.Count; i++)
        {
            if (kind.IsInstanceOfType(worn[i]))
            {
                return true;
            }
        }

        return false;
    }

    private static bool Lacking(Container pack, Type kind, int born, out int wanted)
    {
        wanted = 0;

        if (born <= 0)
        {
            return false;
        }

        var held = pack.GetAmount(kind);

        if (held >= born * Short)
        {
            return false;
        }

        wanted = born - held;

        return wanted > 0;
    }

    /// <summary>
    /// Nothing on any shelf and nothing on any stall: ask the population for it.
    ///
    /// <para>
    /// <b>The end of this method used to be a shrug.</b> It wrote one error line and returned nothing, so a
    /// caster on a shard whose shopkeepers do not stock sulphurous ash simply stopped casting, for ever, and
    /// the only trace was a single line at boot. <see cref="BotSeeker"/> has ended the other way since it was
    /// written — no shop, no stall, so put it on the board — and there was never a reason for supplies to be
    /// the exception. A reagent nobody sells is a reagent somebody can pick: the foragers put them out at a
    /// few gold apiece, and a funded order is what reaches them.
    /// </para>
    ///
    /// <para>
    /// One order per bot per kind, as everywhere else: the want tops itself up and raises its own offer, and
    /// asking again would turn one order into nine.
    /// </para>
    /// </summary>
    /// <summary>
    /// Whether any craft system on this shard can make the thing at all.
    ///
    /// <para>
    /// <b>The caller's own comment has always said Board refuses when nobody can make the thing, and Board
    /// never asked.</b> An order for something only a shopkeeper produces cannot be filled by anybody, ever:
    /// it holds escrow, raises its own offer every StaleMs and charges the buyer more for it each time, and
    /// the goods it is waiting for do not exist. Patrick rolled back exactly this for glass on 04.09.2026 —
    /// see the note in that day's handover — and bandages walked into the same hole from the other side:
    /// nothing in Engines/Craft makes a Bandage, so when the healer's shelf ran dry at 08:00 on 05.09.2026
    /// the shopper put the shortage on the board instead, and the board raised it from five gold to
    /// forty-eight while 7,752 bots stood hurt with nothing to bind a wound with. Twenty-six thousand gold
    /// of escrow against thirteen thousand in every purse on the island.
    /// </para>
    ///
    /// <para>
    /// Asked of the shard's own craft systems rather than of a list kept here, for the reason every other
    /// file in this assembly gives for the same choice: a list is a second copy of the truth and it drifts.
    /// Cached by type, because the answer cannot change while the server is up.
    /// </para>
    /// </summary>
    /// <summary>What craft makes a thing, which skill it is made with, and how much of that skill it takes.</summary>
    /// <summary>
    /// What it takes to make one of a thing: the craft, the trade on a bot's menu, the skill and its
    /// threshold, and the material the recipe consumes.
    ///
    /// <c>Material</c> is the first resource line, which for every recipe on this shard is the one that
    /// matters — ingots for armour, leather for a bustier, boards for a shaft. The others are trimmings.
    /// </summary>
    public readonly record struct BotRecipeFact(
        string Craft,
        string Trade,
        SkillName Skill,
        double MinSkill,
        string Material,
        int Needs
    );

    private static readonly Dictionary<Type, BotRecipeFact?> _recipes = [];

    public static BotRecipeFact? MadeBy(Type wanted)
    {
        if (wanted == null)
        {
            return null;
        }

        if (_recipes.TryGetValue(wanted, out var known))
        {
            return known;
        }

        (string Craft, string Trade, CraftSystem System)[] systems =
        [
            ("smithing", "Smith", BotAnvil.System),
            ("tailoring", "Tailor", BotThread.System),
            ("alchemy", "Alchemist", BotFlask.System),
            ("fletching", "Fletcher", BotFletching.System),
            ("scribing", "Scribe", BotQuill.System)
        ];

        BotRecipeFact? found = null;

        for (var i = 0; i < systems.Length && found == null; i++)
        {
            var recipes = systems[i].System?.CraftItems;

            if (recipes == null)
            {
                continue;
            }

            for (var r = 0; r < recipes.Count; r++)
            {
                var recipe = recipes[r];

                if (recipe?.ItemType != wanted)
                {
                    continue;
                }

                var skill = recipe.Skills?.Count > 0 ? recipe.Skills[0] : null;

                var resource = recipe.Resources?.Count > 0 ? recipe.Resources[0] : null;

                found = new BotRecipeFact(
                    systems[i].Craft,
                    systems[i].Trade,
                    skill?.SkillToMake ?? SkillName.Blacksmith,
                    skill?.MinSkill ?? 0.0,
                    resource?.ItemType?.Name,
                    resource?.Amount ?? 0
                );

                break;
            }
        }

        if (systems[0].System != null)
        {
            _recipes[wanted] = found;
        }

        return found;
    }

    public static bool Makeable(Type wanted)
    {
        if (wanted == null)
        {
            return false;
        }

        if (_makeable.TryGetValue(wanted, out var known))
        {
            return known;
        }

        var systems = new[]
        {
            BotAnvil.System, BotThread.System, BotFlask.System, BotFletching.System, BotQuill.System
        };

        var made = false;

        for (var i = 0; i < systems.Length && !made; i++)
        {
            var recipes = systems[i]?.CraftItems;

            if (recipes == null)
            {
                continue;
            }

            for (var r = 0; r < recipes.Count; r++)
            {
                if (recipes[r]?.ItemType == wanted)
                {
                    made = true;

                    break;
                }
            }
        }

        if (systems[0] != null)
        {
            _makeable[wanted] = made;
        }

        return made;
    }

    private static readonly System.Collections.Generic.Dictionary<Type, bool> _makeable = [];

    public static long Unmakeable { get; private set; }

    private static BotDeed Board(IBotWilful bot, Mobile body, Map map, Type wanted, int amount)
    {
        if (BotAuction.Wanted(bot, wanted) != null)
        {
            return null;
        }

        if (!Makeable(wanted))
        {
            Unmakeable++;

            return null;
        }

        var offer = BotAuction.Worth(wanted, Guess);

        if (BotYield.Wealth(body) - offer * amount <= Reserve)
        {
            return null;
        }

        Asked++;

        return BotOrder.For(map, body.Location, bot, wanted, offer, amount);
    }

    private static void Missing(Type wanted, Map map, Mobile body)
    {
        if (_saidNoShops)
        {
            return;
        }

        _saidNoShops = true;

        logger.Error(
            "{Name} at {Where} on {Map} found no shopkeeper selling {Item} within its own reach; it cannot restock from a counter here",
            body?.Name ?? "a bot",
            body?.Location ?? Point3D.Zero,
            map,
            wanted.Name
        );
    }

    public static void Forget() => _saidNoShops = false;
}
