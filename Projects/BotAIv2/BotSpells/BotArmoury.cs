using System;
using Server.Items;
using Server.Logging;

namespace Server.BotAI.V2;

/// <summary>
/// Offers any bot with a mana pool the chance to lay in a few attack scrolls.
///
/// <para>
/// <b>The market had a supplier and no customers, and this is why.</b> Scribes write scrolls all day and put
/// them on the stalls; the only bot that ever asked for one was a bot filling a spellbook, because
/// <see cref="BotSeeker"/> refuses outright to anybody without a book. So the whole of demand was "mages
/// completing their libraries" — a want each, once, for ever — the goods piled up unsold, and the dashboard's
/// board of what the population is short of stayed empty for days while the log cheerfully reported scrolls
/// being written. A market with one buyer per spell is not a market.
/// </para>
///
/// <para>
/// <b>And a scroll is not a mage's tool.</b> The engine settles that: <c>Spell.ConsumeReagents</c> waves the
/// herbs away the moment a scroll is attached, no book is consulted anywhere, and the scroll is spent on the
/// cast. A warrior can throw two arrows at something on the way in and then draw a blade; a crafter caught in
/// the open can throw the thing that is chasing it instead of dying with a hammer in its hands. That is what
/// these are for, and it makes every fight on the shard into recurring demand for somebody's work — which is
/// the one shape of trade this economy has been missing.
/// </para>
///
/// <para>
/// It is offered, priced and refused like any other work. A bot that would rather dig, digs.
/// </para>
/// </summary>
public sealed class BotArmoury : IBotProposer
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotArmoury));

    public static int Stock { get; set; } = 3;

    public static int Reserve { get; set; } = 150;

    public static int LeastPool { get; set; } = 12;

    public static double Prior { get; set; } = 14.0;

    private static bool _said;

    public static long Asked { get; private set; }

    public static long NoPool { get; private set; }

    public static long Stocked { get; private set; }

    public static long Broke { get; private set; }

    public static long Vending { get; private set; }

    public static long Offered { get; private set; }

    public string Name => "Armoury";

    public BotStanding Rung => BotStanding.Free;

    public static Type Kept(Mobile body, out int spell)
    {
        spell = -1;

        if (body == null || BotGrimoire.Known == 0 || body.ManaMax < LeastPool)
        {
            return null;
        }

        spell = BotStrike.Stock(body);

        return spell < 0 ? null : BotGrimoire.ScrollFor(spell);
    }

    public BotDeed Propose(IBotWilful bot)
    {
        var body = bot?.Self;
        var map = body?.Map;

        if (map == null || map == Map.Internal || !body.Alive || BotGrimoire.Known == 0)
        {
            return null;
        }

        Asked++;

        var kind = Kept(body, out var spell);

        if (kind == null)
        {
            if (spell < 0)
            {
                NoPool++;
            }

            return null;
        }

        var held = body.Backpack?.GetAmount(kind) ?? 0;

        if (held >= Stock)
        {
            Stocked++;

            return null;
        }

        if ((body.Backpack?.TotalGold ?? 0) <= Reserve)
        {
            Broke++;

            return null;
        }

        if (BotAuction.Selling(bot, kind))
        {
            Vending++;

            return null;
        }

        var want = BotAuction.Wanted(bot, kind);

        if (want is { Waiting: > 0 })
        {
            return BotAcquire.Delivery(kind, spell, map, body.Location, toCast: true);
        }

        BotShops.Survey(map, body.Location);

        var shop = BotShops.Nearest(bot, kind);
        var counter = shop == null ? 0 : BotShops.Price(shop, kind);
        var stall = BotAuction.Cheapest(kind, bot);

        Offered++;
        Once(body);

        if (stall != null && (counter <= 0 || stall.Price <= counter))
        {
            return BotAcquire.Stalled(kind, spell, stall, map, body.Location, toCast: true);
        }

        if (counter > 0)
        {
            return BotAcquire.Counter(kind, spell, shop, counter, toCast: true);
        }

        if (want != null)
        {
            return null;
        }

        var offer = BotAuction.Worth(kind, BotGrimoire.ShopPrice(BotGrimoire.Circle(spell)));

        return BotAcquire.Board(kind, spell, map, body.Location, offer, toCast: true);
    }

    private static void Once(Mobile body)
    {
        if (_said)
        {
            return;
        }

        _said = true;

        logger.Information(
            "{Name} is the first bot with no spellbook to go shopping for scrolls; anything with {Pool} mana may now fight with them",
            body.Name,
            LeastPool
        );
    }

    public static string Describe() =>
        Asked == 0
            ? "nobody has been offered scrolls yet"
            : $"{Asked} asked: {Offered} sent shopping, {Stocked} already carrying {Stock}, {Broke} too poor to spare it, {Vending} selling the scroll themselves, {NoPool} with too small a pool";

    public static void Forget()
    {
        _said = false;
        Asked = 0;
        NoPool = 0;
        Stocked = 0;
        Broke = 0;
        Offered = 0;
    }
}
