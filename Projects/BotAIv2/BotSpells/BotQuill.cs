using System;
using System.Collections.Generic;
using Server.Engines.Craft;
using Server.Items;

namespace Server.BotAI.V2;

/// <summary>
/// Writing scrolls: what can be written, what it takes, and the attempt itself.
///
/// <para>
/// <b>Everything goes through the shard's own <see cref="CraftSystem"/></b>, exactly as sewing does — the same
/// call a player's craft window makes. The skill check, the failure, the blank and the herbs burnt on a bad
/// attempt and the scroll that appears are all the engine's, so the Inscribe a scribe gains here is gained by
/// inscribing rather than by being credited.
/// </para>
///
/// <para>
/// <b>It is the same shape as the needle and it needs no place either.</b> A pen works where the bot is
/// standing, so this is the second trade rather than the third: a smith still needs a forge. What it needs
/// instead is mana — four for the first circle and fifty for the eighth, which at fifty Intelligence is a
/// mage's entire pool — and that is what makes Meditation on a mage's own vector something other than
/// decoration.
/// </para>
///
/// <para>
/// <b>And unlike cloth, what comes off it has no shopkeeper above the third circle.</b> That is the whole
/// reason this trade is worth adding: it is the first work in the project whose output only another bot can
/// buy.
/// </para>
/// </summary>
public static class BotQuill
{
    /// <summary>
    /// How far below its own skill a scribe keeps its work. The same five points the needle uses, and the same
    /// argument: at the very edge most attempts burn the material, and well below it nothing is learned.
    /// </summary>
    public static double Margin { get; set; } = 5.0;

    /// <summary>
    /// What a scribe adds to what its materials cost, as a share of them, when it prices its own work.
    ///
    /// <para>
    /// <b>Nothing here had ever compared the two sides of the trade.</b> A scroll was listed at whatever the
    /// market last paid for one, or failing that at what a shopkeeper charges for that circle, and the herbs
    /// and paper it was made of were never in the arithmetic at all. So a scribe could — and on 02.09.2026 a
    /// population of them did — buy a hundred gold of reagents, write, and list the result for ninety. The
    /// mages of this shard were the poorest bots on it, and this is the half of the reason that is about
    /// price rather than about how much they bought.
    /// </para>
    ///
    /// <para>
    /// A fifth. Patrick's own figure, "fifteen or twenty percent, so the reagents pay for themselves and they
    /// earn as well" — and it is a floor under the asking price, not a tax on it: where the market already
    /// pays more than cost and a fifth, the market's number stands and the scribe is simply doing well.
    /// </para>
    /// </summary>
    public static double Markup { get; set; } = 0.20;

    /// <summary>
    /// How many of each herb a caster keeps back from the pen, whatever it is writing.
    ///
    /// <para>
    /// <b>A mage that writes with its last reagent has disarmed itself.</b> Casting from the book consumes
    /// herbs — see <c>BotStrike.Ready</c>, which refuses a spell on an empty pack — so a scribe that writes
    /// until the pack is bare cannot then fight, and fighting is the living it is meant to fall back on. The
    /// old rule asked only whether one attempt's worth was present, so a mage born with thirty of each herb
    /// wrote thirty times and walked into the field with nothing to throw.
    /// </para>
    /// </summary>
    public static int Reserve { get; set; } = 10;

    /// <summary>What a herb is reckoned to cost where no shopkeeper within reach sells it.</summary>
    public static int HerbGuess { get; set; } = 3;

    /// <summary>The inscription system, or null before content initialisation has built it.</summary>
    public static CraftSystem System => DefInscription.CraftSystem;

    /// <summary>
    /// What a counter charges for one of a herb. Remembered, because a shelf price in this era does not move
    /// and this is asked once per recipe per decision.
    /// </summary>
    private static readonly Dictionary<Type, int> _herbage = [];

