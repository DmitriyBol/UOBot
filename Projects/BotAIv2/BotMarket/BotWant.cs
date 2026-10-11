using System;
using System.Collections.Generic;
using Server.Items;

namespace Server.BotAI.V2;

/// <summary>
/// One bot's spoken want: what it wants, how many, and what it says it will pay for one. Said aloud in the trade chat and
/// nothing more.
///
/// <para>
/// <b>Nothing is held, and that is the whole difference from the want it replaces (01.10.2026, Patrick's order of 30.09).</b>
/// The auction's want took the buyer's gold into escrow the moment it was posted and paid a supplier out of it from wherever
/// the supplier stood; the goods then waited in the market until the buyer's own beat fetched them. "Every order and every
/// purchase is hand to hand" leaves neither half of that standing. The gold stays in the buyer's pack and account until a
/// supplier is standing beside it with the goods (<see cref="BotHandover.Exchange"/>), and the goods stay in the supplier's
/// pack until then. So a want is a sentence with a price in it, and the only guarantee it needs is that the buyer could pay
/// when it said it — <see cref="BotMarket.Ask"/> asks the purse, and the market's beat drops a want whose buyer can no longer
/// pay for one.
/// </para>
///
/// <para>
/// <b>What is on its way is counted, so two carriers do not walk to one cook for the same five ribs.</b> A courier that takes
/// the delivery reserves the units it carries (<see cref="Reserve"/>); what is still open to another carrier is
/// <see cref="Payable"/>. The reservation goes with the delivery's end, however it ends.
/// </para>
///
/// <para>
/// The price moves the way the auction's want moved it: dearer when nobody answers for <see cref="BotMarket.RaiseMs"/>,
/// cheaper when answered again inside <see cref="BotMarket.BriskMs"/>, within ×<see cref="BotMarket.LeastMultiple"/> to
/// ×<see cref="BotMarket.MostMultiple"/> of what it opened at — and, since 03.10.2026, never past that multiple of what the
/// cheapest shopkeeper asks for one (<see cref="Shelf"/>, <see cref="Highest"/>).
/// </para>
/// </summary>
public sealed class BotWant
{
    private readonly List<(IBotWilful Courier, int Units, int Price)> _coming = [];

    private readonly List<BotLine> _lines = [];

    private readonly List<(IBotWilful Courier, int Units)> _away = [];

    public BotWant(int id, IBotWilful buyer, Type kind, int units, int offer)
    {
        var look = Look(kind);

        Id = id;
        Buyer = buyer;
        Kind = kind;
        Label = look.Label;
        ItemId = look.ItemId;
        Hue = look.Hue;
        Amount = Math.Max(1, units);
        Offer = Math.Max(1, offer);
        Anchor = Offer;
        Opened = Offer;
        PostedTick = Core.TickCount;
        NearTick = PostedTick;
        TouchedTick = PostedTick;
        FilledTick = PostedTick;
        DiedTick = PostedTick;
        RoseTick = PostedTick;
    }

    internal BotWant(int id, IBotWilful buyer, Type kind, int amount, int offer, int anchor, long postedTick, long touchedTick)
    {
        var look = Look(kind);

        Id = id;
        Buyer = buyer;
        Kind = kind;
        Label = look.Label;
        ItemId = look.ItemId;
        Hue = look.Hue;
        Amount = Math.Max(0, amount);
        Offer = Math.Max(1, offer);
        Anchor = Math.Max(1, anchor);
        Opened = Anchor;
        PostedTick = postedTick;
        NearTick = postedTick;
        TouchedTick = touchedTick;
        FilledTick = touchedTick;
        DiedTick = touchedTick;
        RoseTick = touchedTick;
    }

    internal void Restore(int filled, int paid, int raises, int cuts, bool traded, long filledTick)
    {
        Filled = Math.Max(0, filled);
        Paid = Math.Max(0, paid);
        Raises = Math.Max(0, raises);
        Cuts = Math.Max(0, cuts);
        Traded = traded;
        FilledTick = filledTick;
    }

    public IReadOnlyList<BotLine> Lines => _lines;

    public BotLine Newest => _lines.Count > 0 ? _lines[^1] : null;

    internal BotLine OpenLine(int units, long now, bool procurement)
    {
        var line = new BotLine(this, (Newest?.Seq ?? 0) + 1, units, now, procurement);

        _lines.Add(line);

        return line;
    }

    internal void AddLine(BotLine line)
    {
        if (line != null)
        {
            _lines.Add(line);
        }
    }

    private readonly int[] _passes = new int[BotLines.PassKinds];

    public void Passed(BotPass why) => _passes[(int)why]++;

    public int PassCount(int kind) => kind >= 0 && kind < _passes.Length ? _passes[kind] : 0;

    public int Offers { get; private set; }

    public void Offered()
    {
        Offers++;

        var now = Core.TickCount;

        if (!_farAsked || _farAskedTick != now)
        {
            NearTick = now;
        }
    }

    private bool _farAsked;

    private long _farAskedTick;

    internal void FarAsked()
    {
        _farAsked = true;
        _farAskedTick = Core.TickCount;
    }

