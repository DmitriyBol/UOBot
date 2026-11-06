using System;

namespace Server.BotAI.V2;

public enum BotPass
{

    Reach,

    Land,

    Sealed,

    Poor,

    Petty,

    Material,

    Source,

    Margin,

    Peril,

    Dead,

    Road,

    Room
}

[Flags]
public enum BotRoute
{
    None = 0,

    Carrier = 1,

    Trader = 2,

    Shop = 4
}

public enum BotWithdraw
{

    Have,

    Walked,

    Unpaid,

    Closed,

    Other
}

public enum BotLineState
{

    Open,

    Met,

    Failed,

    KeptOut
}

public sealed class BotLine
{

    private const int MostHands = 4;

    private readonly Serial[] _hands = new Serial[MostHands];

    internal BotLine(BotWant want, int seq, int units, long opened, bool procurement)
    {
        Want = want;
        Seq = seq;
        Units = Math.Max(1, units);
        OpenedTick = opened;
        Procurement = procurement;

        FirstFillTick = opened;
        Snapshot();
    }

    public BotWant Want { get; }

    public int Seq { get; }

    public string Name => $"{Want?.Id ?? 0}.{Seq}";

    public int Units { get; private set; }

    public int Filled { get; private set; }

    public long OpenedTick { get; private set; }

    public long FirstFillTick { get; private set; }

    public bool AnyFill { get; private set; }

    public bool Procurement { get; }

    public bool Restored { get; internal set; }

    public BotLineState State { get; internal set; }

    public bool MetLate { get; internal set; }

    public string Cause { get; internal set; }

    public BotRoute Routes { get; private set; }

    public int Hands { get; private set; }

    public bool Complete => Filled >= Units;

    public bool Settled => State != BotLineState.Open;

    internal readonly int[] PassBase = new int[BotLines.PassKinds];

    internal int OffersBase;

    internal int TakesBase;

    internal long DeadBase;

    internal long AwayBase;

    internal void Snapshot()
    {
        if (Want == null)
        {
            return;
        }

        for (var i = 0; i < PassBase.Length; i++)
        {
            PassBase[i] = Want.PassCount(i);
        }

        OffersBase = Want.Offers;
        TakesBase = Want.Takes;
        DeadBase = Want.DeadMs;
        AwayBase = Want.AwayMs;
    }

    internal void More(int units)
    {
        if (units > 0)
        {
            Units += units;
        }
    }

    internal void Fill(int units, Mobile seller, BotRoute route, long now)
    {
        if (units <= 0)
        {
            return;
        }

        if (!AnyFill)
        {
            AnyFill = true;
            FirstFillTick = now;
        }

        Filled += units;
        Routes |= route;

        if (seller == null)
        {
            return;
        }

        var known = Math.Min(Hands, MostHands);

        for (var i = 0; i < known; i++)
        {
            if (_hands[i] == seller.Serial)
            {
                return;
            }
        }

        if (Hands < MostHands)
        {
            _hands[Hands] = seller.Serial;
        }

        Hands++;
    }

    internal int PassedSince(int kind) => Want == null ? 0 : Math.Max(0, Want.PassCount(kind) - PassBase[kind]);

    internal int OffersSince => Want == null ? 0 : Math.Max(0, Want.Offers - OffersBase);

    internal int TakesSince => Want == null ? 0 : Math.Max(0, Want.Takes - TakesBase);

    internal void Save(IGenericWriter writer, long now)
    {
        writer.WriteEncodedInt(Seq);
        writer.WriteEncodedInt(Units);
        writer.WriteEncodedInt(Filled);
        writer.Write(Math.Max(0L, now - OpenedTick));
        writer.Write(AnyFill);
        writer.Write(AnyFill ? Math.Max(0L, now - FirstFillTick) : 0L);
        writer.WriteEncodedInt((int)State);
        writer.Write(MetLate);
        writer.Write(Cause ?? "");
        writer.WriteEncodedInt((int)Routes);
        writer.Write(Procurement);

        var known = Math.Min(Hands, MostHands);

        writer.WriteEncodedInt(Hands);
        writer.WriteEncodedInt(known);

        for (var i = 0; i < known; i++)
        {
            writer.Write(_hands[i]);
        }
    }

    internal readonly record struct Record(
        int Seq, int Units, int Filled, long Age, bool AnyFill, long FirstAge, int State, bool MetLate, string Cause, int Routes,
        bool Procurement, int Hands, Serial[] Known
    );

    internal static Record Read(IGenericReader reader)
    {
        var seq = reader.ReadEncodedInt();
        var units = reader.ReadEncodedInt();
        var filled = reader.ReadEncodedInt();
        var age = reader.ReadLong();
        var anyFill = reader.ReadBool();
        var firstAge = reader.ReadLong();
        var state = reader.ReadEncodedInt();
        var metLate = reader.ReadBool();
        var cause = reader.ReadString();
        var routes = reader.ReadEncodedInt();
        var procurement = reader.ReadBool();
        var hands = reader.ReadEncodedInt();
        var known = reader.ReadEncodedInt();
        var serials = new Serial[Math.Max(0, known)];

        for (var i = 0; i < serials.Length; i++)
        {
            serials[i] = reader.ReadSerial();
        }

        return new Record(seq, units, filled, age, anyFill, firstAge, state, metLate, cause, routes, procurement, hands, serials);
    }

    internal static BotLine Restore(BotWant want, in Record record, long now)
    {
        var line = new BotLine(want, Math.Max(1, record.Seq), record.Units, now - Math.Max(0L, record.Age), record.Procurement)
        {
            Filled = Math.Max(0, record.Filled),
            AnyFill = record.AnyFill,
            State = Enum.IsDefined(typeof(BotLineState), record.State) ? (BotLineState)record.State : BotLineState.Open,
            MetLate = record.MetLate,
            Cause = string.IsNullOrEmpty(record.Cause) ? null : record.Cause,
            Routes = (BotRoute)(record.Routes & 7),
            Restored = true
        };

        if (record.AnyFill)
        {
            line.FirstFillTick = now - Math.Max(0L, record.FirstAge);
        }

        var known = record.Known ?? [];

        for (var i = 0; i < known.Length && i < MostHands; i++)
        {
            line._hands[i] = known[i];
        }

        line.Hands = Math.Max(record.Hands, Math.Min(known.Length, MostHands));

        return line;
    }
}
