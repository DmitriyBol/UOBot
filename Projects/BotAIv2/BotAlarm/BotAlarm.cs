using System;
using System.Collections.Generic;
using System.IO;
using Server.Logging;
using Server.Text;

namespace Server.BotAI.V2;

/// <summary>
/// The channel the shard raises its own alarms into: one line of JSON per event in
/// <c>logs/alerts.ndjson</c>, meant to be watched rather than searched.
///
/// <para>
/// <b>It exists because everything the shard knows had to be asked for.</b> The session log says everything
/// and therefore says nothing without a question; the debugger answers when spoken to; the summaries come on
/// a clock whether or not anything has happened. So the shard could be broken for half an hour before
/// anybody looked, and the half hour was the expensive part — the evidence for what caused it is in that
/// same half hour, and it scrolls past either way.
/// </para>
///
/// <para>
/// <b>Every event carries a numerator, a denominator and a window, or it is not raised.</b> This shard has
/// produced twelve distinct kinds of false alarm and the majority were shapes of the same fault: a count
/// with nothing to divide it by, or a total that had been accumulating since boot being read as the state
/// of this minute. <see cref="Raise"/> takes <c>n</c>, <c>of</c> and <c>window</c> as arguments rather than
/// as advice, so a rule that cannot say what it counted cannot be written.
/// </para>
///
/// <para>
/// <b>An alarm stays one event while it lasts.</b> A condition true on every tick would otherwise write a
/// line every tick and bury the channel in the very thing it is reporting. The first crossing is written;
/// repeats are counted and said once per <see cref="RestMs"/>; the return to normal is written as its own
/// event, because a watcher who is told only when things break can never be told that they are fixed.
/// </para>
///
/// <para>
/// <b>And it says so when nothing is wrong.</b> <see cref="Alive"/> writes a quiet heartbeat on a slow
/// clock. Silence on this channel would otherwise be indistinguishable from a channel that has stopped
/// working, which is exactly how an instrument fails without anybody noticing — the debugger has the same
/// rule, and the same reason: an observer with no way to say "all is well" is one whose silence means
/// nothing.
/// </para>
/// </summary>
public static class BotAlarm
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotAlarm));

    public static int RestMs { get; set; } = 900000;

    public static int MostChars { get; set; } = 400;

    private sealed class Held
    {
        public DateTime Since;

        public long Repeats;

        public long SaidTick;
    }

    private static readonly Dictionary<string, Held> _up = new();

    private static string _path;

    private static bool _broken;

    public static long Written { get; private set; }

    public static long Swallowed { get; private set; }

    public static int Standing => _up.Count;

    public static string Path => _path;

    public static void Open()
    {
        _broken = false;
        _up.Clear();
        Written = 0;
        Swallowed = 0;

        try
        {
            var folder = System.IO.Path.GetFullPath(System.IO.Path.Combine(Core.BaseDirectory, "..", "logs"));

            Directory.CreateDirectory(folder);

            _path = System.IO.Path.Combine(folder, "alerts.ndjson");

            Note("shard", "the shard is up and the channel is open", 0, 0, "-");

            logger.Information("The alarm channel is open at {Path}: one line of JSON per event", _path);
        }
        catch (Exception e)
        {
            _broken = true;

            logger.Warning("The alarm channel could not be opened: {Message}", e.Message);
        }
    }

    public static void Raise(string kind, string say, long n, long of, string window)
    {
        if (_broken || kind == null)
        {
            return;
        }

        var now = Core.TickCount;

        if (_up.TryGetValue(kind, out var standing))
        {
            standing.Repeats++;

            if (now - standing.SaidTick < RestMs)
            {
                Swallowed++;

                return;
            }

            standing.SaidTick = now;

            Write("still", kind, say, n, of, window, standing);

            return;
        }

        standing = new Held { Since = DateTime.Now, SaidTick = now };
        _up[kind] = standing;

        Write("raised", kind, say, n, of, window, standing);
    }

    public static void Clear(string kind, string say, long n, long of, string window)
    {
        if (_broken || kind == null || !_up.TryGetValue(kind, out var standing))
        {
            return;
        }

        _up.Remove(kind);

        Write("clear", kind, say, n, of, window, standing);
    }

    public static bool Up(string kind) => kind != null && _up.ContainsKey(kind);

    public static void Note(string kind, string say, long n, long of, string window)
    {
        if (_broken)
        {
            return;
        }

        Write("note", kind, say, n, of, window, null);
    }

    public static void Alive(string say, long n, long of, string window) => Write("alive", "alive", say, n, of, window, null);

    private static void Write(string state, string kind, string say, long n, long of, string window, Held standing)
    {
        if (_broken || _path == null)
        {
            return;
        }

        var held = standing == null ? 0 : (int)(DateTime.Now - standing.Since).TotalMinutes;
        var repeats = standing?.Repeats ?? 0;
        var since = standing?.Since ?? DateTime.Now;

        try
        {
            using var line = ValueStringBuilder.Create(MostChars + 220);

            line.Append(
                $"{{\"at\":\"{DateTime.Now:yyyy-MM-ddTHH:mm:ss}\",\"state\":\"{state}\",\"kind\":\"{Escape(kind)}\","
                + $"\"n\":{n},\"of\":{of},\"window\":\"{Escape(window)}\",\"since\":\"{since:HH:mm:ss}\","
                + $"\"heldMinutes\":{held},\"repeats\":{repeats},\"say\":\"{Escape(Short(say))}\"}}"
            );

            File.AppendAllText(_path, line.ToString() + Environment.NewLine);

            Written++;
        }
        catch (Exception e)
        {
            _broken = true;

            logger.Warning("The alarm channel stopped taking events: {Message}", e.Message);
        }
    }

    private static string Short(string say)
    {
        if (say == null)
        {
            return "";
        }

        return say.Length <= MostChars ? say : say[..MostChars] + "...";
    }

    private static string Escape(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return "";
        }

        using var clean = ValueStringBuilder.Create(text.Length + 16);

        foreach (var c in text)
        {
            switch (c)
            {
                case '"':
                    {
                        clean.Append("\\\"");

                        break;
                    }
                case '\\':
                    {
                        clean.Append("\\\\");

                        break;
                    }
                case '\n':
                case '\r':
                case '\t':
                    {
                        clean.Append(' ');

                        break;
                    }
                default:
                    {
                        if (c < ' ')
                        {
                            clean.Append(' ');
                        }
                        else
                        {
                            clean.Append(c);
                        }

                        break;
                    }
            }
        }

        return clean.ToString();
    }

    public static string Describe() =>
        _broken
            ? "the alarm channel is not being written"
            : $"{Written} events written, {_up.Count} alarms standing, {Swallowed} repeats swallowed, at {_path}";

    public static void Forget()
    {
        _up.Clear();
    }
}
