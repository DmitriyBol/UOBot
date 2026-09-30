using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Server.Engines.Pathing.Tiered;
using Server.Items;
using Server.Logging;

namespace Server.BotAI.V2;

/// <summary>
/// The teleporters of the world read as gates between lands, so that a walk can be drawn through a cave mouth
/// instead of stopping at it.
///
/// <para>
/// <b>Patrick's order of 29.09.2026: "we have cave mouths in the world — entrances to the dungeons. Instead of carrying
/// and teleporting, every cave gets its proper entrance that teleports the bots inside, and the way out is the same:
/// come back to the entrance. Travel there as a party, alone, or alone and gather a party inside."</b> The engine already
/// does the teleporting: a <see cref="Teleporter"/> is an invisible item on a tile, and a body that steps onto it is put
/// down at the item's destination (<c>Teleporter.OnMoveOver</c>). What no bot could do was <i>plan</i> a walk through one,
/// because the dungeon block from x 5120 eastward shares no ground with the island — the navigation graph's components say
/// so, provably — and a search for a road that does not exist answers "no way through" however it is asked. So every
/// delve since 11.09.2026 put its party down by <c>MoveToWorld</c> and lifted it out again (<c>BotDelve.Put</c>,
/// <c>BotDelveParty.Surface</c>).
/// </para>
///
/// <para>
/// <b>A gate is a fact about two components.</b> Every active teleporter on the population's map whose destination is on
/// the same map is read once from the world's items, folded with its neighbours (three in a row across a doorway are one
/// gate), and each end is placed in the graph's component it stands in (<see cref="NavigationService.ComponentOf"/>).
/// A land is a component. A route from one land to another is a shortest path over the gates — a few hundred of them,
/// Dijkstra by straight distance — and what the journey is handed is the first gate on it: walk to that tile exactly,
/// the engine does the rest, and the next beat draws the next leg from wherever the walker has been put down
/// (<c>BotJourney.Draw</c>).
/// </para>
///
/// <para>
/// <b>Read from the world rather than written down, by the rule the dungeons are surveyed by.</b> A table of entrances
/// would be a promise about a map; the items are the map. And the lands are not written down at all: the component is
/// asked of the graph, which is rebuilt when houses go up and read back from disk at boot, so a gate's two ends are as
/// current as the graph is.
/// </para>
///
/// <para>
/// <b>What it will not do.</b> It does not route <i>within</i> a land — that is the tiers' business — and it does not
/// step: a walker on a gate's tile is teleported by the engine or is not (a criminal at a guarded gate, a body mid-fight
/// where the gate checks for it), and a walker that was not is a walker planning straight at a goal in another land,
/// which fails the way it always did and is counted (<see cref="Refused"/>).
/// </para>
/// </summary>
public static class BotGates
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotGates));

    /// <summary>One way through: the tile to step on, where it leads, and the land at each end.</summary>
    public sealed class Gate
    {
        public Point3D From;

        public Point3D To;

        public readonly List<Point3D> Tiles = [];

        public int FromLand = -1;

        public int ToLand = -1;

        public string Name = "a passage";

        public long Used;

        public bool Moongate;

        public override string ToString() => $"{Name} at ({From.X}, {From.Y}) to ({To.X}, {To.Y})";
    }

    public static bool Running { get; set; } = true;

    public static int Fold { get; set; } = 4;

    public static int Through { get; set; } = 6;

    public static int LandCacheMs { get; set; } = 60000;

    public static int LandCacheMost { get; set; } = 50000;

    private static readonly List<Gate> _gates = [];

    private static readonly Dictionary<(int X, int Y, int Z), (int Land, long Tick)> _lands = [];

    private static readonly Dictionary<(int A, int B, int FX, int FY, int TX, int TY), (Gate First, long Tick)> _routes = [];

    public static int DungeonToll { get; set; } = 3000;

    private static int Toll(Gate gate, Point3D to)
    {
        if (DungeonToll <= 0 || !BotDungeon.Under(gate.To))
        {
            return 0;
        }

        return BotDungeon.Holding(gate.To) is { } deep && ReferenceEquals(deep, BotDungeon.Holding(to)) ? 0 : DungeonToll;
    }

    public static Gate NearestMoongate(Map map, Point3D from, int toLand, int most)
    {
        if (!Ensure(map) || toLand < 0)
        {
            return null;
        }

        var land = LandOf(map, from);

        if (land < 0 || land == toLand)
        {
            return null;
        }

        Gate best = null;
        var least = int.MaxValue;

        for (var i = 0; i < _gates.Count; i++)
        {
            var gate = _gates[i];

            if (!gate.Moongate || gate.FromLand != land || gate.ToLand != toLand || Away(from, gate.From) > most)
            {
                continue;
            }

            var away = Away(from, gate.From) + Away(gate.To, from) / 16;

            if (away < least)
            {
                least = away;
                best = gate;
            }
        }

        return best;
    }

    public static string Chain(Map map, Point3D from, Point3D to)
    {
        if (!Ensure(map))
        {
            return Describe();
        }

        var la = LandOf(map, from);
        var lb = LandOf(map, to);

        if (la < 0 || lb < 0)
        {
            return $"the graph cannot place {(la < 0 ? "the start" : "the goal")}";
        }

        if (la == lb)
        {
            return $"both stand in land {la}: no gate is needed";
        }

        var chain = new List<Gate>();
        var first = First(la, from, lb, to, chain);

        if (first == null)
        {
            return $"no way over the gates from land {la} to land {lb}";
        }

        var parts = new List<string>();

        for (var i = 0; i < chain.Count; i++)
        {
            parts.Add($"{chain[i]} (land {chain[i].FromLand} to {chain[i].ToLand}{(Toll(chain[i], to) > 0 ? ", into a dungeon" : "")})");
        }

        return $"from land {la} to land {lb} in {chain.Count} gates: {string.Join("; then ", parts)}";
    }

    private static Map _map;

    public static bool Surveyed { get; private set; }

    public static bool Landed { get; private set; }

    public static bool Ready => Running && Surveyed && Landed;

    private static int _landEpoch;

    public static int Relanded { get; private set; }

    public static int Moved { get; private set; }

    public static int Gates => _gates.Count;

    public static bool RaiseMissing { get; set; } = true;

    public static bool Moongates { get; set; } = true;

    public static bool RaiseMoongates { get; set; } = true;

    public static int MoongatesRead { get; private set; }

    public static int MoongatesRaised { get; private set; }

    public static long MoonTrips { get; private set; }

    public static long MoonRefused { get; private set; }

    public static long Nudged { get; private set; }

    public static long NudgeRefused { get; private set; }

    private static readonly Dictionary<Serial, long> _nudgedAt = [];

    public static bool Nudge(Mobile body, Point3D tile)
    {
        var map = body?.Map;

        if (map == null || map == Map.Internal || body.X != tile.X || body.Y != tile.Y)
        {
            return false;
        }

        var now = Core.TickCount;

        if (_nudgedAt.TryGetValue(body.Serial, out var at) && now - at < NudgeEveryMs)
        {
            return true;
        }

        if (_nudgedAt.Count > 512)
        {
            _nudgedAt.Clear();
        }

        _nudgedAt[body.Serial] = now;

        Teleporter found = null;

        foreach (var gate in map.GetItemsInRange<Teleporter>(new Point3D(tile.X, tile.Y, body.Z), 0))
        {
            if (gate is { Deleted: false, Active: true } && gate.X == tile.X && gate.Y == tile.Y && gate.GetType() == typeof(Teleporter))
            {
                found = gate;

                break;
            }
        }

        if (found == null)
        {
            return false;
        }

        var before = body.Location;

        found.OnMoveOver(body);

        if (body.Location != before || found.Delay > TimeSpan.Zero)
        {
            Nudged++;

            return true;
        }

        NudgeRefused++;

        return false;
    }

    public static int NudgeEveryMs { get; set; } = 4000;

    public static int Raised { get; private set; }

    public static int Present { get; private set; }

    public static int Odd { get; private set; }

    public static int Tiles { get; private set; }

    public static int Landless { get; private set; }

    public static long Asked { get; private set; }

    public static long Routed { get; private set; }

    public static long Refused { get; private set; }

    public static long Passed { get; private set; }

    public static IReadOnlyList<Gate> All => _gates;

    public static bool Ensure(Map map)
    {
        if (!Running || map == null || map == Map.Internal)
        {
            return false;
        }

        if (!Surveyed)
        {
            Survey(map);
        }

        if (Surveyed && !Landed && NavigationService.ComponentsCounted(map))
        {
            Land(map);
        }
        else if (Landed && map == _map && NavigationService.ComponentsCounted(map) && NavigationService.ComponentEpoch(map) != _landEpoch)
        {
            Reland(map);
        }

        return Ready;
    }

    public static void Survey(Map map)
    {
        if (Surveyed || map == null || map == Map.Internal)
        {
            return;
        }

        Surveyed = true;
        _map = map;

        if (RaiseMissing)
        {
            Raise(map);
        }

        var read = 0;

        foreach (var gate in map.GetItemsInBounds<Teleporter>(new Rectangle2D(0, 0, map.Width, map.Height)))
        {
            if (gate is not { Deleted: false } || gate.Map != map)
            {
                continue;
            }

            if (!gate.Active || gate.GetType() != typeof(Teleporter) || gate.MapDest != null && gate.MapDest != map || gate.PointDest == Point3D.Zero)
            {
                Odd++;

                continue;
            }

            read++;
            Add(gate.Location, gate.PointDest);
        }

        Tiles = read;

        if (Moongates)
        {
            ReadMoongates(map);
        }

        for (var i = 0; i < _gates.Count; i++)
        {
            Settle(_gates[i]);
        }

        logger.Information(
            "Gates: {Tiles} teleporters on {Map} read as {Gates} gates ({Odd} passed over as not the plain kind, inactive or off the map; {Raised} raised from Data/teleporters.json, {Present} of the file's already stood); their lands are asked of the navigation graph once its components are counted",
            read,
            map.Name,
            _gates.Count,
            Odd,
            Raised,
            Present
        );

        Gate aloft = null;
        sbyte under = 0;

        for (var i = 0; i < _gates.Count; i++)
        {
            if (!_gates[i].Moongate && Aloft(map, _gates[i].To, out var floor))
            {
                Floating++;

                if (aloft == null)
                {
                    aloft = _gates[i];
                    under = floor;
                }
            }
        }

        if (aloft != null)
        {
            logger.Information(
                "Gates: {Floating} of {Gates} put a walker down above the floor there — the first {Gate}, at height {Z} over a floor at {Floor}; a walker put down so is set on the floor as it lands",
                Floating,
                _gates.Count,
                aloft,
                aloft.To.Z,
                under
            );
        }
    }

    public static int Floating { get; private set; }

    public static long Alighted { get; private set; }

    public static int SayAlighted { get; set; } = 12;

    public static bool Aloft(Map map, Point3D at, out sbyte floor)
    {
        floor = 0;

        if (map == null || map == Map.Internal || BotStep.Ground(map, at.X, at.Y, at.Z, 1, out _))
        {
            return false;
        }

        return BotStep.Ground(map, at.X, at.Y, at.Z, BotStep.GroundReach, out floor) && floor < at.Z;
    }

    public static bool Alight(Mobile body)
    {
        var map = body?.Map;

        if (map == null || map == Map.Internal || !body.Alive || !Aloft(map, body.Location, out var floor))
        {
            return false;
        }

        var was = body.Location;

        body.MoveToWorld(new Point3D(was.X, was.Y, floor), map);
        Alighted++;

        if (Alighted <= SayAlighted)
        {
            logger.Information(
                "Gates: {Name} was put down at ({X}, {Y}) at height {Z}, over a floor at {Floor}, and has been set on it",
                body.Name,
                was.X,
                was.Y,
                was.Z,
                floor
            );
        }

        return true;
    }

    private static void Raise(Map map)
    {
        var path = Path.Combine(Core.BaseDirectory, "Data", "teleporters.json");

        if (!File.Exists(path))
        {
            return;
        }

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));

            foreach (var entry in doc.RootElement.EnumerateArray())
            {
                if (!entry.TryGetProperty("src", out var src) || !entry.TryGetProperty("dst", out var dst))
                {
                    continue;
                }

                if (!Read(src, out var srcMap, out var from) || !Read(dst, out var dstMap, out var to))
                {
                    continue;
                }

                if (!string.Equals(srcMap, map.Name, StringComparison.OrdinalIgnoreCase) || !string.Equals(dstMap, map.Name, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                Want(map, from, to);

                if (entry.TryGetProperty("back", out var back) && back.ValueKind == JsonValueKind.True)
                {
                    Want(map, to, from);
                }
            }
        }
        catch (Exception e)
        {
            logger.Error(e, "Gates: Data/teleporters.json could not be read; the world's own teleporters are used as they stand");
        }
    }

    private static bool Read(JsonElement end, out string mapName, out Point3D at)
    {
        mapName = null;
        at = Point3D.Zero;

        if (!end.TryGetProperty("map", out var m) || !end.TryGetProperty("loc", out var loc) || loc.ValueKind != JsonValueKind.Array || loc.GetArrayLength() < 2)
        {
            return false;
        }

        mapName = m.GetString();

        var x = loc[0].GetInt32();
        var y = loc[1].GetInt32();
        var z = loc.GetArrayLength() > 2 ? loc[2].GetInt32() : 0;

        at = new Point3D(x, y, z);

        return true;
    }

    private static void Want(Map map, Point3D from, Point3D to)
    {
        foreach (var item in map.GetItemsAt(from.X, from.Y))
        {
            if (item is Teleporter { Deleted: false } gate && Away(gate.PointDest, to) <= 1)
            {
                Present++;

                return;
            }
        }

        var made = new Teleporter(to, map);

        made.MoveToWorld(from, map);
        Raised++;
    }

    private static void Add(Point3D tile, Point3D dest)
    {
        for (var i = 0; i < _gates.Count; i++)
        {
            var gate = _gates[i];

            if (Away(gate.To, dest) > Fold)
            {
                continue;
            }

            for (var j = 0; j < gate.Tiles.Count; j++)
            {
                if (Away(gate.Tiles[j], tile) <= Fold)
                {
                    gate.Tiles.Add(tile);

                    return;
                }
            }
        }

        var made = new Gate { To = dest };

        made.Tiles.Add(tile);
        _gates.Add(made);
    }

    private static string MoonTown(int number) =>
        number switch
        {
            1012003 => "Moonglow",
            1012004 => "Britain",
            1012005 => "Jhelom",
            1012006 => "Yew",
            1012007 => "Minoc",
            1012008 => "Trinsic",
            1012009 => "Skara Brae",
            1012010 => "Magincia",
            1019001 => "Buccaneer's Den",
            _ => "a far moongate"
        };

    private static void ReadMoongates(Map map)
    {
        var list = map == Map.Felucca ? PMList.Felucca : map == Map.Trammel ? PMList.Trammel : null;

        if (list == null)
        {
            return;
        }

        List<Point3D> stands = [];

        foreach (var gate in map.GetItemsInBounds<PublicMoongate>(new Rectangle2D(0, 0, map.Width, map.Height)))
        {
            if (gate is { Deleted: false } && gate.Map == map)
            {
                stands.Add(gate.Location);
            }
        }

        if (RaiseMoongates)
        {
            foreach (var entry in list.Entries)
            {
                var standing = false;

                for (var s = 0; s < stands.Count; s++)
                {
                    if (Away(stands[s], entry.Location) <= 2)
                    {
                        standing = true;

                        break;
                    }
                }

                if (standing)
                {
                    continue;
                }

                var raised = new PublicMoongate();
                raised.MoveToWorld(entry.Location, map);
                stands.Add(entry.Location);
                MoongatesRaised++;
            }
        }

        MoongatesRead = stands.Count;

        for (var s = 0; s < stands.Count; s++)
        {
            var stand = stands[s];

            foreach (var entry in list.Entries)
            {
                if (Away(stand, entry.Location) <= 2)
                {
                    continue;
                }

                var made = new Gate { To = entry.Location, Moongate = true, Name = $"the moongate to {MoonTown(entry.Number)}" };
                made.Tiles.Add(stand);
                _gates.Add(made);
            }
        }

        logger.Information(
            "Moongates: {Stands} public moongates on {Map} ({Raised} raised from the engine's list), read as ways to {Entries} cities",
            stands.Count,
            map.Name,
            MoongatesRaised,
            list.Entries.Length
        );
    }

    public static Gate Moon(Point3D from, Point3D to)
    {
        for (var i = 0; i < _gates.Count; i++)
        {
            var gate = _gates[i];

            if (gate.Moongate && gate.From.X == from.X && gate.From.Y == from.Y && Away(gate.To, to) <= 1)
            {
                return gate;
            }
        }

        return null;
    }

    public static bool Travel(Mobile body, Gate gate)
    {
        var map = body?.Map;

        if (map == null || map == Map.Internal || gate is not { Moongate: true })
        {
            return false;
        }

        if (body.Criminal || Server.Spells.SpellHelper.CheckCombat(body) || body.Spell != null)
        {
            MoonRefused++;

            return false;
        }

        Server.Mobiles.BaseCreature.TeleportPets(body, gate.To, map);
        body.Combatant = null;
        body.Warmode = false;
        body.MoveToWorld(gate.To, map);
        Effects.PlaySound(gate.To, map, 0x1FE);
        gate.Used++;
        MoonTrips++;

        return true;
    }

    private static void Settle(Gate gate)
    {
        if (gate.Moongate)
        {
            gate.From = gate.Tiles[0];

            return;
        }

        gate.Tiles.Sort(static (a, b) => a.X != b.X ? a.X.CompareTo(b.X) : a.Y.CompareTo(b.Y));
        gate.From = gate.Tiles[gate.Tiles.Count / 2];

        var into = Deep(gate.To);
        var from = Deep(gate.From);

        gate.Name = into != null && into != from
            ? $"into {into.Name}"
            : from != null && into != from
                ? $"out of {from.Name}"
                : "a passage";
    }

    private static BotDungeon.Deep Deep(Point3D where)
    {
        var all = BotDungeon.All;

        for (var i = 0; i < all.Count; i++)
        {
            if (all[i].Holds(where))
            {
                return all[i];
            }
        }

        return null;
    }

    private static void Reland(Map map)
    {
        _landEpoch = NavigationService.ComponentEpoch(map);
        _lands.Clear();
        _routes.Clear();
        Relanded++;
        Landless = 0;

        var moved = 0;

        for (var i = 0; i < _gates.Count; i++)
        {
            var gate = _gates[i];
            var from = NavigationService.ComponentOf(map, gate.From);
            var to = NavigationService.ComponentOf(map, gate.To);

            if (from != gate.FromLand || to != gate.ToLand)
            {
                moved++;
            }

            gate.FromLand = from;
            gate.ToLand = to;

            if (from < 0 || to < 0)
            {
                Landless++;
            }
        }

        Moved += moved;

        if (moved > 0)
        {
            logger.Information(
                "Gates: the graph counted its lands afresh ({Count} times now) and {Moved} of {Gates} gates had their lands renamed; routes over the gates are drawn anew",
                Relanded,
                moved,
                _gates.Count
            );
        }
    }

    private static void Land(Map map)
    {
        Landed = true;
        Landless = 0;
        _landEpoch = NavigationService.ComponentEpoch(map);

        var lands = new HashSet<int>();

        for (var i = 0; i < _gates.Count; i++)
        {
            var gate = _gates[i];

            gate.FromLand = NavigationService.ComponentOf(map, gate.From);
            gate.ToLand = NavigationService.ComponentOf(map, gate.To);

            if (gate.FromLand < 0 || gate.ToLand < 0)
            {
                Landless++;

                continue;
            }

            lands.Add(gate.FromLand);
            lands.Add(gate.ToLand);
        }

        var home = BotPopulation.Where;
        var homeLand = NavigationService.ComponentOf(map, home);
        var reachable = 0;
        var deeps = 0;

        for (var i = 0; i < BotDungeon.All.Count; i++)
        {
            var deep = BotDungeon.All[i];

            if (First(homeLand, home, deep) is { } gate)
            {
                reachable++;
                deeps++;

                logger.Information("Gates: {Deep} is reached from home through {Gate}", deep.Name, gate);
            }
            else
            {
                deeps++;

                logger.Information("Gates: {Deep} has no way in from home over the gates", deep.Name);
            }
        }

        logger.Information(
            "Gates: {Gates} gates join {Lands} lands ({Landless} gates have an end the graph cannot place); from home (land {Home}) {Reachable} of {Deeps} dungeons can be walked into",
            _gates.Count,
            lands.Count,
            Landless,
            homeLand,
            reachable,
            deeps
        );
    }

    public static int LandOf(Map map, Point3D at)
    {
        if (!Ensure(map) || map != _map)
        {
            return -1;
        }

        var key = (at.X, at.Y, (at.Z + 128) >> 3);
        var now = Core.TickCount;

        if (_lands.Count >= LandCacheMost)
        {
            _lands.Clear();
        }

        if (_lands.TryGetValue(key, out var known) && now - known.Tick < LandCacheMs)
        {
            return known.Land;
        }

        var land = NavigationService.ComponentOf(map, at);

        if (land >= 0)
        {
            _lands[key] = (land, now);
        }

        return land;
    }

    public static bool Joined(Map map, Point3D a, Point3D b)
    {
        if (!Ensure(map))
        {
            return BotDungeon.Under(a) == BotDungeon.Under(b);
        }

        var la = LandOf(map, a);
        var lb = LandOf(map, b);

        if (la < 0 || lb < 0)
        {
            return BotDungeon.Under(a) == BotDungeon.Under(b);
        }

        return la == lb || First(la, a, lb, b) != null;
    }

    public static bool Next(Map map, Point3D from, Point3D to, out Gate gate)
    {
        gate = null;

        if (!Ensure(map))
        {
            return false;
        }

        var la = LandOf(map, from);
        var lb = LandOf(map, to);

        if (la < 0 || lb < 0 || la == lb)
        {
            return false;
        }

        gate = First(la, from, lb, to);

        return gate != null;
    }

    public static Gate First(int fromLand, Point3D from, BotDungeon.Deep deep)
    {
        if (deep == null || fromLand < 0 || _map == null)
        {
            return null;
        }

        var room = deep.Rooms.Count > 0 ? deep.Rooms[0] : new Point3D(deep.Middle.X, deep.Middle.Y, 0);
        var land = LandOf(_map, room);

        if (land < 0)
        {
            return null;
        }

        return land == fromLand ? null : First(fromLand, from, land, room);
    }

    public static int Edge { get; set; } = 5120;

    public static Gate Mouth(Map map, Point3D from, Point3D to)
    {
        if (!Ensure(map))
        {
            return null;
        }

        var la = LandOf(map, from);
        var lb = LandOf(map, to);

        if (la < 0 || lb < 0 || la == lb)
        {
            return null;
        }

        var chain = new List<Gate>();

        if (First(la, from, lb, to, chain) == null)
        {
            return null;
        }

        for (var i = 0; i < chain.Count; i++)
        {
            if (chain[i].From.X < Edge && chain[i].To.X >= Edge)
            {
                return chain[i];
            }
        }

        return null;
    }

    public static bool Reaches(Map map, Point3D from, BotDungeon.Deep deep)
    {
        if (!Ensure(map) || deep == null)
        {
            return false;
        }

        var room = deep.Rooms.Count > 0 ? deep.Rooms[0] : new Point3D(deep.Middle.X, deep.Middle.Y, 0);

        return Joined(map, from, room);
    }

    private static Gate First(int fromLand, Point3D from, int toLand, Point3D to, List<Gate> chain = null)
    {
        var now = Core.TickCount;
        var key = (fromLand, toLand, from.X >> 6, from.Y >> 6, to.X >> 6, to.Y >> 6);

        if (chain == null && _routes.TryGetValue(key, out var kept) && now - kept.Tick < LandCacheMs)
        {
            return kept.First;
        }

        if (_routes.Count >= 4096)
        {
            _routes.Clear();
        }

        Asked++;

        var n = _gates.Count;
        Span<int> dist = n <= 1024 ? stackalloc int[n] : new int[n];
        Span<int> prev = n <= 1024 ? stackalloc int[n] : new int[n];
        Span<bool> done = n <= 1024 ? stackalloc bool[n] : new bool[n];

        for (var i = 0; i < n; i++)
        {
            var gate = _gates[i];

            dist[i] = gate.FromLand == fromLand && gate.ToLand >= 0 ? Away(from, gate.From) + Toll(gate, to) : int.MaxValue;
            prev[i] = -1;
            done[i] = false;
        }

        var best = int.MaxValue;
        var bestGate = -1;

        while (true)
        {
            var pick = -1;
            var least = int.MaxValue;

            for (var i = 0; i < n; i++)
            {
                if (!done[i] && dist[i] < least)
                {
                    least = dist[i];
                    pick = i;
                }
            }

            if (pick < 0 || least >= best)
            {
                break;
            }

            done[pick] = true;

            var gate = _gates[pick];

            if (gate.ToLand == toLand)
            {
                var total = least + Away(gate.To, to);

                if (total < best)
                {
                    best = total;
                    bestGate = pick;
                }

                continue;
            }

            for (var j = 0; j < n; j++)
            {
                var next = _gates[j];

                if (done[j] || next.FromLand != gate.ToLand || next.ToLand < 0)
                {
                    continue;
                }

                var through = least + Away(gate.To, next.From) + 1 + Toll(next, to);

                if (through < dist[j])
                {
                    dist[j] = through;
                    prev[j] = pick;
                }
            }
        }

        Gate first = null;

        if (bestGate >= 0)
        {
            var at = bestGate;

            while (prev[at] >= 0)
            {
                at = prev[at];
            }

            first = _gates[at];

            if (chain != null)
            {
                for (var step = bestGate; step >= 0; step = prev[step])
                {
                    chain.Insert(0, _gates[step]);
                }

                return first;
            }

            first.Used++;
            Routed++;
        }
        else if (chain != null)
        {
            return null;
        }

        _routes[key] = (first, now);

        return first;
    }

    public static void NotedRefused() => Refused++;

    public static void NotedPassed() => Passed++;

    private static int Away(Point3D a, Point3D b) => Math.Max(Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));

    public static string Describe() =>
        !Running
            ? "gates are off: every land is an island"
            : !Surveyed
                ? "the gates have not been read"
                : !Landed
                    ? $"{_gates.Count} gates read from {Tiles} teleporters, waiting for the graph's components"
                    : $"{_gates.Count} gates from {Tiles} teleporters and {MoongatesRead} moongates ({MoongatesRaised} raised; {MoonTrips} walkers put through a moongate, {MoonRefused} refused by one; {Nudged} stepped through a teleporter that had not fired for them, {NudgeRefused} refused by it; {Floating} put a walker down above the floor, {Alighted} walkers set on it) ({Landless} with an end the graph cannot place; lands asked again {Relanded} times, {Moved} gates renamed); {Asked} land-to-land routes asked, {Routed} found; {Passed} walkers put through a gate, {Refused} stood on one and stayed";

    public static string Near(Map map, Point3D at, int radius)
    {
        if (!Ensure(map))
        {
            return Describe();
        }

        var lines = new List<string>();

        for (var i = 0; i < _gates.Count && lines.Count < 20; i++)
        {
            var gate = _gates[i];

            if (Away(gate.From, at) > radius && Away(gate.To, at) > radius)
            {
                continue;
            }

            lines.Add(
                $"{gate} (lands {gate.FromLand} to {gate.ToLand} kept, {NavigationService.ComponentOf(map, gate.From)} to {NavigationService.ComponentOf(map, gate.To)} live; used {gate.Used})"
            );
        }

        return lines.Count == 0
            ? $"no gate within {radius} tiles of ({at.X}, {at.Y}); the point stands in land {LandOf(map, at)}"
            : $"the point ({at.X}, {at.Y}) stands in land {LandOf(map, at)}; {lines.Count} gates near: {string.Join("; ", lines)}";
    }

    public static void Forget()
    {
        _gates.Clear();
        _lands.Clear();
        _routes.Clear();
        _map = null;
        Surveyed = false;
        Landed = false;
        _landEpoch = 0;
        Tiles = 0;
        Landless = 0;
        Asked = 0;
        Routed = 0;
        Refused = 0;
        Passed = 0;
        Floating = 0;
        Alighted = 0;
    }
}
