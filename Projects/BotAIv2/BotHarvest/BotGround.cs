using System;
using System.Collections.Generic;
using System.Diagnostics;
using Server.Engines.Harvest;
using Server.Logging;
using Server.Mobiles;
using Server.Regions;

namespace Server.BotAI.V2;

/// <summary>One remembered patch of workable rock, and what is in it.</summary>
public readonly struct BotSeam
{
    public BotSeam(Map map, Point3D where, string ore, double required)
    {
        Map = map;
        Where = where;
        Ore = ore;
        Required = required;
    }

    public Map Map { get; }

    public Point3D Where { get; }

    /// <summary>What the seam yields, by the ore's own type name. For the log, never branched on.</summary>
    public string Ore { get; }

    /// <summary>The mining skill the seam asks for. Below it the engine hands back plain iron instead.</summary>
    public double Required { get; }

    public bool Exists => Map != null;

    public override string ToString() => $"{Ore} at {Where}";
}

/// <summary>
/// What the population knows about the ground: where the rock is, where metal can be melted, and where
/// money and goods can be put away.
///
/// <para>
/// <b>Found by one bounded sweep, kept for everybody.</b> A bot standing in a town cannot see a mine and a
/// bot standing in a mine cannot see a forge, so knowledge of places cannot come from looking around: it
/// has to be swept for once and remembered. The first version reached the same conclusion the expensive
/// way — every workshop on this shard is part of the map rather than an object in it, so no spatial query
/// finds one, and a smith could only discover a forge by standing within two tiles of a forge it had no
/// reason to walk to. It never happened, all night.
/// </para>
///
/// <para>
/// <b>Swept where bots actually are, rather than around a list of towns.</b> The first version swept
/// vendor clusters and missed the only town that mattered: Britain's cluster centre is the average of its
/// shopkeepers' spawn points, which lands inside a wall 246 tiles from the smithy, so it recorded eight
/// forges on four facets and <em>none</em> on Felucca, where the whole population lived. A bot asking
/// where it may work triggers the sweep around itself, which cannot miss the place bots are.
/// </para>
///
/// <para>
/// This is not a persistent map and is not written to disk. It is rebuilt when the world is, because
/// everything in it is a fact about a world that has just been replaced.
/// </para>
/// </summary>
public static class BotGround
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotGround));

    /// <summary>
    /// How far around a bot one sweep looks. A hundred and sixty tiles each way: town and its outskirts,
    /// or a mine and the road out of it.
    /// </summary>
    public static int Reach { get; set; } = 160;

    /// <summary>
    /// How coarsely the sweep samples for rock. Mountains are hundreds of tiles across, so every fourth
    /// tile finds all of them at a sixteenth of the cost. Forges are one tile and are looked for on every
    /// tile — that difference is the reason there are two sampling rates in one pass.
    /// </summary>
    public static int Stride { get; set; } = 4;

    /// <summary>How far apart two rock tiles have to be to be remembered as two seams.</summary>
    public static int SeamSpacing { get; set; } = 16;

    /// <summary>
    /// How far a miner will walk before distance starts halving what a seam is worth to it.
    ///
    /// <para>
    /// <b>It was <see cref="SeamSpacing"/>, and that was one number doing two jobs.</b> Spacing is how far
    /// apart two surveyed seams have to be to count as different rock — a fact about bookkeeping, sixteen
    /// tiles, and correct. Read as patience it says a miner values a seam sixteen tiles off at half, one at
    /// a hundred and sixty at a eleventh, and one in the mountains at nothing at all. So every miner on the
    /// shard took whatever rock was nearest to town however poor it was, and the whole trade collected
    /// within sight of the walls.
    /// </para>
    ///
    /// <para>
    /// Eighty is about a minute's run. At that scale a seam in the mountains is worth a third of one at the
    /// gate rather than a twentieth, so richness can actually win — which is the point of having measured
    /// richness at all.
    /// </para>
    /// </summary>
    public static int Patience { get; set; } = 80;

    /// <summary>
    /// A place worth surveying whether or not a bot has ever walked past it.
    ///
    /// <para>
    /// <b>Every sweep this shard has ever made happened around a bot, which means the map it holds is a map
    /// of where bots already were.</b> That is fine for finding the forge and the bank, and it is exactly
    /// wrong for ore: the good rock is in the mountains, no bot has a reason to go to the mountains until
    /// there is rock recorded there, and there is no rock recorded there because no bot has been. A closed
    /// circle, and the only way out of it is to name one place from outside.
    /// </para>
    ///
    /// <para>
    /// The open cave at (1446, 1227) on Felucca, by order. One sweep of it at boot puts real seams on the
    /// board a quarter of the roam away, and from there the ordinary arithmetic does the rest — patience
    /// carries a miner that far, and what the population learns about what came out of the ground carries
    /// the next one.
    /// </para>
    /// </summary>
    public static Point3D Lode { get; set; } = new(1446, 1227, 0);

    /// <summary>
    /// Sweeps the named lode, once, so that there is ore on the board somewhere no bot has yet been.
    ///
    /// Called from the module's start rather than lazily, because the question it answers — is there
    /// anywhere outside the walls worth walking to — is asked by the first miner in the first minute, and an
    /// answer that arrives later is an answer that arrives after the habit has formed.
    /// </summary>
    public static int Prospect(Map map)
    {
        if (map == null || map == Map.Internal || Lode == Point3D.Zero)
        {
            // Said rather than returned quietly. A nought here means the mountains were never put on the
            // board and every miner will spend the session within sight of the walls, which is a large
            // consequence for a silence.
            logger.Error(
                "The lode at ({X}, {Y}) could not be prospected: there is no facet to sweep it on, so no ore outside the walls has been recorded",
                Lode.X,
                Lode.Y
            );

            return 0;
        }

        var found = Survey(map, Lode);

        logger.Information(
            "Prospected the lode at ({X}, {Y}): {Found} seams, and now {Total} on the board",
            Lode.X,
            Lode.Y,
            found,
            _seams.Count
        );

        return found;
    }

    /// <summary>How far apart two forges have to be to be two workshops. A forge is several tiles wide.</summary>
    public static int PlaceSpacing { get; set; } = 12;

    /// <summary>How near a forge an anvil must stand for the pair to be a workshop.</summary>
    public static int AnvilReach { get; set; } = 3;

    /// <summary>
    /// Most seams remembered at once.
    ///
    /// <para>
    /// <b>Ninety-six was sized for sweeps that happen around bots, and a named lode is not one of those.</b>
    /// Every sweep until 27.08.2026 was a bot's own surroundings — a few dozen tiles of rock each, from
    /// wherever the population happened to be — so the list filled slowly and from everywhere. The first
    /// sweep of the cave at (1446, 1227) returned ninety-six seams by itself and filled the board outright,
    /// and a full board <em>refuses</em> everything after it: no rock anywhere else on the island could be
    /// recorded for the rest of the session, including the town rock the new rule was written to refuse.
    /// The ban read nought, which looked like the ban working and was the ban never being asked.
    /// </para>
    ///
    /// <para>
    /// Raised rather than made to evict, and the difference matters. Eviction needs a rule for which seam to
    /// throw away, and every rule available here is wrong: the poorest is the one a novice can work, the
    /// furthest is the mountains this was all done to reach, the oldest is whatever the shard learned first.
    /// Five hundred is a few sweeps' worth of a large island and costs one comparison per seam per miner per
    /// review, which against the movement budget is nothing.
    /// </para>
    /// </summary>
    public static int MaxSeams { get; set; } = 512;

    public static int MaxPlaces { get; set; } = 48;

    /// <summary>
    /// How many sweeps the population may run in one world. A backstop, not a policy: a sweep is tens of
    /// thousands of tile reads, and a population wandering a continent should not be able to spend its
    /// afternoon surveying it.
    /// </summary>
    public static int MaxSurveys { get; set; } = 16;

    private static readonly List<BotSeam> _seams = [];

    private static readonly List<(Map Map, Point3D Where)> _fires = [];

    private static readonly List<(Map Map, Point3D Where)> _counters = [];

    private static readonly List<(Map Map, Point3D Where)> _surveyed = [];

    private static bool _saidCapped;

    public static IReadOnlyList<BotSeam> Seams => _seams;

    public static IReadOnlyList<(Map Map, Point3D Where)> Fires => _fires;

    public static IReadOnlyList<(Map Map, Point3D Where)> Counters => _counters;

    public static int Surveys => _surveyed.Count;

    /// <summary>
    /// Seams passed over because there was known to be no way through to them.
    ///
    /// A named number: "the miners have worked out all the rock" and "the rock they can see is behind a
    /// wall" are different facts about the island and were the same silence.
    /// </summary>
    public static long Walled { get; private set; }

    /// <summary>A forge, in either of the two forms this shard has them in.</summary>
    public static bool IsForgeId(int id) => id is 4017 or (>= 6522 and <= 6569) or 11736;

    /// <summary>An anvil, which is the other half of a workshop.</summary>
    public static bool IsAnvilId(int id) => id is 4015 or 4016 or 11733 or 11734;

    /// <summary>Whether this patch of the world has already been swept.</summary>
    public static bool Surveyed(Map map, Point3D around)
    {
        for (var i = 0; i < _surveyed.Count; i++)
        {
            if (_surveyed[i].Map == map && Utility.InRange(_surveyed[i].Where, around, Reach / 2))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Sweeps the ground around a point once: rock, fires and counters in a single pass over the tiles,
    /// plus one spatial query for the counters. Says how many seams it found.
    ///
    /// One pass rather than three, because the expensive part is reading the tiles and all three questions
    /// can be asked of the same read.
    /// </summary>
    public static int Survey(Map map, Point3D around)
    {
        if (map == null || map == Map.Internal || Surveyed(map, around))
        {
            return 0;
        }

        if (_surveyed.Count >= MaxSurveys)
        {
            // Said once. Otherwise a population that has wandered past its sweep allowance simply stops
            // finding work, and the census would report bots with nothing worth doing without anything
            // anywhere explaining why.
            if (!_saidCapped)
            {
                _saidCapped = true;

                logger.Error(
                    "The ground has been swept {Count} times, which is the limit; bots further out will find no seams, fires or counters",
                    _surveyed.Count
                );
            }

            return 0;
        }

        _surveyed.Add((map, around));

        var clock = Stopwatch.StartNew();
        var system = Mining.System;

        var seams = 0;
        var fires = 0;

        for (var x = around.X - Reach; x <= around.X + Reach; x++)
        {
            if (x < 0 || x >= map.Width)
            {
                continue;
            }

            for (var y = around.Y - Reach; y <= around.Y + Reach; y++)
            {
                if (y < 0 || y >= map.Height)
                {
                    continue;
                }

                if (NoteFire(map, x, y))
                {
                    fires++;
                }

                if (system == null || x % Stride != 0 || y % Stride != 0)
                {
                    continue;
                }

                if (NoteSeam(map, x, y, system))
                {
                    seams++;
                }
            }
        }

        fires += NoteItemFires(map, around);

        var counters = NoteCounters(map, around);

        clock.Stop();

        // Named and counted, because a count on its own is not knowledge: the first version had eight
        // forges on record all night while a smith standing on Felucca with a pack of ore was told there
        // were none, and no number could have said which of the two was wrong.
        logger.Information(
            "Swept {Reach} tiles around {Where} on {Map} in {Elapsed}ms: {Seams} seams, {Fires} fires, {Counters} counters (now {AllSeams}, {AllFires}, {AllCounters})",
            Reach,
            around,
            map,
            clock.ElapsedMilliseconds,
            seams,
            fires,
            counters,
            _seams.Count,
            _fires.Count,
            _counters.Count
        );

        return seams;
    }

    private static bool NoteSeam(Map map, int x, int y, HarvestSystem system)
    {
        if (_seams.Count >= MaxSeams || BotOre.Examine(map, x, y, system) == null)
        {
            return false;
        }

        var where = new Point3D(x, y, map.GetAverageZ(x, y));

        // <b>Nobody digs inside the walls, and that is a property of the tile rather than an opinion about
        // the moment.</b> The rule was enforced where a seam is chosen, so every miner asked it of every
        // guarded seam on every beat, for the life of the shard: 120 277 region lookups in ninety minutes on
        // 03.09.2026, about twenty-two a second, all of them arriving at the answer they arrived at the first
        // time. Asked once here instead, and such rock never enters the list — which also keeps the list
        // short, since the castle is built into a hillside and the survey sees a great deal of it.
        //
        // Kept as a counter under the same name so the line still says how much rock the walls are holding.
        if (Region.Find(where, map)?.IsPartOf<GuardedRegion>() == true)
        {
            Townbound++;

            return false;
        }

        for (var i = 0; i < _seams.Count; i++)
        {
            if (_seams[i].Map == map && Utility.InRange(_seams[i].Where, where, SeamSpacing))
            {
                return false;
            }
        }

        var vein = BotOre.VeinAt(map, x, y);

        _seams.Add(new BotSeam(map, where, BotOre.NameOf(vein), vein?.ReqSkill ?? 0.0));

        return true;
    }

    private static bool NoteFire(Map map, int x, int y)
    {
        if (_fires.Count >= MaxPlaces)
        {
            return false;
        }

        foreach (var tile in map.Tiles.GetStaticAndMultiTiles(x, y))
        {
            if (!IsForgeId(tile.ID))
            {
                continue;
            }

            var where = new Point3D(x, y, tile.Z);

            // A fire on its own is a fire. Smelting only needs the fire, but a workshop is what a crafter
            // will want later, and remembering the pair costs nothing now.
            if (!HasAnvil(map, x, y) || Known(_fires, map, where))
            {
                return false;
            }

            _fires.Add((map, where));

            return true;
        }

        return false;
    }

    /// <summary>
    /// Forges that are objects rather than map tiles, in one query for the whole sweep.
    ///
    /// <para>
    /// Both kinds have to be looked for and the first version proved why: its sweeps found forges as
    /// statics on four facets and <em>none</em> on Felucca, where every bot lived, because on this shard
    /// Felucca's smithies were placed by the decoration pass as ordinary items.
    /// </para>
    ///
    /// <para>
    /// One spatial query rather than one per tile. Asking per tile would be a hundred thousand queries for
    /// a sweep, which is the difference between a sweep costing milliseconds and costing a visible pause.
    /// </para>
    /// </summary>
    private static int NoteItemFires(Map map, Point3D around)
    {
        var found = 0;

        foreach (var item in map.GetItemsInRange(around, Reach))
        {
            if (item.Deleted || _fires.Count >= MaxPlaces || !IsForgeId(item.ItemID))
            {
                continue;
            }

            var where = item.GetWorldLocation();

            if (Known(_fires, map, where) || !HasAnvil(map, where.X, where.Y))
            {
                continue;
            }

            _fires.Add((map, where));
            found++;
        }

        return found;
    }

    private static int NoteCounters(Map map, Point3D around)
    {
        var found = 0;

        // Bankers are mobiles, so unlike everything else here they answer a spatial query. One query for
        // the whole sweep.
        foreach (var banker in map.GetMobilesInRange<Banker>(around, Reach))
        {
            if (banker.Deleted || _counters.Count >= MaxPlaces)
            {
                continue;
            }

            var where = banker.Location;

            if (Known(_counters, map, where))
            {
                continue;
            }

            _counters.Add((map, where));
            found++;
        }

        return found;
    }

    private static bool HasAnvil(Map map, int x, int y)
    {
        for (var dx = -AnvilReach; dx <= AnvilReach; dx++)
        {
            for (var dy = -AnvilReach; dy <= AnvilReach; dy++)
            {
                foreach (var tile in map.Tiles.GetStaticAndMultiTiles(x + dx, y + dy))
                {
                    if (IsAnvilId(tile.ID))
                    {
                        return true;
                    }
                }
            }
        }

        foreach (var item in map.GetItemsInRange(new Point3D(x, y, 0), AnvilReach))
        {
            if (IsAnvilId(item.ItemID))
            {
                return true;
            }
        }

        return false;
    }

    private static bool Known(List<(Map Map, Point3D Where)> places, Map map, Point3D where)
    {
        for (var i = 0; i < places.Count; i++)
        {
            if (places[i].Map == map && Utility.InRange(places[i].Where, where, PlaceSpacing))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The seam this bot should go to, or one that does not exist when there is none.
    ///
    /// <para>
    /// <b>Richness against distance, decided here rather than by the appraisal.</b> The decision layer
    /// weighs one offer per proposer, so if this handed over the richest seam on the facet it would be
    /// offering a valorite vein three hundred tiles away against nothing, and the appraisal would have no
    /// nearer alternative to prefer. Comparing seams is this subsystem's own job, because it is the only
    /// one that knows what is in them.
    /// </para>
    ///
    /// <para>
    /// Places the bot's own ledger is wary of are skipped: a seam where its last trip went badly is not
    /// worth offering, and offering it anyway wastes the one offer this proposer gets.
    /// </para>
    /// </summary>
    public static BotSeam Seam(IBotWilful bot) => Seam(bot, Point3D.Zero);

    /// <summary>
    /// How long a bot's answer to "which seam" stands before the list is walked for it again.
    ///
    /// <para>
    /// <b>Patrick's order of 03.09.2026: put a cooldown on asking, two or three seconds, so they stop
    /// spamming it a hundred and twenty thousand times.</b> The scan is not one lookup — it is every
    /// remembered seam, each one asked whether somebody else is on it, whether the ledger is wary of it,
    /// whether there is a way through, and what the population has been paid out of that hillside. There are
    /// four hundred of them and thirty-four bots asking on their own beats.
    /// </para>
    ///
    /// <para>
    /// Nothing is lost by the delay. Seams move on the timescale of a mining trip, not of a beat, and the
    /// two things that do change quickly — somebody claiming a seam, and a road being proved shut — are both
    /// re-checked by the errand itself when it gets there. A bot that just finished a vein waits at most
    /// this long to be told where the next one is, and it is already walking home with a full pack.
    /// </para>
    /// </summary>
    public static int AskEveryMs { get; set; } = 2500;

    /// <summary>What each bot was last told, and when. Keyed by serial, pruned with the bot.</summary>
    private static readonly Dictionary<Serial, (long Tick, Point3D Except, BotSeam Seam)> _told = [];

    /// <summary>Scans of the seam list that were answered out of the last one instead.</summary>
    public static long Spared { get; private set; }

    /// <summary>A bot that is gone should not be remembered, or the table grows for the life of the shard.</summary>
    public static void Forget(Mobile bot)
    {
        if (bot != null)
        {
            _told.Remove(bot.Serial);
        }
    }

    /// <summary>
    /// Whether this bot has been told lately, and what it was told.
    ///
    /// The exception is part of the key rather than ignored: an errand that has just proved one seam
    /// unreachable is asking a different question from the one asked a second ago, and answering it out of
    /// the cache would hand back the very seam it is trying to get away from.
    /// </summary>
    private static bool Told(Mobile body, Point3D except, out BotSeam seam)
    {
        seam = default;

        if (!_told.TryGetValue(body.Serial, out var last) || last.Except != except)
        {
            return false;
        }

        if (Core.TickCount - last.Tick >= AskEveryMs)
        {
            return false;
        }

        seam = last.Seam;
        Spared++;

        return true;
    }

    /// <summary>
    /// The same, ignoring one seam. For an undertaking whose way to a seam turned out not to exist: without
    /// the exception it would be handed the same seam and walk into the same wall.
    /// </summary>
    public static BotSeam Seam(IBotWilful bot, Point3D except)
    {
        var body = bot?.Self;
        var map = body?.Map;

        if (map == null || map == Map.Internal)
        {
            return default;
        }

        // Answered out of the last scan while it is still warm. See AskEveryMs.
        if (Told(body, except, out var lately))
        {
            return lately;
        }

        var ledger = bot.Resolve?.Ledger;
        var best = default(BotSeam);
        var bestScore = 0.0;

        for (var i = 0; i < _seams.Count; i++)
        {
            var seam = _seams[i];

            if (seam.Map != map || (except != Point3D.Zero && seam.Where == except))
            {
                continue;
            }

            // Outside the ground this population is allowed to work. See BotPopulation.Roam: a seam across
            // the continent is a bot walking across the continent, and this shard is not ready for that.
            if (!BotPopulation.Within(map, seam.Where))
            {
                continue;
            }


            if (ledger != null && ledger.Cautious(BotDig.Trade, map, seam.Where))
            {
                continue;
            }

            // Somebody is already on it.
            if (!Free(body, seam.Where))
            {
                continue;
            }

            // <b>And there is a way to it.</b> A survey records rock it can see, and seeing is not walking:
            // a vein inside the castle wall, on a ledge, or across a river is close, scores beautifully on
            // the distance below, and is picked by every miner in turn — each of which walks up to it, fails,
            // marks its own five minutes of caution and is replaced by the next one. From the outside it is
            // gatherers crowding around a castle, which is what Patrick was looking at on 27.08.2026.
            //
            // The reach ledger answers from pockets already proved closed by searches that failed, so this
            // stays a dictionary lookup — the same question a patrol, a harrowing and a lesson all ask before
            // they offer anywhere, and the price a real search per candidate would cost was written down once
            // in BotHunter and is not paid again here.
            if (BotReach.Ask(map, body.Location, seam.Where, BotArrival.Within(BotOre.Reach)) == BotReachVerdict.Sealed)
            {
                Walled++;

                continue;
            }

            // Worth is zero for iron and for anything past this bot's skill, so a green miner rates every
            // seam alike and simply takes the nearest — which is correct: it would be handed iron anyway.
            var worth = BotOre.CanWork(body, seam.Required) ? seam.Required / 10.0 : 0.0;

            // <b>And what this ground has actually paid, which is a different fact from what it demands.</b>
            // The requirement above is a property of the tile; this is the population's memory of what came
            // out of that hillside — see BotCommons.Dug. A vein that keeps yielding bronze is worth walking
            // past two that keep yielding iron, and until now every miner had to find that out alone and
            // forgot it whenever the shard restarted.
            worth += BotCommons.Richest(map, seam.Where);
            var away = body.GetDistanceToSqrt(seam.Where);
            var score = (1.0 + worth) / (1.0 + away / Patience);

            if (score <= bestScore)
            {
                continue;
            }

            best = seam;
            bestScore = score;
        }

        _told[body.Serial] = (Core.TickCount, except, best);

        return best;
    }

    /// <summary>The nearest place metal can be melted, or <see cref="Point3D.Zero"/>.</summary>
    public static Point3D Fire(Map map, Point3D from) => Nearest(_fires, map, from);

    /// <summary>
    /// The same, ignoring one that turned out to be unreachable.
    ///
    /// <para>
    /// <b>Without this a miner with a pack of ore and no way to the nearest forge has nowhere to go, for
    /// ever.</b> It picks the nearest, the road is refused, the undertaking fails, the proposer offers mining
    /// again because the bot is still carrying ore, and it picks the same forge — three failures a second,
    /// indefinitely. There are four forges on this shard and the other three may be perfectly reachable; the
    /// bot only has to be allowed to ask for a different one.
    /// </para>
    /// </summary>
    public static Point3D Fire(Map map, Point3D from, Point3D except) => Nearest(_fires, map, from, except);

    /// <summary>The nearest place money and goods can be put away, or <see cref="Point3D.Zero"/>.</summary>
    public static Point3D Counter(Map map, Point3D from) => Nearest(_counters, map, from);

    /// <summary>The same, ignoring one that turned out to be unreachable.</summary>
    public static Point3D Counter(Map map, Point3D from, Point3D except) => Nearest(_counters, map, from, except);

    /// <summary>
    /// The ledger's key for "I could not get to a forge here", and the same for a counter.
    ///
    /// <para>
    /// <b>Per bot, and that correction matters more than it looks.</b> The first version of this was one list
    /// for the whole population, on the reasoning that a forge across water is across water for everybody —
    /// which is only true of bots standing on the same bank. One gatherer that wandered west and got itself
    /// behind a river declared every forge on the shard unreachable, mining stopped being offered to anybody,
    /// and the log said so in as many words: <c>no a fire has been found near any bot yet</c>. Reachability is
    /// a fact about a bot and a place together, never about the place alone.
    /// </para>
    ///
    /// <para>
    /// It rides on <see cref="BotLedger.Beware"/> rather than a list of its own: that is already per bot,
    /// already expires, and already evicts what has not been thought about lately.
    /// </para>
    /// </summary>
    public const string FireKind = "fire";

    public const string CounterKind = "counter";

    /// <summary>
    /// How long one miner holds a seam before anybody else may work it.
    ///
    /// <para>
    /// <b>Two bots on one seam is two bots doing one bot's work.</b> A seam is a patch of rock a few tiles
    /// across; the second miner to arrive spends its swings on the same block, drains it twice as fast, and
    /// the pair of them stand on each other's tiles doing it. Watched live, they simply looked like they were
    /// milling about.
    /// </para>
    ///
    /// <para>
    /// Renewed on every beat the holder is actually digging, so it lapses on its own the moment that bot
    /// walks off, dies, or takes on something else. There are ninety-odd seams on record and fifteen bots:
    /// nobody is short of rock because somebody else got there first.
    /// </para>
    /// </summary>
    public static int DigClaimMs { get; set; } = 90000;

    private static readonly Dictionary<Point3D, (Serial Miner, long Tick)> _digging = [];

    /// <summary>Takes, or renews, this bot's hold on a seam.</summary>
    public static void Working(Mobile miner, Point3D seam)
    {
        if (miner != null && seam != Point3D.Zero && Free(miner, seam))
        {
            _digging[seam] = (miner.Serial, Core.TickCount);
        }
    }

    /// <summary>Whether this bot may work that seam: nobody holds it, or this bot does.</summary>
    public static bool Free(Mobile miner, Point3D seam)
    {
        if (miner == null || !_digging.TryGetValue(seam, out var held))
        {
            return true;
        }

        if (Core.TickCount - held.Tick >= DigClaimMs)
        {
            _digging.Remove(seam);

            return true;
        }

        return held.Miner == miner.Serial;
    }

    /// <summary>Done with it, however that came about.</summary>
    public static void Leave(Point3D seam) => _digging.Remove(seam);

    /// <summary>
    /// A miner walked to this seam, stood on it, and found nothing to swing at. The row goes.
    ///
    /// <para>
    /// <b>The survey samples every fourth tile, so a seam is a sighting and not a promise.</b> When the
    /// sighting turns out to be wrong the only thing that used to happen was a note in that one bot's own
    /// ledger — so the next miner walked the same distance to learn the same thing, and the one after that,
    /// twelve of them in ten minutes on 27.08.2026. What one bot proves by standing on the ground is true
    /// for all of them.
    /// </para>
    ///
    /// <para>
    /// Removed rather than marked, and rock is allowed to come back: the sweeps run wherever bots go, so
    /// ground that grows ore again is recorded again from nothing. That is the same ruling
    /// <c>BotPeril.Cleared</c> makes about a harrowed square — the row goes, and reality writes a new one.
    /// </para>
    /// </summary>
    public static bool Barren(Point3D where)
    {
        for (var i = 0; i < _seams.Count; i++)
        {
            if (_seams[i].Where != where)
            {
                continue;
            }

            _seams.RemoveAt(i);
            _digging.Remove(where);
            Emptied++;

            return true;
        }

        return false;
    }

    /// <summary>Seams struck off because somebody stood on them and found nothing. See <see cref="Barren"/>.</summary>
    public static long Emptied { get; private set; }

    /// <summary>Seams passed over for being inside a town's walls.</summary>
    public static long Townbound { get; private set; }

    /// <summary>The nearest forge this bot has not lately failed to reach.</summary>
    public static Point3D Fire(IBotWilful bot, Point3D from, Point3D except = default) =>
        Nearest(_fires, bot?.Self?.Map, from, except, bot, FireKind);

    /// <summary>The nearest counter this bot has not lately failed to reach.</summary>
    public static Point3D Counter(IBotWilful bot, Point3D from, Point3D except = default) =>
        Nearest(_counters, bot?.Self?.Map, from, except, bot, CounterKind);

    private static Point3D Nearest(
        List<(Map Map, Point3D Where)> places, Map map, Point3D from, Point3D except = default,
        IBotWilful bot = null, string kind = null
    )
    {
        var ledger = kind == null ? null : bot?.Resolve?.Ledger;

        var best = Point3D.Zero;
        var bestAway = double.MaxValue;

        for (var i = 0; i < places.Count; i++)
        {
            var (on, where) = places[i];

            if (on != map || !BotPopulation.Within(on, where))
            {
                continue;
            }

            if (except != Point3D.Zero && where == except)
            {
                continue;
            }

            // Somewhere this bot itself could not get to lately. Everybody else may still use it.
            //
            // <b>And this has never once fired for a counter or a forge, because the two ends speak different
            // words.</b> The caution is filed by <c>BotWill.Settle</c> under the <em>undertaking's</em> name —
            // "unload", "mine", "forge" — and asked for here under the <em>place's</em> name, "counter" or
            // "fire". Both keys are correct and they never meet, so the summary above this method promising
            // "the nearest counter this bot has not lately failed to reach" was a promise nothing kept: Edda
            // walked at the same unreachable counter seventy-six times in eleven minutes on 25.08.2026, and it
            // was the largest single source of failure on the shard.
            //
            // Kept, because a bot that lost money at a place should still avoid it under its own trade's name.
            if (ledger?.Cautious(kind, on, where) == true)
            {
                continue;
            }

            // What the shard has already proved about the ground itself, in a word nobody has to agree on.
            // A pocket walked to its edges answers in one comparison — the same free question the hunt asks of
            // every place it thinks about prowling to — and unlike the ledger it is not a private opinion:
            // no way through is no way through for anybody.
            if (BotReach.Ask(on, from, where, BotArrival.Within(1)) == BotReachVerdict.Sealed)
            {
                continue;
            }

            var away = Math.Sqrt(
                (double)(where.X - from.X) * (where.X - from.X) + (double)(where.Y - from.Y) * (where.Y - from.Y)
            );

            if (away >= bestAway)
            {
                continue;
            }

            best = where;
            bestAway = away;
        }

        return best;
    }

    /// <summary>
    /// Everything back to nothing. Called when the world is reloaded: every point in these lists is a
    /// place in a world that has just been replaced, and a forge remembered from the last one is a bot
    /// walking to an empty field.
    /// </summary>
    public static void Reset()
    {
        _seams.Clear();
        _fires.Clear();
        _counters.Clear();
        _surveyed.Clear();
        _digging.Clear();
        _told.Clear();
        Spared = 0;

        _saidCapped = false;
    }

    public static string Describe() =>
        $"{_surveyed.Count} sweeps: {_seams.Count} seams, {_fires.Count} fires, {_counters.Count} counters; {Walled} seams passed over with no way through, {Townbound} for being inside the walls, {Emptied} struck off as barren, {BotDig.Unwalkable} struck off for nobody getting nearer to them, {Spared} asks answered out of the last scan, patience {Patience} tiles; the lode is at ({Lode.X}, {Lode.Y})";
}
