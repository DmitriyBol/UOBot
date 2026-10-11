using System;
using System.Collections.Generic;
using Server.Text;

namespace Server.BotAI.V2;

/// <summary>
/// Supply against spoken demand, by kind: whether one more of a thing is work or a pile.
///
/// <para>
/// <b>Patrick, 01.10.2026 (night plan v2, point 8): "Special attention to trade balance: do not let bots flood the market;
/// after a pile of offers and zero demand it must pay them better to go hunting or join a company. The bar here is 95 %."</b>
/// </para>
///
/// <para>
/// <b>Every maker counted its output at the island's price whether or not anybody would ever buy it, and that is how a flood
/// pays.</b> <see cref="BotMarket.Worth"/> answers the best standing bid, else what the kind last changed hands for, else the
/// maker's own guess — so a smith beating out its fortieth dagger onto a shelf already holding thirty was credited forty-five
/// gold a piece, the ledger learned that forging pays hundreds a minute, and the forge outbid every hunt. Measured on build 384
/// (session 00:42 of 01.10.2026, cumulative at 10:17): 1,338 tools tinkered, 0 to order, 1,310 put out unsold; 900 forge stints
/// on spec against 136 to order; 409 shop shifts ended and 328 of them sold nothing; 26,938 things stayed in their sellers'
/// hands with nowhere to go; the shops showed 945 ingots of seven metals while 9,075 smiths' looks read "short of metal".
/// Nothing in the appraisal could see the pile, because the number it was handed was a price, and a price is not a buyer.
/// </para>
///
/// <para>
/// <b>What this reads, for one kind.</b> The units open wants ask for (<see cref="BotMarket.Asked"/>); the units sold between
/// bots this half hour (<see cref="Sold"/>, booked where <see cref="BotMarket.Traded"/> is); the units made this half hour
/// (<see cref="Made"/>, booked where every craft counts its own output, and where ore becomes bars); the units peddled to
/// shopkeepers this half hour (<see cref="Peddled"/>, booked at the counter); and what the shops by the bank show now
/// (<see cref="BotShopkeep.Showing"/>). The pile is the larger of what is on show and what was made and not sold or peddled;
/// the demand is what is asked for and what sold. <see cref="Factor"/> is demand over pile, with a shelf's worth
/// (<see cref="Tolerance"/>) always allowed and a floor (<see cref="Floor"/>), so a glut is a low price and never a veto — a
/// multiplier without a floor is a veto, and this project has paid for that shape more than once.
/// </para>
///
/// <para>
/// <b>Where it bites: three places and no more.</b> (1) <see cref="Yield"/> — what a maker counts its output at: the units
/// somebody is asking for at the island's price, the rest at the surplus price, which is what a shopkeeper pays for one when
/// that is known (<see cref="Scrap"/>, learned from real peddles) and otherwise the price times the factor. The ledger learns
/// from takings, so a trade that floods learns within a few stints that flooding pays little, and hunting or a company wins
/// the choice on the ordinary arithmetic. (2) <see cref="Claim"/> — a maker's claim for work on spec reads the factor of the
/// kind it would make, so the choice sees the glut at once rather than after the ledger has learned it; an order is never
/// marked down, because somebody has said it will pay. (3) <see cref="Glutted"/> — a maker choosing what to make on spec
/// passes a glutted kind over for one that is not, when it can make one, so producers keep producing what is wanted and the
/// shelves carry more kinds rather than more of one. No handouts anywhere in it: a bot the glut makes poor hunts.
/// </para>
///
/// <para>
/// <b>Not a second price.</b> Buyers' offers still come from <see cref="BotMarket.Worth"/>; a want for a glutted kind is paid
/// in full at the meeting. Only what the maker <em>reckons</em> changes, and only for the part nobody asked for.
/// </para>
///
/// <para>
/// <b>A trader's want is not demand (01.10.2026, build 385 review).</b> A trader gathering at its town's bank says a want of its
/// own for what a far buyer asked (<c>BotHaul</c>, <c>BotHaulFrom.Gather</c>) and withdraws it when it sets out, five to ten
/// minutes later; the far want it answers is reserved meanwhile and reads as nothing open. Counted here, that want would have
/// turned a glutted kind back to ×1 for the length of a gathering and sent the makers' choice after a buyer who is leaving:
/// the piece finished after the trader has gone is the maker's stock, which is the pile this reading exists to stop. So the
/// units a neutral class asks for are left out of <see cref="Reading.Asked"/>; the carriers still read the trader's want
/// (<see cref="BotMarket.Wants"/>) and bring it what they hold, and what it then hands over counts as sold between bots like
/// any other meeting. Units left out are counted (<see cref="TraderUnitsLeftOut"/>) and printed.
/// </para>
/// </summary>
public static class BotGlut
{
    public static bool Enabled { get; set; } = true;

