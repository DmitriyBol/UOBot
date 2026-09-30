using System;
using System.Collections.Generic;
using Server.Logging;

namespace Server.BotAI.V2;

/// <summary>
/// Work one bot keeps failing for the same reason is not offered to that bot for a while.
///
/// <para>
/// <b>The defect this project has met more often than any other, closed once rather than one instance at a time.</b>
/// A deed fails and the same deed is offered to the same bot at the next review, because nothing between the failure
/// and the choice remembers it: the proposer does not ask the question the deed fails on, the note is written where no
/// chooser reads it, or the work is unpaid and learns nothing. <c>DECISIONS.md</c> C1 records thirty-five of them since
/// 15.08.2026 - the guild's hire failed 4,365 times in 43 minutes on the night of 14.09.2026 and Hale's restock 8,719
/// times in half an hour that evening - each found by its symptom and each mended inside its own proposer.
/// </para>
///
/// <para>
/// <b>Keyed on the reason as well as the kind.</b> A hunter that loses one skeleton and then another has failed twice
/// for two reasons and is not in a loop; a bot told "the pack would not hold the 32gp it drew to pay with" six times in
/// five minutes is. Reasons are compared with their numbers taken out, so "could not raise 1252gp" and "could not raise
/// 1260gp" are one reason. Replayed over every session log of 14.09.2026 before this was written, six in five minutes
/// stopped every loop of that day - 48,287 of the 54,409 failures of the afternoon session, 9,559 of the 9,937 of the
/// night's hire session - and tripped at most once in a session with no loop in it.
/// </para>
///
/// <para>
/// <b>Only failures that produced nothing, which is the test the ledger already takes.</b> <c>BotWill.Settle</c> writes
/// <c>Beware</c> only when a failure's takings are nought or less, because a great many endings called failures did the
/// work - a tool worn through, a seam emptied, a shelf run out. Counted here regardless, the breaker would rest the one
/// trade a scribe is paid for: Ilsa ended eight inscriptions "nothing to write with" in build 41's first half hour, seven
/// of them carrying scrolls worth up to 449. Replayed over the twenty-four session logs of 14.09.2026 both ways, the loops
/// stopped are the same ones - 48,280 failures prevented against 48,287 in the afternoon, 9,548 against 9,559 in the hire
/// session - and the seven trips that go were on errands that earned something on the way.
/// </para>
///
/// <para>
/// <b>A backstop, not a repair, so it speaks every time it trips.</b> A rule that quietly rests a failing kind of work
/// is a rule that hides the defect under it. Each trip is a log line naming the bot, the work and the reason, and the
/// counts are in the Will line; the loop it stopped still has to be found and mended where it starts.
/// </para>
/// </summary>
public static class BotBreaker
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotBreaker));

    public static bool Running { get; set; } = true;

    public static int Failures { get; set; } = 6;

    public static int WindowMs { get; set; } = 300000;

    public static int RestMs { get; set; } = 300000;

    public static int LongestRestMs { get; set; } = 2400000;

    public static long Trips { get; private set; }

    public static long Spared { get; private set; }

    public static long Refused { get; private set; }

    public static long Forgiven { get; private set; }

    public static long Excused { get; set; }

    private sealed class Trail
    {
        public readonly Queue<long> Ticks = new();

        public long Last;
    }

    private sealed class Record
    {
        public string Name;

        public bool Resting;

        public long Until;

        public int Level;

        public string Why;

        public bool Finished;

        public long FinishedTick;
    }

    private static readonly Dictionary<(Serial Bot, string Kind, ulong Reason), Trail> _trails = [];

    private static readonly Dictionary<(Serial Bot, string Kind), Record> _records = [];

    private static readonly Dictionary<(Serial Bot, string Kind, ulong Reason), (string Name, string Why, int Count)> _tally = [];

    private static int _tallied;

    private static readonly List<(Serial Bot, string Kind, ulong Reason)> _staleTrails = [];

    private static readonly List<(Serial Bot, string Kind)> _staleRecords = [];

    private static bool _swept;

    private static long _sweptTick;

    public static int DeathsToRest { get; set; } = 2;

    public static int DeathWindowMs { get; set; } = 1800000;

    public static int DeathRestMs { get; set; } = 900000;

    public static long DeathTrips { get; private set; }

    private static readonly Dictionary<(Serial Bot, string Kind), Queue<long>> _falls = [];

    public static void Fell(Mobile bot, string kind)
    {
        if (!Running || bot == null || string.IsNullOrEmpty(kind)
            || kind is "flee" or "mend" or "rescue" or "await" or "enlist" or "band" or "sweep" or "delve")
        {
            return;
        }

        var now = Core.TickCount;
        var key = (bot.Serial, kind);

        if (!_falls.TryGetValue(key, out var falls))
        {
            if (_falls.Count >= 2048)
            {
                _falls.Clear();
            }

            falls = new Queue<long>();
            _falls[key] = falls;
        }

        falls.Enqueue(now);

        while (falls.Count > 0 && now - falls.Peek() > DeathWindowMs)
        {
            falls.Dequeue();
        }

        if (falls.Count < Math.Max(2, DeathsToRest))
        {
            return;
        }

        var minutes = (now - falls.Peek()) / 60000;

        falls.Clear();

        _records.TryGetValue(key, out var record);

        if (record == null)
        {
            record = new Record();
            _records[key] = record;
        }

        var rest = (long)Math.Min(DeathRestMs * Math.Pow(2.0, Math.Min(record.Level, 16)), LongestRestMs);

        record.Name = bot.Name;
        record.Level++;
        record.Resting = true;
        record.Until = now + rest;
        record.Why = "it died at it";

        Trips++;
        DeathTrips++;

        logger.Information(
            "{Name} died at {Kind} {Times} times in {Minutes} minutes, and that work is not offered to it for {Rest} minutes; tripped {Level} times since it last finished any",
            bot.Name,
            kind,
            Math.Max(2, DeathsToRest),
            minutes,
            rest / 60000,
            record.Level
        );
    }

    public static void Failed(Mobile bot, string kind, string why)
    {
        if (!Running || bot == null || string.IsNullOrEmpty(kind))
        {
            return;
        }

        var now = Core.TickCount;

        Sweep(now);

        var key = (bot.Serial, kind, Shape(why));

        if (_tally.Count >= 4096)
        {
            _tally.Clear();
            _tallied = 0;
        }

        _tally[key] = _tally.TryGetValue(key, out var counted)
            ? (counted.Name, counted.Why, counted.Count + 1)
            : (bot.Name, why ?? "no reason given", 1);
        _tallied++;

        if (!_trails.TryGetValue(key, out var trail))
        {
            trail = new Trail();
            _trails[key] = trail;
        }

        _records.TryGetValue((bot.Serial, kind), out var record);

        trail.Last = now;
        trail.Ticks.Enqueue(now);

        var most = Math.Max(2, Failures);

        while (trail.Ticks.Count > 0)
        {
            var oldest = trail.Ticks.Peek();
            var expired = now - oldest > WindowMs;
            var beforeFinish = record is { Finished: true } && oldest - record.FinishedTick < 0;

            if (!expired && !beforeFinish && trail.Ticks.Count <= most)
            {
                break;
            }

            trail.Ticks.Dequeue();
        }

        if (trail.Ticks.Count < most)
        {
            return;
        }

        var seconds = (now - trail.Ticks.Peek()) / 1000;

        trail.Ticks.Clear();

        if (kind == BotBolt.Trade)
        {
            Spared++;

            return;
        }

        if (record == null)
        {
            record = new Record();
            _records[(bot.Serial, kind)] = record;
        }

        var rest = (long)Math.Min(RestMs * Math.Pow(2.0, Math.Min(record.Level, 16)), LongestRestMs);

        record.Name = bot.Name;
        record.Level++;
        record.Resting = true;
        record.Until = now + rest;
        record.Why = why ?? "no reason given";

        Trips++;

        logger.Information(
            "{Name} failed at {Kind} {Times} times in {Seconds}s for the same reason ({Why}), and that work is not offered to it for {Minutes} minutes; tripped {Level} times since it last finished any",
            bot.Name,
            kind,
            most,
            seconds,
            record.Why,
            rest / 60000,
            record.Level
        );
    }

    public static bool Loudest(out string name, out string kind, out string why, out int times, out int failures)
    {
        name = null;
        kind = null;
        why = null;
        times = 0;
        failures = _tallied;

        foreach (var (key, counted) in _tally)
        {
            if (counted.Count > times)
            {
                name = counted.Name;
                kind = key.Kind;
                why = counted.Why;
                times = counted.Count;
            }
        }

        _tally.Clear();
        _tallied = 0;

        return times > 0;
    }

    public static bool Resting(Mobile bot, string kind, out string why, out long leftMs)
    {
        why = null;
        leftMs = 0;

        if (!Running || bot == null || _records.Count == 0 || string.IsNullOrEmpty(kind))
        {
            return false;
        }

        if (!_records.TryGetValue((bot.Serial, kind), out var record) || !record.Resting)
        {
            return false;
        }

        leftMs = record.Until - Core.TickCount;

        if (leftMs <= 0)
        {
            record.Resting = false;
            leftMs = 0;

            return false;
        }

        Refused++;
        why = record.Why;

        return true;
    }

    public static long RestLeftMs(Mobile bot, string kind)
    {
        if (bot == null || string.IsNullOrEmpty(kind) || !_records.TryGetValue((bot.Serial, kind), out var record) || !record.Resting)
        {
            return 0;
        }

        return Math.Max(0, record.Until - Core.TickCount);
    }

    public static void Finished(Mobile bot, string kind)
    {
        if (bot == null || string.IsNullOrEmpty(kind))
        {
            return;
        }

        if (!_records.TryGetValue((bot.Serial, kind), out var record))
        {
            record = new Record { Name = bot.Name };
            _records[(bot.Serial, kind)] = record;
        }

        if (record.Resting)
        {
            Forgiven++;
        }

        record.Resting = false;
        record.Level = 0;
        record.Finished = true;
        record.FinishedTick = Core.TickCount;
    }

    public static void Forget(Mobile bot)
    {
        if (bot == null)
        {
            return;
        }

        var serial = bot.Serial;

        foreach (var key in _trails.Keys)
        {
            if (key.Bot == serial)
            {
                _staleTrails.Add(key);
            }
        }

        foreach (var key in _records.Keys)
        {
            if (key.Bot == serial)
            {
                _staleRecords.Add(key);
            }
        }

        for (var i = 0; i < _staleTrails.Count; i++)
        {
            _trails.Remove(_staleTrails[i]);
        }

        for (var i = 0; i < _staleRecords.Count; i++)
        {
            _records.Remove(_staleRecords[i]);
        }

        _staleTrails.Clear();
        _staleRecords.Clear();
    }

    private static void Sweep(long now)
    {
        if (_swept && now - _sweptTick < 60000)
        {
            return;
        }

        _swept = true;
        _sweptTick = now;

        foreach (var (key, trail) in _trails)
        {
            if (now - trail.Last > WindowMs)
            {
                _staleTrails.Add(key);
            }
        }

        for (var i = 0; i < _staleTrails.Count; i++)
        {
            _trails.Remove(_staleTrails[i]);
        }

        _staleTrails.Clear();
    }

    private static ulong Shape(string why)
    {
        const ulong prime = 1099511628211UL;

        var hash = 14695981039346656037UL;

        if (string.IsNullOrEmpty(why))
        {
            return hash;
        }

        var number = false;

        for (var i = 0; i < why.Length; i++)
        {
            var c = why[i];
            var digit = char.IsAsciiDigit(c) || (number && c == '.' && i + 1 < why.Length && char.IsAsciiDigit(why[i + 1]));

            if (digit)
            {
                if (!number)
                {
                    hash = unchecked((hash ^ 'N') * prime);
                    number = true;
                }

                continue;
            }

            number = false;
            hash = unchecked((hash ^ c) * prime);
        }

        return hash;
    }

    public static string Describe()
    {
        var now = Core.TickCount;
        var resting = 0;

        foreach (var record in _records.Values)
        {
            if (record.Resting && record.Until - now > 0)
            {
                resting++;
            }
        }

        return $"{Excused} failures kept from the breaker for having made goods or skill first; {Trips} times one bot's work was rested after failing {Math.Max(2, Failures)} times in {WindowMs / 60000} minutes for the same reason ({DeathTrips} of them after dying at it {Math.Max(2, DeathsToRest)} times in {DeathWindowMs / 60000}), {resting} resting now, {Refused} offers refused while resting and {Forgiven} rests lifted because the work was finished after all, {Spared} flights that tripped it and were not rested";
    }

    public static string List()
    {
        var now = Core.TickCount;
        var parts = new List<string>();

        foreach (var (key, record) in _records)
        {
            if (!record.Resting || record.Until - now <= 0)
            {
                continue;
            }

            parts.Add($"{record.Name}: {key.Kind} for {(record.Until - now) / 1000}s more, trip {record.Level}, after \"{record.Why}\"");
        }

        return parts.Count == 0
            ? $"nothing is resting. {Describe()}."
            : $"{parts.Count} resting: {string.Join("; ", parts)}. {Describe()}.";
    }
}
