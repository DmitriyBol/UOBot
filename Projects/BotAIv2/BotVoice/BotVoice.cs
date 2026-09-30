using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Server.Guilds;
using Server.Logging;
using Server.Network;
using Server.Text;

namespace Server.BotAI.V2;

/// <summary>
/// The bots' voices: what they say out loud, to whom, and how often.
///
/// <para>
/// <b>Speech is a by-product of doing, never a thing a bot decides to do.</b> Nothing here is offered to
/// the auction and nothing costs a beat. A line rides on an event that was happening anyway — work taken
/// up or finished, a blow landed, a war declared — and the only decisions taken here are whether to say it
/// and in which channel. That keeps the voices from ever changing what the population does, which is the
/// invariant every observer on this shard has to keep.
/// </para>
///
/// <para>
/// <b>Five channels, and the channel is the audience.</b> <c>local</c> is said overhead where the bot stands;
/// <c>guild</c> goes to the guild's chat and overhead with the guild's letters; <c>world</c> is shouted and,
/// if allowed, broadcast to every client as the crier would; <c>mood</c> is a local line about how the bot
/// feels; <c>cry</c> is a yell for help. Every line, whatever the channel, is also an event on the stream
/// and a line in <c>logs/bot-speech.log</c>, because the person watching is more often at a page than in
/// the world.
/// </para>
///
/// <para>
/// <b>Rationed per bot, not per shard.</b> Fifty bots each saying something every fifteen seconds is three
/// lines a second, which reads as a crowd; one bot saying something every second is a fault. The throttle
/// is by bot and channel, and a cry for help is exempt from everything but its own shorter clock.
/// </para>
/// </summary>
public static class BotVoice
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotVoice));

    public static bool Enabled { get; set; } = true;

    public static bool WorldOn { get; set; } = true;

    public static bool GuildOn { get; set; } = true;

    public static bool LocalOn { get; set; } = true;

    public static bool MoodOn { get; set; } = true;

    public static bool CryOn { get; set; } = true;

    public static int SayEveryMs { get; set; } = 15000;

    public static int CryEveryMs { get; set; } = 20000;

    public static int MoodEveryMs { get; set; } = 900000;

    public static double WorkChance { get; set; } = 0.35;

    public static double FailChance { get; set; } = 0.6;

    public static double DropChance { get; set; } = 0.15;

    public static int MostLetters { get; set; } = 160;

    public static int WorldHue { get; set; } = 0x59;

    public static int GuildHue { get; set; } = 0x3B2;

    public static int CryHue { get; set; } = 0x22;

    public static int MoodHue { get; set; } = 0x3B2;

    public static bool Broadcast { get; set; } = true;

    public static double Desperate { get; set; } = 0.3;

    public static double Hurt { get; set; } = 0.7;

    public static long Said { get; private set; }

    public static long Held { get; private set; }

    public static long Cries { get; private set; }

    public static long Moods { get; private set; }

    public static long Shouts { get; private set; }

    public static long GuildLines { get; private set; }

    private static readonly Dictionary<string, string[]> _phrases = BotPhrases.Defaults();

    private static readonly Dictionary<(Serial, string), long> _last = [];

    private static readonly Dictionary<Serial, string> _lastLine = [];

    private static readonly Dictionary<Serial, long> _moodTick = [];

    private static readonly Dictionary<Serial, Serial> _lastFoe = [];

    private static StreamWriter _log;

    public static string LogPath { get; private set; }

    public static void Open()
    {
        try
        {
            var folder = Path.GetFullPath(Path.Combine(Core.BaseDirectory, "..", "logs"));

            Directory.CreateDirectory(folder);

            LogPath = Path.Combine(folder, "bot-speech.log");
            _log = new StreamWriter(new FileStream(LogPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite), new UTF8Encoding(false))
            {
                AutoFlush = true
            };

            _log.WriteLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss} --- the shard is up; the bots may speak ---");
        }
        catch (Exception e)
        {
            logger.Warning("The speech log could not be opened: {Message}", e.Message);
        }
    }

    public static void Phrases(Dictionary<string, string[]> bank)
    {
        foreach (var (key, lines) in bank)
        {
            if (!string.IsNullOrWhiteSpace(key) && lines is { Length: > 0 })
            {
                _phrases[key] = lines;
            }
        }
    }

    public static bool Has(string occasion) => _phrases.ContainsKey(occasion);

    public static string Phrase(string occasion, string fallback, Mobile who, Dictionary<string, string> fill)
    {
        if (!_phrases.TryGetValue(occasion, out var lines) && (fallback == null || !_phrases.TryGetValue(fallback, out lines)))
        {
            return null;
        }

        if (lines == null || lines.Length == 0)
        {
            return null;
        }

        var line = lines[Utility.Random(lines.Length)];

        if (lines.Length > 1 && who != null && _lastLine.TryGetValue(who.Serial, out var last) && last == line)
        {
            line = lines[(Array.IndexOf(lines, line) + 1) % lines.Length];
        }

        return Fill(line, who, fill);
    }

    private static string Fill(string line, Mobile who, Dictionary<string, string> fill)
    {
        var text = line;

        if (who != null)
        {
            text = text.Replace("{name}", who.Name ?? "somebody");
            text = text.Replace("{class}", (who as BotMobile)?.Class?.Name ?? "");
            text = text.Replace("{guild}", (who.Guild as Guild)?.Name ?? "the guild");
            text = text.Replace("{place}", $"({who.X}, {who.Y})");
        }

        if (fill != null)
        {
            foreach (var (key, value) in fill)
            {
                text = text.Replace("{" + key + "}", value ?? "");
            }
        }

        var open = text.IndexOf('{');

        while (open >= 0)
        {
            var close = text.IndexOf('}', open);

            if (close < 0)
            {
                break;
            }

            text = text.Remove(open, close - open + 1);
            open = text.IndexOf('{');
        }

        text = text.Replace("  ", " ").Trim();

        return text.Length > 0 && char.IsLower(text[0]) ? char.ToUpperInvariant(text[0]) + text[1..] : text;
    }

    public static bool Say(Mobile who, string channel, string text, bool force = false)
    {
        if (!Enabled || who is not { Deleted: false } || string.IsNullOrWhiteSpace(text) || who.Map == null || who.Map == Map.Internal)
        {
            return false;
        }

        channel ??= "local";

        var on = channel switch
        {
            "world" => WorldOn,
            "guild" => GuildOn,
            "mood" => MoodOn,
            "cry" => CryOn,
            _ => LocalOn
        };

        if (!on)
        {
            return false;
        }

        var now = Core.TickCount;
        var every = channel == "cry" ? CryEveryMs : SayEveryMs;

        if (!force && _last.TryGetValue((who.Serial, channel), out var last) && now - last < every)
        {
            Held++;

            return false;
        }

        _last[(who.Serial, channel)] = now;

        var line = text.Replace('\n', ' ').Replace('\r', ' ').Trim();

        if (line.Length > MostLetters)
        {
            line = line[..MostLetters];
        }

        _lastLine[who.Serial] = line;

        try
        {
            Deliver(who, channel, line);
        }
        catch (Exception e)
        {
            logger.Warning("{Name} could not say a line: {Message}", who.Name, e.Message);
        }

        Said++;

        switch (channel)
        {
            case "cry":
                Cries++;

                break;
            case "mood":
                Moods++;

                break;
            case "world":
                Shouts++;

                break;
            case "guild":
                GuildLines++;

                break;
        }

        BotEvents.Post("say", who, line, channel);
        Record(who, channel, line);

        return true;
    }

    public static void Aloud(Mobile who, string text) => Say(who, "local", text, force: true);

    private static void Deliver(Mobile who, string channel, string line)
    {
        var guild = who.Guild as Guild;

        switch (channel)
        {
            case "world":
                who.PublicOverheadMessage(MessageType.Yell, WorldHue, true, line);

                if (Broadcast && NetState.Instances.Count > 0)
                {
                    var tag = guild == null ? "" : $" [{guild.Abbreviation}]";

                    World.Broadcast(WorldHue, true, $"{who.Name}{tag}: {line}");
                }

                break;
            case "guild":
                if (guild != null)
                {
                    guild.GuildChat(who, GuildHue, line);
                    who.PublicOverheadMessage(MessageType.Regular, GuildHue, true, $"[{guild.Abbreviation}] {line}");
                }
                else
                {
                    who.PublicOverheadMessage(MessageType.Regular, GuildHue, true, line);
                }

                break;
            case "cry":
                who.PublicOverheadMessage(MessageType.Yell, CryHue, true, line);

                break;
            case "mood":
                who.PublicOverheadMessage(MessageType.Regular, MoodHue, true, line);

                break;
            default:
                who.Say(line);

                break;
        }
    }

    private static void Record(Mobile who, string channel, string line)
    {
        if (_log == null)
        {
            return;
        }

        try
        {
            var guild = (who.Guild as Guild)?.Abbreviation;

            _log.WriteLine($"{DateTime.Now:HH:mm:ss} [{channel}] {who.Name}{(guild == null ? "" : $" ({guild})")} @{who.X},{who.Y}: {line}");
        }
        catch
        {
        }
    }

    public static void Took(Mobile who, BotDeed deed, string because)
    {
        if (!Enabled || who == null || deed == null)
        {
            return;
        }

        if (deed is BotOrder order)
        {
            var fill = new Dictionary<string, string>
            {
                ["item"] = Words(order.Wanted?.Name),
                ["amount"] = order.Units.ToString(),
                ["price"] = order.Price.ToString(),
                ["stage"] = deed.Stage ?? ""
            };

            var seek = Phrase("seek", null, who, fill);

            if (seek != null)
            {
                Say(who, "world", seek);
            }

            return;
        }

        if (Utility.RandomDouble() >= WorkChance)
        {
            return;
        }

        var line = Phrase($"took:{deed.Kind}", "took:*", who, WorkFill(deed, null, 0, 0));

        if (line != null)
        {
            Say(who, "local", line);
        }
    }

    public static void Ended(Mobile who, BotDeed deed, BotEnding ending, string why, double minutes, double perMinute, int coin)
    {
        if (!Enabled || who == null || deed == null || ending == BotEnding.Died)
        {
            return;
        }

        var (occasion, chance) = ending switch
        {
            BotEnding.Done => ("finished", WorkChance),
            BotEnding.Failed => ("failed", FailChance),
            _ => ("dropped", DropChance)
        };

        if (deed.Kind != BotBolt.Trade && Utility.RandomDouble() >= chance)
        {
            return;
        }

        var line = Phrase($"{occasion}:{deed.Kind}", $"{occasion}:*", who, WorkFill(deed, why, minutes, coin));

        if (line != null)
        {
            Say(who, "local", line);
        }
    }

    private static Dictionary<string, string> WorkFill(BotDeed deed, string why, double minutes, int coin) =>
        new()
        {
            ["work"] = deed.Kind,
            ["stage"] = deed.Stage ?? deed.Kind,
            ["reason"] = Short(why),
            ["minutes"] = minutes.ToString("F0"),
            ["coin"] = coin.ToString()
        };

    public static void Struck(BotMobile who, Mobile from)
    {
        if (!Enabled || !CryOn || who == null || from == null || !who.Alive || who.HitsMax <= 0)
        {
            return;
        }

        var share = (double)who.Hits / who.HitsMax;
        var fresh = !_lastFoe.TryGetValue(who.Serial, out var foe) || foe != from.Serial;

        _lastFoe[who.Serial] = from.Serial;

        if (!(share <= Desperate || (fresh && share < Hurt)))
        {
            return;
        }

        var fill = new Dictionary<string, string> { ["foe"] = Words(from.Name) };
        var line = share <= Desperate ? Phrase("cry:low", "cry", who, fill) : Phrase("cry", null, who, fill);

        if (line != null)
        {
            Say(who, "cry", line);
        }
    }

    public static void Died(BotMobile who, Mobile killer)
    {
        if (!Enabled || who == null)
        {
            return;
        }

        _lastFoe.Remove(who.Serial);

        var fill = new Dictionary<string, string> { ["killer"] = Words(killer?.Name) ?? "something" };
        var line = Phrase("died", null, who, fill);

        if (line != null)
        {
            Say(who, "local", line, force: true);
        }

        if (who.Guild is Guild guild)
        {
            var speaker = Spokesman(guild, who);

            if (speaker != null)
            {
                var fell = Phrase("guild:fell", null, speaker, new Dictionary<string, string> { ["name"] = who.Name, ["place"] = $"({who.X}, {who.Y})" });

                if (fell != null)
                {
                    Say(speaker, "guild", fell, force: true);
                }
            }
        }
    }

    public static bool Mood(BotMobile who, long now)
    {
        if (!Enabled || !MoodOn || who is not { Deleted: false, Alive: true })
        {
            return false;
        }

        if (!_moodTick.TryGetValue(who.Serial, out var last))
        {
            _moodTick[who.Serial] = now - Utility.Random(Math.Max(1, MoodEveryMs));

            return false;
        }

        if (now - last < MoodEveryMs)
        {
            return false;
        }

        _moodTick[who.Serial] = now;

        var mood = who.Mood;
        var occasion = mood >= 0.66 ? "mood:high" : mood >= 0.33 ? "mood:mid" : "mood:low";
        var line = Phrase(occasion, null, who, new Dictionary<string, string> { ["mood"] = mood.ToString("P0") });

        if (line != null)
        {
            Say(who, "mood", line);
        }

        return true;
    }

    public static void ToGuild(string guildName, string occasion, Dictionary<string, string> fill)
    {
        if (!Enabled || !GuildOn || guildName == null)
        {
            return;
        }

        if (BaseGuild.FindByName(guildName) is not Guild guild)
        {
            return;
        }

        var speaker = Spokesman(guild, null);

        if (speaker == null)
        {
            return;
        }

        fill ??= [];
        fill.TryAdd("guild", guild.Name);

        var line = Phrase(occasion, null, speaker, fill);

        if (line != null)
        {
            Say(speaker, "guild", line, force: true);
        }
    }

    private static Mobile Spokesman(Guild guild, Mobile except)
    {
        if (guild.Leader is { Deleted: false, Alive: true } leader && leader != except && leader.Map != Map.Internal)
        {
            return leader;
        }

        var members = guild.Members;

        for (var i = 0; i < members.Count; i++)
        {
            var m = members[i];

            if (m is { Deleted: false, Alive: true } && m != except && m.Map != Map.Internal)
            {
                return m;
            }
        }

        return null;
    }

    public static (string Text, DateTime At)? LastOf(Mobile who)
    {
        var e = BotEvents.LastSaid(who?.Name);

        return e == null ? null : (e.Text, e.At);
    }

    private static string Short(string why)
    {
        if (string.IsNullOrEmpty(why))
        {
            return "";
        }

        var cut = why.IndexOf(':');
        var text = cut > 0 && cut < 60 ? why[..cut] : why;

        if (text.Length <= 60)
        {
            return text;
        }

        var space = text.LastIndexOf(' ', 60);

        return space > 30 ? text[..space] : text[..60];
    }

    public static string Words(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return null;
        }

        using var sb = ValueStringBuilder.Create(name.Length + 8);

        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];

            if (i > 0 && char.IsUpper(c) && !char.IsUpper(name[i - 1]))
            {
                sb.Append(' ');
            }

            sb.Append(i == 0 ? c : char.ToLowerInvariant(c));
        }

        return sb.ToString();
    }

    public static void Forget()
    {
        _last.Clear();
        _lastLine.Clear();
        _moodTick.Clear();
        _lastFoe.Clear();
    }

    public static string Describe() =>
        !Enabled
            ? "the bots are silent"
            : $"{Said} lines said ({Shouts} to the world, {GuildLines} to guilds, {Cries} cries, {Moods} moods), {Held} held back by the clock";
}
