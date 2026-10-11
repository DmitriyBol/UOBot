using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using Microsoft.Win32.SafeHandles;

namespace Server.BotAI.V2;

/// <summary>A decoded picture: ARGB pixels, its size, and the point inside it that stands on the tile.</summary>
public sealed class BotSprite
{
    public int Width;

    public int Height;

    public int AnchorX;

    public int AnchorY;

    public uint[] Pixels;
}

/// <summary>How the client lays out a body's actions: the five kinds of <c>mobtypes.txt</c>.</summary>
public enum BotAnimType : byte
{
    Monster,
    SeaMonster,
    Animal,
    Human,
    Equipment,
    Unknown
}

/// <summary>
/// The client's own files, read the way the client reads them, for the live window's picture (02.10.2026: "can the
/// picture be shown straight from the game?").
///
/// <para>
/// <b>Files, never the world.</b> Everything here is a file in the client folder the server already knows
/// (<see cref="Core.FindDataFile"/>): the art, the land textures, the hues, tiledata, and the animations in both their
/// forms, the old <c>anim*.idx/mul</c> pairs and the newer <c>AnimationFrame*.uop</c> packages. It is opened once, the
/// first time the listener asks for a picture, and read with positional reads that are safe from any thread, so the
/// listener's threads may decode side by side and the game loop never waits on any of it.
/// </para>
///
/// <para>
/// <b>The client's rules, not ours.</b> Which body is drawn from which file follows <c>mobtypes.txt</c>,
/// <c>Bodyconv.def</c>, <c>Body.def</c> and <c>AnimationSequence.uop</c>; which action a standing body shows follows the
/// client's own choice for that kind of body; which directions are mirrored follows the client's five-direction files. The
/// reference was the open-source ClassicUO loaders, read for the formats and not copied.
/// </para>
/// </summary>
public sealed class BotArtFiles : IDisposable
{
    public static Func<string, string> Locate { get; set; } = name => Core.FindDataFile(name, false);

    private static readonly object _openLock = new();

    private static BotArtFiles _instance;

    private static volatile bool _tried;

    public static string Problem { get; private set; }

    public static BotArtFiles Opened => _tried ? _instance : null;

    public static BotArtFiles Instance
    {
        get
        {
            if (_tried)
            {
                return _instance;
            }

            lock (_openLock)
            {
                if (_tried)
                {
                    return _instance;
                }

                try
                {
                    _instance = new BotArtFiles();
                }
                catch (Exception e)
                {
                    Problem = e.Message;
                    _instance = null;
                }

                _tried = true;

                return _instance;
            }
        }
    }

    public const int MostActions = 80;

    private readonly struct Entry
    {
        public readonly long Offset;
        public readonly int Length;
        public readonly int Size;
        public readonly int Flag;

        public Entry(long offset, int length, int size, int flag)
        {
            Offset = offset;
            Length = length;
            Size = size;
            Flag = flag;
        }
    }

    private readonly List<SafeFileHandle> _handles = [];

    private readonly SafeFileHandle _art;
    private readonly Dictionary<int, Entry> _artIndex;
    private readonly SafeFileHandle _tex;
    private readonly Entry[] _texIndex;

    private readonly ushort[][] _hues;

    private readonly ushort[] _landTexture = new ushort[0x4000];
    private readonly ushort[] _itemAnim = new ushort[0x10000];

    private readonly SafeFileHandle[] _animIdx = new SafeFileHandle[6];
    private readonly SafeFileHandle[] _animMul = new SafeFileHandle[6];
    private readonly long[] _animIdxLength = new long[6];
    private readonly SafeFileHandle[] _animUop = new SafeFileHandle[6];
    private readonly Dictionary<ulong, (int File, Entry Entry)> _uopFrames = new();
    private readonly Dictionary<int, int[]> _uopGroups = new();
    private readonly Dictionary<int, (BotAnimType Type, uint Flags)> _mobTypes = new();
    private readonly Dictionary<int, (int File, int Body)> _bodyConv = new();
    private readonly Dictionary<int, (int Body, int Hue)> _bodyDef = new();
    private readonly Dictionary<(int Body, int Anim), (int Anim, int Hue)> _equipConv = new();

    public string Summary { get; }

    public string Stamp { get; }

