using System;
using System.Collections.Generic;
using System.Reflection;
using Server.BotAI.V2;
using Server.Logging;
using Server.Text;

namespace Server.BotAI.Mind;

/// <summary>
/// The debugger itself: the body, the watch it keeps, and the two questions it asks.
///
/// <para>
/// <b>Three clocks, and they are three different questions rather than one question at three speeds.</b>
/// Every couple of seconds it measures — that costs nothing and is the only thing here that produces facts.
/// Every couple of minutes it asks the model what the worst thing in front of it is, cheaply and without
/// thinking, because that question is mostly reading. Every quarter of an hour it asks the expensive
/// thinking question: what do all of these have in common. Collapsing any two of them would either make the
/// measurement as rare as the thinking or the thinking as constant as the measurement, and neither is worth
/// having.
/// </para>
///
/// <para>
/// <b>It shares one slot with the three minds and does not get priority.</b> There is one graphics card and
/// one model on it, and while a thinking call runs nothing else can be asked anything — measured at fifty-
/// eight and ninety-nine seconds on this card. So the debugger asks only when the slot is free and never
/// holds it on the frequent question. A watcher that starves the population it is watching would change the
/// thing it is measuring, which is the one failure a watcher may not have.
/// </para>
///
/// <para>
/// <b>What it writes is a conjecture and the log says so.</b> Every entry carries the model's claim and,
/// under it, the digest the claim was made from. Read on its own, a confident paragraph about a defect is
/// indistinguishable from a true one; read beside the numbers, it can be checked in a minute. That is the
/// whole reason this is worth running at all.
/// </para>
/// </summary>
public static class BotVigil
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotVigil));

    /// <summary>How often the population is measured. Cheap: a pass over the roll and no allocation worth naming.</summary>
    public static int SampleMs { get; set; } = 2000;

    /// <summary>How often the debugger moves to stand beside somebody else.</summary>
    public static int HoverMs { get; set; } = 20000;

    /// <summary>
    /// How often it is asked what the worst thing in front of it is.
    ///
    /// <para>
    /// <b>Ten minutes, and it used to be two.</b> The debugger is not here to keep up with the shard; it is
    /// here to think about it. Every question it asks costs the population its model — see
    /// <see cref="Model"/> — so asking rarely and answering well is strictly better than asking often and
    /// answering fast, which is the opposite of the trade the three minds make.
    /// </para>
    /// </summary>
    public static int ReportMs { get; set; } = 600000;

    /// <summary>
    /// How often it is asked the expensive question, with thinking switched on.
    ///
    /// Half an hour. One of these holds the card for a cold load, a long think and another cold load back,
    /// and the three minds decide nothing throughout. That is worth paying twice an hour for the one question
    /// nothing else on this shard asks, and is not worth paying twenty times.
    /// </summary>
    public static int ReflectMs { get; set; } = 1800000;

    /// <summary>
    /// The model the debugger thinks with, which is deliberately not the one the population thinks with.
    ///
    /// <para>
    /// <b>The three minds and the debugger want opposite things, and one graphics card cannot give both.</b>
    /// A mind is asked every twenty seconds and must answer in a second or two, so it needs the fastest model
    /// that can name a trade. The debugger is asked twice an hour and is asked to explain several symptoms at
    /// once, which is the hardest question anything on this shard is asked; how long it takes matters not at
    /// all.
    /// </para>
    ///
    /// <para>
    /// <b>Measured rather than assumed, on 01.09.2026, on the real reflection question.</b> The population's
    /// own <c>qwen3.5:9b</c> thought for 116 seconds, produced 24,000 characters of reasoning and returned an
    /// empty answer — it fails this question outright, and nobody would have known because the reflection had
    /// never yet run. <c>qwen3:14b</c> answered in 32 seconds, correctly, and named one symptom.
    /// <c>deepseek-r1:14b</c> answered in 44 seconds and did what was actually asked: tied the idle bots, the
    /// abandoned errands and the path counters into one account, and gave a falsifiable test for it.
    /// </para>
    ///
    /// <para>
    /// Nine gigabytes against the card's twelve, so it cannot be resident beside the population's model and
    /// every question the debugger asks costs two cold loads. That is what <see cref="KeepAlive"/> and the
    /// cadences above are for: it must let go of the card the moment it has finished.
    /// </para>
    /// </summary>
    public static string Model { get; set; } = "deepseek-r1:14b";

    /// <summary>
    /// How long the debugger's model stays in video memory after it has answered.
    ///
    /// <para>
    /// <b>Seconds, not minutes, and this is the setting that keeps the debugger from ruining the shard.</b>
    /// The population's model holds the card for half an hour at a time by design. If the debugger's held it
    /// for the same, every one of the three minds' questions for the next half hour would evict a nine-
    /// gigabyte model and load a six-gigabyte one back, twice a minute, and the population would stop
    /// thinking altogether — because of the thing watching it.
    /// </para>
    /// </summary>
    public static string KeepAlive { get; set; } = "10s";

    /// <summary>
    /// How long the debugger will wait for its own answer. Longer than the minds', because it has to cover a
    /// cold load of nine gigabytes and a long think on top of it.
    /// </summary>
    public static int TimeoutMs { get; set; } = 420000;

    /// <summary>
    /// How long a bot may hold no work at all before it is worth a number of its own.
    ///
    /// Three minutes: the auction comes round every fifteen seconds, so a bot that has been offered nothing
    /// twelve times running is not between jobs.
    /// </summary>
    public static int LoiterMs { get; set; } = 180000;

    /// <summary>How many bots are described in full in one report. The rest are in the counts.</summary>
    public static int Rows { get; set; } = 6;

    /// <summary>How many past findings are recited back at a reflection.</summary>
    public static int Recall { get; set; } = 6;

    /// <summary>How much of the shard's own instrumentation is quoted, in characters.</summary>
    public static int SubsystemBudget { get; set; } = 1800;

    /// <summary>
    /// The most a question may be, in characters, before it is cut.
    ///
    /// <para>
    /// <b>Every question the debugger asked between three in the morning and nine went unanswered, and this
    /// is why.</b> The prompt grows on its own: findings accumulate, the long memory accumulates, the
    /// session's own history accumulates. Measured over one night it went 14780 → 17467 → 19981 characters,
    /// which against a context of 8192 tokens leaves a thinking model no room to think in — so it thinks,
    /// runs out, and returns an empty answer. Forty-two reports and twelve reflections came back unreadable
    /// and the only symptom was silence, which is exactly what a healthy quiet night looks like.
    /// </para>
    ///
    /// <para>
    /// Cut here rather than by raising the context, and the reasoning is the card again: this model is nine
    /// gigabytes of a twelve-gigabyte card, and doubling the context adds gigabytes of key-value cache that
    /// would push it onto the processor. A bound that cannot be exceeded is also worth more than a bigger
    /// bound that can: this one cannot grow silently, because what is cut is said in the prompt itself.
    /// </para>
    /// </summary>
    public static int MostChars { get; set; } = 9000;

    /// <summary>What it is called. Its findings are written under this name.</summary>
    public static string Name { get; set; } = "Argus";

    private static readonly Dictionary<Serial, BotWatch> _watch = [];

    private static readonly List<string> _found = [];

    private static readonly Dictionary<string, int> _rungs = [];

    private static readonly Dictionary<string, int> _holding = [];

    private static MethodInfo[] _describers;

    private static Timer _timer;

    private static long _sampledTick;

    private static long _hoveredTick;

    private static long _reportedTick;

    private static long _reflectedTick;

    private static long _wokeTick;

    private static bool _asking;

    private static string _wanted;

    private static string _because = "it was the first bot I saw";

    /// <summary>The body, or null while it has none.</summary>
    public static BotDebugger Body { get; private set; }

    /// <summary>The last thing it claimed, recited back to it next time.</summary>
    public static BotDebugNote Last { get; private set; }

    /// <summary>Questions asked, findings written, and times it looked and said the shard was fine.</summary>
    public static long Asked { get; private set; }

    public static long Findings { get; private set; }

    public static long Quiet { get; private set; }

    public static long Reflections { get; private set; }

    public static bool Running => _timer != null;

    public static void Start()
    {
        Stop();

        _watch.Clear();
        _found.Clear();

        BotAudit.Reset();

        Asked = 0;
        Findings = 0;
        Quiet = 0;
        Reflections = 0;
        Last = null;
        _asking = false;
        _wanted = null;

        var now = Core.TickCount;

        _wokeTick = now;
        _sampledTick = now;
        _hoveredTick = now;
        _reportedTick = now;
        _reflectedTick = now;

        Purge();
        Embody();

        _timer = new VigilTimer(TimeSpan.FromMilliseconds(Math.Max(250, SampleMs)));
        _timer.Start();
    }

    public static void Stop()
    {
        _timer?.Stop();
        _timer = null;
    }

    /// <summary>The body goes with the world it stood in. Called on a reload.</summary>
    public static void Reset()
    {
        Stop();

        Body?.Delete();
        Body = null;

        _watch.Clear();
    }

    /// <summary>
    /// Deletes any debugger that came back from the world save.
    ///
    /// <para>
    /// <b>The same decision the population made, for the same reason.</b> Nothing about this body is worth
    /// keeping — it holds no state, its whole memory is in this file and its log — and a saved one would
    /// come back beside the fresh one every restart until there were a dozen invisible figures standing in
    /// Britain. It is written into the save at all only because the engine writes every mobile; being read
    /// back and deleted is the cheapest way to be sure of that.
    /// </para>
    /// </summary>
    private static void Purge()
    {
        List<BotDebugger> stale = [];

        foreach (var mobile in World.Mobiles.Values)
        {
            if (mobile is BotDebugger old)
            {
                stale.Add(old);
            }
        }

        for (var i = 0; i < stale.Count; i++)
        {
            stale[i].Delete();
        }

        if (stale.Count > 0)
        {
            logger.Information("Deleted {Count} debuggers that came back from the world save", stale.Count);
        }
    }

    /// <summary>Raises the body, next to the population, or says why it could not.</summary>
    private static void Embody()
    {
        var map = BotPopulation.Home;

        if (map == null || map == Map.Internal)
        {
            logger.Error("The debugger has nowhere to stand: the population has no home map, so it was not raised");

            return;
        }

        Body = new BotDebugger();
        Body.Awaken(Name);
        Body.Hover(map, BotPopulation.Where);

        logger.Information(
            "{Name} the debugger is awake at {Where} on {Map}, invisible to everyone below {Rank}, and writing to {Log}",
            Name,
            BotPopulation.Where,
            map,
            BotDebugger.SeenBy,
            BotDebugLog.Path ?? "nowhere"
        );
    }

    private static void Update()
    {
        var now = Core.TickCount;
        var since = now - _sampledTick;

        if (since < SampleMs)
        {
            return;
        }

        _sampledTick = now;

        if (Body is not { Deleted: false })
        {
            // The body can be lost — a reload, a stray delete — and a debugger without one measures nothing
            // and says nothing about it. Raising a new one is cheap and the alternative is silence.
            Embody();

            if (Body == null)
            {
                return;
            }
        }

        Sample(now, since);

        // <b>Before anything that needs the model, and never gated on it.</b> The roll-call is arithmetic
        // and must fire on its own clock whatever the card is doing: a check that only runs when a model is
        // free is a check that stops exactly when the shard is busiest.
        // The door, before anything else and never gated on the model: a question that can be answered from
        // measurements already taken should not wait behind one that cannot.
        BotConsole.Listen(now);

        if (BotAudit.Due(now))
        {
            BotAudit.Sweep(now, Rollcall());
        }

        if (now - _hoveredTick >= HoverMs)
        {
            _hoveredTick = now;

            Follow();
        }

        // World.Saving as well as the slot: the transport answers "no" to both by calling straight back with
        // nothing, and a debugger that treats that as an unreadable answer writes a warning to the log every
        // two seconds for the length of a save.
        if (_asking || !BotOllama.Free || World.Saving)
        {
            return;
        }

        if (now - _reflectedTick >= ReflectMs)
        {
            _reflectedTick = now;
            _reportedTick = now;

            Reflect(now);

            return;
        }

        var waited = now - _reportedTick;

        if (waited >= ReportMs)
        {
            _reportedTick = now;

            Look(now, waited);
        }
    }

    /// <summary>One pass over the population. Everything this file later says was measured here.</summary>
    private static void Sample(long now, long since)
    {
        var bots = BotPopulation.Bots;

        for (var i = 0; i < bots.Count; i++)
        {
            var bot = bots[i];

            if (bot is not { Deleted: false })
            {
                continue;
            }

            if (!_watch.TryGetValue(bot.Serial, out var watch))
            {
                watch = new BotWatch(bot, now);
                _watch[bot.Serial] = watch;
            }

            watch.Sample(now, since);
        }

        // Bots deleted since the last pass leave rows behind that would go on being reported for ever, and a
        // report about a bot that no longer exists is the purest kind of false finding.
        if (_watch.Count <= bots.Count)
        {
            return;
        }

        List<Serial> gone = [];

        foreach (var (serial, watch) in _watch)
        {
            if (watch.Bot is not { Deleted: false })
            {
                gone.Add(serial);
            }
        }

        for (var i = 0; i < gone.Count; i++)
        {
            _watch.Remove(gone[i]);
        }
    }

    /// <summary>
    /// Goes and stands beside whoever most wants looking at.
    ///
    /// <para>
    /// <b>Standing there changes nothing and that is the point of the body.</b> The measurements are taken
    /// off the whole population from anywhere; being beside a bot buys the region it is in, what is around
    /// it, and a person watching over the debugger's shoulder being able to see what it is talking about.
    /// If a place ever mattered to a measurement, that measurement would be wrong.
    /// </para>
    ///
    /// <para>
    /// The model's own request comes first when it made one, then whoever scores worst, and failing both it
    /// keeps moving round the population rather than standing still: a watcher parked beside the one healthy
    /// bot on the shard is worse than one that wanders.
    /// </para>
    /// </summary>
    private static void Follow()
    {
        BotWatch pick = null;
        var worst = 0.0;

        foreach (var (_, watch) in _watch)
        {
            if (watch.Bot is not { Deleted: false })
            {
                continue;
            }

            if (_wanted != null && string.Equals(watch.Name, _wanted, StringComparison.OrdinalIgnoreCase))
            {
                pick = watch;
                _because = "you asked to watch this one";
                _wanted = null;

                break;
            }

            if (watch.Suspicion > worst)
            {
                worst = watch.Suspicion;
                pick = watch;
            }
        }

        if (pick == null)
        {
            // Nobody has a symptom. Move anyway, to whoever has come least far in its trade: that is the bot
            // a question about development is most likely to be answerable beside.
            var least = double.MaxValue;

            foreach (var (_, watch) in _watch)
            {
                if (watch.Bot is { Deleted: false } && watch.Progress < least)
                {
                    least = watch.Progress;
                    pick = watch;
                }
            }

            if (pick != null)
            {
                _because = $"nobody has a symptom, so I went to the least developed of them at {pick.Progress:P0}";
            }
        }
        else if (worst > 0.0 && _wanted == null)
        {
            _because = string.IsNullOrWhiteSpace(pick.Symptoms)
                ? "it was the worst of an untroubled population"
                : pick.Symptoms;
        }

        // Cleared whether or not it was found. A request for a bot that has since died would otherwise be
        // retried on every hop for the rest of the session, and the debugger would spend its life trying to
        // reach somebody who is not there while the suspicion ranking sat unused.
        _wanted = null;

        var bot = pick?.Bot;

        if (bot is not { Deleted: false } || bot.Map == null || bot.Map == Map.Internal)
        {
            return;
        }

        Body.Hover(bot.Map, bot.Location);
    }

    /// <summary>The frequent question. No thinking: this one is mostly reading, and it must not hold the slot.</summary>
    private static void Look(long now, long waited)
    {
        var roster = Roster();

        if (roster.Count == 0)
        {
            return;
        }

        _asking = true;
        Asked++;

        // <b>When the question was asked, kept, because the answer's own stamp is the wrong one.</b> The
        // model takes seconds to answer and a thinking call takes up to a minute and a half, and the shard
        // does not stop while it does: on 01.09.2026 a report saying "116 undertakings taken on" was written
        // into the log at a moment when the true figure was 141, because twenty-five had been taken during
        // the seven seconds the answer was in flight. Both numbers were right. Only one of them was about
        // the moment the log line is stamped with, and reading this file beside the session log is the whole
        // reason it is stamped in local time at all.
        var asked = DateTime.Now;

        // Measured once and kept, so that the digest written under the answer is the digest the answer was
        // made from. Re-measuring when the reply arrives would put a different set of numbers under the
        // claim than the one it was reasoning about, which is the same defect as the stamp above and harder
        // to notice: the counts would look plausible and would quietly not support the sentence over them.
        // The roll-call's verdicts go in beside the measurements, and they are the better half of the report:
        // every other number here describes a moment, and these describe two minutes of what a bot did with
        // itself. They also tell the model what the debugger has done with its own hands, which it is asked
        // to be suspicious of.
        var mine = Measured(now)
                   + "\nTHE LAST ROLL-CALL, TWO MINUTES OF IT, ASKED OF EVERY BOT\n"
                   + BotAudit.Last
                   + "\nWhat the roll-calls have done all session: "
                   + BotAudit.Describe()
                   + ".";

        var report = BotDebugSight.Report(
            Beside(),
            Census(),
            mine,
            Suspects(now),
            Subsystems(SubsystemBudget),
            BotDebugSight.Recite(Last),
            waited
        );

        // Thinking on the cheap question too. It is not cheap any more and is not meant to be: the whole
        // point of this bot is that it thinks about what it sees, and it now asks six times an hour instead
        // of thirty.
        BotOllama.Ask(
            BotDebugSight.System(Name),
            Bounded(report),
            BotDebugNote.Schema(roster),
            true,
            (json, waited) => Answered(json, waited, report, mine, asked),
            Model,
            KeepAlive,
            TimeoutMs
        );
    }

    private static void Answered(string json, long waited, string report, string mine, DateTime asked)
    {
        _asking = false;

        var note = BotDebugNote.Read(json);

        if (note == null)
        {
            logger.Warning("The debugger looked at the population and the model said nothing that could be read");

            return;
        }

        Last = note;

        if (string.Equals(note.Kind, "nothing", StringComparison.OrdinalIgnoreCase))
        {
            Quiet++;

            BotDebugLog.Write($"nothing worth reporting — measured at {asked:HH:mm:ss}, answered {waited}ms later — {note.Finding}");

            // <b>The counts go under a clean bill of health too, and leaving them out was the first thing
            // this log got wrong.</b> "Nothing is wrong" is a claim like any other and is the one most worth
            // being able to check afterwards: a watcher that has quietly stopped measuring says exactly this,
            // in exactly these words, for hours. The rows are left out — they are only interesting when
            // something is — but the aggregate is what would show the silence to be false.
            BotDebugLog.Block("  the counts it said that about:", mine);

            if (!string.IsNullOrWhiteSpace(note.Watch) && note.Watch != "-")
            {
                _wanted = note.Watch;
            }

            return;
        }

        Findings++;

        BotDebugLog.Rule();
        BotDebugLog.Write(
            $"FINDING {Findings} — {note.Kind}, about {note.Bot}, {note.Confidence:P0} sure. "
            + $"Everything below was measured at {asked:HH:mm:ss}; the answer came {waited}ms later"
        );
        BotDebugLog.Block("  claim:", note.Finding);
        BotDebugLog.Block("  evidence it quoted:", note.Evidence);
        BotDebugLog.Block("  what it thinks is behind it (CONJECTURE):", note.Cause);
        BotDebugLog.Block("  change it suggests (CONJECTURE):", note.Fix);
        BotDebugLog.Block("  its last claim:", note.Last);

        // The measurements the claim was made from, under it, unedited. Without this the log is a series of
        // confident paragraphs nobody can check, and a claim nobody can check is worse than none because it
        // gets acted on.
        BotDebugLog.Block("  measured, and this is what it was reasoning from:", report);
        BotDebugLog.Rule();

        Remember(BotDebugSight.Recite(note));

        // Into the long memory, merged with anything it has said before. The count of how often the same
        // thing has been found from fresh measurements is the only reason to weigh one finding above another.
        BotDebugMemory.Believe(note);

        if (string.Equals(note.Last, "gone", StringComparison.OrdinalIgnoreCase) && Last != null)
        {
            BotDebugMemory.Doubt(Last.Finding);
        }

        if (!string.IsNullOrWhiteSpace(note.Watch) && note.Watch != "-")
        {
            _wanted = note.Watch;
        }

        logger.Information(
            "The debugger has a finding ({Kind}, {Sure:P0}) about {Who}: {What}",
            note.Kind,
            note.Confidence,
            note.Bot,
            note.Finding
        );
    }

    /// <summary>The slow question, with thinking switched on. Rare, and it costs the minds their slot while it runs.</summary>
    private static void Reflect(long now)
    {
        _asking = true;
        Asked++;

        var asked = DateTime.Now;

        var question = BotDebugSight.Reflection(
            Census(),
            Measured(now),
            Subsystems(SubsystemBudget * 2),
            _found,
            now - _wokeTick
        );

        BotOllama.Ask(
            BotDebugSight.System(Name),
            Bounded(question),
            BotDebugThought.Schema,
            true,
            (json, waited) => Thought(json, waited, question, asked),
            Model,
            KeepAlive,
            TimeoutMs
        );
    }

    private static void Thought(string json, long waited, string question, DateTime asked)
    {
        _asking = false;

        var thought = BotDebugThought.Read(json);

        if (thought == null)
        {
            logger.Warning("The debugger thought about the shard for a while and the answer could not be read");

            return;
        }

        Reflections++;

        BotDebugLog.Rule();
        BotDebugLog.Write(
            $"REFLECTION {Reflections} — measured at {asked:HH:mm:ss}, thought for {waited / 1000}s, {thought.Confidence:P0} sure"
        );
        BotDebugLog.Block("  what most blocks these bots (CONJECTURE):", thought.Blocking);
        BotDebugLog.Block("  evidence:", thought.Evidence);
        BotDebugLog.Block("  change to make:", thought.Change);
        BotDebugLog.Block("  second most likely:", thought.Second);
        BotDebugLog.Block("  what would show this to be wrong:", thought.Wrong);
        BotDebugLog.Block("  everything it was given:", question);
        BotDebugLog.Rule();

        Remember($"[reflection] {thought.Blocking} — change: {thought.Change}");

        BotDebugMemory.Learn($"{thought.Blocking} (the change worth making: {thought.Change})");

        logger.Information("The debugger has thought about the shard: {What}", thought.Blocking);
    }

    private static void Remember(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return;
        }

        _found.Add(line);

        while (_found.Count > Recall)
        {
            _found.RemoveAt(0);
        }
    }

    /// <summary>
    /// The question, cut to something the model can still think inside, and told what was cut.
    ///
    /// <para>
    /// Never silently: a prompt that has quietly lost its last third produces an answer about the part that
    /// survived, and there is no way to tell that from an answer about all of it. The line at the bottom is
    /// what makes a truncated question honest.
    /// </para>
    /// </summary>
    private static string Bounded(string question)
    {
        if (question == null || question.Length <= MostChars)
        {
            return question;
        }

        var cut = question.Length - MostChars;

        return string.Concat(
            question.AsSpan(0, MostChars),
            $"\n\n[{cut} characters of this report were cut to leave you room to think. What was cut came from"
            + " the end: the older findings and the subsystem summaries. Everything above is complete.]"
        );
    }

    /// <summary>
    /// The whole of what is measured this moment, for whoever asks through the door.
    ///
    /// The same text the model is given, deliberately: a person debugging the debugger needs to see what it
    /// sees, not a second rendering that could differ from it.
    /// </summary>
    public static string Digest() =>
        Census() + "\n" + Measured(Core.TickCount) + "\nTHE LAST ROLL-CALL\n" + BotAudit.Last;

    /// <summary>One bot in full, by name, or the roster if that is not one of them.</summary>
    public static string Row(string name)
    {
        if (!string.IsNullOrWhiteSpace(name))
        {
            foreach (var (_, watch) in _watch)
            {
                if (watch.Bot is { Deleted: false }
                    && string.Equals(watch.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    return watch.Row(Core.TickCount);
                }
            }
        }

        var roster = Roster();

        return $"No bot called \"{name}\". There are {roster.Count}: {string.Join(", ", roster)}";
    }

    /// <summary>
    /// Who is holding no work at all, with their class and how long, worst first.
    ///
    /// <para>
    /// <b>Built because the aggregate could not answer the question it raised.</b> On 02.09.2026 the counts
    /// said 16 of 38 had held nothing for over three minutes and 540 bot-minutes had gone barren — about
    /// half of all the time the population had — and there was no way to ask which bots or of what class
    /// without grepping a log by hand. A number that names a problem and cannot name a subject sends
    /// whoever reads it back to the raw log, which is the state this whole debugger exists to end.
    /// </para>
    /// </summary>
    public static string Loitering()
    {
        List<BotWatch> idle = [];

        foreach (var (_, watch) in _watch)
        {
            if (watch.Bot is { Deleted: false } && watch.IdleMs > 0)
            {
                idle.Add(watch);
            }
        }

        if (idle.Count == 0)
        {
            return "Nobody is holding nothing: every bot has work in hand this moment.";
        }

        idle.Sort((a, b) => b.IdleMs.CompareTo(a.IdleMs));

        var sb = ValueStringBuilder.Create(1024);

        try
        {
            sb.Append(idle.Count);
            sb.AppendLine(" bots hold no work at all this moment, longest first. A moment is ordinary; minutes are not.");

            for (var i = 0; i < idle.Count; i++)
            {
                var watch = idle[i];

                sb.Append("- ");
                sb.Append(watch.Name);
                sb.Append(" the ");
                sb.Append(watch.Class);
                sb.Append(", ");
                sb.Append(watch.IdleMs / 1000);
                sb.Append("s with nothing, on the ");
                sb.Append(watch.Standing);
                sb.Append(" rung at ");
                sb.Append(watch.Where.X);
                sb.Append(",");
                sb.Append(watch.Where.Y);
                sb.Append(" in ");
                sb.Append(watch.Region);
                sb.Append("; worth ");
                sb.Append(watch.Worth);
                sb.Append("gp, trade at ");
                sb.Append(watch.Progress * 100.0, "F0");
                sb.AppendLine("%.");
            }

            return sb.ToString();
        }
        finally
        {
            sb.Dispose();
        }
    }

    public static string TradeTable()
    {
        var sb = ValueStringBuilder.Create(2048);

        try
        {
            Trades(ref sb);

            return sb.ToString();
        }
        finally
        {
            sb.Dispose();
        }
    }

    public static string CombatTable()
    {
        var sb = ValueStringBuilder.Create(512);

        try
        {
            Fighting(ref sb);

            return sb.ToString();
        }
        finally
        {
            sb.Dispose();
        }
    }

    /// <summary>
    /// Puts one question of somebody's own to the model, with everything measured attached.
    ///
    /// <para>
    /// <b>It waits its turn like everything else.</b> If the card is busy the question is refused rather than
    /// queued: whoever asked can ask again in ten seconds, and a queue would let one careless hand hold the
    /// model against the three minds indefinitely.
    /// </para>
    /// </summary>
    public static bool Consider(string question, Action<string> reply)
    {
        if (string.IsNullOrWhiteSpace(question) || reply == null || _asking || !BotOllama.Free || World.Saving)
        {
            return false;
        }

        _asking = true;
        Asked++;

        var asked = DateTime.Now;

        var put = "SOMEBODY AT THE KEYBOARD IS ASKING YOU THIS, AND IT COMES BEFORE ANYTHING ELSE HERE:\n"
                  + question
                  + "\n\nAnswer it from the measurements below. Quote the numbers you use. If the measurements"
                  + " cannot settle it, say so plainly and say what would have to be measured instead — that is"
                  + " a useful answer, and a guess dressed as one is not.\n\n"
                  + Digest()
                  + "\n\nWHAT EACH TRADE HAS COME TO\n"
                  + TradeTable();

        BotOllama.Ask(
            BotDebugSight.System(Name),
            Bounded(put),
            AnswerSchema,
            true,
            (json, waited) =>
            {
                _asking = false;

                var said = BotDebugThought.ReadAnswer(json);

                BotDebugLog.Rule();
                BotDebugLog.Write($"ASKED AT THE DOOR at {asked:HH:mm:ss}, answered {waited / 1000}s later");
                BotDebugLog.Block("  question:", question);
                BotDebugLog.Block("  answer (CONJECTURE):", said ?? "nothing that could be read");
                BotDebugLog.Rule();

                reply(said ?? "the model answered nothing that could be read.");
            },
            Model,
            KeepAlive,
            TimeoutMs
        );

        return true;
    }

    private const string AnswerSchema =
        """
        {"type":"object","properties":{"answer":{"type":"string","minLength":120},"evidence":{"type":"string","minLength":60},"confidence":{"type":"number"}},"required":["answer","evidence","confidence"]}
        """;

    /// <summary>Everybody being watched, in one list, for the roll-call.</summary>
    private static List<BotWatch> Rollcall()
    {
        List<BotWatch> roll = [];

        foreach (var (_, watch) in _watch)
        {
            if (watch.Bot is { Deleted: false })
            {
                roll.Add(watch);
            }
        }

        return roll;
    }

    /// <summary>Everybody who exists, by name. What the answer's bot fields are constrained to.</summary>
    private static List<string> Roster()
    {
        List<string> names = [];

        foreach (var (_, watch) in _watch)
        {
            if (watch.Bot is { Deleted: false } && !string.IsNullOrWhiteSpace(watch.Name) && !names.Contains(watch.Name))
            {
                names.Add(watch.Name);
            }
        }

        return names;
    }

    private static string Beside()
    {
        var body = Body;

        if (body is not { Deleted: false })
        {
            return "Nowhere: I have no body this moment.";
        }

        BotWatch here = null;

        foreach (var (_, watch) in _watch)
        {
            if (watch.Bot is { Deleted: false } && watch.Bot.Map == body.Map && watch.Bot.Location == body.Location)
            {
                here = watch;

                break;
            }
        }

        var where = $"I am standing at {body.Location.X},{body.Location.Y} in {body.Region?.Name ?? "nowhere"}";

        return here == null
            ? $"{where}. Nobody is on this tile. I moved here because {_because}."
            : $"{where}, on the same tile as {here.Name} the {here.Class}. I came here because {_because}.";
    }

    /// <summary>The population as the shard itself counts it, with every case named.</summary>
    private static string Census()
    {
        _rungs.Clear();
        _holding.Clear();

        foreach (var (_, watch) in _watch)
        {
            if (watch.Bot is not { Deleted: false })
            {
                continue;
            }

            _rungs[watch.Standing] = _rungs.GetValueOrDefault(watch.Standing) + 1;

            var kind = watch.Kind == "-" ? "nothing at all" : watch.Kind;

            _holding[kind] = _holding.GetValueOrDefault(kind) + 1;
        }

        return BotDebugSight.Census(_rungs, _holding, BotWill.Describe());
    }

    /// <summary>
    /// What the debugger has measured for itself, as counts with denominators.
    ///
    /// Each line answers one question and none of them has a bucket called "other". A count with no
    /// denominator cannot say whether nought means "it never happens" or "nobody ever got as far as the
    /// check", and telling those two apart is most of this job.
    /// </summary>
    private static string Measured(long now)
    {
        var sb = ValueStringBuilder.Create(1024);

        try
        {
            var total = 0;
            var frozen = 0;
            var nowhere = 0;
            var pacing = 0;
            var gaveUp = 0;
            var immortal = 0;
            var bouncing = 0;
            var refused = 0;
            var hopeless = 0;
            var barren = 0;
            var ghosts = 0;
            var idle = 0;
            var loitering = 0;
            var poor = 0;
            var stalled = 0;
            var settled = 0;
            var worth = 0L;
            var pack = 0L;
            var banked = 0L;
            var pinched = 0;
            var barrenMinutes = 0.0;

            List<double> vectors = [];
            List<double> began = [];
            var risen = 0;

            foreach (var (_, watch) in _watch)
            {
                if (watch.Bot is not { Deleted: false })
                {
                    continue;
                }

                total++;
                worth += watch.Worth;
                pack += watch.Pack;
                banked += watch.Bank;

                if (watch.Pack < 400)
                {
                    pinched++;
                }
                vectors.Add(watch.Progress);
                began.Add(watch.FirstProgress);

                if (watch.Progress > watch.FirstProgress + 0.0005)
                {
                    risen++;
                }
                barrenMinutes += watch.BarrenMinutes;

                if (watch.FrozenForMs >= BotWatch.FrozenMs)
                {
                    frozen++;
                }

                if (watch.NoCloserMs >= BotWatch.FrozenMs)
                {
                    nowhere++;
                }

                if (watch.PacingMs >= BotWatch.PacedMs)
                {
                    pacing++;
                }

                if (watch.Abandoned >= 3)
                {
                    gaveUp++;
                }

                if (watch.WorkingForMs >= BotWatch.ImmortalMs)
                {
                    immortal++;
                }

                if (watch.Quick >= 4)
                {
                    bouncing++;
                }

                if (watch.Refusals >= 4)
                {
                    refused++;
                }

                if (watch.Hopeless > 0)
                {
                    hopeless++;
                }

                if (watch.BarrenMinutes >= 5.0)
                {
                    barren++;
                }

                if (watch.DeadMinutes >= 3.0)
                {
                    ghosts++;
                }

                if (watch.Kind == "-")
                {
                    idle++;

                    if (watch.IdleMs >= LoiterMs)
                    {
                        loitering++;
                    }
                }

                if (watch.Worth < 100)
                {
                    poor++;
                }

                if (watch.WatchedMs(now) >= BotWatch.SettledMs)
                {
                    settled++;

                    if (watch.Progress <= watch.FirstProgress + 0.0005)
                    {
                        stalled++;
                    }
                }
            }

            if (total == 0)
            {
                return "There is nobody to measure: the population is empty.";
            }

            vectors.Sort();
            began.Sort();

            sb.Append("I have been watching ");
            sb.Append(total);
            sb.AppendLine(" bots. Each count below is out of that number unless it says otherwise.");

            sb.Append("Frozen: ");
            sb.Append(frozen);
            sb.Append(" have stood on one tile for more than ");
            sb.Append(BotWatch.FrozenMs / 1000);
            sb.AppendLine("s while their own journey wanted them somewhere else.");

            // Counted apart from being frozen, and the pair is the point: one is a bot that cannot take a
            // step and the other is a bot taking one every beat and ending each no nearer. Merged into
            // "stuck", the second disappears — it moves, so nothing else here notices it.
            sb.Append("Getting nowhere: ");
            sb.Append(nowhere);
            sb.Append(" have been walking somewhere for more than ");
            sb.Append(BotWatch.FrozenMs / 1000);
            sb.AppendLine("s without ever getting one tile closer to it.");

            // <b>Its own row, and it is the one row here that nothing else on the shard could ever produce.</b>
            // A bot treading two tiles moves every beat, so every measure that watches for stillness reads it
            // as healthy; it often has no destination at all, so every measure that watches distance reads it
            // as healthy too. It is invisible in exactly the way that matters.
            sb.Append("Treading the same ground: ");
            sb.Append(pacing);
            sb.Append(" have spent more than ");
            sb.Append(BotWatch.PacedMs / 60000);
            sb.Append(" minutes moving about inside a patch ");
            sb.Append(BotWatch.PacingSpan);
            sb.AppendLine(" tiles across without once leaving it. This is never a healthy state.");

            sb.Append("Gave up short: ");
            sb.Append(gaveUp);
            sb.AppendLine(" have abandoned three or more errands while still further off than arriving needed.");

            sb.Append("Silent work: ");
            sb.Append(immortal);
            sb.Append(" have work in hand that has answered \"working, here\" and nothing else for more than ");
            sb.Append(BotWatch.ImmortalMs / 60000);
            sb.AppendLine(" minutes. Nothing on the shard judges that answer.");

            sb.Append("Bouncing: ");
            sb.Append(bouncing);
            sb.Append(" have dropped four or more undertakings inside ");
            sb.Append(BotWatch.QuickMs / 1000);
            sb.AppendLine("s each.");

            sb.Append("Refused roads: ");
            sb.Append(refused);
            sb.AppendLine(" have had four or more destinations proved unreachable in a row without a step between them.");

            sb.Append("Given up: ");
            sb.Append(hopeless);
            sb.AppendLine(" have had a journey decide it will never reach anything.");

            sb.Append("Nothing worth doing: ");
            sb.Append(barren);
            sb.Append(" have spent five minutes or more with no work on the shard for them; ");
            sb.Append(barrenMinutes, "F0");
            sb.AppendLine(" bot-minutes in total.");

            sb.Append("Holding nothing at all: ");
            sb.Append(idle);
            sb.Append(" this moment, and ");
            sb.Append(loitering);
            sb.Append(" of those have held no work for more than ");
            sb.Append(BotVigil.LoiterMs / 60000);
            sb.AppendLine(" minutes. A bot between jobs holds nothing for a moment; one that holds nothing for minutes is not between jobs, and nothing in the roll-call will call it stuck, because there is nothing for it to be stuck on.");

            sb.Append("Ghosts: ");
            sb.Append(ghosts);
            sb.AppendLine(" have been dead for more than three minutes and are not back on their feet.");

            // <b>Pack and bank apart, and never summed into one figure.</b> The engine pays out of the pack;
            // the bank buys nothing at all until a bot has walked to one. A single total reads as wealth
            // while the shard behaves as though it were destitute, and there is no way to tell the two apart
            // from a sum.
            sb.Append("Money in hand: ");
            sb.Append(pack);
            sb.Append(" gold in their packs between them, which is what they can actually spend; ");
            sb.Append(banked);
            sb.Append(" more sits in the bank and buys nothing until they walk to one. ");
            sb.Append(poor);
            sb.Append(" of them have under 100 gold all told, and ");
            sb.Append(pinched);
            sb.AppendLine(" have under 400 in the pack, which is the most a single piece of armour costs here.");

            sb.Append("Trade progress: lowest ");
            sb.Append(vectors[0] * 100.0, "F0");
            sb.Append("%, middle ");
            sb.Append(vectors[vectors.Count / 2] * 100.0, "F0");
            sb.Append("%, highest ");
            sb.Append(vectors[^1] * 100.0, "F0");
            sb.AppendLine("% of what their classes are aiming for.");

            Fighting(ref sb);
            Trades(ref sb);

            sb.Append("Development: of the ");
            sb.Append(settled);
            sb.Append(" I have watched for more than ");
            sb.Append(BotWatch.SettledMs / 60000);
            sb.Append(" minutes, ");
            sb.Append(stalled);
            sb.AppendLine(" have gained no ground on their trade in the last twenty minutes.");

            // <b>And the same question asked of the whole watch, because the twenty-minute figure alone reads
            // as a verdict it cannot support.</b> Skill arrives in bursts here — a bot can pay a lesson fee,
            // gain four points in one go, and then earn nothing for half an hour — so a window that catches
            // the pause between bursts reports a healthy bot as stalled. Measured 02.09.2026: "16 of 38 have
            // gained no ground" stood in the same report as a population whose middle vector had risen from
            // 74% to 82% within the hour. Both true, about different spans. The pair is the answer; either
            // one alone is a wrong one.
            sb.Append("Against the whole time I have been watching, the middle of the population has gone from ");
            sb.Append(began[began.Count / 2] * 100.0, "F0");
            sb.Append("% to ");
            sb.Append(vectors[vectors.Count / 2] * 100.0, "F0");
            sb.Append("% of what their classes are aiming for, and ");
            sb.Append(risen);
            sb.Append(" of the ");
            sb.Append(total);
            sb.AppendLine(" have risen at all since I first saw them. Read this line and the one above it together.");

            return sb.ToString();
        }
        finally
        {
            sb.Dispose();
        }
    }

    /// <summary>
    /// What the fighting is actually doing, as opposed to how much of it there is.
    ///
    /// <para>
    /// Every other count of combat on this shard counts bots that are in one. This counts bots whose blows
    /// are landing, and names the two reasons they might not be: too far for the weapon, or on the other
    /// side of a floor. The second is the one nothing else looks for and the one that costs whole parties.
    /// </para>
    /// </summary>
    private static void Fighting(ref ValueStringBuilder sb)
    {
        var fighting = 0;
        var futile = 0;
        var far = 0;
        var above = 0;
        var beside = 0;

        foreach (var (_, watch) in _watch)
        {
            if (watch.Bot is not { Deleted: false } || watch.Foe == null)
            {
                continue;
            }

            fighting++;

            if (watch.SwingingMs < BotWatch.FutileMs)
            {
                continue;
            }

            futile++;

            if (watch.Overhead)
            {
                above++;
            }
            else if (watch.BeyondReach)
            {
                far++;
            }
            else
            {
                beside++;
            }
        }

        sb.Append("Fighting: ");
        sb.Append(fighting);
        sb.Append(" have something to fight this moment, and ");
        sb.Append(futile);
        sb.Append(" of those have been at it more than ");
        sb.Append(BotWatch.FutileMs / 1000);
        sb.AppendLine("s without their target's health falling once.");

        sb.Append("Of those ");
        sb.Append(futile);
        sb.Append(": ");
        sb.Append(above);
        sb.Append(" are more than ");
        sb.Append(BotArrival.PersonHeight);
        sb.Append(" units of height from their target, which is a creature on a roof or an upper floor and cannot be hit at all; ");
        sb.Append(far);
        sb.Append(" are further off than their own weapon reaches; ");
        sb.Append(beside);
        sb.AppendLine(" are beside it, in reach, and still landing nothing.");
    }

    /// <summary>
    /// What each trade has actually come to, which is a different question from how often it is chosen.
    ///
    /// <para>
    /// <b>The gold is the column that matters and it is the one nothing else on the shard prints.</b> A
    /// trade taken forty times reads as a busy trade; the same forty with nothing earned reads as a defect,
    /// and negative reads as a trade that costs the population money every time somebody picks it. Crafting
    /// and the market are exactly where that goes wrong — buy cloth, sew, never sell — and every count of
    /// attempts in the world will call it healthy.
    /// </para>
    /// </summary>
    private static void Trades(ref ValueStringBuilder sb)
    {
        Dictionary<string, (int Taken, int Quick, long HeldMs, int Gained, int Made, int Learned, int Bots)> tally = [];

        foreach (var (_, watch) in _watch)
        {
            if (watch.Bot is { Deleted: false })
            {
                watch.Tally(tally);
            }
        }

        if (tally.Count == 0)
        {
            sb.AppendLine("Trades: nothing has been taken up and let go yet, so no trade has a result.");

            return;
        }

        // <b>The two figures are labelled in capitals and separated by bars, and that is not decoration.</b>
        // Written as prose — "and -3897 gold and 718 worth of goods between them all told" — the second
        // figure was read straight past: on 02.09.2026 the debugger reported "acquire shows -3897 gold and no
        // goods, bots are buying without producing, draining the economy" about a row whose goods column read
        // 718. The column had been added a day earlier for exactly that mistake and it did not help, because
        // a number a sentence has to be parsed to find is a number that gets missed.
        //
        // <b>And GOLD is not what the trade earned.</b> It is how much the bot's purse and bank moved over
        // the wall-clock it held that work, sampled every two seconds — so a short piece of work that begins
        // and ends between two samples has its spending charged to whichever trade the sampler saw on either
        // side of it. Measured: prowl showed -1116 gold over 78 goes while the shard's own settlement said
        // "0 coin, 0 made" for every single one of them. The shard is right and this column is smeared. It is
        // still worth having — a trade that is genuinely bleeding shows here first — but it is evidence, not
        // a verdict, and the shard's own per-deed figure in the session log is what settles an argument.
        sb.AppendLine(
            "What each trade has come to since I began watching. TWO SEPARATE FIGURES PER ROW, NEVER ADDED"
            + " TOGETHER AND NEVER READ AS ONE. Read BOTH before saying anything about a trade.\n"
            + "  GOLD = how much the bot's purse and bank moved while it held that work. A trade whose whole"
            + " purpose is buying — restock, acquire, buying materials to craft with — is SUPPOSED to be"
            + " negative here, because that is what buying is. This figure is also SMEARED: it is sampled"
            + " every two seconds, so a short piece of work that begins and ends between two samples has its"
            + " spending charged to its neighbour. Do not call a trade a drain on this figure alone.\n"
            + "  GOODS = what the work itself says it made. This is the figure that says whether anything was"
            + " produced.\n"
            + "  SKILL = points of skill the bot gained while holding that work. THIS IS THE COLUMN THAT"
            + " ANSWERS WHETHER THIS POPULATION IS BECOMING ANYTHING, and it is the one nothing else on the"
            + " shard reports. A trade taken hundreds of times with nothing in this column is where the"
            + " population's hours are going without buying anything.\n"
            + "  The row worth looking at is NEGATIVE GOLD WITH ZERO GOODS AND ZERO SKILL, sustained over"
            + " many takings."
        );

        foreach (var (kind, row) in tally)
        {
            sb.Append("- ");
            sb.Append(kind);
            sb.Append(": taken ");
            sb.Append(row.Taken);
            sb.Append(" times by ");
            sb.Append(row.Bots);
            sb.Append(row.Bots == 1 ? " bot, " : " bots, ");
            sb.Append(row.Quick);
            sb.Append(" of those over inside ");
            sb.Append(BotWatch.QuickMs / 1000);
            sb.Append("s, ");
            sb.Append(row.HeldMs / Math.Max(1, row.Taken) / 1000);
            sb.Append("s a go on average, and ");
            sb.Append(" | GOLD ");
            sb.Append(row.Gained);
            sb.Append(" | GOODS ");
            sb.Append(row.Made);
            sb.Append(" | SKILL ");
            sb.Append(row.Learned / 10.0, "F1");

            // <b>The row says what it is, because saying it in the header three times did not work.</b> The
            // reading that has to be made here is arithmetic — negative coin beside positive goods is a
            // purchase, and a purchase is supposed to look like that — and it is a reading the model got
            // wrong three windows running on the same trade. On 02.09.2026 at 12:59 it wrote
            // "GOLD -3081 | GOODS 702 | SKILL 0.9" into its own evidence and concluded, in the same
            // paragraph, "negative gold, zero goods, zero skill ... unproductive and drains resources". It
            // quoted the number and denied it in the next clause.
            //
            // A column was added for this, then labelled in capitals, then explained in the header. All
            // three were sentences, and a sentence can be reinterpreted. This is not: the verdict any
            // arithmetic can reach is reached here, in the row, and what is left for the model is the part
            // only it can do.
            sb.AppendLine(Verdict(row.Gained, row.Made, row.Learned, row.Taken));
        }
    }

    /// <summary>
    /// What a row of the trade table amounts to, decided by arithmetic rather than left to be inferred.
    ///
    /// <para>
    /// Only the calls that cannot be got wrong are made here. Whether a trade is worth having, whether its
    /// rate is good, whether the shard should offer it less — none of that is arithmetic and none of it is
    /// decided in this method. What is decided is the one thing that kept being misread: coin going down
    /// while goods go up is a purchase working, not an economy bleeding.
    /// </para>
    /// </summary>
    private static string Verdict(int gold, int goods, int skill, int taken) =>
        gold < 0 && (goods > 0 || skill > 0)
            ? " — A PURCHASE OR A CRAFT WORKING AS INTENDED: coin was turned into goods or skill. This row is not a drain."
            : gold <= 0 && goods <= 0 && skill <= 0 && taken >= 10
                // <b>Stated as an ambiguity, because the three columns genuinely cannot resolve it.</b> An
                // hour after this verdict was written it fired on `unload` — 37 takings by 32 bots, nothing
                // in any column — and unload's whole job is carrying gold to the bank, which moves value
                // without creating any and is measured here as nought by construction. Calling that "the
                // shape worth looking at" was a false alarm produced by the very line meant to prevent them.
                // Work that moves value and work that wastes time look identical from these three numbers,
                // and the honest thing is to say so rather than to pick one.
                ? " — NOTHING GAINED IN ANY COLUMN over many takings. That is EITHER wasted time OR work whose"
                  + " job is to move value rather than make it (banking, carrying, posting an order, walking"
                  + " somewhere to be ready). These three columns cannot tell those apart — say which you"
                  + " think it is and why, or say you cannot tell."
                : gold > 0 || goods > 0 || skill > 0
                    ? " — this row gained something."
                    : " — too few takings to say anything yet.";

    /// <summary>The worst bots, worst first, in full.</summary>
    private static List<string> Suspects(long now)
    {
        List<BotWatch> sorted = [];

        foreach (var (_, watch) in _watch)
        {
            if (watch.Bot is { Deleted: false })
            {
                sorted.Add(watch);
            }
        }

        sorted.Sort((a, b) => b.Suspicion.CompareTo(a.Suspicion));

        List<string> rows = [];

        for (var i = 0; i < sorted.Count && rows.Count < Rows; i++)
        {
            // A row for anybody with a symptom, and if nobody has one, rows anyway: the report has to be
            // able to show a healthy population, or "nothing is wrong" is an answer the model can never
            // support with anything.
            rows.Add(sorted[i].Row(now));
        }

        return rows;
    }

    /// <summary>
    /// Every line the shard writes about itself, gathered from the subsystems by asking them.
    ///
    /// <para>
    /// <b>Found by reflection rather than listed here, and the reason is the same one that keeps the minds'
    /// menu off a hard-written list.</b> A list of subsystems in this file would be right on the day it was
    /// written and silently short by one the first time somebody adds a folder — and a debugger that has
    /// never heard of a subsystem will never find a defect in it, while looking exactly like a debugger that
    /// checked. Every subsystem here already writes one line about itself for the boot log and the census;
    /// this asks all of them at once.
    /// </para>
    ///
    /// <para>
    /// Wrapped one at a time: a summary that throws is a subsystem with a defect in its own instrumentation,
    /// which is worth a line in the log and is not worth losing the other forty for.
    /// </para>
    /// </summary>
    private static string Subsystems(int budget)
    {
        _describers ??= Gather();

        var sb = ValueStringBuilder.Create(4096);

        try
        {
            var spent = 0;

            for (var i = 0; i < _describers.Length && spent < budget; i++)
            {
                var method = _describers[i];
                string said;

                try
                {
                    said = method.Invoke(null, null) as string;
                }
                catch (Exception e)
                {
                    said = $"could not say ({e.InnerException?.Message ?? e.Message})";
                }

                if (string.IsNullOrWhiteSpace(said))
                {
                    continue;
                }

                var name = method.DeclaringType?.Name ?? "something";

                sb.Append("- ");
                sb.Append(name.StartsWith("Bot", StringComparison.Ordinal) ? name[3..] : name);
                sb.Append(": ");
                sb.AppendLine(said);

                spent += said.Length + name.Length + 4;
            }

            return sb.ToString();
        }
        finally
        {
            sb.Dispose();
        }
    }

    private static MethodInfo[] Gather()
    {
        List<MethodInfo> found = [];

        try
        {
            var types = typeof(BotCore).Assembly.GetTypes();

            for (var i = 0; i < types.Length; i++)
            {
                var method = types[i].GetMethod("Describe", BindingFlags.Static | BindingFlags.Public, Type.EmptyTypes);

                if (method?.ReturnType == typeof(string) && !Skipped(types[i].Name))
                {
                    found.Add(method);
                }
            }
        }
        catch (Exception e)
        {
            logger.Warning("The debugger could not gather the subsystems' own summaries: {Message}", e.Message);
        }

        found.Sort((a, b) => string.CompareOrdinal(a.DeclaringType?.Name, b.DeclaringType?.Name));

        return [.. found];
    }

    /// <summary>The two that are already quoted in the census, said twice for no benefit.</summary>
    private static bool Skipped(string type) =>
        type is "BotWill" or "BotPopulation";

    /// <summary>One line about what the debugger has done, for the shard's own log.</summary>
    public static string Describe() =>
        Body is not { Deleted: false }
            ? "the debugger has no body"
            : $"{Name} at {Body.Location.X},{Body.Location.Y} after {Body.Hops} hops, watching {_watch.Count} bots; "
              + $"{Asked} questions asked, {Findings} findings, {Quiet} looks that found nothing, {Reflections} reflections; "
              + BotDebugMemory.Describe() + "; "
              + BotAudit.Describe();

    private sealed class VigilTimer : Timer
    {
        public VigilTimer(TimeSpan interval) : base(interval, interval)
        {
        }

        protected override void OnTick() => Update();
    }
}
