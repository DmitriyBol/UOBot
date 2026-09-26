using System;
using System.Collections.Generic;
using Server.Logging;

namespace Server.BotAI.V2;

/// <summary>
/// Holds the modules, works out the order, starts them and says what happened.
///
/// <para>
/// Registration is a flat list and the order of that list means nothing — the sequence comes from what
/// each module says it needs. That is the whole difference from the first version, where the list
/// <em>was</em> the order, and where "why is this line here" was answered by a comment rather than by
/// anything a program could check.
/// </para>
///
/// <para>
/// <b>Failure is loud and contained.</b> A module that throws does not take the shard with it and does
/// not half-run: it is reported by name and anything that declared it as a requirement is refused with
/// the same clarity. The failure mode this exists to prevent is the one the first version had — a
/// subsystem that quietly read an empty list and then behaved plausibly for eight hours.
/// </para>
/// </summary>
public static class BotModules
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotModules));

    private static readonly List<BotModule> _all = [];

    private static readonly Dictionary<string, BotModule> _byName =
        new(StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<BotModule> All => _all;

    public static void Register(BotModule module)
    {
        if (module == null)
        {
            return;
        }

        if (_byName.TryGetValue(module.Name, out var clash))
        {
            logger.Error(
                "Two modules are both called {Name} ({First} and {Second}); the second is ignored",
                module.Name,
                clash.GetType().Name,
                module.GetType().Name
            );

            return;
        }

        module.Enabled = ServerConfiguration.GetOrUpdateSetting(
            $"bots.{module.Name.ToLowerInvariant()}.enabled",
            true
        );

        _all.Add(module);
        _byName[module.Name] = module;
    }

    public static int Start(BotPhase phase)
    {
        var due = Ordered(phase);
        var started = 0;

        for (var i = 0; i < due.Count; i++)
        {
            var module = due[i];

            if (module.Ready)
            {
                continue;
            }

            if (!module.Enabled)
            {
                logger.Information("Module {Name} is switched off", module.Name);
                continue;
            }

            var missing = Unmet(module);

            if (missing != null)
            {
                logger.Error(
                    "Module {Name} needs {Missing}, which is not ready; {Name} did not start",
                    module.Name,
                    missing
                );

                continue;
            }

            try
            {
                module.Start();
                module.Ready = true;
                started++;
            }
            catch (Exception e)
            {
                logger.Error(e, "Module {Name} threw while starting; it is not running", module.Name);
            }
        }

        logger.Information(
            "Bot modules, {Phase}: {Started} of {Due} started",
            phase,
            started,
            due.Count
        );

        return started;
    }

    public static void Rewind(BotPhase phase)
    {
        var rewound = 0;

        for (var i = 0; i < _all.Count; i++)
        {
            var module = _all[i];

            if (!module.Ready || module.Phase != phase)
            {
                continue;
            }

            try
            {
                module.Reset();
            }
            catch (Exception e)
            {
                logger.Error(e, "Module {Name} threw while resetting", module.Name);
            }

            module.Ready = false;
            rewound++;
        }

        if (rewound > 0)
        {
            logger.Information("Bot modules, {Phase}: {Count} rewound to start again", phase, rewound);
        }
    }

    private static string Unmet(BotModule module)
    {
        var requires = module.Requires;

        for (var i = 0; i < requires.Length; i++)
        {
            var name = requires[i];

            if (!_byName.TryGetValue(name, out var needed) || !needed.Ready)
            {
                return name;
            }
        }

        return null;
    }

    private static List<BotModule> Ordered(BotPhase phase)
    {
        List<BotModule> ordered = new(_all.Count);
        Dictionary<string, int> state = new(_all.Count, StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < _all.Count; i++)
        {
            if (_all[i].Phase == phase)
            {
                Visit(_all[i], phase, state, ordered);
            }
        }

        return ordered;
    }

    private const int Visiting = 1;

    private const int Placed = 2;

    private static void Visit(
        BotModule module,
        BotPhase phase,
        Dictionary<string, int> state,
        List<BotModule> ordered
    )
    {
        if (state.TryGetValue(module.Name, out var seen))
        {
            if (seen == Visiting)
            {
                logger.Error(
                    "Module {Name} is part of a circular dependency; the ring is broken here and the modules in it will not start",
                    module.Name
                );
            }

            return;
        }

        state[module.Name] = Visiting;

        var requires = module.Requires;

        for (var i = 0; i < requires.Length; i++)
        {
            if (_byName.TryGetValue(requires[i], out var needed) && needed.Phase == phase)
            {
                Visit(needed, phase, state, ordered);
            }
        }

        state[module.Name] = Placed;
        ordered.Add(module);
    }

    public static string Describe()
    {
        var running = 0;
        var off = 0;
        var broken = 0;

        for (var i = 0; i < _all.Count; i++)
        {
            var module = _all[i];

            if (module.Ready)
            {
                running++;
            }
            else if (!module.Enabled)
            {
                off++;
            }
            else
            {
                broken++;
            }
        }

        return $"{running} modules running, {off} switched off, {broken} that should be running and are not";
    }
}
