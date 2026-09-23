using System;
using System.Collections.Generic;
using Server.Guilds;
using Server.Logging;
using Server.Multis;

namespace Server.BotAI.V2;

/// <summary>
/// Where each guild lives: the point its hall is raised near, its members are born at and rise again at,
/// and the place "home" means to them.
///
/// <para>
/// <b>Patrick's order of 13.09.2026: the bots all live by the graveyard; he wants them south, east and
/// west as well, and one guild moved to the south.</b> The reason they were all there is arithmetic, not
/// preference. The population has one home, 1440,1470, and every bot that dies is put back on its feet
/// there — 3,597 times in the twelve hours before this was written, every one of them onto the same
/// patch. The plot search spirals outward from that same point and Britain's town and the graveyard's
/// belt shut off everything south and west of it, so the four halls went up in the one strip of ground
/// left, sixty tiles north, with their yards on top of each other. Every hall touching every other is
/// what the border drift was written to sour, and it did: that strip is where the wars came from.
/// </para>
///
/// <para>
/// <b>A seat is one point per guild and everything else follows from it.</b> The plot search starts at
/// the seat; a member is born and revived beside the guild's hall, or at the seat until it has one; the
/// homeward walk goes there; and a hall standing further than <see cref="Settled"/> from its seat is
/// carried to it by the same errand that carries a beaten guild out of the winner's yard. So "move a
/// guild south" is one line in <c>bot-estate.json</c> or one word to Argus, and the guild does the rest.
/// </para>
///
/// <para>
/// <b>Configured, overridden by hand, and the hand's word is kept across restarts.</b> The file is the
/// design; the store is what somebody said at the keyboard on a live shard, which would otherwise be lost
/// at the next start and have to be said again. The store only ever holds overrides.
/// </para>
/// </summary>
public static class BotSeat
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotSeat));

    public static bool Running { get; set; } = true;

    public static int Settled { get; set; } = 120;

    public static int MoveEveryMs { get; set; } = 1800000;

    public static int Spread { get; set; } = 6;

    public static long Homed { get; private set; }

    public static long Spoken { get; private set; }

    public static long Carried { get; private set; }

    private static readonly Dictionary<string, Point3D> _configured = [];

    private static readonly Dictionary<string, Point3D> _overrides = [];

    private static readonly Dictionary<string, long> _moved = [];

    public static void Configure(Dictionary<string, int[]> seats)
    {
        _configured.Clear();

        if (seats == null)
        {
            return;
        }

        foreach (var (name, at) in seats)
        {
            if (string.IsNullOrWhiteSpace(name) || at is not { Length: >= 2 })
            {
                continue;
            }

            var seat = new Point3D(at[0], at[1], at.Length > 2 ? at[2] : 0);

            if (TooNear(name, seat, out var other, out var gap))
            {
                Crowded++;

                logger.Warning(
                    "The file seats {Guild} at {X},{Y}, {Gap} tiles from the seat of {Other}, nearer than the {Neighbouring} at which guilds sour on each other; that seat is ignored",
                    name,
                    seat.X,
                    seat.Y,
                    gap,
                    other,
                    BotRegard.Neighbouring
                );

                continue;
            }

            _configured[name] = seat;
        }
    }

    public static long Crowded { get; private set; }

    public static bool TooNear(string guild, Point3D at, out string other, out int gap)
    {
        other = null;
        gap = int.MaxValue;

        foreach (var name in Named())
        {
            if (string.Equals(name, guild, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var seat = Of(name);

            if (seat == Point3D.Zero)
            {
                continue;
            }

            var apart = Gap(at, seat);

            if (apart < BotRegard.Neighbouring && apart < gap)
            {
                other = name;
                gap = apart;
            }
        }

        return other != null;
    }

    private static List<string> Named()
    {
        var names = new List<string>();

        foreach (var name in _overrides.Keys)
        {
            names.Add(name);
        }

        foreach (var name in _configured.Keys)
        {
            if (!names.Contains(name))
            {
                names.Add(name);
            }
        }

        foreach (var name in BotEstate.Held.Keys)
        {
            if (!names.Contains(name))
            {
                names.Add(name);
            }
        }

        return names;
    }

    public static Point3D Of(string guild)
    {
        if (!Running || guild == null)
        {
            return Point3D.Zero;
        }

        if (_overrides.TryGetValue(guild, out var over))
        {
            return over;
        }

        if (_configured.TryGetValue(guild, out var set))
        {
            return set;
        }

        return BotEstate.Held.TryGetValue(guild, out var hall) && hall is { Deleted: false }
            ? hall.Location
            : Point3D.Zero;
    }

    public static Point3D Of(Guild guild) => Of(guild?.Name);

    public static Point3D Home(BotMobile bot)
    {
        var where = BotPopulation.Where;

        if (!Running || bot?.Guild is not Guild guild)
        {
            return where;
        }

        if (BotUnderworld.Member(bot) && BotUnderworld.Hideout != Point3D.Zero)
        {
            return BotUnderworld.Hideout;
        }

        if (BotEstate.Hall(guild) is { Deleted: false } hall && hall.Map == BotPopulation.Home)
        {
            return hall.BanLocation;
        }

        var seat = Of(guild.Name);

        if (seat == Point3D.Zero || BotPopulation.Home == null)
        {
            return where;
        }

        if (!BotStep.Settle(BotPopulation.Home, seat.X, seat.Y, out var z))
        {
            return where;
        }

        return new Point3D(seat.X, seat.Y, z);
    }

    public static void Placed() => Homed++;

    public static bool Moving(Guild guild, out BaseHouse hall, out Point3D seat)
    {
        hall = null;
        seat = Point3D.Zero;

        if (!Running || guild == null)
        {
            return false;
        }

        hall = BotEstate.Hall(guild);
        seat = Of(guild.Name);

        if (hall is not { Deleted: false } || seat == Point3D.Zero)
        {
            return false;
        }

        if (Gap(hall.Location, seat) <= Settled)
        {
            return false;
        }

        return !_moved.TryGetValue(guild.Name, out var last) || Core.TickCount - (last + MoveEveryMs) >= 0;
    }

    public static int Gap(Point3D a, Point3D b) => Math.Max(Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));

    public static void Moved(string guild, bool carried)
    {
        if (guild == null)
        {
            return;
        }

        _moved[guild] = Core.TickCount;

        if (carried)
        {
            Carried++;
        }
    }

    public static void Set(string guild, Point3D at) => Set(guild, at, true);

    public static void Set(string guild, Point3D at, bool byHand)
    {
        if (string.IsNullOrEmpty(guild))
        {
            return;
        }

        _overrides[guild] = at;
        _moved.Remove(guild);

        if (byHand)
        {
            Spoken++;

            logger.Warning("The seat of {Guild} is now {X},{Y}; its hall will be carried there if it stands further than {Settled} tiles off", guild, at.X, at.Y, Settled);

            return;
        }

        logger.Information("The seat of {Guild} follows its hall to {X},{Y}", guild, at.X, at.Y);
    }

    public static void Restore(string guild, Point3D at)
    {
        if (!string.IsNullOrEmpty(guild))
        {
            _overrides[guild] = at;
        }
    }

    public static IEnumerable<KeyValuePair<string, Point3D>> Overrides => _overrides;

    public static string Tell()
    {
        using var say = Server.Text.ValueStringBuilder.Create(128);

        foreach (var guild in BotGuilds.Standing)
        {
            var seat = Of(guild.Name);

            if (say.Length > 0)
            {
                say.Append(", ");
            }

            say.Append(guild.Name);
            say.Append(' ');

            if (seat == Point3D.Zero)
            {
                say.Append("has no seat");

                continue;
            }

            say.Append($"at {seat.X},{seat.Y}");

            if (_overrides.ContainsKey(guild.Name))
            {
                say.Append(" (overriding the file)");
            }

            if (BotEstate.Hall(guild) is { Deleted: false } hall)
            {
                say.Append($", hall {Gap(hall.Location, seat)} off");
            }
        }

        return say.Length == 0 ? "no guilds stand" : say.ToString();
    }

    public static int RoadlessSeats { get; private set; }

    public static bool Roadless(Point3D seat, out string why)
    {
        why = null;

        var map = BotPopulation.Home;

        if (map == null || seat == Point3D.Zero || !BotRoads.Ready || !BotRoads.Covers(map, seat.X, seat.Y))
        {
            return false;
        }

        if (BotRoads.FromHome(map, seat.X, seat.Y) >= 0)
        {
            return false;
        }

        why = $"no road reaches ({seat.X}, {seat.Y}) from home at ({BotRoads.Home.X}, {BotRoads.Home.Y})";

        return true;
    }

    public static void Audit()
    {
        RoadlessSeats = 0;

        foreach (var guild in BotGuilds.Standing)
        {
            var seat = Of(guild.Name);

            if (!Roadless(seat, out var why))
            {
                continue;
            }

            RoadlessSeats++;

            logger.Warning(
                "The seat of {Guild} at {X},{Y} is off the roads: {Why}; bots put down there are carried home from it. Move it with \"seat <guild> <x> <y>\" at the door",
                guild.Name,
                seat.X,
                seat.Y,
                why
            );
        }
    }

    public static string Describe() =>
        !Running
            ? "guilds have no seats"
            : $"seats: {Tell()}; a hall is at home within {Settled} tiles of its seat; {Homed} bots put down at their guild's hall or seat, {Carried} halls carried to a seat, {Spoken} seats set by hand, {RoadlessSeats} seats off the roads at the last audit";

    public static void Forget()
    {
        Homed = 0;
        Carried = 0;
        Spoken = 0;
        _moved.Clear();
    }

    public static int Wipe()
    {
        var gone = _overrides.Count;

        _overrides.Clear();
        Forget();

        return gone;
    }
}

