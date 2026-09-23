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
    bool StepAsideFor(Mobile asker);
}

/// <summary>What one attempt at moving did.</summary>
public enum BotWalkResult
{
    Idle,

    Arrived,

    Stepped,

    OpenedDoor,

    WentRound,

    Improvised,

    Casting,

    Blocked,

    Refused,

    GaveUp,

    Stalled
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

    public const int WalkStepMs = 400;

    public const int RunStepMs = 200;

    public const int WalkMountStepMs = 200;

    public const int RunMountStepMs = 100;

    private const int DoorReach = 3;

    private const int PatienceWithOccupants = 2;

    private const int PersonHeight = BotArrival.PersonHeight;

    private static readonly List<Point3D> _path = [];

    public static bool Walking { get; internal set; }

    public static long Steps { get; private set; }

    public static long Refusals { get; private set; }

    public static long Doors { get; private set; }

    public static long Detours { get; private set; }

    public static long Improvised { get; private set; }

    public static long GaveUp { get; private set; }

    public static long Dropped { get; private set; }

    public static double SpentMs { get; private set; }

    public static void Spent(long ticks) => SpentMs += ticks * 1000.0 / System.Diagnostics.Stopwatch.Frequency;

    public static long Escalated { get; private set; }

    public static long EscalationFound { get; private set; }

    public static long EscalationFailed { get; private set; }

    private static readonly Dictionary<string, long> _burned = [];

    private static readonly Dictionary<string, long> _lost = [];

    private static void Count(Dictionary<string, long> book, string reason)
    {
        reason ??= "?";
        book[reason] = book.GetValueOrDefault(reason) + 1;
    }

    private static string Top(Dictionary<string, long> book)
    {
        if (book.Count == 0)
        {
            return "nobody";
        }

        var rows = new List<KeyValuePair<string, long>>(book);

        rows.Sort(static (a, b) => b.Value.CompareTo(a.Value));

        using var say = Server.Text.ValueStringBuilder.Create(128);

        for (var i = 0; i < rows.Count && i < 5; i++)
        {
            if (i > 0)
            {
                say.Append(", ");
            }

            say.Append($"{rows[i].Key} {rows[i].Value}");
        }

        return say.ToString();
    }

    public static void Reset()
    {
        Steps = 0;
        Refusals = 0;
        Doors = 0;
        Detours = 0;
        Improvised = 0;
        GaveUp = 0;
        Dropped = 0;
        Boxed = 0;
        Knots = 0;
        Escalated = 0;
        Stationed = 0;
        EscalationFound = 0;
        EscalationFailed = 0;
        _burned.Clear();
        _lost.Clear();
    }

    public static string Describe() =>
        $"{Steps} steps taken, {Refusals} refused by the engine, {Doors} doors opened, {Detours} tiles gone round, {Improvised} improvised, {GaveUp} journeys given up, {Dropped} destinations dropped as no good, {Boxed} steps where the engine refused all eight directions, {Knots} stepped aside from somebody who would not; the whole ceiling was burned by: {Top(_burned)} ({Stationed} station searches held to {StationCeilingMs:F0}ms instead); {Escalated} searches at the stranded ceiling for a way round, {EscalationFound} found one and {EscalationFailed} ended the errand instead of nine more searches; errands lost as hopeless or without a way round, by kind: {Top(_lost)}";

    public static int StepDelayMs(bool run) => run ? RunStepMs : WalkStepMs;

    public static int StepDelayMs(Mobile bot, bool run) =>
        bot?.Mounted == true
            ? run ? RunMountStepMs : WalkMountStepMs
            : run ? RunStepMs : WalkStepMs;

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

        if (journey.Prune() > 0 && !journey.Active)
        {
            return BotWalkResult.Idle;
        }

        journey.Catch(bot.Location);

        if (journey.Arrived(bot.Location))
        {
            return BotWalkResult.Arrived;
        }

        if (journey.Stalled())
        {
            var side = journey.Current?.Interruption == true;

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

            return side ? BotWalkResult.Blocked : BotWalkResult.Stalled;
        }

        if (bot.Spell != null)
        {
            return BotWalkResult.Casting;
        }

        journey.Attempted();

        var planning = journey.Current?.Interruption == true;

        if (journey.NeedsPlan(bot.Location) && !Plan(bot, journey, map))
        {
            return planning ? BotWalkResult.Blocked : BotWalkResult.Refused;
        }

        if (journey.Hopeless)
        {
            var side = journey.Current?.Interruption == true;

            Count(_lost, journey.Reason);

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
            return Improvise(bot, journey, next: bot.Location, run);
        }

        var before = bot.Location;
        var direction = bot.GetDirectionTo(next, run);

        bot.Direction = direction;

        if (bot.Move(direction) && bot.Location != before)
        {
            Steps++;

            journey.Stepped(bot.Location);

            BotReach.Contradict(map, before, bot.Location);

            return journey.Arrived(bot.Location) ? BotWalkResult.Arrived : BotWalkResult.Stepped;
        }

        Refusals++;

