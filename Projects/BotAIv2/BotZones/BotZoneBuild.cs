using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

namespace Server.BotAI.V2;

/// <summary>
/// Turns what is known about the haunts, the living creatures, the observed patrols and the deaths into zones: which cells are
/// patrol, which are aggro fringe, what each zone holds, how strong it is against a bot, and its outline.
///
/// <para>
/// <b>Pure: it reads only its input and writes only its own scratch and the one back buffer it was handed.</b> That is what lets
/// <see cref="BotZones"/> run it off the loop when the machine has the cores for it (CLAUDE.md rules 3 and 10): nothing in here
/// touches a map, a mobile, a timer or a static the loop writes. The scratch arrays are static because one build runs at a
/// time — <see cref="BotZones"/> never starts a second before the first has published.
/// </para>
///
/// <para>
/// <b>Three layers, never merged across.</b> Land patrol of creatures that attack on sight (declared discs until observation
/// wins, the living where they stand, the cells seen patrolled awake); the sea's (creatures that cannot leave the water, only
/// where they are); and the passive animals' (hunting grounds), painted only where no attacker's patrol is. A zone is a
/// connected patch of one layer, eight ways. A sea serpent's water never joins an orc camp on the beach into one zone, and a
/// sea spawner that declares a walking range of 175 tiles paints nothing: only its living creatures do.
/// </para>
///
/// <para>
/// <b>Strength is what can come at once, not the worst alone.</b> The analysts' reading of 3474 deaths on 29–30.09 found 78
/// companies of three to six strong bots killed by a single weak kind — snakes, zombies, skeletons — in ground reckoned clear:
/// the worst creature nearby was weak, and there were many of it. So a zone's threat is taken round every living attacker in
/// it: that one and all others within <see cref="BotZoneSettings.ConvergeTiles"/> of it, the strongest in full and the rest at
/// <see cref="BotThreat.Secondary"/>, and the zone's threat is the worst such place in it.
/// </para>
/// </summary>
public static class BotZoneBuild
{
    private const byte Land = 1;

    private const byte Sea = 2;

    private const byte Passive = 4;

    private const byte SeaGroup = 8;

    private static byte[] _mark;

    private static int[] _label;

    private static readonly List<int> _touched = [];

    private static readonly List<int> _cells = [];

    private static readonly List<int> _queue = [];

    private static readonly Dictionary<int, int> _seaLabel = [];

    private static long _ringsTicks;

    private static double Ms(long from, long to) => (to - from) * 1000.0 / Stopwatch.Frequency;

    private static Dictionary<ulong, byte[]> _rings = [];

    private static Dictionary<ulong, byte[]> _ringsNext = [];

    private static int _ringsTraced;

    private static int _ringsReused;

    private static void RingsJson(Utf8JsonWriter json, byte[] mask, int w, int h, int ox, int oy, BotZoneSettings s, int most)
    {
        var key = Fingerprint(mask, w, h, ox, oy, s, most);

        if (!_rings.TryGetValue(key, out var fragment) && !_ringsNext.TryGetValue(key, out fragment))
        {
            var began = Stopwatch.GetTimestamp();
            var rings = BotZoneShapes.Rings(mask, w, h, ox, oy, s.SimplifyTiles, most, s.MostRings, s.SmoothPasses);
            var buffer = new ArrayBufferWriter<byte>(64 + rings.Count * 256);

            using (var writer = new Utf8JsonWriter(buffer))
            {
                Rings(writer, rings);
            }

            fragment = buffer.WrittenSpan.ToArray();
            _ringsTicks += Stopwatch.GetTimestamp() - began;
            _ringsTraced++;
        }
        else
        {
            _ringsReused++;
        }

        _ringsNext[key] = fragment;
        json.WriteRawValue(fragment, true);
    }

    private static ulong Fingerprint(byte[] mask, int w, int h, int ox, int oy, BotZoneSettings s, int most)
    {
        var hash = 14695981039346656037UL;

        void Mix(ulong value)
        {
            hash ^= value;
            hash *= 1099511628211UL;
        }

        Mix((ulong)w);
        Mix((ulong)h);
        Mix((ulong)ox);
        Mix((ulong)oy);
        Mix((ulong)most);
        Mix((ulong)s.MostRings);
        Mix((ulong)s.SmoothPasses);
        Mix((ulong)BitConverter.DoubleToInt64Bits(s.SimplifyTiles));

        for (var i = 0; i < mask.Length; i++)
        {
            hash ^= mask[i];
            hash *= 1099511628211UL;
        }

        return hash;
    }

    private sealed class Comp
    {
        public int Number;

        public byte Layer;

        public int Start;

        public int Count;

        public int MinX = int.MaxValue;

        public int MinY = int.MaxValue;

        public int MaxX = -1;

        public int MaxY = -1;

        public bool Kept;

        public readonly List<int> Haunts = [];

        public readonly List<int> Live = [];

        public double ObservedWeight;

        public int Alive;

        public int Aggressive;

        public int Casters;

        public int Converge;

        public int Max;

        public double Total;

        public double Worst;

        public double Threat;

        public double VsBot;

        public bool Empty;

        public int Reach;

        public int DeclaredRange;

        public int ObservedRange;

        public double Confidence;

        public double AwakeSeconds;

        public long Samples;

        public DateTime LastSeen = DateTime.MinValue;

        public BotZoneLevel Level;

        public bool Promoted;

        public int Deaths;

        public byte[] Aggro;

        public int AX;

        public int AY;

        public int AW;

        public int AH;

        public int AggroCount;

        public int Index;

        public int Id;

        public int CenterX;

        public int CenterY;

        public Point3D Stand;

        public bool StandIsReal;

        public BotZoneKindCount[] Kinds = [];

        public int OtherKinds;

        public string Label;

        public string Why;
    }

    public static BotZoneState Build(BotZoneInput input)
    {
        var began = Stopwatch.GetTimestamp();
        var s = input.Settings;
        var w = input.Width;
        var h = input.Height;

        if (_mark == null || _mark.Length != w * h)
        {
            _mark = new byte[w * h];
            _label = new int[w * h];
        }

        List<Comp> comps = [];

        try
        {
            return Run(input, s, w, h, comps, began);
        }
        finally
        {
            for (var k = 0; k < _touched.Count; k++)
            {
                _mark[_touched[k]] = 0;
                _label[_touched[k]] = 0;
            }

            _touched.Clear();
            _cells.Clear();
            _queue.Clear();
            _seaLabel.Clear();
        }
    }

