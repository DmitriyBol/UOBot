using System;
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
    /// <summary>How far a woodcutter looks for a tree before it needs a journey.</summary>
    /// <summary>
    /// How far around itself a bot looks for a tree.
    ///
    /// <para>
    /// <b>Widened from twelve to eighty on 05.09.2026, together with the rule that puts the woods outside
    /// the walls.</b> Twelve tiles is a glance, and a glance taken from the market square finds the town's
    /// ornamental trees or nothing: "85 had no tree within 12 tiles" was the ordinary answer. Once town
    /// timber stops counting, the number has to be large enough to see past the wall from inside it, or the
    /// two rules together would simply end the trade. Eighty is BotGround.Patience — a minute's run — and
    /// this is the same journey the miner makes to a seam.
    /// </para>
    /// </summary>
    public static int Reach { get; set; } = 80;

    /// <summary>Trees passed over for standing inside a town. See Find.</summary>
    public static long Townbound { get; private set; }

    /// <summary>
    /// How near the trunk the engine insists on before it will let an axe swing.
    ///
    /// <para>
    /// Two, and taken as a constant rather than read off the definition because <c>Lumberjacking</c> keeps
    /// its single definition private — unlike <c>Mining</c>, which exposes <c>OreAndStone</c> because it has
    /// two and has to be told apart. A number copied from the engine is a number that can drift, so it is
    /// named here with where it came from: <c>Lumberjacking.cs</c>, <c>MaxRange = 2</c>.
    /// </para>
    /// </summary>
    public static int SwingReach { get; set; } = 2;

    /// <summary>How many logs make a trip worth taking at all.</summary>
    public static int Worthwhile { get; set; } = 20;

    /// <summary>What a log is reckoned at, so a chopping trip can be priced against a mining one.</summary>
    public static int Worth { get; set; } = 3;

    /// <summary>The engine's lumberjacking system, or null before content has built it.</summary>
    public static HarvestSystem System => Lumberjacking.System;

    /// <summary>
    /// The axe.
    ///
    /// <para>
    /// A hatchet first, because it is the small one and the one a crafter is issued; anything else with an
    /// edge will do, and the engine is the judge of that — every axe in the game is a lumberjacking tool and
    /// a bot that happens to be carrying a battle axe can cut with it.
    /// </para>
    /// </summary>
    public static Item Tool(Mobile bot)
    {
        if (bot == null)
        {
            return null;
        }

        // <b>The hand before the pack, because this trade puts it there itself.</b> Lumberjacking is the one
        // harvest that refuses an axe out of a pack — see BotChop.Wield — so the very act of starting to cut
        // moves the hatchet onto a layer, where a pack-only search cannot see it. It read as "nothing to cut
        // with" on the swing after the first: 14 of 31 trips ended that way at 14:31 on 04.09.2026, on bots
        // that were holding the axe at the time. The same reading BotShopper.Wanting makes about a pickaxe —
        // held or worn, a tool is still a tool.
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

        Item tool = pack.FindItemByType<Hatchet>();

        return tool ?? pack.FindItemByType<BaseAxe>();
    }

    /// <summary>How much wood the bot is carrying.</summary>
    public static int Logs(Mobile bot) => bot?.Backpack?.GetAmount(typeof(Log)) ?? 0;

    /// <summary>
    /// The nearest tree within reach, as the target the engine expects, or null.
    ///
    /// <para>
    /// Nearest and nothing cleverer, unlike the miner's search. Ore comes in veins worth different money and
    /// choosing between them pays; a log is a log, so the only question a woodcutter has is which tree is
    /// closest, and every beat spent answering a richer one would be a beat not spent cutting.
    /// </para>
    /// </summary>
    public static IPoint3D Find(Mobile bot)
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
                    // The edge of each ring only; the inside was covered by the ring before it.
                    if (Math.Abs(dx) != radius && Math.Abs(dy) != radius)
                    {
                        continue;
                    }

                    var found = BotOre.Examine(map, origin.X + dx, origin.Y + dy, system);

                    if (found == null)
                    {
                        continue;
                    }

                    // <b>Nobody fells timber inside the walls, by Patrick's order of 05.09.2026, and it is
                    // the same rule the rock has kept since 03.09.</b> A guarded region is a town: the trees
                    // in it are somebody's garden, and a population that harvests its own market square is a
                    // population that never learns where the woods are. The ore side states the reasoning at
                    // length in BotGround.NoteSeam — asked of the tile rather than of the moment, so the
                    // answer is the same for everybody and costs one lookup.
                    if (Region.Find(new Point3D(found.X, found.Y, found.Z), map)?.IsPartOf<GuardedRegion>() == true)
                    {
                        Townbound++;

                        continue;
                    }

                    return found;
                }
            }
        }

        return null;
    }

    /// <summary>One swing. The engine rolls it exactly as it would for a player.</summary>
    public static bool Swing(Mobile bot, Item tool, IPoint3D target) =>
        BotOre.Swing(bot, tool, System, target);
}
