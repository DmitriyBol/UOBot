using System;
using System.Collections.Generic;
using System.Text.Json;
using Server.Guilds;
using Server.Logging;
using Server.Text;

namespace Server.BotAI.V2;

/// <summary>
/// The towns' councils: in each town the guild with the best standing collects the town's taxes for a tenth of them, the next
/// captains its guard for the town's wage, and a guild the post-holders cannot abide pays more at the town's counters.
///
/// <para>
/// <b>Patrick's order of 07.10.2026, night: "a town council: the guilds with the best standing in a town get a post (tax
/// collector, captain of the guard) and an income from it; a tax on an outcast guild".</b> The standing is the one the towns
/// already keep (<c>BotBurgh</c>): what the town's shops charge the guild, moved half a percent by every task done or failed
/// and raised by every seizure. Every <see cref="TermMs"/> the posts are given again: in each town, among the guilds that have
/// done <see cref="LeastDone"/> of its tasks or more and are not put out of it, the cheapest (then the most tasks done) is its
/// <b>tax collector</b> and the next its <b>captain of the guard</b>.
/// </para>
///
/// <para>
/// <b>The money is the town's own.</b> The island has one treasury (<c>BotCity</c>), and what it is taxed — ground, houses,
/// outlaws' fines, the outcasts' surcharge — is the towns' tax income. At the end of each term the treasury's tax take over the
/// term is shared evenly among the towns that have a collector, and each collector's chest is paid <see cref="TaxShare"/> of
/// its town's share out of the purse (<c>BotCity.Escrow</c> and <c>Disbursed</c>: the purse goes down by what the chest goes
/// up by; an empty purse pays nothing, counted). The captain's members are offered the town's guard for a wage of
/// <see cref="Wage"/>gp a watch, held from the same purse when the watch is taken, up to <see cref="WageBudget"/>gp a town a
/// term (<see cref="BotGuardDuty"/>).
/// </para>
///
/// <para>
/// <b>Outcasts.</b> A guild put out of a town (<c>BotBurgh.Bars</c>), or thought <see cref="OutcastRegard"/> of or worse by
/// either of the town's post-holders, pays <see cref="OutcastFactor"/> times at the town's counters — on top of its standing,
/// through the same factor every counter reads (<c>BotBurgh.Factor</c>) — and that part of the bill goes into the treasury
/// as tax (<see cref="Taxed"/>) rather than out of the world.
/// </para>
/// </summary>
public static class BotCouncil
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotCouncil));

    public static bool Running { get; set; } = true;

    public static int TermMs { get; set; } = 7200000;

    public static int FirstMs { get; set; } = 900000;

    public static double TaxShare { get; set; } = 0.10;

    public static int LeastDone { get; set; } = 1;

    public static double OutcastRegard { get; set; } = -60.0;

    public static double OutcastFactor { get; set; } = 1.05;

    public static bool Captaincy { get; set; } = true;

    public static int Wage { get; set; } = 20;

    public static int WageBudget { get; set; } = 400;

    public static int MostOnDuty { get; set; } = 2;

    public static int Reach { get; set; } = 250;

    /// <summary>One town's council for this term.</summary>
    private sealed class Seat
    {
        public string Town;

        public string Collector;

        public string Captain;

        public int Wages;

        public int OnDuty;

        public int Extra;

        public string Hired;

        public long HiredUntil;
    }

    private static readonly Dictionary<string, Seat> _seats = new(StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<(string Guild, string Town)> _outcast = [];

    private static readonly HashSet<(string Guild, string Town)> _saidOutcast = [];

    private static readonly List<BotBurgh.Standing> _ranked = [];

    private static long _termAt;

    private static long _taxMark;

    private static bool _opened;

    private static (Serial Buyer, double Factor, long Tick) _quote;

    public static long Terms { get; private set; }

    public static long Posts { get; private set; }

    public static long Changed { get; private set; }

    public static long Commissions { get; private set; }

    public static long CommissionGold { get; private set; }

    public static long CommissionsUnpaid { get; private set; }

    public static long Watches { get; private set; }

    public static long WageGold { get; private set; }

    public static long PurseShort { get; private set; }

    public static long BudgetSpent { get; private set; }

    public static long OutcastBuys { get; private set; }

    public static long OutcastGold { get; private set; }

    public static bool AnyOutcast => Running && _outcast.Count > 0;

    public static void Open(long now)
    {
        _termAt = now - Math.Max(0, TermMs) + Math.Max(0, FirstMs);
        _taxMark = BotCity.Taxed;
        _opened = true;
    }

    public static double Surcharge(Mobile buyer, string guild, string town)
    {
        if (!Running || _outcast.Count == 0 || buyer == null || !_outcast.Contains((guild, town)) || OutcastFactor <= 1.0)
        {
            return 1.0;
        }

        _quote = (buyer.Serial, OutcastFactor, Core.TickCount);

        return OutcastFactor;
    }

    public static void Taxed(Mobile buyer, int bill, double factor, int more)
    {
        if (buyer == null || more <= 0 || _quote.Buyer != buyer.Serial || _quote.Tick != Core.TickCount || _quote.Factor <= 1.0)
        {
            return;
        }

        var share = (int)Math.Min(more, Math.Ceiling(bill * (factor - factor / _quote.Factor)));

        _quote = default;

        if (share <= 0)
        {
            return;
        }

        BotCity.Tax(share);
        OutcastBuys++;
        OutcastGold += share;
    }

    public static bool AnyCaptain { get; private set; }

    public static BotTowns.Town CaptainOf(string guild, Point3D at, int reach)
    {
        if (!AnyCaptain || guild == null)
        {
            return null;
        }

        BotTowns.Town best = null;
        var bestDistance = int.MaxValue;

        foreach (var (_, seat) in _seats)
        {
            if (seat.Captain != guild || BotTowns.Find(seat.Town) is not { } town)
            {
                continue;
            }

            var distance = Math.Max(Math.Abs(town.Square.X - at.X), Math.Abs(town.Square.Y - at.Y));

            if (distance <= reach && distance < bestDistance)
            {
                best = town;
                bestDistance = distance;
            }
        }

        return best;
    }

    public static bool Captains(string guild, BotTowns.Town town) =>
        Running && Captaincy && guild != null && town != null && _seats.TryGetValue(town.Name, out var seat) && seat.Captain == guild;

    public static bool Room(BotTowns.Town town)
    {
        if (town == null || !_seats.TryGetValue(town.Name, out var seat))
        {
            return false;
        }

        return seat.OnDuty < MostOnDuty && seat.Wages + Wage <= WageBudget + seat.Extra && BotCity.Purse >= Wage;
    }

    public static bool Hold(BotTowns.Town town)
    {
        if (town == null || !_seats.TryGetValue(town.Name, out var seat) || !BotCity.Escrow(Wage))
        {
            PurseShort++;

            return false;
        }

        seat.Wages += Wage;
        seat.OnDuty++;

        if (seat.Wages + Wage > WageBudget + seat.Extra)
        {
            BudgetSpent++;
        }

        return true;
    }

    public static void Release(BotTowns.Town town, bool paid)
    {
        _seats.TryGetValue(town?.Name ?? "", out var seat);

        if (seat != null)
        {
            seat.OnDuty = Math.Max(0, seat.OnDuty - 1);
        }

        if (paid)
        {
            BotCity.Disbursed(Wage);
            Watches++;
            WageGold += Wage;

            return;
        }

        BotCity.Refund(Wage);

        if (seat != null)
        {
            seat.Wages = Math.Max(0, seat.Wages - Wage);
        }
    }

    public static void Look(long now)
    {
        if (!Running)
        {
            _outcast.Clear();

            return;
        }

        if (!_opened)
        {
            Open(now);
        }

        if (BotTowns.Surveyed && now - _termAt >= TermMs)
        {
            EndTerm(now);
            Assign(now);
            Recount();
        }

        Outcasts();
    }

    private static void EndTerm(long now)
    {
        var taxed = BotCity.Taxed - _taxMark;

        _taxMark = BotCity.Taxed;

        var collectors = 0;

        foreach (var (_, seat) in _seats)
        {
            if (seat.Collector != null)
            {
                collectors++;
            }
        }

        if (taxed <= 0 || collectors == 0 || TaxShare <= 0.0)
        {
            return;
        }

        var fee = (int)Math.Min(int.MaxValue, (long)(TaxShare * taxed / collectors));

        if (fee <= 0)
        {
            return;
        }

        foreach (var (_, seat) in _seats)
        {
            if (seat.Collector == null || BotGuilds.Named(seat.Collector) is not { Disbanded: false })
            {
                continue;
            }

            if (!BotCity.Escrow(fee))
            {
                CommissionsUnpaid++;

                logger.Information(
                    "{Guild} collected {Town}'s taxes this term and the treasury could not pay its {Fee}gp: {Purse}gp in the purse",
                    seat.Collector,
                    seat.Town,
                    fee,
                    BotCity.Purse
                );

                continue;
            }

            BotCity.Disbursed(fee);
            BotChest.Put(seat.Collector, fee);
            Commissions++;
            CommissionGold += fee;

            logger.Information(
                "{Guild} was paid {Fee}gp for collecting {Town}'s taxes: {Share:P0} of the town's share of {Taxed}gp taxed over the term among {Towns} towns; its chest holds {Chest}gp",
                seat.Collector,
                fee,
                seat.Town,
                TaxShare,
                taxed,
                collectors,
                BotChest.Holds(seat.Collector)
            );
        }
    }

    private static void Assign(long now)
    {
        _termAt = now;
        Terms++;

        var towns = BotTowns.All;

        for (var t = 0; t < towns.Count; t++)
        {
            var town = towns[t];

            _ranked.Clear();

            foreach (var st in BotBurgh.All)
            {
                if (st.Town != town.Name || st.Exiled || st.Done < LeastDone || BotGuilds.Named(st.Guild) is not { Disbanded: false } g
                    || BotUnderworld.Band(g))
                {
                    continue;
                }

                var at = _ranked.Count;

                while (at > 0 && Better(st, _ranked[at - 1]))
                {
                    at--;
                }

                _ranked.Insert(at, st);
            }

            _seats.TryGetValue(town.Name, out var seat);

            var collector = _ranked.Count > 0 ? _ranked[0].Guild : null;
            var captain = _ranked.Count > 1 ? _ranked[1].Guild : null;

            if (seat?.Hired != null && seat.HiredUntil - now > 0 && seat.Hired != collector && BotGuilds.Named(seat.Hired) is { Disbanded: false })
            {
                captain = seat.Hired;
            }
            else if (seat != null)
            {
                seat.Hired = null;
                seat.Extra = 0;
            }

            if (seat == null)
            {
                if (collector == null)
                {
                    continue;
                }

                _seats[town.Name] = seat = new Seat { Town = town.Name };
            }

            var wasCollector = seat.Collector;
            var wasCaptain = seat.Captain;

            seat.Collector = collector;
            seat.Captain = captain;
            seat.Wages = 0;

            Posts += (collector != null ? 1 : 0) + (captain != null ? 1 : 0);

            if (collector == wasCollector && captain == wasCaptain)
            {
                continue;
            }

            Changed++;

            logger.Information(
                "{Town}'s council for the next {Hours}h: {Collector} collects its taxes{CollectorWhy}, {Captain} captains its guard{CaptainWhy}{Was}",
                town.Name,
                TermMs / 3600000,
                collector ?? "nobody",
                _ranked.Count > 0 ? $" (×{_ranked[0].Factor:F3}, {_ranked[0].Done} tasks done)" : "",
                captain ?? "nobody",
                _ranked.Count > 1 ? $" (×{_ranked[1].Factor:F3}, {_ranked[1].Done} tasks done)" : "",
                wasCollector != null || wasCaptain != null ? $"; before, {wasCollector ?? "nobody"} and {wasCaptain ?? "nobody"}" : ""
            );

            var fill = new Dictionary<string, string> { ["town"] = town.Name };

            if (collector != null && collector != wasCollector)
            {
                BotVoice.ToGuild(collector, "politics:collector", fill);
                BotEvents.Post("politics", BotGuilds.Head(BotGuilds.Named(collector)), $"{collector} collects {town.Name}'s taxes");
            }

            if (captain != null && captain != wasCaptain)
            {
                BotVoice.ToGuild(captain, "politics:captain", fill);
                BotEvents.Post("politics", BotGuilds.Head(BotGuilds.Named(captain)), $"{captain} captains {town.Name}'s guard");
            }
        }
    }

    private static void Recount()
    {
        AnyCaptain = false;

        foreach (var (_, seat) in _seats)
        {
            if (seat.Captain != null)
            {
                AnyCaptain = true;

                return;
            }
        }
    }

    public static string CaptainIn(string town) => Running && town != null && _seats.TryGetValue(town, out var seat) ? seat.Captain : null;

    public static string CollectorIn(string town) => Running && town != null && _seats.TryGetValue(town, out var seat) ? seat.Collector : null;

    public static bool IsOutcast(string guild, string town) => Running && guild != null && town != null && _outcast.Contains((guild, town));

    public static int BudgetLeft(string town) =>
        town != null && _seats.TryGetValue(town, out var seat) ? Math.Max(0, WageBudget + seat.Extra - seat.Wages) : 0;

    public static bool Eligible(Guild guild, BotTowns.Town town)
    {
        if (guild is not { Disbanded: false } || town == null || BotUnderworld.Band(guild))
        {
            return false;
        }

        foreach (var st in BotBurgh.All)
        {
            if (st.Guild == guild.Name && st.Town == town.Name)
            {
                return !st.Exiled && st.Done >= LeastDone;
            }
        }

        return false;
    }

    public static bool Appoint(BotTowns.Town town, string guild, int budget, long until)
    {
        if (!Running || !Captaincy || town == null || guild == null)
        {
            return false;
        }

        if (!_seats.TryGetValue(town.Name, out var seat))
        {
            _seats[town.Name] = seat = new Seat { Town = town.Name };
        }

        var was = seat.Captain;

        seat.Captain = guild;
        seat.Hired = guild;
        seat.HiredUntil = until;
        seat.Extra += Math.Max(0, budget);
        Recount();

        if (was != guild)
        {
            Changed++;
            BotVoice.ToGuild(guild, "politics:captain", new Dictionary<string, string> { ["town"] = town.Name });

            logger.Information("{Town}'s steward hired {Guild} to captain its guard instead of {Was}, with {Budget}gp more for its watches", town.Name, guild, was ?? "nobody", budget);
        }

        return true;
    }

    public static void Fund(string town, int budget)
    {
        if (town != null && budget > 0 && _seats.TryGetValue(town, out var seat))
        {
            seat.Extra += budget;
        }
    }

    private static bool Better(BotBurgh.Standing a, BotBurgh.Standing b) =>
        a.Factor < b.Factor - 0.0001 || Math.Abs(a.Factor - b.Factor) <= 0.0001 && (a.Done > b.Done || a.Done == b.Done && a.Failed < b.Failed);

    private static void Outcasts()
    {
        _outcast.Clear();

        if (_seats.Count == 0)
        {
            return;
        }

        foreach (var (_, seat) in _seats)
        {
            if (seat.Collector == null && seat.Captain == null)
            {
                continue;
            }

            var town = BotTowns.Find(seat.Town);

            foreach (var guild in BotGuilds.Standing)
            {
                if (guild is not { Disbanded: false } || guild.Name == seat.Collector || guild.Name == seat.Captain)
                {
                    continue;
                }

                var outcast = town != null && BotBurgh.Bars(guild, town)
                              || seat.Collector != null && BotRegard.Of(seat.Collector, guild.Name) <= OutcastRegard
                              || seat.Captain != null && BotRegard.Of(seat.Captain, guild.Name) <= OutcastRegard;

                if (!outcast)
                {
                    continue;
                }

                _outcast.Add((guild.Name, seat.Town));

                if (_saidOutcast.Add((guild.Name, seat.Town)))
                {
                    logger.Information(
                        "{Guild} is {Town}'s outcast: {Why}; it pays ×{Factor:F2} at the town's counters",
                        guild.Name,
                        seat.Town,
                        town != null && BotBurgh.Bars(guild, town) ? "put out of the town" : $"its post-holders think {OutcastRegard:F0} of it or worse",
                        OutcastFactor
                    );

                    BotVoice.ToGuild(guild.Name, "politics:outcast", new Dictionary<string, string> { ["town"] = seat.Town });
                }
            }
        }

        _saidOutcast.IntersectWith(_outcast);
    }

    public static void Write(Utf8JsonWriter w, string guild)
    {
        w.WritePropertyName("posts");
        w.WriteStartArray();

        foreach (var (_, seat) in _seats)
        {
            if (seat.Collector == guild)
            {
                w.WriteStartObject();
                w.WriteString("town", seat.Town);
                w.WriteString("post", "tax collector");
                w.WriteEndObject();
            }

            if (seat.Captain == guild)
            {
                w.WriteStartObject();
                w.WriteString("town", seat.Town);
                w.WriteString("post", "captain of the guard");
                w.WriteNumber("onDuty", seat.OnDuty);
                w.WriteNumber("wages", seat.Wages);
                w.WriteEndObject();
            }
        }

        w.WriteEndArray();

        w.WritePropertyName("outcastIn");
        w.WriteStartArray();

        foreach (var (g, town) in _outcast)
        {
            if (g == guild)
            {
                w.WriteStringValue(town);
            }
        }

        w.WriteEndArray();
    }

    public static string Describe()
    {
        if (!Running)
        {
            return "the towns' councils are OFF";
        }

        using var say = ValueStringBuilder.Create(512);

        say.Append(
            $"councils every {TermMs / 60000}m: {Terms} terms, {Posts} posts given ({Changed} councils changed); {Commissions} commissions of {TaxShare:P0} paid to collectors ({CommissionGold}gp from the treasury, {CommissionsUnpaid} the purse could not pay); "
        );
        say.Append(
            Captaincy
                ? $"{Watches} watches of the town's guard paid {Wage}gp each ({WageGold}gp, up to {WageBudget}gp a town a term, {MostOnDuty} on duty at once; {BotGuardDuty.Taken} taken, {PurseShort} the purse could not hold a wage for, {BudgetSpent} town-terms with the budget spent, {BotGuardDuty.Unpaid} ended unpaid); "
                : "the captaincy's wage is OFF; "
        );
        say.Append($"outcasts at regard {OutcastRegard:F0} or put out pay ×{OutcastFactor:F2}: {_outcast.Count} now, {OutcastBuys} purchases taxed {OutcastGold}gp into the treasury; posts: ");

        var first = true;

        foreach (var (_, seat) in _seats)
        {
            say.Append(first ? "" : ", ");
            first = false;
            say.Append($"{seat.Town} {seat.Collector ?? "-"}/{seat.Captain ?? "-"}");
        }

        if (first)
        {
            say.Append("none yet");
        }

        return say.ToString();
    }

    private static int _wagesAtLoad;

    private static int HeldWages()
    {
        long held = 0;

        foreach (var (_, seat) in _seats)
        {
            held += (long)Math.Max(0, seat.OnDuty) * Math.Max(0, Wage);
        }

        return (int)Math.Min(int.MaxValue, held);
    }

    internal static void SaveHeld(IGenericWriter writer) => writer.WriteEncodedInt((int)Math.Min(int.MaxValue, (long)HeldWages() + _wagesAtLoad));

    internal static int LoadHeld(IGenericReader reader)
    {
        _wagesAtLoad = Math.Max(0, reader.ReadEncodedInt());

        return _wagesAtLoad;
    }

    public static int Settle()
    {
        var gold = _wagesAtLoad;

        _wagesAtLoad = 0;

        if (gold <= 0)
        {
            return 0;
        }

        BotCity.Held(gold);
        BotCity.Refund(gold);

        logger.Information("Councils: {Gold}gp of wages held for watches on duty at the last save handed back to the treasury", gold);

        return gold;
    }

    internal static void Save(IGenericWriter writer)
    {
        writer.Write(Math.Max(0L, Core.TickCount - _termAt));
        writer.WriteEncodedInt(_seats.Count);

        foreach (var (_, seat) in _seats)
        {
            writer.Write(seat.Town);
            writer.Write(seat.Collector ?? "");
            writer.Write(seat.Captain ?? "");
        }
    }

    internal static int Load(IGenericReader reader)
    {
        var now = Core.TickCount;
        var ago = reader.ReadLong();
        var count = reader.ReadEncodedInt();

        for (var i = 0; i < count; i++)
        {
            var town = reader.ReadString();
            var collector = reader.ReadString();
            var captain = reader.ReadString();

            if (string.IsNullOrEmpty(town))
            {
                continue;
            }

            _seats[town] = new Seat
            {
                Town = town,
                Collector = string.IsNullOrEmpty(collector) ? null : collector,
                Captain = string.IsNullOrEmpty(captain) ? null : captain
            };
        }

        _termAt = now - Math.Clamp(ago, 0L, (long)int.MaxValue);
        _taxMark = BotCity.Taxed;
        _opened = true;
        Recount();

        return count;
    }

    public static void Forget()
    {
        Terms = 0;
        Posts = 0;
        Changed = 0;
        Commissions = 0;
        CommissionGold = 0;
        CommissionsUnpaid = 0;
        Watches = 0;
        WageGold = 0;
        PurseShort = 0;
        BudgetSpent = 0;
        OutcastBuys = 0;
        OutcastGold = 0;
        _outcast.Clear();
        _saidOutcast.Clear();
    }

    public static int Wipe()
    {
        var back = 0;

        var held = HeldWages();

        if (held > 0)
        {
            BotCity.Refund(held);
            back += held;
        }

        back += Settle();

        _seats.Clear();
        _outcast.Clear();
        _saidOutcast.Clear();
        _ranked.Clear();
        _quote = default;

        Open(Core.TickCount);
        Recount();
        Forget();

        return back;
    }
}