    private BotArtFiles()
    {
        var stamp = 17L;

        string Find(string name)
        {
            var path = Locate(name);

            if (path == null || !File.Exists(path))
            {
                return null;
            }

            var info = new FileInfo(path);
            stamp = stamp * 31 + info.Length + info.LastWriteTimeUtc.Ticks % 1000003;

            return path;
        }

        SafeFileHandle Open(string path)
        {
            var handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            _handles.Add(handle);

            return handle;
        }

        var artMul = Find("art.mul");
        var artIdx = Find("artidx.mul");

        if (artMul != null && artIdx != null)
        {
            _art = Open(artMul);
            _artIndex = ReadMulIndex(artIdx);
        }
        else if (Find("artLegacyMUL.uop") is { } artUop)
        {
            _art = Open(artUop);
            _artIndex = new Dictionary<int, Entry>(0x14000);

            var wanted = new Dictionary<ulong, int>(0x14000);

            for (var i = 0; i < 0x14000; i++)
            {
                wanted[UOPFiles.HashLittle2($"build/artlegacymul/{i:D8}.tga")] = i;
            }

            foreach (var (hash, entry) in ReadUop(artUop))
            {
                if (wanted.TryGetValue(hash, out var index))
                {
                    _artIndex[index] = entry;
                }
            }
        }
        else
        {
            throw new FileNotFoundException("no art.mul or artLegacyMUL.uop in the client folders");
        }

        if (Find("texmaps.mul") is { } texMul && Find("texidx.mul") is { } texIdx)
        {
            _tex = Open(texMul);

            var index = ReadMulIndex(texIdx);
            _texIndex = new Entry[0x4000];

            foreach (var (i, entry) in index)
            {
                if (i < _texIndex.Length)
                {
                    _texIndex[i] = entry;
                }
            }
        }

        _hues = Find("hues.mul") is { } hues ? ReadHues(hues) : [];

        if (Find("tiledata.mul") is { } tiledata)
        {
            ReadTileData(tiledata);
        }

        string[] names = ["anim", "anim2", "anim3", "anim4", "anim5", "anim6"];

        for (var i = 0; i < names.Length; i++)
        {
            if (Find(names[i] + ".idx") is { } idx && Find(names[i] + ".mul") is { } mul)
            {
                _animIdx[i] = Open(idx);
                _animMul[i] = Open(mul);
                _animIdxLength[i] = new FileInfo(idx).Length;
            }
        }

        ReadMobTypes(Find("mobtypes.txt"));
        ReadBodyConv(Find("Bodyconv.def"));
        ReadBodyDef(Find("Body.def"));
        ReadEquipConv(Find("Equipconv.def"));

        var uopFiles = 0;

        for (var i = 1; i <= 6; i++)
        {
            if (Find($"AnimationFrame{i}.uop") is not { } frames)
            {
                continue;
            }

            uopFiles++;
            _animUop[i - 1] = Open(frames);

            foreach (var (hash, entry) in ReadUop(frames))
            {
                _uopFrames.TryAdd(hash, (i - 1, entry));
            }
        }

        if (Find("AnimationSequence.uop") is { } sequence)
        {
            ReadSequences(sequence);
        }

        Stamp = (stamp & 0xFFFFFF).ToString("x6", CultureInfo.InvariantCulture);
        Summary = $"{_artIndex.Count} art entries, {(_texIndex == null ? 0 : 0x4000)} texture slots, {_hues.Length} hues, "
                  + $"{_mobTypes.Count} body types, {_bodyConv.Count} converted and {_bodyDef.Count} substituted bodies, "
                  + $"{uopFiles} animation packages with {_uopFrames.Count} actions, {_uopGroups.Count} sequences";
    }

    public void Dispose()
    {
        foreach (var handle in _handles)
        {
            handle.Dispose();
        }

        _handles.Clear();
    }

    private static Dictionary<int, Entry> ReadMulIndex(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var count = bytes.Length / 12;
        var index = new Dictionary<int, Entry>(count);

        for (var i = 0; i < count; i++)
        {
            var offset = BitConverter.ToInt32(bytes, i * 12);
            var length = BitConverter.ToInt32(bytes, i * 12 + 4);

            if (offset >= 0 && length > 0)
            {
                index[i] = new Entry(offset, length, length, 0);
            }
        }

        return index;
    }