    private static BotZoneState Run(BotZoneInput input, BotZoneSettings s, int w, int h, List<Comp> comps, long began)
    {
        var haunts = input.Haunts;
        var kinds = input.Kinds;
        var live = input.Live;
        var hostileLand = new bool[haunts.Length];
        var hostileSea = new bool[haunts.Length];
        var passive = new bool[haunts.Length];
        var rooted = new bool[haunts.Length];

        for (var i = 0; i < haunts.Length; i++)
        {
            var haunt = haunts[i];
            var anyAggressive = false;
            var anyLand = false;
            var allRooted = true;

            for (var e = 0; e < haunt.Kinds.Length; e++)
            {
                var kind = kinds[haunt.Kinds[e]];

                if (!kind.Water)
                {
                    anyLand = true;
                }

                if (!kind.Aggressive)
                {
                    continue;
                }

                anyAggressive = true;

                if (kind.Water)
                {
                    hostileSea[i] = true;
                }
                else
                {
                    hostileLand[i] = true;
                    allRooted &= kind.Rooted;
                }
            }

            passive[i] = !anyAggressive && anyLand && !haunt.InTown;
            rooted[i] = hostileLand[i] && allRooted;
        }

        for (var i = 0; i < haunts.Length; i++)
        {
            var haunt = haunts[i];

            if (!hostileLand[i] || !haunt.Running || haunt.Radius > s.DeclaredMost)
            {
                continue;
            }

            var radius = rooted[i] ? 0 : haunt.Radius;

            if (haunt.Confidence >= s.ObservedWins)
            {
                radius = Math.Min(radius, haunt.ObservedRange + s.ObservedMargin * BotZoneState.Cell);
            }

            Paint(haunt.X, haunt.Y, radius, Land, 0, w, h);
        }

        for (var i = 0; i < live.Length; i++)
        {
            var c = live[i];

            if (!c.Aggressive || c.Water)
            {
                continue;
            }

            var radius = c.Rooted ? 0
                : c.Haunt >= 0 && haunts[c.Haunt].Radius > s.DeclaredMost ? s.LooseRadius
                : s.LiveRadius;

            Paint(c.X, c.Y, radius, Land, 0, w, h);
        }

        for (var i = 0; i < input.Observed.Length; i++)
        {
            var cell = input.Observed[i];

            if ((uint)cell < (uint)(w * h))
            {
                Block(cell % w, cell / w, s.ObservedMargin, Land, w, h);
            }
        }

        Label(Land, Land, comps, w, h);

        for (var i = 0; i < live.Length; i++)
        {
            var c = live[i];

            if (c.Aggressive && c.Water)
            {
                Paint(c.X, c.Y, s.WaterRadius, Sea, 0, w, h);
                Paint(c.X, c.Y, Math.Max(s.WaterRadius, s.WaterGroup), SeaGroup, 0, w, h);
            }
        }

        Label(SeaGroup, Sea, comps, w, h);

        for (var i = 0; i < haunts.Length; i++)
        {
            var haunt = haunts[i];

            if (passive[i] && haunt.Running && haunt.Radius <= s.DeclaredMost)
            {
                Paint(haunt.X, haunt.Y, haunt.Radius, Passive, Land, w, h);
            }
        }

        for (var i = 0; i < live.Length; i++)
        {
            var c = live[i];

            if (c.Aggressive || c.Water)
            {
                continue;
            }

            if (c.Haunt >= 0 && (!passive[c.Haunt] || haunts[c.Haunt].Radius <= s.DeclaredMost))
            {
                continue;
            }

            Paint(c.X, c.Y, s.PassiveRadius, Passive, Land, w, h);
        }

        Label(Passive, Passive, comps, w, h);

        var painted = Stopwatch.GetTimestamp();

        var hauntComp = new int[haunts.Length];

        for (var i = 0; i < haunts.Length; i++)
        {
            var haunt = haunts[i];

            if (hostileLand[i])
            {
                hauntComp[i] = Near(haunt.X, haunt.Y, Land, comps, w, h);
            }
            else if (passive[i])
            {
                hauntComp[i] = Near(haunt.X, haunt.Y, Passive, comps, w, h);
            }
        }

        for (var i = 0; i < live.Length; i++)
        {
            var c = live[i];
            var number = 0;

            if (c.Aggressive && c.Water)
            {
                number = At(c.X, c.Y, Sea, comps, w, h);
            }
            else if (c.Aggressive)
            {
                number = At(c.X, c.Y, Land, comps, w, h);
            }
            else if (!c.Water)
            {
                number = At(c.X, c.Y, Land, comps, w, h);

                if (number == 0)
                {
                    number = At(c.X, c.Y, Passive, comps, w, h);
                }
            }

            if (number == 0)
            {
                continue;
            }

            comps[number - 1].Live.Add(i);

            if (c.Haunt >= 0 && hauntComp[c.Haunt] == 0 && (c.Aggressive || passive[c.Haunt]))
            {
                hauntComp[c.Haunt] = number;
            }
        }

        for (var i = 0; i < haunts.Length; i++)
        {
            if (hauntComp[i] > 0)
            {
                comps[hauntComp[i] - 1].Haunts.Add(i);
            }
        }

        for (var i = 0; i < input.Observed.Length; i++)
        {
            if ((uint)input.Observed[i] >= (uint)(w * h))
            {
                continue;
            }

            var number = _label[input.Observed[i]];

            if (number > 0 && comps[number - 1].Layer == Land)
            {
                comps[number - 1].ObservedWeight += input.ObservedWeight[i];
            }
        }

        var assigned = Stopwatch.GetTimestamp();
        var reference = Math.Max(1.0, input.RefBotPower);

        for (var k = 0; k < comps.Count; k++)
        {
            Reckon(comps[k], input, s, reference);
        }

        var reckoned = Stopwatch.GetTimestamp();

        for (var k = 0; k < comps.Count; k++)
        {
            var comp = comps[k];

            if (comp.Kept && comp.Layer != Passive)
            {
                Fringe(comp, w, h);
            }
        }

        var fringed = Stopwatch.GetTimestamp();
        List<Comp> kept = [];

        for (var k = 0; k < comps.Count; k++)
        {
            if (comps[k].Kept)
            {
                comps[k].Index = kept.Count;
                kept.Add(comps[k]);
            }
        }

        var cells = input.Buffer;

        Array.Clear(cells);

        for (var k = 0; k < kept.Count; k++)
        {
            var comp = kept[k];

            if (comp.Layer == Passive)
            {
                WriteCore(comp, cells, (ushort)(comp.Index + 1));
            }
        }

        List<Comp> ranked = [];

        for (var k = 0; k < kept.Count; k++)
        {
            if (kept[k].Layer != Passive)
            {
                ranked.Add(kept[k]);
            }
        }

        ranked.Sort((a, b) => a.Level != b.Level ? a.Level.CompareTo(b.Level) : a.VsBot.CompareTo(b.VsBot));

        for (var k = 0; k < ranked.Count; k++)
        {
            var comp = ranked[k];
            var value = (ushort)((comp.Index + 1) | BotZoneState.Fringe);

            for (var y = 0; y < comp.AH; y++)
            {
                for (var x = 0; x < comp.AW; x++)
                {
                    if (comp.Aggro[y * comp.AW + x] != 0)
                    {
                        cells[(comp.AY + y) * w + comp.AX + x] = value;
                    }
                }
            }
        }

        for (var k = 0; k < ranked.Count; k++)
        {
            WriteCore(ranked[k], cells, (ushort)(ranked[k].Index + 1));
        }

        var deaths = input.Deaths;

        for (var i = 0; i < deaths.Length; i++)
        {
            var v = CellValue(cells, deaths[i].X, deaths[i].Y, w, h);

            if (v != 0)
            {
                kept[(v & BotZoneState.IndexMask) - 1].Deaths++;
            }
        }

        for (var k = 0; k < kept.Count; k++)
        {
            var comp = kept[k];

            if (comp.Layer != Passive && comp.Level == BotZoneLevel.Hostile && s.DeathsPromote > 0 && comp.Deaths >= s.DeathsPromote)
            {
                comp.Level = BotZoneLevel.Deadly;
                comp.Promoted = true;
            }
        }

        int deathsDeadly = 0, deathsHostile = 0, deathsEdge = 0, deathsClear = 0;

        for (var i = 0; i < deaths.Length; i++)
        {
            var v = CellValue(cells, deaths[i].X, deaths[i].Y, w, h);

            if (v == 0)
            {
                deathsClear++;
            }
            else if ((v & BotZoneState.Fringe) != 0)
            {
                deathsEdge++;
            }
            else
            {
                switch (kept[(v & BotZoneState.IndexMask) - 1].Level)
                {
                    case BotZoneLevel.Deadly:
                        deathsDeadly++;
                        break;
                    case BotZoneLevel.Hostile:
                        deathsHostile++;
                        break;
                    case BotZoneLevel.Edge:
                        deathsEdge++;
                        break;
                    default:
                        deathsClear++;
                        break;
                }
            }
        }

        var nextId = Match(kept, input.Previous, w, out var matched);

        for (var k = 0; k < kept.Count; k++)
        {
            Describe(kept[k], input, s);
        }

        var shapesBegan = Stopwatch.GetTimestamp();

        _ringsTicks = 0;
        _ringsTraced = 0;
        _ringsReused = 0;
        _ringsNext.Clear();
        var buffer = new ArrayBufferWriter<byte>(256 * 1024);
        var zones = new BotZone[kept.Count];
        int clear = 0, hostile = 0, deadly = 0, water = 0;

        using (var json = new Utf8JsonWriter(buffer))
        {
            json.WriteStartArray();

            for (var k = 0; k < kept.Count; k++)
            {
                var comp = kept[k];
                var zone = Zone(comp);

                zones[k] = zone;

                switch (comp.Level)
                {
                    case BotZoneLevel.Clear:
                        clear++;
                        break;
                    case BotZoneLevel.Deadly:
                        deadly++;
                        break;
                    default:
                        hostile++;
                        break;
                }

                if (comp.Layer == Sea)
                {
                    water++;
                }

                Write(json, comp, zone, s, w);
            }

            json.WriteEndArray();
        }

        var shapesMs = Stopwatch.GetElapsedTime(shapesBegan).TotalMilliseconds;
        var ringsMs = _ringsTicks * 1000.0 / Stopwatch.Frequency;

        (_rings, _ringsNext) = (_ringsNext, _rings);
        _ringsNext.Clear();
        var phases = string.Create(
            CultureInfo.InvariantCulture,
            $"paint and label {Ms(began, painted):F1}, assign {Ms(painted, assigned):F1}, reckon {Ms(assigned, reckoned):F1}, fringe {Ms(reckoned, fringed):F1}, raster and numbers {Ms(fringed, shapesBegan):F1}, rings {ringsMs:F1} ({_ringsTraced} traced, {_ringsReused} reused) and JSON {shapesMs - ringsMs:F1}"
        );

        return new BotZoneState
        {
            Map = input.Map,
            Width = w,
            Height = h,
            Cells = cells,
            Zones = zones,
            NextId = nextId,
            At = input.At,
            RefBotPower = input.RefBotPower,
            Clear = clear,
            Edge = 0,
            Hostile = hostile,
            Deadly = deadly,
            WaterZones = water,
            Spawners = haunts.Length,
            Creatures = live.Length,
            ObservedCells = input.Observed.Length,
            Kept = matched,
            Born = kept.Count - matched,
            DeathsDeadly = deathsDeadly,
            DeathsHostile = deathsHostile,
            DeathsEdge = deathsEdge,
            DeathsClear = deathsClear,
            ZonesUtf8 = buffer.WrittenSpan.ToArray(),
            ShapesMs = shapesMs,
            Phases = phases,
            OffLoop = input.OffLoop,
            Generation = input.Generation,
            BuildMs = Stopwatch.GetElapsedTime(began).TotalMilliseconds
        };
    }

