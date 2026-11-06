using System;
using System.Collections.Generic;
using System.Text.Json;
using Server.BotAI.V2;
using Server.Logging;

namespace Server.BotAI.Mind;

public static class BotWitness
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotWitness));

    public static bool Running { get; set; } = true;

    public static int TickMs { get; set; } = 1000;

    public static int Aside { get; set; } = 2;

    public static int MostSpeech { get; set; } = 320;

    public static long Summoned { get; private set; }

    public static long NoneFree { get; private set; }

    public static long Asked { get; private set; }

    public static long Answered { get; private set; }

    public static long Unread { get; private set; }

    public static long Stale { get; private set; }

    public static long AskedMs { get; private set; }

    private static readonly Dictionary<BotWatcher, BotHearing> _held = [];

    private static readonly Queue<(BotHearing Hearing, Action<BotVerdict> Then)> _asks = new();

    private static bool _inFlight;

    private static Timer _timer;

    public static void Open()
    {
        BotParley.Summon = Summon;
        BotParley.Dismiss = Dismiss;
        BotParley.Judge = Judge;

        _timer?.Stop();
        _timer = new WitnessTimer(TimeSpan.FromMilliseconds(Math.Max(250, TickMs)));
        _timer.Start();
    }

    public static void Close()
    {
        BotParley.Summon = null;
        BotParley.Dismiss = null;
        BotParley.Judge = null;

        _timer?.Stop();
        _timer = null;

        foreach (var (w, _) in _held)
        {
            if (w.Body is { Deleted: false } body)
            {
                body.Hidden = true;
            }
        }

        _held.Clear();
        _asks.Clear();
        _inFlight = false;
    }

    public static bool Busy(BotWatcher w) => w != null && _held.ContainsKey(w);

    public static string Held(BotWatcher w) =>
        w != null && _held.TryGetValue(w, out var m)
            ? $"witness to meeting #{m.Id}: {m.Guest} {(m.Between ? $"to {m.Host}" : "to the town")} at {m.Town?.Name ?? "their seat"} over {BotDuke.Word(m.Topic)}, shown at {w.Body?.X},{w.Body?.Y}"
            : null;

    public static BotWatcher WitnessOf(int id)
    {
        foreach (var (w, m) in _held)
        {
            if (m.Id == id)
            {
                return w;
            }
        }

        return null;
    }

    private static Mobile Summon(BotHearing m)
    {
        if (!Running || m?.Map == null || m.Map == Map.Internal)
        {
            return null;
        }

        BotWatcher pick = null;

        for (var pass = 0; pass < 2 && pick == null; pass++)
        {
            for (var i = 0; i < BotVigil.Squad.Count && pick == null; i++)
            {
                var w = BotVigil.Squad[i];

                if (w.Organiser != (pass == 1) || _held.ContainsKey(w) || w.Body is not { Deleted: false } || BotVigil.IsKeeper(w))
                {
                    continue;
                }

                if (i == 0 && !BotHalls.Charted && BotHalls.Probing != Point3D.Zero)
                {
                    continue;
                }

                pick = w;
            }
        }

        if (pick == null)
        {
            NoneFree++;

            return null;
        }

        var body = pick.Body;
        var spot = BotTowns.Place(m.Map, m.Place.X + Aside, m.Place.Y, 3, out var aside) ? aside : m.Place;

        body.Hover(m.Map, spot);

        body.Hidden = false;

        _held[pick] = m;
        Summoned++;

        BotDebugLog.Write(
            $"{pick.Name} is summoned as witness to meeting #{m.Id}: {m.Guest} {(m.Between ? $"to {m.Host}" : "to the town")} at {m.Town?.Name ?? "their seat"} over {BotDuke.Word(m.Topic)} — {m.Why}; it shows itself at {spot.X},{spot.Y}"
        );

        logger.Information("{Name} is witness to meeting #{Id} at {Town}, shown at {X},{Y}", pick.Name, m.Id, m.Town?.Name ?? "their seat", spot.X, spot.Y);

        return body;
    }

    private static void Dismiss(BotHearing m)
    {
        BotWatcher found = null;

        foreach (var (w, held) in _held)
        {
            if (ReferenceEquals(held, m))
            {
                found = w;

                break;
            }
        }

        if (found == null)
        {
            return;
        }

        _held.Remove(found);

        if (found.Body is { Deleted: false } body)
        {
            body.Hidden = true;
        }

        BotDebugLog.Write($"{found.Name} leaves meeting #{m.Id} hidden again: {m.Ending}");
    }

    private static bool Judge(BotHearing m, Action<BotVerdict> then)
    {
        if (!Running || m == null || then == null || m.Allowed is not { Length: > 0 })
        {
            return false;
        }

        _asks.Enqueue((m, then));

        return true;
    }

    private static void Tick()
    {
        if (_inFlight || _asks.Count == 0 || !BotOllama.Free || World.Saving)
        {
            return;
        }

        var (m, then) = _asks.Dequeue();

        if (m.Stage is not (BotHearingStage.Hearing or BotHearingStage.Awaiting) || m.Verdict != null)
        {
            Stale++;

            return;
        }

        _inFlight = true;
        Asked++;

        var model = string.IsNullOrWhiteSpace(BotParley.JudgeModel) ? BotOllama.Model : BotParley.JudgeModel;

        BotOllama.Ask(
            m.Between ? Duke(m) : Voice(m),
            m.Brief ?? "",
            Schema(m),
            false,
            (json, waited) =>
            {
                _inFlight = false;
                AskedMs += waited;

                var v = Read(m, json);

                WitnessOf(m.Id)?.Think(
                    v == null ? "unread" : "meeting",
                    $"{(m.Between ? "the duke's word" : "the town's word")} at meeting #{m.Id}: {m.Guest} {(m.Between ? $"to {m.Host}" : "to the town")} over {BotDuke.Word(m.Topic)}",
                    v == null ? "the model gave no word that could be read" : $"{v.Outcome} — \"{v.Speech}\"",
                    v == null ? null : $"{v.Reason}{(v.Report != null ? $"; the town: \"{v.Report}\"" : "")}",
                    null
                );

                if (v == null)
                {
                    Unread++;
                    BotDebugLog.Write($"meeting #{m.Id}: the model gave no word that could be read in {waited}ms{(json == null ? " (no answer at all)" : $": {Cut(json, 300)}")}");
                }
                else
                {
                    Answered++;
                    BotDebugLog.Write($"meeting #{m.Id}: the model's word in {waited}ms is {v.Outcome} — \"{v.Speech}\" ({v.Reason}){(v.Report != null ? $"; the town: \"{v.Report}\"" : "")}");
                }

                then(v);
            },
            model,
            BotParley.JudgeKeepAlive,
            BotParley.JudgeTimeoutMs
        );
    }

    /// <summary>The duke, in Patrick's words of 29.09.2026, and what his answer is for.</summary>
    private static string Duke(BotHearing m) =>
        "You are the duke of this land. The guilds are your vassals, and their quarrels and bargains come before you; your word settles them. "
        + "What you hear is what vassals always bring: one says the other attacks its people too often, the other says the first crosses its borders; "
        + "one takes the reward off the other's fallen fighters, but does it on its own land. Nothing grand — land, blood, trade and pride. "
        + "Judge by the facts you are given, not by the speeches. A war costs both guilds lives and gold and gives the land nothing, "
        + "but a grievance with blood in it that goes unanswered festers; an agreement is worth giving only where both sides can keep it. "
        + $"Your witness, {m.WitnessName ?? "your herald"}, will say your word aloud to both envoys.\n"
        + "Answer in English with: outcome — exactly one of the outcomes offered; speech — what your witness says to both envoys, one or two plain sentences, "
        + "naming the guilds, no titles, no lists, no numbers they were not told; reason — one sentence for the record naming the facts that decided it.";

    private static string Voice(BotHearing m) =>
        $"You are {m.WitnessName ?? "the town's reeve"}, speaking for the town of {m.Town?.Name} to an envoy of the guild {m.Guest}. "
        + "Tell the envoy honestly how the town stands — its shelves, its market, the danger round it — from the numbers you are given, "
        + "and set the guild one task from those offered: the one the town needs most that the guild can do. "
        + "Answer in English with: task — exactly one of the task ids offered; report — how the town stands, one or two plain sentences from the numbers; "
        + "speech — the task put to the guild, one sentence naming it and the place or the goods; reason — one sentence for the record.";

    /// <summary>
    /// The answer's shape: the outcome (or task) an enumeration of exactly what this meeting allows, and floors on the words so

    private static string Schema(BotHearing m)
    {
        var buffer = new System.IO.MemoryStream();

        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("type", "object");
            writer.WriteStartObject("properties");

            writer.WriteStartObject(m.Between ? "outcome" : "task");
            writer.WriteString("type", "string");
            writer.WriteStartArray("enum");

            for (var i = 0; i < m.Allowed.Length; i++)
            {
                writer.WriteStringValue(m.Allowed[i]);
            }

            writer.WriteEndArray();
            writer.WriteEndObject();

            if (!m.Between)
            {
                Text(writer, "report", 20);
            }

            Text(writer, "speech", 20);
            Text(writer, "reason", 20);

            writer.WriteEndObject();

            writer.WriteStartArray("required");
            writer.WriteStringValue(m.Between ? "outcome" : "task");

            if (!m.Between)
            {
                writer.WriteStringValue("report");
            }

            writer.WriteStringValue("speech");
            writer.WriteStringValue("reason");
            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return System.Text.Encoding.UTF8.GetString(buffer.ToArray());
    }

    private static void Text(Utf8JsonWriter writer, string name, int least)
    {
        writer.WriteStartObject(name);
        writer.WriteString("type", "string");
        writer.WriteNumber("minLength", least);
        writer.WriteEndObject();
    }

    private static BotVerdict Read(BotHearing m, string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var outcome = Field(root, m.Between ? "outcome" : "task");
            var speech = Field(root, "speech");

            if (outcome == null || Array.IndexOf(m.Allowed, outcome) < 0 || string.IsNullOrWhiteSpace(speech))
            {
                return null;
            }

            return new BotVerdict
            {
                Outcome = outcome,
                Speech = Cut(speech, MostSpeech),
                Reason = Field(root, "reason") ?? "the model gave no reason",
                Report = m.Between ? null : Cut(Field(root, "report"), MostSpeech)
            };
        }

        catch (Exception)
        {
            return null;
        }
    }

    private static string Field(JsonElement root, string name) =>
        root.ValueKind == JsonValueKind.Object && root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()?.Trim()
            : null;

    private static string Cut(string text, int most)
    {
        if (text == null || text.Length <= most)
        {
            return text;
        }

        var cut = text.LastIndexOf(". ", most, StringComparison.Ordinal);

        if (cut < most / 3)
        {
            cut = text.LastIndexOf(' ', most);
        }

        return cut > 0 ? text[..(cut + 1)].Trim() : text[..most];
    }

    public static string Describe() =>
        $"witnessed {Summoned} meetings ({NoneFree} times none of the squad was free, {_held.Count} standing now); asked the model {Asked} words ({Answered} read, {Unread} not, {Stale} dropped for being given by rule first, {AskedMs / Math.Max(1, Asked)}ms each)";

    public static void Forget()
    {
        Summoned = 0;
        NoneFree = 0;
        Asked = 0;
        Answered = 0;
        Unread = 0;
        Stale = 0;
        AskedMs = 0;
    }

    private sealed class WitnessTimer : Timer
    {
        public WitnessTimer(TimeSpan interval) : base(interval, interval)
        {
        }

        protected override void OnTick() => Tick();
    }
}
