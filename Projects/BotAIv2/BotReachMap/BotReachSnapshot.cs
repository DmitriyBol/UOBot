using System;

namespace Server.BotAI.V2;

/// <summary>
/// The reach map as one solve left it: a class for every tile, and for a tile whose standing places differ (a bridge over a
/// road, a roof over a room) the class of each with its height. Immutable: made on a worker, published through
/// <see cref="BotReachMap"/>'s one volatile reference, read by the loop's walkers and the dashboard's threads alike.
///
/// <para>
/// <b>A byte a tile, shared where a cluster is all one thing.</b> 7168 × 4096 tiles of Felucca are 29 million bytes if every
/// cluster had its own array; the open sea, the solid rock and the open fields are clusters of one class, and those all point at
/// one array per class, so the snapshot holds only the clusters with a border in them.
/// </para>
/// </summary>
public sealed class BotReachSnapshot
{
    public BotReachSnapshot(
        BotReachSolveInput input,
        byte[][] classes,
        int[][] layerClasses,
        int[] bases,
        int[] roots,
        byte[] regionClass,
        int[] landTiles,
        long[] tiles,
        int lands,
        BotReachLand[] largest,
        int[] mixed,
        int regions,
        int edges,
        bool homed,
        double solveMs,
        int ownArrays
    )
    {
        Map = input.Map;
        MapName = input.MapName;
        Width = input.Width;
        Height = input.Height;
        ClustersX = input.ClustersX;
        ClustersY = input.ClustersY;
        Clusters = input.Clusters;
        Home = input.Home;
        Gates = input.Gates.Count;
        Version = input.Version;
        Classes = classes;
        LayerClasses = layerClasses;
        Bases = bases;
        Roots = roots;
        RegionClass = regionClass;
        LandTiles = landTiles;
        Tiles = tiles;
        Lands = lands;
        Largest = largest;
        Mixed = mixed;
        Regions = regions;
        Edges = edges;
        Homed = homed;
        SolveMs = solveMs;
        At = DateTime.UtcNow;
        OwnArrays = ownArrays;
    }

    public Map Map { get; }

    public string MapName { get; }

    public int Width { get; }

    public int Height { get; }

    public int ClustersX { get; }

    public int ClustersY { get; }

    public BotReachCluster[] Clusters { get; }

    public Point3D Home { get; }

    public int Gates { get; }

    public int Version { get; }

    public byte[][] Classes { get; }

    public int[][] LayerClasses { get; }

    public int[] Bases { get; }

    public int[] Roots { get; }

    public byte[] RegionClass { get; }

    public int[] LandTiles { get; }

    public long[] Tiles { get; }

    public int Lands { get; }

    public BotReachLand[] Largest { get; }

    public int[] Mixed { get; }

    public int Regions { get; }

    public int Edges { get; }

    public bool Homed { get; }

    public double SolveMs { get; }

    public DateTime At { get; }

    public int OwnArrays { get; }

    public bool Holds(Map map) => map != null && map == Map;

    public bool Inside(int x, int y) => (uint)x < (uint)Width && (uint)y < (uint)Height;

    public BotReachClass ClassAt(int x, int y)
    {
        if (!Inside(x, y))
        {
            return BotReachClass.Blocked;
        }

        return (BotReachClass)Classes[(y >> 4) * ClustersX + (x >> 4)][((y & 15) << 4) | (x & 15)];
    }

