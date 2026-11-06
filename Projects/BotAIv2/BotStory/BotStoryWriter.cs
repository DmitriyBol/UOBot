using System;
using System.Collections.Generic;
using System.Globalization;
using Server.Guilds;
using Server.Text;

namespace Server.BotAI.V2;

public static class BotStoryWriter
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    public static BotStage? StageOf(BotTale tale) =>
        tale?.Kind switch
        {
            "death"                                         => BotStage.Youth,
            "sale"                                          => BotStage.Apprenticeship,
            "saved" or "raised" or "wronged" or "war" or "duel" => BotStage.Deeds,
            _                                               => null
        };

    private enum LineKind
    {
        Note,
        Baseline,
        Tale,
        Plain
    }

    private struct Line
    {
        public DateTime At;
        public string Text;
        public LineKind Kind;
    }

    private static readonly List<BotStoryNote> _notes = new(16);

    private static readonly List<Line> _lines = new(16);

    public static int Build(BotStoryBook book, BotLife life, BotMobile body, DateTime now, BotStoryChapter[] into)
    {
        var present = 0;

        for (var s = 0; s < BotStages.Count; s++)
        {
            var stage = (BotStage)s;
            var chapter = into[s];

            chapter.Clear(stage);

            _lines.Clear();
            book.Of(stage, _notes);

            for (var i = 0; i < _notes.Count; i++)
            {
                var note = _notes[i];

                _lines.Add(new Line { At = note.At, Text = note.Text, Kind = note.Baseline ? LineKind.Baseline : LineKind.Note });
                chapter.Lived |= !note.Baseline;
            }

            if (life != null)
            {
                for (var i = 0; i < life.Tales.Count; i++)
                {
                    var tale = life.Tales[i];

                    if (StageOf(tale) == stage && !string.IsNullOrEmpty(tale.Text))
                    {
                        _lines.Add(new Line { At = tale.At, Text = tale.Text, Kind = LineKind.Tale });
                        chapter.Lived = true;
                    }
                }
            }

            switch (stage)
            {
                case BotStage.Deeds:
                    {
                        chapter.Tally = Tally(book, life);
                        chapter.Lived |= chapter.Tally != null;

                        break;
                    }
                case BotStage.Friends:
                    {
                        Friends(book, life);
                        chapter.Lived |= _lines.Count > 0;

                        break;
                    }
                case BotStage.OldAge:
                    {
                        chapter.Tally = Age(book, body, now);
                        chapter.Lived |= chapter.Tally != null;

                        break;
                    }
            }

            if (_lines.Count == 0 && chapter.Tally == null)
            {
                continue;
            }

            Sort(_lines);

            for (var i = 0; i < _lines.Count; i++)
            {
                chapter.Facts.Add(Fact(book, _lines[i]));
            }

            chapter.Hash = Hash(chapter.Facts);
            chapter.Text = Prose(book, _lines);

            if (book.Voiced[s] is { } voiced && voiced.Hash == chapter.Hash && chapter.Facts.Count > 0)
            {
                chapter.Voiced = voiced.Text;
            }

            present++;
        }

        return present;
    }

    private static void Friends(BotStoryBook book, BotLife life)
    {
        if (life == null)
        {
            return;
        }

        var (friend, enemy) = BotTies.Strongest(life);

        if (friend != null)
        {
            var why = friend.Saved > 0
                ? $"{friend.Name} had come to {book.Name}'s rescue {Times(friend.Saved)}"
                : friend.Beside > 0
                    ? $"the two had fought side by side {Often(friend.Beside)}"
                    : "the two had dealt fairly with each other";

            _lines.Add(new Line { Text = $"{friend.Name} was {book.Name}'s firmest friend: {why}", Kind = LineKind.Plain });
        }

        if (enemy != null)
        {
            var why = enemy.Wronged > 0
                ? $"{enemy.Name} had wronged {book.Name} {Times(enemy.Wronged)}"
                : "there was bad blood between them";

            _lines.Add(new Line { Text = $"{enemy.Name} was the one {book.Name} could not forgive: {why}", Kind = LineKind.Plain });
        }
    }

    private static string Tally(BotStoryBook book, BotLife life)
    {
        if (life == null)
        {
            return null;
        }

        var kills = (int)Math.Floor(life.Kills);

        if (kills < 1 && life.WarKills == 0 && life.DuelsWon + life.DuelsLost == 0 && life.SavedTimes == 0 && life.RaisedTimes == 0)
        {
            return null;
        }

        using var sb = ValueStringBuilder.Create(160);

        sb.Append($"In all, {book.Name} had ");
        sb.Append(kills == 1 ? "one kill" : $"{kills} kills");
        sb.Append($" to {Possessive(book)} name");

        if (life.WarKills > 0)
        {
            sb.Append($", {life.WarKills} of them in war");
        }

        if (life.DuelsWon > 0)
        {
            sb.Append($"; won {life.DuelsWon} {(life.DuelsWon == 1 ? "duel" : "duels")} and lost {(life.DuelsLost == 0 ? "none" : $"{life.DuelsLost}")}");
        }
        else if (life.DuelsLost > 0)
        {
            sb.Append($"; lost {life.DuelsLost} {(life.DuelsLost == 1 ? "duel" : "duels")} and won none");
        }

        if (life.SavedTimes > 0)
        {
            sb.Append($"; was saved {Times(life.SavedTimes)}");
        }

        if (life.RaisedTimes > 0)
        {
            sb.Append($"; was raised where {Pronoun(book)} fell {Times(life.RaisedTimes)}");
        }

        if (life.Deaths > 0)
        {
            sb.Append($"; and died {Times(life.Deaths)}");
        }

        sb.Append('.');

        return sb.ToString();
    }

    private static string Age(BotStoryBook book, BotMobile body, DateTime now)
    {
        var days = (int)Math.Floor((now - book.FirstSeen).TotalDays);

        if (days < Math.Max(1, BotStory.OldAgeDays))
        {
            return null;
        }

        var since = book.Born ? $"since {book.Name} was born" : "since this chronicle first found " + book.Name;
        var rank = body?.BotRank;
        var guild = (body?.Guild as Guild)?.Name;
        var town = body != null ? BotResidence.NameOf(body) : null;

        using var sb = ValueStringBuilder.Create(160);

        sb.Append($"{days} days had passed {since}");

        if (!string.IsNullOrEmpty(rank) || guild != null || town != null)
        {
            sb.Append($", and {book.Name} was ");
            sb.Append(!string.IsNullOrEmpty(rank) ? A(rank) : $"{A(Words(book.Class))}");

            if (guild != null)
            {
                sb.Append($" of {guild}");
            }

            if (town != null)
            {
                sb.Append($", living in {town}");
            }
        }

        sb.Append('.');

        return sb.ToString();
    }

    private static void Sort(List<Line> lines)
    {

        for (var i = 1; i < lines.Count; i++)
        {
            var line = lines[i];
            var j = i - 1;

            while (j >= 0 && Later(lines[j], line))
            {
                lines[j + 1] = lines[j];
                j--;
            }

            lines[j + 1] = line;
        }
    }

    private static bool Later(Line a, Line b) =>
        b.Kind != LineKind.Plain && (a.Kind == LineKind.Plain || a.At > b.At);

    private static string Fact(BotStoryBook book, Line line) =>
        line.Kind switch
        {
            LineKind.Baseline => $"{Day(line.At)}, when this chronicle began: {Sentence(line.Text)}",
            LineKind.Tale     => $"{Day(line.At)}: {book.Name} tells it in these words: \"{line.Text}\"",
            LineKind.Plain    => Sentence(line.Text),
            _                 => $"{Day(line.At)}: {Sentence(line.Text)}"
        };

    private static string Prose(BotStoryBook book, List<Line> lines)
    {
        using var sb = ValueStringBuilder.Create(512);

        var seed = Seed(book.Name);
        var last = DateTime.MinValue;
        var full = book.Name;
        var given = First(full);
        var named = false;

        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];

            if (sb.Length > 0)
            {
                sb.Append(' ');
            }

            switch (line.Kind)
            {
                case LineKind.Baseline:
                    {
                        sb.Append($"When this chronicle began on {Day(line.At)}, {Sentence(Shorten(line.Text, full, given, ref named))}");
                        last = line.At.ToLocalTime().Date;

                        break;
                    }
                case LineKind.Tale:
                    {

                        var who = named ? given : full;
                        var when = When(line.At, last, seed + (uint)i);
                        var said = ((seed + (uint)i) % 3) switch
                        {
                            0 => $"{when}as {who} tells it: \"{line.Text}\"",
                            1 => $"{when}by {who}'s account: \"{line.Text}\"",
                            _ => $"{when}in {who}'s own words: \"{line.Text}\""
                        };

                        sb.Append(said);
                        named = true;
                        last = line.At.ToLocalTime().Date;

                        break;
                    }
                case LineKind.Plain:
                    {
                        sb.Append(Sentence(Shorten(line.Text, full, given, ref named)));

                        break;
                    }
                default:
                    {
                        sb.Append(When(line.At, last, seed + (uint)i));
                        sb.Append(Sentence(Shorten(line.Text, full, given, ref named)));
                        last = line.At.ToLocalTime().Date;

                        break;
                    }
            }
        }

        return sb.ToString();
    }

    private static string When(DateTime at, DateTime last, uint pick)
    {
        var local = at.ToLocalTime().Date;

        if (last != DateTime.MinValue && local == last)
        {
            return (pick % 3) switch
            {
                0 => "That same day, ",
                1 => "Later that day, ",
                _ => "The same day, "
            };
        }

        return last != DateTime.MinValue && local == last.AddDays(1) ? "The next day, " : $"On {Day(at)}, ";
    }

    private static string Shorten(string text, string full, string given, ref bool named)
    {
        var at = text.IndexOf(full, StringComparison.Ordinal);

        if (at < 0)
        {
            return text;
        }

        if (string.Equals(full, given, StringComparison.Ordinal))
        {
            named = true;

            return text;
        }

        if (named)
        {
            return text.Replace(full, given, StringComparison.Ordinal);
        }

        named = true;

        var after = at + full.Length;

        return string.Concat(text.AsSpan(0, after), text[after..].Replace(full, given, StringComparison.Ordinal));
    }

    public static string Day(DateTime utc) => utc.ToLocalTime().ToString("d MMMM", Invariant);

    public static string Sentence(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return "";
        }

        text = text.Trim();

        if (text.Length == 0)
        {
            return "";
        }

        if (char.IsLower(text[0]))
        {
            text = char.ToUpperInvariant(text[0]) + text[1..];
        }

        var end = text[^1];

        return end is '.' or '!' or '?' or '"' ? text : text + ".";
    }

    public static string Words(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return "wanderer";
        }

        using var sb = ValueStringBuilder.Create(24);

        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];

            if (char.IsUpper(c) && i > 0)
            {
                sb.Append(' ');
            }

            sb.Append(char.ToLowerInvariant(c));
        }

        return sb.ToString();
    }

    public static string A(string phrase) =>
        string.IsNullOrEmpty(phrase) ? "" : ("aeiouAEIOU".IndexOf(phrase[0]) >= 0 ? "an " : "a ") + phrase;

    public static string Pronoun(BotStoryBook book) => book.Female ? "she" : "he";

    public static string Possessive(BotStoryBook book) => book.Female ? "her" : "his";

    public static string Times(int n) =>
        n switch
        {
            1 => "once",
            2 => "twice",
            _ => $"{n} times"
        };

    public static string Often(int n) =>
        n switch
        {
            1      => "once",
            < 10   => "several times",
            < 50   => "many times",
            _      => "more times than either could count"
        };

    public static uint Seed(string name)
    {
        var hash = 2166136261u;

        for (var i = 0; i < (name?.Length ?? 0); i++)
        {
            hash = (hash ^ name[i]) * 16777619u;
        }

        return hash;
    }

    public static ulong Hash(List<string> facts)
    {
        var hash = 14695981039346656037ul;

        for (var f = 0; f < facts.Count; f++)
        {
            var fact = facts[f];

            for (var i = 0; i < fact.Length; i++)
            {
                hash = (hash ^ fact[i]) * 1099511628211ul;
            }

            hash = (hash ^ '\n') * 1099511628211ul;
        }

        return hash == 0 ? 1 : hash;
    }

    private static readonly HashSet<string> Starters = new(StringComparer.Ordinal)
    {
        "a", "an", "the", "he", "she", "it", "they", "his", "her", "its", "their", "this", "that", "these", "those", "there",
        "then", "after", "afterwards", "before", "when", "while", "later", "soon", "once", "in", "on", "at", "by", "with", "from",
        "for", "of", "to", "but", "and", "yet", "so", "as", "though", "although", "still", "now", "thus", "here", "where", "which",
        "who", "whose", "what", "despite", "during", "within", "over", "under", "among", "between", "since", "until", "through",
        "upon", "into", "all", "both", "each", "every", "many", "few", "some", "no", "not", "never", "also", "even", "only",
        "first", "next", "last", "finally", "meanwhile", "together", "again", "such", "none", "one", "two", "three",
        "four", "five", "six", "seven", "eight", "nine", "ten", "twice", "thrice", "word", "news", "fortune", "fate", "time",
        "life", "death", "war", "peace", "days", "day", "nights", "night", "morning", "evening", "dawn", "dusk", "year", "years",
        "chronicle", "chronicles", "island", "story", "tale", "history", "like", "unlike", "however", "indeed", "instead",
        "having", "being", "beside", "alongside", "against", "without", "about", "around", "near", "far", "long",
        "little", "much", "more", "most", "less", "several", "other", "others", "another", "whatever", "whoever",
        "i", "my", "we", "our", "you", "your", "if", "or", "nor", "because", "throughout", "following", "toward",
        "towards", "ever", "always", "often", "rarely", "seldom", "already", "eventually", "ultimately", "shortly"
    };

    public static string Check(string text, string facts, string subject, int mostChars, out string why, out string word)
    {
        word = null;

        if (string.IsNullOrWhiteSpace(text))
        {
            why = "empty";

            return null;
        }

        text = Squeeze(text);

        if (text.Length > 1 && text[0] == '"' && text[^1] == '"')
        {
            text = text[1..^1].Trim();
        }

        if (text.Length > Math.Max(80, mostChars))
        {
            var cut = Math.Max(text.LastIndexOf(". ", mostChars, StringComparison.Ordinal), Math.Max(text.LastIndexOf("! ", mostChars, StringComparison.Ordinal), text.LastIndexOf("? ", mostChars, StringComparison.Ordinal)));

            if (cut < 40)
            {
                why = "length";

                return null;
            }

            text = text[..(cut + 1)];
        }

        if (text.Length < 40)
        {
            why = "empty";

            return null;
        }

        var known = new HashSet<string>(StringComparer.Ordinal);
        var numbers = new HashSet<string>(StringComparer.Ordinal);

        Learn(facts, known, numbers);
        Learn(subject, known, numbers);

        if (subject != null && text.IndexOf(First(subject), StringComparison.Ordinal) < 0)
        {
            why = "subject";

            return null;
        }

        var start = true;
        var i = 0;

        while (i < text.Length)
        {
            var c = text[i];

            if (char.IsLetter(c))
            {
                var from = i;

                while (i < text.Length && (char.IsLetter(text[i]) || text[i] == '\'' || text[i] == '’'))
                {
                    i++;
                }

                var token = Bare(text.AsSpan(from, i - from));

                if (char.IsUpper(c) && token.Length > 0 && !known.Contains(token) && !(start && Starters.Contains(token)))
                {
                    why = "names";
                    word = text[from..i];

                    return null;
                }

                start = false;

                continue;
            }

            if (char.IsDigit(c))
            {
                var from = i;

                while (i < text.Length && char.IsDigit(text[i]))
                {
                    i++;
                }

                var number = Number(text.AsSpan(from, i - from));

                if (!numbers.Contains(number))
                {
                    why = "numbers";
                    word = number;

                    return null;
                }

                start = false;

                continue;
            }

            if (c is '.' or '!' or '?' or ':' or ';' or '"' or '“' or '(' or '\n')
            {
                start = true;
            }

            i++;
        }

        why = null;

        return text;
    }

    private static string Number(ReadOnlySpan<char> digits)
    {
        var trimmed = digits.TrimStart('0');

        return trimmed.Length == 0 ? "0" : trimmed.ToString();
    }

    private static string First(string name)
    {
        var space = name.IndexOf(' ');

        return space > 0 ? name[..space] : name;
    }

    private static void Learn(string text, HashSet<string> words, HashSet<string> numbers)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        var i = 0;

        while (i < text.Length)
        {
            var c = text[i];

            if (char.IsLetter(c))
            {
                var from = i;

                while (i < text.Length && (char.IsLetter(text[i]) || text[i] == '\'' || text[i] == '’'))
                {
                    i++;
                }

                var token = Bare(text.AsSpan(from, i - from));

                if (token.Length > 0)
                {
                    words.Add(token);
                }

                continue;
            }

            if (char.IsDigit(c))
            {
                var from = i;

                while (i < text.Length && char.IsDigit(text[i]))
                {
                    i++;
                }

                numbers.Add(Number(text.AsSpan(from, i - from)));

                continue;
            }

            i++;
        }
    }

    private static string Bare(ReadOnlySpan<char> word)
    {
        if (word.EndsWith("'s", StringComparison.OrdinalIgnoreCase) || word.EndsWith("’s", StringComparison.OrdinalIgnoreCase))
        {
            word = word[..^2];
        }

        word = word.TrimEnd('\'').TrimEnd('’').TrimStart('\'');

        return word.Length == 0 ? "" : word.ToString().ToLowerInvariant();
    }

    private static string Squeeze(string text)
    {
        using var sb = ValueStringBuilder.Create(text.Length);

        var space = false;

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];

            if (char.IsWhiteSpace(c))
            {
                space = sb.Length > 0;

                continue;
            }

            if (space)
            {
                sb.Append(' ');
                space = false;
            }

            sb.Append(c);
        }

        return sb.ToString();
    }
}
