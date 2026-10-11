using System;
using System.Collections.Generic;
using Server.Guilds;
using Server.Logging;

namespace Server.BotAI.V2;

/// <summary>
/// A claim on a square another guild holds: war at once, without a meeting, the price of it in every guild's and every town's
/// regard for the claimant, and the weighing a guild does before it pays that price.
///
/// <para>
/// <b>Patrick's order of 06.10.2026, 00:10:</b> "if another guild tries to remove a claim or seizes a square, war is declared
/// without negotiations … but for such a thing every guild's and every town's rating towards that guild falls; they must
/// understand that they are taking a risky step with a great price". Until build 402 a claim on held ground only moved the
/// holder's opinion (<c>BotRegard.Claimed</c>), and since 29.09.2026 an opinion reaches a war only through a meeting — whose
/// duke, on 04.10, answered every grievance with trade terms. At 00:35 on 06.10 The Hammer thought −198 of The Wolf for
/// "claiming the ground by our hall ×8, claiming our ground ×2", and the two stood under trade terms: ten claims on one
/// victim's ground and no war.
/// </para>
///
/// <para>
/// <b>Both kinds of claim count, and only on ground somebody holds.</b> Taking a square (<c>Oust</c>) and striking a name off
/// it (<c>Strip</c>) are the order's "seizes" and "removes a claim". Ground nobody holds is untouched by all of this, including
/// ground by another guild's hall that it never claimed: that is a border (<c>BotRegard.Border</c>), not a seizure. The
/// grievance "claiming the ground by our hall" is written only for a square the victim <em>holds</em> next to its hall
/// (<c>BotClaim.Important</c>), so both of the victim's grievance kinds are seizures.
/// </para>
///
/// <para>
/// <b>The price compounds and is felt for days.</b> The n-th seizure inside <see cref="MemoryMs"/> costs n times
/// <see cref="Regard"/> of every other guild's opinion and n times <see cref="TownStep"/> on the claimant's prices in every
/// town; while a seizure is remembered, time's forgetting (<c>BotRegard.Mend</c>) lifts no opinion of the claimant above
/// −<see cref="Cold"/> times the number remembered; the towns' prices come back down only by tasks done for them
/// (<c>BotBurgh</c>, half a percent each). The fall stops one point above the war line for any guild that was above it, so one
/// seizure never sets the island on the claimant on the same beat — but every neighbour is that much nearer its own grievance.
/// </para>
/// </summary>
public static class BotSeizure
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotSeizure));

    public static bool Running { get; set; } = true;

    public static double Regard { get; set; } = 30.0;

    public static double TownStep { get; set; } = 0.02;

    public static int MemoryMs { get; set; } = 172800000;

    public static double Cold { get; set; } = 20.0;

    public static int MostRemembered { get; set; } = 2;

    public static double Odds { get; set; } = 1.0;

    public static int WeighMs { get; set; } = 60000;

    public const string Grievance = "seizing another guild's ground";

    /// <summary>Why a weighing said no.</summary>
    public enum Cause
    {
        None,
        Ally,
        Busy,
        TheirsBusy,
        Infamy,
        Weaker,

        Tribute
    }

    public static long Seized { get; private set; }

    public static long WarsBegun { get; private set; }

    public static long WarsWithheld { get; private set; }

    public static long InWar { get; private set; }

    public static long PactsBroken { get; private set; }

    public static long AlliancesBroken { get; private set; }

    public static long TrucesBroken { get; private set; }

    public static long Overtaken { get; private set; }

    public static double RegardTaken { get; private set; }

    public static long Cooled { get; private set; }

    public static long TownsRaised { get; private set; }

    public static double TownsAdded { get; private set; }

    public static long Weighed { get; private set; }

    private static readonly long[] _refused = new long[7];

    public static long Withheld { get; private set; }

    private static readonly Dictionary<string, List<long>> _memory = [];

    private static readonly Dictionary<(string Ours, string Holder), (bool Go, long Tick, Cause Cause)> _weighed = [];

    private static readonly List<string> _others = [];

    public static int Remembered(string guild)
    {
        if (guild == null || !_memory.TryGetValue(guild, out var ticks))
        {
            return 0;
        }

        var now = Core.TickCount;

        for (var i = ticks.Count - 1; i >= 0; i--)
        {
            if (now - ticks[i] >= MemoryMs)
            {
                ticks.RemoveAt(i);
            }
        }

        if (ticks.Count == 0)
        {
            _memory.Remove(guild);
        }

        return ticks.Count;
    }

    public static bool Ceiling(string about, out double ceiling)
    {
        ceiling = 0.0;

        if (!Running || Cold <= 0.0 || _memory.Count == 0)
        {
            return false;
        }

        var n = Remembered(about);

        if (n == 0)
        {
            return false;
        }

        ceiling = -Cold * n;

        return true;
    }

    public static void Kept() => Withheld++;

    public static bool Weigh(Guild ours, string holder)
    {
        if (!Running || ours == null || holder == null || holder == ours.Name)
        {
            return true;
        }

        var key = (ours.Name, holder);
        var now = Core.TickCount;

        var had = _weighed.TryGetValue(key, out var was);

        if (had && now - was.Tick < WeighMs)
        {
            return was.Go;
        }

        var go = Judge(ours, holder, out var cause, out var said);

        _weighed[key] = (go, now, cause);
        Weighed++;

        if (!go)
        {
            _refused[(int)cause]++;
        }

        if (had ? was.Cause != cause : !go)
        {
            if (go)
            {
                logger.Information("{Ours} would now lay claim to {Holder}'s ground and pay for it: {Said}", ours.Name, holder, said);
            }
            else
            {
                logger.Information("{Ours} will not lay claim to {Holder}'s ground: {Said}", ours.Name, holder, said);
            }
        }

        return go;
    }

    private static bool Judge(Guild ours, string holder, out Cause cause, out string said)
    {
        cause = Cause.None;
        said = "nothing stands against it";

        if ((BotGuilds.Named(holder) ?? BaseGuild.FindByName(holder) as Guild) is not Guild theirs)
        {
            return true;
        }

        if (BotWar.Of(ours.Name, holder) != null || ours.IsWar(theirs))
        {
            said = "they are at war already";

            return true;
        }

        if (ours.IsAlly(theirs))
        {
            cause = Cause.Ally;
            said = "they are allies";

            return false;
        }

        if (BotWar.Fighting(ours.Name) > 0)
        {
            cause = Cause.Busy;
            said = "it is fighting a war already and could not carry a second";

            return false;
        }

        if (BotWar.Fighting(holder) >= Math.Max(1, BotWar.MostWars))
        {
            cause = Cause.TheirsBusy;
            said = $"{holder} stands in as many wars as it may";

            return false;
        }

        var remembered = Remembered(ours.Name);

        if (MostRemembered > 0 && remembered >= MostRemembered)
        {
            cause = Cause.Infamy;
            said = $"it has seized {remembered} times in {MemoryMs / 3600000} hours, and the next would cost {Regard * (remembered + 1):F0} of every guild's regard and {TownStep * (remembered + 1):P0} in every town";

            return false;
        }

        var mine = Side(ours, theirs);
        var against = Side(theirs, ours);

        if (mine <= 0.0 || against > mine * Odds)
        {
            cause = Cause.Weaker;
            said = $"{holder}'s side is {(mine > 0.0 ? against / mine : 0.0):F2} times its might ({against:F0} against {mine:F0}), and the war starts the moment it declares";

            return false;
        }

        if (BotTribute.Demand(ours, theirs, "its ground", out var terms))
        {
            cause = Cause.Tribute;
            said = terms;

            return false;
        }

        said = $"its side's might {mine:F0} against {against:F0}, {remembered} seizures remembered, the price {Regard * (remembered + 1):F0} of every guild's regard and {TownStep * (remembered + 1):P0} in every town";

        return true;
    }

    private static double Side(Guild guild, Guild enemy)
    {
        var total = BotWar.Might(guild.Name);
        var allies = guild.Allies;

        for (var i = 0; allies != null && i < allies.Count; i++)
        {
            if (allies[i] is Guild ally && ally != enemy && !ally.IsWar(enemy) && !ally.IsAlly(enemy))
            {
                total += BotWar.Might(ally.Name);
            }
        }

        return total;
    }

    public static void Seize(Guild ours, string holder, Point3D middle, BotClaim.Kind want, int paid)
    {
        if (!Running || ours == null || holder == null || holder == ours.Name)
        {
            return;
        }

        if ((BotGuilds.Named(holder) ?? BaseGuild.FindByName(holder) as Guild) is not Guild theirs)
        {
            return;
        }

        Seized++;

        var deed = want == BotClaim.Kind.Strip ? "to strike its name off it" : "to take it";

        if (BotWar.Of(ours.Name, holder) != null || ours.IsWar(theirs))
        {
            InWar++;

            logger.Warning(
                "{Ours} lays claim to {Holder}'s square at {X},{Y} ({Deed}, {Paid}gp) inside the war between them: no new war and no new price",
                ours.Name,
                holder,
                middle.X,
                middle.Y,
                deed,
                paid
            );

            return;
        }

        var n = Remembered(ours.Name) + 1;
        string broken = null;

        if (BotParley.Overtake(ours.Name, holder, $"{ours.Name} laid claim to {holder}'s square at {middle.X},{middle.Y}"))
        {
            Overtaken++;
            broken = "the meeting between them";
        }

        if (ours.IsAlly(theirs))
        {
            ours.RemoveAlly(theirs);
            AlliancesBroken++;
            broken = broken == null ? "their alliance" : $"{broken}, their alliance";
        }

        if (BotPact.Break(ours.Name, holder) is { } pacts)
        {
            PactsBroken++;
            broken = broken == null ? pacts : $"{broken}, {pacts}";
        }

        if (BotWar.Untruce(ours.Name, holder))
        {
            TrucesBroken++;
            broken = broken == null ? "a truce" : $"{broken}, a truce";
        }

        if (BotRegard.Warring)
        {
            BotWar.Declare(ours, theirs, $"seizing: a claim on its square at {middle.X},{middle.Y} {deed}");
            WarsBegun++;
        }
        else
        {
            WarsWithheld++;
        }

        var fall = Regard * n;
        var cooled = 0;
        var taken = 0.0;

        _others.Clear();

        foreach (var guild in BotGuilds.Standing)
        {
            if (guild != null && !guild.Disbanded && guild.Name != ours.Name && guild.Name != holder && guild.Name != BotUnderworld.GuildName)
            {
                _others.Add(guild.Name);
            }
        }

        for (var i = 0; i < _others.Count; i++)
        {
            var other = _others[i];
            var before = BotRegard.Of(other, ours.Name);
            var to = before - fall;

            if (before > BotRegard.Enmity)
            {
                to = Math.Max(to, BotRegard.Enmity + 1.0);
            }

            to = Math.Max(to, BotRegard.Floor);

            if (to >= before)
            {
                continue;
            }

            BotRegard.Move(other, ours.Name, to - before, Grievance);

            var moved = before - BotRegard.Of(other, ours.Name);

            if (moved > 0.0)
            {
                cooled++;
                taken += moved;
            }
        }

        Cooled += cooled;
        RegardTaken += taken;

        var add = TownStep * n;
        var towns = BotBurgh.Shame(ours.Name, add, out var dearest, out var added);

        TownsRaised += towns;
        TownsAdded += added;

        if (!_memory.TryGetValue(ours.Name, out var ticks))
        {
            _memory[ours.Name] = ticks = [];
        }

        ticks.Add(Core.TickCount);
        _weighed.Clear();

        logger.Warning(
            "{Ours} laid claim to {Holder}'s square at {X},{Y} ({Deed}, {Paid}gp): {War}{Broken}; the price, as its seizure {N} in {Hours} hours: {Cooled} guilds think {Taken:F0} worse of it in all ({Fall:F0} each, never past the war line on this beat) and {Towns} towns charge it {Add:P1} more (dearest now ×{Dearest:F3}); time will mend no opinion of it above {Ceiling:F0} while this is remembered",
            ours.Name,
            holder,
            middle.X,
            middle.Y,
            deed,
            paid,
            BotRegard.Warring ? "war at once, without a meeting" : "no war, for war is switched off",
            broken == null ? "" : $", breaking {broken}",
            n,
            MemoryMs / 3600000,
            cooled,
            taken,
            fall,
            towns,
            add,
            dearest,
            -Cold * n
        );

        BotEvents.Post("parley", ours.Name, null, ours.Name, middle.X, middle.Y, $"{ours.Name} laid claim to {holder}'s ground at {middle.X},{middle.Y}: war at once, {taken:F0} of regard and {add:P1} in {towns} towns", "seizure", null);
    }

    public static void Restore(string guild, long agoMs)
    {
        if (string.IsNullOrEmpty(guild) || agoMs < 0 || agoMs >= MemoryMs)
        {
            return;
        }

        if (!_memory.TryGetValue(guild, out var ticks))
        {
            _memory[guild] = ticks = [];
        }

        ticks.Add(Core.TickCount - agoMs);
    }

    public static IEnumerable<(string Guild, long AgoMs)> Memory()
    {
        var now = Core.TickCount;

        foreach (var (guild, ticks) in _memory)
        {
            for (var i = 0; i < ticks.Count; i++)
            {
                var ago = now - ticks[i];

                if (ago >= 0 && ago < MemoryMs)
                {
                    yield return (guild, ago);
                }
            }
        }
    }

    private static string Remembering()
    {
        if (_memory.Count == 0)
        {
            return "nobody";
        }

        using var say = Server.Text.ValueStringBuilder.Create(128);

        foreach (var (guild, ticks) in _memory)
        {
            if (ticks.Count == 0)
            {
                continue;
            }

            say.Append(say.Length > 0 ? ", " : "");
            say.Append($"{guild} ×{ticks.Count}");
        }

        return say.Length == 0 ? "nobody" : say.ToString();
    }

    public static string Describe() =>
        !Running
            ? "seizures: a claim on held ground moves only the holder's opinion"
            : $"seizures (a claim on a square another guild holds is war at once, without a meeting, and costs the claimant {Regard:F0}×n of every other guild's regard, never past the war line on that beat, and +{TownStep:P0}×n in every town, n counting its seizures in {MemoryMs / 3600000}h, time mending no opinion of it above −{Cold:F0}×n; a guild with {MostRemembered} remembered, at war, allied with the holder or weaker than the holder's side ×{Odds:0.##} does not seize): "
            + $"{Seized} declared, {WarsBegun} wars begun by one ({WarsWithheld} withheld while war is switched off; {Overtaken} meetings, {AlliancesBroken} alliances, {PactsBroken} agreements and {TrucesBroken} truces broken by them), {InWar} inside a war already standing; "
            + $"{RegardTaken:F0} regard taken from the claimants in {Cooled} opinions, {TownsRaised} town standings raised by {TownsAdded:P1} in all, {Withheld} steps of forgetting withheld; "
            + $"{Weighed} weighings, refused {_refused[(int)Cause.Ally]} for an ally, {_refused[(int)Cause.Busy]} for a war of its own, {_refused[(int)Cause.TheirsBusy]} for a holder at its limit of wars, {_refused[(int)Cause.Infamy]} for the seizures it remembers, {_refused[(int)Cause.Weaker]} for a stronger holder, {_refused[(int)Cause.Tribute]} for a holder paying tribute instead; remembered now: {Remembering()}";

    public static void Forget()
    {
        Seized = 0;
        WarsBegun = 0;
        WarsWithheld = 0;
        InWar = 0;
        PactsBroken = 0;
        AlliancesBroken = 0;
        TrucesBroken = 0;
        Overtaken = 0;
        RegardTaken = 0;
        Cooled = 0;
        TownsRaised = 0;
        TownsAdded = 0;
        Weighed = 0;
        Withheld = 0;
        Array.Clear(_refused);
        _weighed.Clear();
        _others.Clear();
    }

    public static int Wipe()
    {
        var gone = _memory.Count;

        _memory.Clear();
        Forget();

        return gone;
    }
}
