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
    /// <summary>What has been delivered and not yet collected.</summary>
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

    /// <summary>Whose want it is. A reference, so a deleted bot's want goes with it.</summary>
    public IBotWilful Buyer { get; }

    public Type Kind { get; }

    public string Label { get; }

    public int ItemId { get; }

    public int Hue { get; }

    /// <summary>How many are still wanted.</summary>
    public int Amount { get; private set; }

    /// <summary>Gold per unit, as this bot currently reckons it.</summary>
    public int Offer { get; private set; }

    /// <summary>What it first offered. Both bounds on movement are multiples of this.</summary>
    public int Anchor { get; }

    /// <summary>
    /// Gold taken from the buyer and held here.
    ///
    /// <b>This, and not <see cref="Amount"/>, is what the want can actually buy.</b> Raising the offer
    /// without putting more money down buys fewer things at a better price, which is what raising an offer
    /// means and needs no rule of its own.
    /// </summary>
    public int Escrow { get; private set; }

    /// <summary>Units received over the life of the want, and what they came to.</summary>
    public int Filled { get; private set; }

    public int Paid { get; private set; }

    public bool Traded { get; private set; }

    public long FilledTick { get; private set; }

    /// <summary>When anything last happened to this want — a delivery, a top-up or a price move.</summary>
    public long TouchedTick { get; private set; }

    public int Raises { get; private set; }

    public int Cuts { get; private set; }

    /// <summary>Who filled it last, and when. The whole of the rule that one supplier may not take it all.</summary>
    public IBotWilful LastSupplier { get; private set; }

    public long LastSupplierTick { get; private set; }

    /// <summary>How many units the money on the table will actually pay for.</summary>
    public int Payable => Math.Min(Amount, Escrow / Offer);

    /// <summary>Whether this want is still asking for anything it can pay for.</summary>
    public bool IsOpen => Payable > 0;

    /// <summary>What is sitting here waiting to be collected.</summary>
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

    /// <summary>What the want is worth to whoever can fill it: everything it can still pay for.</summary>
    public int Worth => Payable * Offer;

    /// <summary>Adds money and units to an existing want. The offer already on it is not overwritten.</summary>
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

    /// <summary>
    /// Whether this supplier may fill this want at this moment.
    ///
    /// <b>The one rule here that is about fairness rather than about arithmetic.</b> A supplier with a large
    /// stock would otherwise close a want whole the instant it appeared, and the price would never get the
    /// chance to fall that would have told a second supplier to look elsewhere — so the first bot to own a
    /// pile owns every want for that pile. One supplier takes at most a slice, and then the want goes back
    /// on the board before it will take from the same one again. It is a window rather than a quota: if
    /// nobody else is producing, the same supplier comes back and finishes the job.
    ///
    /// It says nothing about a want for a single indivisible thing, and cannot: one scroll goes to one
    /// scribe.
    /// </summary>
    public bool Yields(IBotWilful supplier, int sliceMs) =>
        !ReferenceEquals(LastSupplier, supplier) || Core.TickCount - LastSupplierTick >= sliceMs;

    /// <summary>
    /// Takes delivery of goods against this want and says what it cost. Does not move money — see
    /// <see cref="BotAuction.Fill"/>.
    /// </summary>
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

        // Brisk means "again, soon" here too, and it means the offer was generous: somebody was willing
        // twice in ten minutes, so the want can afford to ask for less.
        return brisk ? bill : -bill;
    }

    /// <summary>Everything delivered, handed to the buyer. Returns units collected.</summary>
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

    /// <summary>
    /// Gives up: the money left goes back and the want is finished with. Returns what is owed to the buyer.
    ///
    /// Called when the offer has run out of room to rise and still nobody has filled it. That is information
    /// rather than a failure — the shard has said, in the only language it has, that nobody can make this —
    /// and it must not sit there holding the buyer's money for ever while saying it.
    /// </summary>
    public int Close()
    {
        var owed = Escrow;

        Escrow = 0;
        Amount = 0;

        return owed;
    }

    /// <summary>Goods left here destroyed. For a world that is being replaced.</summary>
    public void Discard() => Collect(null);

    /// <summary>
    /// What a raise would make the offer, or zero when there is no room left to move.
    ///
    /// <para>
    /// Split from the act of raising because <b>a raise has to be paid for</b>, and that turns out to be the
    /// whole mechanism rather than a detail. What a want can buy is <see cref="Escrow"/> divided by
    /// <see cref="Offer"/> — so a want for one scroll with exactly one scroll's money down, whose offer went up
    /// fifteen per cent on its own, could suddenly pay for nothing at all. It would have raised itself out of
    /// existence on the first beat and no supplier would ever have seen it. So the caller is told what the new
    /// offer would be, funds it out of the buyer's own purse, and only then lifts it.
    /// </para>
    /// </summary>
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

    /// <summary>Puts the offer up to a figure that has already been funded.</summary>
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

    /// <summary>Offers less, within the bound. Returns whether the offer actually moved.</summary>
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

    /// <summary>
    /// What a kind of thing looks like and is called, worked out once per type and remembered.
    ///
    /// <para>
    /// A want exists before any of the thing does, so unlike a stall it has nothing to copy the art and the
    /// name off. One throwaway instance answers both questions, and the answer is kept for the life of the
    /// process because a type's art does not change. The alternative — a dashboard row reading
    /// "GreaterHealScroll" with no picture — is the difference between a market you can read and a
    /// spreadsheet.
    /// </para>
    /// </summary>
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

        // The engine's own activator rather than the framework's. It fills optional constructor parameters
        // with Type.Missing, and nearly everything worth wanting is declared <c>Foo(int amount = 1)</c> — for
        // which plain reflection finds no parameterless constructor at all and every scroll in the game would
        // come out as a row with no picture.
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

    /// <summary>"GreaterHealScroll" as "Greater Heal Scroll". The same trick <see cref="BotListing"/> uses.</summary>
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
