using System;
using System.Collections.Generic;
using Server.Guilds;
using Server.Logging;

namespace Server.BotAI.V2;

/// <summary>
/// How long a bot plays before it is tired, and the rest it takes before it plays again.
///
/// <para>
/// <b>Patrick's order of the night of 24.09.2026: bots have their own stamina for the game.</b> One is fresh for five
/// hours, another plays three, another ten — from the worker who plays after his shift to the player who never
/// stops — and then they tire, leave the world, rest for five to eight hours and come back. Everybody tires at a
/// different time, so the island is never empty.
/// </para>
///
/// <para>
/// <b>A tired bot finishes what it is doing, and only then goes.</b> Tiredness takes nothing away: it closes the
/// auction, so the bot is offered nothing new (<see cref="BotWill"/>), and the bot leaves the first time it is free —
/// nothing in hand, in no company, not in a fight, not underground, not in a cell, not in a duel. A bot still not free
/// <see cref="GraceMs"/> after it tired is let go of its work and its company and leaves anyway; a fight, a dungeon and
/// a cell are waited out, because nobody walks out of those.
/// </para>
///
/// <para>
/// <b>Leaving is logging out, the engine's own way.</b> The body goes to <c>Map.Internal</c> with its place written to
/// <c>LogoutLocation</c> — exactly where the engine parks a player with no connection — and its slot in the population
/// becomes a hole (<see cref="BotPopulation.Park"/>), so nothing that walks the population can see it or offer it
/// anything: not the auction, not the companies, not the guilds' muster, not the watchers. It comes back where it left,
/// or at home when that ground no longer takes a body.
/// </para>
///
/// <para>
/// How long a bot plays is a fact about the bot rather than a roll each session: its kind is dealt once, from a deck
/// holding the kinds in their exact shares (<see cref="Deal"/>), and kept, so a keen player stays keen across restarts
/// and the log can say which kind each one is. How long it rests is rolled every time.
/// Play counts only while the bot is in the world and the shard is up; rest counts on the wall clock, shard or no shard,
/// because a player's evening does not stop when the server does. Both are kept in <see cref="BotRestStore"/>.
/// </para>
/// </summary>
public static class BotRest
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotRest));

    public static bool Running { get; set; } = true;

    public static int LookMs { get; set; } = 20000;

    public static double LeastRestHours { get; set; } = 5.0;

    public static double MostRestHours { get; set; } = 8.0;

    public static int GraceMs { get; set; } = 3600000;

    public static double FirstShare { get; set; } = 0.85;

    public static double FloorShare { get; set; } = 0.5;

    public static bool Calls { get; set; } = true;

    public static int CallMost { get; set; } = 5;

    public static int CallMostBig { get; set; } = 10;

    public static int CallGraceMs { get; set; } = 600000;

    public static double CalledLeastRestHours { get; set; } = 1.0;

    public static long Called { get; private set; }

    public static long Dismissed { get; private set; }

    public static int EarlyMinutes { get; set; } = 60;

    public static long Early { get; private set; }

    private static readonly List<(BotMobile Bot, Record Record, double Over)> _due = [];

    private static int _heldByFloor;

    private static readonly (string Name, double Share, double Least, double Most)[] Kinds =
    [
        ("an evening player", 0.30, 2.5, 4.0),
        ("a steady player", 0.35, 4.0, 6.0),
        ("a keen player", 0.20, 6.0, 8.0),
        ("a player who never stops", 0.15, 9.0, 12.0)
    ];

    internal sealed class Record
    {
        public int Kind = -1;

        public double Hours;

        public double PlayedMinutes;

        public DateTime RestUntil;

        public DateTime LeftAt;

        public int Sessions;

        public long TiredTick;

        public string CalledBy;

        public double OwedHours;

        public long CallOverTick;

        public string Inn;

        public int Paid;

        public DateTime BuffUntil;
    }

    private static readonly Dictionary<string, Record> _records = new(StringComparer.OrdinalIgnoreCase);

    internal static IReadOnlyDictionary<string, Record> Records => _records;

    private static RestTimer _timer;

    private static long _lookedTick;

    public static long Left { get; private set; }

    public static long LetGo { get; private set; }

    public static long Returned { get; private set; }

    public static long ReturnedHome { get; private set; }

    private static int _tired;
    private static int _heldWorking;
    private static int _heldCompany;
    private static int _heldFight;
    private static int _heldUnder;
    private static int _heldCell;
    private static int _heldDead;

    public static void Start()
    {
        _timer?.Stop();
        _lookedTick = Core.TickCount;
        _timer = new RestTimer(TimeSpan.FromMilliseconds(Math.Max(1000, LookMs)));
        _timer.Start();

        var dealt = Deal();
        var kinds = new int[Kinds.Length];

        foreach (var (_, record) in _records)
        {
            if (record.Kind >= 0 && record.Kind < Kinds.Length)
            {
                kinds[record.Kind]++;
            }
        }

        logger.Information(
            "Bots tire: {Evening} evening players (2.5–4h), {Steady} steady (4–6h), {Keen} keen (6–8h) and {Never} who never stop (9–12h), {Dealt} of them dealt just now — each finishes what it holds, leaves the world and rests {Least}–{Most}h; {Away} are resting now, and a tired bot still not free after {Grace} minutes is let go of its work",
            kinds[0],
            kinds[1],
            kinds[2],
            kinds[3],
            dealt,
            LeastRestHours,
            MostRestHours,
            BotPopulation.Away.Count,
            GraceMs / 60000
        );
    }

    private static int Deal()
    {
        List<(string Name, Record Record)> undealt = [];

        void Collect(IReadOnlyList<BotMobile> bots)
        {
            for (var i = 0; i < bots.Count; i++)
            {
                if (bots[i] is not { Deleted: false } bot || string.IsNullOrEmpty(bot.Name))
                {
                    continue;
                }

                if (!_records.TryGetValue(bot.Name, out var record))
                {
                    record = new Record();
                    _records[bot.Name] = record;
                    undealt.Add((bot.Name, record));
                }
                else if (record.Kind < 0 || record.Kind >= Kinds.Length)
                {
                    undealt.Add((bot.Name, record));
                }
            }
        }

        Collect(BotPopulation.Bots);
        Collect(BotPopulation.Away);

        if (undealt.Count == 0)
        {
            return 0;
        }

        var counts = new int[Kinds.Length];
        var given = 0;

        for (var k = 0; k < Kinds.Length; k++)
        {
            counts[k] = (int)Math.Floor(Kinds[k].Share * undealt.Count);
            given += counts[k];
        }

        while (given < undealt.Count)
        {
            var best = 0;
            var bestFraction = -1.0;

            for (var k = 0; k < Kinds.Length; k++)
            {
                var fraction = Kinds[k].Share * undealt.Count - counts[k];

                if (fraction > bestFraction)
                {
                    bestFraction = fraction;
                    best = k;
                }
            }

            counts[best]++;
            given++;
        }

        List<int> deck = [];

        for (var k = 0; k < Kinds.Length; k++)
        {
            for (var n = 0; n < counts[k]; n++)
            {
                deck.Add(k);
            }
        }

        for (var i = deck.Count - 1; i > 0; i--)
        {
            var j = Utility.Random(i + 1);
            (deck[i], deck[j]) = (deck[j], deck[i]);
        }

        for (var i = 0; i < undealt.Count; i++)
        {
            var (_, record) = undealt[i];
            var fresh = record.RestUntil == default;

            record.Kind = deck[i];
            record.Hours = Kinds[deck[i]].Least + (Kinds[deck[i]].Most - Kinds[deck[i]].Least) * Utility.RandomDouble();

            if (fresh)
            {
                record.PlayedMinutes = record.Hours * 60 * Utility.RandomDouble() * Math.Clamp(FirstShare, 0, 1);
            }
        }

        return undealt.Count;
    }

    private static int Draw()
    {
        var roll = Utility.RandomDouble();
        var sum = 0.0;

        for (var k = 0; k < Kinds.Length; k++)
        {
            sum += Kinds[k].Share;

            if (roll < sum)
            {
                return k;
            }
        }

        return Kinds.Length - 1;
    }

    public static void Stop()
    {
        _timer?.Stop();
        _timer = null;
    }

    private static string KindName(Record record) =>
        record.Kind >= 0 && record.Kind < Kinds.Length ? Kinds[record.Kind].Name : "a player";

    public static bool Resting(string name) =>
        Running && name != null && _records.TryGetValue(name, out var record) && record.RestUntil > Core.Now;

    private static Record Of(BotMobile bot)
    {
        if (!_records.TryGetValue(bot.Name, out var record))
        {
            record = new Record();
            _records[bot.Name] = record;
        }

        if (record.Kind < 0 || record.Kind >= Kinds.Length)
        {
            record.Kind = Draw();
            record.Hours = Kinds[record.Kind].Least + (Kinds[record.Kind].Most - Kinds[record.Kind].Least) * Utility.RandomDouble();
        }

        return record;
    }

    private static void Look()
    {
        var tick = Core.TickCount;

        var minutes = Math.Clamp((tick - _lookedTick) / 60000.0, 0, 2.0);

        _lookedTick = tick;

        _tired = 0;
        _heldWorking = 0;
        _heldCompany = 0;
        _heldFight = 0;
        _heldUnder = 0;
        _heldCell = 0;
        _heldDead = 0;

        var bots = BotPopulation.Bots;

        _due.Clear();
        _heldByFloor = 0;

        var tiredAlready = 0;
        var onCall = 0;

        for (var i = 0; i < bots.Count; i++)
        {
            var bot = bots[i];

            if (bot is not { Deleted: false } || string.IsNullOrEmpty(bot.Name) || bot.Map == null || bot.Map == Map.Internal)
            {
                continue;
            }

            var record = Of(bot);

            record.RestUntil = default;

            if (!Running)
            {
                bot.Tired = false;
                continue;
            }

            if (record.CalledBy != null)
            {
                onCall++;

                if (CallStands(record.CalledBy))
                {
                    record.CallOverTick = 0;
                    continue;
                }

                if (record.CallOverTick == 0)
                {
                    record.CallOverTick = tick;
                }

                if (!bot.Tired && tick - record.CallOverTick >= CallGraceMs)
                {
                    bot.Tired = true;
                    record.TiredTick = tick;

                    logger.Information(
                        "{Name} the {Class}: the fight {Guild} called it back for is over; it goes back to its rest when it is free",
                        bot.Name,
                        bot.Class?.Name,
                        record.CalledBy
                    );
                }

                continue;
            }

            record.PlayedMinutes = Math.Max(0.0, record.PlayedMinutes + BotCamp.Played(bot, minutes, record.Hours));

            var over = record.PlayedMinutes - record.Hours * 60;

            if (over < 0)
            {
                bot.Tired = false;
                record.TiredTick = 0;
                continue;
            }

            if (bot.Tired)
            {
                tiredAlready++;
            }
            else
            {
                _due.Add((bot, record, over));
            }
        }

        var island = BotPopulation.Count + BotPopulation.Away.Count;
        var allowance = BotPopulation.Count - onCall - (int)Math.Ceiling(Math.Clamp(FloorShare, 0, 1) * island) - tiredAlready;

        _due.Sort((a, b) => b.Over.CompareTo(a.Over));

        for (var i = 0; i < _due.Count; i++)
        {
            var (bot, record, _) = _due[i];

            if (allowance <= 0)
            {
                _heldByFloor++;
                continue;
            }

            allowance--;
            bot.Tired = true;
            record.TiredTick = tick;

            logger.Information(
                "{Name} the {Class}, {Kind}, is tired after {Played:0.0}h of play and takes on nothing new; it leaves when it is free",
                bot.Name,
                bot.Class?.Name,
                KindName(record),
                record.PlayedMinutes / 60.0
            );
        }

        for (var i = 0; i < bots.Count; i++)
        {
            var bot = bots[i];

            if (bot is not { Deleted: false, Tired: true } || bot.Map == null || bot.Map == Map.Internal
                || !_records.TryGetValue(bot.Name ?? "", out var record))
            {
                continue;
            }

            _tired++;

            if (record.TiredTick == 0)
            {
                record.TiredTick = tick;
            }

            var held = Holds(bot);

            if (held == null)
            {
                if (BotCamp.Beds(bot))
                {
                    Leave(bot, record, null);
                    continue;
                }

                if (BotRepose.HomeToRest(bot))
                {
                    continue;
                }

                if (BotInns.ToInn(bot, RestHours(record)))
                {
                    continue;
                }

                if (BotCamp.ToFire(bot))
                {
                    continue;
                }

                Leave(bot, record, null);
            }
            else if (tick - record.TiredTick >= GraceMs && held is "working" or "in a company")
            {
                Leave(bot, record, held);
            }
        }

        var away = BotPopulation.Away;
        var now = Core.Now;

        for (var i = away.Count - 1; i >= 0; i--)
        {
            var bot = away[i];

            if (bot is not { Deleted: false })
            {
                continue;
            }

            _records.TryGetValue(bot.Name ?? "", out var record);

            if (!Running || record == null || now >= record.RestUntil)
            {
                Return(bot, record);
            }
        }

        if (Running)
        {
            var present = BotPopulation.Count - onCall;
            var floor = (int)Math.Ceiling(Math.Clamp(FloorShare, 0, 1) * (BotPopulation.Count + away.Count));

            if (present < floor)
            {
                List<(BotMobile Bot, Record Record)> soon = [];

                for (var i = 0; i < away.Count; i++)
                {
                    if (away[i] is { Deleted: false } bot && _records.TryGetValue(bot.Name ?? "", out var record)
                        && record.RestUntil != default && (record.RestUntil - now).TotalMinutes <= EarlyMinutes)
                    {
                        soon.Add((bot, record));
                    }
                }

                soon.Sort((a, b) => a.Record.RestUntil.CompareTo(b.Record.RestUntil));

                for (var i = 0; i < soon.Count && present < floor; i++)
                {
                    var (bot, record) = soon[i];
                    var early = (record.RestUntil - now).TotalMinutes;

                    Return(bot, record, $"back {early:0} minutes early: the island was under its floor of {floor}");

                    present++;
                    Early++;
                }
            }
        }

        if (Running && Calls)
        {
            foreach (var war in BotWar.Standing)
            {
                BotRest.Call(BotGuilds.Named(war.A), $"its war with {war.B}");
                BotRest.Call(BotGuilds.Named(war.B), $"its war with {war.A}");
            }

            foreach (var bid in BotClaim.Bids)
            {
                if (bid.From != null && BotClaim.Important(bid.From, bid.Map, bid.Middle))
                {
                    BotRest.Call(BotGuilds.Named(bid.From), $"{bid.Guild} laying claim to its square at {bid.Middle.X},{bid.Middle.Y}");
                }
            }
        }

        BotGrowth.Look(now);
    }

    private static string Holds(BotMobile bot)
    {
        if (!bot.Alive || bot.Fallen)
        {
            _heldDead++;
            return "dead";
        }

        if (BotOutlaw.Jailed(bot))
        {
            _heldCell++;
            return "in a cell";
        }

        if (BotDelveParty.Delving(bot) || BotDungeon.Under(bot.Location))
        {
            _heldUnder++;
            return "underground";
        }

        if (BotDuel.Duelling(bot) || bot.Resolve.Standing is BotStanding.Failing or BotStanding.Hunted)
        {
            _heldFight++;
            return "in a fight";
        }

        if (bot.Squad != null)
        {
            _heldCompany++;
            return "in a company";
        }

        if (bot.Resolve.Deed != null)
        {
            _heldWorking++;
            return "working";
        }

        return null;
    }

    private static void Leave(BotMobile bot, Record record, string letGoOf)
    {
        if (letGoOf != null)
        {
            if (bot.Squad is { } company)
            {
                if (ReferenceEquals(company.Leader, bot))
                {
                    BotSquads.Disband(company, $"{bot.Name} was too tired to lead it");
                }
                else
                {
                    BotSquads.Leave(bot);
                }
            }

            if (bot.Resolve.Deed != null)
            {
                bot.Journey.Discard();
                BotWill.PutDown(bot, "too tired to go on");
            }

            LetGo++;
        }

        var where = bot.Location;
        var map = bot.Map;
        var played = record.PlayedMinutes / 60.0;
        var called = record.CalledBy;
        var rest = called != null
            ? Math.Max(CalledLeastRestHours, record.OwedHours)
            : LeastRestHours + Utility.RandomDouble() * Math.Max(0, MostRestHours - LeastRestHours);

        if (called != null)
        {
            record.CalledBy = null;
            record.OwedHours = 0;
            record.CallOverTick = 0;
            Dismissed++;
        }

        bot.Journey.Discard();
        bot.Combatant = null;
        bot.Tired = false;

        record.PlayedMinutes = 0;
        record.TiredTick = 0;
        record.Sessions++;
        record.LeftAt = Core.Now;
        record.RestUntil = Core.Now + TimeSpan.FromHours(rest);

        bot.LogoutLocation = where;
        bot.LogoutMap = map;

        var inn = BotInns.Settle(bot, out var paid);
        var from = inn?.Name;

        if (inn == null && BotAbode.Of(bot) is { Deleted: false } house && house.Map == map && house.IsInside(bot))
        {
            from = "its own house";
            paid = 1;
            BotInns.NotedAtHome();
        }

        var fire = inn == null && from == null ? BotCamp.Settle(bot) : null;

        if (fire != null)
        {
            from = fire;
        }

        record.Inn = from;
        record.Paid = paid;

        BotPopulation.Park(bot);
        bot.Internalize();

        Left++;

        logger.Information(
            "{Name} the {Class}, {Kind}, leaves the world at {Where}{Inn} after {Played:0.0}h of play{How} and rests {Rest:0.0}h, back about {Back:HH:mm}",
            bot.Name,
            bot.Class?.Name,
            KindName(record),
            where,
            from == null ? " on the open ground" : fire != null ? $" by {fire}, unpaid" : inn == null ? " in its own house" : paid > 0 ? $" in {inn.Name}, a bed paid for at {paid}gp" : $" in {inn.Name}, the bed unpaid",
            played,
            (called == null ? "" : $", the fight {called} called it back for being over")
            + (letGoOf == null ? "" : $", let go of what held it an hour ({letGoOf})"),
            rest,
            record.RestUntil.ToLocalTime()
        );
    }

    private static double RestHours(Record record)
    {
        if (record == null)
        {
            return LeastRestHours;
        }

        if (record.CalledBy != null)
        {
            return Math.Max(CalledLeastRestHours, record.OwedHours);
        }

        return (LeastRestHours + MostRestHours) / 2.0;
    }

    private static void Return(BotMobile bot, Record record, string call = null)
    {
        var rested = record == null || record.LeftAt == default ? 0.0 : (Core.Now - record.LeftAt).TotalHours;

        if (record != null)
        {
            record.RestUntil = default;
            record.PlayedMinutes = 0;
            record.TiredTick = 0;
        }

        bot.Tired = false;
        bot.Refusals = 0;
        bot.ReviveComplained = false;

        var own = BotPopulation.Unpark(bot);

        Returned++;

        if (!own)
        {
            ReturnedHome++;
        }

        var buff = "";

        if (record is { Paid: > 0, Inn: not null } && call == null && rested > 0.0)
        {
            BotInns.Rested(bot, rested, out var until);
            record.BuffUntil = until;
            buff = $", rested well at {record.Inn}: regenerating until {until.ToLocalTime():HH:mm}";
        }
        else if (call == null && rested > 0.0 && BotCamp.Rested(bot, record?.Inn, rested, out var fireUntil))
        {
            record.BuffUntil = fireUntil;
            buff = $", rested by {record.Inn}: regenerating until {fireUntil.ToLocalTime():HH:mm}";
        }
        else if (record is { BuffUntil: var kept } && kept > Core.Now)
        {
            bot.WellRestedUntil = kept;
        }

        if (record != null)
        {
            record.Inn = null;
            record.Paid = 0;
        }

        logger.Information(
            "{Name} the {Class} is back after {Rested:0.0}h of rest, at {Where}{Home}{Call}{Buff}",
            bot.Name,
            bot.Class?.Name,
            rested,
            bot.Location,
            own ? "" : " (the ground it left would not take a body, so at home)",
            call == null ? "" : $" - {call}",
            buff
        );
    }

    public static int Call(Guild guild, string why)
    {
        if (!Running || !Calls || guild?.Name == null || guild.Disbanded)
        {
            return 0;
        }

        var most = BotWar.Fighting(guild.Name) > 1 || BotWar.InBig(guild.Name) ? CallMostBig : CallMost;
        var onCall = 0;
        var bots = BotPopulation.Bots;

        for (var i = 0; i < bots.Count; i++)
        {
            if (bots[i] is { Deleted: false } bot && _records.TryGetValue(bot.Name ?? "", out var record)
                && string.Equals(record.CalledBy, guild.Name, StringComparison.OrdinalIgnoreCase))
            {
                onCall++;
            }
        }

        most = Math.Max(most, onCall + BotWar.FromRest(guild.Name));

        if (onCall >= most)
        {
            return 0;
        }

        List<(BotMobile Bot, Record Record)> resting = [];
        var away = BotPopulation.Away;

        for (var i = 0; i < away.Count; i++)
        {
            if (away[i] is { Deleted: false } bot && ReferenceEquals(bot.Guild, guild)
                && _records.TryGetValue(bot.Name ?? "", out var record) && record.CalledBy == null)
            {
                resting.Add((bot, record));
            }
        }

        resting.Sort((a, b) => a.Record.LeftAt.CompareTo(b.Record.LeftAt));

        var now = Core.Now;
        var came = 0;

        for (var i = 0; i < resting.Count && onCall + came < most; i++)
        {
            var (bot, record) = resting[i];

            record.OwedHours = record.RestUntil > now ? (record.RestUntil - now).TotalHours : 0.0;
            record.CalledBy = guild.Name;
            record.CallOverTick = 0;

            Return(bot, record, $"called back by {guild.Name}: {why}, owing {record.OwedHours:0.0}h of rest");

            came++;
            Called++;
        }

        if (came > 0)
        {
            logger.Warning(
                "{Guild} calls {Came} of its resting members back to fight - {Why}; {OnCall} on its call now, {Left} still resting",
                guild.Name,
                came,
                why,
                onCall + came,
                resting.Count - came
            );
        }

        return came;
    }

    private static bool CallStands(string guild) => BotWar.Fighting(guild) > 0 || BotClaim.Contested(guild);

    public static string Offline(BotMobile bot)
    {
        if (bot?.Name == null || !_records.TryGetValue(bot.Name, out var record) || record.RestUntil == default)
        {
            return null;
        }

        return record.Inn == null
            ? $"resting, back about {record.RestUntil.ToLocalTime():HH:mm}"
            : $"resting at {record.Inn}{(record.Paid > 0 ? "" : " (unpaid)")}, back about {record.RestUntil.ToLocalTime():HH:mm}";
    }

    public static string CalledBy(BotMobile bot) =>
        bot?.Name != null && _records.TryGetValue(bot.Name, out var record) ? record.CalledBy : null;

    public static void Forget() => _records.Clear();

    public static string Describe()
    {
        var soonest = DateTime.MaxValue;
        var away = BotPopulation.Away;

        for (var i = 0; i < away.Count; i++)
        {
            if (away[i] is { Deleted: false } bot && _records.TryGetValue(bot.Name ?? "", out var record)
                && record.RestUntil < soonest)
            {
                soonest = record.RestUntil;
            }
        }

        var next = soonest == DateTime.MaxValue ? "nobody resting" : $"the next back about {soonest.ToLocalTime():HH:mm}";

        var tiring = double.MaxValue;
        string tiringName = null;
        var bots = BotPopulation.Bots;

        for (var i = 0; i < bots.Count; i++)
        {
            if (bots[i] is { Deleted: false, Tired: false } bot && _records.TryGetValue(bot.Name ?? "", out var record)
                && record.Hours > 0 && record.Hours * 60 - record.PlayedMinutes < tiring)
            {
                tiring = record.Hours * 60 - record.PlayedMinutes;
                tiringName = bot.Name;
            }
        }

        var tires = tiringName == null ? "" : $"; {tiringName} tires next, in {Math.Max(0, tiring):0} minutes";

        return $"{BotPopulation.Count} in the world, {away.Count} resting ({next}{tires}); {_heldByFloor} past their hours and playing on so the island is not left under {FloorShare:P0}; {_tired} tired and not yet free "
               + $"({_heldWorking} finishing work, {_heldCompany} in a company, {_heldFight} in a fight, {_heldUnder} underground, "
               + $"{_heldCell} in a cell, {_heldDead} dead); this session {Left} left to rest ({LetGo} let go of what held them after "
               + $"{GraceMs / 60000} minutes) and {Returned} came back ({ReturnedHome} at home); {Called} called back from rest to fight "
               + $"and {Dismissed} gone back to it{(Calls ? "" : " (calls OFF)")}; {Early} back up to {EarlyMinutes} minutes early to hold the floor";
    }

    internal static void Save(IGenericWriter writer)
    {
        writer.WriteEncodedInt(_records.Count);

        foreach (var (name, record) in _records)
        {
            writer.Write(name);
            writer.WriteEncodedInt(record.Kind);
            writer.Write(record.Hours);
            writer.Write(record.PlayedMinutes);
            writer.Write(record.RestUntil);
            writer.Write(record.LeftAt);
            writer.WriteEncodedInt(record.Sessions);
            writer.Write(record.CalledBy ?? "");
            writer.Write(record.OwedHours);
            writer.Write(record.Inn ?? "");
            writer.WriteEncodedInt(record.Paid);
            writer.Write(record.BuffUntil);
        }
    }

    internal static int Load(IGenericReader reader, int shape)
    {
        _records.Clear();

        var count = reader.ReadEncodedInt();

        for (var i = 0; i < count; i++)
        {
            var name = reader.ReadString();
            var kind = shape >= 2 ? reader.ReadEncodedInt() : -1;
            var hours = shape >= 2 ? reader.ReadDouble() : 0.0;
            var record = new Record
            {
                Kind = kind,
                Hours = hours,
                PlayedMinutes = reader.ReadDouble(),
                RestUntil = reader.ReadDateTime(),
                LeftAt = reader.ReadDateTime(),
                Sessions = reader.ReadEncodedInt()
            };

            if (shape >= 3)
            {
                var called = reader.ReadString();

                record.CalledBy = string.IsNullOrEmpty(called) ? null : called;
                record.OwedHours = reader.ReadDouble();
            }

            if (shape >= 4)
            {
                var inn = reader.ReadString();

                record.Inn = string.IsNullOrEmpty(inn) ? null : inn;
                record.Paid = reader.ReadEncodedInt();
                record.BuffUntil = reader.ReadDateTime();
            }

            if (!string.IsNullOrEmpty(name))
            {
                _records[name] = record;
            }
        }

        return _records.Count;
    }

    private sealed class RestTimer : Timer
    {
        public RestTimer(TimeSpan interval) : base(interval, interval)
        {
        }

        protected override void OnTick() => Look();
    }
}
