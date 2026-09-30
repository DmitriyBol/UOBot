using System;
using Server.Logging;

namespace Server.BotAI.V2;

/// <summary>
/// Bots' own shops as a module: reads its numbers, cuts the auction to <c>AuctionLots</c> kinds a bot, offers the shift and the
/// customer's walk, and says every five minutes what the shops did.
///
/// <para>
/// <b>The cap on the auction belongs here and nowhere else.</b> <c>BotAuction.LotsPerBot</c> is nought — no cap — in the code,
/// and this module sets it when it starts. A shard with the shops switched off, by this file or by the module's own switch, has
/// the market it always had: cutting the lots without a shop to take what they turn away would only send the surplus to the
/// bank box for good.
/// </para>
/// </summary>
public sealed class BotShopkeepModule : BotModule
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotShopkeepModule));

    public override string Name => "Shopkeep";

    public override BotPhase Phase => BotPhase.World;

    public override string[] Requires => ["Will", "Shops", "Auction", "Population"];

    public static int SayEveryMs { get; set; } = 300000;

    private static Timer _timer;

    public override void Start()
    {
        BotShopkeepConfig.Load();

        BotAuction.LotsPerBot = BotShopkeep.Running ? Math.Max(0, BotShopkeep.AuctionLots) : 0;

        BotWill.Offer(new BotShopkeeper());
        BotWill.Offer(new BotPatron());

        logger.Information(
            "Bots' shops are {State}: a bot with {Worth}gp or more of what the island is short of or has money down for keeps shop beside the bank nearest home (within {Bank} tiles) for {Shift} minutes, {Rest} minutes between shifts, at most {PerBank} to a bank and {Most} on the island; a customer looks {Seek} tiles for one and counts a minute's walk at {Walk:F0}gp; prices open at the nearest shopkeeper's ask or the market's reckoning, whichever is less, and move by the auction's own steps; the auction takes at most {Lots} kinds from one bot",
            BotShopkeep.Running ? "open" : "shut",
            BotShopkeep.OpenWorth,
            BotShopkeep.BankTiles,
            BotShopkeep.ShiftMinutes,
            BotShopkeep.RestMs / 60000,
            BotShopkeep.PerBank,
            BotShopkeep.MostOpen,
            BotShopkeep.SeekTiles,
            BotShopkeep.WalkGold,
            BotAuction.LotsPerBot > 0 ? BotAuction.LotsPerBot.ToString() : "any number of"
        );

        _timer?.Stop();
        _timer = new ShopsTimer(TimeSpan.FromMilliseconds(Math.Max(10000, SayEveryMs)));
        _timer.Start();
    }

    public override void Reset()
    {
        logger.Information("Bot shops, before the reload: {State}", BotShopkeep.Describe());

        _timer?.Stop();
        _timer = null;

        BotShopkeep.Forget();
    }

    private sealed class ShopsTimer : Timer
    {
        public ShopsTimer(TimeSpan interval) : base(interval, interval)
        {
        }

        protected override void OnTick() => logger.Information("Bot shops: {What}", BotShopkeep.Describe());
    }
}
