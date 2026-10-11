using System;
using System.Collections.Generic;
using Server.Items;
using Server.Logging;

namespace Server.BotAI.V2;

/// <summary>
/// Goods from one bot's pack into another's, and coin the other way, with the two standing together. The one place a trade
/// between two bots away from a shop happens.
///
/// <para>
/// <b>Patrick's order of 30.09.2026: every order and every purchase hand to hand.</b> The auction settled a fill between a
/// supplier in a mine and a buyer in a dungeon, the goods waiting in the market for the buyer's beat to fetch them. Here the
/// exchange is refused unless both are alive, on one map and within <see cref="MeetTiles"/> of each other — the distance two
/// people stand to pass a sack across — and the goods go from pack to pack and the coin from pack to pack.
/// </para>
///
/// <para>
/// <b>The auction's order is kept, because it is the whole of the money safety:</b> the buyer is charged first, all or nothing
/// (<see cref="BotMarket.Charge(Mobile, int, out int)"/>: coin out of the pack, then the account — a bot at work keeps a hundred on
/// it and the rest in the bank, so the balance behind the purse pays as a cheque would); then as much as the buyer's pack will
/// take is handed over; what did not move is refunded; and only then is the seller paid, as coin into its pack. Charged equals
/// paid plus refunded, always. How much went over as coin and how much was drawn on the account is counted apart.
/// </para>
///
/// <para>
/// <b>The buyer's purchase is booked outside the work it was doing.</b> A carrier walks up to a hunter in the middle of a hunt
/// and the hunter pays; the hunt must not learn from that coin that hunting loses money (<see cref="BotYield.Aside"/>, the
/// ledger's own rule for money moved by a decision outside the errand in hand).
/// </para>
/// </summary>
public static class BotHandover
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotHandover));

    public static int MeetTiles { get; set; } = 2;

    public static long Exchanges { get; private set; }

    public static long Units { get; private set; }

    public static long Gold { get; private set; }

    public static long FromPack { get; private set; }

    public static long ByCheque { get; private set; }

    public static long Apart { get; private set; }

    public static long Broke { get; private set; }

    public static long CutToFree { get; private set; }

    public static long CutUnits { get; private set; }

    public static long OnAdvance { get; private set; }

    public static long Empty { get; private set; }

    public static long PackFull { get; private set; }

    public static long Absent { get; private set; }

    public static long KeptForBuyer { get; private set; }

    public static long WornAtOnce { get; private set; }

    public static bool Promised(IBotWilful bot, Type kind)
    {
        if (bot?.Resolve is not { } resolve || kind == null)
        {
            return false;
        }

        var carrier = resolve.Deed as IBotWantCarrier ?? resolve.Paused?.Deed as IBotWantCarrier;

        if (carrier?.Goods != kind)
        {
            return false;
        }

        KeptForBuyer++;

        return true;
    }

    private static readonly Dictionary<Type, (long Units, long Gold)> _kinds = [];

    private static readonly List<(Item Item, int Take)> _lots = [];

    public static int Exchange(
        IBotWilful seller, Mobile buyer, Type kind, int units, int price, bool aside, string why, out int paid, out string refused
    )
    {
        paid = 0;
        refused = null;

        var giver = seller?.Self;
        var from = giver?.Backpack;
        var into = buyer?.Backpack;

        if (giver is not { Deleted: false, Alive: true } || buyer is not { Deleted: false, Alive: true } || from == null
            || into == null || kind == null || units <= 0 || price <= 0)
        {
            Absent++;
            refused = "nobody to hand it to";

            return 0;
        }

        if (giver.Map != buyer.Map || !giver.InRange(buyer.Location, MeetTiles))
        {
            Apart++;
            refused = $"{buyer.Name} was not standing beside it";

            return 0;
        }

        Gather(seller, from, kind);

        var available = 0;

        for (var i = 0; i < _lots.Count; i++)
        {
            available += _lots[i].Take;
        }

        if (available <= 0)
        {
            Empty++;
            refused = $"it had no {kind.Name} left to hand over";

            return 0;
        }

        var credit = BotAdvance.Credit(seller, buyer, kind);
        var purse = (int)Math.Min(BotYield.Wealth(buyer), (long)BotYield.Spendable(buyer) + BotMarket.ReservedFor(buyer, kind)) + credit;

        var want = BotMarket.Of(buyer, kind);
        var payable = want != null ? BotMarket.Affords(want, price, seller) : purse / price;

        if (purse < price || payable <= 0)
        {
            Broke++;
            refused = purse < price || want == null || price <= want.Offer
                ? $"{buyer.Name} could not pay {price}gp for one {kind.Name} with {purse}gp"
                : $"{buyer.Name}'s free money would not carry {price - want.Offer}gp over its offer of {want.Offer}gp for one {kind.Name}";

            return 0;
        }

        var asked = Math.Min(units, available);

        if (want != null)
        {
            asked = Math.Min(asked, want.Amount);
        }

        units = Math.Min(Math.Min(asked, purse / price), payable);

        if (units < asked)
        {
            CutToFree++;
            CutUnits += asked - units;
        }

        var bill = units * price;
        var lent = Math.Min(credit, bill);

        if (!BotMarket.Charge(buyer, bill - lent, out var fromPack, kind))
        {
            Broke++;
            refused = $"{buyer.Name} could not put {bill - lent}gp down for {units} {kind.Name}";

            return 0;
        }

        var given = 0;

        for (var i = 0; i < _lots.Count && given < units; i++)
        {
            var (item, take) = _lots[i];

            if (item == null || item.Deleted)
            {
                continue;
            }

            var part = Math.Min(take, units - given);

            if (part < Math.Max(1, item.Amount) && (!item.Stackable || Mobile.LiftItemDupe(item, part) == null))
            {
                continue;
            }

            var moved = Math.Max(1, item.Amount);

            if (!into.TryDropItem(buyer, item, false))
            {
                PackFull++;

                BotRoom.Refused(buyer, item.TotalWeight + item.PileWeight);

                break;
            }

            given += moved;
        }

        if (given < units)
        {
            var settled = Math.Min(lent, Math.Max(0, given) * price);
            var back = bill - lent - (Math.Max(0, given) * price - settled);

            lent = settled;

            var toPack = Math.Min(back, fromPack);

            if (toPack > 0)
            {
                into.DropItem(new Gold(toPack));
                fromPack -= toPack;
            }

            BotMarket.Refund(buyer, back - toPack);
        }

        if (given <= 0)
        {
            refused = $"{buyer.Name}'s pack would not take {kind.Name}";

            return 0;
        }

        var takings = given * price;

        BotMarket.Pay(giver, takings - lent);

        if (lent > 0)
        {
            BotAdvance.Settle(seller, buyer, kind, lent, given);
            OnAdvance += lent;
        }

        BotAdvance.Handed(kind, given, lent > 0);

        paid = takings;

        Exchanges++;
        Units += given;
        Gold += takings;
        FromPack += Math.Min(fromPack, takings - lent);
        ByCheque += Math.Max(0, takings - lent - fromPack);

        var (u, g) = _kinds.GetValueOrDefault(kind);
        _kinds[kind] = (u + given, g + takings);

        if (aside)
        {
            BotYield.Aside(buyer, takings - lent);
        }

        BotMarket.Traded(kind, given, takings);

        BotRegard.Traded((buyer.Guild as Guilds.Guild)?.Name, (giver.Guild as Guilds.Guild)?.Name);

        BotBiography.Sold(giver, buyer, given, kind, takings);

        BotBurgh.Supplied(giver, kind, given);

        if (buyer is BotMobile wearer && (typeof(BaseArmor).IsAssignableFrom(kind) || typeof(BaseClothing).IsAssignableFrom(kind)
            || typeof(BaseWeapon).IsAssignableFrom(kind)) && wearer.Rearm() > 0)
        {
            WornAtOnce++;
        }

        logger.Information(
            "{Seller} handed {Units} {Item} to {Buyer} for {Paid}gp, {Price} each — {Why}",
            giver.Name,
            given,
            kind.Name,
            buyer.Name,
            takings,
            price,
            why ?? "hand to hand"
        );

        BotEvents.Post("market", giver, $"handed {given} {kind.Name} to {buyer.Name} for {takings}gp");

        var fill = new Dictionary<string, string>
        {
            ["buyer"] = buyer.Name,
            ["seller"] = giver.Name,
            ["amount"] = given.ToString(),
            ["item"] = BotShopkeep.Words(kind),
            ["price"] = takings.ToString()
        };

        BotVoice.Say(giver, BotVoice.HandOver, BotVoice.Phrase("market:hand", null, giver, fill) ?? $"Here you are, {buyer.Name}: {given} {fill["item"]}.", true);
        BotVoice.Say(buyer, BotVoice.HandOver, BotVoice.Phrase("market:paid", null, buyer, fill) ?? $"{takings} gold, as agreed. Thank you, {giver.Name}.", true);

        return given;
    }

    private static void Gather(IBotWilful seller, Container from, Type kind)
    {
        _lots.Clear();

        var bond = seller.Bond;
        var carried = from.Items;

        for (var i = 0; i < carried.Count; i++)
        {
            var item = carried[i];

            if (item?.GetType() == kind && BotShopkeep.Merchandise(item, bond))
            {
                _lots.Add((item, Math.Max(1, item.Amount)));
            }
        }
    }

    public static int Carried(IBotWilful bot, Type kind)
    {
        var pack = bot?.Self?.Backpack;

        if (pack == null || kind == null)
        {
            return 0;
        }

        var bond = bot.Bond;
        var total = 0;
        var carried = pack.Items;

        for (var i = 0; i < carried.Count; i++)
        {
            var item = carried[i];

            if (item?.GetType() == kind && BotShopkeep.Merchandise(item, bond))
            {
                total += Math.Max(1, item.Amount);
            }
        }

        return total;
    }

    private static string Commonest(int most)
    {
        if (_kinds.Count == 0)
        {
            return "nothing yet";
        }

        List<(Type Kind, long Units, long Gold)> ranked = [];

        foreach (var (kind, (units, gold)) in _kinds)
        {
            ranked.Add((kind, units, gold));
        }

        ranked.Sort(static (a, b) => b.Gold.CompareTo(a.Gold));

        List<string> said = [];

        for (var i = 0; i < ranked.Count && i < most; i++)
        {
            said.Add($"{ranked[i].Kind.Name} {ranked[i].Units} for {ranked[i].Gold}gp");
        }

        return string.Join(", ", said);
    }

    public static string Describe() =>
        $"{Exchanges} meetings moved {Units} things for {Gold}gp ({FromPack}gp paid over in coin, {ByCheque}gp drawn on the buyer's account); "
        + $"refused: {Apart} not standing together, {Broke} the buyer could not pay, {Empty} the seller had none left, {PackFull} the buyer's pack took less, {Absent} one of the two gone; "
        + $"{CutToFree} handed fewer than brought for what the buyer could pay — a price over the offer its free money would not carry, or a purse short of its own reservation ({CutUnits} units kept back, the rest of the want still held); "
        + $"{OnAdvance}gp of it settled out of a brewer's advance for its glass and herbs rather than paid at the meeting; "
        + $"{KeptForBuyer} times a piece a carrier would have worn was left in its pack for the buyer (counted per look at the pack, not per piece) and {WornAtOnce} buyers put on what they were handed; "
        + $"most by value: {Commonest(6)}";

    public static void Forget()
    {
        Exchanges = 0;
        Units = 0;
        Gold = 0;
        FromPack = 0;
        ByCheque = 0;
        Apart = 0;
        Broke = 0;
        CutToFree = 0;
        CutUnits = 0;
        OnAdvance = 0;
        Empty = 0;
        PackFull = 0;
        Absent = 0;
        KeptForBuyer = 0;
        WornAtOnce = 0;
        _kinds.Clear();
    }
}