    /// <summary>What one of this herb costs, from the nearest shelf that has it, or the guess.</summary>
    public static int Herb(Mobile bot, Type kind)
    {
        if (kind == null)
        {
            return 0;
        }

        if (_herbage.TryGetValue(kind, out var known))
        {
            return known;
        }

        var shop = BotShops.Nearest(bot, kind);
        var price = shop == null ? 0 : BotShops.Price(shop, kind);

        if (price <= 0)
        {
            return HerbGuess;
        }

        _herbage[kind] = price;

        return price;
    }

    /// <summary>
    /// What one attempt at this recipe costs in materials: the paper, and every herb it burns.
    ///
    /// <para>
    /// The blank is passed in rather than looked up because the proposer has just priced it at the counter it
    /// is about to walk to, and that is the price this bot will actually pay.
    /// </para>
    /// </summary>
    public static int Cost(Mobile bot, CraftItem recipe, int paper)
    {
        var resources = recipe?.Resources;

        if (resources == null)
        {
            return 0;
        }

        var bill = Math.Max(0, paper);

        for (var i = 0; i < resources.Count; i++)
        {
            var res = resources[i];

            if (res.ItemType == null || res.ItemType == typeof(BlankScroll))
            {
                continue;
            }

            bill += Math.Max(1, res.Amount) * Herb(bot, res.ItemType);
        }

        return bill;
    }

    /// <summary>The least this may be listed for: what it cost to make, and the markup on top.</summary>
    public static int Asking(int cost) => cost <= 0 ? 0 : (int)Math.Ceiling(cost * (1.0 + Markup));

    /// <summary>The pen this bot writes with, if it is carrying one.</summary>
    public static ScribesPen Pen(Mobile bot) => bot?.Backpack?.FindItemByType<ScribesPen>();

    /// <summary>How many blank scrolls are in the pack.</summary>
    public static int Blanks(Mobile bot) => bot?.Backpack?.GetAmount(typeof(BlankScroll)) ?? 0;

    /// <summary>The Inscribe this recipe asks for, or zero when it asks for none.</summary>
    public static double Requirement(CraftItem recipe)
    {
        var skills = recipe?.Skills;

        if (skills == null)
        {
            return 0.0;
        }

        for (var i = 0; i < skills.Count; i++)
        {
            if (skills[i].SkillToMake == SkillName.Inscribe)
            {
                return skills[i].MinSkill;
            }
        }

        return 0.0;
    }

