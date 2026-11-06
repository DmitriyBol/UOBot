using System;
using System.Collections.Generic;
using System.Diagnostics;
using Server.Logging;
using Server.Mobiles;
using Server.Regions;

namespace Server.BotAI.V2;

public sealed class BotReaches
{

    public const int Most = 8;

    private readonly int[] _x = new int[Most];
    private readonly int[] _y = new int[Most];
    private readonly int[] _r = new int[Most];

    public int Count { get; private set; }

    public void Clear() => Count = 0;

    public bool Add(int x, int y, int reach)
    {
        if (Count >= Most)
        {
            return false;
        }

        _x[Count] = x;
        _y[Count] = y;
        _r[Count] = Math.Max(0, reach);
        Count++;

        return true;
    }

    public bool Within(int x, int y, int slack)
    {
        for (var i = 0; i < Count; i++)
        {
            var r = _r[i] + slack;

            if (r >= 0 && Math.Abs(x - _x[i]) <= r && Math.Abs(y - _y[i]) <= r)
            {
                return true;
            }
        }

        return false;
    }

    public int Penalty(int x, int y) => Within(x, y, 0) ? BotSight.StepCost : 0;
}

public static class BotSight
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotSight));

    public static bool Running { get; set; } = true;

    public static int EveryMs { get; set; } = 1000;

    public static int SightTiles { get; set; } = 20;

    public static int Margin { get; set; } = 2;

    public static double Odds { get; set; } = 1.5;

    public static int PackTiles { get; set; } = 12;

    public static int AllyTiles { get; set; } = 8;

    public static int StepCost { get; set; } = 30;

    public static int KeepMs { get; set; } = 8000;

    public static int ReplanMs { get; set; } = 4000;

    public static int LookAhead { get; set; } = 48;

    public static int RouteAhead { get; set; } = 72;

    public static int Imminent { get; set; } = 6;

    public static int WaitMs { get; set; } = 30000;

    public static bool Standing { get; set; } = true;

    public static int FightCalmMs { get; set; } = 5000;

    public static int DeathWindowMs { get; set; } = 60000;

    public static int MostWeighed { get; set; } = 24;

    public static long Looks { get; private set; }

    public static long LooksWithDanger { get; private set; }

    public static long PassedBeatable { get; private set; }

    public static long DangersSeen { get; private set; }

    public static long FirstInside { get; private set; }

    public static long FirstNear { get; private set; }

    public static long FirstFar { get; private set; }

    public static long Redrawn { get; private set; }

    public static long Replanned { get; private set; }

    public static long RoutesHeld { get; private set; }

    public static long Waited { get; private set; }

    public static long TurnedBack { get; private set; }

    public static long StoodDown { get; private set; }

    public static long FlightsBent { get; private set; }

    public static long WaitedOut { get; private set; }

    public static long DiedNotLooking { get; private set; }

    public static long DiedUnseen { get; private set; }

    public static long DiedBeatable { get; private set; }

    public static long DiedUnheeded { get; private set; }

    public static long DiedHeeded { get; private set; }

    private static long _ticks;

    public static double SpentMs => _ticks * 1000.0 / Stopwatch.Frequency;

    private static readonly Dictionary<string, long> _changed = [];

    private static readonly Dictionary<string, long> _unheeded = [];

    private static readonly Dictionary<string, long> _heeded = [];

    private enum Mode
    {
        Off,
        Full,
        Replan,
        Flight,
        Stand
    }

    private const byte Beatable = 1;
    private const byte Danger = 2;
    private const byte Heeded = 3;

    private sealed class Watch
    {
        public long Next;

        public bool Looking;

        public readonly BotReaches Reaches = new();

        public long Seen;

        public int Set;

        public long SetTick;

        public bool Redrawn;

        public long ReplanTick;

        public long RouteTick;

        public long WaitSince;

        public bool Bent;

        public readonly Serial[] Who = new Serial[32];

        public readonly long[] When = new long[32];

        public readonly byte[] What = new byte[32];

        public int Ring;

        public void Note(Mobile c, long now, byte verdict, out bool first)
        {
            first = verdict >= Danger;

            for (var i = 0; i < Who.Length; i++)
            {
                if (Who[i] == c.Serial && now - When[i] < DeathWindowMs)
                {
                    first = verdict >= Danger && What[i] < Danger;
                    When[i] = now;

                    What[i] = Math.Max(What[i], verdict);

                    return;
                }
            }

            Who[Ring] = c.Serial;
            When[Ring] = now;
            What[Ring] = verdict;
            Ring = (Ring + 1) % Who.Length;
        }

        public void Heed(long now)
        {
            for (var i = 0; i < Who.Length; i++)
            {
                if (What[i] == Danger && now - When[i] < 2 * EveryMs + 500)
                {
                    What[i] = Heeded;
                }
            }
        }

        public byte Verdict(Mobile killer, long now)
        {
            if (killer == null)
            {
                return 0;
            }

            for (var i = 0; i < Who.Length; i++)
            {
                if (Who[i] == killer.Serial && now - When[i] < DeathWindowMs)
                {
                    return What[i];
                }
            }

            return 0;
        }
    }

    private static readonly Dictionary<Serial, Watch> _watches = [];

    private static Watch WatchOf(Mobile bot)
    {
        if (!_watches.TryGetValue(bot.Serial, out var watch))
        {
            if (_watches.Count > 4096)
            {
                _watches.Clear();
            }

            watch = new Watch { Next = Core.TickCount + Utility.Random(Math.Max(1, EveryMs)) };
            _watches[bot.Serial] = watch;
        }

        return watch;
    }

    private static readonly List<(BaseCreature C, double Threat, int Reach)> _weighed = [];

    private static readonly List<(BaseCreature C, double Threat, int Reach)> _dangers = [];

    public static void Look(BotMobile bot)
    {
        if (!Running || bot == null || bot.Deleted)
        {
            return;
        }

        var now = Core.TickCount;
        var watch = WatchOf(bot);

        if (now - watch.Next < 0)
        {
            return;
        }

        watch.Next = now + Math.Max(100, EveryMs);

        var journey = bot.Journey;
        var mode = ModeOf(bot, journey, now);

        watch.Looking = mode != Mode.Off;

        if (mode == Mode.Off)
        {
            Calm(watch, journey, now);

            return;
        }

        var began = Stopwatch.GetTimestamp();

        try
        {
            See(bot, journey, watch, mode, now);
        }
        finally
        {
            _ticks += Stopwatch.GetTimestamp() - began;
        }
    }

    private static Mode ModeOf(BotMobile bot, BotJourney journey, long now)
    {
        var map = bot.Map;

        if (!bot.Alive || map == null || map == Map.Internal)
        {
            return Mode.Off;
        }

        if (bot.Region?.IsPartOf<TownRegion>() == true || BotDungeon.Under(bot.Location))
        {
            return Mode.Off;
        }

        if (journey?.Current?.Reason is "station" or "sweep")
        {
            return Mode.Off;
        }

        var deed = bot.Resolve?.Deed;

        if (deed is BotVoyage)
        {
            return Mode.Off;
        }

        if (deed is BotBolt)
        {
            return Mode.Flight;
        }

        if (bot.Combatant is Mobile { Deleted: false, Alive: true } foe && foe.Map == map && bot.InRange(foe, BotBolt.Watch))
        {
            return Mode.Off;
        }

        if (bot.Resolve is { Struck: true } resolve && now - resolve.HurtTick < FightCalmMs)
        {
            return Mode.Off;
        }

        if (bot.Squad != null || deed?.Foe != null)
        {
            return Mode.Replan;
        }

        if (journey == null || !journey.Active || journey.Arrived(bot.Location))
        {
            return Standing && deed != null ? Mode.Stand : Mode.Off;
        }

        return Mode.Full;
    }

    private static void Calm(Watch watch, BotJourney journey, long now)
    {
        if (watch.WaitSince != 0)
        {
            watch.WaitSince = 0;
            WaitedOut++;
        }

        if (watch.Reaches.Count > 0 && now - watch.Seen >= KeepMs)
        {
            watch.Reaches.Clear();
            watch.Set = 0;
            watch.Redrawn = false;
            watch.Bent = false;
            journey?.Sight(null, now);
        }
    }

    private static void See(BotMobile bot, BotJourney journey, Watch watch, Mode mode, long now)
    {
        Looks++;

        var map = bot.Map;
        var deed = bot.Resolve?.Deed;
        var follow = journey?.Current?.Follow;
        var bottom = journey?.Bottom?.Follow;
        var foe = deed?.Foe;
        var allies = 0.0;

        _weighed.Clear();
        _dangers.Clear();

        foreach (var m in map.GetMobilesInRange<Mobile>(bot.Location, Math.Max(1, SightTiles)))
        {
            if (m == bot || m.Deleted || !m.Alive)
            {
                continue;
            }

            if (m is BaseCreature c)
            {
                if (_weighed.Count >= MostWeighed || !Candidate(bot, c, follow, bottom, foe))
                {
                    continue;
                }

                var share = c.HitsMax > 0 ? Math.Clamp(c.Hits / (double)c.HitsMax, 0.1, 1.0) : 1.0;

                _weighed.Add((c, BotQuarry.Priced(c) * share, ReachOf(c)));
            }
            else if (m is IBotAlly { AbleToFight: true } && !ReferenceEquals(m, follow) && Math.Max(Math.Abs(m.X - bot.X), Math.Abs(m.Y - bot.Y)) <= AllyTiles)
            {
                allies += BotThreat.Power(m);
            }
        }

        if (_weighed.Count == 0)
        {
            Calm(watch, journey, now);

            return;
        }

        var ours = BotThreat.Now(bot) + allies;

        if (bot.Squad != null)
        {
            ours = Math.Max(ours, BotQuad.Strength(bot));
        }

        if (follow is IBotAlly { AbleToFight: true } && follow.Alive)
        {
            ours += BotThreat.Power(follow);
        }

        var bar = Odds * Math.Max(1.0, ours);

        for (var i = 0; i < _weighed.Count; i++)
        {
            var (c, threat, reach) = _weighed[i];
            var pack = threat;

            for (var j = 0; j < _weighed.Count; j++)
            {
                if (j != i)
                {
                    var o = _weighed[j].C;

                    if (Math.Max(Math.Abs(o.X - c.X), Math.Abs(o.Y - c.Y)) <= PackTiles)
                    {
                        pack += _weighed[j].Threat * BotThreat.Secondary;
                    }
                }
            }

            var away = Math.Max(Math.Abs(c.X - bot.X), Math.Abs(c.Y - bot.Y));

            if (pack <= bar)
            {
                PassedBeatable++;
                watch.Note(c, now, Beatable, out _);

                continue;
            }

            DangersSeen++;
            watch.Note(c, now, Danger, out var first);

            if (first)
            {
                if (away <= reach)
                {
                    FirstInside++;
                }
                else if (away <= reach + Margin)
                {
                    FirstNear++;
                }
                else
                {
                    FirstFar++;
                }
            }

            _dangers.Add((c, pack, reach));
        }

        if (_dangers.Count == 0)
        {
            Calm(watch, journey, now);

            return;
        }

        LooksWithDanger++;

        _dangers.Sort(static (a, b) => b.Threat.CompareTo(a.Threat));

        var reaches = watch.Reaches;
        var set = 17;

        reaches.Clear();

        for (var i = 0; i < _dangers.Count && reaches.Count < BotReaches.Most; i++)
        {
            var c = _dangers[i].C;

            reaches.Add(c.X, c.Y, _dangers[i].Reach + Margin);
            set = unchecked(set * 31 + c.Serial.GetHashCode());
        }

        watch.Seen = now;
        journey?.Sight(reaches, now + KeepMs);

        if (set != watch.Set)
        {
            watch.Set = set;
            watch.SetTick = now;
            watch.Redrawn = false;
            watch.Bent = false;
            watch.ReplanTick = now - ReplanMs;
        }

        var worst = _dangers[0];

        switch (mode)
        {
            case Mode.Stand:
                {

                    for (var i = 0; i < _dangers.Count; i++)
                    {
                        var (c, threat, reach) = _dangers[i];

                        if (Math.Max(Math.Abs(c.X - bot.X), Math.Abs(c.Y - bot.Y)) <= reach && c.InLOS(bot))
                        {
                            StoodDown++;
                            Changed(c);
                            watch.Heed(now);
                            TurnBack(bot, watch, $"left its work: {c.Name} ({threat:F0}), which it cannot beat with the {ours:F0} it brings, came within {Math.Max(Math.Abs(c.X - bot.X), Math.Abs(c.Y - bot.Y))} tiles of it");

                            return;
                        }
                    }

                    return;
                }
            case Mode.Flight:
                {
                    Steer(journey, watch, worst.C, now, bot);

                    if (!watch.Bent && deed is BotBolt { ForTown: false } bolt && journey is { Active: true }
                        && reaches.Within(journey.Target.X, journey.Target.Y, -Margin) && bolt.Bend(bot))
                    {
                        watch.Bent = true;
                        FlightsBent++;
                        Changed(worst.C);
                        watch.Heed(now);
                    }

                    return;
                }
            case Mode.Replan:
                {
                    Steer(journey, watch, worst.C, now, bot);

                    return;
                }
        }

        var target = journey.Target;
        var goalIn = !BotHaven.Guarded(map, target) && Covering(target.X, target.Y) is not null;
        var selfIn = reaches.Within(bot.X, bot.Y, -Margin);

        if (goalIn && selfIn && Covering(target.X, target.Y) is { } there)
        {
            var (c, threat) = there;

            Changed(c);
            watch.Heed(now);
            TurnBack(bot, watch, $"turned back: {c.Name} ({threat:F0}), which it cannot beat with the {ours:F0} it brings, stands within {Math.Max(Math.Abs(c.X - target.X), Math.Abs(c.Y - target.Y))} tiles of where it was going, and the bot stood in its reach");

            return;
        }

        if (Steer(journey, watch, worst.C, now, bot, replan: watch.WaitSince == 0))
        {
            return;
        }

        var enters = selfIn ? -1 : journey.PlanEnters(reaches, Imminent, -Margin);

        if (enters >= 0)
        {
            var tile = journey.PlanAt(enters);

            if (Covering(tile.X, tile.Y) is { } hit)
            {
                Wait(bot, journey, watch, hit.C, hit.Threat, ours, now, goalIn ? "stands within reach of where it was going" : "stands on the only way on");

                return;
            }
        }

        if (watch.WaitSince != 0)
        {
            watch.WaitSince = 0;
            WaitedOut++;
        }
    }

    private static (BaseCreature C, double Threat)? Covering(int x, int y)
    {
        for (var i = 0; i < _dangers.Count; i++)
        {
            var (c, threat, reach) = _dangers[i];

            if (Math.Abs(c.X - x) <= reach && Math.Abs(c.Y - y) <= reach)
            {
                return (c, threat);
            }
        }

        return null;
    }

    private static bool Steer(BotJourney journey, Watch watch, BaseCreature worst, long now, Mobile bot, bool replan = true)
    {
        if (journey is not { Active: true })
        {
            return false;
        }

        var reaches = watch.Reaches;

        if (!watch.Redrawn && journey.RouteEnters(reaches, bot.Location, RouteAhead))
        {

            if (now - watch.RouteTick < ReplanMs)
            {
                RoutesHeld++;

                return false;
            }

            watch.Redrawn = true;
            watch.RouteTick = now;
            watch.ReplanTick = now;
            journey.Resight();
            Redrawn++;
            Changed(worst);
            watch.Heed(now);

            return true;
        }

        var stale = journey.PlanBuilt - watch.SetTick < 0;

        if (replan && now - watch.ReplanTick >= ReplanMs && journey.PlanEnters(reaches, LookAhead, stale ? 0 : -Margin) >= 0)
        {
            watch.ReplanTick = now;
            journey.Discard();
            Replanned++;
            Changed(worst);
            watch.Heed(now);

            return true;
        }

        return false;
    }

    private static void Wait(BotMobile bot, BotJourney journey, Watch watch, BaseCreature c, double threat, double ours, long now, string where)
    {
        watch.Heed(now);

        if (watch.WaitSince == 0)
        {
            watch.WaitSince = now;
            Waited++;
            Changed(c);

            logger.Information(
                "{Name} waits at ({X}, {Y}), {Tiles} tiles from {Foe} ({Threat:F0}), which it cannot beat with the {Ours:F0} it brings and which {Where}",
                bot.Name,
                bot.X,
                bot.Y,
                Math.Max(Math.Abs(c.X - bot.X), Math.Abs(c.Y - bot.Y)),
                c.Name,
                threat,
                ours,
                where
            );
        }

        if (now - watch.WaitSince >= WaitMs)
        {
            TurnBack(bot, watch, $"turned back: {c.Name} ({threat:F0}), which it cannot beat with the {ours:F0} it brings, {where} after {(now - watch.WaitSince) / 1000}s of waiting");

            return;
        }

        journey.Hold(Math.Max(100, EveryMs) + 500);
    }

    private static void TurnBack(BotMobile bot, Watch watch, string why)
    {
        watch.WaitSince = 0;
        TurnedBack++;

        if (bot.Resolve?.Deed == null)
        {
            bot.Journey?.Finish();

            return;
        }

        BotWill.Abandon(bot, why, unreached: true);
    }

    private static bool Candidate(Mobile bot, BaseCreature c, Mobile follow, Mobile bottom, Mobile foe)
    {
        if (c.Hidden || c.Blessed || c.IsDeadBondedPet || c.Controlled || c.Summoned)
        {
            return false;
        }

        if (!BotHaunts.AttacksFirst(c.FightMode))
        {
            return false;
        }

        if (c.Combatant == bot || bot.Combatant == c || ReferenceEquals(c, follow) || ReferenceEquals(c, bottom) || ReferenceEquals(c, foe))
        {
            return false;
        }

        return BotThreat.Hostile(bot, c);
    }

    private static int ReachOf(BaseCreature c)
    {
        var caster = c.AI == AIType.AI_Mage;
        var sight = Math.Clamp(c.RangePerception, 1, Math.Max(1, BotZones.PerceptionMost));

        if (c.CantWalk && c.CanSwim)
        {
            return caster ? Math.Min(sight, BotZones.WaterCasterReach) : BotZones.WaterMeleeReach;
        }

        return c.CantWalk && !caster ? BotZones.RootedReach : sight;
    }

    private static void Changed(BaseCreature c)
    {
        var kind = c.GetType().Name;

        _changed[kind] = _changed.GetValueOrDefault(kind) + 1;
    }

    public static bool Covers(Mobile body, Point3D where) =>
        Running && (body as BotMobile)?.Journey?.Sighted is { } reaches && reaches.Within(where.X, where.Y, 0);

    public static void Died(Mobile bot, Mobile killer)
    {
        if (bot == null || killer is not BaseCreature || !_watches.TryGetValue(bot.Serial, out var watch))
        {
            if (killer is BaseCreature)
            {
                DiedNotLooking++;
            }

            return;
        }

        var kind = killer.GetType().Name;

        switch (watch.Verdict(killer, Core.TickCount))
        {
            case Beatable:
                DiedBeatable++;
                break;
            case Danger:
                DiedUnheeded++;
                _unheeded[kind] = _unheeded.GetValueOrDefault(kind) + 1;
                break;
            case Heeded:
                DiedHeeded++;
                _heeded[kind] = _heeded.GetValueOrDefault(kind) + 1;
                break;
            default:
                if (watch.Looking)
                {
                    DiedUnseen++;
                }
                else
                {
                    DiedNotLooking++;
                }

                break;
        }

        watch.WaitSince = 0;
        watch.Reaches.Clear();
        watch.Set = 0;
    }

    public static void Forget(Mobile bot)
    {
        if (bot != null)
        {
            _watches.Remove(bot.Serial);
        }
    }

    private static string Top(Dictionary<string, long> book)
    {
        if (book.Count == 0)
        {
            return "none";
        }

        var rows = new List<KeyValuePair<string, long>>(book);

        rows.Sort(static (a, b) => b.Value.CompareTo(a.Value));

        using var say = Server.Text.ValueStringBuilder.Create(128);

        for (var i = 0; i < rows.Count && i < 6; i++)
        {
            if (i > 0)
            {
                say.Append(", ");
            }

            say.Append($"{rows[i].Key} {rows[i].Value}");
        }

        return say.ToString();
    }

    public static string Describe() =>
        !Running
            ? "sight: bots do not look"
            : $"sight: {Looks} looks within {SightTiles} tiles every {EveryMs}ms ({SpentMs:F0}ms of the loop, {(Looks == 0 ? 0 : SpentMs * 1000.0 / Looks):F1}µs a look), {LooksWithDanger} saw something it could not beat (past ×{Odds:F1} what the bot brings, at its kind's price): {DangersSeen} dangers seen and {PassedBeatable} creatures passed as beatable; each danger first seen {FirstFar} beyond its reach and margin, {FirstNear} in the {Margin}-tile margin, {FirstInside} with the bot already inside its reach; "
              + $"walks changed: {Redrawn} routes drawn round ({RoutesHeld} looks at a route in a reach held off, one route a bot every {ReplanMs / 1000}s), {Replanned} plans drawn round, {Waited} waits out of reach ({WaitedOut} waited out), {TurnedBack} turned back after {WaitMs / 1000}s or standing in reach ({StoodDown} of them work left standing), {FlightsBent} flights bent off a landing in reach; by kind: {Top(_changed)}; "
              + $"deaths to a creature by what the bot had made of its killer in the {DeathWindowMs / 1000}s before: {DiedNotLooking} not looking (a town, a dungeon, a company's line, a fight), {DiedUnseen} looking and never seen, {DiedBeatable} seen and judged beatable, {DiedUnheeded} seen as a danger and the walk not changed ({Top(_unheeded)}), {DiedHeeded} after the walk was changed for it ({Top(_heeded)})";

    public static void Reset()
    {
        Looks = 0;
        LooksWithDanger = 0;
        PassedBeatable = 0;
        DangersSeen = 0;
        FirstInside = 0;
        FirstNear = 0;
        FirstFar = 0;
        Redrawn = 0;
        RoutesHeld = 0;
        Replanned = 0;
        Waited = 0;
        WaitedOut = 0;
        TurnedBack = 0;
        StoodDown = 0;
        FlightsBent = 0;
        DiedNotLooking = 0;
        DiedUnseen = 0;
        DiedBeatable = 0;
        DiedUnheeded = 0;
        DiedHeeded = 0;
        _ticks = 0;
        _changed.Clear();
        _unheeded.Clear();
        _heeded.Clear();
        _watches.Clear();
    }
}
