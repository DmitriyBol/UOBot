using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;

namespace Server.BotAI.Mind;

/// <summary>
/// What a mind came back with: one trade, one number it is prepared to be judged on, and its reason.
///
/// <para>
/// <b>The prediction is the point of the whole arrangement.</b> Without a number the model can say anything
/// and nothing can ever be shown to have been wrong; with one, every choice ends in a comparison between
/// what was promised and what the ledger actually measured, and that comparison is the only honest material
/// a lesson can be made of. It is also what the auction weighs the offer by, so an optimistic mind loses
/// its bots to the shard's own arithmetic within a few jobs rather than being argued with.
/// </para>
/// </summary>
public sealed class BotMindChoice
{
    /// <summary>The name of a proposer, exactly as <c>IBotProposer.Name</c> gives it.</summary>
    public string Intent { get; init; }

    /// <summary>Gold-equivalent a minute the mind expects this to come to.</summary>
    public double Expect { get; init; }

    /// <summary>How long it thinks it will take.</summary>
    public double Minutes { get; init; }

    /// <summary>Its reason, in its own words. Never parsed — read by people and fed back at review.</summary>
    public string Why { get; init; }

    /// <summary>
    /// One line to the others, or nothing.
    ///
    /// <para>
    /// Optional in the schema and allowed to be empty, which is the part that matters: a required field is a
    /// field that gets filled every time, and a mind that announces every decision to two colleagues is
    /// noise rather than communication. What is wanted is the line worth saying — something found, something
    /// given up on, somewhere worth coming to.
    /// </para>
    /// </summary>
    public string Say { get; init; }

    /// <summary>
    /// The schema the sampler is constrained by, built from the trades that actually exist right now.
    ///
    /// <para>
    /// <b>An enum rather than a free string, and that is a defect class removed rather than handled.</b> A
    /// model asked in words for the name of a trade will eventually invent one — "Mining", "go mining",
    /// "Miner (ore)" — and every one of those is a decision that silently does nothing. Constrained to the
    /// list, the answer is always a trade the shard has.
    /// </para>
    /// </summary>
    public static string Schema(IReadOnlyList<string> trades)
    {
        var buffer = new System.IO.MemoryStream();

        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("type", "object");

            writer.WriteStartObject("properties");

            writer.WriteStartObject("intent");
            writer.WriteString("type", "string");
            writer.WriteStartArray("enum");

            for (var i = 0; i < trades.Count; i++)
            {
                writer.WriteStringValue(trades[i]);
            }

            writer.WriteEndArray();
            writer.WriteEndObject();

            writer.WriteStartObject("expect");
            writer.WriteString("type", "number");
            writer.WriteEndObject();

            writer.WriteStartObject("minutes");
            writer.WriteString("type", "number");
            writer.WriteEndObject();

            writer.WriteStartObject("why");
            writer.WriteString("type", "string");
            writer.WriteEndObject();

            writer.WriteStartObject("say");
            writer.WriteString("type", "string");
            writer.WriteEndObject();

            writer.WriteEndObject();

            writer.WriteStartArray("required");
            writer.WriteStringValue("intent");
            writer.WriteStringValue("expect");
            writer.WriteStringValue("minutes");
            writer.WriteStringValue("why");
            writer.WriteEndArray();

            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    /// <summary>The schema for a reckoning: one lesson, and whether it is worth keeping at all.</summary>
    public const string LessonSchema =
        """
        {"type":"object","properties":{"lesson":{"type":"string"},"keep":{"type":"boolean"}},"required":["lesson","keep"]}
        """;

    /// <summary>Reads an answer, or null if it is not one. Never throws at the caller.</summary>
    public static BotMindChoice Read(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            if (!root.TryGetProperty("intent", out var intent))
            {
                return null;
            }

            var name = intent.GetString();

            if (string.IsNullOrWhiteSpace(name))
            {
                return null;
            }

            return new BotMindChoice
            {
                Intent = name,
                Expect = Number(root, "expect"),
                Minutes = Number(root, "minutes"),
                Why = root.TryGetProperty("why", out var why) ? why.GetString() : null,
                Say = root.TryGetProperty("say", out var say) ? say.GetString() : null
            };
        }
        // <b>Anything at all, and the word JsonException was not wide enough to keep a promise made three
        // lines above this method's name.</b> A malformed answer makes System.Text.Json build a
        // JsonReaderException — and building its message asks the framework for a localised string, which on
        // a Russian-locale machine means loading System.Text.Json.resources for ru-RU, which is not deployed.
        // The FileNotFoundException from that is thrown while the JsonException is being constructed, so it
        // is not a JsonException and nothing here caught it. It went all the way to the event loop and took
        // the shard down at 07:15:32 on 03.09.2026, after seven hours up, on one bad answer from the model.
        //
        // What comes back from a language model is untrusted input. Any failure to read it means the same
        // thing — this is not an answer — and the caller is entitled to that and nothing else.
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Reads a reckoning: the lesson and whether the mind thinks it worth keeping.</summary>
    public static (string Lesson, bool Keep) ReadLesson(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return (null, false);
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            var lesson = root.TryGetProperty("lesson", out var text) ? text.GetString() : null;
            var keep = root.TryGetProperty("keep", out var flag) && flag.ValueKind == JsonValueKind.True;

            return (string.IsNullOrWhiteSpace(lesson) ? null : lesson.Trim(), keep);
        }
        // The same reasoning as Read above: any failure to read an answer is not an answer.
        catch (Exception)
        {
            return (null, false);
        }
    }

    private static double Number(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetDouble()
            : 0.0;

    public override string ToString() =>
        $"{Intent} at {Expect:F0}/min over {Minutes:F0} min — {Why}";
}
