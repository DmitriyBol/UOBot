using System;
using System.Collections.Generic;
using Server.Items;

namespace Server.BotAI.V2;

/// <summary>
/// One bot's standing offer to <em>buy</em> one kind of thing: what it wants, how many, what it is paying,
/// and the money it has already put down.
///
/// <para>
/// <b>The mirror of <see cref="BotListing"/>, deliberately and all the way down.</b> One want per buyer per
/// kind, for the life of the shard; topping up adds to the same want; nothing expires, and instead of an
/// expiry the price moves — a want nobody fills gets <em>dearer</em>, and a want filled the moment it is
/// posted gets cheaper. Both are the same sentence a stall says, with the sign turned round, which is why
/// they share one set of numbers in one configuration file. The first version had "I have" and "I want" in
/// four subsystems and an auction; every one of them had to learn prices separately, and none of them did.
/// </para>
///
/// <para>
/// <b>The money is down before the want is on the board.</b> Nothing here is a promise: the gold is taken
/// out of the buyer's purse and account when it asks, and it is held as a number until somebody earns it or
/// the want is given up. The first version left this out and somebody offered fifteen hundred gold for
/// twenty feathers with an empty purse — and the cost of that is not the coin, it is that no bot can then
/// tell an offer worth crossing a continent for from one that will not be honoured on arrival. It is also
/// the only defence against a bot bidding absurdly to make its own production look valuable: an offer costs
/// exactly what it says it costs.
/// </para>
///
/// <para>
/// <b>What has been delivered waits here rather than being pushed at the buyer.</b> A market holds goods out
/// of the world — that is the whole difference between a market and a promise — and it holds them for the
/// buyer for the same reasons it holds them for the seller: a pack can be full, a bot can be underground,
/// and a delivery that can fail on the receiving end is a delivery that can lose the goods and the money at
/// once.
/// </para>
/// </summary>
public sealed class BotWant
{
    private readonly List<Item> _holding = [];

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
        TouchedTick = Core.TickCount;
    }

    public int Id { get; }

    public IBotWilful Buyer { get; }

    public Type Kind { get; }

    public string Label { get; }

    public int ItemId { get; }

    public int Hue { get; }

    public int Amount { get; private set; }

    public int Offer { get; private set; }

    public int Anchor { get; }

    public int Escrow { get; private set; }

    public int Filled { get; private set; }

    public int Paid { get; private set; }

    public bool Traded { get; private set; }

    public long FilledTick { get; private set; }

    public long TouchedTick { get; private set; }

    public int Raises { get; private set; }

    public int Cuts { get; private set; }

    public IBotWilful LastSupplier { get; private set; }

    public long LastSupplierTick { get; private set; }

    public int Payable => Math.Min(Amount, Escrow / Offer);

    public bool IsOpen => Payable > 0;

    public int Waiting
    {
        get
        {
            var total = 0;

            for (var i = 0; i < _holding.Count; i++)
            {
                var item = _holding[i];

                if (item is { Deleted: false })
                {
                    total += Math.Max(1, item.Amount);
                }
            }

            return total;
        }
    }

    public int Worth => Payable * Offer;

    public void Top(int units, int gold)
    {
        if (units > 0)
        {
            Amount += units;
        }

        if (gold > 0)
        {
            Escrow += gold;
        }

        TouchedTick = Core.TickCount;
    }

    public bool Yields(IBotWilful supplier, int sliceMs) =>
        !ReferenceEquals(LastSupplier, supplier) || Core.TickCount - LastSupplierTick >= sliceMs;

    public int Take(Item goods, IBotWilful supplier, int units, int briskMs)
    {
        var now = Core.TickCount;
        var bill = units * Offer;

        goods.Internalize();

        _holding.Add(goods);

        Amount -= units;
        Escrow -= bill;
        Filled += units;
        Paid += bill;

        var brisk = Traded && now - FilledTick < briskMs;

        Traded = true;
        FilledTick = now;
        TouchedTick = now;

        LastSupplier = supplier;
        LastSupplierTick = now;

        return brisk ? bill : -bill;
    }

    public int Collect(Container into)
    {
        var moved = 0;

        for (var i = _holding.Count - 1; i >= 0; i--)
        {
            var item = _holding[i];

            _holding.RemoveAt(i);

            if (item == null || item.Deleted)
            {
                continue;
            }

            if (into == null)
            {
                item.Delete();

                continue;
            }

            moved += Math.Max(1, item.Amount);

            into.DropItem(item);
        }

        if (moved > 0)
        {
            TouchedTick = Core.TickCount;
        }

        return moved;
    }

    public int Close()
    {
        var owed = Escrow;

        Escrow = 0;
        Amount = 0;

        return owed;
    }

    public void Discard() => Collect(null);

    public int Stepped(double step, double mostMultiple)
    {
        var ceiling = Math.Max(1, (int)(Anchor * mostMultiple));
        var offering = Math.Max(Offer + 1, (int)(Offer * (1.0 + step)));

        if (offering > ceiling)
        {
            offering = ceiling;
        }

        return offering > Offer ? offering : 0;
    }

    public void Lift(int offering)
    {
        if (offering <= Offer)
        {
            return;
        }

        Offer = offering;
        Raises++;
        TouchedTick = Core.TickCount;
    }

    public bool Cut(double step, double leastMultiple)
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

        return true;
    }

    private static (string Label, int ItemId, int Hue) Look(Type kind)
    {
        if (kind == null)
        {
            return ("?", 0x1F4C, 0);
        }

        if (_looks.TryGetValue(kind, out var known))
        {
            return known;
        }

        var look = (Spaced(kind.Name), 0x1F4C, 0);

        var sample = kind.CreateInstance<Item>();

        if (sample != null)
        {
            var name = sample.Name;

            look = (string.IsNullOrWhiteSpace(name) ? Spaced(kind.Name) : name, sample.ItemID, sample.Hue);

            sample.Delete();
        }

        _looks[kind] = look;

        return look;
    }

    private static readonly Dictionary<Type, (string Label, int ItemId, int Hue)> _looks = [];

    private static string Spaced(string raw)
    {
        using var spaced = Server.Text.ValueStringBuilder.Create(raw.Length + 8);

        for (var i = 0; i < raw.Length; i++)
        {
            var c = raw[i];

            if (i > 0 && char.IsUpper(c))
            {
                spaced.Append(" ");
            }

            spaced.Append(c);
        }

        return spaced.ToString();
    }

    public override string ToString() => $"{Amount} × {Label} wanted at {Offer}gp";
}