    private static void Paint(int x, int y, int radius, byte bit, byte skip, int w, int h)
    {
        var reach = Math.Max(0, radius) + BotZoneState.Cell / 2;
        var reach2 = reach * reach;
        var x0 = Math.Max(0, (x - reach) >> BotZoneState.Shift);
        var y0 = Math.Max(0, (y - reach) >> BotZoneState.Shift);
        var x1 = Math.Min(w - 1, (x + reach) >> BotZoneState.Shift);
        var y1 = Math.Min(h - 1, (y + reach) >> BotZoneState.Shift);
        var ux = x >> BotZoneState.Shift;
        var uy = y >> BotZoneState.Shift;

        for (var cy = y0; cy <= y1; cy++)
        {
            var dy = (cy << BotZoneState.Shift) + BotZoneState.Cell / 2 - y;

            for (var cx = x0; cx <= x1; cx++)
            {
                var dx = (cx << BotZoneState.Shift) + BotZoneState.Cell / 2 - x;

                if (dx * dx + dy * dy <= reach2 || cx == ux && cy == uy)
                {
                    Set(cy * w + cx, bit, skip);
                }
            }
        }
    }

    private static void Block(int cx, int cy, int margin, byte bit, int w, int h)
    {
        for (var y = Math.Max(0, cy - margin); y <= Math.Min(h - 1, cy + margin); y++)
        {
            for (var x = Math.Max(0, cx - margin); x <= Math.Min(w - 1, cx + margin); x++)
            {
                Set(y * w + x, bit, 0);
            }
        }
    }

