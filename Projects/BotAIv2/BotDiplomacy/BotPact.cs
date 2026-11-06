using System;
using System.Collections.Generic;
using Server.Guilds;
using Server.Logging;

namespace Server.BotAI.V2;

public static class BotPact
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotPact));

    public enum Kind
    {
        Peace,
        Trade,
        Orders
    }

    public static double Hours { get; set; } = 12.0;

    public static double TradeDiscount { get; set; } = 0.10;

    public static long Peaces { get; private set; }

    public static long Trades { get; private set; }

    public static long Orders { get; private set; }

    public static long Discounted { get; private set; }

    public static long Saved { get; private set; }

    public static long Favoured { get; private set; }

    private sealed class Terms
    {
        public readonly long[] Ends = new long[3];

        public readonly bool[] Sworn = new bool[3];

        public bool Stands(int kind, long now) => Sworn[kind] && now - Ends[kind] < 0;
    }

    private static readonly Dictionary<(string A, string B), Terms> _pacts = [];

    private static (string A, string B) Pair(string one, string other) =>
        string.CompareOrdinal(one, other) <= 0 ? (one, other) : (other, one);

    public static void Swear(string one, string other, Kind kind, long ms)
    {
        if (one == null || other == null || one == other || ms <= 0)
        {
            return;
        }

        var pair = Pair(one, other);

        if (!_pacts.TryGetValue(pair, out var terms))
        {
            _pacts[pair] = terms = new Terms();
        }

        var now = Core.TickCount;
        var ends = now + ms;

        if (!terms.Stands((int)kind, now) || ends - terms.Ends[(int)kind] > 0)
        {
            terms.Ends[(int)kind] = ends;
            terms.Sworn[(int)kind] = true;
        }

        switch (kind)
        {
            case Kind.Peace:
                {
                    Peaces++;
                    BotWar.RestoreTruce(one, other, terms.Ends[(int)Kind.Peace] - now);

                    break;
                }
            case Kind.Trade:
                {
                    Trades++;

                    break;
                }
            default:
                {
                    Orders++;

                    break;
                }
        }

        logger.Information("{One} and {Other} have sworn {Kind} for {Hours:0.#} hours", one, other, Word(kind), ms / 3600000.0);
    }

    public static bool Holds(string one, string other, Kind kind)
    {
        if (_pacts.Count == 0 || one == null || other == null || one == other)
        {
            return false;
        }

        return _pacts.TryGetValue(Pair(one, other), out var terms) && terms.Stands((int)kind, Core.TickCount);
    }

    public static long Broken { get; private set; }

    public static string Break(string one, string other)
    {
        if (_pacts.Count == 0 || one == null || other == null || one == other)
        {
            return null;
        }

        var pair = Pair(one, other);

        if (!_pacts.TryGetValue(pair, out var terms))
        {
            return null;
        }

        var now = Core.TickCount;
        string said = null;

        for (var k = 0; k < terms.Ends.Length; k++)
        {
            if (terms.Stands(k, now))
            {
                said = said == null ? Word((Kind)k) : $"{said} and {Word((Kind)k)}";
            }
        }

        _pacts.Remove(pair);

        if (said != null)
        {
            Broken++;
        }

        return said;
    }

    public static int MinutesLeft(string one, string other, Kind kind)
    {
        if (!Holds(one, other, kind))
        {
            return 0;
        }

        return (int)((_pacts[Pair(one, other)].Ends[(int)kind] - Core.TickCount) / 60000);
    }

    public static bool Any(string one, string other) =>
        Holds(one, other, Kind.Peace) || Holds(one, other, Kind.Trade) || Holds(one, other, Kind.Orders);

    private static string GuildOf(Mobile m) => (m?.Guild as Guild)?.Name;

    public static int Price(Mobile buyer, Mobile seller, int asking)
    {
        if (_pacts.Count == 0 || asking <= 1 || TradeDiscount <= 0.0)
        {
            return asking;
        }

        var ours = GuildOf(buyer);
        var theirs = GuildOf(seller);

        if (ours == null || theirs == null || ours == theirs || !Holds(ours, theirs, Kind.Trade))
        {
            return asking;
        }

        return Math.Max(1, (int)Math.Round(asking * (1.0 - Math.Clamp(TradeDiscount, 0.0, 0.5))));
    }

    public static void Bought(int asking, int paid, int units)
    {
        if (units <= 0 || paid >= asking)
        {
            return;
        }

        Discounted++;
        Saved += (long)(asking - paid) * units;
    }

    public static bool Serves(Mobile supplier, Mobile buyer)
    {
        if (_pacts.Count == 0)
        {
            return false;
        }

        var ours = GuildOf(supplier);
        var theirs = GuildOf(buyer);

        return ours != null && theirs != null && ours != theirs && Holds(ours, theirs, Kind.Orders);
    }

    public static void Filled(Mobile supplier, Mobile buyer)
    {
        if (Serves(supplier, buyer))
        {
            Favoured++;
        }
    }

    public static string Word(Kind kind) =>
        kind switch
        {
            Kind.Peace => "peace",
            Kind.Trade => "trade terms",
            _ => "standing orders"
        };

    public static string Tell()
    {
        using var say = Server.Text.ValueStringBuilder.Create(128);

        var now = Core.TickCount;

        foreach (var (pair, terms) in _pacts)
        {
            for (var k = 0; k < terms.Ends.Length; k++)
            {
                if (!terms.Stands(k, now))
                {
                    continue;
                }

                var ends = terms.Ends[k];

                if (say.Length > 0)
                {
                    say.Append(", ");
                }

                say.Append($"{pair.A} and {pair.B} under {Word((Kind)k)} for {(ends - now) / 60000} min more");
            }
        }

        return say.Length == 0 ? "no agreements stand" : say.ToString();
    }

    public static IEnumerable<(string A, string B, Kind Kind, long LeftMs)> Standing()
    {
        var now = Core.TickCount;

        foreach (var (pair, terms) in _pacts)
        {
            for (var k = 0; k < terms.Ends.Length; k++)
            {
                if (terms.Stands(k, now))
                {
                    yield return (pair.A, pair.B, (Kind)k, terms.Ends[k] - now);
                }
            }
        }
    }

    public static void Restore(string a, string b, Kind kind, long leftMs)
    {
        if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b) || a == b || leftMs <= 0)
        {
            return;
        }

        var pair = Pair(a, b);

        if (!_pacts.TryGetValue(pair, out var terms))
        {
            _pacts[pair] = terms = new Terms();
        }

        terms.Ends[(int)kind] = Core.TickCount + leftMs;
        terms.Sworn[(int)kind] = true;
    }

    public static void Sweep()
    {
        if (_pacts.Count == 0)
        {
            return;
        }

        List<(string, string)> gone = null;
        var now = Core.TickCount;

        foreach (var (pair, terms) in _pacts)
        {
            var alive = false;

            for (var k = 0; k < terms.Ends.Length; k++)
            {
                alive |= terms.Stands(k, now);
            }

            if (!alive)
            {
                (gone ??= []).Add(pair);
            }
        }

        if (gone == null)
        {
            return;
        }

        for (var i = 0; i < gone.Count; i++)
        {
            _pacts.Remove(gone[i]);
        }
    }

    public static string Describe() =>
        $"{Tell()}; {Peaces} peaces, {Trades} trade terms and {Orders} standing orders sworn for {Hours:0.#}h each; {Discounted} purchases off a partner's stall at {TradeDiscount:P0} off, {Saved}gp kept by the buyers; {Favoured} orders filled for a partner under standing orders; {Broken} pairs' agreements broken by a seizure";

    public static void Forget()
    {
        Broken = 0;
        Peaces = 0;
        Trades = 0;
        Orders = 0;
        Discounted = 0;
        Saved = 0;
        Favoured = 0;
    }

    public static int Wipe()
    {
        var gone = _pacts.Count;

        _pacts.Clear();
        Forget();

        return gone;
    }
}
