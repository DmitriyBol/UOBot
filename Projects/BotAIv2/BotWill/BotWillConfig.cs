using System.IO;
using Server.Json;
using Server.Logging;

namespace Server.BotAI.V2;

/// <summary>
/// What <c>Configuration/bot-will.json</c> is allowed to say. Everything optional; absent means the number
/// the code chose.
///
/// <para>
/// Its own file, like every other subsystem's. In the first version every knob on the shard lived in one
/// <c>bots.json</c>, so changing how readily a bot changes its mind was an edit to the file that also sets
/// the size of the population, and a typo in either half put the whole thing out.
/// </para>
///
/// <para>
/// <b>The one to look at first is <see cref="GoldPerSkillPoint"/>.</b> It is the exchange rate between the
/// two things this population is for, and every comparison between a smith's afternoon and a miner's goes
/// through it. Nothing else here changes behaviour as much.
/// </para>
/// </summary>
public sealed class BotWillSettings
{
    public double? GoldPerSkillPoint { get; set; }

    public double? DeathMinutes { get; set; }

    public double? StrayFactor { get; set; }

    public double? LeastMinutes { get; set; }

    public double? MostPerMinute { get; set; }

    public int? ReviewMs { get; set; }

    public int? IdleMs { get; set; }

    public int? DwellMs { get; set; }

    public int? DwellCapMs { get; set; }

    public int? AsideCapMs { get; set; }

    public double? SwitchMargin { get; set; }

    public double? Inertia { get; set; }

    public double? CommitStretch { get; set; }

    public int? CommitCapMs { get; set; }

    public double? TroubleShare { get; set; }

    public bool? Resume { get; set; }

    public double? ResumeHealth { get; set; }

    public int? ReturnMs { get; set; }

    public double? OwnTrade { get; set; }

    public double? OtherTrade { get; set; }

    public double? CrowdBite { get; set; }

    public double? LeastRoom { get; set; }

    public double? RepetitionBite { get; set; }

    public double? Suspicion { get; set; }

    public double? BoredomPerMinute { get; set; }

    public double? ReliefPerHundred { get; set; }

    public double? Restless { get; set; }

    public int? BandSize { get; set; }

    public int? MaxPlaces { get; set; }

    public double? PriorWeight { get; set; }

    public int? Confidence { get; set; }

    public double? Smoothing { get; set; }

    public int? SpinHalfLifeMs { get; set; }

    public int? CautionMs { get; set; }

    public double? FailingFraction { get; set; }

    public int? HuntedMs { get; set; }

    public int? CensusMs { get; set; }

    public bool? Chatty { get; set; }
}

/// <summary>Reads the decision file and moves the numbers it names.</summary>
public static class BotWillConfig
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotWillConfig));

    private const string ConfigPath = "Configuration/bot-will.json";

    public static void Load()
    {
        var path = Path.Combine(Core.BaseDirectory, ConfigPath);
        var settings = JsonConfig.Deserialize<BotWillSettings>(path);

        if (settings == null)
        {
            JsonConfig.Serialize(path, new BotWillSettings());

            logger.Information(
                "Wrote a starter decision file to {Path}; every number stays as the code has it",
                ConfigPath
            );

            return;
        }

        BotYield.GoldPerSkillPoint = settings.GoldPerSkillPoint ?? BotYield.GoldPerSkillPoint;
        BotYield.DeathMinutes = settings.DeathMinutes ?? BotYield.DeathMinutes;
        BotYield.StrayFactor = settings.StrayFactor ?? BotYield.StrayFactor;
        BotYield.LeastMinutes = settings.LeastMinutes ?? BotYield.LeastMinutes;
        BotYield.MostPerMinute = settings.MostPerMinute ?? BotYield.MostPerMinute;

        BotWill.ReviewMs = settings.ReviewMs ?? BotWill.ReviewMs;
        BotWill.IdleMs = settings.IdleMs ?? BotWill.IdleMs;
        BotWill.DwellMs = settings.DwellMs ?? BotWill.DwellMs;
        BotWill.DwellCapMs = settings.DwellCapMs ?? BotWill.DwellCapMs;
        BotWill.AsideCapMs = settings.AsideCapMs ?? BotWill.AsideCapMs;
        BotWill.SwitchMargin = settings.SwitchMargin ?? BotWill.SwitchMargin;
        BotWill.CommitStretch = settings.CommitStretch ?? BotWill.CommitStretch;
        BotWill.CommitCapMs = settings.CommitCapMs ?? BotWill.CommitCapMs;
        BotWill.TroubleShare = settings.TroubleShare ?? BotWill.TroubleShare;
        BotWill.Resume = settings.Resume ?? BotWill.Resume;
        BotWill.ResumeHealth = settings.ResumeHealth ?? BotWill.ResumeHealth;
        BotWill.ReturnMs = settings.ReturnMs ?? BotWill.ReturnMs;
        BotCalling.OwnTrade = settings.OwnTrade ?? BotCalling.OwnTrade;
        BotCalling.OtherTrade = settings.OtherTrade ?? BotCalling.OtherTrade;
        BotWill.CensusMs = settings.CensusMs ?? BotWill.CensusMs;
        BotWill.Chatty = settings.Chatty ?? BotWill.Chatty;

        BotAppraisal.Inertia = settings.Inertia ?? BotAppraisal.Inertia;
        BotAppraisal.CrowdBite = settings.CrowdBite ?? BotAppraisal.CrowdBite;
        BotAppraisal.LeastRoom = settings.LeastRoom ?? BotAppraisal.LeastRoom;
        BotAppraisal.RepetitionBite = settings.RepetitionBite ?? BotAppraisal.RepetitionBite;
        BotAppraisal.Suspicion = settings.Suspicion ?? BotAppraisal.Suspicion;

        BotUrges.BoredomPerMinute = settings.BoredomPerMinute ?? BotUrges.BoredomPerMinute;
        BotUrges.ReliefPerHundred = settings.ReliefPerHundred ?? BotUrges.ReliefPerHundred;
        BotUrges.Restless = settings.Restless ?? BotUrges.Restless;

        BotLedger.BandSize = settings.BandSize ?? BotLedger.BandSize;
        BotLedger.MaxPlaces = settings.MaxPlaces ?? BotLedger.MaxPlaces;
        BotLedger.PriorWeight = settings.PriorWeight ?? BotLedger.PriorWeight;
        BotLedger.Confidence = settings.Confidence ?? BotLedger.Confidence;
        BotLedger.Smoothing = settings.Smoothing ?? BotLedger.Smoothing;
        BotLedger.SpinHalfLifeMs = settings.SpinHalfLifeMs ?? BotLedger.SpinHalfLifeMs;
        BotLedger.CautionMs = settings.CautionMs ?? BotLedger.CautionMs;

        BotLadder.FailingFraction = settings.FailingFraction ?? BotLadder.FailingFraction;
        BotLadder.HuntedMs = settings.HuntedMs ?? BotLadder.HuntedMs;
    }
}
