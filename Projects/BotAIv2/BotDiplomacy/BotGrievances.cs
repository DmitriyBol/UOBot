using System;
using System.Collections.Generic;

namespace Server.BotAI.V2;

public static class BotGrievances
{

    public sealed class Entry
    {
        public int Count;

        public double Moved;
    }

    public const string Forgetting = "time";

    private static readonly Dictionary<(string Of, string About), Dictionary<string, Entry>> _books = [];

    public static long Written { get; private set; }

    public static long Closed { get; private set; }

    public static void Noted(string of, string about, double moved, string why)
    {
        if (of == null || about == null || of == about || why == null || moved == 0.0 || why == Forgetting)
        {
            return;
        }

        var key = (of, about);

        if (!_books.TryGetValue(key, out var book))
        {
            _books[key] = book = new Dictionary<string, Entry>(StringComparer.Ordinal);
        }

        if (!book.TryGetValue(why, out var entry))
        {
            book[why] = entry = new Entry();
        }

        entry.Count++;
        entry.Moved += moved;
        Written++;
    }

    public static void Settled(string one, string other)
    {
        if (one == null || other == null)
        {
            return;
        }

        var any = _books.Remove((one, other));
        any |= _books.Remove((other, one));

        if (any)
        {
            Closed++;
        }
    }

    public static List<(string Why, int Count, double Moved)> Of(string of, string about)
    {
        List<(string, int, double)> found = [];

        if (of == null || about == null || !_books.TryGetValue((of, about), out var book))
        {
            return found;
        }

        foreach (var (why, entry) in book)
        {
            found.Add((why, entry.Count, entry.Moved));
        }

        found.Sort((a, b) => a.Item3.CompareTo(b.Item3));

        return found;
    }

    public static int Count(string of, string about, string why) =>
        of != null && about != null && why != null && _books.TryGetValue((of, about), out var book) && book.TryGetValue(why, out var entry)
            ? entry.Count
            : 0;

    public static string Tell(string of, string about)
    {
        var book = Of(of, about);

        if (book.Count == 0)
        {
            return "nothing written";
        }

        using var say = Server.Text.ValueStringBuilder.Create(128);

        for (var i = 0; i < book.Count; i++)
        {
            var (why, count, moved) = book[i];

            if (i > 0)
            {
                say.Append(", ");
            }

            say.Append($"{why} ×{count} ({moved:+0;-0})");
        }

        return say.ToString();
    }

    public static IEnumerable<KeyValuePair<(string Of, string About), Dictionary<string, Entry>>> Books => _books;

    public static void Restore(string of, string about, string why, int count, double moved)
    {
        if (string.IsNullOrEmpty(of) || string.IsNullOrEmpty(about) || of == about || string.IsNullOrEmpty(why) || count <= 0)
        {
            return;
        }

        if (!_books.TryGetValue((of, about), out var book))
        {
            _books[(of, about)] = book = new Dictionary<string, Entry>(StringComparer.Ordinal);
        }

        book[why] = new Entry { Count = count, Moved = moved };
    }

    public static string Describe() => $"{_books.Count} books of grievances open, {Written} moves written into them, {Closed} closed by a settlement";

    public static void Forget()
    {
        Written = 0;
        Closed = 0;
    }

    public static int Wipe()
    {
        var gone = _books.Count;

        _books.Clear();
        Forget();

        return gone;
    }
}
