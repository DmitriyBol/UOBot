using System;
using System.Collections.Generic;
using Server.Guilds;
using Server.Items;
using Server.Multis;

namespace Server.BotAI.V2;

/// <summary>
/// What goes inside a hall: a chest, and the guild's own tools of the trade.
///
/// <para>
/// <b>This is the cheapest part of the whole order and the one that pays for itself immediately.</b>
/// <see cref="BotGround"/> already sweeps the ground wherever bots go and remembers forges with an anvil
/// beside them, every fire the engine will cook over, and counters. A forge inside a hall is an ordinary
/// item in the world, so the sweep finds it with no new code at all — the hall becomes a workshop by being
/// built, and the economy picks it up without a line of arithmetic changing.
/// </para>
///
/// <para>
/// <b>The fittings say what the guild is.</b> The Hammer gets a forge and an anvil, because that is what
/// smiths and miners are for; the Needle gets a loom, a wheel and an oven; the Blade and the Crown get a
/// chest and a roof, because a barracks is a place to be rather than a place to make things.
/// </para>
///
/// <para>
/// <b>Inside means inside the walls, and working that out took a chest across a doorway.</b> The first
/// version asked the multi which tiles it covered and treated all of them as the room — but a house's front
/// steps are part of its multi, and <c>BaseHouse.IsInside</c> agrees they are inside it. So the Blade's
/// chest was set down on the porch, in the one tile everybody has to walk through. The room is now found by
/// flooding outwards from the middle of the house and refusing to pass through a doorway: what the flood
/// reaches is the room, what it cannot reach is the world.
/// </para>
/// </summary>
public static class BotFittings
{
    public static bool Running { get; set; } = true;

    public static int Doorway { get; set; } = 1;

    public static long Moved { get; private set; }

    /// <summary>One thing a guild wants in its hall, and what the guild will have to pay for it.</summary>
    public readonly struct Bench
    {
        public Bench(string name, Type kind, Func<BaseAddon> make, int price)
        {
            Name = name;
            Kind = kind;
            Make = make;
            Price = price;
        }

        public string Name { get; }

        public Type Kind { get; }

        public Func<BaseAddon> Make { get; }

        public int Price { get; }
    }

    private static readonly Dictionary<string, Bench[]> _wanted = new()
    {
        ["The Hammer"] =
        [
            new Bench("a forge", typeof(SmallForgeAddon), () => new SmallForgeAddon(), 800),
            new Bench("an anvil", typeof(AnvilEastAddon), () => new AnvilEastAddon(), 500)
        ],
        ["The Needle"] =
        [
            new Bench("an oven", typeof(StoneOvenEastAddon), () => new StoneOvenEastAddon(), 600),
            new Bench("a loom", typeof(LoomEastAddon), () => new LoomEastAddon(), 600),
            new Bench("a spinning wheel", typeof(SpinningWheelEastAddon), () => new SpinningWheelEastAddon(), 400)
        ]
    };

    public static long Bought { get; private set; }

    public static long Spent { get; private set; }

    public static bool Wanting(BaseHouse hall, Guild guild, out Bench bench)
    {
        bench = default;

        if (hall is not { Deleted: false } || !_wanted.TryGetValue(guild?.Name ?? string.Empty, out var list))
        {
            return false;
        }

        for (var i = 0; i < list.Length; i++)
        {
            if (!Has(hall, list[i].Kind))
            {
                bench = list[i];

                return true;
            }
        }

        return false;
    }

    private static bool Has(BaseHouse hall, Type kind)
    {
        if (hall.Addons == null)
        {
            return false;
        }

        for (var i = 0; i < hall.Addons.Count; i++)
        {
            if (hall.Addons[i] is { Deleted: false } addon && addon.GetType() == kind)
            {
                return true;
            }
        }

        return false;
    }

    public static bool Install(BaseHouse hall, Bench bench)
    {
        if (hall is not { Deleted: false } || hall.Map == null || hall.Map == Map.Internal)
        {
            return false;
        }

        var addon = bench.Make?.Invoke();

        if (addon == null)
        {
            return false;
        }

        var spots = Free(Room(hall), hall.Map);
        var beside = Nearest(hall);

        if (beside != Point3D.Zero)
        {
            spots.Sort((a, b) => a.GetDistanceToSqrt(beside).CompareTo(b.GetDistanceToSqrt(beside)));
        }
        else
        {
            var door = Doorstep(hall);

            spots.Sort((a, b) => b.GetDistanceToSqrt(door).CompareTo(a.GetDistanceToSqrt(door)));
        }

        for (var i = 0; i < spots.Count; i++)
        {
            BaseHouse found = null;

            if (addon.CouldFit(spots[i], hall.Map, hall.Owner, ref found) != AddonFitResult.Valid || found != hall)
            {
                continue;
            }

            addon.MoveToWorld(spots[i], hall.Map);
            hall.Addons ??= [];
            hall.Addons.Add(addon);

            Bought++;
            Spent += bench.Price;

            return true;
        }

        addon.Delete();

        return false;
    }

