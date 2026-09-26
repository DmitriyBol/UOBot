using System;
using System.Collections.Generic;
using Server.Logging;

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

    public static bool Barred(Map map, Point3D where)
    {
        if (!Running || _bars.Count == 0 || map == null || map == Map.Internal)
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

        return false;
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

        return $"{_bars.Count} pieces of ground are barred outright, turning {Turned} choices away: {string.Join("; ", said)}";
    }

    public static void Forget()
    {
        Turned = 0;

        for (var i = 0; i < _bars.Count; i++)
        {
            _bars[i].Turned = 0;
        }
    }
}
