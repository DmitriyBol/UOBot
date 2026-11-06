using System;
using System.IO;
using Server.Json;
using Server.Logging;

namespace Server.BotAI.V2;

public sealed class BotTravelSettings
{

    public bool? Running { get; set; }

    public double? EveryHours { get; set; }

    public int? Furthest { get; set; }

    public double? Prior { get; set; }

    public int? TownRoam { get; set; }

    public double? Curiosity { get; set; }
}

public sealed class BotTravelModule : BotModule
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotTravelModule));

    private const string ConfigPath = "Configuration/bot-travel.json";

    public static bool Enabled { get; set; } = true;

    public static int SliceMs { get; set; } = 250;

    private static Timer _timer;

    public override string Name => "Travel";

    public override BotPhase Phase => BotPhase.World;

    public override string[] Requires => ["Population", "Will", "Movement"];

    public override void Start()
    {
        Load();
        BotRoadbookStore.Configure();

        if (Enabled)
        {
            BotWill.Offer(new BotTraveller());
        }

        logger.Information(
            "Travel is {State}: a bot with nothing pressing walks to a reachable town it has not seen, once in {Every}h, the roads nobody has walked first (curiosity {Curiosity}), at {Prior}/min, no further than {Furthest} tiles; the population may work within {Roam} tiles of any reachable town; on arriving it tells its guild what the road read on the danger map",
            Enabled ? "on" : "off",
            BotTraveller.EveryMs / 3600000.0,
            BotTraveller.Curiosity,
            BotTravel.Prior,
            BotTraveller.Furthest,
            BotTowns.Roam
        );

        _timer?.Stop();
        _timer = new TravelTimer(TimeSpan.FromMilliseconds(Math.Max(50, SliceMs)));
        _timer.Start();
    }

    private static void Load()
    {
        var path = Path.Combine(Core.BaseDirectory, ConfigPath);
        var settings = JsonConfig.Deserialize<BotTravelSettings>(path);

        if (settings == null)
        {
            JsonConfig.Serialize(path, new BotTravelSettings());
            logger.Information("Wrote a starter travel file to {Path}; every number stays as the code has it", ConfigPath);

            return;
        }

        Enabled = settings.Running ?? Enabled;
        BotTraveller.EveryMs = settings.EveryHours is { } hours ? (int)(hours * 3600000) : BotTraveller.EveryMs;
        BotTraveller.Furthest = settings.Furthest ?? BotTraveller.Furthest;
        BotTraveller.Curiosity = settings.Curiosity ?? BotTraveller.Curiosity;
        BotTravel.Prior = settings.Prior ?? BotTravel.Prior;
        BotTowns.Roam = settings.TownRoam ?? BotTowns.Roam;
    }

    public override void Reset()
    {
        logger.Information("Travel, before the reload: {Towns}; {Roads}; {Travel}", BotTowns.Describe(), BotRoadbook.Describe(), BotTraveller.Describe());

        _timer?.Stop();
        _timer = null;

        BotTraveller.Forget();
        BotRoadbook.Forget();
        BotTowns.Forget();
    }

    private sealed class TravelTimer : Timer
    {
        private long _wroteTick;

        public TravelTimer(TimeSpan interval) : base(interval, interval)
        {
        }

        protected override void OnTick()
        {
            var map = BotPopulation.Home;

            if (map == null || map == Map.Internal)
            {
                return;
            }

            if (BotRoadbook.Slice(map))
            {
                return;
            }

            if (Core.TickCount - _wroteTick >= 60000)
            {
                _wroteTick = Core.TickCount;
                BotRoadbook.Refresh();
            }
        }
    }
}
