using System;
using System.Collections.Generic;
using Server.Mobiles;
using Server.Text;

namespace Server.BotAI.V2;

public static class BotCalling
{

    public enum Fit
    {

        Anybody,

        Own,

        Other
    }

    public static double OwnTrade { get; set; } = 1.3;

    public static double OtherTrade { get; set; } = 0.6;

    public static double LeastCalling { get; set; } = 0.1;

    private static readonly HashSet<SkillName> _claimed = [];

    private static bool _surveyed;

    private sealed class Book
    {
        public double Own;

        public double Anybody;

        public double Other;

        public readonly Dictionary<string, double> Kinds = new(StringComparer.OrdinalIgnoreCase);
    }

    private static readonly Dictionary<string, Book> _books = new(StringComparer.OrdinalIgnoreCase);

    public static Fit Of(Mobile body, BotDeed deed)
    {
        if (deed?.Trains is not { } skill || body is not BotMobile { Class: { } calling })
        {
            return Fit.Anybody;
        }

        if (calling.Wants(skill))
        {
            return Fit.Own;
        }

        Survey();

        return _claimed.Contains(skill) ? Fit.Other : Fit.Anybody;
    }

    public static double Worth(Mobile body, BotDeed deed) =>
        Of(body, deed) switch
        {
            Fit.Own => Math.Max(LeastCalling, OwnTrade),
            Fit.Other => Math.Max(LeastCalling, OtherTrade),
            _ => 1.0
        };

    public static string Word(double calling) =>
        calling > 1.0 ? "its own trade" : calling < 1.0 ? "another's trade" : "anybody's work";

    private static void Survey()
    {
        if (_surveyed)
        {
            return;
        }

        _surveyed = true;

        var classes = BotClasses.All;

        foreach (var skill in Enum.GetValues<SkillName>())
        {
            for (var i = 0; i < classes.Count; i++)
            {
                if (classes[i].Wants(skill))
                {
                    _claimed.Add(skill);

                    break;
                }
            }
        }
    }

    public static void Worked(Mobile body, BotDeed deed, double minutes)
    {
        if (body is not BotMobile { Class: { } calling } || deed == null || minutes <= 0.0)
        {
            return;
        }

        if (!_books.TryGetValue(calling.Name, out var book))
        {
            book = new Book();
            _books[calling.Name] = book;
        }

        switch (Of(body, deed))
        {
            case Fit.Own:
                book.Own += minutes;

                break;

            case Fit.Other:
                book.Other += minutes;

                break;

            default:
                book.Anybody += minutes;

                break;
        }

        book.Kinds.TryGetValue(deed.Kind ?? "?", out var spent);
        book.Kinds[deed.Kind ?? "?"] = spent + minutes;
    }

    public static string Describe()
    {
        if (_books.Count == 0)
        {
            return "no work has ended yet";
        }

        using var line = ValueStringBuilder.Create(1024);

        var classes = new List<KeyValuePair<string, Book>>(_books);

        classes.Sort((a, b) => (b.Value.Own + b.Value.Anybody + b.Value.Other).CompareTo(a.Value.Own + a.Value.Anybody + a.Value.Other));

        for (var i = 0; i < classes.Count; i++)
        {
            var (name, book) = classes[i];
            var total = book.Own + book.Anybody + book.Other;

            if (total <= 0.0)
            {
                continue;
            }

            line.Append(i == 0 ? "" : "; ");
            line.Append(
                $"{name} {total:F0} min: own {100.0 * book.Own / total:F0}%, anybody's {100.0 * book.Anybody / total:F0}%, another's {100.0 * book.Other / total:F0}% ("
            );

            var kinds = new List<KeyValuePair<string, double>>(book.Kinds);

            kinds.Sort((a, b) => b.Value.CompareTo(a.Value));

            for (var k = 0; k < kinds.Count && k < 4; k++)
            {
                line.Append(k == 0 ? "" : ", ");
                line.Append($"{kinds[k].Key} {100.0 * kinds[k].Value / total:F0}%");
            }

            line.Append(")");
        }

        line.Append(Crafters());

        return line.ToString();
    }

    private static readonly string[] _making = ["forge", "sew", "tinker", "fletch", "bowyer", "weave", "brew"];

    private static readonly string[] _digging = ["mine", "prospect"];

    private static string Crafters()
    {
        double total = 0.0, making = 0.0, digging = 0.0;
        Dictionary<string, double> trades = new(StringComparer.OrdinalIgnoreCase);

        foreach (var (name, book) in _books)
        {
            if (!string.Equals(name, "Crafter", StringComparison.OrdinalIgnoreCase) && !string.Equals(name, "Architect", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            total += book.Own + book.Anybody + book.Other;

            for (var i = 0; i < _making.Length; i++)
            {
                if (book.Kinds.TryGetValue(_making[i], out var spent))
                {
                    making += spent;
                    trades[_making[i]] = trades.GetValueOrDefault(_making[i]) + spent;
                }
            }

            for (var i = 0; i < _digging.Length; i++)
            {
                digging += book.Kinds.GetValueOrDefault(_digging[i]);
            }
        }

        if (total <= 0.0)
        {
            return "";
        }

        List<string> by = [];

        for (var i = 0; i < _making.Length; i++)
        {
            if (trades.TryGetValue(_making[i], out var spent) && spent > 0.0)
            {
                by.Add($"{_making[i]} {spent:F0}");
            }
        }

        return $"; crafters (Crafter, Architect) {total:F0} min: making things {making:F0} min ({100.0 * making / total:F1}%: {(by.Count == 0 ? "none" : string.Join(", ", by))}), digging {digging:F0} min ({100.0 * digging / total:F1}%); "
               + BotCraftFirst.Describe();
    }

    public static void Forget() => _books.Clear();
}
