using System;
using System.Collections.Generic;
using Server.Logging;

namespace Server.BotAI.V2;

/// <summary>
/// Who exists. Builds the population at every world load, hands the clock its list, and puts the fallen
/// back on their feet.
///
/// <para>
/// <b>Rebuilt from configuration rather than restored from the save, and that is a design choice with a
/// reason.</b> A bot's state lives in objects — a bond, a journey, a ledger of what paid — and none of it is
/// worth a save format. Loading a saved bot means rebuilding all of it anyway, and the one thing that
/// <em>would</em> come back intact is its pack, so handing out the kit again would produce a bot with two of
/// everything. So bots that arrive from a save are deleted and the population is raised fresh. What survives
/// a restart is what should: names, and the configuration that says who exists.
/// </para>
///
/// <para>
/// The population is small on purpose. Nothing here holds a client, and content that assumes one is the
/// standing hazard of putting <see cref="PlayerMobile"/>-derived bots in a world; a handful of them makes
/// that discoverable, a hundred and fifty makes it a log to read.
/// </para>
/// </summary>
public static class BotPopulation
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotPopulation));

    /// <summary>Which facet the population lives on.</summary>
    public static Map Home { get; set; }

    /// <summary>
    /// Where on it. Britain by default, and specifically the point this shard's own location list calls
    /// Britain — <c>Data/Locations/felucca.json</c> — rather than a coordinate somebody remembered.
    ///
    /// It matters more than it looks: the first bot to want work sweeps the ground around itself for seams,
    /// fires and counters, so where the population is born decides what it can do at all.
    /// </summary>
    public static Point3D Where { get; set; } = new(1592, 1680, 10);

    /// <summary>How far around that point bots are scattered, so they do not all arrive on one tile.</summary>
    public static int Spread { get; set; } = 6;

    /// <summary>
    /// How far from home this population is allowed to want anything. Two hundred tiles: Britain and its
    /// outskirts.
    ///
    /// <para>
    /// <b>A bound on wanting, not on walking.</b> Nothing stops a bot being chased across a field; what this
    /// does is keep every <em>offer</em> inside one town, so the population does not spread itself across a
    /// continent it cannot survive. A seam, a forge, a counter or a shop outside it is simply not proposed.
    /// </para>
    ///
    /// <para>
    /// It is deliberately crude and deliberately temporary. The real answer is travel — stones to the banks of
    /// the big cities — and when that exists this becomes a bound per city instead of one bound in total.
    /// </para>
    /// </summary>
    public static int Roam { get; set; } = 200;

    /// <summary>
    /// Whether this place is somewhere the population may work.
    ///
    /// With no home configured there is no bound at all: an unconfigured population is not a population that
    /// may not work, it is one nobody has placed yet.
    /// </summary>
    public static bool Within(Map map, Point3D where) =>
        Home == null || map == Home && Utility.InRange(Where, where, Roam);

    /// <summary>How long a dead bot lies there before it is put back on its feet.</summary>
    public static int ReviveMs { get; set; } = 60000;

    /// <summary>How many placements are tried before falling back to the configured point itself.</summary>
    private const int Attempts = 20;

    /// <summary>
    /// Everybody, including holes.
    ///
    /// <b>Deleted bots leave a null rather than shifting the list</b>, because a bot can be deleted by its own
    /// turn — and a list that shifts underneath the clock's loop skips whoever moved into the gap. Holes are
    /// filled when the population is next raised.
    /// </summary>
    private static readonly List<BotMobile> _bots = [];

    private static int _holes;

    public static IReadOnlyList<BotMobile> Bots => _bots;

    /// <summary>How many bots actually exist.</summary>
    public static int Count => _bots.Count - _holes;

    public static int Living
    {
        get
        {
            var living = 0;

            for (var i = 0; i < _bots.Count; i++)
            {
                if (_bots[i] is { Deleted: false, Alive: true })
                {
                    living++;
                }
            }

            return living;
        }
    }

    /// <summary>
    /// Deletes every bot that came back from the world save.
    ///
    /// <para>
    /// <b>The one place in this assembly that walks the whole world, and it is deliberate.</b> The shard's
    /// own rules forbid iterating <c>World.Mobiles</c> in favour of spatial queries, for good reason — but
    /// there is no spatial query for "everywhere", this runs once per world load, and the alternative is a
    /// population that doubles every restart. The first version reached the same conclusion for the same
    /// reason.
    /// </para>
    /// </summary>
    public static int PurgeSaved()
    {
        List<BotMobile> stale = [];

        foreach (var mobile in World.Mobiles.Values)
        {
            if (mobile is BotMobile bot)
            {
                stale.Add(bot);
            }
        }

        for (var i = 0; i < stale.Count; i++)
        {
            stale[i].Delete();
        }

        return stale.Count;
    }

    /// <summary>
    /// Raises the population described by configuration: so many of this class, so many of that. Returns how
    /// many were actually born.
    /// </summary>
    public static int Raise(IReadOnlyDictionary<string, int> mix)
    {
        if (mix == null || mix.Count == 0)
        {
            logger.Error("No population is configured, so no bots exist. Name classes and counts in Configuration/bot-population.json");

            return 0;
        }

        var born = 0;

        foreach (var (name, count) in mix)
        {
            var klass = BotClasses.Find(name);

            if (klass == null)
            {
                // By name, because this is a typo in a config file and the only useful answer names it.
                logger.Error("No class is called {Name}, so none of the {Count} asked for were raised", name, count);

                continue;
            }

            for (var i = 0; i < count; i++)
            {
                if (Raise(klass) != null)
                {
                    born++;
                }
            }
        }

        return born;
    }

    /// <summary>One bot of the given class, placed, outfitted and put on the clock.</summary>
    public static BotMobile Raise(BotClass klass) => Raise(klass, null);

    /// <summary>
    /// The same, under a name of the caller's choosing.
    ///
    /// <para>
    /// <b>For bots that are not drawn from the population, and the King's Rangers are the first of them.</b>
    /// The name pool is the population's own roll of townsfolk, dealt out in order — so a company raised from
    /// it comes out as "Kerrin 2" and "Lysa 2", which reads as two ordinary bots with duplicate names rather
    /// than as the crown's. Who a bot is meant to be is the caller's to say when the caller is not the
    /// population.
    /// </para>
    /// </summary>
    public static BotMobile Raise(BotClass klass, string called)
    {
        if (klass == null || Home == null || Home == Map.Internal)
        {
            return null;
        }

        var bot = new BotMobile();

        bot.Become(klass, string.IsNullOrWhiteSpace(called) ? Christen() : called, Utility.Random(2) == 0);

        // After Become, never before: the class deals out its starting skills in there, and a restore that
        // ran first would be overwritten by them. See BotProgress for why only the learning comes back and
        // the belongings are still built from nothing.
        BotProgress.Restore(bot);

        if (!TryPlace(bot))
        {
            logger.Error(
                "Nowhere to put {Name} the {Class} near {Where} on {Map}; it was not raised",
                bot.Name,
                klass.Name,
                Where,
                Home
            );

            bot.Delete();

            return null;
        }

        Enlist(bot);

        return bot;
    }

    /// <summary>
    /// Puts a fallen bot back on its feet once it has lain there long enough, and returns whether it did.
    ///
    /// <para>
    /// Somebody has to: nothing else in this project resurrects anybody, and a dead bot is a ghost for the
    /// rest of the shard's life. The delay is not decoration — dying has to cost something, and what it costs
    /// a bot is time. The decision layer charges it separately in its own units; this is the same fact in
    /// wall-clock.
    /// </para>
    /// </summary>
    public static bool Revive(BotMobile bot)
    {
        if (bot == null || bot.Deleted || bot.Alive || !bot.Fallen)
        {
            return false;
        }

        if (Core.TickCount - bot.FellTick < ReviveMs)
        {
            return false;
        }

        // Home first, then up: a ghost resurrected where it died is a bot standing in whatever killed it.
        TryPlace(bot);

        bot.Resurrect();

        if (!bot.Alive)
        {
            // The engine refused. It does that silently — <c>Mobile.Resurrect</c> returns nothing and simply
            // does not raise a mobile whose region or state says no — so without this a ghost lies there for
            // the rest of the shard's life and the only symptom is a bot that never moves again.
            if (!bot.ReviveComplained)
            {
                bot.ReviveComplained = true;

                logger.Error(
                    "{Name} the {Class} would not get up at {Where} in {Region}; it stays a ghost",
                    bot.Name,
                    bot.Class?.Name,
                    bot.Location,
                    bot.Region?.Name ?? "nowhere"
                );
            }

            return false;
        }

        logger.Information("{Name} the {Class} is back on its feet at {Where}", bot.Name, bot.Class?.Name, bot.Location);

        return true;
    }

    /// <summary>
    /// How many roads a bot may be refused, one after another, before it is presumed to be somewhere it
    /// cannot get out of.
    ///
    /// A refusal is proof: the search walked every tile the bot can reach and the destination was not among
    /// them. One of those is ordinary — an island across the water, a locked crypt. A dozen in a row, to a
    /// dozen different places, is not a statement about the destinations. It is a statement about where the
    /// bot is standing.
    /// </summary>
    public static int StrandedLimit { get; set; } = 12;

    /// <summary>Bots carried home after getting themselves somewhere with no way out. For the summary.</summary>
    public static long Rescued { get; private set; }

    /// <summary>
    /// Puts a stranded bot back where the population lives.
    ///
    /// <para>
    /// <b>The one thing in this project that moves a bot without it walking, and it earns that.</b> A bot on a
    /// spit of land in the water, or inside a yard whose gate was built over, cannot be argued out of it: every
    /// undertaking it takes is refused on its first beat, it fails, the proposer offers another, and that is
    /// the whole of its life from then on — measured at a hundred and eighty failures in twenty minutes,
    /// from two bots, while everything else on the shard worked perfectly. No amount of choosing better work
    /// helps, because the problem is not the work.
    /// </para>
    ///
    /// <para>
    /// It is deliberately not a teleport a bot can want or plan around: nothing offers it, nothing prices it,
    /// and it fires only on proof — a dozen destinations proved unreachable one after another, without a
    /// single step taken in between. Anything less and it would become a way of travelling.
    /// </para>
    /// </summary>
    public static bool Rescue(BotMobile bot)
    {
        if (bot == null || bot.Deleted || Home == null || Home == Map.Internal)
        {
            return false;
        }

        var from = bot.Location;

        // Already home. Then the bot is not stranded and moving it three tiles proves nothing — whatever is
        // refusing its roads is refusing them here too, and carrying it "home" only wipes the errand it was
        // holding and hides the real fault. Eighteen of these in twenty minutes were the symptom of a starved
        // path-search budget, not of bad ground, and every one of them read in the log as a rescue.
        if (Utility.InRange(from, Where, Spread * 2))
        {
            if (!bot.ReviveComplained)
            {
                bot.ReviveComplained = true;

                logger.Error(
                    "{Name} the {Class} can reach nothing from {Where}, and it is standing at home — this is not bad ground, look at what is refusing the roads",
                    bot.Name,
                    bot.Class?.Name,
                    from
                );
            }

            bot.Refusals = 0;

            return false;
        }

        // Before it is moved: what has just been proved about this ground, written where everybody reads it.
        //
        // A dozen roads refused in a row from one tile is the strongest evidence of a pocket the shard ever
        // produces, and until now the whole of it was spent carrying one bot home. The same ground goes on
        // catching the next bot, and the log for the night says so — (1757, 976) took three, (1623, 1179) took
        // three, (1651, 1112) took two. A look from where the bot is standing costs a couple of milliseconds
        // and files the trap for the life of the shard, so the next bot is refused the road in rather than
        // rescued out of it.
        var trap = BotPath.Enclose(bot.Map, from, BotArrival.Exactly, urgent: true);

        if (!TryPlace(bot))
        {
            return false;
        }

        bot.Journey?.Finish();
        bot.Refusals = 0;
        Rescued++;

        logger.Error(
            "{Name} the {Class} could get nowhere at all from {From} and has been carried home to {Where}; the ground it was on is {Trap}",
            bot.Name,
            bot.Class?.Name,
            from,
            bot.Location,
            trap
        );

        return true;
    }

    /// <summary>
    /// This bot is gone. Its slot becomes a hole rather than being removed, so the clock's loop cannot skip
    /// its neighbour. Called from <see cref="BotMobile.OnAfterDelete"/>.
    /// </summary>
    public static void Forget(BotMobile bot)
    {
        BotStall.Forget(bot);

        if (bot == null)
        {
            return;
        }

        for (var i = 0; i < _bots.Count; i++)
        {
            if (!ReferenceEquals(_bots[i], bot))
            {
                continue;
            }

            _bots[i] = null;
            _holes++;

            return;
        }
    }

    /// <summary>
    /// The whole population deleted and forgotten. Called before a world is replaced: every bot in the list
    /// belongs to a world that is about to stop existing.
    /// </summary>
    public static void Reset()
    {
        for (var i = 0; i < _bots.Count; i++)
        {
            _bots[i]?.Delete();
        }

        _bots.Clear();

        _holes = 0;
        _named = 0;
    }

    public static string Describe()
    {
        var fallen = 0;

        for (var i = 0; i < _bots.Count; i++)
        {
            if (_bots[i] is { Deleted: false, Fallen: true })
            {
                fallen++;
            }
        }

        return $"{Count} bots, {Living} on their feet, {fallen} waiting to be revived";
    }

    /// <summary>Fills a hole if there is one, so the list does not grow for ever across a long session.</summary>
    private static void Enlist(BotMobile bot)
    {
        // Staggered across one step's worth of turns, which is what spreads the population's work across the
        // clock's ticks. Seeded from a real tick rather than left at zero, because a due time of zero is
        // already overdue — and on a host whose counter starts enormous, zero is not even in the past.
        var step = Math.Max(1, BotWalk.StepDelayMs(BotMobile.Runs));

        bot.Scheduled = true;
        bot.DueTick = Core.TickCount + Count * BotBeat.IntervalMs % step;

        if (_holes > 0)
        {
            for (var i = 0; i < _bots.Count; i++)
            {
                if (_bots[i] != null)
                {
                    continue;
                }

                _bots[i] = bot;
                _holes--;

                return;
            }
        }

        _bots.Add(bot);
    }

    /// <summary>
    /// Somewhere near home that the engine agrees a body can stand.
    ///
    /// <b>Asked of the engine rather than assumed</b> — <see cref="Map.CanSpawnMobile"/> is the same test the
    /// shard's own spawners use, including the region's opinion and a search for a floor within a few units
    /// of the configured height. A bot placed inside a wall is a bot whose first act is to prove that there
    /// is no way out of it.
    /// </summary>
    private static bool TryPlace(BotMobile bot)
    {
        var map = Home;

        if (bot == null || map == null || map == Map.Internal)
        {
            return false;
        }

        for (var attempt = 0; attempt < Attempts; attempt++)
        {
            var x = Where.X + Utility.RandomMinMax(-Spread, Spread);
            var y = Where.Y + Utility.RandomMinMax(-Spread, Spread);

            if (map.CanSpawnMobile(x, y, Where.Z - 8, Where.Z + 8, false, false, out var z))
            {
                bot.MoveToWorld(new Point3D(x, y, z), map);

                return true;
            }
        }

        // The configured point itself, whatever is standing on it. Better a crowded tile than no population.
        if (!map.CanSpawnMobile(Where))
        {
            return false;
        }

        bot.MoveToWorld(Where, map);

        return true;
    }

    private static int _named;

    /// <summary>
    /// A name, and it is not decoration. The population is rebuilt every world load, so a name is the only
    /// thing about a bot that survives a restart — which is why the slow tier files what a bot has learned
    /// under one, and why the log is readable at all.
    /// </summary>
    private static string Christen()
    {
        var pool = Names;
        var index = _named++;

        return index < pool.Length ? pool[index] : $"{pool[index % pool.Length]} {index / pool.Length + 1}";
    }

    private static readonly string[] Names =
    [
        "Alden", "Bryn", "Calla", "Doran", "Edda", "Faron", "Gerda", "Hale",
        "Ilsa", "Joss", "Kerrin", "Lysa", "Merrick", "Nessa", "Orin", "Perri",
        "Quill", "Rowan", "Sable", "Torvin", "Ulla", "Vance", "Wynn", "Yarrow"
    ];
}
