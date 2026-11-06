using Server.Items;
using Server.Logging;

namespace Server.BotAI.V2;

public sealed class BotScribe : IBotProposer
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotScribe));

    private static bool _saidNoPaper;

    private static bool _saidNoSystem;

    public string Name => "Scribe";

    public BotStanding Rung => BotStanding.Free;

    public BotDeed Propose(IBotWilful bot)
    {
        var body = bot?.Self;
        var map = body?.Map;

        if (map == null || map == Map.Internal || BotQuill.Pen(body) == null)
        {
            return null;
        }

        BotScribes.Looked();

        if (BotGrimoire.Book(body) == null)
        {

            BotScribes.Bookless();

            return null;
        }

        if (BotQuill.System == null)
        {

            if (!_saidNoSystem)
            {
                _saidNoSystem = true;

                logger.Error("The inscription system does not exist yet, so nobody can write scrolls");
            }

            return null;
        }

        BotShops.Survey(map, body.Location);

        var shop = BotShops.Nearest(bot, typeof(BlankScroll));

        if (shop == null)
        {
            BotScribes.Paperless();

            if (!_saidNoPaper)
            {
                _saidNoPaper = true;

                logger.Error(
                    "No shopkeeper within reach of the bots on {Map} sells blank scrolls, so nobody will write",
                    map
                );
            }

            return null;
        }

        var price = BotShops.Price(shop, typeof(BlankScroll));

        if (price <= 0)
        {
            BotScribes.Paperless();

            return null;
        }

        var plan = BotQuill.Plan(bot, shop, price);

        if (plan == null)
        {
            BotScribes.Idle();

            return null;
        }

        BotScribes.Offered(plan, BotYield.Spendable(body) < plan.Outlay);

        return new BotInscribe(shop, price, plan, body);
    }

    public static void Forget()
    {
        _saidNoPaper = false;
        _saidNoSystem = false;
    }
}
