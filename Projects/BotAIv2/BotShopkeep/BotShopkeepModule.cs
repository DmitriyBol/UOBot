using System;
using Server.Logging;

namespace Server.BotAI.V2;

public sealed class BotShopkeepModule : BotModule
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotShopkeepModule));

    public override string Name => "Shopkeep";

    public override BotPhase Phase => BotPhase.World;

    public override string[] Requires => ["Will", "Shops", "Market", "Population"];

    public static int SayEveryMs { get; set; } = 300000;

    private static Timer _timer;

    public override void Start()
    {
        BotShopkeepConfig.Load();

        BotWill.Offer(new BotShopkeeper());
        BotWill.Offer(new BotPatron());

        logger.Information(
            "Bots' shops are {State}, and they are the market: a bot with {Worth}gp or more of what the island is short of or somebody has asked for keeps shop by Britain's bank, or a guild's member by its guild's own town's, for {Shift} minutes, {Rest} minutes between shifts, at most {PerBank} to a bank and {Most} on the island; a customer looks {Seek} tiles for one, counts a minute's walk at {Walk:F0}gp, and buys within {Serve} tiles of the keeper; prices open under the nearest shopkeeper's ask or at the market's reckoning, whichever is less, and move by the market's own steps",
            BotShopkeep.Running ? "open" : "shut",
            BotShopkeep.OpenWorth,
            BotShopkeep.BankTiles,
            BotShopkeep.ShiftMinutes,
            BotShopkeep.RestMs / 60000,
            BotShopkeep.PerBank,
            BotShopkeep.MostOpen,
            BotShopkeep.SeekTiles,
            BotShopkeep.WalkGold,
            BotShopkeep.ServeReach
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

        protected override void OnTick() => logger.Information("Bot shops: {What}", BotShopkeep.Describe() + BotTrades.Clause());
    }
}