    public void OfferedFar() => Offers++;

    public long NearTick { get; private set; }

    public bool Quiet(long now, int quietMs) => Coming == 0 && now - NearTick >= quietMs;

    public int Takes { get; private set; }

    internal bool Dead { get; private set; }

    internal long DiedTick { get; private set; }

    internal long RoseTick { get; private set; }

    internal bool Rising { get; set; }

    internal bool DiedWhileStanding { get; private set; }

    public long DeadMs { get; private set; }

    public long DeadMsAt(long now) => Dead ? DeadMs + Math.Max(0L, now - DiedTick) : DeadMs;

    internal bool Fell(long now)
    {
        if (Dead)
        {
            return false;
        }

        Dead = true;
        Rising = false;
        DiedTick = now;
        DiedWhileStanding = true;

        return true;
    }

    internal bool Rose(long now)
    {
        if (!Dead)
        {
            return false;
        }

        Dead = false;
        DeadMs += Math.Max(0L, now - DiedTick);
        RoseTick = now;
        Rising = true;

        return true;
    }

    internal bool Away { get; private set; }

    private long _awaySince;

    public long AwayMs { get; private set; }

    public long AwayMsAt(long now) => Away ? AwayMs + Math.Max(0L, now - _awaySince) : AwayMs;

    internal bool Left(long now)
    {
        if (Away)
        {
            return false;
        }

        Away = true;
        _awaySince = now;

        return true;
    }

    internal void Returned(long now)
    {
        if (!Away)
        {
            return;
        }

        Away = false;
        AwayMs += Math.Max(0L, now - _awaySince);
    }

    internal bool Short { get; set; }

    internal bool Uncovered { get; set; }

    public int Id { get; }

    public IBotWilful Buyer { get; }

    public Type Kind { get; }

    public string Label { get; }

    public int ItemId { get; }

    public int Hue { get; }

    public int Amount { get; private set; }

    public int Offer { get; private set; }

    public int Anchor { get; }

    public int Ceiling { get; internal set; }

    internal bool CeilingPending { get; set; }

    public int Shelf { get; internal set; }

    internal bool ShelfPending { get; set; }

    public int Opened { get; }

    public int Highest(double mostMultiple)
    {
        var top = Math.Max(1, (int)(Anchor * mostMultiple));

        if (Ceiling > 0 && Ceiling < top)
        {
            top = Ceiling;
        }

        if (Shelf > 0)
        {
            var shelf = (int)Math.Min(int.MaxValue, Shelf * (long)Math.Max(1.0, mostMultiple));

            if (shelf < top)
            {
                top = Math.Max(1, shelf);
            }
        }

        return top;
    }

    internal int Bring(int ceiling)
    {
        if (ceiling <= 0 || Offer <= ceiling)
        {
            return 0;
        }

        var down = Offer - ceiling;

        Offer = ceiling;
        TouchedTick = Core.TickCount;
        BotMarket.Touched();

        return down;
    }

    public long PostedTick { get; }

    public long TouchedTick { get; private set; }

    public long SaidTick { get; private set; }

    public bool Said { get; private set; }

    internal void Spoken(long now)
    {
        Said = true;
        SaidTick = now;
    }

    public int Filled { get; private set; }

    public int Paid { get; private set; }

    public bool Traded { get; private set; }

    public long FilledTick { get; private set; }

    public int Raises { get; private set; }

    public int Cuts { get; private set; }

    public int Coming
    {
        get
        {
            var held = Held;

            if (_away.Count == 0)
            {
                return held;
            }

            var away = 0;

            for (var i = 0; i < _away.Count; i++)
            {
                away += _away[i].Units;
            }

            return Math.Max(held, Math.Min(Amount, held + away));
        }
    }

    private int Held
    {
        get
        {
            var total = 0;

            for (var i = 0; i < _coming.Count; i++)
            {
                total += _coming[i].Units;
            }

            return total;
        }
    }

    public int Payable => Math.Max(0, Amount - Held);

    public bool IsOpen => Payable > 0;

    public int Worth => Payable * Offer;

    public void Top(int units)
    {
        if (units > 0)
        {
            Amount += units;
        }

        TouchedTick = Core.TickCount;
        BotMarket.Touched();
    }

    internal void Reserve(IBotWilful courier, int units) => Reserve(courier, units, PriceOf(courier));

    internal void Reserve(IBotWilful courier, int units, int price, bool retake = false)
    {
        var again = Carried(courier);

        Release(courier);

        if (courier != null && units > 0)
        {
            _coming.Add((courier, units, Math.Max(0, price)));
            BotMarket.Touched();

            if (!again && !retake)
            {
                Takes++;
            }
        }
    }

    internal void Release(IBotWilful courier)
    {
        for (var i = _coming.Count - 1; i >= 0; i--)
        {
            if (ReferenceEquals(_coming[i].Courier, courier))
            {
                _coming.RemoveAt(i);
                BotMarket.Touched();
            }
        }

        for (var i = _away.Count - 1; i >= 0; i--)
        {
            if (ReferenceEquals(_away[i].Courier, courier))
            {
                _away.RemoveAt(i);
            }
        }
    }

