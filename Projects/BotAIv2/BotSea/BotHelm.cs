using System;
using System.Collections.Generic;
using Server.Engines.CannedEvil;
using Server.Items;
using Server.Logging;
using Server.Multis;
using Server.Regions;

namespace Server.BotAI.V2;

/// <summary>One ship at sea with a bot at its helm: where it is bound, how far along, and how it is going.</summary>
public sealed class BotSeaVoyage
{
    public enum Tide
    {
        Sailing,
        Rerouting,
        Landing,
        Landed,
        Lost
    }

    public BotMobile Captain;

    public readonly List<Mobile> Crew = [];

    public BaseBoat Boat;

    public BotDock From;

    public BotBerth Berth;

    public BotDock To;

    public BotDock LandAt;

    public BotSeaLane Lane;

    public List<Point3D> Course;

    public int Sea;

    public int Harbour;

    public int Leg;

    public int[] Rest;

    public Tide State;

    public string Why;

    public long Began;

    public long Headway;

    public int Best = int.MaxValue;

    public int Blocked;

    public int Reroutes;

    public long LimitMs;

    public bool Harried;

    public BotSeaCourse.Ask Asking;

    public double Minutes => (Core.TickCount - Began) / 60000.0;

    public void Measure()
    {
        Rest = new int[Course.Count];

        for (var i = Course.Count - 2; i >= 0; i--)
        {
            Rest[i] = Rest[i + 1] + BotHelm.Away(Course[i], Course[i + 1]);
        }
    }

    public int Left(Point3D at) => Leg >= Course.Count ? 0 : BotHelm.Away(at, Course[Leg]) + Rest[Leg];
}