    private static Dictionary<ulong, Entry> ReadUop(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var reader = new BinaryReader(stream);
        var entries = new Dictionary<ulong, Entry>();

        if (reader.ReadUInt32() != 0x50594D)
        {
            throw new FileLoadException($"{path} is not a UOP package");
        }

        reader.ReadInt32();
        reader.ReadUInt32();

        var next = reader.ReadInt64();
        var length = stream.Length;

        while (next > 0 && next < length)
        {
            stream.Seek(next, SeekOrigin.Begin);

            var count = reader.ReadInt32();
            next = reader.ReadInt64();

            for (var i = 0; i < count; i++)
            {
                var offset = reader.ReadInt64();
                var header = reader.ReadInt32();
                var stored = reader.ReadInt32();
                var unpacked = reader.ReadInt32();
                var hash = reader.ReadUInt64();
                reader.ReadUInt32();
                var flag = reader.ReadInt16();

                if (offset == 0 || stored <= 0)
                {
                    continue;
                }

                entries[hash] = new Entry(offset + header, stored, unpacked, flag);
            }
        }

        return entries;
    }

    private static byte[] ReadAt(SafeFileHandle handle, long offset, int length)
    {
        if (handle == null || length <= 0 || length > 64 * 1024 * 1024)
        {
            return null;
        }

        var bytes = new byte[length];
        var done = 0;

        while (done < length)
        {
            var n = RandomAccess.Read(handle, bytes.AsSpan(done), offset + done);

            if (n <= 0)
            {
                return null;
            }

            done += n;
        }

        return bytes;
    }

    private static byte[] Load(SafeFileHandle handle, in Entry entry)
    {
        var raw = ReadAt(handle, entry.Offset, entry.Length);

        if (raw == null || entry.Flag == 0)
        {
            return raw;
        }

        if (entry.Flag != 1 && entry.Flag != 3)
        {
            return null;
        }

        var unpacked = new byte[entry.Size];

        using (var zip = new ZLibStream(new MemoryStream(raw), CompressionMode.Decompress))
        {
            var done = 0;

            while (done < unpacked.Length)
            {
                var n = zip.Read(unpacked, done, unpacked.Length - done);

                if (n <= 0)
                {
                    break;
                }

                done += n;
            }
        }

        if (entry.Flag == 3)
        {
            using var bwt = new MemoryStream(unpacked, 4, unpacked.Length - 4);

            return BwtDecompress.Decompress(bwt, unpacked.Length - 4);
        }

        return unpacked;
    }

    private static ushort[][] ReadHues(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var blocks = bytes.Length / 708;
        var hues = new ushort[blocks * 8][];

        for (var b = 0; b < blocks; b++)
        {
            for (var h = 0; h < 8; h++)
            {
                var at = b * 708 + 4 + h * 88;
                var colours = new ushort[32];

                for (var c = 0; c < 32; c++)
                {
                    colours[c] = (ushort)(BitConverter.ToUInt16(bytes, at + c * 2) & 0x7FFF);
                }

                hues[b * 8 + h] = colours;
            }
        }

        return hues;
    }

    private void ReadTileData(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var wide = bytes.Length >= 3188736;
        var flagSize = wide ? 8 : 4;
        var landSize = flagSize + 2 + 20;
        var at = 0;

        for (var i = 0; i < 0x4000 && at + landSize <= bytes.Length; i++)
        {
            if ((i & 0x1F) == 0)
            {
                at += 4;
            }

            _landTexture[i] = BitConverter.ToUInt16(bytes, at + flagSize);
            at += landSize;
        }

        var itemSize = flagSize + 1 + 1 + 4 + 2 + 2 + 2 + 1 + 20;
        var items = wide ? 0x10000 : bytes.Length >= 1644544 ? 0x8000 : 0x4000;

        for (var i = 0; i < items && at + itemSize <= bytes.Length; i++)
        {
            if ((i & 0x1F) == 0)
            {
                at += 4;
            }

            _itemAnim[i] = BitConverter.ToUInt16(bytes, at + flagSize + 6);
            at += itemSize;
        }
    }

    private static IEnumerable<string[]> DefLines(string path)
    {
        if (path == null)
        {
            yield break;
        }

        foreach (var raw in File.ReadLines(path))
        {
            var line = raw;
            var hash = line.IndexOf('#');

            if (hash >= 0)
            {
                line = line[..hash];
            }

            line = line.Trim().Trim('"');

            if (line.Length == 0)
            {
                continue;
            }

            yield return line.Split([' ', '\t', ','], StringSplitOptions.RemoveEmptyEntries);
        }
    }

