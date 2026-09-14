using System;
using System.Collections.Generic;
using Server.Mobiles;
using Server.Text;

namespace Server.BotAI.V2;

/// <summary>
/// Whether a piece of work is this bot's own trade, another class's trade, or anybody's work — what that does to
/// its price, and how the population's working minutes actually divide between the three.
///
/// <para>
/// <b>Measured before it was priced.</b> Over the first seventy minutes of 14.09.2026 gatherers spent 67% of their
/// working minutes on their own trade and crafters 73%, while healers spent 10%, warriors 12%, mages 15% and
/// archers 16%. Almost none of that gap is fighters doing other classes' work — the share was nought to five in a
/// hundred — it is fighters doing anybody's work: looking for a fight for a quarter of the day, carrying loot to a
/// counter, standing for the guild. So this is the smaller of the two repairs a role needs. It keeps a warrior away
/// from the forge and the mortar; it cannot give a warrior something to fight.
/// </para>
///
/// <para>
/// <b>Read off what a class already says about itself, rather than a table somebody typed.</b> A class names the
/// skills it works towards — <see cref="BotClass.Wants"/> — and a piece of work names the one skill it trains —
/// <see cref="BotDeed.Trains"/>. Work that trains one of this class's skills is its own trade; work that trains a
/// skill some other class is for is another trade; work that trains nothing, or a skill no class is for — carrying,
/// selling, standing for the guild, cooking — is anybody's. That is RimWorld's split between a pawn's passions and
/// what it is incapable of, and Crusader Kings' personality weights, without a second list that could drift from
/// the classes it describes.
/// </para>
///
/// <para>
/// <b>Applied outside the appraisal's fifth root, and on purpose.</b> Everything inside that geometric mean is
/// flattened by it — a fiftieth arrives as 0.46 — which suits considerations about a place and does not suit a
/// statement about who the bot is. Own trade at 1.3 against another's at 0.6 is a ratio of a little over two: a
/// warrior walks past a mortar paying half as much again as its hunt, and a miner with nothing else on the island
/// still mines. Never nought; see <see cref="LeastCalling"/>.
/// </para>
/// </summary>
public static class BotCalling
{
    /// <summary>Which of the three a piece of work is to one bot.</summary>
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

    /// <summary>One class's working minutes, split three ways and by kind of work.</summary>
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

        return line.ToString();
    }

    public static void Forget() => _books.Clear();
}
