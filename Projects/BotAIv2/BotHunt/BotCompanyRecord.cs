using System;
using System.Collections.Generic;
using Server.Mobiles;
using Server.Text;

namespace Server.BotAI.V2;

public static class BotCompanyRecord
{

    public static bool Prices { get; set; } = true;

    public static double Vouch { get; set; } = 3.0;

    public static double Margin { get; set; } = 1.5;

    public static int FightMs { get; set; } = 120000;

    public static int CacheMs { get; set; } = 5000;

    public static int MostEvents { get; set; } = 256;

    private struct Fight
    {
        public double Odds;

        public bool Death;

        public long Tick;
    }

    private sealed class Book
    {
        public readonly List<Fight> Fights = [];

        public double Needed = double.PositiveInfinity;

        public double Kills;

        public double Deaths;

        public long Worked;

        public bool Stale = true;
    }

    private static readonly Dictionary<Type, Book> _books = [];

    public static long Kills { get; private set; }

    public static long Deaths { get; private set; }

    public static long Elsewhere { get; private set; }

    public static long LetIn { get; private set; }

    public static long Stood { get; private set; }

    public static long LookLetIn { get; private set; }

    public static long ZoneLetIn { get; private set; }

    private static readonly Dictionary<Type, (long LetIn, long Stood)> _byKind = [];

    internal static void Let(Mobile foe)
    {
        LetIn++;
        Tally(foe, true);
    }

    internal static void Stand(Mobile foe)
    {
        Stood++;
        Tally(foe, false);
    }

    internal static void Looked() => LookLetIn++;

    internal static void Zoned() => ZoneLetIn++;

    private static void Tally(Mobile foe, bool letIn)
    {
        if (foe == null)
        {
            return;
        }

        var kind = foe.GetType();

        if (_byKind.Count > 256 && !_byKind.ContainsKey(kind))
        {
            return;
        }

        _byKind.TryGetValue(kind, out var had);
        _byKind[kind] = letIn ? (had.LetIn + 1, had.Stood) : (had.LetIn, had.Stood + 1);
    }

    public static void Killed(Type kind, double odds)
    {
        if (kind == null || !(odds > 0.0) || double.IsInfinity(odds))
        {
            return;
        }

        Kills++;
        Add(kind, odds, false);
    }

    public static void Fell(Type kind, double odds)
    {
        if (kind == null || !(odds > 0.0) || double.IsInfinity(odds))
        {
            return;
        }

        Deaths++;
        Add(kind, odds, true);
    }

    public static void FellElsewhere() => Elsewhere++;

    private static void Add(Type kind, double odds, bool death)
    {
        if (!_books.TryGetValue(kind, out var book))
        {
            if (_books.Count >= 1024)
            {
                _books.Clear();
            }

            book = new Book();
            _books[kind] = book;
        }

        if (book.Fights.Count >= Math.Max(8, MostEvents))
        {
            book.Fights.RemoveAt(0);
        }

        book.Fights.Add(new Fight { Odds = odds, Death = death, Tick = Core.TickCount });
        book.Stale = true;
    }

    private static double Yardstick => Math.Max(0.1, BotSquad.WithdrawOdds);

    public static double Price(Type kind)
    {
        var island = BotQuarry.Price(kind);

        if (!Prices || kind == null || island <= 1.0 || !_books.TryGetValue(kind, out var book))
        {
            return island;
        }

        var needed = Needed(book);

        return double.IsInfinity(needed) ? island : Math.Clamp(Yardstick * needed, 1.0, island);
    }

    public static double Priced(BaseCreature creature) =>
        creature == null ? 0.0 : BotThreat.Power(creature) * Price(creature.GetType());

    private static readonly List<double> _points = [];

    private static readonly List<(double Odds, bool Death, double Weight)> _weighed = [];

