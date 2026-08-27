using System;
using Server.Items;
using Server.Logging;

namespace Server.BotAI.V2;

/// <summary>
/// Asking the population, by name, for a replacement for something that is wearing out.
///
/// <para>
/// <b>The board of what the population needs was empty for days, and this is the half that was missing.</b>
/// Everything on this shard could already <em>supply</em> a want — a miner selling ingots, a tailor selling
/// cloth and a scribe selling scrolls all check the board before they check a shopkeeper — and exactly one
/// thing in the whole assembly ever <em>raised</em> one. So the board held at most a handful of scroll
/// requests from mages completing their books, and a smith could have read it every minute of every session
/// without ever finding work. A market needs somebody to want something.
/// </para>
///
/// <para>
/// <b>What a bot may ask for is settled by two refusals, and both are the order of 24.08.2026.</b> It may not
/// ask for something it could not use, and it may not ask for something it could not pay for. The first is
/// answered without a table of what each class may wear: a bot asks for <em>another one of what it is already
/// wearing</em>, so the question "could this bot use it" has already been answered by the fact that it is
/// holding one. The second is the market's own rule — <see cref="BotAuction.Ask"/> takes the money down when
/// the want is raised — with a reserve on top, because a bot that spends its last coin ordering a sword has
/// no coin left to be resurrected with.
/// </para>
/// </summary>
public sealed class BotUpkeep : IBotProposer
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotUpkeep));

    /// <summary>
    /// The share of an item's life left at which a replacement is worth ordering.
    ///
    /// <para>
    /// Ordered before it breaks, not after. A weapon that shatters mid-fight leaves a bot swinging its fists
    /// against something that is already hitting it, and the order it would then place takes minutes to be
    /// filled — so the whole point is to be holding the new one before the old one goes. A third of the life
    /// left is several fights' worth of warning.
    /// </para>
    /// </summary>
    public static double Worn { get; set; } = 0.34;

    /// <summary>Coin a bot keeps back rather than spending on gear. The same rule the scroll shopping uses.</summary>
    public static int Reserve { get; set; } = 200;

    /// <summary>What ordering a replacement is reckoned at per minute before the ledger corrects it.</summary>
    public static double Prior { get; set; } = 30.0;

    /// <summary>What a piece of gear is guessed to be worth when nothing on the shard has priced one.</summary>
    public static int Guess { get; set; } = 60;

    private static bool _said;

    /// <summary>Every gate, counted apart. There is no bucket called "other".</summary>
    public static long Asked { get; private set; }

    public static long Sound { get; private set; }

    public static long Broke { get; private set; }

    public static long Standing { get; private set; }

    public static long Raised { get; private set; }

    /// <summary>Bots that already had something on the way. See the note in <see cref="Propose"/>.</summary>
    public static long Waiting { get; private set; }

    public string Name => "Upkeep";

    public BotStanding Rung => BotStanding.Free;

    public BotDeed Propose(IBotWilful bot)
    {
        var body = bot?.Self;
        var map = body?.Map;

        if (map == null || map == Map.Internal || !body.Alive)
        {
            return null;
        }

        Asked++;

        // <b>Goods already made and paid for are not ordered again, and they are not fetched here either.</b>
        // Collecting used to be this proposer's first branch, on the sound reasoning that goods a bot has paid
        // for and left on the board are goods it is not wearing. It was in the wrong place: an errand that
        // costs nothing and takes no time cannot win an auction against work that pays, and it never did. It
        // is a reflex on the population's beat now — see BotAuction.Fetch — so by the time this is asked the
        // shelf is already empty. What remains here is the half that was always right: a bot with something
        // on the way does not order a second one.
        if (BotAuction.Owed(bot) > 0)
        {
            Waiting++;

            return null;
        }

        var tired = Failing(body);

        if (tired == null)
        {
            Sound++;

            return null;
        }

        var kind = tired.GetType();

        // Already on the board under this bot's name. The want raises its own offer on the market's beat and
        // gives up at the ceiling; asking again would only turn one order into nine.
        if (BotAuction.Wanted(bot, kind) != null)
        {
            Standing++;

            return null;
        }

        var offer = BotAuction.Worth(kind, Guess);

        if ((body.Backpack?.TotalGold ?? 0) - offer <= Reserve)
        {
            Broke++;

            return null;
        }

        Raised++;
        Once(body, tired);

        return new BotOrder(map, body.Location, bot, kind, offer);
    }

    /// <summary>
    /// The worn-out thing this bot is wearing, or null.
    ///
    /// <para>
    /// Worn rather than owned: only what is on the body counts, and that is the wearability rule doing its
    /// work. A pickaxe in the pack is a tool and has its own shopping; the blade in a bot's hand and the mail
    /// on its back are the things whose failure is a fight lost.
    /// </para>
    ///
    /// <para>
    /// The worst first, so a bot with a nearly-dead sword and slightly scuffed boots orders the sword.
    /// </para>
    /// </summary>
    private static Item Failing(Mobile body)
    {
        Item worst = null;
        var least = 1.0;

        foreach (var item in body.Items)
        {
            var (now, max) = Life(item);

            if (max <= 0)
            {
                continue;
            }

            var left = now / (double)max;

            if (left > Worn || left >= least)
            {
                continue;
            }

            least = left;
            worst = item;
        }

        return worst;
    }

    /// <summary>How much life a thing has, and how much it had new. Zeroes for anything that does not wear out.</summary>
    private static (int Now, int Max) Life(Item item) =>
        item switch
        {
            BaseWeapon weapon => (weapon.HitPoints, weapon.MaxHitPoints),
            BaseArmor armor => (armor.HitPoints, armor.MaxHitPoints),
            _ => (0, 0)
        };

    private static void Once(Mobile body, Item tired)
    {
        if (_said)
        {
            return;
        }

        _said = true;

        var (now, max) = Life(tired);

        logger.Information(
            "{Name} is the first to put an order on the board: its {Item} is down to {Now} of {Max} and it wants another",
            body.Name,
            tired.GetType().Name,
            now,
            max
        );
    }

    public static string Describe() =>
        Asked == 0
            ? "nobody has been asked about their gear"
            : $"{Asked} asked: {Raised} ordered a replacement, {Waiting} already had one on the way, {Standing} already have one on order, "
              + $"{Broke} could not afford one, {Sound} are carrying nothing worn out";

    public static void Forget()
    {
        _said = false;
        Asked = 0;
        Sound = 0;
        Broke = 0;
        Standing = 0;
        Raised = 0;
        Waiting = 0;
    }
}
