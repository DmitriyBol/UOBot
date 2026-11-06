using System;
using System.IO;
using Server.Json;
using Server.Logging;

namespace Server.BotAI.V2;

public sealed class BotResidenceSettings
{

    public bool? Running { get; set; }

    public double? HomeShare { get; set; }

    public double? SpreadWeight { get; set; }

    public double? GuildPull { get; set; }

    public int? LeastShops { get; set; }

    public bool? NeedsBank { get; set; }

    public bool? GateTowns { get; set; }

    public int? TownScatter { get; set; }

    public double? StretchHours { get; set; }

    public int? PoorGold { get; set; }

    public double? PoorProgress { get; set; }

    public int? DeathsToMove { get; set; }

    public double? DeathHours { get; set; }

    public double? MoveEveryHours { get; set; }

    public double? MoveOdds { get; set; }

    public double? PendingMinutes { get; set; }

    public double? Prior { get; set; }

    public string[] StayHome { get; set; }
}

public sealed class BotResidenceModule : BotModule
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotResidenceModule));

    private const string ConfigPath = "Configuration/bot-residence.json";

    public static int SliceMs { get; set; } = 5000;

    private static Timer _timer;

    public override string Name => "Residence";

    public override BotPhase Phase => BotPhase.World;

    public override string[] Requires => ["Population", "Travel"];

    public override void Start()
    {
        Load();

        BotTowns.Ensure(BotPopulation.Home);
        BotResidence.Start();

        if (BotResidence.Active)
        {
            BotWill.Offer(new BotMover());
        }

        logger.Information(
            "Residences are {State}: each bot lives in a town of its own — home keeps {Share:P0}, the spread weighs ×{Spread:F1}, a guild pulls ×{Pull:F1}; a town needs {Shops} shops{Bank} and {Way}; a living is judged over {Stretch:0.#}h (under {Gold}gp and {Progress:P0} of the trade is poor) and {Deaths} deaths in {DeathHours:0.#}h where it lives is too many; a move at most once in {Every:0.#}h, taken at {Odds:P0}, worth {Prior}/min; {Unhomed} newborns of the boot to move in once the towns are read",
            BotResidence.Active ? "on" : "off",
            BotSettle.HomeShare,
            BotSettle.SpreadWeight,
            BotSettle.GuildPull,
            BotSettle.LeastShops,
            BotSettle.NeedsBank ? " and a bank" : "",
            BotSettle.GateTowns ? "a road in the book or a gate" : "a road in the book",
            BotRelocate.StretchHours,
            BotRelocate.PoorGold,
            BotRelocate.PoorProgress,
            BotRelocate.DeathsToMove,
            BotRelocate.DeathHours,
            BotRelocate.MoveEveryHours,
            BotRelocate.MoveOdds,
            BotResettle.Prior,
            BotResidence.Unhomed.Count
        );

        _timer?.Stop();
        _timer = null;

        if (!BotResidence.Active)
        {
            return;
        }

        _timer = new ResidenceTimer(TimeSpan.FromMilliseconds(Math.Max(250, SliceMs)));
        _timer.Start();
    }

    private static void Load()
    {
        var path = Path.Combine(Core.BaseDirectory, ConfigPath);
        var settings = JsonConfig.Deserialize<BotResidenceSettings>(path);

        if (settings == null)
        {
            JsonConfig.Serialize(path, new BotResidenceSettings());
            logger.Information("Wrote a starter residence file to {Path}; every number stays as the code has it", ConfigPath);

            return;
        }

        BotResidence.Running = settings.Running ?? BotResidence.Running;
        BotResidence.TownScatter = settings.TownScatter ?? BotResidence.TownScatter;
        BotSettle.HomeShare = settings.HomeShare ?? BotSettle.HomeShare;
        BotSettle.SpreadWeight = settings.SpreadWeight ?? BotSettle.SpreadWeight;
        BotSettle.GuildPull = settings.GuildPull ?? BotSettle.GuildPull;
        BotSettle.LeastShops = settings.LeastShops ?? BotSettle.LeastShops;
        BotSettle.NeedsBank = settings.NeedsBank ?? BotSettle.NeedsBank;
        BotSettle.GateTowns = settings.GateTowns ?? BotSettle.GateTowns;
        BotRelocate.StretchHours = settings.StretchHours ?? BotRelocate.StretchHours;
        BotRelocate.PoorGold = settings.PoorGold ?? BotRelocate.PoorGold;
        BotRelocate.PoorProgress = settings.PoorProgress ?? BotRelocate.PoorProgress;
        BotRelocate.DeathsToMove = settings.DeathsToMove ?? BotRelocate.DeathsToMove;
        BotRelocate.DeathHours = settings.DeathHours ?? BotRelocate.DeathHours;
        BotRelocate.MoveEveryHours = settings.MoveEveryHours ?? BotRelocate.MoveEveryHours;
        BotRelocate.MoveOdds = settings.MoveOdds ?? BotRelocate.MoveOdds;
        BotRelocate.PendingMinutes = settings.PendingMinutes ?? BotRelocate.PendingMinutes;
        BotResettle.Prior = settings.Prior ?? BotResettle.Prior;
        BotRelocate.StayHome = settings.StayHome ?? BotRelocate.StayHome;
    }

    public override void Reset()
    {
        logger.Information("Residences, before the reload: {What}", BotResidence.Describe());

        _timer?.Stop();
        _timer = null;

        BotResidence.Forget();
        BotRelocate.Forget();
        BotMover.Forget();
        BotSettle.Forget();
    }

    private sealed class ResidenceTimer : Timer
    {
        public ResidenceTimer(TimeSpan interval) : base(interval, interval)
        {
        }

        protected override void OnTick() => BotRelocate.Tick();
    }
}
