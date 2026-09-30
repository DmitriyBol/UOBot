using System;
using Server.Guilds;
using Server.Logging;

namespace Server.BotAI.V2;

/// <summary>
/// Who should move house, and why: first sight, a guild seated abroad, a poor living, or dying where it lives.
///
/// <para>
/// <b>Once a minute, over the whole population, and nothing here walks or searches.</b> It reads a record, a purse, a
/// share of a trade and a list of deaths per bot — a hundred bots is a few hundred field reads — and at most it marks a
/// bot with a town it may move to. The move itself is work the bot takes when it has nothing better (<see cref="BotMover"/>,
/// <see cref="BotResettle"/>), and the residence changes when the bot takes it, never here: an offer is not an errand
/// (DECISIONS C9).
/// </para>
///
/// <list type="bullet">
/// <item><b>First sight.</b> A bot with no record — every bot on the first boot of this build — is filed where home was
/// until now (its guild's seat, else the population's home) and then chooses among the fit towns it can walk to
/// (<see cref="BotSettle.Choose"/>); a choice other than where it lives is marked as a move. This is the choice Patrick's
/// order is about, made once per bot.</item>
/// <item><b>A guild seated abroad.</b> Its members live by it, at once, as they always did (<c>BotSeat.Home</c>): the
/// guild chose, and the walk home or the next rising takes them there.</item>
/// <item><b>A poor living.</b> A bot judged over <see cref="StretchHours"/> in one town that neither earned
/// <see cref="PoorGold"/> nor learned <see cref="PoorProgress"/> of its trade there.</item>
/// <item><b>Dying where it lives.</b> <see cref="DeathsToMove"/> deaths within <see cref="DeathHours"/> inside its own
/// leash of home.</item>
/// </list>
///
/// <para>
/// <b>A reason to move is a chance, not an order</b> (<see cref="MoveOdds"/>), and never within
/// <see cref="MoveEveryHours"/> of the last settling: a bad hour in one town must not empty it into the next, which is the
/// herding DECISIONS C11 records four times.
/// </para>
/// </summary>
public static class BotRelocate
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotRelocate));

    /// <summary>Why a move was marked.</summary>
    public enum Cause
    {
        Settling,
        Poor,
        Deaths
    }

    public static int LookEveryMs { get; set; } = 60000;

    public static double StretchHours { get; set; } = 2.0;

    public static int PoorGold { get; set; } = 150;

    public static double PoorProgress { get; set; } = 0.01;

    public static int DeathsToMove { get; set; } = 3;

    public static double DeathHours { get; set; } = 2.0;

    public static double MoveEveryHours { get; set; } = 4.0;

    public static double MoveOdds { get; set; } = 0.5;

    public static double PendingMinutes { get; set; } = 60.0;

    public static double NewbornMinutes { get; set; } = 10.0;

    public static long Filed { get; private set; }

    public static long FirstChoices { get; private set; }

    public static long StoodIn { get; private set; }

    public static long Judged { get; private set; }

    public static long Poor { get; private set; }

    public static long Deadly { get; private set; }

    public static long Misplaced { get; private set; }

    public static long Stayed { get; private set; }

    public static long Nowhere { get; private set; }

    public static long Outmatched { get; private set; }

    public static long Marked { get; private set; }

    public static long MarkedSettling { get; private set; }

    public static long MarkedPoor { get; private set; }

    public static long MarkedDeaths { get; private set; }

    public static long Expired { get; private set; }

    public static long Followed { get; private set; }

    public static long MovedIn { get; private set; }

    public static long Late { get; private set; }

    private static long _lookedTick;

    private static bool _said;

    internal static void Tick()
    {
        var map = BotPopulation.Home;

        if (!BotResidence.Active || map == null || map == Map.Internal)
        {
            return;
        }

        if (!BotSettle.Reckoned)
        {
            if (!BotSettle.Reckon(map))
            {
                return;
            }

            MoveIn();
            _lookedTick = Core.TickCount - LookEveryMs;
        }

        var now = Core.TickCount;

        if (now - _lookedTick < LookEveryMs)
        {
            return;
        }

        _lookedTick = now;

        var bots = BotPopulation.Bots;

        for (var i = 0; i < bots.Count; i++)
        {
            if (bots[i] is { Deleted: false } bot && bot.Map == map)
            {
                Judge(bot, now);
            }
        }

        BotResidence.Refresh();
    }

    public static string[] StayHome { get; set; } = ["Baron", "Captain"];

    public static bool Pinned(BotMobile bot)
    {
        var name = bot?.Class?.Name;

        if (name == null || StayHome == null)
        {
            return false;
        }

        for (var i = 0; i < StayHome.Length; i++)
        {
            if (string.Equals(StayHome[i], name, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    public static BotTowns.Town Forced(BotMobile bot)
    {
        if (bot == null || BotUnderworld.Member(bot))
        {
            return null;
        }

        if (Pinned(bot))
        {
            return BotSettle.HomeTown;
        }

        if (bot.Guild is not Guild guild)
        {
            return null;
        }

        if (BotGuildHouses.TownOf(guild) is { } housed && !BotBurgh.Bars(guild, housed))
        {
            return housed;
        }

        var seat = BotSeat.Of(guild);

        if (seat == Point3D.Zero)
        {
            return null;
        }

        var town = BotTowns.Nearest(seat);

        return town == null || ReferenceEquals(town, BotSettle.HomeTown) || BotBurgh.Bars(guild, town) ? null : town;
    }

    private static void Judge(BotMobile bot, long now)
    {
        var rec = BotResidence.Adopt(bot);
        var forced = Forced(bot);

        if (rec == null || BotResidence.TownOf(rec) == null)
        {
            FirstSight(bot, forced, now);

            return;
        }

        var town = BotResidence.TownOf(rec);

        if (forced != null)
        {
            if (!ReferenceEquals(forced, town))
            {
                Follow(bot, rec, town, forced);
            }

            return;
        }

        if (BotUnderworld.Member(bot))
        {
            return;
        }

        if (!rec.Watched || now - rec.SeenTick > 2L * LookEveryMs)
        {
            Restart(bot, rec, now);
            rec.Watched = true;
        }

        rec.SeenTick = now;

        if (rec.Pending != null)
        {
            Pending(bot, rec, now);

            return;
        }

        if (!bot.Alive || bot.Tired)
        {
            return;
        }

        var deathsMs = (long)(DeathHours * 3600000);

        for (var d = rec.Deaths.Count - 1; d >= 0; d--)
        {
            if (now - rec.Deaths[d] > deathsMs)
            {
                rec.Deaths.RemoveAt(d);
            }
        }

        var cause = Cause.Settling;
        string why = null;

        if (bot.Guild is Guild guild && BotBurgh.Bars(guild, town))
        {
            why = $"{guild.Name} was put out of {town.Name}";
        }
        else if (rec.Deaths.Count >= Math.Max(1, DeathsToMove))
        {
            cause = Cause.Deaths;
            why = $"died {rec.Deaths.Count} times in {DeathHours:0.#}h where it lives";
        }
        else if (now - rec.WindowTick >= (long)(StretchHours * 3600000))
        {
            Judged++;

            var gold = BotYield.Standing(bot) - rec.WindowWealth;
            var learned = bot.Progress - rec.WindowProgress;
            var info = BotSettle.Of(town);

            if (info is { Fit: false } && !ReferenceEquals(town, BotSettle.HomeTown))
            {
                cause = Cause.Settling;
                why = $"{town.Name} is not fit to live in ({info.Why})";
            }
            else if (gold < PoorGold && learned < PoorProgress)
            {
                cause = Cause.Poor;
                why = $"earned {gold}gp and {learned:P1} of its trade in {StretchHours:0.#}h there";
            }
            else
            {
                Restart(bot, rec, now);

                return;
            }
        }

        if (why == null)
        {
            return;
        }

        switch (cause)
        {
            case Cause.Deaths:
                {
                    Deadly++;

                    break;
                }
            case Cause.Poor:
                {
                    Poor++;

                    break;
                }
            default:
                {
                    Misplaced++;

                    break;
                }
        }

        if (cause != Cause.Settling
            && ((Core.Now - rec.Since).TotalHours < MoveEveryHours || Utility.RandomDouble() >= MoveOdds))
        {
            Stayed++;
            Restart(bot, rec, now);

            return;
        }

        var from = BotTowns.Nearest(bot.Location);
        var to = BotSettle.Choose(bot, from, true, town, rec.Left, out var outmatched);

        if (outmatched > 0)
        {
            Outmatched++;
        }

        if (to == null || ReferenceEquals(to, town))
        {
            Nowhere++;
            Restart(bot, rec, now);

            return;
        }

        Mark(bot, rec, to, cause, why, now);
    }

    private static void FirstSight(BotMobile bot, BotTowns.Town forced, long now)
    {
        var was = forced ?? BotTowns.Nearest(BotSeat.Home(bot)) ?? BotSettle.HomeTown;

        if (was == null)
        {
            return;
        }

        var rec = BotResidence.File(bot, was);

        Filed++;
        Restart(bot, rec, now);
        rec.SeenTick = now;
        rec.Watched = true;

        if (forced != null || BotUnderworld.Member(bot))
        {
            return;
        }

        var from = BotTowns.Nearest(bot.Location) ?? was;
        var to = BotSettle.Choose(bot, from, true, null, null, out var outmatched);

        FirstChoices++;

        if (outmatched > 0)
        {
            Outmatched++;
        }

        if (to == null || ReferenceEquals(to, was))
        {
            return;
        }

        if (ReferenceEquals(to, from) && to.Bounds.Contains(bot.Location))
        {
            BotResidence.File(bot, to);
            StoodIn++;

            return;
        }

        Mark(bot, rec, to, Cause.Settling, $"settling: chose {to.Name} over {was.Name}", now);
    }

    private static void Follow(BotMobile bot, BotResidence.Record rec, BotTowns.Town town, BotTowns.Town forced)
    {
        var left = town?.Name;

        rec.Pending = null;
        BotResidence.File(bot, forced);
        rec.Left = left;
        rec.Moves++;
        Followed++;
        Restart(bot, rec, Core.TickCount);

        var why = Pinned(bot) ? "its calling is done at home" : $"{(bot.Guild as Guild)?.Name} is seated there";

        logger.Information(
            "{Name} the {Class} lives in {Town} from now on: {Why}; it lived in {Left}",
            bot.Name,
            bot.Class?.Name,
            forced.Name,
            why,
            left ?? "no town"
        );

        BotEvents.Post("residence", bot, $"lives in {forced.Name}: {why}");
    }

    private static void Mark(BotMobile bot, BotResidence.Record rec, BotTowns.Town to, Cause cause, string why, long now)
    {
        rec.Pending = to;
        rec.PendingCause = cause;
        rec.PendingWhy = why;
        rec.PendingTick = now;
        Marked++;

        switch (cause)
        {
            case Cause.Settling:
                {
                    MarkedSettling++;

                    break;
                }
            case Cause.Poor:
                {
                    MarkedPoor++;

                    break;
                }
            default:
                {
                    MarkedDeaths++;

                    break;
                }
        }

        if (!_said)
        {
            _said = true;

            logger.Information("{Name} the {Class} is the first bot marked to move house: to {Town}, {Why}", bot.Name, bot.Class?.Name, to.Name, why);
        }
    }

    private static void Pending(BotMobile bot, BotResidence.Record rec, long now)
    {
        var stale = now - rec.PendingTick > (long)(PendingMinutes * 60000);
        var barred = BotBarred.Holds(bot.Map, BotSettle.Hearth(rec.Pending));

        if (!stale && !barred)
        {
            return;
        }

        rec.Pending = null;
        Expired++;
        Restart(bot, rec, now);
    }

    private static void Restart(BotMobile bot, BotResidence.Record rec, long now)
    {
        rec.WindowTick = now;
        rec.WindowWealth = BotYield.Standing(bot);
        rec.WindowProgress = bot.Progress;
        rec.Deaths.Clear();
    }

    internal static void MoveIn()
    {
        var unhomed = BotResidence.Unhomed;
        var now = Core.TickCount;

        for (var i = 0; i < unhomed.Count; i++)
        {
            var (bot, tick) = unhomed[i];

            if (bot is not { Deleted: false, Alive: true } || bot.Map != BotPopulation.Home || bot.Squad != null
                || now - tick > (long)(NewbornMinutes * 60000))
            {
                Late++;

                continue;
            }

            var rec = BotResidence.Adopt(bot);
            var town = BotResidence.TownOf(rec);

            if (town == null)
            {
                town = Forced(bot) ?? BotSettle.Choose(bot, null, false, null, null, out _) ?? BotSettle.HomeTown;

                if (town == null)
                {
                    continue;
                }

                BotResidence.File(bot, town);
            }

            var home = BotPopulation.HomeOf(bot);

            if (ReferenceEquals(town, BotSettle.HomeTown) || town.Bounds.Contains(bot.Location)
                || Utility.InRange(bot.Location, home, BotHomeward.Away))
            {
                continue;
            }

            var from = bot.Location;

            BotWill.PutDown(bot, $"born to {town.Name}, and moved in");
            bot.Journey?.Discard();

            if (!BotPopulation.PlaceAtHome(bot))
            {
                continue;
            }

            MovedIn++;

            logger.Information(
                "{Name} the {Class}, born at the boot before the towns were read, is moved into {Town}: from {From} to {Where}",
                bot.Name,
                bot.Class?.Name,
                town.Name,
                from,
                bot.Location
            );
        }

        unhomed.Clear();
    }

    public static string Describe() =>
        $"{Filed} filed where they lived and {FirstChoices} asked to choose ({StoodIn} chose the town they stood in); {Judged} living judged over {StretchHours:0.#}h, {Poor} earning poorly (under {PoorGold}gp and {PoorProgress:P0} of the trade), {Deadly} dying where they live ({DeathsToMove} in {DeathHours:0.#}h), {Misplaced} living in a town not fit to live in, {Stayed} stayed (by the draw at {MoveOdds:P0} or within {MoveEveryHours:0.#}h of settling), {Nowhere} had nowhere to go; {Outmatched} choices passed a town over for a road too strong for the bot; {Marked} marked to move ({MarkedSettling} settling, {MarkedPoor} for a poor living, {MarkedDeaths} for deaths), {Expired} let go untaken; {Followed} followed their guild abroad or their calling home; {MovedIn} newborns of the boot moved into their towns, {Late} too late or gone";

    public static void Forget()
    {
        Filed = 0;
        FirstChoices = 0;
        StoodIn = 0;
        Judged = 0;
        Poor = 0;
        Deadly = 0;
        Misplaced = 0;
        Stayed = 0;
        Nowhere = 0;
        Outmatched = 0;
        Marked = 0;
        MarkedSettling = 0;
        MarkedPoor = 0;
        MarkedDeaths = 0;
        Expired = 0;
        Followed = 0;
        MovedIn = 0;
        Late = 0;
        _lookedTick = 0;
        _said = false;
    }
}