    public static int WindowMs { get; set; } = 1800000;

    public static double Floor { get; set; } = 0.1;

    public static int Tolerance { get; set; } = 6;

    public static double Shunned { get; set; } = 0.34;

    public static int ReadMs { get; set; } = 5000;

    private sealed class Tally
    {
        public int Made;

        public int Sold;

        public int Peddled;
    }

    private static Dictionary<Type, Tally> _now = [];

    private static Dictionary<Type, Tally> _then = [];

    private static long _turnedTick;

    private static bool _turned;

    public static long MadeAll { get; private set; }

    public static long SoldAll { get; private set; }

    public static long PeddledAll { get; private set; }

    public static long MadeIntoGlut { get; private set; }

    public static long OnSpec { get; private set; }

    public static long OnSpecGlutted { get; private set; }

    public static long Turned { get; private set; }

    public static long TraderUnitsLeftOut { get; private set; }

    private static void Turn()
    {
        var now = Core.TickCount;

        if (!_turned)
        {
            _turned = true;
            _turnedTick = now;

            return;
        }

        var half = Math.Max(1000, WindowMs / 2);

        if (now - _turnedTick < half)
        {
            return;
        }

        if (now - _turnedTick >= half * 2)
        {
            _now.Clear();
            _then.Clear();
        }
        else
        {
            (_now, _then) = (_then, _now);
            _now.Clear();
        }

        _turnedTick = now;
    }

    private static Tally Row(Type kind)
    {
        Turn();

        if (!_now.TryGetValue(kind, out var row))
        {
            row = new Tally();
            _now[kind] = row;
        }

        return row;
    }

    private static (int Made, int Sold, int Peddled) Window(Type kind)
    {
        Turn();

        var made = 0;
        var sold = 0;
        var peddled = 0;

        if (_now.TryGetValue(kind, out var a))
        {
            made += a.Made;
            sold += a.Sold;
            peddled += a.Peddled;
        }

        if (_then.TryGetValue(kind, out var b))
        {
            made += b.Made;
            sold += b.Sold;
            peddled += b.Peddled;
        }

        return (made, sold, peddled);
    }

    public static void Made(Type kind, int units)
    {
        if (kind == null || units <= 0)
        {
            return;
        }

        Row(kind).Made += units;
        MadeAll += units;

        if (Enabled && Factor(kind) < 1.0)
        {
            MadeIntoGlut += units;
        }
    }

    public static void Sold(Type kind, int units)
    {
        if (kind == null || units <= 0)
        {
            return;
        }

        Row(kind).Sold += units;
        SoldAll += units;
        BotOre.Sold(kind, units);
    }

    private static readonly Dictionary<Type, (long Units, long Gold)> _scrap = [];

    public static void Peddled(Type kind, int units, int gold)
    {
        if (kind == null || units <= 0)
        {
            return;
        }

        Row(kind).Peddled += units;
        PeddledAll += units;
        BotOre.Peddled(kind, units, gold);

        if (gold <= 0)
        {
            return;
        }

        var (u, g) = _scrap.GetValueOrDefault(kind);

        if (u + units > 1000)
        {
            u /= 2;
            g /= 2;
        }

        _scrap[kind] = (u + units, g + gold);
    }

    public static int Scrap(Type kind) =>
        kind != null && _scrap.TryGetValue(kind, out var sold) && sold.Units > 0 ? (int)Math.Max(1, sold.Gold / sold.Units) : 0;

    public static int ScrapKinds => _scrap.Count;

    private sealed class Reading
    {
        public double Factor;

        public int Pile;

        public int Asked;

        public int Demand;

        public long Tick;
    }

    private static readonly Dictionary<Type, Reading> _readings = [];

    public static double Factor(Type kind) => kind == null ? 1.0 : Read(kind).Factor;

    public static bool Glutted(Type kind) => Enabled && kind != null && Read(kind).Factor <= Shunned;

    public static int Seen(Type kind)
    {
        if (kind == null)
        {
            return 0;
        }

        var (made, sold, _) = Window(kind);

        return made + sold + BotShopkeep.Showing(kind);
    }

