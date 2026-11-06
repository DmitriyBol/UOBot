using System;
using System.IO;
using Server.Logging;

namespace Server.BotAI.Mind;

public static class BotMindLog
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotMindLog));

    private static string _path;

    private static bool _broken;

    public static long Lines { get; private set; }

    public static string Path => _path;

    public static void Open()
    {
        _broken = false;
        Lines = 0;

        try
        {

            var folder = System.IO.Path.GetFullPath(System.IO.Path.Combine(Core.BaseDirectory, "..", "logs"));

            Directory.CreateDirectory(folder);

            _path = System.IO.Path.Combine(folder, "bot-minds.log");

            Write($"==== minds awake, {BotOllama.Model} at {BotOllama.Endpoint} ====");
        }
        catch (Exception e)
        {
            _broken = true;

            logger.Warning("The minds' own log could not be opened, so thinking will only appear here: {Message}", e.Message);
        }
    }

    public static void Write(string line)
    {
        if (_broken || _path == null || line == null)
        {
            return;
        }

        try
        {
            File.AppendAllText(_path, $"[{DateTime.Now:HH:mm:ss}] {line}{Environment.NewLine}");
            Lines++;
        }
        catch (Exception e)
        {
            _broken = true;

            logger.Warning("The minds' own log stopped taking lines and is switched off: {Message}", e.Message);
        }
    }

    public static void Write(string who, string heading, string body)
    {
        Write($"{who} — {heading}");

        if (!string.IsNullOrWhiteSpace(body))
        {
            Write($"    {body.Replace("\n", " ").Replace("\r", " ").Trim()}");
        }
    }
}
