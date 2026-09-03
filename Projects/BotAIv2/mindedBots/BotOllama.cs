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

    /// <summary>Where the daemon listens. Local by default; nothing here is meant to leave the machine.</summary>
    public static string Endpoint { get; set; } = "http://127.0.0.1:11434";

    /// <summary>Which model answers. See the module's opening line for what is actually loaded.</summary>
    public static string Model { get; set; } = "qwen3.5:9b";

    /// <summary>
    /// How long the model is held in video memory between questions.
    ///
    /// <para>
    /// Sent on <em>every</em> request rather than set once, because the timer is refreshed per call and a
    /// bot that thinks every few minutes would otherwise pay the cold load — twenty-seven seconds, measured —
    /// each time. Twelve gigabytes holds one model of this size and no more, so there is never a second one
    /// to make room for.
    /// </para>
    /// </summary>
    public static string KeepAlive { get; set; } = "30m";

    /// <summary>
    /// How long one question may take before it is abandoned. Generous: a cold load is half of it.
    ///
    /// <para>
    /// <b>Enforced per request, and it used to be enforced nowhere.</b> The figure was handed to the
    /// <see cref="HttpClient"/> in its field initialiser — which runs once, before any configuration file is
    /// read — so <c>Configuration/bot-mind.json</c> could name any timeout it liked and the transport went on
    /// using two minutes. A setting that appears to have been read and silently does nothing is this shard's
    /// most-repeated defect, and here it was in the one file that talks to the outside. The client is now
    /// given a bound it will never reach and each request carries its own.
    /// </para>
    /// </summary>
    public static int TimeoutMs { get; set; } = 120000;

    /// <summary>How many questions may be in flight at once. Two minds, one graphics card, one question.</summary>
    public static int MostInFlight { get; set; } = 1;

    private static readonly HttpClient _http = new() { Timeout = Timeout.InfiniteTimeSpan };

    private static int _inFlight;

    /// <summary>Questions asked, answers that parsed, and answers that did not.</summary>
    public static long Asked { get; private set; }

    public static long Answered { get; private set; }

    public static long Refused { get; private set; }

    /// <summary>
    /// Wall-clock milliseconds spent waiting on plain questions, and on thinking ones, counted apart.
    ///
    /// <para>
    /// <b>One average over both describes neither.</b> A decision comes back in about a second and a half;
    /// a reckoning with thinking switched on took fifty-eight seconds once and ninety-nine another time on
    /// this card. Averaged together they read "4326ms a question", which is not how long anything actually
    /// takes and hides the only figure that matters — because while a thinking call runs, the single slot
    /// is held and neither mind can decide anything at all.
    /// </para>
    /// </summary>
    public static long WaitedMs { get; private set; }

    public static long Thoughts { get; private set; }

    public static long ThoughtMs { get; private set; }

    /// <summary>Whether another question may be asked at all right now.</summary>
    public static bool Free => _inFlight < MostInFlight;

    /// <summary>
    /// Asks, and calls back on the game thread with the raw JSON the model produced, or null.
    ///
    /// <para>
    /// Deferred while the world is being written out. A save is the one moment the loop is genuinely busy
    /// with something that cannot be interleaved, and a mind that has waited three seconds can wait three
    /// more.
    /// </para>
    /// </summary>
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
            // Swallowed rather than thrown: the daemon being off is an ordinary state of the world for this
            // assembly, and it must cost the shard nothing but a line.
            logger.Warning("Could not reach the model at {Endpoint}: {Message}", Endpoint, e.Message);
        }

        clock.Stop();

        var waited = clock.ElapsedMilliseconds;

        // Back onto the game thread. Everything the caller does with this touches the world.
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

    /// <summary>Pulls the assistant's message out of a chat response, or null if it is not there.</summary>
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
        // Any failure to read an answer is not an answer. See BotMindChoice.Read: a narrow catch here let a
        // missing ru-RU resource assembly, thrown while a JsonException was being built, reach the event loop
        // and take the shard down on 03.09.2026.
        catch (Exception)
        {
            // Falls through to null. A daemon that answers with something other than its own protocol is a
            // daemon this code has no business guessing about.
        }

        return null;
    }

    /// <summary>
    /// The request. Written with the writer rather than by interpolation because the prompt contains
    /// newlines, quotes and whatever a creature happens to be called, and a hand-built JSON string is a
    /// defect waiting for the first monster with an apostrophe in its name.
    /// </summary>
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

            // Low but not zero. A bot that answers identically to an identical situation never tries the
            // second-best idea, and the second-best idea is where every lesson in this file came from.
            writer.WriteNumber("temperature", 0.4);
            writer.WriteNumber("num_ctx", 8192);
            writer.WriteEndObject();

            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    /// <summary>What the transport has done this session, in one line, with the two kinds of call apart.</summary>
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
