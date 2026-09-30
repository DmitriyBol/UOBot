using System;
using System.Collections.Generic;
using Server.Items;
using Server.Logging;
using Server.Mobiles;
using Server.Text;

namespace Server.BotAI.V2;

/// <summary>
/// Argus's proving ground in Green Acres: who is fought against what, what each fight measured, and the strength a bot is
/// judged by once it has been measured.
///
/// <para>
/// <b>Patrick's order of 26.09.2026, the fifth and the largest of the night:</b> the bots dress, train and grow stronger,
/// and nobody goes past the Orc Caves; Argus is to spawn a copy of a bot and a creature on the green and judge the bot's
/// strength, with the skills, the weapon's properties "and everything else" counted — "it is time for a new branch of
/// judging what a bot can do". One fight at a time is <see cref="BotTrial"/>; this is the schedule, the record and the
/// reading of it.
/// </para>
///
/// <para>
/// <b>What a bot is tried against is the dungeons' own ladder.</b> The delve already ranks every dungeon by its worst
/// inhabitant (<see cref="BotDungeon"/>) and sends a band where it is half again the strength of that one creature. So the
/// rungs here are exactly those creatures, one per dungeon, weakest first: a bot is tried at the lowest rung it has not
/// beaten, climbs one rung when it wins, and is tried again when its build moves (<see cref="BotBuild"/>) or its reading
/// grows old. A fight against the creature a decision is actually about is worth more than any number of fights against
/// creatures it is not, and a dozen rungs is a ladder a population of fifty can climb in an evening.
/// </para>
///
/// <para>
/// <b>What it changes: the delve reads proven strength instead of the formula</b> (<see cref="Against"/>, when
/// <see cref="Judges"/>). A bot measured against a dungeon's worst creature is counted at what it proved there; one
/// measured only against other rungs at what it proved on the nearest, discounted when that is a rung below
/// (<see cref="Reach"/>); one never measured at the old formula scaled by what the ground has found that formula to be
/// worth across everybody measured (<see cref="Calibration"/>). Nothing is vetoed by the absence of a measurement.
/// </para>
///
/// <para>
/// <b>The ground is Argus's, not the population's.</b> The double is a <see cref="BotStandIn"/>, which nothing in the
/// population can see; the creature and both corpses are deleted when the fight ends, so no gold, no loot and no skill
/// leaves the field; a double or a creature that outlived a stop is swept off at boot (<see cref="Sweep"/>).
/// </para>
/// </summary>
public static class BotProving
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotProving));

    public static bool Running { get; set; } = true;

    public static bool Judges { get; set; } = true;

    public static int Rings { get; set; } = 2;

    public static int TickMs { get; set; } = 200;

    public static int CapMs { get; set; } = 180000;

    public static int StillMs { get; set; } = 45000;

    public static int PauseMs { get; set; } = 5000;

    public static int StepMs { get; set; } = 200;

    public static int TraceRanged { get; set; } = 3;

    public static int RetestMs { get; set; } = 5400000;

    public static double RMax { get; set; } = 5.0;

    public static double CalibrateUnder { get; set; } = 3.0;

    public static int CalibrateLeast { get; set; } = 5;

    public static double CalibrateLow { get; set; } = 0.25;

    public static double CalibrateHigh { get; set; } = 4.0;

    public static double NetFloor { get; set; } = 0.02;

    public static double ManaShare { get; set; } = 0.1;

    public static double Pass { get; set; } = 1.0;

    public static double Reach { get; set; } = 0.75;

    public static int UnfitMs { get; set; } = 600000;

    public static int Apart { get; set; } = 3;

    public static int Leash { get; set; } = 12;

    public static int Keep { get; set; } = 4;

    public static int SkillDrift { get; set; } = 5;

    public static double SkillDriftShare { get; set; } = 0.06;

    public static int StatDrift { get; set; } = 5;

    public static int ArmourDrift { get; set; } = 4;

    public static int DamageDrift { get; set; } = 2;

    public static readonly Point3D[] Middles =
    [
        new(5510, 1150, 0), new(5550, 1150, 0), new(5510, 1190, 0), new(5550, 1190, 0)
    ];

    public static int PartyRings { get; set; } = 1;

    public static int PartySize { get; set; } = 5;

    public static int PartyMultiple { get; set; } = 2;

    public static int PartyLeast { get; set; } = 3;

    public static int MostFoes { get; set; } = 6;

    public static int RoomCapMs { get; set; } = 300000;

    public static int RoomValidMs { get; set; } = 7200000;

    public static int RoomRetryMs { get; set; } = 1800000;

    public static bool RoomGate { get; set; } = true;

    public static long Rooms { get; private set; }

    public static long RoomsCleared { get; private set; }

    public static long RoomsFailed { get; private set; }

    /// <summary>One guild's company against one dungeon's worst room: the last time, and how it went.</summary>
    private sealed class RoomMark
    {
        public bool Cleared;

        public int Deaths;

        public double Killed;

        public double Spent;

        public DateTime When;

        public string Line;
    }

    private static readonly Dictionary<(string Guild, string Deep), RoomMark> _rooms = [];

    private static readonly BotRoomTrial[] _roomRings = new BotRoomTrial[Middles.Length];

    private static readonly Dictionary<string, DateTime> _roomTried = new(StringComparer.Ordinal);

    public static bool RoomCleared(string guild, BotDungeon.Deep deep) =>
        guild != null && deep != null && _rooms.TryGetValue((guild, deep.Name), out var mark) && mark.Cleared
        && (DateTime.UtcNow - mark.When).TotalMilliseconds < RoomValidMs;

    public static long Held { get; private set; }

    public static long Won { get; private set; }

    public static long Lost { get; private set; }

    public static long Called { get; private set; }

    public static long Unset { get; private set; }

    public static long ByHand { get; private set; }

    public static long Faults { get; private set; }

    public static long Swept { get; private set; }

    public static long Proved { get; private set; }

    public static long Neighboured { get; private set; }

    public static long Formula { get; private set; }

    /// <summary>One bot's reading against one creature.</summary>
    private sealed class Mark
    {
        public string Class;

        public string Kind;

        public int Trials;

        public int Wins;

        public double R;

        public double LastR;

        public string LastVerdict;

        public double Seconds;

        public double Might;

        public double PowerThen;

        public BotBuild Build;

        public DateTime When;

        public double Strength => Math.Min(R, RMax) * Might;
    }

    private static readonly Dictionary<string, Dictionary<string, Mark>> _marks = new(StringComparer.Ordinal);

    private static readonly BotTrial[] _rings = new BotTrial[Middles.Length];

    private static readonly long[] _freeAt = new long[Middles.Length];

    private static readonly HashSet<string> _busy = new(StringComparer.Ordinal);

    private static readonly Queue<(string Bot, string Kind, string By)> _asked = new();

    /// <summary>One rung: the worst creature of one or more dungeons.</summary>
    private sealed class Rung
    {
        public string Kind;

        public double Might;

        public readonly List<string> Deeps = [];
    }

    private static readonly List<Rung> _rungs = [];

    private static readonly Dictionary<string, string> _worst = new(StringComparer.Ordinal);

    private static readonly Dictionary<string, long> _unfit = new(StringComparer.Ordinal);

    private static bool _laddered;

    private static double _calibration = 1.0;

    private static long _calibratedTick;

    private static bool _calibrated;

    public static void Wake(Map map)
    {
        if (map == null || map == Map.Internal)
        {
            return;
        }

        for (var i = 0; i < Middles.Length; i++)
        {
            map.ActivateSectors(Middles[i].X >> Map.SectorShift, Middles[i].Y >> Map.SectorShift);
        }
    }

    public static void Tick()
    {
        var map = BotPopulation.Home ?? Map.Felucca;
        var now = Core.TickCount;
        var rings = Math.Clamp(Rings, 0, Middles.Length);

        var roomFrom = Middles.Length - Math.Clamp(PartyRings, 0, Middles.Length);

        for (var i = roomFrom; i < Middles.Length; i++)
        {
            TickRoom(i, map, now);
        }

        for (var i = 0; i < roomFrom; i++)
        {
            var trial = _rings[i];

            if (trial != null)
            {
                bool over;

                try
                {
                    over = trial.Drive() || !Running || i >= rings;
                }
                catch (Exception e)
                {
                    Faults++;
                    logger.Error(e, "The fight of {Bot}'s double against the {Kind} threw; it is taken off the field", trial.Bot, trial.Kind);
                    over = true;
                }

                if (over)
                {
                    Settle(trial);
                    _rings[i] = null;
                    _freeAt[i] = now + PauseMs;
                }

                continue;
            }

            if (!Running || i >= Math.Min(rings, roomFrom) || now - _freeAt[i] < 0)
            {
                continue;
            }

            var next = Next(map);

            if (next.Bot == null)
            {
                continue;
            }

            var made = new BotTrial(next.Bot, next.Kind, i, map, Middles[i], next.By);

            Wake(map);

            if (!made.Begin())
            {
                Unset++;
                _unfit[next.Kind] = now;
                _freeAt[i] = now + PauseMs;

                logger.Information("Proving: {Bot} could not be tried against the {Kind}: {Why}", next.Bot.Name, next.Kind, made.Refused);

                continue;
            }

            _rings[i] = made;
            _busy.Add(next.Bot.Name);
        }
    }

    private static void TickRoom(int i, Map map, long now)
    {
        var room = _roomRings[i];

        if (room != null)
        {
            bool over;

            try
            {
                over = room.Drive() || !Running;
            }
            catch (Exception e)
            {
                Faults++;
                logger.Error(e, "{Guild}'s company against {Deep} threw; it is taken off the field", room.Guild, room.Deep?.Name);
                over = true;
            }

            if (over)
            {
                SettleRoom(room);
                _roomRings[i] = null;
                _freeAt[i] = now + PauseMs;
            }

            return;
        }

        if (!Running || now - _freeAt[i] < 0)
        {
            return;
        }

        var next = NextRoom(map);

        if (next == null)
        {
            _freeAt[i] = now + PauseMs * 6;

            return;
        }

        var made = new BotRoomTrial(next.Value.Guild, next.Value.Party, next.Value.Deep, next.Value.Room, i, map, Middles[i]);

        _roomTried[next.Value.Guild] = DateTime.UtcNow;

        Wake(map);

        if (!made.Begin())
        {
            Unset++;
            _freeAt[i] = now + PauseMs;

            logger.Information("Proving: {Guild}'s company could not be set against {Deep}: {Why}", next.Value.Guild, next.Value.Deep.Name, made.Refused);

            return;
        }

        _roomRings[i] = made;
    }

    private static void SettleRoom(BotRoomTrial room)
    {
        try
        {
            if (room.Verdict == null)
            {
                logger.Information("Proving: {Guild}'s company against {Deep} was taken off the field undecided", room.Guild, room.Deep?.Name);

                return;
            }

            Rooms++;

            if (room.Cleared)
            {
                RoomsCleared++;
            }
            else
            {
                RoomsFailed++;
            }

            _rooms[(room.Guild, room.Deep.Name)] = new RoomMark
            {
                Cleared = room.Cleared,
                Deaths = room.Deaths,
                Killed = room.Killed,
                Spent = room.Spent,
                When = DateTime.UtcNow,
                Line = room.Say()
            };

            logger.Information("Proving: {Line}", room.Say());
        }
        finally
        {
            room.End();
        }
    }

    private static (string Guild, List<BotMobile> Party, BotDungeon.Deep Deep, List<string> Room)? NextRoom(Map map)
    {
        Ladder(map);

        var deeps = new List<BotDungeon.Deep>();
        var all = BotDungeon.All;

        for (var i = 0; i < all.Count; i++)
        {
            if (all[i].Ready)
            {
                deeps.Add(all[i]);
            }
        }

        if (deeps.Count < 2)
        {
            return null;
        }

        deeps.Sort(static (a, b) => a.Worst.CompareTo(b.Worst));

        string pick = null;
        BotDungeon.Deep pickDeep = null;
        var oldest = DateTime.MaxValue;

        foreach (var guild in BotGuilds.Standing)
        {
            if (guild == null || guild.Disbanded || Fighters(guild).Count < PartyLeast)
            {
                continue;
            }

            BotDungeon.Deep target = null;

            for (var d = 1; d < deeps.Count; d++)
            {
                if (RoomCleared(guild.Name, deeps[d]))
                {
                    continue;
                }

                if (_rooms.TryGetValue((guild.Name, deeps[d].Name), out var last) && !last.Cleared
                    && (DateTime.UtcNow - last.When).TotalMilliseconds < RoomRetryMs)
                {
                    break;
                }

                target = deeps[d];

                break;
            }

            if (target == null)
            {
                continue;
            }

            var tried = _roomTried.TryGetValue(guild.Name, out var when) ? when : DateTime.MinValue;

            if (tried < oldest)
            {
                oldest = tried;
                pick = guild.Name;
                pickDeep = target;
            }
        }

        if (pick == null)
        {
            return null;
        }

        var fighters = Fighters(BotGuilds.Named(pick));

        fighters.Sort((a, b) => Against(b, pickDeep).CompareTo(Against(a, pickDeep)));

        var room = BotRoomTrial.Worst(map, pickDeep, MostFoes);

        var size = Math.Max(PartySize, room.Count * PartyMultiple);

        if (fighters.Count > size)
        {
            fighters.RemoveRange(size, fighters.Count - size);
        }

        return room.Count == 0 ? null : (pick, fighters, pickDeep, room);
    }

    internal static List<BotMobile> Fighters(Guilds.Guild guild)
    {
        List<BotMobile> list = [];

        if (guild == null)
        {
            return list;
        }

        for (var i = 0; i < guild.Members.Count; i++)
        {
            if (guild.Members[i] is BotMobile { Deleted: false } bot && bot.Map != null && bot.Map != Map.Internal
                && bot.Class is { } klass && klass.Role != BotRole.Producer)
            {
                list.Add(bot);
            }
        }

        return list;
    }

    private static void Settle(BotTrial trial)
    {
        try
        {
            if (trial.Verdict == null)
            {
                logger.Information("Proving: the fight of {Bot}'s double against the {Kind} was taken off the field undecided", trial.Bot, trial.Kind);
            }
            else
            {
                Held++;

                if (trial.Verdict == "won")
                {
                    Won++;
                }
                else if (trial.Verdict == "lost")
                {
                    Lost++;
                }
                else
                {
                    Called++;
                }

                Record(trial);

                logger.Information("Proving: {Line}", trial.Say());
            }
        }
        finally
        {
            trial.End();
            _busy.Remove(trial.Bot ?? "");
        }
    }

    private static void Record(BotTrial trial)
    {
        if (trial.Bot == null || trial.Kind == null)
        {
            return;
        }

        if (!_marks.TryGetValue(trial.Bot, out var marks))
        {
            _marks[trial.Bot] = marks = new Dictionary<string, Mark>(StringComparer.Ordinal);
        }

        if (!marks.TryGetValue(trial.Kind, out var mark) || mark.Class != trial.Class || !Close(mark.Build, trial.Build))
        {
            marks[trial.Kind] = mark = new Mark { Class = trial.Class, Kind = trial.Kind };
        }

        var n = Math.Min(mark.Trials, Math.Max(0, Keep - 1));

        mark.R = (Math.Min(mark.R, RMax) * n + Math.Min(trial.R, RMax)) / (n + 1);
        mark.Trials++;
        mark.Wins += trial.Verdict == "won" ? 1 : 0;
        mark.LastR = trial.R;
        mark.LastVerdict = trial.Verdict;
        mark.Seconds = trial.Seconds;
        mark.Might = trial.Might;
        mark.PowerThen = trial.PowerThen;
        mark.Build = trial.Build;
        mark.When = DateTime.UtcNow;
    }

    private static readonly Dictionary<Serial, (long Tick, BotBuild Build)> _builds = [];

    public static int BuildMs { get; set; } = 10000;

    private static BotBuild BuildOf(BotMobile bot)
    {
        var now = Core.TickCount;

        if (_builds.TryGetValue(bot.Serial, out var had) && now - had.Tick < BuildMs)
        {
            return had.Build;
        }

        var build = BotBuild.Of(bot);

        _builds[bot.Serial] = (now, build);

        return build;
    }

    public static bool Close(BotBuild a, BotBuild b) =>
        Math.Abs(a.Skills - b.Skills) < Math.Max(SkillDrift, (int)(Math.Max(a.Skills, b.Skills) * SkillDriftShare))
        && Math.Abs(a.Stats - b.Stats) < StatDrift
        && Math.Abs(a.Armour - b.Armour) < ArmourDrift
        && Math.Abs(a.Damage - b.Damage) < DamageDrift
        && a.Weapon == b.Weapon;

    private static bool Fresh(Mark mark, BotMobile bot, BotBuild now) =>
        mark != null && mark.Class == bot?.Class?.Name && Close(mark.Build, now);

    public static bool Fresh(BotMobile bot, string kind) =>
        bot != null && kind != null && _marks.TryGetValue(bot.Name, out var marks) && marks.TryGetValue(kind, out var mark)
        && Fresh(mark, bot, BuildOf(bot));

    private static (BotMobile Bot, string Kind, string By) Next(Map map)
    {
        while (_asked.Count > 0)
        {
            var (name, kind, by) = _asked.Dequeue();
            var bot = Find(name);

            if (bot != null && !_busy.Contains(bot.Name))
            {
                return (bot, kind ?? Aim(bot), by);
            }
        }

        Ladder(map);

        if (_rungs.Count == 0)
        {
            return default;
        }

        BotMobile best = null;
        string bestKind = null;
        var bestScore = 0;
        var bestAge = -1.0;

        var bots = BotPopulation.Bots;

        for (var i = 0; i < bots.Count; i++)
        {
            var bot = bots[i];

            if (bot is not { Deleted: false, Alive: true } || bot.Map != map || bot.Class is not { } klass
                || klass.Role == BotRole.Producer || _busy.Contains(bot.Name))
            {
                continue;
            }

            var (score, kind, age) = Wants(bot);

            if (score > bestScore || score == bestScore && score > 0 && age > bestAge)
            {
                best = bot;
                bestKind = kind;
                bestScore = score;
                bestAge = age;
            }
        }

        return best == null ? default : (best, bestKind, "the schedule");
    }

    private static (int Score, string Kind, double Age) Wants(BotMobile bot)
    {
        var build = BuildOf(bot);
        _marks.TryGetValue(bot.Name, out var marks);

        var top = -1;

        for (var i = 0; i < _rungs.Count; i++)
        {
            if (marks != null && marks.TryGetValue(_rungs[i].Kind, out var m) && Fresh(m, bot, build) && m.R >= Pass)
            {
                top = i;
            }
        }

        var now = DateTime.UtcNow;

        if (top + 1 < _rungs.Count)
        {
            var kind = _rungs[top + 1].Kind;
            Mark next = null;

            if (_unfit.TryGetValue(kind, out var refused) && Core.TickCount - refused < UnfitMs)
            {
                return (0, null, 0);
            }

            if (marks == null || !marks.TryGetValue(kind, out next))
            {
                return (3, kind, double.MaxValue);
            }

            var age = (now - next.When).TotalMilliseconds;

            if (!Fresh(next, bot, build))
            {
                return (2, kind, age);
            }

            if (age > RetestMs)
            {
                return (1, kind, age);
            }
        }

        if (top >= 0 && marks != null && marks.TryGetValue(_rungs[top].Kind, out var held))
        {
            var age = (now - held.When).TotalMilliseconds;

            if (!Fresh(held, bot, build))
            {
                return (2, held.Kind, age);
            }

            if (age > RetestMs)
            {
                return (1, held.Kind, age);
            }
        }

        return (0, null, 0);
    }

    private static string Aim(BotMobile bot)
    {
        Ladder(bot.Map);

        var (_, kind, _) = Wants(bot);

        return kind ?? (_rungs.Count > 0 ? _rungs[0].Kind : "OrcishLord");
    }

    private static void Ladder(Map map)
    {
        if (_laddered || map == null || map == Map.Internal)
        {
            return;
        }

        BotDungeon.Survey(map);

        if (!BotDungeon.Surveyed)
        {
            return;
        }

        _rungs.Clear();
        _worst.Clear();

        var all = BotDungeon.All;

        for (var i = 0; i < all.Count; i++)
        {
            var deep = all[i];

            if (!deep.Ready)
            {
                continue;
            }

            string worst = null;
            var most = 0.0;

            for (var k = 0; k < deep.Kinds.Count; k++)
            {
                var might = BotDungeon.Might(deep.Kinds[k]);

                if (might > most)
                {
                    most = might;
                    worst = deep.Kinds[k];
                }
            }

            if (worst == null)
            {
                continue;
            }

            _worst[deep.Name] = worst;

            var rung = _rungs.Find(r => r.Kind == worst);

            if (rung == null)
            {
                _rungs.Add(rung = new Rung { Kind = worst, Might = most });
            }

            rung.Deeps.Add(deep.Name);
        }

        _rungs.Sort(static (a, b) => a.Might.CompareTo(b.Might));
        _laddered = _rungs.Count > 0;

        if (_laddered)
        {
            using var sb = ValueStringBuilder.Create(1024);

            for (var i = 0; i < _rungs.Count; i++)
            {
                if (i > 0)
                {
                    sb.Append("; ");
                }

                sb.Append($"{i + 1}. {_rungs[i].Kind} {_rungs[i].Might:F0} ({string.Join(", ", _rungs[i].Deeps)})");
            }

            logger.Information("Proving: the ladder is read off the dungeons, {Count} rungs: {Rungs}", _rungs.Count, sb.ToString());
        }
    }

    public static double Against(BotMobile bot, BotDungeon.Deep deep)
    {
        if (bot == null || deep == null)
        {
            return 0.0;
        }

        if (!Judges)
        {
            return BotThreat.Power(bot);
        }

        Ladder(bot.Map);

        if (!_worst.TryGetValue(deep.Name, out var kind))
        {
            Formula++;

            return BotThreat.Power(bot) * Calibration();
        }

        return Against(bot, kind, deep.Worst);
    }

    public static double Against(BotMobile bot, string kind, double might)
    {
        var build = BuildOf(bot);

        if (_marks.TryGetValue(bot.Name, out var marks))
        {
            if (marks.TryGetValue(kind, out var exact) && Fresh(exact, bot, build))
            {
                Proved++;

                return Math.Min(exact.R, RMax) * might;
            }

            Mark near = null;
            var gap = double.MaxValue;

            foreach (var mark in marks.Values)
            {
                if (!Fresh(mark, bot, build) || mark.Might <= 0.0 || might <= 0.0)
                {
                    continue;
                }

                var g = Math.Abs(Math.Log(mark.Might) - Math.Log(might));

                if (g < gap)
                {
                    gap = g;
                    near = mark;
                }
            }

            if (near != null)
            {
                Neighboured++;

                return near.Might < might
                    ? Math.Min(Math.Min(near.R, RMax), Pass) * near.Might * Reach
                    : Math.Min(near.R, RMax) * near.Might;
            }
        }

        Formula++;

        return BotThreat.Power(bot) * Calibration();
    }

    public static double Calibration()
    {
        if (_calibrated && Core.TickCount - _calibratedTick < 60000)
        {
            return _calibration;
        }

        _calibrated = true;
        _calibratedTick = Core.TickCount;

        var ratios = new List<double>();

        foreach (var marks in _marks.Values)
        {
            foreach (var mark in marks.Values)
            {
                if (mark.PowerThen > 0.0 && mark.Might > 0.0 && mark.R < CalibrateUnder)
                {
                    ratios.Add(mark.Strength / mark.PowerThen);
                }
            }
        }

        if (ratios.Count < Math.Max(1, CalibrateLeast))
        {
            _calibration = 1.0;

            return _calibration;
        }

        ratios.Sort();

        var middle = ratios.Count % 2 == 1
            ? ratios[ratios.Count / 2]
            : (ratios[ratios.Count / 2 - 1] + ratios[ratios.Count / 2]) / 2.0;

        _calibration = Math.Clamp(middle, CalibrateLow, CalibrateHigh);

        return _calibration;
    }

    public static double Proven(BotMobile bot)
    {
        if (bot == null || !_marks.TryGetValue(bot.Name, out var marks))
        {
            return 0.0;
        }

        var build = BuildOf(bot);
        Mark top = null;

        foreach (var mark in marks.Values)
        {
            if (Fresh(mark, bot, build) && (top == null || mark.Might > top.Might))
            {
                top = mark;
            }
        }

        return top?.Strength ?? 0.0;
    }

    public static string Beats(BotMobile bot)
    {
        if (bot == null || !_marks.TryGetValue(bot.Name, out var marks) || marks.Count == 0)
        {
            return "?";
        }

        var build = BuildOf(bot);
        Rung best = null;
        var bestMight = 0.0;

        for (var i = 0; i < _rungs.Count; i++)
        {
            var rung = _rungs[i];

            if (marks.TryGetValue(rung.Kind, out var mark) && Fresh(mark, bot, build) && mark.R >= Pass && rung.Might > bestMight)
            {
                best = rung;
                bestMight = rung.Might;
            }
        }

        if (best == null || best.Deeps.Count == 0)
        {
            return "-";
        }

        var name = best.Deeps[0].StartsWith("the ", StringComparison.Ordinal) ? best.Deeps[0][4..] : best.Deeps[0];

        return name.Length > 8 ? name[..8] : name;
    }

    public static int Sweep(Map map, Point3D middle, int range)
    {
        if (map == null || map == Map.Internal)
        {
            return 0;
        }

        List<IEntity> gone = null;

        foreach (var mobile in map.GetMobilesInRange<Mobile>(middle, range))
        {
            if (mobile is BotStandIn
                || mobile is BaseCreature { Deleted: false } creature
                && (creature.ControlMaster is not PlayerMobile || creature.ControlMaster is BotStandIn))
            {
                (gone ??= []).Add(mobile);
            }
        }

        foreach (var corpse in map.GetItemsInRange<Corpse>(middle, range))
        {
            if (corpse.Owner is null or BotStandIn or BaseCreature)
            {
                (gone ??= []).Add(corpse);
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

        Swept += gone.Count;

        return gone.Count;
    }

    public static void SweepAll()
    {
        var map = BotPopulation.Home ?? Map.Felucca;
        var total = 0;

        for (var i = 0; i < Middles.Length; i++)
        {
            total += Sweep(map, Middles[i], Leash + Apart + 4);
        }

        if (total > 0)
        {
            logger.Information("Proving: {Count} things a stop had left on the rings were taken off them", total);
        }
    }

    private static BotMobile Find(string name)
    {
        var bots = BotPopulation.Bots;

        for (var i = 0; i < bots.Count; i++)
        {
            if (bots[i] is { Deleted: false } bot && string.Equals(bot.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return bot;
            }
        }

        return null;
    }

    public static string Prove(string name, string kind, string by)
    {
        var bot = Find(name);

        if (bot == null)
        {
            return $"there is no bot called \"{name}\" in the world.";
        }

        if (!string.IsNullOrWhiteSpace(kind) && BotDungeon.Might(kind) <= 0.0)
        {
            return $"\"{kind}\" is nothing the engine can build as a creature; give the name a spawner uses, like OrcishLord or Lich.";
        }

        ByHand++;
        _asked.Enqueue((bot.Name, string.IsNullOrWhiteSpace(kind) ? null : kind, by));

        return $"{bot.Name} goes on the proving ground next, against the {(string.IsNullOrWhiteSpace(kind) ? Aim(bot) : kind)}; "
            + "the fight is in the log as \"Proving:\" and in \"proof <bot>\" when it is over.";
    }

    public static string Tell(string name)
    {
        var bot = Find(name);

        if (bot == null)
        {
            return $"there is no bot called \"{name}\" in the world.";
        }

        var build = BuildOf(bot);
        using var sb = ValueStringBuilder.Create(4096);

        sb.Append($"{bot.Name} the {bot.Class?.Name} as it stands: {build}; the old formula says {BotThreat.Power(bot):F0}. ");

        if (!_marks.TryGetValue(bot.Name, out var marks) || marks.Count == 0)
        {
            sb.Append("Never tried on the proving ground.");
        }
        else
        {
            foreach (var mark in marks.Values)
            {
                sb.Append($"Against the {mark.Kind} ({mark.Might:F0}): {mark.Trials} fights, {mark.Wins} won, last {mark.LastVerdict} at R {mark.LastR:F2}; ");
                sb.Append($"R {mark.R:F2}, strength {mark.Strength:F0}, {(Fresh(mark, bot, build) ? "fresh" : $"of an older build ({mark.Build})")}, ");
                sb.Append($"{(DateTime.UtcNow - mark.When).TotalMinutes:F0} minutes ago. ");
            }
        }

        Ladder(bot.Map);

        var all = BotDungeon.All;

        if (_worst.Count > 0)
        {
            sb.Append($"Against each dungeon's worst, alone (needs ×{BotDelver.Odds:F1}): ");

            for (var i = 0; i < all.Count; i++)
            {
                if (!all[i].Ready)
                {
                    continue;
                }

                sb.Append($"{all[i].Name} {Against(bot, all[i]):F0} of {all[i].Worst * BotDelver.Odds:F0}; ");
            }
        }

        return sb.ToString().TrimEnd(' ', ';') + ".";
    }

    public static string Board()
    {
        using var sb = ValueStringBuilder.Create(4096);

        sb.Append(Describe());
        sb.Append('\n');

        var rows = new List<(double Strength, string Line)>();
        var bots = BotPopulation.Bots;

        for (var i = 0; i < bots.Count; i++)
        {
            var bot = bots[i];

            if (bot is not { Deleted: false } || !_marks.TryGetValue(bot.Name, out var marks) || marks.Count == 0)
            {
                continue;
            }

            var build = BuildOf(bot);
            Mark top = null;
            var won = 0;
            var fought = 0;

            foreach (var mark in marks.Values)
            {
                won += mark.Wins;
                fought += mark.Trials;

                if (Fresh(mark, bot, build) && (top == null || mark.Might > top.Might))
                {
                    top = mark;
                }
            }

            var strength = top?.Strength ?? 0.0;
            var formula = BotThreat.Power(bot);

            rows.Add((strength, top == null
                ? $"{bot.Name} the {bot.Class?.Name}: {fought} fights, {won} won, nothing fresh; formula {formula:F0}"
                : $"{bot.Name} the {bot.Class?.Name} ({(bot.Guild as Guilds.Guild)?.Abbreviation ?? "-"}): {strength:F0} proven against the {top.Kind} at R {top.R:F2} ({top.Wins} of {top.Trials} won), formula {formula:F0} (×{(formula > 0 ? strength / formula : 0):F1})"));
        }

        rows.Sort(static (a, b) => b.Strength.CompareTo(a.Strength));

        foreach (var (_, mark) in _rooms)
        {
            sb.Append("  company: ");
            sb.Append(mark.Line);
            sb.Append($" ({(DateTime.UtcNow - mark.When).TotalMinutes:F0} minutes ago)");
            sb.Append('\n');
        }

        for (var i = 0; i < rows.Count; i++)
        {
            sb.Append("  ");
            sb.Append(rows[i].Line);
            sb.Append('\n');
        }

        return sb.ToString().TrimEnd('\n');
    }

    public static string Describe()
    {
        var measured = 0;
        var readings = 0;

        foreach (var marks in _marks.Values)
        {
            if (marks.Count > 0)
            {
                measured++;
                readings += marks.Count;
            }
        }

        using var rungs = ValueStringBuilder.Create(512);

        for (var i = 0; i < _rungs.Count; i++)
        {
            if (i > 0)
            {
                rungs.Append(", ");
            }

            rungs.Append($"{_rungs[i].Kind} {_rungs[i].Might:F0}");
        }

        var standing = 0;

        for (var i = 0; i < _rings.Length; i++)
        {
            if (_rings[i] != null)
            {
                standing++;
            }
        }

        var cleared = new List<string>();

        foreach (var (key, mark) in _rooms)
        {
            if (mark.Cleared && (DateTime.UtcNow - mark.When).TotalMilliseconds < RoomValidMs)
            {
                cleared.Add($"{key.Guild} {key.Deep}");
            }
        }

        return $"{(Running ? "held" : "stopped")}, {Held} fights ({Won} won, {Lost} lost, {Called} called), {standing} on the field now, "
            + $"{Rooms} companies against a dungeon's worst room ({RoomsCleared} cleared it, {RoomsFailed} did not), cleared lately: {(cleared.Count == 0 ? "none" : string.Join(", ", cleared))}, "
            + $"{Unset} could not be set up, {ByHand} asked for by hand, {Faults} faults, {Swept} things swept; {measured} bots measured, "
            + $"{readings} readings; the delve {(Judges ? "reads proven strength" : "reads the old formula")}: {Proved} proven, {Neighboured} "
            + $"from a neighbouring rung, {Formula} by the formula ×{Calibration():F2}; the ladder: {(rungs.Length == 0 ? "not read yet" : rungs.ToString())}";
    }

    public static void Halt()
    {
        for (var i = 0; i < _rings.Length; i++)
        {
            if (_rings[i] is { } trial)
            {
                trial.End();
                _rings[i] = null;
            }

            if (_roomRings[i] is { } room)
            {
                room.End();
                _roomRings[i] = null;
            }
        }

        _busy.Clear();
    }

    public static void Forget()
    {
        Halt();

        _marks.Clear();
        _rooms.Clear();
        _roomTried.Clear();
        _builds.Clear();
        _asked.Clear();
        _rungs.Clear();
        _worst.Clear();
        _unfit.Clear();
        _laddered = false;
        _calibration = 1.0;
        _calibrated = false;

        Held = 0;
        Won = 0;
        Lost = 0;
        Called = 0;
        Unset = 0;
        ByHand = 0;
        Faults = 0;
        Proved = 0;
        Neighboured = 0;
        Formula = 0;
    }

    public static void Rewind()
    {
        Halt();

        _rungs.Clear();
        _worst.Clear();
        _laddered = false;
    }

    private const int Shape = 3;

    private const int OldestShape = 3;

    public static void Save(IGenericWriter writer)
    {
        writer.WriteEncodedInt(Shape);
        writer.WriteEncodedInt(_marks.Count);

        foreach (var (name, marks) in _marks)
        {
            writer.Write(name);
            writer.WriteEncodedInt(marks.Count);

            foreach (var mark in marks.Values)
            {
                writer.Write(mark.Class ?? "");
                writer.Write(mark.Kind ?? "");
                writer.WriteEncodedInt(mark.Trials);
                writer.WriteEncodedInt(mark.Wins);
                writer.Write(mark.R);
                writer.Write(mark.LastR);
                writer.Write(mark.LastVerdict ?? "");
                writer.Write(mark.Seconds);
                writer.Write(mark.Might);
                writer.Write(mark.PowerThen);
                writer.WriteEncodedInt(mark.Build.Skills);
                writer.WriteEncodedInt(mark.Build.Stats);
                writer.WriteEncodedInt(mark.Build.Armour);
                writer.Write(mark.Build.Weapon ?? "");
                writer.WriteEncodedInt(mark.Build.Damage);
                writer.Write(mark.When);
            }
        }

        writer.WriteEncodedInt(_rooms.Count);

        foreach (var ((guild, deep), room) in _rooms)
        {
            writer.Write(guild ?? "");
            writer.Write(deep ?? "");
            writer.Write(room.Cleared);
            writer.WriteEncodedInt(room.Deaths);
            writer.Write(room.Killed);
            writer.Write(room.Spent);
            writer.Write(room.When);
            writer.Write(room.Line ?? "");
        }
    }

    public static int Load(IGenericReader reader)
    {
        var shape = reader.ReadEncodedInt();

        if (shape < 1 || shape > Shape)
        {
            logger.Warning("The saved proving ground is shape {Found} and this build reads {Wanted}; every bot is measured afresh", shape, Shape);

            return 0;
        }

        var stale = shape < OldestShape;

        _marks.Clear();

        var bots = reader.ReadEncodedInt();
        var read = 0;

        for (var i = 0; i < bots; i++)
        {
            var name = reader.ReadString();
            var count = reader.ReadEncodedInt();
            var marks = new Dictionary<string, Mark>(StringComparer.Ordinal);

            for (var k = 0; k < count; k++)
            {
                var mark = new Mark
                {
                    Class = reader.ReadString(),
                    Kind = reader.ReadString(),
                    Trials = reader.ReadEncodedInt(),
                    Wins = reader.ReadEncodedInt(),
                    R = Math.Min(reader.ReadDouble(), RMax),
                    LastR = reader.ReadDouble(),
                    LastVerdict = reader.ReadString(),
                    Seconds = reader.ReadDouble(),
                    Might = reader.ReadDouble(),
                    PowerThen = reader.ReadDouble()
                };

                var skills = reader.ReadEncodedInt();
                var stats = reader.ReadEncodedInt();
                var armour = reader.ReadEncodedInt();
                var weapon = reader.ReadString();
                var damage = reader.ReadEncodedInt();

                mark.Build = new BotBuild(skills, stats, armour, weapon, damage);
                mark.When = reader.ReadDateTime();

                if (!string.IsNullOrEmpty(mark.Kind))
                {
                    marks[mark.Kind] = mark;
                    read++;
                }
            }

            if (name != null && marks.Count > 0)
            {
                _marks[name] = marks;
            }
        }

        if (shape >= 2)
        {
            _rooms.Clear();

            var rooms = reader.ReadEncodedInt();

            for (var i = 0; i < rooms; i++)
            {
                var guild = reader.ReadString();
                var deep = reader.ReadString();
                var room = new RoomMark
                {
                    Cleared = reader.ReadBool(),
                    Deaths = reader.ReadEncodedInt(),
                    Killed = reader.ReadDouble(),
                    Spent = reader.ReadDouble(),
                    When = reader.ReadDateTime(),
                    Line = reader.ReadString()
                };

                if (!string.IsNullOrEmpty(guild) && !string.IsNullOrEmpty(deep))
                {
                    _rooms[(guild, deep)] = room;
                }
            }
        }

        if (stale)
        {
            _marks.Clear();
            _rooms.Clear();

            logger.Warning("The saved proving ground is shape {Found}, measured before build 249 against creatures asleep; every bot and company is measured afresh", shape);

            return 0;
        }

        return read;
    }
}
