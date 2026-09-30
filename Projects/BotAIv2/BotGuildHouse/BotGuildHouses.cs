using System;
using System.Collections.Generic;
using Server.Guilds;
using Server.Items;
using Server.Logging;
using Server.Multis;

namespace Server.BotAI.V2;

/// <summary>
/// The guilds' houses in the towns: which guild lives in which empty building, the door it put into the doorway and the sign
/// it hung beside it, and the one price — Britain's.
///
/// <para>
/// <b>Patrick's idea of 29.09.2026, evening: "one or two guilds may settle in each of the other towns, but only in an empty
/// and suitable building; when a guild settles there, a door is put in and a sign is hung on the wall. There may be guilds in
/// Britain, but settling there costs 10,000 gold; that counts as the guild's home. They still have to raise a hall and take
/// ground."</b> Until now a guild's seat was a bare point: set by hand, in the file, where its hall stood, or — for a guild
/// founded after the morning of 29.09 — a random spot within forty tiles of another town's middle (<c>BotSeat.Choose</c>).
/// Now it is a building the survey found empty (<see cref="BotHouseSurvey"/>): the guild's seat is the building's heart,
/// its members are born and rise there and live in its town, and the hall and the claims still have to be earned the old way.
/// </para>
///
/// <para>
/// <b>One notion of home, not two.</b> Nothing here answers "where does this bot live"; it answers "where is this guild
/// seated", and three existing questions ask it: <c>BotSeat.Of</c> (the seat the hall is looked for from and carried to),
/// <c>BotSeat.Home</c> (where a member is born, rises and walks home to — the house before the hall) and
/// <c>BotRelocate.Forced</c> (the town a member lives in — Britain too, when the house is there).
/// </para>
///
/// <para>
/// <b>Who goes where.</b> Each guild wishes once, kept across restarts: abroad (by <c>BotSeat.AbroadShare</c>, the order of
/// 29.09.2026 that some guilds settle by other towns) or at home in Britain. A guild already seated at a bare point by
/// another town wishes abroad. Abroad, a guild takes the free building nearest the town's bank in the town with the fewest
/// guild houses, among the towns the gates join to home that are fit to live in (<c>BotSettle</c>: a road, a bank, shops),
/// at most <see cref="MostPerTown"/> a town, for <see cref="TownPrice"/> (nothing, by default: the building stood empty). In
/// Britain a guild waits for its hall (<see cref="BritainAfterHall"/>), then saves <see cref="BritainPrice"/>, which is levied
/// off its chest and members like a hall (<c>BotEstate.Levy</c>) and paid to the city's treasury (<c>BotCity.Tax</c>), so
/// the coin goes round rather than out of the world. A guild with no house and no seat lives as it did: at the population's
/// home, its hall looked for from there.
/// </para>
///
/// <para>
/// <b>The world is the record of what stands; the store is the record of whose.</b> The doors and signs are items and the
/// world save keeps them; <see cref="BotGuildHouseStore"/> keeps which guild holds which building (by the tile of its first
/// door) and each guild's wish. At every boot the survey is read, every house is found again and its door and sign put back
/// if the world lost them, and a door or sign of ours standing in a doorway nobody holds is taken down. A guild gone from
/// the roll for <see cref="GoneLooks"/> looks in a row leaves its house: the door and sign come down and the building is
/// empty again.
/// </para>
/// </summary>
public static class BotGuildHouses
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotGuildHouses));

    public static bool Running { get; set; } = true;

    public static int MostPerTown { get; set; } = 2;

    public static int MostInBritain { get; set; } = 3;

    public static int TownPrice { get; set; } = 0;

    public static int BritainPrice { get; set; } = 10000;

    public static bool BritainAfterHall { get; set; } = true;

    public static bool FitTownsOnly { get; set; } = true;

    public static bool Rehouse { get; set; } = true;

    public static int LookMs { get; set; } = 60000;

    public static int GoneLooks { get; set; } = 2;

    public static int RecountMs { get; set; } = 600000;

    public static int Spread { get; set; } = 3;

    /// <summary>What one guild holds: the building, where its seat is, and what it paid.</summary>
    public sealed class Holding
    {
        public string Guild;

        public string Town;

        public Point3D Key;

        public Point3D Heart;

        public bool Britain;

        public int Paid;

        public DateTime Since;

        internal BotHouseSurvey.Building Building;

        internal int Gone;
    }

    private static readonly Dictionary<string, Holding> _held = new(StringComparer.OrdinalIgnoreCase);

    private static readonly Dictionary<string, bool> _britain = new(StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> _pinned = new(StringComparer.OrdinalIgnoreCase);

    private static readonly Dictionary<string, long> _said = new(StringComparer.OrdinalIgnoreCase);

    private static readonly Dictionary<string, int> _free = new(StringComparer.OrdinalIgnoreCase);

    private static readonly Dictionary<string, int> _why = new(StringComparer.Ordinal);

    private static bool _reconciled;

    private static long _nextLook;

    private static long _nextCount;

    public static long Settled { get; private set; }

    public static long SettledBritain { get; private set; }

    public static long PaidGold { get; private set; }

    public static long Vacated { get; private set; }

    public static long DoorsPut { get; private set; }

    public static long SignsHung { get; private set; }

    public static long Turned { get; private set; }

    public static long NoRoom { get; private set; }

    public static long AwaitingHall { get; private set; }

    public static long SavingBritain { get; private set; }

    public static long BritainFull { get; private set; }

    public static long NoBuilding { get; private set; }

    public static long Short { get; private set; }

    public static long Blocked { get; private set; }

    public static int ReadBack { get; private set; }

    public static int PutBack { get; private set; }

    public static int Orphans { get; private set; }

    public static bool Started { get; internal set; }

    public static bool Active => Running && Started && BotHouseSurvey.Ready;

    public static Holding Of(string guild) => Running && Started && guild != null ? _held.GetValueOrDefault(guild) : null;

    public static IEnumerable<Holding> All => _held.Values;

    public static Point3D Seat(string guild) => Of(guild)?.Heart ?? Point3D.Zero;

    public static bool Held(Guild guild) => Of(guild?.Name) != null;

    public static bool IsHeart(Point3D at)
    {
        if (!Running || !Started || _held.Count == 0)
        {
            return false;
        }

        foreach (var held in _held.Values)
        {
            if (held.Heart == at)
            {
                return true;
            }
        }

        return false;
    }

    public static BotTowns.Town TownOf(Guild guild) => Of(guild?.Name) is { } held && BotTowns.Surveyed ? BotTowns.Find(held.Town) : null;

    public static int Saving(Guild guild)
    {
        if (!Running || !Started || guild == null || _held.ContainsKey(guild.Name) || !_britain.TryGetValue(guild.Name, out var britain) || !britain)
        {
            return 0;
        }

        return BritainAfterHall && BotEstate.Running && BotEstate.Hall(guild) == null ? 0 : BritainPrice;
    }

    public static void Founded(string guild)
    {
        if (guild != null)
        {
            _britain.Remove(guild);
            _pinned.Remove(guild);
        }
    }

    public static void Pin(string guild)
    {
        if (!Running || string.IsNullOrEmpty(guild) || !_pinned.Add(guild))
        {
            return;
        }

        if (_held.TryGetValue(guild, out var held))
        {
            held.Building ??= BotHouseSurvey.ByDoor(held.Key);
            Vacate(held, BotPopulation.Home, "its seat was set by hand", true);
        }
    }

    public static bool MayOpen(BotGuildDoor door, Mobile from)
    {
        if (from.AccessLevel >= AccessLevel.GameMaster)
        {
            return true;
        }

        if (string.Equals((from.Guild as Guild)?.Name, door.Guild, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var held = Of(door.Guild);

        return held == null || held.Building != null && held.Building.Holds(from.X, from.Y);
    }

    public static void Refused(Mobile from) => Turned++;

    internal static void Tick(Map map)
    {
        if (!Running || !Started || map == null || map == Map.Internal)
        {
            return;
        }

        if (!BotHouseSurvey.Ready)
        {
            BotHouseSurvey.Step(map);

            return;
        }

        var now = Core.TickCount;

        if (!_reconciled)
        {
            _reconciled = true;
            Reconcile(map);
            Count(map);
            _nextCount = now + RecountMs;
            _nextLook = now;

            return;
        }

        if (now - _nextCount >= 0)
        {
            _nextCount = now + RecountMs;
            Count(map);
        }

        if (now - _nextLook < 0)
        {
            return;
        }

        _nextLook = now + Math.Max(5000, LookMs);
        Look(map);
    }

    private static void Reconcile(Map map)
    {
        ReadBack = 0;
        PutBack = 0;
        Orphans = 0;

        foreach (var held in new List<Holding>(_held.Values))
        {
            var building = BotHouseSurvey.ByDoor(held.Key);

            if (building == null)
            {
                logger.Warning(
                    "Guild houses: the house of {Guild} in {Town} at {X},{Y} is not among the surveyed buildings any more; it is emptied",
                    held.Guild,
                    held.Town,
                    held.Key.X,
                    held.Key.Y
                );

                Vacate(held, map, "its building is gone from the survey", false);

                continue;
            }

            held.Building = building;
            held.Heart = building.Heart;
            ReadBack++;
            PutBack += Hang(held, map);

            if (BotTowns.Find(held.Town) is { } town)
            {
                town.Seated++;
            }
        }

        for (var i = 0; i < BotHouseSurvey.All.Count; i++)
        {
            var building = BotHouseSurvey.All[i];
            string holder = null;

            foreach (var held in _held.Values)
            {
                if (held.Building == building)
                {
                    holder = held.Guild;

                    break;
                }
            }

            Orphans += TakeDown(building, map, holder);
        }

        logger.Information(
            "Guild houses: {Houses} read back from the save ({List}); {PutBack} doors and signs put back that the world had lost, {Orphans} of nobody's taken down",
            ReadBack,
            ReadBack == 0 ? "none" : Tell(),
            PutBack,
            Orphans
        );
    }

    private static void Count(Map map)
    {
        _free.Clear();
        _why.Clear();

        for (var i = 0; i < BotHouseSurvey.All.Count; i++)
        {
            var building = BotHouseSurvey.All[i];

            if (HolderOf(building) != null)
            {
                _why["held by a guild"] = _why.GetValueOrDefault("held by a guild") + 1;

                continue;
            }

            var why = BotHouseSurvey.Occupied(building);

            if (why == null)
            {
                _free[building.Town] = _free.GetValueOrDefault(building.Town) + 1;

                continue;
            }

            var cut = why.IndexOf(" (", StringComparison.Ordinal);
            var kind = cut > 0 ? why[..cut] : why.Contains(" lives in it", StringComparison.Ordinal) ? "somebody lives in it" : why;

            _why[kind] = _why.GetValueOrDefault(kind) + 1;
        }
    }

    public static int Free(string town) => town == null ? 0 : _free.GetValueOrDefault(town);

    public static int Houses(string town)
    {
        var many = 0;

        foreach (var held in _held.Values)
        {
            if (string.Equals(held.Town, town, StringComparison.OrdinalIgnoreCase))
            {
                many++;
            }
        }

        return many;
    }

    private static string HolderOf(BotHouseSurvey.Building building)
    {
        foreach (var held in _held.Values)
        {
            if (held.Building == building || held.Building == null && held.Key == building.Door)
            {
                return held.Guild;
            }
        }

        return null;
    }

    private static void Look(Map map)
    {
        if (!BotTowns.Ready)
        {
            return;
        }

        foreach (var held in new List<Holding>(_held.Values))
        {
            var guild = BotGuilds.Find(held.Guild);

            if (guild != null && Members(guild) > 0)
            {
                held.Gone = 0;

                continue;
            }

            if (++held.Gone >= Math.Max(1, GoneLooks))
            {
                Vacate(held, map, "the guild is gone", true);
            }
        }

        foreach (var guild in new List<Guild>(BotGuilds.Standing))
        {
            if (guild is not { Disbanded: false } || BotUnderworld.Band(guild) || Members(guild) == 0 || _held.ContainsKey(guild.Name)
                || _pinned.Contains(guild.Name))
            {
                continue;
            }

            if (Wish(guild))
            {
                Britain(guild, map);
            }
            else
            {
                Abroad(guild, map);
            }
        }
    }

    private static int Members(Guild guild)
    {
        var many = 0;

        for (var i = 0; i < guild.Members.Count; i++)
        {
            if (guild.Members[i] is BotMobile { Deleted: false })
            {
                many++;
            }
        }

        return many;
    }

    private static bool Wish(Guild guild)
    {
        if (_britain.TryGetValue(guild.Name, out var britain))
        {
            return britain;
        }

        var bare = BotSeat.Bare(guild.Name);

        britain = bare != Point3D.Zero
            ? BotTowns.Nearest(bare) is { Home: true }
            : Utility.RandomDouble() >= BotSeat.AbroadShare;

        _britain[guild.Name] = britain;

        logger.Information(
            "{Guild} will settle {Where}",
            guild.Name,
            britain
                ? $"in Britain, for {BritainPrice}gp{(BritainAfterHall ? " once its hall stands" : "")}"
                : bare != Point3D.Zero
                    ? $"in an empty building by its seat at {bare.X},{bare.Y}, or in another town with room"
                    : "in an empty building of another town"
        );

        return britain;
    }

    private static void Abroad(Guild guild, Map map)
    {
        var bare = BotSeat.Bare(guild.Name);
        var own = bare != Point3D.Zero ? BotTowns.Nearest(bare) : null;
        var hall = BotEstate.Hall(guild);

        if (own is { Home: true } && hall != null)
        {
            _britain[guild.Name] = true;
            logger.Information("{Guild} raised its hall by Britain before a building abroad was free; it will settle in Britain, for {Price}gp", guild.Name, BritainPrice);

            return;
        }

        if (bare != Point3D.Zero && own is null or { Home: true })
        {
            return;
        }

        var towns = new List<BotTowns.Town>();

        for (var i = 0; i < BotTowns.All.Count; i++)
        {
            var town = BotTowns.All[i];

            if (!Fit(town) || town.Home || ReferenceEquals(town, BotSettle.HomeTown) || Houses(town.Name) >= MostPerTown)
            {
                continue;
            }

            if (Free(town.Name) == 0)
            {
                continue;
            }

            if (own != null && !ReferenceEquals(town, own))
            {
                continue;
            }

            towns.Add(town);
        }

        var home = BotPopulation.Where;

        towns.Sort(
            (a, b) =>
            {
                if (ReferenceEquals(a, own) != ReferenceEquals(b, own))
                {
                    return ReferenceEquals(a, own) ? -1 : 1;
                }

                var byHouses = Houses(a.Name).CompareTo(Houses(b.Name));

                return byHouses != 0 ? byHouses : BotSeat.Gap(a.Square, home).CompareTo(BotSeat.Gap(b.Square, home));
            }
        );

        for (var i = 0; i < towns.Count; i++)
        {
            var building = Pick(towns[i], guild, hall);

            if (building != null && Settle(guild, building, towns[i], false, TownPrice, map))
            {
                return;
            }
        }

        NoRoom++;
        Say(guild, "finds no free building in a town fit to live in with room for another guild house");
    }

    private static void Britain(Guild guild, Map map)
    {
        var britain = BotSettle.HomeTown ?? HomeTown();

        if (britain == null)
        {
            return;
        }

        var hall = BotEstate.Hall(guild);

        if (BritainAfterHall && BotEstate.Running && hall == null)
        {
            AwaitingHall++;

            return;
        }

        if (Houses(britain.Name) >= MostInBritain)
        {
            BritainFull++;
            Say(guild, $"has no room in Britain: {MostInBritain} guild houses stand there");

            return;
        }

        if (BotEstate.Fund(guild) < BritainPrice)
        {
            SavingBritain++;

            return;
        }

        var building = Pick(britain, guild, hall);

        if (building == null)
        {
            NoBuilding++;
            Say(guild, "has the price of a house in Britain and finds no free building there");

            return;
        }

        Settle(guild, building, britain, true, BritainPrice, map);
    }

    private static BotTowns.Town HomeTown()
    {
        for (var i = 0; i < BotTowns.All.Count; i++)
        {
            if (BotTowns.All[i].Home)
            {
                return BotTowns.All[i];
            }
        }

        return null;
    }

    private static bool Fit(BotTowns.Town town)
    {
        if (!town.FromHome)
        {
            return false;
        }

        if (BotPlot.Mainland && BotPopulation.Home is { } map
            && (!BotGates.Ready || BotGates.LandOf(map, town.Square) != BotGates.LandOf(map, BotPopulation.Where)))
        {
            return false;
        }

        if (!FitTownsOnly || !BotResidence.Active || !BotSettle.Reckoned)
        {
            return true;
        }

        return BotSettle.Of(town) is { Fit: true };
    }

    private static BotHouseSurvey.Building Pick(BotTowns.Town town, Guild guild, BaseHouse hall)
    {
        var near = hall is { Deleted: false } ? hall.Location : BotSettle.Hearth(town);
        var found = new List<BotHouseSurvey.Building>();

        for (var i = 0; i < BotHouseSurvey.All.Count; i++)
        {
            var building = BotHouseSurvey.All[i];

            if (string.Equals(building.Town, town.Name, StringComparison.OrdinalIgnoreCase) && HolderOf(building) == null)
            {
                found.Add(building);
            }
        }

        found.Sort((a, b) => BotSeat.Gap(a.Heart, near).CompareTo(BotSeat.Gap(b.Heart, near)));

        for (var i = 0; i < found.Count; i++)
        {
            if (BotHouseSurvey.Occupied(found[i], guild.Name) == null)
            {
                return found[i];
            }
        }

        return null;
    }

    private static bool Settle(Guild guild, BotHouseSurvey.Building building, BotTowns.Town town, bool britain, int price, Map map)
    {
        for (var i = 0; i < building.Entrances.Length; i++)
        {
            var doors = building.Entrances[i].Doors;

            for (var j = 0; j < doors.Length; j++)
            {
                if (!map.CanFit(doors[j].X, doors[j].Y, doors[j].Z, 16, false, false))
                {
                    Blocked++;

                    return false;
                }
            }
        }

        if (price > 0)
        {
            var paid = new List<BotEstate.Contribution>();
            var got = BotEstate.Levy(guild, price, paid);

            if (got < price)
            {
                BotEstate.Refund(paid);
                Short++;

                return false;
            }

            BotCity.Tax(price);
            PaidGold += price;
        }

        var held = new Holding
        {
            Guild = guild.Name,
            Town = town.Name,
            Key = building.Door,
            Heart = building.Heart,
            Britain = britain,
            Paid = price,
            Since = Core.Now,
            Building = building
        };

        _held[guild.Name] = held;
        town.Seated++;
        Hang(held, map);
        Settled++;

        if (britain)
        {
            SettledBritain++;
        }

        _free[town.Name] = Math.Max(0, _free.GetValueOrDefault(town.Name) - 1);

        var door = building.Entrances[0].Doors[0];

        logger.Information(
            "{Guild} settles in {Town}: the empty building at {X},{Y} ({Area} tiles of floor, {Ways} way{S} in) is its house now — a door put into the doorway at {DX},{DY} and its sign hung beside it; {Paid}; its {Members} members live in {Town} from now on, born and risen there",
            guild.Name,
            town.Name,
            building.Heart.X,
            building.Heart.Y,
            building.Area,
            building.Entrances.Length,
            building.Entrances.Length == 1 ? "" : "s",
            door.X,
            door.Y,
            price > 0 ? $"{price}gp paid to the city out of its chest and members" : "nothing paid, the building stood empty",
            Members(guild),
            town.Name
        );

        BotEvents.Post("house", guild.Leader, $"{guild.Name} settles in {town.Name} at {building.Heart.X},{building.Heart.Y}{(price > 0 ? $" for {price}gp" : "")}");

        return true;
    }

    private static void Vacate(Holding held, Map map, string why, bool counted)
    {
        _held.Remove(held.Guild);

        if (held.Building != null)
        {
            TakeDown(held.Building, map, null);
        }
        else if (map != null)
        {
            var gone = new List<Item>();

            foreach (var door in map.GetItemsInRange<BotGuildDoor>(held.Key, 3))
            {
                if (string.Equals(door.Guild, held.Guild, StringComparison.OrdinalIgnoreCase))
                {
                    gone.Add(door);
                }
            }

            foreach (var sign in map.GetItemsInRange<BotGuildSign>(held.Key, 4))
            {
                if (string.Equals(sign.Guild, held.Guild, StringComparison.OrdinalIgnoreCase))
                {
                    gone.Add(sign);
                }
            }

            for (var i = 0; i < gone.Count; i++)
            {
                gone[i].Delete();
            }
        }

        if (BotTowns.Surveyed && BotTowns.Find(held.Town) is { } town)
        {
            town.Seated = Math.Max(0, town.Seated - 1);
        }

        if (held.Town != null)
        {
            _free[held.Town] = _free.GetValueOrDefault(held.Town) + 1;
        }

        if (counted)
        {
            Vacated++;
        }

        logger.Information("{Guild} leaves its house in {Town} at {X},{Y}: {Why}; the door and the sign are taken down and the building stands empty", held.Guild, held.Town, held.Heart.X, held.Heart.Y, why);
    }

    private static int Hang(Holding held, Map map)
    {
        var building = held.Building;

        if (building == null || map == null)
        {
            return 0;
        }

        var put = 0;

        for (var i = 0; i < building.Entrances.Length; i++)
        {
            var way = building.Entrances[i];

            if (Doors(way, map, held.Guild) == way.Doors.Length)
            {
                continue;
            }

            Remove(way, map);
            put += PutDoors(way, map, held.Guild);
        }

        if (Sign(building, map, held.Guild) == null)
        {
            put += HangSign(held, building, map);
        }

        return put;
    }

    private static int Doors(BotHouseSurvey.Entrance way, Map map, string guild)
    {
        var many = 0;

        foreach (var door in map.GetItemsInRange<BotGuildDoor>(way.Doors[0], 3))
        {
            if (door.Deleted || !string.Equals(door.Guild, guild, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var at = door.Open ? new Point3D(door.X - door.Offset.X, door.Y - door.Offset.Y, door.Z) : door.Location;

            for (var j = 0; j < way.Doors.Length; j++)
            {
                if (way.Doors[j].X == at.X && way.Doors[j].Y == at.Y)
                {
                    many++;
                }
            }
        }

        return many;
    }

    private static int Remove(BotHouseSurvey.Entrance way, Map map, string sparing = null)
    {
        List<BotGuildDoor> gone = null;

        foreach (var door in map.GetItemsInRange<BotGuildDoor>(way.Doors[0], 3))
        {
            if (door.Deleted || sparing != null && string.Equals(door.Guild, sparing, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var at = door.Open ? new Point3D(door.X - door.Offset.X, door.Y - door.Offset.Y, door.Z) : door.Location;

            for (var j = 0; j < way.Doors.Length; j++)
            {
                if (way.Doors[j].X == at.X && way.Doors[j].Y == at.Y)
                {
                    (gone ??= []).Add(door);

                    break;
                }
            }
        }

        if (gone == null)
        {
            return 0;
        }

        for (var i = 0; i < gone.Count; i++)
        {
            gone[i].Delete();
        }

        return gone.Count;
    }

    private static int PutDoors(BotHouseSurvey.Entrance way, Map map, string guild)
    {
        if (way.Doors.Length == 1)
        {
            var door = new BotGuildDoor(way.AlongX ? DoorFacing.WestCW : DoorFacing.SouthCW, guild);

            door.MoveToWorld(way.Doors[0], map);
            DoorsPut++;

            return 1;
        }

        var first = new BotGuildDoor(way.AlongX ? DoorFacing.WestCW : DoorFacing.NorthCCW, guild);
        var second = new BotGuildDoor(way.AlongX ? DoorFacing.EastCCW : DoorFacing.SouthCW, guild);

        first.MoveToWorld(way.Doors[0], map);
        second.MoveToWorld(way.Doors[1], map);
        first.Link = second;
        second.Link = first;
        DoorsPut += 2;

        return 2;
    }

    private static (Point3D At, SignFacing Facing) SignSpot(BotHouseSurvey.Building building)
    {
        var way = building.Entrances[0];
        var door = way.Doors[0];

        return way.AlongX
            ? (new Point3D(door.X - 1, door.Y + way.OutY, door.Z), SignFacing.West)
            : (new Point3D(door.X + way.OutX, door.Y - 1, door.Z), SignFacing.North);
    }

    private static BotGuildSign Sign(BotHouseSurvey.Building building, Map map, string guild)
    {
        var (at, _) = SignSpot(building);

        foreach (var sign in map.GetItemsInRange<BotGuildSign>(at, 0))
        {
            if (!sign.Deleted && string.Equals(sign.Guild, guild, StringComparison.OrdinalIgnoreCase))
            {
                return sign;
            }
        }

        return null;
    }

    private static int HangSign(Holding held, BotHouseSurvey.Building building, Map map)
    {
        var (at, facing) = SignSpot(building);
        var guild = BotGuilds.Find(held.Guild);
        var sign = new BotGuildSign(held.Britain ? SignType.BrassSign : SignType.WoodenSign, facing, held.Guild)
        {
            Name = guild?.Abbreviation is { Length: > 0 } tag ? $"{held.Guild} [{tag}]" : held.Guild
        };

        sign.MoveToWorld(at, map);
        SignsHung++;

        return 1;
    }

    private static int TakeDown(BotHouseSurvey.Building building, Map map, string holder)
    {
        if (map == null)
        {
            return 0;
        }

        var gone = 0;

        for (var i = 0; i < building.Entrances.Length; i++)
        {
            gone += Remove(building.Entrances[i], map, holder);
        }

        var (at, _) = SignSpot(building);
        List<BotGuildSign> signs = null;

        foreach (var sign in map.GetItemsInRange<BotGuildSign>(at, 1))
        {
            if (!sign.Deleted && (holder == null || !string.Equals(sign.Guild, holder, StringComparison.OrdinalIgnoreCase)))
            {
                (signs ??= []).Add(sign);
            }
        }

        if (signs != null)
        {
            for (var i = 0; i < signs.Count; i++)
            {
                signs[i].Delete();
            }

            gone += signs.Count;
        }

        return gone;
    }

    private static void Say(Guild guild, string what)
    {
        var now = Core.TickCount;

        if (_said.TryGetValue(guild.Name, out var last) && now - last < 3600000)
        {
            return;
        }

        _said[guild.Name] = now;
        logger.Information("{Guild} {What}", guild.Name, what);
    }

    public static int Wipe()
    {
        var map = BotPopulation.Home;
        var gone = _held.Count;

        foreach (var held in new List<Holding>(_held.Values))
        {
            held.Building ??= BotHouseSurvey.ByDoor(held.Key);
            Vacate(held, map, "the world is reset", false);
        }

        _held.Clear();
        _britain.Clear();
        _pinned.Clear();
        _said.Clear();

        return gone;
    }

    internal static void Save(IGenericWriter writer)
    {
        writer.WriteEncodedInt(_held.Count);

        foreach (var held in _held.Values)
        {
            writer.Write(held.Guild);
            writer.Write(held.Town);
            writer.Write(held.Key);
            writer.Write(held.Heart);
            writer.Write(held.Britain);
            writer.WriteEncodedInt(held.Paid);
            writer.Write(held.Since);
        }

        writer.WriteEncodedInt(_britain.Count);

        foreach (var (guild, britain) in _britain)
        {
            writer.Write(guild);
            writer.Write(britain);
        }

        writer.WriteEncodedInt(_pinned.Count);

        foreach (var guild in _pinned)
        {
            writer.Write(guild);
        }
    }

    internal static int Load(IGenericReader reader)
    {
        _held.Clear();
        _britain.Clear();

        var houses = reader.ReadEncodedInt();

        for (var i = 0; i < houses; i++)
        {
            var held = new Holding
            {
                Guild = reader.ReadString(),
                Town = reader.ReadString(),
                Key = reader.ReadPoint3D(),
                Heart = reader.ReadPoint3D(),
                Britain = reader.ReadBool(),
                Paid = reader.ReadEncodedInt(),
                Since = reader.ReadDateTime()
            };

            if (!string.IsNullOrEmpty(held.Guild))
            {
                _held[held.Guild] = held;
            }
        }

        var wishes = reader.ReadEncodedInt();

        for (var i = 0; i < wishes; i++)
        {
            var guild = reader.ReadString();
            var britain = reader.ReadBool();

            if (!string.IsNullOrEmpty(guild))
            {
                _britain[guild] = britain;
            }
        }

        _pinned.Clear();

        var pinned = reader.ReadEncodedInt();

        for (var i = 0; i < pinned; i++)
        {
            var guild = reader.ReadString();

            if (!string.IsNullOrEmpty(guild))
            {
                _pinned.Add(guild);
            }
        }

        return _held.Count;
    }

    public static string Tell()
    {
        if (_held.Count == 0)
        {
            return "no guild has a house";
        }

        using var say = Server.Text.ValueStringBuilder.Create(256);

        foreach (var held in _held.Values)
        {
            say.Append(say.Length > 0 ? ", " : "");
            say.Append($"{held.Guild} in {held.Town} at {held.Heart.X},{held.Heart.Y}{(held.Britain ? $" (its home, bought for {held.Paid}gp)" : "")}");
        }

        return say.ToString();
    }

    private static string Waiting()
    {
        using var say = Server.Text.ValueStringBuilder.Create(256);

        foreach (var guild in BotGuilds.Standing)
        {
            if (guild is not { Disbanded: false } || BotUnderworld.Band(guild) || _held.ContainsKey(guild.Name))
            {
                continue;
            }

            say.Append(say.Length > 0 ? ", " : "");
            say.Append(guild.Name);

            if (_pinned.Contains(guild.Name))
            {
                say.Append(" keeps the seat set by hand");

                continue;
            }

            if (!_britain.TryGetValue(guild.Name, out var britain))
            {
                say.Append(" has not chosen yet");

                continue;
            }

            if (!britain)
            {
                say.Append(" wants a building abroad and has found none free");

                continue;
            }

            if (BritainAfterHall && BotEstate.Running && BotEstate.Hall(guild) == null)
            {
                say.Append($" wants Britain once its hall stands ({BotEstate.Fund(guild)} of the hall's {BotEstate.Price}gp)");

                continue;
            }

            say.Append($" saves for Britain ({BotEstate.Fund(guild)} of {BritainPrice}gp)");
        }

        return say.Length == 0 ? "every guild has a house" : say.ToString();
    }

    public static string Describe()
    {
        if (!Running)
        {
            return "guilds do not settle in town buildings";
        }

        if (!BotHouseSurvey.Ready)
        {
            return $"{Tell()}; {BotHouseSurvey.Describe()}";
        }

        using var free = Server.Text.ValueStringBuilder.Create(256);

        foreach (var (town, many) in _free)
        {
            free.Append(free.Length > 0 ? ", " : "");
            free.Append($"{town} {many}");
        }

        using var why = Server.Text.ValueStringBuilder.Create(256);

        foreach (var (kind, many) in _why)
        {
            why.Append(why.Length > 0 ? ", " : "");
            why.Append($"{many} {kind}");
        }

        return $"{Tell()}; waiting: {Waiting()}; {BotHouseSurvey.Describe()}, free now: {(free.Length == 0 ? "none" : free.ToString())} (not free: {(why.Length == 0 ? "none" : why.ToString())}); "
               + $"at most {MostPerTown} a town and {MostInBritain} in Britain, a town house {(TownPrice > 0 ? $"{TownPrice}gp" : "free")}, Britain {BritainPrice}gp{(BritainAfterHall ? " after the hall" : "")}; "
               + $"{Settled} settled ({SettledBritain} in Britain, {PaidGold}gp paid to the city), {Vacated} left, {DoorsPut} doors put in and {SignsHung} signs hung, {Turned} strangers turned away at a guild's door; "
               + $"looks: {NoRoom} found no free building abroad, {AwaitingHall} waited for the hall before Britain, {SavingBritain} were saving for Britain, {BritainFull} found Britain full, {NoBuilding} found no free building in Britain, {Short} levies fell short, {Blocked} doorways were blocked";
    }

    public static void Forget()
    {
        Settled = 0;
        SettledBritain = 0;
        PaidGold = 0;
        Vacated = 0;
        DoorsPut = 0;
        SignsHung = 0;
        Turned = 0;
        NoRoom = 0;
        AwaitingHall = 0;
        SavingBritain = 0;
        BritainFull = 0;
        NoBuilding = 0;
        Short = 0;
        Blocked = 0;
        _reconciled = false;
        Started = false;
        _free.Clear();
        _why.Clear();
        _said.Clear();
    }
}