    private static Reading Read(Type kind)
    {
        var now = Core.TickCount;

        if (_readings.TryGetValue(kind, out var known) && now - known.Tick < ReadMs)
        {
            return known;
        }

        var (made, sold, peddled) = Window(kind);
        var shown = BotShopkeep.Showing(kind);

        var pile = Math.Max(shown, made - sold - peddled);
        var asked = Asked(kind);
        var demand = asked + sold;
        var factor = 1.0;

        if (Enabled && pile > Tolerance && demand < pile)
        {
            factor = Math.Clamp((demand + Tolerance) / (double)pile, Math.Clamp(Floor, 0.01, 1.0), 1.0);
        }

        if (known == null)
        {
            if (_readings.Count > 512)
            {
                _readings.Clear();
            }

            known = new Reading();
            _readings[kind] = known;
        }

        known.Factor = factor;
        known.Pile = pile;
        known.Asked = asked;
        known.Demand = demand;
        known.Tick = now;

        return known;
    }

    private static int Asked(Type kind)
    {
        var units = 0;
        var wants = BotMarket.Wants;

        for (var i = 0; i < wants.Count; i++)
        {
            var want = wants[i];

            if (want.Kind != kind)
            {
                continue;
            }

            if (want.Buyer?.Self is BotMobile { Class: { Neutral: true } })
            {
                TraderUnitsLeftOut += want.Payable;

                continue;
            }

            units += want.Payable;
        }

        return units;
    }

    public static int Yield(Type kind, int units, int guess)
    {
        if (kind == null || units <= 0)
        {
            return 0;
        }

        var worth = Math.Max(1, BotMarket.Worth(kind, Math.Max(1, guess)));

        if (!Enabled)
        {
            return units * worth;
        }

        var reading = Read(kind);
        var wanted = Math.Min(units, Math.Max(0, reading.Asked));
        var rest = units - wanted;

        if (rest <= 0 || reading.Factor >= 1.0)
        {
            return units * worth;
        }

        var surplus = Math.Max(1, Math.Max(Scrap(kind), (int)Math.Round(worth * reading.Factor)));

        if (surplus >= worth)
        {
            return units * worth;
        }

        return wanted * worth + rest * surplus;
    }

    public static double Claim(Type kind, double prior) => kind == null || !Enabled ? prior : prior * Read(kind).Factor;

    public static void Spec(Type kind)
    {
        OnSpec++;

        if (Enabled && kind != null && Read(kind).Factor < 1.0)
        {
            OnSpecGlutted++;
        }
    }

    public static void Steered() => Turned++;

    public static string Describe()
    {
        List<(Type Kind, Reading Reading)> glutted = [];
        var read = 0;
        var madeNow = 0;
        var soldNow = 0;
        var peddledNow = 0;

        foreach (var (kind, _) in _readings)
        {
            var reading = Read(kind);

            read++;

            if (reading.Factor < 1.0)
            {
                glutted.Add((kind, reading));
            }
        }

        foreach (var row in _now.Values)
        {
            madeNow += row.Made;
            soldNow += row.Sold;
            peddledNow += row.Peddled;
        }

        foreach (var row in _then.Values)
        {
            madeNow += row.Made;
            soldNow += row.Sold;
            peddledNow += row.Peddled;
        }

        glutted.Sort(static (a, b) => a.Reading.Factor.CompareTo(b.Reading.Factor));

        using var worst = ValueStringBuilder.Create(256);

        for (var i = 0; i < glutted.Count && i < 6; i++)
        {
            if (i > 0)
            {
                worst.Append(", ");
            }

            var (kind, reading) = glutted[i];

            worst.Append($"{kind.Name} {reading.Pile} piled against {reading.Asked} asked at ×{reading.Factor:F2}");
        }

        if (glutted.Count > 6)
        {
            worst.Append(", and more");
        }

        return $"{(Enabled ? "on" : "off")}: {glutted.Count} of {read} kinds the makers asked about are glutted ({(glutted.Count == 0 ? "none" : worst.ToString())}); "
               + $"this half hour {madeNow} things were made, {soldNow} sold between bots and {peddledNow} peddled to shopkeepers ({MadeAll}, {SoldAll} and {PeddledAll} since the world loaded); "
               + $"{MadeIntoGlut} things were made into a glut and counted at the surplus price; the makers offered {OnSpec} stints on spec, {OnSpecGlutted} of them for a kind already glutted, and {Turned} choices on spec were turned from a glutted kind to another; "
               + $"shopkeepers' prices learned from peddles for {_scrap.Count} kinds; {TraderUnitsLeftOut} units of traders' gathering wants left out of the demand over the readings; a shelf of {Tolerance} is never a glut and the floor is ×{Floor:F2}";
    }

    public static void Reset()
    {
        _now.Clear();
        _then.Clear();
        _readings.Clear();
        _scrap.Clear();
        _turned = false;
        MadeAll = 0;
        SoldAll = 0;
        PeddledAll = 0;
        MadeIntoGlut = 0;
        OnSpec = 0;
        OnSpecGlutted = 0;
        Turned = 0;
        TraderUnitsLeftOut = 0;
    }
}