    /// <summary>
    /// Whether the pack holds the herbs this recipe consumes.
    ///
    /// <para>
    /// <b>The blank scroll is deliberately not counted here</b>, and it is not an oversight. This question is
    /// asked by a proposer that is about to walk to a shop for paper, so counting paper would have it answer
    /// "nothing can be written" and stay home. Mana is left out for the same kind of reason: it comes back on
    /// its own, and a missing herb does not.
    /// </para>
    /// </summary>
    public static bool Stocked(Mobile bot, CraftItem recipe)
    {
        var pack = bot?.Backpack;
        var resources = recipe?.Resources;

        if (pack == null || resources == null || resources.Count == 0)
        {
            return false;
        }

        for (var i = 0; i < resources.Count; i++)
        {
            var res = resources[i];

            if (res.ItemType == null || res.ItemType == typeof(BlankScroll))
            {
                continue;
            }

            // The attempt's own herbs, and the handful kept back to fight with. See Reserve: a scribe that
            // spends its last ginseng on a scroll it means to sell cannot cast the spell it just wrote.
            if (pack.GetAmount(res.ItemType) < Math.Max(1, res.Amount) + Math.Max(0, Reserve))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// The best scroll this bot could write right now, or null.
    ///
    /// <para>
    /// <b>Only what the book already holds, and that is the engine's rule rather than a preference.</b>
    /// <c>DefInscription.CanCraft</c> asks the scribe's own spellbook whether it knows the spell and refuses
    /// outright if it does not — "You don't have that spell!". So a scribe copies what it knows; it cannot
    /// write its way into a spell it has never seen.
    /// </para>
    ///
    /// <para>
    /// <b>This file used to assume the exact opposite</b>, and it cost two mages their whole evening. It
    /// picked the hardest thing its Inscribe could reliably make and, at equal difficulty, deliberately
    /// preferred a spell the book was <em>missing</em> — on the theory that a scribe fills its book by
    /// writing. Every one of those attempts was refused, silently, because the refusal is a message to a
    /// client and a bot has none: twenty blanks, fifty mana, nothing written, sixteen minutes, and not one
    /// line in the log.
    /// </para>
    ///
    /// <para>
    /// So the loop runs the other way round, and it is a better one: a book grows by <em>buying</em> scrolls —
    /// see <see cref="BotAcquire"/> — and each spell bought is one more the scribe can then copy and sell.
    /// Hardest first among what it knows, because difficulty is where the skill comes from; then whichever the
    /// market pays most for.
    /// </para>
    ///
    /// <para>
    /// <b>Except where somebody has put money down, and that exception had to be added because the ordering
    /// swallowed the answer.</b> "Hardest first, then price" reads like price is consulted; it is not, except
    /// between two recipes of identical difficulty, which almost never happens. Anything easier than the best
    /// so far was dropped <em>before</em> the market was asked — so a funded, standing, twice-raised order
    /// could sit on the board all evening while the scribe wrote something harder that nobody had asked for.
    /// A Mind Blast scroll raised its offer every eight minutes from 18:00 on 26.08.2026 and never once
    /// reached the front of this loop. The tailor's chooser had the same fault and the same cure, and the
    /// cure is the ordering rather than a weight: <b>paid work first, practice afterwards.</b>
    /// </para>
    ///
    /// <para>
    /// It does not become a permanent diet — a want closes when it is met, so the scribe writes the scroll,
    /// the order clears, and the next choice is the hardest thing again. The want's own life is the limit and
    /// no second number is needed.
    /// </para>
    /// </summary>
    public static CraftItem Choose(Mobile bot, out Type scroll, out int worth) =>
        Choose(bot, 0, out scroll, out worth);

    /// <summary>
    /// The same choice, with what the paper costs, so that what a scroll is made of can be weighed against
    /// what it will fetch. See <see cref="Markup"/> — the price out had never been compared to the price in.
    /// </summary>
    public static CraftItem Choose(Mobile bot, int paper, out Type scroll, out int worth)
    {
        scroll = null;
        worth = 0;

        var system = System;
        var pack = bot?.Backpack;

        if (system == null || pack == null)
        {
            return null;
        }

        var skill = bot.Skills[SkillName.Inscribe].Value;
        var pool = bot.ManaMax;
        var recipes = system.CraftItems;

        // The book is fetched once. Asking whether a spell is already known used to walk the pack looking for
        // the book on every candidate recipe, which is thirty-odd pack scans for one decision — and this runs
        // on the bot's own beat.
        var book = BotGrimoire.Book(bot);

        CraftItem best = null;
        var bestNeeds = -1.0;
        var bestWorth = 0;

        // Whatever somebody has funded, kept apart from whatever is hardest, and preferred outright at the
        // end. Two tallies rather than one weighting, so that neither question can quietly answer the other.
        CraftItem asked = null;
        var bestBid = 0;
        Type askedKind = null;
        var askedWorth = 0;

        for (var i = 0; i < recipes.Count; i++)
        {
            var recipe = recipes[i];
            var kind = recipe.ItemType;
            var spell = BotGrimoire.SpellOf(kind);

            // Scrolls only. The same system makes runebooks, spellbooks and bulk order books, and none of
            // those is what this trade is for.
            if (spell < 0)
            {
                continue;
            }

            var needs = Requirement(recipe);

            if (needs > skill - Margin || recipe.Mana > pool)
            {
                continue;
            }


            // Has anybody put money down for one of these. A scan of the open wants, which is a list of a
            // dozen or so — cheap enough to ask of every recipe, and asked here because the drop below would
            // otherwise throw the answer away before anybody looked at it.
            var bid = BotAuction.Best(kind);

            // Difficulty decides among practice pieces, and a funded order is not practice. For everything
            // else the drop stands: it is what keeps the pack from being counted for all forty writable
            // scrolls every time a mage wonders what to do, which was the first version's cost model in a new
            // hat. Only the few that somebody is paying for now get past it.
            if (bid <= 0 && best != null && needs < bestNeeds)
            {
                continue;
            }

            if (bid > 0 && bid <= bestBid)
            {
                continue;
            }

            if (!Stocked(bot, recipe))
            {
                continue;
            }

            // The book decides what may be attempted at all. Asked before the pack is counted, because it is
            // cheaper than counting herbs and it excludes far more.
            if (book?.HasSpell(spell) != true)
            {
                continue;
            }

            var asking = BotAuction.Worth(kind, BotGrimoire.ShopPrice(BotGrimoire.Circle(spell)));

            // <b>What it is made of, against what it will fetch.</b> Two rules out of the one comparison and
            // they are not the same rule: writing at a loss is refused outright, and what is written is
            // listed at no less than cost and the markup. The first keeps a scribe from turning gold into
            // scrolls worth less than the gold; the second is where its living comes from. Where the market
            // already pays more than that, the market's number stands.
            var cost = Cost(bot, recipe, paper);
            var revenue = Math.Max(asking, bid);

            if (cost > 0 && revenue < cost)
            {
                continue;
            }

            asking = Math.Max(asking, Asking(cost));

            if (bid > 0)
            {
                asked = recipe;
                bestBid = bid;
                askedKind = kind;
                askedWorth = asking;

                continue;
            }

            if (best != null && !Better(needs, asking, bestNeeds, bestWorth))
            {
                continue;
            }

            best = recipe;
            bestNeeds = needs;
            bestWorth = asking;

            scroll = kind;
            worth = asking;
        }

        if (asked == null)
        {
            return best;
        }

        scroll = askedKind;
        worth = askedWorth;

        return asked;
    }

    /// <summary>Harder wins, because that is where the skill is; at equal difficulty, the better price.</summary>
    private static bool Better(double needs, int worth, double bestNeeds, int bestWorth)
    {
        if (needs > bestNeeds)
        {
            return true;
        }

        if (needs < bestNeeds)
        {
            return false;
        }

        return worth > bestWorth;
    }

    /// <summary>
    /// One attempt. The engine takes it from here: it rolls, spends the mana, and either a scroll appears in
    /// the pack a moment later or the blank and the herbs are gone.
    ///
    /// Nothing is returned about the outcome, because the outcome is not known yet — the craft runs on its own
    /// timer. Whoever swings counts what is in the pack afterwards. The first version counted attempts and
    /// reported forty-four things made about a smith that had produced nothing.
    /// </summary>
    public static bool Swing(Mobile bot, CraftItem recipe, BaseTool pen)
    {
        if (bot == null || recipe == null || pen == null || System == null)
        {
            return false;
        }

        // No resource type to choose: a scroll is made of the herbs the recipe names and nothing else, so
        // there is no equivalent of "which cloth" for the engine to ask about.
        recipe.Craft(bot, System, null, pen);

        return true;
    }

    /// <summary>How many of that kind of scroll are in the pack.</summary>
    public static int Held(Mobile bot, Type kind) =>
        kind == null ? 0 : bot?.Backpack?.GetAmount(kind, true) ?? 0;

    /// <summary>Every scroll of that kind in the pack, so it can be written into a book or sold.</summary>
    public static List<SpellScroll> Gather(Mobile bot, Type kind)
    {
        List<SpellScroll> made = [];

        var pack = bot?.Backpack;

        if (pack == null || kind == null)
        {
            return made;
        }

        // A snapshot: writing into a book and listing on the market both move things out of the pack, which
        // mutates the list being read.
        List<Item> carried = [.. pack.Items];

        for (var i = 0; i < carried.Count; i++)
        {
            if (carried[i] is SpellScroll scroll && !scroll.Deleted && scroll.Movable && kind.IsInstanceOfType(scroll))
            {
                made.Add(scroll);
            }
        }

        return made;
    }
}
