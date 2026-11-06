using Server.Logging;

namespace Server.BotAI.V2;

public sealed class BotSeeker : IBotProposer
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotSeeker));

    private static bool _saidNoMap;

    public string Name => "Seeker";

    public BotStanding Rung => BotStanding.Free;

    public BotDeed Propose(IBotWilful bot)
    {
        var body = bot?.Self;
        var map = body?.Map;

        if (map == null || map == Map.Internal || BotGrimoire.Book(body) == null)
        {
            return null;
        }

        if (BotGrimoire.Known == 0)
        {
            if (!_saidNoMap)
            {
                _saidNoMap = true;

                logger.Error("No scroll types were mapped to spells, so no book can ever be filled in");
            }

            return null;
        }

        var spell = BotGrimoire.Missing(body);

        if (spell < 0)
        {
            return null;
        }

        if (spell == BotRaise.Spell && BotRaise.KeepsScroll(body))
        {
            return null;
        }

        var kind = BotGrimoire.ScrollFor(spell);
        var want = BotMarket.Wanted(bot, kind);

        if (BotQuill.Held(body, kind) > 0)
        {
            return BotAcquire.Delivery(kind, spell, map, body.Location);
        }

        BotShops.Survey(map, body.Location);

        var shop = BotShops.Nearest(bot, kind);
        var counter = shop == null ? 0 : BotShops.Price(shop, kind);
        var front = BotShopkeep.Best(bot, kind, 1, out var ask, out _, out var landed);

        if (front != null && (counter <= 0 || landed <= counter))
        {
            return BotAcquire.AtShop(kind, spell, front, ask);
        }

        if (counter > 0)
        {
            return BotAcquire.Counter(kind, spell, shop, counter);
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

        return BotAcquire.Board(kind, spell, map, body.Location, offer);
    }

    public static void Forget() => _saidNoMap = false;
}
