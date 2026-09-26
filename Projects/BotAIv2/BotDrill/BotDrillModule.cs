using System;
using System.IO;
using Server.Json;
using Server.Logging;

namespace Server.BotAI.V2;

/// <summary>
/// What <c>Configuration/bot-drill.json</c> may say. Everything optional; empty keeps the code's numbers.
///
/// <para>
/// <b>PascalCase, and it is not a style question.</b> The deserialiser matches these names as written, so a
/// key in lower case is not an error and not a warning — it is a value silently left at its default, and a
/// configuration file that appears to have been read is worse than one that fails to load.
/// </para>
/// </summary>
public sealed class BotDrillSettings
{
    public int[] Ground { get; set; }

    public int? Pace { get; set; }

    public int? Rank { get; set; }

    public int? Most { get; set; }

    public int? GatherMs { get; set; }

    public int? LessonMs { get; set; }

    public int? BeatMs { get; set; }

    public int? Voice { get; set; }

    public double? Rate { get; set; }

    public double? Distant { get; set; }

    public int? Fee { get; set; }

    public int? FeePerPoint { get; set; }

    public int? Range { get; set; }
}

/// <summary>
/// The captain's other office: a field, a fee, and an hour of being shouted at.
///
/// <para>
/// <b>Two proposers rather than one, and they are on two different bots.</b> Teaching cannot be a thing the
/// captain does <em>to</em> people — a bot standing in a square for a quarter of an hour has given up
/// everything else it could have been doing, and on this shard that has to be its own decision, weighed by
/// the same auction against the same alternatives. So the captain offers to hold a class and each student
/// offers itself a place, and either half can lose. A class nobody comes to is a real answer.
/// </para>
/// </summary>
public sealed class BotDrillModule : BotModule
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotDrillModule));

    private const string ConfigPath = "Configuration/bot-drill.json";

    public override string Name => "Drill";

    public override BotPhase Phase => BotPhase.World;

    public override string[] Requires => ["Classes", "Will", "Population"];

    public override void Start()
    {
        Load();

        BotHarness.Survey();

        BotWill.Offer(new BotDrill());
        BotWill.Offer(new BotStudent());

        BotWill.Offer(new BotArmourer());

        BotWill.Offer(new BotScoutmaster());

        logger.Information(
            "The drill field is at ({X}, {Y}, {Z}): up to {Most} in ranks of {Rank} at {Pace} tiles, the roll open {Gather}ms and the class {Lesson}ms, a beat every {Beat}ms worth {Rate:F2} points within {Voice} tiles and {Distant:P0} of that beyond; a lesson costs {Fee} + {Per} a point and a master teaches only as far as it has got itself; one field and one master at a time, a captain for those who swing and shoot and a sage for those who cast, whose lessons cost {Magic:F2} times as much",
            BotSchool.Ground.X,
            BotSchool.Ground.Y,
            BotSchool.Ground.Z,
            BotSchool.Most,
            BotSchool.Rank,
            BotSchool.Pace,
            BotSchool.GatherMs,
            BotSchool.LessonMs,
            BotSchool.BeatMs,
            BotSchool.Rate,
            BotSchool.Voice,
            BotSchool.Distant,
            BotSchool.Fee,
            BotSchool.FeePerPoint,
            BotSchool.MagicFee
        );

        _timer?.Stop();
        _timer = new CaptainTimer(TimeSpan.FromMilliseconds(SayEveryMs));
        _timer.Start();
    }

    private static void Load()
    {
        var path = Path.Combine(Core.BaseDirectory, ConfigPath);
        var settings = JsonConfig.Deserialize<BotDrillSettings>(path);

        if (settings == null)
        {
            JsonConfig.Serialize(path, new BotDrillSettings());

            logger.Information("Wrote a starter drill file to {Path}; every number stays as the code has it", ConfigPath);

            return;
        }

        if (settings.Ground is { Length: >= 3 })
        {
            BotSchool.Ground = new Point3D(settings.Ground[0], settings.Ground[1], settings.Ground[2]);
        }

        BotSchool.Pace = settings.Pace ?? BotSchool.Pace;
        BotSchool.Rank = settings.Rank ?? BotSchool.Rank;
        BotSchool.Most = settings.Most ?? BotSchool.Most;
        BotSchool.GatherMs = settings.GatherMs ?? BotSchool.GatherMs;
        BotSchool.LessonMs = settings.LessonMs ?? BotSchool.LessonMs;
        BotSchool.BeatMs = settings.BeatMs ?? BotSchool.BeatMs;
        BotSchool.Voice = settings.Voice ?? BotSchool.Voice;
        BotSchool.Rate = settings.Rate ?? BotSchool.Rate;
        BotSchool.Distant = settings.Distant ?? BotSchool.Distant;
        BotSchool.Fee = settings.Fee ?? BotSchool.Fee;
        BotSchool.FeePerPoint = settings.FeePerPoint ?? BotSchool.FeePerPoint;
        BotDrill.Range = settings.Range ?? BotDrill.Range;
    }

    public static int SayEveryMs { get; set; } = 300000;

    private static Timer _timer;

    private sealed class CaptainTimer : Timer
    {
        public CaptainTimer(TimeSpan interval) : base(interval, interval)
        {
        }

        protected override void OnTick() =>
            logger.Information("The captain: {What}", Summarise());
    }

    public override void Reset()
    {
        logger.Information("The drill field, before the reload: {State}", BotSchool.Describe());

        _timer?.Stop();
        _timer = null;

        BotSchool.Forget();
        BotDrill.Forget();
        BotStudent.Forget();
        BotArmourer.Forget();
        BotScoutmaster.Forget();
    }

    public static string Summarise() =>
        $"{BotPatrol.Describe()}; {BotScoutmaster.Describe()}; {BotDrill.Describe()}; {BotStudent.Describe()}; {BotArmourer.Describe()}";
}
