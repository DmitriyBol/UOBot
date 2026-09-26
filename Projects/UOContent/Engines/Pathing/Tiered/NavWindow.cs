using System;
using System.Collections.Generic;

namespace Server.Engines.Pathing.Tiered;

/// <summary>
/// One cluster of the navigation graph with the ring of cells round it, probed lazily from the terrain: each cell's
/// standable surfaces (its strata — a floor over a street, a bridge over a road are separate places) and, per surface,
/// the raw step mask and the height each step lands at.
///
/// <para>
/// <b>The step rule is the strict one a player walks by:</b> a diagonal needs both of its flanking straight steps open
/// from the same surface, and a step is taken only onto a surface of the next cell within <see cref="MatchTolerance"/> of
/// where the engine says it lands. The graph, the planners' insertion searches and the tests all step through
/// <see cref="Step"/>, so they cannot disagree about what is walkable.
/// </para>
/// </summary>
public sealed class NavWindow
{
    public const int ClusterSide = 16;

    public const int Side = ClusterSide + 2;

    public const int MaxStrata = 4;

    public const int MatchTolerance = 3;

    public const int States = ClusterSide * ClusterSide * MaxStrata;

    private static readonly int[] _dx = [0, 1, 1, 1, 0, -1, -1, -1];

    private static readonly int[] _dy = [-1, -1, 0, 1, 1, 1, 0, -1];

    public static int Dx(int d) => _dx[d];

    public static int Dy(int d) => _dy[d];

    private INavTerrain _terrain;

    private int _ox;

    private int _oy;

    private readonly bool[] _probed = new bool[Side * Side];

    private readonly byte[] _count = new byte[Side * Side];

    private readonly sbyte[] _z = new sbyte[Side * Side * MaxStrata];

    private readonly byte[] _walk = new byte[Side * Side * MaxStrata];

    private readonly sbyte[] _land = new sbyte[Side * Side * MaxStrata * 8];

    private readonly int[] _dist = new int[States];

    private readonly PriorityQueue<int, int> _open = new();

    public long Probes { get; private set; }

    public int X0 { get; private set; }

    public int Y0 { get; private set; }

    public int Cluster { get; set; } = -1;

    public long Used { get; set; }

    public void Reset(INavTerrain terrain, int cluster, int x0, int y0)
    {
        _terrain = terrain;
        Cluster = cluster;
        X0 = x0;
        Y0 = y0;
        _ox = x0 - 1;
        _oy = y0 - 1;
        Array.Clear(_probed);
    }

    public bool InCluster(int x, int y) => x >= X0 && y >= Y0 && x < X0 + ClusterSide && y < Y0 + ClusterSide;

    public bool InWindow(int x, int y) => x >= _ox && y >= _oy && x < _ox + Side && y < _oy + Side;

    private int CellAt(int x, int y) => (y - _oy) * Side + (x - _ox);

    public int State(int x, int y, int s) => (((y - Y0) << 4) | (x - X0)) * MaxStrata + s;

    public int StateX(int state) => X0 + (state / MaxStrata & 15);

    public int StateY(int state) => Y0 + (state / MaxStrata >> 4);

    public static int StateStratum(int state) => state % MaxStrata;

    private void Probe(int c, int x, int y)
    {
        _probed[c] = true;
        Probes++;

        if (x < 0 || y < 0 || x >= _terrain.Width || y >= _terrain.Height)
        {
            _count[c] = 0;

            return;
        }

        Span<sbyte> zs = stackalloc sbyte[16];
        var n = Math.Min(_terrain.Surfaces(x, y, zs), MaxStrata);

        _count[c] = (byte)n;

        for (var s = 0; s < n; s++)
        {
            var at = c * MaxStrata + s;
            var mask = _terrain.Step(x, y, zs[s]);

            _z[at] = zs[s];
            _walk[at] = mask.WalkMask;

            var land = at * 8;

            _land[land] = mask.WalkZ_N;
            _land[land + 1] = mask.WalkZ_NE;
            _land[land + 2] = mask.WalkZ_E;
            _land[land + 3] = mask.WalkZ_SE;
            _land[land + 4] = mask.WalkZ_S;
            _land[land + 5] = mask.WalkZ_SW;
            _land[land + 6] = mask.WalkZ_W;
            _land[land + 7] = mask.WalkZ_NW;
        }
    }

