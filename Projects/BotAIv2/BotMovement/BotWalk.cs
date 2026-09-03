using System;
using System.Collections.Generic;
using Server.Items;
using Server.Logging;

namespace Server.BotAI.V2;

/// <summary>
/// Something standing in the way that can be asked to move. Implemented by the bot.
///
/// The measurement that made this necessary: (1371, 1477, 10) is the gate to the Britain graveyard, and
/// two bots parked on it accounted for seventy-seven refused steps in two minutes — twice, on two
/// different days, with two different bots. A wandering monster moves on by itself; a bot at an auction or
/// going through a corpse does not, and if it happens to be in a doorway then nobody gets through the
/// doorway. The tile is the problem, not the bot, so the answer belongs to whoever is standing on it.
/// </summary>
public interface IBotAside
{
    /// <summary>
    /// Take a step, if there is anything else to be doing. Returns whether the tile was actually freed.
    /// </summary>
    bool StepAsideFor(Mobile asker);
}

/// <summary>What one attempt at moving did.</summary>
public enum BotWalkResult
{
    /// <summary>No journey. Nothing to do.</summary>
    Idle,

    /// <summary>Standing on, or beside, the destination. See <see cref="BotArrival"/>.</summary>
    Arrived,

    /// <summary>A tile of the plan was walked.</summary>
    Stepped,

    /// <summary>A shut door was in the way and has been opened. Next tick walks through it.</summary>
    OpenedDoor,

    /// <summary>Somebody was on the next tile. They have been asked to move and the tile is off the next plan.</summary>
    WentRound,

    /// <summary>The plan would not walk, but a step was possible, so a step was taken.</summary>
    Improvised,

    /// <summary>Mid-spell. The engine forbids movement, and this is not a wall.</summary>
    Casting,

    /// <summary>The step was refused and nothing helped. Not fatal — the next tick tries again.</summary>
    Blocked,

    /// <summary>There is no way there. Proven, not guessed. The journey is over.</summary>
    Refused,

    /// <summary>Genuinely getting nowhere for long enough to admit it. The journey is over.</summary>
    GaveUp
}

