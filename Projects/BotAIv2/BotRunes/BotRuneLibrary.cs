using System;
using System.Collections.Generic;
using Server.Guilds;
using Server.Items;
using Server.Logging;

namespace Server.BotAI.V2;

/// <summary>
/// The guild's rune library: a runebook in the hall's chest, or in the pack of the guild's keeper where there is no hall,
/// written from what the guild knows (<see cref="BotRuneShelf"/>), and the copies it lends to the guild's rune-mages.
///
/// <para>
/// <b>The library is the guild's record; the runes that travel are the mages' own.</b> A rune in a chest two hundred tiles
/// away takes nobody anywhere, so the pathfinder that marks a place keeps the rune in its own book
/// (<see cref="BotRunes.Satchel"/>) and the knowledge goes to the library at once, where Patrick can open the chest and
/// read every place the guild has stood. A rune-mage standing at the library — within <see cref="Reach"/> of the chest,
/// or of the keeper — is lent a copy of every rune its own book lacks, and <b>each copy costs it a blank rune</b>. Copying
/// a rune is not an engine operation; the blank is the engine's price of a rune, so what a library holds spreads through
/// the guild at the price of the blanks, and a guild with no blanks spreads nothing (<see cref="BotRuneBuyer"/>).
/// </para>
///
/// <para>
/// <b>The book outlives the body that carried it.</b> A hall's chest is saved with the world and its book with it; a
/// keeper's pack is not, since the population is raised afresh at every boot. A library missing from where it belongs is
/// written again from the shelf, as the keeper's knowledge was never in the book in the first place.
/// </para>
/// </summary>
public static class BotRuneLibrary
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotRuneLibrary));

    public const string Prefix = "the library of ";

    public static int Reach { get; set; } = 16;

    public static int KeeperReach { get; set; } = 8;

    public static int EveryMs { get; set; } = 5000;

    public static long Books { get; private set; }

    public static long Written { get; private set; }

    public static long Lent { get; private set; }

    public static long Unlent { get; private set; }

    public static long Moved { get; private set; }

    private static readonly Dictionary<string, Runebook> _books = new(StringComparer.Ordinal);

    private static readonly Dictionary<string, int> _written = new(StringComparer.Ordinal);

    public static Container Chest(Guild guild) =>
        BotEstate.Hall(guild) is { Deleted: false } hall ? BotAbode.Chest(hall) : null;

    public static BotMobile Keeper(Guild guild)
    {
        BotMobile best = null;
        var most = BotRunes.LeastMagery - 0.1;
        var members = guild?.Members;

        for (var i = 0; members != null && i < members.Count; i++)
        {
            if (members[i] is BotMobile { Deleted: false, Alive: true } mage && mage.Map != null && mage.Map != Map.Internal
                && mage.Skills[SkillName.Magery].Value > most)
            {
                most = mage.Skills[SkillName.Magery].Value;
                best = mage;
            }
        }

        return best ?? BotGuilds.Head(guild);
    }

    private static string Title(Guild guild) => Prefix + guild.Name;

    private static bool IsLibrary(Runebook book, Guild guild) =>
        book is { Deleted: false } && string.Equals(book.Description, Title(guild), StringComparison.Ordinal);

    public static Runebook Book(Guild guild, bool make)
    {
        if (guild == null)
        {
            return null;
        }

        var chest = Chest(guild);

        _books.TryGetValue(guild.Name, out var book);

        if (!IsLibrary(book, guild))
        {
            book = Search(guild, chest);
        }

        if (book == null)
        {
            if (!make)
            {
                return null;
            }

            book = new Runebook(0) { Description = Title(guild) };
            Books++;
            _written.Remove(guild.Name);

            if (!Place(book, guild, chest))
            {
                book.Delete();

                return null;
            }

            logger.Information(
                "{Guild} has a rune library now, {Where}",
                guild.Name,
                chest != null ? "in its hall's chest" : $"carried by {(book.RootParent as Mobile)?.Name}"
            );
        }
        else if (!Placed(book, guild, chest))
        {
            Place(book, guild, chest);
            Moved++;

            logger.Information(
                "The rune library of {Guild} has been moved {Where}",
                guild.Name,
                chest != null ? "into its hall's chest" : $"to {(book.RootParent as Mobile)?.Name}"
            );
        }

        _books[guild.Name] = book;

        return book;
    }

    private static Runebook Search(Guild guild, Container chest)
    {
        if (chest != null)
        {
            foreach (var book in chest.FindItemsByType<Runebook>())
            {
                if (IsLibrary(book, guild))
                {
                    return book;
                }
            }
        }

        var members = guild.Members;

        for (var i = 0; members != null && i < members.Count; i++)
        {
            if (members[i] is not BotMobile { Deleted: false, Backpack: { } pack })
            {
                continue;
            }

            foreach (var book in pack.FindItemsByType<Runebook>())
            {
                if (IsLibrary(book, guild))
                {
                    return book;
                }
            }
        }

        return null;
    }

    private static bool Placed(Runebook book, Guild guild, Container chest) =>
        chest != null
            ? ReferenceEquals(book.Parent, chest)
            : book.RootParent is BotMobile { Deleted: false } holder && holder.Guild == guild;

    private static bool Place(Runebook book, Guild guild, Container chest)
    {
        if (chest != null)
        {
            chest.DropItem(book);

            return true;
        }

        if (Keeper(guild) is not { Backpack: { } pack } keeper)
        {
            return false;
        }

        pack.DropItem(book);
        BotRunes.Bind(keeper, book);

        return true;
    }

    public static void Write(Guild guild)
    {
        var places = BotRuneShelf.Of(guild?.Name);

        if (places.Count == 0)
        {
            return;
        }

        var version = BotRuneShelf.Version(guild.Name);
        var book = Book(guild, true);

        if (book == null || _written.TryGetValue(guild.Name, out var done) && done == version && book.Entries.Count >= Math.Min(places.Count, 16))
        {
            return;
        }

        var entries = book.Entries;

        for (var i = entries.Count - 1; i >= 0; i--)
        {
            var entry = entries[i];
            var place = BotRuneShelf.Held(guild.Name, entry.Map, entry.Location, 0);

            if (place == null)
            {
                entries.RemoveAt(i);
            }
        }

        for (var i = 0; i < places.Count && entries.Count < 16; i++)
        {
            var place = places[i];

            if (!Listed(book, place))
            {
                entries.Add(new RunebookEntry(book, place.Spot, place.Map, place.Name));
                Written++;
            }
        }

        book.InvalidateProperties();
        _written[guild.Name] = version;
    }

    private static bool Listed(Runebook book, BotRunePlace place)
    {
        var entries = book.Entries;

        for (var i = 0; i < entries.Count; i++)
        {
            if (entries[i].Map == place.Map && entries[i].Location == place.Spot)
            {
                return true;
            }
        }

        return false;
    }

    public static int Lend(BotMobile mage, Guild guild, Runebook library)
    {
        if (library == null || !BotRunes.Mage(mage) || mage.Guild != guild || BotDungeon.Under(mage.Location))
        {
            return 0;
        }

        var holder = library.RootParent as Mobile;

        if (ReferenceEquals(holder, mage))
        {
            return 0;
        }

        var at = library.GetWorldLocation();
        var reach = holder != null ? KeeperReach : Reach;

        if (library.Map != mage.Map || !mage.InRange(at, reach))
        {
            return 0;
        }

        var entries = library.Entries;
        var own = (Runebook)null;
        var copies = 0;

        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];

            own ??= BotRunes.Satchel(mage, false);

            if (own != null && BotRunes.Has(own, entry.Map, entry.Location))
            {
                continue;
            }

            var blank = BotRunes.Blank(mage);

            if (blank == null)
            {
                Unlent++;

                break;
            }

            own ??= BotRunes.Satchel(mage, true);

            if (own == null || own.Entries.Count >= 16)
            {
                break;
            }

            blank.Delete();
            own.Entries.Add(new RunebookEntry(own, entry.Location, entry.Map, entry.Description));
            copies++;
        }

        if (copies > 0)
        {
            own.InvalidateProperties();
            Lent += copies;

            logger.Information(
                "{Name} the {Class} was lent {Copies} runes at {Guild}'s library and holds {Held} now",
                mage.Name,
                mage.Class?.Name,
                copies,
                guild.Name,
                own.Entries.Count
            );
        }

        return copies;
    }

    public static void Tick()
    {
        if (!BotRunes.Running)
        {
            return;
        }

        foreach (var guild in BotGuilds.Standing)
        {
            if (guild is not { Disbanded: false } || BotRuneShelf.Of(guild.Name).Count == 0)
            {
                continue;
            }

            Write(guild);

            if (!_books.TryGetValue(guild.Name, out var library) || library is not { Deleted: false })
            {
                continue;
            }

            var members = guild.Members;

            for (var i = 0; members != null && i < members.Count; i++)
            {
                if (members[i] is BotMobile member)
                {
                    Lend(member, guild, library);
                }
            }
        }
    }

    public static string Describe() =>
        $"{_books.Count} libraries ({Books} books made, {Moved} moved into a hall's chest or to a new keeper), {Written} runes written into them, {Lent} copies lent to rune-mages, {Unlent} not lent for want of a blank";

    public static void Forget()
    {
        _books.Clear();
        _written.Clear();
        Books = 0;
        Written = 0;
        Lent = 0;
        Unlent = 0;
        Moved = 0;
    }
}
