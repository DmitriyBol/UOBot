using System;
using Server.Engines.Pathing.Cache;
using Server.Items;
using CalcMoves = Server.Movement.Movement;

namespace Server.BotAI.V2;

/// <summary>
/// One tile, and everything the engine knows about stepping off it.
///
/// <para>
/// This is the whole foundation, and the reason the rest of the movement code can be simple. The engine
/// caches, per tile, an eight-direction mask — "can I step this way, and what height do I land on" —
/// computed from the very rules <c>MovementImpl</c> enforces. It needs no mobile and has no distance
/// ceiling. The first version spent months building a compensation layer around the belief that no such
/// thing existed: a lattice of guessed legs, an arc sweep that walked at obstacles until something gave,
/// ring probes hunting for a way out of enclosures, and a memory of walls learned by bots leaning on
/// them for twenty-five seconds at a time. One night's log holds 1728 abandoned errands. A fence is not
/// discovered. It is seen.
/// </para>
///
/// <para>
/// <b>Everything here mirrors an engine rule exactly, and every deviation in the first version cost an
/// evening.</b> A planner that simplifies a rule is not faster — it is a planner whose paths the bot
/// cannot walk, and the difference shows up as a bot pressed against a railing insisting its route is
/// fine.
/// </para>
///
/// <para>
/// What is deliberately <b>not</b> here: creatures, dropped items and shut doors. They move. A planner
/// that treats a skeleton standing in a doorway as a wall teaches itself that the doorway is one. Those
/// belong to the moment the step is taken — see <see cref="BotWalk"/>.
/// </para>
/// </summary>
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
