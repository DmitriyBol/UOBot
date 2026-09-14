using Server.Logging;

namespace Server.BotAI.V2;

/// <summary>
/// Offers a caster the next spell its book is short of, by whichever route exists for it.
///
/// <para>
/// <b>The book decides, and it decides in order.</b> Spell ids are laid out by circle, so the lowest gap is
/// the cheapest one — which is how anybody fills anything in, and needs no rule of its own. It works on that
/// one gap and no other: a caster saves for one spell at a time, and when it cannot get that one it goes back
/// to its trade rather than putting its whole purse down on nine claims at once.
/// </para>
///
/// <para>
/// <b>A gap that already has a want standing on it produces nothing rather than being asked for twice.</b>
/// That is the one thing this file has to get right. A want is a standing position with money behind it and it
/// raises its own offer on the market's beat; asking again would only top it up. The first version wrote six
/// hundred and eighty-eight identical board postings in six minutes for exactly this reason, and the fix is
/// not a cooldown — it is that the want is already there, doing its job.
/// </para>
///
/// <para>
/// It is offered to <em>every</em> caster including scribes, and the arithmetic sorts out who does it: a mage
/// with a pen reckons writing at sixty a minute against this at twelve, so it writes — until it wants
/// something above its own Inscribe, and then it buys like everybody else. Nobody is assigned a role here.
/// </para>
/// </summary>
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

        var kind = BotGrimoire.ScrollFor(spell);
        var want = BotAuction.Wanted(bot, kind);

        if (want is { Waiting: > 0 } || BotQuill.Held(body, kind) > 0)
        {
            return BotAcquire.Delivery(kind, spell, map, body.Location);
        }

        BotShops.Survey(map, body.Location);

        var shop = BotShops.Nearest(bot, kind);
        var counter = shop == null ? 0 : BotShops.Price(shop, kind);
        var stall = BotAuction.Cheapest(kind, bot);

        if (stall != null && (counter <= 0 || stall.Price <= counter))
        {
            return BotAcquire.Stalled(kind, spell, stall, map, body.Location);
        }

        if (counter > 0)
        {
            return BotAcquire.Counter(kind, spell, shop, counter);
        }

        if (want != null)
        {
            return null;
        }

        var offer = BotAuction.Worth(kind, BotGrimoire.ShopPrice(BotGrimoire.Circle(spell)));

        return BotAcquire.Board(kind, spell, map, body.Location, offer);
    }

    public static void Forget() => _saidNoMap = false;
}
