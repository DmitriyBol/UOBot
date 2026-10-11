using Server.Items;
using Server.Logging;

namespace Server.BotAI.V2;

/// <summary>
/// Offers the pen to anybody carrying one.
///
/// <para>
/// <b>The tool decides, as it does with the pickaxe and the needle.</b> A mage is born with a pen because
/// Inscribe is on its own list of skills — not because a table anywhere says mages write. Anybody who buys a
/// pen is a scribe while they hold it, and adding a class cannot silently leave it out.
/// </para>
///
/// <para>
/// The preconditions are here rather than in the work, and each of the three is a different way for the chain
/// to end with a bot standing somewhere pointlessly. No inscription system: nothing can be written at all. No
/// shop selling paper: the chain ends at a counter it cannot buy from. Nothing writable: the scribe is out of
/// herbs, and the honest consequence of that is a trip to the shops, which somebody else already proposes.
/// The ledger must not learn that <em>writing</em> is worthless for a reason that has nothing to do with
/// writing.
/// </para>
///
/// <para>
/// <b>Except for a scroll somebody needs (03.10.2026).</b> A session to order — somebody's want, or its own book's next gap —
/// buys the herbs its few attempts burn at the paper's counter as part of the session, so a want is not passed over for want
/// of the one herb the shopper buys last (<see cref="BotQuill.Plan"/>). Every answer this proposer gives is counted in the
/// <c>scrolls:</c> clause of the <c>Needs:</c> line (<see cref="BotScribes.Describe"/>).
/// </para>
/// </summary>
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
