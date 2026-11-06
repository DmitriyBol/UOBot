using System;
using System.IO;
using Server.Json;
using Server.Logging;

namespace Server.BotAI.V2;

public sealed class BotAlarmSettings
{

    public int? TickMs { get; set; }

    public int? ErrorsAt { get; set; }

    public int? WorkMs { get; set; }

    public int? WorkLeast { get; set; }

    public double? WorkFloor { get; set; }

    public double? StandShare { get; set; }

    public int? StandLeast { get; set; }

    public int? MarketMs { get; set; }

    public int? DeathsAt { get; set; }

    public int? AliveMs { get; set; }

    public int? RestMs { get; set; }
}

public static class BotAlarmConfig
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotAlarmConfig));

    private const string ConfigPath = "Configuration/bot-alarm.json";

    public static void Load()
    {
        var path = Path.Combine(Core.BaseDirectory, ConfigPath);
        var settings = JsonConfig.Deserialize<BotAlarmSettings>(path);

        if (settings == null)
        {
            JsonConfig.Serialize(path, new BotAlarmSettings());

            logger.Information("Wrote a starter alarm file to {Path}; every number stays as the code has it", ConfigPath);

            return;
        }

        BotSigns.TickMs = settings.TickMs ?? BotSigns.TickMs;
        BotSigns.ErrorsAt = settings.ErrorsAt ?? BotSigns.ErrorsAt;
        BotSigns.WorkMs = settings.WorkMs ?? BotSigns.WorkMs;
        BotSigns.WorkLeast = settings.WorkLeast ?? BotSigns.WorkLeast;
        BotSigns.WorkFloor = settings.WorkFloor ?? BotSigns.WorkFloor;
        BotSigns.StandShare = settings.StandShare ?? BotSigns.StandShare;
        BotSigns.StandLeast = settings.StandLeast ?? BotSigns.StandLeast;
        BotSigns.MarketMs = settings.MarketMs ?? BotSigns.MarketMs;
        BotSigns.DeathsAt = settings.DeathsAt ?? BotSigns.DeathsAt;
        BotSigns.AliveMs = settings.AliveMs ?? BotSigns.AliveMs;

        BotAlarm.RestMs = settings.RestMs ?? BotAlarm.RestMs;
    }
}

public sealed class BotAlarmModule : BotModule
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotAlarmModule));

    private static Timer _timer;

    public override string Name => "Alarm";

    public override BotPhase Phase => BotPhase.World;

    public override string[] Requires => ["Population"];

    private sealed class SignsTimer : Timer
    {
        public SignsTimer(TimeSpan interval) : base(interval, interval)
        {
        }

        protected override void OnTick() => BotSigns.Look();
    }

    public override void Start()
    {
        BotAlarmConfig.Load();

        BotTail.Open();
        BotAlarm.Open();
        BotSigns.Start();

        BotBoot.Check();

        _timer?.Stop();
        _timer = new SignsTimer(TimeSpan.FromMilliseconds(BotSigns.TickMs));
        _timer.Start();

        logger.Information("The alarm: {Channel}. {Rules}", BotAlarm.Describe(), BotSigns.Describe());
    }

    public static string Summarise() => $"{BotAlarm.Describe()}; {BotTail.Describe()}; {BotBoot.Describe()}";

    public override void Reset()
    {
        logger.Information("The alarm, before the reload: {State}", Summarise());

        _timer?.Stop();
        _timer = null;

        BotAlarm.Forget();
        BotTail.Forget();
        BotSigns.Forget();
    }
}
