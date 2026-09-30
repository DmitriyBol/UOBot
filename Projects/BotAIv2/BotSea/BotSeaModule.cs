using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Server.Json;
using Server.Logging;

namespace Server.BotAI.V2;

/// <summary>The numbers of <c>Configuration/bot-sea.json</c>. A missing key keeps the code's value.</summary>
public sealed class BotSeaSettings
{
    public bool? Running { get; set; }

    public double? Share { get; set; }

    public int? Furthest { get; set; }

    public double? Prior { get; set; }

    public double? HomeWorth { get; set; }

    public bool? GuildPays { get; set; }

    public bool? Grant { get; set; }

    public bool? Cache { get; set; }

    public int? DockReach { get; set; }

    public Dictionary<string, int[]> Docks { get; set; }
}

/// <summary>
/// The sea as a module: charts the water, finds the docks, draws the lanes, offers the way home from an island, and keeps
/// the helm's clock. Patrick's point 7 of 29.09.2026. See <see cref="BotSeaChart"/>, <see cref="BotDocks"/>,
/// <see cref="BotSeaLanes"/>, <see cref="BotHelm"/>, <see cref="BotVoyage"/> and <see cref="BotSea"/>.
/// </summary>
public sealed class BotSeaModule : BotModule
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotSeaModule));

    private const string ConfigPath = "Configuration/bot-sea.json";

    public static int SliceMs { get; set; } = 100;

    private static Timer _survey;

    private static Timer _helm;

    private static bool _recovered;

    public override string Name => "Sea";

    public override BotPhase Phase => BotPhase.World;

    public override string[] Requires => ["Population", "Will", "Movement", "Shops", "Travel"];

    public override void Start()
    {
        Load();

        if (BotSea.Running)
        {
            BotWill.Offer(new BotSeafarer());
        }

        logger.Information(
            "Sea is {State}: a traveller on home's land is offered a town across the sea {Share:P0} of the time and one on an island always, no voyage longer than {Furthest} tiles; a small ship is bought ({Price}gp listed{Guild}){Grant}; the helm steers every {Stroke}ms, {Speed} tiles a stroke; the chart {Cache}",
            BotSea.Running ? "on" : "off",
            BotSea.Share,
            BotSea.Furthest,
            BotSea.ListPrice,
            BotSea.GuildPays ? ", the guild standing what a bot lacks" : "",
            BotSea.Grant ? " or HANDED OVER FOR NOTHING (Grant)" : "",
            BotHelm.StrokeMs,
            BotHelm.Speed,
            BotSeaChart.Cache ? $"kept in {BotSeaChart.CachePath}" : "read afresh every boot"
        );

        if (!BotSea.Running)
        {
            return;
        }

        _recovered = false;

        _survey?.Stop();
        _survey = new SurveyTimer(TimeSpan.FromMilliseconds(Math.Max(20, SliceMs)));
        _survey.Start();

        _helm?.Stop();
        _helm = new HelmTimer(TimeSpan.FromMilliseconds(Math.Max(100, BotHelm.StrokeMs)));
        _helm.Start();
    }

    private static void Load()
    {
        var path = Path.Combine(Core.BaseDirectory, ConfigPath);
        var settings = JsonConfig.Deserialize<BotSeaSettings>(path);

        if (settings == null)
        {
            JsonConfig.Serialize(path, new BotSeaSettings());
            logger.Information("Wrote a starter sea file to {Path}; every number stays as the code has it", ConfigPath);

            return;
        }

        BotSea.Running = settings.Running ?? BotSea.Running;
        BotSea.Share = settings.Share ?? BotSea.Share;
        BotSea.Furthest = settings.Furthest ?? BotSea.Furthest;
        BotSea.GuildPays = settings.GuildPays ?? BotSea.GuildPays;
        BotSea.Grant = settings.Grant ?? BotSea.Grant;
        BotVoyage.Prior = settings.Prior ?? BotVoyage.Prior;
        BotVoyage.HomeWorth = settings.HomeWorth ?? BotVoyage.HomeWorth;
        BotSeaChart.Cache = settings.Cache ?? BotSeaChart.Cache;
        BotDocks.Reach = settings.DockReach ?? BotDocks.Reach;

        if (settings.Docks != null)
        {
            foreach (var (town, at) in settings.Docks)
            {
                if (at is { Length: >= 2 })
                {
                    BotDocks.Pins[town] = new Point2D(at[0], at[1]);
                }
            }
        }
    }

    public override void Reset()
    {
        logger.Information("Sea, before the reload: {Sea}", BotSea.Describe());

        _survey?.Stop();
        _survey = null;
        _helm?.Stop();
        _helm = null;
        _recovered = false;

        BotSea.Forget();
    }

    /// <summary>The chart, then a town's dock a tick, then the courses; once they are drawn, the ships found at boot and the order by hand.</summary>
    private sealed class SurveyTimer : Timer
    {
        private int _faults;

        public SurveyTimer(TimeSpan interval) : base(interval, interval)
        {
        }

        protected override void OnTick()
        {
            var map = BotPopulation.Home;

            if (map == null || map == Map.Internal)
            {
                return;
            }

            try
            {
                if (!BotSeaChart.Step(map) || !BotDocks.Step(map))
                {
                    return;
                }

                BotSeaCourse.Step(Stopwatch.GetTimestamp() + (long)(BotSeaChart.SliceMs * Stopwatch.Frequency / 1000.0));

                if (!BotSeaLanes.Step(map))
                {
                    return;
                }

                if (!_recovered)
                {
                    _recovered = true;
                    BotHelm.Recover(map);
                }

                BotSea.Obey();
            }
            catch (Exception e)
            {
                logger.Error(e, "Sea: the survey threw ({Faults} so far); it is tried again next tick", ++_faults);

                if (_faults >= 10)
                {
                    logger.Error("Sea: the survey is stopped after {Faults} faults; no docks or lanes will be drawn until a restart", _faults);
                    Stop();
                }
            }
        }
    }

    private sealed class HelmTimer : Timer
    {
        public HelmTimer(TimeSpan interval) : base(interval, interval)
        {
        }

        protected override void OnTick() => BotHelm.Stroke();
    }
}
