using System;
using System.Collections.Generic;
using Server.Logging;
using Server.Mobiles;
using Server.Text;

namespace Server.BotAI.V2;

/// <summary>
/// The decision. Reads the ladder, holds what it has taken on, and runs an auction when it is free to want
/// something.
///
/// <para>
/// <b>It does not know what work exists.</b> It knows proposers — see <see cref="IBotProposer"/> — and they
/// belong to the subsystems that own the work. That is the structural answer to the first version's brain:
/// <c>ChooseGoal</c> was 1209 lines inside a file of 7985 which referenced 57 other modules, so every
/// change to any behaviour was a change to that file, and nobody could touch mining without touching
/// trade. Adding a kind of work here is a new folder and one line of registration; nothing in this file
/// changes.
/// </para>
///
/// <para>
/// <b>It does not re-decide every tick.</b> A decision is reviewed on a clock and switched only against a
/// margin, with what is already being done given a bonus for being underway. In the first version a bot in
/// state <em>Trade</em> was seen walking a graveyard: it was trading honestly, one tick at a time — two
/// steps towards town, a skeleton noticed ten tiles away, back to hunting, town noticed again. Any
/// intention longer than a second was impossible in principle, and the only reason it did not look broken
/// is that a bot walking in circles looks busy.
/// </para>
///
/// <para>
/// <b>Every decision is recorded in words and numbers.</b> The first version's brain took 85 of the 135
/// plans its own slow tier offered and nothing anywhere said so; the tier spent the night learning from
/// noise. A choice nobody can read is a choice nobody can correct.
/// </para>
/// </summary>
public static class BotWill
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotWill));

    /// <summary>
    /// Whether deciding is switched on, from <c>bots.will.enabled</c>. Off, bots keep whatever the rest of
    /// the population's machinery gives them — squads still form up, movement still walks, combat still
    /// answers — and nothing chooses anything. That is the diagnostic: half the first version's
    /// investigations were the question "is this the brain, or is the brain covering for something else".
    /// </summary>
    public static bool Deciding { get; internal set; }

    /// <summary>
    /// How often a bot with something on looks up to see whether anything better has appeared.
    ///
    /// Fifteen seconds. The number that matters is that it is not "every tick": a bot that reconsiders four
    /// times a second cannot cross a continent, and one that reconsiders every quarter minute can.
    /// </summary>
    public static int ReviewMs { get; set; } = 15000;

    /// <summary>
    /// How soon a bot with nothing on looks again.
    ///
    /// Shorter than the review, because the two are different questions: a busy bot is being asked whether to
    /// change its mind, and an idle one is being asked whether there is anything to do at all. It is not the
    /// beat itself, though — sweeping every proposer twice a second for a population of a hundred and fifty is
    /// the first version's cost model, and two seconds of standing about is invisible.
    /// </summary>
    public static int IdleMs { get; set; } = 2000;

    /// <summary>
    /// How long a fresh undertaking is safe from being swapped out whatever the numbers say.
    ///
    /// The floor under the margin, and it is there for a specific pathology: two pieces of work whose scores
    /// keep crossing produce a bot that alternates between them and finishes neither. Half a minute is
    /// enough to get somewhere and find out.
    ///
    /// <para>
    /// <b>A floor now rather than the whole rule, because as the whole rule it was half a minute against a
    /// six-minute job.</b> See <see cref="Dwell"/>.
    /// </para>
    /// </summary>
    public static int DwellMs { get; set; } = 30000;

    /// <summary>
    /// The most a fresh undertaking may be safe for, however long it reckons itself.
    ///
    /// <para>
    /// Two minutes, and the number is chosen against <c>BotStall.PatienceMs</c> rather than against any
    /// work: the stall detector calls a bot stuck at four minutes, so nothing here may be protected long
    /// enough to hide from it. The cap is not tidiness — without one, an eight-minute errand that has gone
    /// wrong is immune for eight minutes, which is the frozen-work family this project has paid for more
    /// than once.
    /// </para>
    /// </summary>
    public static int DwellCapMs { get; set; } = 120000;

    /// <summary>
    /// How long this particular undertaking is safe from being swapped out: its own reckoning, floored by
    /// <see cref="DwellMs"/> and capped by <see cref="DwellCapMs"/>.
    ///
    /// <para>
    /// <b>The smith could not finish a single thing, and this is why.</b> Work is reconsidered every
    /// <see cref="ReviewMs"/> against what it has produced so far, and a craft that yields nothing until the
    /// item is done reads as nought a minute for as long as it takes. Mining and foraging are spared because
    /// they yield continuously; forging is not. On 04.09.2026 Calla dropped "beating out Cutlass (2 attempts,
    /// 0 made)" at exactly 0.5 minutes — the instant the flat dwell expired — and over that session the smith
    /// took five errands, dropped four and finished one, against 29 sewn and 37 brewed. The ledger then
    /// marked the trade down for failures it had itself caused: the base fell 55 → 37 → 28 across three
    /// readings, so forging lost every later auction as well.
    /// </para>
    ///
    /// <para>
    /// Two numbers on one shelf, and the cure is the usual one — stop having the second number.
    /// <see cref="BotDeed.Minutes"/> is already on the deed and already means "how long this takes".
    /// </para>
    /// </summary>
    private static int Dwell(BotDeed deed) =>
        Math.Clamp((int)Math.Round((deed?.Minutes ?? 0.0) * 60000.0), DwellMs, DwellCapMs);

    /// <summary>
    /// How long an undertaking may sit set aside — the bot dead, overloaded, dying, in a squad — before its
    /// reason is presumed stale and it is dropped.
    ///
    /// Ten minutes. An undertaking is not lost the moment something interrupts it, which is the whole point
    /// of holding one; but the market it was walking to closes, the vein it wanted is worked out, and the
    /// bot that comes back to it after an hour is acting on a fact from an hour ago.
    /// </summary>
    public static int AsideCapMs { get; set; } = 600000;

    /// <summary>
    /// How many beats an undertaking may hold one unchanged walk order without ever beating its own best
    /// distance to the place, before it is given up.
    ///
    /// <para>
    /// Six hundred, which is two minutes at a turn every two hundred milliseconds, and deliberately three
    /// times what <c>BotDig</c> and <c>BotProwl</c> allow themselves. Those two measure a walk to a known
    /// seam or a known square; this measures every walk on the shard, including a trip across Britain that
    /// has to go round a wall, and the counter only moves on a beat where the bot failed to beat its own
    /// record — so an honest walk resets it constantly and can never meet this at all. It sits well inside
    /// the stall watch's four minutes on purpose: the errand should end itself and teach its own ledger,
    /// rather than be cancelled from outside with nothing learned.
    /// </para>
    /// </summary>
    public static int TrekLimit { get; set; } = 600;

    /// <summary>Undertakings given up for a walk that stopped closing. For the summary.</summary>
    public static long Trudges { get; private set; }

    /// <summary>
    /// How long an undertaking may answer "working" without ever answering anything else before it is taken
    /// away as jammed.
    ///
    /// <para>
    /// A quarter of an hour, which is far above any honest stretch of standing still — the longest work in
    /// the project reckons itself at eight minutes and breaks that up with walking, and every step, finish or
    /// failure pushes the clock forward. Nothing that is really working can reach this.
    /// </para>
    ///
    /// <para>
    /// It exists because <c>Work</c> is the one answer nothing judges, which makes it the one an undertaking
    /// can hide behind. See the case that handles it for the two afternoons this cost.
    /// </para>
    /// </summary>
    public static int LabourMs { get; set; } = 900000;

    /// <summary>
    /// How much better a new want must be than what is already underway, on top of
    /// <see cref="BotAppraisal.Inertia"/>. Together they are what makes behaviour look deliberate.
    /// </summary>
    public static double SwitchMargin { get; set; } = 1.25;

    /// <summary>How often the population's decisions are summarised in the log.</summary>
    public static int CensusMs { get; set; } = 300000;

    /// <summary>
    /// Whether every commitment is logged as it is taken.
    ///
    /// On by default, because "why is this bot doing that" is the question this project exists to be able to
    /// answer, and a commitment happens at most every <see cref="DwellMs"/> per bot. Off when the log
    /// matters more than the answer: the first version left a 37 MB log for one session.
    /// </summary>
    public static bool Chatty { get; set; } = true;

    private static readonly List<IBotProposer> _proposers = [];

    /// <summary>
    /// Scratch space for one auction. Static and reused, like the planner's path buffer: decisions are made
    /// one bot at a time on the population's beat, and a fresh list per bot per fifteen seconds is garbage
    /// for nothing.
    /// </summary>
    private static readonly List<BotDeed> _offers = [];

    private static readonly Dictionary<string, int> _busy = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Rungs already complained about, so a missing proposer is said once and not per bot per beat.</summary>
    private static readonly HashSet<BotStanding> _mute = [];

    private static long _censusTick;

    /// <summary>
    /// Whether the census clock has been seeded from a real tick. Not "is the stamp zero": on some hosts the
    /// tick count is the machine's uptime counter passed through, so it starts enormous and can wrap
    /// negative. See <c>dev-docs/tick-counts.md</c> in the fork.
    /// </summary>
    private static bool _censused;

    public static long Taken { get; private set; }

    public static long Finished { get; private set; }

    public static long Failed { get; private set; }

    public static long Dropped { get; private set; }

    public static long Deaths { get; private set; }

    /// <summary>
    /// How many times a bot looked for work and found nothing at all worth doing.
    ///
    /// <b>The number to watch, and it is about the world rather than the bots.</b> If work is valued by what
    /// it actually yields, then a bot with nothing worth doing is a shard with nothing left on it that this
    /// bot can profit from. The first version could not distinguish that from a broken motive: thirty-eight
    /// of fifty-one bots ended up patrolling with their drive frozen, which read as a bug in the drive and
    /// was not one.
    /// </summary>
    public static long Barren { get; private set; }

    /// <summary>
    /// Offers not made because the bot's class is sworn to other work. A named nought: see
    /// <see cref="Sworn"/>.
    /// </summary>
    public static long Unsworn { get; private set; }

    /// <summary>
    /// Whether this proposer may be put to this bot at all.
    ///
    /// <para>
    /// <b>Only the free rung is a choice.</b> Mending, flight and the answer to being hit live below the
    /// auction and fire whether or not anything is deciding — a list that could switch those off would be a
    /// class that has sworn not to survive. So the gate is asked of the free rung and nowhere else, and
    /// every class but one leaves the list empty and passes in a single comparison.
    /// </para>
    /// </summary>
    private static bool Sworn(IBotWilful bot, BotStanding rung, IBotProposer proposer)
    {
        if (rung != BotStanding.Free)
        {
            return true;
        }

        var only = (bot?.Self as BotMobile)?.Class?.Sworn;

        if (only is not { Length: > 0 })
        {
            return true;
        }

        for (var i = 0; i < only.Length; i++)
        {
            if (string.Equals(only[i], proposer.Name, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Takes a proposer. Called by the subsystem that owns the work, from its own module's start.
    ///
    /// Registration is not reset when the world reloads: a proposer is code, not state.
    /// </summary>
    public static void Offer(IBotProposer proposer)
    {
        if (proposer == null)
        {
            return;
        }

        for (var i = 0; i < _proposers.Count; i++)
        {
            if (!string.Equals(_proposers[i].Name, proposer.Name, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            logger.Error(
                "Two proposers are both called {Name} ({First} and {Second}); the second is ignored",
                proposer.Name,
                _proposers[i].GetType().Name,
                proposer.GetType().Name
            );

            return;
        }

        _proposers.Add(proposer);

        logger.Information(
            "Proposer {Name} offers work on the {Rung} rung",
            proposer.Name,
            proposer.Rung
        );
    }

    /// <summary>Proposers registered, whatever rung they serve.</summary>
    public static IReadOnlyList<IBotProposer> Proposers => _proposers;

    /// <summary>
    /// One decision. Called on the bot's own beat, and it is the only entry point the bot needs.
    ///
    /// <para>
    /// It does at most three things: settles anything that has ended, advances what is held, and — when the
    /// review is due — holds an auction. Advancing is cheap and happens every beat, because an undertaking
    /// has to be able to notice that it has arrived; choosing is not, and does not.
    /// </para>
    /// </summary>
    public static void Decide(IBotWilful bot)
    {
        if (!Deciding)
        {
            return;
        }

        var resolve = bot?.Resolve;
        var body = bot?.Self;

        if (resolve == null || body == null || body.Deleted)
        {
            return;
        }

        // <b>Some classes are not run by this layer at all, and the King's Rangers are the first of them.</b>
        // Everything here is an auction: proposers offer, the bot weighs, the best offer wins and displaces
        // what it was doing. That is exactly right for a population of tradesmen and exactly wrong for a
        // standing patrol under orders — every skirmish outbid the sweep and threw it away, every ending left
        // the company with nothing in hand, and no amount of adjusting prices or sworn lists fixed it,
        // because the problem was being in the auction at all. They have their own keeper: see BotRangers.
        if ((body as BotMobile)?.Class is { Bidding: false })
        {
            return;
        }

        var now = Core.TickCount;

        Census(now);

        var minutes = resolve.Urges.Since(now);
        var standing = BotLadder.Standing(bot);

        resolve.Standing = standing;

        if (standing == BotStanding.Dead)
        {
            Settle(bot, BotEnding.Died);

            return;
        }

        // Anything above the bot's own business. Set aside rather than abandoned — the road is still
        // underneath, exactly as it is in the journey — unless something on this rung has work of its own.
        if (standing < BotStanding.Busy)
        {
            Aside(bot, resolve, standing, now, minutes);

            return;
        }

        resolve.Aside = false;

        if (resolve.Deed != null)
        {
            resolve.Urges.Held(minutes);

            Advance(bot, resolve);
        }
        else
        {
            resolve.Urges.Idle(minutes);
        }

        // Free to shop, on a clock. The dwell is a floor under this, and the two mean different things — one
        // is how often it looks, the other how soon it may change its mind.
        if (resolve.Due || now - resolve.ReviewedTick >= (resolve.Deed == null ? IdleMs : ReviewMs))
        {
            Auction(bot, resolve, BotStanding.Free, now);
        }
    }

    /// <summary>
    /// The walk an undertaking asked for has ended one way or another. Called from the tick with whatever
    /// <see cref="BotWalk.Advance"/> returned.
    ///
    /// <para>
    /// Only the two endings matter here, and both are the journey saying the place cannot be worked towards:
    /// <see cref="BotWalkResult.Refused"/> is proof there is no way, <see cref="BotWalkResult.GaveUp"/> is a
    /// bot admitting it is getting nowhere. Arriving is not reported, because the undertaking asks the
    /// journey itself — it is the one that knows what arriving means for its own stage.
    /// </para>
    /// </summary>
    /// <summary>Creatures deferred because the road to them was refused. See <see cref="Note"/>.</summary>
    public static long Unreachable { get; private set; }

    public static void Note(IBotWilful bot, BotWalkResult result)
    {
        if (!Deciding || result is not (BotWalkResult.Refused or BotWalkResult.GaveUp))
        {
            return;
        }

        var resolve = bot?.Resolve;
        var deed = resolve?.Deed;

        if (deed == null)
        {
            return;
        }

        // Kept before it is cleared: it is the only record of where the walk was actually headed, and the
        // log line below is the whole reason anybody will know which place was refused.
        var refused = resolve.Sent;

        resolve.Sent = default;

        var bent = false;

        try
        {
            bent = deed.Bend(bot);
        }
        catch (Exception e)
        {
            logger.Error(e, "Undertaking {Kind} threw when told its way was blocked; it is given up", deed.Kind);
        }

        if (bent)
        {
            if (Chatty)
            {
                logger.Information(
                    "{Name} could not get to {Where} and is trying elsewhere for {Deed}",
                    bot.Self?.Name,
                    refused.Follow != null ? refused.Follow.Location : refused.Where,
                    deed
                );
            }

            return;
        }

        // <b>The one exit from an undertaking that said nothing about itself.</b> Every other failure carries
        // a sentence — the shelf was bare, the odds were hopeless, nothing of it was in sight — and this one,
        // which is simply "the way there does not exist and this work has no second place to try", printed
        // the bare words "failed at mine" and stopped. Thirty-five of them in twelve minutes on the morning of
        // 25.08.2026, across unload, mine and hunt, and the only way to find out what any of them meant was
        // to go and read a BotWalk line somewhere above it and match up the timestamps by eye.
        //
        // The destination is right here and costs nothing to say. Named after the creature where there is
        // one, because "no way through to a skeleton" is a fact about a fight and "no way through to
        // (1424, 1468, 9)" is a fact about the ground, and they are chased down differently.
        // <b>A follow whose target has gone prints a place that never existed.</b> The two arms below are
        // "there is a creature" and "there is a spot", and an errand that was following something which has
        // since been deleted is neither: Follow has gone null, Where was never set for it, and the sentence
        // came out as "no way through to (0, 0, 0)". Nought is not a coordinate, it is the absence of one —
        // and a message that prints it sends the next reader hunting for a coordinate bug that is not there.
        // It cost half an hour of exactly that on 26.08.2026.
        var where = refused.Follow != null
            ? $"no way through to {refused.Follow.Name}"
            : refused.Where == Point3D.Zero
                ? "no way through to what it was following, which is gone"
                : $"no way through to {refused.Where}";

        // <b>And the creature is deferred, because no way through is no way through for anybody.</b> The same
        // ruling BotSlay already makes about a quarry it cannot see from the tile beside it: "as true for the
        // next bot along as for this one, which is precisely what the shared list means". A refused road was
        // not making it. On the night of 02-03.09.2026, in two hours and three quarters, 88 of the 830
        // hunts that ended did so on "no way through to" — a rat, a crow, a cat: creatures inside buildings
        // in Britain that every passing bot walked to in turn and none could reach. The mark lapses in two
        // minutes, so this defers them rather than writing them off, and a creature that wanders back out is
        // hunted again.
        //
        // Creatures only. A mend that cannot reach the bot it was going to bandage is a fact about the road
        // and about a friend, and the quarry list is not where either belongs — and the compiler settles that
        // for us, since a bot is a PlayerMobile and can never be a BaseCreature.
        if (refused.Follow is BaseCreature)
        {
            BotQuarry.Shun(refused.Follow);
            Unreachable++;
        }
        else if (refused.Follow != null)
        {
            // The same note about one of ours, kept on its own list because a patient is not quarry. See
            // BotMend.Beyond: 209 of the 230 refused roads of that night were a healer walking to somebody
            // it could not reach, and the picker had no way to know it had just tried.
            BotMend.Beyond(refused.Follow);
            Unreachable++;
        }

        Settle(bot, BotEnding.Failed, where);
    }

    /// <summary>
    /// Something hit this bot. Called from <c>OnDamage</c>, beside <see cref="BotThreat.Decide"/>.
    ///
    /// <b>Told rather than observed</b>, and that is the whole of it: a caster strikes from eight tiles and
    /// never closes, so a bot that learns it is under attack by looking for something adjacent never learns
    /// it at all.
    /// </summary>
    public static void Hurt(IBotWilful bot)
    {
        var resolve = bot?.Resolve;

        if (resolve != null)
        {
            resolve.HurtTick = Core.TickCount;
            resolve.Struck = true;
        }
    }

    /// <summary>The bot died. Called from <c>OnDeath</c>: whatever it was doing ended, and badly.</summary>
    public static void Died(IBotWilful bot) => Settle(bot, BotEnding.Died);

    /// <summary>
    /// This bot is going away for good. Called when it is deleted, and it exists for one reason: the census
    /// counts what is being worked on, and a bot deleted mid-undertaking would leave its work in that count
    /// for ever. A total that is never released does not look wrong — it looks like a busier population.
    /// </summary>
    public static void Forget(IBotWilful bot)
    {
        var resolve = bot?.Resolve;
        var deed = resolve?.Deed;

        if (deed == null)
        {
            return;
        }

        Count(deed.Kind, -1);

        resolve.Deed = null;
        resolve.Sent = default;
    }

    /// <summary>
    /// Above the bot's own business: hold what is held, and let the rung offer something if it can.
    /// </summary>
    private static void Aside(
        IBotWilful bot,
        BotResolve resolve,
        BotStanding standing,
        long now,
        double minutes
    )
    {
        // Not idle: something is happening to it. Boredom is for bots with nothing to do, not for bots in
        // trouble.
        resolve.Urges.Held(minutes);

        // A squad is not a rung with work on it — the squad *is* the work, and it rebases the journey itself.
        // Joining one will be an undertaking when something proposes it.
        if (standing != BotStanding.Bound && Auction(bot, resolve, standing, now))
        {
            return;
        }

        if (resolve.Deed == null)
        {
            // <b>Nothing in hand and something hitting it.</b> That is not a bot too busy being attacked to
            // go shopping — it is a bot standing in a fight with no plan at all, which is the state this rung
            // was written to prevent rather than to produce. The rung lasts eight seconds past the last blow,
            // so a bot that has just given a fight up spends them motionless in the middle of it, and
            // ordinary work is what gets it out: a walk to a mine is a walk away.
            if (standing == BotStanding.Hunted)
            {
                Auction(bot, resolve, BotStanding.Free, now);
            }

            return;
        }

        // <b>Work this rung handed out is not work set aside — it is what the bot is supposed to be doing at
        // this instant, and it was the one thing that never happened.</b> An undertaking is only ever
        // advanced from the Busy path, so a rung could offer a bot its own rescue and then decline to carry
        // it out on the grounds that a bot in trouble is not busy: the medic proposed a bandage, the bandage
        // became the bot's undertaking, and a bot at a third of its health then stood perfectly still holding
        // a plan to heal itself until something killed it. Every rung above Busy had the same hole.
        //
        // Hunted is included whatever handed the work out, because that rung has never been about the work —
        // its own note says it means "no business shopping for work right now" — and suspending a fight
        // because a fight is happening left the whole of BotSlay unreachable for as long as anything was
        // hitting the bot: no giving ground, no casting, no flight, no corpse gone through.
        if (standing == BotStanding.Hunted
            || resolve.Took <= standing
            || standing == BotStanding.Bound && resolve.Deed.Alongside)
        {
            resolve.Aside = false;

            Advance(bot, resolve);

            return;
        }

        // <b>Here, and not before the branch above, and getting that wrong cost an afternoon of my own
        // making.</b> The labour clock must not run on an undertaking nobody is turning the handle of —
        // AsideCapMs is the clock for waiting, and two clocks on one deed is the project's oldest mistake.
        // But the branch above <em>does</em> advance the deed on elevated rungs, so resetting at the top of
        // the aside path reset it on every beat of a bot that was working perfectly normally while something
        // hit it. Godric held a hunt for seventeen minutes with the backstop watching and rejuvenated four
        // times a second. Only the two genuine parkings below reset it.
        if (!resolve.Aside)
        {
            resolve.Aside = true;
            resolve.AsideTick = now;
            resolve.StirredTick = now;

            return;
        }

        if (now - resolve.AsideTick < AsideCapMs)
        {
            resolve.StirredTick = now;

            return;
        }

        logger.Information(
            "{Name} gave up {Deed}: set aside {Minutes:F0} minutes while {Standing}",
            bot.Self?.Name,
            resolve.Deed,
            (now - resolve.AsideTick) / 60000.0,
            standing
        );

        Settle(bot, BotEnding.Dropped);
    }

    /// <summary>Asks what is held what to do now, and does exactly that and nothing else.</summary>
    private static void Advance(IBotWilful bot, BotResolve resolve)
    {
        var deed = resolve.Deed;
        BotDoing doing;

        try
        {
            doing = deed.Advance(bot);
        }
        catch (Exception e)
        {
            // Loud and contained, as everywhere in this assembly: one subsystem's mistake must not take the
            // bot's beat with it, and it must not be quiet either.
            logger.Error(e, "Undertaking {Kind} threw while being advanced; it is given up", deed.Kind);

            Settle(bot, BotEnding.Failed);

            return;
        }

        switch (doing.Kind)
        {
            case BotDoingKind.Walk:
                if (doing.Map == null)
                {
                    logger.Error("Undertaking {Kind} asked for a walk to nowhere; it is given up", deed.Kind);

                    Settle(bot, BotEnding.Failed);

                    return;
                }

                var journey = bot.Journey;

                if (journey == null)
                {
                    // A wilful bot without a journey cannot be sent anywhere. A programming mistake rather
                    // than a situation, and it is said by name instead of throwing inside the beat.
                    logger.Error(
                        "{Name} has no journey, so {Kind} cannot be walked; it is given up",
                        bot.Self?.Name,
                        deed.Kind
                    );

                    Settle(bot, BotEnding.Failed);

                    return;
                }

                // Only when it is somewhere new. Rebasing throws the plan away, and an undertaking that says
                // "walk to the forge" on every beat would otherwise buy a search on every beat and never
                // arrive from further off than a few tiles.
                if (!doing.Matches(resolve.Sent))
                {
                    // Rebase, never Begin. Begin clears the queue, which would wipe the fight the bot is in
                    // the middle of; the bottom of the queue is where the bot belongs and the top is what is
                    // happening to it.
                    if (doing.Follow != null)
                    {
                        journey.Rebase(doing.Map, doing.Follow, doing.Arrival, doing.Note ?? deed.Kind);
                    }
                    else
                    {
                        journey.Rebase(doing.Map, doing.Where, doing.Arrival, doing.Note ?? deed.Kind);
                    }

                    resolve.Sent = doing;
                    resolve.Nearest = int.MaxValue;
                    resolve.Trudged = 0;

                    // <b>Inside the rebase, not beside it, and the difference is a bot standing still for
                    // twenty minutes.</b> The labour clock was reset on every walk answer, on the reasoning
                    // that a step is a sign of life. A step is; the same step is not. An undertaking that
                    // asks for one unchanged destination beat after beat has not moved through its chain at
                    // all, and it kept the clock young for ever — Perri held a rescue for twenty-one minutes
                    // that way with the backstop watching and never firing.
                    //
                    // Here it fires only when the order actually changed, which is the same test the rebase
                    // itself uses, and that is precisely the definition of the undertaking getting somewhere:
                    // a new place to be, a new stage, a new leg. The longest honest journey on this shard is
                    // a couple of minutes, so nothing that is really walking can age out.
                    resolve.StirredTick = Core.TickCount;

                    return;
                }

                // <b>The same order, beat after beat, with the bot no nearer than it was — and until now
                // nothing anywhere counted that.</b> Two errands out of a dozen wrote this rule for
                // themselves — <c>BotDig</c> and <c>BotProwl</c>, both after a night of it — and the ones
                // that did not were left to the stall watch, which is four minutes away and cures the
                // symptom by cancelling the work. On 04.09.2026 the whole of what was left of standing
                // still was that: Kelda four minutes into "taking 1 Katana to Aislinn", Merrick into
                // "taking 10 Leather to Abra", Rowan into "taking 10 Raw Ribs to Iman", none of them a
                // company, none of them a fight, all of them a walk to a shopkeeper that had stopped
                // closing. Written here so it is one rule rather than a twelfth copy.
                //
                // <b>Only a fixed place, never a follow.</b> Work that walks after something moving points
                // at the target's own tile every beat, so "no nearer than it was" is true of it while it is
                // winning — that is this project's oldest false alarm and it is fenced out by construction.
                if (doing.Follow == null && bot.Self is { Deleted: false } walker && walker.Map == doing.Map)
                {
                    var away = Math.Max(
                        Math.Abs(walker.Location.X - doing.Where.X),
                        Math.Abs(walker.Location.Y - doing.Where.Y)
                    );

                    if (away < resolve.Nearest)
                    {
                        resolve.Nearest = away;
                        resolve.Trudged = 0;
                    }
                    else if (++resolve.Trudged >= TrekLimit)
                    {
                        Trudges++;

                        Settle(
                            bot,
                            BotEnding.Failed,
                            $"it got no nearer than {away} tiles to ({doing.Where.X}, {doing.Where.Y})"
                        );
                    }
                }

                return;

            case BotDoingKind.Work:
                // Nothing at all, and that is the answer. A bot at a counter, at a forge or at a vein is
                // standing still on purpose, its journey has no plan, and nothing judges it for progress.
                //
                // <b>Except that an undertaking which never answers anything else is invisible and
                // immortal, and two of them were found in one afternoon.</b> Three tailors held a craft the
                // engine had quietly stopped serving — CraftItem.Craft takes an action lock and returns in
                // silence when it is already held — and stood at their benches for two hours; Merrick held a
                // rescue for fifty-seven minutes without dying, failing, or writing a single line. Both read
                // as "working" in every summary, because this is the answer that means exactly that.
                //
                // So the licence stands and gets an outer edge. Any other answer — a step, a finish, a
                // failure — is a sign of life and pushes the clock forward; only an unbroken run of this one
                // ages. LabourMs is set far above any honest stretch of standing still, so nothing that is
                // really working ever meets it, and a bot the world has stopped talking to is back in the
                // population within the quarter hour instead of at the end of the day.
                if (Core.TickCount - resolve.StirredTick >= LabourMs)
                {
                    Settle(bot, BotEnding.Failed, $"nothing has come of this in {LabourMs / 60000} minutes");

                    return;
                }

                return;

            case BotDoingKind.Done:
                Settle(bot, BotEnding.Done, doing.Note);

                return;

            case BotDoingKind.Failed:
                Settle(bot, BotEnding.Failed, doing.Note);

                return;

            default:
                logger.Error("Undertaking {Kind} said nothing when asked what to do; it is given up", deed.Kind);

                Settle(bot, BotEnding.Failed);

                return;
        }
    }

    /// <summary>
    /// Collects what is on offer for this rung, scores it, and takes the best if it is worth changing to.
    /// Returns whether anything was taken on.
    /// </summary>
    private static bool Auction(IBotWilful bot, BotResolve resolve, BotStanding rung, long now)
    {
        var held = resolve.Deed;

        // Too soon to change our mind on purpose. Not applied to the rungs above: a bot that cannot walk
        // needs to put something down now, not in half a minute.
        //
        // <b>Noted rather than returned on, and that is a change with a reason.</b> Refusing to look at all
        // meant the dwell could not tell "a better field to dig" from "an ogre eight tiles away": both were
        // simply not asked about for half a minute, and a bot that is not asked walks past. So the offers are
        // collected either way and the floor is applied at the end, where the one thing that can lift it —
        // <see cref="BotDeed.Pressing"/> — is known.
        var fresh = held != null && rung == BotStanding.Free && now - resolve.SinceTick < Dwell(held);

        resolve.ReviewedTick = now;
        resolve.Due = false;

        _offers.Clear();

        // What the free rung had for this bot, kept rather than dropped. See BotResolve.Offered: the whole
        // list is computed here anyway, and asking a second time elsewhere is not a free question.
        var keep = rung == BotStanding.Free;

        if (keep)
        {
            resolve.Offered.Clear();
            resolve.OfferedTick = now;
        }

        var asked = 0;
        var largestOutlay = 0;

        for (var i = 0; i < _proposers.Count; i++)
        {
            var proposer = _proposers[i];

            if (proposer.Rung != rung)
            {
                continue;
            }

            // <b>Work this bot's class is not allowed to be offered at all, which is not the same as work it
            // would score badly.</b> See BotClass.Sworn: a class whose errands are chosen for reasons the
            // arithmetic cannot see cannot be expressed by scoring, because a price high enough to win is a
            // rigged auction and an honest one loses to every hunt on the island. So the offer is never made.
            //
            // Counted rather than silent, and counted here rather than inside the proposers: every proposer
            // on this shard tallies its own refusals by reason, and a bot that is never asked would otherwise
            // vanish from all of them at once — a Baron doing nothing would look exactly like a Baron with
            // nothing to do.
            if (!Sworn(bot, rung, proposer))
            {
                Unsworn++;

                continue;
            }

            asked++;

            BotDeed offer = null;

            try
            {
                offer = proposer.Propose(bot);
            }
            catch (Exception e)
            {
                logger.Error(e, "Proposer {Name} threw while offering work; it is skipped", proposer.Name);
            }

            if (offer?.Map == null)
            {
                continue;
            }

            if (offer.Outlay > largestOutlay)
            {
                largestOutlay = offer.Outlay;
            }

            if (keep)
            {
                resolve.Offered.Add(proposer.Name);
            }

            _offers.Add(offer);
        }

        if (asked == 0)
        {
            // Nobody at all answers this rung. On the free rung that is the same fact as "nothing was worth
            // doing" and has to be counted as it, or a shard with no proposers registered would report a
            // contented population.
            if (rung == BotStanding.Free && held == null)
            {
                Nothing(resolve, now, "nobody answers this rung at all");
            }

            Unserved(rung);

            return false;
        }

        // Need is what the plans on offer cost against what is in the purse. Computed here because this is
        // the only place both halves are known, and zero when nothing on offer costs anything.
        resolve.Urges.Weigh(BotYield.Wealth(bot.Self), largestOutlay);

        BotDeed best = null;
        var bestScore = 0.0;
        var bestWeigh = default(BotWeigh);
        BotDeed second = null;
        var secondScore = 0.0;
        var viable = 0;
        string firstVeto = null;

        for (var i = 0; i < _offers.Count; i++)
        {
            var offer = _offers[i];
            var score = BotAppraisal.Weigh(bot, offer, Share(offer.Kind), out var weigh, out var veto);

            if (score <= 0.0)
            {
                firstVeto ??= veto;

                continue;
            }

            viable++;

            if (score > bestScore)
            {
                second = best;
                secondScore = bestScore;
                best = offer;
                bestScore = score;
                bestWeigh = weigh;
            }
            else if (score > secondScore)
            {
                second = offer;
                secondScore = score;
            }
        }

        if (best == null)
        {
            if (held == null && rung == BotStanding.Free)
            {
                Nothing(
                    resolve,
                    now,
                    _offers.Count == 0
                        ? $"{asked} proposers asked, not one of them had anything to offer"
                        : $"{asked} proposers asked, {_offers.Count} offered work, and it was refused: {firstVeto}"
                );
            }

            return false;
        }

        if (held != null)
        {
            // Something that will not wait, and is not more of what the bot is already doing. The second half
            // of that matters as much as the first: without it a hunter swaps quarry every time a second
            // creature wanders inside the notice, which is the impulsive bot the margin was written against
            // wearing the word "urgent".
            var jumps = best.Pressing(bot) && !held.Kind.InsensitiveEquals(best.Kind);

            if (fresh && !jumps)
            {
                return false;
            }

            // The margin, and it is the difference between a bot with intentions and a bot with impulses.
            // Waived for what will not wait — the comparison is then a plain one, "is this worth more than
            // what I am doing", with no bonus for stubbornness on either side.
            var heldScore = BotAppraisal.Weigh(bot, held, Share(held.Kind), out _) * (jumps ? 1.0 : BotAppraisal.Inertia);

            if (bestScore <= heldScore * (jumps ? 1.0 : SwitchMargin))
            {
                return false;
            }
        }

        Commit(bot, resolve, rung, best, bestWeigh, second, secondScore, now, _offers.Count, viable);

        return true;
    }

    private static void Commit(
        IBotWilful bot,
        BotResolve resolve,
        BotStanding rung,
        BotDeed deed,
        BotWeigh weigh,
        BotDeed instead,
        double insteadScore,
        long now,
        int table,
        int viable
    )
    {
        var dropped = resolve.Deed;

        if (dropped != null)
        {
            // Chosen against, not failed. The takings are recorded honestly and no blame attaches to the
            // place — the first version's answer here was a five-minute ban on the whole activity.
            Settle(bot, BotEnding.Dropped);
        }

        resolve.Deed = deed;
        resolve.Took = rung;
        resolve.Stake = BotYield.Take(bot, deed);
        resolve.SinceTick = now;
        resolve.ReviewedTick = now;
        resolve.StirredTick = now;
        resolve.Due = false;
        resolve.Aside = false;
        resolve.Sent = default;
        // <b>How many were on the table, because absence of a runner-up read as a rich choice.</b> The take
        // line named a second place only when one existed, so "chosen over five better ideas" and "the only
        // thing anybody offered" printed identically. On 02.09.2026 Quill took acquire twelve times inside two
        // seconds, each at 0/min, and went from 400gp to 8gp; not one of those lines said whether anything
        // else had been offered at all, and the answer changes what the finding is.
        resolve.Because = $"{weigh.Describe()}; {viable} of {table} offers worth anything";

        // Cleared here for the same reason Because is written here: a stale one reads as the present one.
        resolve.Empty = null;
        resolve.Urges.Fruitful();

        Count(deed.Kind, 1);
        Taken++;

        if (!Chatty)
        {
            return;
        }

        logger.Information(
            "{Name} took on {Deed}: {Why}{Instead}{Dropped}",
            bot.Self?.Name,
            deed,
            resolve.Because,
            instead == null ? "" : $"; over {instead} at {insteadScore:F0}/min",
            dropped == null ? "" : $"; dropped {dropped}"
        );
    }

    /// <summary>
    /// The takings are counted, the ledger is told, and the undertaking is let go. The one place an
    /// undertaking ends, whichever of the four ways it ended.
    /// </summary>
    /// <param name="why">
    /// What the undertaking said as it ended, in its own words.
    ///
    /// <b>It used to be thrown away, and that was a hole in the one thing this project is built to do.</b>
    /// Every undertaking already explains itself — "the fire would not take the ore", "that stall is empty
    /// now", "nothing left to mend with" — and the settlement logged only the arithmetic, so a hundred and
    /// forty-five identical failures were indistinguishable from each other and from bad luck. A reason that
    /// is written and then discarded is worse than no reason at all: it looks like the question was answered.
    /// </param>
    /// <summary>
    /// Takes a piece of work off a bot that has stopped getting anywhere with it. See <c>BotStall</c>.
    ///
    /// <para>
    /// <b>A work that answers "still going" for ever is immortal, and this shard has produced several.</b>
    /// Every leg of every errand must end in done, failed, or a step — but a step that cannot be taken is
    /// none of the three: the bot asks the same walk, the journey gives it up, the work asks for the same
    /// walk again, and the pair of them will do that until the world reloads. A crafter stood four minutes
    /// on "after Cloth" that way, and nothing in either subsystem was wrong on its own.
    /// </para>
    ///
    /// <para>
    /// Ended as a failure rather than quietly dropped, so the ledger learns the place was no good and the
    /// bot is less inclined to take the same errand to the same spot again. That is the same treatment any
    /// other unreachable destination gets, and it is what stops this from being a loop with a longer period.
    /// </para>
    ///
    /// <para>
    /// <b>"The ledger learns the place was no good" means <c>Ledger.Note</c> here and not
    /// <see cref="BotDeed.Bend"/>, and that distinction was worth a reverted change to find out.</b> Note
    /// folds a nought-a-minute reading into the band; Bend writes the outright caution that
    /// <c>Ledger.Cautious</c> reads, and it is reached only from
    /// <see cref="Note(IBotWilful,BotWalkResult)"/> — the walker's own refusal. A journey that
    /// <em>follows</em> a mobile never refuses, so a shopkeeper nobody can reach is followed until this
    /// method takes the errand away, and the shop is never cautioned. Calling Bend from here looks like the
    /// missing line and is not: this island has four fires and <b>two counters</b>, so cautioning one on a
    /// single stall takes half of a bot's counters away for the span, and "can reach nothing from here, and
    /// it is standing at home" went from one to three in the matched window on 05.09.2026 while nothing got
    /// better. The claimed benefit was not there either — no bot repeats a vendor even without it, because
    /// the queue at an unreachable shopkeeper is a different bot each time and a per-bot ledger cannot see
    /// that. Whatever the cure for a followed mobile is, it is not a stronger mark in one bot's own book.
    /// </para>
    /// </summary>
    public static void Abandon(IBotWilful bot, string why)
    {
        if (bot?.Resolve?.Deed == null)
        {
            return;
        }

        Settle(bot, BotEnding.Failed, why);
    }

    private static void Settle(IBotWilful bot, BotEnding ending, string why = null)
    {
        var resolve = bot?.Resolve;
        var deed = resolve?.Deed;

        if (deed == null)
        {
            return;
        }

        var takings = BotYield.Settle(bot, deed, resolve.Stake, ending);

        resolve.Ledger.Note(deed.Kind, deed.Map, deed.Where, takings.PerMinute);

        // And the same outcome on the population's board, which is a different record answering a different
        // question: this one is what the island is like, and it is what a bot who has never been here reads.
        // See BotCommons. The mind's flag rides along so the report can say how much of what the shard knows
        // it was told by a bot that thinks — which is the whole of what those three are for.
        var told = bot.Self is BotMobile { Minded: true };

        BotCommons.Note(deed.Kind, deed.Map, deed.Where, takings.PerMinute, told);

        // And what the trade claimed against what it came to, which is how the shard corrects the constants
        // in its own source. See BotCommons.Corrected.
        BotCommons.Claimed(deed.Kind, deed.Expects, takings.PerMinute, told);

        // <b>Wary of a place that paid, which is the opposite of what caution is for.</b>
        //
        // Beware exists to stop a bot going back to somewhere the work does not happen. A great many
        // undertakings end as failures having nonetheless done the work: a tool wears through - the engine
        // gives one 25 to 75 uses and destroys it at zero - a seam empties, a shelf runs out, the light goes.
        // On the night of 03.09.2026 the scribes ended 89 errands with "nothing to write with" and those 89
        // endings carried 15358 gold of scrolls between them, at rates up to 730 a minute. Every one of them
        // taught the bot to be wary of the shop it had just earned that in.
        //
        // The takings are the test and they are already in hand a few lines above. Nothing was produced is
        // what "this place was no good" means; a death says it regardless, because coming back from one with
        // a full pack does not make the ground safe.
        if (ending == BotEnding.Died || (ending == BotEnding.Failed && takings.Worth <= 0))
        {
            resolve.Ledger.Beware(deed.Kind, deed.Map, deed.Where);
        }
        else if (ending == BotEnding.Done)
        {
            // Only Done, and never Dropped: a bot that walked away from this to do something better has
            // learned nothing whatever about the place, and letting that clear a suspicion would let a busy
            // bot forgive the same wall over and over. See BotLedger.Worked.
            resolve.Ledger.Worked(deed.Kind, deed.Map, deed.Where);
        }

        resolve.Urges.Paid(takings.Worth);

        Count(deed.Kind, -1);

        switch (ending)
        {
            case BotEnding.Done:
                Finished++;

                break;

            case BotEnding.Failed:
                Failed++;

                break;

            case BotEnding.Dropped:
                Dropped++;

                break;

            case BotEnding.Died:
                Deaths++;

                break;
        }

        resolve.Deed = null;
        resolve.Sent = default;
        resolve.Aside = false;

        // <b>And the road it was on, which nothing else was going to put down.</b> A journey is completed by
        // the walker, and the walker is only asked while a bot is walking — so an undertaking that ends any
        // other way (done on the spot, failed, dropped for something better) leaves its errand standing in
        // the queue for ever. For a bot that immediately takes new work this is invisible: the next walk
        // rebases the journey over it. For a bot that takes nothing it is a lie that never expires.
        //
        // Measured 02.09.2026: Joss the Warrior, holding no work at all for four minutes and motionless for
        // three of them, reported as "walking to 1440,1470, 15 tiles off, set out 5m ago" — an errand from a
        // walk home that had finished five minutes earlier. Six bots beside it were in the same state. Every
        // instrument on this shard read them as travelling, which is exactly the sort of quiet wrong number
        // that costs an evening to unpick.
        //
        // Only when there is nothing left to do: a deed that ended by handing over to another leg of its own
        // queue still owns the rest of it, and Finish would throw that away.
        if (bot.Journey is { Active: true } road && resolve.Deed == null)
        {
            road.Finish();
        }

        // Due to look again at once. A bot that has just finished a job should not stand about waiting for a
        // review it does not need: there is nothing to change its mind about. A flag rather than a zeroed
        // stamp, because a tick count of zero is a legitimate reading on this shard's hosts.
        resolve.Due = true;

        try
        {
            deed.Drop(bot);
        }
        catch (Exception e)
        {
            logger.Error(e, "Undertaking {Kind} threw while being let go", deed.Kind);
        }

        if (Chatty)
        {
            logger.Information(
                "{Name} {Ending} {Deed}: {Takings}{Why}",
                bot.Self?.Name,
                Word(ending),
                deed,
                takings,
                why == null ? "" : $" — {why}"
            );
        }
    }

    private static string Word(BotEnding ending) =>
        ending switch
        {
            BotEnding.Done => "finished",
            BotEnding.Failed => "failed at",
            BotEnding.Dropped => "dropped",
            _ => "died doing"
        };

    /// <summary>
    /// What share of the population's undertakings are this kind of work. The crowd every bot can see,
    /// derived from a common fact rather than sent to anybody.
    /// </summary>
    private static double Share(string kind)
    {
        if (kind == null || !_busy.TryGetValue(kind, out var mine) || mine <= 0)
        {
            return 0.0;
        }

        var total = 0;

        foreach (var (_, count) in _busy)
        {
            if (count > 0)
            {
                total += count;
            }
        }

        return total <= 0 ? 0.0 : (double)mine / total;
    }

    /// <summary>
    /// A bot looked for work and there was none.
    ///
    /// Counted once per drought rather than once per look, so the figure means "this many times a bot ran out
    /// of things worth doing" and not "this many beats went by while it had nothing".
    /// </summary>
    /// <summary>
    /// Nothing was taken, and the two numbers that say why.
    ///
    /// <para>
    /// <b>The count alone sent whoever read it back to the raw log.</b> Until 02.09.2026 this recorded a
    /// boolean and a tally, so fifteen idle bots in the camp — 300gp each, two minutes apiece — could be
    /// seen but not explained: an empty table and a table nobody would pay for read identically. The words
    /// are kept on the resolve rather than logged, because a barren bot is barren every tick and a line per
    /// tick per bot is not a measurement, it is a flood.
    /// </para>
    /// </summary>
    private static void Nothing(BotResolve resolve, long now, string why)
    {
        if (!resolve.Urges.IsBarren)
        {
            Barren++;
        }

        resolve.Empty = why;
        resolve.Urges.Barren(now);
    }

    private static void Count(string kind, int by)
    {
        if (kind == null)
        {
            return;
        }

        _busy.TryGetValue(kind, out var count);

        count += by;

        if (count <= 0)
        {
            _busy.Remove(kind);

            return;
        }

        _busy[kind] = count;
    }

    /// <summary>
    /// A rung that should have somebody answering it and has not. Said once, by name, in the same voice the
    /// module loader uses for a subsystem that ought to be running: silence about a missing answer is how
    /// the first version's gaps survived whole sessions.
    /// </summary>
    private static void Unserved(BotStanding rung)
    {
        // Only the rungs that are meant to have somebody on them. Being hit is answered by BotThreat and a
        // squad answers for itself, so silence on those two is the design rather than a gap.
        if (rung is not (BotStanding.Free or BotStanding.Failing) || !_mute.Add(rung))
        {
            return;
        }

        if (rung == BotStanding.Free)
        {
            logger.Error(
                "Nothing proposes any work at all, so every bot will find nothing worth doing. A kind of work is a folder with an IBotProposer in it, handed to BotWill.Offer"
            );

            return;
        }

        logger.Error(
            "Nothing proposes work for the {Rung} rung; bots on it will keep what they have and wait it out",
            rung
        );
    }

    private static void Census(long now)
    {
        if (!_censused)
        {
            _censused = true;
            _censusTick = now;

            return;
        }

        if (now - _censusTick < CensusMs)
        {
            return;
        }

        _censusTick = now;

        logger.Information("Will: {State}", Describe());
    }

    /// <summary>
    /// One line about what the population is up to. For the census and for the boot log.
    ///
    /// <see cref="ValueStringBuilder"/> rather than <c>System.Text.StringBuilder</c>, which this shard does
    /// not use anywhere: it writes into a stack buffer and grows into a pooled one only if it has to.
    /// </summary>
    public static string Describe()
    {
        using var line = ValueStringBuilder.Create(256);

        line.Append(
            $"{Taken} taken on, {Finished} finished, {Failed} failed, {Dropped} dropped, {Deaths} died doing it; {Barren} times nothing was worth doing, {Unsworn} offers withheld from classes sworn elsewhere, {Trudges} given up for a walk that stopped closing; holding now:"
        );

        var kinds = 0;

        foreach (var (kind, count) in _busy)
        {
            if (count <= 0)
            {
                continue;
            }

            // The separator is appended on its own rather than chosen inside the interpolation: a ternary
            // with interpolated branches is built before the call and allocates a string, which is the one
            // thing the zero-allocation handler is for.
            if (kinds > 0)
            {
                line.Append(",");
            }

            line.Append($" {kind} {count}");
            kinds++;
        }

        if (kinds == 0)
        {
            line.Append(" nothing");
        }

        return line.ToString();
    }

    /// <summary>
    /// Everything about the last world back to nothing. Proposers stay — they are code — and so does the
    /// switch; the counters and the census do not, because they describe a population that no longer exists.
    /// </summary>
    public static void Reset()
    {
        _offers.Clear();
        _busy.Clear();
        _mute.Clear();

        _censused = false;

        Taken = 0;
        Finished = 0;
        Failed = 0;
        Dropped = 0;
        Deaths = 0;
        Barren = 0;
        Unsworn = 0;
        Trudges = 0;
    }
}
