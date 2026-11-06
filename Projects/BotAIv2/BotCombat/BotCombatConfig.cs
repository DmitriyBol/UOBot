using System.IO;
using Server.Json;
using Server.Logging;

namespace Server.BotAI.V2;

public sealed class BotCombatSettings
{

    public bool? NearCalls { get; set; }

    public int? NearReach { get; set; }

    public double? NearOdds { get; set; }

    public int? MostNear { get; set; }

    public bool? Stands { get; set; }

    public int? StandReach { get; set; }

    public bool? Summons { get; set; }

    public double? DireAt { get; set; }

    public int? MostCalled { get; set; }

    public int? HelperRestMs { get; set; }

    public int? Carries { get; set; }

    public int? CryHoldsMs { get; set; }

    public bool? ReflexOn { get; set; }

    public int? CrySampleEvery { get; set; }
}

public static class BotCombatConfig
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotCombatConfig));

    private const string ConfigPath = "Configuration/bot-combat.json";

    public static void Load()
    {
        var path = Path.Combine(Core.BaseDirectory, ConfigPath);
        var settings = JsonConfig.Deserialize<BotCombatSettings>(path);

        if (settings == null)
        {
            JsonConfig.Serialize(path, new BotCombatSettings());

            logger.Information(
                "Wrote a starter combat file to {Path}; every number stays as the code has it",
                ConfigPath
            );

            return;
        }

        BotCry.NearCalls = settings.NearCalls ?? BotCry.NearCalls;
        BotCry.NearReach = settings.NearReach ?? BotCry.NearReach;
        BotCry.NearOdds = settings.NearOdds ?? BotCry.NearOdds;
        BotCry.MostNear = settings.MostNear ?? BotCry.MostNear;

        BotDefender.Stands = settings.Stands ?? BotDefender.Stands;
        BotDefender.StandReach = settings.StandReach ?? BotDefender.StandReach;

        BotCry.Summons = settings.Summons ?? BotCry.Summons;
        BotCry.DireAt = settings.DireAt ?? BotCry.DireAt;
        BotCry.MostCalled = settings.MostCalled ?? BotCry.MostCalled;
        BotCry.HelperRestMs = settings.HelperRestMs ?? BotCry.HelperRestMs;
        BotCry.Carries = settings.Carries ?? BotCry.Carries;
        BotCry.HoldsMs = settings.CryHoldsMs ?? BotCry.HoldsMs;

        BotReflex.On = settings.ReflexOn ?? BotReflex.On;
        BotReflex.SampleEvery = settings.CrySampleEvery ?? BotReflex.SampleEvery;
    }
}
