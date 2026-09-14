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

    public static int PatienceMs { get; set; } = 240000;

    public static int EveryMs { get; set; } = 30000;

    public static int SayEveryMs { get; set; } = 600000;

    private sealed class Watch
    {
        public Point3D Where;

        public string Doing;

        public long Since;

        public long Looked;

        public long Said;

        public bool Stuck;

        public Point3D Anchor;

        public long AnchorSince;

        public int Swaps;

        public long Churned;

        public object Deed;
    }

    private static readonly Dictionary<Serial, Watch> _watched = [];

    public static int Stuck { get; private set; }

    public static long Reported { get; private set; }

    public static long Freed { get; private set; }

    public static long Carried { get; private set; }

    public static long Steady { get; private set; }

    public static int ChurnMs { get; set; } = 480000;

    public static int ChurnAt { get; set; } = 6;

    public static long Churn { get; private set; }

    public static int PocketMs { get; set; } = 1800000;

    private static readonly List<(Point3D Where, long Until)> _pockets = [];

    private static bool Pocket(Point3D where)
    {
        var now = Core.TickCount;

        for (var i = _pockets.Count - 1; i >= 0; i--)
        {
            var (at, until) = _pockets[i];

            if (now - until >= 0)
            {
                _pockets.RemoveAt(i);

                continue;
            }

            if (Utility.InRange(where, at, Elbow))
            {
                _pockets[i] = (at, now + PocketMs);

                return true;
            }
        }

        _pockets.Add((where, now + PocketMs));

        return false;
    }

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

        var deed = bot.Resolve?.Deed;

        var parked = bot.Squad != null && deed is not (null or { Alongside: true });

        var doing = parked
            ? $"in a company; its own {deed.Kind} is set aside"
            : deed?.Stage ?? deed?.Kind ?? "nothing";

        if (watch == null)
        {
            _watched[bot.Serial] = new Watch
            {
                Where = bot.Location,
                Doing = doing,
                Deed = deed,
                Since = now,
                Looked = now
            };

            return;
        }

        watch.Looked = now;

        var swapped = !ReferenceEquals(deed, watch.Deed);

        watch.Deed = deed;

        if (deed is { Still: true })
        {
            Steady++;

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

        if (bot.Location != watch.Anchor)
        {
            watch.Anchor = bot.Location;
            watch.AnchorSince = now;
            watch.Swaps = 0;
        }
        else
        {
            if (swapped)
            {
                watch.Swaps++;
            }

            if (watch.Swaps >= ChurnAt
                && now - watch.AnchorSince >= ChurnMs
                && (watch.Churned == 0 || now - watch.Churned >= SayEveryMs))
            {
                watch.Churned = now;
                Churn++;

                logger.Error(
                    "{Name} the {Class} has not left ({X}, {Y}) for {Minutes} minutes while taking and dropping {Swaps} errands, carrying {Load} of {Ceiling} stones with {Stamina} stamina: it is not idle, it cannot move",
                    bot.Name,
                    bot.Class?.Name ?? "bot",
                    bot.Location.X,
                    bot.Location.Y,
                    (now - watch.AnchorSince) / 60000,
                    watch.Swaps,
                    BotLadder.Load(bot),
                    BotLadder.Ceiling(bot),
                    bot.Stam
                );

                if (bot.Resolve?.Deed != null)
                {
                    BotWill.Abandon(bot, "it was swapping errands without moving");
                    Freed++;
                }

                watch.Swaps = 0;
                watch.AnchorSince = now;
            }
        }

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

        if (watch.Said != 0 && now - watch.Said < SayEveryMs)
        {
            return;
        }

        watch.Said = now;
        Reported++;

        if (bot.Resolve?.Deed != null)
        {
            BotWill.Abandon(bot, "it had stopped getting anywhere");
            Freed++;
        }

        var stalledAt = bot.Location;

        var crowd = Elbows(bot);

        var company = bot is IBotSquadMember { Squad: not null };
        var barren = bot.Resolve?.Urges?.BarrenMinutes(now) ?? 0.0;

        if (Pocket(stalledAt))
        {
            if (BotPopulation.Rescue(bot))
            {
                Carried++;
            }
        }

        Worst = $"{bot.Name} the {bot.Class?.Name}, {held / 60000} minutes on \"{doing}\" at {stalledAt}";

        logger.Error(
            "{Name} the {Class} has not moved or changed what it is doing for {Held} minutes: \"{Doing}\" at {Where}, carrying {Load} of {Ceiling} stones with {Stam} stamina, with {Crowd} of ours within {Elbow} tiles, {Company} and out of work for {Barren:F1} minutes by its own clock",
            bot.Name,
            bot.Class?.Name,
            held / 60000,
            doing,
            stalledAt,
            BotLadder.Load(bot),
            BotLadder.Ceiling(bot),
            bot.Stam,
            crowd,
            Elbow,
            company ? "in a company" : "on its own",
            barren
        );
    }

    public static void Forget(BotMobile bot)
    {
        if (bot != null && _watched.Remove(bot.Serial, out var watch) && watch.Stuck)
        {
            Stuck--;
        }
    }

    public static string Describe() =>
        Reported == 0 && Churn == 0
            ? $"nobody has stood still for {PatienceMs / 60000} minutes ({Steady} looks passed over as work that stands still on purpose)"
            : $"{Stuck} bots are stuck right now, {Reported} stalls reported, {Freed} errands taken off them and {Carried} bots carried out of {_pockets.Count} known pockets, {Churn} caught swapping errands without moving a tile, {Steady} looks passed over as work that stands still on purpose, {BotPopulation.Rescued} carried home for reaching nothing at all ({BotPopulation.Delving} more were left alone for being down a dungeon and {BotPopulation.Unbound} let go of their company instead) and {BotPopulation.Boxedin} of those put down with no way off the tile; worst: {Worst}";

    public static void Forget()
    {
        _watched.Clear();
        Stuck = 0;
        Reported = 0;
        Freed = 0;
        Carried = 0;
        Steady = 0;
        Worst = null;
    }

    public static int Elbow { get; set; } = 2;

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