    private static double Needed(Book book)
    {
        var now = Core.TickCount;

        if (!book.Stale && now - book.Worked < CacheMs)
        {
            return book.Needed;
        }

        book.Stale = false;
        book.Worked = now;
        book.Needed = double.PositiveInfinity;
        book.Kills = 0.0;
        book.Deaths = 0.0;

        _weighed.Clear();
        _points.Clear();

        var half = Math.Max(1, BotQuarry.RiskHalfMs);
        var margin = Math.Max(1.0, Margin);

        var kept = 0;

        for (var i = 0; i < book.Fights.Count; i++)
        {
            if (now - book.Fights[i].Tick <= half * 6.65)
            {
                book.Fights[kept++] = book.Fights[i];
            }
        }

        if (kept < book.Fights.Count)
        {
            book.Fights.RemoveRange(kept, book.Fights.Count - kept);
        }

        foreach (var fight in book.Fights)
        {
            var weight = Math.Pow(0.5, Math.Max(0L, now - fight.Tick) / (double)half);

            _weighed.Add((fight.Odds, fight.Death, weight));

            if (fight.Death)
            {
                book.Deaths += weight;
                _points.Add(fight.Odds * margin);
            }
            else
            {
                book.Kills += weight;
                _points.Add(fight.Odds);
            }
        }

        if (book.Kills < Vouch)
        {
            return book.Needed;
        }

        _points.Add(0.0);
        _points.Sort();

        for (var i = 0; i < _points.Count; i++)
        {
            var at = _points[i];

            if (i + 1 < _points.Count && _points[i + 1] <= at)
            {
                continue;
            }

            var kills = 0.0;
            var deaths = 0.0;

            for (var j = 0; j < _weighed.Count; j++)
            {
                var (odds, death, weight) = _weighed[j];

                if (death ? odds * margin > at : odds <= at)
                {
                    if (death)
                    {
                        deaths += weight;
                    }
                    else
                    {
                        kills += weight;
                    }
                }
            }

            if (kills < Vouch)
            {
                continue;
            }

            var let = Math.Max(at, BotQuarry.Formula(deaths, kills) / Yardstick);
            var next = double.PositiveInfinity;

            for (var k = i + 1; k < _points.Count; k++)
            {
                if (_points[k] > at)
                {
                    next = _points[k];

                    break;
                }
            }

            if (let < next)
            {
                book.Needed = let;

                break;
            }
        }

        return book.Needed;
    }

    public static void Forget()
    {
        _books.Clear();
        _byKind.Clear();
        Kills = 0;
        Deaths = 0;
        Elsewhere = 0;
        LetIn = 0;
        Stood = 0;
        LookLetIn = 0;
        ZoneLetIn = 0;
    }

    public static string Describe()
    {
        if (!Prices)
        {
            return "the company record: off (every company weighs every kind at the island's price)";
        }

        List<(string Name, double Company, double Island, double Odds, double Kills, double Deaths)> lower = [];
        var island = 0;

        foreach (var (kind, book) in _books)
        {
            var needed = Needed(book);
            var price = BotQuarry.Price(kind);

            if (price <= 1.0)
            {
                continue;
            }

            var company = Price(kind);

            if (company < price)
            {
                lower.Add((kind.Name, company, price, needed, book.Kills, book.Deaths));
            }
            else
            {
                island++;
            }
        }

        lower.Sort(static (a, b) => (b.Island - b.Company).CompareTo(a.Island - a.Company));

        using var sb = ValueStringBuilder.Create(512);

        for (var i = 0; i < lower.Count && i < 8; i++)
        {
            if (i > 0)
            {
                sb.Append(", ");
            }

            var (name, company, price, odds, kills, deaths) = lower[i];
            sb.Append($"{name} x{company:F1} against x{price:F1} (companies at {odds:F2} of its strength let in; {kills:F0} of them put down and {deaths:F1} of ours lost in companies' fights)");
        }

        var stood = ValueStringBuilder.Create(128);

        try
        {
            foreach (var (kind, n) in _byKind)
            {
                if (stood.Length > 0)
                {
                    stood.Append(", ");
                }

                stood.Append($"{kind.Name} {n.LetIn}/{n.Stood}");
            }

            return $"the company record (a kind priced by companies' own fights, never above the island's price): {Kills} company kills and {Deaths} deaths of ours in companies' fights recorded, {Elsewhere} deaths in a company to a kind it was not fighting left to the island's record; kinds priced below the island for companies: {(lower.Count > 0 ? sb.ToString() : "none")}; {island} priced kinds still at the island's price for companies (no {Vouch:F0} company kills vouching, or the record no better); {LetIn} creatures a company chose, joined or answered and {Stood} withdrawals not made on the company record that the island's price would have refused (by kind, let in/stood: {(stood.Length > 0 ? stood.ToString() : "none")}), {LookLetIn} creatures a look for a company's quarry and {ZoneLetIn} zones a look for company ground let in on it";
        }
        finally
        {
            stood.Dispose();
        }
    }
}