        return Refused(bot, journey, map, next, run);
    }

    private static BotWalkResult Ended(bool interruption) =>
        interruption ? BotWalkResult.Blocked : BotWalkResult.GaveUp;

    public static int PlansBeforeAskingTheFarSide { get; set; } = 2;

    public static double StationCeilingMs { get; set; } = 15.0;

    public static long Stationed { get; private set; }

    private static bool Plan(Mobile bot, BotJourney journey, Map map)
    {
        var escalate = journey.Probed && !journey.Escalated && journey.PlansSinceCloser >= PlansBeforeAskingTheFarSide;

        var ceiling = escalate ? BotPath.StrandedCeilingMs : journey.PlansSinceCloser > 0 ? BotPath.CeilingMs : 0.0;

        if (ceiling > 0.0 && journey.Reason is "station" or "sweep")
        {
            ceiling = StationCeilingMs;
            Stationed++;
        }
        else if (ceiling > 0.0)
        {
            Count(_burned, journey.Reason);
        }

        var outcome = BotPath.Find(
            map,
            bot.Location,
            journey.Target,
            journey.Arrival,
            _path,
            BotOutlaw.Road(bot, map, journey.Target, journey.Avoid(bot.Location)),
            ceiling
        );

        if (outcome == BotPathOutcome.Sealed)
        {
            return Drop(bot, journey, "there is no way from here");
        }

        journey.Planned(outcome, _path, bot.Location);

        if (escalate && !BotPath.LastStarved)
        {
            journey.Escalated = true;
            Escalated++;

            if (outcome == BotPathOutcome.Partial && journey.PlansSinceCloser > 0)
            {
                EscalationFailed++;
                Count(_lost, journey.Reason);

                return Drop(bot, journey, $"no way round it was found in {BotPath.StrandedCeilingMs:F0}ms");
            }

            EscalationFound++;

            return true;
        }

        if (outcome != BotPathOutcome.Partial || journey.Probed || journey.PlansSinceCloser < PlansBeforeAskingTheFarSide)
        {
            return true;
        }

        var far = BotPath.Enclose(map, journey.Target, journey.Arrival);

        if (far == BotEnclosure.Deferred)
        {
            return true;
        }

        journey.Probed = true;

        if (far == BotEnclosure.NoFooting)
        {
            return Drop(bot, journey, "there is nowhere there to stand");
        }

        if (far == BotEnclosure.Enclosed
            && BotReach.Ask(map, bot.Location, journey.Target, journey.Arrival, tally: false) == BotReachVerdict.Sealed)
        {
            return Drop(bot, journey, "it is shut in and this bot is outside it");
        }

        return true;
    }

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

    private static BotWalkResult Refused(Mobile bot, BotJourney journey, Map map, Point3D next, bool run)
    {
        if (bot.Spell != null)
        {
            return BotWalkResult.Casting;
        }

        if (OpenDoorTowards(bot, next))
        {
            Doors++;

            return BotWalkResult.OpenedDoor;
        }

        if (AskAsideAt(bot, map, next, out var freed))
        {
            if (journey.NoteBlocked(next) >= PatienceWithOccupants)
            {
                journey.AvoidTile(next);
                journey.Discard();
                Detours++;

                if (!freed)
                {
                    Knots++;

                    return Improvise(bot, journey, next, run);
                }
            }

            return BotWalkResult.WentRound;
        }

        return Improvise(bot, journey, next, run);
    }

    private static BotWalkResult Improvise(Mobile bot, BotJourney journey, Point3D next, bool run)
    {
        var nothingTried = next == bot.Location;

        var heading = nothingTried
            ? (int)(bot.GetDirectionTo(journey.Target) & Direction.Mask)
            : (int)(bot.GetDirectionTo(next) & Direction.Mask);

        ReadOnlySpan<int> offsets = [0, 1, -1, 2, -2, 3, -3, 4];

        for (var i = nothingTried ? 0 : 1; i < offsets.Length; i++)
        {
            var direction = (Direction)((heading + offsets[i] + 8) & 0x7) | (run ? Direction.Running : 0);
            var before = bot.Location;

            bot.Direction = direction;

            if (!bot.Move(direction) || bot.Location == before)
            {
                continue;
            }

            Improvised++;

            BotReach.Contradict(bot.Map, before, bot.Location);

            journey.Discard();

            return BotWalkResult.Improvised;
        }

        Boxed++;

        return BotWalkResult.Blocked;
    }

    public static long Boxed { get; private set; }

    public static long Knots { get; private set; }

    private static bool AskAsideAt(Mobile bot, Map map, Point3D tile, out bool freed)
    {
        Mobile occupant = null;

        freed = false;

        foreach (var mobile in map.GetMobilesAt(tile.X, tile.Y))
        {
            if (mobile == bot || !mobile.Alive || Math.Abs(mobile.Z - tile.Z) >= PersonHeight)
            {
                continue;
            }

            occupant = mobile;

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
            freed = aside.StepAsideFor(bot);
        }

        return true;
    }

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