    public int Count(int x, int y)
    {
        if (!InWindow(x, y))
        {
            return 0;
        }

        var c = CellAt(x, y);

        if (!_probed[c])
        {
            Probe(c, x, y);
        }

        return _count[c];
    }

    public sbyte Z(int x, int y, int s) => _z[CellAt(x, y) * MaxStrata + s];

    public int Match(int x, int y, int z)
    {
        var n = Count(x, y);
        var best = -1;
        var bestGap = MatchTolerance + 1;
        var c = CellAt(x, y) * MaxStrata;

        for (var s = 0; s < n; s++)
        {
            var gap = Math.Abs(_z[c + s] - z);

            if (gap < bestGap)
            {
                bestGap = gap;
                best = s;
            }
        }

        return best;
    }

    public int Nearest(int x, int y, int z)
    {
        var n = Count(x, y);
        var best = -1;
        var bestGap = int.MaxValue;
        var c = n > 0 ? CellAt(x, y) * MaxStrata : 0;

        for (var s = 0; s < n; s++)
        {
            var gap = Math.Abs(_z[c + s] - z);

            if (gap < bestGap)
            {
                bestGap = gap;
                best = s;
            }
        }

        return best;
    }

    public bool Step(int x, int y, int s, int d, out int nx, out int ny, out int ns)
    {
        nx = x + _dx[d];
        ny = y + _dy[d];
        ns = -1;

        if (Count(x, y) <= s)
        {
            return false;
        }

        var at = CellAt(x, y) * MaxStrata + s;
        var walk = _walk[at];

        if ((walk & (1 << d)) == 0)
        {
            return false;
        }

        if ((d & 1) != 0 && ((walk & (1 << ((d + 7) & 7))) == 0 || (walk & (1 << ((d + 1) & 7))) == 0))
        {
            return false;
        }

        if (!InWindow(nx, ny) || Count(nx, ny) == 0)
        {
            return false;
        }

        ns = Match(nx, ny, _land[at * 8 + d]);

        return ns >= 0;
    }

    public int[] Flood(int x, int y, int s, bool backward)
    {
        Array.Fill(_dist, -1);
        _open.Clear();

        if (!InCluster(x, y) || Count(x, y) <= s)
        {
            return _dist;
        }

        var from = State(x, y, s);

        _dist[from] = 0;
        _open.Enqueue(from, 0);

        while (_open.TryDequeue(out var state, out var cost))
        {
            if (cost > _dist[state])
            {
                continue;
            }

            var cx = StateX(state);
            var cy = StateY(state);
            var cs = StateStratum(state);

            for (var d = 0; d < 8; d++)
            {
                int tx;
                int ty;
                int ts;

                if (!backward)
                {
                    if (!Step(cx, cy, cs, d, out tx, out ty, out ts) || !InCluster(tx, ty))
                    {
                        continue;
                    }
                }
                else
                {
                    tx = cx - _dx[d];
                    ty = cy - _dy[d];
                    ts = -1;

                    if (!InCluster(tx, ty))
                    {
                        continue;
                    }

                    var n = Count(tx, ty);

                    for (var ps = 0; ps < n; ps++)
                    {
                        if (Step(tx, ty, ps, d, out _, out _, out var landed) && landed == cs)
                        {
                            Relax(State(tx, ty, ps), cost + NavCost.Of(d));
                        }
                    }

                    continue;
                }

                Relax(State(tx, ty, ts), cost + NavCost.Of(d));
            }
        }

        return _dist;
    }

    private void Relax(int state, int cost)
    {
        if (_dist[state] >= 0 && _dist[state] <= cost)
        {
            return;
        }

        _dist[state] = cost;
        _open.Enqueue(state, cost);
    }
}
