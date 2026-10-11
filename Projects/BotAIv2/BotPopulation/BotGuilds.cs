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

    public static int Most { get; set; } = 35;

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

    public static readonly string[] Barred = ["Baron", "Captain", "Architect", "Sage", "Brawler", "Trader"];

    public static long Excused { get; private set; }

    public static bool Outside(BotMobile bot)
    {
        if (BotUnderworld.Outlawed(bot))
        {
            return true;
        }

        if (BotCrime.Unrepentant(bot))
        {
            return true;
        }

        return bot?.Class is { Neutral: true } || Bars(bot?.Class?.Name);
    }

    public static bool Bars(string klass)
    {
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
        ("The Ash", "ASH"),
        ("The Raven", "RVN"),
        ("The Oak", "OAK"),
        ("The Stag", "STG"),
        ("The Forge", "FRG"),
        ("The Compass", "CMP"),
        ("The Tide", "TID"),
        ("The Ember", "EMB"),
        ("The Thorn", "THN"),
        ("The Falcon", "FAL"),
        ("The Chalice", "CHL"),
        ("The Gauntlet", "GNT"),
        ("The Lyre", "LYR"),
        ("The Sickle", "SCK"),
        ("The Mill", "MIL"),
        ("The Bell", "BEL"),
        ("The Scroll", "SCR"),
        ("The Loom", "LOM"),
        ("The Hearth", "HRT"),
        ("The Vine", "VIN"),
        ("The Griffin", "GRF"),
        ("The Serpent", "SRP"),
        ("The Boar", "BOR"),
        ("The Heron", "HRN"),
        ("The Otter", "OTR"),
        ("The Badger", "BDG"),
        ("The Hawk", "HWK"),
        ("The Bear", "BER"),
        ("The Rose", "ROS"),
        ("The Lily", "LIL"),
        ("The Star", "STR"),
        ("The Moon", "MON"),
        ("The Sun", "SUN"),
        ("The Comet", "CMT"),
        ("The Storm", "STM"),
        ("The Frost", "FRS"),
        ("The Flint", "FLN"),
        ("The Anvil", "ANV"),
        ("The Plough", "PLW"),
        ("The Sheaf", "SHF"),
        ("The Barrel", "BRL"),
        ("The Key", "KEY"),
        ("The Tower", "TWR"),
        ("The Bridge", "BRG"),
        ("The Gate", "GAT"),
        ("The Harp", "HRP"),
        ("The Horn", "HOR"),
        ("The Banner", "BNR"),
        ("The Shield", "SHL"),
        ("The Spear", "SPR"),
        ("The Arrow", "ARW")
    ];

    private const int Inherited = 8;

    public static int MostGuilds { get; set; } = 10;

    public static long AtMost { get; private set; }

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
            "Guild membership: founding is {Founding} — every {Gather}s one bot without a guild joins the smallest guild under {Band}, or founds its own with the {Fellows} nearest bots without one if no guild is under {Band}, {Names} names to go round (the first {Inherited} in order, the rest drawn at random), at most {MostGuilds} guilds, {Most} the ceiling; a member gives a guild with nothing to show {Patience} minutes before it may leave, one a minute at most, {Defecting}, and is then barred from joining any guild for {Rejoin} hours, though not from founding one; a leader may put one member out and take one bot on every {Roster} minutes, and may not take back somebody who walked out of its own guild; the head of a guild neither leaves nor is put out; closing is {Closing}: a guild of fewer than {Least} with attractiveness under {LowAppeal} and nobody joining for {CloseAfter} minutes closes, its chest shared among its members, its name resting {Rest} hours ({Clocks} clocks and {Closed} closed names read back off the save)",
            Founding ? "ON" : "OFF",
            GatherMs / 1000,
            Band,
            Least - 1,
            Band,
            Names.Length,
            Inherited,
            MostGuilds,
            Most,
            Patience / 60000,
            Defecting ? "and only for a guild that has something to show and room" : "into no guild at all",
            RejoinMs / 3600000,
            BotRoster.EveryMs / 60000,
            Closing ? "ON" : "OFF",
            Least,
            LowAppeal,
            CloseAfterMs / 60000,
            NameRestHours,
            _low.Count,
            _closed.Count
        );

        logger.Information(
            "Guild splits: {Splitting} — a guild at its ceiling loses {Least} members to a new guild under one of the fifty names when {Least} of them, not its head, not in a company, not inside the walk-out bar, have a reason: under the {Keep}gp keep for {Unshared} minutes while its chest holds {Rich}gp or more, living in another town than its house or hall or out of {Reach} tiles of both, or a guild half the others hold at {Enmity} or under or a town that put it out; not while it is at war, not within {Cool} hours of its last split, not with {MostGuilds} guilds standing, not while a guild with room reads appeal {Bar} or more, not within {Gap} hours of any split; founders barred from their old guild and from crossing for {Rejoin} hours of running time ({Barred} inside the bar read back off the save), the new guild spared the closing clock for {Young} hours ({Spared} read back)",
            Splitting ? "ON" : "OFF",
            Least,
            Least,
            BotEstate.Keep,
            UnsharedMs / 60000,
            RichChest,
            BotSeat.Settled,
            BotRegard.Enmity,
            SplitParentMs / 3600000,
            MostGuilds,
            SplitBar,
            SplitGapMs / 3600000,
            RejoinMs / 3600000,
            InsideBar(),
            YoungMs / 3600000,
            _young.Count
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

    public static bool Crown(Guild guild, BotMobile head)
    {
        if (guild is not { Disbanded: false } || head is not { Deleted: false } || head.Guild != guild)
        {
            return false;
        }

        if (!ReferenceEquals(guild.Leader, head))
        {
            guild.Leader = head;
        }

        for (var i = 0; i < guild.Members.Count; i++)
        {
            if (guild.Members[i] is { Deleted: false } member)
            {
                member.GuildFealty = head;
            }
        }

        if (BotEstate.Hall(guild) is { Deleted: false } hall && hall.Owner != head)
        {
            BotEstate.Take(guild, hall);
        }

        if (BotOutpost.Of(guild.Name) is { Deleted: false } outpost && outpost.Owner != head)
        {
            BotOutpost.Keep(guild, outpost);
        }

        return true;
    }

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
                _young.Remove(dead[i]);

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

        if (Living() >= MostGuilds)
        {
            AtMost++;
        }
        else if (FreeName(out var named))
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

                _foundedNames.Add(named.Name);
                BotAlarm.Note("guild", $"{named.Name} [{named.Abbrev}] was founded by {bot.Name} the {bot.Class?.Name} with {fellows.Count} others", Count, MostGuilds, "-");

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

    private static bool FreeName(out (string Name, string Abbrev) named) => FreeName(out named, false);

    private static bool FreeName(out (string Name, string Abbrev) named, bool split)
    {
        for (var i = 0; !split && i < Inherited && i < Names.Length; i++)
        {
            if (Free(Names[i].Name))
            {
                named = Names[i];

                return true;
            }
        }

        var free = 0;

        for (var i = Inherited; i < Names.Length; i++)
        {
            if (Free(Names[i].Name) && (!split || Bare(Names[i].Name)))
            {
                free++;
            }
        }

        if (free > 0)
        {
            var pick = Utility.Random(free);

            for (var i = Inherited; i < Names.Length; i++)
            {
                if (Free(Names[i].Name) && (!split || Bare(Names[i].Name)) && pick-- == 0)
                {
                    named = Names[i];

                    return true;
                }
            }
        }

        named = default;

        return false;
    }

    private static bool Free(string name)
    {
        if (_guilds.TryGetValue(name, out var guild) && !guild.Disbanded)
        {
            return false;
        }

        if (Resting(name))
        {
            return false;
        }

        return BaseGuild.FindByName(name) is not Guild { Disbanded: false };
    }

    private static bool Bare(string name) =>
        !BotEstate.Held.ContainsKey(name) && BotGuildHouses.Of(name) == null && BotClaim.Holds(name) == 0;

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
        if (!Running || _guilds.Count == 0)
        {
            return;
        }

        var now = Core.TickCount;

        if (now - (_reviewed + ReviewMs) < 0)
        {
            return;
        }

        _reviewed = now;

        Wane(now);

        Hive(now);

        if (!Leaving || _guilds.Count == 0)
        {
            return;
        }

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

    public static double Appeal(Guild guild) =>
        guild is not { Disbanded: false } ? 0.0 : Worth(guild) + (double)guild.Members.Count / Math.Max(1, Least) + BotRelics.AppealOf(guild);

    public static bool Closing { get; set; } = true;

    public static double LowAppeal { get; set; } = 2.0;

    public static int CloseAfterMs { get; set; } = 7200000;

    public static int NameRestHours { get; set; } = 168;

    public static long Closed { get; private set; }

    public static long ClosedGold { get; private set; }

    public static long Released { get; private set; }

    public static long HeldAtWar { get; private set; }

    public static long HeldClaiming { get; private set; }

    public static long HeldInCompany { get; private set; }

    public static long HeldNoRoom { get; private set; }

    private static readonly Dictionary<string, long> _low = new(StringComparer.OrdinalIgnoreCase);

    private static readonly Dictionary<string, int> _seen = new(StringComparer.OrdinalIgnoreCase);

    private static readonly Dictionary<string, DateTime> _closed = new(StringComparer.OrdinalIgnoreCase);

    private static readonly List<string> _foundedNames = [];

    private static readonly List<string> _closedNames = [];

    public static int LowMinutes(Guild guild) =>
        guild?.Name != null && _low.TryGetValue(guild.Name, out var since) ? (int)Math.Max(0, (Core.TickCount - since) / 60000) : 0;

    private static bool Resting(string name) =>
        name != null && _closed.TryGetValue(name, out var when) && Core.Now - when < TimeSpan.FromHours(Math.Max(0, NameRestHours));

    private static int RestingNames()
    {
        var n = 0;

        foreach (var name in _closed.Keys)
        {
            if (Resting(name))
            {
                n++;
            }
        }

        return n;
    }

    private static int Living()
    {
        var n = 0;

        foreach (var guild in _guilds.Values)
        {
            if (!guild.Disbanded)
            {
                n++;
            }
        }

        return n;
    }

    private static int Room(Guild but)
    {
        var room = 0;

        foreach (var guild in _guilds.Values)
        {
            if (ReferenceEquals(guild, but) || guild.Disbanded)
            {
                continue;
            }

            room += Math.Max(0, Ceiling(guild) - guild.Members.Count);
        }

        return room;
    }

    private static void Wane(long now)
    {
        List<Guild> due = null;
        List<string> stale = null;

        foreach (var name in _low.Keys)
        {
            if (!_guilds.ContainsKey(name))
            {
                (stale ??= []).Add(name);
            }
        }

        for (var i = 0; stale != null && i < stale.Count; i++)
        {
            _low.Remove(stale[i]);
        }

        foreach (var (name, guild) in _guilds)
        {
            if (guild.Disbanded)
            {
                continue;
            }

            var count = guild.Members.Count;
            var gained = _seen.TryGetValue(name, out var was) && count > was;

            _seen[name] = count;

            if (count >= Least || gained || Appeal(guild) >= LowAppeal)
            {
                _low.Remove(name);

                continue;
            }

            if (Young(name, now))
            {
                _low.Remove(name);
                HeldYoung++;

                continue;
            }

            if (!_low.TryGetValue(name, out var since))
            {
                _low[name] = now;

                continue;
            }

            if (now - (since + CloseAfterMs) >= 0)
            {
                (due ??= []).Add(guild);
            }
        }

        if (!Closing || due == null)
        {
            return;
        }

        for (var i = 0; i < due.Count; i++)
        {
            var guild = due[i];

            if (BotWar.Fighting(guild.Name) > 0)
            {
                HeldAtWar++;

                continue;
            }

            if (BotClaim.Making(guild) != null)
            {
                HeldClaiming++;

                continue;
            }

            if (InCompany(guild))
            {
                HeldInCompany++;

                continue;
            }

            if (Room(guild) < guild.Members.Count)
            {
                HeldNoRoom++;

                continue;
            }

            if (Close(guild, now))
            {
                return;
            }
        }
    }

    private static bool InCompany(Guild guild)
    {
        for (var i = 0; i < guild.Members.Count; i++)
        {
            if (guild.Members[i] is BotMobile { Deleted: false, Squad: not null })
            {
                return true;
            }
        }

        return false;
    }

    private static bool Close(Guild guild, long now)
    {
        var name = guild.Name;
        var abbrev = guild.Abbreviation;
        var appeal = Appeal(guild);
        var lowMinutes = LowMinutes(guild);
        var head = Head(guild);
        List<BotMobile> members = [];

        if (head != null)
        {
            members.Add(head);
        }

        for (var i = 0; i < guild.Members.Count; i++)
        {
            if (guild.Members[i] is BotMobile { Deleted: false } member && !ReferenceEquals(member, head))
            {
                members.Add(member);
            }
        }

        if (members.Count == 0)
        {
            return false;
        }

        var gold = BotChest.Empty(name);
        var each = gold / members.Count;
        var odd = gold - each * members.Count;

        for (var i = 0; i < members.Count; i++)
        {
            var share = each + (i == 0 ? odd : 0);

            if (share > 0 && !Banker.Deposit(members[i], share))
            {
                members[i].AddToBackpack(new Gold(share));
            }
        }

        guild.Disband();

        var released = new string[members.Count];

        for (var i = 0; i < members.Count; i++)
        {
            var bot = members[i];

            bot.DisplayGuildTitle = false;
            _joined.Remove(bot.Serial);

            _quit.Remove(bot.Serial);
            released[i] = $"{bot.Name} the {bot.Class?.Name}";
        }

        _guilds.Remove(name);
        _low.Remove(name);
        _seen.Remove(name);
        _widened.Remove(name);
        _young.Remove(name);
        _splitAt.Remove(name);
        _left.Remove(name);
        _closed[name] = Core.Now;
        _closedNames.Add(name);
        BotSeat.Drop(name);

        Closed++;
        ClosedGold += gold;
        Released += members.Count;

        logger.Warning(
            "{Guild} [{Abbrev}] has closed: attractiveness {Appeal:F1} under {Line:F1} with {Members} members for {Minutes} minutes and nobody joining; its chest of {Gold}gp was shared among {Released}; {Left} guilds stand, and the name rests {Hours} hours",
            name,
            abbrev,
            appeal,
            LowAppeal,
            members.Count,
            lowMinutes,
            gold,
            string.Join(", ", released),
            _guilds.Count,
            NameRestHours
        );

        BotAlarm.Note("guild", $"{name} closed: attractiveness {appeal:F1} with {members.Count} members for {lowMinutes} minutes, nobody joining; {gold}gp shared", members.Count, _guilds.Count, $"{lowMinutes}m");

        return true;
    }

    internal static void SaveClosings(IGenericWriter writer)
    {
        var now = Core.TickCount;

        writer.WriteEncodedInt(_low.Count);

        foreach (var (name, since) in _low)
        {
            writer.Write(name);
            writer.Write(Math.Max(0L, now - since));
        }

        writer.WriteEncodedInt(_closed.Count);

        foreach (var (name, when) in _closed)
        {
            writer.Write(name);
            writer.Write(when);
        }
    }

    internal static int LoadClosings(IGenericReader reader)
    {
        _low.Clear();
        _closed.Clear();

        var now = Core.TickCount;
        var low = reader.ReadEncodedInt();

        for (var i = 0; i < low; i++)
        {
            var name = reader.ReadString();
            var elapsed = reader.ReadLong();

            if (!string.IsNullOrEmpty(name))
            {
                _low[name] = now - Math.Max(0L, elapsed);
            }
        }

        var closed = reader.ReadEncodedInt();

        for (var i = 0; i < closed; i++)
        {
            var name = reader.ReadString();
            var when = reader.ReadDateTime();

            if (!string.IsNullOrEmpty(name))
            {
                _closed[name] = when;
            }
        }

        return _low.Count;
    }

    public static bool Splitting { get; set; } = true;

    public static double SplitBar { get; set; } = 5.0;

    public static int UnsharedMs { get; set; } = 3600000;

    public static int RichChest => BotEstate.Price / 2;

    public static int SplitGapMs { get; set; } = 10800000;

    public static int SplitParentMs { get; set; } = 43200000;

    public static int YoungMs { get; set; } = 21600000;

    public static long Splits { get; private set; }

    public static long FoundersUnshared { get; private set; }

    public static long FoundersFar { get; private set; }

    public static long FoundersQuarrel { get; private set; }

    public static long SplitFew { get; private set; }

    public static long SplitAtWar { get; private set; }

    public static long SplitParentCool { get; private set; }

    public static long SplitAtMost { get; private set; }

    public static long SplitRoom { get; private set; }

    public static long SplitGap { get; private set; }

    public static long SplitNameless { get; private set; }

    public static long HeldYoung { get; private set; }

    private static readonly Dictionary<Serial, long> _unshared = [];

    private static readonly HashSet<Serial> _unsharedNow = [];

    private static readonly List<Serial> _unsharedGone = [];

    private static readonly Dictionary<string, long> _splitAt = new(StringComparer.OrdinalIgnoreCase);

    private static readonly Dictionary<string, int> _left = new(StringComparer.OrdinalIgnoreCase);

    private static readonly Dictionary<string, (long Tick, string From)> _young = new(StringComparer.OrdinalIgnoreCase);

    private static long _lastSplit;

    private static bool _hasSplit;

    private static readonly List<string> _splitNames = [];

    private const int WhyUnshared = 1;

    private const int WhyFar = 2;

    private const int WhyQuarrel = 4;

    private static bool Young(string name, long now) =>
        name != null && _young.TryGetValue(name, out var young) && now - (young.Tick + YoungMs) < 0;

    private static void Hive(long now)
    {
        if (!Running || !_dealt)
        {
            return;
        }

        Unshared(now);
        ShrinkLeft();

        if (!Splitting)
        {
            return;
        }

        Guild parent = null;
        List<(BotMobile Bot, int Why, int Far)> leavers = null;
        var war = false;
        var cool = false;

        foreach (var guild in _guilds.Values)
        {
            if (guild.Disbanded || guild.Members.Count < Ceiling(guild))
            {
                continue;
            }

            var found = Leavers(guild, now);

            if (found.Count < Least)
            {
                continue;
            }

            if (_splitAt.TryGetValue(guild.Name, out var at) && now - (at + SplitParentMs) < 0)
            {
                cool = true;

                continue;
            }

            if (BotWar.Fighting(guild.Name) > 0)
            {
                war = true;

                continue;
            }

            if (leavers == null || found.Count > leavers.Count)
            {
                parent = guild;
                leavers = found;
            }
        }

        if (parent == null)
        {
            if (war)
            {
                SplitAtWar++;
            }
            else if (cool)
            {
                SplitParentCool++;
            }
            else
            {
                SplitFew++;
            }

            return;
        }

        if (Living() >= MostGuilds)
        {
            SplitAtMost++;

            return;
        }

        if (WantedRoom(parent) != null)
        {
            SplitRoom++;

            return;
        }

        if (_hasSplit && now - (_lastSplit + SplitGapMs) < 0)
        {
            SplitGap++;

            return;
        }

        if (!FreeName(out var named, true))
        {
            SplitNameless++;

            return;
        }

        Found(parent, leavers, named, now);
    }

    private static void Unshared(long now)
    {
        _unsharedNow.Clear();

        foreach (var guild in _guilds.Values)
        {
            if (guild.Disbanded || BotChest.Holds(guild.Name) < RichChest)
            {
                continue;
            }

            for (var i = 0; i < guild.Members.Count; i++)
            {
                if (guild.Members[i] is BotMobile { Deleted: false } member && BotYield.Spendable(member) < BotEstate.Keep)
                {
                    _unsharedNow.Add(member.Serial);
                    _unshared.TryAdd(member.Serial, now);
                }
            }
        }

        _unsharedGone.Clear();

        foreach (var serial in _unshared.Keys)
        {
            if (!_unsharedNow.Contains(serial))
            {
                _unsharedGone.Add(serial);
            }
        }

        for (var i = 0; i < _unsharedGone.Count; i++)
        {
            _unshared.Remove(_unsharedGone[i]);
        }
    }

    private static void ShrinkLeft()
    {
        if (_left.Count == 0)
        {
            return;
        }

        List<(string Name, int Left)> change = null;

        foreach (var (name, left) in _left)
        {
            var free = _guilds.TryGetValue(name, out var guild) && !guild.Disbanded ? Math.Max(0, Ceiling(guild) - guild.Members.Count) : 0;

            if (free < left)
            {
                (change ??= []).Add((name, free));
            }
        }

        for (var i = 0; change != null && i < change.Count; i++)
        {
            if (change[i].Left <= 0)
            {
                _left.Remove(change[i].Name);
            }
            else
            {
                _left[change[i].Name] = change[i].Left;
            }
        }
    }

    private static List<(BotMobile Bot, int Why, int Far)> Leavers(Guild guild, long now)
    {
        List<(BotMobile Bot, int Why, int Far)> found = [];
        var despised = Despised(guild);
        var hall = BotEstate.Hall(guild);
        var seat = BotSeat.Of(guild.Name);

        for (var i = 0; i < guild.Members.Count; i++)
        {
            if (guild.Members[i] is not BotMobile { Deleted: false, Alive: true, Squad: null } bot
                || bot.Map == null || bot.Map == Map.Internal || IsHead(bot) || Outside(bot) || Cools(bot)
                || hall is { Deleted: false } && hall.Owner == bot)
            {
                continue;
            }

            var why = 0;

            if (_unshared.TryGetValue(bot.Serial, out var since) && now - (since + UnsharedMs) >= 0)
            {
                why |= WhyUnshared;
            }

            var lives = BotResidence.Of(bot);

            if (Far(lives, guild, hall))
            {
                why |= WhyFar;
            }

            if (despised || lives != null && BotBurgh.Bars(guild, lives))
            {
                why |= WhyQuarrel;
            }

            if (why != 0)
            {
                found.Add((bot, why, seat == Point3D.Zero ? 0 : BotSeat.Gap(bot.Location, seat)));
            }
        }

        return found;
    }

    private static bool Far(BotTowns.Town lives, Guild guild, Server.Multis.BaseHouse hall)
    {
        if (lives == null)
        {
            return false;
        }

        var standing = hall is { Deleted: false };
        var home = BotGuildHouses.TownOf(guild) ?? (standing ? BotTowns.Nearest(hall.Location) : null);

        if (home != null && !string.Equals(home.Name, lives.Name, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var house = BotGuildHouses.Seat(guild.Name);
        var nearHall = standing && BotSeat.Gap(hall.Location, lives.Square) <= BotSeat.Settled;
        var nearHouse = house != Point3D.Zero && BotSeat.Gap(house, lives.Square) <= BotSeat.Settled;

        return !nearHall && !nearHouse;
    }

    private static bool Despised(Guild guild)
    {
        var others = 0;
        var enemies = 0;

        foreach (var other in _guilds.Values)
        {
            if (ReferenceEquals(other, guild) || other.Disbanded)
            {
                continue;
            }

            others++;

            if (BotRegard.Of(other.Name, guild.Name) <= BotRegard.Enmity)
            {
                enemies++;
            }
        }

        return enemies > 0 && enemies * 2 >= others;
    }

    private static Guild WantedRoom(Guild but)
    {
        foreach (var guild in _guilds.Values)
        {
            if (ReferenceEquals(guild, but) || guild.Disbanded)
            {
                continue;
            }

            var room = Ceiling(guild) - guild.Members.Count - _left.GetValueOrDefault(guild.Name);

            if (room > 0 && Appeal(guild) >= SplitBar)
            {
                return guild;
            }
        }

        return null;
    }

    private static void Found(Guild parent, List<(BotMobile Bot, int Why, int Far)> leavers, (string Name, string Abbrev) named, long now)
    {
        Dictionary<string, int> towns = [];
        BotTowns.Town town = null;
        var most = 0;

        for (var i = 0; i < leavers.Count; i++)
        {
            if (BotResidence.Of(leavers[i].Bot) is not { } lives)
            {
                continue;
            }

            var n = towns.GetValueOrDefault(lives.Name) + 1;
            towns[lives.Name] = n;

            if (n > most)
            {
                most = n;
                town = lives;
            }
        }

        leavers.Sort(
            (a, b) =>
            {
                var aHere = town != null && ReferenceEquals(BotResidence.Of(a.Bot), town);
                var bHere = town != null && ReferenceEquals(BotResidence.Of(b.Bot), town);

                if (aHere != bHere)
                {
                    return aHere ? -1 : 1;
                }

                var byWhy = Reasons(b.Why).CompareTo(Reasons(a.Why));

                return byWhy != 0 ? byWhy : b.Far.CompareTo(a.Far);
            }
        );

        List<BotMobile> band = [];
        var said = new string[Least];

        for (var i = 0; i < leavers.Count && band.Count < Least; i++)
        {
            var (bot, why, _) = leavers[i];

            band.Add(bot);
            said[band.Count - 1] = $"{bot.Name} the {bot.Class?.Name} ({Say(why)})";

            if ((why & WhyUnshared) != 0)
            {
                FoundersUnshared++;
            }

            if ((why & WhyFar) != 0)
            {
                FoundersFar++;
            }

            if ((why & WhyQuarrel) != 0)
            {
                FoundersQuarrel++;
            }
        }

        town ??= BotTowns.Nearest(band[0].Location);

        var keys = Unkey(BotEstate.Hall(parent), band) + Unkey(BotOutpost.Of(parent.Name), band);

        Form(named, band);

        if (town != null && BotSettle.Hearth(town) is var hearth && hearth != Point3D.Zero)
        {
            BotSeat.Restore(named.Name, hearth);
        }

        for (var i = 0; i < band.Count; i++)
        {
            _quit[band[i].Serial] = (now, parent.Name);
        }

        _splitAt[parent.Name] = now;
        _left[parent.Name] = _left.GetValueOrDefault(parent.Name) + band.Count;
        _young[named.Name] = (now, parent.Name);
        _lastSplit = now;
        _hasSplit = true;
        Splits++;
        _splitNames.Add($"{named.Name} ← {parent.Name}");

        logger.Warning(
            "{Guild} [{Abbrev}] has been founded by a split: {Count} left {Parent} ({Left} of {Ceiling} now, appeal {Appeal:F1}) — {Founders}, led by {Leader}; seated by {Town}, with no hall, no ground and no house, the chest left behind, {Keys} keys to the old hall and outpost given back; barred from {Parent} and any other guild for {Bar} hours, {Parent} splits again no sooner than {Cool} hours, nobody splits for {Gap} hours, and the closing clock spares {Guild} for {Young} hours; {Standing} guilds stand",
            named.Name,
            named.Abbrev,
            band.Count,
            parent.Name,
            parent.Members.Count,
            Ceiling(parent),
            Appeal(parent),
            string.Join(", ", said),
            band[0].Name,
            town?.Name ?? "nowhere known",
            keys,
            parent.Name,
            RejoinMs / 3600000,
            parent.Name,
            SplitParentMs / 3600000,
            SplitGapMs / 3600000,
            named.Name,
            YoungMs / 3600000,
            _guilds.Count
        );

        BotAlarm.Note("guild", $"{named.Name} [{named.Abbrev}] was founded by {band.Count} who left {parent.Name}, led by {band[0].Name}", band.Count, _guilds.Count, "-");
    }

    private static int Reasons(int why) => (why & WhyUnshared) + ((why & WhyFar) >> 1) + ((why & WhyQuarrel) >> 2);

    private static string Say(int why)
    {
        var say = (why & WhyUnshared) != 0 ? "no share" : null;

        if ((why & WhyFar) != 0)
        {
            say = say == null ? "far" : say + ", far";
        }

        if ((why & WhyQuarrel) != 0)
        {
            say = say == null ? "a quarrel" : say + ", a quarrel";
        }

        return say ?? "none";
    }

    private static int Unkey(Server.Multis.BaseHouse house, List<BotMobile> band)
    {
        if (house is not { Deleted: false })
        {
            return 0;
        }

        var n = 0;

        for (var i = 0; i < band.Count; i++)
        {
            if (house.CoOwners?.Remove(band[i]) == true)
            {
                n++;
            }

            if (house.Friends?.Remove(band[i]) == true)
            {
                n++;
            }
        }

        return n;
    }

    private static int InsideBar()
    {
        var now = Core.TickCount;
        var n = 0;

        foreach (var (_, quit) in _quit)
        {
            if (now - (quit.Tick + RejoinMs) < 0)
            {
                n++;
            }
        }

        return n;
    }

    internal static void SaveSplits(IGenericWriter writer)
    {
        var now = Core.TickCount;

        writer.WriteEncodedInt(InsideBar());

        foreach (var (serial, quit) in _quit)
        {
            if (now - (quit.Tick + RejoinMs) < 0)
            {
                writer.Write(serial);
                writer.Write(Math.Max(0L, now - quit.Tick));
                writer.Write(quit.Guild ?? "");
            }
        }

        writer.WriteEncodedInt(_splitAt.Count);

        foreach (var (name, at) in _splitAt)
        {
            writer.Write(name);
            writer.Write(Math.Max(0L, now - at));
        }

        writer.WriteEncodedInt(_left.Count);

        foreach (var (name, left) in _left)
        {
            writer.Write(name);
            writer.WriteEncodedInt(left);
        }

        writer.WriteEncodedInt(_young.Count);

        foreach (var (name, young) in _young)
        {
            writer.Write(name);
            writer.Write(Math.Max(0L, now - young.Tick));
            writer.Write(young.From ?? "");
        }

        writer.Write(_hasSplit);
        writer.Write(_hasSplit ? Math.Max(0L, now - _lastSplit) : 0L);
    }

    internal static int LoadSplits(IGenericReader reader, out int young)
    {
        _quit.Clear();
        _splitAt.Clear();
        _left.Clear();
        _young.Clear();

        var now = Core.TickCount;
        var quits = reader.ReadEncodedInt();

        for (var i = 0; i < quits; i++)
        {
            var serial = reader.ReadSerial();
            var elapsed = reader.ReadLong();
            var guild = reader.ReadString();

            _quit[serial] = (now - Math.Max(0L, elapsed), guild);
        }

        var splits = reader.ReadEncodedInt();

        for (var i = 0; i < splits; i++)
        {
            var name = reader.ReadString();
            var elapsed = reader.ReadLong();

            if (!string.IsNullOrEmpty(name))
            {
                _splitAt[name] = now - Math.Max(0L, elapsed);
            }
        }

        var lefts = reader.ReadEncodedInt();

        for (var i = 0; i < lefts; i++)
        {
            var name = reader.ReadString();
            var left = reader.ReadEncodedInt();

            if (!string.IsNullOrEmpty(name) && left > 0)
            {
                _left[name] = left;
            }
        }

        var youngs = reader.ReadEncodedInt();

        for (var i = 0; i < youngs; i++)
        {
            var name = reader.ReadString();
            var elapsed = reader.ReadLong();
            var from = reader.ReadString();

            if (!string.IsNullOrEmpty(name))
            {
                _young[name] = (now - Math.Max(0L, elapsed), from);
            }
        }

        _hasSplit = reader.ReadBool();

        var since = reader.ReadLong();

        _lastSplit = _hasSplit ? now - Math.Max(0L, since) : 0;

        young = _young.Count;

        return _quit.Count;
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

    public static long Poached { get; private set; }

    public static bool Poach(Guild from, Guild to, BotMobile bot, string why)
    {
        if (!Running || !Leaving || !Defecting || from == null || to == null || to.Disbanded || ReferenceEquals(from, to)
            || bot is not { Deleted: false, Alive: true } || !ReferenceEquals(bot.Guild, from) || IsHead(bot))
        {
            return false;
        }

        if (Cools(bot) || Spurned(bot, to))
        {
            Cooling++;

            return false;
        }

        if (from.Enemies != null && from.Enemies.Contains(to))
        {
            return false;
        }

        if (to.Members.Count >= Ceiling(to))
        {
            Crowded++;

            return false;
        }

        var now = Core.TickCount;
        var since = _joined.TryGetValue(bot.Serial, out var joined) ? joined : now;

        to.AddMember(bot);
        Show(bot);

        _quit[bot.Serial] = (now, from.Name);
        Joined(bot);
        Walked++;
        Crossed++;
        Poached++;

        logger.Information(
            "{Bot} the {Class} has left {From} for {To} after {Minutes} minutes in it — {Why}; {To} is {Count} of {Most} now",
            bot.Name,
            bot.Class?.Name,
            from.Name,
            to.Name,
            (now - since) / 60000,
            why ?? "no reason given",
            to.Name,
            to.Members.Count,
            Ceiling(to)
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

    public static double StandShare { get; set; } = 0.25;

    public static long CappedGold { get; private set; }

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

            var above = BotYield.Spendable(mate) - Keep;

            if (above <= 0)
            {
                continue;
            }

            var allowed = Math.Max(0, (int)(above * Math.Clamp(StandShare, 0.0, 1.0)));
            var asked = Math.Min(above, need - got);

            if (allowed < asked)
            {
                CappedGold += asked - allowed;
            }

            var spare = Math.Min(allowed, need - got);

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

                say.Append(" appeal ");
                say.Append(Appeal(guild).ToString("F1"));

                if (_low.ContainsKey(name))
                {
                    say.Append(" unwanted ");
                    say.Append(LowMinutes(guild).ToString());
                    say.Append("m");
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
                    : $"; {Stood}gp stood for members {Standings} times, {Cannot} times a guild could not, {CappedGold}gp left with their holders by the {StandShare:P0} cap on a call above the {Keep}gp keep")
                + $"; {Walked} members left a guild that had nothing to show ({Crossed} of them for a guild that had, {Nowhere} looks found no guild worth leaving for), {Cooling} refused a guild for having lately left one"
                + $"; {BotRoster.Describe()}"
                + $"; {BotCharter.Describe()}";

            var founding = !Founding
                ? "founding is OFF"
                : $"{Founded} founded and {Gathered} joined since the shard came up, {Stones} given the stone the engine wants; "
                  + $"looks that placed nobody: {TooFew} found fewer than {Least - 1} others without a guild to found with, "
                  + $"{Nameless} found every name taken, {AtMost} found {MostGuilds} guilds standing, {Barred48} were inside the bar for walking out, {Unplaced} found every guild full"
                  + (_foundedNames.Count == 0 ? "" : $" (founded: {string.Join(", ", _foundedNames)})");

            var closing = !Closing
                ? "; closing is OFF"
                : $"; closing: a guild of fewer than {Least} with attractiveness (worth + members/{Least}) under {LowAppeal:F1} and nobody joining for {CloseAfterMs / 60000} minutes closes — "
                  + $"{Closed} closed{(_closedNames.Count == 0 ? "" : $" ({string.Join(", ", _closedNames)})")}, {ClosedGold}gp of their chests shared among {Released} members let go, "
                  + $"looks that held a due guild open: {HeldAtWar} at war, {HeldClaiming} laying claim, {HeldInCompany} with a member in a company, {HeldNoRoom} with too few free places elsewhere; "
                  + $"{RestingNames()} names resting";

            var splits = !Splitting
                ? "; splits are OFF"
                : $"; splits: five of a full guild with a reason found under one of the fifty while no guild with room reads appeal {SplitBar:F1} or more — "
                  + $"{Splits} founded by a split{(_splitNames.Count == 0 ? "" : $" ({string.Join(", ", _splitNames)})")}, "
                  + $"founders' reasons: {FoundersUnshared} no share (under the {BotEstate.Keep}gp keep {UnsharedMs / 60000}m while the chest held {RichChest}gp+), {FoundersFar} far from the guild's house or hall, {FoundersQuarrel} a quarrel; "
                  + $"looks that split nobody: {SplitFew} no full guild had {Least} free to go with a reason, {SplitAtWar} at war, {SplitParentCool} inside a guild's {SplitParentMs / 3600000}h after its split, "
                  + $"{SplitAtMost} at {MostGuilds} guilds, {SplitRoom} a guild with room at the bar, {SplitGap} inside {SplitGapMs / 3600000}h of the last split, {SplitNameless} no bare name; "
                  + $"{_unshared.Count} members under the keep in a rich guild now; {HeldYoung} closing looks spared a split guild under {YoungMs / 3600000}h; {InsideBar()} bots inside the walk-out bar (kept across restarts)";

            var rule = $"{Least} to found and {Most} at most, anybody may found, a full guild widening by {WidenBy} for {WidenPrice}gp up to {MostWidenings} times ({Widenings} bought, {Unwidened} wanted and not raised)";
            var short_ = makerless == 0 ? "" : $", {makerless} of them with nobody to make anything";

            return $"{_guilds.Count} guilds holding {held} bots with {loose} outside them ({rule}{short_}), a guildmate worth ×{Kinship:F2}: {say.ToString()}; {founding}{closing}{splits}{stood}; {BotElection.Describe()}";
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
        CappedGold = 0;
        Hired = 0;
        Expelled = 0;
        HeldElsewhere = 0;
        Crowded = 0;
        Spared = 0;
        Crossed = 0;
        Poached = 0;
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

        _low.Clear();
        _seen.Clear();
        _closed.Clear();
        _foundedNames.Clear();
        _closedNames.Clear();
        AtMost = 0;
        Closed = 0;
        ClosedGold = 0;
        Released = 0;
        HeldAtWar = 0;
        HeldClaiming = 0;
        HeldInCompany = 0;
        HeldNoRoom = 0;

        _unshared.Clear();
        _splitAt.Clear();
        _left.Clear();
        _young.Clear();
        _splitNames.Clear();
        _hasSplit = false;
        _lastSplit = 0;
        Splits = 0;
        FoundersUnshared = 0;
        FoundersFar = 0;
        FoundersQuarrel = 0;
        SplitFew = 0;
        SplitAtWar = 0;
        SplitParentCool = 0;
        SplitAtMost = 0;
        SplitRoom = 0;
        SplitGap = 0;
        SplitNameless = 0;
        HeldYoung = 0;

        BotRoster.Forget();
    }
}
