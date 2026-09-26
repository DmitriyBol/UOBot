using System;
using System.Collections.Generic;
using Server.Items;

namespace Server.BotAI.V2;

/// <summary>
/// One bot's standing offer of one kind of thing: what it is, how much of it is left, what it is asking, and
/// what it has learned from selling it.
///
/// <para>
/// <b>One listing per seller per kind, for the life of the shard — it is not a ticket, it is a stall.</b>
/// Topping up adds to the same listing rather than making another, which is what makes the asking price and
/// the sales history mean something: a bot that has learned iron ingots move at nine gold keeps that number
/// when the next load comes in. A market of one-shot tickets would relearn its own prices from nothing every
/// trip, which is how the first version's auction managed to be busy and know nothing.
/// </para>
///
/// <para>
/// <b>Nothing expires.</b> Goods sit here as long as it takes; there are no listing fees, no durations and no
/// re-listing chores, because every one of those is a mechanic whose only output is bookkeeping. What replaces
/// them is the price moving — an unsold stall gets cheaper, which is the market saying the same thing an
/// expiry would have said, in the one language a bot can act on.
/// </para>
///
/// <para>
/// The goods themselves are held here, out of the world. That is the point of a market rather than a promise:
/// a bot cannot sell the same ingots twice, cannot drop them, and cannot lose them to whatever kills it on
/// the way home.
/// </para>
/// </summary>
public sealed class BotListing
{
    private readonly List<Item> _stock = [];

    public BotListing(int id, IBotWilful seller, Item first, int price)
    {
        Id = id;
        Seller = seller;
        Kind = first.GetType();
        Label = Name(first);
        ItemId = first.ItemID;
        Hue = first.Hue;
        Price = Math.Max(1, price);
        Anchor = Price;
        DealtTick = Core.TickCount;
        ListedTick = Core.TickCount;
        TouchedTick = Core.TickCount;
    }

    public int Id { get; }

    public IBotWilful Seller { get; }

    public Type Kind { get; }

    public string Label { get; }

    public int ItemId { get; }

    public int Hue { get; }

    public int Price { get; private set; }

    public int Anchor { get; }

    public int Sold { get; private set; }

    public int Earned { get; private set; }

    public bool Traded { get; private set; }

    public long DealtTick { get; private set; }

    public long SoldTick { get; private set; }

    public long TouchedTick { get; private set; }

    public long ListedTick { get; }

    public int Raises { get; private set; }

    public int Cuts { get; private set; }

    public int Amount
    {
        get
        {
            var total = 0;

            for (var i = 0; i < _stock.Count; i++)
            {
                var item = _stock[i];

                if (item is { Deleted: false })
                {
                    total += Math.Max(1, item.Amount);
                }
            }

            return total;
        }
    }

    public bool IsEmpty => Amount <= 0;

    public Item Sample
    {
        get
        {
            for (var i = 0; i < _stock.Count; i++)
            {
                if (_stock[i] is { Deleted: false })
                {
                    return _stock[i];
                }
            }

            return null;
        }
    }

    public int Worth => Amount * Price;

    public void Add(Item item)
    {
        if (item == null || item.Deleted)
        {
            return;
        }

        item.Internalize();

        _stock.Add(item);

        TouchedTick = Core.TickCount;
    }

    public int Deliver(int units, Container into)
    {
        if (units <= 0 || into == null)
        {
            return 0;
        }

        var given = 0;

        for (var i = _stock.Count - 1; i >= 0 && given < units; i--)
        {
            var item = _stock[i];

            if (item == null || item.Deleted)
            {
                _stock.RemoveAt(i);

                continue;
            }

            var held = Math.Max(1, item.Amount);
            var wanted = units - given;

            if (held <= wanted)
            {
                _stock.RemoveAt(i);
                into.DropItem(item);

                given += held;

                continue;
            }

            var split = Portion(item, wanted);

            if (split == null)
            {
                continue;
            }

            into.DropItem(split);

            given += wanted;
        }

        if (given > 0)
        {
            TouchedTick = Core.TickCount;
        }

        return given;
    }

