using System;
using System.Collections.Generic;
using Server.Logging;
using Server.Text;

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
/// Classes are drawn in proportion to the configured mix, leaving out the offices — a second Baron is not a newcomer, it
/// is a constitutional crisis. <b>An office is a fact of the class since 07.10.2026</b> (<see cref="BotClass.Office"/>: the
/// Captain, the Baron, the Architect, the Sage, one each at any population, Patrick's rule): the draw never takes one,
/// <see cref="Mix"/> never lays a newcomer over one, and a critical post or a guild's gap never asks for one that is held.
/// Build 425 had it as "a class the mix counts at 1", which a stale file undid and which no branch ever actually tested.
/// </para>
/// </summary>
public static class BotGrowth
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotGrowth));

    public static bool Running { get; set; } = true;

    public static int EveryMinutes { get; set; } = 120;

    public static int By { get; set; } = 2;

    public static int Most { get; set; } = 210;

    public static int FillTo { get; set; } = 200;

    public static int BurstBy { get; set; } = 5;

    private static readonly Dictionary<string, int> _added = new(StringComparer.OrdinalIgnoreCase);

    private static DateTime _last;

    private static bool _saidFull;

    private static bool _saidFilled;

    public static long Raised { get; private set; }

    public static long Burst { get; private set; }

    public static int LeastToWant { get; set; } = 3;

    public static long ByNeed { get; private set; }

    private static readonly BotRole[] _wanted = [BotRole.Producer, BotRole.Medic];

    public static IReadOnlyDictionary<string, int> Mix(IReadOnlyDictionary<string, int> configured)
    {
        if (configured == null)
        {
            return null;
        }

        var mix = new Dictionary<string, int>(configured);

        foreach (var (name, many) in configured)
        {
            if (many > 1 && BotClasses.Find(name) is { Office: true })
            {
                mix[name] = 1;
            }
        }

        foreach (var (name, many) in _added)
        {
            if (BotClasses.Find(name) is { Office: true })
            {
                continue;
            }

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
        }

        var have = BotPopulation.Count + BotPopulation.Away.Count;

        if (FillTo > 0 && have < Math.Min(FillTo, Most))
        {
            Fill(have);

            return;
        }

        if (now - _last < TimeSpan.FromMinutes(Math.Max(1, EveryMinutes)))
        {
            return;
        }

        _last = now;

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
            var klass = Pick(false);

            if (klass == null)
            {
                return;
            }

            var bot = BotPopulation.RaiseNewcomer(klass);

            if (bot == null)
            {
                continue;
            }

            Added(klass);
            Raised++;
            have++;
            BotCritical.Born(bot);

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

    private static void Fill(int have)
    {
        var goal = Math.Min(FillTo, Most);
        var raised = 0;

        using var who = ValueStringBuilder.Create(160);

        for (var i = 0; i < Math.Max(1, BurstBy) && have < goal; i++)
        {
            var klass = Pick(true);

            if (klass == null)
            {
                break;
            }

            var bot = BotPopulation.RaiseNewcomer(klass);

            if (bot == null)
            {
                continue;
            }

            Added(klass);
            Raised++;
            Burst++;
            have++;
            raised++;

            BotRest.Spread(bot);
            BotCritical.Born(bot);

            if (raised > 1)
            {
                who.Append(", ");
            }

            who.Append(bot.Name);
            who.Append(" the ");
            who.Append(klass.Name);
        }

        if (raised > 0)
        {
            logger.Information(
                "The burst: {Raised} newcomers ({Who}); the island now has {Have} bots towards {Goal}, {Away} of them resting",
                raised,
                who.ToString(),
                have,
                goal,
                BotPopulation.Away.Count
            );
        }

        if (have >= goal && !_saidFilled)
        {
            _saidFilled = true;
            logger.Information("The burst is done: {Have} bots on the island; the growth carries on at {By} every {Every} minutes up to {Most}", have, By, EveryMinutes, Most);
        }
    }

    private static void Added(BotClass klass)
    {
        if (!klass.Office)
        {
            _added[klass.Name] = _added.GetValueOrDefault(klass.Name) + 1;
        }
    }

    private static readonly HashSet<string> _saidHeld = new(StringComparer.OrdinalIgnoreCase);

    private static bool Held(BotClass klass)
    {
        if (klass is not { Office: true } || BotPopulation.Holder(klass) is not { } holder)
        {
            return false;
        }

        OfficesPassedOver++;

        if (_saidHeld.Add(klass.Name))
        {
            logger.Information("No second {Class}: {Holder} holds the office, so a newcomer is something else", klass.Name, holder.Name);
        }

        return true;
    }

    public static long OfficesPassedOver { get; private set; }

    private static BotClass Pick(bool burst)
    {
        var mix = BotPopulationConfig.Mix;

        if (BotCritical.Running && BotCritical.Wanting(out var post, Held) is { } asked)
        {
            BotCritical.ByPost++;
            logger.Information("A newcomer is born a {Class}: the island has {Have} of its {Floor} {Post}(s)", asked.Name, BotCritical.OnIsland(post), BotCritical.Floor(post), post);

            return asked;
        }

        if (LeastToWant > 0 && !burst)
        {
            for (var r = 0; r < _wanted.Length; r++)
            {
                var guild = BotGuilds.Lacking(_wanted[r], LeastToWant);

                if (guild == null)
                {
                    continue;
                }

                var wanted = OfRole(_wanted[r], mix);

                if (wanted == null || Held(wanted))
                {
                    continue;
                }

                ByNeed++;
                logger.Information("A newcomer is born a {Class}: {Guild} has nobody of that role", wanted.Name, guild);

                return wanted;
            }
        }

        var total = 0;

        foreach (var (name, many) in mix)
        {
            if (many > 0 && BotClasses.Find(name) is { Office: false })
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
            if (many <= 0 || BotClasses.Find(name) is not { Office: false } klass)
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

    private static BotClass OfRole(BotRole role, IReadOnlyDictionary<string, int> mix)
    {
        var total = 0;

        foreach (var (name, many) in mix)
        {
            if (many > 0 && BotClasses.Find(name) is { } k && k.Role == role && !BotGuilds.Bars(k.Name) && !k.Neutral)
            {
                total += many;
            }
        }

        if (total > 0)
        {
            var roll = Utility.Random(total);

            foreach (var (name, many) in mix)
            {
                if (many <= 0 || BotClasses.Find(name) is not { } k || k.Role != role || BotGuilds.Bars(k.Name) || k.Neutral)
                {
                    continue;
                }

                if (roll < many)
                {
                    return k;
                }

                roll -= many;
            }
        }

        return role switch
        {
            BotRole.Producer => BotClasses.Find("Crafter"),
            BotRole.Medic => BotClasses.Find("Healer"),
            _ => null
        };
    }

    public static void Forget()
    {
        ByNeed = 0;
        _added.Clear();
        _last = default;
        _saidFull = false;
        _saidFilled = false;
    }

    public static string Describe()
    {
        var added = 0;

        foreach (var (_, many) in _added)
        {
            added += many;
        }

        var next = _last == default ? "the clock starts at the first look" : $"the next about {(_last + TimeSpan.FromMinutes(EveryMinutes)).ToLocalTime():HH:mm}";
        var have = BotPopulation.Count + BotPopulation.Away.Count;
        var burst = FillTo <= 0
            ? "no burst"
            : have < Math.Min(FillTo, Most)
                ? $"the burst filling to {Math.Min(FillTo, Most)} at {BurstBy} a look, {Math.Min(FillTo, Most) - have} to go"
                : $"filled to {Math.Min(FillTo, Most)}";

        return $"newcomers: {By} every {EveryMinutes} minutes up to {Most} bots ({burst}, {Burst} raised by it this session); {added} added in all, {Raised} this session, {ByNeed} of them born into a role a guild of {LeastToWant} lacked, {OfficesPassedOver} times a held office passed over; {next}";
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