/// <summary>
/// The helm: puts a bot's ship in the water and takes it aboard, steers every ship at sea on its own clock, puts it ashore
/// at the far dock and dry-docks the ship into its pack again.
///
/// <para>
/// <b>Steered through the ship's own movement, not the tillerman's words.</b> The tillerman obeys speech keywords, and a
/// keyword is a number the client encodes into what a player types; a bot's <c>Say</c> carries none, so "forward" from a
/// bot moves nothing. The helm calls what those keywords call — <c>BaseBoat.Move</c>, the same step the ship's own move
/// timer takes, which asks <c>CanFit</c> of every tile it moves through, and <c>BaseBoat.Turn</c> — once every
/// <see cref="StrokeMs"/>, the engine's own interval before High Seas, at the engine's own <see cref="Speed"/>: three tiles
/// forward, one sideways or back. No ship here is faster than a player's.
/// </para>
///
/// <para>
/// <b>The ship is steered whatever the bot is doing.</b> A sea serpent that comes alongside puts the captain on the Hunted
/// rung and its own will fights it from the deck, or runs about the deck; the helm is a timer, not the voyage's deed, so
/// the ship sails on meanwhile — which is the flight a ship has. A voyage whose deed is dropped or paused is sailed to its
/// dock and put ashore all the same: nobody is left adrift because an auction changed its mind.
/// </para>
///
/// <para>
/// <b>What happens when it goes wrong, each counted.</b> A ship that cannot move tries a step to either side (another
/// ship in the way); after <see cref="StuckStrokes"/> it shuts the cell ahead and asks for a new course from where it is,
/// up to <see cref="MostReroutes"/> times; blocked in the arrival harbour it lands where it is if there is shore within
/// reach; blocked leaving its own harbour it puts back in. A captain who dies aboard leaves the ship at anchor where it is
/// (the key is on the corpse); a captain no longer aboard leaves it too. A ship that makes no headway for
/// <see cref="BecalmedMs"/>, or is out past its time, is wrecked: the shard's hand carries captain and crew to the nearest
/// dock and dry-docks the ship into the captain's pack — the one thing here that moves a bot by hand, the last resort, and
/// said by name.
/// </para>
/// </summary>
public static class BotHelm
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotHelm));

    public static int StrokeMs { get; set; } = 750;

    public static int Speed { get; set; } = 3;

    public static int StuckStrokes { get; set; } = 8;

    public static int MostReroutes { get; set; } = 3;

    public static int BecalmedMs { get; set; } = 180000;

    public static int SlackMs { get; set; } = 600000;

    public static int LandReach => BotDocks.Gangway + BotSeaChart.Half + 2;

    public static int CrewReach { get; set; } = 12;

    private static readonly List<BotSeaVoyage> _afloat = [];

    public static IReadOnlyList<BotSeaVoyage> Afloat => _afloat;

    public static long Embarked { get; private set; }

    public static long Arrived { get; private set; }

    public static long TurnedBack { get; private set; }

    public static long Died { get; private set; }

    public static long LeftShip { get; private set; }

    public static long Sunk { get; private set; }

    public static long Wrecked { get; private set; }

    public static long Carried { get; private set; }

    public static long DryDocked { get; private set; }

    public static long Moored { get; private set; }

    public static long Harried { get; private set; }

    public static long CrewCarried { get; private set; }

    public static long Strokes { get; private set; }

    public static long Moves { get; private set; }

    public static long Blocks { get; private set; }

    public static long SideSteps { get; private set; }

    public static long Turns { get; private set; }

    public static long Reroutes { get; private set; }

    public static long Recovered { get; private set; }

    public static long Scuttled { get; private set; }

    public static long Refused { get; private set; }

    public static long Lost => TurnedBack + Died + LeftShip + Sunk + Wrecked;

    public static int Away(Point3D a, Point3D b) => Math.Max(Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));

    public static BotSeaVoyage Of(Mobile captain)
    {
        for (var i = 0; i < _afloat.Count; i++)
        {
            if (_afloat[i].Captain == captain)
            {
                return _afloat[i];
            }
        }

        return null;
    }

    public static bool IsShip(Item item) =>
        item is BaseBoatDeed { Deleted: false, MultiId: BotSeaChart.ShipNorth } or BaseDockedBoat { Deleted: false, MultiId: BotSeaChart.ShipNorth };

    public static Item ShipOf(Mobile bot)
    {
        var pack = bot?.Backpack;

        if (pack == null)
        {
            return null;
        }

        return (Item)pack.FindItemByType<BaseDockedBoat>(true, b => b.MultiId == BotSeaChart.ShipNorth)
               ?? pack.FindItemByType<BaseBoatDeed>(true, d => d.MultiId == BotSeaChart.ShipNorth);
    }

    public static void Keep(Mobile bot, Item ship)
    {
        if (bot is BotMobile { Bond: { } bond } && ship != null)
        {
            bond.Items.Add(ship.Serial);
        }
    }

    public static void Unkeep(Mobile bot, Item ship)
    {
        if (bot is BotMobile { Bond: { } bond } && ship != null)
        {
            bond.Items.Remove(ship.Serial);
        }
    }

    public static BotSeaVoyage Embark(BotMobile captain, BotDock from, BotDock to, BotSeaLane lane, IReadOnlyList<Mobile> company, out string why) =>
        Embark(captain, from, from?.First, to, lane, company, out why, out _);

    public static BotSeaVoyage Embark(
        BotMobile captain, BotDock from, BotBerth berth, BotDock to, BotSeaLane lane, IReadOnlyList<Mobile> company, out string why, out BotBerthBlock block
    )
    {
        why = null;
        block = BotBerthBlock.None;

        var map = captain?.Map;

        if (map == null || map == Map.Internal || from == null || berth == null || to == null || lane == null || !captain.Alive)
        {
            why = "nobody to sail, or nowhere to sail to";

            return null;
        }

        var ship = ShipOf(captain);

        if (ship == null)
        {
            Clear(map, berth);
            ship = ShipOf(captain);
        }

        if (ship == null)
        {
            why = "there is no ship in the pack";

            return null;
        }

        if (!captain.InRange(berth.Shore, 2))
        {
            why = $"it is not at the dock in {from.Name}";

            return null;
        }

        if (captain.Region.IsPartOf<HouseRegion>() || BaseBoat.FindBoatAt(captain.Location, map) != null)
        {
            why = "a ship may not be put down from inside a house or from another ship";

            return null;
        }

        var region = Region.Find(berth.Berth, map);

        if (region.IsPartOf<DungeonRegion>() || region.IsPartOf<HouseRegion>() || region.IsPartOf<ChampionSpawnRegion>())
        {
            Refused++;
            block = BotBerthBlock.Multi;
            why = $"the berth at {from.Name} lies where the engine puts no ship";

            return null;
        }

        var occupant = Blocked(map, berth, out block);

        if (occupant != null)
        {
            why = $"the berth at {from.Name} ({berth.Berth.X}, {berth.Berth.Y}) is taken: {occupant}";

            return null;
        }

        switch (ship)
        {
            case BaseBoatDeed deed:
                {
                    deed.OnPlacement(captain, berth.Berth);

                    break;
                }
            case BaseDockedBoat docked:
                {
                    docked.OnPlacement(captain, berth.Berth);

                    break;
                }
        }

        var boat = ship.Deleted ? Placed(captain, berth.Berth, map) : null;

        if (boat == null)
        {
            Refused++;
            why = $"the engine would not put the ship down at ({berth.Berth.X}, {berth.Berth.Y})";

            return null;
        }

        Unkeep(captain, ship);

        if (berth.East && !boat.SetFacing(Direction.East))
        {
            Refused++;
            DockBack(captain, boat);
            why = $"the ship could not be laid east–west at {from.Name}";

            return null;
        }

        var plank = Nearer(boat.PPlank, boat.SPlank, captain.Location);

        if (plank == null || !captain.InRange(plank.GetWorldLocation(), 8))
        {
            Refused++;
            DockBack(captain, boat);
            why = "the plank lies out of reach of the shore";

            return null;
        }

        var key = KeyOf(captain, plank.KeyValue);

        if (plank.Locked)
        {
            key?.UseOn(captain, plank);
        }

        plank.Open();

        captain.Location = new Point3D(plank.X, plank.Y, plank.Z + 3);
        captain.Location = Deck(boat, 0);

        var voyage = new BotSeaVoyage
        {
            Captain = captain,
            Boat = boat,
            From = from,
            Berth = berth,
            To = to,
            LandAt = to,
            Lane = lane,
            Began = Core.TickCount,
            Headway = Core.TickCount,
            State = BotSeaVoyage.Tide.Sailing
        };

        Board(voyage, company);

        plank.Close();

        if (key != null && !plank.Locked)
        {
            key.UseOn(captain, plank);
        }

        boat.RaiseAnchor(false);
        captain.Journey?.Finish();

        voyage.Course = lane.Course(from, berth, out voyage.Sea, out voyage.Harbour);
        voyage.Measure();
        voyage.LimitMs = (long)(lane.Tiles * 2.0 * StrokeMs / Math.Max(1, Speed)) + SlackMs;

        _afloat.Add(voyage);
        Embarked++;
        from.Departures++;
        berth.Departures++;

        var which = from.Berths.IndexOf(berth);

        logger.Information(
            "{Name} the {Class} has put to sea from {From}{Berth} for {To}: {Tiles} tiles in {Legs} legs, {Crew} aboard with it",
            captain.Name,
            captain.Class?.Name,
            from.Name,
            which > 0 ? $" (berth {which + 1}, at ({berth.Berth.X}, {berth.Berth.Y}))" : "",
            to.Name,
            lane.Tiles,
            voyage.Course.Count - 1,
            voyage.Crew.Count
        );

        return voyage;
    }

    public static long TakenByShip { get; private set; }

    public static long TakenByGround { get; private set; }

    public static long TakenByStatic { get; private set; }

    public static long TakenByMulti { get; private set; }

    public static long TakenByItem { get; private set; }

    public static long Cleared { get; private set; }

    public static string Blocked(Map map, BotBerth berth, out BotBerthBlock block)
    {
        var occupant = Ask(map, berth, out block, out var ship);

        if (block == BotBerthBlock.Ship && Idle(ship) && Clear(map, berth))
        {
            occupant = Ask(map, berth, out block, out _);
        }

        return occupant;
    }

    public static void Count(BotBerthBlock block)
    {
        switch (block)
        {
            case BotBerthBlock.Ship:
                {
                    TakenByShip++;

                    break;
                }
            case BotBerthBlock.Ground:
                {
                    TakenByGround++;

                    break;
                }
            case BotBerthBlock.Static:
                {
                    TakenByStatic++;

                    break;
                }
            case BotBerthBlock.Multi:
                {
                    TakenByMulti++;

                    break;
                }
            case BotBerthBlock.Item:
                {
                    TakenByItem++;

                    break;
                }
        }
    }

    private static string Ask(Map map, BotBerth berth, out BotBerthBlock block, out BaseBoat ship) =>
        BotSeaChart.Occupant(map, berth.Berth, false, out block, out ship)
        ?? (berth.East ? BotSeaChart.Occupant(map, berth.Berth, true, out block, out ship) : null);

    public static bool Free(Map map, BotBerth berth) => berth != null && Ask(map, berth, out _, out _) == null;

    private static bool Idle(BaseBoat ship)
    {
        if (ship is not { Deleted: false } || ship.Owner is not BotMobile owner)
        {
            return false;
        }

        var voyage = Of(owner);

        return voyage == null || voyage.Boat != ship;
    }

    public static bool Clear(Map map, BotBerth berth)
    {
        if (map == null || berth == null)
        {
            return false;
        }

        var hull = BotSeaChart.HullEast;
        var reach = Math.Max(hull.Width, hull.Height);
        var bounds = new Rectangle2D(berth.Berth.X - reach, berth.Berth.Y - reach, reach * 2 + 1, reach * 2 + 1);
        List<BaseBoat> idle = null;

        foreach (var multi in map.GetMultisInBounds(bounds))
        {
            if (multi is BaseBoat ship && Idle(ship))
            {
                (idle ??= []).Add(ship);
            }
        }

        if (idle == null)
        {
            return false;
        }

        var cleared = false;

        for (var i = 0; i < idle.Count; i++)
        {
            var ship = idle[i];
            var owner = (BotMobile)ship.Owner;

            Unload(map, ship, berth.Shore);

            if (owner.Deleted)
            {
                ship.Delete();
                Scuttled++;
                cleared = true;
            }
            else if (DryDock(new BotSeaVoyage { Captain = owner, Boat = ship }))
            {
                cleared = true;
            }

            if (ship.Deleted)
            {
                Cleared++;

                logger.Information("Sea: {Owner}'s ship, lying idle at ({X}, {Y}), was cleared out of a berth", owner.Name, ship.X, ship.Y);
            }
        }

        return cleared;
    }

    private static void Unload(Map map, BaseBoat ship, Point3D shore)
    {
        List<Mobile> aboard = null;

        foreach (var entity in ship.GetMovingEntities())
        {
            if (entity is BotMobile or Server.Mobiles.BaseCreature { ControlMaster: BotMobile })
            {
                (aboard ??= []).Add((Mobile)entity);
            }
        }

        if (aboard == null)
        {
            return;
        }

        for (var i = 0; i < aboard.Count; i++)
        {
            aboard[i].MoveToWorld(Beside(map, shore) ?? shore, map);

            if (aboard[i] is BotMobile bot)
            {
                bot.Journey?.Finish();
            }
        }
    }

    private static BaseBoat Placed(Mobile owner, Point3D berth, Map map)
    {
        var boats = BaseBoat.Boats;

        for (var i = boats.Count - 1; i >= 0 && i >= boats.Count - 16; i--)
        {
            var boat = boats[i];

            if (boat is { Deleted: false } && boat.Owner == owner && boat.Map == map && boat.X == berth.X && boat.Y == berth.Y)
            {
                return boat;
            }
        }

        return null;
    }

    private static void Board(BotSeaVoyage voyage, IReadOnlyList<Mobile> company)
    {
        var captain = voyage.Captain;
        var boat = voyage.Boat;
        var spot = 1;

        if (captain.AllFollowers is { } followers)
        {
            foreach (var follower in followers)
            {
                if (follower is { Deleted: false, Alive: true } && follower.Map == captain.Map && follower.InRange(captain.Location, CrewReach))
                {
                    follower.Location = Deck(boat, spot++);
                    voyage.Crew.Add(follower);
                }
            }
        }

        if (company == null)
        {
            return;
        }

        for (var i = 0; i < company.Count; i++)
        {
            var member = company[i];

            if (member is { Deleted: false, Alive: true } && member != captain && member.Map == captain.Map
                && member.InRange(captain.Location, CrewReach) && !voyage.Crew.Contains(member))
            {
                member.Location = Deck(boat, spot++);
                voyage.Crew.Add(member);

                if (member is BotMobile hand)
                {
                    hand.Journey?.Finish();
                }
            }
        }

        CrewCarried += voyage.Crew.Count;
    }

    private static readonly (int X, int Y)[] _deck =
        [(0, 1), (0, -1), (0, 2), (0, -2), (0, 3), (0, -3), (-1, 1), (1, 1), (-1, -1), (1, -1), (-1, 2), (1, 2), (-1, -2), (1, -2)];

    public static Point3D Deck(BaseBoat boat, int i)
    {
        var turns = ((int)boat.Facing & 7) / 2;

        for (var k = 0; k < _deck.Length; k++)
        {
            var (dx, dy) = _deck[(i + k) % _deck.Length];
            var p = boat.Rotate(new Point3D(boat.X + dx, boat.Y + dy, boat.Z), turns);

            if (!OnHull(boat, p.X, p.Y) || At(boat.TillerMan, p) || At(boat.Hold, p) || At(boat.PPlank, p) || At(boat.SPlank, p))
            {
                continue;
            }

            return new Point3D(p.X, p.Y, boat.Z + 3);
        }

        return new Point3D(boat.X, boat.Y, boat.Z + 3);
    }

    private static bool At(Item item, Point3D p) => item != null && item.X == p.X && item.Y == p.Y;

    private static bool OnHull(BaseBoat boat, int x, int y)
    {
        var hull = boat.Components;
        var i = x - boat.X - hull.Min.X;
        var j = y - boat.Y - hull.Min.Y;

        return i >= 0 && j >= 0 && i < hull.Width && j < hull.Height && hull.Tiles[i][j].Length > 0;
    }

    private static Plank Nearer(Plank a, Plank b, Point3D to)
    {
        if (a == null || a.Deleted)
        {
            return b is { Deleted: false } ? b : null;
        }

        if (b == null || b.Deleted)
        {
            return a;
        }

        return Away(a.Location, to) <= Away(b.Location, to) ? a : b;
    }

    private static Key KeyOf(Mobile bot, uint value) =>
        value == 0 ? null : bot.Backpack?.FindItemByType<Key>(true, k => k.KeyValue == value);

    public static void Stroke()
    {
        for (var i = _afloat.Count - 1; i >= 0; i--)
        {
            var voyage = _afloat[i];

            try
            {
                Steer(voyage);
            }
            catch (Exception e)
            {
                logger.Error(e, "Sea: steering {Name}'s ship threw; the voyage is given up", voyage.Captain?.Name);

                Lose(voyage, "the helm threw");
            }

            if (voyage.State is BotSeaVoyage.Tide.Landed or BotSeaVoyage.Tide.Lost)
            {
                _afloat.RemoveAt(i);
            }
        }
    }

    private static void Steer(BotSeaVoyage voyage)
    {
        Strokes++;

        var boat = voyage.Boat;
        var captain = voyage.Captain;

        if (boat == null || boat.Deleted)
        {
            Sunk++;
            Lose(voyage, "the ship is gone");

            return;
        }

        if (captain == null || captain.Deleted)
        {
            Anchor(boat);
            Lose(voyage, "the captain is gone");

            return;
        }

        if (!captain.Alive)
        {
            Died++;
            Anchor(boat);
            Lose(voyage, $"died aboard off ({boat.X}, {boat.Y}); the ship is left at anchor");

            logger.Information(
                "{Name} the {Class} died aboard off ({X}, {Y}) {Minutes:F1} minutes out of {From} for {To}; the ship is left at anchor",
                captain.Name,
                captain.Class?.Name,
                boat.X,
                boat.Y,
                voyage.Minutes,
                voyage.From.Name,
                voyage.To.Name
            );

            return;
        }

        if (captain.Map != boat.Map || !boat.Contains(captain))
        {
            LeftShip++;
            Anchor(boat);
            Lose(voyage, $"it is no longer aboard; the ship is left at anchor off ({boat.X}, {boat.Y})");

            return;
        }

        captain.Refusals = 0;

        for (var i = 0; i < voyage.Crew.Count; i++)
        {
            if (voyage.Crew[i] is BotMobile hand && boat.Contains(hand))
            {
                hand.Refusals = 0;
            }
        }

        if (!voyage.Harried && captain.Aggressors.Count > 0)
        {
            voyage.Harried = true;
            Harried++;
        }

        var now = Core.TickCount;

        if (voyage.State != BotSeaVoyage.Tide.Landing)
        {
            if (now - voyage.Began >= voyage.LimitMs)
            {
                Wreck(voyage, $"still at sea after {voyage.Minutes:F0} minutes");

                return;
            }

            if (now - voyage.Headway >= BecalmedMs)
            {
                Wreck(voyage, $"no headway for {BecalmedMs / 60000} minutes, {voyage.Left(boat.Location)} tiles from {voyage.To.Name}");

                return;
            }
        }

        switch (voyage.State)
        {
            case BotSeaVoyage.Tide.Rerouting:
                {
                    Reroute(voyage);

                    return;
                }
            case BotSeaVoyage.Tide.Landing:
                {
                    Land(voyage);

                    return;
                }
            default:
                {
                    Sail(voyage);

                    return;
                }
        }
    }

    private static void Sail(BotSeaVoyage voyage)
    {
        var boat = voyage.Boat;
        var course = voyage.Course;

        while (voyage.Leg < course.Count && Away(boat.Location, course[voyage.Leg]) == 0)
        {
            voyage.Leg++;
        }

        if (voyage.Leg >= course.Count)
        {
            voyage.State = BotSeaVoyage.Tide.Landing;

            return;
        }

        var left = voyage.Left(boat.Location);

        if (left < voyage.Best)
        {
            voyage.Best = left;
            voyage.Headway = Core.TickCount;
        }

        var waypoint = course[voyage.Leg];

        var lie = voyage.Leg < voyage.Sea ? voyage.Berth?.East ?? voyage.From.East : voyage.Leg >= voyage.Harbour ? voyage.To.East : (bool?)null;

        if (lie is { } east)
        {
            var want = Lying(Utility.GetDirection(boat.Location, waypoint), east, boat.Facing);

            if (want != boat.Facing && Face(boat, want))
            {
                Turns++;

                return;
            }
        }

        var rel = boat.GetMovementFor(waypoint.X, waypoint.Y, out var most);

        if (most <= 0)
        {
            voyage.Leg++;

            return;
        }

        var ahead = Ahead(rel);

        if (lie == null && !ahead)
        {
            var world = (Direction)(((int)rel + (int)boat.Facing) & 7);

            if (Face(boat, Heading(world, boat.Facing)))
            {
                Turns++;

                return;
            }
        }

        if (boat.Move(rel, ahead ? Math.Min(Speed, most) : 1, 0x4, false))
        {
            Moves++;
            voyage.Blocked = 0;

            return;
        }

        Blocks++;
        voyage.Blocked++;

        for (var turn = 1; turn <= 2; turn++)
        {
            if (boat.Move((Direction)(((int)rel + turn) & 7), 1, 0x4, false) || boat.Move((Direction)(((int)rel - turn) & 7), 1, 0x4, false))
            {
                SideSteps++;

                return;
            }
        }

        if (voyage.Blocked < StuckStrokes)
        {
            return;
        }

        Stuck(voyage, rel);
    }

    private static void Stuck(BotSeaVoyage voyage, Direction rel)
    {
        var boat = voyage.Boat;

        voyage.Blocked = 0;

        if (voyage.Leg >= voyage.Harbour || Away(boat.Location, voyage.To.Berth) <= LandReach)
        {
            voyage.State = BotSeaVoyage.Tide.Landing;

            return;
        }

        if (voyage.Leg < voyage.Sea)
        {
            TurnedBack++;
            voyage.LandAt = voyage.From;
            voyage.Why = $"the way out of {voyage.From.Name}'s harbour was blocked";
            voyage.State = BotSeaVoyage.Tide.Landing;

            return;
        }

        if (voyage.Reroutes >= MostReroutes)
        {
            Wreck(voyage, $"blocked at ({boat.X}, {boat.Y}) after {voyage.Reroutes} new courses");

            return;
        }

        var world = ((int)rel + (int)boat.Facing) & 7;
        var ahead = BotSeaChart.CellOf(boat.X + BotSeaChart.DX(world) * 8, boat.Y + BotSeaChart.DY(world) * 8);
        var here = BotSeaChart.CellOf(boat.X, boat.Y);

        BotSeaCourse.Shut(ahead);

        if (!BotSeaChart.IsOpen(here))
        {
            Wreck(voyage, $"blocked at ({boat.X}, {boat.Y}) off the open sea");

            return;
        }

        voyage.Reroutes++;
        Reroutes++;
        voyage.Asking = new BotSeaCourse.Ask { From = here, To = voyage.To.Entry, For = $"{voyage.Captain.Name}'s new course" };
        voyage.State = BotSeaVoyage.Tide.Rerouting;

        BotSeaCourse.Enqueue(voyage.Asking);
    }

    private static void Reroute(BotSeaVoyage voyage)
    {
        var ask = voyage.Asking;

        if (ask == null || !ask.Done)
        {
            return;
        }

        voyage.Asking = null;

        if (!ask.Found)
        {
            Wreck(voyage, $"no way on from ({voyage.Boat.X}, {voyage.Boat.Y})");

            return;
        }

        List<Point3D> course = [];

        for (var i = 0; i < ask.Turns.Count; i++)
        {
            course.Add(BotSeaChart.Middle(ask.Turns[i]));
        }

        var harbour = course.Count;

        for (var i = voyage.Harbour; i < voyage.Course.Count; i++)
        {
            course.Add(voyage.Course[i]);
        }

        voyage.Course = course;
        voyage.Sea = 0;
        voyage.Harbour = harbour;
        voyage.Leg = 0;
        voyage.Best = int.MaxValue;
        voyage.Headway = Core.TickCount;
        voyage.Measure();
        voyage.State = BotSeaVoyage.Tide.Sailing;

        logger.Information(
            "Sea: {Name}'s ship was blocked and sails on by a new course, {Tiles} tiles to {To}",
            voyage.Captain.Name,
            voyage.Left(voyage.Boat.Location),
            voyage.To.Name
        );
    }

    private static void Land(BotSeaVoyage voyage)
    {
        var boat = voyage.Boat;
        var captain = voyage.Captain;
        var map = boat.Map;
        var dock = voyage.LandAt ?? voyage.To;

        Anchor(boat);

        if (!Ashore(map, boat, dock, out var spot))
        {
            Wreck(voyage, $"no shore within {LandReach} tiles of ({boat.X}, {boat.Y}) to step onto");

            return;
        }

        var plank = Nearer(boat.PPlank, boat.SPlank, spot);
        var key = plank != null ? KeyOf(captain, plank.KeyValue) : null;

        if (plank is { Locked: true })
        {
            key?.UseOn(captain, plank);
        }

        plank?.Open();

        captain.Location = spot;
        captain.Journey?.Finish();

        Ashore(voyage, spot);
        DryDock(voyage);

        var home = dock == voyage.From;

        voyage.State = home ? BotSeaVoyage.Tide.Lost : BotSeaVoyage.Tide.Landed;

        if (home)
        {
            logger.Information("{Name} the {Class} put back into {From}: {Why}", captain.Name, captain.Class?.Name, voyage.From.Name, voyage.Why);

            return;
        }

        Arrived++;
        voyage.To.Arrivals++;
        voyage.Lane.Sailed++;

        logger.Information(
            "{Name} the {Class} has sailed from {From} to {To} in {Minutes:F1} minutes and stepped ashore at ({X}, {Y}){Harried}; the ship is {Ship}",
            captain.Name,
            captain.Class?.Name,
            voyage.From.Name,
            voyage.To.Name,
            voyage.Minutes,
            spot.X,
            spot.Y,
            voyage.Harried ? ", fighting on the way" : "",
            boat.Deleted ? "dry-docked in its pack" : "left at anchor"
        );
    }

    private static void Ashore(BotSeaVoyage voyage, Point3D spot)
    {
        var boat = voyage.Boat;
        var map = boat.Map;

        for (var i = 0; i < voyage.Crew.Count; i++)
        {
            var hand = voyage.Crew[i];

            if (hand is not { Deleted: false, Alive: true } || hand.Map != map || !boat.Contains(hand))
            {
                continue;
            }

            hand.Location = Beside(map, spot) ?? spot;

            if (hand is BotMobile bot)
            {
                bot.Journey?.Finish();
            }
        }
    }

    private static bool Ashore(Map map, BaseBoat boat, BotDock dock, out Point3D spot)
    {
        spot = Point3D.Zero;

        var reach = LandReach;

        if (dock != null && Away(boat.Location, dock.Shore) <= reach && map.CanSpawnMobile(dock.Shore.X, dock.Shore.Y, dock.Shore.Z))
        {
            spot = dock.Shore;

            return true;
        }

        for (var r = 1; r <= reach; r++)
        {
            for (var dx = -r; dx <= r; dx++)
            {
                var edge = Math.Abs(dx) == r;

                for (var dy = -r; dy <= r; dy += edge ? 1 : 2 * r)
                {
                    var x = boat.X + dx;
                    var y = boat.Y + dy;

                    if (boat.Contains(x, y) || !BotStep.Settle(map, x, y, out var z) || !map.CanSpawnMobile(x, y, z))
                    {
                        continue;
                    }

                    var p = new Point3D(x, y, z);

                    if (BaseBoat.FindBoatAt(p, map) != null || dock != null && !BotGates.Joined(map, p, dock.Shore))
                    {
                        continue;
                    }

                    spot = p;

                    return true;
                }
            }
        }

        return false;
    }

    private static Point3D? Beside(Map map, Point3D spot)
    {
        for (var r = 1; r <= 3; r++)
        {
            for (var dx = -r; dx <= r; dx++)
            {
                for (var dy = -r; dy <= r; dy++)
                {
                    if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != r)
                    {
                        continue;
                    }

                    var x = spot.X + dx;
                    var y = spot.Y + dy;

                    if (BotStep.Settle(map, x, y, out var z) && map.CanSpawnMobile(x, y, z) && BaseBoat.FindBoatAt(new Point3D(x, y, z), map) == null)
                    {
                        return new Point3D(x, y, z);
                    }
                }
            }
        }

        return null;
    }

    private static bool DryDock(BotSeaVoyage voyage)
    {
        var boat = voyage.Boat;
        var captain = voyage.Captain;

        if (boat == null || boat.Deleted || captain is not { Deleted: false, Alive: true })
        {
            return false;
        }

        Anchor(boat);

        var result = boat.CheckDryDock(captain);

        if (result != BaseBoat.DryDockResult.Valid)
        {
            Moored++;

            logger.Information(
                "Sea: {Name}'s ship could not be dry-docked ({Why}) and is left at anchor at ({X}, {Y})",
                captain.Name,
                result,
                boat.X,
                boat.Y
            );

            return false;
        }

        boat.EndDryDock(captain);
        DryDocked++;

        var docked = ShipOf(captain);

        if (docked == null)
        {
            foreach (var item in captain.Map.GetItemsInRange<BaseDockedBoat>(captain.Location, 1))
            {
                docked = item;

                break;
            }

            if (docked != null)
            {
                captain.Backpack?.DropItem(docked);
            }
        }

        Keep(captain, docked);

        return true;
    }

    private static void DockBack(BotMobile captain, BaseBoat boat)
    {
        DryDock(new BotSeaVoyage { Captain = captain, Boat = boat });
    }

    private static void Anchor(BaseBoat boat)
    {
        if (boat is { Deleted: false, Anchored: false })
        {
            boat.LowerAnchor(false);
        }
    }

    private static void Wreck(BotSeaVoyage voyage, string why)
    {
        var boat = voyage.Boat;
        var captain = voyage.Captain;
        var map = boat.Map;

        Wrecked++;
        Anchor(boat);

        var dock = BotDocks.Nearest(boat.Location) ?? voyage.From;

        for (var i = 0; i < voyage.Crew.Count; i++)
        {
            var hand = voyage.Crew[i];

            if (hand is { Deleted: false, Alive: true } && hand.Map == map && boat.Contains(hand))
            {
                hand.MoveToWorld(Beside(map, dock.Shore) ?? dock.Shore, map);
            }
        }

        captain.MoveToWorld(dock.Shore, map);
        captain.Journey?.Finish();
        Carried++;

        DryDock(voyage);
        Lose(voyage, $"{why}; carried ashore at {dock.Name}");

        logger.Information(
            "{Name} the {Class} was wrecked on the way from {From} to {To}: {Why}; carried ashore at {Dock}, the ship {Ship}",
            captain.Name,
            captain.Class?.Name,
            voyage.From.Name,
            voyage.To.Name,
            why,
            dock.Name,
            boat.Deleted ? "dry-docked in its pack" : "left at anchor"
        );
    }

    private static void Lose(BotSeaVoyage voyage, string why)
    {
        voyage.State = BotSeaVoyage.Tide.Lost;
        voyage.Why ??= why;
    }

    private static Direction Lying(Direction heading, bool east, Direction facing)
    {
        var w = (int)heading & 7;
        var a = east ? 2 : 0;
        var b = east ? 6 : 4;
        var toA = Math.Min((w - a) & 7, (a - w) & 7);
        var toB = Math.Min((w - b) & 7, (b - w) & 7);
        var f = (int)facing & 7;

        if (toA == toB)
        {
            return f == a || f == b ? facing : (Direction)a;
        }

        return (Direction)(toA < toB ? a : b);
    }

    private static bool Face(BaseBoat boat, Direction want)
    {
        var delta = ((int)want - (int)boat.Facing) & 7;

        if (delta == 0)
        {
            return true;
        }

        return boat.Turn(delta == 6 ? -2 : delta, false);
    }

    private static bool Ahead(Direction rel) => ((int)rel & 7) is 0 or 1 or 7;

    private static Direction Heading(Direction world, Direction facing)
    {
        var w = (int)world & 7;

        if ((w & 1) == 0)
        {
            return (Direction)w;
        }

        var f = (int)facing & 7;
        var a = (w + 7) & 7;
        var b = (w + 1) & 7;

        return Math.Min((a - f) & 7, (f - a) & 7) <= Math.Min((b - f) & 7, (f - b) & 7) ? (Direction)a : (Direction)b;
    }

    public static void Recover(Map map)
    {
        var boats = BaseBoat.Boats.ToArray();
        var found = 0;

        for (var i = 0; i < boats.Length; i++)
        {
            var boat = boats[i];

            if (boat == null || boat.Deleted || boat.Map != map || boat.Owner is not BotMobile owner || Of(owner) != null)
            {
                continue;
            }

            found++;

            var dock = BotDocks.Nearest(boat.Location);
            List<Mobile> aboard = [];

            foreach (var entity in boat.GetMovingEntities())
            {
                if (entity is Mobile m)
                {
                    aboard.Add(m);
                }
            }

            for (var j = 0; j < aboard.Count; j++)
            {
                var m = aboard[j];

                if (dock != null && (m is BotMobile || m is Server.Mobiles.BaseCreature { ControlMaster: BotMobile }))
                {
                    m.MoveToWorld(Beside(map, dock.Shore) ?? dock.Shore, map);

                    if (m is BotMobile bot)
                    {
                        bot.Journey?.Finish();
                    }
                }
            }

            if (owner.Deleted)
            {
                boat.Delete();
                Scuttled++;

                continue;
            }

            if (DryDock(new BotSeaVoyage { Captain = owner, Boat = boat }))
            {
                Recovered++;
            }
        }

        if (found > 0)
        {
            logger.Information(
                "Sea: {Found} ships with a bot's name on them were in the water at boot: {Recovered} dry-docked into their owners' packs, {Scuttled} scuttled for want of an owner",
                found,
                Recovered,
                Scuttled
            );
        }
    }

    public static string Describe() =>
        Embarked == 0 && Refused + TakenByShip + TakenByGround + TakenByStatic + TakenByMulti + TakenByItem + Cleared == 0
            ? "no ship has put to sea"
            : $"{Embarked} ships put to sea, {Arrived} arrived, {_afloat.Count} at sea now; {TurnedBack} put back, {Died} captains died aboard, {LeftShip} left their ship, {Sunk} ships gone, {Wrecked} wrecked and {Carried} carried ashore; "
              + $"{DryDocked} dry-docked again, {Moored} left at anchor, {Harried} fought on the way, {CrewCarried} carried as crew; {Strokes} strokes, {Moves} moves, {Turns} turns, {Blocks} blocked, {SideSteps} side-steps, {Reroutes} new courses; "
              + $"{Refused} embarkations refused; berths found taken by a ship {TakenByShip} times, by ground {TakenByGround}, a static {TakenByStatic}, a building {TakenByMulti}, an item {TakenByItem}; {Cleared} idle ships cleared out of a berth; {Recovered} ships recovered and {Scuttled} scuttled";

    public static void Forget()
    {
        _afloat.Clear();
        Embarked = 0;
        Arrived = 0;
        TurnedBack = 0;
        Died = 0;
        LeftShip = 0;
        Sunk = 0;
        Wrecked = 0;
        Carried = 0;
        DryDocked = 0;
        Moored = 0;
        Harried = 0;
        CrewCarried = 0;
        Strokes = 0;
        Moves = 0;
        Blocks = 0;
        SideSteps = 0;
        Turns = 0;
        Reroutes = 0;
        Recovered = 0;
        Scuttled = 0;
        Refused = 0;
        TakenByShip = 0;
        TakenByGround = 0;
        TakenByStatic = 0;
        TakenByMulti = 0;
        TakenByItem = 0;
        Cleared = 0;
    }
}
