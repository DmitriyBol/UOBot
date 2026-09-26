using System;
using System.Collections.Generic;
using Server.BotAI.V2;
using Server.Items;
using Server.Mobiles;
using Server.Text;

namespace Server.BotAI.Mind;

/// <summary>
/// One bot as the debugger has actually seen it: everything here was measured by this file, on this file's
/// own clock, since the moment the debugger first laid eyes on the bot.
///
/// <para>
/// <b>Nothing is read out of a stamp the bot is carrying, and that is deliberate.</b> A bot holds several
/// tick stamps — when it took its work on, when its work last said anything but "working" — and every one
/// of them is unusable for this purpose the moment it has not been set: on some hosts the tick counter is
/// the machine's uptime and starts enormous, so an unseeded stamp does not read as "never", it reads as
/// "eleven days". A debugger built on those would report a population that had been frozen since before the
/// shard started. So the durations below are all of the form "for as long as I have been watching", which is
/// a smaller claim and a true one, and the prompt says so in those words.
/// </para>
///
/// <para>
/// <b>Every symptom carries the number it was raised on.</b> A row that says "stuck" is worth nothing; a row
/// that says "has not left 1441,1502 for 11 minutes while its journey wants 1502,1601, 40 tiles off" is a
/// defect report. This is the same rule the shard's own summaries are held to — a count with no denominator
/// and no unit is a sentence that cannot be checked — and it matters more here than anywhere else, because
/// what reads these rows is a language model, and a model handed an adjective will produce a paragraph of
/// adjectives back.
/// </para>
/// </summary>
public sealed class BotWatch
{
    public static int FrozenMs { get; set; } = 90000;

    public static int ImmortalMs { get; set; } = 300000;

    public static int QuickMs { get; set; } = 15000;

    public static int SettledMs { get; set; } = 1200000;

    public static int GaveUpQuietMs { get; set; } = 10000;

    public static int PacingSpan { get; set; } = 2;

    public static int PacedMs { get; set; } = 120000;

    public static int PacingStirs { get; set; } = 3;

    public static int FutileMs { get; set; } = 20000;

    private sealed class Trade
    {
        public int Taken;

        public int Quick;

        public long HeldMs;

        public int Gained;

        public int Made;

        public int Learned;
    }

    private readonly Dictionary<string, Trade> _trades = [];

    private Point3D _was;

    private int _lowX;

    private int _highX;

    private int _lowY;

    private int _highY;

    private long _patchSince;

    private int _stirs;

    private bool _patched;

    private Point3D _stirred;

    private Mobile _foe;

    private int _foeHits;

    private Point3D _goal;

    private long _goalSince;

    private bool _going;

    private int _closest;

    private int _slack = 1;

    private string _kind = "-";

    private long _kindSince;

    private int _kindWorth;

    private int _kindMade;

    private int _kindSkill;

    private BotDeed _deed;

    private long _stillSince;

    private bool _seen;

    public BotWatch(BotMobile bot, long now)
    {
        Bot = bot;
        Name = bot.Name;
        Class = bot.Class?.Name ?? "no class";
        FirstTick = now;
        _kindSince = now;
        _stillSince = now;
        _was = bot.Location;
        FirstWorth = Coin(bot);
        FirstProgress = bot.Progress;
    }

    public BotMobile Bot { get; }

    public string Name { get; private set; }

    public string Class { get; }

    public long FirstTick { get; }

    public int FirstWorth { get; }

    public int Worth { get; private set; }

    public int Pack { get; private set; }

    public int Bank { get; private set; }

    public int Skill { get; private set; }

    public double FirstProgress { get; }

    public double Progress { get; private set; }

    public double Mood { get; private set; }

    public string Standing { get; private set; } = "-";

    public string Kind => _kind;

    public long HeldMs { get; private set; }

    public long IdleMs =>
        _kind == "-" && !string.Equals(Standing, "Bound", StringComparison.Ordinal) ? HeldMs : 0;

    public long StillMs { get; private set; }

    public long FrozenForMs { get; private set; }

    public long WorkingForMs { get; private set; }

    public int Deeds { get; private set; }

    public long Takes { get; private set; }

    public int Quick { get; private set; }

    public int Refusals { get; private set; }

    public int Hopeless { get; private set; }

    public long NoCloserMs { get; private set; }

    public long GoingMs { get; private set; }

    public static int WorthSayingMs { get; set; } = 20000;

