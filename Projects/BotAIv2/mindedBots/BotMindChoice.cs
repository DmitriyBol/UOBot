using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using Server.BotAI.V2;

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
    public string Intent { get; init; }

    public double Expect { get; init; }

    public double Minutes { get; init; }

    public string Why { get; init; }

    public string Say { get; init; }

    public string Gather { get; init; }

    public string Make { get; init; }

    public int MarchX { get; init; }

    public int MarchY { get; init; }

    public string Expel { get; init; }

    public string Recruit { get; init; }

    public string Take { get; init; }

    public string Want { get; init; }

    public int WantAmount { get; init; }

    public static string Schema(IReadOnlyList<string> trades, IReadOnlyList<string> ours = null, IReadOnlyList<string> theirs = null, IReadOnlyList<string> orders = null, IReadOnlyList<string> band = null)
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

            var named = band ?? trades;

            for (var g = 0; g < 2; g++)
            {
                writer.WriteStartObject(g == 0 ? "gather" : "make");
                writer.WriteString("type", "string");
                writer.WriteStartArray("enum");
                writer.WriteStringValue("none");

                for (var i = 0; i < named.Count; i++)
                {
                    writer.WriteStringValue(named[i]);
                }

                writer.WriteEndArray();
                writer.WriteEndObject();
            }

            writer.WriteStartObject("marchx");
            writer.WriteString("type", "number");
            writer.WriteEndObject();

            writer.WriteStartObject("marchy");
            writer.WriteString("type", "number");
            writer.WriteEndObject();

            Names(writer, "expel", ours);
            Names(writer, "recruit", theirs);

            Names(writer, "take", orders);
            Names(writer, "want", BotCharter.Materials);

            writer.WriteStartObject("wantamount");
            writer.WriteString("type", "number");
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

    private static void Names(Utf8JsonWriter writer, string field, IReadOnlyList<string> who)
    {
        if (who == null || who.Count == 0)
        {
            return;
        }

        writer.WriteStartObject(field);
        writer.WriteString("type", "string");
        writer.WriteStartArray("enum");
        writer.WriteStringValue("none");

        for (var i = 0; i < who.Count; i++)
        {
            writer.WriteStringValue(who[i]);
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static string Order(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var said = value.GetString();

        return string.IsNullOrWhiteSpace(said) || said.InsensitiveEquals("none") ? null : said;
    }

    private static int Whole(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
        && value.TryGetInt32(out var whole)
            ? whole
            : 0;

    public const string LessonSchema =
        """
        {"type":"object","properties":{"lesson":{"type":"string"},"keep":{"type":"boolean"}},"required":["lesson","keep"]}
        """;

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
                Say = root.TryGetProperty("say", out var say) ? say.GetString() : null,
                Gather = Order(root, "gather"),
                Make = Order(root, "make"),
                MarchX = Whole(root, "marchx"),
                MarchY = Whole(root, "marchy"),
                Expel = Order(root, "expel"),
                Recruit = Order(root, "recruit"),
                Take = Order(root, "take"),
                Want = Order(root, "want"),
                WantAmount = Whole(root, "wantamount")
            };
        }
        catch (Exception)
        {
            return null;
        }
    }

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
