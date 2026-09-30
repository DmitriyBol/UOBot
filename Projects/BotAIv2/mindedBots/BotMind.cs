using System;
using System.Collections.Generic;
using Server.BotAI.V2;
using Server.Logging;

namespace Server.BotAI.Mind;

/// <summary>One thing a mind chose, and what it turned out to be worth.</summary>
public sealed class BotMindOutcome
{
    public string Trade { get; init; }

    public double Expected { get; init; }

    public int Gained { get; init; }

    public double Minutes { get; init; }

    public double Measured => Minutes > 0 ? Gained / Minutes : 0.0;

    public bool Long => Minutes * 60000 >= BotMind.WorthCountingMs;

    public string Ending { get; init; }
}

/// <summary>
/// One bot's slow tier of thought: it chooses the next trade, watches what came of it, and writes down what
/// it takes to be the rule.
///
/// <para>
/// <b>It chooses an undertaking; it does not drive.</b> This is the one architectural decision the first
/// version of this got right and it is worth restating: the model never says which tile to step onto, never
/// picks a target and is not consulted about survival. Those are reflexes, they run at ten a second, and a
/// thing that answers in three seconds cannot be in that loop at all. What it decides is what the bot is
/// <em>for</em> over the next few minutes — and that decision is offered into the shard's own auction, where
/// it competes with the arithmetic on equal terms and loses when it deserves to.
/// </para>
///
/// <para>
/// <b>It thinks while working, and it is protected from itself by the auction rather than by a rule here.</b>
/// A mind that could reconsider every few seconds would produce a bot that finishes nothing — but the cure
/// for that already exists one level down, in the dwell and the ×1.25 floor every offer has to clear, and
/// duplicating it here would only mean two half-rules disagreeing. What this does refuse to think during is
/// bleeding, being hit, and being in a company: none of those is a decision, and all three are answered by
/// reflexes far faster than anything that has to be asked.
/// </para>
/// </summary>
public sealed class BotMind
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotMind));

    public static int ThinkEveryMs { get; set; } = 20000;

    public static int ChoiceHoldsMs { get; set; } = 45000;

    public static int WorthReviewingMs { get; set; } = 45000;

    public static int WorthCountingMs { get; set; } = 30000;

    public static int ReviewEveryMs { get; set; } = 180000;

    public static int MostLessons { get; set; } = 8;

    public static int MostPast { get; set; } = 24;

    public static int MostBarren { get; set; } = 24;

    public static int LeastMenu { get; set; } = 3;

    public static int LosesBeforeRest { get; set; } = 3;

    public static int RestMs { get; set; } = 300000;

    public static int MostRests { get; set; } = 6;

    public static int MostPerTrade { get; set; } = 2;

    public static int BarrenHoldsMs { get; set; } = 240000;

    public static int MostStrikes { get; set; } = 8;

    public static double SameLesson { get; set; } = 0.67;

    private readonly List<string> _lessons = [];

    private readonly List<BotMindOutcome> _past = [];

    private readonly List<(string Trade, long Tick, int Strikes)> _barren = [];

    private readonly Dictionary<string, (int Lost, int Rests, long Tick)> _losing = new(StringComparer.OrdinalIgnoreCase);

    private IReadOnlyList<string> _trades = [];

    private bool _idle;

    private long _askedTick;

    private long _reviewedTick;

    private long _choiceTick;

    private long _spokeTick;

    private bool _asking;

    public BotMind(string name, string trade)
    {
        Name = name;
        Trade = trade;
        _askedTick = Core.TickCount;
        _reviewedTick = Core.TickCount;
        _spokeTick = Core.TickCount;
    }

    public string Name { get; }

    public string Trade { get; }

    public BotMobile Body { get; internal set; }

    public IReadOnlyList<string> Lessons => _lessons;

    public IReadOnlyList<BotMindOutcome> Past => _past;

    public BotMindChoice Choice { get; private set; }

    public List<string> Orders { get; } = [];

    public long Took { get; private set; }

    public long Asked { get; private set; }

    public long Chose { get; private set; }

    public long Taken { get; private set; }

    public long Passed { get; private set; }

    public long Agreed { get; private set; }

    public long Rested { get; private set; }

    public long Spared { get; private set; }

    public long Barren { get; private set; }

    public long Idle { get; private set; }

    public long Over { get; private set; }

    public long Under { get; private set; }

    public void Stagger(int ms)
    {
        _askedTick = Core.TickCount + ms;
    }

    public void Beat(IReadOnlyList<string> trades)
    {
        var body = Body;

        if (body is not { Deleted: false, Alive: true } || _asking)
        {
            return;
        }

        if (Choice != null)
        {
            if (Core.TickCount - _choiceTick < ChoiceHoldsMs)
            {
                return;
            }

            var holding = body.Resolve?.Deed;

            BotMindLog.Write(
                Name,
                $"its choice of {Choice.Intent} was not taken up; the auction is holding {holding?.Kind ?? "nothing"} instead",
                null
            );

            Passed++;
            Outbid(Choice.Intent, holding);
            Choice = null;
        }

        if (Core.TickCount - _askedTick < ThinkEveryMs)
        {
            return;
        }

        var standing = body.Resolve?.Standing ?? BotStanding.Dead;

        if (standing is not (BotStanding.Free or BotStanding.Busy or BotStanding.Hunted))
        {
            return;
        }

        if (!BotOllama.Free)
        {
            return;
        }

        _askedTick = Core.TickCount;
        _trades = trades;

        var live = BotMinds.Working(body, trades);

        if (live.Count == 0)
        {
            Idle++;

            if (!_idle)
            {
                _idle = true;

                BotMindLog.Write(Name, "was not asked: no trade on the shard has work in it right now", null);
            }

            return;
        }

        if (_idle)
        {
            _idle = false;

            BotMindLog.Write(Name, $"has work to choose between again ({live.Count} trades)", null);
        }

        _asking = true;

        var open = Menu(live);
        var menu = Rest(open);

        var system = BotMindSight.System(this);
        var state = BotMindSight.State(this, body, menu, open);

        var (ours, theirs) = Roster(body);

        BotOllama.Ask(
            system,
            state,
            BotMindChoice.Schema(menu, ours, theirs, Orders, open),
            false,
            (json, waited) => Answered(json, waited, menu)
        );
    }

    private IReadOnlyList<string> Menu(IReadOnlyList<string> trades)
    {
        if (_barren.Count == 0)
        {
            return trades;
        }

        List<string> menu = [];

        for (var i = 0; i < trades.Count; i++)
        {
            if (!Empty(trades[i]))
            {
                menu.Add(trades[i]);
            }
        }

        return menu.Count < LeastMenu ? trades : menu;
    }

    private IReadOnlyList<string> Rest(IReadOnlyList<string> open)
    {
        if (_losing.Count == 0)
        {
            return open;
        }

        List<(string Trade, int Rests)> resting = null;

        for (var i = 0; i < open.Count; i++)
        {
            if (_losing.TryGetValue(open[i], out var losing) && losing.Rests > 0
                && Core.TickCount - losing.Tick < RestFor(losing.Rests))
            {
                (resting ??= []).Add((open[i], losing.Rests));
            }
        }

        if (resting == null)
        {
            return open;
        }

        resting.Sort((a, b) => b.Rests.CompareTo(a.Rests));

        List<string> menu = [..open];

        for (var i = 0; i < resting.Count; i++)
        {
            if (menu.Count <= LeastMenu)
            {
                Spared++;

                continue;
            }

            menu.Remove(resting[i].Trade);
        }

        return menu;
    }

    private static long RestFor(int rests) => (long)RestMs * Math.Min(rests, MostRests);

    private void Outbid(string trade, BotDeed holding)
    {
        if (trade == null)
        {
            return;
        }

        var work = holding is BotMindDeed minded ? minded.Work : holding;

        if (work != null && BotWill.OfferedAs(work.Kind, trade))
        {
            Agreed++;
            _losing.Remove(trade);

            return;
        }

        _losing.TryGetValue(trade, out var losing);
        losing.Lost++;

        if (losing.Lost < LosesBeforeRest)
        {
            _losing[trade] = losing;

            return;
        }

        losing.Lost = 0;
        losing.Rests = Math.Min(losing.Rests + 1, MostRests);
        losing.Tick = Core.TickCount;
        _losing[trade] = losing;
        Rested++;

        BotMindLog.Write(
            Name,
            $"lost {trade} {LosesBeforeRest} times running with the bot at other work ({work?.Kind ?? "nothing"} now), so {trade} rests for {RestFor(losing.Rests) / 60000} minutes",
            null
        );
    }

    private bool Empty(string trade)
    {
        for (var i = 0; i < _barren.Count; i++)
        {
            if (string.Equals(_barren[i].Trade, trade, StringComparison.OrdinalIgnoreCase)
                && Core.TickCount - _barren[i].Tick < Holds(_barren[i].Strikes))
            {
                return true;
            }
        }

        return false;
    }

    private void Charge(BotMindChoice choice)
    {
        if (Body is not { Deleted: false } body || body.Guild is not Server.Guilds.Guild guild)
        {
            return;
        }

        if (choice.Gather == null && choice.Make == null && choice.MarchX == 0 && choice.MarchY == 0)
        {
            return;
        }

        var march = choice.MarchX > 0 && choice.MarchY > 0
            ? new Point3D(choice.MarchX, choice.MarchY, 0)
            : Point3D.Zero;

        BotCharter.Order(guild, choice.Gather, choice.Make, body.Map, march, choice.Why);
        Charged++;

        BotMindLog.Write(
            Name,
            $"charged {guild.Name}: gather {choice.Gather ?? "as they like"}, make {choice.Make ?? "as they like"}"
            + (march == Point3D.Zero ? "" : $", muster at {march.X},{march.Y}"),
            choice.Why
        );
    }

    public long Charged { get; private set; }

    public static long Rostered { get; private set; }

    private static (List<string> Ours, List<string> Theirs) Roster(BotMobile body)
    {
        List<string> ours = [];
        List<string> theirs = [];

        if (body?.Guild is not Server.Guilds.Guild guild || !BotRoster.Free(guild))
        {
            return (ours, theirs);
        }

        var weakest = BotRoster.Weakest(guild, BotRoster.Shortlist);

        for (var i = 0; i < weakest.Count; i++)
        {
            ours.Add(weakest[i].Name);
        }

        var candidates = BotRoster.Candidates(guild, BotRoster.Shortlist);

        for (var i = 0; i < candidates.Count; i++)
        {
            theirs.Add(candidates[i].Name);
        }

        return (ours, theirs);
    }

    private void Roll(BotMindChoice choice)
    {
        if (Body is not { Deleted: false } body || body.Guild is not Server.Guilds.Guild guild)
        {
            return;
        }

        if (choice.Expel == null && choice.Recruit == null)
        {
            return;
        }

        var done = BotRoster.Change(guild, Named(choice.Expel), Named(choice.Recruit), choice.Why);

        if (done == null)
        {
            return;
        }

        Rostered++;

        BotMindLog.Write(Name, $"{guild.Name}: {done}", choice.Why);
    }

    private static BotMobile Named(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var roll = BotPopulation.Bots;

        for (var i = 0; i < roll.Count; i++)
        {
            if (roll[i] is { Deleted: false } bot && string.Equals(bot.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return bot;
            }
        }

        return null;
    }

    private void Answered(string json, long waited, IReadOnlyList<string> trades)
    {
        _asking = false;

        var choice = BotMindChoice.Read(json);

        if (choice == null)
        {
            return;
        }

        if (!Known(trades, choice.Intent))
        {
            logger.Warning("{Name} chose {Intent}, which is not a trade on offer; nothing is taken up", Name, choice.Intent);

            return;
        }

        Choice = choice;
        _choiceTick = Core.TickCount;
        Chose++;

        BotMindLog.Write(Name, $"chose {choice.Intent}, expects {choice.Expect:F0}/min over {choice.Minutes:F0} min ({waited}ms)", choice.Why);

        Charge(choice);

        Roll(choice);

        if (choice.Take != null && BotMindClaims.Claim(Name, choice.Take))
        {
            Took++;
            BotMindLog.Write(Name, $"takes the order for {choice.Take}", choice.Why);
        }

        if (choice.Want != null && choice.WantAmount > 0 && Body?.Guild is Server.Guilds.Guild band)
        {
            Asked++;
            BotCharter.Want(band, choice.Want, choice.WantAmount, Name, choice.Why);
        }

        BotMindLog.Write(Name, "could have taken", string.Join(", ", trades));
        BotMindLog.Write(Name, "and", BotMindSight.Brief(this, Body));

        Speak(choice.Say);
    }

    private void Speak(string line)
    {
        if (string.IsNullOrWhiteSpace(line) || !BotMindTalk.Post(Name, line))
        {
            return;
        }

        BotMindLog.Write(Name, "said aloud", line);

        var body = Body;

        if (body is not { Deleted: false, Alive: true } || Core.TickCount - _spokeTick < BotMindTalk.SpeakEveryMs)
        {
            return;
        }

        _spokeTick = Core.TickCount;

        BotVoice.Say(body, "local", line.Length > BotMindTalk.MostLetters ? line[..BotMindTalk.MostLetters] : line, force: true);
    }

    private static bool Known(IReadOnlyList<string> trades, string intent)
    {
        for (var i = 0; i < trades.Count; i++)
        {
            if (string.Equals(trades[i], intent, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    public bool Began(BotMindChoice choice)
    {
        if (choice == null || !ReferenceEquals(Choice, choice))
        {
            return false;
        }

        Choice = null;
        Taken++;
        _losing.Remove(choice.Intent ?? "");

        return true;
    }

    public void Discard()
    {
        var trade = Choice?.Intent;

        Choice = null;
        Barren++;

        if (trade == null)
        {
            return;
        }

        var strikes = 1;

        for (var i = _barren.Count - 1; i >= 0; i--)
        {
            if (string.Equals(_barren[i].Trade, trade, StringComparison.OrdinalIgnoreCase))
            {
                strikes = _barren[i].Strikes + 1;

                _barren.RemoveAt(i);
            }
        }

        _barren.Add((trade, Core.TickCount, strikes));

        while (_barren.Count > MostBarren)
        {
            _barren.RemoveAt(0);
        }

        BotMindLog.Write(Name, $"chose {trade}, which had no work in it ({strikes} times now)", null);
    }

    private static long Holds(int strikes) => (long)BarrenHoldsMs * Math.Min(strikes, MostStrikes);

    public IEnumerable<(string Trade, int SecondsAgo)> Barrens()
    {
        for (var i = 0; i < _barren.Count; i++)
        {
            var since = Core.TickCount - _barren[i].Tick;

            if (since < Holds(_barren[i].Strikes))
            {
                yield return (_barren[i].Trade, (int)(since / 1000));
            }
        }
    }

    public void Settle(string trade, double expected, int gained, double minutes, string ending)
    {
        var outcome = new BotMindOutcome
        {
            Trade = trade,
            Expected = expected,
            Gained = gained,
            Minutes = minutes,
            Ending = ending
        };

        _past.Add(outcome);

        if (_past.Count > MostPast)
        {
            _past.RemoveAt(0);
        }

        if (outcome.Long)
        {
            if (outcome.Measured < expected)
            {
                Over++;
            }
            else
            {
                Under++;
            }

            BotMindLog.Write(
                Name,
                $"{trade} {ending}: expected {expected:F0}/min, got {outcome.Measured:F0}/min over {minutes:F1} min",
                null
            );
        }
        else
        {
            BotMindLog.Write(
                Name,
                $"{trade} {ending} after {minutes * 60:F0}s with {gained}gp — too short to have a rate",
                null
            );
        }

        Review(outcome, minutes);
    }

    private void Review(BotMindOutcome outcome, double minutes)
    {
        if (!outcome.Long || minutes * 60000 < WorthReviewingMs || Core.TickCount - _reviewedTick < ReviewEveryMs)
        {
            return;
        }

        if (_asking || !BotOllama.Free)
        {
            return;
        }

        _reviewedTick = Core.TickCount;
        _asking = true;

        var system = BotMindSight.System(this);

        var question =
            $"""
             You chose {outcome.Trade} and expected it to be worth {outcome.Expected:F0} gold a minute over
             {outcome.Minutes:F0} minutes. It {outcome.Ending} and came to {outcome.Measured:F0} gold a minute
             across {outcome.Minutes:F1} minutes.

             Write one short rule you will use next time you are choosing. It must be about this shard and
             this bot, specific enough to change a decision — not general advice. If nothing here is worth
             remembering, say so and set keep to false.

             What you already believe:
             {Recited()}
             """;

        BotOllama.Ask(system, question, BotMindChoice.LessonSchema, true, Learned);
    }

    private void Learned(string json, long waited)
    {
        _asking = false;

        var (lesson, keep) = BotMindChoice.ReadLesson(json);

        if (!keep || lesson == null)
        {
            return;
        }

        if (Same(lesson))
        {
            BotMindLog.Write(Name, $"wrote a lesson it already holds, dropped ({waited}ms)", lesson);

            return;
        }

        var about = About(lesson);

        if (about != null)
        {
            for (var held = Counted(about); held >= MostPerTrade; held--)
            {
                var oldest = Oldest(about);

                if (oldest < 0)
                {
                    break;
                }

                BotMindLog.Write(Name, $"has enough rules about {about} already, so the oldest goes", _lessons[oldest]);

                _lessons.RemoveAt(oldest);
            }
        }

        _lessons.Add(lesson);

        if (_lessons.Count > MostLessons)
        {
            _lessons.RemoveAt(0);
        }

        BotMindLog.Write(Name, $"learned something ({waited}ms)", lesson);
        logger.Information("{Name} wrote itself a rule: {Lesson}", Name, lesson);

        BotMinds.Save();
    }

    private string About(string lesson)
    {
        for (var i = 0; i < _trades.Count; i++)
        {
            if (lesson.Contains(_trades[i], StringComparison.OrdinalIgnoreCase))
            {
                return _trades[i];
            }
        }

        return null;
    }

    private int Counted(string trade)
    {
        var count = 0;

        for (var i = 0; i < _lessons.Count; i++)
        {
            if (_lessons[i].Contains(trade, StringComparison.OrdinalIgnoreCase))
            {
                count++;
            }
        }

        return count;
    }

    private int Oldest(string trade)
    {
        for (var i = 0; i < _lessons.Count; i++)
        {
            if (_lessons[i].Contains(trade, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    private bool Same(string lesson)
    {
        var words = Words(lesson);

        if (words.Count == 0)
        {
            return true;
        }

        for (var i = 0; i < _lessons.Count; i++)
        {
            var held = Words(_lessons[i]);
            var shared = 0;

            foreach (var word in words)
            {
                if (held.Contains(word))
                {
                    shared++;
                }
            }

            if (shared / (double)Math.Max(words.Count, held.Count) >= SameLesson)
            {
                return true;
            }
        }

        return false;
    }

    private static HashSet<string> Words(string text)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var parts = text.Split([' ', ',', '.', ';', ':', '\n', '\r', '\t'], StringSplitOptions.RemoveEmptyEntries);

        for (var i = 0; i < parts.Length; i++)
        {
            if (parts[i].Length > 3)
            {
                set.Add(parts[i]);
            }
        }

        return set;
    }

    private string Recited()
    {
        if (_lessons.Count == 0)
        {
            return "Nothing yet.";
        }

        return string.Join("\n", _lessons);
    }

    internal void Restore(IEnumerable<string> lessons)
    {
        _lessons.Clear();

        if (lessons == null)
        {
            return;
        }

        foreach (var lesson in lessons)
        {
            if (!string.IsNullOrWhiteSpace(lesson) && _lessons.Count < MostLessons)
            {
                _lessons.Add(lesson.Trim());
            }
        }
    }

    public string Describe() =>
        $"{Name} the {Trade}: {Chose} decisions, {Taken} taken up, {Passed} outbid ({Agreed} with the bot at that trade anyway), {Rested} rests for losing {LosesBeforeRest} running ({Spared} held back to keep {LeastMenu} on the menu), {Barren} on empty trades, {Idle} beats with no work anywhere, {Over} predictions too high against {Under} not too high, {_lessons.Count} rules held";
}