    public static Point3D Spot(BaseHouse hall)
    {
        if (hall is not { Deleted: false } || hall.Map == null || hall.Map == Map.Internal)
        {
            return Point3D.Zero;
        }

        var spare = Free(Room(hall), hall.Map);

        if (spare.Count == 0)
        {
            return Point3D.Zero;
        }

        var door = Doorstep(hall);

        spare.Sort((a, b) => b.GetDistanceToSqrt(door).CompareTo(a.GetDistanceToSqrt(door)));

        return spare[0];
    }

    public static long Unblocked { get; private set; }

    public static long Cornered { get; private set; }

    public static int Unblock(BaseHouse hall)
    {
        if (hall is not { Deleted: false } || hall.Map == null || hall.Map == Map.Internal
            || hall.PlayerVendors == null || hall.Doors == null)
        {
            return 0;
        }

        var moved = 0;

        for (var i = 0; i < hall.PlayerVendors.Count; i++)
        {
            if (hall.PlayerVendors[i] is not { Deleted: false } merchant)
            {
                continue;
            }

            var nearest = int.MaxValue;
            Item way = null;

            for (var d = 0; d < hall.Doors.Count; d++)
            {
                if (hall.Doors[d] is Item { Deleted: false } door)
                {
                    var gap = Math.Max(Math.Abs(door.X - merchant.X), Math.Abs(door.Y - merchant.Y));

                    if (gap < nearest)
                    {
                        nearest = gap;
                        way = door;
                    }
                }
            }

            if (way == null || nearest > Doorway + 1)
            {
                continue;
            }

            var spot = Spot(hall);

            if (spot == Point3D.Zero
                || Math.Max(Math.Abs(way.X - spot.X), Math.Abs(way.Y - spot.Y)) <= nearest)
            {
                Cornered++;

                continue;
            }

            merchant.MoveToWorld(spot, hall.Map);
            moved++;
        }

        Unblocked += moved;

        return moved;
    }

    public static bool InRoom(BaseHouse hall, Point3D at)
    {
        if (hall is not { Deleted: false })
        {
            return false;
        }

        return Within(Room(hall), at);
    }

    private static Point3D Nearest(BaseHouse hall)
    {
        if (hall.Addons == null)
        {
            return Point3D.Zero;
        }

        for (var i = hall.Addons.Count - 1; i >= 0; i--)
        {
            if (hall.Addons[i] is { Deleted: false } addon)
            {
                return addon.Location;
            }
        }

        return Point3D.Zero;
    }

    public static int Furnish(BaseHouse house, Guild guild)
    {
        if (!Running || house is not { Deleted: false } || house.Map == null || house.Map == Map.Internal)
        {
            return 0;
        }

        var owner = house.Owner;
        var map = house.Map;
        var spots = Free(Room(house), map);

        _ = guild;

        if (spots.Count == 0)
        {
            return 0;
        }

        var placed = 0;
        var door = Doorstep(house);

        if (spots.Count > 0)
        {
            spots.Sort((a, b) => b.GetDistanceToSqrt(door).CompareTo(a.GetDistanceToSqrt(door)));

            var chest = new WoodenChest();
            chest.MoveToWorld(spots[0], map);

            if (owner == null || !house.LockDown(owner, chest))
            {
                chest.Movable = false;
            }

            placed++;
        }

        return placed;
    }

    public static int Tidy(BaseHouse house)
    {
        if (!Running || house is not { Deleted: false } || house.Map == null || house.Map == Map.Internal)
        {
            return 0;
        }

        var room = Room(house);

        if (room.Count == 0)
        {
            return 0;
        }

        var door = Doorstep(house);

        var spare = Free(room, house.Map);

        spare.Sort((a, b) => b.GetDistanceToSqrt(door).CompareTo(a.GetDistanceToSqrt(door)));

        var stray = new List<Item>();

        if (house.Addons != null)
        {
            for (var i = 0; i < house.Addons.Count; i++)
            {
                if (house.Addons[i] is { Deleted: false } addon && !Within(room, addon.Location))
                {
                    stray.Add(addon);
                }
            }
        }

        if (house.LockDowns != null)
        {
            foreach (var item in house.LockDowns)
            {
                if (item is { Deleted: false } && !Within(room, item.Location))
                {
                    stray.Add(item);
                }
            }
        }

        var moved = 0;

        for (var i = 0; i < stray.Count && spare.Count > 0; i++)
        {
            var to = spare[0];

            spare.RemoveAt(0);
            stray[i].MoveToWorld(to, house.Map);
            moved++;
        }

        if (house.PlayerVendors != null)
        {
            for (var i = 0; i < house.PlayerVendors.Count && spare.Count > 0; i++)
            {
                if (house.PlayerVendors[i] is not { Deleted: false } merchant || Within(room, merchant.Location))
                {
                    continue;
                }

                var to = spare[0];

                spare.RemoveAt(0);
                merchant.MoveToWorld(to, house.Map);
                moved++;
            }
        }

        Moved += moved;

        return moved;
    }

