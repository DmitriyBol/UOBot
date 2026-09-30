using System;
using System.Collections.Generic;
using System.Diagnostics;
using Server.Engines.Spawners;
using Server.Logging;
using Server.Mobiles;
using Server.Regions;

namespace Server.BotAI.V2;

/// <summary>
/// One kind of creature as the zones know it: whether it attacks on sight, casts, how far it sees, whether it can leave the
/// water, and how strong one is.
///
/// <para>
/// <b>Read off a living one when there is one, and built once when there is not.</b> A living creature carries its own fight
/// mode, mind and sight, and that is the truth about it; a kind nobody has seen alive yet — every creature of its spawners
/// dead — is asked of <see cref="BotDungeon.Traits"/>, which builds one specimen, reads it and destroys it in the same breath,
/// the same specimen <see cref="BotDungeon.Might"/> already builds for the delve, so a kind is never built twice.
/// </para>
/// </summary>
public sealed class BotZoneKind
{
    public BotZoneKind(Type type)
    {
        Type = type;
        Name = type.Name;
    }

    public Type Type { get; }

    public string Name { get; }

    public string Spawned { get; set; }

    public bool Known { get; set; }

    public bool FromLife { get; set; }

    public bool Unbuildable { get; set; }

    public bool Aggressive { get; set; }

    public bool Caster { get; set; }

    public int Perception { get; set; } = 16;

    public bool Water { get; set; }

    public bool Rooted { get; set; }

    public bool Townsfolk { get; set; }

    public double Power => _powerCount == 0 ? 0.0 : _powerSum / _powerCount;

    public const int MostPowerSamples = 32;

    private double _powerSum;

    private int _powerCount;

    public void AddPower(double power)
    {
        if (power <= 0.0 || _powerCount >= MostPowerSamples)
        {
            return;
        }

        _powerSum += power;
        _powerCount++;
    }

    public void ReadFrom(BaseCreature c)
    {
        Aggressive = BotHaunts.AttacksFirst(c.FightMode);
        Caster = c.AI == AIType.AI_Mage;
        Perception = c.RangePerception;
        Water = c.CantWalk && c.CanSwim;
        Rooted = c.CantWalk && !c.CanSwim;
        Townsfolk = BotHaunts.IsTownsfolk(c);
        Known = true;
        FromLife = true;
    }
}

/// <summary>One entry of a spawner: a kind and how many of it the spawner keeps.</summary>
public readonly record struct BotHauntEntry(BotZoneKind Kind, int Entry, int Max, int Probability);

/// <summary>
/// One spawner as a haunt: where its creatures live by declaration, what they are, and what the shard has seen of them awake.
///
/// <para>
/// <b>The declaration is a promise about a disc; the observation is what the creatures actually did.</b> The spawner says its
/// creatures may wander <see cref="Radius"/> tiles from it. Where the ground is awake, the scan records where they really
/// were (<see cref="BotZoneRaster"/>) and how far from home (<see cref="ObservedRange"/>); once <see cref="Confidence"/> is
/// high, the build shrinks the declared disc to the observed reach and the observed shape wins. A creature on sleeping ground stands where it
/// was spawned and proves nothing about its patrol, so only awake sightings count here.
/// </para>
/// </summary>
public sealed class BotHaunt
{
    public BotHaunt(BaseSpawner spawner, long now)
    {
        Spawner = spawner;
        _sampledTick = now;
    }

    public BaseSpawner Spawner { get; }

    public Point3D At { get; set; }

    public int Radius { get; set; }

    public int Count { get; set; }

    public bool Running { get; set; }

    public bool InTown { get; set; }

    public BotHauntEntry[] Entries { get; set; } = [];

    public bool Found { get; set; }

    public int Index { get; set; } = -1;

    public double AwakeSeconds { get; set; }

    public long Samples { get; set; }

    public int ObservedRange { get; set; }

    public DateTime LastAwake { get; set; } = DateTime.MinValue;

    public int Toured { get; set; }

    public DateTime TouredAt { get; set; } = DateTime.MinValue;

