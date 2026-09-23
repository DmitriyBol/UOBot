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

    public static int SampleMs { get; set; } = 2000;

    public static int HoverMs { get; set; } = 20000;

    public static int ReportMs { get; set; } = 600000;

    public static int ReflectMs { get; set; } = 1800000;

    public static string Model { get; set; } = "deepseek-r1:14b";

    public static string KeepAlive { get; set; } = "10s";

    public static int TimeoutMs { get; set; } = 420000;

    public static int LoiterMs { get; set; } = 180000;

    public static int Rows { get; set; } = 6;

    public static int Recall { get; set; } = 6;

    public static int SubsystemBudget { get; set; } = 1800;

    public static int MostChars { get; set; } = 9000;

    public static string Name { get; set; } = "Argus";

    public static string[] Helpers { get; set; } = ["Lynceus", "Heimdall"];

    public static int[] Hues { get; set; } = [33, 99, 63];

    public static readonly List<BotWatcher> Squad = [];

    public static BotWatcher Lead => Squad.Count > 0 ? Squad[0] : null;

    private static int _reflectTurn;

    private static readonly Dictionary<Serial, BotWatch> _watch = [];

    private static readonly Dictionary<string, int> _rungs = [];

    private static readonly Dictionary<string, int> _holding = [];

    private static MethodInfo[] _describers;

    private static Timer _timer;

    private static long _sampledTick;

    private static long _hoveredTick;

    private static long _reflectedTick;

    private static long _wokeTick;

    private static bool _asking;

    public static BotDebugger Body => Lead?.Body;

    public static BotDebugNote Last => Lead?.Last;

    public static long Asked
    {
        get
        {
            var n = 0L;

            for (var i = 0; i < Squad.Count; i++)
            {
                n += Squad[i].Asked;
            }

            return n;
        }
    }

    public static long Findings
    {
        get
        {
            var n = 0L;

            for (var i = 0; i < Squad.Count; i++)
            {
                n += Squad[i].Findings;
            }

            return n;
        }
    }

    public static long Quiet
    {
        get
        {
            var n = 0L;

            for (var i = 0; i < Squad.Count; i++)
            {
                n += Squad[i].Quiet;
            }

            return n;
        }
    }

    public static long Reflections
    {
        get
        {
            var n = 0L;

            for (var i = 0; i < Squad.Count; i++)
            {
                n += Squad[i].Reflections;
            }

            return n;
        }
    }

    public static BotWatcher Called(string name)
    {
        for (var i = 0; i < Squad.Count; i++)
        {
            if (string.Equals(Squad[i].Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return Squad[i];
            }
        }

        return null;
    }

    public static IReadOnlyList<(string Name, Map Map, Point3D At)> Standing()
    {
        List<(string, Map, Point3D)> rows = [];

        for (var i = 0; i < Squad.Count; i++)
        {
            var body = Squad[i].Body;

            if (body is { Deleted: false } && body.Map != null && body.Map != Map.Internal)
            {
                rows.Add((Squad[i].Name, body.Map, body.Location));
            }
        }

        return rows;
    }

    public static bool Running => _timer != null;

    public static void Start()
    {
        Stop();

        _watch.Clear();

        BotAudit.Reset();

        _asking = false;
        _reflectTurn = 0;

        var now = Core.TickCount;

        _wokeTick = now;
        _sampledTick = now;
        _hoveredTick = now;
        _reflectedTick = now;

        BotTourney.Load();

        Squad.Clear();
        Squad.Add(new BotWatcher(Name, Hues.Length > 0 ? Hues[0] : BotDebugger.RobeHue, 0));

        for (var i = 0; i < Helpers.Length; i++)
        {
            if (!string.IsNullOrWhiteSpace(Helpers[i]))
            {
                Squad.Add(new BotWatcher(Helpers[i].Trim(), Hues.Length > i + 1 ? Hues[i + 1] : BotDebugger.RobeHue, i + 1));
            }
        }

        BotMarshal.Standing = null;

        if (BotMarshal.Running && !string.IsNullOrWhiteSpace(BotMarshal.Name))
        {
            var marshal = new BotWatcher(BotMarshal.Name.Trim(), BotMarshal.Hue, Squad.Count) { Organiser = true };

            Squad.Add(marshal);
            BotMarshal.Standing = marshal;
            BotMarshal.Woke(now);
        }

        for (var i = 0; i < Squad.Count; i++)
        {
            Squad[i].ReportedTick = now - ReportMs * i / Math.Max(1, Squad.Count);
        }

        Purge();

        for (var i = 0; i < Squad.Count; i++)
        {
            Embody(Squad[i]);
        }

        _timer = new VigilTimer(TimeSpan.FromMilliseconds(Math.Max(250, SampleMs)));
        _timer.Start();
    }

    public static void Stop()
    {
        _timer?.Stop();
        _timer = null;
    }

    public static void Reset()
    {
        Stop();

        for (var i = 0; i < Squad.Count; i++)
        {
            Squad[i].Body?.Delete();
            Squad[i].Body = null;
        }

        _watch.Clear();
    }

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

    private static void Embody(BotWatcher w)
    {
        var map = BotPopulation.Home;

        if (map == null || map == Map.Internal)
        {
            logger.Error("The debugger has nowhere to stand: the population has no home map, so it was not raised");

            return;
        }

        w.Body = new BotDebugger();
        w.Body.Awaken(w.Name, w.Hue);
        w.Body.Hover(map, BotPopulation.Where);

        logger.Information(
            "{Name} the watcher is awake at {Where} on {Map} in a robe of hue {Hue}, invisible to everyone below {Rank}, charged with {Charge}; it writes to {Log}",
            w.Name,
            BotPopulation.Where,
            map,
            w.Hue,
            BotDebugger.SeenBy,
            w.Charge,
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

        for (var i = 0; i < Squad.Count; i++)
        {
            if (Squad[i].Body is not { Deleted: false })
            {
                Embody(Squad[i]);
            }
        }

        if (Lead?.Body == null)
        {
            return;
        }

        Sample(now, since);

        BotConsole.Listen(now);

        BotTourney.Beat(now);

        if (BotAudit.Due(now))
        {
            BotAudit.Sweep(now, Rollcall());
        }

        BotHalls.Beat(BotPopulation.Home, now);

        if (now - _hoveredTick >= HoverMs)
        {
            _hoveredTick = now;

            for (var i = 0; i < Squad.Count; i++)
            {
                var w = Squad[i];

                if (i == 0 && !BotHalls.Charted && BotHalls.Probing != Point3D.Zero && w.Body is { Deleted: false })
                {
                    w.Body.Hover(BotPopulation.Home, BotHalls.Probing);
                }
                else
                {
                    Follow(w);
                }
            }
        }

        BotRevel.Settle();

        BotWaves.Muster();

        BotRevel.Tax();

        if (_asking || !BotOllama.Free || World.Saving)
        {
            return;
        }

        if (_pending != null)
        {
            var question = _pending;
            var reply = _pendingReply;

            _pending = null;
            _pendingReply = null;

            Consider(question, reply);

            return;
        }

        if (BotMarshal.Standing != null)
        {
            if (BotMarshal.Due(now))
            {
                Organise(now);

                return;
            }
        }
        else if (BotRevel.Due())
        {
            Revel();

            return;
        }

        if (now - _reflectedTick >= ReflectMs && Squad.Count > 0)
        {
            _reflectedTick = now;

            var thinker = Squad[_reflectTurn % Squad.Count];

            _reflectTurn++;

            if (thinker.Organiser && Squad.Count > 1)
            {
                thinker = Squad[_reflectTurn % Squad.Count];
                _reflectTurn++;
            }
            thinker.ReportedTick = now;

            Reflect(thinker, now);

            return;
        }

        BotWatcher due = null;

        for (var i = 0; i < Squad.Count; i++)
        {
            var w = Squad[i];

            if (!w.Organiser && now - w.ReportedTick >= ReportMs && (due == null || w.ReportedTick < due.ReportedTick))
            {
                due = w;
            }
        }

        if (due != null)
        {
            var waited = now - due.ReportedTick;

            due.ReportedTick = now;

            Look(due, now, waited);
        }
    }

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

    private static void Follow(BotWatcher w)
    {
        BotWatch pick = null;

        if (w.Wanted != null)
        {
            foreach (var (_, watch) in _watch)
            {
                if (watch.Bot is { Deleted: false } && string.Equals(watch.Name, w.Wanted, StringComparison.OrdinalIgnoreCase))
                {
                    pick = watch;
                    w.Because = "you asked to watch this one";

                    break;
                }
            }
        }

        w.Wanted = null;

        if (pick == null)
        {
            List<BotWatch> sorted = [];

            foreach (var (_, watch) in _watch)
            {
                if (watch.Bot is { Deleted: false })
                {
                    sorted.Add(watch);
                }
            }

            if (sorted.Count == 0)
            {
                return;
            }

            sorted.Sort((a, b) => b.Suspicion.CompareTo(a.Suspicion));

            var slot = Math.Min(w.Rank, sorted.Count - 1);

            if (sorted[slot].Suspicion > 0.0)
            {
                pick = sorted[slot];
                w.Because = string.IsNullOrWhiteSpace(pick.Symptoms)
                    ? "it was among the worst of an untroubled population"
                    : pick.Symptoms;
            }
            else
            {
                sorted.Sort((a, b) => a.Progress.CompareTo(b.Progress));
                pick = sorted[slot];
                w.Because = $"nobody has a symptom, so I went to one of the least developed of them at {pick.Progress:P0}";
            }
        }

        var bot = pick?.Bot;

        if (bot is not { Deleted: false } || bot.Map == null || bot.Map == Map.Internal || w.Body is not { Deleted: false })
        {
            return;
        }

        w.Body.Hover(bot.Map, bot.Location);
    }

    private static void Revel()
    {
        _asking = true;

        var report = BotDebugSight.Report(
            Beside(Lead),
            Census(),
            "",
            [],
            Subsystems(null, SubsystemBudget),
            "",
            0
        );

        BotOllama.Ask(
            BotRevel.System(Name),
            Bounded(report),
            BotRevel.Schema,
            true,
            (json, waited) => Revelled(json, waited),
            Model,
            KeepAlive,
            TimeoutMs
        );
    }

    private static void Revelled(string json, long waited)
    {
        _asking = false;

        var plan = BotRevelPlan.Read(json);

        if (plan == null)
        {
            logger.Warning("The watcher was asked for a revel and said nothing that could be read");

            return;
        }

        if (string.Equals(plan.Kind, "nothing", StringComparison.OrdinalIgnoreCase))
        {
            BotDebugLog.Write($"no revel this time ({waited}ms): {plan.Why}");

            return;
        }

        if (!BotRevel.Known(plan.Kind))
        {
            logger.Warning(
                "The watcher wanted a revel for {Kind}, which is not work anybody takes; nothing is declared",
                plan.Kind
            );

            return;
        }

        var said = BotRevel.Declare(plan.Kind, plan.Prize, plan.Say, plan.Why, plan.Camp, plan.Spot);

        BotDebugLog.Write($"revel after {waited}ms — {said}");
    }

    private static void Organise(long now)
    {
        var marshal = BotMarshal.Standing;

        if (marshal == null)
        {
            return;
        }

        BotMarshal.Woke(now);
        _asking = true;
        marshal.Asked++;

        var report = BotDebugSight.Report(
            Beside(marshal),
            Census(),
            "",
            [],
            Subsystems(null, SubsystemBudget),
            "",
            0
        );

        var sight = BotMarshal.Sight();
        var room = Math.Max(MostChars / 3, MostChars - sight.Length - 2);
        var question = Bounded(report, room) + "\n\n" + sight;

        _marshalQuestion = question;

        BotDebugLog.Write(
            $"{marshal.Name} is asked in {question.Length} characters: the report {report.Length}{(report.Length > room ? $", cut to {room}" : "")}, its own sight {sight.Length}"
        );

        BotOllama.Ask(
            BotMarshal.System(marshal.Name),
            question,
            BotMarshal.Schema,
            true,
            (json, waited) => Organised(json, waited),
            Model,
            KeepAlive,
            TimeoutMs
        );
    }

    private static string _marshalQuestion;

    public static int UnansweredWritten { get; private set; }

    public static int MostUnansweredWritten { get; set; } = 3;

    private static void Organised(string json, long waited)
    {
        _asking = false;

        var plan = BotMarshalPlan.Read(json);
        var said = BotMarshal.Act(plan, BotPopulation.Home);

        if (said == null)
        {
            logger.Warning("The marshal was asked for an event and said nothing that could be read");

            if (UnansweredWritten < MostUnansweredWritten && _marshalQuestion != null)
            {
                UnansweredWritten++;
                BotDebugLog.Write($"{BotMarshal.Name}'s question that came to nothing, whole ({_marshalQuestion.Length} characters):\n{_marshalQuestion}");
            }

            return;
        }

        BotDebugLog.Write($"{BotMarshal.Name} after {waited}ms — {said}");

        var kind = (plan.Event ?? "nothing").Trim().ToLowerInvariant();

        if (plan.Say is { Length: > 0 } say && kind is not ("nothing" or "revel" or "camp"))
        {
            BotMarshal.Standing?.Body?.Say(say.Length > 180 ? say[..180] : say);
        }
    }

    private static void Look(BotWatcher w, long now, long waited)
    {
        var roster = Roster();

        if (roster.Count == 0)
        {
            return;
        }

        _asking = true;
        w.Asked++;

        var asked = DateTime.Now;

        var mine = Brief(w)
                   + Measured(now)
                   + "\nTHE LAST ROLL-CALL, TWO MINUTES OF IT, ASKED OF EVERY BOT\n"
                   + BotAudit.Last
                   + "\nWhat the roll-calls have done all session: "
                   + BotAudit.Describe()
                   + ".\nWhat the squad's hands have done all session: "
                   + BotHand.Describe()
                   + ".";

        var report = BotDebugSight.Report(
            Beside(w),
            Census(),
            mine,
            Suspects(w, now),
            Subsystems(w, SubsystemBudget),
            BotDebugSight.Recite(w.Last),
            waited
        );

        BotOllama.Ask(
            BotDebugSight.System(w.Name),
            Bounded(report),
            BotDebugNote.Schema(roster),
            true,
            (json, waited) => Answered(w, json, waited, report, mine, asked),
            Model,
            KeepAlive,
            TimeoutMs
        );
    }

    private static string Brief(BotWatcher w)
    {
        var sb = ValueStringBuilder.Create(1024);

        try
        {
            sb.Append("YOUR CHARGE, ");
            sb.Append(w.Name);
            sb.Append(": you are one of ");
            sb.Append(Squad.Count);
            sb.Append(" watchers, and yours is ");
            sb.Append(w.Charge);
            sb.AppendLine(". Look there first and hardest; the rest is context. The subsystem lines below are the ones under your charge.");

            var any = false;

            for (var i = 0; i < Squad.Count; i++)
            {
                var other = Squad[i];

                if (ReferenceEquals(other, w) || other.Last == null
                    || string.Equals(other.Last.Kind, "nothing", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!any)
                {
                    sb.AppendLine("WHAT YOUR SQUAD-MATES CLAIMED AT THEIR LAST LOOK. These are conjectures, not facts: check each against your own numbers below and say whether you agree, with the number that decides it. Do not repeat a claim you cannot support from your own charge.");
                    any = true;
                }

                sb.Append("- ");
                sb.Append(other.Name);
                sb.Append(", charged with ");
                sb.Append(other.Charge);
                sb.Append(": ");
                sb.AppendLine(BotDebugSight.Recite(other.Last));
            }

            sb.AppendLine("");

            return sb.ToString();
        }
        finally
        {
            sb.Dispose();
        }
    }

    public static long Labels { get; private set; }

    public static long Echoes { get; private set; }

    private static string Echoed(BotWatcher w, BotDebugNote note)
    {
        var said = Flat(note.Finding);

        for (var i = 0; i < Squad.Count; i++)
        {
            var other = Squad[i];
            var last = other.Last;

            if (last == null || string.Equals(last.Kind, "nothing", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (string.Equals(Flat(last.Finding), said, StringComparison.Ordinal))
            {
                return ReferenceEquals(other, w) ? "its own" : $"{other.Name}'s";
            }
        }

        return null;
    }

    private static string Flat(string text) =>
        string.IsNullOrWhiteSpace(text)
            ? ""
            : string.Join(' ', text.ToLowerInvariant().Split((char[])null, StringSplitOptions.RemoveEmptyEntries));

    private static void Answered(BotWatcher w, string json, long waited, string report, string mine, DateTime asked)
    {
        _asking = false;

        var note = BotDebugNote.Read(json);

        if (note == null)
        {
            logger.Warning("{Name} looked at the population and the model said nothing that could be read", w.Name);

            return;
        }

        if (!string.Equals(note.Kind, "nothing", StringComparison.OrdinalIgnoreCase))
        {
            if (BotDebugMemory.Label(note.Finding))
            {
                w.Labels++;
                Labels++;

                BotDebugLog.Write($"{w.Name}: a label rather than a finding, not filed — [{note.Kind}] about {note.Bot}: {note.Finding}");
                logger.Information(
                    "{Name} answered with a label rather than a sentence about {Who}, and it is not filed",
                    w.Name,
                    note.Bot
                );

                return;
            }

            var whose = Echoed(w, note);

            if (whose != null)
            {
                w.Echoes++;
                Echoes++;

                BotDebugLog.Write($"{w.Name}: repeated {whose} last claim about {note.Bot} word for word, not filed — {note.Finding}");
                logger.Information(
                    "{Name} repeated {Whose} last claim about {Who} word for word, and it is not filed",
                    w.Name,
                    whose,
                    note.Bot
                );

                return;
            }
        }

        w.Last = note;

        if (string.Equals(note.Kind, "nothing", StringComparison.OrdinalIgnoreCase))
        {
            w.Quiet++;

            BotDebugLog.Write($"{w.Name}: nothing worth reporting — measured at {asked:HH:mm:ss}, answered {waited}ms later — {note.Finding}");

            BotDebugLog.Block("  the counts it said that about:", mine);

            if (!string.IsNullOrWhiteSpace(note.Watch) && note.Watch != "-")
            {
                w.Wanted = note.Watch;
            }

            Reach(w, note);

            return;
        }

        w.Findings++;

        BotDebugLog.Rule();
        BotDebugLog.Write(
            $"FINDING {Findings} by {w.Name} — {note.Kind}, about {note.Bot}, {note.Confidence:P0} sure. "
            + $"Everything below was measured at {asked:HH:mm:ss}; the answer came {waited}ms later"
        );
        BotDebugLog.Block("  claim:", note.Finding);
        BotDebugLog.Block("  evidence it quoted:", note.Evidence);
        BotDebugLog.Block("  what it thinks is behind it (CONJECTURE):", note.Cause);
        BotDebugLog.Block("  change it suggests (CONJECTURE):", note.Fix);
        BotDebugLog.Block("  its last claim:", note.Last);

        BotDebugLog.Block("  measured, and this is what it was reasoning from:", report);
        BotDebugLog.Rule();

        Remember(w, BotDebugSight.Recite(note));

        BotDebugMemory.Believe(note);

        if (string.Equals(note.Last, "gone", StringComparison.OrdinalIgnoreCase) && w.Last != null)
        {
            BotDebugMemory.Doubt(w.Last.Finding);
        }

        if (!string.IsNullOrWhiteSpace(note.Watch) && note.Watch != "-")
        {
            w.Wanted = note.Watch;
        }

        Reach(w, note);

        logger.Information(
            "{Name} has a finding ({Kind}, {Sure:P0}) about {Who}: {What}",
            w.Name,
            note.Kind,
            note.Confidence,
            note.Bot,
            note.Finding
        );

        BotAlarm.Note(
            "finding",
            $"CONJECTURE from {w.Name} ({note.Kind}, {(int)Math.Round(note.Confidence * 100)}% sure) about"
            + $" {note.Bot}: {note.Finding}",
            Findings,
            0,
            "-"
        );
    }

    private static void Reach(BotWatcher w, BotDebugNote note)
    {
        if (note == null)
        {
            return;
        }

        var answer = BotHand.Run(w.Name, note.Probe, note.At, note.Finding);

        if (answer == null)
        {
            return;
        }

        BotDebugLog.Block($"  {w.Name} used its hands — {note.Probe} {note.At}:", answer);

        Remember(w, $"{note.Probe} {note.At} -> {answer}");
    }

    private static void Reflect(BotWatcher w, long now)
    {
        _asking = true;
        w.Asked++;

        var asked = DateTime.Now;

        var question = BotDebugSight.Reflection(
            Census(),
            Brief(w) + Measured(now),
            Subsystems(w, SubsystemBudget * 2),
            w.Found,
            now - _wokeTick
        );

        BotOllama.Ask(
            BotDebugSight.System(w.Name),
            Bounded(question),
            BotDebugThought.Schema,
            true,
            (json, waited) => Thought(w, json, waited, question, asked),
            Model,
            KeepAlive,
            TimeoutMs
        );
    }

    private static void Thought(BotWatcher w, string json, long waited, string question, DateTime asked)
    {
        _asking = false;

        var thought = BotDebugThought.Read(json);

        if (thought == null)
        {
            logger.Warning("{Name} thought about the shard for a while and the answer could not be read", w.Name);

            return;
        }

        w.Reflections++;

        BotDebugLog.Rule();
        BotDebugLog.Write(
            $"REFLECTION {Reflections} by {w.Name} — measured at {asked:HH:mm:ss}, thought for {waited / 1000}s, {thought.Confidence:P0} sure"
        );
        BotDebugLog.Block("  what most blocks these bots (CONJECTURE):", thought.Blocking);
        BotDebugLog.Block("  evidence:", thought.Evidence);
        BotDebugLog.Block("  change to make:", thought.Change);
        BotDebugLog.Block("  second most likely:", thought.Second);
        BotDebugLog.Block("  what would show this to be wrong:", thought.Wrong);
        BotDebugLog.Block("  everything it was given:", question);
        BotDebugLog.Rule();

        Remember(w, $"[reflection] {thought.Blocking} — change: {thought.Change}");

        BotDebugMemory.Learn($"{thought.Blocking} (the change worth making: {thought.Change})");

        logger.Information("{Name} has thought about the shard: {What}", w.Name, thought.Blocking);
    }

    private static void Remember(BotWatcher w, string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return;
        }

        w.Found.Add(line);

        while (w.Found.Count > Recall)
        {
            w.Found.RemoveAt(0);
        }
    }

    private static string Bounded(string question) => Bounded(question, MostChars);

    private static string Bounded(string question, int most)
    {
        if (question == null || question.Length <= most)
        {
            return question;
        }

        var cut = question.Length - most;

        return string.Concat(
            question.AsSpan(0, most),
            $"\n\n[{cut} characters of this report were cut to leave you room to think. What was cut came from"
            + " the end: the older findings and the subsystem summaries. Everything above is complete.]"
        );
    }

    public static string Digest() =>
        Census() + "\n" + Measured(Core.TickCount) + "\nTHE LAST ROLL-CALL\n" + BotAudit.Last + Tampered();

    private static string Tampered()
    {
        var note = BotDials.Note();

        return note == null
            ? ""
            : $"\n\nCHANGED BY HAND SINCE THE SHARD CAME UP — these are deliberate, not faults: {note}";
    }

    public static string Row(string name)
    {
        if (!string.IsNullOrWhiteSpace(name))
        {
            var asked = name.Trim();

            if (asked.StartsWith("- ", StringComparison.Ordinal))
            {
                asked = asked[2..].Trim();
            }

            var joint = asked.IndexOf(" the ", StringComparison.OrdinalIgnoreCase);
            var bare = joint > 0 ? asked[..joint].Trim() : asked;

            foreach (var (_, watch) in _watch)
            {
                if (watch.Bot is { Deleted: false }
                    && (string.Equals(watch.Name, asked, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(watch.Name, bare, StringComparison.OrdinalIgnoreCase)))
                {
                    return watch.Row(Core.TickCount);
                }
            }
        }

        var roster = Roster();

        return $"No bot called \"{name}\". There are {roster.Count}: {string.Join(", ", roster)}";
    }

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
                sb.Append("%.");

                if (!string.IsNullOrWhiteSpace(watch.Empty))
                {
                    sb.Append(" ");
                    sb.Append(watch.Empty);
                    sb.Append(".");
                }

                sb.AppendLine("");
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

    private static string _pending;

    private static Action<string> _pendingReply;

    public static long Held { get; private set; }

    public static bool Consider(string question, Action<string> reply)
    {
        if (string.IsNullOrWhiteSpace(question) || reply == null)
        {
            return false;
        }

        if (_asking || !BotOllama.Free || World.Saving)
        {
            _pending = question;
            _pendingReply = reply;
            Held++;

            reply("the model is busy; the question is held and goes first the moment it frees.");

            return true;
        }

        _asking = true;

        if (Lead != null)
        {
            Lead.Asked++;
        }

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

    private static string Beside(BotWatcher w)
    {
        var body = w?.Body;

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
            ? $"{where}. Nobody is on this tile. I moved here because {w.Because}."
            : $"{where}, on the same tile as {here.Name} the {here.Class}. I came here because {w.Because}.";
    }

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

            sb.Append("Getting nowhere: ");
            sb.Append(nowhere);
            sb.Append(" have been walking somewhere for more than ");
            sb.Append(BotWatch.FrozenMs / 1000);
            sb.AppendLine("s without ever getting one tile closer to it.");

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

            sb.AppendLine(Verdict(row.Gained, row.Made, row.Learned, row.Taken, BotAppraisal.IsUnpaid(kind)));
        }
    }

    private static string Verdict(int gold, int goods, int skill, int taken, bool unpaid) =>
        unpaid
            ? " — PAID NOTHING ON PURPOSE: a guild duty (a war, a claim, a hall, a hire). The shard does not judge"
              + " it on gold and neither should you; its GOLD is only what the bot happened to spend meanwhile."
              + " This row is not a drain."
            : gold < 0 && (goods > 0 || skill > 0)
            ? " — A PURCHASE OR A CRAFT WORKING AS INTENDED: coin was turned into goods or skill. This row is not a drain."
            : gold <= 0 && goods <= 0 && skill <= 0 && taken >= 10
                ? " — NOTHING GAINED IN ANY COLUMN over many takings. That is EITHER wasted time OR work whose"
                  + " job is to move value rather than make it (banking, carrying, posting an order, walking"
                  + " somewhere to be ready). These three columns cannot tell those apart — say which you"
                  + " think it is and why, or say you cannot tell."
                : gold > 0 || goods > 0 || skill > 0
                    ? " — this row gained something."
                    : " — too few takings to say anything yet.";

    private static List<string> Suspects(BotWatcher w, long now)
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

        var from = Math.Min(sorted.Count > Rows ? (w?.Rank ?? 0) * (Rows / 2) : 0, Math.Max(0, sorted.Count - Rows));

        for (var i = from; i < sorted.Count && rows.Count < Rows; i++)
        {
            rows.Add(sorted[i].Row(now));
        }

        return rows;
    }

    private static string Subsystems(BotWatcher w, int budget)
    {
        _describers ??= Gather();

        var sb = ValueStringBuilder.Create(4096);

        try
        {
            var spent = 0;

            for (var i = 0; i < _describers.Length && spent < budget; i++)
            {
                var method = _describers[i];

                if (w != null && !w.Watches(method.DeclaringType?.Name))
                {
                    continue;
                }

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

    private static bool Skipped(string type) =>
        type is "BotWill" or "BotPopulation";

    public static string Describe()
    {
        if (Squad.Count == 0)
        {
            return "the debugger has no body";
        }

        var sb = ValueStringBuilder.Create(512);

        try
        {
            sb.Append("a squad of ");
            sb.Append(Squad.Count);
            sb.Append(" watching ");
            sb.Append(_watch.Count);
            sb.Append(" bots: ");

            for (var i = 0; i < Squad.Count; i++)
            {
                if (i > 0)
                {
                    sb.Append("; ");
                }

                sb.Append(Squad[i].Describe());
            }

            sb.Append("; ");
            sb.Append(BotDebugMemory.Describe());
            sb.Append("; ");
            sb.Append(BotAudit.Describe());
            sb.Append("; ");
            sb.Append(BotTourney.Describe());

            return sb.ToString();
        }
        finally
        {
            sb.Dispose();
        }
    }

    private sealed class VigilTimer : Timer
    {
        public VigilTimer(TimeSpan interval) : base(interval, interval)
        {
        }

        protected override void OnTick() => Update();
    }
}