    public long PacingMs { get; private set; }

    public int Patch { get; private set; } = 1;

    public int Stirs => _stirs;

    public string Foe { get; private set; }

    public int FoeAway { get; private set; }

    public int FoeHigh { get; private set; }

    public int Reach { get; private set; } = 1;

    public long SwingingMs { get; private set; }

    public bool BeyondReach { get; private set; }

    public bool Overhead { get; private set; }

    public bool Fighting => Foe != null;

    public bool Following { get; private set; }

    public int Abandoned { get; private set; }

    public int ShortBy { get; private set; }

    public double BarrenMinutes { get; private set; }

    public double DeadMinutes { get; private set; }

    public Point3D Where { get; private set; }

    public string Region { get; private set; } = "-";

    public Point3D Wants { get; private set; }

    public int WantsAway { get; private set; }

    public int Slack => _slack;

    public string Why { get; private set; }

    public string Empty { get; private set; }

    public string Asked { get; private set; } = "-";

    public double Suspicion { get; private set; }

    public string Symptoms { get; private set; } = "";

    public long WatchedMs(long now) => now - FirstTick;

    private static int Coin(BotMobile bot) => (bot.Backpack?.TotalGold ?? 0) + Banker.GetBalance(bot);

    private static (int Pack, int Bank) Purse(BotMobile bot) =>
        (bot.Backpack?.TotalGold ?? 0, Banker.GetBalance(bot));

    public void Sample(long now, long sinceMs)
    {
        var bot = Bot;

        if (bot is not { Deleted: false })
        {
            return;
        }

        Name = bot.Name;
        Where = bot.Location;
        Region = bot.Region?.Name ?? "nowhere";
        (Pack, Bank) = Purse(bot);
        Worth = Pack + Bank;
        Skill = bot.SkillsTotal;
        Progress = bot.Progress;
        Mood = bot.Mood;

        var resolve = bot.Resolve;
        var journey = bot.Journey;

        Standing = resolve?.Standing.ToString() ?? "-";
        Why = resolve?.Because;
        Empty = resolve?.Empty;
        Refusals = bot.Refusals;

        if (!bot.Alive)
        {
            DeadMinutes += sinceMs / 60000.0;
        }

        if (resolve?.Urges is { IsBarren: true })
        {
            BarrenMinutes += sinceMs / 60000.0;
        }

        var sent = resolve?.Sent ?? default;

        Asked = sent.Kind == BotDoingKind.None ? "-" : sent.Kind.ToString().ToLowerInvariant();

        var wants = journey is { Active: true };

        Following = wants && journey.Current?.Follow != null;

        Wants = wants ? journey.Target : Point3D.Zero;
        WantsAway = wants ? (int)bot.GetDistanceToSqrt(journey.Target) : 0;

        if (journey is { Hopeless: true })
        {
            Hopeless++;
        }

        Fight(bot, sinceMs);

        Travel(now, wants, sinceMs, journey);

        Pace(now, wants);

        if (bot.Location == _was)
        {
            StillMs = now - _stillSince;

            if (wants && journey.Walking && WantsAway > Math.Max(1, journey.Arrival.Tiles))
            {
                FrozenForMs += sinceMs;
            }
        }
        else
        {
            _was = bot.Location;
            _stillSince = now;
            StillMs = 0;
            FrozenForMs = 0;
        }

        var work = resolve?.Deed;
        var kind = work?.Kind ?? "-";

        if (!_seen)
        {
            _seen = true;
            Takes++;
            _kind = kind;
            _kindSince = now;
            _kindWorth = Worth;
            _kindSkill = Skill;
        }
        else if (!ReferenceEquals(work, _deed))
        {
            var held = now - _kindSince;

            if (!string.Equals(_kind, "-", StringComparison.Ordinal))
            {
                var trade = Note(_kind);

                trade.Taken++;
                trade.HeldMs += held;
                trade.Gained += Worth - _kindWorth;
                trade.Made += _deed?.Made ?? _kindMade;
                trade.Learned += Skill - _kindSkill;

                Deeds++;
                Takes++;

                if (held < QuickMs)
                {
                    trade.Quick++;
                    Quick++;
                }
            }

            _kind = kind;
            _kindSince = now;
            _kindWorth = Worth;
            _kindMade = 0;
            _kindSkill = Skill;
            WorkingForMs = 0;
        }

        _deed = work;
        HeldMs = now - _kindSince;

        _kindMade = work?.Made ?? _kindMade;

        if (sent.Kind == BotDoingKind.Work)
        {
            WorkingForMs += sinceMs;
        }
        else
        {
            WorkingForMs = 0;
        }

        Weigh(now);
    }