    private static bool Int(string text, out int value)
    {
        text = text.Trim('{', '}', ' ');

        return text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? int.TryParse(text[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value)
            : int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    private void ReadMobTypes(string path)
    {
        foreach (var parts in DefLines(path))
        {
            if (parts.Length < 2 || !Int(parts[0], out var body))
            {
                continue;
            }

            BotAnimType? type = parts[1].ToUpperInvariant() switch
            {
                "MONSTER"     => BotAnimType.Monster,
                "SEA_MONSTER" => BotAnimType.SeaMonster,
                "ANIMAL"      => BotAnimType.Animal,
                "HUMAN"       => BotAnimType.Human,
                "EQUIPMENT"   => BotAnimType.Equipment,
                _             => null
            };

            if (type == null)
            {
                continue;
            }

            var flags = parts.Length > 2 && uint.TryParse(parts[2], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var f) ? f : 0u;
            _mobTypes[body] = (type.Value, flags);
        }
    }

    private void ReadBodyConv(string path)
    {
        foreach (var parts in DefLines(path))
        {
            if (parts.Length < 2 || !Int(parts[0], out var body))
            {
                continue;
            }

            for (var i = 1; i < parts.Length && i < 6; i++)
            {
                if (Int(parts[i], out var other) && other >= 0 && _animIdx[i] != null)
                {
                    _bodyConv[body] = (i, other);
                }
            }
        }
    }

    private void ReadBodyDef(string path)
    {
        foreach (var raw in path == null ? [] : File.ReadLines(path))
        {
            var line = raw;
            var hash = line.IndexOf('#');

            if (hash >= 0)
            {
                line = line[..hash];
            }

            var open = line.IndexOf('{');
            var close = line.IndexOf('}');

            if (open < 0 || close < open || !Int(line[..open], out var body))
            {
                continue;
            }

            var list = line[(open + 1)..close].Split(',', StringSplitOptions.RemoveEmptyEntries);

            if (list.Length == 0 || !Int(list[^1], out var instead))
            {
                continue;
            }

            Int(line[(close + 1)..].Trim(), out var hue);
            _bodyDef[body] = (instead, hue);
        }
    }

    private void ReadEquipConv(string path)
    {
        foreach (var parts in DefLines(path))
        {
            if (parts.Length >= 3 && Int(parts[0], out var body) && Int(parts[1], out var worn) && Int(parts[2], out var drawn))
            {
                var hue = parts.Length >= 5 && Int(parts[4], out var h) ? h : 0;
                _equipConv[(body, worn)] = (drawn, hue);
            }
        }
    }

    private void ReadSequences(string path)
    {
        using var handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.Read);

        foreach (var (_, entry) in ReadUop(path))
        {
            var data = Load(handle, entry);

            if (data == null || data.Length < 56)
            {
                continue;
            }

            var body = (int)BitConverter.ToUInt32(data, 0);
            var count = BitConverter.ToInt32(data, 52);
            var at = 56;
            int[] groups = null;

            for (var i = 0; i < count && at + 72 <= data.Length; i++, at += 72)
            {
                var old = BitConverter.ToInt32(data, at);
                var frames = BitConverter.ToUInt32(data, at + 4);
                var instead = BitConverter.ToInt32(data, at + 8);

                if (frames == 0 && old is >= 0 and < MostActions && instead is >= 0 and < MostActions)
                {
                    if (groups == null)
                    {
                        groups = new int[MostActions];

                        for (var g = 0; g < groups.Length; g++)
                        {
                            groups[g] = g;
                        }
                    }

                    groups[old] = instead;
                }
            }

            if (groups != null)
            {
                _uopGroups[body] = groups;
            }
        }
    }

    public static uint Argb(ushort c)
    {
        var r = (c >> 10) & 0x1F;
        var g = (c >> 5) & 0x1F;
        var b = c & 0x1F;

        return 0xFF000000u | (uint)(((r << 3) | (r >> 2)) << 16) | (uint)(((g << 3) | (g >> 2)) << 8) | (uint)((b << 3) | (b >> 2));
    }

    public ushort[] Hue(int hue, out bool partial)
    {
        partial = (hue & 0x8000) != 0;
        hue &= 0x3FFF;

        return hue > 0 && hue <= _hues.Length ? _hues[hue - 1] : null;
    }

    public static ushort Through(ushort c, ushort[] hue, bool partial)
    {
        if (hue == null || c == 0)
        {
            return c;
        }

        var r = (c >> 10) & 0x1F;

        if (partial)
        {
            var g = (c >> 5) & 0x1F;
            var b = c & 0x1F;

            return r == g && r == b ? hue[r] : c;
        }

        return hue[r];
    }

    public int LandTexture(int land) => land is >= 0 and < 0x4000 ? _landTexture[land] : 0;

    public bool HasTexture(int land)
    {
        var tex = LandTexture(land);

        return _texIndex != null && tex > 0 && tex < _texIndex.Length && _texIndex[tex].Length > 0;
    }

    public int ItemAnim(int item) => item is >= 0 and < 0x10000 ? _itemAnim[item] : 0;

    public BotSprite Land(int id)
    {
        if (id is < 0 or >= 0x4000 || !_artIndex.TryGetValue(id, out var entry))
        {
            return null;
        }

        var data = Load(_art, entry);

        if (data == null || data.Length < 2024)
        {
            return null;
        }

        var pixels = new uint[44 * 44];
        var at = 0;

        for (var row = 0; row < 44; row++)
        {
            var start = row < 22 ? 21 - row : row - 22;
            var width = row < 22 ? (row + 1) * 2 : (44 - row) * 2;

            for (var x = start; x < start + width; x++, at += 2)
            {
                var c = (ushort)(BitConverter.ToUInt16(data, at) & 0x7FFF);
                pixels[row * 44 + x] = c == 0 ? 0 : Argb(c);
            }
        }

        return new BotSprite { Width = 44, Height = 44, AnchorX = 0, AnchorY = 0, Pixels = pixels };
    }

    public BotSprite Texture(int land)
    {
        var tex = LandTexture(land);

        if (!HasTexture(land))
        {
            return null;
        }

        var entry = _texIndex[tex];
        var data = Load(_tex, entry);

        if (data == null)
        {
            return null;
        }

        var side = data.Length >= 0x8000 ? 128 : 64;
        var pixels = new uint[side * side];

        for (var i = 0; i < pixels.Length && i * 2 + 1 < data.Length; i++)
        {
            pixels[i] = Argb((ushort)(BitConverter.ToUInt16(data, i * 2) & 0x7FFF));
        }

        return new BotSprite { Width = side, Height = side, Pixels = pixels };
    }

    public BotSprite Static(int id, int hue, bool partialByTile)
    {
        if (id is < 0 or >= 0x10000 || !_artIndex.TryGetValue(0x4000 + id, out var entry))
        {
            return null;
        }

        var data = Load(_art, entry);

        if (data == null || data.Length < 8)
        {
            return null;
        }

        int width = BitConverter.ToUInt16(data, 4);
        int height = BitConverter.ToUInt16(data, 6);

        if (width == 0 || height == 0 || width > 2048 || height > 2048 || 8 + height * 2 > data.Length)
        {
            return null;
        }

        var colours = Hue(hue, out var partial);
        partial |= partialByTile;

        var pixels = new uint[width * height];
        var lookups = 8;
        var start = 8 + height * 2;

        for (var y = 0; y < height; y++)
        {
            var at = start + BitConverter.ToUInt16(data, lookups + y * 2) * 2;
            var x = 0;

            while (at + 4 <= data.Length)
            {
                int skip = BitConverter.ToUInt16(data, at);
                int run = BitConverter.ToUInt16(data, at + 2);
                at += 4;

                if (skip + run == 0)
                {
                    break;
                }

                x += skip;

                for (var i = 0; i < run && at + 2 <= data.Length; i++, x++, at += 2)
                {
                    var c = (ushort)(BitConverter.ToUInt16(data, at) & 0x7FFF);

                    if (c != 0 && x >= 0 && x < width)
                    {
                        pixels[y * width + x] = Argb(Through(c, colours, partial));
                    }
                }
            }
        }

        return new BotSprite { Width = width, Height = height, AnchorX = width / 2, AnchorY = height, Pixels = pixels };
    }

    /// <summary>What a body is drawn as: from which file, under which number, laid out how.</summary>
    public readonly struct AnimSource
    {
        public readonly int File;

        public readonly int Body;

        public readonly BotAnimType Type;

        public readonly uint Flags;

        public readonly int Hue;

        public AnimSource(int file, int body, BotAnimType type, uint flags, int hue)
        {
            File = file;
            Body = body;
            Type = type;
            Flags = flags;
            Hue = hue;
        }

        public bool Uop => File < 0;
    }

    public const uint FlagIdleAt8 = 0x4;
    public const uint FlagLowExtended = 0x20;
    public const uint FlagByLowGroup = 0x40;
    public const uint FlagByPeopleGroup = 0x400;
    public const uint FlagUop = 0x10000;

    private static BotAnimType TypeByNumber(int body, int file) =>
        file switch
        {
            1 => body < 200 ? BotAnimType.Monster : BotAnimType.Animal,
            2 => body < 300 ? BotAnimType.Animal : body < 400 ? BotAnimType.Monster : BotAnimType.Human,
            _ => body < 200 ? BotAnimType.Monster : body < 400 ? BotAnimType.Animal : BotAnimType.Human
        };

    private AnimSource Source(int body)
    {
        var known = _mobTypes.TryGetValue(body, out var mob);
        var flags = known ? mob.Flags : 0u;

        if ((flags & FlagUop) != 0)
        {
            return new AnimSource(-1, body, known && mob.Type != BotAnimType.Unknown ? mob.Type : TypeByNumber(body, 0), flags, 0);
        }

        var file = 0;
        var drawn = body;

        if (_bodyConv.TryGetValue(body, out var conv))
        {
            file = conv.File;
            drawn = conv.Body;
        }

        var type = known && mob.Type != BotAnimType.Unknown ? mob.Type : TypeByNumber(drawn, file);

        return new AnimSource(file, drawn, type, flags, 0);
    }

    public AnimSource Resolve(int body, bool worn = false)
    {
        var source = Source(body);

        if (!worn && !HasAny(source) && _bodyDef.TryGetValue(body, out var instead) && instead.Body != body)
        {
            var other = Source(instead.Body);

            return new AnimSource(other.File, other.Body, other.Type, other.Flags, instead.Hue);
        }

        return source;
    }

    public int WornAnim(int item, int wearer, out int hue)
    {
        hue = 0;
        var anim = ItemAnim(item);

        if (anim <= 0)
        {
            return 0;
        }

        if (_equipConv.TryGetValue((wearer, anim), out var conv) && conv.Anim > 0)
        {
            hue = conv.Hue;

            return conv.Anim;
        }

        return anim;
    }

    private bool HasAny(in AnimSource source)
    {
        for (var g = 0; g < 4; g++)
        {
            if (Exists(source, g))
            {
                return true;
            }
        }

        return false;
    }

    private long MulBase(in AnimSource source, out int groups)
    {
        var people = false;
        var low = false;

        switch (source.Type)
        {
            case BotAnimType.Monster:
                people = (source.Flags & FlagByPeopleGroup) != 0;
                low = !people && (source.Flags & FlagByLowGroup) != 0;
                break;
            case BotAnimType.SeaMonster:
                groups = 13;

                return source.Body * 110L;
            case BotAnimType.Animal:
                if ((source.Flags & FlagLowExtended) != 0)
                {
                    people = (source.Flags & FlagByPeopleGroup) != 0;
                    low = !people && (source.Flags & FlagByLowGroup) != 0;
                }
                else
                {
                    low = true;
                }

                break;
            default:
                people = true;
                break;
        }

        if (people)
        {
            groups = 35;

            return (source.Body - 400) * 175L + 35000;
        }

        if (low)
        {
            groups = 13;

            return (source.Body - 200) * 65L + 22000;
        }

        groups = 22;

        return source.Body * 110L;
    }

    private bool MulEntry(in AnimSource source, int group, int fileDir, out Entry entry)
    {
        entry = default;

        var file = source.File;

        if (file < 0 || file >= _animIdx.Length || _animIdx[file] == null)
        {
            return false;
        }

        var start = MulBase(source, out var groups);

        if (start < 0 || group < 0 || group >= groups)
        {
            return false;
        }

        var at = (start + group * 5 + fileDir) * 12;

        if (at < 0 || at + 12 > _animIdxLength[file])
        {
            return false;
        }

        Span<byte> block = stackalloc byte[12];

        if (RandomAccess.Read(_animIdx[file], block, at) < 12)
        {
            return false;
        }

        var offset = BitConverter.ToInt32(block);
        var length = BitConverter.ToInt32(block[4..]);

        if (offset < 0 || length <= 0)
        {
            return false;
        }

        entry = new Entry(offset, length, length, 0);

        return true;
    }

    private int UopGroup(int body, int group) =>
        _uopGroups.TryGetValue(body, out var groups) && group is >= 0 and < MostActions ? groups[group] : group;

    private bool UopEntry(in AnimSource source, int group, out int file, out Entry entry)
    {
        var hash = UOPFiles.HashLittle2($"build/animationlegacyframe/{source.Body:D6}/{UopGroup(source.Body, group):D2}.bin");

        if (_uopFrames.TryGetValue(hash, out var found) && _animUop[found.File] != null)
        {
            file = found.File;
            entry = found.Entry;

            return true;
        }

        file = -1;
        entry = default;

        return false;
    }

    public bool Exists(in AnimSource source, int group) =>
        source.Uop ? UopEntry(source, group, out _, out _) : MulEntry(source, group, 0, out _);

    public static int FileDirection(int dir, out bool mirror)
    {
        switch (dir & 7)
        {
            case 0:
                mirror = true;

                return 3;
            case 1:
                mirror = true;

                return 2;
            case 2:
                mirror = true;

                return 1;
            case 3:
                mirror = false;

                return 0;
            case 4:
                mirror = false;

                return 1;
            case 5:
                mirror = false;

                return 2;
            case 6:
                mirror = false;

                return 3;
            default:
                mirror = false;

                return 4;
        }
    }

    /// <summary>What the body is doing, as the page asks for it; turned into a group by the body's own kind.</summary>
    public enum Pose
    {
        Stand,
        War,
        WarTwoHanded,
        Ride,
        Die
    }

    public int Group(in AnimSource source, Pose pose)
    {
        var type = source.Type;
        var flags = source.Flags;
        var uop = (flags & FlagUop) != 0;

        if (pose == Pose.Die)
        {
            return type switch
            {
                BotAnimType.Monster when (flags & FlagByPeopleGroup) != 0 => 21,
                BotAnimType.Monster when (flags & FlagByLowGroup) != 0    => 8,
                BotAnimType.Monster                                       => 2,
                BotAnimType.Animal when (flags & FlagLowExtended) != 0 && (flags & FlagByPeopleGroup) != 0 => 21,
                BotAnimType.Animal when (flags & FlagLowExtended) != 0 && (flags & FlagByLowGroup) == 0    => 2,
                BotAnimType.Animal or BotAnimType.SeaMonster => 8,
                _                                            => 21
            };
        }

        var war = pose is Pose.War or Pose.WarTwoHanded;

        switch (type)
        {
            case BotAnimType.Monster:
                return High(source, war);
            case BotAnimType.Animal:
                if ((flags & FlagLowExtended) != 0)
                {
                    return High(source, war);
                }

                if (uop)
                {
                    return war && Exists(source, 1) ? 1 : 25;
                }

                return 2;
            case BotAnimType.SeaMonster:
                return 2;
            default:
                return pose switch
                {
                    Pose.Ride         => 25,
                    Pose.War          => 7,
                    Pose.WarTwoHanded => 8,
                    _                 => 4
                };
        }
    }

    private int High(in AnimSource source, bool war)
    {
        var flags = source.Flags;

        if ((flags & FlagByPeopleGroup) != 0)
        {
            return 0;
        }

        if ((flags & FlagByLowGroup) != 0)
        {
            return 2;
        }

        if ((flags & FlagIdleAt8) != 0 && Exists(source, 8))
        {
            return 8;
        }

        return (flags & FlagUop) != 0 && !war ? 25 : 1;
    }

    public BotSprite Anim(int body, int dir, int hue, Pose pose, bool worn = false)
    {
        var source = Resolve(body, worn);

        if (hue == 0 && source.Hue != 0)
        {
            hue = source.Hue;
        }

        var group = Group(source, pose);
        var fileDir = FileDirection(dir, out var mirror);
        var sprite = Frame(source, group, fileDir, pose == Pose.Die, hue);

        if (sprite == null && group != 0)
        {
            sprite = Frame(source, 0, fileDir, false, hue);
        }

        return sprite == null || !mirror ? sprite : Mirror(sprite);
    }

    private BotSprite Frame(in AnimSource source, int group, int fileDir, bool last, int hue)
    {
        byte[] data;
        int palette;
        int frame;

        if (source.Uop)
        {
            if (!UopEntry(source, group, out var file, out var entry))
            {
                return null;
            }

            data = Load(_animUop[file], entry);
            frame = UopFrame(data, fileDir, last);

            palette = frame;
            frame += 512;
        }
        else
        {
            if (!MulEntry(source, group, fileDir, out var entry))
            {
                return null;
            }

            data = ReadAt(_animMul[source.File], entry.Offset, entry.Length);
            frame = MulFrame(data, last);

            palette = 0;
        }

        return frame < 0 || palette < 0 ? null : Decode(data, palette, frame, hue);
    }

    private static int MulFrame(byte[] data, bool last)
    {
        if (data == null || data.Length < 520)
        {
            return -1;
        }

        var count = BitConverter.ToInt32(data, 512);

        if (count <= 0 || 516 + count * 4 > data.Length)
        {
            return -1;
        }

        var offset = BitConverter.ToInt32(data, 516 + (last ? count - 1 : 0) * 4);

        return offset > 0 && 512 + offset + 8 <= data.Length ? 512 + offset : -1;
    }

    private static int UopFrame(byte[] data, int fileDir, bool last)
    {
        if (data == null || data.Length < 40)
        {
            return -1;
        }

        var count = BitConverter.ToInt32(data, 32);
        var start = (int)BitConverter.ToUInt32(data, 36);

        if (count <= 0 || start <= 0 || start + count * 16 > data.Length)
        {
            return -1;
        }

        var most = 0;

        for (var i = 0; i < count; i++)
        {
            most = Math.Max(most, (int)BitConverter.ToUInt16(data, start + i * 16 + 2));
        }

        var perDir = Math.Max(1, most / 5);
        var first = fileDir * perDir + 1;
        var final = first + perDir - 1;
        var best = -1;
        var bestId = 0;

        for (var i = 0; i < count; i++)
        {
            var at = start + i * 16;
            int id = BitConverter.ToUInt16(data, at + 2);

            if (id < first || id > final)
            {
                continue;
            }

            if (best < 0 || (last ? id > bestId : id < bestId))
            {
                best = at;
                bestId = id;
            }
        }

        if (best < 0)
        {
            return -1;
        }

        var pixels = (int)BitConverter.ToUInt32(data, best + 12);

        return best + pixels + 512 + 8 <= data.Length ? best + pixels : -1;
    }

    private BotSprite Decode(byte[] data, int paletteAt, int at, int hue)
    {
        if (at + 8 > data.Length || paletteAt + 512 > data.Length)
        {
            return null;
        }

        var colours = Hue(hue, out var partial);
        Span<uint> palette = stackalloc uint[256];

        for (var i = 0; i < 256; i++)
        {
            palette[i] = Argb(Through((ushort)(BitConverter.ToUInt16(data, paletteAt + i * 2) & 0x7FFF), colours, partial));
        }

        int centreX = BitConverter.ToInt16(data, at);
        int centreY = BitConverter.ToInt16(data, at + 2);
        int width = BitConverter.ToUInt16(data, at + 4);
        int height = BitConverter.ToUInt16(data, at + 6);
        at += 8;

        if (width == 0 || height == 0 || width > 1024 || height > 1024)
        {
            return null;
        }

        var pixels = new uint[width * height];

        while (at + 4 <= data.Length)
        {
            var header = BitConverter.ToUInt32(data, at);
            at += 4;

            if (header == 0x7FFF7FFF)
            {
                break;
            }

            var run = (int)(header & 0xFFF);
            var x = (int)((header >> 22) & 0x3FF);
            var y = (int)((header >> 12) & 0x3FF);

            if ((x & 0x200) != 0)
            {
                x |= unchecked((int)0xFFFFFE00);
            }

            if ((y & 0x200) != 0)
            {
                y |= unchecked((int)0xFFFFFE00);
            }

            x += centreX;
            y += centreY + height;

            for (var i = 0; i < run && at < data.Length; i++, at++, x++)
            {
                if (x >= 0 && x < width && y >= 0 && y < height)
                {
                    pixels[y * width + x] = palette[data[at]];
                }
            }
        }

        return new BotSprite { Width = width, Height = height, AnchorX = centreX, AnchorY = height + centreY, Pixels = pixels };
    }

    private static BotSprite Mirror(BotSprite s)
    {
        var pixels = new uint[s.Pixels.Length];

        for (var y = 0; y < s.Height; y++)
        {
            for (var x = 0; x < s.Width; x++)
            {
                pixels[y * s.Width + (s.Width - 1 - x)] = s.Pixels[y * s.Width + x];
            }
        }

        return new BotSprite { Width = s.Width, Height = s.Height, AnchorX = s.Width - s.AnchorX, AnchorY = s.AnchorY, Pixels = pixels };
    }
}
