using System;

namespace Server.BotAI.V2;

/// <summary>
/// Offers a bot with money down on the board a walk to a bot's shop that sells the thing for less than it is offering.
///
/// <para>
/// <b>The board's half of a shop's custom.</b> A want is a buyer that has already said what it needs and what it will pay —
/// a tailor's leather, a cook's ribs, a warrior's replacement cap — and until now it could only wait for a supplier to fill it
/// from wherever the supplier happened to be, or for a stall to cross it. A shop is somewhere the thing can be fetched: when an
/// open shop within the bot's reach holds the kind and its price, with the walk counted (<see cref="BotShopkeep.Best"/>), is at
/// or under the want's own offer, the bot is offered the walk, and at the till it takes its escrow back off the board and pays
/// the keeper's price instead (<see cref="BotShopkeep.Visit"/>). What the shop cannot supply goes back on the board at the same
/// offer.
/// </para>
///
/// <para>
/// <b>A purchase, not work</b> (see <c>BotRestock</c>): the errand is the shopper's own, unpaid and claimed at what a shopping
/// trip is claimed at, so it is taken when nothing that produces anything is on offer and never over work in hand.
/// </para>
/// </summary>
public sealed class BotPatron : IBotProposer
{
    public string Name => "Patron";

    public BotStanding Rung => BotStanding.Free;

    public static long Looks { get; private set; }

    public static long Wanting { get; private set; }

    public static long Dearer { get; private set; }

    public static long Offered { get; private set; }

    public BotDeed Propose(IBotWilful bot)
    {
        if (!BotShopkeep.Running || BotShopkeep.OpenCount == 0)
        {
            return null;
        }

        var body = bot?.Self;

        if (body is not { Alive: true } || body.Map == null || body.Map == Map.Internal)
        {
            return null;
        }

        Looks++;

        var wants = BotAuction.Wants;
        var mine = false;

        BotStorefront best = null;
        Type kind = null;
        var unit = 0;
        var units = 0;
        var saving = double.MinValue;

        for (var i = 0; i < wants.Count; i++)
        {
            var want = wants[i];

            if (want == null || !ReferenceEquals(want.Buyer, bot) || !want.IsOpen)
            {
                continue;
            }

            mine = true;

            var front = BotShopkeep.Best(bot, want.Kind, want.Payable, out var ask, out var n, out var landed);

            if (front == null)
            {
                continue;
            }

            if (landed > want.Offer)
            {
                Dearer++;

                continue;
            }

            var save = (want.Offer - landed) * n;

            if (best != null && save <= saving)
            {
                continue;
            }

            best = front;
            kind = want.Kind;
            unit = ask;
            units = n;
            saving = save;
        }

        if (mine)
        {
            Wanting++;
        }

        if (best == null)
        {
            return null;
        }

        Offered++;

        return new BotRestock(best, kind, units, unit, true);
    }

    public static string Describe() =>
        Looks == 0
            ? "nobody has looked at a shop for a want on the board"
            : $"{Looks} looks at the shops for wants on the board: {Wanting} bots had a want open, {Offered} walks to a shop offered, {Dearer} times a shop held the thing dearer than the want offers";

    public static void Forget()
    {
        Looks = 0;
        Wanting = 0;
        Dearer = 0;
        Offered = 0;
    }
}
