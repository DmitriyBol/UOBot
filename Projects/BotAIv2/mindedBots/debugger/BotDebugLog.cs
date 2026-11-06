using System;
using System.IO;
using Server.Logging;

namespace Server.BotAI.Mind;

public static class BotDebugLog
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotDebugLog));

    private static string _path;

    private static bool _broken;

    public static long Lines { get; private set; }

    public static string Path => _path;

    public static void Open(string who)
    {
        _broken = false;
        Lines = 0;

        try
        {
            var folder = System.IO.Path.GetFullPath(System.IO.Path.Combine(Core.BaseDirectory, "..", "logs"));

            Directory.CreateDirectory(folder);

            _path = System.IO.Path.Combine(folder, "bot-debugger.log");

            Rule();

            Write($"{who} is awake, thinking with {BotVigil.Model} at {BotOllama.Endpoint}");
            Rule();
        }
        catch (Exception e)
        {
            _broken = true;

            logger.Warning("The debugger's own log could not be opened, so it will only speak here: {Message}", e.Message);
        }
    }

    public static void Rule() => Write(new string('=', 96));

    public static void Write(string line)
    {

        try
        {
            BotDebugWeb.Heard(line);
        }
        catch
        {

        }

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

            logger.Warning("The debugger's own log stopped taking lines and is switched off: {Message}", e.Message);
        }
    }

    public static void Block(string heading, string body)
    {
        Write(heading);

        if (string.IsNullOrWhiteSpace(body))
        {
            return;
        }

        var lines = body.Replace("\r", "").Split('\n');

        for (var i = 0; i < lines.Length; i++)
        {
            if (!string.IsNullOrWhiteSpace(lines[i]))
            {
                Write($"    {lines[i].TrimEnd()}");
            }
        }
    }
}
