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

    /// <summary>Gold that actually arrived. The measurement; everything else about it is derived.</summary>
    public int Gained { get; init; }

    public double Minutes { get; init; }

    /// <summary>
    /// What that comes to per minute — and only meaningful over a window long enough to have a rate.
    ///
    /// <para>
    /// <b>A six-second sample has no rate, and dividing one out of it is how the whole cycle goes wrong.</b>
    /// A hunt that finds nothing to hunt ends as a prowl in a few seconds, and "expected 250 a minute, got
    /// nought a minute" is arithmetically true and says nothing about the decision: no work of any kind
    /// could have paid anything in that time. Left uncorrected, every prediction reads as too high, the
    /// tally shows five against nought, and the expensive reckoning writes a rule about a day that did not
    /// happen — which is exactly what became of every lesson the first version of this ever wrote.
    /// </para>
    /// </summary>
    public double Measured => Minutes > 0 ? Gained / Minutes : 0.0;

    /// <summary>Whether the window was long enough for the rate above to mean anything.</summary>
    public bool Long => Minutes * 60000 >= BotMind.WorthCountingMs;

    /// <summary>finished, failed, or dropped. The word the reckoning is written about.</summary>
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

    /// <summary>How often a free bot may be asked to choose again.</summary>
    public static int ThinkEveryMs { get; set; } = 20000;

    /// <summary>
    /// How long a choice waits to be picked up by the auction before it goes stale.
    ///
    /// <para>
    /// <b>Comfortably longer than one review, and it started out exactly equal to one.</b> The auction comes
    /// round every fifteen seconds and the choice used to last fifteen seconds, so whether an offer was ever
    /// seen at all came down to which of the two clocks happened to be ahead. Three reviews' worth means a
    /// choice is put in front of the auction at least twice before it is given up on.
    /// </para>
    /// </summary>
    public static int ChoiceHoldsMs { get; set; } = 45000;

    /// <summary>Shortest piece of work worth writing a lesson about.</summary>
    public static int WorthReviewingMs { get; set; } = 45000;

    /// <summary>
    /// Shortest window that has a rate in it at all.
    ///
    /// Below this the outcome is remembered and told to the mind as what it was — so many seconds, so much
    /// gold — but it is not divided into a rate, not counted for or against the prediction, and never made
    /// into a rule. See <see cref="BotMindOutcome.Measured"/> for what that cost to learn twice.
    /// </summary>
    public static int WorthCountingMs { get; set; } = 30000;

    /// <summary>How often one mind may spend a thinking-length call on a reckoning.</summary>
    public static int ReviewEveryMs { get; set; } = 180000;

    /// <summary>Most lessons kept. Beyond this the oldest goes, because the prompt has to stay short.</summary>
    public static int MostLessons { get; set; } = 8;

    /// <summary>Most outcomes remembered.</summary>
    public static int MostPast { get; set; } = 24;

    /// <summary>
    /// Most empty trades remembered at once. Wider than the menu, on purpose.
    ///
    /// <para>
    /// <b>Set below the number of trades, this silently cancels the escalation above it.</b> At five, an
    /// archer working ground where eight different trades had nothing in them pushed the oldest entry out
    /// every time it learned a new one — so Peddler was rediscovered as empty eleven times and never once
    /// got past its first strike. The two numbers were each defensible and could not both be right: a list
    /// that forgets faster than it counts is a list that cannot count. Entries expire on their own clock, so
    /// holding every trade costs nothing but the row.
    /// </para>
    /// </summary>
    public static int MostBarren { get; set; } = 24;

    /// <summary>Fewest trades a menu may be cut down to before the cutting is abandoned.</summary>
    public static int LeastMenu { get; set; } = 3;

    /// <summary>
    /// Most rules one mind may hold about any single trade.
    ///
    /// <para>
    /// <b>Without it the store fills with one trade, and the trade it fills with is the one that failed.</b>
    /// A reckoning is only ever written about work that has just ended badly, so the trade a mind is having
    /// the worst luck with is the trade it writes about — and by the evening of 25.08.2026 five of Aldric's
    /// six rules were about prowling, four of Godric's eight about hunting, and five of Cedric's eight about
    /// inscribing. Two of Godric's contradicted each other outright ("reject any Hunt offer predicting more
    /// than 20 gold/min" beside "reject Hunt offers predicting under 35"), which is a band no answer can be
    /// inside, held because there was room for both. A cap per trade spends the store on breadth: eight
    /// rules about eight trades is a mind that has learned something about the shard, and eight about one is
    /// a mind that has learned the same thing eight times.
    /// </para>
    /// </summary>
    public static int MostPerTrade { get; set; } = 2;

    /// <summary>
    /// How long a trade is remembered as having been empty.
    ///
    /// Long enough to stop the same choice being made every twenty seconds, short enough that a shopkeeper
    /// two minutes' walk away, or a vein the world has refilled, gets another chance. This is a fact about
    /// a moment, not about the trade.
    /// </summary>
    public static int BarrenHoldsMs { get; set; } = 240000;

    /// <summary>
    /// How many confirmations of a trade's emptiness are allowed to lengthen the silence.
    ///
    /// Eight of them is half an hour at the window above — long enough that a shopkeeper on the far side of
    /// the map stops being reconsidered every few minutes, short enough that nothing is written off for the
    /// session.
    /// </summary>
    public static int MostStrikes { get; set; } = 8;

    /// <summary>
    /// How alike two lessons have to be before the second is thrown away, as a share of shared words.
    ///
    /// <para>
    /// <b>By overlap, never by prefix.</b> A model asked the same question about the same mistake writes the
    /// same rule in different words every time — "do not hunt inside the town", "hunting in town is
    /// pointless, nothing spawns there" — and a check on the opening characters lets every one of them
    /// through. Two thirds of the words in common is one rule twice.
    /// </para>
    /// </summary>
    public static double SameLesson { get; set; } = 0.67;

    private readonly List<string> _lessons = [];

    private readonly List<BotMindOutcome> _past = [];

    private readonly List<(string Trade, long Tick, int Strikes)> _barren = [];

    /// <summary>Every trade the shard has, as of the last beat. Kept so a lesson can be filed against one.</summary>
    private IReadOnlyList<string> _trades = [];

    /// <summary>Whether the last beat found nothing at all to choose between, so the change can be logged once.</summary>
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

    /// <summary>The bot's name, which is also the key its lessons are kept under.</summary>
    public string Name { get; }

    /// <summary>What it is — a warrior or an archer. Said to the model, and nothing else reads it.</summary>
    public string Trade { get; }

    /// <summary>The body, once the population has raised one and this mind has claimed it.</summary>
    public BotMobile Body { get; internal set; }

    public IReadOnlyList<string> Lessons => _lessons;

    public IReadOnlyList<BotMindOutcome> Past => _past;

    /// <summary>What the mind has settled on and is waiting to have taken up, or null.</summary>
    public BotMindChoice Choice { get; private set; }

    /// <summary>Decisions asked for, taken up by the auction, and beaten by the shard's own arithmetic.</summary>
    public long Chose { get; private set; }

    public long Taken { get; private set; }

    /// <summary>
    /// Chosen, offered, and beaten by the shard's own arithmetic. An honest outcome and not a fault.
    /// </summary>
    public long Passed { get; private set; }

    /// <summary>
    /// Chosen, and the trade turned out to have no work in it at all — no shopkeeper, no ore, no quarry.
    ///
    /// <para>
    /// <b>Counted apart from being outbid, because the two say opposite things about the mind.</b> Merged,
    /// the first evening read "14 decisions, 0 taken up, 14 passed over", which is equally consistent with a
    /// model whose judgement is poor and with a model being asked to choose from a menu of things that do
    /// not exist. It was the second, and the merged counter could not have told anybody so.
    /// </para>
    /// </summary>
    public long Barren { get; private set; }

    /// <summary>
    /// Beats on which the shard had no work of any kind for this bot, so nothing was asked.
    ///
    /// <para>
    /// <b>Silence is a fact and it needs a row of its own.</b> Not asking looks identical to being asked and
    /// having nothing to say, and only one of the two is worth doing anything about.
    /// </para>
    /// </summary>
    public long Idle { get; private set; }

    /// <summary>How often the mind's prediction was too high, and how often too low. Both, never a total.</summary>
    public long Over { get; private set; }

    public long Under { get; private set; }

    /// <summary>One beat of thought. Cheap when there is nothing to do, which is most beats.</summary>
    public void Beat(IReadOnlyList<string> trades)
    {
        var body = Body;

        if (body is not { Deleted: false, Alive: true } || _asking)
        {
            return;
        }

        // A choice already made and still fresh. Waiting for the auction to come round is not idleness.
        if (Choice != null)
        {
            if (Core.TickCount - _choiceTick < ChoiceHoldsMs)
            {
                return;
            }

            // Never picked up: the shard preferred its own arithmetic, which is allowed and is worth counting.
            Passed++;
            Choice = null;
        }

        if (Core.TickCount - _askedTick < ThinkEveryMs)
        {
            return;
        }

        // <b>Busy counts, and reading it the other way switched the whole thing off silently.</b> A bot
        // holding work stands on the <c>Busy</c> rung, and the first version of this gate asked only bots
        // standing on <c>Free</c> — which is a rung a working bot is almost never on. Two minds were awake,
        // embodied, connected to a model that answered in a second, and asked nothing at all: the log said
        // they had bodies and said nothing after that, which is exactly what a mind with no opinions looks
        // like. The auction itself runs on the Free rung whatever the bot is standing on (see
        // <c>BotWill.Decide</c>), so an offer is wanted either way.
        //
        // What the rung does still exclude is the whole point of checking it: <c>Failing</c>, <c>Hunted</c>
        // and <c>Bound</c> are bleeding, being hit, and being in a company — none of them anybody's decision,
        // and none of them improved by a model with a three-second opinion. And nothing is disturbed by
        // thinking while busy: the auction's own dwell and its ×1.25 floor protect the work in hand, and they
        // protect it better than a rule written out here could.
        // <b>Being hit counts too, and leaving it out silently switched the warrior off.</b> Hunted was
        // excluded on the reasoning that a bot with something chewing on it has no business shopping for
        // work — which was true while nothing served that rung and the bot simply held what it had. The
        // moment BotDefender started answering it, a fighter spent most of its life there: Aldric took nought
        // decisions in ten minutes on 24.08.2026 while the other two thought normally, and the log showed him
        // working — 500 gold a minute of it — because the fighting is the reflex's and needs no opinion. What
        // the mind decides is what to do *next*, and the best moment to have that ready is while the current
        // scrap finishes.
        //
        // Bound stays out, and for a different reason: a squad owns where its members stand and what they
        // hit, so an opinion has nowhere to land. Failing stays out because a bot at a fifth of its health
        // has one thing to do and a three-second answer cannot be part of it.
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

        // <b>What has work in it, asked of the work itself, and not what exists.</b> The menu used to be the
        // shard's list of trades less whatever had lately come up empty — which is a list of things that
        // might have work in them, learned about only by choosing one and finding out. On 25.08.2026 that
        // cost twenty-two of Aldric's twenty-four decisions and nine of Cedric's fourteen: a fifth of every
        // thought the three of them had all day was spent naming a trade with no shopkeeper, no ore and no
        // quarry behind it. The auction has already asked every one of them on this bot's last free review
        // and now keeps the answers, so this is a read rather than a second round of questions — which
        // matters, because every proposer counts its own refusals and a speculative round would inflate the
        // shard's own instrumentation. See BotMinds.Working.
        //
        // It is a fact about the last moment the bot was free, so it ages while the bot is in a fight it is
        // holding work through. That is the right staleness to have: a menu is checked again for real when
        // the choice is offered, and the barren memory is what catches a trade that emptied out in between.
        var live = BotMinds.Working(body, trades);

        if (live.Count == 0)
        {
            // Nothing on the shard for this bot this moment. Not a failure of judgement and not a decision —
            // there was nothing to decide between, and it is counted as its own thing for exactly that reason.
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

        var menu = Menu(live);

        var system = BotMindSight.System(this);
        var state = BotMindSight.State(this, body, menu);

        BotOllama.Ask(system, state, BotMindChoice.Schema(menu), false, (json, waited) => Answered(json, waited, menu));
    }

    /// <summary>
    /// The trades actually put in front of the model: everything on offer, less whatever came up empty
    /// lately.
    ///
    /// <para>
    /// <b>Taken off the menu rather than argued about, and the argument is what was tried first.</b> The
    /// state used to carry a section naming the empty trades and saying in plain words that choosing one
    /// again would come to nothing — and the model read it straight back to front: <em>"the peddler trade
    /// was only attempted seconds ago, making it the highest probability for quick coin conversion"</em>.
    /// Seven choices in a row for a trade that had no work in it, each one citing the warning as the reason.
    /// A constraint the sampler enforces cannot be reinterpreted; a sentence in a prompt always can. This is
    /// the same reason the answer is an enum rather than a string.
    /// </para>
    ///
    /// <para>
    /// Never cut below <see cref="LeastMenu"/>: a menu of one is not a choice, and a mind with nothing to
    /// choose between should be asked the ordinary question and be outbid honestly.
    /// </para>
    /// </summary>
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

    /// <summary>Whether this trade came up empty recently enough to be worth leaving off the menu.</summary>
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

    private void Answered(string json, long waited, IReadOnlyList<string> trades)
    {
        _asking = false;

        var choice = BotMindChoice.Read(json);

        if (choice == null)
        {
            return;
        }

        // The schema constrains this, so a miss here means the schema and the list disagree — worth a line
        // rather than a silent nothing, because a mind whose every answer is discarded looks exactly like a
        // mind that is not being asked.
        if (!Known(trades, choice.Intent))
        {
            logger.Warning("{Name} chose {Intent}, which is not a trade on offer; nothing is taken up", Name, choice.Intent);

            return;
        }

        Choice = choice;
        _choiceTick = Core.TickCount;
        Chose++;

        BotMindLog.Write(Name, $"chose {choice.Intent}, expects {choice.Expect:F0}/min over {choice.Minutes:F0} min ({waited}ms)", choice.Why);

        Speak(choice.Say);
    }

    /// <summary>
    /// A line to the others, and out loud where anybody watching can read it.
    ///
    /// <para>
    /// <b>Said in the world, not only into a file.</b> The whole of what makes these three different from the
    /// other twelve is that something is thinking about them, and thinking is the one thing a watcher cannot
    /// see. A bot that says what it is about — and is then seen doing it, or not — puts the decision and its
    /// consequence in the same place, which is worth more than any amount of logging.
    /// </para>
    ///
    /// <para>
    /// Rationed, and the board refuses a repeat of the speaker's own last line. A model handed somewhere to
    /// write will write every time it is asked; three of them doing that every twenty seconds is a shard
    /// where nothing else can be read.
    /// </para>
    /// </summary>
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

        body.Say(line.Length > BotMindTalk.MostLetters ? line[..BotMindTalk.MostLetters] : line);
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

    /// <summary>
    /// The choice actually became the work in hand. Counted here and nowhere else.
    ///
    /// <para>
    /// <b>Offering is not being taken up, and counting the two as one thing made the tally flatter than the
    /// truth.</b> The mind used to spend its choice the moment the proposer was asked for it — but the
    /// auction asks every proposer on every review and keeps one answer, so an offer that was weighed and
    /// beaten was recorded as "taken up" all the same. That is the same defect the barren counter was split
    /// off to cure, one level further up, and it had the same effect: <c>0 outbid</c> in every summary all
    /// day, from a counter that could not reach anything but nought, because the choice was always gone
    /// before it could go stale. Now the choice survives being offered, competes again on the next review,
    /// and is only spent by the deed that the shard actually starts.
    /// </para>
    /// </summary>
    /// <returns>Whether this was the live choice; false for a losing offer's deed, which claims nothing.</returns>
    public bool Began(BotMindChoice choice)
    {
        if (choice == null || !ReferenceEquals(Choice, choice))
        {
            return false;
        }

        Choice = null;
        Taken++;

        return true;
    }

    /// <summary>
    /// The choice came to nothing: the trade the mind named had no work in it when it was asked.
    ///
    /// Written down as well as counted. A mind that is not told this picks the same empty trade every twenty
    /// seconds for as long as the shard runs — which is what fourteen decisions in a row about selling an
    /// empty pack actually were.
    /// </summary>
    public void Discard()
    {
        var trade = Choice?.Intent;

        Choice = null;
        Barren++;

        if (trade == null)
        {
            return;
        }

        // <b>Each repeat buys a longer silence.</b> One fixed window means a trade that is empty because the
        // population is out at a graveyard, four hundred tiles from the nearest shopkeeper, comes back onto
        // the menu every four minutes and is chosen again, all evening — which is what fourteen discards
        // against five taken up actually was. A trade whose emptiness keeps being confirmed is being told
        // about the ground the bot is standing on, not about a moment, and it should be believed further
        // each time. It still expires: the bot walks, and the shard refills.
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

    /// <summary>How long one trade's emptiness is believed, given how often it has been confirmed.</summary>
    private static long Holds(int strikes) => (long)BarrenHoldsMs * Math.Min(strikes, MostStrikes);

    /// <summary>Trades that came up empty lately, newest last, with how long ago in seconds.</summary>
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

    /// <summary>
    /// A piece of work the mind chose has ended. Write down what it came to, and sometimes think about it.
    /// </summary>
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

        // Only judged on windows long enough to be judged on. A prediction is about a rate, and a piece of
        // work that ended in six seconds has no rate to compare it with — counting those made every mind
        // look uniformly over-optimistic within a minute of waking up, which said nothing about any of them.
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

    /// <summary>
    /// The expensive half: a thinking call that turns one comparison into a rule.
    ///
    /// <para>
    /// Rationed on two counts, and both are about the single graphics card this shard shares with the model.
    /// A thinking answer takes twenty seconds against three for a plain one, and while it runs no bot can be
    /// asked anything — so a mind reviewing every little errand would spend the session thinking about
    /// walking to a shop, and the other mind would spend it waiting.
    /// </para>
    /// </summary>
    private void Review(BotMindOutcome outcome, double minutes)
    {
        // Never about a window with no rate in it. This is the same guard as the tally's and it matters more
        // here: a lesson is kept for the rest of the session and fed back into every later decision, so one
        // rule drawn from a six-second prowl is a wrong belief with a long life.
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

        // Room made among that trade's own rules first, and only then among everybody's. Dropping the
        // globally oldest instead is what let one unlucky trade eat the whole store: see MostPerTrade.
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

    /// <summary>
    /// Which trade a rule is about, or null for one that names none.
    ///
    /// <para>
    /// By the trade's own name, and that only became possible once there was one name to look for. While the
    /// menu said <c>Scribe</c> and the recital of outcomes said <c>inscribe</c>, a rule could be written in
    /// either vocabulary and matched in neither. See <see cref="BotMindDeed"/> for what that cost.
    /// </para>
    /// </summary>
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

    /// <summary>Whether this is a lesson already held, by shared words rather than by shared opening.</summary>
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
            // Short words are grammar, not content, and counting them makes every pair of sentences look alike.
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

    /// <summary>Puts lessons back after a restart. Used by the store; nothing else adds in bulk.</summary>
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

    /// <summary>One line for the shard's own log.</summary>
    public string Describe() =>
        $"{Name} the {Trade}: {Chose} decisions, {Taken} taken up, {Passed} outbid, {Barren} on empty trades, {Idle} beats with no work anywhere, {Over} predictions too high against {Under} not too high, {_lessons.Count} rules held";
}