    public BotReachClass ClassAt(int x, int y, int z)
    {
        if (!Inside(x, y))
        {
            return BotReachClass.Blocked;
        }

        var c = (y >> 4) * ClustersX + (x >> 4);
        var cell = ((y & 15) << 4) | (x & 15);
        var layers = LayerClasses[c];

        if (layers == null)
        {
            return (BotReachClass)Classes[c][cell];
        }

        var lo = 0;
        var hi = layers.Length - 1;

        while (lo < hi)
        {
            var mid = (lo + hi) >> 1;

            if ((layers[mid] & 0xFF) < cell)
            {
                lo = mid + 1;
            }
            else
            {
                hi = mid;
            }
        }

        if (lo >= layers.Length || (layers[lo] & 0xFF) != cell)
        {
            return (BotReachClass)Classes[c][cell];
        }

        var best = (BotReachClass)Classes[c][cell];
        var gap = int.MaxValue;

        for (var i = lo; i < layers.Length && (layers[i] & 0xFF) == cell; i++)
        {
            var g = Math.Abs(((layers[i] >> 8) & 0xFF) - 128 - z);

            if (g < gap)
            {
                gap = g;
                best = (BotReachClass)((layers[i] >> 16) & 0xFF);
            }
        }

        return best;
    }

    public bool Any(int x, int y, int reach, BotReachClass want)
    {
        var w = (byte)want;

        for (var dy = -reach; dy <= reach; dy++)
        {
            var ty = y + dy;

            if ((uint)ty >= (uint)Height)
            {
                continue;
            }

            var row = (ty >> 4) * ClustersX;
            var cy = (ty & 15) << 4;

            for (var dx = -reach; dx <= reach; dx++)
            {
                var tx = x + dx;

                if ((uint)tx < (uint)Width && Classes[row + (tx >> 4)][cy | (tx & 15)] == w)
                {
                    return true;
                }
            }
        }

        return false;
    }

    public int LandAt(int x, int y, out int tiles, out BotReachClass kind)
    {
        tiles = 0;
        kind = ClassAt(x, y);

        if (!Inside(x, y))
        {
            return -1;
        }

        var c = (y >> 4) * ClustersX + (x >> 4);

        if (Clusters[c] is not { } cluster)
        {
            return -1;
        }

        var code = cluster.Cells[((y & 15) << 4) | (x & 15)];

        if (code >= BotReachCluster.Crowded)
        {
            return -1;
        }

        var g = Bases[c] + code;
        var root = Roots[g];

        tiles = LandTiles[root];

        return root;
    }

    public byte[] Picture(int x0, int y0, int columns, int rows, int scale)
    {
        var bytes = new byte[columns * rows];
        Span<int> seen = stackalloc int[6];

        for (var py = 0; py < rows; py++)
        {
            for (var px = 0; px < columns; px++)
            {
                var tx = x0 + px * scale;
                var ty = y0 + py * scale;

                if (scale == 1)
                {
                    bytes[py * columns + px] = Inside(tx, ty) ? Classes[(ty >> 4) * ClustersX + (tx >> 4)][((ty & 15) << 4) | (tx & 15)] : (byte)0;

                    continue;
                }

                seen.Clear();

                for (var dy = 0; dy < scale; dy++)
                {
                    for (var dx = 0; dx < scale; dx++)
                    {
                        var x = tx + dx;
                        var y = ty + dy;

                        if (Inside(x, y))
                        {
                            seen[Classes[(y >> 4) * ClustersX + (x >> 4)][((y & 15) << 4) | (x & 15)]]++;
                        }
                    }
                }

                var few = Math.Max(1, scale * scale / 8);

                bytes[py * columns + px] = seen[(int)BotReachClass.Pocket] >= few ? (byte)BotReachClass.Pocket
                    : seen[(int)BotReachClass.Isle] >= few ? (byte)BotReachClass.Isle
                    : seen[(int)BotReachClass.Open] * 2 >= scale * scale ? (byte)BotReachClass.Open
                    : seen[(int)BotReachClass.Water] >= seen[(int)BotReachClass.Blocked] && seen[(int)BotReachClass.Water] > 0 ? (byte)BotReachClass.Water
                    : seen[(int)BotReachClass.Blocked] > 0 ? (byte)BotReachClass.Blocked
                    : seen[(int)BotReachClass.Open] > 0 ? (byte)BotReachClass.Open
                    : (byte)BotReachClass.Unknown;
            }
        }

        return bytes;
    }
}
