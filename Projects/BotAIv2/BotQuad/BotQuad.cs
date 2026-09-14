using System;
using System.Collections.Generic;
using Server.Logging;
using Server.Mobiles;
using Server.Regions;

namespace Server.BotAI.V2;

/// <summary>
/// The island cut into squares thirty tiles across, each carrying one number: how safe the population has
/// found it to be.
///
/// <para>
/// <b>This is a different question from <see cref="BotPeril"/>'s and the two must not be merged.</b> Peril
/// answers "where is it dangerous <em>now</em>" — a decaying frequency of blows, right for a captain deciding
/// where a company should be standing this minute, and deliberately forgetful, because a graveyard that was
/// terrible an hour ago and quiet since is not where anybody should be sent. This answers "what sort of
/// ground is that" — a standing reputation that a place earns slowly, in both directions, and does not
/// forget on its own. A quiet meadow and a graveyard nobody has visited since the last massacre read the
/// same on Peril's map and must never read the same here.
/// </para>
///
/// <para>
/// <b>Both directions, which is what makes it a reputation rather than a scar.</b> Blows and deaths push a
/// square down; bots walking through it and coming out the other side push it back up. So ground that was
/// cleared genuinely recovers — by being walked, which is evidence — rather than by a clock running out,
/// which is not. A square nothing has happened in for a day reads exactly what it read yesterday, and that
/// is the point: the population's memory of the island should outlive any one session's worth of walking.
/// </para>
///
/// <para>
/// <b>Shared by everybody, kept nowhere else.</b> There is one map and every bot reads and writes the same
/// squares, the way the squad's stations and the market's prices are shared: nothing is messaged anywhere,
/// and two bots asking the same question of the same ground get the same answer by construction.
/// </para>
/// </summary>
public static class BotQuad
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotQuad));

    public const int Side = 30;

    public const double Safest = 1.0;

    public const double Bleakest = -1.0;

    public const double Fresh = 0.0;

    public const double Positive = 0.01;

    public const double Neutral = 0.0;

    public const double Unsafe = -0.01;

    public static string Band(double safety) =>
        safety >= Safest ? "safe"
        : safety >= Positive ? "positive"
        : safety > Unsafe ? "neutral"
        : safety > Bleakest ? "unsafe"
        : "dangerous";

    public static int PerPass { get; set; } = 25;

    public static double PassWorth { get; set; } = 0.05;

    public static int PerBlows { get; set; } = 5;

    public static double BlowsWorth { get; set; } = -0.01;

    public static int PerHarvest { get; set; } = 50;

    public static double HarvestWorth { get; set; } = 0.01;

    public static double MobWorth { get; set; } = -0.1;

    public static double Fearless { get; set; } = Neutral;

    public static double DeathWorth { get; set; } = -0.05;

    public static double BaronWorth { get; set; } = -0.5;

    public static double SweptWorth { get; set; }

    public static int PerRangerBlows { get; set; } = 50;

    public static double RangerBlowsWorth { get; set; } = -0.05;

    public static double RangerDeathWorth { get; set; } = -0.1;

    public static double WipedWorth { get; set; } = -0.5;

    public static long Sweeps { get; private set; }

    public static long Wiped { get; private set; }

    public static double TooQuiet { get; set; } = 0.5;

    public static double Wanted { get; set; } = -0.1;

    public static double Dire { get; set; } = -0.3;

    public static int Reinforcement { get; set; } = 5;

    public static int DireLoss { get; set; } = 30;

    public static double Damned { get; set; } = Bleakest;

    public static int Levy(Map map, Point3D where, int ordinary)
    {
        var quad = Known(map, where);

        return quad == null ? ordinary : Math.Max(ordinary, quad.Levied);
    }

    public static bool Damning(Map map, Point3D where)
    {
        var quad = Known(map, where);

        return quad != null && quad.Wipes > 0 && quad.Safety <= Damned;
    }

    public static void LostCompany(Map map, Point3D where, int lost)
    {
        if (map == null || map == Map.Internal || lost <= 0)
        {
            return;
        }

        var quad = At(map, where);

        if (quad == null)
        {
            return;
        }

        quad.Wipes++;
        quad.Levied = Math.Max(quad.Levied, lost) + Reinforcement;
        quad.Tick = Core.TickCount;

        Wiped++;

        if (lost >= DireLoss)
        {
            quad.Safety = Damned;

            logger.Warning(
                "A company of {Lost} was lost whole around ({X}, {Y}); the ground is damned at {Safety:F2} and only grandmasters may be sent to it",
                lost,
                quad.Middle.X,
                quad.Middle.Y,
                quad.Safety
            );

            return;
        }

        quad.Safety = Math.Clamp(quad.Safety + WipedWorth, Bleakest, Safest);

        logger.Warning(
            "A company of {Lost} was lost whole around ({X}, {Y}); the ground now reads {Safety:F2} and the next levy is {Levy}",
            lost,
            quad.Middle.X,
            quad.Middle.Y,
            quad.Safety,
            quad.Levied
        );
    }

    public static double Harrowed { get; set; } = 0.0;

    public static int BaulkMs { get; set; } = 600000;

    public static int MostBaulks { get; set; } = 12;

    public static long Baulked { get; private set; }

    public static void Baulk(Map map, Point3D where)
    {
        var quad = At(map, where);

        if (quad == null)
        {
            return;
        }

        if (quad.Baulks < MostBaulks)
        {
            quad.Baulks++;
        }

        quad.BaulkedTick = Core.TickCount;
        Baulked++;
    }

    private static bool Resting(Quad quad, long now) =>
        quad.Baulks > 0 && now - quad.BaulkedTick < (long)BaulkMs * quad.Baulks;

    public static bool Baulking(Map map, Point3D where)
    {
        if (map == null)
        {
            return false;
        }

        return _quads.TryGetValue(Key(map, where), out var quad) && Resting(quad, Core.TickCount);
    }

    public static int Most { get; set; } = 32768;

    private static bool _saidFull;

    /// <summary>One square of the island, and everything the population knows about it.</summary>
    public sealed class Quad
    {
        public Map Map;

        public int X;

        public int Y;

        public double Safety;

        public int Passes;

        public int Levied;

        public int Wipes;

        public int Towards;

        public int Blows;

        public int Baulks;

        public long BaulkedTick;

        public int Bruising;

        public int Harvests;

        public int Reaping;

        public int Mobs;

        public long Sighted;

        public bool Townbound;

        public int RangerBruising;

        public int RangersLost;

        public bool Swept;

        public int Deaths;

        public bool Trodden;

        public long Tick;

        public long HarrowedTick;

        public Point2D Middle => new(X * Side + Side / 2, Y * Side + Side / 2);

        public override string ToString() =>
            $"({Middle.X}, {Middle.Y}) at {Safety:F2} on {Passes} crossings, {Blows} blows and {Deaths} dead";
    }

    private static readonly Dictionary<(int Map, int X, int Y), Quad> _quads = [];

    public static IReadOnlyCollection<Quad> All => _quads.Values;

    private static readonly List<Quad> _feared = [];

    private static long _fearedTick;

    private static bool _fearedEver;

    public static int FearedRefreshMs { get; set; } = 15000;

    public static int MostFeared { get; set; } = 16;

    public static Point2D WorstNear(Map map, Point3D from, int within)
    {
        if (map == null || map == Map.Internal)
        {
            return Point2D.Zero;
        }

        var now = Core.TickCount;

        if (!_fearedEver || now - _fearedTick >= FearedRefreshMs)
        {
            _fearedEver = true;
            _fearedTick = now;
            _feared.Clear();

            foreach (var quad in _quads.Values)
            {
                if (quad.Safety <= Wanted)
                {
                    _feared.Add(quad);
                }
            }

            _feared.Sort(static (a, b) => a.Safety.CompareTo(b.Safety));

            if (_feared.Count > MostFeared)
            {
                _feared.RemoveRange(MostFeared, _feared.Count - MostFeared);
            }
        }

        for (var i = 0; i < _feared.Count; i++)
        {
            var quad = _feared[i];

            if (quad.Map != map)
            {
                continue;
            }

            if (Resting(quad, now))
            {
                continue;
            }

            var middle = quad.Middle;

            if (Math.Abs(middle.X - from.X) <= within && Math.Abs(middle.Y - from.Y) <= within)
            {
                return middle;
            }
        }

        return Point2D.Zero;
    }

    public static int Count => _quads.Count;

    public static long Credited { get; private set; }

    public static long Marked { get; private set; }

    public static long Mourned { get; private set; }

    public static long Discovered { get; private set; }

    public static long Cleansed { get; private set; }

    public static (int Map, int X, int Y) Key(Map map, Point3D where) =>
        (map?.MapID ?? -1, Floor(where.X), Floor(where.Y));

    private static int Floor(int tile) => (int)Math.Floor(tile / (double)Side);

    public static Quad At(Map map, Point3D where)
    {
        if (map == null || map == Map.Internal)
        {
            return null;
        }

        var key = Key(map, where);

        if (_quads.TryGetValue(key, out var quad))
        {
            return quad;
        }

        if (_quads.Count >= Most)
        {
            if (!_saidFull)
            {
                _saidFull = true;

                logger.Error(
                    "The island map is full at {Most} squares and will record no new ground; scouting will report everything as already walked until this is raised",
                    Most
                );
            }

            return null;
        }

        quad = new Quad
        {
            Map = map,
            X = key.X,
            Y = key.Y,
            Safety = Fresh,
            Tick = Core.TickCount
        };

        Settle(quad, map);

        _quads[key] = quad;

        return quad;
    }

    public static long Walled { get; private set; }

    private static void Settle(Quad quad, Map map)
    {
        if (quad == null || map == null || map == Map.Internal)
        {
            return;
        }

        var x = quad.X * Side + Side / 2;
        var y = quad.Y * Side + Side / 2;

        var middle = new Point3D(x, y, map.GetAverageZ(x, y));

        if (Region.Find(middle, map)?.IsPartOf<GuardedRegion>() != true)
        {
            return;
        }

        quad.Safety = Safest;
        quad.Trodden = true;
        quad.Townbound = true;

        Walled++;
    }

    public static Quad Known(Map map, Point3D where) =>
        map == null || map == Map.Internal ? null : _quads.GetValueOrDefault(Key(map, where));

    public static double Safety(Map map, Point3D where)
    {
        var quad = Known(map, where);

        if (quad == null)
        {
            return Fresh;
        }

        return Math.Clamp(quad.Safety + MobWorth * Living(quad), Bleakest, Safest);
    }

    public static int SightMs { get; set; } = 300000;

    public static double Reading(Quad quad) =>
        quad == null ? Fresh : Math.Clamp(quad.Safety + MobWorth * Living(quad), Bleakest, Safest);

    private static int Living(Quad quad) =>
        quad.Mobs > 0 && Core.TickCount - quad.Sighted < SightMs ? quad.Mobs : 0;

    public static double Earned(Map map, Point3D where) => Known(map, where)?.Safety ?? Fresh;

    public static void Sighted(Map map, Point3D where, int mobs)
    {
        var quad = At(map, where);

        if (quad == null)
        {
            return;
        }

        quad.Mobs = Math.Max(0, mobs);
        quad.Sighted = Core.TickCount;

        Counted++;
    }

    public static long Counted { get; private set; }

    public static int LookEveryMs { get; set; } = 10000;

    public static long Looks { get; private set; }

    public static void Look(Mobile body)
    {
        if (body is not { Deleted: false, Alive: true } || body.Map == null || body.Map == Map.Internal)
        {
            return;
        }

        var quad = At(body.Map, body.Location);

        if (quad == null || Core.TickCount - quad.Sighted < LookEveryMs)
        {
            return;
        }

        var mobs = 0;

        foreach (var creature in body.Map.GetMobilesInRange<BaseCreature>(body.Location, Side / 2))
        {
            if (creature is { Deleted: false, Alive: true } && BotThreat.Hostile(body, creature))
            {
                mobs++;
            }
        }

        Looks++;

        Sighted(body.Map, body.Location, mobs);
    }

    public static double Muscle(double safety)
    {
        if (safety > Fearless)
        {
            return 0.0;
        }

        if (safety > -0.01)
        {
            return 0.0;
        }

        if (safety > -0.05)
        {
            return 1000.0;
        }

        if (safety > -0.10)
        {
            return 3000.0;
        }

        var steps = (int)Math.Floor((-safety - 0.10) / 0.05 + 1e-9);

        return 4500.0 + steps * 500.0;
    }

    public static double Muscle(Map map, Point3D where) => Muscle(Safety(map, where));

    public static double Strength(Mobile body)
    {
        if (body is not { Deleted: false, Alive: true })
        {
            return 0.0;
        }

        if (body is not IBotSquadMember { Squad: not null } member)
        {
            return BotThreat.Power(body);
        }

        var members = member.Squad.Members;
        var strength = 0.0;

        for (var i = 0; i < members.Count; i++)
        {
            if (members[i] is IBotAlly { AbleToFight: true } && members[i].Self is { Deleted: false, Alive: true } self)
            {
                strength += BotThreat.Power(self);
            }
        }

        return strength;
    }

    public static long Feared { get; private set; }

    public static bool Together(Mobile body, Map map, Point3D where, int within)
    {
        var asked = Muscle(map, where);

        if (asked <= 0.0)
        {
            return true;
        }

        var strength = Strength(body);
        var facet = body?.Map;

        if (facet == null || facet == Map.Internal)
        {
            return false;
        }

        foreach (var mobile in facet.GetMobilesInRange<Mobile>(body.Location, within))
        {
            if (mobile == body || mobile is not IBotSquadMember { Squad: null })
            {
                continue;
            }

            if (mobile is IBotAlly { AbleToFight: true } && mobile is { Deleted: false, Alive: true })
            {
                strength += BotThreat.Power(mobile);
            }
        }

        return strength >= asked;
    }

    public static bool Dares(Mobile body, Map map, Point3D where)
    {
        var asked = Muscle(map, where);

        if (asked <= 0.0)
        {
            return true;
        }

        if (Strength(body) >= asked)
        {
            return true;
        }

        Feared++;

        return false;
    }

    public static bool Trodden(Map map, Point3D where) => Known(map, where)?.Trodden == true;

    public static void Crossed(Map map, Point3D left, Point3D entered)
    {
        var into = At(map, entered);

        if (into != null && !into.Trodden)
        {
            into.Trodden = true;
            into.Tick = Core.TickCount;

            Discovered++;
        }

        var quad = Known(map, left);

        if (quad == null)
        {
            return;
        }

        quad.Passes++;
        quad.Towards++;
        quad.Tick = Core.TickCount;

        if (quad.Towards < PerPass)
        {
            return;
        }

        quad.Towards = 0;

        Raise(quad, PassWorth);
        Credited++;
    }

    public static void Harvested(Map map, Point3D where)
    {
        var quad = Known(map, where);

        if (quad == null)
        {
            return;
        }

        quad.Harvests++;
        quad.Reaping++;
        quad.Tick = Core.TickCount;

        if (quad.Reaping < PerHarvest)
        {
            return;
        }

        quad.Reaping = 0;

        Raise(quad, HarvestWorth);
        Reaped++;
    }

    public static long Reaped { get; private set; }

    public static void Seen(Map map, Point3D where)
    {
        var quad = At(map, where);

        if (quad == null)
        {
            return;
        }

        quad.Tick = Core.TickCount;

        if (quad.Trodden)
        {
            return;
        }

        quad.Trodden = true;
        Discovered++;
    }

    public static void Struck(Map map, Point3D where, bool ranger = false)
    {
        var quad = At(map, where);

        if (quad == null)
        {
            return;
        }

        quad.Blows++;
        quad.Tick = Core.TickCount;

        if (ranger)
        {
            quad.RangerBruising++;

            if (quad.RangerBruising < PerRangerBlows)
            {
                return;
            }

            quad.RangerBruising = 0;

            Raise(quad, RangerBlowsWorth);
            Marked++;

            return;
        }

        quad.Towards = 0;
        quad.Reaping = 0;

        quad.Bruising++;

        if (quad.Bruising < PerBlows)
        {
            return;
        }

        quad.Bruising = 0;

        Raise(quad, BlowsWorth);
        Marked++;
    }

    public static void Swept(Map map, Point3D where)
    {
        var quad = At(map, where);

        if (quad == null)
        {
            return;
        }

        quad.Swept = true;
        quad.Tick = Core.TickCount;

        if (!quad.Trodden)
        {
            quad.Trodden = true;
            Discovered++;
        }

        if (SweptWorth != 0.0)
        {
            Raise(quad, SweptWorth);
        }

        Sweeps++;
    }

    public static void FellRanger(Map map, Point3D where, bool wiped)
    {
        Fell(map, where, RangerDeathWorth);

        var quad = Known(map, where);

        if (quad == null)
        {
            return;
        }

        quad.RangersLost++;

        if (!wiped)
        {
            return;
        }

        Raise(quad, WipedWorth);
        Wiped++;

        logger.Warning(
            "The King's Rangers were destroyed around ({X}, {Y}); the ground now reads {Safety:F2}",
            quad.Middle.X,
            quad.Middle.Y,
            quad.Safety
        );
    }

    public static void Fell(Map map, Point3D where, double worth)
    {
        var quad = At(map, where);

        if (quad == null)
        {
            return;
        }

        quad.Deaths++;
        quad.Tick = Core.TickCount;

        Raise(quad, worth);
        Mourned++;

        logger.Information(
            "The ground around ({X}, {Y}) has taken somebody and now reads {Safety:F2}, on {Deaths} dead",
            quad.Middle.X,
            quad.Middle.Y,
            quad.Safety,
            quad.Deaths
        );
    }

    public static void Fell(Map map, Point3D where) => Fell(map, where, DeathWorth);

    public static void Cleared(Quad quad)
    {
        if (quad == null)
        {
            return;
        }

        var was = quad.Safety;

        quad.Safety = Harrowed;
        quad.Bruising = 0;
        quad.Towards = 0;
        quad.HarrowedTick = Core.TickCount;
        quad.Tick = Core.TickCount;

        Cleansed++;

        logger.Information(
            "The ground around ({X}, {Y}) has been harrowed: it read {Was:F2} and is now {Now:F2}",
            quad.Middle.X,
            quad.Middle.Y,
            was,
            quad.Safety
        );
    }

    private static void Raise(Quad quad, double by)
    {
        var was = quad.Safety;

        quad.Safety = Math.Clamp(quad.Safety + by, Bleakest, Safest);

        if (was <= TooQuiet && quad.Safety > TooQuiet)
        {
            Hushed++;
        }
        else if (was > TooQuiet && quad.Safety <= TooQuiet)
        {
            Roused++;
        }
    }

    public static long Hushed { get; private set; }

    public static long Roused { get; private set; }

    public static List<Quad> Worst(int most, Map map = null)
    {
        List<Quad> found = [];

        foreach (var quad in _quads.Values)
        {
            if (map != null && quad.Map != map)
            {
                continue;
            }

            found.Add(quad);
        }

        found.Sort(static (a, b) => a.Safety.CompareTo(b.Safety));

        if (most > 0 && found.Count > most)
        {
            found.RemoveRange(most, found.Count - most);
        }

        return found;
    }

    public static List<Quad> Around(Quad quad, bool madeIfNew)
    {
        List<Quad> found = [];

        if (quad?.Map == null)
        {
            return found;
        }

        for (var dx = -1; dx <= 1; dx++)
        {
            for (var dy = -1; dy <= 1; dy++)
            {
                var key = (quad.Map.MapID, quad.X + dx, quad.Y + dy);

                if (_quads.TryGetValue(key, out var near))
                {
                    found.Add(near);

                    continue;
                }

                if (!madeIfNew || _quads.Count >= Most)
                {
                    continue;
                }

                near = new Quad
                {
                    Map = quad.Map,
                    X = quad.X + dx,
                    Y = quad.Y + dy,
                    Safety = Fresh,
                    Tick = Core.TickCount
                };

                Settle(near, quad.Map);

                _quads[key] = near;
                found.Add(near);
            }
        }

        return found;
    }

    public static Point3D Frontier(Map map, Point3D from, int within, Func<Point3D, bool> fit)
    {
        if (map == null || map == Map.Internal)
        {
            return Point3D.Zero;
        }

        var closest = int.MaxValue;
        var best = Point3D.Zero;

        foreach (var quad in _quads.Values)
        {
            if (quad.Map != map || !quad.Trodden)
            {
                continue;
            }

            for (var dx = -1; dx <= 1; dx++)
            {
                for (var dy = -1; dy <= 1; dy++)
                {
                    if (dx == 0 && dy == 0)
                    {
                        continue;
                    }

                    if (_quads.TryGetValue((map.MapID, quad.X + dx, quad.Y + dy), out var near) && near.Trodden)
                    {
                        continue;
                    }

                    var x = (quad.X + dx) * Side + Side / 2;
                    var y = (quad.Y + dy) * Side + Side / 2;
                    var away = Math.Max(Math.Abs(x - from.X), Math.Abs(y - from.Y));

                    if (away > within || away >= closest)
                    {
                        continue;
                    }

                    if (!BotStep.Settle(map, x, y, out var z))
                    {
                        continue;
                    }

                    var at = new Point3D(x, y, z);

                    if (fit != null && !fit(at))
                    {
                        continue;
                    }

                    closest = away;
                    best = at;
                }
            }
        }

        return best;
    }

    public static Quad Direst(Map map, Point3D from, int within, Func<Point3D, bool> fit)
    {
        if (map == null || map == Map.Internal)
        {
            return null;
        }

        Quad best = null;
        var lowest = Dire;

        foreach (var quad in _quads.Values)
        {
            if (quad.Map != map || quad.Safety > lowest)
            {
                continue;
            }

            var middle = quad.Middle;

            if (Math.Max(Math.Abs(middle.X - from.X), Math.Abs(middle.Y - from.Y)) > within)
            {
                continue;
            }

            if (!BotStep.Settle(map, middle.X, middle.Y, out var z))
            {
                continue;
            }

            if (fit != null && !fit(new Point3D(middle.X, middle.Y, z)))
            {
                continue;
            }

            lowest = quad.Safety;
            best = quad;
        }

        return best;
    }

    public static Point3D Stand(Quad quad)
    {
        if (quad?.Map == null)
        {
            return Point3D.Zero;
        }

        var middle = quad.Middle;

        return BotStep.Settle(quad.Map, middle.X, middle.Y, out var z)
            ? new Point3D(middle.X, middle.Y, z)
            : Point3D.Zero;
    }

    public static string Describe()
    {
        if (_quads.Count == 0)
        {
            return "no ground has been walked yet";
        }

        var trodden = 0;
        var quiet = 0;
        var wanted = 0;
        var dire = 0;
        var damned = 0;
        Quad worst = null;

        foreach (var quad in _quads.Values)
        {
            if (quad.Trodden)
            {
                trodden++;
            }

            if (quad.Safety > TooQuiet)
            {
                quiet++;
            }

            if (quad.Safety <= Wanted)
            {
                wanted++;
            }

            if (quad.Safety <= Dire)
            {
                dire++;
            }

            if (quad.Wipes > 0 && quad.Safety <= Damned)
            {
                damned++;
            }

            if (worst == null || quad.Safety < worst.Safety)
            {
                worst = quad;
            }
        }

        return $"{_quads.Count} quadrants of {Side} tiles, {trodden} of them stood in: {quiet} too quiet to hunt "
            + $"({Hushed} shut and {Roused} reopened since the shard came up, which is the direction rather than the level) "
               + $"(above {TooQuiet:F2}), {wanted} worth going to (at or below {Wanted:F2}), {dire} dire (at or below {Dire:F2}) of which {damned} damned by a company being lost in them; "
               + $"worst is {worst}; {Discovered} first set foot in, {Credited} raised for crossings, "
               + $"{Marked} marked for blows, {Mourned} for a death, {Cleansed} harrowed, {Sweeps} swept by rangers, {Wiped} took a whole company, {Baulked} rested because nobody could get near them, {Reaped} credited for undisturbed harvests, {Counted} counts of what lives in a square over {Looks} sweeps, {Feared} refused to somebody not strong enough, {Walled} born safe inside the walls";
    }

    public static void Restore(
        int facet,
        int x,
        int y,
        double safety,
        int passes,
        int blows,
        int deaths,
        int rangersLost,
        bool trodden,
        bool swept,
        bool harrowed,
        int levied = 0,
        int wipes = 0
    )
    {
        var map = Map.Maps is { Length: > 0 } && facet >= 0 && facet < Map.Maps.Length ? Map.Maps[facet] : null;

        if (map == null || map == Map.Internal || _quads.Count >= Most)
        {
            return;
        }

        _quads[(facet, x, y)] = new Quad
        {
            Map = map,
            X = x,
            Y = y,
            Safety = Math.Clamp(safety, Bleakest, Safest),
            Passes = passes,
            Blows = blows,
            Deaths = deaths,
            RangersLost = rangersLost,
            Trodden = trodden,
            Swept = swept,
            Levied = levied,
            Wipes = wipes,

            Tick = Core.TickCount,
            HarrowedTick = harrowed ? Core.TickCount : 0
        };
    }

    public static void Forget()
    {
        _quads.Clear();

        Credited = 0;
        Marked = 0;
        Mourned = 0;
        Discovered = 0;
        Cleansed = 0;
        Reaped = 0;
        Counted = 0;
        Looks = 0;
        Feared = 0;
        Walled = 0;
        Baulked = 0;
        Sweeps = 0;
        Wiped = 0;
        Hushed = 0;
        Roused = 0;
    }
}