    internal int PriceOf(IBotWilful courier)
    {
        for (var i = 0; i < _coming.Count; i++)
        {
            if (ReferenceEquals(_coming[i].Courier, courier))
            {
                return _coming[i].Price;
            }
        }

        return 0;
    }

    public int Premium
    {
        get
        {
            long total = 0;

            for (var i = 0; i < _coming.Count; i++)
            {
                total += (long)_coming[i].Units * Math.Max(0, _coming[i].Price - Offer);
            }

            return (int)Math.Min(int.MaxValue, total);
        }
    }

    public int PremiumOf(IBotWilful courier)
    {
        long total = 0;

        for (var i = 0; i < _coming.Count; i++)
        {
            if (ReferenceEquals(_coming[i].Courier, courier))
            {
                total += (long)_coming[i].Units * Math.Max(0, _coming[i].Price - Offer);
            }
        }

        return (int)Math.Min(int.MaxValue, total);
    }

    public bool Carried(IBotWilful courier)
    {
        for (var i = 0; i < _coming.Count; i++)
        {
            if (ReferenceEquals(_coming[i].Courier, courier))
            {
                return true;
            }
        }

        return false;
    }

    internal int Sweep()
    {
        var swept = 0;

        for (var i = _coming.Count - 1; i >= 0; i--)
        {
            var courier = _coming[i].Courier;

            var held = courier?.Resolve?.Deed as IBotWantCarrier ?? courier?.Resolve?.Paused?.Deed as IBotWantCarrier;

            if (courier?.Self is not { Deleted: false, Alive: true } || held == null || !held.Carries(this) || held.Stale)
            {
                if (held is BotDeliver { Away: true } && held.Carries(this) && courier?.Self is { Deleted: false, Alive: true })
                {
                    _away.Add((courier, _coming[i].Units));
                }

                _coming.RemoveAt(i);
                swept++;
                BotMarket.Touched();
            }
        }

        for (var i = _away.Count - 1; i >= 0; i--)
        {
            var courier = _away[i].Courier;
            var held = courier?.Resolve?.Deed as IBotWantCarrier ?? courier?.Resolve?.Paused?.Deed as IBotWantCarrier;

            if (courier?.Self is not { Deleted: false, Alive: true } || held is not BotDeliver { Away: true } || !held.Carries(this))
            {
                _away.RemoveAt(i);
            }
        }

        return swept;
    }

    internal bool Took(int units, int gold, int briskMs)
    {
        var now = Core.TickCount;

        Amount = Math.Max(0, Amount - units);
        Filled += units;
        Paid += gold;
        BotMarket.Touched();

        var brisk = Traded && now - FilledTick < briskMs;

        Traded = true;
        FilledTick = now;
        TouchedTick = now;

        return brisk;
    }

    public int Stepped(double step, double mostMultiple)
    {
        var ceiling = Highest(mostMultiple);

        var offering = Math.Max(Offer + 1, (int)(Offer * (1.0 + step)));

        if (offering > ceiling)
        {
            offering = ceiling;
        }

        return offering > Offer ? offering : 0;
    }

    internal void Lift(int offering)
    {
        if (offering <= Offer)
        {
            return;
        }

        Offer = offering;
        Raises++;
        TouchedTick = Core.TickCount;
        BotMarket.Touched();
    }

    internal void Waited() => TouchedTick = Core.TickCount;

    internal void CutTo(int offer, int ceiling)
    {
        Ceiling = Math.Max(1, ceiling);

        CeilingPending = false;

        if (offer < Offer)
        {
            Offer = Math.Max(1, offer);
            BotMarket.Touched();
        }
    }

    internal bool Cut(double step, double leastMultiple)
    {
        var floor = Math.Max(1, (int)(Anchor * leastMultiple));
        var offering = Math.Min(Offer - 1, (int)(Offer * (1.0 - step)));

        if (offering < floor)
        {
            offering = floor;
        }

        if (offering >= Offer)
        {
            return false;
        }

        Offer = offering;
        Cuts++;
        TouchedTick = Core.TickCount;
        BotMarket.Touched();

        return true;
    }

    internal static (string Label, int ItemId, int Hue) Look(Type kind)
    {
        if (kind == null)
        {
            return ("?", 0x1F4C, 0);
        }

        if (_looks.TryGetValue(kind, out var known))
        {
            return known;
        }

        var look = (BotMarket.Spaced(kind.Name), 0x1F4C, 0);

        var sample = kind.CreateInstance<Item>();

        if (sample != null)
        {
            var name = sample.Name;

            look = (string.IsNullOrWhiteSpace(name) ? BotMarket.Spaced(kind.Name) : name, sample.ItemID, sample.Hue);

            sample.Delete();
        }

        _looks[kind] = look;

        return look;
    }

    private static readonly Dictionary<Type, (string Label, int ItemId, int Hue)> _looks = [];

    public override string ToString() => $"{Amount} × {Label} wanted at {Offer}gp";
}