    private static void Set(int i, byte bit, byte skip)
    {
        var m = _mark[i];

        if ((m & skip) != 0 || (m & bit) != 0)
        {
            return;
        }

        if (m == 0)
        {
            _touched.Add(i);
        }

        _mark[i] = (byte)(m | bit);
    }

    private static int LabelOf(int i, byte layer) =>
        layer == Sea ? _seaLabel.TryGetValue(i, out var n) ? n : 0 : _label[i];

    private static void SetLabel(int i, byte layer, int number)
    {
        if (layer == Sea)
        {
            _seaLabel[i] = number;
        }
        else
        {
            _label[i] = number;
        }
    }

    private static void Label(byte bit, byte layer, List<Comp> comps, int w, int h)
    {
        var count = _touched.Count;

        for (var k = 0; k < count; k++)
        {
            var seed = _touched[k];

            if ((_mark[seed] & bit) == 0 || LabelOf(seed, layer) != 0)
            {
                continue;
            }

            var comp = new Comp { Number = comps.Count + 1, Layer = layer, Start = _cells.Count };

            comps.Add(comp);
            SetLabel(seed, layer, comp.Number);
            _queue.Clear();
            _queue.Add(seed);

            for (var head = 0; head < _queue.Count; head++)
            {
                var at = _queue[head];
                var x = at % w;
                var y = at / w;

                _cells.Add(at);

                if (x < comp.MinX)
                {
                    comp.MinX = x;
                }

                if (x > comp.MaxX)
                {
                    comp.MaxX = x;
                }

                if (y < comp.MinY)
                {
                    comp.MinY = y;
                }

                if (y > comp.MaxY)
                {
                    comp.MaxY = y;
                }

                for (var ny = Math.Max(0, y - 1); ny <= Math.Min(h - 1, y + 1); ny++)
                {
                    for (var nx = Math.Max(0, x - 1); nx <= Math.Min(w - 1, x + 1); nx++)
                    {
                        var next = ny * w + nx;

                        if ((_mark[next] & bit) != 0 && LabelOf(next, layer) == 0)
                        {
                            SetLabel(next, layer, comp.Number);
                            _queue.Add(next);
                        }
                    }
                }
            }

            comp.Count = _cells.Count - comp.Start;

            if (bit != layer)
            {
                Narrow(comp, layer, w);
            }
        }
    }

    private static void Narrow(Comp comp, byte layer, int w)
    {
        var kept = comp.Start;

        comp.MinX = comp.MinY = int.MaxValue;
        comp.MaxX = comp.MaxY = -1;

        for (var k = comp.Start; k < comp.Start + comp.Count; k++)
        {
            var cell = _cells[k];

            if ((_mark[cell] & layer) == 0)
            {
                continue;
            }

            (_cells[kept], _cells[k]) = (cell, _cells[kept]);
            kept++;

            var x = cell % w;
            var y = cell / w;

            comp.MinX = Math.Min(comp.MinX, x);
            comp.MaxX = Math.Max(comp.MaxX, x);
            comp.MinY = Math.Min(comp.MinY, y);
            comp.MaxY = Math.Max(comp.MaxY, y);
        }

        comp.Count = kept - comp.Start;
    }

    private static int At(int x, int y, byte layer, List<Comp> comps, int w, int h)
    {
        var cx = x >> BotZoneState.Shift;
        var cy = y >> BotZoneState.Shift;

        if ((uint)cx >= (uint)w || (uint)cy >= (uint)h)
        {
            return 0;
        }

        var number = LabelOf(cy * w + cx, layer);

        return number > 0 && comps[number - 1].Layer == layer ? number : 0;
    }

    private static int Near(int x, int y, byte layer, List<Comp> comps, int w, int h)
    {
        var number = At(x, y, layer, comps, w, h);

        if (number > 0)
        {
            return number;
        }

        for (var dy = -1; dy <= 1; dy++)
        {
            for (var dx = -1; dx <= 1; dx++)
            {
                number = At(x + dx * BotZoneState.Cell, y + dy * BotZoneState.Cell, layer, comps, w, h);

                if (number > 0)
                {
                    return number;
                }
            }
        }

        return 0;
    }

    private static int CellValue(ushort[] cells, int x, int y, int w, int h)
    {
        var cx = x >> BotZoneState.Shift;
        var cy = y >> BotZoneState.Shift;

        return (uint)cx >= (uint)w || (uint)cy >= (uint)h ? 0 : cells[cy * w + cx];
    }

    private static void WriteCore(Comp comp, ushort[] cells, ushort value)
    {
        for (var k = comp.Start; k < comp.Start + comp.Count; k++)
        {
            cells[_cells[k]] = value;
        }
    }

    private static int ReachOf(bool water, bool rooted, bool caster, int perception, BotZoneSettings s)
    {
        var sight = Math.Clamp(perception, 1, Math.Max(1, s.PerceptionMost));

        if (water)
        {
            return caster ? Math.Min(sight, s.WaterCasterReach) : s.WaterMeleeReach;
        }

        return rooted && !caster ? s.RootedReach : sight;
    }

