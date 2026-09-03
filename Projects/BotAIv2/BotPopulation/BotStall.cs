using System.Collections.Generic;
using Server.Logging;
using Server.Mobiles;

namespace Server.BotAI.V2;

/// <summary>
/// Notices a bot that has stopped getting anywhere, and says so as an error.
///
/// <para>
/// <b>Standing still is this shard's most expensive defect and its quietest one.</b> Every failure this
/// project has hunted all day ended the same way from outside: a bot in a field, doing nothing, for an hour.
/// The Baron whose sweep failed on one unwalkable hilltop; the rangers whose round was thrown away by every
/// skirmish; the company with three bots each believing they led it. Not one of those wrote a single error
/// line — each was a chain of individually reasonable decisions — and every one of them was found by a
/// person looking at the world and asking why nobody was moving.
/// </para>
///
/// <para>
/// <b>So it is watched directly rather than inferred.</b> Two facts per bot: where it stood when last looked
/// at, and what it was doing. A bot that has neither moved nor changed its mind in <see cref="PatienceMs"/>
/// is stuck by any definition worth having, whatever the subsystem underneath believes. It is reported at
/// error level on purpose — this shard's error log is otherwise empty, so a stall is the loudest thing in
/// it, which is exactly what it deserves to be.
/// </para>
///
/// <para>
/// <b>Standing still is not always wrong, and the exceptions are named rather than guessed.</b> A crafter at
/// an anvil, a captain teaching a class, a bot meditating and one mending itself are all doing their work
/// precisely by not moving. What they have in common is that their work is <em>advancing</em> — the stage
/// they report changes — so the test is movement <b>or</b> a change of stage, never movement alone.
/// </para>
/// </summary>
public static class BotStall
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotStall));

    /// <summary>
    /// How long a bot may neither move nor change what it is doing before it is called stuck.
    ///
    /// Four minutes. Long enough that a genuine long errand — a lesson runs ten, a harrowing thirty — is not
    /// slandered for standing at a station, because those change their stage as they go. Short enough that a
    /// person watching does not find it first.
    /// </summary>
    public static int PatienceMs { get; set; } = 240000;

    /// <summary>How often one bot is looked at. Cheap: two comparisons and a dictionary entry.</summary>
    public static int EveryMs { get; set; } = 30000;

    /// <summary>How long after complaining about a bot before it may be complained about again.</summary>
    public static int SayEveryMs { get; set; } = 600000;

    private sealed class Watch
    {
        public Point3D Where;

        public string Doing;

        public long Since;

        public long Looked;

        public long Said;

        public bool Stuck;

        /// <summary>How many times this bot has been reported stalled without having left the spot.</summary>
        public int Stalls;

        public Point3D Stood;
    }

    private static readonly Dictionary<Serial, Watch> _watched = [];

    /// <summary>Bots currently stuck, and how many stalls have been reported all told.</summary>
    public static int Stuck { get; private set; }

    public static long Reported { get; private set; }

    /// <summary>Stalls that were also a piece of work taken off a bot that could not finish it.</summary>
    public static long Freed { get; private set; }

    /// <summary>Bots carried out of a pocket they kept stalling in. See the escalation in Report.</summary>
    public static long Carried { get; private set; }

    /// <summary>The worst one seen: name, what it was doing, and for how long.</summary>
    public static string Worst { get; private set; }

    public static void Look(BotMobile bot)
    {
        if (bot is not { Deleted: false, Alive: true } || bot.Map == null || bot.Map == Map.Internal)
        {
            return;
        }

        var now = Core.TickCount;

        if (_watched.TryGetValue(bot.Serial, out var watch) && now - watch.Looked < EveryMs)
        {
            return;
        }

        // The stage rather than the deed's name: "walking to (1395, 1425)" and "scouting (1425, 1455)" are
        // the same errand and different progress, and progress is the thing being tested for.
        var doing = bot.Resolve?.Deed?.Stage ?? bot.Resolve?.Deed?.Kind ?? "nothing";

        if (watch == null)
        {
            _watched[bot.Serial] = new Watch
            {
                Where = bot.Location,
                Doing = doing,
                Since = now,
                Looked = now
            };

            return;
        }

        watch.Looked = now;

        if (bot.Location != watch.Where || !string.Equals(doing, watch.Doing, System.StringComparison.Ordinal))
        {
            if (watch.Stuck)
            {
                watch.Stuck = false;
                Stuck--;
            }

            watch.Where = bot.Location;
            watch.Doing = doing;
            watch.Since = now;

            return;
        }

        var held = now - watch.Since;

        if (held < PatienceMs)
        {
            return;
        }

        if (!watch.Stuck)
        {
            watch.Stuck = true;
            Stuck++;
        }

        // Said at most once per bot per SayEveryMs. A bot stuck for an hour is one defect, not a hundred and
        // twenty lines of log — and a log that scrolls is a log nobody reads, which is how the thing got
        // missed in the first place.
        if (watch.Said != 0 && now - watch.Said < SayEveryMs)
        {
            return;
        }

        watch.Said = now;
        Reported++;

        // <b>And the work is taken off it, because reporting a stall and leaving it standing is half a
        // repair.</b> What produces these is almost always an errand that cannot finish and will not fail —
        // a walk to somewhere unreachable, asked again every beat for ever. Ended as a failure so the ledger
        // learns the place was no good, which is what stops the same bot taking the same errand to the same
        // spot a second time. A bot with no work in hand is offered some on its very next beat, so this
        // costs it nothing but the errand it was never going to finish.
        if (bot.Resolve?.Deed != null)
        {
            BotWill.Abandon(bot, "it had stopped getting anywhere");
            Freed++;
        }

        // <b>And if taking the work away did not move it, the place is the fault and the bot is carried out
        // of it.</b> BotPopulation.Rescue exists for exactly this and fires on a dozen refused roads with no
        // step in between — which is the trapped bot that stands still. It is not the trapped bot that
        // paces: a step of any kind clears that count, so a bot walking circles inside a pocket it cannot
        // leave never reaches the limit. On 03.09.2026 one pocket at 1755-1757, 970-976 caught Merrick,
        // Torvin, Kerrin, Perri, Edda 2, Bryn, Ilsa, Calla and Doran in turn, each for four minutes, each
        // having its errand taken away and each still there afterwards; the elbow count ruled out their
        // blocking one another, at one or two of ours nearby rather than a knot.
        //
        // Second report from the same spot, so the first is still a stall and only the second is a trap.
        if (watch.Stalls > 0 && Utility.InRange(bot.Location, watch.Stood, Elbow))
        {
            if (BotPopulation.Rescue(bot))
            {
                Carried++;
                watch.Stalls = 0;
            }
        }
        else
        {
            watch.Stalls = 1;
            watch.Stood = bot.Location;
        }
        Worst = $"{bot.Name} the {bot.Class?.Name}, {held / 60000} minutes on \"{doing}\" at {bot.Location}";

        // The load is in the line because it is the first thing worth ruling out: the engine charges stamina
        // for every step over the ceiling and refuses the step outright at nought, so an overloaded bot is
        // stuck in a way no subsystem above it can see or fix. "Full pack" errands stalling three at a time
        // is exactly what that looks like.
        logger.Error(
            "{Name} the {Class} has not moved or changed what it is doing for {Held} minutes: \"{Doing}\" at {Where}, carrying {Load} of {Ceiling} stones with {Stam} stamina, with {Crowd} of ours within {Elbow} tiles",
            bot.Name,
            bot.Class?.Name,
            held / 60000,
            doing,
            bot.Location,
            BotLadder.Load(bot),
            BotLadder.Ceiling(bot),
            bot.Stam,
            Elbows(bot),
            Elbow
        );
    }

    /// <summary>A bot that is gone should not be watched, or the table grows for the life of the shard.</summary>
    public static void Forget(BotMobile bot)
    {
        if (bot != null && _watched.Remove(bot.Serial, out var watch) && watch.Stuck)
        {
            Stuck--;
        }
    }

    public static string Describe() =>
        Reported == 0
            ? $"nobody has stood still for {PatienceMs / 60000} minutes"
            : $"{Stuck} bots are stuck right now, {Reported} stalls reported, {Freed} errands taken off them and {Carried} bots carried out of a spot they stalled in twice; worst: {Worst}";

    public static void Forget()
    {
        _watched.Clear();
        Stuck = 0;
        Reported = 0;
        Freed = 0;
        Carried = 0;
        Worst = null;
    }

    /// <summary>How close another of ours has to be to be in this one's way. Two tiles: a doorway is one.</summary>
    public static int Elbow { get; set; } = 2;

    /// <summary>
    /// How many of ours are standing within <see cref="Elbow"/> of this one.
    ///
    /// <para>
    /// <b>The other thing worth ruling out, and it was not in the line.</b> On 03.09.2026 four bots stalled
    /// together at 1756-1758, 973-974, all on "taking a full pack to the counter", and the load and stamina
    /// printed beside them were 30 of 219 stones at 25 to 45 stamina — nothing wrong with either, so the
    /// message pointed away from the one fact all four had in common. Bots do not walk through one another;
    /// a knot of them in a doorway is a different fault from a bot too heavy to lift its feet, and without
    /// this the two read identically.
    /// </para>
    /// </summary>
    private static int Elbows(BotMobile bot)
    {
        var map = bot?.Map;

        if (map == null || map == Map.Internal)
        {
            return 0;
        }

        var near = 0;

        foreach (var mobile in map.GetMobilesInRange<Mobile>(bot.Location, Elbow))
        {
            if (mobile != bot && mobile is BotMobile { Deleted: false, Alive: true })
            {
                near++;
            }
        }

        return near;
    }

}
