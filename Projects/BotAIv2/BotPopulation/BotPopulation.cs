using System;
using System.Collections.Generic;
using Server.Logging;
using Server.Regions;
using Server.Text;

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

    public static Map Home { get; set; }

    public static Point3D Where { get; set; } = new(1592, 1680, 10);

    public static int Spread { get; set; } = 10;

    public static long Boxedin { get; private set; }

    public static int Roam { get; set; } = 200;

    public static int Scatter { get; set; } = 350;

    public static bool Within(Map map, Point3D where) =>
        Home == null || map == Home && Utility.InRange(Where, where, Roam);

    public static int GateReach { get; set; } = 400;

    public static int GateStride { get; set; } = 8;

    public static long Gates { get; private set; }

    public static Point3D Gate(Map map, Point3D from, Point3D toward, bool counted = true)
    {
        if (map == null || map == Map.Internal || Region.Find(from, map)?.IsPartOf<GuardedRegion>() != true)
        {
            return Point3D.Zero;
        }

        var dx = toward.X - from.X;
        var dy = toward.Y - from.Y;
        var span = Math.Max(Math.Abs(dx), Math.Abs(dy));

        if (span <= 0)
        {
            return Point3D.Zero;
        }

        for (var out_ = GateStride; out_ <= GateReach; out_ += GateStride)
        {
            var x = from.X + dx * out_ / span;
            var y = from.Y + dy * out_ / span;

            if (x < 0 || y < 0 || x >= map.Width || y >= map.Height)
            {
                return Point3D.Zero;
            }

            if (!BotStep.Settle(map, x, y, out var z))
            {
                continue;
            }

            var here = new Point3D(x, y, z);

            if (Region.Find(here, map)?.IsPartOf<GuardedRegion>() == true)
            {
                continue;
            }

            if (counted)
            {
                Gates++;
            }

            return here;
        }

        return Point3D.Zero;
    }

    public static int ReviveMs { get; set; } = 60000;

    private const int Attempts = 20;

    private static readonly List<BotMobile> _bots = [];

    private static int _holes;

    public static IReadOnlyList<BotMobile> Bots => _bots;

    private static readonly List<BotMobile> _away = [];

    public static IReadOnlyList<BotMobile> Away => _away;

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

    public static int Reclaim(IReadOnlyDictionary<string, int> mix, out Dictionary<string, int> kept)
    {
        List<BotMobile> saved = [];
        kept = [];

        foreach (var mobile in World.Mobiles.Values)
        {
            if (mobile is BotMobile bot)
            {
                saved.Add(bot);
            }
        }

        var deleted = 0;

        for (var i = 0; i < saved.Count; i++)
        {
            var bot = saved[i];
            var name = bot.Was;
            var klass = name == null ? null : BotClasses.Find(name);

            var resting = BotRest.Resting(bot.Name);

            if (klass == null || mix == null || !mix.TryGetValue(klass.Name, out var want)
                || kept.GetValueOrDefault(klass.Name) >= want || !bot.Revive(klass, resting))
            {
                bot.Delete();
                deleted++;

                continue;
            }

            kept[klass.Name] = kept.GetValueOrDefault(klass.Name) + 1;

            if (resting)
            {
                _away.Add(bot);
                continue;
            }

            Enlist(bot);
        }

        Unduplicate();

        return deleted;
    }

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

    public static int Raise(IReadOnlyDictionary<string, int> mix) => Raise(mix, null);

    public static int Raise(IReadOnlyDictionary<string, int> mix, IReadOnlyDictionary<string, int> already)
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
                logger.Error("No class is called {Name}, so none of the {Count} asked for were raised", name, count);

                continue;
            }

            var wanted = already != null && already.TryGetValue(klass.Name, out var standing)
                ? count - standing
                : count;

            for (var i = 0; i < wanted; i++)
            {
                if (Raise(klass) != null)
                {
                    born++;
                }
            }
        }

        return born;
    }

    public static BotMobile Raise(BotClass klass) => Raise(klass, null);

    public static BotMobile RaiseNewcomer(BotClass klass)
    {
        for (var index = 0; index < Names.Length * (Houses.Length + 1); index++)
        {
            var name = NameAt(index);

            if (!InUse(name) && !BotProgress.Remembers(name))
            {
                return Raise(klass, name);
            }
        }

        return null;
    }

    public static long Renamed { get; private set; }

    private static void Unduplicate()
    {
        List<BotMobile> everybody = [];

        for (var i = 0; i < _bots.Count; i++)
        {
            if (_bots[i] is { Deleted: false } bot)
            {
                everybody.Add(bot);
            }
        }

        everybody.AddRange(_away);
        everybody.Sort((a, b) => a.Serial.CompareTo(b.Serial));

        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < everybody.Count; i++)
        {
            var bot = everybody[i];

            if (string.IsNullOrEmpty(bot.Name) || seen.Add(bot.Name))
            {
                continue;
            }

            string fresh = null;

            for (var index = 0; index < Names.Length * (Houses.Length + 1); index++)
            {
                var name = NameAt(index);

                if (!seen.Contains(name) && !InUse(name) && !BotProgress.Remembers(name))
                {
                    fresh = name;
                    break;
                }
            }

            if (fresh == null)
            {
                continue;
            }

            logger.Warning(
                "{Old} the {Class} answered to the same name as an older bot; it is {New} from now on",
                bot.Name,
                bot.Class?.Name,
                fresh
            );

            bot.Name = fresh;
            seen.Add(fresh);
            Renamed++;
        }
    }

    public static void Park(BotMobile bot)
    {
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

            break;
        }

        bot.Scheduled = false;

        if (!_away.Contains(bot))
        {
            _away.Add(bot);
        }
    }

    public static bool Unpark(BotMobile bot)
    {
        if (bot == null)
        {
            return false;
        }

        _away.Remove(bot);

        var map = bot.LogoutMap;
        var at = bot.LogoutLocation;
        var own = map != null && map != Map.Internal && map.CanSpawnMobile(at);

        if (own)
        {
            bot.MoveToWorld(at, map);
        }
        else if (!TryPlace(bot) && Home != null)
        {
            bot.MoveToWorld(Where, Home);
        }

        Enlist(bot);

        return own;
    }

    public static BotMobile Raise(BotClass klass, string called)
    {
        if (klass == null || Home == null || Home == Map.Internal)
        {
            return null;
        }

        var bot = new BotMobile();

        bot.Become(klass, string.IsNullOrWhiteSpace(called) ? Christen() : called, Utility.Random(2) == 0);

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

    public static bool Revive(BotMobile bot)
    {
        if (bot == null || bot.Deleted || bot.Alive || !bot.Fallen)
        {
            return false;
        }

        if (BotDelveParty.Raise(bot))
        {
            return true;
        }

        if (Core.TickCount - bot.FellTick < ReviveMs)
        {
            return false;
        }

        TryPlace(bot);

        bot.Resurrect();

        if (!bot.Alive)
        {
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

    public static int StrandedLimit { get; set; } = 12;

    public static long Rescued { get; private set; }

    public static long Delving { get; private set; }

    public static long Unbound { get; private set; }

    public static bool Rescue(BotMobile bot)
    {
        if (bot == null || bot.Deleted || Home == null || Home == Map.Internal)
        {
            return false;
        }

        if (BotDelveParty.Delving(bot) && bot is IBotSquadMember { Squad: not null })
        {
            Delving++;

            return false;
        }

        if (bot is IBotSquadMember { Squad: { } company } member && !ReferenceEquals(company.Leader, member))
        {
            BotSquads.Leave(member);
            Unbound++;
            bot.Refusals = 0;

            logger.Information(
                "{Name} the {Class} was let go of company {Squad} at {Where}: it could not reach its place in it {Limit} times running",
                bot.Name,
                bot.Class?.Name,
                company.Id,
                bot.Location,
                StrandedLimit
            );

            return false;
        }

        var from = bot.Location;

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

        var trap = BotPath.Enclose(bot.Map, from, BotArrival.Exactly, urgent: true);

        var footing = (sbyte)Math.Clamp(from.Z, sbyte.MinValue, sbyte.MaxValue);
        var allowed = BotStep.Mask(bot.Map, from.X, from.Y, footing).WalkMask;
        var floor = BotStep.Settle(bot.Map, from.X, from.Y, out var under) ? under.ToString() : "no floor at all";

        if (!TryPlace(bot))
        {
            return false;
        }

        bot.Journey?.Finish();
        bot.Refusals = 0;
        Rescued++;

        logger.Error(
            "{Name} the {Class} could get nowhere at all from {From} and has been carried home to {Where}; the ground it was on is {Trap}, its tile allows {Allowed:X2} of the eight directions and the floor there is {Floor} against the {Z} it thought it stood at",
            bot.Name,
            bot.Class?.Name,
            from,
            bot.Location,
            trap,
            allowed,
            floor,
            from.Z
        );

        return true;
    }

    public static void Forget(BotMobile bot)
    {
        BotStall.Forget(bot);
        BotGround.Forget(bot);

        if (bot == null)
        {
            return;
        }

        _away.Remove(bot);

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

    public static void Reset()
    {
        for (var i = 0; i < _bots.Count; i++)
        {
            _bots[i]?.Delete();
        }

        for (var i = _away.Count - 1; i >= 0; i--)
        {
            _away[i]?.Delete();
        }

        _bots.Clear();
        _away.Clear();

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

        return $"{Count} bots, {Living} on their feet, {fallen} waiting to be revived, {_away.Count} resting";
    }

    private static void Enlist(BotMobile bot)
    {
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

    public static long Carried { get; private set; }

    public static bool Carry(BotMobile bot)
    {
        if (bot is not { Deleted: false } || !TryPlace(bot))
        {
            return false;
        }

        Carried++;

        return true;
    }

    private static bool TryPlace(BotMobile bot)
    {
        var map = Home;

        if (bot == null || map == null || map == Map.Internal)
        {
            return false;
        }

        var at = BotSeat.Home(bot);

        if (at != Where)
        {
            BotSeat.Placed();
        }

        var span = at == Where && Scatter > Spread ? Scatter : Spread;

        for (var pass = 0; pass < 3; pass++)
        {
            var reach = pass < 2 ? span : Spread;

            for (var attempt = 0; attempt < Attempts; attempt++)
            {
                var x = at.X + Utility.RandomMinMax(-reach, reach);
                var y = at.Y + Utility.RandomMinMax(-reach, reach);

                if (!map.CanSpawnMobile(x, y, at.Z - 8, at.Z + 8, false, false, out var z))
                {
                    continue;
                }

                if (pass == 0 && !Roomy(map, x, y, z))
                {
                    continue;
                }

                if (pass > 0)
                {
                    Boxedin++;
                }

                bot.MoveToWorld(new Point3D(x, y, z), map);

                return true;
            }
        }

        if (!map.CanSpawnMobile(Where))
        {
            return false;
        }

        bot.MoveToWorld(Where, map);

        return true;
    }

    private static bool Roomy(Map map, int x, int y, int z)
    {
        var footing = (sbyte)Math.Clamp(z, sbyte.MinValue, sbyte.MaxValue);
        var mask = BotStep.Mask(map, x, y, footing);

        if (mask.WalkMask == 0)
        {
            return false;
        }

        for (var d = 0; d < 8; d++)
        {
            if ((mask.WalkMask & (1 << d)) == 0)
            {
                continue;
            }

            var nx = x;
            var ny = y;

            Movement.Movement.Offset((Direction)d, ref nx, ref ny);

            if (map.CanSpawnMobile(nx, ny, z - 8, z + 8, false, false, out _))
            {
                return true;
            }
        }

        return false;
    }

    private static int _named;

    public static void Reserve(IReadOnlyList<string> names, string who)
    {
        if (names == null)
        {
            return;
        }

        for (var i = 0; i < names.Count; i++)
        {
            var name = names[i];

            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            for (var j = 0; j < Names.Length; j++)
            {
                if (!Names[j].InsensitiveEquals(name))
                {
                    continue;
                }

                logger.Error(
                    "{Name} is both the {Which} name the population hands out and a name {Who} gives away, so two bots will answer to it and share one record of what they have learned. Take it out of BotPopulation.Names",
                    name,
                    (j + 1).ToString(),
                    who
                );
            }
        }
    }

    private static string Christen()
    {
        string name;

        do
        {
            name = NameAt(_named++);
        }
        while (InUse(name) && _named < Names.Length * (Houses.Length + 1));

        return name;
    }

    private static bool InUse(string name)
    {
        for (var i = 0; i < _bots.Count; i++)
        {
            if (_bots[i] is { Deleted: false } bot && bot.Name.InsensitiveEquals(name))
            {
                return true;
            }
        }

        for (var i = 0; i < _away.Count; i++)
        {
            if (_away[i] is { Deleted: false } bot && bot.Name.InsensitiveEquals(name))
            {
                return true;
            }
        }

        return false;
    }

    private static string NameAt(int index)
    {
        var pool = Names;

        if (index < pool.Length)
        {
            return pool[index];
        }

        var round = index / pool.Length - 1;

        return $"{pool[index % pool.Length]} {Houses[round % Houses.Length]}";
    }

    private static readonly string[] Names =
    [
        "Alden", "Bryn", "Calla", "Doran", "Edda", "Faron", "Gerda", "Hale",
        "Ilsa", "Joss", "Kerrin", "Lysa", "Merrick", "Nessa", "Orin", "Perri",
        "Quill", "Rowan", "Sable", "Torvin", "Ulla", "Vance", "Wynn", "Yarrow",
        "Aric", "Brannoc", "Corwin", "Delwyn", "Emrys", "Fenna", "Garrow", "Hollis",
        "Isolde", "Jarek", "Kelda", "Lorcan", "Maeve", "Neriah", "Oswin", "Pell",
        "Quenna", "Ronan", "Selwyn", "Talia", "Ulwin", "Vesna", "Wystan", "Ysolt",
        "Bertram", "Cassia", "Dain", "Elspeth", "Fendrel", "Gwendra", "Harlan", "Ivo",
        "Jorunn", "Kestrel", "Leofric", "Marek", "Nyla", "Otho", "Piers", "Rhiannon"
    ];

    private static readonly string[] Houses =
    [
        "Ashdown", "Blackbriar", "Coldwell", "Duskmere", "Eastmarch", "Fairholt",
        "Greywood", "Hartley", "Ironvale", "Larkspur", "Marlow", "Northgate",
        "Oakhurst", "Pinewood", "Quarrytop", "Ravenscar", "Stonebridge", "Thornwood",
        "Umberly", "Vinemoor", "Westford", "Yewdale"
    ];
}
