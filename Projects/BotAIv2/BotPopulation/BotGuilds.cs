using System;
using System.Collections.Generic;
using Server.Guilds;
using Server.Items;
using Server.Logging;
using Server.Mobiles;

namespace Server.BotAI.V2;

/// <summary>
/// Who each bot belongs to, and what that belonging is worth.
///
/// <para>
/// <b>Ordered by Patrick on 08.09.2026: something that makes them pull together.</b> A guild on this shard
/// is not decoration — the engine already treats guildmates as <c>Notoriety.Ally</c>, so it is the one
/// standing statement the world itself understands about which bots are on the same side.
/// </para>
///
/// <para>
/// <b>Rewritten the same night, and the rewrite is the interesting half.</b> The first version sorted the
/// population by trade: every fighter into one guild, every maker into another. That is a caste system
/// rather than a society, and it produced exactly what a caste system produces — the Blade held thirty-five
/// of forty-nine bots and the Needle held one. Patrick's rules replace it:
/// </para>
///
/// <list type="bullet">
/// <item>a guild is formed only if it will have at least <see cref="Least"/> members;</item>
/// <item>at least one of them must be able to make things — see <see cref="Makers"/>;</item>
/// <item>no guild may hold more than <see cref="Most"/>.</item>
/// </list>
///
/// <para>
/// <b>What those three rules make is a band rather than a class.</b> Every guild ends up with fighters, a
/// healer or two and a maker of its own, because the roster is dealt out by role rather than by trade. That
/// is a social circle: a group small enough that everybody in it is somebody you keep meeting, and mixed
/// enough that it can look after itself. It is also the unit the rest of this project's plans need — a
/// guild that owns a hall, quarrels with its neighbours and equips its own can only be a mixed band.
/// </para>
///
/// <para>
/// <b>And it is a preference, never a condition.</b> That distinction is the one this project has paid for
/// most often — a rule that reads "only with my own" is a veto, and a veto here would split the population
/// into groups that cannot help each other, which is the same defect as a muster that could find nobody. So
/// guild membership multiplies what help is worth; it never refuses it. See <see cref="Kinship"/>.
/// </para>
///
/// <para>
/// <b>Patrick's order of 21.09.2026: anybody may found a guild.</b> Until then a guild existed only if the
/// boot's muster dealt one out around a crafter, and the only door in afterwards was a leader's mind taking
/// somebody on — and the minds were switched off on 18.09, so a bot outside a guild stayed outside for ever.
/// Founding is now something a bot does while the shard runs: see <see cref="Gather"/>. The rule of five and
/// the ceiling of fifteen stand; the rule that one of the five must be a maker is the one that was lifted,
/// and with it the guild's fixed point moved from its maker to its <see cref="Head"/>.
/// </para>
/// </summary>
public static class BotGuilds
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotGuilds));

    public static bool Running { get; set; } = true;

    public static double Kinship { get; set; } = 1.35;

    public static int Least { get; set; } = 5;

    public static int Most { get; set; } = 15;

    public static int WidenBy { get; set; } = 10;

    public static int WidenPrice { get; set; } = 10000;

    public static int MostWidenings { get; set; } = 3;

    public static long Widenings { get; private set; }

    public static long Unwidened { get; private set; }

    private static readonly Dictionary<string, int> _widened = new(StringComparer.OrdinalIgnoreCase);

    public static int Widened(Guild guild) =>
        guild?.Name != null && _widened.TryGetValue(guild.Name, out var times) ? times : 0;

    public static int Ceiling(Guild guild) => Math.Min(Most + Math.Max(0, WidenBy) * Widened(guild), Math.Max(Most, BotHallKind.Holds(guild)));

    public static long Cramped { get; private set; }

    private static bool Widen(Guild guild, string why)
    {
        if (guild?.Name == null || guild.Disbanded || WidenBy <= 0 || WidenPrice < 0 || Widened(guild) >= MostWidenings)
        {
            return false;
        }

        if (Most + WidenBy * (Widened(guild) + 1) > BotHallKind.Holds(guild))
        {
            Cramped++;

            return false;
        }

        if (BotEstate.Fund(guild) < WidenPrice)
        {
            Unwidened++;

            return false;
        }

        var paid = new List<BotEstate.Contribution>();
        var got = BotEstate.Levy(guild, WidenPrice, paid);

        if (got < WidenPrice)
        {
            BotEstate.Refund(paid);
            Unwidened++;

            return false;
        }

        _widened[guild.Name] = Widened(guild) + 1;
        Widenings++;

        logger.Information(
            "{Guild} has widened to {Ceiling} places for {Price}gp raised off its chest and members — {Why}",
            guild.Name,
            Ceiling(guild),
            WidenPrice,
            why ?? "no reason given"
        );

        return true;
    }

    internal static void SaveWidenings(IGenericWriter writer)
    {
        writer.WriteEncodedInt(_widened.Count);

        foreach (var (name, times) in _widened)
        {
            writer.Write(name);
            writer.WriteEncodedInt(times);
        }
    }

    internal static int LoadWidenings(IGenericReader reader)
    {
        _widened.Clear();

        var count = reader.ReadEncodedInt();

        for (var i = 0; i < count; i++)
        {
            var name = reader.ReadString();
            var times = reader.ReadEncodedInt();

            if (!string.IsNullOrEmpty(name) && times > 0)
            {
                _widened[name] = times;
            }
        }

        return _widened.Count;
    }

    public static int Band { get; set; } = 10;

    public static readonly string[] Makers = ["Crafter"];

    public static readonly string[] Barred = ["Baron", "Captain", "Architect", "Sage", "Brawler"];

    public static long Excused { get; private set; }

    public static bool Outside(BotMobile bot)
    {
        if (BotUnderworld.Outlawed(bot))
        {
            return true;
        }

        var klass = bot?.Class?.Name;

        if (klass == null)
        {
            return false;
        }

        for (var i = 0; i < Barred.Length; i++)
        {
            if (string.Equals(klass, Barred[i], StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static readonly (string Name, string Abbrev)[] Names =
    [
        ("The Hammer", "HAM"),
        ("The Blade", "BLD"),
        ("The Crown", "CRN"),
        ("The Needle", "NDL"),
        ("The Lantern", "LAN"),
        ("The Anchor", "ANC"),
        ("The Wolf", "WLF"),
        ("The Ash", "ASH")
    ];

    public static long Enrolled { get; private set; }

    public static long Unplaced { get; private set; }

    public static int Count => _guilds.Count;

    public static IEnumerable<Guild> Standing => _guilds.Values;

    public static bool Leads(BotMobile bot) => bot?.Guild is Guild guild && ReferenceEquals(Head(guild), bot);

    public static string Lacking(BotRole role, int least)
    {
        foreach (var guild in _guilds.Values)
        {
            var members = 0;

            for (var i = 0; i < guild.Members.Count; i++)
            {
                if (guild.Members[i] is BotMobile { Deleted: false })
                {
                    members++;
                }
            }

            if (members >= least && OfRole(guild, role) == 0)
            {
                return guild.Name;
            }
        }

        return null;
    }

    public static Guild Named(string name) => name == null ? null : _guilds.GetValueOrDefault(name);

    public static Guild Find(string name) =>
        !string.IsNullOrEmpty(name) && _guilds.TryGetValue(name, out var guild) && !guild.Disbanded
            ? guild
            : null;

    private static readonly Dictionary<string, Guild> _guilds = [];

    private static bool _dealt;

    public static bool IsMaker(BotMobile bot)
    {
        var klass = bot?.Class?.Name;

        if (klass == null)
        {
            return false;
        }

        for (var i = 0; i < Makers.Length; i++)
        {
            if (string.Equals(klass, Makers[i], StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    public static void Muster()
    {
        if (!Running)
        {
            return;
        }

        var roster = BotPopulation.Bots;
        var kept = 0;
        var free = 0;

        for (var i = 0; i < roster.Count; i++)
        {
            var bot = roster[i];

            if (bot is not { Deleted: false } || bot.Class == null)
            {
                continue;
            }

            if (Outside(bot))
            {
                Excused++;

                continue;
            }

            if (bot.Guild is not Guild { Disbanded: false } guild || !Ours(guild.Name))
            {
                free++;

                continue;
            }

            _guilds[guild.Name] = guild;

            Show(bot);
            Joined(bot);
            kept++;
        }

        var resting = 0;
        var away = BotPopulation.Away;

        for (var i = 0; i < away.Count; i++)
        {
            if (away[i] is not { Deleted: false } bot || bot.Class == null || Outside(bot)
                || bot.Guild is not Guild { Disbanded: false } guild || !Ours(guild.Name))
            {
                continue;
            }

            _guilds[guild.Name] = guild;
            Joined(bot);
            resting++;
        }

        foreach (var guild in _guilds.Values)
        {
            Stone(guild);

            for (var i = guild.Members.Count - 1; i >= 0; i--)
            {
                if (guild.Members[i] is not { Deleted: false })
                {
                    guild.RemoveMember(guild.Members[i]);
                }
            }
        }

        _dealt = true;
        _gathered = Core.TickCount;
        _cursor = roster.Count == 0 ? 0 : Utility.Random(roster.Count);

        logger.Information(
            "Guilds mustered: {Guilds} came back from the world save holding {Kept} bots and {Resting} resting members, {Free} bots belong to none and {Excused} stand outside the bands by class — {What}",
            _guilds.Count,
            kept,
            resting,
            free,
            Excused,
            Describe()
        );

        logger.Information(
            "Guild membership: founding is {Founding} — every {Gather}s one bot without a guild joins the smallest guild under {Band}, or founds its own with the {Fellows} nearest bots without one if no guild is under {Band}, {Names} names to go round, {Most} the ceiling; a member gives a guild with nothing to show {Patience} minutes before it may leave, one a minute at most, {Defecting}, and is then barred from joining any guild for {Rejoin} hours, though not from founding one; a leader may put one member out and take one bot on every {Roster} minutes, and may not take back somebody who walked out of its own guild; the head of a guild neither leaves nor is put out",
            Founding ? "ON" : "OFF",
            GatherMs / 1000,
            Band,
            Least - 1,
            Band,
            Names.Length,
            Most,
            Patience / 60000,
            Defecting ? "and only for a guild that has something to show and room" : "into no guild at all",
            RejoinMs / 3600000,
            BotRoster.EveryMs / 60000
        );
    }

    private static bool Ours(string name)
    {
        for (var i = 0; i < Names.Length; i++)
        {
            if (string.Equals(Names[i].Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    public static void Stone(Guild guild)
    {
        if (guild is not { Disbanded: false } || Guild.NewGuildSystem || guild.Guildstone is { Deleted: false })
        {
            return;
        }

        guild.Guildstone = new Guildstone(guild);
        Stones++;
    }

    public static long Stones { get; private set; }

    public static bool Founding { get; set; } = true;

    public static int GatherMs { get; set; } = 5000;

    public static long Founded { get; private set; }

    public static long Gathered { get; private set; }

    public static long TooFew { get; private set; }

    public static long Nameless { get; private set; }

    public static long Barred48 { get; private set; }

    private static long _gathered;

    private static int _cursor;

    public static BotMobile Head(Guild guild) =>
        guild is { Disbanded: false } && guild.Leader is BotMobile { Deleted: false } head ? head : null;

    public static bool IsHead(BotMobile bot) =>
        bot?.Guild is Guild guild && ReferenceEquals(Head(guild), bot);

    private static bool Loose(BotMobile bot) =>
        bot is { Deleted: false, Alive: true, Guild: null, Class: not null }
        && bot.Map != null
        && bot.Map != Map.Internal
        && !Outside(bot);

    public static void Gather()
    {
        if (!Running || !Founding || !_dealt)
        {
            return;
        }

        var now = Core.TickCount;

        if (now - (_gathered + GatherMs) < 0)
        {
            return;
        }

        _gathered = now;

        List<string> dead = null;

        foreach (var (name, guild) in _guilds)
        {
            if (guild.Disbanded)
            {
                (dead ??= []).Add(name);
            }
        }

        if (dead != null)
        {
            for (var i = 0; i < dead.Count; i++)
            {
                _guilds.Remove(dead[i]);

                logger.Information("{Guild} is no more: its last member is gone, and the name is free to be taken again", dead[i]);
            }
        }

        var roster = BotPopulation.Bots;
        BotMobile bot = null;

        for (var looked = 0; looked < roster.Count && bot == null; looked++)
        {
            var next = roster[_cursor++ % roster.Count];

            if (Loose(next))
            {
                bot = next;
            }
        }

        if (bot == null)
        {
            return;
        }

        var cooling = Cools(bot);

        if (!cooling && Roomiest(Band, bot) is { } small)
        {
            Admit(small, bot);

            return;
        }

        if (FreeName(out var named))
        {
            var fellows = Nearest(bot, Least - 1);

            if (fellows.Count >= Least - 1)
            {
                List<BotMobile> band = [bot];

                band.AddRange(fellows);
                Form(named, band);
                Founded++;

                var names = new string[fellows.Count];

                for (var i = 0; i < fellows.Count; i++)
                {
                    names[i] = $"{fellows[i].Name} the {fellows[i].Class?.Name}";
                }

                logger.Information(
                    "{Founder} the {Class} has founded {Guild} [{Abbrev}] at ({X}, {Y}) with {Fellows} — {Count} guilds stand now",
                    bot.Name,
                    bot.Class?.Name,
                    named.Name,
                    named.Abbrev,
                    bot.X,
                    bot.Y,
                    string.Join(", ", names),
                    _guilds.Count
                );

                return;
            }

            TooFew++;
        }
        else
        {
            Nameless++;
        }

        if (cooling)
        {
            Barred48++;

            return;
        }

        if (Roomiest(0) is { } any)
        {
            Admit(any, bot);

            return;
        }

        if (Widest() is { } widened && Widen(widened, $"{bot.Name} the {bot.Class?.Name} had nowhere else to go"))
        {
            Admit(widened, bot);

            return;
        }

        Unplaced++;
    }

    private static Guild Roomiest(int under) => Roomiest(under, null);

    private static Guild Roomiest(int under, BotMobile bot)
    {
        Guild best = null;
        var bestShare = double.MaxValue;
        var bestCount = int.MaxValue;
        var role = bot?.Class?.Role;

        foreach (var guild in _guilds.Values)
        {
            if (guild.Disbanded || guild.Members.Count >= (under > 0 ? under : Ceiling(guild)))
            {
                continue;
            }

            var count = guild.Members.Count;
            var share = role == null || count == 0 ? 0.0 : (double)OfRole(guild, role.Value) / count;

            if (share < bestShare - 0.001 || Math.Abs(share - bestShare) <= 0.001 && count < bestCount)
            {
                best = guild;
                bestShare = share;
                bestCount = count;
            }
        }

        return best;
    }

    private static int OfRole(Guild guild, BotRole role)
    {
        var n = 0;

        for (var i = 0; i < guild.Members.Count; i++)
        {
            if (guild.Members[i] is BotMobile { Deleted: false } member && member.Class?.Role == role)
            {
                n++;
            }
        }

        return n;
    }

    private static Guild Widest()
    {
        Guild best = null;
        var bestFund = -1;

        foreach (var guild in _guilds.Values)
        {
            if (guild.Disbanded || Widened(guild) >= MostWidenings || guild.Members.Count < Ceiling(guild))
            {
                continue;
            }

            var fund = BotEstate.Fund(guild);

            if (fund > bestFund)
            {
                bestFund = fund;
                best = guild;
            }
        }

        return best;
    }

    private static bool FreeName(out (string Name, string Abbrev) named)
    {
        for (var i = 0; i < Names.Length; i++)
        {
            if (!_guilds.TryGetValue(Names[i].Name, out var guild) || guild.Disbanded)
            {
                named = Names[i];

                return true;
            }
        }

        named = default;

        return false;
    }

    private static List<BotMobile> Nearest(BotMobile founder, int many)
    {
        List<BotMobile> found = [];
        var roster = BotPopulation.Bots;

        for (var i = 0; i < roster.Count; i++)
        {
            var other = roster[i];

            if (!ReferenceEquals(other, founder) && Loose(other) && other.Map == founder.Map)
            {
                found.Add(other);
            }
        }

        found.Sort((a, b) => founder.GetDistanceToSqrt(a.Location).CompareTo(founder.GetDistanceToSqrt(b.Location)));

        List<BotMobile> band = [];
        List<BotRole> roles = [];

        if (founder.Class != null)
        {
            roles.Add(founder.Class.Role);
        }

        for (var i = 0; i < found.Count && band.Count < many; i++)
        {
            var role = found[i].Class?.Role;

            if (role != null && !roles.Contains(role.Value))
            {
                roles.Add(role.Value);
                band.Add(found[i]);
            }
        }

        for (var i = 0; i < found.Count && band.Count < many; i++)
        {
            if (!band.Contains(found[i]))
            {
                band.Add(found[i]);
            }
        }

        return band;
    }

    private static void Admit(Guild guild, BotMobile bot)
    {
        guild.AddMember(bot);
        Show(bot);
        Joined(bot);
        Gathered++;

        logger.Information(
            "{Bot} the {Class} has joined {Guild}, {Count} of {Most} now, under {Head}",
            bot.Name,
            bot.Class?.Name,
            guild.Name,
            guild.Members.Count,
            Ceiling(guild),
            Head(guild)?.Name ?? "nobody"
        );
    }

    private static void Form((string Name, string Abbrev) named, List<BotMobile> band)
    {
        if (band == null || band.Count == 0)
        {
            return;
        }

        var leader = band[0];
        var guild = BaseGuild.FindByName(named.Name) as Guild;

        if (guild == null || guild.Disbanded)
        {
            guild = new Guild(leader, named.Name, named.Abbrev)
            {
                Type = GuildType.Regular,
                Charter = "A company of the island's own."
            };
        }
        else
        {
            guild.Leader = leader;

            for (var i = guild.Members.Count - 1; i >= 0; i--)
            {
                if (guild.Members[i] is not BotMobile { Deleted: false })
                {
                    guild.RemoveMember(guild.Members[i]);
                }
            }
        }

        _guilds[named.Name] = guild;

        Stone(guild);

        BotSeat.Choose(named.Name, leader.Map);

        for (var i = 0; i < band.Count; i++)
        {
            guild.AddMember(band[i]);
            Show(band[i]);
            Joined(band[i]);
            Enrolled++;
        }

        BotUnderworld.EnemyOfAll();
    }

    private static void Show(Mobile bot)
    {
        if (bot is { Deleted: false })
        {
            bot.DisplayGuildTitle = true;
        }
    }

    public static void Enrol(BotMobile bot)
    {
        if (!Running || !_dealt || bot is not { Deleted: false } || bot.Guild != null)
        {
            return;
        }

        if (Outside(bot))
        {
            Excused++;

            return;
        }

        if (Cools(bot))
        {
            Cooling++;

            return;
        }

        Guild smallest = null;

        foreach (var guild in _guilds.Values)
        {
            if (guild.Disbanded || guild.Members.Count >= Ceiling(guild))
            {
                continue;
            }

            if (smallest == null || guild.Members.Count < smallest.Members.Count)
            {
                smallest = guild;
            }
        }

        if (smallest == null)
        {
            Unplaced++;

            return;
        }

        smallest.AddMember(bot);
        Show(bot);
        Joined(bot);
        Enrolled++;
    }

    public static bool Leaving { get; set; } = true;

    public static int Patience { get; set; } = 1800000;

    public static int RejoinMs { get; set; } = 172800000;

    public static int ReviewMs { get; set; } = 60000;

    public static long Walked { get; private set; }

    public static long Cooling { get; private set; }

    private static readonly Dictionary<Serial, (long Tick, string Guild)> _quit = [];

    private static readonly Dictionary<Serial, long> _joined = [];

    private static long _reviewed;

    private static int _turn;

    public static bool Cools(Mobile bot) =>
        bot != null && _quit.TryGetValue(bot.Serial, out var when) && Core.TickCount - (when.Tick + RejoinMs) < 0;

    public static bool Spurned(Mobile bot, Guild guild) =>
        bot != null
        && guild != null
        && _quit.TryGetValue(bot.Serial, out var when)
        && string.Equals(when.Guild, guild.Name, StringComparison.OrdinalIgnoreCase)
        && Core.TickCount - (when.Tick + RejoinMs) < 0;

    public static int Worth(Guild guild)
    {
        if (guild == null)
        {
            return 0;
        }

        var worth = BotEstate.Hall(guild) is { Deleted: false } ? 2 : 0;

        worth += BotClaim.Holds(guild.Name);

        if (BotGuildHouses.Held(guild))
        {
            worth++;
        }

        if (BotEstate.Fund(guild) >= BotEstate.Price / 2)
        {
            worth++;
        }

        return worth;
    }

    public static bool Defecting { get; set; } = true;

    public static long Crossed { get; private set; }

    public static long Nowhere { get; private set; }

    private static Guild Worthier(Guild than)
    {
        Guild best = null;
        var bestWorth = 0;

        foreach (var other in _guilds.Values)
        {
            if (other == null || ReferenceEquals(other, than) || other.Disbanded || other.Members.Count >= Ceiling(other))
            {
                continue;
            }

            if (than.Enemies != null && than.Enemies.Contains(other))
            {
                continue;
            }

            var worth = Worth(other);

            if (worth <= 0)
            {
                continue;
            }

            if (best == null || worth > bestWorth || (worth == bestWorth && other.Members.Count < best.Members.Count))
            {
                best = other;
                bestWorth = worth;
            }
        }

        return best;
    }

    public static void Review()
    {
        if (!Running || !Leaving || _guilds.Count == 0)
        {
            return;
        }

        var now = Core.TickCount;

        if (now - (_reviewed + ReviewMs) < 0)
        {
            return;
        }

        _reviewed = now;

        List<Guild> bands = [.. _guilds.Values];

        if (bands.Count == 0)
        {
            return;
        }

        var guild = bands[_turn++ % bands.Count];

        if (guild == null || guild.Disbanded || Worth(guild) > 0)
        {
            return;
        }

        var better = Defecting ? Worthier(guild) : null;

        if (Defecting && better == null)
        {
            Nowhere++;

            return;
        }

        var members = guild.Members;

        for (var i = 0; i < members.Count; i++)
        {
            if (members[i] is not BotMobile { Deleted: false, Alive: true } bot || IsHead(bot))
            {
                continue;
            }

            if (!_joined.TryGetValue(bot.Serial, out var since) || now - (since + Patience) < 0)
            {
                continue;
            }

            if (Cools(bot))
            {
                continue;
            }

            if (better != null)
            {
                if (Spurned(bot, better))
                {
                    continue;
                }

                better.AddMember(bot);
                Show(bot);

                _quit[bot.Serial] = (now, guild.Name);
                Joined(bot);
                Walked++;
                Crossed++;

                logger.Information(
                    "{Bot} the {Class} has left {Guild} for {Better}: it has no hall, no ground and no purse to speak of after {Minutes} minutes, and {Better} is {Showing} with {Count} of {Most}",
                    bot.Name,
                    bot.Class?.Name,
                    guild.Name,
                    better.Name,
                    (now - since) / 60000,
                    better.Name,
                    Worth(better) >= 2 ? "holding a hall" : "holding ground or a purse",
                    better.Members.Count,
                    Ceiling(better)
                );

                return;
            }

            guild.RemoveMember(bot);
            bot.DisplayGuildTitle = false;

            _quit[bot.Serial] = (now, guild.Name);
            _joined.Remove(bot.Serial);
            Walked++;

            logger.Information(
                "{Bot} has left {Guild}: it has no hall, no ground and no purse to speak of, and {Bot} had given it {Minutes} minutes",
                bot.Name,
                guild.Name,
                bot.Name,
                (now - since) / 60000
            );

            return;
        }
    }

    public static string Aim(Guild guild)
    {
        if (guild == null)
        {
            return "nothing";
        }

        var hall = BotEstate.Hall(guild);
        var fund = BotEstate.Fund(guild);

        if (hall is not { Deleted: false })
        {
            return $"a hall — {fund} of {BotEstate.Price}gp";
        }

        if (BotEstate.Merchants(hall) < BotEstate.MostMerchants)
        {
            return "a merchant for its counter";
        }

        var held = BotClaim.Holds(guild.Name);

        if (held < BotClaim.Free)
        {
            return $"ground — {held} of {BotClaim.Free} free squares taken";
        }

        return $"more ground — {held} squares held, {BotClaim.Price}gp the next";
    }

    public static string Task(Guild guild)
    {
        if (guild == null)
        {
            return "nothing";
        }

        var bid = BotClaim.Making(guild);

        if (bid != null)
        {
            return $"holding {bid.Middle.X},{bid.Middle.Y} — {BotClaim.Left(bid) / 1000}s left, {bid.Peak} gathered";
        }

        if (BotExile.Owed(guild) is { Deleted: false } winner)
        {
            return $"moving its hall out of the yard at {winner.X},{winner.Y}";
        }

        if (BotOffice.Busy(BotSupplier.Office, guild))
        {
            return "stocking its counter";
        }

        if (BotOffice.Busy(BotSteward.Office, guild))
        {
            return "raising its hall";
        }

        return guild.Enemies is { Count: > 0 } ? "at war" : "its own work";
    }

    public static long Hired { get; private set; }

    public static long Expelled { get; private set; }

    public static bool Recruit(Guild guild, BotMobile bot, string why)
    {
        if (!Running || guild == null || guild.Disbanded || bot is not { Deleted: false, Alive: true })
        {
            return false;
        }

        if (bot.Guild != null)
        {
            HeldElsewhere++;

            return false;
        }

        if (Outside(bot))
        {
            Excused++;

            return false;
        }

        if (guild.Members.Count >= Ceiling(guild) && !Widen(guild, $"to take {bot.Name} the {bot.Class?.Name} on: {why ?? "no reason given"}"))
        {
            Crowded++;

            return false;
        }

        if (Spurned(bot, guild))
        {
            Cooling++;

            return false;
        }

        guild.AddMember(bot);
        Show(bot);
        Joined(bot);
        Hired++;

        logger.Information(
            "{Guild} has taken {Bot} the {Class} on, {Count} of {Most} now — {Why}",
            guild.Name,
            bot.Name,
            bot.Class?.Name,
            guild.Members.Count,
            Ceiling(guild),
            why ?? "no reason given"
        );

        return true;
    }

    public static bool Expel(Guild guild, BotMobile bot, string why)
    {
        if (!Running || guild == null || bot is not { Deleted: false })
        {
            return false;
        }

        if (!ReferenceEquals(bot.Guild, guild))
        {
            return false;
        }

        if (IsHead(bot))
        {
            Spared++;

            return false;
        }

        guild.RemoveMember(bot);
        bot.DisplayGuildTitle = false;
        _joined.Remove(bot.Serial);
        Expelled++;

        logger.Information(
            "{Guild} has put {Bot} the {Class} out, {Count} left — {Why}",
            guild.Name,
            bot.Name,
            bot.Class?.Name,
            guild.Members.Count,
            why ?? "no reason given"
        );

        return true;
    }

    public static long HeldElsewhere { get; private set; }

    public static long Crowded { get; private set; }

    public static long Spared { get; private set; }

    private static void Joined(Mobile bot)
    {
        if (bot is { Deleted: false })
        {
            _joined[bot.Serial] = Core.TickCount;
        }
    }

    public static bool Same(Mobile a, Mobile b) =>
        Running && a?.Guild != null && ReferenceEquals(a.Guild, b?.Guild);

    public static double Worth(Mobile bot, Mobile other) => Same(bot, other) ? Kinship : 1.0;

    public static BotMobile Maker(Guild guild)
    {
        if (guild?.Members == null)
        {
            return null;
        }

        BotMobile best = null;
        var bestSkill = -1.0;

        for (var i = 0; i < guild.Members.Count; i++)
        {
            if (guild.Members[i] is not BotMobile { Deleted: false } member || !IsMaker(member))
            {
                continue;
            }

            var skill = member.Class?.MainSkill == null
                ? 0.0
                : member.Skills[member.Class.MainSkill.Value]?.Base ?? 0.0;

            if (skill > bestSkill)
            {
                best = member;
                bestSkill = skill;
            }
        }

        return best;
    }

    public static int Keep => BotEstate.Keep;

    public static long Stood { get; private set; }

    public static long Standings { get; private set; }

    public static long Cannot { get; private set; }

    public static bool Stand(BotMobile member, int need)
    {
        if (!Running || member is not { Deleted: false } || need <= 0 || member.Guild is not Guild guild)
        {
            return false;
        }

        var mates = new List<BotMobile>();

        for (var i = 0; i < guild.Members.Count; i++)
        {
            if (guild.Members[i] is BotMobile { Deleted: false } mate && mate != member)
            {
                mates.Add(mate);
            }
        }

        mates.Sort((a, b) => BotYield.Wealth(b).CompareTo(BotYield.Wealth(a)));

        var got = 0;

        for (var i = 0; i < mates.Count && got < need; i++)
        {
            var mate = mates[i];
            var pack = mate.Backpack;

            if (pack == null)
            {
                continue;
            }

            var spare = Math.Min(BotYield.Wealth(mate) - Keep, need - got);

            if (spare <= 0)
            {
                continue;
            }

            var carried = pack.GetAmount(typeof(Gold));
            var fromPack = Math.Min(carried, spare);
            var fromBank = spare - fromPack;

            if (fromPack > 0 && !pack.ConsumeTotal(typeof(Gold), fromPack))
            {
                continue;
            }

            if (fromBank > 0 && !Banker.Withdraw(mate, fromBank))
            {
                if (fromPack > 0)
                {
                    pack.DropItem(new Gold(fromPack));
                }

                continue;
            }

            got += spare;

            BotYield.Aside(mate, spare);
        }

        if (got < need)
        {
            if (got > 0)
            {
                Banker.Deposit(member, got);

                BotYield.Aside(member, -got);
            }

            Cannot++;

            return false;
        }

        Banker.Deposit(member, got);
        Stood += got;
        Standings++;

        return true;
    }

    public static string Describe()
    {
        if (!Running)
        {
            return "the population keeps no guilds";
        }

        if (_guilds.Count == 0)
        {
            return "no guilds have been raised yet";
        }

        var say = Server.Text.ValueStringBuilder.Create(320);

        try
        {
            var makerless = 0;
            var held = 0;

            foreach (var (name, guild) in _guilds)
            {
                if (say.Length > 0)
                {
                    say.Append(", ");
                }

                say.Append(name);
                say.Append(" [");
                say.Append(guild.Abbreviation);
                say.Append("] ");
                say.Append((guild.Members?.Count ?? 0).ToString());

                held += guild.Members?.Count ?? 0;

                if (guild.Disbanded)
                {
                    say.Append(" DISBANDED");

                    continue;
                }

                var head = Head(guild);

                if (head != null)
                {
                    say.Append(" under ");
                    say.Append(head.Name);
                    say.Append(" the ");
                    say.Append(head.Class?.Name ?? "?");
                }

                if (Maker(guild) == null)
                {
                    makerless++;
                }
            }

            var loose = 0;
            var roster = BotPopulation.Bots;

            for (var i = 0; i < roster.Count; i++)
            {
                if (roster[i] is { Deleted: false, Guild: null } bot && !Outside(bot))
                {
                    loose++;
                }
            }

            var stood = (Standings == 0
                    ? "; nothing has been stood for anybody yet"
                    : $"; {Stood}gp stood for members {Standings} times, {Cannot} times a guild could not")
                + $"; {Walked} members left a guild that had nothing to show ({Crossed} of them for a guild that had, {Nowhere} looks found no guild worth leaving for), {Cooling} refused a guild for having lately left one"
                + $"; {BotRoster.Describe()}"
                + $"; {BotCharter.Describe()}";

            var founding = !Founding
                ? "founding is OFF"
                : $"{Founded} founded and {Gathered} joined since the shard came up, {Stones} given the stone the engine wants; "
                  + $"looks that placed nobody: {TooFew} found fewer than {Least - 1} others without a guild to found with, "
                  + $"{Nameless} found every name taken, {Barred48} were inside the bar for walking out, {Unplaced} found every guild full";

            var rule = $"{Least} to found and {Most} at most, anybody may found, a full guild widening by {WidenBy} for {WidenPrice}gp up to {MostWidenings} times ({Widenings} bought, {Unwidened} wanted and not raised)";
            var short_ = makerless == 0 ? "" : $", {makerless} of them with nobody to make anything";

            return $"{_guilds.Count} guilds holding {held} bots with {loose} outside them ({rule}{short_}), a guildmate worth ×{Kinship:F2}: {say.ToString()}; {founding}{stood}";
        }
        finally
        {
            say.Dispose();
        }
    }

    public static void Forget()
    {
        _guilds.Clear();
        Enrolled = 0;
        Excused = 0;
        Walked = 0;
        Cooling = 0;
        _quit.Clear();
        _joined.Clear();
        _reviewed = 0;
        _turn = 0;
        Unplaced = 0;
        Stood = 0;
        Standings = 0;
        Cannot = 0;
        Hired = 0;
        Expelled = 0;
        HeldElsewhere = 0;
        Crowded = 0;
        Spared = 0;
        Crossed = 0;
        Nowhere = 0;
        Founded = 0;
        Gathered = 0;
        TooFew = 0;
        Nameless = 0;
        Barred48 = 0;
        Stones = 0;
        _cursor = 0;
        _dealt = false;
        _widened.Clear();
        Widenings = 0;
        Unwidened = 0;

        BotRoster.Forget();
    }
}
