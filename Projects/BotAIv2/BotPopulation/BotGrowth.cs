using System;
using System.Collections.Generic;
using Server.Logging;

namespace Server.BotAI.V2;

/// <summary>
/// Newcomers: a couple of novices raised every two hours, so the island has old hands and beginners at once.
///
/// <para>
/// <b>Patrick's order of the night of 24.09.2026: the limit is eighty now; add a couple of bots every two hours, so
/// the growth is steady and the game has both novices and veterans.</b> A newcomer is an ordinary bot of an ordinary
/// class, raised with a name nobody on the island has worn — neither a bot in the world, nor one resting, nor one whose
/// learning <see cref="BotProgress"/> still remembers — so it really does start as a novice; the guilds take it in the
/// way they take anybody without one.
/// </para>
///
/// <para>
/// <b>Written down, because configuration is the authority on who exists.</b> <see cref="BotPopulation.Reclaim"/>
/// deletes whatever the configured mix does not ask for, so a newcomer the mix did not know about would be deleted at
/// the next boot. What was added is kept by class in <see cref="BotGrowthStore"/> and laid over the configured mix at
/// every boot (<see cref="Mix"/>), with the time of the last arrival, so a restart neither loses the newcomers nor
/// restarts the two-hour clock.
/// </para>
///
/// <para>
/// Classes are drawn in proportion to the configured mix, leaving out the offices a population has exactly one of —
/// a second Baron is not a newcomer, it is a constitutional crisis.
/// </para>
/// </summary>
public static class BotGrowth
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotGrowth));

    public static bool Running { get; set; } = true;

    public static int EveryMinutes { get; set; } = 120;

    public static int By { get; set; } = 2;

    public static int Most { get; set; } = 120;

    private static readonly Dictionary<string, int> _added = new(StringComparer.OrdinalIgnoreCase);

    private static DateTime _last;

    private static bool _saidFull;

    public static long Raised { get; private set; }

    public static IReadOnlyDictionary<string, int> Mix(IReadOnlyDictionary<string, int> configured)
    {
        if (_added.Count == 0 || configured == null)
        {
            return configured;
        }

        var mix = new Dictionary<string, int>(configured);

        foreach (var (name, many) in _added)
        {
            var key = name;

            foreach (var (existing, _) in configured)
            {
                if (existing.InsensitiveEquals(name))
                {
                    key = existing;
                    break;
                }
            }

            mix[key] = mix.GetValueOrDefault(key) + many;
        }

        return mix;
    }

    internal static void Look(DateTime now)
    {
        if (!Running)
        {
            return;
        }

        if (_last == default)
        {
            _last = now;
            return;
        }

        if (now - _last < TimeSpan.FromMinutes(Math.Max(1, EveryMinutes)))
        {
            return;
        }

        _last = now;

        var have = BotPopulation.Count + BotPopulation.Away.Count;

        if (have >= Most)
        {
            if (!_saidFull)
            {
                _saidFull = true;
                logger.Information("No newcomers: {Have} bots is the ceiling of {Most}", have, Most);
            }

            return;
        }

        for (var i = 0; i < By && have < Most; i++)
        {
            var klass = Pick();

            if (klass == null)
            {
                return;
            }

            var bot = BotPopulation.RaiseNewcomer(klass);

            if (bot == null)
            {
                continue;
            }

            _added[klass.Name] = _added.GetValueOrDefault(klass.Name) + 1;
            Raised++;
            have++;

            logger.Information(
                "A newcomer: {Name} the {Class} arrives at {Where}, a novice; the island now has {Have} bots, {Away} of them resting",
                bot.Name,
                klass.Name,
                bot.Location,
                have,
                BotPopulation.Away.Count
            );
        }
    }

    private static BotClass Pick()
    {
        var mix = BotPopulationConfig.Mix;
        var total = 0;

        foreach (var (name, many) in mix)
        {
            if (many > 1 && BotClasses.Find(name) != null)
            {
                total += many;
            }
        }

        if (total <= 0)
        {
            return null;
        }

        var roll = Utility.Random(total);

        foreach (var (name, many) in mix)
        {
            if (many <= 1 || BotClasses.Find(name) is not { } klass)
            {
                continue;
            }

            if (roll < many)
            {
                return klass;
            }

            roll -= many;
        }

        return null;
    }

    public static void Forget()
    {
        _added.Clear();
        _last = default;
        _saidFull = false;
    }

    public static string Describe()
    {
        var added = 0;

        foreach (var (_, many) in _added)
        {
            added += many;
        }

        var next = _last == default ? "the clock starts at the first look" : $"the next about {(_last + TimeSpan.FromMinutes(EveryMinutes)).ToLocalTime():HH:mm}";

        return $"newcomers: {By} every {EveryMinutes} minutes up to {Most} bots; {added} added in all, {Raised} this session; {next}";
    }

    internal static void Save(IGenericWriter writer)
    {
        writer.Write(_last);
        writer.WriteEncodedInt(_added.Count);

        foreach (var (name, many) in _added)
        {
            writer.Write(name);
            writer.WriteEncodedInt(many);
        }
    }

    internal static int Load(IGenericReader reader)
    {
        _added.Clear();
        _last = reader.ReadDateTime();

        var count = reader.ReadEncodedInt();
        var added = 0;

        for (var i = 0; i < count; i++)
        {
            var name = reader.ReadString();
            var many = reader.ReadEncodedInt();

            if (!string.IsNullOrEmpty(name) && many > 0)
            {
                _added[name] = many;
                added += many;
            }
        }

        return added;
    }
}
