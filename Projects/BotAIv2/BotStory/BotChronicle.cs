using System;
using System.Collections.Generic;
using System.Globalization;
using Server.Text;

namespace Server.BotAI.V2;

public static class BotChronicle
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    public static int MostDays { get; set; } = 30;

    public static int MostPerDay { get; set; } = 80;

    public static int EveryMs { get; set; } = 60000;

    public static int VoiceLeast { get; set; } = 4;

    public static int VoiceMostFacts { get; set; } = 30;

    public static int RepeatMinutes { get; set; } = 30;

    public static double RepeatDayBelow { get; set; } = 30.0;

    public static long Recorded { get; private set; }

    public static long Repeats { get; private set; }

    public static long Dropped { get; private set; }

    public static long Built { get; private set; }

    private static readonly Dictionary<string, long> _byKind = new(StringComparer.Ordinal);

    private static readonly Dictionary<string, BotChronicleDay> _days = new(StringComparer.Ordinal);

    private static readonly List<string> _keys = new(32);

    private static readonly Dictionary<string, (string Who, long At)> _founded = new(StringComparer.Ordinal);

    private static readonly Dictionary<string, long> _closed = new(StringComparer.Ordinal);

    private static bool _dirty = true;

    private static long _builtTick;

    private static bool _builtOnce;

    private static volatile string _json = "{\"days\":[]}";

    public static string Json => _json;

    public static IReadOnlyDictionary<string, BotChronicleDay> Days => _days;

    public static void Heard(BotEvent e)
    {
        var text = e.Text;

        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        switch (e.Kind)
        {
            case "war":
                {

                    Record("war", text, 80.0, text);

                    break;
                }
            case "alarm":
                {
                    var kind = DataKind(e.Data);

                    if (kind == "guild")
                    {
                        Record("guild", Closing(text), 70.0);
                        Guilds(text);
                    }
                    else if (kind == "exile")
                    {
                        Record("town", text, 30.0);
                    }

                    break;
                }
            case "relic":
                {
                    Record("relic", e.Bot != null ? $"{e.Bot} {text}" : text, 65.0, text);

                    break;
                }
            case "politics":
                {
                    Politics(e, text);

                    break;
                }
            case "parley":
                {

                    if (e.Channel == "heard" && text.Contains(" and ", StringComparison.Ordinal) && !text.Contains("town's favour", StringComparison.Ordinal))
                    {
                        Record("diplomacy", $"The duke's word on {Meeting(text)}", text.Contains(": trade terms", StringComparison.Ordinal) ? 12.0 : 40.0);
                    }
                    else if (e.Channel == null && text.Contains(" laid claim to ", StringComparison.Ordinal))
                    {
                        Record("war", text, 50.0, text);
                    }

                    break;
                }
            case "officers":
                {
                    if (e.Bot != null && text.StartsWith("won the guilds' bout", StringComparison.Ordinal))
                    {
                        Record("contest", e.Guild != null ? $"{e.Bot} of {e.Guild} {text}" : $"{e.Bot} {text}", 35.0);
                    }

                    break;
                }
            case "charter":
                {

                    var outcome = DataField(e.Data, "outcome");

                    if (outcome == "won")
                    {
                        Record("town", text, 20.0);
                    }
                    else if (outcome == "revoked")
                    {
                        Record("town", text, 15.0);
                    }

                    break;
                }
            case "event":
                {
                    Crier(e.Channel, text);

                    break;
                }
            case "house":
                {
                    Record("guild", text, 40.0, text);

                    break;
                }
            case "crime":
                {
                    if (e.Bot != null && text.StartsWith("stole ", StringComparison.Ordinal))
                    {
                        Record("crime", $"{e.Bot} {text}", 50.0);
                    }

                    break;
                }
        }
    }

    private static void Politics(BotEvent e, string text)
    {
        if (text.Contains(" re-elected head of ", StringComparison.Ordinal))
        {
            return;
        }

        if (text.Contains(" elected head of ", StringComparison.Ordinal))
        {
            Record("politics", text.Replace(" elected head of ", " was elected head of ", StringComparison.Ordinal), 45.0);
        }
        else if (e.Channel == "steward")
        {
            if (text.Contains(" deposed its steward ", StringComparison.Ordinal))
            {
                Record("politics", text, 45.0);
            }
            else if (text.Contains(" is the steward of ", StringComparison.Ordinal))
            {
                Record("politics", Steward(text), 35.0);
            }
        }
        else if (text.Contains(" deposed ", StringComparison.Ordinal))
        {
            Record("politics", text, 55.0);
        }
        else if (text.Contains(" tribute ", StringComparison.Ordinal))
        {
            Record("politics", text, 40.0);
        }
        else if (text.Contains(" collects ", StringComparison.Ordinal) || text.Contains(" captains ", StringComparison.Ordinal))
        {
            Record("town", text, 20.0);
        }
    }

    private static string Meeting(string text)
    {
        if (text.Length > 1 && text[0] == '#')
        {
            var space = text.IndexOf(' ');

            if (space > 0)
            {
                text = text[(space + 1)..];
            }
        }

        var by = text.LastIndexOf(" (by ", StringComparison.Ordinal);

        return by > 0 && text.EndsWith(')') ? text[..by] : text;
    }

    public static string Steward(string text) =>
        text.Replace(" is the steward of ", " became the steward of ", StringComparison.Ordinal).Replace(" now.", ".", StringComparison.Ordinal);

    private static void Crier(string channel, string text)
    {
        switch (channel)
        {
            case "invasion":
                {
                    if (!text.StartsWith("They come!", StringComparison.Ordinal) && !text.StartsWith("Wave ", StringComparison.Ordinal))
                    {
                        Record("town", text, text.StartsWith("To arms", StringComparison.Ordinal) ? 30.0 : 40.0);
                    }

                    break;
                }
            case "fair":
                {
                    if ((text.Contains("Great Fair", StringComparison.Ordinal) && text.Contains(" calls ", StringComparison.Ordinal)) || text.Contains(" lottery: ", StringComparison.Ordinal))
                    {
                        Record("town", text, 30.0);
                    }

                    break;
                }
            case "rumour":
                {
                    if (text.Contains(" found the chest", StringComparison.Ordinal))
                    {
                        Record("road", text, 20.0);
                    }

                    break;
                }
            case "caravan":
                {
                    if (text.StartsWith("The caravan", StringComparison.Ordinal))
                    {
                        Record("road", text, 15.0);
                    }

                    break;
                }
        }
    }

    private static string Closing(string text)
    {
        var at = text.IndexOf(" closed:", StringComparison.Ordinal);

        if (at <= 0)
        {
            return text;
        }

        var shared = text.LastIndexOf("; ", StringComparison.Ordinal);
        var gold = shared > at && text.EndsWith("gp shared", StringComparison.Ordinal) ? $"; {text[(shared + 2)..]} among its members" : "";

        return $"{text[..at]} closed, nobody joining{gold}";
    }

    private static void Guilds(string text)
    {
        var now = Core.TickCount;
        var founded = text.IndexOf(" was founded by ", StringComparison.Ordinal);

        if (founded > 0)
        {
            var guild = text[..founded];
            var bracket = guild.IndexOf(" [", StringComparison.Ordinal);

            if (bracket > 0)
            {
                guild = guild[..bracket];
            }

            var rest = text[(founded + " was founded by ".Length)..];
            string who = null;
            var led = rest.IndexOf(", led by ", StringComparison.Ordinal);

            if (led >= 0)
            {
                who = rest[(led + ", led by ".Length)..].Trim();
            }
            else
            {
                var the = rest.IndexOf(" the ", StringComparison.Ordinal);

                if (the > 0)
                {
                    who = rest[..the];
                }
            }

            if (!string.IsNullOrEmpty(who))
            {
                _founded[guild] = (who, now);
            }

            return;
        }

        var closed = text.IndexOf(" closed:", StringComparison.Ordinal);

        if (closed > 0)
        {
            _closed[text[..closed]] = now;
        }
    }

    public static string FounderOf(string guild, long now, long withinMs) =>
        guild != null && _founded.TryGetValue(guild, out var f) && now - f.At < withinMs ? f.Who : null;

    public static bool ClosedLately(string guild, long now, long withinMs) =>
        guild != null && _closed.TryGetValue(guild, out var at) && now - at < withinMs;

    public static void Record(string kind, string text, double weight, string key = null)
    {
        if (!BotStory.Running || BotStory.Sealed || string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        var now = DateTime.Now;
        var day = Day(now);
        var line = BotStoryWriter.Sentence(BotStory.Plain(text));
        var self = key ?? line;

        for (var i = day.Entries.Count - 1; i >= 0; i--)
        {
            var entry = day.Entries[i];

            if ((weight < RepeatDayBelow || (now - entry.At).TotalMinutes < Math.Max(0, RepeatMinutes))
                && string.Equals(entry.Key ?? entry.Text, self, StringComparison.Ordinal))
            {
                Repeats++;

                return;
            }
        }

        if (day.Entries.Count >= Math.Max(1, MostPerDay))
        {
            var lightest = 0;

            for (var i = 1; i < day.Entries.Count; i++)
            {
                if (day.Entries[i].Weight < day.Entries[lightest].Weight)
                {
                    lightest = i;
                }
            }

            day.Dropped++;
            Dropped++;

            if (day.Entries[lightest].Weight >= weight)
            {
                return;
            }

            day.Entries.RemoveAt(lightest);
            day.Hashed = null;
        }

        day.Entries.Add(new BotChronicleEntry { At = now, Kind = kind, Text = line, Key = key, Weight = weight });
        day.Hashed = null;
        Recorded++;
        _byKind[kind] = _byKind.GetValueOrDefault(kind) + 1;
        _dirty = true;
    }

    private static BotChronicleDay Day(DateTime local)
    {
        var key = local.ToString("yyyy-MM-dd", Invariant);

        if (_days.TryGetValue(key, out var day))
        {
            return day;
        }

        day = new BotChronicleDay { Key = key, Date = local.Date };
        Put(day);

        return day;
    }

    private static void Put(BotChronicleDay day)
    {
        _days[day.Key] = day;

        if (!_keys.Contains(day.Key))
        {
            _keys.Add(day.Key);
            _keys.Sort(StringComparer.Ordinal);
        }

        while (_keys.Count > Math.Max(1, MostDays))
        {
            _days.Remove(_keys[0]);
            _keys.RemoveAt(0);
        }

        _dirty = true;
    }

    private static string DataKind(string data) => DataField(data, "kind");

    private static string DataField(string data, string name)
    {
        if (string.IsNullOrEmpty(data))
        {
            return null;
        }

        var at = data.IndexOf($"\"{name}\":\"", StringComparison.Ordinal);

        if (at < 0)
        {
            return null;
        }

        at += name.Length + 4;

        var end = data.IndexOf('"', at);

        return end > at ? data[at..end] : null;
    }

    private static readonly string[] Order = ["war", "guild", "champion", "relic", "contest", "diplomacy", "politics", "town", "crime", "road"];

    public static void Tick(long now)
    {
        if (!_dirty || _builtOnce && now - _builtTick < Math.Max(1000, EveryMs))
        {
            return;
        }

        _dirty = false;
        _builtOnce = true;
        _builtTick = now;
        Built++;
        _json = Build();
    }

    private static string Build() =>
        BotJson.Object(
            w =>
            {
                w.WriteString("at", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", Invariant));
                w.WriteNumber("recorded", Recorded);
                w.WriteNumber("dropped", Dropped);
                w.WritePropertyName("days");
                w.WriteStartArray();

                for (var k = _keys.Count - 1; k >= 0; k--)
                {
                    var day = _days[_keys[k]];

                    w.WriteStartObject();
                    w.WriteString("day", day.Key);
                    w.WriteString("title", Title(day));
                    w.WriteNumber("count", day.Entries.Count);
                    w.WriteString("text", Prose(day));

                    if (day.Voiced is { Text: { } voiced } && day.Voiced.Hash == Hash(day))
                    {
                        w.WriteString("voiced", voiced);
                    }
                    else
                    {
                        w.WriteNull("voiced");
                    }

                    w.WritePropertyName("entries");
                    w.WriteStartArray();

                    for (var i = 0; i < day.Entries.Count; i++)
                    {
                        var entry = day.Entries[i];

                        w.WriteStartObject();
                        w.WriteString("at", entry.At.ToString("HH:mm", Invariant));
                        w.WriteString("kind", entry.Kind);
                        w.WriteString("text", entry.Text);
                        w.WriteNumber("weight", Math.Round(entry.Weight));
                        w.WriteEndObject();
                    }

                    w.WriteEndArray();
                    w.WriteEndObject();
                }

                w.WriteEndArray();
            }
        );

    public static string Title(BotChronicleDay day) => day.Date.ToString("dddd, d MMMM yyyy", Invariant);

    public static string Prose(BotChronicleDay day)
    {
        if (day.Entries.Count == 0)
        {
            return $"{Title(day)}. Nothing the chronicle keeps has happened yet.";
        }

        using var sb = ValueStringBuilder.Create(1024);

        sb.Append($"{Title(day)}.");

        for (var o = 0; o < Order.Length; o++)
        {
            var kind = Order[o];
            var first = true;

            for (var i = 0; i < day.Entries.Count; i++)
            {
                var entry = day.Entries[i];

                if (entry.Kind != kind)
                {
                    continue;
                }

                if (first)
                {
                    sb.Append($"\n\n{Heading(kind)}. ");
                }
                else
                {
                    sb.Append(' ');
                }

                sb.Append($"At {entry.At.ToString("HH:mm", Invariant)}, {entry.Text}");
                first = false;
            }
        }

        return sb.ToString();
    }

    private static string Heading(string kind) =>
        kind switch
        {
            "war"       => "War",
            "guild"     => "The guilds",
            "champion"  => "The championship",
            "relic"     => "Relics and banners",
            "contest"   => "Bouts between the guilds",
            "diplomacy" => "The duke's words",
            "politics"  => "Offices",
            "town"      => "The towns",
            "crime"     => "Crime",
            _           => "The roads"
        };

    public static List<string> Facts(BotChronicleDay day)
    {
        var picked = new List<BotChronicleEntry>(day.Entries);

        if (picked.Count > Math.Max(1, VoiceMostFacts))
        {
            picked.Sort(static (a, b) => b.Weight.CompareTo(a.Weight));
            picked.RemoveRange(Math.Max(1, VoiceMostFacts), picked.Count - Math.Max(1, VoiceMostFacts));
            picked.Sort(static (a, b) => a.At.CompareTo(b.At));
        }

        var facts = new List<string>(picked.Count);

        for (var i = 0; i < picked.Count; i++)
        {
            facts.Add($"{picked[i].At.ToString("HH:mm", Invariant)} — {picked[i].Text}");
        }

        return facts;
    }

    public static ulong Hash(BotChronicleDay day) => day.Hashed ??= BotStoryWriter.Hash(Facts(day));

    public static BotChronicleDay Unvoiced()
    {
        var today = DateTime.Now.ToString("yyyy-MM-dd", Invariant);

        for (var k = _keys.Count - 1; k >= 0; k--)
        {
            if (string.CompareOrdinal(_keys[k], today) >= 0)
            {
                continue;
            }

            var day = _days[_keys[k]];

            if (day.Entries.Count < Math.Max(1, VoiceLeast))
            {
                continue;
            }

            if (day.Voiced == null || day.Voiced.Hash != Hash(day))
            {
                return day;
            }
        }

        return null;
    }

    public static void Voice(string key, ulong hash, string text)
    {
        if (!_days.TryGetValue(key, out var day))
        {
            return;
        }

        day.Voiced = new BotStoryProse { Hash = hash, Text = text, At = DateTime.UtcNow };
        _dirty = true;
    }

    public static int Voiced()
    {
        var n = 0;

        foreach (var (_, day) in _days)
        {
            if (day.Voiced is { Text: not null })
            {
                n++;
            }
        }

        return n;
    }

    public static int Today() => _days.TryGetValue(DateTime.Now.ToString("yyyy-MM-dd", Invariant), out var day) ? day.Entries.Count : 0;

    public static string Kinds()
    {
        var kinds = "";

        foreach (var (kind, n) in _byKind)
        {
            kinds += (kinds.Length == 0 ? "" : ", ") + $"{kind} {n}";
        }

        return kinds.Length == 0 ? "none" : kinds;
    }

    public static void Save(IGenericWriter writer)
    {
        writer.WriteEncodedInt(_keys.Count);

        for (var k = 0; k < _keys.Count; k++)
        {
            var day = _days[_keys[k]];

            writer.Write(day.Key);
            writer.Write(day.Date);
            writer.WriteEncodedInt(day.Entries.Count);

            for (var i = 0; i < day.Entries.Count; i++)
            {
                var entry = day.Entries[i];

                writer.Write(entry.At);
                writer.Write(entry.Kind);
                writer.Write(entry.Text);
                writer.Write(entry.Weight);
            }

            writer.Write(day.Voiced != null);

            if (day.Voiced != null)
            {
                writer.Write(day.Voiced.Hash);
                writer.Write(day.Voiced.Text);
                writer.Write(day.Voiced.At);
            }
        }
    }

    public static int Load(IGenericReader reader)
    {
        var count = reader.ReadEncodedInt();

        for (var k = 0; k < count; k++)
        {

            var day = new BotChronicleDay { Key = reader.ReadString(), Date = reader.ReadDateTime().ToLocalTime().Date };
            var entries = reader.ReadEncodedInt();

            for (var i = 0; i < entries; i++)
            {
                day.Entries.Add(
                    new BotChronicleEntry
                    {
                        At = reader.ReadDateTime().ToLocalTime(),
                        Kind = reader.ReadString(),
                        Text = reader.ReadString(),
                        Weight = reader.ReadDouble()
                    }
                );
            }

            if (reader.ReadBool())
            {
                day.Voiced = new BotStoryProse { Hash = reader.ReadULong(), Text = reader.ReadString(), At = reader.ReadDateTime() };
            }

            if (!string.IsNullOrEmpty(day.Key))
            {
                Put(day);
            }
        }

        return count;
    }

    public static void Forget()
    {
        Recorded = 0;
        Repeats = 0;
        Dropped = 0;
        Built = 0;
        _byKind.Clear();
        _founded.Clear();
        _closed.Clear();
        _dirty = true;
        _builtOnce = false;
    }

    public static int Wipe()
    {
        var days = _days.Count;

        _days.Clear();
        _keys.Clear();
        Forget();
        _json = "{\"days\":[]}";

        return days;
    }
}
