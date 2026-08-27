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
        ListedTick = Core.TickCount;
        TouchedTick = Core.TickCount;
    }

    public int Id { get; }

    /// <summary>Whose stall it is. Held as a reference, so a deleted bot's stall goes with it.</summary>
    public IBotWilful Seller { get; }

    public Type Kind { get; }

    /// <summary>What to call it in a gump. Taken from the first thing listed, never guessed from the type.</summary>
    public string Label { get; }

    /// <summary>The art, so a dashboard can show the thing rather than its name.</summary>
    public int ItemId { get; }

    public int Hue { get; }

    /// <summary>Gold per unit, as this bot currently reckons it.</summary>
    public int Price { get; private set; }

    /// <summary>
    /// What it first asked. Both bounds on price movement are multiples of this, so a run of luck in either
    /// direction cannot walk the price off the map.
    /// </summary>
    public int Anchor { get; }

    /// <summary>Units sold over the life of the stall, and what they came to.</summary>
    public int Sold { get; private set; }

    public int Earned { get; private set; }

    /// <summary>Whether anything has ever sold here, and when the last one did.</summary>
    public bool Traded { get; private set; }

    public long SoldTick { get; private set; }

    /// <summary>When anything last happened to this stall — a sale, a top-up or a price move.</summary>
    public long TouchedTick { get; private set; }

    /// <summary>
    /// When the stall opened, and it never moves again.
    ///
    /// Separate from <see cref="TouchedTick"/> because the two answer different questions and only this one
    /// answers the useful one. "Has anybody wanted this in half an hour" cannot be asked of a tick that a
    /// price cut resets — that clock restarts every time the stall gives up a little more, so it is never
    /// more than one beat old. How long the goods have been on offer is what matters, and that is this.
    /// </summary>
    public long ListedTick { get; }

    /// <summary>How many times the price has moved, and which way. For the dashboard and the log.</summary>
    public int Raises { get; private set; }

    public int Cuts { get; private set; }

    /// <summary>How much is on offer. Counted rather than cached: stacks are mutated in place when sold.</summary>
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

    /// <summary>
    /// One of the things on offer, or null when there are none.
    ///
    /// Exists because a shopkeeper can only be asked whether it buys <em>this object</em> — <c>IsSellable</c>
    /// looks at the item, not the type — so anybody wondering what a stall's goods would fetch over a counter
    /// needs something to show.
    /// </summary>
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

    /// <summary>What the stall is worth at the asking price.</summary>
    public int Worth => Amount * Price;

    /// <summary>
    /// Adds goods. The stock list holds whole objects rather than one merged stack, because merging is what
    /// makes a partial sale need to invent an item — see <see cref="Deliver"/>.
    /// </summary>
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

    /// <summary>
    /// Hands over up to <paramref name="units"/> units into <paramref name="into"/>, and says how many
    /// actually went. Does not touch money — see <see cref="BotAuction.Buy"/>.
    ///
    /// <para>
    /// Whole objects while they fit and a split for the remainder. The split is the only fiddly part: nothing
    /// in the engine hands back "half of this stack", so a new one of the same type is made and the original
    /// is reduced. A type that cannot be made that way — no parameterless constructor — is sold in whole
    /// objects only, which is the honest fallback rather than a broken sale.
    /// </para>
    /// </summary>
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

    /// <summary>Everything left, dropped into a container. For a stall being withdrawn.</summary>
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

    /// <summary>Everything left, destroyed. For a world that is being replaced.</summary>
    public void Discard() => Reclaim(null);

    /// <summary>
    /// Notes a sale and returns whether it was brisk enough to be worth putting the price up.
    ///
    /// <b>Brisk means "again, soon" rather than "a lot".</b> Volume says how much somebody wanted at the
    /// price already asked; the gap between two sales is the thing that says the price was too low.
    /// </summary>
    public bool Note(int units, int gold, int briskMs)
    {
        var now = Core.TickCount;
        var brisk = Traded && now - SoldTick < briskMs;

        Sold += units;
        Earned += gold;
        Traded = true;
        SoldTick = now;
        TouchedTick = now;

        return brisk;
    }

    /// <summary>Puts the price up, within the bound. Returns whether it actually moved.</summary>
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

    /// <summary>
    /// Puts the price down, within the bound. Returns whether it actually moved.
    ///
    /// <b>Two floors, and the market's own is the harder of them.</b> A quarter of the opening ask is what
    /// this stall may fall to; <see cref="BotAuction.Floor"/> is what anything on this market may fall to,
    /// and a cut that ignored it would walk straight past a rule the listing side enforces. See that field
    /// for why the number exists at all.
    /// </summary>
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

    /// <summary>
    /// Takes <paramref name="units"/> off a stack as a new object, leaving the rest where it was, or null if
    /// the type cannot be made from nothing.
    ///
    /// Public because the demand side needs exactly the same thing for exactly the same reason: a supplier
    /// delivering a slice of what it carries is a partial stack, and nothing in the engine hands back half of
    /// one.
    /// </summary>
    public static Item Portion(Item from, int units)
    {
        if (!from.Stackable || units <= 0 || units >= from.Amount)
        {
            return null;
        }

        // The engine's own activator rather than the framework's, and it is not a preference.
        // <c>Activator.CreateInstance</c> looks for a genuinely parameterless constructor; almost every
        // stackable in the game declares <c>Foo(int amount = 1)</c> instead, which has none. So the old call
        // threw for ore, reagents, scrolls and bandages alike, and every one of them was quietly sold in whole
        // objects only. This one fills optional parameters with <c>Type.Missing</c> and gets the object.
        var made = from.GetType().CreateInstance<Item>();

        if (made == null)
        {
            // A type that cannot be made from nothing after all. Whole objects only, and every caller is
            // already written to cope with a short delivery.
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

        // The type's own name, spaced out: "IronIngot" reads as "Iron Ingot". Better than a cliloc number a
        // dashboard cannot render and better than "Unknown".
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
