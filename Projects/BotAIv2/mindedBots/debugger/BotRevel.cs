using System;
using System.Collections.Generic;
using Server.BotAI.V2;
using Server.Guilds;
using Server.Logging;
using Server.Mobiles;
using Server.Multis;
using Server.Text;

namespace Server.BotAI.Mind;

/// <summary>
/// Something for the population to do that nobody planned: a bounty, a hunt, a contest.
///
/// <para>
/// <b>Ordered by Patrick on 08.09.2026 — the watcher thinks, so let it think of something worth doing.</b>
/// Argus already reads the whole shard every ten minutes and forms an opinion about it. This turns that
/// opinion into an event: it looks at what the population is short of, what it has too much of, who is idle
/// and who is rich, and declares a revel.
/// </para>
///
/// <para>
/// <b>The design rule that makes this possible at all: a revel invents no mechanism.</b> Bots know nothing
/// of events and never will. What a revel does is move two things they already answer to — the price of a
/// kind of work, and the world itself. Declare rock worth triple for twelve minutes and the miners go;
/// put an orc camp on the ridge and the hunters go. Nothing is scheduled, nothing is assigned, and a revel
/// that nobody enters simply pays nobody, which is itself a measurement.
/// </para>
///
/// <para>
/// <b>The prize comes out of a fixed treasury, and that is deliberate.</b> Money minted for a party is
/// still money minted: this shard has already paid once for coin entering the world faster than it left —
/// see the note on shopkeepers' shelves. So the crown has <see cref="Treasury"/> for the session and no
/// more; when it is spent, revels go on being declared and are paid in the only other currency the
/// population has, which is a fight worth having.
/// </para>
/// </summary>
public static class BotRevel
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotRevel));

    public static int EveryMs { get; set; } = 900000;

    public static int HoldsMs { get; set; } = 720000;

    public static int Treasury { get; set; } = 6000;

    public static int Purse { get; private set; } = 6000;

    public static int TaxMs { get; set; } = 900000;

    public static double TaxShare { get; set; } = 0.06;

    public static double BandShare { get; set; } = 0.5;

    public static long Collected { get; private set; }

    public static long Bands { get; private set; }

    public static int MostPrize { get; set; } = 1200;

    public static int EnoughForPrize { get; set; } = 20;

    public static double Bonus { get; set; } = 3.0;

    public static long Declared { get; private set; }

    public static long Won { get; private set; }

    public static long Ignored { get; private set; }

    public static long Paid { get; private set; }

    public static long Camps { get; private set; }

    public static string Kind { get; private set; }

    public static int Prize { get; private set; }

    public static string Why { get; private set; }

    public static string Said { get; private set; }

    public static Point3D Where { get; private set; }

    public static int NearestCamp { get; set; } = 30;

    public static int FurthestCamp { get; set; } = 120;

    public static long Placed { get; private set; }

    public static long Hauled { get; private set; }

    public static Map Ground { get; private set; }

    public static bool Pitched { get; private set; }

    private static readonly List<string> _past = [];

    private static long _startedTick;

    private static long _thoughtTick;

    private static readonly Dictionary<Serial, int> _tally = [];

    private static readonly Dictionary<string, int> _bands = [];

    private static long _taxedTick;

    private static bool _taxed;

    private static bool _halfWay;

    private static readonly Dictionary<string, (int Declared, int Entrants, int Paid)> _ledger = [];

    public static bool Running => Kind != null && Core.TickCount - _startedTick < HoldsMs;

    public static double Worth(string kind) =>
        Running && string.Equals(kind, Kind, StringComparison.OrdinalIgnoreCase) ? Bonus : 1.0;

    public static bool Due()
    {
        if (Running || Core.TickCount - _thoughtTick < EveryMs)
        {
            return false;
        }

        _thoughtTick = Core.TickCount;

        return true;
    }

    public static void Did(Mobile bot, string kind)
    {
        if (bot == null || !Running || !string.Equals(kind, Kind, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _tally[bot.Serial] = _tally.TryGetValue(bot.Serial, out var had) ? had + 1 : 1;

        if (bot.Guild is Guild guild)
        {
            _bands[guild.Name] = _bands.TryGetValue(guild.Name, out var many) ? many + 1 : 1;
        }
    }

    public static void Tax()
    {
        var now = Core.TickCount;

        if (!_taxed)
        {
            _taxed = true;
            _taxedTick = now;

            return;
        }

        if (now - _taxedTick < TaxMs)
        {
            return;
        }

        _taxedTick = now;

        var got = 0;

        foreach (var guild in BotGuilds.Standing)
        {
            var spare = BotEstate.Fund(guild);

            if (spare <= 0)
            {
                continue;
            }

            got += BotEstate.Tax(guild, (int)(spare * TaxShare));
        }

        if (got <= 0)
        {
            return;
        }

        Purse += got;
        Collected += got;

        logger.Information(
            "The crown collected {Got}gp from the guilds; its purse stands at {Purse}gp",
            got,
            Purse
        );
    }

    public static string Declare(string kind, int prize, string say, string why, bool camp, Point3D where)
    {
        if (string.IsNullOrWhiteSpace(kind))
        {
            return "no trade named, so nothing was declared.";
        }

        if (Running)
        {
            return $"a revel of {Kind} is already on for {Math.Max(1, (HoldsMs - (Core.TickCount - _startedTick)) / 60000)} more minutes; nothing was declared.";
        }

        if (Kind != null)
        {
            Settle();
        }

        var body = BotVigil.Body;
        var map = body?.Map;

        if (map == null || map == Map.Internal)
        {
            return "the watcher is nowhere, so it can declare nothing.";
        }

        Kind = kind.Trim().ToLowerInvariant();
        Prize = Math.Clamp(prize, 0, Math.Min(MostPrize, Purse));
        Why = why;
        Said = say;
        _startedTick = Core.TickCount;
        _tally.Clear();
        _bands.Clear();
        _halfWay = false;
        Declared++;

        var seen = _ledger.TryGetValue(Kind, out var had) ? had : default;

        _ledger[Kind] = (seen.Declared + 1, seen.Entrants, seen.Paid);

        Ground = BotPopulation.Home ?? map;
        Where = BotPopulation.Where;
        Pitched = false;

        var raised = "";

        if (camp)
        {
            var at = Ring(Where, where == Point3D.Zero ? Where : where);

            if (where != Point3D.Zero)
            {
                Placed++;
            }

            if (Footing(map, at, out var pitchAt, out var z))
            {
                at = pitchAt;

                var pitch = new OrcCamp();

                pitch.MoveToWorld(new Point3D(at.X, at.Y, z), map);
                pitch.DecayDelay = TimeSpan.FromMilliseconds(HoldsMs);
                Camps++;

                Where = new Point3D(at.X, at.Y, z);
                Pitched = true;

                raised = $" A camp stands at ({at.X}, {at.Y}).";
            }
        }

        if (!string.IsNullOrWhiteSpace(say))
        {
            body.Say(say.Length > 180 ? say[..180] : say);
        }

        logger.Information(
            "A revel is declared: {Kind} is worth ×{Bonus} for {Minutes} minutes, prize {Prize}gp — {Why}{Raised}",
            Kind,
            Bonus,
            HoldsMs / 60000,
            Prize,
            Why,
            raised
        );

        BotDebugLog.Rule();
        BotDebugLog.Write($"REVEL: {Kind} ×{Bonus} for {HoldsMs / 60000} minutes, prize {Prize}gp");
        BotDebugLog.Block("  because:", Why ?? "no reason given");
        BotDebugLog.Rule();

        return $"declared: {Kind} is worth ×{Bonus} for {HoldsMs / 60000} minutes, prize {Prize}gp.{raised}";
    }

    public static void Settle()
    {
        if (Kind == null)
        {
            return;
        }

        if (Core.TickCount - _startedTick < HoldsMs)
        {
            Standings();

            return;
        }

        var was = Kind;
        var prize = Prize;
        var band = Band();
        var entered = _tally.Count;

        Kind = null;
        Prize = 0;

        Mobile best = null;
        var bestDid = 0;

        var done = 0;

        foreach (var (serial, did) in _tally)
        {
            done += did;

            if (did <= bestDid)
            {
                continue;
            }

            var who = World.FindMobile(serial);

            if (who is { Deleted: false })
            {
                best = who;
                bestDid = did;
            }
        }

        _tally.Clear();

        if (best == null)
        {
            Ignored++;
            _bands.Clear();
            Remember($"{was}: nobody came, {prize}gp unclaimed");

            logger.Information("The revel for {Kind} ended with nobody having done any", was);
            BotVigil.Body?.Say($"The {was} revel ends. Nobody came.");

            return;
        }

        Won++;

        if (EnoughForPrize > 0 && done < EnoughForPrize)
        {
            prize = prize * done / EnoughForPrize;
        }

        var was2 = _ledger.TryGetValue(was, out var seen2) ? seen2 : default;

        _ledger[was] = (was2.Declared, was2.Entrants + entered, was2.Paid + prize);

        var single = band == null ? prize : (int)(prize * (1.0 - BandShare));

        if (single > 0 && Banker.Deposit(best, single))
        {
            Paid += single;
            Purse = Math.Max(0, Purse - single);
        }

        if (band != null)
        {
            var pot = prize - single;
            var shared = Share(band, pot);

            if (shared > 0)
            {
                Bands++;
                Paid += shared;
                Purse = Math.Max(0, Purse - shared);

                BotVigil.Body?.Say($"{band.Name} did most of it between them. {shared}gp split among them.");
            }
        }

        _bands.Clear();

        logger.Information(
            "The revel for {Kind} is won by {Name} with {Did} of them, {Done} done in all, and {Prize}gp is paid; {Left}gp left in the treasury",
            was,
            best.Name,
            bestDid,
            done,
            prize,
            Math.Max(0, Treasury - (int)Paid)
        );

        BotVigil.Body?.Say($"{best.Name} wins the {was} revel with {bestDid}. {prize}gp to the winner.");

        Remember($"{was}: {best.Name} won with {bestDid}, {prize}gp paid");
    }

    private static void Standings()
    {
        if (_halfWay || Core.TickCount - _startedTick < HoldsMs / 2)
        {
            return;
        }

        _halfWay = true;

        var band = Band();

        if (band == null)
        {
            BotVigil.Body?.Say($"Half the {Kind} revel gone and not one of you has taken it up.");

            return;
        }

        var leader = band.Leader?.Name ?? "somebody";

        BotVigil.Body?.Say($"{band.Name} leads the {Kind} revel. {leader}, hold it.");
    }

    public static string Ledger()
    {
        if (_ledger.Count == 0)
        {
            return "none have been held yet";
        }

        var say = Server.Text.ValueStringBuilder.Create(256);

        try
        {
            foreach (var (kind, (declared, entrants, paid) ) in _ledger)
            {
                if (say.Length > 0)
                {
                    say.Append("; ");
                }

                say.Append(kind);
                say.Append(": ");
                say.Append(declared.ToString());
                say.Append(" held, ");
                say.Append(entrants.ToString());
                say.Append(" took it up, ");
                say.Append(paid.ToString());
                say.Append("gp paid");
            }

            return say.ToString();
        }
        finally
        {
            say.Dispose();
        }
    }

    public static string Leaders()
    {
        var say = Server.Text.ValueStringBuilder.Create(192);

        try
        {
            foreach (var guild in BotGuilds.Standing)
            {
                if (say.Length > 0)
                {
                    say.Append(", ");
                }

                say.Append(guild.Name);
                say.Append(" under ");
                say.Append(guild.Leader?.Name ?? "nobody");
            }

            return say.Length == 0 ? "there are no guilds" : say.ToString();
        }
        finally
        {
            say.Dispose();
        }
    }

    private static Guild Band()
    {
        Guild best = null;
        var bestDid = 0;

        foreach (var (name, did) in _bands)
        {
            if (did <= bestDid)
            {
                continue;
            }

            var guild = BotGuilds.Find(name);

            if (guild != null)
            {
                best = guild;
                bestDid = did;
            }
        }

        return best;
    }

    private static int Share(Guild guild, int pot)
    {
        if (guild?.Members == null || pot <= 0)
        {
            return 0;
        }

        var many = 0;

        for (var i = 0; i < guild.Members.Count; i++)
        {
            if (guild.Members[i] is BotMobile { Deleted: false })
            {
                many++;
            }
        }

        if (many == 0)
        {
            return 0;
        }

        var each = Math.Max(1, pot / many);
        var given = 0;

        for (var i = 0; i < guild.Members.Count && given + each <= pot; i++)
        {
            if (guild.Members[i] is BotMobile { Deleted: false } member && Banker.Deposit(member, each))
            {
                given += each;
            }
        }

        return given;
    }

    private static void Remember(string line)
    {
        _past.Insert(0, line);

        while (_past.Count > 5)
        {
            _past.RemoveAt(_past.Count - 1);
        }
    }

    public static BotNotice Notice()
    {
        var body = BotVigil.Body;
        var running = Running;

        Mobile best = null;
        var bestDid = 0;

        if (running)
        {
            foreach (var (serial, did) in _tally)
            {
                if (did <= bestDid)
                {
                    continue;
                }

                var who = World.FindMobile(serial);

                if (who is { Deleted: false })
                {
                    best = who;
                    bestDid = did;
                }
            }
        }

        return new BotNotice
        {
            Running = running,
            Kind = Kind,
            Prize = Prize,
            Bonus = Bonus,
            Said = Said,
            Why = Why,
            EndsIn = running ? HoldsMs - (Core.TickCount - _startedTick) : 0,
            NextIn = Math.Max(0, EveryMs - (Core.TickCount - _thoughtTick)),
            Entered = _tally.Count,
            Leading = best?.Name,
            LeadingDid = bestDid,
            Map = Ground,
            Where = Where,
            Camp = Pitched,
            WatcherMap = body?.Map,
            Watcher = body?.Location ?? Point3D.Zero,
            WatcherName = body?.Name,
            Declared = Declared,
            Won = Won,
            Ignored = Ignored,
            Paid = Paid,
            Treasury = Treasury,
            Purse = Purse,
            Collected = Collected,
            Bands = Bands,
            Past = _past.ToArray(),
            Wave = BotWaves.Wave,
            Standing = BotWaves.Standing,
            Waves = BotWaves.Describe(),
            Ledger = Ledger()
        };
    }

    public static string Describe() =>
        Declared == 0
            ? $"no revels have been declared; the crown holds {Purse}gp"
            : $"{Declared} revels declared, {Won} won and {Ignored} ignored, {Bands} of them by a whole band; "
            + $"{Paid}gp paid out and {Collected}gp taken in tax, the crown holding {Purse}gp; "
            + $"{Camps} camps raised for them ({Placed} on a spot the watcher chose, {Hauled} of those pulled back into the ring, "
            + $"{Inland} moved off the water, {Awash} refused for standing in it, {Walled} refused for standing in a town, {Unpitched} that found no ground to stand on)"
            + (Running ? $"; running now: {Kind} at ×{Bonus} for {Prize}gp" : "");

    public static void Forget()
    {
        Kind = null;
        Prize = 0;
        Why = null;
        Said = null;
        Where = Point3D.Zero;
        Ground = null;
        Pitched = false;
        _past.Clear();
        _tally.Clear();
        Declared = 0;
        Won = 0;
        Ignored = 0;
        Paid = 0;
        Camps = 0;
        Placed = 0;
        Hauled = 0;
        Unpitched = 0;
        Inland = 0;
        Awash = 0;
        Walled = 0;
        Collected = 0;
        Bands = 0;
        Purse = Treasury;
        _bands.Clear();
        _ledger.Clear();
        _halfWay = false;
        _taxed = false;
    }

    public const string Schema =
        """
        {"type":"object","properties":{"kind":{"type":"string"},"prize":{"type":"number"},"camp":{"type":"boolean"},"x":{"type":"number"},"y":{"type":"number"},"say":{"type":"string","minLength":20},"why":{"type":"string","minLength":40}},"required":["kind","prize","camp","say","why"]}
        """;

    public static readonly string[] Trades =
    [
        "mine", "chop", "hunt", "cook", "sew", "forge", "brew",
        "inscribe", "herbs", "forage", "peddle", "plunder", "liberate"
    ];

    public static bool Known(string kind)
    {
        if (string.IsNullOrWhiteSpace(kind))
        {
            return false;
        }

        var want = kind.Trim();

        for (var i = 0; i < Trades.Length; i++)
        {
            if (string.Equals(Trades[i], want, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    public static long Unpitched { get; private set; }

    public static long Inland { get; private set; }

    public static long Awash { get; private set; }

    public static long Walled { get; private set; }

    public static int CampDry { get; set; } = 7;

    public static int CampSweep { get; set; } = 32;

    public static int CampStep { get; set; } = 4;

    public static bool Footing(Map map, Point3D near, out Point3D found, out sbyte z)
    {
        found = near;

        var wet = false;
        var town = false;

        if (BotStep.Settle(map, near.X, near.Y, out z))
        {
            if (Outdoors(map, near.X, near.Y))
            {
                if (BotStep.Dry(map, near.X, near.Y, CampDry))
                {
                    return true;
                }

                wet = true;
            }
            else
            {
                town = true;
            }
        }

        for (var step = CampStep; step <= CampSweep; step += CampStep)
        {
            for (var dir = 0; dir < 8; dir++)
            {
                var angle = dir * Math.PI / 4.0;
                var x = near.X + (int)Math.Round(Math.Cos(angle) * step);
                var y = near.Y + (int)Math.Round(Math.Sin(angle) * step);

                if (!BotStep.Settle(map, x, y, out z))
                {
                    continue;
                }

                if (!Outdoors(map, x, y))
                {
                    town = true;

                    continue;
                }

                if (!BotStep.Dry(map, x, y, CampDry))
                {
                    wet = true;

                    continue;
                }

                found = new Point3D(x, y, z);

                if (wet)
                {
                    Inland++;
                }

                return true;
            }
        }

        if (town)
        {
            Walled++;
        }
        else if (wet)
        {
            Awash++;
        }
        else
        {
            Unpitched++;
        }

        return false;
    }

    private static bool Outdoors(Map map, int x, int y)
    {
        for (var dy = -CampDry; dy <= CampDry; dy += CampDry)
        {
            for (var dx = -CampDry; dx <= CampDry; dx += CampDry)
            {
                if (Region.Find(new Point3D(x + dx, y + dy, 0), map)?.IsPartOf<Server.Regions.GuardedRegion>() == true)
                {
                    return false;
                }
            }
        }

        return true;
    }

    public static Point3D Ring(Point3D home, Point3D asked)
    {
        double dx = asked.X - home.X;
        double dy = asked.Y - home.Y;
        var span = Math.Sqrt(dx * dx + dy * dy);

        if (span < 1.0)
        {
            Hauled++;

            return new Point3D(home.X + NearestCamp, home.Y, home.Z);
        }

        var want = Math.Clamp(span, NearestCamp, FurthestCamp);

        if (Math.Abs(want - span) < 1.0)
        {
            return new Point3D(asked.X, asked.Y, home.Z);
        }

        Hauled++;

        return new Point3D(
            home.X + (int)Math.Round(dx / span * want),
            home.Y + (int)Math.Round(dy / span * want),
            home.Z
        );
    }

    public static string System(string name) =>
        $"You are {name}, the watcher of a shard of autonomous bots. Every so often you may declare a revel: "
        + "a contest that makes one kind of work worth three times as much for twelve minutes, with a prize "
        + "of gold to whoever does most of it. You are looking at the population's own numbers. Choose work "
        + "that is being neglected, or that the island is short of, or that would put the bots somewhere "
        + "interesting — and set 'camp' true when you want an orc camp raised for them to fight over. "
        + $"When you raise a camp you also choose where it stands. The population lives at "
        + $"({BotPopulation.Where.X}, {BotPopulation.Where.Y}); give 'x' and 'y' for the camp as map "
        + $"coordinates between {NearestCamp} and {FurthestCamp} tiles from that point — closer than that "
        + "and it is not somewhere to go, further and nobody will walk to it. Your direction is always "
        + "kept; only a distance outside those bounds is corrected, and being corrected is a sign you have "
        + "misjudged the scale. Leave x and y out to have it pitched beside the population. "
        + "Answer 'nothing' when the shard is busy and healthy and needs no prodding; that is a good answer "
        + "and you should give it often. 'say' is what you shout to the island, in the voice of a herald. "
        + "'why' is your reasoning, for the log. 'kind' must be exactly one of: "
        + string.Join(", ", Trades) + ", or the word nothing. 'prize' is a whole number of gold from 0 to "
        + MostPrize + ". Half of what you offer goes to the single bot that does most of it and half to the "
        + "guild that does most of it between them, so a revel is a contest between the bands as well. "
        + $"The guilds are: {Leaders()}. Your treasury holds {Purse} gold and is filled by a small tax on "
        + "those guilds, so a prize you do not need to offer is a prize you keep. What your past revels came "
        + $"to: {Ledger()} — a kind that has been held more than once and drawn nobody is a price the "
        + "population walked past, and declaring it again is not reading your own results.";
}

/// <summary>
/// What the model answered when asked to think of a revel.
///
/// A record of its own rather than a tuple, for the same reason <c>BotDebugNote</c> is one: the fields are
/// what the schema promises, and a mismatch between the two should be a compile error rather than a silent
/// null at two in the morning.
/// </summary>
public sealed class BotRevelPlan
{
    public string Kind { get; init; }

    public int Prize { get; init; }

    public bool Camp { get; init; }

    public Point3D Spot { get; init; }

    public string Say { get; init; }

    public string Why { get; init; }

    public static BotRevelPlan Read(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.ValueKind != System.Text.Json.JsonValueKind.Object)
            {
                return null;
            }

            return new BotRevelPlan
            {
                Kind = root.TryGetProperty("kind", out var kind) ? kind.GetString() : null,
                Prize = root.TryGetProperty("prize", out var prize) && prize.TryGetInt32(out var got) ? got : 0,
                Camp = root.TryGetProperty("camp", out var camp) && camp.ValueKind == System.Text.Json.JsonValueKind.True,
                Say = root.TryGetProperty("say", out var say) ? say.GetString() : null,
                Why = root.TryGetProperty("why", out var why) ? why.GetString() : null,
                Spot = root.TryGetProperty("x", out var x) && x.TryGetInt32(out var ax)
                       && root.TryGetProperty("y", out var y) && y.TryGetInt32(out var ay)
                    ? new Point3D(ax, ay, 0)
                    : Point3D.Zero
            };
        }
        catch
        {
            return null;
        }
    }
}