/// <summary>
/// Keeps the seats that were set by hand across restarts. Only those: the file is read every start.
/// </summary>
public sealed class BotSeatStore : GenericPersistence
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotSeatStore));

    private const int Shape = 1;

    private const int Oldest = 1;

    private static BotSeatStore _store;

    public static void Configure() => _store ??= new BotSeatStore();

    public BotSeatStore() : base("BotSeats", 15)
    {
    }

    public override void Serialize(IGenericWriter writer)
    {
        writer.WriteEncodedInt(Shape);

        var many = 0;

        foreach (var _ in BotSeat.Overrides)
        {
            many++;
        }

        writer.WriteEncodedInt(many);

        foreach (var (guild, at) in BotSeat.Overrides)
        {
            writer.Write(guild);
            writer.WriteEncodedInt(at.X);
            writer.WriteEncodedInt(at.Y);
            writer.WriteEncodedInt(at.Z);
        }
    }

    public override void Deserialize(IGenericReader reader)
    {
        var shape = reader.ReadEncodedInt();

        if (shape < Oldest || shape > Shape)
        {
            logger.Warning(
                "The saved seats are shape {Found} and this build reads {Oldest} to {Wanted}; they cannot be read, and the shard will stop on the engine's own prompt until Saves/BotSeats/BotSeats.bin is deleted",
                shape,
                Oldest,
                Shape
            );

            return;
        }

        var many = reader.ReadEncodedInt();

        for (var i = 0; i < many; i++)
        {
            var guild = reader.ReadString();
            var x = reader.ReadEncodedInt();
            var y = reader.ReadEncodedInt();
            var z = reader.ReadEncodedInt();

            BotSeat.Restore(guild, new Point3D(x, y, z));
        }

        if (many > 0)
        {
            logger.Information("{Many} seats set by hand read back off the save", many);
        }
    }
}
