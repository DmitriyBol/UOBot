using System;
using Server.Items;
using Server.Logging;

namespace Server.BotAI.V2;

/// <summary>
/// Puts an order on the board for the best piece of armour this bot is not wearing.
///
/// <para>
/// <b>Nobody on this shard wore armour, and nothing had ever noticed.</b> <c>BotOutfit</c> hands out a
/// shirt, a pair of trousers, boots and a weapon; there is no armour anywhere in the kit, so every bot on the
/// island fought skeletons in its shirtsleeves from the day the population was first raised. That was never
/// a fault in the outfitter — armour is meant to be made and bought, which is the whole point of having
/// smiths and tailors — but the demand side of it did not exist, so the smiths had nothing anybody wanted and
/// the bots had nothing on. Half the machinery was already written and idle: <c>Rearm</c> puts on any
/// <c>BaseArmor</c> it finds in a pack, <c>BotUpkeep</c> reads armour durability and reorders a worn piece,
/// and <c>BotClass.NeedsMeditation</c> was documented as "refuses armour that would stop it meditating" with
/// nothing in the world for it to refuse.
/// </para>
///
/// <para>
/// <b>What to want is asked rather than listed.</b> The first version of this file carried five hard-coded
/// ringmail types and gave a mage the same answer as a brawler. <see cref="BotHarness"/> reads the shard's
/// own craft systems instead, ranks by <em>harm stopped over a piece's life, per gold</em>, and lets each
/// bot spend in proportion to how often something has actually been hitting it. Those two together are what
/// send plate to the warrior in the graveyard and nothing at all to the miner.
/// </para>
///
/// <para>
/// <b>And it only ever wants what somebody can actually make.</b> A want nobody can fill is worse than an
/// empty board: it reads as demand, it holds escrow, and it teaches every seeker that orders are not worth
/// walking to. The best crafter alive is asked before an order is raised, so "nobody is good enough yet" is a
/// refusal with a name on it rather than a mystery on the market.
/// </para>
/// </summary>
public sealed class BotArmourer : IBotProposer
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotArmourer));

    public static int Reserve { get; set; } = 400;

    public static int Keeps(int price) => Math.Min(Reserve, Math.Max(1, price));

    public static int MostOrders { get; set; } = 2;

    public static double Prior { get; set; } = 30.0;

    private static bool _said;

    public string Name => "Armourer";

    public BotStanding Rung => BotStanding.Free;

    public static long Asked { get; private set; }

    public static long Covered { get; private set; }

    public static long Standing { get; private set; }

    public static long Unmakeable { get; private set; }

    public static long Unbloodied { get; private set; }

    public static long Broke { get; private set; }

    public static long Guilded { get; private set; }

    public static long Richest { get; private set; }

    public static long Offers { get; private set; }

    public BotDeed Propose(IBotWilful bot)
    {
        var body = bot?.Self;
        var map = body?.Map;

        if (map == null || map == Map.Internal || !body.Alive || body is not BotMobile wearer)
        {
            return null;
        }

        Asked++;

        if (Outstanding(bot) >= MostOrders)
        {
            Standing++;

            return null;
        }

        var purse = BotHarness.Purse(wearer);

        if (purse <= 0)
        {
            Unbloodied++;

            return null;
        }

        var (piece, bare, unmakeable) = Wanted(bot, wearer, purse);

        if (piece == null)
        {
            if (unmakeable)
            {
                Unmakeable++;
            }
            else if (bare)
            {
                Standing++;
            }
            else
            {
                Covered++;
            }

            return null;
        }

        var offer = BotAuction.Worth(piece.Kind, piece.Cost * 2);

        var wealth = BotYield.Wealth(body);

        if (wealth - offer <= Keeps(offer))
        {
            var owing = offer + Keeps(offer) - wealth;

            if (BotGuilds.Stand(wearer, owing))
            {
                Guilded++;
            }
            else
            {
                Broke++;

                if (wealth > Richest)
                {
                    Richest = wealth;
                }

                return null;
            }
        }

        Offers++;

        Once(body, piece, offer);

        return BotOrder.For(map, body.Location, bot, piece.Kind, offer);
    }

    private static (BotHarness.Piece Piece, bool Bare, bool Unmakeable) Wanted(IBotWilful bot, BotMobile wearer, int purse)
    {
        var layers = BotHarness.Layers;
        var bare = false;
        var unmakeable = false;

        for (var i = 0; i < layers.Count; i++)
        {
            var where = layers[i];

            if (wearer.FindItemOnLayer(where) is BaseArmor)
            {
                continue;
            }

            bare = true;

            var best = BotHarness.Best(wearer, where, BotHarness.Ablest, purse);

            if (best == null)
            {
                unmakeable |= BotHarness.Best(wearer, where, null, purse) != null;

                continue;
            }

            if (Carrying(wearer, best.Kind) || BotAuction.Wanted(bot, best.Kind) != null)
            {
                continue;
            }

            return (best, true, false);
        }

        return (null, bare, unmakeable);
    }

    private static int Outstanding(IBotWilful bot)
    {
        var wants = BotAuction.Wants;
        var mine = 0;

        for (var i = 0; i < wants.Count; i++)
        {
            if (ReferenceEquals(wants[i].Buyer, bot))
            {
                mine++;
            }
        }

        return mine;
    }

    private static bool Carrying(Mobile body, Type kind)
    {
        var pack = body.Backpack;

        if (pack == null)
        {
            return false;
        }

        var items = pack.Items;

        for (var i = 0; i < items.Count; i++)
        {
            if (kind.IsInstanceOfType(items[i]))
            {
                return true;
            }
        }

        return false;
    }

    private static void Once(Mobile body, BotHarness.Piece piece, int offer)
    {
        if (_said)
        {
            return;
        }

        _said = true;

        logger.Information(
            "{Name} is the first bot on this shard ever to want armour: a {Item} — {Rating:F0} of protection for {Cost}gp of material — offered at {Offer}gp",
            body.Name,
            piece.Kind.Name,
            piece.Rating,
            piece.Cost,
            offer
        );
    }

    public static string Describe() =>
        Asked == 0
            ? "nobody has ever looked at what it is wearing"
            : $"{Asked} looks at what a bot is wearing: {Offers} offered for (the auction takes what it takes), {Covered} were covered, {Standing} were already waiting on one, {Unmakeable} wanted something no crafter here is good enough to make, {Unbloodied} have not been hit lately enough to want any, {Broke} could not afford a piece and keep the price of it back, up to {Reserve}gp (the fattest purse among them held {Richest}gp); {BotHarness.Describe()}";

    public static void Forget()
    {
        _said = false;
        Asked = 0;
        Covered = 0;
        Standing = 0;
        Unmakeable = 0;
        Unbloodied = 0;
        Broke = 0;
        Richest = 0;
        Offers = 0;
    }
}