    private static void Reckon(Comp comp, BotZoneInput input, BotZoneSettings s, double reference)
    {
        var kinds = input.Kinds;
        var haunts = input.Haunts;
        var live = input.Live;
        var attackers = new List<(int X, int Y, double Power)>();
        Dictionary<int, (int Live, double Max)> byKind = [];

        for (var k = 0; k < comp.Live.Count; k++)
        {
            var c = live[comp.Live[k]];

            comp.Alive++;
            byKind[c.Kind] = byKind.TryGetValue(c.Kind, out var had) ? (had.Live + 1, had.Max) : (1, 0.0);

            if (!c.Aggressive)
            {
                continue;
            }

            comp.Aggressive++;

            if (c.Caster)
            {
                comp.Casters++;
            }

            var power = c.Power > 0 ? c.Power : kinds[c.Kind].Power;

            attackers.Add((c.X, c.Y, power));
            comp.Total += power;
            comp.Worst = Math.Max(comp.Worst, power);
            comp.Reach = Math.Max(comp.Reach, ReachOf(c.Water, c.Rooted, c.Caster, c.Perception, s));
        }

        var declared = 0.0;
        var declaredTotal = 0.0;
        var declaredWorst = 0.0;
        var weight = 0.0;
        var confidence = 0.0;
        var awake = 0.0;

        for (var k = 0; k < comp.Haunts.Count; k++)
        {
            var haunt = haunts[comp.Haunts[k]];
            var worst = 0.0;
            var sum = 0.0;

            comp.Max += haunt.Count;
            comp.DeclaredRange = Math.Max(comp.DeclaredRange, haunt.Radius);
            comp.ObservedRange = Math.Max(comp.ObservedRange, haunt.ObservedRange);
            comp.Samples += haunt.Samples;

            if (haunt.LastAwake > comp.LastSeen)
            {
                comp.LastSeen = haunt.LastAwake;
            }

            weight += haunt.Count;
            confidence += haunt.Confidence * haunt.Count;
            awake += haunt.AwakeSeconds * haunt.Count;

            for (var e = 0; e < haunt.Kinds.Length; e++)
            {
                var index = haunt.Kinds[e];
                var kind = kinds[index];
                var expected = haunt.Expected[e];

                byKind[index] = byKind.TryGetValue(index, out var had) ? (had.Live, had.Max + expected) : (0, expected);

                if (!kind.Aggressive || kind.Power <= 0.0)
                {
                    continue;
                }

                worst = Math.Max(worst, kind.Power);
                sum += expected * kind.Power;
                comp.Reach = Math.Max(comp.Reach, ReachOf(kind.Water, kind.Rooted, kind.Caster, kind.Perception, s));
            }

            declaredTotal += sum;
            declaredWorst = Math.Max(declaredWorst, worst);
            declared = Math.Max(declared, worst + s.Secondary * Math.Max(0.0, sum - worst));
        }

        comp.Confidence = weight > 0 ? confidence / weight : Math.Min(1.0, comp.ObservedWeight / Math.Max(1, s.ConfidentSamples));
        comp.AwakeSeconds = weight > 0 ? awake / weight : 0.0;

        if (comp.Haunts.Count == 0)
        {
            comp.Samples = (long)Math.Round(comp.ObservedWeight);
        }

        comp.Kept = comp.Layer switch
        {
            Land => comp.Haunts.Count > 0 || comp.Aggressive > 0,
            Sea => comp.Aggressive > 0,
            _ => comp.Alive >= Math.Max(1, s.ClearLeastLive)
        };

        if (!comp.Kept)
        {
            return;
        }

        if (comp.Layer == Passive)
        {
            comp.Level = BotZoneLevel.Clear;
            comp.Threat = comp.Worst;

            for (var k = 0; k < comp.Live.Count; k++)
            {
                var c = live[comp.Live[k]];
                var power = c.Power > 0 ? c.Power : kinds[c.Kind].Power;

                comp.Total += power;
                comp.Threat = Math.Max(comp.Threat, power);
            }

            comp.Worst = comp.Threat;
            comp.VsBot = comp.Threat / reference;
        }
        else if (attackers.Count > 0)
        {
            (comp.Threat, comp.Converge) = Converging(attackers, s);
            comp.VsBot = comp.Threat / reference;
            comp.Level = comp.VsBot >= s.DeadlyVsBot ? BotZoneLevel.Deadly : BotZoneLevel.Hostile;
        }
        else
        {
            comp.Empty = true;
            comp.Total = declaredTotal;
            comp.Worst = declaredWorst;
            comp.Threat = declared * s.EmptyWeight;
            comp.VsBot = comp.Threat / reference;
            comp.Level = comp.VsBot >= s.DeadlyVsBot ? BotZoneLevel.Deadly : BotZoneLevel.Hostile;
        }

        if (comp.Reach == 0 && comp.Layer != Passive)
        {
            comp.Reach = Math.Min(16, s.PerceptionMost);
        }

        List<BotZoneKindCount> list = [];

        foreach (var (index, count) in byKind)
        {
            var kind = kinds[index];

            list.Add(new BotZoneKindCount(kind.Name, count.Live, (int)Math.Round(count.Max), Math.Round(kind.Power), kind.Aggressive, kind.Caster));
        }

        list.Sort(
            (a, b) =>
            {
                if (a.Aggressive != b.Aggressive)
                {
                    return a.Aggressive ? -1 : 1;
                }

                var ai = Math.Max(a.Live, a.Max) * a.Power;
                var bi = Math.Max(b.Live, b.Max) * b.Power;

                return bi.CompareTo(ai);
            }
        );

        var most = Math.Max(1, s.MostKinds);

        comp.OtherKinds = Math.Max(0, list.Count - most);

        if (list.Count > most)
        {
            list.RemoveRange(most, list.Count - most);
        }

        comp.Kinds = list.ToArray();

        long sx = 0, sy = 0;

        for (var k = comp.Start; k < comp.Start + comp.Count; k++)
        {
            sx += _cells[k] % input.Width;
            sy += _cells[k] / input.Width;
        }

        comp.CenterX = (int)(sx * BotZoneState.Cell / Math.Max(1, comp.Count)) + BotZoneState.Cell / 2;
        comp.CenterY = (int)(sy * BotZoneState.Cell / Math.Max(1, comp.Count)) + BotZoneState.Cell / 2;
        comp.Stand = new Point3D(comp.CenterX, comp.CenterY, 0);

        var nearest = long.MaxValue;

        for (var k = 0; k < comp.Haunts.Count; k++)
        {
            var haunt = haunts[comp.Haunts[k]];
            var dx = (long)(haunt.X - comp.CenterX);
            var dy = (long)(haunt.Y - comp.CenterY);
            var d = dx * dx + dy * dy;

            if (d < nearest)
            {
                nearest = d;
                comp.Stand = new Point3D(haunt.X, haunt.Y, haunt.Z);
                comp.StandIsReal = true;
            }
        }
    }

