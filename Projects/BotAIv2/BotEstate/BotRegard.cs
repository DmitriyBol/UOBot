using System;
using System.Collections.Generic;
using Server.Guilds;
using Server.Logging;
using Server.Mobiles;

namespace Server.BotAI.V2;

/// <summary>
/// What one guild thinks of another, and the two thresholds that turn an opinion into a war.
///
/// <para>
/// <b>Stage four of Patrick's order of 08.09.2026, and the one he deliberately left unbuilt</b> — his own
/// words: the only part that can make the population smaller. So it is built with the fighting behind a
/// switch that is off, and the opinions running and visible from the first minute. An evening of watching
/// guilds come to dislike each other, with nobody dying of it, is what says whether the numbers are right;
/// the switch is then one dial.
/// </para>
///
/// <para>
/// <b>A number per ordered pair, and it is only ever moved by something that happened.</b> Nothing here
/// decays towards a designed outcome and nothing drifts on a timer except the border, which is itself a
/// fact about the ground. Every mover is listed in <see cref="Describe"/> with its own count, so a war can
/// always be explained by what caused it rather than guessed at.
/// </para>
///
/// <para>
/// <b>Two thresholds and not one.</b> War is declared below <see cref="Enmity"/> and ended above
/// <see cref="Amity"/>, and the gap between them is the whole reason a war does not flicker on and off every
/// minute as one trade lands. That is the same shape the shard's other hysteresis has — see
/// <c>BotUnload</c>'s pair — and it is here for the same reason.
/// </para>
///
/// <para>
/// <b>The engine does the enforcing.</b> <c>Guild.AddEnemy</c> makes the other side
/// <c>Notoriety.Enemy</c>, which makes striking them lawful with no criminal flag and no guard reaction,
/// and makes their corpses lawful to loot. Nothing here touches karma, the criminal flag or the watch, and
/// no looting code is written: <c>BotPickings</c> already loots what a bot killed itself.
/// </para>
/// </summary>
public static class BotRegard
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotRegard));

    public static bool Running { get; set; } = true;

    public static bool Warring { get; set; } = true;

    public static double Enmity { get; set; } = -40.0;

    public static double Amity { get; set; } = -15.0;

    public static double Floor { get; set; } = -200.0;

    public static double Ceiling { get; set; } = 100.0;

    public static double Trespass { get; set; } = -0.5;

    public static double Defiance { get; set; } = -12.0;

    public static double Claim { get; set; } = -50.0;

    public static double HallClaim { get; set; } = -100.0;

    public static long Claims { get; private set; }

    public static double Blood { get; set; } = -25.0;

    public static double Border { get; set; } = -4.0;

    public static int Neighbouring => BotLand.Reach * 2;

    public static int DriftMs { get; set; } = 300000;

    public static double Trade { get; set; } = 0.5;

    public static double Comradeship { get; set; } = 0.5;

    public static int MostAllies { get; set; } = 1;

    public static double Aid { get; set; } = 5.0;

    public static double Alliance { get; set; } = 60.0;

    public static double Estrangement { get; set; } = 20.0;

    public static long Comradeships { get; private set; }

    public static long Aids { get; private set; }

    public static long Allied { get; private set; }

    public static long Estranged { get; private set; }

    public static void Comraded(string one, string other)
    {
        if (one == null || other == null || one == other)
        {
            return;
        }

        Comradeships++;
        Move(one, other, Comradeship, "a corpse shared");
        Move(other, one, Comradeship, "a corpse shared");
    }

    public static void Helped(string helper, string helped)
    {
        if (helper == null || helped == null || helper == helped)
        {
            return;
        }

        Aids++;
        Move(helped, helper, Aid, "help in a fight");
        Move(helper, helped, Aid / 2.0, "help given");
    }

    public static bool AreAllied(string one, string other) =>
        one != null && other != null && one != other
        && BaseGuild.FindByName(one) is Guild a && BaseGuild.FindByName(other) is Guild b && a.IsAlly(b);

    public static long Trespasses { get; private set; }

    public static long Defiances { get; private set; }

    public static long Expected { get; private set; }

    public static long Bloodshed { get; private set; }

    public static long Borders { get; private set; }

    public static long Trades { get; private set; }

    public static long Declared { get; private set; }

    public static long Ended { get; private set; }

    public static long Withheld { get; private set; }

    private static readonly Dictionary<(string Of, string For), double> _regard = [];

    private static readonly List<(string Of, string For, double Held)> _mending = [];

    private static readonly List<(string Of, string For, double Held)> _grieving = [];

    public static long Waiting { get; private set; }

    public static long Overdue { get; private set; }

    private static long _drifted;

    private static bool _everDrifted;

    public static double Of(string of, string about)
    {
        if (of == null || about == null || of == about)
        {
            return 0.0;
        }

        return _regard.TryGetValue((of, about), out var held) ? held : 0.0;
    }

    public static double Of(Guild of, Guild about) => Of(of?.Name, about?.Name);

    public static IEnumerable<KeyValuePair<(string Of, string For), double>> Opinions => _regard;

    public static void Restore(string of, string about, double value)
    {
        if (!string.IsNullOrEmpty(of) && !string.IsNullOrEmpty(about) && of != about)
        {
            _regard[(of, about)] = value;
        }
    }

    public static void Move(string of, string about, double by, string why)
    {
        if (!Running || of == null || about == null || of == about || by == 0.0)
        {
            return;
        }

        var was = Of(of, about);
        var now = Math.Clamp(was + by, Floor, Ceiling);

        if (Math.Abs(now - was) < 0.0001)
        {
            return;
        }

        _regard[(of, about)] = now;

        BotGrievances.Noted(of, about, now - was, why);

        Reckon(of, about, was, now, why);
    }

    public static void Trespassed(string who, string whose)
    {
        if (who == null || whose == null || who == whose)
        {
            return;
        }

        Trespasses++;
        Move(whose, who, Trespass, "working our land");
    }

    public static void Defied(string who, string whose)
    {
        if (who == null || whose == null || who == whose)
        {
            return;
        }

        Defiances++;
        Move(whose, who, Defiance, "refusing to move along");
    }

    public static void Claimed(string who, string whose, bool byTheHall = false)
    {
        if (who == null || whose == null || who == whose)
        {
            return;
        }

        Claims++;

        if (byTheHall)
        {
            Move(whose, who, Math.Min(HallClaim, Enmity - Of(whose, who) - 1.0), "claiming the ground by our hall");

            return;
        }

        Move(whose, who, Claim, "claiming our ground");
    }

    public static void Killed(string killer, string fallen)
    {
        if (killer == null || fallen == null || killer == fallen)
        {
            return;
        }

        Bloodshed++;

        BotWar.Killed(killer, fallen);

        if (BaseGuild.FindByName(fallen) is Guild ours && BaseGuild.FindByName(killer) is Guild theirs &&
            ours.IsWar(theirs))
        {
            Expected++;

            return;
        }

        Move(fallen, killer, Blood, "blood");
    }

    public static void Traded(string buyer, string seller)
    {
        if (buyer == null || seller == null || buyer == seller)
        {
            return;
        }

        Trades++;
        Move(seller, buyer, Trade, "trade");
        Move(buyer, seller, Trade, "trade");
    }

    public static void Drift()
    {
        if (!Running || Border == 0.0)
        {
            return;
        }

        var now = Core.TickCount;

        if (_everDrifted && now - (_drifted + DriftMs) < 0)
        {
            return;
        }

        _drifted = now;
        _everDrifted = true;

        if (Mend > 0.0)
        {
            _mending.Clear();

            foreach (var (pair, held) in _regard)
            {
                if (held != 0.0)
                {
                    _mending.Add((pair.Of, pair.For, held));
                }
            }

            for (var i = 0; i < _mending.Count; i++)
            {
                var (of, about, held) = _mending[i];
                var step = held < 0.0 ? Math.Min(Mend, -held) : -Math.Min(Mend, held);

                Mended++;
                Move(of, about, step, "time");
            }
        }

        if (Warring)
        {
            _grieving.Clear();

            foreach (var (pair, held) in _regard)
            {
                if (held <= Enmity)
                {
                    _grieving.Add((pair.Of, pair.For, held));
                }
            }

            for (var i = 0; i < _grieving.Count; i++)
            {
                var (of, about, held) = _grieving[i];

                if ((BotGuilds.Named(of) ?? BaseGuild.FindByName(of) as Guild) is not Guild mine
                    || (BotGuilds.Named(about) ?? BaseGuild.FindByName(about) as Guild) is not Guild theirs
                    || mine.IsWar(theirs))
                {
                    continue;
                }

                if (!BotWar.MayDeclare(of, about, out _))
                {
                    Waiting++;

                    continue;
                }

                if (BotParley.Grieve(mine, theirs, $"a standing grievance (regard {held:F1})"))
                {
                    continue;
                }

                if (mine.IsAlly(theirs))
                {
                    mine.RemoveAlly(theirs);
                    Estranged++;
                }

                BotWar.Declare(mine, theirs, $"a standing grievance (regard {held:F1})");
                Declared++;
                Overdue++;
            }
        }

        foreach (var (mine, ours) in BotEstate.Held)
        {
            if (ours is not { Deleted: false })
            {
                continue;
            }

            foreach (var (theirs, yours) in BotEstate.Held)
            {
                if (mine == theirs || yours is not { Deleted: false } || yours.Map != ours.Map)
                {
                    continue;
                }

                var gap = Math.Max(Math.Abs(ours.X - yours.X), Math.Abs(ours.Y - yours.Y));

                if (gap >= Neighbouring)
                {
                    continue;
                }

                Borders++;
                Move(mine, theirs, Border * (1.0 - gap / (double)Neighbouring), "a shared border");
            }
        }
    }

    public static bool AtWar(Guild a, Guild b) => a != null && b != null && a != b && a.IsWar(b);

    public static bool AtWar(Mobile a, Mobile b) => AtWar(a?.Guild as Guild, b?.Guild as Guild);

    public static bool Hostile(Mobile a, Mobile b)
    {
        if (a?.Guild is not Guild ga || b?.Guild is not Guild gb || ga == gb)
        {
            return false;
        }

        return AtWar(ga, gb) || Of(ga, gb) <= Enmity || Of(gb, ga) <= Enmity;
    }

    private static void Reckon(string of, string about, double was, double now, string why)
    {
        if (BaseGuild.FindByName(of) is not Guild mine || BaseGuild.FindByName(about) is not Guild theirs)
        {
            return;
        }

        var warring = mine.IsWar(theirs);

        if (!warring)
        {
            if (!mine.IsAlly(theirs) && now >= Alliance && Of(about, of) >= Alliance
                && (mine.Allies?.Count ?? 0) < MostAllies && (theirs.Allies?.Count ?? 0) < MostAllies)
            {
                if (BotParley.Befriend(mine, theirs, $"{why} (regard {now:F1} and {Of(about, of):F1})"))
                {
                    return;
                }

                mine.AddAlly(theirs);
                Allied++;

                logger.Warning("{Mine} and {Theirs} are allies: regard {Now:F1} and {Back:F1} over {Why}", of, about, now, Of(about, of), why);
            }
            else if (mine.IsAlly(theirs) && now < Estrangement)
            {
                mine.RemoveAlly(theirs);
                Estranged++;

                logger.Warning("{Mine} is no longer allied with {Theirs}: regard fell to {Now:F1} over {Why}", of, about, now, why);
            }
        }

        if (!warring && now <= Enmity && was > Enmity)
        {
            if (!Warring)
            {
                Withheld++;

                return;
            }

            if (mine.IsAlly(theirs))
            {
                mine.RemoveAlly(theirs);
                Estranged++;
            }

            if (!BotWar.MayDeclare(of, about, out var refused))
            {
                Refused++;

                logger.Information(
                    "{Mine} would declare war on {Theirs} (regard {Now:F1} over {Why}) but for {Refused}",
                    of,
                    about,
                    now,
                    why,
                    refused
                );

                return;
            }

            if (BotParley.Grieve(mine, theirs, $"{why} (regard {now:F1})"))
            {
                return;
            }

            BotWar.Declare(mine, theirs, $"{why} (regard {now:F1})");
            Declared++;

            return;
        }

        if (warring && now >= Amity && Of(about, of) >= Amity && BotWar.MayPeace(of, about))
        {
            Ended++;

            logger.Information("{Mine} is at peace with {Theirs} again: regard back up to {Now:F1}", of, about, now);

            BotWar.Peace(of, about);
        }
    }

    public static long Refused { get; private set; }

    public static long Settled { get; private set; }

    public static long Mended { get; private set; }

    public static double Mend { get; set; } = 1.0;

    public static void Settle(string one, string other)
    {
        if (one == null || other == null || one == other)
        {
            return;
        }

        _regard.Remove((one, other));
        _regard.Remove((other, one));
        Settled++;

        BotGrievances.Settled(one, other);
    }

    private static string Worst()
    {
        string worst = null;
        var lowest = 0.0;

        foreach (var ((of, about), held) in _regard)
        {
            if (held < lowest)
            {
                lowest = held;
                worst = $"{of} of {about} at {held:F0}";
            }
        }

        return worst ?? "nobody thinks ill of anybody";
    }

    public static string Describe() =>
        !Running
            ? "guilds form no opinions of each other"
            : $"{_regard.Count} opinions between guilds, worst {Worst()}; moved by {Trespasses} pieces of work on somebody else's land, "
              + $"{Defiances} refusals to move along, {Bloodshed} killings ({Expected} more between guilds already at war, which move nothing), {Borders} border drifts, {Claims} claims on somebody's ground, {Trades} trades, {Comradeships} corpses shared between guilds, {Aids} rescues across guilds and {Mended} steps of forgetting at {Mend:F1} a drift; {Allied} alliances made at {Alliance:F0} and {Estranged} ended below {Estrangement:F0}; "
              + $"{Declared} wars declared, {Refused} called for and refused by the war's rules, {Waiting} drifts a standing grievance waited on them and {Overdue} wars it got when they allowed, {Ended} ended in peace, {Settled} pairs settled to nought by a war ending"
              + (Warring ? "" : $", and {Withheld} withheld because war is switched off");

    public static void Forget()
    {
        _regard.Clear();
        _drifted = 0;
        _everDrifted = false;
        Trespasses = 0;
        Defiances = 0;
        Bloodshed = 0;
        Expected = 0;
        Claims = 0;
        Borders = 0;
        Trades = 0;
        Declared = 0;
        Ended = 0;
        Withheld = 0;
        Refused = 0;
        Waiting = 0;
        Overdue = 0;
        Settled = 0;
        Mended = 0;
        Comradeships = 0;
        Aids = 0;
        Allied = 0;
        Estranged = 0;
        _mending.Clear();
    }
}
