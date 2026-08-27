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

        // Already bought and paid for — either waiting in the market or already handed over on its beat and
        // sitting in the pack. Either way there is nothing to buy: it only has to go into the book, and that
        // comes before every route that costs money.
        if (want is { Waiting: > 0 } || BotQuill.Held(body, kind) > 0)
        {
            return BotAcquire.Delivery(kind, spell, map, body.Location);
        }

        // <b>One gap at a time, and asking the world about it exactly once.</b> The first draft of this walked
        // all sixty-four spells and asked, for each one, which shopkeeper sells it — and asking that means
        // walking every remembered shop and reading its whole stock list. Sixty-four times a beat, per bot.
        // That is the first version's cost model wearing a new hat, and the interface this implements says so
        // in as many words: it may be a real question of the world, it must not be an expensive one.
        BotShops.Survey(map, body.Location);

        var shop = BotShops.Nearest(bot, kind);
        var counter = shop == null ? 0 : BotShops.Price(shop, kind);
        var stall = BotAuction.Cheapest(kind, bot);

        // Whichever is cheaper, and a shopkeeper is the ceiling: no bot can charge more than the shelf for
        // something the shelf has. That is what keeps this market honest without a rule about it.
        //
        // <b>A tie goes to one of ours.</b> The two prices being equal does not make the two purchases equal:
        // coin paid to a bot stays in the population and comes round again, while coin paid across a counter
        // leaves the world, and the only place new coin enters is a monster's purse. Written the other way
        // round, the shelf won every tie and the scribes' stalls sat full.
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
            // Already asked and still unfilled. The want raises its own offer on the market's beat and gives
            // up at the ceiling; there is nothing for the bot to do about this spell in the meantime, and
            // asking again would only top the want up into an order for nine copies of one scroll.
            return null;
        }

        var offer = BotAuction.Worth(kind, BotGrimoire.ShopPrice(BotGrimoire.Circle(spell)));

        return BotAcquire.Board(kind, spell, map, body.Location, offer);
    }

    /// <summary>Lets the complaint be made again after a world reload.</summary>
    public static void Forget() => _saidNoMap = false;
}
