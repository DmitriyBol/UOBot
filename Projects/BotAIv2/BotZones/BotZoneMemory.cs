using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Server.Logging;

namespace Server.BotAI.V2;

/// <summary>
/// What the zones learned, kept across a restart: each haunt's awake seconds, sightings, observed range and tour stops, and the
/// cells patrols were seen in.
///
/// <para>
/// <b>Because the shard restarts more often than the tour finishes.</b> A round of the tour is a few hundred zones at a minute
/// and a quarter each, shared by three or four walkers — hours — and the shard is redeployed about hourly. Without this every
/// session would begin again from the declared discs and walk the same first zones. With it, a new session starts from
/// everything the last one saw, and the tour goes on with the zones it had not reached.
/// </para>
///
/// <para>
/// <b>A plain file in <c>Data/</c>, not a save store.</b> The <c>Saves</c> folder is archived at every save, and a persistence
/// that cannot read its own shape stops the shard on a console prompt nobody can answer (see <c>BotQuadStore</c>). A file that
/// fails to read here is logged and ignored, and the zones start from the spawners, which is what they would do without it.
/// The file is read and written off the loop; what the loop does is copy numbers in and out.
/// </para>
/// </summary>
public static class BotZoneMemory
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotZoneMemory));

    private const int Shape = 1;

    public static bool Running { get; set; } = true;

    public static string File { get; set; } = "Data/bot-zones-seen.json";

    public static int SaveEveryMs { get; set; } = 600000;

    public static int MostCells { get; set; } = 80000;

    public static long Saves { get; private set; }

    public static long SaveFailures { get; private set; }

    public static int RestoredHaunts { get; private set; }

    public static int RestoredCells { get; private set; }

    public static double SaveMs { get; private set; }

    private static string _loaded = "nothing read yet";

    private static Remembered _pending;

    private static long _savedTick;

    private static bool _started;

    private static bool _writing;

    private static bool _read;

    private sealed class Remembered
    {
        public long SavedAt;

        public int Width;

        public readonly Dictionary<string, (double Awake, long Samples, int Range, long LastAwake, int Toured, long TouredAt)> Haunts = [];

        public readonly List<(int Cell, float Weight)> Cells = [];
    }

    private static string PathOf() => System.IO.Path.Combine(Core.BaseDirectory, File);

    public static void Load(Map map)
    {
        _pending = null;
        _started = true;
        _read = false;
        _savedTick = Core.TickCount;

        if (!Running)
        {
            _loaded = "switched off";
            _read = true;

            return;
        }

        var path = PathOf();

        _ = Task.Run(
            () =>
            {
                try
                {
                    if (!System.IO.File.Exists(path))
                    {
                        Core.LoopContext.Post(
                            () =>
                            {
                                _loaded = "no file yet";
                                _read = true;
                            }
                        );

                        return;
                    }

                    var read = Parse(System.IO.File.ReadAllBytes(path));

                    Core.LoopContext.Post(
                        () =>
                        {
                            _pending = read;
                            _read = true;
                            _loaded = read == null ? "a file of another shape, ignored" : $"read {read.Haunts.Count} haunts and {read.Cells.Count} cells";
                        }
                    );
                }
                catch (Exception e)
                {
                    Core.LoopContext.Post(
                        () =>
                        {
                            _loaded = $"unreadable ({e.Message}), ignored";
                            _read = true;
                            logger.Warning("Zones: {File} could not be read, so the zones start from the spawners: {Message}", File, e.Message);
                        }
                    );
                }
            }
        );
    }

    private static Remembered Parse(byte[] bytes)
    {
        using var document = JsonDocument.Parse(bytes);
        var root = document.RootElement;

        if (!root.TryGetProperty("shape", out var shape) || shape.GetInt32() != Shape)
        {
            return null;
        }

        var read = new Remembered
        {
            SavedAt = root.GetProperty("savedAt").GetInt64(),
            Width = root.GetProperty("width").GetInt32()
        };

        foreach (var h in root.GetProperty("haunts").EnumerateArray())
        {
            read.Haunts[h[0].GetString() ?? ""] = (h[1].GetDouble(), h[2].GetInt64(), h[3].GetInt32(), h[4].GetInt64(), h[5].GetInt32(), h[6].GetInt64());
        }

        foreach (var c in root.GetProperty("cells").EnumerateArray())
        {
            read.Cells.Add((c[0].GetInt32(), c[1].GetSingle()));
        }

        return read;
    }

    public static void Apply(long now)
    {
        var read = _pending;

        if (read == null || !BotHaunts.Surveyed)
        {
            return;
        }

        _pending = null;

        var haunts = BotHaunts.All;
        var restored = 0;

        for (var i = 0; i < haunts.Count; i++)
        {
            var haunt = haunts[i];

            if (!read.Haunts.TryGetValue(haunt.Key, out var was))
            {
                continue;
            }

            restored++;
            haunt.AwakeSeconds += was.Awake;
            haunt.Samples += was.Samples;
            haunt.ObservedRange = Math.Max(haunt.ObservedRange, was.Range);
            haunt.Toured += was.Toured;

            if (was.LastAwake > 0)
            {
                var last = DateTimeOffset.FromUnixTimeSeconds(was.LastAwake).UtcDateTime;

                if (last > haunt.LastAwake)
                {
                    haunt.LastAwake = last;
                }
            }

            if (was.TouredAt > 0)
            {
                var toured = DateTimeOffset.FromUnixTimeSeconds(was.TouredAt).UtcDateTime;

                if (toured > haunt.TouredAt)
                {
                    haunt.TouredAt = toured;
                }
            }
        }

        var cells = 0;

        if (read.Width == BotZoneRaster.Width)
        {
            var minutes = Math.Max(0, (DateTimeOffset.UtcNow.ToUnixTimeSeconds() - read.SavedAt) / 60.0);
            var fade = (float)Math.Pow(0.5, minutes / Math.Max(1, BotZoneRaster.HalfLifeMinutes));

            for (var i = 0; i < read.Cells.Count; i++)
            {
                BotZoneRaster.Restore(read.Cells[i].Cell, read.Cells[i].Weight * fade, now);
                cells++;
            }
        }

        RestoredHaunts = restored;
        RestoredCells = cells;

        logger.Information(
            "Zones: what the last session learned is back: {Haunts} of {Kept} haunts matched by their spawner's place, {Cells} observed cells (saved {Minutes:F0} minutes ago)",
            restored,
            read.Haunts.Count,
            cells,
            Math.Max(0, (DateTimeOffset.UtcNow.ToUnixTimeSeconds() - read.SavedAt) / 60.0)
        );
    }

    public static void Beat(long now)
    {
        if (!Running || !_started || !BotHaunts.Surveyed || now - _savedTick < SaveEveryMs || BotZones.SaveUnderWay())
        {
            return;
        }

        _savedTick = now;
        Save();
    }

    public static void Save()
    {
        if (!Running || !_started || !_read || _writing || !BotHaunts.Surveyed || BotZoneRaster.Width == 0)
        {
            return;
        }

        if (_pending != null)
        {
            return;
        }

        var began = System.Diagnostics.Stopwatch.GetTimestamp();
        List<int> cells = [];
        List<float> weights = [];

        BotZoneRaster.Collect(Core.TickCount, BotZoneRaster.Forgotten * 2, cells, weights);

        List<(string Key, double Awake, long Samples, int Range, long LastAwake, int Toured, long TouredAt)> haunts = [];
        var all = BotHaunts.All;

        for (var i = 0; i < all.Count; i++)
        {
            var haunt = all[i];

            if (haunt.Samples == 0 && haunt.Toured == 0)
            {
                continue;
            }

            haunts.Add(
                (
                    haunt.Key,
                    haunt.AwakeSeconds,
                    haunt.Samples,
                    haunt.ObservedRange,
                    haunt.LastAwake == DateTime.MinValue ? 0 : new DateTimeOffset(haunt.LastAwake, TimeSpan.Zero).ToUnixTimeSeconds(),
                    haunt.Toured,
                    haunt.TouredAt == DateTime.MinValue ? 0 : new DateTimeOffset(haunt.TouredAt, TimeSpan.Zero).ToUnixTimeSeconds()
                )
            );
        }

        var width = BotZoneRaster.Width;
        var most = Math.Max(0, MostCells);
        var path = PathOf();

        SaveMs = System.Diagnostics.Stopwatch.GetElapsedTime(began).TotalMilliseconds;
        _writing = true;

        _ = Task.Run(
            () =>
            {
                try
                {
                    var bytes = Serialize(haunts, cells, weights, width, most);

                    var temp = path + ".tmp";

                    System.IO.File.WriteAllBytes(temp, bytes);
                    System.IO.File.Move(temp, path, true);

                    Core.LoopContext.Post(
                        () =>
                        {
                            _writing = false;
                            Saves++;
                        }
                    );
                }
                catch (Exception e)
                {
                    Core.LoopContext.Post(
                        () =>
                        {
                            _writing = false;
                            SaveFailures++;
                            logger.Warning("Zones: {File} could not be written: {Message}", File, e.Message);
                        }
                    );
                }
            }
        );
    }

    private static byte[] Serialize(
        List<(string Key, double Awake, long Samples, int Range, long LastAwake, int Toured, long TouredAt)> haunts,
        List<int> cells, List<float> weights, int width, int most
    )
    {
        var order = new int[cells.Count];

        for (var i = 0; i < order.Length; i++)
        {
            order[i] = i;
        }

        Array.Sort(order, (a, b) => weights[b].CompareTo(weights[a]));

        var buffer = new ArrayBufferWriter<byte>(256 * 1024);

        using (var w = new Utf8JsonWriter(buffer))
        {
            w.WriteStartObject();
            w.WriteNumber("shape", Shape);
            w.WriteNumber("savedAt", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            w.WriteNumber("width", width);
            w.WriteStartArray("haunts");

            for (var i = 0; i < haunts.Count; i++)
            {
                var haunt = haunts[i];

                w.WriteStartArray();
                w.WriteStringValue(haunt.Key);
                w.WriteNumberValue(Math.Round(haunt.Awake, 1));
                w.WriteNumberValue(haunt.Samples);
                w.WriteNumberValue(haunt.Range);
                w.WriteNumberValue(haunt.LastAwake);
                w.WriteNumberValue(haunt.Toured);
                w.WriteNumberValue(haunt.TouredAt);
                w.WriteEndArray();
            }

            w.WriteEndArray();
            w.WriteStartArray("cells");

            for (var k = 0; k < order.Length && k < most; k++)
            {
                var i = order[k];

                w.WriteStartArray();
                w.WriteNumberValue(cells[i]);
                w.WriteNumberValue(Math.Round(weights[i], 3));
                w.WriteEndArray();
            }

            w.WriteEndArray();
            w.WriteEndObject();
        }

        return buffer.WrittenSpan.ToArray();
    }

    public static string Describe() =>
        !Running
            ? "Memory: off."
            : $"Memory: {_loaded}; {RestoredHaunts} haunts and {RestoredCells} cells restored; written {Saves} times ({SaveFailures} failed), {SaveMs:F1}ms of the loop to copy the last.";
}
