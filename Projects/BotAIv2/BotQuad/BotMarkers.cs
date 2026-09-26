using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using Server.Logging;

namespace Server.BotAI.V2;

/// <summary>
/// Writes what the population knows about the island into the client's own world-map pins.
///
/// <para>
/// <b>The one thing on this shard that a person reads without the server telling them anything.</b> Every
/// other view of this project is a log line or a gump: both require somebody to go and look at the right
/// place at the right moment. A pin sits on the world map inside the client the whole time, so "which parts
/// of the island are dangerous" stops being a question anybody has to ask and becomes something they simply
/// see while doing something else.
/// </para>
///
/// <para>
/// <b>The client reads this file once, when it opens its map.</b> Nothing here pushes anything to a running
/// client: the file is rewritten on a slow clock and the map picks it up next time it is opened. That is a
/// property of ClassicUO and not a choice made here, and it is why the clock is minutes rather than seconds
/// — writing it faster would cost the same and change nothing anybody can see.
/// </para>
///
/// <para>
/// <b>Written from the loop rather than from a thread, and measured rather than assumed.</b> The threading
/// rule in this repository says heavy external I/O belongs off the loop; this is a few tens of kilobytes
/// written once every few minutes, which is not heavy, and a background writer would need its own snapshot
/// of a table the loop is mutating. The cost of each write is logged — see <see cref="Spent"/> — so the
/// assumption is checked by the shard itself rather than by me. If it ever stops being a fraction of a
/// millisecond, that number is the argument for moving it.
/// </para>
/// </summary>
public static class BotMarkers
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotMarkers));

    public static string Path { get; set; } = @"..\..\ClassicUO\Data\Client\userMarkers.usr";

    public static int EveryMs { get; set; } = 60000;

    public static int Most { get; set; } = 10000;

    public static bool PinUnknown { get; set; }

    private static long _tick;

    private static bool _started;

    private static bool _complained;

    public static int Written { get; private set; }

    public static long Writes { get; private set; }

    public static double Spent { get; private set; }

    public static void Tick()
    {
        var now = Core.TickCount;

        if (!_started)
        {
            _started = true;
            _tick = now;

            Write();

            return;
        }

        if (now - _tick < EveryMs)
        {
            return;
        }

        _tick = now;

        Write();
    }

    public static void Write()
    {
        var quads = BotQuad.Worst(0);

        if (quads.Count == 0)
        {
            return;
        }

        var watch = Stopwatch.StartNew();

        var text = new StringBuilder(quads.Count * 64);
        var written = 0;

        for (var i = 0; i < quads.Count && written < Most; i++)
        {
            var quad = quads[i];

            if (!quad.Trodden && !PinUnknown)
            {
                continue;
            }

            var reading = BotQuad.Reading(quad);

            if (!PinUnknown
                && reading > BotQuad.Unsafe
                && reading < BotQuad.Safest
                && quad.Blows == 0
                && quad.Deaths == 0)
            {
                continue;
            }

            var middle = quad.Middle;

            text.Append(middle.X).Append(',')
                .Append(middle.Y).Append(",0,")
                .Append(Label(quad, reading))
                .Append(",,")
                .Append(Colour(quad, reading))
                .Append(",3\n");

            written++;
        }

        try
        {
            var full = System.IO.Path.GetFullPath(Path);
            var folder = System.IO.Path.GetDirectoryName(full);

            if (folder == null || !Directory.Exists(folder))
            {
                Complain("there is no folder at {Where}", full);

                return;
            }

            File.WriteAllText(full, text.ToString());
        }
        catch (Exception e)
        {
            Complain("the file at {Where} could not be written: " + e.Message, Path);

            return;
        }

        watch.Stop();

        Written = written;
        Writes++;
        Spent = watch.Elapsed.TotalMilliseconds;
        _complained = false;

        logger.Information(
            "The world map has {Count} pins on it, written in {Spent:F1}ms",
            written,
            Spent
        );
    }

    private static string Label(BotQuad.Quad quad, double reading)
    {
        var text = new StringBuilder(24);

        text.Append(reading.ToString("F1", System.Globalization.CultureInfo.InvariantCulture));

        var held = BotClaim.Pin(
            quad.Map,
            new Point3D(
                quad.X * BotQuad.Side + BotQuad.Side / 2,
                quad.Y * BotQuad.Side + BotQuad.Side / 2,
                0
            )
        );

        if (held != null)
        {
            text.Append(" - ").Append(held);
        }

        return text.ToString();
    }

    private static string Colour(BotQuad.Quad quad, double reading)
    {
        if (!quad.Trodden)
        {
            return "purple";
        }

        if (reading <= BotQuad.Bleakest)
        {
            return "red";
        }

        if (reading <= BotQuad.Unsafe)
        {
            return "yellow";
        }

        if (reading >= BotQuad.Safest)
        {
            return "green";
        }

        return reading >= BotQuad.Positive ? "blue" : "white";
    }

    private static void Complain(string what, object where)
    {
        if (_complained)
        {
            return;
        }

        _complained = true;

        logger.Warning("The world map pins were not written: " + what, where);
    }

    public static string Describe() =>
        Writes == 0
            ? "no world-map pins have been written yet"
            : $"{Written} pins written {Writes} times, last of them in {Spent:F1}ms, at {Path}";

    public static void Forget()
    {
        Written = 0;
        Writes = 0;
        Spent = 0.0;
        _started = false;
        _complained = false;
    }
}
