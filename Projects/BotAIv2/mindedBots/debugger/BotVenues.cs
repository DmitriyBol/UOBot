using System;
using System.Collections.Generic;
using Server.BotAI.V2;
using Server.Logging;
using Server.Regions;
using Server.Text;

namespace Server.BotAI.Mind;

/// <summary>
/// Where the watchers hold what they hold: a revel's feast in a town, a hunt's waves and an orc camp in the countryside, the
/// championship's ring — each chosen from the places that fit it, are safe, can be walked to, were not used lately, and lie
/// where the bots actually are.
///
/// <para>
/// <b>Patrick, 02.10.2026: "I always see the debuggers holding feasts and events in one and the same place, which is also very
/// bad."</b> They were in one place by construction. A revel was "called" at the population's home and every hunt's waves stood
/// there — 27 of the 29 hunt revels between the wipe of 01.10.2026 11:32 and 11:00 on 02.10.2026 put every wave at (1440, 1470);
/// the marshal's two camps stood at (1356, 1384) and two tiles from it, the model naming the same spot each time; and all 136
/// championship bouts were fought on the drill ground at (1458, 1500). Nothing chose a place: a constant did.
/// </para>
///
/// <para>
/// <b>Four tests, one order.</b> A candidate must fit its use (a town's square for a feast; outdoors, out of every guarded
/// region, for a field; enough open ground in sight of the middle for a ring), be safe (no hostile or deadly ground at the
/// middle and no deadly ground within <see cref="SafeRadius"/> — <c>BotZones.LevelAt</c>, read only), be reached (open ground on
/// the reach map, or joined by the gates while the map is still being drawn), and not lie within <see cref="RepeatApart"/> of
/// the last <see cref="Recent"/> places of its use. Among those that pass, the draw is weighted by the bots near each: the
/// square root of the count within <see cref="NearReach"/>, so the town most of the island lives in is likeliest without
/// being the only answer. A field is pitched off a bot that is working in the open near the chosen town, which is where a
/// hunter will see a wave.
/// </para>
///
/// <para>
/// <b>Every refusal has its own counter</b>, and a choice that fell back to the old fixed place says so (<see cref="Fallbacks"/>):
/// a venue chooser that quietly returned home would read exactly like the defect it replaces.
/// </para>
/// </summary>
public static class BotVenues
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotVenues));

    /// <summary>What a place is for.</summary>
    public enum Use
    {
        Feast,

        Field,

        Ring
    }

    /// <summary>One place chosen: what for, a name a person reads, where, and how many bots were near it when it was chosen.</summary>
    public sealed class Venue
    {
        public Use Use { get; init; }

        public string Name { get; init; }

        public string Town { get; init; }

        public Map Map { get; init; }

        public Point3D At { get; set; }

        public int Near { get; init; }

        public bool Fallback { get; init; }

        public string Phrase => Use == Use.Field && !Fallback ? Name : $"at {Name}";

        public override string ToString() => $"{Name} ({At.X}, {At.Y})";
    }

    public static bool Running { get; set; } = true;

    public static int Recent { get; set; } = 3;

    public static int RepeatApart { get; set; } = 40;

    public static int NearReach { get; set; } = 200;

    public static double EmptyWeight { get; set; } = 0.25;

    public static int FieldNear { get; set; } = 18;

    public static int FieldFar { get; set; } = 42;

    public static int SafeRadius { get; set; } = 12;

    public static int RingOpen { get; set; } = 60;

    public static int HeraldHoldMs { get; set; } = 120000;

    public static long Chosen { get; private set; }

    public static long Deadly { get; private set; }

    public static long Hostile { get; private set; }

    public static long Unreached { get; private set; }

    public static long Repeats { get; private set; }

    public static long Cramped { get; private set; }

    public static long Walled { get; private set; }

    public static long Unstood { get; private set; }

    public static long Reused { get; private set; }

    public static long Fallbacks { get; private set; }

    public static long ModelKept { get; private set; }

    public static long ModelMoved { get; private set; }

    public static long Heralded { get; private set; }

    private static readonly List<Venue>[] _recent = [[], [], []];

    private static readonly Dictionary<string, int>[] _where = [new(), new(), new()];

    private static readonly Dictionary<string, bool> _rings = new(StringComparer.OrdinalIgnoreCase);

    private static BotWatcher _herald;

    private static long _heraldUntil;

    private static Map _heraldMap;

    private static Point3D _heraldAt;

    private static string _heraldWhat;

    private static BotWatcher _referee;

    private static Map _refereeMap;

    private static Point3D _refereeAt;

    private static string _refereeWhat;

    public static long Refereed { get; private set; }

    public static Venue Choose(Use use, Map map, IReadOnlyList<Mobile> who = null)
    {
        Venue found = null;

        if (Running && map != null && map != Map.Internal && BotTowns.Ensure(map))
        {
            found = use == Use.Field ? Field(map) : Town(use, map, who);
        }

        found ??= Default(use, map);
        Note(found);

        return found;
    }

    public static Venue Accept(Map map, Point3D spot)
    {
        if (!Running || map == null || map == Map.Internal || spot == Point3D.Zero)
        {
            return null;
        }

        BotTowns.Ensure(map);

        Nearest(map, spot, out var town, out var near);

        if (near > 0 && Fits(Use.Field, map, ref spot) && !Lately(Use.Field, spot))
        {
            ModelKept++;

            var venue = new Venue
            {
                Use = Use.Field, Name = Bearing(town, spot), Town = town?.Name, Map = map, At = spot, Near = near
            };

            Note(venue);

            return venue;
        }

        ModelMoved++;

        return null;
    }

    public static Venue InTown(Use use, Map map, string name)
    {
        if (!Running || map == null || map == Map.Internal || string.IsNullOrEmpty(name) || !BotTowns.Ensure(map) || BotTowns.Find(name) is not { } town)
        {
            return null;
        }

        var at = use == Use.Ring && town.Bounds.Contains(BotTourney.Ring) ? BotTourney.Ring : town.Square;

        if (!Safe(map, at) || !Reached(map, at) || use == Use.Ring && !RingFits(map, town.Name, at))
        {
            return null;
        }

        var venue = new Venue { Use = use, Name = town.Name, Town = town.Name, Map = map, At = at, Near = Count(BotPopulation.Bots, map, at) };

        Note(venue);

        return venue;
    }

    public static Venue Exact(Use use, Map map, Point3D spot)
    {
        BotTowns.Ensure(map);
        Nearest(map, spot, out var town, out var near);

        var venue = new Venue { Use = use, Name = Bearing(town, spot), Town = town?.Name, Map = map, At = spot, Near = near };

        Note(venue);

        return venue;
    }

    public static void Moved(Venue venue, Point3D at)
    {
        if (venue != null)
        {
            venue.At = at;
        }
    }

    private static Venue Town(Use use, Map map, IReadOnlyList<Mobile> who)
    {
        List<(Venue Venue, double Weight)> passed = [];
        List<Venue> recent = [];

        var towns = BotTowns.All;

        for (var i = 0; i < towns.Count; i++)
        {
            var town = towns[i];

            if (!town.FromHome)
            {
                continue;
            }

            var at = use == Use.Ring && town.Bounds.Contains(BotTourney.Ring) ? BotTourney.Ring : town.Square;

            if (!Safe(map, at) || !Reached(map, at))
            {
                continue;
            }

            var near = who != null ? Count(who, map, at) : Count(BotPopulation.Bots, map, at);

            if (use == Use.Ring && near == 0 && at != BotTourney.Ring)
            {
                continue;
            }

            var venue = new Venue { Use = use, Name = town.Name, Town = town.Name, Map = map, At = at, Near = near };

            if (Lately(use, at))
            {
                Repeats++;
                recent.Add(venue);

                continue;
            }

            passed.Add((venue, near > 0 ? Math.Sqrt(near) : EmptyWeight));
        }

        while (passed.Count > 0)
        {
            var pick = Draw(passed);

            if (use != Use.Ring || RingFits(map, pick.Name, pick.At))
            {
                return pick;
            }

            passed.RemoveAll(p => ReferenceEquals(p.Venue, pick));
        }

        while (recent.Count > 0)
        {
            var pick = Oldest(use, recent);

            if (use != Use.Ring || RingFits(map, pick.Name, pick.At))
            {
                Reused++;

                return pick;
            }

            recent.Remove(pick);
        }

        return null;
    }

    private static bool RingFits(Map map, string name, Point3D at)
    {
        if (_rings.TryGetValue(name, out var fits))
        {
            if (!fits)
            {
                Cramped++;
            }

            return fits;
        }

        var open = 0;
        var eye = new Point3D(at.X, at.Y, at.Z + 10);

        for (var dy = -7; dy <= 7; dy++)
        {
            for (var dx = -7; dx <= 7; dx++)
            {
                if (map.CanSpawnMobile(at.X + dx, at.Y + dy, at.Z - 12, at.Z + 12, false, false, out var z)
                    && map.LineOfSight(new Point3D(at.X + dx, at.Y + dy, z + 10), eye))
                {
                    open++;
                }
            }
        }

        fits = open >= RingOpen
               && BotTourney.Spot(map, at, at.X - BotTourney.Apart, at.Y, out _)
               && BotTourney.Spot(map, at, at.X + BotTourney.Apart, at.Y, out _);

        _rings[name] = fits;

        logger.Information(
            "Venues: {Town}'s square at {X},{Y} {Fits} a ring: {Open} of 225 tiles round it open and in sight",
            name,
            at.X,
            at.Y,
            fits ? "holds" : "does not hold",
            open
        );

        if (!fits)
        {
            Cramped++;
        }

        return fits;
    }

    private static Venue Field(Map map)
    {
        List<(BotTowns.Town Town, double Weight, List<BotMobile> Out)> towns = [];
        var all = BotTowns.All;
        var bots = BotPopulation.Bots;

        for (var i = 0; i < all.Count; i++)
        {
            var town = all[i];

            if (!town.FromHome)
            {
                continue;
            }

            var near = 0;
            List<BotMobile> outdoors = [];

            for (var k = 0; k < bots.Count; k++)
            {
                if (bots[k] is not { Deleted: false, Alive: true } bot || bot.Map != map || !Utility.InRange(bot.Location, town.Square, NearReach))
                {
                    continue;
                }

                near++;

                if (!Guarded(map, bot.Location))
                {
                    outdoors.Add(bot);
                }
            }

            if (near > 0)
            {
                towns.Add((town, Math.Sqrt(near), outdoors));
            }
        }

        Venue recent = null;

        for (var attempt = 0; attempt < 24 && towns.Count > 0; attempt++)
        {
            var (town, _, outdoors) = Pick(towns);
            Point3D anchor;

            if (outdoors.Count > 0)
            {
                anchor = outdoors[Utility.Random(outdoors.Count)].Location;
            }
            else if (!Edge(map, town, out anchor))
            {
                continue;
            }

            var bearing = Utility.RandomDouble() * Math.PI * 2.0;
            var span = Utility.RandomMinMax(FieldNear, FieldFar);
            var spot = new Point3D(
                anchor.X + (int)Math.Round(Math.Cos(bearing) * span),
                anchor.Y + (int)Math.Round(Math.Sin(bearing) * span),
                anchor.Z
            );

            if (!Fits(Use.Field, map, ref spot))
            {
                continue;
            }

            var venue = new Venue
            {
                Use = Use.Field, Name = Bearing(town, spot), Town = town.Name, Map = map, At = spot,
                Near = Count(bots, map, spot)
            };

            if (Lately(Use.Field, spot))
            {
                Repeats++;
                recent ??= venue;

                continue;
            }

            return venue;
        }

        if (recent != null)
        {
            Reused++;
        }

        return recent;
    }

    private static bool Fits(Use use, Map map, ref Point3D spot)
    {
        if (use == Use.Field)
        {
            if (!BotStep.Settle(map, spot.X, spot.Y, out var z))
            {
                Unstood++;

                return false;
            }

            spot = new Point3D(spot.X, spot.Y, z);

            if (Guarded(map, spot))
            {
                Walled++;

                return false;
            }
        }

        return Safe(map, spot) && Reached(map, spot);
    }

    private static bool Edge(Map map, BotTowns.Town town, out Point3D at)
    {
        var bearing = Utility.RandomDouble() * Math.PI * 2.0;

        for (var span = 8; span <= 400; span += 8)
        {
            var x = town.Square.X + (int)Math.Round(Math.Cos(bearing) * span);
            var y = town.Square.Y + (int)Math.Round(Math.Sin(bearing) * span);
            var p = new Point3D(x, y, town.Square.Z);

            if (!Guarded(map, p))
            {
                at = p;

                return true;
            }
        }

        at = Point3D.Zero;

        return false;
    }

    private static bool Safe(Map map, Point3D at)
    {
        var middle = BotZones.LevelAt(map, at.X, at.Y);

        if (middle == BotZoneLevel.Deadly)
        {
            Deadly++;

            return false;
        }

        if (middle == BotZoneLevel.Hostile)
        {
            Hostile++;

            return false;
        }

        for (var ring = SafeRadius / 2; ring <= SafeRadius; ring += Math.Max(1, SafeRadius / 2))
        {
            for (var dir = 0; dir < 8; dir++)
            {
                var angle = dir * Math.PI / 4.0;
                var x = at.X + (int)Math.Round(Math.Cos(angle) * ring);
                var y = at.Y + (int)Math.Round(Math.Sin(angle) * ring);

                if (BotZones.LevelAt(map, x, y) == BotZoneLevel.Deadly)
                {
                    Deadly++;

                    return false;
                }
            }
        }

        return true;
    }

    private static bool Reached(Map map, Point3D at)
    {
        var kind = BotReachMap.Class(map, at.X, at.Y, at.Z);

        if (kind != BotReachClass.Unknown)
        {
            if (kind == BotReachClass.Open)
            {
                return true;
            }

            Unreached++;

            return false;
        }

        if (BotGates.Ready && !BotGates.Joined(map, BotPopulation.Where, at))
        {
            Unreached++;

            return false;
        }

        return true;
    }

    private static bool Guarded(Map map, Point3D at) => map != null && Region.Find(at, map)?.IsPartOf<GuardedRegion>() == true;

    private static bool Lately(Use use, Point3D at)
    {
        var list = _recent[(int)use];

        for (var i = 0; i < list.Count; i++)
        {
            if (Utility.InRange(list[i].At, at, RepeatApart))
            {
                return true;
            }
        }

        return false;
    }

    private static Venue Draw(List<(Venue Venue, double Weight)> passed)
    {
        var total = 0.0;

        for (var i = 0; i < passed.Count; i++)
        {
            total += passed[i].Weight;
        }

        var roll = Utility.RandomDouble() * total;

        for (var i = 0; i < passed.Count; i++)
        {
            roll -= passed[i].Weight;

            if (roll < 0.0)
            {
                return passed[i].Venue;
            }
        }

        return passed[^1].Venue;
    }

    private static (BotTowns.Town Town, double Weight, List<BotMobile> Out) Pick(List<(BotTowns.Town Town, double Weight, List<BotMobile> Out)> towns)
    {
        var total = 0.0;

        for (var i = 0; i < towns.Count; i++)
        {
            total += towns[i].Weight;
        }

        var roll = Utility.RandomDouble() * total;

        for (var i = 0; i < towns.Count; i++)
        {
            roll -= towns[i].Weight;

            if (roll < 0.0)
            {
                return towns[i];
            }
        }

        return towns[^1];
    }

    private static Venue Oldest(Use use, List<Venue> candidates)
    {
        var list = _recent[(int)use];
        Venue best = null;
        var bestAge = -1;

        for (var c = 0; c < candidates.Count; c++)
        {
            var age = list.Count;

            for (var i = 0; i < list.Count; i++)
            {
                if (Utility.InRange(list[i].At, candidates[c].At, RepeatApart))
                {
                    age = i;

                    break;
                }
            }

            if (age > bestAge)
            {
                bestAge = age;
                best = candidates[c];
            }
        }

        return best;
    }

    private static int Count<T>(IReadOnlyList<T> who, Map map, Point3D at) where T : Mobile
    {
        var n = 0;

        for (var i = 0; i < who.Count; i++)
        {
            if (who[i] is { Deleted: false, Alive: true } m && m.Map == map && Utility.InRange(m.Location, at, NearReach))
            {
                n++;
            }
        }

        return n;
    }

    private static void Nearest(Map map, Point3D spot, out BotTowns.Town town, out int near)
    {
        town = BotTowns.Surveyed ? BotTowns.Nearest(spot) : null;
        near = Count(BotPopulation.Bots, map, spot);
    }

    private static string Bearing(BotTowns.Town town, Point3D at)
    {
        if (town == null)
        {
            return $"at {at.X},{at.Y}";
        }

        if (town.Bounds.Contains(at))
        {
            return Guarded(BotPopulation.Home, at) ? $"in {town.Name}" : $"near {town.Name}";
        }

        var dx = at.X - town.Square.X;
        var dy = at.Y - town.Square.Y;
        var ns = Math.Abs(dy) * 2 < Math.Abs(dx) ? "" : dy < 0 ? "north" : "south";
        var ew = Math.Abs(dx) * 2 < Math.Abs(dy) ? "" : dx < 0 ? "west" : "east";
        var way = ns.Length > 0 && ew.Length > 0 ? $"{ns}-{ew}" : ns + ew;

        return $"{(way.Length > 0 ? way : "outside")} of {town.Name}";
    }

    private static Venue Default(Use use, Map map)
    {
        Fallbacks++;

        var at = use == Use.Ring ? BotTourney.Ring : BotPopulation.Where;
        var town = BotTowns.Surveyed ? BotTowns.Of(at)?.Name : null;

        return new Venue
        {
            Use = use,
            Name = use == Use.Ring ? "the drill ground" : town ?? "home",
            Town = town,
            Map = map ?? BotPopulation.Home,
            At = at,
            Fallback = true
        };
    }

    private static void Note(Venue venue)
    {
        if (venue == null)
        {
            return;
        }

        Chosen++;

        var list = _recent[(int)venue.Use];

        list.Insert(0, venue);

        while (list.Count > Math.Max(1, Recent))
        {
            list.RemoveAt(list.Count - 1);
        }

        var key = venue.Fallback ? $"{venue.Name} (the old fixed place)" : venue.Use == Use.Field ? $"by {venue.Town ?? "nowhere named"}" : venue.Name;
        var tally = _where[(int)venue.Use];

        tally[key] = tally.TryGetValue(key, out var had) ? had + 1 : 1;
    }

    private static BotWatcher Crier()
    {
        if (BotMarshal.Standing is { Body: { Deleted: false } } marshal && !BotWitness.Busy(marshal) && !BotZoneTour.Holding(marshal.Body)
            && !ReferenceEquals(marshal, _referee))
        {
            return marshal;
        }

        if (BotVigil.Lead is { Body: { Deleted: false } } lead && !BotWitness.Busy(lead) && !BotZoneTour.Holding(lead.Body)
            && (BotHalls.Charted || BotHalls.Probing == Point3D.Zero) && !ReferenceEquals(lead, _referee))
        {
            return lead;
        }

        return null;
    }

    public static string Referee(Map map, Point3D at, string what)
    {
        if (map == null || map == Map.Internal)
        {
            return null;
        }

        BotWatcher pick = null;

        for (var i = 0; i < BotVigil.Squad.Count && pick == null; i++)
        {
            var w = BotVigil.Squad[i];

            if (w.Body is not { Deleted: false } || BotWitness.Busy(w) || BotZoneTour.Holding(w.Body))
            {
                continue;
            }

            if (i == 0 && !BotHalls.Charted && BotHalls.Probing != Point3D.Zero)
            {
                continue;
            }

            pick = w;
        }

        if (pick == null)
        {
            return null;
        }

        if (ReferenceEquals(pick, _herald))
        {
            _herald = null;
        }

        _referee = pick;
        _refereeMap = map;
        _refereeAt = at;
        _refereeWhat = what;
        Refereed++;
        ShowReferee();

        return pick.Name;
    }

    public static void ShowReferee()
    {
        if (_referee?.Body is not { Deleted: false } body || BotWitness.Busy(_referee))
        {
            return;
        }

        if (body.Map != _refereeMap || body.Location != _refereeAt)
        {
            body.Hover(_refereeMap, _refereeAt);
        }

        if (body.Hidden)
        {
            body.Hidden = false;
        }
    }

    public static void Unreferee()
    {
        if (_referee?.Body is { Deleted: false } body)
        {
            body.Hidden = true;
        }

        _referee = null;
    }

    public static string RefereeName => _referee?.Name;

    public static Mobile Voice
    {
        get
        {
            if (Crier()?.Body is { Deleted: false } body)
            {
                return body;
            }

            for (var i = 0; i < BotVigil.Squad.Count; i++)
            {
                if (BotVigil.Squad[i].Body is { Deleted: false } any && any.Map != null && any.Map != Map.Internal)
                {
                    return any;
                }
            }

            return null;
        }
    }

    public static void Herald(Venue venue, int holdMs)
    {
        var crier = Crier();

        if (venue?.Map == null || venue.Map == Map.Internal || crier?.Body is not { Deleted: false } body)
        {
            return;
        }

        var spot = BotTowns.Place(venue.Map, venue.At.X + 2, venue.At.Y + 2, 3, out var aside) ? aside : venue.At;

        body.Hover(venue.Map, spot);

        _herald = crier;
        _heraldMap = venue.Map;
        _heraldAt = spot;
        _heraldWhat = venue.Name;
        _heraldUntil = Core.TickCount + Math.Max(0, holdMs);
        Heralded++;
    }

    public static void Release() => _herald = null;

    public static bool Holds(BotWatcher w) =>
        w != null && (ReferenceEquals(w, _herald) && Core.TickCount < _heraldUntil || ReferenceEquals(w, _referee));

    public static string HeraldOf(BotWatcher w) =>
        w != null && ReferenceEquals(w, _referee)
            ? $"referee of {_refereeWhat ?? "the arena"} ({_refereeAt.X}, {_refereeAt.Y}), shown"
            : Holds(w)
                ? $"herald at {_heraldWhat ?? "the place"} ({_heraldAt.X}, {_heraldAt.Y}), {Math.Max(0, _heraldUntil - Core.TickCount) / 1000}s more"
                : null;

    public static bool Keep(BotWatcher w)
    {
        if (w != null && ReferenceEquals(w, _referee))
        {
            ShowReferee();

            return true;
        }

        if (!Holds(w))
        {
            if (ReferenceEquals(w, _herald))
            {
                _herald = null;
            }

            return false;
        }

        if (w.Body is { Deleted: false } body && !BotWitness.Busy(w) && (body.Map != _heraldMap || body.Location != _heraldAt))
        {
            body.Hover(_heraldMap, _heraldAt);
        }

        return true;
    }

    public static void Announce(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return;
        }

        var voice = Voice;

        logger.Information("Herald: {Line}", line);

        if (voice == null)
        {
            return;
        }

        if (!BotVoice.Say(voice, "world", line, true))
        {
            voice.Say(line.Length > 180 ? line[..180] : line);
        }
    }

    public static void Save(IGenericWriter writer)
    {
        writer.WriteEncodedInt(_recent.Length);

        for (var u = 0; u < _recent.Length; u++)
        {
            var list = _recent[u];

            writer.WriteEncodedInt(list.Count);

            for (var i = 0; i < list.Count; i++)
            {
                writer.Write(list[i].Name ?? "");
                writer.Write(list[i].Town ?? "");
                writer.Write(list[i].At);
            }
        }
    }

    public static void Load(IGenericReader reader)
    {
        for (var u = 0; u < _recent.Length; u++)
        {
            _recent[u].Clear();
        }

        var uses = reader.ReadEncodedInt();

        for (var u = 0; u < uses; u++)
        {
            var count = reader.ReadEncodedInt();

            for (var i = 0; i < count; i++)
            {
                var name = reader.ReadString();
                var town = reader.ReadString();
                var at = reader.ReadPoint3D();

                if (u < _recent.Length)
                {
                    _recent[u].Add(new Venue { Use = (Use)u, Name = name, Town = town.Length > 0 ? town : null, At = at });
                }
            }
        }
    }

    public static void Forget()
    {
        for (var u = 0; u < _recent.Length; u++)
        {
            _recent[u].Clear();
            _where[u].Clear();
        }

        _rings.Clear();
        _herald = null;
        Unreferee();
    }

    public static string Describe()
    {
        if (Chosen == 0)
        {
            return "venues: none chosen yet";
        }

        var say = ValueStringBuilder.Create(512);

        try
        {
            say.Append($"venues: {Chosen} chosen —");
            Tally(ref say, "feasts", _where[(int)Use.Feast]);
            Tally(ref say, "; fields", _where[(int)Use.Field]);
            Tally(ref say, "; rings", _where[(int)Use.Ring]);
            say.Append(
                $"; candidates refused: {Deadly} deadly ground, {Hostile} hostile, {Unreached} unreached, {Repeats} a recent place, {Cramped} no room for a ring, {Walled} in a town, {Unstood} no floor; {Reused} recent places reused, {Fallbacks} fallbacks to the old fixed place; the marshal's own camp spot kept {ModelKept} and moved {ModelMoved}; the herald went to {Heralded}, the arena's referee stood at {Refereed}"
            );

            return say.ToString();
        }
        finally
        {
            say.Dispose();
        }
    }

    private static void Tally(ref ValueStringBuilder say, string label, Dictionary<string, int> tally)
    {
        say.Append(' ');
        say.Append(label);

        if (tally.Count == 0)
        {
            say.Append(" none");

            return;
        }

        List<(string Name, int N)> rows = [];

        foreach (var (name, n) in tally)
        {
            rows.Add((name, n));
        }

        rows.Sort((a, b) => b.N.CompareTo(a.N));

        for (var i = 0; i < rows.Count; i++)
        {
            say.Append(i == 0 ? " " : ", ");
            say.Append(rows[i].Name);
            say.Append(' ');
            say.Append(rows[i].N.ToString());
        }
    }
}
