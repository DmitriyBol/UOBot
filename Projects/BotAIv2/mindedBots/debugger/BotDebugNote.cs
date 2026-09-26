using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;

namespace Server.BotAI.Mind;

/// <summary>
/// What the debugger came back with after one look at the population: one claim, the numbers it was made
/// from, a guess at the cause and one change worth making.
///
/// <para>
/// <b>Every field here exists to make the claim checkable by somebody who was not there.</b> A finding
/// without evidence is an opinion, a cause without a finding is a theory about nothing, and a fix without
/// either is a patch waiting to be applied to code that was working. The one that earns its place hardest
/// is <see cref="Last"/>: the debugger is shown what it said the time before and has to say whether the
/// numbers still support it. A watcher that never revisits its own claims produces a log of confident
/// paragraphs with no way to tell the true ones from the rest — which is exactly what the minds' own
/// reckonings were before they were made to predict a number and be measured against it.
/// </para>
/// </summary>
public sealed class BotDebugNote
{
    public string Kind { get; init; }

    public string Bot { get; init; }

    public string Finding { get; init; }

    public string Evidence { get; init; }

    public string Cause { get; init; }

    public string Fix { get; init; }

    public double Confidence { get; init; }

    public string Last { get; init; }

    public string Watch { get; init; }

    public string Probe { get; init; }

    public string At { get; init; }

    public static readonly string[] Kinds =
    [
        "stuck",
        "loop",
        "starved",
        "mismatch",
        "waste",
        "unreachable",
        "nothing"
    ];

    private static readonly string[] Verdicts = ["first", "holds", "gone", "unclear"];

    public static string Schema(IReadOnlyList<string> names)
    {
        var buffer = new System.IO.MemoryStream();

        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("type", "object");

            writer.WriteStartObject("properties");

            Enumeration(writer, "kind", Kinds);
            Roster(writer, "bot", names);
            Text(writer, "finding", 90);
            Text(writer, "evidence", 70);
            Text(writer, "cause", 45);
            Text(writer, "fix", 45);

            writer.WriteStartObject("confidence");
            writer.WriteString("type", "number");
            writer.WriteEndObject();

            Enumeration(writer, "last", Verdicts);
            Roster(writer, "watch", names);
            Enumeration(writer, "probe", BotHand.Verbs);
            Text(writer, "at", 1);

            writer.WriteEndObject();

            writer.WriteStartArray("required");
            writer.WriteStringValue("kind");
            writer.WriteStringValue("bot");
            writer.WriteStringValue("finding");
            writer.WriteStringValue("evidence");
            writer.WriteStringValue("cause");
            writer.WriteStringValue("fix");
            writer.WriteStringValue("confidence");
            writer.WriteStringValue("last");
            writer.WriteStringValue("watch");
            writer.WriteStringValue("probe");
            writer.WriteStringValue("at");
            writer.WriteEndArray();

            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    private static void Text(Utf8JsonWriter writer, string name, int least = 0)
    {
        writer.WriteStartObject(name);
        writer.WriteString("type", "string");

        if (least > 0)
        {
            writer.WriteNumber("minLength", least);
        }

        writer.WriteEndObject();
    }

    private static void Enumeration(Utf8JsonWriter writer, string name, IReadOnlyList<string> values)
    {
        writer.WriteStartObject(name);
        writer.WriteString("type", "string");
        writer.WriteStartArray("enum");

        for (var i = 0; i < values.Count; i++)
        {
            writer.WriteStringValue(values[i]);
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void Roster(Utf8JsonWriter writer, string name, IReadOnlyList<string> names)
    {
        writer.WriteStartObject(name);
        writer.WriteString("type", "string");
        writer.WriteStartArray("enum");
        writer.WriteStringValue("-");

        for (var i = 0; i < names.Count; i++)
        {
            writer.WriteStringValue(names[i]);
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    public static BotDebugNote Read(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            var finding = Word(root, "finding");

            if (string.IsNullOrWhiteSpace(finding))
            {
                return null;
            }

            return new BotDebugNote
            {
                Kind = Word(root, "kind") ?? "nothing",
                Bot = Word(root, "bot") ?? "-",
                Finding = finding,
                Evidence = Word(root, "evidence"),
                Cause = Word(root, "cause"),
                Fix = Word(root, "fix"),
                Confidence = Sure(root),
                Last = Word(root, "last") ?? "first",
                Watch = Word(root, "watch") ?? "-",
                Probe = Word(root, "probe") ?? "none",
                At = Word(root, "at") ?? "-"
            };
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string Word(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    internal static double Sure(JsonElement root)
    {
        if (!root.TryGetProperty("confidence", out var value) || value.ValueKind != JsonValueKind.Number)
        {
            return 0.0;
        }

        var sure = value.GetDouble();

        return Math.Clamp(sure > 1.0 ? sure / 100.0 : sure, 0.0, 1.0);
    }

    public override string ToString() => $"{Kind} — {Finding}";
}

/// <summary>
/// The slower answer: what the debugger makes of an hour of its own findings put together.
///
/// <para>
/// <b>A different question from the one above, and it is worth the cost of a thinking call precisely
/// because it is not the same question asked more often.</b> A finding is about this minute — that bot, that
/// pair of numbers. This asks what the findings have in common, which is where a defect that shows up
/// differently in six places is actually visible. The shard's worst faults have all had that shape: an
/// empty purse read as a veto looked like idle gatherers, timid crafters and a market with no smith, and
/// each of the three on its own looked like a different bug.
/// </para>
///
/// <para>
/// <see cref="Wrong"/> is the field that keeps this honest. A conjecture that names nothing which could
/// falsify it is not a conjecture, and a log full of those is a log that gets acted on and never checked.
/// </para>
/// </summary>
public sealed class BotDebugThought
{
    public string Blocking { get; init; }

    public string Evidence { get; init; }

    public string Change { get; init; }

    public string Second { get; init; }

    public string Wrong { get; init; }

    public double Confidence { get; init; }

    public const string Schema =
        """
        {"type":"object","properties":{"blocking":{"type":"string","minLength":120},"evidence":{"type":"string","minLength":80},"change":{"type":"string","minLength":60},"second":{"type":"string","minLength":50},"wrong":{"type":"string","minLength":60},"confidence":{"type":"number"}},"required":["blocking","evidence","change","second","wrong","confidence"]}
        """;

    public static BotDebugThought Read(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            var blocking = Word(root, "blocking");

            if (string.IsNullOrWhiteSpace(blocking))
            {
                return null;
            }

            return new BotDebugThought
            {
                Blocking = blocking,
                Evidence = Word(root, "evidence"),
                Change = Word(root, "change"),
                Second = Word(root, "second"),
                Wrong = Word(root, "wrong"),
                Confidence = BotDebugNote.Sure(root)
            };
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string Word(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    public static string ReadAnswer(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            var answer = Word(root, "answer");

            if (string.IsNullOrWhiteSpace(answer))
            {
                return null;
            }

            var evidence = Word(root, "evidence");

            return string.IsNullOrWhiteSpace(evidence) ? answer : $"{answer}\n  evidence: {evidence}";
        }
        catch (Exception)
        {
            return null;
        }
    }

    public override string ToString() => Blocking;
}
