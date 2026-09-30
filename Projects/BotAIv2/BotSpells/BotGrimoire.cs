using System;
using System.Collections.Generic;
using Server.Items;
using Server.Logging;

namespace Server.BotAI.V2;

/// <summary>
/// What a book holds, what it is short of, and how a scroll becomes a spell in it.
///
/// <para>
/// <b>The book is the one thing a caster owns that grows.</b> It is bound at birth — weightless, kept through
/// death, never merchandise — and it starts with the two or three spells that make the class function at all.
/// Everything above that has to come from somewhere, and this is the file that knows where.
/// </para>
///
/// <para>
/// <b>The engine draws a line across the middle of the spell list, and the whole economy of magic on this
/// shard is that line.</b> A mage vendor stocks the first three circles — twenty-four scrolls, at twelve,
/// twenty-two and thirty-two gold — and stops. The other forty spells are sold by nobody at any price: they
/// come from a monster's corpse or from somebody with the Inscribe skill, a blank scroll and a handful of
/// herbs. There is no hunting in this version, so on this shard, today, they come from exactly one place —
/// another bot. That is not a rule anybody wrote; it is the shape of the content, and it is the reason a
/// caster's ambition is a market rather than a shopping list.
/// </para>
/// </summary>
public static class BotGrimoire
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotGrimoire));

    public const int Spells = 64;

    public const int PerCircle = 8;

    public const int ShopCircles = 3;

    private static readonly Dictionary<Type, int> _idOf = [];

    private static readonly Type[] _scrollOf = new Type[Spells];

    public static int Known { get; private set; }

    public static void Read()
    {
        if (Known > 0)
        {
            return;
        }

        var types = Loot.RegularScrollTypes;

        for (var i = 0; i < types.Length; i++)
        {
            var type = types[i];

            if (type == null || _idOf.ContainsKey(type))
            {
                continue;
            }

            var sample = type.CreateInstance<SpellScroll>();

            if (sample == null)
            {
                continue;
            }

            var id = sample.SpellID;

            sample.Delete();

            if (id < 0 || id >= Spells)
            {
                continue;
            }

            _idOf[type] = id;
            _scrollOf[id] = type;

            Known++;
        }

        logger.Information(
            "Read {Known} of {Total} scrolls; circles 1 to {Shop} can be bought and the rest have to be written",
            Known,
            Spells,
            ShopCircles
        );
    }

    public static int Circle(int spellId) => spellId / PerCircle + 1;

    public static Type ScrollFor(int spellId) =>
        spellId >= 0 && spellId < Spells ? _scrollOf[spellId] : null;

    public static int SpellOf(Type scroll) =>
        scroll != null && _idOf.TryGetValue(scroll, out var id) ? id : -1;

    public static bool Sold(int spellId) => Circle(spellId) <= ShopCircles;

    public static int ShopPrice(int circle) => 12 + 10 * (Math.Max(1, circle) - 1);

    public static Spellbook Book(Mobile bot)
    {
        if (bot == null)
        {
            return null;
        }

        var pack = bot.Backpack;

        if (pack != null)
        {
            foreach (var book in pack.FindItemsByType<Spellbook>())
            {
                if (book is { Deleted: false, SpellbookType: SpellbookType.Regular })
                {
                    return book;
                }
            }
        }

        return bot.FindItemOnLayer(Layer.OneHanded) as Spellbook is { SpellbookType: SpellbookType.Regular } held
            ? held
            : bot.FindItemOnLayer(Layer.TwoHanded) as Spellbook is { SpellbookType: SpellbookType.Regular } other
                ? other
                : null;
    }

    public static bool Holds(Mobile bot, int spellId) => Book(bot)?.HasSpell(spellId) == true;

    public static int Count(Mobile bot) => Book(bot)?.SpellCount ?? 0;

    public static int Missing(Mobile bot)
    {
        var book = Book(bot);

        if (book == null)
        {
            return -1;
        }

        if (bot is BotMobile { Class.BookFirst: { Length: > 0 } first })
        {
            for (var i = 0; i < first.Length; i++)
            {
                var wanted = first[i];

                if (wanted >= 0 && wanted < Spells && _scrollOf[wanted] != null && !book.HasSpell(wanted))
                {
                    return wanted;
                }
            }
        }

        var travel = BotRunes.Wanted(bot, book);

        if (travel >= 0 && _scrollOf[travel] != null)
        {
            return travel;
        }

        for (var id = 0; id < Spells; id++)
        {
            if (_scrollOf[id] != null && !book.HasSpell(id))
            {
                return id;
            }
        }

        return -1;
    }

    public static bool Wants(Mobile bot, Type scroll)
    {
        var id = SpellOf(scroll);

        return id >= 0 && !Holds(bot, id);
    }

    public static bool Write(Mobile bot, SpellScroll scroll) => Write(bot, scroll, out _);

    public static bool Write(Mobile bot, SpellScroll scroll, out string why)
    {
        why = null;

        var book = Book(bot);

        if (book == null)
        {
            why = "it is not carrying a spellbook";

            return false;
        }

        if (scroll is not { Deleted: false })
        {
            why = "the scroll went";

            return false;
        }

        var id = scroll.SpellID;

        if (book.HasSpell(id))
        {
            why = "the book already had it";

            return false;
        }

        book.OnDragDrop(bot, scroll);

        if (book.HasSpell(id))
        {
            return true;
        }

        why = Spellbook.GetTypeForSpell(id) != book.SpellbookType
            ? $"spell {id} belongs to a {Spellbook.GetTypeForSpell(id)} book, not this one"
            : $"the engine refused spell {id} for a {book.SpellbookType} book";

        return false;
    }

    public static string Describe() =>
        $"{Known} scrolls mapped, circles 1-{ShopCircles} on shop shelves and {Spells - ShopCircles * PerCircle} that have to be written";
}