    private static bool Within(List<Point3D> room, Point3D at)
    {
        for (var i = 0; i < room.Count; i++)
        {
            if (room[i].X == at.X && room[i].Y == at.Y)
            {
                return true;
            }
        }

        return false;
    }

    private static Point3D Doorstep(BaseHouse house) =>
        house.Doors is { Count: > 0 } && house.Doors[0] is Item door ? door.Location : house.Location;

    private static List<Point3D> Room(BaseHouse house)
    {
        var mcl = house.Components;
        var wide = mcl.Width;
        var high = mcl.Height;

        var open = new bool[wide, high];
        var floor = new sbyte[wide, high];

        for (var ix = 0; ix < wide; ix++)
        {
            for (var iy = 0; iy < high; iy++)
            {
                var tiles = mcl.Tiles[ix][iy];
                var found = sbyte.MinValue;
                var blocked = false;

                for (var t = 0; t < tiles.Length; t++)
                {
                    var tile = tiles[t];
                    var data = TileData.ItemTable[tile.ID & TileData.MaxItemValue];

                    if ((data.Flags & TileFlag.Impassable) != 0)
                    {
                        blocked = true;

                        continue;
                    }

                    if ((data.Flags & TileFlag.Surface) != 0)
                    {
                        var top = (sbyte)(tile.Z + data.CalcHeight);

                        if (top > found)
                        {
                            found = top;
                        }
                    }
                }

                open[ix, iy] = !blocked && found != sbyte.MinValue;
                floor[ix, iy] = found;
            }
        }

        var shut = new bool[wide, high];

        if (house.Doors != null)
        {
            for (var i = 0; i < house.Doors.Count; i++)
            {
                if (house.Doors[i] is not Item door)
                {
                    continue;
                }

                var dx = door.X - house.X - mcl.Min.X;
                var dy = door.Y - house.Y - mcl.Min.Y;

                for (var ox = -Doorway; ox <= Doorway; ox++)
                {
                    for (var oy = -Doorway; oy <= Doorway; oy++)
                    {
                        var x = dx + ox;
                        var y = dy + oy;

                        if (x >= 0 && x < wide && y >= 0 && y < high)
                        {
                            shut[x, y] = true;
                        }
                    }
                }
            }
        }

        var seen = new bool[wide, high];
        var queue = new Queue<(int X, int Y)>();
        var start = Middle(open, wide, high, -mcl.Min.X, -mcl.Min.Y);

        if (start.X < 0)
        {
            return [];
        }

        queue.Enqueue(start);
        seen[start.X, start.Y] = true;

        var room = new List<Point3D>();

        while (queue.Count > 0)
        {
            var (x, y) = queue.Dequeue();

            var at = new Point3D(house.X + mcl.Min.X + x, house.Y + mcl.Min.Y + y, house.Z + floor[x, y]);

            if (!shut[x, y])
            {
                room.Add(at);
            }

            Step(open, seen, shut, queue, x + 1, y, wide, high);
            Step(open, seen, shut, queue, x - 1, y, wide, high);
            Step(open, seen, shut, queue, x, y + 1, wide, high);
            Step(open, seen, shut, queue, x, y - 1, wide, high);
        }

        return room;
    }

    private static void Step(
        bool[,] open, bool[,] seen, bool[,] shut, Queue<(int X, int Y)> queue, int x, int y, int wide, int high
    )
    {
        if (x < 0 || x >= wide || y < 0 || y >= high || seen[x, y] || !open[x, y] || shut[x, y])
        {
            return;
        }

        seen[x, y] = true;
        queue.Enqueue((x, y));
    }

    private static (int X, int Y) Middle(bool[,] open, int wide, int high, int cx, int cy)
    {
        for (var ring = 0; ring < Math.Max(wide, high); ring++)
        {
            for (var ox = -ring; ox <= ring; ox++)
            {
                for (var oy = -ring; oy <= ring; oy++)
                {
                    var x = cx + ox;
                    var y = cy + oy;

                    if (x >= 0 && x < wide && y >= 0 && y < high && open[x, y])
                    {
                        return (x, y);
                    }
                }
            }
        }

        return (-1, -1);
    }

    private static List<Point3D> Free(List<Point3D> room, Map map)
    {
        var spare = new List<Point3D>(room.Count);

        for (var i = 0; i < room.Count; i++)
        {
            if (!Taken(map, room[i].X, room[i].Y))
            {
                spare.Add(room[i]);
            }
        }

        return spare;
    }

    private static bool Taken(Map map, int x, int y)
    {
        foreach (var item in map.GetItemsAt(x, y))
        {
            if (item is { Deleted: false })
            {
                return true;
            }
        }

        return false;
    }

    public static string Fittings() =>
        (Bought == 0
            ? "no benches have been bought"
            : $"{Bought} benches bought for {Spent}gp")
        + $"; {Unblocked} merchants moved out of a doorway and {Cornered} left in one for want of anywhere further in";

    public static void Forget()
    {
        Moved = 0;
        Bought = 0;
        Spent = 0;
    }
}