    private static (double Threat, int Crowd) Converging(List<(int X, int Y, double Power)> attackers, BotZoneSettings s)
    {
        attackers.Sort((a, b) => b.Power.CompareTo(a.Power));

        var reach = Math.Max(1, s.ConvergeTiles);
        var anchors = Math.Min(attackers.Count, 96);
        var threat = 0.0;
        var crowd = 0;

        for (var a = 0; a < anchors; a++)
        {
            var anchor = attackers[a];
            var worst = 0.0;
            var sum = 0.0;
            var count = 0;

            for (var b = 0; b < attackers.Count; b++)
            {
                var other = attackers[b];

                if (Math.Abs(other.X - anchor.X) > reach || Math.Abs(other.Y - anchor.Y) > reach)
                {
                    continue;
                }

                count++;
                sum += other.Power;
                worst = Math.Max(worst, other.Power);
            }

            threat = Math.Max(threat, worst + s.Secondary * Math.Max(0.0, sum - worst));
            crowd = Math.Max(crowd, count);
        }

        return (threat, crowd);
    }

    private static void Fringe(Comp comp, int w, int h)
    {
        var reachTiles = comp.Reach + BotZoneState.Cell / 2;
        var r = (reachTiles + BotZoneState.Cell - 1) / BotZoneState.Cell;
        var x0 = Math.Max(0, comp.MinX - r);
        var y0 = Math.Max(0, comp.MinY - r);
        var x1 = Math.Min(w - 1, comp.MaxX + r);
        var y1 = Math.Min(h - 1, comp.MaxY + r);
        var aw = x1 - x0 + 1;
        var ah = y1 - y0 + 1;
        var d = new int[aw * ah];
        const int far = int.MaxValue / 4;

        Array.Fill(d, far);

        for (var k = comp.Start; k < comp.Start + comp.Count; k++)
        {
            var cell = _cells[k];

            d[(cell / w - y0) * aw + cell % w - x0] = 0;
        }

        for (var y = 0; y < ah; y++)
        {
            for (var x = 0; x < aw; x++)
            {
                var i = y * aw + x;
                var v = d[i];

                if (x > 0)
                {
                    v = Math.Min(v, d[i - 1] + 3);
                }

                if (y > 0)
                {
                    v = Math.Min(v, d[i - aw] + 3);

                    if (x > 0)
                    {
                        v = Math.Min(v, d[i - aw - 1] + 4);
                    }

                    if (x < aw - 1)
                    {
                        v = Math.Min(v, d[i - aw + 1] + 4);
                    }
                }

                d[i] = v;
            }
        }

        for (var y = ah - 1; y >= 0; y--)
        {
            for (var x = aw - 1; x >= 0; x--)
            {
                var i = y * aw + x;
                var v = d[i];

                if (x < aw - 1)
                {
                    v = Math.Min(v, d[i + 1] + 3);
                }

                if (y < ah - 1)
                {
                    v = Math.Min(v, d[i + aw] + 3);

                    if (x < aw - 1)
                    {
                        v = Math.Min(v, d[i + aw + 1] + 4);
                    }

                    if (x > 0)
                    {
                        v = Math.Min(v, d[i + aw - 1] + 4);
                    }
                }

                d[i] = v;
            }
        }

        var limit = (int)Math.Round(reachTiles * 3.0 / BotZoneState.Cell);
        var mask = new byte[aw * ah];
        var count = 0;

        for (var i = 0; i < d.Length; i++)
        {
            if (d[i] <= limit)
            {
                mask[i] = 1;
                count++;
            }
        }

        comp.Aggro = mask;
        comp.AX = x0;
        comp.AY = y0;
        comp.AW = aw;
        comp.AH = ah;
        comp.AggroCount = count;
    }

    private static int Match(List<Comp> kept, BotZoneState previous, int w, out int matched)
    {
        matched = 0;

        var nextId = Math.Max(1, previous?.NextId ?? 1);
        List<(int Comp, int Id, int Overlap)> pairs = [];

        if (previous?.Zones != null && previous.Width == w)
        {
            Dictionary<int, int> overlap = [];
            byte[] families = [Land, Sea, Passive];

            for (var k = 0; k < _touched.Count; k++)
            {
                _label[_touched[k]] = 0;
            }

            for (var f = 0; f < families.Length; f++)
            {
                var family = families[f];
                var old = previous.Zones;

                for (var z = 0; z < old.Length; z++)
                {
                    if (Family(old[z]) != family || old[z].CellList == null)
                    {
                        continue;
                    }

                    var list = old[z].CellList;

                    for (var c = 0; c < list.Length; c++)
                    {
                        _label[list[c]] = z + 1;
                    }
                }

                for (var k = 0; k < kept.Count; k++)
                {
                    var comp = kept[k];

                    if (comp.Layer != family)
                    {
                        continue;
                    }

                    overlap.Clear();

                    for (var c = comp.Start; c < comp.Start + comp.Count; c++)
                    {
                        var owner = _label[_cells[c]];

                        if (owner > 0)
                        {
                            var id = old[owner - 1].Id;

                            overlap[id] = overlap.TryGetValue(id, out var n) ? n + 1 : 1;
                        }
                    }

                    foreach (var (id, n) in overlap)
                    {
                        pairs.Add((k, id, n));
                    }
                }

                for (var z = 0; z < old.Length; z++)
                {
                    var list = old[z].CellList;

                    if (Family(old[z]) != family || list == null)
                    {
                        continue;
                    }

                    for (var c = 0; c < list.Length; c++)
                    {
                        _label[list[c]] = 0;
                    }
                }
            }
        }

        pairs.Sort((a, b) => b.Overlap.CompareTo(a.Overlap));

        HashSet<int> taken = [];

        for (var p = 0; p < pairs.Count; p++)
        {
            var (k, id, _) = pairs[p];

            if (kept[k].Id != 0 || !taken.Add(id))
            {
                continue;
            }

            kept[k].Id = id;
            matched++;
        }

        for (var k = 0; k < kept.Count; k++)
        {
            if (kept[k].Id == 0)
            {
                kept[k].Id = nextId++;
            }
        }

        return nextId;
    }

