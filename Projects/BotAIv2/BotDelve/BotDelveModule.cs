using System;
using System.IO;
using Server.Json;
using Server.Logging;

namespace Server.BotAI.V2;

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

    public double? WeakFloor { get; set; }

    public double? WeakShare { get; set; }

    public bool? LiftWhenBled { get; set; }

    public int? MaxStrays { get; set; }

    public int? HeadsPerParty { get; set; }

    public int? Margin { get; set; }
}

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
            BotWill.Offer(new BotVenturer());
        }

        logger.Information(
            "Delving is {State}: any member of a guild may take {Multiple} for each creature of a dungeon's worst room down it, {Company} at the fewest and {Most} at the most — {Fighters} of them able to hold a line, its own band called first, and more than the worst room holds or it does not go — for {Quota} corpses or {Minutes} minutes, after {Muster} minutes of calling at the home ground. It goes to the richest dungeon whose worst inhabitant the band is ×{Odds} the strength of, walks in by the cave mouth where the world's teleporters join it to the island and walks out by it (put down by hand only where none does), and has {Raisings} raisings between them; a party keeps {WeakShare} of its seats, the leader's among them and never a medic's, for bodies under {WeakFloor} of its dungeon's worst and levies no more of them, and one that loses two inside a minute is {Bled}; a fighter alone may venture into a dungeon whose worst it is ×{Alone} the strength of, once an hour; what it takes is swept into one pot and divided at the end, {Share} to the leader and the rest evenly. A band waits {Between} minutes between delves; a delve is reckoned at {Prior} a minute until the ledger has measured one",
            Enabled ? "on" : "off",
            BotProving.PartyMultiple,
            BotDelve.Company,
            Math.Max(BotDelve.Company, BotDelver.HeadsPerParty),
            BotDelve.Fighters,
            BotDelve.Quota,
            BotDelve.CapMs / 60000,
            BotDelve.MusterMs / 60000,
            BotDelver.Odds.ToString("F2"),
            BotDelveParty.Raisings,
            BotDelve.WeakShare.ToString("P0"),
            BotDelve.WeakFloor.ToString("P0"),
            BotDelve.LiftWhenBled ? "lifted out where it stands" : "walked out by the mouth",
            BotVenturer.Odds.ToString("F2"),
            BotDelveParty.LeadersShare.ToString("P0"),
            BotDelver.BetweenMs / 60000,
            BotDelve.Prior.ToString("F0")
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
        BotDelve.WeakFloor = settings.WeakFloor ?? BotDelve.WeakFloor;
        BotDelve.WeakShare = settings.WeakShare ?? BotDelve.WeakShare;
        BotDelve.LiftWhenBled = settings.LiftWhenBled ?? BotDelve.LiftWhenBled;
        BotDelve.MaxStrays = settings.MaxStrays ?? BotDelve.MaxStrays;
        BotDelver.HeadsPerParty = settings.HeadsPerParty ?? BotDelver.HeadsPerParty;
        BotDungeon.Margin = settings.Margin ?? BotDungeon.Margin;
        BotDelver.BetweenMs = settings.BetweenMs ?? BotDelver.BetweenMs;
        BotDelver.Odds = settings.Odds ?? BotDelver.Odds;
        BotDelveParty.Raisings = settings.Raisings ?? BotDelveParty.Raisings;
        BotDelveParty.RaiseMs = settings.RaiseMs ?? BotDelveParty.RaiseMs;
        BotDelveParty.LeadersShare = settings.LeadersShare ?? BotDelveParty.LeadersShare;
    }

    public static string Summarise() => $"{BotDelver.Describe()}; alone: {BotVenturer.Describe()}";

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
