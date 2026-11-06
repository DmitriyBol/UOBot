using System;
using System.IO;
using Server.Logging;

namespace Server.BotAI.V2;

public static class BotTail
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotTail));

    public static int MostBytes { get; set; } = 2000000;

    private static string _path;

    private static long _at;

    private static bool _broken;

    public static long Errors { get; private set; }

    public static long Skips { get; private set; }

    public static string Worst { get; private set; }

    public static string Path => _path;

    public static void Open()
    {
        _broken = false;
        _at = 0;
        Errors = 0;
        Skips = 0;
        Worst = null;

        try
        {
            var folder = System.IO.Path.GetFullPath(System.IO.Path.Combine(Core.BaseDirectory, "..", "logs"));

            if (!Directory.Exists(folder))
            {
                _broken = true;

                return;
            }

            var newest = default(FileInfo);

            foreach (var name in Directory.GetFiles(folder, "session-*.log"))
            {
                var file = new FileInfo(name);

                if (newest == null || file.LastWriteTimeUtc > newest.LastWriteTimeUtc)
                {
                    newest = file;
                }
            }

            if (newest == null)
            {
                _broken = true;

                logger.Warning("No session log to watch for errors, so the alarm channel will not count them");

                return;
            }

            _path = newest.FullName;
            _at = newest.Length;

            logger.Information("Errors will be counted from {Path}, starting at its end ({At} bytes in)", _path, _at);
        }
        catch (Exception e)
        {
            _broken = true;

            logger.Warning("The error tail could not be opened: {Message}", e.Message);
        }
    }

    public static int Since()
    {
        if (_broken || _path == null)
        {
            return 0;
        }

        try
        {

            using var file = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

            if (file.Length < _at)
            {

                _at = 0;
            }

            var behind = file.Length - _at;

            if (behind <= 0)
            {
                return 0;
            }

            if (behind > MostBytes)
            {
                _at = file.Length;
                Skips++;

                return 0;
            }

            file.Seek(_at, SeekOrigin.Begin);

            using var reader = new StreamReader(file);

            var found = 0;
            string line;

            while ((line = reader.ReadLine()) != null)
            {
                if (line.Contains(" ERR]", StringComparison.Ordinal))
                {
                    found++;
                    Errors++;
                    Worst = line.Length > 220 ? line[..220] : line;
                }
            }

            _at = file.Length;

            return found;
        }
        catch (Exception e)
        {
            logger.Warning("The error tail stopped reading: {Message}", e.Message);

            _broken = true;

            return 0;
        }
    }

    public static string Describe() =>
        _broken || _path == null
            ? "errors are not being counted; no session log is being read"
            : $"{Errors} errors seen in {System.IO.Path.GetFileName(_path)} since the shard came up, {Skips} times too far behind to read";

    public static void Forget()
    {
        _at = 0;
        Errors = 0;
        Skips = 0;
        Worst = null;
    }
}