    private void Travel(long now, bool wants, long sinceMs, BotJourney journey)
    {
        var goal = wants ? journey.Target : Point3D.Zero;

        if (_going && (!wants || goal != _goal))
        {
            _going = false;

            if (_closest > _slack && NoCloserMs >= GaveUpQuietMs)
            {
                Abandoned++;
                ShortBy = _closest;
            }
        }

        if (!wants)
        {
            NoCloserMs = 0;
            GoingMs = 0;

            return;
        }

        _slack = Math.Max(1, journey.Arrival.Tiles);

        GoingMs = _going ? now - _goalSince : 0;

        if (!_going)
        {
            _going = true;
            _goal = goal;
            _goalSince = now;
            _closest = WantsAway;
            NoCloserMs = 0;
        }
        else if (WantsAway < _closest)
        {
            _closest = WantsAway;
            NoCloserMs = 0;
        }
        else if (WantsAway > _slack)
        {
            NoCloserMs += sinceMs;
        }
        else
        {
            NoCloserMs = 0;
        }
    }

    private void Pace(long now, bool wants)
    {
        var at = Where;

        if (!_patched)
        {
            Fresh(at, now);

            return;
        }

        var lowX = Math.Min(_lowX, at.X);
        var highX = Math.Max(_highX, at.X);
        var lowY = Math.Min(_lowY, at.Y);
        var highY = Math.Max(_highY, at.Y);

        if (highX - lowX >= PacingSpan || highY - lowY >= PacingSpan)
        {
            Fresh(at, now);

            return;
        }

        _lowX = lowX;
        _highX = highX;
        _lowY = lowY;
        _highY = highY;

        Patch = Math.Max(highX - lowX, highY - lowY) + 1;

        if (at != _stirred)
        {
            _stirs++;
            _stirred = at;
        }

        var arrived = wants && WantsAway <= _slack;

        PacingMs = _stirs >= PacingStirs && !arrived ? now - _patchSince : 0;
    }

    private void Fresh(Point3D at, long now)
    {
        _patched = true;
        _lowX = _highX = at.X;
        _lowY = _highY = at.Y;
        _stirred = at;
        _patchSince = now;
        _stirs = 0;
        Patch = 1;
        PacingMs = 0;
    }

    private void Fight(BotMobile bot, long sinceMs)
    {
        var foe = bot.Combatant;

        if (foe is not { Deleted: false, Alive: true } || foe.Map != bot.Map)
        {
            _foe = null;
            Foe = null;
            FoeAway = 0;
            FoeHigh = 0;
            SwingingMs = 0;
            BeyondReach = false;
            Overhead = false;

            return;
        }

        if (!ReferenceEquals(foe, _foe))
        {
            _foe = foe;
            _foeHits = foe.Hits;
            SwingingMs = 0;
        }

        Foe = foe.Name;
        FoeAway = (int)bot.GetDistanceToSqrt(foe.Location);
        FoeHigh = Math.Abs(bot.Z - foe.Z);
        Reach = Math.Max(1, bot.Weapon?.MaxRange ?? 1);

        BeyondReach = FoeAway > Reach;
        Overhead = FoeHigh >= BotArrival.PersonHeight;

        if (foe.Hits < _foeHits)
        {
            _foeHits = foe.Hits;
            SwingingMs = 0;

            return;
        }

        SwingingMs += sinceMs;
    }

    public void Tally(Dictionary<string, (int Taken, int Quick, long HeldMs, int Gained, int Made, int Learned, int Bots)> into)
    {
        if (into == null)
        {
            return;
        }

        foreach (var (kind, trade) in _trades)
        {
            var was = into.GetValueOrDefault(kind);

            into[kind] = (
                was.Taken + trade.Taken,
                was.Quick + trade.Quick,
                was.HeldMs + trade.HeldMs,
                was.Gained + trade.Gained,
                was.Made + trade.Made,
                was.Learned + trade.Learned,
                was.Bots + 1
            );
        }
    }

    private Trade Note(string kind)
    {
        if (_trades.TryGetValue(kind, out var trade))
        {
            return trade;
        }

        trade = new Trade();
        _trades[kind] = trade;

        return trade;
    }

