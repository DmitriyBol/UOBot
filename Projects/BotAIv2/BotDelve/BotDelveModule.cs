using System;
using System.IO;
using Server.Json;
using Server.Logging;

namespace Server.BotAI.V2;

/// <summary>
/// What <c>Configuration/bot-delve.json</c> may say. Everything optional; empty keeps the code's numbers.
///
/// <para>
/// <b>PascalCase, and it is not a style question.</b> The deserialiser matches these names as written, so a
/// key in lower case is not an error and not a warning — it is a value silently left at its default.
/// </para>
/// </summary>
public sealed class BotDelveSettings
{
    public bool? Enabled { get; set; }

    public int? Company { get; set; }

    public int? Fighters { get; set; }

    public int? Quota { get; set; }

    public int? CapMs { get; set; }

    public int? MusterMs { get; set; }

    public int? Reach { get; set; }

    public int? RoomMs { get; set; }

    public int? Sight { get; set; }

    public int? BetweenMs { get; set; }

    public double? Odds { get; set; }

    public int? Raisings { get; set; }

    public int? RaiseMs { get; set; }

    public double? LeadersShare { get; set; }

    public double? Prior { get; set; }

    public int? MaxStrays { get; set; }

    public int? HeadsPerParty { get; set; }

    public int? Margin { get; set; }
}

/// <summary>
/// Dungeons as a module: one offer registered, the numbers read, the party watchdog started and one line
/// every few minutes saying what it has all come to.
///
/// <para>
/// <see cref="BotPhase.World"/>, because a dungeon is a place on the map and the rooms are read out of the
/// world's own spawners. It requires <c>Squads</c> for the plainest possible reason — the whole of this
/// subsystem is five bots standing together — and <c>Population</c>, because a delve is called by the
/// maker of a guild and there are no guilds until the population has been dealt out.
/// </para>
///
/// <para>
/// <b>The timer is not only for the log.</b> It also walks <see cref="BotDelveParty.Watch"/>, which is the
/// net under the one failure this feature can produce that nothing else on the shard could recover from: a
/// bot left in a place with no road out. It runs on its own clock rather than on the delve's, precisely so
/// that a broken delve cannot stop it.
/// </para>
/// </summary>
public sealed class BotDelveModule : BotModule
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotDelveModule));

    private const string ConfigPath = "Configuration/bot-delve.json";

    public override string Name => "Delve";

    public override BotPhase Phase => BotPhase.World;

    public override string[] Requires => ["Squads", "Population"];

    public static int SayEveryMs { get; set; } = 300000;

    public static int WatchMs { get; set; } = 10000;

    public static bool Enabled { get; set; } = true;

    private static Timer _timer;

    private static Timer _watch;

    public override void Start()
    {
        Load();

        if (Enabled)
        {
            BotWill.Offer(new BotDelver());
        }

        logger.Information(
            "Delving is {State}: any member of a guild may take {Company} down a dungeon — {Fighters} of them able to hold a line, its own band called first — for {Quota} corpses or {Minutes} minutes, after {Muster} minutes of calling at the home ground. It goes to the richest dungeon whose worst inhabitant the band is ×{Odds} the strength of, is carried down and carried back up because no road runs there, and has {Raisings} raisings between them; what it takes is swept into one pot and divided at the end, {Share} to the leader and the rest evenly. A band waits {Between} minutes between delves",
            Enabled ? "on" : "off",
            BotDelve.Company,
            BotDelve.Fighters,
            BotDelve.Quota,
            BotDelve.CapMs / 60000,
            BotDelve.MusterMs / 60000,
            BotDelver.Odds.ToString("F2"),
            BotDelveParty.Raisings,
            BotDelveParty.LeadersShare.ToString("P0"),
            BotDelver.BetweenMs / 60000
        );

        _timer?.Stop();
        _timer = new DelveTimer(TimeSpan.FromMilliseconds(SayEveryMs));
        _timer.Start();

        _watch?.Stop();
        _watch = new WatchTimer(TimeSpan.FromMilliseconds(WatchMs));
        _watch.Start();
    }

    private static void Load()
    {
        var path = Path.Combine(Core.BaseDirectory, ConfigPath);
        var settings = JsonConfig.Deserialize<BotDelveSettings>(path);

        if (settings == null)
        {
            JsonConfig.Serialize(path, new BotDelveSettings());

            logger.Information("Wrote a starter delve file to {Path}; every number stays as the code has it", ConfigPath);

            return;
        }

        Enabled = settings.Enabled ?? Enabled;
        BotDelve.Company = settings.Company ?? BotDelve.Company;
        BotDelve.Fighters = settings.Fighters ?? BotDelve.Fighters;
        BotDelve.Quota = settings.Quota ?? BotDelve.Quota;
        BotDelve.CapMs = settings.CapMs ?? BotDelve.CapMs;
        BotDelve.MusterMs = settings.MusterMs ?? BotDelve.MusterMs;
        BotDelve.Reach = settings.Reach ?? BotDelve.Reach;
        BotDelve.RoomMs = settings.RoomMs ?? BotDelve.RoomMs;
        BotDelve.Sight = settings.Sight ?? BotDelve.Sight;
        BotDelve.Prior = settings.Prior ?? BotDelve.Prior;
        BotDelve.MaxStrays = settings.MaxStrays ?? BotDelve.MaxStrays;
        BotDelver.HeadsPerParty = settings.HeadsPerParty ?? BotDelver.HeadsPerParty;
        BotDungeon.Margin = settings.Margin ?? BotDungeon.Margin;
        BotDelver.BetweenMs = settings.BetweenMs ?? BotDelver.BetweenMs;
        BotDelver.Odds = settings.Odds ?? BotDelver.Odds;
        BotDelveParty.Raisings = settings.Raisings ?? BotDelveParty.Raisings;
        BotDelveParty.RaiseMs = settings.RaiseMs ?? BotDelveParty.RaiseMs;
        BotDelveParty.LeadersShare = settings.LeadersShare ?? BotDelveParty.LeadersShare;
    }

    public static string Summarise() => BotDelver.Describe();

    private sealed class DelveTimer : Timer
    {
        public DelveTimer(TimeSpan interval) : base(interval, interval)
        {
        }

        protected override void OnTick() => logger.Information("Delving: {What}", Summarise());
    }

    private sealed class WatchTimer : Timer
    {
        public WatchTimer(TimeSpan interval) : base(interval, interval)
        {
        }

        protected override void OnTick()
        {
            BotDelveParty.Watch();

            BotDungeon.Restock();
        }
    }

    public override void Reset()
    {
        logger.Information("Delving, before the reload: {State}", Summarise());

        _timer?.Stop();
        _timer = null;

        _watch?.Stop();
        _watch = null;

        BotDelver.Forget();
    }
}
