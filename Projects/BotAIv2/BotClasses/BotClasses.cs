using System;
using System.Collections.Generic;
using Server.Logging;

namespace Server.BotAI.V2;

public static class BotClasses
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotClasses));

    private static readonly BotClass[] _all =
    [
        new BotWarrior(),
        new BotCaptain(),
        new BotBaron(),
        new BotArchitect(),
        new BotSage(),
        new BotWarriorMage(),
        new BotWarriorArcher(),
        new BotArcher(),
        new BotBrawler(),
        new BotMage(),
        new BotHealer(),
        new BotCrafter(),
        new BotGatherer(),
        new BotTamer(),
        new BotTrader()
    ];

    private static readonly Dictionary<string, BotClass> _byName =
        new(_all.Length, StringComparer.OrdinalIgnoreCase);

    static BotClasses()
    {
        for (var i = 0; i < _all.Length; i++)
        {

            _all[i].Reset();

            _byName[_all[i].Name] = _all[i];

            if (_all[i].Casts)
            {
                Casting++;
            }
        }
    }

    public static IReadOnlyList<BotClass> All => _all;

    public static int Casting { get; private set; }

    public static BotClass Find(string name) =>
        name != null && _byName.TryGetValue(name, out var found) ? found : null;

    public static int Count(BotRole role)
    {
        var count = 0;

        for (var i = 0; i < _all.Length; i++)
        {
            if (_all[i].Role == role)
            {
                count++;
            }
        }

        return count;
    }

    public static void Override(IReadOnlyDictionary<string, BotClassOverride> overrides)
    {
        if (overrides == null)
        {
            return;
        }

        for (var i = 0; i < _all.Length; i++)
        {
            _all[i].Reset();
        }

        var applied = 0;

        foreach (var (name, change) in overrides)
        {
            var found = Find(name);

            if (found == null)
            {
                logger.Warning("Configuration overrides an unknown bot class {Class}; ignored", name);
                continue;
            }

            change.ApplyTo(found);
            applied++;
        }

        if (applied > 0)
        {
            logger.Information("Configuration moved numbers on {Count} bot classes", applied);
        }
    }
}

public sealed class BotClassOverride
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotClassOverride));

    public int? Str { get; set; }

    public int? Dex { get; set; }

    public int? Int { get; set; }

    public Dictionary<string, double> SkillTargets { get; set; }

    public Dictionary<string, int> PotionLimits { get; set; }

    public bool? NeedsMeditation { get; set; }

    public bool? HandsAlwaysFree { get; set; }

    public int? IntrinsicManaTrickle { get; set; }

    public int? StaffManaTrickle { get; set; }

    public double? CritChancePerSkill { get; set; }

    public int? CritMultiplier { get; set; }

    public int? FreeCraftIntervalMs { get; set; }

    public int? ForageIntervalMs { get; set; }

    public int? ForageYieldMin { get; set; }

    public int? ForageYieldMax { get; set; }

    public int? BrewIntervalMs { get; set; }

    public int? Stipend { get; set; }

    internal void ApplyTo(BotClass target)
    {
        target.Str = Str ?? target.Str;
        target.Dex = Dex ?? target.Dex;
        target.Int = Int ?? target.Int;
        target.NeedsMeditation = NeedsMeditation ?? target.NeedsMeditation;
        target.HandsAlwaysFree = HandsAlwaysFree ?? target.HandsAlwaysFree;
        target.IntrinsicManaTrickle = IntrinsicManaTrickle ?? target.IntrinsicManaTrickle;
        target.StaffManaTrickle = StaffManaTrickle ?? target.StaffManaTrickle;
        target.CritChancePerSkill = CritChancePerSkill ?? target.CritChancePerSkill;
        target.CritMultiplier = CritMultiplier ?? target.CritMultiplier;
        target.FreeCraftIntervalMs = FreeCraftIntervalMs ?? target.FreeCraftIntervalMs;
        target.ForageIntervalMs = ForageIntervalMs ?? target.ForageIntervalMs;
        target.ForageYieldMin = ForageYieldMin ?? target.ForageYieldMin;
        target.ForageYieldMax = ForageYieldMax ?? target.ForageYieldMax;
        target.BrewIntervalMs = BrewIntervalMs ?? target.BrewIntervalMs;
        target.Stipend = Stipend ?? target.Stipend;

        if (SkillTargets is { Count: > 0 })
        {
            List<(SkillName Skill, double Target)> resolved = new(SkillTargets.Count);

            foreach (var (name, value) in SkillTargets)
            {
                if (Enum.TryParse<SkillName>(name, true, out var skill))
                {
                    resolved.Add((skill, value));
                    continue;
                }

                logger.Warning(
                    "Configuration names an unknown skill {Skill} for class {Class}; ignored",
                    name,
                    target.Name
                );
            }

            if (resolved.Count > 0)
            {
                target.Skills = resolved;
            }
        }

        if (PotionLimits is not { Count: > 0 })
        {
            return;
        }

        foreach (var (name, limit) in PotionLimits)
        {
            if (Enum.TryParse<BotPotionKind>(name, true, out var kind))
            {
                target.PotionLimits[kind] = limit;
                continue;
            }

            logger.Warning(
                "Configuration names an unknown potion kind {Kind} for class {Class}; ignored",
                name,
                target.Name
            );
        }
    }
}