    private void Weigh(long now)
    {
        var sb = ValueStringBuilder.Create(256);
        var score = 0.0;

        try
        {
            var said = 0;

            if (FrozenForMs >= FrozenMs)
            {
                score += Math.Min(3.0, FrozenForMs / (double)FrozenMs);

                Say(ref sb, ref said, $"has not left {Where.X},{Where.Y} for {Minutes(FrozenForMs)} while walking to {Wants.X},{Wants.Y}, {WantsAway} tiles off");
            }

            if (NoCloserMs >= FrozenMs)
            {
                score += Math.Min(3.0, NoCloserMs / (double)FrozenMs);

                Say(ref sb, ref said, $"has been walking to {Wants.X},{Wants.Y} for {Minutes(NoCloserMs)} and has never got closer than {_closest} tiles, standing {WantsAway} off now");
            }

            if (PacingMs >= PacedMs)
            {
                score += Math.Min(3.0, PacingMs / (double)PacedMs + 1.0);

                Say(
                    ref sb,
                    ref said,
                    $"has spent {Minutes(PacingMs)} treading a patch {Patch} tiles across at {Where.X},{Where.Y}, changing tile {Stirs} times without once leaving it"
                );
            }

            if (IdleMs >= BotVigil.LoiterMs)
            {
                score += Math.Min(4.0, 2.0 + IdleMs / (double)BotVigil.LoiterMs);

                Say(
                    ref sb,
                    ref said,
                    $"has held NO WORK AT ALL for {Minutes(IdleMs)} on the {Standing} rung at {Where.X},{Where.Y} with {Pack}gp in its pack — nothing is being offered to it"
                );
            }

            if (SwingingMs >= FutileMs)
            {
                score += 2.5;

                var why = Overhead
                    ? $"it is {FoeHigh} units of height away from it, which is more than a floor — that thing is on a roof or an upper storey and cannot be hit at all"
                    : BeyondReach
                        ? $"it is {FoeAway} tiles off and this bot's weapon reaches {Reach}"
                        : "it is beside it and in reach, so something else is stopping the blows";

                Say(ref sb, ref said, $"has been fighting {Foe} for {Minutes(SwingingMs)} without its health falling once: {why}");
            }

            if (Abandoned >= 3)
            {
                score += Math.Min(2.0, Abandoned / 3.0);

                Say(ref sb, ref said, $"has given up {Abandoned} errands while still short of them, the last one {ShortBy} tiles out");
            }

            if (WorkingForMs >= ImmortalMs)
            {
                score += Math.Min(3.0, WorkingForMs / (double)ImmortalMs);

                Say(ref sb, ref said, $"its {_kind} has answered \"working, here\" and nothing else for {Minutes(WorkingForMs)}");
            }

            if (Quick >= 4)
            {
                score += Math.Min(2.5, Quick / 4.0);

                Say(ref sb, ref said, $"{Quick} of its {Deeds} undertakings ended inside {QuickMs / 1000}s");
            }

            var loop = Worst();

            if (loop != null)
            {
                score += 2.0;

                Say(ref sb, ref said, loop);
            }

            if (Refusals >= 4)
            {
                score += Math.Min(2.0, Refusals / 6.0);

                Say(ref sb, ref said, $"{Refusals} roads refused in a row without a step between them");
            }

            if (Hopeless > 0)
            {
                score += 1.0;

                Say(ref sb, ref said, $"its journey has given up on reaching anything {Hopeless} times");
            }

            if (BarrenMinutes >= 5.0)
            {
                score += Math.Min(2.0, BarrenMinutes / 10.0);

                Say(ref sb, ref said, $"{BarrenMinutes:F0} minutes with nothing on the shard worth doing");
            }

            if (DeadMinutes >= 3.0)
            {
                score += 2.0;

                Say(ref sb, ref said, $"a ghost for {DeadMinutes:F0} minutes and not back on its feet");
            }

            var watched = WatchedMs(now);

            if (watched >= SettledMs && Progress <= FirstProgress + 0.0005)
            {
                score += 1.5;

                Say(ref sb, ref said, $"its trade has not moved off {Progress:P0} in the {Minutes(watched)} it has been watched");
            }

            if (watched >= SettledMs && Worth <= FirstWorth)
            {
                score += 1.0;

                Say(ref sb, ref said, $"worth {Worth}gp against {FirstWorth}gp when first seen, {Minutes(watched)} ago");
            }

            Symptoms = sb.ToString();
            Suspicion = score;
        }
        finally
        {
            sb.Dispose();
        }
    }

