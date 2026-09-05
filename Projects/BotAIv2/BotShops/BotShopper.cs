using System;
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

    /// <summary>
    /// How far below what it was born with a supply has to fall before the bot goes shopping.
    ///
    /// Half. Higher and bots live at the counter; lower and a caster discovers it is dry in the middle of
    /// the one fight where that matters.
    /// </summary>
    public static double Short { get; set; } = 0.5;

    /// <summary>
    /// The eight the world hands a caster at birth. What it runs out of is what it goes back for.
    ///
    /// All eight rather than the six the starting spells need: bloodmoss and mandrake root are what half
    /// the first three circles are made of, and every scroll worth writing wants one or the other. A list
    /// short of them is a list that lets a caster collect spells it can never cast.
    /// </summary>
    private static readonly Type[] Reagents =
    [
        typeof(SulfurousAsh), typeof(BlackPearl), typeof(Garlic), typeof(Ginseng),
        typeof(SpidersSilk), typeof(Nightshade), typeof(Bloodmoss), typeof(MandrakeRoot)
    ];

    /// <summary>
    /// What to open an order at when nothing of the kind has ever changed hands here.
    ///
    /// <para>
    /// Only ever the last resort: <see cref="BotAuction.Worth"/> answers with what somebody is paying, then
    /// with what one has really sold for, and reaches this only when the shard has no experience of the thing
    /// at all. Deliberately small — the want raises its own offer every beat until somebody bites, so an
    /// opening ask that is too low costs a minute and one that is too high overpays for ever.
    /// </para>
    /// </summary>
    public static int Guess { get; set; } = 5;

    /// <summary>What a bot keeps back rather than spend on restocking. It still has to eat and be patched up.</summary>
    public static int Reserve { get; set; } = 100;

    /// <summary>Supplies asked of the population because no shelf and no stall had any.</summary>
    public static long Asked { get; private set; }

    private static bool _saidNoShops;

    public string Name => "Shopper";

    public BotStanding Rung => BotStanding.Free;

    // ---- Every gate, counted apart. This proposer had none at all. ------------------------------
    //
    // <b>The one errand that stands between "supplies run out" and "bots die of it", and it was silent.</b>
    // Every other proposer on this shard names its refusals; this one named none, so "nobody needs anything"
    // and "everybody needs something and cannot get it" were the same nought. It mattered on the evening of
    // 27.08.2026: 221 of 252 moments at death's door were a bot reaching for a potion it did not have, and
    // nothing anywhere could say whether that was a bot too poor to buy one, a shard with none for sale, or a
    // bot that had simply never been offered the trip.

    /// <summary>Bots looked at.</summary>
    public static long Looks { get; private set; }

    /// <summary>Bots short of nothing at all. Most answers, and not a refusal.</summary>
    public static long Stocked { get; private set; }

    /// <summary>Trips to a shopkeeper offered.</summary>
    public static long ToCounter { get; private set; }

    /// <summary>Trips to another bot's stall offered — cheaper than the shelf.</summary>
    public static long ToStall { get; private set; }

    /// <summary>Orders put on the needs board because nobody sells the thing at all.</summary>
    public static long ToBoard { get; private set; }

    /// <summary>Wanted something nobody sells and could not afford to have one made either.</summary>
    public static long Broke { get; private set; }

    /// <summary>Wanted something nobody sells, could afford one made, and still got no order onto the board.</summary>
    public static long Unboarded { get; private set; }

    /// <summary>The fattest purse among those. See <c>BotStable.Richest</c> for why this is kept.</summary>
    public static long Richest { get; private set; }

    /// <summary>What the population is short of, by kind, so the summary can name the commonest.</summary>
    private static readonly System.Collections.Generic.Dictionary<Type, long> _short = [];

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

        // Whatever this bot is standing in the middle of, swept once for everybody.
        BotShops.Survey(map, body.Location);

        Looks++;

        if (!Wanting(bot, klass, pack, out var wanted, out var amount))
        {
            Stocked++;

            return null;
        }

        _short.TryGetValue(wanted, out var seen);
        _short[wanted] = seen + 1;

        var shop = BotShops.Nearest(bot, wanted);
        var counter = shop == null ? 0 : BotShops.Price(shop, wanted);
        var stall = BotAuction.Cheapest(wanted, bot);

        // Whichever is cheaper, and that ordering is where a crafter's living comes from: a fighter's gold
        // came off a monster, and it goes to a smith rather than out of the world whenever the smith asks
        // less than the shelf. A shopkeeper is the ceiling, never the preference.
        //
        // <b>And a tie goes to one of ours.</b> This read a strict less-than while BotSeeker, which asks the
        // identical question about scrolls, reads at-most and says why in as many words: the two prices being
        // equal does not make the two purchases equal, because coin paid to a bot stays in the population and
        // comes round again while coin paid across a counter leaves the world. This side answers for every
        // consumable there is — bandages, reagents, arrows, potions, tools — and it was handing every tie to
        // the shelf. Arrows are the case that shows it: a fletcher opens at what the provisioner asks, by
        // design, so under a strict less-than an arrow made on this island could never be sold on it.
        if (stall != null && (counter <= 0 || stall.Price <= counter))
        {
            ToStall++;

            return new BotRestock(stall, wanted, Math.Min(amount, stall.Amount), map, body.Location);
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
                // <b>Counted by what actually happened, not by the branch it fell into.</b> Board returns
                // null for several reasons — nobody can make the thing, an order is already out for it, the
                // bot cannot afford the deposit — and every one of them used to be tallied under a figure
                // the summary calls "could not afford one made". A counter that names one cause and catches
                // all of them is this shard's most-repeated defect, and on 02.09.2026 it printed
                // "0 could not afford one made (the fattest purse among them held 0gp)" — a nought beside an
                // unset maximum, read as a destitute population.
                var wealth = BotYield.Wealth(body);

                // The same sum Board itself refuses on, read from the same place — see Board below.
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

    /// <summary>What the population is most often short of, and how often.</summary>
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
              + $"{ToStall} to a cheaper stall, {ToBoard} put an order on the board, {Unmakeable} were left off it because nothing on this shard makes the thing, "
              + $"{Broke} wanted something nobody sells and could not afford one made (the fattest purse among them held {Richest}gp); "
              + $"most often short of {Commonest()}";

    public static void ForgetCounts()
    {
        Looks = 0;
        Stocked = 0;
        ToCounter = 0;
        ToStall = 0;
        ToBoard = 0;
        Unmakeable = 0;
        Broke = 0;
        Richest = 0;
        _short.Clear();
    }

    /// <summary>
    /// The first thing this bot is short of, and how many it wants.
    ///
    /// <para>
    /// <b>A worn-through tool comes before everything.</b> Only the weapon is bound; a tool is a thing that
    /// wears out — the engine gives a fresh one twenty-five to seventy-five uses, spends one an attempt and
    /// destroys it at zero — so a crafter that has been sewing for a few minutes has no sewing kit, and a bot
    /// with no tool has no trade at all. It cannot even earn the price of bandages. And the failure is silent
    /// by nature: a proposer simply stops offering the work, which looks exactly like a bot that never had a
    /// kit. This errand is the whole of what stands between "tools wear out" and "trades quietly end".
    /// </para>
    ///
    /// <para>
    /// Then bandages before reagents: one of them is what keeps a bot standing and the other is what makes it
    /// useful.
    /// </para>
    /// </summary>
    private static bool Wanting(IBotWilful bot, BotClass klass, Container pack, out Type wanted, out int amount)
    {
        var kit = klass.Kit;
        var tools = BotOutfit.ToolsFor(klass);
        var body = bot.Self;
        var bond = bot.Bond;

        // The weapon before the tools, because a bot with no weapon cannot even defend itself, and because
        // this is the one thing on the list that a crafter can make. A blade wears down every time it lands
        // and the engine destroys it at zero — the same mechanic as the pickaxe, on the thing the shard's
        // whole economy of gold runs through.
        var rolled = bond?.Weapon;

        if (rolled?.Weapon != null && pack.GetAmount(rolled.Value.Weapon) <= 0 && !Held(body, rolled.Value.Weapon))
        {
            wanted = rolled.Value.Weapon;
            amount = 1;

            return true;
        }

        // Then what it shoots. Ammunition is bound by count, so death gives back what it was born with and
        // never more — but shooting spends it, and an archer out of arrows is an archer with a knife.
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
            // Held or worn: a pickaxe in a hand is still a pickaxe. GetAmount walks the pack only, so the
            // layers are asked separately rather than trusting one of the two.
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

        // Then the bottles, and they are replaced one for one rather than at half: there are only ever one or
        // two, and a bot with none has nothing at all that works while something is hitting it. The engine sells
        // only the lesser tier anywhere in this era — the better ones are an alchemist's, and there is no
        // alchemist yet.
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

        // Anybody whose build includes magic at all, not only those called mages: a healer's cures are spells
        // too. The class's own kit is the test, so this follows the build without being told about it.
        //
        // <b>And anybody holding a mortar, whose build says nothing about magic at all.</b> A brewer needs the
        // same eight herbs and its kit declares none of them, so its reagents were nobody's errand: the
        // gatherer picked them, the market held them, and the one bot that could turn them into something
        // read "had the glass but no herbs" 57 times in five minutes while 1936 of them sat on stalls it was
        // never sent to. The tool decides, which is the rule the cook and the smith are already offered work
        // by — a bot that picks up a pestle tomorrow is a brewer tomorrow.
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

        // <b>And the brewer's two, which are not the caster's eight.</b> Asking for the whole list was the
        // first cut of this and it was worse than useless: the list is in casting order — ash and pearl
        // first — the shopper buys one kind an errand at about two errands a bot in twenty minutes, and the
        // only draughts on this shard are heal and cure. So a brewer spent its first four trips on reagents
        // it can never use while the summary read "had the glass but no herbs" on a rising share, 35% of new
        // asks in one window and 58% in the next. BotFlask.Needs reads the recipe table, so a third draught
        // family adds its reagent there and nowhere else.
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

    /// <summary>Whether this bot has one of these in a hand or on its body rather than in the pack.</summary>
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

        // Only cached once the systems exist. Content initialisation builds them, and an answer of "nobody
        // can make anything" taken before that would be remembered for the life of the shard.
        if (systems[0] != null)
        {
            _makeable[wanted] = made;
        }

        return made;
    }

    private static readonly System.Collections.Generic.Dictionary<Type, bool> _makeable = [];

    /// <summary>Shortages left off the board because nothing on this shard can make the thing.</summary>
    public static long Unmakeable { get; private set; }

    private static BotDeed Board(IBotWilful bot, Mobile body, Map map, Type wanted, int amount)
    {
        if (BotAuction.Wanted(bot, wanted) != null)
        {
            return null;
        }

        // An order nobody can fill is worse than an empty board: it freezes the buyer's money and teaches
        // every seeker that orders are not worth walking to. See Makeable.
        if (!Makeable(wanted))
        {
            Unmakeable++;

            return null;
        }

        var offer = BotAuction.Worth(wanted, Guess);

        // Pack and bank together, because every seller on this shard is paid by deposit and a bot keeps only
        // a working float on it. Asking the pack alone is the mistake BotArmourer paid for.
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

        // <b>Said of the bot that asked, because that is all this knows.</b> The answer above came from a
        // search made from ONE bot's position within ITS reach; the sentence used to promote that into a
        // claim about the map and the whole population — "nobody will restock it" — at error level. Its
        // sister line in BotTailor did the same and was measured wrong on 02.09.2026: "no shopkeeper sells
        // cloth, so nobody will sew" was printed four times in a session during which thirty-six bots
        // finished sewing and cloth was bought from two named vendors. A per-bot fact reported as a
        // population-wide one is this shard's most-repeated defect, and here it was in the shard's own
        // diagnostics, sending anybody who read the log after the wrong thing entirely.
        logger.Error(
            "{Name} at {Where} on {Map} found no shopkeeper selling {Item} within its own reach; it cannot restock from a counter here",
            body?.Name ?? "a bot",
            body?.Location ?? Point3D.Zero,
            map,
            wanted.Name
        );
    }

    /// <summary>Lets the complaint be made again after a world reload, which may have shops in it.</summary>
    public static void Forget() => _saidNoShops = false;
}
