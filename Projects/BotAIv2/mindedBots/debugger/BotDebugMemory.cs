using System;
using System.Collections.Generic;
using System.IO;
using Server.Json;
using Server.Logging;
using Server.Text;

namespace Server.BotAI.Mind;

/// <summary>One thing the debugger has come to believe, and how many times it has come to believe it.</summary>
public sealed class BotDebugBelief
{
    public string Kind { get; set; }

    public string Who { get; set; }

    public string Finding { get; set; }

    public string Cause { get; set; }

    public string Fix { get; set; }

    public double Confidence { get; set; }

    public int Seen { get; set; }

    public string First { get; set; }

    public string Last { get; set; }

    public bool Gone { get; set; }
}

/// <summary>Something a person said to the debugger. Kept until it is answered, and after.</summary>
public sealed class BotDebugAsk
{
    public string Who { get; set; }

    public string What { get; set; }

    public string When { get; set; }

    public bool Answered { get; set; }
}

/// <summary>What survives a restart.</summary>
public sealed class BotDebugRecall
{
    public int Sessions { get; set; }

    public List<BotDebugBelief> Beliefs { get; set; } = [];

    public List<string> Lessons { get; set; } = [];

    public List<BotDebugAsk> Notes { get; set; } = [];
}

/// <summary>
/// The debugger's long memory: <c>Configuration/bot-debugger-memory.json</c>.
///
/// <para>
/// <b>Without it every restart is the first evening.</b> The population is rebuilt from configuration on
/// every world load and keeps nothing but names and skills, and until now the debugger kept even less — its
/// findings, its conclusions and every question a person had asked it went with the process. That made it
/// structurally incapable of the one thing that would make it worth having: saying "this is the ninth time I
/// have found this, over four sessions". A watcher that cannot count how often it has been right about the
/// same thing is a watcher whose confidence means nothing.
/// </para>
///
/// <para>
/// <b>Written the moment something is added rather than at shutdown, and that is not caution.</b> A shard is
/// killed far more often than it is stopped — this one has been killed a dozen times today — and a memory
/// that is flushed on a clean exit is a memory that is empty exactly when it is wanted.
/// </para>
///
/// <para>
/// <b>Beliefs are merged by what they say, never by when they were said.</b> A model asked the same question
/// about the same defect writes it in different words every time; a check on the opening characters lets
/// every one of them through and the file fills with one finding restated forty ways. Two thirds of the
/// words in common is one belief twice — the same rule, and the same figure, the minds' own lessons use.
/// </para>
/// </summary>
public static class BotDebugMemory
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotDebugMemory));

    private const string MemoryPath = "Configuration/bot-debugger-memory.json";

    public static double Same { get; set; } = 0.67;

    public static int MostBeliefs { get; set; } = 40;

    public static int MostLessons { get; set; } = 12;

    public static int Recall { get; set; } = 8;

    private static BotDebugRecall _recall = new();

    public static int Sessions => _recall.Sessions;

    public static int Beliefs => _recall.Beliefs.Count;

    public static IReadOnlyList<BotDebugAsk> Notes => _recall.Notes;

    public static void Load()
    {
        var path = Path.Combine(Core.BaseDirectory, MemoryPath);

        try
        {
            _recall = JsonConfig.Deserialize<BotDebugRecall>(path) ?? new BotDebugRecall();
        }
        catch (Exception e)
        {
            _recall = new BotDebugRecall();

            logger.Warning("The debugger's memory could not be read and it starts this session with none: {Message}", e.Message);
        }

        _recall.Beliefs ??= [];
        _recall.Lessons ??= [];
        _recall.Notes ??= [];

        for (var i = _recall.Beliefs.Count - 1; i >= 0; i--)
        {
            var belief = _recall.Beliefs[i];

            if (!Label(belief.Finding))
            {
                continue;
            }

            logger.Information(
                "The debugger drops a belief that was a label rather than a sentence, found {Seen} times from {First} to {Last}: [{Kind}] {Finding}",
                belief.Seen,
                belief.First,
                belief.Last,
                belief.Kind,
                belief.Finding
            );

            _recall.Beliefs.RemoveAt(i);
        }

        _recall.Sessions++;

        Save();

        logger.Information(
            "The debugger remembers {Beliefs} findings and {Lessons} conclusions from {Sessions} earlier sessions, and {Asks} things people asked it that it has not answered",
            _recall.Beliefs.Count,
            _recall.Lessons.Count,
            Math.Max(0, _recall.Sessions - 1),
            Unanswered()
        );
    }

    public static void Save()
    {
        try
        {
            JsonConfig.Serialize(Path.Combine(Core.BaseDirectory, MemoryPath), _recall);
        }
        catch (Exception e)
        {
            logger.Warning("The debugger's memory could not be written down: {Message}", e.Message);
        }
    }

    public static void Believe(BotDebugNote note)
    {
        if (note == null || string.IsNullOrWhiteSpace(note.Finding) || note.Kind == "nothing" || Label(note.Finding))
        {
            return;
        }

        var now = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
        var held = Alike(note.Finding);

        if (held != null)
        {
            held.Seen++;
            held.Last = now;
            held.Gone = false;

            if (note.Confidence >= held.Confidence)
            {
                held.Confidence = note.Confidence;
                held.Cause = note.Cause ?? held.Cause;
                held.Fix = note.Fix ?? held.Fix;
            }

            Save();

            return;
        }

        _recall.Beliefs.Add(
            new BotDebugBelief
            {
                Kind = note.Kind,
                Who = note.Bot,
                Finding = note.Finding,
                Cause = note.Cause,
                Fix = note.Fix,
                Confidence = note.Confidence,
                Seen = 1,
                First = now,
                Last = now
            }
        );

        Trim();
        Save();
    }

    public static void Doubt(string finding)
    {
        var held = Alike(finding);

        if (held == null)
        {
            return;
        }

        held.Gone = true;
        held.Last = DateTime.Now.ToString("yyyy-MM-dd HH:mm");

        Save();
    }

    public static void Learn(string lesson)
    {
        if (string.IsNullOrWhiteSpace(lesson))
        {
            return;
        }

        for (var i = 0; i < _recall.Lessons.Count; i++)
        {
            if (Overlap(_recall.Lessons[i], lesson) >= Same)
            {
                return;
            }
        }

        _recall.Lessons.Add(lesson.Trim());

        while (_recall.Lessons.Count > MostLessons)
        {
            _recall.Lessons.RemoveAt(0);
        }

        Save();
    }

    public static void Ask(string who, string what)
    {
        if (string.IsNullOrWhiteSpace(what))
        {
            return;
        }

        _recall.Notes.Add(
            new BotDebugAsk
            {
                Who = who,
                What = what.Trim(),
                When = DateTime.Now.ToString("yyyy-MM-dd HH:mm")
            }
        );

        Save();
    }

    public static void Answered()
    {
        var moved = false;

        for (var i = 0; i < _recall.Notes.Count; i++)
        {
            if (!_recall.Notes[i].Answered)
            {
                _recall.Notes[i].Answered = true;
                moved = true;
            }
        }

        if (moved)
        {
            Save();
        }
    }

    public static int Unanswered()
    {
        var waiting = 0;

        for (var i = 0; i < _recall.Notes.Count; i++)
        {
            if (!_recall.Notes[i].Answered)
            {
                waiting++;
            }
        }

        return waiting;
    }

    public static string Recite()
    {
        if (_recall.Beliefs.Count == 0 && _recall.Lessons.Count == 0)
        {
            return null;
        }

        var sb = ValueStringBuilder.Create(1024);

        try
        {
            if (_recall.Beliefs.Count > 0)
            {
                List<BotDebugBelief> sorted = [.. _recall.Beliefs];

                sorted.Sort((a, b) => b.Seen.CompareTo(a.Seen));

                sb.Append("Across ");
                sb.Append(_recall.Sessions);
                sb.AppendLine(" sessions of watching this shard, these are the things you have found more than once. The count is how many separate times you reached the same conclusion from fresh measurements, which is the only reason to weigh one above another.");

                var recited = 0;

                for (var i = 0; i < sorted.Count && recited < Recall; i++)
                {
                    var belief = sorted[i];

                    if (Label(belief.Finding))
                    {
                        continue;
                    }

                    recited++;

                    sb.Append("- found ");
                    sb.Append(belief.Seen);
                    sb.Append(belief.Seen == 1 ? " time" : " times");
                    sb.Append(" (");
                    sb.Append(belief.First);
                    sb.Append(" to ");
                    sb.Append(belief.Last);
                    sb.Append(belief.Gone ? ", and last time the numbers no longer supported it" : "");
                    sb.Append(") [");
                    sb.Append(belief.Kind);
                    sb.Append("] ");
                    sb.Append(belief.Finding);

                    if (!string.IsNullOrWhiteSpace(belief.Fix))
                    {
                        sb.Append(" — you suggested: ");
                        sb.Append(belief.Fix);
                    }

                    sb.AppendLine("");
                }
            }

            if (_recall.Lessons.Count > 0)
            {
                sb.AppendLine("\nWhat you have concluded about this shard as a whole:");

                for (var i = 0; i < _recall.Lessons.Count; i++)
                {
                    sb.Append("- ");
                    sb.AppendLine(_recall.Lessons[i]);
                }
            }

            return sb.ToString();
        }
        finally
        {
            sb.Dispose();
        }
    }

    public static int LongestWord { get; set; } = 32;

    public static bool Label(string finding)
    {
        if (string.IsNullOrWhiteSpace(finding) || finding.IndexOf(' ') < 0)
        {
            return true;
        }

        var run = 0;

        for (var i = 0; i < finding.Length; i++)
        {
            if (char.IsWhiteSpace(finding[i]))
            {
                run = 0;

                continue;
            }

            if (++run > LongestWord)
            {
                return true;
            }
        }

        return false;
    }

    private static BotDebugBelief Alike(string finding)
    {
        if (string.IsNullOrWhiteSpace(finding))
        {
            return null;
        }

        for (var i = 0; i < _recall.Beliefs.Count; i++)
        {
            if (Overlap(_recall.Beliefs[i].Finding, finding) >= Same)
            {
                return _recall.Beliefs[i];
            }
        }

        return null;
    }

    private static double Overlap(string a, string b)
    {
        if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b))
        {
            return 0.0;
        }

        var one = a.ToLowerInvariant().Split([' ', ',', '.', ';', ':', '(', ')', '"', '\''], StringSplitOptions.RemoveEmptyEntries);
        var two = b.ToLowerInvariant().Split([' ', ',', '.', ';', ':', '(', ')', '"', '\''], StringSplitOptions.RemoveEmptyEntries);

        if (one.Length == 0 || two.Length == 0)
        {
            return 0.0;
        }

        HashSet<string> words = [.. one];
        var shared = 0;

        for (var i = 0; i < two.Length; i++)
        {
            if (words.Contains(two[i]))
            {
                shared++;
            }
        }

        return shared / (double)Math.Min(one.Length, two.Length);
    }

    private static void Trim()
    {
        while (_recall.Beliefs.Count > MostBeliefs)
        {
            var worst = 0;

            for (var i = 1; i < _recall.Beliefs.Count; i++)
            {
                if (_recall.Beliefs[i].Seen < _recall.Beliefs[worst].Seen)
                {
                    worst = i;
                }
            }

            _recall.Beliefs.RemoveAt(worst);
        }
    }

    public static string Describe() =>
        $"{_recall.Beliefs.Count} findings remembered over {_recall.Sessions} sessions, "
        + $"{_recall.Lessons.Count} standing conclusions, {Unanswered()} questions from people still unanswered";
}
