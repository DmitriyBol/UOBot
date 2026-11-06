using System;
using Server.Engines.Pathing.Cache;
using Server.Items;
using CalcMoves = Server.Movement.Movement;

namespace Server.BotAI.V2;

public static class BotStep
{

    public const int StepUp = 2;

    public const int StandingReach = 24;

    public const int GroundReach = 8;

    private const int ZPlaneHeight = 20;

    private const int PersonHeight = BotArrival.PersonHeight;

    public static int Cell(int x, int y, sbyte z) => ((z + 128) / ZPlaneHeight << 26) | (x << 13) | y;

    public static StepMask Mask(Map map, int x, int y, sbyte z)
    {
        var lookup = StepCache.Instance.TryGetMask(map, x, y, z);

        if (lookup.IsHit)
        {

            if (!ClimbsTooHigh(lookup, z))
            {
                return lookup;
            }

            return StepProbe.ComputeMaskAt(map, x, y, z);
        }

        switch (lookup.HitKind)
        {
            case CacheHitKind.Fallthrough_OffMap:
                {
                    return default;
                }
            case CacheHitKind.Fallthrough_Multi:
                {

                    return MultiMaskCache.Instance.GetMask(map, x, y, z);
                }
            default:
                {

                    return StepProbe.ComputeMaskAt(map, x, y, z);
                }
        }
    }

    private static bool ClimbsTooHigh(in StepMask mask, sbyte z)
    {
        var walk = mask.WalkMask;

        for (var d = 0; d < 8; d++)
        {
            if ((walk & (1 << d)) != 0 && mask.GetWalkZ((Direction)d) - z > StepUp)
            {
                return true;
            }
        }

        return false;
    }

    public static bool BlockedByItems(Map map, int x, int y, sbyte z, bool doorsShut = false)
    {
        var ourTop = z + PersonHeight;

        foreach (var item in map.GetItemsAt(x, y))
        {
            var data = item.ItemData;

            if (!data.ImpassableSurface)
            {
                continue;
            }

            var id = item.ItemID & TileData.MaxItemValue;

            if (data.Door || id is 0x692 or 0x846 or 0x873 || id is >= 0x6F5 and <= 0x6F6)
            {
                if (item is BaseDoor { Locked: true, Open: false })
                {
                    return true;
                }

                if (doorsShut && item is BaseDoor { Open: false })
                {
                    return true;
                }

                continue;
            }

            var itemZ = item.Z;

            if (itemZ + data.CalcHeight > z && ourTop > itemZ)
            {
                return true;
            }
        }

        return false;
    }

    public static bool FlankBlocked(Map map, int x, int y, int dir, in StepMask mask, bool doorsShut = false)
    {
        var fx = x;
        var fy = y;

        CalcMoves.Offset((Direction)dir, ref fx, ref fy);

        return BlockedByItems(map, fx, fy, mask.GetWalkZ((Direction)dir), doorsShut);
    }

    public static bool Ground(Map map, int x, int y, int nearZ, int tolerance, out sbyte z)
    {
        z = 0;

        if (!OnMap(map, x, y))
        {
            return false;
        }

        Span<sbyte> surfaces = stackalloc sbyte[16];

        var count = StepProbe.ComputeStandableSurfaceZs(map, x, y, surfaces);

        if (count == 0)
        {
            return false;
        }

        var best = int.MaxValue;
        var found = false;

        for (var i = 0; i < count; i++)
        {
            var gap = Math.Abs(surfaces[i] - nearZ);

            if (gap > tolerance || gap >= best)
            {
                continue;
            }

            best = gap;
            z = surfaces[i];
            found = true;
        }

        return found;
    }

    public static bool LowestGround(Map map, int x, int y, out sbyte z)
    {
        z = 0;

        if (!OnMap(map, x, y))
        {
            return false;
        }

        Span<sbyte> surfaces = stackalloc sbyte[16];

        if (StepProbe.ComputeStandableSurfaceZs(map, x, y, surfaces) == 0)
        {
            return false;
        }

        z = surfaces[0];

        return true;
    }

    public static bool Settle(Map map, int x, int y, out sbyte z)
    {
        z = 0;

        if (!OnMap(map, x, y))
        {
            return false;
        }

        return Ground(map, x, y, map.GetAverageZ(x, y), GroundReach, out z) || LowestGround(map, x, y, out z);
    }

    public static bool Wet(Map map, int x, int y)
    {
        if (!OnMap(map, x, y))
        {
            return false;
        }

        var land = map.Tiles.GetLandTile(x, y);

        if (!land.Ignored && (TileData.LandTable[land.ID & TileData.MaxLandValue].Flags & TileFlag.Wet) != 0)
        {
            return true;
        }

        foreach (var tile in map.Tiles.GetStaticTiles(x, y))
        {
            if ((TileData.ItemTable[tile.ID & TileData.MaxItemValue].Flags & TileFlag.Wet) != 0)
            {
                return true;
            }
        }

        return false;
    }

    public static bool Dry(Map map, int x, int y, int reach)
    {
        for (var dy = -reach; dy <= reach; dy++)
        {
            for (var dx = -reach; dx <= reach; dx++)
            {
                if (!OnMap(map, x + dx, y + dy) || Wet(map, x + dx, y + dy))
                {
                    return false;
                }
            }
        }

        return true;
    }

    public static bool OnMap(Map map, int x, int y) =>
        map != null && map != Map.Internal && x >= 0 && y >= 0 && x < map.Width && y < map.Height;
}