    public Item Lift(int units)
    {
        if (units <= 0)
        {
            return null;
        }

        for (var i = _stock.Count - 1; i >= 0; i--)
        {
            var item = _stock[i];

            if (item == null || item.Deleted)
            {
                _stock.RemoveAt(i);

                continue;
            }

            var held = Math.Max(1, item.Amount);

            if (held <= units)
            {
                _stock.RemoveAt(i);
                TouchedTick = Core.TickCount;

                return item;
            }

            var split = Portion(item, units);

            if (split == null)
            {
                continue;
            }

            TouchedTick = Core.TickCount;

            return split;
        }

        return null;
    }

    public int Reclaim(Container into)
    {
        var moved = 0;

        for (var i = _stock.Count - 1; i >= 0; i--)
        {
            var item = _stock[i];

            _stock.RemoveAt(i);

            if (item == null || item.Deleted)
            {
                continue;
            }

            if (into == null)
            {
                item.Delete();

                continue;
            }

            into.DropItem(item);
            moved++;
        }

        return moved;
    }

    public int Return(Mobile owner)
    {
        var pack = owner?.Backpack;

        if (pack == null)
        {
            return 0;
        }

        var moved = 0;

        for (var i = _stock.Count - 1; i >= 0; i--)
        {
            var item = _stock[i];

            if (item == null || item.Deleted)
            {
                _stock.RemoveAt(i);

                continue;
            }

            if (!pack.TryDropItem(owner, item, false))
            {
                break;
            }

            _stock.RemoveAt(i);
            moved++;
        }

        if (moved > 0)
        {
            TouchedTick = Core.TickCount;
        }

        return moved;
    }

    public void Discard() => Reclaim(null);

    public bool Note(int units, int gold, int briskMs)
    {
        var now = Core.TickCount;
        var brisk = Traded && now - SoldTick < briskMs;

        Sold += units;
        DealtTick = Core.TickCount;
        Earned += gold;
        Traded = true;
        SoldTick = now;
        TouchedTick = now;

        return brisk;
    }

    public bool Meet(int offer, double step, double leastMultiple, double mostMultiple)
    {
        if (offer <= 0 || step <= 0.0)
        {
            return false;
        }

        var floor = Math.Max(BotAuction.Floor, (int)(Anchor * leastMultiple));
        var ceiling = Math.Max(1, (int)(Anchor * mostMultiple));
        var want = Math.Clamp(offer, floor, ceiling);

        if (want == Price)
        {
            return false;
        }

        var stride = Math.Max(1, (int)(Math.Abs(want - Price) * step));
        var asking = want > Price ? Math.Min(want, Price + stride) : Math.Max(want, Price - stride);

        if (asking == Price)
        {
            return false;
        }

        if (asking > Price)
        {
            Raises++;
        }
        else
        {
            Cuts++;
        }

        Price = asking;
        TouchedTick = Core.TickCount;

        return true;
    }

    public bool Raise(double step, double mostMultiple)
    {
        var ceiling = Math.Max(1, (int)(Anchor * mostMultiple));
        var asking = Math.Max(Price + 1, (int)(Price * (1.0 + step)));

        if (asking > ceiling)
        {
            asking = ceiling;
        }

        if (asking <= Price)
        {
            return false;
        }

        Price = asking;
        Raises++;
        TouchedTick = Core.TickCount;

        return true;
    }

    public bool Cut(double step, double leastMultiple)
    {
        var floor = Math.Max(BotAuction.Floor, (int)(Anchor * leastMultiple));
        var asking = Math.Min(Price - 1, (int)(Price * (1.0 - step)));

        if (asking < floor)
        {
            asking = floor;
        }

        if (asking >= Price)
        {
            return false;
        }

        Price = asking;
        Cuts++;
        TouchedTick = Core.TickCount;

        return true;
    }

    public static Item Portion(Item from, int units)
    {
        if (!from.Stackable || units <= 0 || units >= from.Amount)
        {
            return null;
        }

        var made = from.GetType().CreateInstance<Item>();

        if (made == null)
        {
            return null;
        }

        made.Hue = from.Hue;
        made.Amount = units;

        from.Amount -= units;

        return made;
    }

    private static string Name(Item item)
    {
        var name = item.Name;

        if (!string.IsNullOrWhiteSpace(name))
        {
            return name;
        }

        var raw = item.GetType().Name;

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

    public override string ToString() => $"{Amount} × {Label} at {Price}gp";
}