/// <summary>
/// The moment a step is actually taken — where everything the planner deliberately ignores gets handled.
///
/// <para>
/// The planner reasons about static ground only: land, statics, houses, boats. Creatures, dropped items
/// and shut doors are not in it, because they move, and a planner that treats a skeleton in a doorway as a
/// wall teaches itself that the doorway is one. This is where that debt is paid, once per step, with the
/// engine as the final authority.
/// </para>
///
/// <para>
/// <b>The last line of defence lives here, and it is the one place in the whole design that trusts the
/// engine over the plan.</b> If the plan will not walk and some step is nonetheless possible, take it. The
/// engine is the only authority on whether a step is legal right now, and a bot that can get somewhere is
/// worth more than a bot that is correct about being unable to get where it meant to go.
/// </para>
/// </summary>
public static class BotWalk
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotWalk));

    /// <summary>
    /// A walking step, matching the engine's own <c>movement.delay.walkFoot</c> default. Bots have no
    /// <c>NetState</c>, so nothing throttles them, and this is the only thing keeping them from
    /// teleporting along their route at tick rate.
    /// </summary>
    public const int WalkStepMs = 400;

    /// <summary>A running step, matching <c>movement.delay.runFoot</c>.</summary>
    public const int RunStepMs = 200;

    /// <summary>How far away a door can be and still be the thing blocking this step.</summary>
    private const int DoorReach = 3;

    /// <summary>
    /// Attempts against the same occupied tile before it is planned around rather than waited out.
    /// </summary>
    private const int PatienceWithOccupants = 2;

    /// <summary>The vertical space a standing person occupies — two bodies further apart are on two floors.</summary>
    private const int PersonHeight = BotArrival.PersonHeight;

    /// <summary>Scratch, reused: a plan that allocates is a plan that costs more in collection than in work.</summary>
    private static readonly List<Point3D> _path = [];

    /// <summary>
    /// Whether the population may move at all. <b>False until the movement module starts</b>, so switching
    /// the module off in <c>modernuo.json</c> freezes every bot where it stands while everything else goes
    /// on running.
    ///
    /// That is the diagnostic the first version never had. Half its investigations were the same question —
    /// is this navigation, or is navigation covering for something else? — and the answer took hours of
    /// watching a live shard. Four of the things it eventually found under the navigation layer were not
    /// navigation at all.
    /// </summary>
    public static bool Walking { get; internal set; }

    public static long Steps { get; private set; }

    public static long Refusals { get; private set; }

    public static long Doors { get; private set; }

    public static long Detours { get; private set; }

    public static long Improvised { get; private set; }

    public static long GaveUp { get; private set; }

    /// <summary>Errands ended because the destination itself was proved no good, rather than the road to it.</summary>
    public static long Dropped { get; private set; }

    public static void Reset()
    {
        Steps = 0;
        Refusals = 0;
        Doors = 0;
        Detours = 0;
        Improvised = 0;
        GaveUp = 0;
        Dropped = 0;
    }

    public static string Describe() =>
        $"{Steps} steps taken, {Refusals} refused by the engine, {Doors} doors opened, {Detours} tiles gone round, {Improvised} improvised, {GaveUp} journeys given up, {Dropped} destinations dropped as no good";

    /// <summary>How long the caller should wait before asking again.</summary>
    public static int StepDelayMs(bool run) => run ? RunStepMs : WalkStepMs;

    /// <summary>
    /// One attempt at getting closer. Plans if it must, steps if it can, and says what happened.
    ///
    /// The caller owns the clock: this does one thing and returns, and the tick decides when to come back
    /// — <see cref="StepDelayMs"/>.
    /// </summary>
    public static BotWalkResult Advance(Mobile bot, BotJourney journey, bool run)
    {
        if (!Walking || bot == null || journey == null || !journey.Active)
        {
            return BotWalkResult.Idle;
        }

        var map = bot.Map;

        if (map == null || map == Map.Internal || !bot.Alive)
        {
            return BotWalkResult.Idle;
        }

        // Errands whose reason has evaporated go first, and this is how the road resumes by itself: the
        // interruption that was "kill that thing" ends when the thing is dead, and what it was covering is
        // already on top by the time anything else is decided.
        if (journey.Prune() > 0 && !journey.Active)
        {
            return BotWalkResult.Idle;
        }

        journey.Catch(bot.Location);

        if (journey.Arrived(bot.Location))
        {
            return BotWalkResult.Arrived;
        }

        // Before anything else, because everything below assumes the bot is trying: a bot that has made no
        // progress along its plan for long enough has to be allowed to admit it, or it is lost until the
        // shard restarts.
        if (journey.Stalled())
        {
            // Whose errand is ending matters to everybody upstairs. See Ended.
            var side = journey.Current?.Interruption == true;

            // The load and the stamina are in the line because "made no progress" has two completely
            // different causes and they are indistinguishable without them: ground it cannot cross, or a pack
            // it cannot carry. Past the engine's overweight ceiling every step costs five stamina and at zero
            // the step is refused outright — a bot in that state cannot walk to the bank that would cure it.
            logger.Information(
                "{Name} made no progress towards {Where} and has given up ({Reason}); {Left} errands left, carrying {Load} of {Ceiling} stones, {Stam} stamina",
                bot.Name,
                journey.Target,
                journey.Reason,
                journey.Queued - 1,
                BotLadder.Load(bot),
                BotLadder.Ceiling(bot),
                bot.Stam
            );

            journey.Complete();
            GaveUp++;

            return Ended(side);
        }

        // Mid-cast, before anything is counted.
        //
        // <b>The first version could not tell a spell from a wall, and this is the last corner of that.</b>
        // v2 already knew not to redraw the route over a cast — the refusal reports itself as
        // <see cref="BotWalkResult.Casting"/> — but the attempt was tallied on the way there, and the stall
        // measure counts attempts. So a caster standing still to finish a spell spent its patience on doing
        // exactly what it was supposed to be doing: a hundred attempts is twenty seconds, and the journey gave
        // up in the middle of the fight. One warrior-mage took nine hunts that way and finished none of them.
        //
        // The engine will not move a caster at all, so there is nothing here to attempt and nothing to learn
        // from failing to.
        if (bot.Spell != null)
        {
            return BotWalkResult.Casting;
        }

        // Counted here, before anything can succeed or fail: the stall measure is attempts at stepping, not
        // seconds, so that a bot held up by a fight pays nothing for the delay.
        journey.Attempted();

        var planning = journey.Current?.Interruption == true;

        if (journey.NeedsPlan(bot.Location) && !Plan(bot, journey, map))
        {
            return planning ? BotWalkResult.Blocked : BotWalkResult.Refused;
        }

        // Search after search with nowhere at all to walk. Not a refusal — that is provable and this is not
        // — but continuing to ask is the fence-hugging this subsystem exists to end, in a new form.
        if (journey.Hopeless)
        {
            var side = journey.Current?.Interruption == true;

            logger.Information(
                "{Name} could not get one tile closer to {Where} in {Plans} plans and has dropped it ({Reason})",
                bot.Name,
                journey.Target,
                journey.Plans,
                journey.Reason
            );

            journey.Complete();
            GaveUp++;

            return Ended(side);
        }

        if (!journey.TryNextTile(out var next))
        {
            // Planned, and there is nothing to walk: the search could not get one tile closer. Not a
            // refusal — that is a separate, provable thing — so try the world directly before concluding.
            return Improvise(bot, journey, next: bot.Location, run);
        }

        var before = bot.Location;
        var direction = bot.GetDirectionTo(next, run);

        // Set first, then move. The engine treats a Move whose direction differs from the mobile's own as a
        // turn and goes nowhere, so without this every other tick is spent pivoting.
        bot.Direction = direction;

        if (bot.Move(direction) && bot.Location != before)
        {
            Steps++;

            journey.Stepped(bot.Location);

            // The bot has just done something the reach ledger may believe impossible. If the two tiles are
            // filed under different sealed pockets, the ledger is out of date — somebody built a door, or a
            // wall came down — and the world has just corrected it.
            BotReach.Contradict(map, before, bot.Location);

            return journey.Arrived(bot.Location) ? BotWalkResult.Arrived : BotWalkResult.Stepped;
        }

        Refusals++;

        return Refused(bot, journey, map, next, run);
    }

    /// <summary>
    /// How to report an errand that has just been given up: as the undertaking's failure, or as nothing much.
    ///
    /// <para>
    /// <b>Only the bot's own errand belongs to the undertaking.</b> An interruption is something that happened
    /// <em>to</em> the bot — it was hit, and the road went underneath while it dealt with that — so giving up
    /// on one says nothing whatever about the work. Reporting it as a refusal made the decision layer fail the
    /// undertaking instead: a caster shooting from across a river, which cannot be walked to, was quietly
    /// cancelling whatever the bot had been doing, over and over. A healer lost the patient it was mending;
    /// miners lost the ore they were carrying.
    /// </para>
    ///
    /// <para>
    /// <see cref="BotWalkResult.Blocked"/> is the honest answer for that case, and the decision layer already
    /// ignores it: nothing is wrong with the work, the bot simply could not get at something. The road it was
    /// on is still underneath, exactly as the queue intends.
    /// </para>
    /// </summary>
    private static BotWalkResult Ended(bool interruption) =>
        interruption ? BotWalkResult.Blocked : BotWalkResult.GaveUp;

    /// <summary>
    /// How many plans in a row may come back short of the destination, without the destination ever getting
    /// nearer, before the far side of it is asked about.
    ///
    /// <para>
    /// Two, and the number is a discriminator rather than a patience setting. A bot walking a long road
    /// legitimately gets a short plan every time — but each one starts further along, so its best distance
    /// keeps improving and this never reaches two. What does reach two is a bot whose road ends at the
    /// same place however often it is redrawn, which is the signature of something in the way rather than
    /// something far off. Costing the far-side look at that signature is what keeps it off the hot path.
    /// </para>
    /// </summary>
    public static int PlansBeforeAskingTheFarSide { get; set; } = 2;

    /// <summary>Draws a plan. Returns false only when the destination is provably unreachable.</summary>
    private static bool Plan(Mobile bot, BotJourney journey, Map map)
    {
        // A search is charged by how far it is going, so a chase costs a few milliseconds and an island
        // crossing costs sixty. That is right for almost everything and wrong for one case: a goal twenty
        // tiles off whose road runs four hundred tiles round a lake is a short journey by every measure the
        // planner has. The signal that tells them apart is already here — a journey that has drawn a plan and
        // got no closer — so the cheap search goes first and the whole ceiling is bought only where the cheap
        // one has been shown to fail.
        //
        // <b>One plan, not two, and it was tried at two and measured.</b> A single plan that fails to better
        // the errand's best distance looks like ordinary noise, and raising the bar to two - the same
        // threshold the far side is asked at - did exactly what it was meant to: searches asking for the whole
        // ceiling fell from a third of all of them to under three per cent, and the price of a search from
        // 20.45ms to 8.68. It also cost the population eleven per cent of its work. Jobs finished in minutes
        // five to ten of a run, which is the only number here that is an outcome rather than a cost: 208 and
        // 210 with the bar at one, 187 with it at two. The cheap search was not finding the way and the bots
        // were walking further to get anywhere.
        //
        // So the escalation stays eager, and what pays for it is that it is still bounded by the governor:
        // 181 to 212ms a second of an allowance of five hundred, against 469 before searches were charged by
        // distance at all.
        var outcome = BotPath.Find(
            map,
            bot.Location,
            journey.Target,
            journey.Arrival,
            _path,
            journey.Avoid(bot.Location),
            journey.PlansSinceCloser > 0 ? BotPath.CeilingMs : 0.0
        );

        // A statement about the world, and therefore worth acting on at once. The first version spent
        // twenty-five seconds of a bot's life to reach this same conclusion, and then only sometimes.
        if (outcome == BotPathOutcome.Sealed)
        {
            return Drop(bot, journey, "there is no way from here");
        }

        journey.Planned(outcome, _path, bot.Location);

        // Everything below is the other question, and it is asked from the other end.
        //
        // <b>This is the whole of what a bot standing here can and cannot find out.</b> "How do I get there"
        // has now failed three times running without the destination coming any nearer, and asking it a third
        // time buys nothing: a search begun at the bot has to enumerate the mainland before it may conclude
        // anything, which no clock on any shard will pay for. "Is there anything there to get to" is a
        // different question, it is cheap from the destination's side, and the answer is a fact about the
        // world that every bot on the shard gets to keep.
        if (outcome != BotPathOutcome.Partial || journey.Probed || journey.PlansSinceCloser < PlansBeforeAskingTheFarSide)
        {
            return true;
        }

        var far = BotPath.Enclose(map, journey.Target, journey.Arrival);

        // The population has looked at somebody else's far side too recently. Not an answer, so the errand is
        // not marked as having had one — the next plan asks again.
        if (far == BotEnclosure.Deferred)
        {
            return true;
        }

        journey.Probed = true;

        // No road can end at a tile that will not take a body. Nothing in the walker could see this before:
        // the plan is drawn, the steps are legal, the bot walks them perfectly and simply never arrives.
        if (far == BotEnclosure.NoFooting)
        {
            return Drop(bot, journey, "there is nowhere there to stand");
        }

        // A pocket has been filed and it belongs to everybody now. Whether it refuses *this* journey is the
        // ledger's own question, because the bot may be standing inside the pocket itself.
        if (far == BotEnclosure.Enclosed
            && BotReach.Ask(map, bot.Location, journey.Target, journey.Arrival, tally: false) == BotReachVerdict.Sealed)
        {
            return Drop(bot, journey, "it is shut in and this bot is outside it");
        }

        return true;
    }

    /// <summary>
    /// Ends the errand because the destination is provably no good, and says which proof it was.
    ///
    /// Only this errand. Whatever it was covering is still worth doing — a road proved impossible does not
    /// make the market it led to uninteresting, and clearing the queue would throw away the very thing the
    /// queue exists to keep.
    /// </summary>
    private static bool Drop(Mobile bot, BotJourney journey, string why)
    {
        logger.Information(
            "{Name} has dropped {Where} because {Why} ({Reason})",
            bot.Name,
            journey.Target,
            why,
            journey.Reason
        );

        journey.Complete();
        Dropped++;

        return false;
    }

    /// <summary>Works out why a step was refused, and does whatever that calls for.</summary>
    private static BotWalkResult Refused(Mobile bot, BotJourney journey, Map map, Point3D next, bool run)
    {
        // Mid-spell. The engine will not move a caster, so this refusal says nothing about the ground —
        // and in the first version the walker could not tell a spell from a wall, so it counted the
        // failures, redrew the route twice and abandoned the errand. Mages were being told that their own
        // casting was a dead end.
        if (bot.Spell != null)
        {
            return BotWalkResult.Casting;
        }

        // A door first, and early. Waiting several failures before trying the handle means standing in the
        // street outside a bank looking foolish, and writing the doorway off would teach the population
        // that the inside of every building in the world is unreachable.
        if (OpenDoorTowards(bot, next))
        {
            Doors++;

            return BotWalkResult.OpenedDoor;
        }

        // Something alive. It will move — or can be asked to — so the thing to do is go round it, and never
        // to conclude anything about the ground.
        if (AskAsideAt(bot, map, next))
        {
            // Once is not evidence. Most things in the way are already leaving, and redrawing the route for a
            // skeleton walking past spends a search to avoid a tile that will be empty before the plan ends.
            // Twice on the same tile is somebody who is staying — a bot at an auction, going through a
            // corpse, waiting out a cooldown — and that is worth planning around.
            if (journey.NoteBlocked(next) >= PatienceWithOccupants)
            {
                journey.AvoidTile(next);
                journey.Discard();
                Detours++;
            }

            return BotWalkResult.WentRound;
        }

        return Improvise(bot, journey, next, run);
    }

    /// <summary>
    /// The plan will not walk. Ask the engine what will.
    ///
    /// One step off the intended line to either side, then two. A wider arc than that is not going round an
    /// obstacle, it is going somewhere else — and the plan that gets drawn from wherever this lands will
    /// find the real way round anyway.
    /// </summary>
    private static BotWalkResult Improvise(Mobile bot, BotJourney journey, Point3D next, bool run)
    {
        var nothingTried = next == bot.Location;

        var heading = nothingTried
            ? (int)(bot.GetDirectionTo(journey.Target) & Direction.Mask)
            : (int)(bot.GetDirectionTo(next) & Direction.Mask);

        // Straight at it first, but only when nothing has been tried yet.
        //
        // The offsets exist to go round something the plan already failed against, so retrying the failed
        // direction would be wasted. But when the plan had nowhere to walk at all, nothing has been refused
        // — and skipping the straight direction there means never attempting the most obvious move.
        ReadOnlySpan<int> offsets = [0, 1, -1, 2, -2];

        for (var i = nothingTried ? 0 : 1; i < offsets.Length; i++)
        {
            // The run flag is put back on. Stripping it to do the arithmetic is necessary — the flag is a bit
            // above the three that name a compass point — but stepping without it tells every client watching
            // that the bot is walking, so a running bot moved at a run's pace with a walk's gait. That is the
            // odd skating step, and it is only ever visible from a client, which is why it survived so long.
            var direction = (Direction)((heading + offsets[i] + 8) & 0x7) | (run ? Direction.Running : 0);
            var before = bot.Location;

            bot.Direction = direction;

            if (!bot.Move(direction) || bot.Location == before)
            {
                continue;
            }

            Improvised++;

            // An improvised step is the likeliest move in the whole system to prove the reach ledger wrong:
            // it is the bot going somewhere no plan sent it.
            BotReach.Contradict(bot.Map, before, bot.Location);

            // Off the plan now, so the plan is worthless. Cheaper to draw a new one from where the bot
            // actually is than to walk a route that starts two tiles away.
            journey.Discard();

            return BotWalkResult.Improvised;
        }

        return BotWalkResult.Blocked;
    }

    /// <summary>
    /// Whether somebody alive is on that tile, and if any of them is a bot, asks it to move.
    ///
    /// <para>
    /// <b>The asking happens after the enumeration, never inside it.</b> Stepping aside moves an entity, and
    /// moving an entity mutates the very collection being walked — the engine's enumerator then throws
    /// "collection was modified", which with a population of bots means it throws almost immediately and
    /// repeatedly. The first version documents this as a crash it had. Two lines and a local variable are the
    /// entire difference between a working mechanism and a shard full of exceptions.
    /// </para>
    ///
    /// <para>
    /// One asked per attempt, and one is enough: a tile has one occupant that matters, and if a second is
    /// somehow on it, the next refusal asks that one.
    /// </para>
    /// </summary>
    private static bool AskAsideAt(Mobile bot, Map map, Point3D tile)
    {
        Mobile occupant = null;

        foreach (var mobile in map.GetMobilesAt(tile.X, tile.Y))
        {
            if (mobile == bot || !mobile.Alive || Math.Abs(mobile.Z - tile.Z) >= PersonHeight)
            {
                continue;
            }

            occupant = mobile;

            // A bot can be asked to move, so it is the one worth remembering; anything else merely proves
            // the tile is taken. Keep looking in case something askable is standing here too.
            if (mobile is IBotAside)
            {
                break;
            }
        }

        if (occupant == null)
        {
            return false;
        }

        if (occupant is IBotAside aside)
        {
            aside.StepAsideFor(bot);
        }

        return true;
    }

    /// <summary>
    /// Opens the nearest shut door that is actually between the bot and where it is going.
    ///
    /// Locked doors stay locked: <see cref="BaseDoor.Use"/> enforces that itself, and a bot without the key
    /// learns that this way is genuinely shut — which is the honest lesson, and the one the planner already
    /// agrees with, since it counts a locked door as a wall.
    /// </summary>
    private static bool OpenDoorTowards(Mobile bot, Point3D goal)
    {
        var map = bot.Map;

        if (map == null || map == Map.Internal)
        {
            return false;
        }

        BaseDoor best = null;
        var bestDistance = double.MaxValue;

        foreach (var door in map.GetItemsInRange<BaseDoor>(bot.Location, DoorReach))
        {
            if (door.Deleted || door.Open)
            {
                continue;
            }

            var location = door.GetWorldLocation();

            // Only a door that is in the way: one on the far side of the bot is somebody else's problem.
            // Measured with a tile of slack, because a doorway the bot is level with is still the thing
            // between it and the room beyond.
            if (Utility.GetDistanceToSqrt(location, goal) > bot.GetDistanceToSqrt(goal) + 1.0)
            {
                continue;
            }

            var distance = bot.GetDistanceToSqrt(location);

            if (distance >= bestDistance)
            {
                continue;
            }

            best = door;
            bestDistance = distance;
        }

        if (best == null)
        {
            return false;
        }

        best.Use(bot);

        return best.Open;
    }
}
