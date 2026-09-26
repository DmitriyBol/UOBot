using System;
using System.Collections.Generic;
using Server.Logging;

namespace Server.BotAI.V2;

/// <summary>
/// The training field: where it is, who is standing on it, where each of them stands, and what an hour of
/// being shouted at is actually worth.
///
/// <para>
/// <b>One session at a time, held by one captain, and everybody else derives their place from it.</b> This
/// is the squad's own rule applied to a different problem: the shared facts are the ground, the roster and
/// the order the roster is in, and every student's station falls out of those three identically for
/// everybody who asks. Nobody is told where to stand. Two students cannot be given the same tile, a student
/// that dies and is replaced does not orphan an assignment, and there is no message anywhere.
/// </para>
///
/// <para>
/// <b>The teaching is a rate, not an event, and that is what makes the captain's walking matter.</b> A
/// lesson that granted its points on arrival would be a shop that sells skill, and the captain pacing the
/// ranks would be scenery. Points are handed out per beat, to whoever the captain is near <em>at that
/// beat</em> — so a student at the far corner of the block genuinely learns less than the one he is standing
/// over, and he genuinely has to walk to fix it. See <see cref="Teach"/>.
/// </para>
/// </summary>
public static class BotSchool
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotSchool));

    public static Point3D Ground { get; set; } = new(1479, 1629, 20);

    public static int Pace { get; set; } = 2;

    public static int Rank { get; set; } = 3;

    public static int Most { get; set; } = 10;

    public static int GatherMs { get; set; } = 90000;

    public static int LessonMs { get; set; } = 600000;

    public static int BeatMs { get; set; } = 15000;

    public static int Voice { get; set; } = 4;

    public static double Distant { get; set; } = 0.3;

    public static double Rate { get; set; } = 0.4;

    public static double Floor { get; set; } = 30.0;

    public static double LeastRoom { get; set; } = 0.12;

    public static int Fee { get; set; } = 60;

    public static int FeePerPoint { get; set; } = 20;

    public static double MagicFee { get; set; } = 1.5;

    public static int RestMs { get; set; } = 720000;

    private static readonly Dictionary<Serial, long> _taught = [];

    public static bool Rested(BotMobile student) =>
        student == null
        || !_taught.TryGetValue(student.Serial, out var when)
        || Core.TickCount - when >= RestMs;

    public static void Learned(BotMobile student)
    {
        if (student != null)
        {
            _taught[student.Serial] = Core.TickCount;
        }
    }

    public static long Rested_Away { get; internal set; }

    public static long Sessions { get; private set; }

    public static long Empty { get; private set; }

    public static long Taught { get; private set; }

    public static double Points { get; private set; }

    public static long Fees { get; private set; }

    public static long Nothing { get; private set; }

    public static long Levelled { get; private set; }

    public static long Shouted { get; private set; }

    public static long Beats { get; private set; }

    public static BotMobile Master { get; private set; }

    public static bool Gathering { get; private set; }

    private static long _openedTick;

    private static bool _everOpened;

    public static int Left =>
        Gathering && _everOpened ? Math.Max(0, GatherMs - (int)(Core.TickCount - _openedTick)) : 0;

    private static readonly List<BotMobile> _students = [];

    private static readonly Dictionary<Serial, long> _coming = [];

    public static int PromiseMs { get; set; } = 30000;

    public static void Promise(BotMobile student)
    {
        if (student != null && Gathering && !_students.Contains(student))
        {
            _coming[student.Serial] = Core.TickCount;
        }
    }

    public static int Coming(BotMobile except)
    {
        var count = 0;
        var now = Core.TickCount;

        _lapsed.Clear();

        foreach (var (serial, tick) in _coming)
        {
            if (now - tick >= PromiseMs)
            {
                _lapsed.Add(serial);
            }
            else if (except == null || serial != except.Serial)
            {
                count++;
            }
        }

        for (var i = 0; i < _lapsed.Count; i++)
        {
            _coming.Remove(_lapsed[i]);
        }

        return count;
    }

    private static readonly List<Serial> _lapsed = [];

    public static IReadOnlyList<BotMobile> Students => _students;

    public static Point3D Standing(Map map)
    {
        if (_settled || map == null || map == Map.Internal)
        {
            return Ground;
        }

        _settled = true;

        if (!BotStep.Settle(map, Ground.X, Ground.Y, out var z))
        {
            logger.Warning(
                "The training field at ({X}, {Y}) is not ground a bot can stand on at all; it is left as ordered",
                Ground.X,
                Ground.Y
            );

            return Ground;
        }

        if (z == Ground.Z)
        {
            logger.Information("The training field at ({X}, {Y}, {Z}) is standable as ordered", Ground.X, Ground.Y, Ground.Z);

            return Ground;
        }

        logger.Information(
            "The training field was ordered at ({X}, {Y}, {Was}) but a bot stands there at {Is}; the field is moved to where feet actually go",
            Ground.X,
            Ground.Y,
            Ground.Z,
            z
        );

        Ground = new Point3D(Ground.X, Ground.Y, z);

        return Ground;
    }

    private static bool _settled;

    public static bool Open(BotMobile master)
    {
        if (master is not { Deleted: false, Alive: true })
        {
            return false;
        }

        if (Master is { Deleted: false } && !ReferenceEquals(Master, master))
        {
            return false;
        }

        Master = master;
        Gathering = true;
        _openedTick = Core.TickCount;
        _everOpened = true;
        _students.Clear();
        _coming.Clear();

        Sessions++;

        logger.Information(
            "{Name} has opened the training field at ({X}, {Y}) and is calling for warriors and archers",
            master.Name,
            Ground.X,
            Ground.Y
        );

        return true;
    }

    public static void Begin()
    {
        Gathering = false;

        if (_students.Count == 0)
        {
            Empty++;
        }
    }

    public static void Close()
    {
        Master = null;
        Gathering = false;
        _students.Clear();
        _coming.Clear();
    }

    public static bool Teachable(BotMobile student) => Teachable(Master, student);

    public static bool Teachable(BotMobile master, BotMobile student) =>
        student is { Deleted: false, Alive: true }
        && Suits(master, student.Class)
        && !ReferenceEquals(student, master)
        && Lacking(master, student) != null;

    public static bool Suits(BotMobile master, BotClass klass)
    {
        if (klass == null || master?.Class == null)
        {
            return false;
        }

        if (klass.Leads || klass.Tutors)
        {
            return false;
        }

        return master.Class.Tutors
            ? klass.Role is BotRole.Caster or BotRole.Medic
            : klass.Role is BotRole.Melee or BotRole.Ranged;
    }

    public static SkillName? Lacking(BotMobile student) => Lacking(Master, student);

    public static SkillName? Lacking(BotMobile master, BotMobile student)
    {
        var klass = student?.Class;

        if (master is not { Deleted: false } || klass == null)
        {
            return null;
        }

        var teachable = master.Class?.Skills;

        if (teachable == null)
        {
            return null;
        }

        SkillName? chosen = null;
        var gap = 0.0;
        var chosenTrade = false;

        for (var i = 0; i < teachable.Count; i++)
        {
            var skill = teachable[i].Skill;

            if (!klass.Wants(skill))
            {
                continue;
            }

            var behind = master.Skills[skill].Base - student.Skills[skill].Base;

            if (behind < 0.1)
            {
                continue;
            }

            var trade = Trade(student, skill);

            if (chosen == null || (trade && !chosenTrade) || (trade == chosenTrade && behind > gap))
            {
                chosen = skill;
                gap = behind;
                chosenTrade = trade;
            }
        }

        return chosen;
    }

    private static bool Trade(BotMobile student, SkillName skill) =>
        student.Bond?.Weapon?.Skill == skill || student.Class?.MainSkill == skill;

    public static int Bill(BotMobile student) => Bill(Master, student);

    public static int Bill(BotMobile master, BotMobile student)
    {
        var skill = Lacking(master, student);

        if (skill == null)
        {
            return 0;
        }

        var held = Math.Max(Floor, student.Skills[skill.Value].Base);

        var rate = master.Class is { Tutors: true } ? MagicFee : 1.0;

        return (int)((Fee + (held - Floor) * FeePerPoint) * rate);
    }

    public static bool Enrol(BotMobile student)
    {
        if (!Gathering || _students.Count >= Most || student == null || _students.Contains(student))
        {
            return false;
        }

        _students.Add(student);
        _coming.Remove(student.Serial);

        _students.Sort(static (a, b) => a.Serial.Value.CompareTo(b.Serial.Value));

        return true;
    }

    public static void Leave(BotMobile student)
    {
        if (student == null)
        {
            return;
        }

        _students.Remove(student);
        _coming.Remove(student.Serial);
    }

    public static bool Holds(BotMobile student) => _students.Contains(student);

    public static Point3D Station(BotMobile student)
    {
        var index = _students.IndexOf(student);

        return index < 0 ? Ground : Station(index, _students.Count);
    }

    public static Point3D Station(int index, int count)
    {
        var ranks = Math.Max(1, (count + Rank - 1) / Rank);

        var row = index / Rank;
        var column = index % Rank;

        var wide = Math.Min(Rank, count - row * Rank);

        var x = Ground.X + (column - (wide - 1) / 2.0) * Pace;
        var y = Ground.Y + (row - (ranks - 1) / 2.0) * Pace;

        return new Point3D((int)Math.Round(x), (int)Math.Round(y), Ground.Z);
    }

    public static Point3D Post(int turn, int count)
    {
        var ranks = Math.Max(1, (count + Rank - 1) / Rank);

        var wide = Math.Min(Math.Max(1, count), Rank);

        var out_x = (int)Math.Round((wide - 1) / 2.0 * Pace) + Pace;
        var out_y = (int)Math.Round((ranks - 1) / 2.0 * Pace) + Pace;

        var (dx, dy) = (turn % 8) switch
        {
            0 => (-out_x, -out_y),
            1 => (0, -out_y),
            2 => (out_x, -out_y),
            3 => (out_x, 0),
            4 => (out_x, out_y),
            5 => (0, out_y),
            6 => (-out_x, out_y),
            _ => (-out_x, 0)
        };

        return new Point3D(Ground.X + dx, Ground.Y + dy, Ground.Z);
    }

    public static Point3D Post(Map map, int turn, int count)
    {
        for (var i = 0; i < 8; i++)
        {
            var post = Post(turn + i, count);

            if (map == null || map == Map.Internal)
            {
                return post;
            }

            if (BotFooting.Footless(map, post.X, post.Y) || !BotStep.Settle(map, post.X, post.Y, out var z))
            {
                continue;
            }

            return new Point3D(post.X, post.Y, z);
        }

        return Post(turn, count);
    }

    public static double Teach(BotMobile student)
    {
        var master = Master;

        if (master is not { Deleted: false, Alive: true } || student is not { Deleted: false, Alive: true })
        {
            return 0.0;
        }

        var which = Lacking(student);

        if (which == null)
        {
            Nothing++;

            return 0.0;
        }

        var skill = student.Skills[which.Value];
        var ceiling = master.Skills[which.Value].Base;

        if (skill.Base >= ceiling)
        {
            Levelled++;

            return 0.0;
        }

        var span = Math.Max(1.0, ceiling - Floor);
        var room = Math.Clamp((ceiling - skill.Base) / span, LeastRoom, 1.0);

        var near = master.InRange(student.Location, Voice);
        var attention = near ? 1.0 : Distant;

        if (!near)
        {
            Shouted++;
        }

        var gain = Math.Min(Rate * room * attention, ceiling - skill.Base);

        if (gain <= 0.0)
        {
            Levelled++;

            return 0.0;
        }

        Beats++;

        skill.Base += gain;

        Points += gain;

        student.Resolve.Urges.Paid(gain * BotYield.GoldPerSkillPoint);

        return gain;
    }

    public static void Paid(int fee)
    {
        Fees += fee;
        Taught++;
    }

    public static string Describe() =>
        Sessions == 0
            ? "no class has ever been held"
            : $"{Sessions} classes held at ({Ground.X}, {Ground.Y}), {Empty} of them with nobody; {Taught} lessons paid for at {Fees}gp against {BotScout.Wages}gp paid back out in wages, {Points:F1} points handed out over {Beats} beats that taught something ({Nothing} found nothing left to teach, {Levelled} found the student level with the master, {Shouted} taught out of earshot)"
              + (Master is { Deleted: false } ? $"; {Master.Name} is on the field now with {_students.Count}" : "; the field is empty");

    public static void Forget()
    {
        Close();

        Sessions = 0;
        Empty = 0;
        Taught = 0;
        Points = 0.0;
        Fees = 0;
        Nothing = 0;
        Levelled = 0;
        Shouted = 0;
        Beats = 0;
    }
}
