using System;
using Server.Engines.Pathing.Tiered;

namespace Server.BotAI.V2;

/// <summary>
/// <b>The road goes round the danger (29.09.2026, Patrick's second answer of the morning).</b> "Guilds have the right to
/// stay in other towns; if the road is dangerous, through the bog for instance, they can go by the north where the road
/// is not so dangerous." The tiered planner routed by distance alone, so The Needle's bots walked from Cove to Britain
/// through the plague bog every time, and the delve's march went past the orc fort. This is the hook the planners ask
/// at every node (<c>NavigationService.Danger</c>): a node on a quadrant the map reads as bloodied (<c>BotQuad.Muscle</c>)
/// costs <see cref="BloodCost"/> tiles more, one inside barred or quarantined ground <see cref="BarredCost"/> more, so a
/// detour up to that many tiles is taken before the blood is crossed. The knowledge is the same the samplers and the
/// travellers already read; this is the walk reading it too.
/// </summary>
public static class BotDanger
{
    public static bool Running { get; set; } = true;

    public static int BloodCost { get; set; } = 60;

    public static int BarredCost { get; set; } = 800;

    public static bool ZonesPriced { get; set; } = true;

    public static int EdgePerBot { get; set; } = 10;

    public static int EdgeMost { get; set; } = 40;

    public static int HostilePerBot { get; set; } = 30;

    public static int HostileLeast { get; set; } = 20;

    public static int HostileMost { get; set; } = 150;

    public static int DeadlyCost { get; set; } = 300;

    public static long InFringe { get; private set; }

    public static long InHostile { get; private set; }

    public static long InDeadly { get; private set; }

    public static long Asked { get; private set; }

    public static long Bloodied { get; private set; }

    public static long Barred { get; private set; }

    public static void Hook() => NavigationService.Danger = Cost;

    public static int Cost(int x, int y)
    {
        if (!Running)
        {
            return 0;
        }

        var map = BotPopulation.Home ?? Map.Felucca;

        if (map == null)
        {
            return 0;
        }

        Asked++;
        var at = new Point3D(x, y, 0);

        if (BotBarred.Holds(map, at))
        {
            Barred++;

            return BarredCost * NavCost.Step;
        }

        var tiles = 0;

        if (BotQuad.Muscle(map, at) > 0.0)
        {
            Bloodied++;
            tiles = BloodCost;
        }

        if (ZonesPriced)
        {
            var level = BotZones.LevelAt(map, x, y, out var vsBot, out _);
            var zoned = level switch
            {
                BotZoneLevel.Deadly => DeadlyCost,
                BotZoneLevel.Hostile => Math.Clamp((int)(HostilePerBot * vsBot), HostileLeast, HostileMost),
                BotZoneLevel.Edge => Math.Clamp((int)(EdgePerBot * vsBot), 0, EdgeMost),
                _ => 0
            };

            if (zoned > 0)
            {
                switch (level)
                {
                    case BotZoneLevel.Deadly:
                        InDeadly++;
                        break;
                    case BotZoneLevel.Hostile:
                        InHostile++;
                        break;
                    default:
                        InFringe++;
                        break;
                }

                tiles = Math.Max(tiles, zoned);
            }
        }

        return tiles * NavCost.Step;
    }

    public static string Describe() =>
        !Running
            ? "the roads do not read the danger"
            : $"the roads read the danger: {Asked} nodes asked, {Bloodied} bloodied (+{BloodCost} tiles each) and {Barred} barred (+{BarredCost}); "
              + (ZonesPriced
                  ? $"the zones priced {InFringe} in an aggro fringe (+{EdgePerBot} a bot, at most {EdgeMost}), {InHostile} in a hostile patrol (+{HostilePerBot} a bot, {HostileLeast}–{HostileMost}) and {InDeadly} deadly (+{DeadlyCost})"
                  : "the zones are not priced");

    public static void Forget()
    {
        Asked = 0;
        Bloodied = 0;
        Barred = 0;
        InFringe = 0;
        InHostile = 0;
        InDeadly = 0;
    }
}
