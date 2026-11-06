using System;
using Server.Engines.Pathing.Cache;

namespace Server.Engines.Pathing.Tiered;

public interface INavTerrain
{
    int Width { get; }

    int Height { get; }

    int Surfaces(int x, int y, Span<sbyte> zs);

    StepMask Step(int x, int y, sbyte z);
}

public sealed class MapNavTerrain : INavTerrain
{
    private readonly Map _map;

    public MapNavTerrain(Map map) => _map = map;

    public Map Map => _map;

    public int Width => _map.Width;

    public int Height => _map.Height;

    public int Surfaces(int x, int y, Span<sbyte> zs) =>
        Multis(x, y)
            ? StepProbe.ComputeStandableSurfaceZsWithMultis(_map, x, y, zs)
            : StepProbe.ComputeStandableSurfaceZs(_map, x, y, zs);

    public StepMask Step(int x, int y, sbyte z) =>
        Multis(x, y) ? StepProbe.ComputeMultiMaskAt(_map, x, y, z) : StepProbe.ComputeMaskAt(_map, x, y, z);

    public bool Multis(int x, int y)
    {
        var sx = x >> Map.SectorShift;
        var sy = y >> Map.SectorShift;

        if (_map.GetRealSector(sx, sy).HasMultis)
        {
            return true;
        }

        var west = (x & 15) == 0;
        var east = (x & 15) == 15;
        var north = (y & 15) == 0;
        var south = (y & 15) == 15;

        if (!(west || east || north || south))
        {
            return false;
        }

        return west && _map.GetRealSector(sx - 1, sy).HasMultis
            || east && _map.GetRealSector(sx + 1, sy).HasMultis
            || north && _map.GetRealSector(sx, sy - 1).HasMultis
            || south && _map.GetRealSector(sx, sy + 1).HasMultis
            || west && north && _map.GetRealSector(sx - 1, sy - 1).HasMultis
            || east && north && _map.GetRealSector(sx + 1, sy - 1).HasMultis
            || west && south && _map.GetRealSector(sx - 1, sy + 1).HasMultis
            || east && south && _map.GetRealSector(sx + 1, sy + 1).HasMultis;
    }
}
