using System;
using System.Collections.Generic;
using Server.Engines.Harvest;
using Server.Items;
using Server.Regions;
using Server.Targeting;

namespace Server.BotAI.V2;

/// <summary>
/// What a woodcutter needs to know: the axe, the trees within reach, and how much wood is worth a trip.
///
/// <para>
/// <b>Lumberjacking stood in the Gatherer's skill list from the day the class was written and nothing on
/// this shard ever used it.</b> One line, one hundred points, and not a single errand that swung an axe — so
/// the only wood on the island was whatever a carpenter had on his shelf. That mattered the moment arrows
/// became something a bot could make: an arrow is a shaft and a feather, a shaft is a log, and a trade whose
/// raw material can only be bought is a trade that stops when the shopkeeper's stock does.
/// </para>
///
/// <para>
/// <b>Almost all of this is the miner's, and that is the point.</b> <c>BotOre.Examine</c> asks the engine
/// which harvest definition a tile belongs to and <c>BotOre.Swing</c> hands the target over — neither knows
/// or cares that it was written for rock. Trees are static tiles rather than land, which <c>Examine</c>
/// already handles because most stone is a static too. What is left for this file is the axe, the ring
/// search and the arithmetic of when a trip is worth taking.
/// </para>
/// </summary>
public static class BotTimber
{
    public static int Reach { get; set; } = 80;

    public static long Townbound { get; private set; }

    public static long Fenced { get; private set; }

    public static int TreeRestMs { get; set; } = 1800000;

    public static long Unwalkable { get; private set; }

    private static readonly Dictionary<(int Map, int X, int Y), long> _unreachable = [];

    public static void Unreachable(Map map, IPoint3D tree)
    {
        if (map == null || tree == null)
        {
            return;
        }

        if (_unreachable.Count > 4096)
        {
            _unreachable.Clear();
        }

        _unreachable[(map.MapID, tree.X, tree.Y)] = Core.TickCount;
    }

    public static void Reached(Map map, IPoint3D tree)
    {
        if (map != null && tree != null && _unreachable.Count > 0)
        {
            _unreachable.Remove((map.MapID, tree.X, tree.Y));
        }
    }

    private static bool Unwalked(Map map, IPoint3D tree) =>
        _unreachable.Count > 0 && _unreachable.TryGetValue((map.MapID, tree.X, tree.Y), out var at)
        && Core.TickCount - at < TreeRestMs;

    public static int SwingReach { get; set; } = 2;

    public static int Worthwhile { get; set; } = 20;

    public static int Worth { get; set; } = 3;

    public static HarvestSystem System => Lumberjacking.System;

    public static Item Tool(Mobile bot) => Tool(bot, out _);

    public static Item Tool(Mobile bot, out bool heavy)
    {
        heavy = false;

        if (bot == null)
        {
            return null;
        }

        var held = bot.FindItemOnLayer(Layer.OneHanded) ?? bot.FindItemOnLayer(Layer.TwoHanded);

        if (held is Hatchet or BaseAxe)
        {
            return held;
        }

        var pack = bot.Backpack;

        if (pack == null)
        {
            return null;
        }

        var hatchet = pack.FindItemByType<Hatchet>();

        if (hatchet != null && Lifts(bot, hatchet))
        {
            return hatchet;
        }

        var axe = pack.FindItemByType<BaseAxe>();

        if (axe == null || Lifts(bot, axe))
        {
            return axe;
        }

        axe = pack.FindItemByType<BaseAxe>(true, each => Lifts(bot, each));
        heavy = axe == null;

        return axe;
    }

    private static bool Lifts(Mobile bot, Item axe) => bot is not BotMobile body || body.Suits(axe);

    public static int Logs(Mobile bot) => bot?.Backpack?.GetAmount(typeof(Log)) ?? 0;

    public static int Keeps { get; set; } = 20;

    public static int KeptBy(Mobile bot) => BotFletching.Kit(bot) != null ? Keeps : 0;

    public static long Ordered { get; private set; }

    public static long Listed { get; private set; }

    public static (int Ordered, int Listed) Store(IBotWilful bot)
    {
        var body = bot?.Self;
        var pack = body?.Backpack;

        if (pack == null)
        {
            return (0, 0);
        }

        var ordered = 0;
        var listed = 0;

        List<Item> carried = [.. pack.Items];

        for (var i = 0; i < carried.Count; i++)
        {
            if (carried[i] is not Log wood || wood.Deleted || !wood.Movable)
            {
                continue;
            }

            var held = Math.Max(1, wood.Amount);
            var spare = held - KeptBy(body);

            if (spare <= 0)
            {
                continue;
            }

            var goods = spare >= held ? wood : Mobile.LiftItemDupe(wood, held - spare);

            if (goods == null)
            {
                continue;
            }

            var (went, out_) = BotAuction.Offer(bot, goods, Worth);

            ordered += went;
            listed += out_;
        }

        Ordered += ordered;
        Listed += listed;

        return (ordered, listed);
    }

    public static void ForgetTrade()
    {
        Ordered = 0;
        Listed = 0;
    }

    public static IPoint3D Find(Mobile bot, HashSet<(int X, int Y)> shun = null)
    {
        var system = System;
        var map = bot?.Map;

        if (system == null || map == null || map == Map.Internal)
        {
            return null;
        }

        var origin = bot.Location;

        for (var radius = 1; radius <= Reach; radius++)
        {
            for (var dx = -radius; dx <= radius; dx++)
            {
                for (var dy = -radius; dy <= radius; dy++)
                {
                    if (Math.Abs(dx) != radius && Math.Abs(dy) != radius)
                    {
                        continue;
                    }

                    var found = BotOre.Examine(map, origin.X + dx, origin.Y + dy, system);

                    if (found == null)
                    {
                        continue;
                    }

                    if (shun != null && shun.Contains((found.X, found.Y)))
                    {
                        continue;
                    }

                    if (Unwalked(map, found))
                    {
                        Unwalkable++;

                        continue;
                    }

                    if (Region.Find(new Point3D(found.X, found.Y, found.Z), map)?.IsPartOf<GuardedRegion>() == true)
                    {
                        Townbound++;

                        continue;
                    }

                    if (BotRefused.Refusing(map, new Point3D(found.X, found.Y, found.Z)))
                    {
                        Fenced++;

                        continue;
                    }

                    return found;
                }
            }
        }

        return null;
    }

    public static bool Swing(Mobile bot, Item tool, IPoint3D target) =>
        BotOre.Swing(bot, tool, System, target);
}
