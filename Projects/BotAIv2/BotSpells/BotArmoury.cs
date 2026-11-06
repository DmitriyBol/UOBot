using System;
using Server.Items;
using Server.Logging;

namespace Server.BotAI.V2;

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

        if (BotMarket.FromBank(bot, kind, Stock) > 0)
        {
            Vending++;

            return null;
        }

        var want = BotMarket.Wanted(bot, kind);

        BotShops.Survey(map, body.Location);

        var shop = BotShops.Nearest(bot, kind);
        var counter = shop == null ? 0 : BotShops.Price(shop, kind);
        var front = BotShopkeep.Best(bot, kind, 1, out var ask, out _, out var landed);

        Offered++;
        Once(body);

        if (front != null && (counter <= 0 || landed <= counter))
        {
            return BotAcquire.AtShop(kind, spell, front, ask, toCast: true);
        }

        if (counter > 0)
        {
            return BotAcquire.Counter(kind, spell, shop, counter, toCast: true);
        }

        if (want != null)
        {
            return null;
        }

        if (BotQuill.OwnPen(bot, kind))
        {
            BotScribes.LeftToPen(body, kind);

            return null;
        }

        var offer = BotMarket.Worth(kind, BotGrimoire.ShopPrice(BotGrimoire.Circle(spell)));

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
            : $"{Asked} asked: {Offered} sent shopping, {Stocked} already carrying {Stock}, {Broke} too poor to spare it, {Vending} took their own out of their bank box at a bank, {NoPool} with too small a pool";

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
