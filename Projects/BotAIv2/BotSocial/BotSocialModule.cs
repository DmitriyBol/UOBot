using System;
using System.IO;
using Server.Json;
using Server.Logging;

namespace Server.BotAI.V2;

/// <summary>The numbers of <c>Configuration/bot-social.json</c>. A missing key keeps the code's value; keys are PascalCase.</summary>
public sealed class BotSocialSettings
{
    public bool? Running { get; set; }

    public int? MostListeners { get; set; }

    public double? ReplySeconds { get; set; }

    public double? AnswerEveryMinutes { get; set; }

    public double? PairEveryMinutes { get; set; }

    public double? InviteSeconds { get; set; }

    public int? InviteReach { get; set; }

    public double? ThanksChance { get; set; }

    public bool? OffersMending { get; set; }

    public bool? OffersFires { get; set; }

    public bool? OffersHunts { get; set; }

    public bool? OffersCompany { get; set; }

    public double? FellowWorth { get; set; }

    public double? PoorNeed { get; set; }

    public double? LonelyFrom { get; set; }

    public double? GrieveMinutes { get; set; }

    public double? ProudMinutes { get; set; }

    public double? ProudPerMinute { get; set; }
}

/// <summary>
/// Bots answering each other's moods, as a module: reads its numbers, puts its words into the voice's bank, listens to the
/// voice for mood lines with an audience, offers the help a listener undertook through the auction (<see cref="BotFellow"/>),
/// counts how the offers end through the will's own hooks, and runs the layer's second. Patrick's point 4 of the night plan
/// of 01.10.2026. See <see cref="BotSocial"/> and <see cref="BotMoods"/>.
/// </summary>
public sealed class BotSocialModule : BotModule
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotSocialModule));

    private const string ConfigPath = "Configuration/bot-social.json";

    public static int TickMs { get; set; } = 1000;

    private static SocialTimer _timer;

    private static bool _hooked;

    public override string Name => "Social";

    public override BotPhase Phase => BotPhase.World;

    public override string[] Requires => ["Population", "Will", "Voice"];

    public override void Start()
    {
        Load();

        var words = BotSocialTalk.Phrases();

        if (BotSocial.Running)
        {
            BotWill.Offer(new BotFellow());

            if (!_hooked)
            {
                _hooked = true;
                BotVoice.MoodSaid += BotSocial.Heard;
                BotWill.Started += BotSocial.Started;
                BotWill.Ended += BotSocial.Ended;
            }
        }

        _timer?.Stop();
        _timer = new SocialTimer(TimeSpan.FromMilliseconds(Math.Max(100, TickMs)));
        _timer.Start();

        logger.Information(
            "Society is {State}: a mood line waits up to {Hold} min for somebody within {Earshot} tiles, and the nearest {Most} not of a hostile guild answer it {Reply}s later, each once in {Every} min and a pair once in {Pair} min; "
            + "offers (mending {Mend}, fires {Fire}, hunts {Hunt}, company {Company}) wait {Invite}s for the listener's auction; poor below {Poor}gp or need {Need:F2}, lonely from boredom {Lonely:F2}, grief for {Grieve} min, pride for {Proud} min above {ProudPay}/min; {Words} occasions added to the voice's bank",
            BotSocial.Running ? "on" : "off",
            BotVoice.MoodHoldMs / 60000,
            BotVoice.Earshot,
            BotSocial.MostListeners,
            BotSocial.ReplyMs / 1000,
            BotSocial.AnswerEveryMs / 60000,
            BotSocial.PairEveryMs / 60000,
            BotSocial.OffersMending,
            BotSocial.OffersFires,
            BotSocial.OffersHunts,
            BotSocial.OffersCompany,
            BotSocial.InviteMs / 1000,
            BotHunter.PoorBelow,
            BotMoods.PoorNeed,
            BotMoods.LonelyFrom,
            BotMoods.GrieveMs / 60000,
            BotMoods.ProudMs / 60000,
            BotMoods.ProudPerMinute,
            words
        );
    }

    private static void Load()
    {
        var path = Path.Combine(Core.BaseDirectory, ConfigPath);
        var settings = JsonConfig.Deserialize<BotSocialSettings>(path);

        if (settings == null)
        {
            JsonConfig.Serialize(path, new BotSocialSettings());
            logger.Information("Wrote a starter social file to {Path}; every number stays as the code has it", ConfigPath);

            return;
        }

        BotSocial.Running = settings.Running ?? BotSocial.Running;
        BotSocial.MostListeners = settings.MostListeners ?? BotSocial.MostListeners;
        BotSocial.ReplyMs = settings.ReplySeconds is { } reply ? (int)(reply * 1000) : BotSocial.ReplyMs;
        BotSocial.AnswerEveryMs = settings.AnswerEveryMinutes is { } every ? (int)(every * 60000) : BotSocial.AnswerEveryMs;
        BotSocial.PairEveryMs = settings.PairEveryMinutes is { } pair ? (int)(pair * 60000) : BotSocial.PairEveryMs;
        BotSocial.InviteMs = settings.InviteSeconds is { } invite ? (int)(invite * 1000) : BotSocial.InviteMs;
        BotSocial.InviteReach = settings.InviteReach ?? BotSocial.InviteReach;
        BotSocial.ThanksChance = settings.ThanksChance ?? BotSocial.ThanksChance;
        BotSocial.OffersMending = settings.OffersMending ?? BotSocial.OffersMending;
        BotSocial.OffersFires = settings.OffersFires ?? BotSocial.OffersFires;
        BotSocial.OffersHunts = settings.OffersHunts ?? BotSocial.OffersHunts;
        BotSocial.OffersCompany = settings.OffersCompany ?? BotSocial.OffersCompany;
        BotSocial.FellowWorth = settings.FellowWorth ?? BotSocial.FellowWorth;
        BotMoods.PoorNeed = settings.PoorNeed ?? BotMoods.PoorNeed;
        BotMoods.LonelyFrom = settings.LonelyFrom ?? BotMoods.LonelyFrom;
        BotMoods.GrieveMs = settings.GrieveMinutes is { } grieve ? (int)(grieve * 60000) : BotMoods.GrieveMs;
        BotMoods.ProudMs = settings.ProudMinutes is { } proud ? (int)(proud * 60000) : BotMoods.ProudMs;
        BotMoods.ProudPerMinute = settings.ProudPerMinute ?? BotMoods.ProudPerMinute;
    }

    public override void Reset()
    {
        logger.Information("Society, before the reload: {What}", BotSocial.Describe());

        _timer?.Stop();
        _timer = null;

        BotSocial.Forget();
        BotMoods.Forget();
    }

    private sealed class SocialTimer : Timer
    {
        public SocialTimer(TimeSpan interval) : base(interval, interval)
        {
        }

        protected override void OnTick() => BotSocial.Tick(Core.TickCount);
    }
}
