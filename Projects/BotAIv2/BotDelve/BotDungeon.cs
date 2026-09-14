using System;
using System.Collections.Generic;
using Server.Engines.Spawners;
using Server.Logging;
using Server.Mobiles;

namespace Server.BotAI.V2;

/// <summary>
/// The dungeons of this era: where each one is, what rooms it has, and how hard it is.
///
/// <para>
/// <b>Patrick's order of 11.09.2026: the places at the bottom of the map are meant to be dungeons.</b>
/// They have been standing in the world since it was generated — twelve of them, six hundred-odd spawners
/// between them — and nothing on this shard had ever been inside one. They are cut into their own block of
/// the map, from x 5120 eastward, with no walkable road from the island at all: the engine's own way in is
/// a teleporter at an entrance, and a bot that plans a walk to one gets "no way through" as many times as
/// it is asked.
/// </para>
///
/// <para>
/// <b>The rooms are read out of the world rather than written down here, and that is the difference
/// between a table and a survey.</b> Every dungeon in the save carries spawners, and a spawner's own
/// location is a tile the engine itself has decided a creature can stand on. So the rooms of a dungeon are
/// its spawners — guaranteed to be real floor, guaranteed to be where the fighting is, and guaranteed to
/// still be right if somebody re-imports the spawn files tomorrow. A hand-typed list of coordinates would
/// be a promise about a map, and this project has paid for that kind of promise before.
/// </para>
///
/// <para>
/// <b>And the difficulty is measured, not asserted.</b> Patrick's order names the reason — a demon is
/// plainly not a harpy — and the shard already has the arithmetic for it: <see cref="BotThreat.Power"/>,
/// health times what a thing hits for, which is the same number the hunt and the muster weigh a fight by.
/// One of each creature a dungeon's spawners name is built, measured and destroyed at once, exactly as
/// <c>BotHarness</c> measures armour. Nothing here decides what those numbers mean; that is
/// <see cref="BotDelver"/>'s business.
/// </para>
///
/// <para>
/// <b>The boxes are the one hand-written thing, and they are drawn tight on purpose.</b> Each is the
/// cluster its own spawn file actually occupies, with the neighbours checked — Shame ends before Ice
/// begins, the Orc Caves before Terathan Keep — because two boxes that overlap would hand one dungeon the
/// other's rooms and quietly report a lizard den as a drake pit.
/// </para>
/// </summary>
public static class BotDungeon
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotDungeon));

    public static int Margin { get; set; } = 32;

    public sealed class Deep
    {
        public Deep(string name, int x1, int y1, int x2, int y2)
        {
            Name = name;
            Bounds = new Rectangle2D(x1, y1, x2 - x1 + 1, y2 - y1 + 1);
        }

        public string Name { get; }

        public Rectangle2D Bounds { get; }

        public List<Point3D> Rooms { get; } = [];

        public List<string> Kinds { get; } = [];

        public double Power { get; set; }

        public double Worst { get; set; }

        public int Heads { get; set; }

        public long Runs;

        public Point2D Middle => new(Bounds.X + Bounds.Width / 2, Bounds.Y + Bounds.Height / 2);

        public bool Ready => Rooms.Count > 0 && Power > 0.0;

        public bool Holds(Point3D where) =>
            where.X >= Bounds.Start.X - Margin
            && where.X < Bounds.End.X + Margin
            && where.Y >= Bounds.Start.Y - Margin
            && where.Y < Bounds.End.Y + Margin;

        public override string ToString() => $"{Name} ({Rooms.Count} rooms, {Power:F0} each, worst {Worst:F0})";
    }

    private static readonly Deep[] _deeps =
    [
        new("the Orc Caves", 5280, 1295, 5365, 1385),
        new("Shame", 5380, 5, 5630, 240),
        new("Deceit", 5130, 520, 5350, 760),
        new("Despise", 5380, 515, 5615, 1005),
        new("Wrong", 5650, 520, 5865, 600),
        new("Covetous", 5380, 1790, 5625, 2045),
        new("Ice", 5660, 130, 5875, 370),
        new("Khaldun", 5385, 1285, 5615, 1495),
        new("Terathan Keep", 5125, 1545, 5370, 1765),
        new("Fire", 5630, 1285, 5875, 1480),
        new("Destard", 5130, 770, 5360, 1015),
        new("Hythloth", 5900, 15, 6135, 245)
    ];

    public static IReadOnlyList<Deep> All => _deeps;

    public static bool Surveyed { get; private set; }

    public static int Rooms { get; private set; }

    public static int Measured { get; private set; }

    public static int Unknown { get; private set; }

    private static readonly Dictionary<string, double> _might = [];

    public static void Survey(Map map)
    {
        if (Surveyed || map == null || map == Map.Internal)
        {
            return;
        }

        Surveyed = true;

        for (var i = 0; i < _deeps.Length; i++)
        {
            Read(map, _deeps[i]);
        }

        for (var i = 0; i < _deeps.Length; i++)
        {
            var deep = _deeps[i];

            logger.Information(
                "{Deep} surveyed: {Rooms} rooms around ({X}, {Y}), {Heads} creatures of {Kinds} kinds, {Power} of strength each and {Worst} at worst",
                deep.Name,
                deep.Rooms.Count,
                deep.Middle.X,
                deep.Middle.Y,
                deep.Heads,
                deep.Kinds.Count,
                deep.Power.ToString("F0"),
                deep.Worst.ToString("F0")
            );
        }
    }

    private static void Read(Map map, Deep deep)
    {
        var total = 0.0;
        var counted = 0;

        foreach (var spawner in map.GetItemsInBounds<BaseSpawner>(deep.Bounds))
        {
            if (spawner is not { Deleted: false } || spawner.Entries == null)
            {
                continue;
            }

            deep.Rooms.Add(spawner.Location);
            Rooms++;

            for (var i = 0; i < spawner.Entries.Count; i++)
            {
                var named = spawner.Entries[i]?.SpawnedName;

                if (string.IsNullOrWhiteSpace(named))
                {
                    continue;
                }

                var might = Might(named);

                if (might <= 0.0)
                {
                    continue;
                }

                if (!deep.Kinds.Contains(named))
                {
                    deep.Kinds.Add(named);
                }

                deep.Heads += Math.Max(1, spawner.Entries[i].SpawnedMaxCount);
                total += might;
                counted++;

                if (might > deep.Worst)
                {
                    deep.Worst = might;
                }
            }
        }

        deep.Power = counted == 0 ? 0.0 : total / counted;
    }

    public static double Might(string named)
    {
        if (string.IsNullOrWhiteSpace(named))
        {
            return 0.0;
        }

        if (_might.TryGetValue(named, out var known))
        {
            return known;
        }

        var might = 0.0;
        var kind = AssemblyHandler.FindTypeByName(named);

        if (kind == null || !typeof(BaseCreature).IsAssignableFrom(kind))
        {
            Unknown++;
            _might[named] = 0.0;

            return 0.0;
        }

        BaseCreature made = null;

        try
        {
            made = Activator.CreateInstance(kind) as BaseCreature;

            if (made != null)
            {
                might = BotThreat.Power(made);
                Measured++;
            }
        }
        catch (Exception)
        {
            Unknown++;
        }
        finally
        {
            made?.Delete();
        }

        _might[named] = might;

        return might;
    }

    public static Deep Find(string name)
    {
        for (var i = 0; i < _deeps.Length; i++)
        {
            if (string.Equals(_deeps[i].Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return _deeps[i];
            }
        }

        return null;
    }

    public static Point3D Room(Map map, Deep deep, int which)
    {
        if (map == null || deep == null || deep.Rooms.Count == 0)
        {
            return Point3D.Zero;
        }

        var room = deep.Rooms[Math.Abs(which) % deep.Rooms.Count];

        return BotStep.Settle(map, room.X, room.Y, out var z)
            ? new Point3D(room.X, room.Y, z)
            : room;
    }

    public static string Describe()
    {
        if (!Surveyed)
        {
            return "the dungeons have not been surveyed";
        }

        var ready = 0;

        for (var i = 0; i < _deeps.Length; i++)
        {
            if (_deeps[i].Ready)
            {
                ready++;
            }
        }

        return $"{ready} of {_deeps.Length} dungeons surveyed, {Rooms} rooms between them, "
            + $"{Measured} kinds of creature measured and {Unknown} the engine would not build";
    }

    public static void Forget()
    {
        for (var i = 0; i < _deeps.Length; i++)
        {
            _deeps[i].Rooms.Clear();
            _deeps[i].Kinds.Clear();
            _deeps[i].Power = 0.0;
            _deeps[i].Worst = 0.0;
            _deeps[i].Heads = 0;
            _deeps[i].Runs = 0;
        }

        _might.Clear();
        Surveyed = false;
        Rooms = 0;
        Measured = 0;
        Unknown = 0;
    }
}
