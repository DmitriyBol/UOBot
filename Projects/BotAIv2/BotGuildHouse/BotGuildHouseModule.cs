using System;
using System.IO;
using Server.Json;
using Server.Logging;

namespace Server.BotAI.V2;

/// <summary>The numbers of <c>Configuration/bot-guildhouse.json</c>. A missing key keeps the code's value.</summary>
public sealed class BotGuildHouseSettings
{
    public bool? Running { get; set; }

    public int? MostPerTown { get; set; }

    public int? MostInBritain { get; set; }

    public int? TownPrice { get; set; }

    public int? BritainPrice { get; set; }

    public bool? BritainAfterHall { get; set; }

    public bool? FitTownsOnly { get; set; }

    public bool? Rehouse { get; set; }

    public int? LookMs { get; set; }

    public int? GoneLooks { get; set; }

    public int? Spread { get; set; }

    public int? RecountMs { get; set; }

    public int? TickMs { get; set; }

    public int? MinArea { get; set; }

    public int? MaxArea { get; set; }

    public double? RoofShare { get; set; }

    public int? MostEntrances { get; set; }

    public double? SliceMs { get; set; }

    public bool? Cache { get; set; }
}

/// <summary>
/// Guild houses in the towns, as a module: reads its file, surveys the towns' empty buildings on its own clock, puts every
/// house of the save back, and looks the guilds over once a minute. Patrick's idea of 29.09.2026, evening. See
/// <see cref="BotGuildHouses"/>.
///
/// <para>
/// <b>After the estate, the travel and the residences</b>: a house is a seat (<c>BotSeat</c>, the estate's), it is chosen
/// among the towns the gates join to home (<c>BotTowns</c>, the travel's) and fit to live in (<c>BotSettle</c>, the
/// residences'), and it moves its members' residence. Switched off (<c>bots.guildhouse.enabled</c>, or <c>Running</c> in the
/// file), no guild has a house and every seat is what it was before.
/// </para>
/// </summary>
public sealed class BotGuildHouseModule : BotModule
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotGuildHouseModule));

    private const string ConfigPath = "Configuration/bot-guildhouse.json";

    public static int TickMs { get; set; } = 100;

    private static Timer _timer;

    public override string Name => "GuildHouse";

    public override BotPhase Phase => BotPhase.World;

    public override string[] Requires => ["Population", "Estate", "Travel", "Residence"];

    public override void Start()
    {
        Load();

        logger.Information(
            "Guild houses are {State}: a guild settles in an empty town building — a floor of {Least} to {Most} tiles, {Roof:P0} of it roofed, at most {Ways} ways in — {PerTown} to a town{Fit}, {TownPrice}; in Britain {InBritain} at most, for {Britain}gp paid to the city{After}; a door goes into every doorway and a sign on the wall, and the guild's seat, birth and rising move there; {Houses}",
            BotGuildHouses.Running ? "on" : "off",
            BotHouseSurvey.MinArea,
            BotHouseSurvey.MaxArea,
            BotHouseSurvey.RoofShare,
            BotHouseSurvey.MostEntrances,
            BotGuildHouses.MostPerTown,
            BotGuildHouses.FitTownsOnly ? " fit to live in" : "",
            BotGuildHouses.TownPrice > 0 ? $"for {BotGuildHouses.TownPrice}gp" : "for nothing",
            BotGuildHouses.MostInBritain,
            BotGuildHouses.BritainPrice,
            BotGuildHouses.BritainAfterHall ? " once its hall stands" : "",
            BotGuildHouses.Tell()
        );

        _timer?.Stop();
        _timer = null;

        if (!BotGuildHouses.Running)
        {
            return;
        }

        BotGuildHouses.Started = true;

        BotTowns.Ensure(BotPopulation.Home);

        _timer = new GuildHouseTimer(TimeSpan.FromMilliseconds(Math.Max(20, TickMs)));
        _timer.Start();
    }

    private static void Load()
    {
        var path = Path.Combine(Core.BaseDirectory, ConfigPath);
        var settings = JsonConfig.Deserialize<BotGuildHouseSettings>(path);

        if (settings == null)
        {
            JsonConfig.Serialize(path, new BotGuildHouseSettings());
            logger.Information("Wrote a starter guild house file to {Path}; every number stays as the code has it", ConfigPath);

            return;
        }

        BotGuildHouses.Running = settings.Running ?? BotGuildHouses.Running;
        BotGuildHouses.MostPerTown = settings.MostPerTown ?? BotGuildHouses.MostPerTown;
        BotGuildHouses.MostInBritain = settings.MostInBritain ?? BotGuildHouses.MostInBritain;
        BotGuildHouses.TownPrice = settings.TownPrice ?? BotGuildHouses.TownPrice;
        BotGuildHouses.BritainPrice = settings.BritainPrice ?? BotGuildHouses.BritainPrice;
        BotGuildHouses.BritainAfterHall = settings.BritainAfterHall ?? BotGuildHouses.BritainAfterHall;
        BotGuildHouses.FitTownsOnly = settings.FitTownsOnly ?? BotGuildHouses.FitTownsOnly;
        BotGuildHouses.Rehouse = settings.Rehouse ?? BotGuildHouses.Rehouse;
        BotGuildHouses.LookMs = settings.LookMs ?? BotGuildHouses.LookMs;
        BotGuildHouses.GoneLooks = settings.GoneLooks ?? BotGuildHouses.GoneLooks;
        BotGuildHouses.Spread = settings.Spread ?? BotGuildHouses.Spread;
        BotGuildHouses.RecountMs = settings.RecountMs ?? BotGuildHouses.RecountMs;
        TickMs = settings.TickMs ?? TickMs;
        BotHouseSurvey.MinArea = settings.MinArea ?? BotHouseSurvey.MinArea;
        BotHouseSurvey.MaxArea = settings.MaxArea ?? BotHouseSurvey.MaxArea;
        BotHouseSurvey.RoofShare = settings.RoofShare ?? BotHouseSurvey.RoofShare;
        BotHouseSurvey.MostEntrances = settings.MostEntrances ?? BotHouseSurvey.MostEntrances;
        BotHouseSurvey.SliceMs = settings.SliceMs ?? BotHouseSurvey.SliceMs;
        BotHouseSurvey.Cache = settings.Cache ?? BotHouseSurvey.Cache;
    }

    public override void Reset()
    {
        logger.Information("Guild houses, before the reload: {What}", BotGuildHouses.Describe());

        _timer?.Stop();
        _timer = null;

        BotGuildHouses.Forget();
        BotHouseSurvey.Forget();
    }

    private sealed class GuildHouseTimer : Timer
    {
        public GuildHouseTimer(TimeSpan interval) : base(interval, interval)
        {
        }

        protected override void OnTick() => BotGuildHouses.Tick(BotPopulation.Home);
    }
}
