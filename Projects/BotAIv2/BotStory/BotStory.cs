using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Threading;
using Server.Guilds;
using Server.Logging;
using Server.Text;

namespace Server.BotAI.V2;

public static class BotStory
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotStory));

    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    public static bool Running { get; set; } = true;

    public static int TickMs { get; set; } = 2000;

    public static int EveryMs { get; set; } = 300000;

    public static int PublishEveryMs { get; set; } = 10000;

    public static int SettleMs { get; set; } = 120000;

    public static int MostNotes { get; set; } = 40;

    public static int MostBooks { get; set; } = 1500;

    public static int OldAgeDays { get; set; } = 14;

    public static bool Voiced { get; set; } = true;

    public static int VoicePerHour { get; set; } = 6;

    public static string VoiceModel { get; set; } = "";

    public static int VoiceLeastFacts { get; set; } = 3;

    public static int VoiceMostChars { get; set; } = 1200;

    public static int VoiceMostWords { get; set; } = 150;

    public static int MostWanted { get; set; } = 20;

    public static Func<BotStoryAsk, Action<string, long>, bool> Ask { get; set; }

    public static long BornNoted { get; private set; }

    public static long Opened { get; private set; }

    public static long FromEvents { get; private set; }

    public static long FromWalk { get; private set; }

    public static long Trimmed { get; private set; }

    public static long Repeats { get; private set; }

    public static long Looks { get; private set; }

    public static long Builds { get; private set; }

    public static long BuildTicks { get; private set; }

    public static long BuildMostTicks { get; private set; }

    public static long Published { get; private set; }

    public static long Requests { get; private set; }

    public static long Unknown { get; private set; }

    public static long VoiceAsked { get; private set; }

    public static long VoiceAnswered { get; private set; }

    public static long VoiceNone { get; private set; }

    public static long VoiceKept { get; private set; }

    public static long VoiceBusy { get; private set; }

    public static long VoiceStale { get; private set; }

    public static long VoiceLost { get; private set; }

    public static long VoiceWaitedMs { get; private set; }

    public static long CacheHits { get; private set; }

    public static long CacheMisses { get; private set; }

    public static long Faults { get; private set; }

    public static long TestCrowns { get; private set; }

    private static readonly Dictionary<string, long> _thrown = new(StringComparer.Ordinal);

    private static readonly Dictionary<string, long> _byKind = new(StringComparer.Ordinal);

    private static long _offLoop;

    private static readonly Dictionary<string, BotStoryBook> _books = new(StringComparer.Ordinal);

    private static readonly Dictionary<string, string> _json = new(StringComparer.OrdinalIgnoreCase);

    private static readonly BotStoryChapter[] _chapters = NewChapters();

    private static readonly List<string> _wanted = new(8);

    private static readonly Queue<long> _asks = new(8);

    private static int _cursor;

    private static int _voiceCursor;

    private static bool _asking;

    private static long _askedTick;

    private static long _publishedTick;

    private static bool _publishedOnce;

    private static bool _changed;

    private static bool _owed;

    private static bool _sealed;

    private static int _generation;

    private static long _startedTick;

    private static bool _started;

    private static bool Booting(long now) => !_started || now - _startedTick < Math.Max(0, SettleMs);

    public static void Started(long now)
    {
        _startedTick = now;
        _started = true;
        _sealed = false;

        if (!Running)
        {
            return;
        }

        var bots = BotPopulation.Bots;

        for (var i = 0; i < bots.Count; i++)
        {
            if (bots[i] is { Deleted: false } bot && !string.IsNullOrEmpty(bot.Name) && !_books.ContainsKey(bot.Name))
            {
                Book(bot.Name, bot);
            }
        }
    }

    private sealed class Shelf
    {
        public static readonly Shelf Empty = new(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase), "{\"stories\":[]}");

        public Shelf(Dictionary<string, string> stories, string index)
        {
            Stories = stories;
            Index = index;
        }

        public readonly Dictionary<string, string> Stories;

        public readonly string Index;
    }

    private static volatile Shelf _shelf = Shelf.Empty;

    private static BotStoryChapter[] NewChapters()
    {
        var chapters = new BotStoryChapter[BotStages.Count];

        for (var i = 0; i < chapters.Length; i++)
        {
            chapters[i] = new BotStoryChapter();
        }

        return chapters;
    }

    public static int Count => _books.Count;

    public static IReadOnlyDictionary<string, BotStoryBook> Books => _books;

    public static string Story(string name, bool poll = false)
    {
        var shelf = _shelf;

        if (string.IsNullOrWhiteSpace(name))
        {
            return shelf.Index;
        }

        name = name.Trim();

        var json = shelf.Stories.TryGetValue(name, out var found) ? found : null;

        if (!poll || json == null)
        {
            Core.LoopContext.Post(() => Wanted(name));
        }

        return json;
    }

    public static void Heard(BotEvent e)
    {
        if (!Running || _sealed || e?.Kind == null)
        {
            return;
        }

        if (Thread.CurrentThread != Core.Thread)
        {
            Interlocked.Increment(ref _offLoop);

            return;
        }

        try
        {
            BotChronicle.Heard(e);
            Noted(e);
        }
        catch (Exception ex)
        {
            Fault(ex, "an event");
        }
    }

    private static void Noted(BotEvent e)
    {
        var bot = e.Bot;
        var text = e.Text;

        if (bot == null || string.IsNullOrEmpty(text) || Booting(Core.TickCount))
        {
            return;
        }

        switch (e.Kind)
        {
            case "officers":
                {
                    if (text.StartsWith("joined ", StringComparison.Ordinal) || text.StartsWith("left ", StringComparison.Ordinal))
                    {

                        Note(bot, BotStage.Guild, "guild", $"{bot} {text}", 40.0);

                        if (_books.TryGetValue(bot, out var book))
                        {
                            book.SeenGuild = e.Guild;
                        }
                    }
                    else if (text.StartsWith("appointed ", StringComparison.Ordinal))
                    {
                        Note(bot, BotStage.Guild, "office", $"{bot} was {text}", 45.0);
                    }
                    else if (text.StartsWith("won the guilds' bout", StringComparison.Ordinal))
                    {
                        Note(bot, BotStage.Deeds, "bout", $"{bot} {text}", 45.0);
                    }

                    break;
                }
            case "politics":
                {
                    if (text.Contains(" re-elected head of ", StringComparison.Ordinal))
                    {
                        break;
                    }

                    if (text.Contains(" elected head of ", StringComparison.Ordinal))
                    {
                        if (text.StartsWith($"{bot} elected head of ", StringComparison.Ordinal))
                        {
                            Note(bot, BotStage.Guild, "office", text.Replace(" elected head of ", " was elected head of ", StringComparison.Ordinal), 55.0);
                        }
                    }
                    else if (e.Channel == "steward")
                    {
                        if (StewardNamed(text, e.Guild) is { } steward)
                        {
                            Note(steward, BotStage.Guild, "office", BotChronicle.Steward(text), 50.0);
                        }
                        else if (DeposedSteward(text, e.Guild) is { } deposed)
                        {
                            Note(deposed, BotStage.Guild, "office", text, 50.0);
                        }
                    }
                    else if (text.Contains($" deposed {bot}", StringComparison.Ordinal))
                    {
                        Note(bot, BotStage.Guild, "office", text, 50.0);
                    }

                    break;
                }
            case "relic":
                {
                    Note(bot, BotStage.Deeds, "relic", $"{bot} {text}", 60.0);

                    break;
                }
            case "crime":
                {

                    if (text.StartsWith("stole ", StringComparison.Ordinal))
                    {
                        Note(bot, BotStage.Deeds, "crime", $"{bot} {text}", 55.0);
                    }
                    else if (text.StartsWith("recruited by ", StringComparison.Ordinal) || text.StartsWith("repentant ", StringComparison.Ordinal)
                             || text.StartsWith("ransomed by ", StringComparison.Ordinal))
                    {
                        Note(bot, BotStage.Deeds, "crime", $"{bot} was {text}", 35.0, own: true);
                    }
                    else if (text.StartsWith("caught by ", StringComparison.Ordinal) || text.StartsWith("posted as wanted ", StringComparison.Ordinal))
                    {
                        Note(bot, BotStage.Deeds, "crime", $"{bot} was {text}", 25.0, own: true);
                    }
                    else if (text.StartsWith("takes the bounty ", StringComparison.Ordinal))
                    {
                        Note(bot, BotStage.Deeds, "bounty", $"{bot} took the bounty {text["takes the bounty ".Length..]}", 30.0);
                    }

                    break;
                }
            case "residence":
                {
                    if (text.StartsWith("lives in ", StringComparison.Ordinal))
                    {
                        Note(bot, BotStage.Home, "home", $"{bot} came to live in {text["lives in ".Length..]}", 30.0);
                    }
                    else if (text.StartsWith("moving from ", StringComparison.Ordinal))
                    {
                        Note(bot, BotStage.Home, "home", $"{bot} moved from {text["moving from ".Length..]}", 30.0, own: true);
                    }

                    break;
                }
            case "house":
                {

                    var settled = text.Replace(" settles in ", " settled in ", StringComparison.Ordinal);

                    Note(bot, BotStage.Guild, "hall", $"{settled}, with {bot} at its head", 40.0, key: text);

                    break;
                }
            case "infirmary":
            case "library":
            case "tavern":
            case "ragman":
                {
                    if (text.StartsWith("opened ", StringComparison.Ordinal))
                    {
                        Note(bot, BotStage.Apprenticeship, $"opened:{e.Kind}", $"{bot} {text}", 25.0, true);
                    }

                    break;
                }
            case "market":
                {
                    if (text.StartsWith("opened a shop ", StringComparison.Ordinal))
                    {

                        var first = _books.TryGetValue(bot, out var opener) && opener.Born ? "first " : "";

                        Note(bot, BotStage.Apprenticeship, "opened:shop", $"{bot} {first}{text}", 20.0, true);
                    }

                    break;
                }
        }
    }

    private static string StewardNamed(string text, string guild)
    {
        var at = text.IndexOf(" is the steward of ", StringComparison.Ordinal);

        return at > 0 ? Before(text[..at], guild) : null;
    }

    private static string DeposedSteward(string text, string guild)
    {
        const string deposed = " deposed its steward ";

        var at = text.IndexOf(deposed, StringComparison.Ordinal);

        if (at < 0)
        {
            return null;
        }

        var rest = text[(at + deposed.Length)..];
        var colon = rest.IndexOf(':');

        return Before(colon > 0 ? rest[..colon] : rest, guild);
    }

    private static string Before(string who, string guild)
    {
        var of = guild != null && who.EndsWith($" of {guild}", StringComparison.Ordinal)
            ? who.Length - guild.Length - 4
            : who.LastIndexOf(" of ", StringComparison.Ordinal);

        return of > 0 ? who[..of] : null;
    }

    public static BotStoryBook Note(string name, BotStage stage, string kind, string text, double weight, bool once = false, bool own = false, string key = null)
    {
        if (!Running || _sealed || string.IsNullOrEmpty(name) || BotLives.Find(name) == null)
        {
            return null;
        }

        var book = Book(name, BotLives.Body(name));

        if (book == null)
        {
            return null;
        }

        if (once && book.Has(kind))
        {
            Repeats++;

            return null;
        }

        if (own)
        {
            text = text.Replace(" its ", $" {BotStoryWriter.Possessive(book)} ", StringComparison.Ordinal);
        }

        if (Write(book, stage, kind, text, weight, DateTime.UtcNow, key))
        {
            FromEvents++;
            Counted(kind);
        }

        return book;
    }

    public static void Born(BotMobile bot, bool fresh)
    {
        if (!Running || _sealed || bot?.Name == null)
        {
            return;
        }

        try
        {

            if (!fresh)
            {
                return;
            }

            if (_books.Remove(bot.Name))
            {
                _json.Remove(bot.Name);
            }

            var now = DateTime.UtcNow;
            var book = Open(bot, now);

            book.Born = true;

            var town = book.Town != null ? $" in {book.Town}" : "";

            Write(book, BotStage.Youth, "born", $"{bot.Name} was born {BotStoryWriter.A(Kind(book))}{town}", 100.0, now);
            BornNoted++;
        }
        catch (Exception ex)
        {
            Fault(ex, "a birth");
        }
    }

    private static BotStoryBook Book(string name, BotMobile body)
    {
        if (_books.TryGetValue(name, out var book))
        {
            return book;
        }

        if (body == null || body.Deleted)
        {
            return null;
        }

        var now = DateTime.UtcNow;

        book = Open(body, now);

        var town = book.Town != null ? $" living in {book.Town}" : "";

        Write(book, BotStage.Youth, "seen", $"{book.Name} was {BotStoryWriter.A(Kind(book))}{town}", 10.0, now);

        if (book.SeenGuild != null)
        {
            Write(book, BotStage.Guild, "seen", $"{book.Name} belonged to {book.SeenGuild}", 10.0, now);
        }

        if (Level(book.SeenRank) >= 2)
        {
            Write(book, BotStage.Apprenticeship, "seen", $"{book.Name} was already {BotStoryWriter.A(book.SeenRank)}", 10.0, now);
        }

        if (book.SeenHouse != null)
        {
            Write(book, BotStage.Home, "seen", $"{book.Name} had a house of {BotStoryWriter.Possessive(book)} own {book.SeenHouse}", 10.0, now);
        }

        if (book.SeenChampion)
        {
            Write(book, BotStage.Deeds, "seen", $"{book.Name} was the champion of the island", 10.0, now);
        }

        if (book.SeenVeteran)
        {
            Write(book, BotStage.Deeds, "seen", $"{book.Name} was already called {BotTraits.VeteranTitle}", 10.0, now);
        }

        return book;
    }

    private static BotStoryBook Open(BotMobile body, DateTime now)
    {
        var book = new BotStoryBook(body.Name)
        {
            FirstSeen = now,
            LastSeen = now,
            Female = body.Female,
            Class = body.Class?.Name,
            People = BotPeoples.Of(body.Name)?.Name,
            Town = Town(body),
            SeenGuild = (body.Guild as Guild)?.Name,
            SeenRank = body.BotRank,
            SeenHouse = House(body),
            SeenChampion = body.NameHue == BotMobile.ChampionHue,
            SeenVeteran = BotLives.Find(body.Name) is { Veteran: true }
        };

        book.OpenedTick = Core.TickCount;
        _books[book.Name] = book;
        Opened++;
        _changed = true;

        if (_books.Count > Math.Max(64, MostBooks))
        {
            Shed();
        }

        return book;
    }

    private static void Shed()
    {
        BotStoryBook oldest = null;

        foreach (var (_, book) in _books)
        {
            if (BotLives.Body(book.Name) == null && (oldest == null || book.LastSeen < oldest.LastSeen))
            {
                oldest = book;
            }
        }

        if (oldest != null)
        {
            _books.Remove(oldest.Name);
            _json.Remove(oldest.Name);
        }
    }

    private static bool Write(BotStoryBook book, BotStage stage, string kind, string text, double weight, DateTime at, string key = null)
    {
        text = Plain(text);

        if (book.Said(text, key, at))
        {
            Repeats++;

            return false;
        }

        book.Notes.Add(new BotStoryNote { Stage = stage, Kind = kind, Text = text, Key = key, At = at, Weight = weight });
        _changed = true;

        while (book.Notes.Count > Math.Max(BotStages.Count, MostNotes))
        {
            Trim(book);
        }

        return true;
    }

    private static void Counted(string kind) => _byKind[kind] = _byKind.GetValueOrDefault(kind) + 1;

    private static void Trim(BotStoryBook book)
    {
        Span<int> perStage = stackalloc int[BotStages.Count];

        for (var i = 0; i < book.Notes.Count; i++)
        {
            perStage[(int)book.Notes[i].Stage]++;
        }

        var drop = -1;

        for (var pass = 0; pass < 2 && drop < 0; pass++)
        {
            for (var i = 0; i < book.Notes.Count; i++)
            {
                var note = book.Notes[i];

                if ((pass == 0 && perStage[(int)note.Stage] < 2) || note.Kind == "born")
                {
                    continue;
                }

                if (drop < 0 || note.Weight < book.Notes[drop].Weight)
                {
                    drop = i;
                }
            }
        }

        book.Notes.RemoveAt(drop < 0 ? 0 : drop);
        Trimmed++;
    }

    public static void Tick(long now)
    {
        if (!Running || _sealed)
        {
            return;
        }

        try
        {
            Walk(now);
            BotChronicle.Tick(now);
            Voice(now);
            Publish(now, false);
        }
        catch (Exception ex)
        {
            Fault(ex, "the clock");
        }
    }

    private static void Walk(long now)
    {
        var bots = BotPopulation.Bots;

        if (bots.Count == 0)
        {
            return;
        }

        var many = (int)Math.Ceiling(bots.Count * (double)Math.Max(250, TickMs) / Math.Max(TickMs, EveryMs));

        for (var n = 0; n < Math.Max(1, many); n++)
        {
            if (_cursor >= bots.Count)
            {
                _cursor = 0;
            }

            var bot = bots[_cursor++];

            if (bot is not { Deleted: false } || string.IsNullOrEmpty(bot.Name))
            {
                continue;
            }

            Look(bot);
        }
    }

    private static void Look(BotMobile bot)
    {
        Looks++;

        var book = Book(bot.Name, bot);

        if (book == null)
        {
            return;
        }

        var now = DateTime.UtcNow;
        var life = BotLives.Find(bot.Name);

        book.LastSeen = now;

        if (book.Born && book.Town == null && BotResidence.NameOf(bot) is { } town)
        {
            book.Town = town;

            for (var i = 0; i < book.Notes.Count; i++)
            {
                if (book.Notes[i].Kind == "born")
                {
                    book.Notes[i].Text = $"{book.Name} was born {BotStoryWriter.A(Kind(book))} in {town}";
                }
            }
        }

        book.Female = bot.Female;

        var tick = Core.TickCount;

        if (book.OpenedTick is { } opened)
        {

            Absorb(book, bot, life, now);

            if (book.Born && !Booting(tick))
            {
                Guilds(book, bot, now);
            }

            if (tick - opened >= Math.Max(0, SettleMs))
            {
                book.OpenedTick = null;
            }
        }
        else if (!Booting(tick))
        {
            Guilds(book, bot, now);
            Rank(book, bot, now);
            Houses(book, bot, now);
            Champion(book, bot, now);

            if (life is { Veteran: true } && !book.SeenVeteran)
            {
                book.SeenVeteran = true;
                Walked(book, BotStage.Deeds, "veteran", $"{book.Name} was called {BotTraits.VeteranTitle} from then on, for {BotTraits.VeteranKills} kills and more", 55.0, life.VeteranAt == default ? now : life.VeteranAt);
            }
        }

        Build(book, life, bot, false);
    }

    private static void Absorb(BotStoryBook book, BotMobile bot, BotLife life, DateTime now)
    {
        var guild = (bot.Guild as Guild)?.Name;

        if (!book.Born && !string.Equals(guild, book.SeenGuild, StringComparison.Ordinal))
        {
            book.SeenGuild = guild;
            Seen(book, BotStage.Guild, guild != null ? $"{book.Name} belonged to {guild}" : null, now);
        }

        var rank = bot.BotRank;

        if (!string.IsNullOrEmpty(rank) && Level(rank) > Level(book.SeenRank))
        {
            book.SeenRank = rank;
            Seen(book, BotStage.Apprenticeship, Level(rank) >= 2 ? $"{book.Name} was already {BotStoryWriter.A(rank)}" : null, now);
        }

        var house = House(bot);

        if (!string.Equals(house, book.SeenHouse, StringComparison.Ordinal))
        {
            book.SeenHouse = house;
            Seen(book, BotStage.Home, house != null ? $"{book.Name} had a house of {BotStoryWriter.Possessive(book)} own {house}" : null, now);
        }

        var champion = bot.NameHue == BotMobile.ChampionHue;

        if (champion != book.SeenChampion)
        {
            book.SeenChampion = champion;
            Seen(book, BotStage.Deeds, champion ? $"{book.Name} was the champion of the island" : null, now, "champion");
        }

        if (life is { Veteran: true } && !book.SeenVeteran)
        {
            book.SeenVeteran = true;
            Seen(book, BotStage.Deeds, $"{book.Name} was already called {BotTraits.VeteranTitle}", now, "called");
        }
    }

    private static void Seen(BotStoryBook book, BotStage stage, string text, DateTime now, string about = null)
    {
        if (book.Born)
        {
            return;
        }

        for (var i = 0; i < book.Notes.Count; i++)
        {
            var note = book.Notes[i];

            if (note.Kind != "seen" || note.Stage != stage || (about != null && !note.Text.Contains(about, StringComparison.Ordinal)))
            {
                continue;
            }

            if (text == null)
            {
                book.Notes.RemoveAt(i);
            }
            else
            {
                note.Text = Plain(text);
            }

            _changed = true;

            return;
        }

        if (text != null)
        {
            Write(book, stage, "seen", text, 10.0, now);
        }
    }

    private static void Walked(BotStoryBook book, BotStage stage, string kind, string text, double weight, DateTime at)
    {
        if (Write(book, stage, kind, text, weight, at))
        {
            FromWalk++;
            Counted(kind);
        }
    }

    private static void Guilds(BotStoryBook book, BotMobile bot, DateTime now)
    {
        var guild = (bot.Guild as Guild)?.Name;

        if (string.Equals(guild, book.SeenGuild, StringComparison.Ordinal))
        {
            return;
        }

        var old = book.SeenGuild;
        var tick = Core.TickCount;
        var founded = guild != null && string.Equals(BotChronicle.FounderOf(guild, tick, 3600000), book.Name, StringComparison.Ordinal);

        book.SeenGuild = guild;

        if (old == null)
        {
            Walked(book, BotStage.Guild, founded ? "founded" : "guild", founded ? $"{book.Name} founded {guild}" : $"{book.Name} joined {guild}", founded ? 60.0 : 40.0, now);
        }
        else if (guild == null)
        {
            Walked(book, BotStage.Guild, "guild", BotChronicle.ClosedLately(old, tick, 3600000) ? $"{book.Name}'s guild, {old}, closed" : $"{book.Name} left {old}", 35.0, now);
        }
        else
        {
            Walked(book, BotStage.Guild, founded ? "founded" : "guild", founded ? $"{book.Name} left {old} and founded {guild}" : $"{book.Name} left {old} for {guild}", founded ? 60.0 : 40.0, now);
        }
    }

    private static void Rank(BotStoryBook book, BotMobile bot, DateTime now)
    {
        var rank = bot.BotRank;

        if (string.IsNullOrEmpty(rank) || string.Equals(rank, book.SeenRank, StringComparison.Ordinal))
        {
            return;
        }

        var level = Level(rank);

        if (level <= Level(book.SeenRank))
        {
            return;
        }

        book.SeenRank = rank;

        if (level >= 2)
        {
            Walked(book, BotStage.Apprenticeship, "rank", $"{book.Name} became {BotStoryWriter.A(rank)}", 15.0 + 5.0 * level, now);
        }
    }

    private static readonly string[] Levels =
        ["Neophyte", "Novice", "Apprentice", "Journeyman", "Expert", "Adept", "Master", "Grandmaster", "Elder", "Legendary"];

    public static int Level(string rank)
    {
        if (string.IsNullOrEmpty(rank))
        {
            return -1;
        }

        var space = rank.IndexOf(' ');
        var word = space > 0 ? rank[..space] : rank;

        for (var i = Levels.Length - 1; i >= 0; i--)
        {
            if (string.Equals(word, Levels[i], StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return word is "Tatsujin" or "Shinobi" ? 8 : word is "Kengo" or "Ka-ge" ? 9 : -1;
    }

    private static void Houses(BotStoryBook book, BotMobile bot, DateTime now)
    {
        var place = House(bot);

        if (string.Equals(place, book.SeenHouse, StringComparison.Ordinal))
        {
            return;
        }

        var old = book.SeenHouse;

        book.SeenHouse = place;

        if (place == null)
        {
            Walked(book, BotStage.Home, "house", $"{book.Name} no longer had the house {old}", 30.0, now);
        }
        else if (old == null)
        {
            Walked(book, BotStage.Home, "house", $"{book.Name} bought a house of {BotStoryWriter.Possessive(book)} own {place}", 50.0, now);
        }
        else
        {
            Walked(book, BotStage.Home, "house", $"{book.Name} moved into a house {place}", 45.0, now);
        }
    }

    private static void Champion(BotStoryBook book, BotMobile bot, DateTime now)
    {
        var champion = bot.NameHue == BotMobile.ChampionHue;

        if (champion == book.SeenChampion)
        {
            return;
        }

        book.SeenChampion = champion;

        if (!champion)
        {

            if (HeldTitle(book))
            {
                Walked(book, BotStage.Deeds, "champion", $"{book.Name} lost the champion's title", 40.0, now);
            }

            return;
        }

        var guild = (bot.Guild as Guild)?.Name;

        Walked(book, BotStage.Deeds, "champion", $"{book.Name} became the champion of the island", 70.0, now);
        BotChronicle.Record("champion", $"{book.Name} the {BotStoryWriter.Words(bot.Class?.Name)}{(guild != null ? $" of {guild}" : "")} became the champion of the island", 75.0);
    }

    private static bool HeldTitle(BotStoryBook book)
    {
        for (var i = book.Notes.Count - 1; i >= 0; i--)
        {
            var note = book.Notes[i];

            if (note.Kind == "champion")
            {
                return !note.Text.Contains("lost the champion's title", StringComparison.Ordinal);
            }

            if (note.Baseline && note.Text.Contains("was the champion of the island", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    public static void TestCrowned(BotMobile bot)
    {
        if (!Running || _sealed || bot?.Name == null || !_books.TryGetValue(bot.Name, out var book))
        {
            return;
        }

        book.SeenChampion = true;
        TestCrowns++;
    }

    public static void Crowned(BotMobile bot, string how)
    {
        if (!Running || _sealed || bot?.Name == null)
        {
            return;
        }

        try
        {
            var guild = (bot.Guild as Guild)?.Name;
            var said = string.IsNullOrWhiteSpace(how) ? "became" : $"{how.Trim()} and became";

            BotChronicle.Record("champion", $"{bot.Name} the {BotStoryWriter.Words(bot.Class?.Name)}{(guild != null ? $" of {guild}" : "")} {said} the champion of the island", 75.0);

            if (Note(bot.Name, BotStage.Deeds, "champion", $"{bot.Name} {said} the champion of the island", 70.0) is { } book)
            {
                book.SeenChampion = true;
            }
        }
        catch (Exception ex)
        {
            Fault(ex, "a crowning");
        }
    }

    public static void Defended(string champion, string challenger, int defences)
    {
        if (!Running || _sealed || champion == null)
        {
            return;
        }

        try
        {
            var against = challenger != null ? $" against {challenger}" : "";

            BotChronicle.Record("champion", $"{champion} defended the title of champion{against}, {defences} {(defences == 1 ? "defence" : "defences")} in this reign", 45.0);
            Note(champion, BotStage.Deeds, "defence", $"{champion} defended the title of champion{against}", 45.0);
        }
        catch (Exception ex)
        {
            Fault(ex, "a title defence");
        }
    }

    private static string House(BotMobile bot) =>
        BotAbode.Of(bot) is { Deleted: false } house ? BotBiography.Place(house.Map, house.Location) : null;

    private static string Town(BotMobile bot) =>
        BotResidence.NameOf(bot)
        ?? (bot.Map != null && bot.Map != Map.Internal && bot.Location != Point3D.Zero ? BotTowns.Nearest(bot.Location)?.Name : null);

    private static string Kind(BotStoryBook book)
    {
        var klass = BotStoryWriter.Words(book.Class);

        return string.IsNullOrEmpty(book.People) ? klass : $"{book.People.ToLowerInvariant()} {klass}";
    }

    private static void Build(BotStoryBook book, BotLife life, BotMobile body, bool read)
    {
        var started = Stopwatch.GetTimestamp();
        var now = DateTime.UtcNow;
        var count = BotStoryWriter.Build(book, life, body, now, _chapters);
        var lived = 0;
        var unvoiced = 0;

        for (var s = 0; s < _chapters.Length; s++)
        {
            var chapter = _chapters[s];

            if (!chapter.Present)
            {
                continue;
            }

            if (chapter.Lived)
            {
                lived++;
            }

            if (Voiceable(book, chapter))
            {
                unvoiced++;
            }

            if (read && chapter.Lived && chapter.Facts.Count >= Math.Max(1, VoiceLeastFacts))
            {
                if (chapter.Voiced != null)
                {
                    CacheHits++;
                }
                else
                {
                    CacheMisses++;
                }
            }
        }

        book.Chapters = count;
        book.Lived = lived;
        book.Unvoiced = unvoiced;

        _json[book.Name] = Json(book, life, body, count, lived);
        _changed = true;

        var spent = Stopwatch.GetTimestamp() - started;

        Builds++;
        BuildTicks += spent;

        if (spent > BuildMostTicks)
        {
            BuildMostTicks = spent;
        }
    }

    private static bool Voiceable(BotStoryBook book, BotStoryChapter chapter) =>
        chapter.Present && chapter.Lived && chapter.Facts.Count >= Math.Max(1, VoiceLeastFacts)
        && (book.Voiced[(int)chapter.Stage] is not { } voiced || voiced.Hash != chapter.Hash);

    private static string Json(BotStoryBook book, BotLife life, BotMobile body, int count, int lived) =>
        BotJson.Object(
            w =>
            {
                w.WriteString("bot", book.Name);
                w.WriteString("class", body?.Class?.Name ?? book.Class);
                BotJson.StringOrNull(w, "people", book.People);
                BotJson.StringOrNull(w, "guild", (body?.Guild as Guild)?.Name ?? book.SeenGuild);
                BotJson.StringOrNull(w, "rank", body?.BotRank ?? book.SeenRank);
                w.WriteBoolean("born", book.Born);
                w.WriteString("firstSeen", book.FirstSeen.ToLocalTime().ToString("yyyy-MM-dd HH:mm", Invariant));
                w.WriteString("built", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", Invariant));
                w.WriteBoolean("inWorld", body is { Deleted: false });
                w.WriteNumber("count", count);
                w.WriteNumber("lived", lived);
                w.WriteNumber("notes", book.Notes.Count);
                w.WriteBoolean("wanted", _wanted.Contains(book.Name));
                w.WritePropertyName("chapters");
                w.WriteStartArray();

                for (var s = 0; s < _chapters.Length; s++)
                {
                    var chapter = _chapters[s];

                    if (!chapter.Present)
                    {
                        continue;
                    }

                    w.WriteStartObject();
                    w.WriteString("stage", BotStages.Key(chapter.Stage));
                    w.WriteString("title", BotStages.Title(chapter.Stage));
                    w.WriteBoolean("lived", chapter.Lived);
                    BotJson.StringOrNull(w, "text", chapter.Text);
                    BotJson.StringOrNull(w, "voiced", chapter.Voiced);
                    BotJson.StringOrNull(w, "tally", chapter.Tally);
                    w.WritePropertyName("facts");
                    w.WriteStartArray();

                    for (var i = 0; i < chapter.Facts.Count; i++)
                    {
                        w.WriteStringValue(chapter.Facts[i]);
                    }

                    w.WriteEndArray();
                    w.WriteEndObject();
                }

                w.WriteEndArray();
            }
        );

    private static void Wanted(string name)
    {
        if (!Running || _sealed)
        {
            return;
        }

        try
        {
            Requests++;

            var body = BotLives.Body(name);
            var real = body?.Name ?? Real(name);

            if (real == null || BotLives.Find(real) == null)
            {
                Unknown++;

                return;
            }

            var book = Book(real, body);

            if (book == null)
            {
                Unknown++;

                return;
            }

            _wanted.Remove(real);
            _wanted.Add(real);

            while (_wanted.Count > Math.Max(1, MostWanted))
            {
                _wanted.RemoveAt(0);
            }

            Build(book, BotLives.Find(real), body, true);
            Publish(Core.TickCount, true);
        }
        catch (Exception ex)
        {
            Fault(ex, "a story asked for");
        }
    }

    private static string Real(string name)
    {
        if (_books.ContainsKey(name))
        {
            return name;
        }

        foreach (var (key, _) in _books)
        {
            if (string.Equals(key, name, StringComparison.OrdinalIgnoreCase))
            {
                return key;
            }
        }

        return null;
    }

    private static void Publish(long now, bool force)
    {
        force |= _owed;

        if (!_changed || _publishedOnce && now - _publishedTick < (force ? 1000 : Math.Max(1000, PublishEveryMs)))
        {
            _owed = force && _changed;

            return;
        }

        _owed = false;
        _changed = false;
        _publishedOnce = true;
        _publishedTick = now;
        Published++;

        _shelf = new Shelf(new Dictionary<string, string>(_json, StringComparer.OrdinalIgnoreCase), Index());
    }

    private static string Index()
    {
        var list = new List<BotStoryBook>(_books.Count);

        foreach (var (_, book) in _books)
        {
            if (book.Chapters > 0)
            {
                list.Add(book);
            }
        }

        list.Sort(static (a, b) => b.Lived != a.Lived ? b.Lived.CompareTo(a.Lived) : b.Notes.Count.CompareTo(a.Notes.Count));

        return BotJson.Object(
            w =>
            {
                w.WriteString("at", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", Invariant));
                w.WriteNumber("books", _books.Count);
                w.WritePropertyName("stories");
                w.WriteStartArray();

                for (var i = 0; i < list.Count && i < 200; i++)
                {
                    var book = list[i];

                    w.WriteStartObject();
                    w.WriteString("bot", book.Name);
                    BotJson.StringOrNull(w, "class", book.Class);
                    BotJson.StringOrNull(w, "guild", book.SeenGuild);
                    w.WriteNumber("chapters", book.Chapters);
                    w.WriteNumber("lived", book.Lived);
                    w.WriteNumber("notes", book.Notes.Count);
                    w.WriteBoolean("born", book.Born);
                    w.WriteEndObject();
                }

                w.WriteEndArray();
            }
        );
    }

    private static void Voice(long now)
    {
        if (_asking && now - _askedTick >= 600000)
        {

            _asking = false;
            VoiceLost++;
        }

        if (!Voiced || _asking || Ask is not { } ask || World.WorldState != WorldState.Running || VoicePerHour <= 0)
        {
            return;
        }

        while (_asks.Count > 0 && now - _asks.Peek() >= 3600000)
        {
            _asks.Dequeue();
        }

        if (_asks.Count >= VoicePerHour)
        {
            return;
        }

        if (_wanted.Count == 0 && _asks.Count > 0 && now - _askedTick < 3600000 / VoicePerHour)
        {
            return;
        }

        if (!Next(out var q, out var key, out var stage, out var hash))
        {
            return;
        }

        var generation = _generation;

        var before = _askedTick;

        _asking = true;
        _askedTick = now;

        if (!ask(q, (text, waited) => Answered(generation, q, key, stage, hash, text, waited)))
        {
            _asking = false;
            _askedTick = before;
            VoiceBusy++;

            return;
        }

        _asks.Enqueue(now);
        VoiceAsked++;
    }

    private static bool Next(out BotStoryAsk q, out string key, out int stage, out ulong hash)
    {

        for (var i = _wanted.Count - 1; i >= 0; i--)
        {
            var name = _wanted[i];

            if (_books.TryGetValue(name, out var book) && Chapter(book, out q, out stage, out hash))
            {
                key = name;

                return true;
            }

            _wanted.RemoveAt(i);
        }

        if (BotChronicle.Unvoiced() is { } day)
        {
            var facts = BotChronicle.Facts(day);

            key = day.Key;
            stage = -1;
            hash = BotStoryWriter.Hash(facts);
            q = new BotStoryAsk
            {
                Kind = "day",
                Subject = BotChronicle.Title(day),
                Title = "The Chronicle of the island",
                Who = $"the island, on {BotChronicle.Title(day)}",
                Facts = string.Join('\n', facts),
                MostWords = VoiceMostWords * 2,
                Model = VoiceModel
            };

            return true;
        }

        var bots = BotPopulation.Bots;

        for (var n = 0; n < 50 && bots.Count > 0; n++)
        {
            if (_voiceCursor >= bots.Count)
            {
                _voiceCursor = 0;
            }

            var bot = bots[_voiceCursor++];

            if (bot?.Name != null && _books.TryGetValue(bot.Name, out var book) && book.Unvoiced > 0 && Chapter(book, out q, out stage, out hash))
            {
                key = book.Name;

                return true;
            }
        }

        q = null;
        key = null;
        stage = -1;
        hash = 0;

        return false;
    }

    private static bool Chapter(BotStoryBook book, out BotStoryAsk q, out int stage, out ulong hash)
    {
        var body = BotLives.Body(book.Name);
        var life = BotLives.Find(book.Name);

        BotStoryWriter.Build(book, life, body, DateTime.UtcNow, _chapters);

        for (var s = 0; s < _chapters.Length; s++)
        {
            var chapter = _chapters[s];

            if (!Voiceable(book, chapter))
            {
                continue;
            }

            stage = s;
            hash = chapter.Hash;

            q = new BotStoryAsk
            {
                Kind = "chapter",
                Subject = book.Name,
                Title = BotStages.Title(chapter.Stage),
                Who = $"{book.Name}, {BotStoryWriter.A(Kind(book))}",
                Facts = string.Join('\n', chapter.Facts),
                MostWords = VoiceMostWords,
                Model = VoiceModel
            };

            return true;
        }

        book.Unvoiced = 0;
        q = null;
        stage = -1;
        hash = 0;

        return false;
    }

    private static void Answered(int generation, BotStoryAsk q, string key, int stage, ulong hash, string text, long waited)
    {

        if (generation != _generation)
        {
            VoiceStale++;

            return;
        }

        _asking = false;

        try
        {
            VoiceWaitedMs += waited;

            if (text == null)
            {

                VoiceNone++;

                return;
            }

            VoiceAnswered++;

            var told = $"{q.Facts}\n{q.Who}\n{q.Title}\n{q.Subject}";
            var kept = BotStoryWriter.Check(text, told, q.Kind == "chapter" ? q.Subject : null, VoiceMostChars, out var why, out var word);

            if (kept == null)
            {
                _thrown[why] = _thrown.GetValueOrDefault(why) + 1;

                logger.Information(
                    "Story: the model's retelling of {Subject} ({Title}) was thrown away for {Why}{Word} after {Ms}ms; the template stands",
                    q.Subject,
                    q.Title,
                    why,
                    word != null ? $" (\"{word}\")" : "",
                    waited
                );
            }
            else
            {
                VoiceKept++;

                logger.Information("Story: the model retold {Subject} ({Title}) in {Ms}ms: \"{Text}\"", q.Subject, q.Title, waited, kept);
            }

            var prose = new BotStoryProse { Hash = hash, Text = kept, At = DateTime.UtcNow };

            if (stage < 0)
            {
                BotChronicle.Voice(key, hash, kept);

                return;
            }

            if (_books.TryGetValue(key, out var book))
            {
                book.Voiced[stage] = prose;
                Build(book, BotLives.Find(key), BotLives.Body(key), false);
            }
        }
        catch (Exception ex)
        {
            Fault(ex, "the model's answer");
        }
    }

    public static string Plain(string text)
    {
        if (string.IsNullOrEmpty(text) || text.IndexOf(',') < 0)
        {
            return text;
        }

        using var sb = ValueStringBuilder.Create(text.Length);

        var i = 0;

        while (i < text.Length)
        {
            var skip = Coordinates(text, i);

            if (skip > 0)
            {
                i += skip;

                continue;
            }

            sb.Append(text[i]);
            i++;
        }

        return sb.ToString();
    }

    private static int Coordinates(string text, int i)
    {
        var j = i;

        if (string.CompareOrdinal(text, j, " at ", 0, 4) == 0)
        {
            j += 4;
        }
        else if (text[j] == ' ' && j + 1 < text.Length && text[j + 1] == '(')
        {
            j += 1;
        }
        else
        {
            return 0;
        }

        var open = j < text.Length && text[j] == '(';

        if (open)
        {
            j++;
        }

        var digits = j;

        while (j < text.Length && char.IsDigit(text[j]))
        {
            j++;
        }

        if (j == digits || j >= text.Length || text[j] != ',')
        {
            return 0;
        }

        j++;

        if (j < text.Length && text[j] == ' ')
        {
            j++;
        }

        digits = j;

        while (j < text.Length && char.IsDigit(text[j]))
        {
            j++;
        }

        if (j == digits)
        {
            return 0;
        }

        if (open && j < text.Length && text[j] == ',')
        {
            var k = j + 1;

            if (k < text.Length && text[k] == ' ')
            {
                k++;
            }

            var z = k;

            while (k < text.Length && (char.IsDigit(text[k]) || text[k] == '-'))
            {
                k++;
            }

            if (k > z)
            {
                j = k;
            }
        }

        if (open)
        {
            if (j >= text.Length || text[j] != ')')
            {
                return 0;
            }

            j++;
        }

        return j - i;
    }

    private static void Fault(Exception ex, string what)
    {
        Faults++;

        if (Faults <= 3 || Faults % 100 == 0)
        {
            logger.Error(ex, "Story: {What} could not be taken in ({Times} times so far)", what, Faults);
        }
    }

    public static string Describe()
    {
        if (!Running)
        {
            return "the stories are off";
        }

        var three = 0;
        var threeLived = 0;
        var born = 0;
        Span<int> stages = stackalloc int[BotStages.Count];
        var voiced = 0;
        var voiceable = 0;

        foreach (var (_, book) in _books)
        {
            three += book.Chapters >= 3 ? 1 : 0;
            threeLived += book.Lived >= 3 ? 1 : 0;
            born += book.Born ? 1 : 0;
            voiceable += book.Unvoiced;

            for (var s = 0; s < book.Voiced.Length; s++)
            {
                voiced += book.Voiced[s] is { Text: not null } ? 1 : 0;
            }
        }

        Stages(stages);

        var hour = 0;
        var now = Core.TickCount;

        foreach (var at in _asks)
        {
            hour += now - at < 3600000 ? 1 : 0;
        }

        var thrown = "";

        foreach (var (why, n) in _thrown)
        {
            thrown += (thrown.Length == 0 ? "" : ", ") + $"{why} {n}";
        }

        var mean = Builds == 0 ? 0.0 : BuildTicks * 1000000.0 / Stopwatch.Frequency / Builds;
        var most = BuildMostTicks * 1000000.0 / Stopwatch.Frequency;
        var books = Math.Max(1, _books.Count);

        return $"{_books.Count} stories ({born} born under this chronicle, {_books.Count - born} found living); {three} with 3 chapters or more ({100.0 * three / books:F0}%), "
               + $"{threeLived} with 3 or more of things seen to happen ({100.0 * threeLived / books:F0}%); chapters: youth {stages[0]}, apprenticeship {stages[1]}, guild {stages[2]}, deeds {stages[3]}, "
               + $"friends {stages[4]}, home {stages[5]}, family {stages[6]}, old age {stages[7]}; notes: {BornNoted} births, {FromEvents} from events, {FromWalk} from the walk (by kind: {ByKind()}), "
               + $"{Trimmed} trimmed, {Repeats} repeats left out; {Looks} looks, built {Builds} times at {mean:F0}µs a story ({most:F0}µs the longest), "
               + $"handed to the page {Published} times, asked for there {Requests} times ({Unknown} unknown); "
               + $"the model: {(Voiced ? Ask == null ? "on but not wired" : $"on, at most {VoicePerHour} an hour" : "off")}, {hour} in the last hour, asked {VoiceAsked}, answered {VoiceAnswered}, "
               + $"kept {VoiceKept}, thrown away {VoiceAnswered - VoiceKept} ({(thrown.Length == 0 ? "none" : thrown)}), no answer {VoiceNone}, slot busy {VoiceBusy}, stale {VoiceStale}, lost {VoiceLost}, "
               + $"{VoiceWaitedMs / Math.Max(1, VoiceAnswered + VoiceNone)}ms a call; {voiced} chapters retold, {voiceable} waiting; read on the page {CacheHits} in the model's words and {CacheMisses} in the template's; "
               + $"the Chronicle: {BotChronicle.Days.Count} days, {BotChronicle.Today()} entries today, {BotChronicle.Recorded} recorded ({BotChronicle.Kinds()}), {BotChronicle.Repeats} repeats, "
               + $"{BotChronicle.Dropped} past a full page, {BotChronicle.Voiced()} days retold; {TestCrowns} test crowns left out; {Faults} faults, {Interlocked.Read(ref _offLoop)} events heard off the loop";
    }

    private static string ByKind()
    {
        if (_byKind.Count == 0)
        {
            return "none";
        }

        var kinds = new List<KeyValuePair<string, long>>(_byKind);

        kinds.Sort(static (a, b) => b.Value.CompareTo(a.Value));

        using var sb = ValueStringBuilder.Create(256);

        for (var i = 0; i < kinds.Count; i++)
        {
            sb.Append(i == 0 ? "" : ", ");
            sb.Append($"{kinds[i].Key} {kinds[i].Value}");
        }

        return sb.ToString();
    }

    private static void Stages(Span<int> stages)
    {

        Span<bool> has = stackalloc bool[BotStages.Count];

        foreach (var (_, book) in _books)
        {
            has.Clear();

            for (var i = 0; i < book.Notes.Count; i++)
            {
                has[(int)book.Notes[i].Stage] = true;
            }

            if (BotLives.Find(book.Name) is { } life)
            {
                for (var i = 0; i < life.Tales.Count; i++)
                {
                    if (BotStoryWriter.StageOf(life.Tales[i]) is { } stage)
                    {
                        has[(int)stage] = true;
                    }
                }

                var (friend, enemy) = BotTies.Strongest(life);

                has[(int)BotStage.Friends] |= friend != null || enemy != null;
            }

            has[(int)BotStage.OldAge] |= (DateTime.UtcNow - book.FirstSeen).TotalDays >= Math.Max(1, OldAgeDays);

            for (var s = 0; s < BotStages.Count; s++)
            {
                stages[s] += has[s] ? 1 : 0;
            }
        }
    }

    public static void Web(Utf8JsonWriter w)
    {
        var three = 0;
        var threeLived = 0;
        var born = 0;

        foreach (var (_, book) in _books)
        {
            three += book.Chapters >= 3 ? 1 : 0;
            threeLived += book.Lived >= 3 ? 1 : 0;
            born += book.Born ? 1 : 0;
        }

        w.WritePropertyName("stories");
        w.WriteStartObject();
        w.WriteBoolean("running", Running);
        w.WriteNumber("books", _books.Count);
        w.WriteNumber("born", born);
        w.WriteNumber("threeChapters", three);
        w.WriteNumber("threeLived", threeLived);
        w.WriteNumber("births", BornNoted);
        w.WriteNumber("fromEvents", FromEvents);
        w.WriteNumber("fromWalk", FromWalk);
        w.WriteNumber("trimmed", Trimmed);
        w.WriteNumber("builds", Builds);
        w.WriteNumber("buildMeanUs", Builds == 0 ? 0 : Math.Round(BuildTicks * 1000000.0 / Stopwatch.Frequency / Builds, 1));
        w.WriteNumber("buildMostUs", Math.Round(BuildMostTicks * 1000000.0 / Stopwatch.Frequency, 1));
        w.WriteNumber("requests", Requests);
        w.WriteBoolean("voiced", Voiced);
        w.WriteBoolean("wired", Ask != null);
        w.WriteNumber("voicePerHour", VoicePerHour);
        w.WriteNumber("voiceAsked", VoiceAsked);
        w.WriteNumber("voiceAnswered", VoiceAnswered);
        w.WriteNumber("voiceKept", VoiceKept);
        w.WriteNumber("voiceNone", VoiceNone);
        w.WriteNumber("voiceBusy", VoiceBusy);
        w.WriteNumber("cacheHits", CacheHits);
        w.WriteNumber("cacheMisses", CacheMisses);
        w.WriteNumber("chronicleDays", BotChronicle.Days.Count);
        w.WriteNumber("chronicleToday", BotChronicle.Today());
        w.WriteNumber("chronicleRecorded", BotChronicle.Recorded);
        w.WriteNumber("faults", Faults);
        w.WriteEndObject();
    }

    internal const int Shape = 1;

    public static void Save(IGenericWriter writer)
    {
        writer.WriteEncodedInt(Shape);
        writer.WriteEncodedInt(_books.Count);

        foreach (var (_, book) in _books)
        {
            writer.Write(book.Name);
            writer.Write(book.FirstSeen);
            writer.Write(book.LastSeen);
            writer.Write(book.Born);
            writer.Write(book.Female);
            writer.Write(book.Class);
            writer.Write(book.People);
            writer.Write(book.Town);
            writer.Write(book.SeenGuild);
            writer.Write(book.SeenRank);
            writer.Write(book.SeenHouse);
            writer.Write(book.SeenChampion);
            writer.Write(book.SeenVeteran);

            writer.WriteEncodedInt(book.Notes.Count);

            for (var i = 0; i < book.Notes.Count; i++)
            {
                var note = book.Notes[i];

                writer.WriteEncodedInt((int)note.Stage);
                writer.Write(note.Kind);
                writer.Write(note.Text);
                writer.Write(note.At);
                writer.Write(note.Weight);
            }

            var voiced = 0;

            for (var s = 0; s < book.Voiced.Length; s++)
            {
                voiced += book.Voiced[s] != null ? 1 : 0;
            }

            writer.WriteEncodedInt(voiced);

            for (var s = 0; s < book.Voiced.Length; s++)
            {
                if (book.Voiced[s] is not { } prose)
                {
                    continue;
                }

                writer.WriteEncodedInt(s);
                writer.Write(prose.Hash);
                writer.Write(prose.Text);
                writer.Write(prose.At);
            }
        }

        BotChronicle.Save(writer);
    }

    public static void Load(IGenericReader reader)
    {
        var shape = reader.ReadEncodedInt();

        if (shape != Shape)
        {

            logger.Error("The saved stories are shape {Found} and this build reads {Wanted}, past the store's own check", shape, Shape);

            return;
        }

        var count = reader.ReadEncodedInt();
        var notes = 0;
        var voiced = 0;

        for (var n = 0; n < count; n++)
        {
            var book = new BotStoryBook(reader.ReadString())
            {
                FirstSeen = reader.ReadDateTime(),
                LastSeen = reader.ReadDateTime(),
                Born = reader.ReadBool(),
                Female = reader.ReadBool(),
                Class = reader.ReadString(),
                People = reader.ReadString(),
                Town = reader.ReadString(),
                SeenGuild = reader.ReadString(),
                SeenRank = reader.ReadString(),
                SeenHouse = reader.ReadString(),
                SeenChampion = reader.ReadBool(),
                SeenVeteran = reader.ReadBool()
            };

            var noteCount = reader.ReadEncodedInt();

            for (var i = 0; i < noteCount; i++)
            {
                var stage = reader.ReadEncodedInt();

                book.Notes.Add(
                    new BotStoryNote
                    {
                        Stage = (BotStage)Math.Clamp(stage, 0, BotStages.Count - 1),
                        Kind = reader.ReadString(),
                        Text = reader.ReadString(),
                        At = reader.ReadDateTime(),
                        Weight = reader.ReadDouble()
                    }
                );
            }

            var voicedCount = reader.ReadEncodedInt();

            for (var i = 0; i < voicedCount; i++)
            {
                var stage = reader.ReadEncodedInt();
                var prose = new BotStoryProse { Hash = reader.ReadULong(), Text = reader.ReadString(), At = reader.ReadDateTime() };

                if (stage >= 0 && stage < BotStages.Count)
                {
                    book.Voiced[stage] = prose;
                    voiced += prose.Text != null ? 1 : 0;
                }
            }

            if (!string.IsNullOrEmpty(book.Name))
            {
                _books[book.Name] = book;
                notes += book.Notes.Count;
            }
        }

        var days = BotChronicle.Load(reader);

        logger.Information(
            "Stories read back: {Count} books with {Notes} notes and {Voiced} chapters in the model's words; the Chronicle's {Days} days",
            count,
            notes,
            voiced,
            days
        );
    }

    public static void Forget()
    {
        _generation++;
        _asking = false;
        _wanted.Clear();
        _asks.Clear();
        _thrown.Clear();
        _byKind.Clear();
        BornNoted = 0;
        Opened = 0;
        FromEvents = 0;
        FromWalk = 0;
        Trimmed = 0;
        Repeats = 0;
        Looks = 0;
        Builds = 0;
        BuildTicks = 0;
        BuildMostTicks = 0;
        Published = 0;
        Requests = 0;
        Unknown = 0;
        VoiceAsked = 0;
        VoiceAnswered = 0;
        VoiceNone = 0;
        VoiceKept = 0;
        VoiceBusy = 0;
        VoiceStale = 0;
        VoiceLost = 0;
        VoiceWaitedMs = 0;
        CacheHits = 0;
        CacheMisses = 0;
        Faults = 0;
        TestCrowns = 0;
        _publishedOnce = false;
        _owed = false;
        _changed = true;
        BotChronicle.Forget();
    }

    public static int Wipe()
    {
        var books = _books.Count;

        _books.Clear();
        _json.Clear();
        _cursor = 0;
        _voiceCursor = 0;
        Forget();
        BotChronicle.Wipe();
        _shelf = Shelf.Empty;

        _sealed = true;

        return books;
    }

    public static bool Sealed => _sealed;

    public static void Unseal() => _sealed = false;
}