    private static byte Family(BotZone zone) => zone.Water ? Sea : zone.Level == BotZoneLevel.Clear ? Passive : Land;

    private static void Describe(Comp comp, BotZoneInput input, BotZoneSettings s)
    {
        var who = Who(comp);
        var where = Place(comp.CenterX, comp.CenterY, comp.Layer == Sea, input);

        comp.Label = where.Length == 0 ? who : $"{who}, {where}";

        if (comp.Layer == Passive)
        {
            comp.Why = Invariant($"{comp.Alive} that do not attack first; the strongest is ×{comp.VsBot:F1} a bot if set upon");
        }
        else if (comp.Empty)
        {
            comp.Why = Invariant($"none alive now; {Plural(comp.Haunts.Count, "spawner")} keep {comp.Max}, ×{comp.VsBot:F1} a bot counted at half until they return");
        }
        else if (comp.Layer == Sea)
        {
            comp.Why = Invariant($"{comp.Aggressive} that cannot leave the water; ×{comp.VsBot:F1} a bot within {comp.Reach} tiles of where they swim");
        }
        else
        {
            var casters = comp.Casters > 0 ? $" ({Plural(comp.Casters, "caster")})" : "";
            var line = comp.Level == BotZoneLevel.Deadly && !comp.Promoted ? $", past the ×{s.DeadlyVsBot:F0} line" : "";

            comp.Why = Invariant($"{comp.Aggressive} attack on sight{casters}; up to {comp.Converge} can close at once: ×{comp.VsBot:F1} a bot{line}");
        }

        if (comp.Promoted)
        {
            comp.Why += Invariant($"; deadly for {comp.Deaths} bots killed here in {s.DeathsWindowHours}h");
        }
        else if (comp.Deaths > 0)
        {
            comp.Why += Invariant($"; {Plural(comp.Deaths, "bot")} killed here in {s.DeathsWindowHours}h");
        }
    }

    private static string Invariant(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);

    private static string Plural(int n, string word) => n == 1 ? $"1 {word}" : $"{n} {word}s";

    private static string Who(Comp comp)
    {
        var first = -1;
        var second = -1;

        for (var k = 0; k < comp.Kinds.Length; k++)
        {
            var kind = comp.Kinds[k];

            if (comp.Layer != Passive && !kind.Aggressive)
            {
                continue;
            }

            if (first < 0)
            {
                first = k;
            }
            else if (second < 0)
            {
                second = k;

                break;
            }
        }

        if (first < 0)
        {
            return comp.Layer == Passive ? "animals" : "creatures";
        }

        var one = Words(comp.Kinds[first].Kind);

        return second < 0 ? one : $"{one} and {Words(comp.Kinds[second].Kind)}";
    }

    public static string Words(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return "creatures";
        }

        Span<char> chars = stackalloc char[name.Length * 2];
        var n = 0;

        for (var i = 0; i < name.Length && n < chars.Length - 1; i++)
        {
            var c = name[i];

            if (char.IsUpper(c) && i > 0)
            {
                chars[n++] = ' ';
            }

            chars[n++] = char.ToLowerInvariant(c);
        }

        var words = new string(chars[..n]);

        if (words.EndsWith("man", StringComparison.Ordinal))
        {
            return string.Concat(words.AsSpan(0, words.Length - 3), "men");
        }

        if (words.EndsWith("wolf", StringComparison.Ordinal))
        {
            return string.Concat(words.AsSpan(0, words.Length - 1), "ves");
        }

        if (words.EndsWith("sheep", StringComparison.Ordinal) || words.EndsWith("deer", StringComparison.Ordinal))
        {
            return words;
        }

        if (words.EndsWith('s') || words.EndsWith('x') || words.EndsWith("sh", StringComparison.Ordinal) || words.EndsWith("ch", StringComparison.Ordinal))
        {
            return words + "es";
        }

        if (words.Length > 1 && words.EndsWith('y') && "aeiou".IndexOf(words[^2]) < 0)
        {
            return string.Concat(words.AsSpan(0, words.Length - 1), "ies");
        }

