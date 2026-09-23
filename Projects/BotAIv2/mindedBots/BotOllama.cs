using System;
using System.Diagnostics;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Server.Logging;

namespace Server.BotAI.Mind;

/// <summary>
/// The only thing in this assembly that talks to the model, and the only thing that leaves the game thread.
///
/// <para>
/// <b>Off the loop going out, back onto it coming in.</b> A warm answer takes seconds and a cold one takes
/// half a minute; the shard's whole world runs on one thread and a second of it is a second in which nothing
/// in the world moves. So the request goes to a background task with <c>ConfigureAwait(false)</c> — which is
/// what keeps the continuation off the loop rather than merely hoping — and the answer is handed back with
/// <see cref="Core.LoopContext"/><c>.Post</c>, which is the only sanctioned way in. Nothing here touches a
/// mobile, an item or a map: it takes a string and gives back a string.
/// </para>
///
/// <para>
/// <b>The schema is not optional.</b> Asked for JSON in words, the model glues a paragraph in front of it
/// about one answer in twenty — measured, on the first version of this — and a parser that copes with that
/// is a parser that will one day cope with something worse. Ollama's <c>format</c> takes a JSON schema and
/// constrains the sampler itself, so malformed output stops being a case that has to be handled.
/// </para>
///
/// <para>
/// <b>Timing is by the wall clock and by nothing else.</b> Ollama reports <c>eval_count</c> and
/// <c>eval_duration</c>, and thinking tokens appear in neither: a call measured at 2.6 seconds by its own
/// metrics took nineteen. What matters here is how long the answer was not available, so that is what is
/// measured.
/// </para>
/// </summary>
public static class BotOllama
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotOllama));

    public static string Endpoint { get; set; } = "http://127.0.0.1:11434";

    public static string Model { get; set; } = "qwen3.5:9b";

    public static string KeepAlive { get; set; } = "30m";

    public static int TimeoutMs { get; set; } = 120000;

    public static int MostInFlight { get; set; } = 1;

    public static int ThinkingMostTokens { get; set; } = 4500;

    private static readonly HttpClient _http = new() { Timeout = Timeout.InfiniteTimeSpan };

    private static int _inFlight;

    public static long Asked { get; private set; }

    public static long Answered { get; private set; }

    public static long Refused { get; private set; }

    public static long WaitedMs { get; private set; }

    public static long Thoughts { get; private set; }

    public static long ThoughtMs { get; private set; }

    public static bool Free => _inFlight < MostInFlight;

    public static void Ask(
        string system,
        string user,
        string schema,
        bool think,
        Action<string, long> then,
        string model = null,
        string keepAlive = null,
        int timeoutMs = 0
    )
    {
        if (then == null)
        {
            return;
        }

        if (!Free || World.Saving)
        {
            then(null, 0);

            return;
        }

        _inFlight++;
        Asked++;

        var body = Body(system, user, schema, think, model ?? Model, keepAlive ?? KeepAlive);

        _ = Run(body, think, then, timeoutMs > 0 ? timeoutMs : TimeoutMs);
    }

    private static async Task Run(string body, bool think, Action<string, long> then, int timeoutMs)
    {
        string answer = null;
        var clock = Stopwatch.StartNew();

        try
        {
            using var content = new StringContent(body, Encoding.UTF8, "application/json");
            using var giveUp = new CancellationTokenSource(TimeSpan.FromMilliseconds(Math.Max(1000, timeoutMs)));

            using var response = await _http
                .PostAsync($"{Endpoint}/api/chat", content, giveUp.Token)
                .ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
                var text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

                answer = Content(text);
            }
            else
            {
                logger.Warning("The model answered {Status} to a question; nothing is decided by it", (int)response.StatusCode);
            }
        }
        catch (Exception e)
        {
            logger.Warning("Could not reach the model at {Endpoint}: {Message}", Endpoint, e.Message);
        }

        clock.Stop();

        var waited = clock.ElapsedMilliseconds;

        Core.LoopContext.Post(
            _ =>
            {
                _inFlight--;

                if (think)
                {
                    Thoughts++;
                    ThoughtMs += waited;
                }
                else
                {
                    WaitedMs += waited;
                }

                if (answer == null)
                {
                    Refused++;
                }
                else
                {
                    Answered++;
                }

                then(answer, waited);
            },
            null
        );
    }

    private static string Content(string payload)
    {
        try
        {
            using var document = JsonDocument.Parse(payload);

            if (document.RootElement.TryGetProperty("message", out var message)
                && message.TryGetProperty("content", out var content))
            {
                var text = content.GetString();

                return string.IsNullOrWhiteSpace(text) ? null : text;
            }
        }
        catch (Exception)
        {
        }

        return null;
    }

    private static string Body(string system, string user, string schema, bool think, string model, string keepAlive)
    {
        var buffer = new System.IO.MemoryStream();

        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("model", model);
            writer.WriteBoolean("stream", false);
            writer.WriteString("keep_alive", keepAlive);
            writer.WriteBoolean("think", think);

            writer.WriteStartArray("messages");

            writer.WriteStartObject();
            writer.WriteString("role", "system");
            writer.WriteString("content", system);
            writer.WriteEndObject();

            writer.WriteStartObject();
            writer.WriteString("role", "user");
            writer.WriteString("content", user);
            writer.WriteEndObject();

            writer.WriteEndArray();

            if (schema != null)
            {
                writer.WritePropertyName("format");
                writer.WriteRawValue(schema);
            }

            writer.WriteStartObject("options");

            writer.WriteNumber("temperature", 0.4);
            writer.WriteNumber("num_ctx", 8192);

            if (think && ThinkingMostTokens > 0)
            {
                writer.WriteNumber("num_predict", ThinkingMostTokens);
            }

            writer.WriteEndObject();

            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    public static string Describe()
    {
        if (Asked == 0)
        {
            return "nothing has been asked of the model yet";
        }

        var decisions = Asked - Thoughts;

        return $"{Asked} asked, {Answered} answered, {Refused} refused; "
               + $"{decisions} decisions at {WaitedMs / Math.Max(1, decisions)}ms, "
               + $"{Thoughts} reckonings at {ThoughtMs / Math.Max(1, Thoughts)}ms, both on the wall clock";
    }

    public static void Forget()
    {
        Asked = 0;
        Answered = 0;
        Refused = 0;
        WaitedMs = 0;
        Thoughts = 0;
        ThoughtMs = 0;
        _inFlight = 0;
    }
}