    private string Worst()
    {
        string worst = null;
        var quick = 0;
        var taken = 0;

        foreach (var (kind, trade) in _trades)
        {
            if (trade.Quick > quick)
            {
                worst = kind;
                quick = trade.Quick;
                taken = trade.Taken;
            }
        }

        return quick < 3
            ? null
            : $"it has taken {worst} {taken} times and {quick} of those were over inside {QuickMs / 1000}s, averaging {_trades[worst].HeldMs / Math.Max(1, taken) / 1000}s a go";
    }

    private static void Say(ref ValueStringBuilder sb, ref int said, string phrase)
    {
        if (said++ > 0)
        {
            sb.Append("; ");
        }

        sb.Append(phrase);
    }

    private static string Minutes(long ms) =>
        ms < 90000 ? $"{ms / 1000}s" : $"{ms / 60000}m";

    public string Row(long now)
    {
        var sb = ValueStringBuilder.Create(512);

        try
        {
            sb.Append("- ");
            sb.Append(Name);
            sb.Append(" the ");
            sb.Append(Class);
            sb.Append(": on the ");
            sb.Append(Standing);
            sb.Append(" rung, holding ");
            sb.Append(_kind == "-" ? "nothing at all" : _kind);
            sb.Append(" for ");
            sb.Append(Minutes(HeldMs));
            sb.Append(", which last asked it to ");
            sb.Append(Asked);
            sb.Append(". At ");
            sb.Append(Where.X);
            sb.Append(",");
            sb.Append(Where.Y);
            sb.Append(" in ");
            sb.Append(Region);

            if (WantsAway > 0)
            {
                sb.Append(", walking to ");
                sb.Append(Wants.X);
                sb.Append(",");
                sb.Append(Wants.Y);
                sb.Append(" (");
                sb.Append(WantsAway);
                sb.Append(" tiles off, set out ");
                sb.Append(Minutes(GoingMs));
                sb.Append(" ago");

                if (NoCloserMs >= WorthSayingMs)
                {
                    sb.Append(", and has got no nearer than ");
                    sb.Append(_closest);
                    sb.Append(" tiles for ");
                    sb.Append(Minutes(NoCloserMs));
                }

                sb.Append(")");
            }

            sb.Append(". It has kept to a patch ");
            sb.Append(Patch);
            sb.Append(" tiles across for ");
            sb.Append(Minutes(PacingMs > 0 ? PacingMs : 0));
            sb.Append(", changing tile ");
            sb.Append(Stirs);
            sb.Append(" times inside it. Given up ");
            sb.Append(Abandoned);
            sb.Append(" errands while still short of them. Still for ");
            sb.Append(Minutes(StillMs));
            sb.Append(". ");
            sb.Append(Deeds);
            sb.Append(" undertakings seen, ");
            sb.Append(Quick);
            sb.Append(" of them over inside ");
            sb.Append(QuickMs / 1000);
            sb.Append("s. Worth ");
            sb.Append(Worth);
            sb.Append("gp now against ");
            sb.Append(FirstWorth);
            sb.Append("gp when first seen ");
            sb.Append(Minutes(WatchedMs(now)));
            sb.Append(" ago. Trade at ");
            sb.Append(Progress * 100.0, "F0");
            sb.Append("% of what its class is aiming for, from ");
            sb.Append(FirstProgress * 100.0, "F0");
            sb.Append("%. Contentment ");
            sb.Append(Mood, "F2");
            sb.Append(".");

            if (_kind == "-" && !string.IsNullOrWhiteSpace(Empty))
            {
                sb.Append(" It holds nothing because: ");
                sb.Append(Empty);
                sb.Append(".");
            }

            if (!string.IsNullOrWhiteSpace(Why))
            {
                sb.Append(
                    _kind == "-"
                        ? " The last work it took, now over, was chosen because: "
                        : " It says it is doing this because: "
                );
                sb.Append(Why);
                sb.Append(".");
            }

            if (!string.IsNullOrWhiteSpace(Symptoms))
            {
                sb.Append("\n  SYMPTOMS: ");
                sb.Append(Symptoms);
                sb.Append(".");
            }

            return sb.ToString();
        }
        finally
        {
            sb.Dispose();
        }
    }
}