        return words + "s";
    }

    private static string Place(int x, int y, bool water, BotZoneInput input)
    {
        var deeps = input.Deeps;

        for (var i = 0; i < deeps.Length; i++)
        {
            var deep = deeps[i];

            if (x >= deep.X1 && x <= deep.X2 && y >= deep.Y1 && y <= deep.Y2)
            {
                return $"in {deep.Name}";
            }
        }

        var towns = input.Towns;
        var best = -1;
        var bestDistance = double.MaxValue;

        for (var i = 0; i < towns.Length; i++)
        {
            var dx = (double)(x - towns[i].X1);
            var dy = (double)(y - towns[i].Y1);
            var d = Math.Sqrt(dx * dx + dy * dy);

            if (d < bestDistance)
            {
                bestDistance = d;
                best = i;
            }
        }

        if (best < 0)
        {
            return "";
        }

        var town = towns[best];
        var compass = Compass(x - town.X1, y - town.Y1);

        if (water)
        {
            return bestDistance <= 150 ? $"off {town.Name}" : $"at sea {compass} of {town.Name}";
        }

        return bestDistance <= 100 ? $"by {town.Name}" : $"{compass} of {town.Name}";
    }

    private static string Compass(int dx, int dy)
    {
        var angle = Math.Atan2(-dy, dx) * 180.0 / Math.PI;
        var sector = (int)Math.Round((angle + 360.0) % 360.0 / 45.0) % 8;

        return sector switch
        {
            0 => "E",
            1 => "NE",
            2 => "N",
            3 => "NW",
            4 => "W",
            5 => "SW",
            6 => "S",
            _ => "SE"
        };
    }

    private static BotZone Zone(Comp comp) =>
        new()
        {
            Id = comp.Id,
            Level = comp.Level,
            Label = comp.Label,
            Why = comp.Why,
            Water = comp.Layer == Sea,
            CenterX = comp.CenterX,
            CenterY = comp.CenterY,
            X1 = comp.MinX * BotZoneState.Cell,
            Y1 = comp.MinY * BotZoneState.Cell,
            X2 = comp.MaxX * BotZoneState.Cell + BotZoneState.Cell - 1,
            Y2 = comp.MaxY * BotZoneState.Cell + BotZoneState.Cell - 1,
            CoreCells = comp.Count,
            AggroCells = comp.AggroCount,
            Live = comp.Alive,
            Max = comp.Max,
            Aggressive = comp.Aggressive,
            Casters = comp.Casters,
            Converge = comp.Converge,
            Kinds = comp.Kinds,
            OtherKinds = comp.OtherKinds,
            PowerTotal = comp.Total,
            Worst = comp.Worst,
            Threat = comp.Threat,
            VsBot = comp.VsBot,
            Empty = comp.Empty,
            DeclaredRange = comp.DeclaredRange,
            ObservedRange = comp.ObservedRange,
            AggroRange = comp.Layer == Passive ? 0 : comp.Reach,
            Confidence = comp.Confidence,
            Samples = comp.Samples,
            AwakeSeconds = comp.AwakeSeconds,
            LastSeen = comp.LastSeen,
            Deaths = comp.Deaths,
            Haunts = comp.Haunts.Count,
            Stand = comp.Stand,
            StandIsReal = comp.StandIsReal,
            CellList = _cells.GetRange(comp.Start, comp.Count).ToArray()
        };

    private static void Write(Utf8JsonWriter json, Comp comp, BotZone zone, BotZoneSettings s, int w)
    {
        json.WriteStartObject();
        json.WriteNumber("id", zone.Id);
        json.WriteString("level", BotZones.Word(zone.Level));
        json.WriteString("label", zone.Label);
        json.WriteString("why", zone.Why);

        if (zone.Water)
        {
            json.WriteBoolean("water", true);
        }

        json.WritePropertyName("center");
        json.WriteStartArray();
        json.WriteNumberValue(zone.CenterX);
        json.WriteNumberValue(zone.CenterY);
        json.WriteEndArray();

        json.WritePropertyName("bounds");
        json.WriteStartArray();
        json.WriteNumberValue(zone.X1);
        json.WriteNumberValue(zone.Y1);
        json.WriteNumberValue(zone.X2);
        json.WriteNumberValue(zone.Y2);
        json.WriteEndArray();

        var most = comp.Layer switch
        {
            Land => s.MostPoints,
            Sea => s.SeaMostPoints,
            _ => s.ClearMostPoints
        };

        var cw = comp.MaxX - comp.MinX + 1;
        var ch = comp.MaxY - comp.MinY + 1;
        var core = new byte[cw * ch];

        for (var k = comp.Start; k < comp.Start + comp.Count; k++)
        {
            var cell = _cells[k];

            core[(cell / w - comp.MinY) * cw + cell % w - comp.MinX] = 1;
        }

        json.WritePropertyName("core");
        RingsJson(json, core, cw, ch, comp.MinX, comp.MinY, s, most);

        json.WritePropertyName("aggro");

        if (comp.Aggro == null)
        {
            json.WriteStartArray();
            json.WriteEndArray();
        }
        else
        {
            RingsJson(json, comp.Aggro, comp.AW, comp.AH, comp.AX, comp.AY, s, most);
        }

        json.WriteStartObject("mobs");
        json.WriteNumber("live", zone.Live);
        json.WriteNumber("max", zone.Max);
        json.WriteNumber("aggressive", zone.Aggressive);
        json.WriteNumber("casters", zone.Casters);
        json.WriteNumber("converge", zone.Converge);
        json.WriteStartArray("kinds");

        for (var k = 0; k < zone.Kinds.Length; k++)
        {
            var kind = zone.Kinds[k];

            json.WriteStartObject();
            json.WriteString("kind", kind.Kind);
            json.WriteNumber("live", kind.Live);
            json.WriteNumber("max", kind.Max);
            json.WriteNumber("power", Math.Round(kind.Power));
            json.WriteBoolean("aggressive", kind.Aggressive);
            json.WriteBoolean("caster", kind.Caster);
            json.WriteEndObject();
        }

        json.WriteEndArray();
        json.WriteNumber("others", zone.OtherKinds);
        json.WriteEndObject();

        json.WriteStartObject("power");
        json.WriteNumber("total", Math.Round(zone.PowerTotal));
        json.WriteNumber("worst", Math.Round(zone.Worst));
        json.WriteNumber("threat", Math.Round(zone.Threat));
        json.WriteNumber("vsBot", Math.Round(zone.VsBot, 2));
        json.WriteEndObject();

        json.WriteStartObject("patrol");
        json.WriteNumber("declared", zone.DeclaredRange);

        if (zone.Samples > 0 && zone.Haunts > 0)
        {
            json.WriteNumber("observed", zone.ObservedRange);
        }
        else
        {
            json.WriteNull("observed");
        }

        json.WriteNumber("aggroRange", zone.AggroRange);
        json.WriteEndObject();

        json.WriteNumber("confidence", Math.Round(zone.Confidence, 2));

        json.WriteStartObject("observed");
        json.WriteNumber("samples", zone.Samples);
        json.WriteNumber("awakeSeconds", Math.Round(zone.AwakeSeconds));

        if (zone.LastSeen == DateTime.MinValue)
        {
            json.WriteNull("lastSeen");
        }
        else
        {
            json.WriteString("lastSeen", zone.LastSeen.ToLocalTime().ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture));
        }

        json.WriteEndObject();

        json.WriteStartObject("deaths");
        json.WriteNumber("bots", zone.Deaths);
        json.WriteString("window", $"{s.DeathsWindowHours}h");
        json.WriteEndObject();

        json.WriteEndObject();
    }

    private static void Rings(Utf8JsonWriter json, List<int[]> rings)
    {
        json.WriteStartArray();

        for (var r = 0; r < rings.Count; r++)
        {
            var ring = rings[r];

            json.WriteStartArray();

            for (var k = 0; k + 1 < ring.Length; k += 2)
            {
                json.WriteStartArray();
                json.WriteNumberValue(ring[k]);
                json.WriteNumberValue(ring[k + 1]);
                json.WriteEndArray();
            }

            json.WriteEndArray();
        }

        json.WriteEndArray();
    }
}
