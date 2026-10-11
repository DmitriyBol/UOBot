using System;
using System.Collections.Generic;
using Server.Guilds;
using Server.Items;
using Server.Logging;
using Server.Mobiles;

namespace Server.BotAI.V2;

/// <summary>
/// The town's side of crime: the thieves' roll, the reports a victim makes, the wanted list with a bounty on each head paid out
/// of the town's purse, the term in a cell, the guild's ransom and the repentance that brings a criminal back to honest life.
///
/// <para>
/// <b>Patrick's idea of 07.10.2026, approved for the night ("a livelier world").</b> "Thieves stealing from packs in the crowd
/// by the bank, ambushes on lone carriers with goods on the road, a fence, a wanted list: the town sets a bounty on a head,
/// bounty hunters from any guild, trial and jail when caught (time in a cell, a ransom by the guild), repentance and a return
/// to honest life. This gives a living reason for guards, escorts and caravan guards."
/// </para>
///
/// <para>
/// <b>Built on what the underworld already had.</b> The cells, the clock that lets a prisoner go, the engine's grey for the
/// wanted and the law-abiding who set on an outlaw in sight are <see cref="BotOutlaw"/>'s; the band and its fence are
/// <see cref="BotUnderworld"/>'s and <see cref="BotFence"/>'s; a beaten mark's choice is <see cref="BotQuarter"/>'s. This
/// class adds the record those did not keep — who was reported for what, in which town, what the town will pay for it — and
/// the four pieces of work in this folder: the cutpurse (<see cref="BotCutpurse"/>), the ambush (<see cref="BotHighway"/>),
/// the fence's trade (<see cref="BotFencer"/>) and the bounty (<see cref="BotBountyBoard"/>). Counted at the end of the
/// <c>Underworld:</c> line.
/// </para>
///
/// <para>
/// <b>Money only moves.</b> A theft moves coin from one pack to another by the engine's own Stealing; the fence pays the thief
/// out of its own purse; a bounty is paid out of the city's treasury (<see cref="BotCity.Reward"/>) and a ransom goes into it
/// (<see cref="BotCity.Tax"/>); the only new coin is a stolen thing sold over an NPC's counter, which is the one way the
/// shard has always minted it.
/// </para>
/// </summary>
public static class BotCrime
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotCrime));

    public static bool Running { get; set; } = true;

    public static int ThiefPerBots { get; set; } = 20;

    public static int RecruitEveryMs { get; set; } = 900000;

    public static int PoorBelow { get; set; } = 150;

    public static int RecruitLeastSkills { get; set; } = 2500;

    public static int ReportsToPost { get; set; } = 2;

    public static int BountyBase { get; set; } = 200;

    public static int BountyMost { get; set; } = 2000;

    public static int PostedMs { get; set; } = 21600000;

    public static double YieldShare { get; set; } = 0.25;

    public static bool Yields { get; set; } = true;

    public static int TermMs { get; set; } = 1800000;

    public static int MostDoublings { get; set; } = 3;

    public static bool Ransoms { get; set; } = true;

    public static int RansomBase { get; set; } = 300;

    public static int RansomPerMinute { get; set; } = 20;

    public static int RansomAfterMs { get; set; } = 300000;

    public static int ChestKeep { get; set; } = 500;

    public static int RepentMs { get; set; } = 7200000;

    public static int HotMs { get; set; } = 3600000;

    public static int SweepMs { get; set; } = 60000;

    public static long Recruited { get; private set; }

    public static long NoRecruit { get; private set; }

    public static long Reports { get; private set; }

    public static long PostedCount { get; private set; }

    public static long Lapsed { get; private set; }

    public static long Captured { get; private set; }

    public static long TakenInTown { get; private set; }

    public static long Yielded { get; private set; }

    public static long BountiesPaid { get; private set; }

    public static long BountyGold { get; private set; }

    public static long BountyShort { get; private set; }

    public static long Terms { get; private set; }

    public static long TermMinutes { get; private set; }

    public static long Doubled { get; private set; }

    public static long RansomsPaid { get; private set; }

    public static long RansomGold { get; private set; }

    public static long RansomUnpaid { get; private set; }

    public static long Released { get; private set; }

    public static long TurnedOut { get; private set; }

    public static long Repented { get; private set; }

    public static long Relapsed { get; private set; }

    public static long CriminalDeaths { get; private set; }

    /// <summary>One name's record in the town's books. Ticks are of uptime; nought means never.</summary>
    public sealed class Sheet
    {
        public string Name;

        public bool Thief;

        public int Reports;

        public bool Posted;

        public long PostedTick;

        public string Town;

        public string Why;

        public int Damage;

        public int Bounty;

        public int Raised;

        public int Served;

        public long CaughtTick;

        public long ReleasedTick;

        public long LastCrimeTick;

        public bool Ransomed;

        public int Traced;

        public int Thefts;

        public int Ambushes;
    }

    private static readonly Dictionary<string, Sheet> _sheets = new(StringComparer.Ordinal);

    /// <summary>One lot of stolen goods: what, how many, and where and from whom it was taken.</summary>
    public sealed class Hot
    {
        public Type Kind;

        public int Amount;

        public string Town;

        public string Victim;

        public long Tick;
    }

    private static readonly Dictionary<Serial, List<Hot>> _hot = [];

    private static readonly List<Sheet> _walk = [];

    private static long _sweptTick;

    private static long _recruitTick;

    private static bool _recruitClock;

    public static Sheet Of(string name, bool make = false)
    {
        if (string.IsNullOrEmpty(name))
        {
            return null;
        }

        if (_sheets.TryGetValue(name, out var sheet))
        {
            return sheet;
        }

        if (!make)
        {
            return null;
        }

        _sheets[name] = sheet = new Sheet { Name = name };

        return sheet;
    }

    public static IEnumerable<Sheet> All => _sheets.Values;

    public static string TownAt(Point3D where) => BotTowns.Of(where)?.Name ?? BotTowns.Nearest(where)?.Name ?? "the island";

    public static bool Posted(Mobile m) => Running && Of(m?.Name) is { Posted: true };

    public static int MostThieves => Math.Max(1, BotPopulation.Count / Math.Max(1, ThiefPerBots));

    public static bool IsThief(Mobile m)
    {
        if (!Running || m is not BotMobile { Deleted: false } bot)
        {
            return false;
        }

        if (BotUnderworld.Member(bot))
        {
            return !BotFence.Is(bot);
        }

        return Of(bot.Name) is { Thief: true };
    }

    public static int ThievesNow()
    {
        var count = 0;
        var bots = BotPopulation.Bots;

        for (var i = 0; i < bots.Count; i++)
        {
            if (bots[i] is { Deleted: false } bot && IsThief(bot))
            {
                count++;
            }
        }

        return count;
    }

    private static bool Fit(BotMobile bot) =>
        bot.Class is { } klass && klass.Role is not (BotRole.Producer or BotRole.Medic) && !klass.Unpaid && !klass.Leads
        && !klass.Neutral && klass is not (BotSage or BotArchitect) && bot.Squad == null;

    private static void Recruit(long now)
    {
        if (!BotUnderworld.Exists)
        {
            return;
        }

        var have = ThievesNow();
        var most = MostThieves;

        if (have >= most)
        {
            return;
        }

        BotMobile best = null;
        var poorest = int.MaxValue;
        var bots = BotPopulation.Bots;

        for (var i = 0; i < bots.Count; i++)
        {
            if (bots[i] is not { Deleted: false, Alive: true } bot || bot.Map == null || bot.Map == Map.Internal || IsThief(bot)
                || BotUnderworld.Member(bot) || BotOutlaw.Jailed(bot) || !Fit(bot) || BotGuilds.IsHead(bot)
                || Of(bot.Name) is { Posted: true } or { ReleasedTick: > 0 })
            {
                continue;
            }

            var wealth = BotYield.Wealth(bot);

            if (wealth >= PoorBelow || wealth >= poorest || bot.SkillsTotal < RecruitLeastSkills)
            {
                continue;
            }

            poorest = wealth;
            best = bot;
        }

        if (best == null)
        {
            NoRecruit++;

            return;
        }

        var sheet = Of(best.Name, true);

        sheet.Thief = true;
        Recruited++;

        logger.Information(
            "{Name} the {Class}, with {Gold}gp to its name, is recruited by The Shadow to work the crowds at the banks: {Have} thieves of {Most}",
            best.Name,
            best.Class?.Name,
            poorest,
            have + 1,
            most
        );

        BotVoice.Say(best, "local", $"Times are hard. The Shadow pays better than honest work.", true);
        BotEvents.Post("crime", best, $"recruited by The Shadow as a thief with {poorest}gp to its name");
    }

    public static void Report(BotMobile criminal, Mobile by, string town, string why, int damage, int weight = 1)
    {
        if (!Running || criminal is not { Deleted: false })
        {
            return;
        }

        var sheet = Of(criminal.Name, true);
        var now = Core.TickCount;

        sheet.Reports += Math.Max(1, weight);
        sheet.Damage += Math.Max(0, damage);
        sheet.LastCrimeTick = now;
        sheet.Town = town ?? sheet.Town;
        sheet.Why = why ?? sheet.Why;
        Reports++;

        if (sheet.ReleasedTick != 0)
        {
            sheet.ReleasedTick = 0;
            Relapsed++;
        }

        logger.Information(
            "{By} reports {Name} to {Town} for {Why}: {Reports} of {Need} reports, {Damage}gp taken all told",
            by?.Name ?? "somebody",
            criminal.Name,
            sheet.Town,
            sheet.Why,
            sheet.Reports,
            ReportsToPost,
            sheet.Damage
        );

        if (sheet.Posted)
        {
            sheet.Bounty = Math.Min(BountyMost, BountyBase + sheet.Damage) + sheet.Raised;
            BotOutlaw.Want(criminal, by?.Name ?? sheet.Town, sheet.Why);

            return;
        }

        if (sheet.Reports >= Math.Max(1, ReportsToPost))
        {
            Post(criminal, sheet, by);
        }
    }

    public static void Crime(Mobile criminal)
    {
        if (Of(criminal?.Name, true) is { } sheet)
        {
            sheet.LastCrimeTick = Core.TickCount;

            if (sheet.ReleasedTick != 0)
            {
                sheet.ReleasedTick = 0;
                Relapsed++;
            }
        }
    }

    private static void Post(BotMobile criminal, Sheet sheet, Mobile by)
    {
        sheet.Posted = true;
        sheet.PostedTick = Core.TickCount;
        sheet.Raised = 0;
        sheet.Bounty = Math.Min(BountyMost, BountyBase + sheet.Damage);
        PostedCount++;

        BotOutlaw.Want(criminal, by?.Name ?? sheet.Town, sheet.Why);

        logger.Information(
            "{Town} posts {Name} the {Class} as wanted for {Why}: {Bounty}gp on its head ({Base} and {Damage}gp taken), out of a treasury of {Purse}gp",
            sheet.Town,
            criminal.Name,
            criminal.Class?.Name,
            sheet.Why,
            sheet.Bounty,
            BountyBase,
            sheet.Damage,
            BotCity.Purse
        );

        if (by is BotMobile teller && teller.Alive)
        {
            BotVoice.Say(teller, "world", $"{sheet.Town} wants {criminal.Name} for {sheet.Why}: {sheet.Bounty} gold on its head!", true);
        }

        BotEvents.Post("crime", criminal, $"posted as wanted in {sheet.Town} for {sheet.Why}, {sheet.Bounty}gp on its head");
    }

    public static int Raise(string name, int gold, int most)
    {
        if (!Running || gold <= 0 || Of(name) is not { Posted: true } sheet)
        {
            return 0;
        }

        var added = Math.Min(gold, Math.Max(0, most - sheet.Bounty));

        if (added <= 0)
        {
            return 0;
        }

        sheet.Raised += added;
        sheet.Bounty += added;

        logger.Information("{Town}'s steward raises the bounty on {Name} by {Added}gp to {Bounty}gp", sheet.Town, name, added, sheet.Bounty);

        return added;
    }

    public static void Wanted(List<(BotMobile Bot, Sheet Sheet)> into)
    {
        into.Clear();

        if (!Running)
        {
            return;
        }

        var bots = BotPopulation.Bots;

        for (var i = 0; i < bots.Count; i++)
        {
            if (bots[i] is not { Deleted: false } bot || Of(bot.Name) is not { Posted: true } sheet || BotOutlaw.Jailed(bot))
            {
                continue;
            }

            into.Add((bot, sheet));
        }

        into.Sort((a, b) => b.Sheet.Bounty.CompareTo(a.Sheet.Bounty));
    }

    public static void Heat(Mobile holder, Type kind, int amount, string town, string victim)
    {
        if (holder == null || kind == null || amount <= 0 || kind == typeof(Gold))
        {
            return;
        }

        if (!_hot.TryGetValue(holder.Serial, out var list))
        {
            _hot[holder.Serial] = list = [];
        }

        list.Add(new Hot { Kind = kind, Amount = amount, Town = town, Victim = victim, Tick = Core.TickCount });
    }

    public static List<Hot> HotOf(Mobile holder) =>
        holder != null && _hot.TryGetValue(holder.Serial, out var list) && list.Count > 0 ? list : null;

    public static int HotAmount(Mobile holder, Type kind)
    {
        var list = HotOf(holder);
        var amount = 0;

        if (list == null)
        {
            return 0;
        }

        for (var i = 0; i < list.Count; i++)
        {
            if (list[i].Kind == kind)
            {
                amount += list[i].Amount;
            }
        }

        return amount;
    }

    public static void Cool(Mobile holder, Type kind, int amount, Mobile to = null)
    {
        var list = HotOf(holder);

        if (list == null || amount <= 0)
        {
            return;
        }

        for (var i = 0; i < list.Count && amount > 0; i++)
        {
            var lot = list[i];

            if (lot.Kind != kind || lot.Amount <= 0)
            {
                continue;
            }

            var part = Math.Min(lot.Amount, amount);

            lot.Amount -= part;
            amount -= part;

            if (to != null)
            {
                Heat(to, kind, part, lot.Town, lot.Victim);
            }
        }

        list.RemoveAll(l => l.Amount <= 0);

        if (list.Count == 0)
        {
            _hot.Remove(holder.Serial);
        }
    }

    public static int Worth(Type kind, int fallback = 1) => Math.Max(1, BotMarket.Worth(kind, Math.Max(1, fallback)));

    public static int HotWorth(Mobile holder)
    {
        var list = HotOf(holder);
        var worth = 0;

        if (list == null)
        {
            return 0;
        }

        for (var i = 0; i < list.Count; i++)
        {
            worth += list[i].Amount * Worth(list[i].Kind);
        }

        return worth;
    }

    public static Dictionary<Type, int> Census(Mobile body)
    {
        var count = new Dictionary<Type, int>();
        var pack = body?.Backpack;

        if (pack == null)
        {
            return count;
        }

        var bond = (body as BotMobile)?.Bond;
        var items = pack.Items;

        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];

            if (item is Gold gold)
            {
                count[typeof(Gold)] = count.GetValueOrDefault(typeof(Gold)) + gold.Amount;

                continue;
            }

            if (!BotShopkeep.Merchandise(item, bond))
            {
                continue;
            }

            var kind = item.GetType();

            count[kind] = count.GetValueOrDefault(kind) + Math.Max(1, item.Amount);
        }

        return count;
    }

    public static int Gained(Dictionary<Type, int> before, Dictionary<Type, int> after, List<(Type Kind, int Amount)> goods)
    {
        goods.Clear();
        var gold = 0;

        foreach (var (kind, amount) in after)
        {
            var more = amount - before.GetValueOrDefault(kind);

            if (more <= 0)
            {
                continue;
            }

            if (kind == typeof(Gold))
            {
                gold += more;
            }
            else
            {
                goods.Add((kind, more));
            }
        }

        return gold;
    }

    public static void Fell(BotMobile fallen, Mobile killer)
    {
        if (!Running || fallen == null)
        {
            return;
        }

        BotHighway.Fell(fallen, killer);

        var sheet = Of(fallen.Name);

        if (sheet == null || !sheet.Posted && !sheet.Thief && !IsThief(fallen))
        {
            return;
        }

        CriminalDeaths++;

        if (!sheet.Posted || BotOutlaw.Jailed(fallen) || fallen.Map is not { } map)
        {
            return;
        }

        if (killer is BaseGuard || BotOutlaw.Guarded(map, fallen.X, fallen.Y, fallen.Z))
        {
            if (BotOutlaw.Jail(fallen, killer))
            {
                TakenInTown++;

                logger.Information(
                    "{Name}, wanted in {Town}, was brought down in town by {Killer} and is taken to the cells",
                    fallen.Name,
                    sheet.Town,
                    killer?.Name ?? "the guards"
                );
            }
        }
    }

    public static bool Hunts(BotMobile by, Mobile fallen) =>
        Running && by?.Resolve?.Deed is BotBountyHunt hunt && ReferenceEquals(hunt.Foe, fallen);

    public static bool Yield(IBotWilful hunter, BotMobile foe)
    {
        if (!Running || !Yields || hunter?.Self is not { Deleted: false, Alive: true } by || foe is not { Deleted: false, Alive: true }
            || foe.HitsMax <= 0 || foe.Hits > foe.HitsMax * Math.Clamp(YieldShare, 0.05, 0.95) || !Posted(foe) || BotOutlaw.IsRed(foe)
            || BotOutlaw.Jailed(foe))
        {
            return false;
        }

        foe.Combatant = null;
        foe.Warmode = false;
        BotVoice.Say(foe, "local", $"Enough, {by.Name}! I yield. Take me to the cells.", true);

        if (!BotOutlaw.Jail(foe, by))
        {
            return false;
        }

        Yielded++;

        logger.Information("{Name} yields to {By} at {Hits} of {Max} and is taken to the cells alive", foe.Name, by.Name, foe.Hits, foe.HitsMax);

        return true;
    }

    public static int Term(BotMobile bot, int jailMs)
    {
        if (!Running || Of(bot?.Name) is not { } sheet || !sheet.Posted && !sheet.Thief && sheet.Traced == 0)
        {
            return jailMs;
        }

        var doublings = Math.Clamp(sheet.Served, 0, Math.Max(0, MostDoublings));
        var term = (long)Math.Max(60000, TermMs) << doublings;

        if (doublings > 0)
        {
            Doubled++;
        }

        if (BotOutlaw.IsRed(bot) || BotUnderworld.Of(bot.Name) is { Murders: > 0 })
        {
            term = Math.Max(term, jailMs);
        }

        return (int)Math.Min(int.MaxValue, term);
    }

    public static void Caught(BotMobile bot, Mobile by)
    {
        if (!Running || Of(bot?.Name) is not { } sheet || !sheet.Posted && !sheet.Thief && sheet.Traced == 0)
        {
            return;
        }

        var now = Core.TickCount;
        var term = BotOutlaw.LeftInCell(bot);

        sheet.Served++;
        sheet.CaughtTick = now;
        _asked.Remove(bot.Name);
        sheet.ReleasedTick = 0;
        sheet.Ransomed = false;
        Captured++;
        Terms++;
        TermMinutes += term;

        var bounty = sheet.Posted ? sheet.Bounty : 0;
        var town = sheet.Town ?? TownAt(bot.Location);

        sheet.Posted = false;
        sheet.Reports = 0;
        sheet.Damage = 0;
        sheet.Bounty = 0;
        sheet.Raised = 0;

        var paid = 0;

        if (bounty > 0 && by is BotMobile captor && !ReferenceEquals(captor, bot))
        {
            paid = BotCity.Reward(captor, bounty);

            if (paid > 0)
            {
                BountiesPaid++;
                BountyGold += paid;
                BotVoice.Say(captor, "local", $"{bot.Name} is taken. {town} owes me {paid} gold, and it has paid.", true);
            }

            if (paid < bounty)
            {
                BountyShort++;
            }
        }

        logger.Information(
            "{Name} the {Class} is caught by {By} for {Why} and serves {Minutes} minutes (term {Served}); the bounty of {Bounty}gp paid {Paid}gp out of {Town}'s purse, {Purse}gp left",
            bot.Name,
            bot.Class?.Name,
            by?.Name ?? "the guards",
            sheet.Why ?? "its crimes",
            term,
            sheet.Served,
            bounty,
            paid,
            town,
            BotCity.Purse
        );

        BotEvents.Post("crime", bot, $"caught by {by?.Name ?? "the guards"}: {term} minutes in a cell, bounty {paid}gp paid");
    }

    public static void Freed(BotMobile bot)
    {
        if (!Running || Of(bot?.Name) is not { CaughtTick: > 0 } sheet)
        {
            return;
        }

        var now = Core.TickCount;
        var served = (now - sheet.CaughtTick) / 60000;

        sheet.ReleasedTick = now;
        sheet.CaughtTick = 0;
        bot.Criminal = false;
        Released++;

        if (!sheet.Ransomed && bot.Guild is Guild guild && !BotUnderworld.Band(guild)
            && BotGuilds.Expel(guild, bot, "it served a term for its crimes and its guild paid no ransom"))
        {
            TurnedOut++;
        }

        logger.Information(
            "{Name} is let out after {Minutes} minutes in a cell{Ransom}; it is repentant if it keeps clean for {Hours} hours",
            bot.Name,
            served,
            sheet.Ransomed ? ", its ransom paid" : "",
            RepentMs / 3600000.0
        );

        BotVoice.Say(bot, "local", sheet.Ransomed ? "My guild paid for me. I will not forget it." : "Free again. Never again, I swear it.", true);
    }

    public static bool Unrepentant(BotMobile bot)
    {
        if (!Running || bot is not { Guild: null } || Of(bot.Name) is not { } sheet)
        {
            return false;
        }

        return sheet.Posted || sheet.Thief || sheet.CaughtTick > 0 || sheet.ReleasedTick > 0;
    }

    public static void Beat(long now)
    {
        if (!Running)
        {
            return;
        }

        BotHighway.Sweep(now);

        if (now - _sweptTick < SweepMs)
        {
            return;
        }

        _sweptTick = now;

        _walk.Clear();
        _walk.AddRange(_sheets.Values);

        for (var i = 0; i < _walk.Count; i++)
        {
            var sheet = _walk[i];
            var bot = Named(sheet.Name);

            if (sheet.Posted)
            {
                if (now - sheet.PostedTick >= PostedMs)
                {
                    sheet.Posted = false;
                    sheet.Reports = 0;
                    Lapsed++;

                    logger.Information("{Town}'s posting of {Name} lapses after {Hours} hours uncaught", sheet.Town, sheet.Name, PostedMs / 3600000.0);
                }
                else if (bot is { Alive: true } && !BotOutlaw.Jailed(bot) && !BotOutlaw.IsWanted(bot))
                {
                    BotOutlaw.Want(bot, sheet.Town, sheet.Why ?? "its crimes");
                }
            }

            if (sheet.CaughtTick > 0 && bot != null && BotOutlaw.Jailed(bot))
            {
                Ransom(bot, sheet, now);
            }
            else if (sheet.CaughtTick > 0 && bot != null && !BotOutlaw.Jailed(bot)
                     && now - sheet.CaughtTick > ((long)Math.Max(60000, TermMs) << Math.Clamp(MostDoublings, 0, 8)) + 600000)
            {
                Freed(bot);
            }

            if (sheet.ReleasedTick > 0 && now - sheet.ReleasedTick >= RepentMs && sheet.LastCrimeTick - sheet.ReleasedTick <= 0)
            {
                sheet.ReleasedTick = 0;
                sheet.Thief = false;
                Repented++;

                logger.Information("{Name} has kept clean for {Hours} hours since its term and is repentant: any guild may take it in again", sheet.Name, RepentMs / 3600000.0);

                if (bot is { Alive: true })
                {
                    BotVoice.Say(bot, "local", "I am done with the dark trades. An honest living for me now.", true);
                    BotEvents.Post("crime", bot, "repentant after its term: back to honest life");
                }
            }

            if (!sheet.Thief && !sheet.Posted && sheet.Reports == 0 && sheet.CaughtTick == 0 && sheet.ReleasedTick == 0
                && sheet.Served == 0 && sheet.Traced == 0 && now - sheet.LastCrimeTick >= RepentMs)
            {
                _sheets.Remove(sheet.Name);
            }
        }

        Cool(now);

        if (!_recruitClock)
        {
            _recruitClock = true;
            _recruitTick = now;
        }
        else if (now - _recruitTick >= RecruitEveryMs)
        {
            _recruitTick = now;
            Recruit(now);
        }
    }

    private static void Ransom(BotMobile bot, Sheet sheet, long now)
    {
        if (!Ransoms || sheet.Ransomed || _asked.Contains(bot.Name) || now - sheet.CaughtTick < RansomAfterMs || bot.Guild is not Guild guild
            || BotUnderworld.Band(guild))
        {
            return;
        }

        var left = BotOutlaw.LeftInCell(bot);

        if (left <= 1)
        {
            return;
        }

        var price = RansomBase + RansomPerMinute * left;

        _asked.Add(bot.Name);

        if (BotChest.Holds(guild.Name) < price + ChestKeep)
        {
            RansomUnpaid++;

            logger.Information(
                "{Guild} cannot ransom {Name} for {Price}gp: its chest holds {Chest}gp and keeps {Keep}gp back; it serves its {Left} minutes",
                guild.Name,
                bot.Name,
                price,
                BotChest.Holds(guild.Name),
                ChestKeep,
                left
            );

            return;
        }

        var drawn = BotChest.Draw(guild.Name, price);

        if (drawn < price)
        {
            BotChest.Return(guild.Name, drawn);
            RansomUnpaid++;

            return;
        }

        BotCity.Tax(drawn);
        sheet.Ransomed = true;
        RansomsPaid++;
        RansomGold += drawn;

        logger.Information(
            "{Guild} pays {Price}gp out of its chest into the town's purse to ransom {Name}, {Left} minutes early",
            guild.Name,
            price,
            bot.Name,
            left
        );

        BotEvents.Post("crime", bot, $"ransomed by {guild.Name} for {price}gp, {left} minutes early");
        BotOutlaw.Free(bot);
    }

    private static readonly HashSet<string> _asked = new(StringComparer.Ordinal);

    private static void Cool(long now)
    {
        List<Serial> cold = null;

        foreach (var (serial, list) in _hot)
        {
            list.RemoveAll(l => now - l.Tick >= HotMs || l.Amount <= 0);

            if (list.Count == 0)
            {
                (cold ??= []).Add(serial);
            }
        }

        if (cold == null)
        {
            return;
        }

        for (var i = 0; i < cold.Count; i++)
        {
            _hot.Remove(cold[i]);
        }
    }

    public static BotMobile Named(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return null;
        }

        var bots = BotPopulation.Bots;

        for (var i = 0; i < bots.Count; i++)
        {
            if (bots[i] is { Deleted: false } bot && bot.Name == name)
            {
                return bot;
            }
        }

        return null;
    }

    public static string Describe()
    {
        if (!Running)
        {
            return "crime: off";
        }

        var posted = 0;
        var thieves = 0;
        var owed = 0;

        foreach (var (_, sheet) in _sheets)
        {
            if (sheet.Posted)
            {
                posted++;
                owed += sheet.Bounty;
            }

            if (sheet.Thief)
            {
                thieves++;
            }
        }

        return $"crime: {ThievesNow()} thieves at work of {MostThieves} allowed ({thieves} recruits on the roll, {Recruited} recruited, {NoRecruit} looks found nobody poor and fit); {BotCutpurse.Describe()}; {BotHighway.Describe()}; {BotFencer.Describe()}; "
               + $"the wanted list: {Reports} reports, {PostedCount} posted, {posted} posted now with {owed}gp on their heads, {Lapsed} lapsed uncaught; {BotBountyBoard.Describe()}; "
               + $"the law: {Captured} caught ({TakenInTown} brought down in town, {Yielded} yielded alive), {BountiesPaid} bounties paid {BountyGold}gp out of the town's purse ({BountyShort} short), {Terms} terms for {TermMinutes} minutes ({Doubled} doubled for a repeat), {RansomsPaid} ransoms paid {RansomGold}gp into the purse and {RansomUnpaid} unpaid, {Released} let out ({TurnedOut} turned out of their guilds), {Repented} repented and {Relapsed} relapsed; {CriminalDeaths} criminals died";
    }

    public static void Forget()
    {
        _hot.Clear();
        _asked.Clear();
        Recruited = 0;
        NoRecruit = 0;
        Reports = 0;
        PostedCount = 0;
        Lapsed = 0;
        Captured = 0;
        TakenInTown = 0;
        Yielded = 0;
        BountiesPaid = 0;
        BountyGold = 0;
        BountyShort = 0;
        Terms = 0;
        TermMinutes = 0;
        Doubled = 0;
        RansomsPaid = 0;
        RansomGold = 0;
        RansomUnpaid = 0;
        Released = 0;
        TurnedOut = 0;
        Repented = 0;
        Relapsed = 0;
        CriminalDeaths = 0;
        _recruitClock = false;
        BotCutpurse.Forget();
        BotHighway.Forget();
        BotFencer.Forget();
        BotBountyBoard.Forget();
    }

    public static int Wipe()
    {
        var names = _sheets.Count;

        _sheets.Clear();
        _walk.Clear();
        Forget();

        return names;
    }

    internal static void Save(IGenericWriter writer)
    {
        var now = Core.TickCount;

        writer.WriteEncodedInt(_sheets.Count);

        foreach (var (_, s) in _sheets)
        {
            writer.Write(s.Name);
            writer.Write(s.Thief);
            writer.WriteEncodedInt(s.Reports);
            writer.Write(s.Posted);
            writer.Write(Ago(s.PostedTick, now));
            writer.Write(s.Town ?? "");
            writer.Write(s.Why ?? "");
            writer.WriteEncodedInt(s.Damage);
            writer.WriteEncodedInt(s.Bounty);
            writer.WriteEncodedInt(s.Served);
            writer.Write(Ago(s.CaughtTick, now));
            writer.Write(Ago(s.ReleasedTick, now));
            writer.Write(Ago(s.LastCrimeTick, now));
            writer.Write(s.Ransomed);
            writer.WriteEncodedInt(s.Traced);
            writer.WriteEncodedInt(s.Thefts);
            writer.WriteEncodedInt(s.Ambushes);
        }
    }

    internal static int Load(IGenericReader reader)
    {
        var now = Core.TickCount;
        var count = reader.ReadEncodedInt();

        _sheets.Clear();

        for (var i = 0; i < count; i++)
        {
            var s = new Sheet
            {
                Name = reader.ReadString(),
                Thief = reader.ReadBool(),
                Reports = reader.ReadEncodedInt(),
                Posted = reader.ReadBool(),
                PostedTick = Back(reader.ReadLong(), now),
                Town = reader.ReadString(),
                Why = reader.ReadString(),
                Damage = reader.ReadEncodedInt(),
                Bounty = reader.ReadEncodedInt(),
                Served = reader.ReadEncodedInt(),
                CaughtTick = Back(reader.ReadLong(), now),
                ReleasedTick = Back(reader.ReadLong(), now),
                LastCrimeTick = Back(reader.ReadLong(), now),
                Ransomed = reader.ReadBool(),
                Traced = reader.ReadEncodedInt(),
                Thefts = reader.ReadEncodedInt(),
                Ambushes = reader.ReadEncodedInt()
            };

            if (string.IsNullOrEmpty(s.Town))
            {
                s.Town = null;
            }

            if (string.IsNullOrEmpty(s.Why))
            {
                s.Why = null;
            }

            if (!string.IsNullOrEmpty(s.Name))
            {
                _sheets[s.Name] = s;
            }
        }

        return _sheets.Count;
    }

    private static long Ago(long tick, long now) => tick == 0 ? -1L : Math.Max(0L, now - tick);

    private static long Back(long ago, long now)
    {
        if (ago < 0)
        {
            return 0;
        }

        var tick = now - ago;

        return tick == 0 ? 1 : tick;
    }
}