    private long _sampledTick;

    private bool _wasAwake;

    public double Confidence =>
        0.5 * Math.Min(1.0, AwakeSeconds / Math.Max(1, BotZones.ConfidentSeconds))
        + 0.5 * Math.Min(1.0, Samples / (double)Math.Max(1, BotZones.ConfidentSamples));

    public void Sample(long now, bool awake, int away)
    {
        if (awake)
        {
            var gap = now - _sampledTick;

            if (_wasAwake && gap > 0 && gap <= BotZones.AwakeGapMs)
            {
                AwakeSeconds += gap / 1000.0;
            }

            Samples++;
            LastAwake = DateTime.UtcNow;

            if (away > ObservedRange)
            {
                ObservedRange = away;
            }
        }

        _wasAwake = awake;
        _sampledTick = now;
    }

    public string Key => $"{At.X},{At.Y},{At.Z}";
}

/// <summary>What the scan last knew about one living creature.</summary>
public sealed class BotZoneSeen
{
    public BotZoneKind Kind;

    public BotHaunt Haunt;

    public bool Awake;

    public long Tick;

    public float Power;

    public int Sightings;
}

/// <summary>
/// The spawners of the bots' home map read as haunts, the kinds of creature they keep, and the register of living creatures
/// the scan has passed.
///
/// <para>
/// <b>Read in strips, a strip a beat.</b> <c>map.GetItemsInBounds&lt;BaseSpawner&gt;</c> over one column of the map is a
/// fraction of a millisecond; the whole map at once is the nine milliseconds <see cref="BotLairs"/> spends on the island
/// alone, and the dungeons and the lost lands double it. So the survey walks the map a strip per beat, and walks it again
/// every <see cref="ResurveyMs"/> to find spawners added or removed, which on this shard is nearly never.
/// </para>
/// </summary>
public static class BotHaunts
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotHaunts));

    public static int StripTiles { get; set; } = 256;

    public static int ResurveyMs { get; set; } = 1800000;

    public static int MeasurePerBeat { get; set; } = 2;

    public static int RepowerEvery { get; set; } = 16;

    public static int StaleMs { get; set; } = 180000;

    public static Map Map { get; private set; }

    public static bool Surveyed { get; private set; }

    public static int Surveys { get; private set; }

    public static double SurveyMs { get; private set; }

    public static int Spawners => _all.Count;

    public static int NoCreature { get; private set; }

    public static int Stopped { get; private set; }

    public static int UnknownNames { get; private set; }

    public static long Measured { get; private set; }

    public static long Unbuildable { get; private set; }

    public static int Pending { get; private set; }

    public static IReadOnlyList<BotHaunt> All => _all;

    public static IReadOnlyCollection<BotZoneKind> Kinds => _kinds.Values;

    public static int Registered => _seen.Count;

    private static readonly List<BotHaunt> _all = [];

    private static readonly Dictionary<BaseSpawner, BotHaunt> _bySpawner = [];

    private static readonly Dictionary<Type, BotZoneKind> _kinds = [];

    private static readonly Dictionary<string, Type> _named = new(StringComparer.OrdinalIgnoreCase);

    private static readonly Dictionary<BaseCreature, BotZoneSeen> _seen = [];

    private static readonly List<BaseCreature> _gone = [];

    private static readonly List<BotHauntEntry> _entries = [];

    private static int _strip;

    private static bool _passing;

    private static long _passedTick;

    private static double _passMs;

    private static int _passNoCreature;

    private static int _passStopped;

    public static void Open(Map map, long now)
    {
        Forget();
        Map = map;

        _passedTick = now - ResurveyMs;
    }

    public static bool AttacksFirst(FightMode mode) => mode is FightMode.Closest or FightMode.Strongest or FightMode.Weakest;

    public static bool IsTownsfolk(BaseCreature c) => c is BaseVendor or BaseEscortable || c.IsInvulnerable;

    public static bool Step(long now)
    {
        var map = Map;

        if (map == null)
        {
            return false;
        }

        if (!_passing)
        {
            if (now - _passedTick < ResurveyMs)
            {
                return false;
            }

            _passing = true;
            _strip = 0;
            _passMs = 0.0;
            _passNoCreature = 0;
            _passStopped = 0;

            for (var i = 0; i < _all.Count; i++)
            {
                _all[i].Found = false;
            }
        }

        var began = Stopwatch.GetTimestamp();
        var side = Math.Max(16, StripTiles);
        var x = _strip * side;

        if (x < map.Width)
        {
            Strip(map, new Rectangle2D(x, 0, Math.Min(side, map.Width - x), map.Height), now);
            _strip++;
        }

        _passMs += Stopwatch.GetElapsedTime(began).TotalMilliseconds;

        if (_strip * side < map.Width)
        {
            return true;
        }

        for (var i = _all.Count - 1; i >= 0; i--)
        {
            var haunt = _all[i];

            if (haunt.Found && haunt.Spawner is { Deleted: false })
            {
                continue;
            }

            haunt.Index = -1;
            _bySpawner.Remove(haunt.Spawner);
            _all.RemoveAt(i);
        }

        _passing = false;
        _passedTick = now;
        NoCreature = _passNoCreature;
        Stopped = _passStopped;
        SurveyMs = _passMs;
        Surveys++;

        if (!Surveyed)
        {
            Surveyed = true;

            logger.Information(
                "Zones: the spawners of {Map} were read in {Ms:F1}ms of the loop over {Strips} strips: {Haunts} keep creatures of {Kinds} kinds, {None} keep none, {Stopped} are stopped, {Unknown} names are no creature",
                map,
                SurveyMs,
                _strip,
                _all.Count,
                _kinds.Count,
                NoCreature,
                Stopped,
                UnknownNames
            );
        }

        return true;
    }

    private static void Strip(Map map, Rectangle2D bounds, long now)
    {
        foreach (var spawner in map.GetItemsInBounds<BaseSpawner>(bounds))
        {
            if (spawner is not { Deleted: false } || spawner.Map != map)
            {
                continue;
            }

            _entries.Clear();

            var list = spawner.Entries;

            if (list != null)
            {
                for (var i = 0; i < list.Count; i++)
                {
                    var entry = list[i];
                    var kind = KindNamed(entry?.SpawnedName);

                    if (kind == null || (kind.Townsfolk && kind.Known))
                    {
                        continue;
                    }

                    _entries.Add(new BotHauntEntry(kind, i, Math.Max(1, entry.SpawnedMaxCount), Math.Max(1, entry.SpawnedProbability)));
                }
            }

            if (_entries.Count == 0)
            {
                _passNoCreature++;

                continue;
            }

            if (!_bySpawner.TryGetValue(spawner, out var haunt))
            {
                haunt = new BotHaunt(spawner, now);
                _bySpawner[spawner] = haunt;
                _all.Add(haunt);
            }

            haunt.Found = true;
            haunt.At = spawner.Location;
            haunt.Radius = Math.Max(0, Math.Max(spawner.HomeRange, spawner.WalkingRange));
            haunt.Count = Math.Max(1, spawner.Count);
            haunt.Running = spawner.Running;
            haunt.Entries = _entries.ToArray();
            haunt.InTown = Region.Find(spawner.Location, map)?.IsPartOf<TownRegion>() == true;

            if (!haunt.Running)
            {
                _passStopped++;
            }
        }
    }

    public static BotZoneKind KindNamed(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        name = name.Trim();

        if (!_named.TryGetValue(name, out var type))
        {
            type = AssemblyHandler.FindTypeByName(name);

            if (type != null && !typeof(BaseCreature).IsAssignableFrom(type))
            {
                type = null;
            }

            if (type == null)
            {
                UnknownNames++;
            }

            _named[name] = type;
        }

        if (type == null)
        {
            return null;
        }

        var kind = KindOf(type);
        kind.Spawned ??= name;

        return kind;
    }

    public static BotZoneKind KindOf(Type type)
    {
        if (!_kinds.TryGetValue(type, out var kind))
        {
            kind = new BotZoneKind(type);
            _kinds[type] = kind;
        }

        return kind;
    }

    public static bool Measure()
    {
        var done = 0;
        var pending = 0;

        foreach (var kind in _kinds.Values)
        {
            if (kind.Known || kind.Unbuildable || kind.Spawned == null)
            {
                continue;
            }

            if (done >= MeasurePerBeat)
            {
                pending++;

                continue;
            }

            done++;

            var might = BotDungeon.Might(kind.Spawned);

            if (!BotDungeon.Traits(kind.Spawned, out var traits))
            {
                kind.Unbuildable = true;
                Unbuildable++;

                continue;
            }

            kind.AddPower(might);
            kind.Aggressive = AttacksFirst(traits.Mode);
            kind.Caster = traits.AI == AIType.AI_Mage;
            kind.Perception = traits.Perception;
            kind.Water = traits.CantWalk && traits.CanSwim;
            kind.Rooted = traits.CantWalk && !traits.CanSwim;
            kind.Townsfolk = traits.Townsfolk;
            kind.Known = true;
            Measured++;
        }

        Pending = pending;

        return done > 0;
    }

    public static BotHaunt Of(BaseSpawner spawner) =>
        spawner != null && _bySpawner.TryGetValue(spawner, out var haunt) ? haunt : null;

    public static BotZoneKind Note(BaseCreature c, bool awake, long now)
    {
        var kind = KindOf(c.GetType());

        if (!kind.FromLife)
        {
            kind.ReadFrom(c);
        }

        if (kind.Townsfolk || IsTownsfolk(c))
        {
            return null;
        }

        if (!_seen.TryGetValue(c, out var seen))
        {
            seen = new BotZoneSeen { Kind = kind };
            _seen[c] = seen;
        }

        if (seen.Sightings++ % Math.Max(1, RepowerEvery) == 0)
        {
            var power = BotThreat.Power(c);

            seen.Power = (float)power;
            kind.AddPower(power);
        }

        seen.Awake = awake;
        seen.Tick = now;

        var haunt = c.Spawner is BaseSpawner spawner ? Of(spawner) : null;

        seen.Haunt = haunt;

        if (haunt != null)
        {
            var home = c.Home == Point3D.Zero ? haunt.At : c.Home;

            haunt.Sample(now, awake, Math.Max(Math.Abs(c.X - home.X), Math.Abs(c.Y - home.Y)));
        }

        return kind;
    }

    public static void Living(Map map, long now, Action<BaseCreature, BotZoneSeen> each)
    {
        _gone.Clear();

        foreach (var (c, seen) in _seen)
        {
            if (c is not { Deleted: false, Alive: true } || c.Map != map || c.Controlled || c.Summoned || now - seen.Tick > StaleMs)
            {
                _gone.Add(c);

                continue;
            }

            each(c, seen);
        }

        for (var i = 0; i < _gone.Count; i++)
        {
            _seen.Remove(_gone[i]);
        }

        _gone.Clear();
    }

    public static string Describe() =>
        Map == null
            ? "no map"
            : $"{_all.Count} spawners keep creatures ({NoCreature} keep none, {Stopped} stopped), {_kinds.Count} kinds ({Measured} built to be measured, {Unbuildable} would not build, {UnknownNames} names no creature), {_seen.Count} living creatures registered; the survey {(Surveyed ? $"took {SurveyMs:F1}ms over {Surveys} passes" : "is under way")}";

    public static void Forget()
    {
        Map = null;
        Surveyed = false;
        Surveys = 0;
        SurveyMs = 0.0;
        NoCreature = 0;
        Stopped = 0;
        UnknownNames = 0;
        Measured = 0;
        Unbuildable = 0;
        _all.Clear();
        _bySpawner.Clear();
        _kinds.Clear();
        _named.Clear();
        _seen.Clear();
        _gone.Clear();
        _passing = false;
        _strip = 0;
    }
}
