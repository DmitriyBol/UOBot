using System;
using Server.Items;
using Server.Logging;

namespace Server.BotAI.V2;

/// <summary>
/// A crafter short of the raw material of its trade, putting the order to the population.
///
/// <para>
/// <b>Two materials, and they are the two that no counter on this island sells at any price.</b> A feather
/// exists because somebody killed a bird; a hide exists because somebody killed a beast. Everything else a
/// crafter eats — cloth, logs, blank scrolls, reagents — is on a shelf, so a bot short of those has an
/// errand rather than a problem. Metal is the third of this kind and has had its own since 24.08.2026; see
/// <see cref="BotBullion"/>, which this is deliberately a copy of. The smith's question is <em>which</em>
/// metal and needs a file of its own; these two only ask whether.
/// </para>
///
/// <para>
/// <b>Without this the arrow chain has no seed and cannot start at all.</b> The links were all built and
/// every one of them was waiting on the one before it: a fletcher will not work without feathers, a hunter
/// values a bird only when the board is asking for what it carries — <see cref="BotQuarry.Sought"/> — and
/// the board only ever asked for arrows when nobody sold one, which is never, because every bowyer in
/// Britain keeps a stack. So the chain was a ring: 84 fletchers passed over for want of feathers in eleven
/// minutes, 194 woodcutters told nobody was asking, and in seven hours of logs the word "feather" does not
/// appear once. A crafter that can say "I need feathers" out loud is the cut in that ring.
/// </para>
///
/// <para>
/// <b>On the beat and not in the auction, and that is the difference between working and not.</b> Written
/// first as a proposer, it made the offer 62 times in six minutes and was chosen none of them: an order is
/// half a minute of paperwork worth perhaps twenty gold a minute, and it stood against a tailor's own bench
/// at three hundred and forty. That is the auction being right — Calla should sew — and it is also the
/// reason the trade she is sewing for can never be supplied. The same lesson is written out in
/// <see cref="BotUpkeep"/> about collecting goods already paid for, and in <see cref="BotOrder"/> in as many
/// words: <em>an errand that costs nothing and takes no time cannot win an auction against work that pays,
/// and it never did.</em> Both live on the population's beat now beside <c>BotAuction.Fetch</c>, and so does
/// this.
/// </para>
///
/// <para>
/// <b>Logs were left out on purpose and the measurement took it back.</b> The reasoning was that a carpenter
/// sells them, so a fletcher short of wood has a shopping trip rather than a problem. It was wrong about this
/// island: once the feathers began arriving, 94 of 138 fletchers were passed over at 11:31 on 04.09.2026 with
/// "could not find wood" — <c>BotShops.Nearest(bot, typeof(Log))</c> answering null, because no carpenter
/// stands within any of their reach. So wood is the third thing nobody sells here, and it is asked for the
/// same way. It also opens the one trade on the shard that has never done a day's work: 323 woodcutters in
/// the same half hour, every one of them told that nobody was asking for wood or arrows.
/// </para>
///
/// <para>
/// Feathers first and wood second, for a fletcher that carries both gates. A fletcher with wood and no
/// feathers has nothing to make; a fletcher with feathers and no wood is one errand from working.
/// </para>
/// </summary>
public static class BotStores
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotStores));

    /// <summary>Coin a crafter keeps back rather than putting into materials. The same float BotBullion keeps.</summary>
    public static int Reserve { get; set; } = 150;

    /// <summary>
    /// How much of a material is enough to be working, below which more is worth ordering.
    ///
    /// <para>
    /// Twenty, which is what the trades already mean by a working quantity: a bolt of cloth is twenty, the
    /// smith's batch is twenty, and the leather in a chest piece is between eight and sixteen. For feathers
    /// it is also very close to a quiver — the fletcher makes arrows in tens.
    /// </para>
    /// </summary>
    public static int Enough { get; set; } = 20;

    /// <summary>How many are ordered at a time when the purse allows it.</summary>
    public static int Batch { get; set; } = 20;

    /// <summary>
    /// The fewest worth putting on the board at all.
    ///
    /// Five: enough that filling the order is a real errand rather than handing over a pocketful, and small
    /// enough that a crafter down to its last hundred gold is still in the market. Every multiplier on this
    /// shard needs a floor; without one an empty purse is a veto.
    /// </summary>
    public static int Least { get; set; } = 5;

    /// <summary>What a feather is offered at when nothing on the shard has ever priced one.</summary>
    public static int GuessFeather { get; set; } = 3;

    /// <summary>What a leather is offered at when nothing on the shard has ever priced one.</summary>
    public static int GuessLeather { get; set; } = 6;

    /// <summary>What a log is offered at when nothing on the shard has ever priced one. The carpenter's ask.</summary>
    public static int GuessLog { get; set; } = 3;

    /// <summary>What an empty bottle is offered at when nothing has priced one. The alchemist's own ask.</summary>
    public static int GuessGlass { get; set; } = 5;

    /// <summary>
    /// What a raw rib opens at. Three, which is what the market has actually been settling them at.
    ///
    /// <para>
    /// <b>Meat passes the rule glass failed.</b> Nothing may be ordered by the armful unless somebody's
    /// living is fetching it — that is the lesson the bottle cost this shard four-fifths of its trade for
    /// half an hour. Meat has the best producer on the island: every hunt ends in a carcass, hunting is the
    /// commonest thing anybody does, and carving is already folded into going through a corpse. What was
    /// missing was the ask. The cook could only ever cook what it happened to be holding, so 94% of every
    /// look at the skillet answered "no meat worth cooking" while the hunters who had it walked past.
    /// </para>
    ///
    /// <para>
    /// Ribs rather than the other four: 57 of the 67 raw meats this shard traded in a session were ribs, and
    /// an order for a kind nothing drops is an order that freezes the money behind it.
    /// </para>
    /// </summary>
    public static int GuessMeat { get; set; } = 3;

    /// <summary>What this is reckoned at per minute. Low: it is one beat of paperwork and then waiting.</summary>
    public static double Prior { get; set; } = 20.0;

    private static bool _said;

    /// <summary>
    /// Scratch for the materials one bot is asked about, reused rather than allocated.
    ///
    /// This runs on every bot's beat, and game logic is one thread — see the threading model — so a shared
    /// three-slot buffer is safe and costs nothing. A managed type cannot be stackalloc'd.
    /// </summary>
    private static readonly Type[] _wanted = new Type[5];

    /// <summary>Every gate apart, and the denominator with them. There is no bucket called "other".</summary>
    public static long Asked { get; private set; }

    public static long NoTrade { get; private set; }

    /// <summary>Crafters turned away for coming again inside the minute. Bots, not materials. See BotNeeds.</summary>
    public static long Soon { get; private set; }

    public static long Stocked { get; private set; }

    public static long Shelved { get; private set; }

    public static long Standing { get; private set; }

    public static long Poor { get; private set; }

    public static long Ordered { get; private set; }

    /// <summary>The fattest purse among those that could not afford one. See BotShopper.Richest for why.</summary>
    public static long Richest { get; private set; }

    /// <summary>
    /// Asked of every bot on its own beat. Nearly every answer is "no kit", which costs a null and a return.
    /// </summary>
    public static void Keep(IBotWilful bot)
    {
        var body = bot?.Self;
        var map = body?.Map;

        if (map == null || map == Map.Internal || !body.Alive)
        {
            return;
        }

        // Asked of the tool in the pack rather than of the class name, exactly as the smith's is: a bot that
        // picks up a fletching kit tomorrow is a fletcher tomorrow, and this file does not have to hear of it.
        var count = Materials(body, _wanted);

        if (count == 0)
        {
            NoTrade++;

            return;
        }

        // Once a minute per bot. See BotNeeds: this runs on the bot's own beat, five times a second, and
        // whether a fletcher is short of feathers cannot change in a fifth of a second.
        //
        // <b>Below the trade check and not above it, which is where it went first and where the number it
        // produced was nonsense.</b> Above, every bot on the shard is asked on every beat whether it may
        // reconsider — including the forty who have no crafting kit and no business here — so the bucket
        // counted beats rather than asks: 295,278 turned away against 96 asked, at 03:12 on 05.09.2026. A
        // throttle placed before the cheap test it is meant to protect also pays a dictionary lookup for
        // every bot it was never going to help. Here it shares a denominator with the gates below it, which
        // is the only position from which the line reconciles.
        if (!BotNeeds.Due(body, "stores"))
        {
            Soon++;

            return;
        }

        // <b>Every material the trade is short of, and not merely the first one.</b> This asked about one and
        // stopped, so a fletcher holding five feathers with an order already out for more never got as far as
        // the question about wood — and wood was what it was actually standing still for: 135 of 180
        // fletchers "could not find wood" at 12:01 on 04.09.2026 while 9381 answers here were "already have
        // one out". A bot waiting on one delivery is not a bot with nothing else to ask for.
        for (var i = 0; i < count; i++)
        {
            if (Ask(bot, body, _wanted[i]))
            {
                return;
            }
        }
    }

    /// <summary>One material. True when an order went on the board, false when this one had nothing to ask.</summary>
    private static bool Ask(IBotWilful bot, Mobile body, Type kind)
    {
        // <b>Counted here and not once per bot, so the buckets and the denominator are the same question.</b>
        // A fletcher is asked about feathers and about wood on one beat, so a per-bot denominator produced
        // "17401 asked … 31074 already have one out" on 04.09.2026 — a share of more than one, which is not
        // a share of anything. This counts material questions, and every figure below answers one.
        Asked++;

        if ((body.Backpack?.GetAmount(kind) ?? 0) >= Enough)
        {
            Stocked++;

            return false;
        }

        // Its own feathers are feathers. A bot cannot buy from its own stall and the market's refusal would
        // arrive only after the errand had been chosen — thirty-one failed orders for iron in an hour taught
        // the smith this on 26.08.2026, and it is the same market.
        if (BotAuction.Selling(bot, kind))
        {
            Shelved++;

            return false;
        }

        // <b>Asked here and not left to the market, because the market's answer to a want that already
        // stands is to top it up.</b> BotAuction.Ask calls Top on an open want, which charges the escrow
        // again — so a beat that asked every second would empty a crafter's account into one order for
        // feathers in about a minute. This gate is the whole of what makes it safe to run on a beat.
        if (BotAuction.Wanted(bot, kind) != null)
        {
            Standing++;

            return false;
        }

        var offer = BotAuction.Worth(kind, Guess(kind));

        // Everything the bot owns, not what happens to be in the pocket: the deposit is taken by
        // BotAuction.Charge, which spends the pack and then reaches into the account.
        var wealth = BotYield.Wealth(body);

        // <b>As many as the purse will carry, down to a floor, rather than a batch of twenty or nothing.</b>
        // A fixed batch against a fixed reserve is a multiplier with no floor, which is this shard's most
        // expensive shape: at 12:01 on 04.09.2026 it read "3478 cannot afford one (the fattest purse among
        // them held 132gp)" — a population of crafters locked out of its own materials by twenty logs at
        // three gold, when five would have kept every one of them working.
        var afford = offer <= 0 ? 0 : (wealth - Reserve) / offer;
        var units = Math.Min(Batch, afford);

        if (units < Least)
        {
            Poor++;

            if (wealth > Richest)
            {
                Richest = wealth;
            }

            return false;
        }

        // The market does the rest and says so in the log: it takes the escrow, refuses when the board is
        // full and hands the money straight back, and settles the case of a bot that turns out to be selling
        // the thing it wants. There is nothing here for a bot to stand still for.
        if (BotAuction.Ask(bot, kind, units, offer) == null)
        {
            // The board was full, or the market settled it another way. Counted with the ones already
            // waiting, because from the trade's side the state is the same: the material is asked for and
            // this bot is waiting. Inventing a bucket for it would be inventing a bucket called "other".
            Standing++;

            return false;
        }

        Ordered++;
        Once(body, kind);

        return true;
    }

    /// <summary>
    /// The one material this bot's trade eats and nobody sells, or null.
    ///
    /// <para>
    /// A bot may carry both kits; the fletcher is asked first because feathers are the scarcer half by a long
    /// way — hide comes off nearly everything the population kills and a feather off almost nothing.
    /// </para>
    /// </summary>
    private static int Materials(Mobile body, Type[] into)
    {
        var count = 0;

        if (BotFletching.Kit(body) != null)
        {
            // Feathers first: they are the half nobody sells and the binding one. Wood second, because a
            // fletcher with feathers and no wood is one delivery away from working.
            into[count++] = typeof(Feather);

            // Shafts count as wood: they are what a log becomes, and a fletcher holding twenty of them is
            // not short of anything. Asking otherwise would order wood on top of wood already cut.
            if (BotFletching.Logs(body) + BotFletching.Shafts(body) < Enough)
            {
                into[count++] = typeof(Log);
            }
        }

        if (BotThread.Kit(body) != null)
        {
            into[count++] = typeof(Leather);
        }

        // The cook, last, because it is the newest of these and the cheapest to go without. A funded want for
        // ribs is also the only way a hunter is ever paid for meat — and the board already steers a kill by
        // what the carcass carries, so the ask does two things at once.
        if (BotOven.Kit(body) != null && BotOven.Amount(body, typeof(RawRibs)) < BotOven.Worthwhile)
        {
            into[count++] = typeof(RawRibs);
        }

        // <b>Glass was added here on 04.09.2026 and taken out again half an hour later, and the reason is
        // worth keeping.</b> The argument was sound on its face — an alchemist sells a bottle for five gold
        // and most of the population cannot reach one, exactly as with wood — and the shard got measurably
        // worse: trade between bots fell from 3683gp a window to 490, sales from 38 to 12, fills from 63 to
        // 23, and the richest locked-out crafter from 158gp to 89.
        //
        // The difference is that <b>nobody makes bottles as a trade.</b> A feather, a log and a hide each
        // have somebody whose living it is to go and fetch one; glass only trickles back a bottle at a time
        // from whoever happens to drink a potion. So an order for twenty could not be filled — twelve raised,
        // one filled — and every one of them froze a hundred gold of a brewer's purse in escrow until the
        // market gave up on it an hour later. Money in escrow is money not spent, and three of this shard's
        // classes brew.
        //
        // The rule this leaves behind: <b>a material with no producer must not be ordered by the armful.</b>
        // The brewer's answer to glass is a counter and another bot's stall, not the board.

        return count;
    }

    /// <summary>What one of these opens at before the shard has ever traded one.</summary>
    private static int Guess(Type kind)
    {
        if (kind == typeof(Feather))
        {
            return GuessFeather;
        }

        if (kind == typeof(Log))
        {
            return GuessLog;
        }

        if (kind == typeof(RawRibs))
        {
            return GuessMeat;
        }

        return kind == typeof(Bottle) ? GuessGlass : GuessLeather;
    }

    private static void Once(Mobile body, Type kind)
    {
        if (_said)
        {
            return;
        }

        _said = true;

        logger.Information(
            "{Name} is the first crafter on this shard ever to ask the population for {Material}: until now the only way one could enter the world was a bot happening to kill the right animal",
            body.Name,
            kind.Name
        );
    }

    public static string Describe() =>
        Asked == 0
            ? $"no crafter has been asked about its materials ({NoTrade} answers went to bots with neither kit)"
            : $"{Asked} asked about a feather, a log, a hide or a rib: {Ordered} put the order to the population, {Standing} already have one out, "
              + $"{Stocked} have {Enough} already, {Shelved} have their own out on a stall, "
              + $"{Poor} cannot afford one (the fattest purse among them held {Richest}gp); {Soon} crafters came again inside the minute (Asked counts materials, this counts bots)";

    public static void Forget()
    {
        _said = false;
        Asked = 0;
        NoTrade = 0;
        Stocked = 0;
        Shelved = 0;
        Standing = 0;
        Poor = 0;
        Soon = 0;
        Ordered = 0;
        Richest = 0;
    }
}
