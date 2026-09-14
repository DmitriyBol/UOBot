using System;
using System.Collections.Generic;
using Server.Guilds;
using Server.Logging;

namespace Server.BotAI.V2;

/// <summary>
/// The wars themselves: which pairs are fighting, what each side has taken off the other, and the rules
/// that begin, end and forbid one.
///
/// <para>
/// <b>Patrick's order of 13.09.2026, and the twelve hours that produced it.</b> Between 02:12 and 14:21
/// the shard declared twelve wars and ended five; the worst opinion on the island sat at the floor of the
/// scale for the whole session, seven wars stood at once, and 3,054 bots were killed by other bots —
/// about 250 an hour, one death every fifteen seconds. Nothing in the war ever finished it: it began as a
/// number crossing a threshold and it could only end as the same number climbing back, and 4,337 refusals
/// to move along at twelve apiece meant the number never climbed anywhere. A war with no way to be won is
/// not a war, it is the weather.
/// </para>
///
/// <para>
/// <b>Four rules, each a dial, and the whole design is that a war now has an end it can reach.</b> It is
/// won by <see cref="Kills"/> of the enemy or by <see cref="Loot"/> gold's worth taken off their corpses;
/// it cannot be ended by a change of heart before <see cref="LeastMs"/>, and it is judged on what blood
/// there was when <see cref="LongestMs"/> runs out. A guild may declare one war in <see cref="DeclareEveryMs"/>
/// and stand in <see cref="MostWars"/> at a time, and the two guilds keep a truce of <see cref="TruceMs"/>
/// afterwards — a peace that is broken the next minute was not a peace.
/// </para>
///
/// <para>
/// <b>The opinion still decides when a war begins; this decides whether it may.</b> <c>BotRegard</c> keeps
/// what every guild thinks of every other and asks here before it declares. The engine still holds the war
/// itself (<c>Guild.IsWar</c>) and still does the enforcing — nothing about notoriety or looting changed.
/// What changed is that a war is a thing with a beginning, a score and an end, all of them printed.
/// </para>
/// </summary>
public static class BotWar
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotWar));

    public static bool Running { get; set; } = true;

    public static int Kills { get; set; } = 50;

    public static int Loot { get; set; } = 10000;

    public static int LeastMs { get; set; } = 900000;

    public static int LongestMs { get; set; } = 10800000;

    public static int TruceMs { get; set; } = 3600000;

    public static int DeclareEveryMs { get; set; } = 1800000;

    public static int MostWars { get; set; } = 1;

    public static int LookMs { get; set; } = 10000;

    public static long Declared { get; private set; }

    public static long Truced { get; private set; }

    public static long Cooling { get; private set; }

    public static long Busy { get; private set; }

    public static long WonByBlood { get; private set; }

    public static long WonByPlunder { get; private set; }

    public static long TimedOut { get; private set; }

    public static long Peaced { get; private set; }

    public static long Drawn { get; private set; }

    public static long Counted { get; private set; }

    public static long Plundered { get; private set; }

    /// <summary>One war.</summary>
    public sealed class War
    {
        public string A;

        public string B;

        public string Declarer;

        public long Began;

        public int KillsA;

        public int KillsB;

        public int LootA;

        public int LootB;

        public string Why;

        public (string A, string B)? Joined;

        public string Against(string guild) => guild == A ? B : guild == B ? A : null;

        public (int Kills, int Loot) Score(string guild) => guild == A ? (KillsA, LootA) : (KillsB, LootB);

        public int Minutes => (int)((Core.TickCount - Began) / 60000);
    }

    private static readonly Dictionary<(string A, string B), War> _wars = [];

    private static readonly Dictionary<(string A, string B), long> _truces = [];

    private static readonly Dictionary<string, long> _declared = [];

    private static long _looked;

    private static readonly List<War> _over = [];

    private static (string A, string B) Pair(string one, string other) =>
        string.CompareOrdinal(one, other) <= 0 ? (one, other) : (other, one);

    public static War Of(string one, string other) =>
        one == null || other == null ? null : _wars.GetValueOrDefault(Pair(one, other));

    public static IEnumerable<War> Standing => _wars.Values;

    public static IEnumerable<KeyValuePair<(string A, string B), long>> Truces => _truces;

    public static IEnumerable<KeyValuePair<string, long>> Declarations => _declared;

    public static void Restore(War war, long elapsedMs)
    {
        if (war?.A == null || war.B == null)
        {
            return;
        }

        war.Began = Core.TickCount - Math.Max(0, elapsedMs);
        _wars[(war.A, war.B)] = war;
    }

    public static void RestoreTruce(string a, string b, long leftMs)
    {
        if (a != null && b != null && leftMs > 0)
        {
            _truces[Pair(a, b)] = Core.TickCount + leftMs;
        }
    }

    public static void RestoreDeclared(string guild, long agoMs)
    {
        if (guild != null && agoMs >= 0)
        {
            _declared[guild] = Core.TickCount - agoMs;
        }
    }

    public static int Fighting(string guild)
    {
        var many = 0;

        foreach (var war in _wars.Values)
        {
            if (war.A == guild || war.B == guild)
            {
                many++;
            }
        }

        return many;
    }

    public static bool MayDeclare(string mine, string theirs, out string why)
    {
        why = null;

        if (!Running)
        {
            return true;
        }

        var now = Core.TickCount;
        var pair = Pair(mine, theirs);

        if (_truces.TryGetValue(pair, out var until))
        {
            if (now - until < 0)
            {
                Truced++;
                why = $"a truce with {(until - now) / 60000} minutes left";

                return false;
            }

            _truces.Remove(pair);
        }

        if (_declared.TryGetValue(mine, out var last) && now - (last + DeclareEveryMs) < 0)
        {
            Cooling++;
            why = $"{mine} declared one {(now - last) / 60000} minutes ago";

            return false;
        }

        if (Fighting(mine) >= MostWars || Fighting(theirs) >= MostWars)
        {
            Busy++;
            why = Fighting(mine) >= MostWars ? $"{mine} is already at war" : $"{theirs} is already at war";

            return false;
        }

        return true;
    }

    public static void Declare(Guild mine, Guild theirs, string why)
    {
        if (mine == null || theirs == null || mine == theirs)
        {
            return;
        }

        var pair = Pair(mine.Name, theirs.Name);

        if (_wars.ContainsKey(pair))
        {
            return;
        }

        mine.AddEnemy(theirs);

        var war = new War
        {
            A = pair.A,
            B = pair.B,
            Declarer = mine.Name,
            Began = Core.TickCount,
            Why = why
        };

        _wars[pair] = war;
        _declared[mine.Name] = war.Began;
        Declared++;

        Enlist(mine, theirs, pair);
        Enlist(theirs, mine, pair);

        logger.Warning(
            "{Mine} has declared war on {Theirs} over {Why}; it is won at {Kills} dead or {Loot}gp of plunder, judged after {Longest} minutes, and cannot be ended for {Least}",
            mine.Name,
            theirs.Name,
            why,
            Kills,
            Loot,
            LongestMs / 60000,
            LeastMs / 60000
        );

        BotAlarm.Note("war", $"{mine.Name} declared war on {theirs.Name} over {why}", Fighting(mine.Name), _wars.Count, "-");
    }

    public static void Reconcile()
    {
        var ended = 0;
        List<(Guild, Guild)> standing = [];

        List<(string A, string B)> stale = null;

        foreach (var (pair, _) in _wars)
        {
            var ga = BotGuilds.Named(pair.A) ?? BaseGuild.FindByName(pair.A) as Guild;
            var gb = BotGuilds.Named(pair.B) ?? BaseGuild.FindByName(pair.B) as Guild;

            if (ga != null && gb != null)
            {
                var knew = ga.IsWar(gb);

                if (!knew)
                {
                    ga.AddEnemy(gb);
                }

                logger.Information(
                    "The war of {A} on {B} read back off the save: the engine {Knew}; the first now lists {Na} enemies and the second {Nb}",
                    pair.A,
                    pair.B,
                    knew ? "already held it" : "was told again",
                    ga.Enemies?.Count ?? 0,
                    gb.Enemies?.Count ?? 0
                );

                continue;
            }

            (stale ??= []).Add(pair);
        }

        if (stale != null)
        {
            for (var i = 0; i < stale.Count; i++)
            {
                _wars.Remove(stale[i]);
            }

            logger.Information("{Many} wars read back off the save were dropped for a guild that no longer stands", stale.Count);
        }

        foreach (var mine in BotGuilds.Standing)
        {
            var enemies = mine?.Enemies;

            if (enemies == null)
            {
                continue;
            }

            for (var i = 0; i < enemies.Count; i++)
            {
                if (enemies[i] is Guild theirs && Of(mine.Name, theirs.Name) == null)
                {
                    standing.Add((mine, theirs));
                }
            }
        }

        for (var i = 0; i < standing.Count; i++)
        {
            var (mine, theirs) = standing[i];

            if (mine.IsWar(theirs))
            {
                mine.RemoveEnemy(theirs);
                ended++;
            }
        }

        if (ended > 0)
        {
            logger.Information("{Ended} wars left standing by the last session were ended at boot; the ledger starts empty", ended);
        }
    }

    private static void Enlist(Guild ours, Guild theirs, (string A, string B) cause)
    {
        var allies = ours?.Allies;

        if (allies == null)
        {
            return;
        }

        for (var i = 0; i < allies.Count; i++)
        {
            if (allies[i] is not Guild ally || ally == theirs || ally.IsWar(theirs) || ally.IsAlly(theirs))
            {
                continue;
            }

            var pair = Pair(ally.Name, theirs.Name);

            if (_wars.ContainsKey(pair))
            {
                continue;
            }

            ally.AddEnemy(theirs);

            _wars[pair] = new War
            {
                A = pair.A,
                B = pair.B,
                Declarer = ally.Name,
                Began = Core.TickCount,
                Why = $"alliance with {ours.Name}",
                Joined = cause
            };

            logger.Warning("{Ally} joins the war on {Theirs} beside its ally {Ours}", ally.Name, theirs.Name, ours.Name);
        }
    }

    public static void Killed(string killer, string fallen)
    {
        var war = Of(killer, fallen);

        if (war == null)
        {
            return;
        }

        Counted++;

        if (killer == war.A)
        {
            war.KillsA++;
        }
        else
        {
            war.KillsB++;
        }

        Reckon(war);
    }

    public static void Looted(string taker, string victim, int worth)
    {
        var war = Of(taker, victim);

        if (war == null || worth <= 0)
        {
            return;
        }

        Plundered += worth;

        if (taker == war.A)
        {
            war.LootA += worth;
        }
        else
        {
            war.LootB += worth;
        }

        Reckon(war);
    }

    public static bool MayPeace(string one, string other)
    {
        if (!Running)
        {
            return true;
        }

        var war = Of(one, other);

        if (war?.Joined != null)
        {
            return false;
        }

        return war != null && Core.TickCount - (war.Began + LeastMs) >= 0;
    }

    public static void Peace(string one, string other)
    {
        var war = Of(one, other);

        if (war == null)
        {
            return;
        }

        Peaced++;

        var ahead = Ahead(war);
        var margin = Math.Abs(war.KillsA - war.KillsB);

        End(war, margin >= PeaceMargin ? ahead : null, "peace");
    }

    public static int PeaceMargin { get; set; } = 5;

    public static void Beat()
    {
        if (!Running || _wars.Count == 0)
        {
            return;
        }

        var now = Core.TickCount;

        if (now - (_looked + LookMs) < 0)
        {
            return;
        }

        _looked = now;
        _over.Clear();

        foreach (var war in _wars.Values)
        {
            if (war.Joined == null && now - (war.Began + LongestMs) >= 0)
            {
                _over.Add(war);
            }
        }

        for (var i = 0; i < _over.Count; i++)
        {
            TimedOut++;
            End(_over[i], Ahead(_over[i]), "the clock");
        }
    }

    private static void Reckon(War war)
    {
        if (!Running || war.Joined != null)
        {
            return;
        }

        if (war.KillsA >= Kills || war.KillsB >= Kills)
        {
            WonByBlood++;
            End(war, war.KillsA >= Kills ? war.A : war.B, "blood");

            return;
        }

        if (war.LootA >= Loot || war.LootB >= Loot)
        {
            WonByPlunder++;
            End(war, war.LootA >= Loot ? war.A : war.B, "plunder");
        }
    }

    private static string Ahead(War war) =>
        war.KillsA > war.KillsB ? war.A : war.KillsB > war.KillsA ? war.B : null;

    private static void End(War war, string winner, string how)
    {
        _wars.Remove((war.A, war.B));
        _truces[(war.A, war.B)] = Core.TickCount + TruceMs;

        BotFeud.Disband(war.A);
        BotFeud.Disband(war.B);

        if (war.Joined == null)
        {
            List<War> joined = null;

            foreach (var other in _wars.Values)
            {
                if (other.Joined is { } cause && cause.A == war.A && cause.B == war.B)
                {
                    (joined ??= []).Add(other);
                }
            }

            if (joined != null)
            {
                for (var i = 0; i < joined.Count; i++)
                {
                    var side = joined[i];

                    _wars.Remove((side.A, side.B));

                    if (BaseGuild.FindByName(side.A) is Guild ga && BaseGuild.FindByName(side.B) is Guild gb)
                    {
                        ga.RemoveEnemy(gb);
                    }

                    BotFeud.Disband(side.A);
                    BotFeud.Disband(side.B);
                    BotRegard.Settle(side.A, side.B);

                    logger.Information("{A} leaves the war with {B}, which it had joined for an ally", side.Declarer, side.Against(side.Declarer));
                }
            }
        }

        if (BaseGuild.FindByName(war.A) is Guild a && BaseGuild.FindByName(war.B) is Guild b)
        {
            a.RemoveEnemy(b);
        }

        BotRegard.Settle(war.A, war.B);

        if (winner == null)
        {
            Drawn++;
            BotExile.Draw(war.A, war.B);

            logger.Warning(
                "The war between {A} and {B} ended by {How} after {Minutes} minutes with nobody ahead: {KillsA} to {KillsB} dead, {LootA}gp to {LootB}gp taken; a truce of {Truce} minutes",
                war.A,
                war.B,
                how,
                war.Minutes,
                war.KillsA,
                war.KillsB,
                war.LootA,
                war.LootB,
                TruceMs / 60000
            );

            BotAlarm.Note("war", $"the war between {war.A} and {war.B} ended by {how} after {war.Minutes} minutes, drawn {war.KillsA}:{war.KillsB}", 0, _wars.Count, $"{war.Minutes}m");

            return;
        }

        var loser = war.Against(winner);
        var (wonKills, wonLoot) = war.Score(winner);
        var (lostKills, lostLoot) = war.Score(loser);

        logger.Warning(
            "{Winner} won its war with {Loser} by {How} after {Minutes} minutes: {WonKills} to {LostKills} dead, {WonLoot}gp to {LostLoot}gp taken; a truce of {Truce} minutes",
            winner,
            loser,
            how,
            war.Minutes,
            wonKills,
            lostKills,
            wonLoot,
            lostLoot,
            TruceMs / 60000
        );

        BotAlarm.Note("war", $"{winner} won its war with {loser} by {how} after {war.Minutes} minutes, {wonKills}:{lostKills} dead and {wonLoot}:{lostLoot}gp taken", wonKills, lostKills, $"{war.Minutes}m");

        BotExile.Sentence(winner, loser);
    }

    public static string Tell()
    {
        if (_wars.Count == 0)
        {
            return "no war is standing";
        }

        using var say = Server.Text.ValueStringBuilder.Create(256);

        foreach (var war in _wars.Values)
        {
            if (say.Length > 0)
            {
                say.Append("; ");
            }

            say.Append(
                $"{war.Declarer} on {war.Against(war.Declarer)} for {war.Minutes} min over {war.Why}{(war.Joined == null ? "" : " (joined)")}: {war.A} {war.KillsA} dead {war.LootA}gp, {war.B} {war.KillsB} dead {war.LootB}gp"
            );
        }

        return say.ToString();
    }

    public static string Describe() =>
        !Running
            ? "wars are not ruled"
            : $"{_wars.Count} wars standing ({Tell()}); won at {Kills} dead or {Loot}gp of plunder, no peace before {LeastMs / 60000} minutes, "
            + $"judged at {LongestMs / 60000}, a truce of {TruceMs / 60000} after, one declaration a guild per {DeclareEveryMs / 60000} minutes and {MostWars} at a time; "
            + $"{Declared} declared, {Truced} refused for a truce, {Cooling} for declaring too soon, {Busy} for a guild already at war; "
            + $"{WonByBlood} won by blood, {WonByPlunder} by plunder, {TimedOut} judged by the clock, {Peaced} ended in peace, {Drawn} of those drawn; "
            + $"{Counted} kills and {Plundered}gp of plunder counted";

    public static void Forget()
    {
        _wars.Clear();
        _truces.Clear();
        _declared.Clear();
        _over.Clear();
        _looked = 0;
        Declared = 0;
        Truced = 0;
        Cooling = 0;
        Busy = 0;
        WonByBlood = 0;
        WonByPlunder = 0;
        TimedOut = 0;
        Peaced = 0;
        Drawn = 0;
        Counted = 0;
        Plundered = 0;
    }
}
