using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Server.Logging;
using Server.Regions;

namespace Server.BotAI.V2;

/// <summary>
/// Ground the population is never to want, however attractive whatever is standing on it.
///
/// <para>
/// <b>Patrick's order of 11.09.2026, and the difference between this and <see cref="BotRefused"/> is the
/// whole reason it is a second class.</b> The refusal ledger is a measurement: somebody tried, somebody
/// failed, and the square rests for a while and is cleared the moment anybody arrives in it. That is right
/// for ground whose reachability is a question. It is wrong for ground whose reachability is settled —
/// a walled pocket with no door — because the ledger keeps letting the question be asked again, and each
/// re-ask costs a path search, an errand and the minutes the bot spent walking at it.
/// </para>
///
/// <para>
/// <b>The evidence this was written from.</b> A cooking hearth stands at (2161, 1354) inside a pocket east
/// of Britain that nothing can walk into. In one nine-hour session it took 70 of the shard's 2460 walk
/// failures on its own, every one of them a cook who took the job, walked at it for minutes and gave up;
/// the rest of the pocket — a fleeing bot choosing (2208, 1379) as a refuge, a healer hunting something
/// shut in at (2163, 1384) — took 60 more. The shard had already <i>learned</i> the shape of it:
/// <c>BotReach</c> logged "a pocket of 527 tiles has been walked to its edges" and "a pocket of 25 tiles",
/// and it went on choosing destinations inside them, because knowing a journey will be refused is not the
/// same as not wanting to go.
/// </para>
///
/// <para>
/// <b>So this is a fact stated rather than learned, and it belongs in the choice.</b> It is asked by
/// <see cref="BotRefused.Refusing"/>, which every sampler that picks a place already calls before it picks,
/// so a barred square is never on any shortlist: not a hearth, not a vein, not a refuge, not a corpse. It
/// never expires and arriving does not clear it — those are both properties of a measurement, and this is
/// not one. The only thing that lifts it is somebody editing <c>bot-movement.json</c>.
/// </para>
///
/// <para>
/// <b>Kept honest by its own counter.</b> A gate with no bucket either breaks somebody else's denominator
/// or files its refusals under somebody else's name — this project's most repeated instrumentation defect —
/// so what this turns away is counted here, by square, and printed beside the ledger's own numbers rather
/// than inside them.
/// </para>
/// </summary>
public static class BotBarred
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotBarred));

    public static bool Running { get; set; } = true;

    /// <summary>One piece of ground nobody may want, and why it is on the list.</summary>
    public sealed class Bar
    {
        public Bar(int x1, int y1, int x2, int y2, string why)
        {
            X1 = Math.Min(x1, x2);
            Y1 = Math.Min(y1, y2);
            X2 = Math.Max(x1, x2);
            Y2 = Math.Max(y1, y2);
            Why = why;
        }

        public int X1 { get; }

        public int Y1 { get; }

        public int X2 { get; }

        public int Y2 { get; }

        public string Why { get; }

        public long Turned;

        public bool Holds(int x, int y) => x >= X1 && x <= X2 && y >= Y1 && y <= Y2;

        public override string ToString() => $"({X1},{Y1})-({X2},{Y2}) {Why}";
    }

    private static readonly List<Bar> _bars =
    [
        new(2155, 1352, 2230, 1405, "a walled pocket east of Britain with no way in — Patrick's order, 11.09.2026"),
        new(1312, 1035, 1382, 1120, "the trap north of Britain round the trolls at (1318–1320, 1047): twelve bots carried home out of (1320–1378, 1048–1116) and 72 hunts refused a road to the trolls on 14–15.09.2026 — Patrick's word, 15.09.2026"),
        new(1020, 2110, 1240, 2295, "the water trap south-west of Britain: 32 bots carried home out of (1031–1230, 2119–2284) on 14–15.09.2026 — Patrick's word, 15.09.2026"),
        new(1905, 960, 2100, 1140, "the plague beast's bog north-east of Britain, until groups of thirty to fifty can be raised for it: about twelve bots died in it between 20:30 and 20:42 on 15.09.2026 while the quadrant record read it safe — Patrick's order, 15.09.2026")
    ];

    public static IReadOnlyList<Bar> Bars => _bars;

    public static int QuarantineMs { get; set; } = 7200000;

    public static int QuarantineHalf { get; set; } = 45;

    public static double QuarantinePower { get; set; } = 30000.0;

    public static int KillingDeaths { get; set; } = 3;

    public static int KillingWindowMs { get; set; } = 1800000;

    public static long Fields { get; private set; }

    private static readonly List<(Map Map, int X, int Y, long Tick)> _fallen = [];

    public static long Quarantined { get; private set; }

    public static long QuarantineTurned { get; private set; }

    private sealed class Quarantined_
    {
        public Bar Box;

        public long Until;

        public int Deaths;
    }

    private static readonly List<Quarantined_> _quarantine = [];

    public static bool Quarantines(Mobile killer) =>
        killer is Server.Mobiles.BaseCreature beast
        && (beast.GetType().Name.StartsWith("PlagueBeast", StringComparison.Ordinal) || BotThreat.Power(beast) >= QuarantinePower);

    public static long Spared { get; private set; }

    private static bool Homely(Map map, Point3D where)
    {
        var half = QuarantineHalf;

        if (map != null && Region.Find(where, map)?.IsPartOf<GuardedRegion>() == true)
        {
            return true;
        }

        if (Math.Max(Math.Abs(BotPopulation.Where.X - where.X), Math.Abs(BotPopulation.Where.Y - where.Y)) <= half)
        {
            return true;
        }

        if (BotResidence.Homely(where, half))
        {
            return true;
        }

        foreach (var (_, hall) in BotEstate.Held)
        {
            if (hall is { Deleted: false } && Math.Max(Math.Abs(hall.X - where.X), Math.Abs(hall.Y - where.Y)) <= half)
            {
                return true;
            }
        }

        foreach (var (_, seat) in BotSeat.Overrides)
        {
            if (Math.Max(Math.Abs(seat.X - where.X), Math.Abs(seat.Y - where.Y)) <= half)
            {
                return true;
            }
        }

        return false;
    }

    public static void Fell(Map map, Point3D where, Mobile killer, string who)
    {
        if (!Running || map == null || map == Map.Internal)
        {
            return;
        }

        if (killer != null && Quarantines(killer))
        {
            Quarantine(map, where, $"{killer.Name} killed {who} here");

            return;
        }

        if (KillingDeaths <= 0 || KillingWindowMs <= 0)
        {
            return;
        }

        var now = Core.TickCount;

        for (var i = _fallen.Count - 1; i >= 0; i--)
        {
            if (now - _fallen[i].Tick > KillingWindowMs)
            {
                _fallen.RemoveAt(i);
            }
        }

        _fallen.Add((map, where.X, where.Y, now));

        var near = 0;

        for (var i = 0; i < _fallen.Count; i++)
        {
            var f = _fallen[i];

            if (f.Map == map && Math.Max(Math.Abs(f.X - where.X), Math.Abs(f.Y - where.Y)) <= QuarantineHalf)
            {
                near++;
            }
        }

        if (near < KillingDeaths)
        {
            return;
        }

        var before = Quarantined;

        Quarantine(
            map,
            where,
            $"a killing field: {near} bots dead within {QuarantineHalf} tiles in {KillingWindowMs / 60000} minutes, the last {who} by {killer?.Name ?? "something"}"
        );

        if (Quarantined > before)
        {
            Fields++;
        }
    }

    public static void Quarantine(Map map, Point3D where, string why)
    {
        if (!Running || map == null || map == Map.Internal || QuarantineMs <= 0)
        {
            return;
        }

        var now = Core.TickCount;

        for (var i = 0; i < _quarantine.Count; i++)
        {
            var q = _quarantine[i];

            if (q.Box.Holds(where.X, where.Y))
            {
                q.Until = now + QuarantineMs;
                q.Deaths++;
                Save();

                return;
            }
        }

        if (Homely(map, where))
        {
            Spared++;

            return;
        }

        var box = new Bar(where.X - QuarantineHalf, where.Y - QuarantineHalf, where.X + QuarantineHalf, where.Y + QuarantineHalf, why);

        _quarantine.Add(new Quarantined_ { Box = box, Until = now + QuarantineMs, Deaths = 1 });
        Quarantined++;
        Save();

        logger.Warning(
            "Quarantine: the ground ({X1},{Y1})-({X2},{Y2}) is closed to the population for {Minutes} minutes — {Why}",
            box.X1,
            box.Y1,
            box.X2,
            box.Y2,
            QuarantineMs / 60000,
            why
        );
    }

    private static bool InQuarantine(Point3D where)
    {
        var now = Core.TickCount;

        for (var i = _quarantine.Count - 1; i >= 0; i--)
        {
            var q = _quarantine[i];

            if (q.Until - now <= 0)
            {
                logger.Information("Quarantine lifted: ({X1},{Y1})-({X2},{Y2}) after {Deaths} deaths, {Turned} choices turned away", q.Box.X1, q.Box.Y1, q.Box.X2, q.Box.Y2, q.Deaths, q.Box.Turned);
                _quarantine.RemoveAt(i);
                Save();

                continue;
            }

            if (q.Box.Holds(where.X, where.Y))
            {
                q.Box.Turned++;
                QuarantineTurned++;

                return true;
            }
        }

        return false;
    }

    public static IEnumerable<(int X1, int Y1, int X2, int Y2, string Why, int Deaths, int MinutesLeft, long Turned)> Standing()
    {
        var now = Core.TickCount;

        for (var i = 0; i < _quarantine.Count; i++)
        {
            var q = _quarantine[i];
            var left = q.Until - now;

            if (left > 0)
            {
                yield return (q.Box.X1, q.Box.Y1, q.Box.X2, q.Box.Y2, q.Box.Why, q.Deaths, (int)(left / 60000), q.Box.Turned);
            }
        }
    }

    public static string StorePath { get; set; } = Path.Combine("Data", "bot-quarantines.json");

    private sealed class Kept
    {
        public int X1 { get; set; }
        public int Y1 { get; set; }
        public int X2 { get; set; }
        public int Y2 { get; set; }
        public string Why { get; set; }
        public int Deaths { get; set; }
        public DateTime UntilUtc { get; set; }
    }

    public static void Save()
    {
        try
        {
            var path = Path.Combine(Core.BaseDirectory, StorePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var now = Core.TickCount;
            List<Kept> kept = [];

            for (var i = 0; i < _quarantine.Count; i++)
            {
                var q = _quarantine[i];
                var left = q.Until - now;

                if (left > 0)
                {
                    kept.Add(new Kept { X1 = q.Box.X1, Y1 = q.Box.Y1, X2 = q.Box.X2, Y2 = q.Box.Y2, Why = q.Box.Why, Deaths = q.Deaths, UntilUtc = DateTime.UtcNow.AddMilliseconds(left) });
                }
            }

            File.WriteAllText(path, JsonSerializer.Serialize(kept, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception e)
        {
            logger.Warning(e, "The quarantines could not be written to {Path}", StorePath);
        }
    }

    public static void Load()
    {
        try
        {
            var path = Path.Combine(Core.BaseDirectory, StorePath);

            if (!File.Exists(path))
            {
                return;
            }

            var kept = JsonSerializer.Deserialize<List<Kept>>(File.ReadAllText(path)) ?? [];
            var now = Core.TickCount;
            var read = 0;

            for (var i = 0; i < kept.Count; i++)
            {
                var k = kept[i];
                var left = (long)(k.UntilUtc - DateTime.UtcNow).TotalMilliseconds;

                if (left <= 0 || Homely(Map.Felucca, new Point3D((k.X1 + k.X2) / 2, (k.Y1 + k.Y2) / 2, 0)))
                {
                    continue;
                }

                _quarantine.Add(new Quarantined_ { Box = new Bar(k.X1, k.Y1, k.X2, k.Y2, k.Why ?? "kept from the last session"), Until = now + left, Deaths = k.Deaths });
                read++;
            }

            if (read > 0)
            {
                logger.Information("Quarantines: {Read} read back from the last session, still in force", read);
            }
        }
        catch (Exception e)
        {
            logger.Warning(e, "The quarantines could not be read from {Path}", StorePath);
        }
    }

    public static IEnumerable<(int X1, int Y1, int X2, int Y2, string Why, int Deaths)> Quarantines()
    {
        for (var i = 0; i < _quarantine.Count; i++)
        {
            var q = _quarantine[i];

            yield return (q.Box.X1, q.Box.Y1, q.Box.X2, q.Box.Y2, q.Box.Why, q.Deaths);
        }
    }

    public static long Turned { get; private set; }

    public static void Set(IEnumerable<Bar> bars)
    {
        _bars.Clear();

        if (bars == null)
        {
            return;
        }

        foreach (var bar in bars)
        {
            if (bar != null)
            {
                _bars.Add(bar);
            }
        }
    }

    public static bool Holds(Map map, Point3D where)
    {
        if (!Running || map == null || map == Map.Internal)
        {
            return false;
        }

        for (var i = 0; i < _bars.Count; i++)
        {
            if (_bars[i].Holds(where.X, where.Y))
            {
                return true;
            }
        }

        var now = Core.TickCount;

        for (var i = 0; i < _quarantine.Count; i++)
        {
            var q = _quarantine[i];

            if (q.Until - now > 0 && q.Box.Holds(where.X, where.Y))
            {
                return true;
            }
        }

        return false;
    }

    public static bool Barred(Map map, Point3D where)
    {
        if (!Running || _bars.Count == 0 && _quarantine.Count == 0 || map == null || map == Map.Internal)
        {
            return false;
        }

        for (var i = 0; i < _bars.Count; i++)
        {
            if (!_bars[i].Holds(where.X, where.Y))
            {
                continue;
            }

            _bars[i].Turned++;
            Turned++;

            return true;
        }

        return _quarantine.Count > 0 && InQuarantine(where);
    }

    public static void Announce()
    {
        if (!Running)
        {
            logger.Information("No ground is barred to the population; every place on the island may be chosen");

            return;
        }

        for (var i = 0; i < _bars.Count; i++)
        {
            var bar = _bars[i];

            logger.Information(
                "Barred ground: nobody may take work inside ({X1}, {Y1})-({X2}, {Y2}) — {Why}",
                bar.X1,
                bar.Y1,
                bar.X2,
                bar.Y2,
                bar.Why
            );
        }
    }

    public static string Describe()
    {
        if (!Running)
        {
            return "no ground is barred";
        }

        if (_bars.Count == 0)
        {
            return "no ground is barred";
        }

        List<string> said = [];

        for (var i = 0; i < _bars.Count; i++)
        {
            said.Add($"({_bars[i].X1},{_bars[i].Y1})-({_bars[i].X2},{_bars[i].Y2}) turned {_bars[i].Turned}");
        }

        var now = Core.TickCount;
        var inForce = 0;

        for (var i = 0; i < _quarantine.Count; i++)
        {
            if (_quarantine[i].Until - now > 0)
            {
                inForce++;
            }
        }

        return $"{_bars.Count} pieces of ground are barred outright, turning {Turned} choices away: {string.Join("; ", said)}; "
            + $"{Quarantined} quarantines declared ({Fields} of them killing fields of {KillingDeaths} dead in {KillingWindowMs / 60000} minutes), "
            + $"{inForce} in force, {QuarantineTurned} choices turned by them, {Spared} spared for being home";
    }

    public static void Forget()
    {
        Turned = 0;
        Fields = 0;
        _fallen.Clear();

        for (var i = 0; i < _bars.Count; i++)
        {
            _bars[i].Turned = 0;
        }
    }
}
