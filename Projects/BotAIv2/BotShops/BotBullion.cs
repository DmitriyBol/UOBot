using System;
using Server.Items;
using Server.Logging;

namespace Server.BotAI.V2;

/// <summary>
/// A crafter with money buys its metal instead of going and digging it.
///
/// <para>
/// <b>Digging is the cheapest way to get metal and very nearly the worst.</b> A mining trip is eight minutes
/// of walking to a seam, swinging at it, carrying rock to a fire and the metal to a counter — and at the end
/// of it a crafter has twenty ingots and has spent the eight minutes not crafting. Its skill is in the forge,
/// not in the rock. So once there is coin in the purse the sensible thing is to let somebody whose trade
/// <em>is</em> the rock do the walking, and pay them for it.
/// </para>
///
/// <para>
/// <b>It buys by asking rather than by shopping, and that is the whole elegance of it.</b> An order on the
/// board is read by every miner on the shard before it banks a single ingot — <c>BotDig</c> checks
/// <see cref="BotAuction.Demand"/> ahead of the bank box, and has since it was written. So a crafter putting
/// up money for metal does not merely acquire metal: it redirects the population's miners onto the thing the
/// population is short of, without anybody being told anything. That is the same trick as everything else
/// here — a fact left where others will pass it, and arithmetic on the far side.
/// </para>
///
/// <para>
/// Ordered on 24.08.2026: <em>if a crafter has a decent amount of money, say more than eight hundred to a
/// thousand, let it simply buy the ore rather than walk after it — buying and smelting is faster, and for a
/// crafter that is exactly right.</em>
/// </para>
/// </summary>
public sealed class BotBullion : IBotProposer
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotBullion));

    /// <summary>
    /// What a bot must still have left <em>after</em> paying for the metal: the blade it is wearing out, the
    /// bandages it heals with, the scrolls it fights with.
    ///
    /// <para>
    /// <b>This used to be an absolute bar of nine hundred, and it shut the forge for an entire night.</b> The
    /// reasoning was sound and the arithmetic was never done: a batch is twenty ingots offered at eight, so
    /// the order costs a hundred and sixty — and the bar stood at five and a half times the bill. Over eight
    /// hours the population banked a hundred and twenty-four times, averaging a hundred and thirty-nine gold,
    /// and broke a thousand exactly once. The summary read "0 ordered metal, 3412 cannot afford to buy it"
    /// window after window while the smith sat at the same thirty-three pieces it had forged before dawn.
    /// </para>
    ///
    /// <para>
    /// A reserve rather than a bar, so the two numbers on this shelf cannot drift apart again: whatever a
    /// batch happens to cost, what is asked is that cost plus enough to go on living.
    /// </para>
    ///
    /// <para>
    /// <b>A hundred and fifty, and the first attempt at this put it at two hundred and fifty by eye and
    /// landed ten gold above where crafters plateau.</b> A bot banks everything over its working float
    /// whenever the pack reaches BotUnload.Purse, so it cycles between a hundred and about four hundred all
    /// told — and a batch at a hundred and sixty plus a reserve of two hundred and fifty comes to four
    /// hundred and ten. Sixteen minutes of "0 ordered metal, 248 cannot afford to buy it" for want of ten
    /// coins. Set instead to what a bot's living actually costs: a bolt of cloth is forty, a pack of bandages
    /// a hundred, and what is left over buys the scrolls and reagents in between.
    /// </para>
    /// </summary>
    public static int Reserve { get; set; } = 150;

    /// <summary>How many ingots are enough to be worth working with. Below this it is worth ordering more.</summary>
    public static int Enough { get; set; } = 20;

    /// <summary>How many are ordered at a time when the purse allows it.</summary>
    public static int Batch { get; set; } = 20;

    /// <summary>The fewest worth putting on the board at all. See the note where it is used.</summary>
    public static int Least { get; set; } = 6;

    /// <summary>What an ingot is offered at when nothing on the shard has priced one.</summary>
    public static int Guess { get; set; } = 8;

    /// <summary>What this is reckoned at per minute. Low: it is one beat's work and then waiting.</summary>
    public static double Prior { get; set; } = 20.0;

    private static bool _said;

    /// <summary>Every gate apart, and the denominator with them.</summary>
    public static long Asked { get; private set; }

    public static long Poor { get; private set; }

    /// <summary>Asks turned away for coming again inside the minute. See BotNeeds.</summary>
    public static long Soon { get; private set; }

    public static long Stocked { get; private set; }

    public static long Standing { get; private set; }

    /// <summary>
    /// Smiths that are short of iron in the pack and have their own iron out on a stall.
    ///
    /// <para>
    /// <b>A bot cannot buy from itself, and this one kept trying.</b> The market refuses an order from a bot
    /// that is selling the same thing — "a bot with the thing on a stall is not short of it, whatever it
    /// thinks" — and the refusal was the market's, so it arrived only after the undertaking had been chosen
    /// and taken on. Thirty-one failed orders for iron in one hour on 26.08.2026, from three smiths, every
    /// one of them standing next to its own ingots. The tailor learned this about leather weeks ago and the
    /// note in <c>BotSew</c> says so in as many words; the smith's metal was never given the same reading.
    /// </para>
    /// </summary>
    public static long Shelved { get; private set; }

    public static long Ordered { get; private set; }

    public string Name => "Bullion";

    public BotStanding Rung => BotStanding.Free;

    public BotDeed Propose(IBotWilful bot)
    {
        var body = bot?.Self;
        var map = body?.Map;

        if (map == null || map == Map.Internal || !body.Alive)
        {
            return null;
        }

        // Only for bots whose trade eats metal. A warrior with a thousand gold has better uses for it, and a
        // gatherer buying ore would be buying its own job. Asked of the kit rather than of the class name, so
        // any class that picks up a hammer later is included without this file being edited.
        if (!Smiths(bot.Class))
        {
            return null;
        }

        Asked++;

        // Once a minute per bot, on the same clock the rest of the needs keep. See BotNeeds. Counted, or
        // the gates below stop adding up to Asked.
        if (!BotNeeds.Due(body, "metal"))
        {
            Soon++;

            return null;
        }

        // <b>What it is short of, not what the file says.</b> A smith with eighty Blacksmithy works bronze,
        // and ordering iron while forty bronze ingots sat in the pack was a bot buying what it did not need
        // with money it wanted for what it did. Best answers with the dearest metal the bot can both use and
        // fill a batch from; when it cannot fill any, that answer is iron and iron is what gets ordered.
        var kind = BotAnvil.Best(body, Batch);

        var carried = body.Backpack?.GetAmount(kind) ?? 0;

        if (carried >= Enough)
        {
            Stocked++;

            return null;
        }

        // Its own iron is iron. Asked with the market's own question rather than a second one of this file's
        // own — see BotAuction.Selling — and fetched back at the anvil, see BotForge.
        if (BotAuction.Selling(bot, kind))
        {
            Shelved++;

            return null;
        }

        if (BotAuction.Wanted(bot, kind) != null)
        {
            Standing++;

            return null;
        }

        var offer = BotAuction.Worth(kind, Guess);

        // <b>Measured against everything the bot owns, not against what happens to be in the pocket.</b> The
        // deposit this order puts down is taken by BotAuction.Charge, which spends the pack and then reaches
        // into the account — so asking the pack alone asks a question nobody is answering. It mattered from
        // the moment the purse stopped being emptied into the bank on every trip to a counter: a bot keeps a
        // hundred on it by design now, so a pack-only test could never clear any bar worth having.
        // <b>As many as the purse will carry, down to a floor, rather than a batch of twenty or nothing.</b>
        // A fixed batch against a fixed reserve is a multiplier with no floor, and a multiplier with no floor
        // is a veto: at 12:01 on 04.09.2026 this read "157 cannot afford to buy it" out of 288, while the
        // same smiths could have paid for six ingots and gone to work. The twin of this correction is in
        // BotStores, on the trades that eat feather, wood and hide.
        var wealth = BotYield.Wealth(body);
        var afford = offer <= 0 ? 0 : (wealth - Reserve) / offer;
        var units = Math.Min(Batch, afford);

        if (units < Least)
        {
            Poor++;

            return null;
        }

        Ordered++;
        Once(body);

        return BotOrder.For(map, body.Location, bot, kind, offer, units);
    }

    /// <summary>Whether this class carries a hammer, which is the only definition of a smith worth having.</summary>
    private static bool Smiths(BotClass klass)
    {
        var tools = klass?.Kit?.Tools;

        for (var i = 0; tools != null && i < tools.Count; i++)
        {
            if (tools[i] == typeof(SmithHammer))
            {
                return true;
            }
        }

        return false;
    }

    private static void Once(Mobile body)
    {
        if (_said)
        {
            return;
        }

        _said = true;

        logger.Information(
            "{Name} has money enough to buy its metal rather than dig it, and has put the order to the miners",
            body.Name
        );
    }

    public static string Describe() =>
        Asked == 0
            ? "no crafter has been asked about metal"
            : $"{Asked} asked: {Ordered} ordered metal, {Standing} already have an order out, {Stocked} have {Enough} ingots already, {Shelved} have their own out on a stall, {Poor} cannot afford to buy it, {Soon} asked again inside the minute";

    public static void Forget()
    {
        _said = false;
        Asked = 0;
        Poor = 0;
        Soon = 0;
        Stocked = 0;
        Shelved = 0;
        Standing = 0;
        Ordered = 0;
    }
}
