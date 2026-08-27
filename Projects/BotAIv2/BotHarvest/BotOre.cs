using System;
using System.Collections.Generic;
using Server.Engines.Harvest;
using Server.Items;
using Server.Logging;
using Server.Targeting;

namespace Server.BotAI.V2;

/// <summary>
/// Ore: what is in a hill, whether this bot can get it out, how it is dug, and what it becomes.
///
/// <para>
/// <b>The work is the engine's, not ours.</b> Digging goes through the shard's own
/// <see cref="HarvestSystem"/> — the same call a player's double-click makes — so the swing, the skill
/// check, the yield and the seam running dry are the real ones. A bot mining is a miner, not a bot being
/// credited with ore. That also makes the thing this project measures real: skill gain here is the
/// engine raising a number, not us deciding a number should go up.
/// </para>
/// </summary>
public static class BotOre
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotOre));

    /// <summary>How far a bot will look around itself for something worth swinging at.</summary>
    public static int Reach { get; set; } = 12;

    /// <summary>
    /// How near the worked tile the bot has to stand, <b>asked of the engine rather than assumed</b>.
    ///
    /// It is <c>MaxRange</c> on the ore definition — the same number <c>HarvestSystem.CheckRange</c> compares
    /// against — so this cannot drift out of step with the shard the way a copy would. It is deliberately not
    /// configurable: a settings file able to disagree with the engine about reach is a settings file able to
    /// make every swing fail silently.
    /// </summary>
    public static int SwingReach => Mining.System?.OreAndStone?.MaxRange ?? 2;

    /// <summary>How near a forge the ore has to be to go into it.</summary>
    public static int FireReach { get; set; } = 2;

    /// <summary>
    /// Enough ore to be worth carrying to a fire.
    ///
    /// Smelting needs no tool, no skill and no trade — the fire does the work — so this is not a smith's
    /// number, it is a miner's. Four, as in the first version.
    /// </summary>
    public static int WorthSmelting { get; set; } = 4;

    /// <summary>
    /// What a patch of ground actually holds, or null where a pick would find nothing.
    ///
    /// <para>
    /// <b>The fact the whole trade rests on, and it is the shard's own.</b> Ore is not the same
    /// everywhere: <c>HarvestDefinition.GetVeinAt</c> seeds a stable draw off the bank's coordinates, so
    /// every eight-by-eight block of mountain has a fixed kind for the life of the shard, and iron is only
    /// about half of them. The rest is dull copper, shadow iron, copper, bronze, gold, agapite, verite and
    /// valorite, sitting in hills where nobody has looked.
    /// </para>
    ///
    /// <para>
    /// The first version's miners only ever came home with iron, and this is the reason: they dug whatever
    /// rock was nearest, and rock near a town is as likely as any other to be plain iron. The shard will
    /// say what is in a hill if it is asked properly — <b>bank coordinates, not tile coordinates</b>.
    /// <c>GetVeinAt</c> divides before it asks, so passing the tile puts the question to a bank
    /// sixty-four times too far away, which reads as ore scattered at random instead of ore in seams.
    /// </para>
    /// </summary>
    public static HarvestResource VeinAt(Map map, int x, int y)
    {
        var definition = Mining.System?.OreAndStone;

        if (definition == null || map == null || map == Map.Internal)
        {
            return null;
        }

        return definition
            .GetVeinAt(map, x / definition.BankWidth, y / definition.BankHeight)
            ?.PrimaryResource;
    }

    /// <summary>
    /// How much ore is left in the block this tile belongs to.
    ///
    /// <para>
    /// <b>The engine's own count, and it settles a question that would otherwise be guesswork.</b> A depleted
    /// vein looks exactly like a full one to every test that can be made from outside: what is readable from
    /// the tile is its definition, and depletion lives in the engine's bank of resources. Without this a bot
    /// can only notice an empty rock by swinging at it and getting nothing, six times, and it still cannot
    /// tell a spent seam from a run of failed rolls.
    /// </para>
    ///
    /// <para>
    /// Tile coordinates here, unlike <see cref="VeinAt"/>: <c>GetBank</c> divides them itself. The two take
    /// different coordinates and both are right, which is exactly the sort of thing that produces ore
    /// scattered at random instead of ore in seams.
    /// </para>
    ///
    /// <para>
    /// Asking creates the block's record if it does not exist yet, so it belongs where a bot is actually
    /// working and not in a sweep of a whole region.
    /// </para>
    /// </summary>
    public static int Left(Map map, int x, int y)
    {
        if (map == null || map == Map.Internal)
        {
            return 0;
        }

        return Mining.System?.OreAndStone?.GetBank(map, x, y)?.Current ?? 0;
    }

    /// <summary>Iron asks nothing of the miner, which is exactly what makes it common.</summary>
    public static bool IsCommon(HarvestResource vein) => vein == null || vein.ReqSkill <= 0.0;

    /// <summary>
    /// Whether this bot could actually get the good ore out of a seam of this difficulty.
    ///
    /// Below the requirement the engine quietly hands back iron instead, so a green miner in a valorite
    /// seam is a green miner digging iron: the walk was wasted and nothing anywhere would have said so.
    /// </summary>
    public static bool CanWork(Mobile bot, double required) =>
        bot != null && bot.Skills[SkillName.Mining].Value >= required;

    /// <summary>How much better than iron a seam is to this bot, from zero upwards.</summary>
    public static double Worth(Mobile bot, HarvestResource vein) =>
        IsCommon(vein) || !CanWork(bot, vein.ReqSkill) ? 0.0 : vein.ReqSkill / 10.0;

    /// <summary>What the seam is called. The ore's own type name is the plainest thing there is.</summary>
    public static string NameOf(HarvestResource vein)
    {
        var types = vein?.Types;

        return types is { Length: > 0 } ? types[0].Name : "rock";
    }

    /// <summary>
    /// The digging tool this bot is carrying, or null.
    ///
    /// <b>The tool decides who mines, not the class name.</b> A gatherer is born with a pickaxe and a
    /// hatchet, both bound and weightless; anybody else who buys or loots a pick is a miner for as long as
    /// it holds one. Asking about archetypes instead is how the first version ended up with a list of
    /// which classes were allowed to work, which then had to be edited every time a class was added.
    /// </summary>
    public static Item Tool(Mobile bot)
    {
        var pack = bot?.Backpack;

        if (pack == null)
        {
            return null;
        }

        Item tool = pack.FindItemByType<Pickaxe>();

        return tool ?? pack.FindItemByType<Shovel>();
    }

    /// <summary>How much unsmelted ore the bot is carrying.</summary>
    public static int Carried(Mobile bot) => bot?.Backpack?.GetAmount(typeof(BaseOre)) ?? 0;

    /// <summary>How many ingots the bot is carrying.</summary>
    public static int Ingots(Mobile bot) => bot?.Backpack?.GetAmount(typeof(BaseIngot)) ?? 0;

    /// <summary>
    /// Somewhere within reach worth swinging at, or null. Returns the target the harvest system expects,
    /// so the caller can hand it straight over.
    ///
    /// <para>
    /// Outwards in rings, so the nearest workable thing is found first and anything after it is only taken
    /// if it is better ore. Better ore beats nearer ore <b>within a pick's walk only</b> — anything
    /// further is a journey, and journeys are weighed a level up where the road can be priced.
    /// </para>
    /// </summary>
    /// <param name="skip">
    /// Tiles already found to give nothing. <b>Not optional in practice, and the reason is not obvious:</b>
    /// a depleted vein looks exactly like a full one to every test that can be made from outside. Depletion
    /// lives in the engine's own bank of resources, while what is readable here is the tile's definition —
    /// so a bot that has just emptied a rock and looks again is handed the same rock, for ever. Whoever is
    /// digging has to remember what it has already exhausted.
    /// </param>
    /// <param name="anchor">
    /// The seam this search belongs to, when the caller has one. Together with <paramref name="leash"/> it
    /// keeps the answer inside the patch of ground the trip was taken on.
    /// </param>
    /// <param name="leash">
    /// How far from <paramref name="anchor"/> a tile may lie, or zero for no limit.
    ///
    /// <para>
    /// <b>Without it the search and its caller measure from two different points, and a bot walks on the
    /// spot.</b> This looks outwards from the <em>bot</em>, so it will happily hand back a rock twelve tiles
    /// away; whoever is digging then checks whether the bot is still near the <em>seam</em>, and a rock
    /// twelve tiles the far side of the bot is twenty-four from the seam. One step towards the rock puts the
    /// bot out of the seam's range, the next beat sends it back to the seam, arriving puts the rock in front
    /// of it again, and it paces between the same two tiles for as long as the trip lasts — over ground with
    /// no ore on it, because the ore is at neither end of the pacing.
    /// </para>
    /// </param>
    public static IPoint3D Find(
        Mobile bot,
        out HarvestSystem system,
        List<Point3D> skip = null,
        Point3D anchor = default,
        int leash = 0
    )
    {
        system = Mining.System;

        var map = bot?.Map;

        if (system == null || map == null || map == Map.Internal)
        {
            return null;
        }

        var origin = bot.Location;

        IPoint3D nearest = null;
        IPoint3D richest = null;
        var bestWorth = 0.0;

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

                    var x = origin.X + dx;
                    var y = origin.Y + dy;

                    // Outside the seam this trip is being paid for. See the leash note above: a rock the
                    // caller will walk away from as soon as it has walked to it is worse than no rock.
                    if (leash > 0 && (Math.Abs(x - anchor.X) > leash || Math.Abs(y - anchor.Y) > leash))
                    {
                        continue;
                    }

                    if (Skipped(skip, x, y))
                    {
                        continue;
                    }

                    var found = Examine(map, x, y, system);

                    if (found == null)
                    {
                        continue;
                    }

                    // Emptied blocks are skipped outright rather than discovered by swinging. This is the one
                    // place the engine can be asked a question no amount of looking at the ground answers.
                    if (Left(map, x, y) <= 0)
                    {
                        continue;
                    }

                    nearest ??= found;

                    var worth = Worth(bot, VeinAt(map, found.X, found.Y));

                    if (worth <= bestWorth)
                    {
                        continue;
                    }

                    richest = found;
                    bestWorth = worth;
                }
            }
        }

        return richest ?? nearest;
    }

    private static bool Skipped(List<Point3D> skip, int x, int y)
    {
        if (skip == null)
        {
            return false;
        }

        for (var i = 0; i < skip.Count; i++)
        {
            if (skip[i].X == x && skip[i].Y == y)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Whether this tile holds anything a pick can work, as the target the engine wants.</summary>
    public static IPoint3D Examine(Map map, int x, int y, HarvestSystem system)
    {
        if (map == null || system == null || x < 0 || y < 0 || x >= map.Width || y >= map.Height)
        {
            return null;
        }

        // Statics first: most stone worth swinging at is a static.
        foreach (var tile in map.Tiles.GetStaticTiles(x, y))
        {
            if (Workable(system, system.GetDefinition(tile.ID & 0x3FFF, false)))
            {
                return new StaticTarget(new Point3D(x, y, tile.Z), tile.ID);
            }
        }

        var land = map.Tiles.GetLandTile(x, y);

        return Workable(system, system.GetDefinition(land.ID & 0x3FFF, true))
            ? new LandTarget(new Point3D(x, y, map.GetAverageZ(x, y)), map)
            : null;
    }

    /// <summary>
    /// Whether a definition is one an ordinary bot can get anything at all out of.
    ///
    /// <b>Mining knows two kinds of ground and only one of them is mining.</b> Sand is worked by masters —
    /// a hundred points of skill and a flag nobody but a trained sand miner has — and a bot swinging at a
    /// beach gets a message it has no client to read and nothing in its pack. Britain is ringed with sand,
    /// so in the first version both gatherers spent an entire night digging it: the ground looked workable
    /// to every test the bot could make and produced not one ingot in eight hours.
    /// </summary>
    private static bool Workable(HarvestSystem system, HarvestDefinition definition) =>
        definition != null && (system is not Mining mining || definition == mining.OreAndStone);

    /// <summary>
    /// One swing. The engine takes it from here: it rolls against the skill and either produces something
    /// or does not, exactly as it would for a player.
    /// </summary>
    public static bool Swing(Mobile bot, Item tool, HarvestSystem system, IPoint3D target)
    {
        if (bot == null || tool == null || system == null || target == null)
        {
            return false;
        }

        bot.Direction = bot.GetDirectionTo(new Point3D(target.X, target.Y, target.Z));

        system.StartHarvesting(bot, tool, target);

        return true;
    }

    /// <summary>
    /// Puts every pile of ore the bot is carrying into a nearby fire, and says how many ingots came out.
    ///
    /// <para>
    /// <b>The forge has to be pointed at, not merely stood near, and this is why nothing was ever
    /// smelted.</b> Ore is smelted by double-clicking it and then targeting a forge — the double-click
    /// only opens a target and waits for a player to answer. A bot has no client, so nothing ever
    /// answered: every pile of ore ever mined in the first version opened a target that hung there until
    /// something replaced it, and not one ingot was produced. The answer is to supply the target
    /// ourselves, which is exactly what a client does.
    /// </para>
    /// </summary>
    public static int Melt(Mobile bot)
    {
        var pack = bot?.Backpack;

        if (pack == null)
        {
            return 0;
        }

        var forge = Fire(bot, FireReach);

        if (forge == null)
        {
            return 0;
        }

        var before = Ingots(bot);

        // A snapshot: smelting replaces the ore, which mutates the list being read.
        List<Item> carried = [.. pack.Items];
        var piles = 0;

        for (var i = 0; i < carried.Count; i++)
        {
            if (carried[i] is not BaseOre ore || ore.Deleted)
            {
                continue;
            }

            ore.OnDoubleClick(bot);

            var target = bot.Target;

            if (target == null)
            {
                continue;
            }

            target.Invoke(bot, forge);
            piles++;
        }

        var made = Ingots(bot) - before;

        if (piles > 0)
        {
            logger.Information(
                "{Name} put {Piles} piles of ore into the fire at {Where} and got {Ingots} ingots",
                bot.Name,
                piles,
                bot.Location,
                made
            );
        }

        return made > 0 ? made : 0;
    }

    /// <summary>
    /// Something to melt ore in, within range, as a target the ore's own smelting will accept.
    ///
    /// Both kinds are looked for, and that distinction is the whole of why the first version forged
    /// nothing: a forge placed by a player is an item and turns up in a spatial query, while every forge
    /// in every town on this shard is a <b>static tile</b>, part of the map, which no query returns. A
    /// smith standing directly in front of the Britain smithy concluded there was no forge in the world.
    /// </summary>
    public static object Fire(Mobile bot, int range)
    {
        var map = bot?.Map;

        if (map == null || map == Map.Internal)
        {
            return null;
        }

        foreach (var item in map.GetItemsInRange(bot.Location, range))
        {
            if (BotGround.IsForgeId(item.ItemID))
            {
                return item;
            }
        }

        var origin = bot.Location;

        for (var dx = -range; dx <= range; dx++)
        {
            for (var dy = -range; dy <= range; dy++)
            {
                var x = origin.X + dx;
                var y = origin.Y + dy;

                if (x < 0 || y < 0 || x >= map.Width || y >= map.Height)
                {
                    continue;
                }

                foreach (var tile in map.Tiles.GetStaticAndMultiTiles(x, y))
                {
                    if (BotGround.IsForgeId(tile.ID))
                    {
                        return new StaticTarget(new Point3D(x, y, tile.Z), tile.ID);
                    }
                }
            }
        }

        return null;
    }
}
