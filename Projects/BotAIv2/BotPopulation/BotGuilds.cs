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
/// </summary>
public static class BotGuilds
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotGuilds));

    public static bool Running { get; set; } = true;

    public static double Kinship { get; set; } = 1.35;

    public static int Least { get; set; } = 5;

    public static int Most { get; set; } = 15;

    public static int Band { get; set; } = 10;

    public static readonly string[] Makers = ["Crafter"];

    public static readonly string[] Barred = ["Baron", "Captain", "Architect", "Sage", "Brawler"];

    public static long Excused { get; private set; }

    public static bool Outside(BotMobile bot)
    {
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
        var makers = new List<BotMobile>();
        var rest = new List<BotMobile>();

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

            if (IsMaker(bot))
            {
                makers.Add(bot);
            }
            else
            {
                rest.Add(bot);
            }
        }

        var all = makers.Count + rest.Count;

        if (all < Least || makers.Count == 0)
        {
            logger.Warning(
                "No guild can be formed: {All} bots and {Makers} of them able to make anything, against a rule of {Least} with at least one maker",
                all,
                makers.Count,
                Least
            );

            return;
        }

        var bands = Math.Max(1, (int)Math.Round(all / (double)Band));

        bands = Math.Min(bands, makers.Count);
        bands = Math.Min(bands, Names.Length);
        bands = Math.Min(bands, all / Least);
        bands = Math.Max(bands, (all + Most - 1) / Most);
        bands = Math.Min(bands, Math.Min(makers.Count, Names.Length));

        if (bands < 1)
        {
            return;
        }

        rest.Sort((a, b) => ((int)(a.Class?.Role ?? BotRole.Producer)).CompareTo((int)(b.Class?.Role ?? BotRole.Producer)));

        var bench = new List<BotMobile>[bands];

        for (var i = 0; i < bands; i++)
        {
            bench[i] = [makers[i]];
        }

        for (var i = bands; i < makers.Count; i++)
        {
            rest.Add(makers[i]);
        }

        var into = 0;

        for (var i = 0; i < rest.Count; i++)
        {
            var tries = 0;

            while (bench[into % bands].Count >= Most && tries++ < bands)
            {
                into++;
            }

            if (bench[into % bands].Count >= Most)
            {
                Unplaced++;

                continue;
            }

            bench[into++ % bands].Add(rest[i]);
        }

        for (var i = 0; i < bands; i++)
        {
            Form(Names[i], bench[i]);
        }

        _dealt = true;

        logger.Information(
            "Guilds mustered: {Bands} of them out of {All} bots, {Makers} makers, {Unplaced} left out, {Excused} whose class stands outside the bands — {What}",
            bands,
            all,
            makers.Count,
            Unplaced,
            Excused,
            Describe()
        );

        logger.Information(
            "Guild membership: a member gives a guild with nothing to show {Patience} minutes before it may walk out, one a minute at most, and is then barred from every guild for {Rejoin} hours; a leader may put one member out and take one bot on every {Roster} minutes, and may not take back somebody who walked out of its own guild",
            Patience / 60000,
            RejoinMs / 3600000,
            BotRoster.EveryMs / 60000
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

        for (var i = 0; i < band.Count; i++)
        {
            guild.AddMember(band[i]);
            Show(band[i]);
            Joined(band[i]);
            Enrolled++;
        }
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
            if (guild.Disbanded || guild.Members.Count >= Most)
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

        if (BotEstate.Fund(guild) >= BotEstate.Price / 2)
        {
            worth++;
        }

        return worth;
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

        var members = guild.Members;

        for (var i = 0; i < members.Count; i++)
        {
            if (members[i] is not BotMobile { Deleted: false, Alive: true } bot || IsMaker(bot))
            {
                continue;
            }

            if (!_joined.TryGetValue(bot.Serial, out var since) || now - (since + Patience) < 0)
            {
                continue;
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

        if (guild.Members.Count >= Most)
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
            Most,
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

        if (IsMaker(bot))
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
        }

        if (got < need)
        {
            if (got > 0)
            {
                Banker.Deposit(member, got);
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

                var maker = Maker(guild);

                if (maker == null)
                {
                    makerless++;
                }
                else
                {
                    say.Append(" under ");
                    say.Append(maker.Name);
                }
            }

            var stood = Standings == 0
                ? "; nothing has been stood for anybody yet"
                : $"; {Stood}gp stood for members {Standings} times, {Cannot} times a guild could not"
                  + $"; {Walked} members walked out of a guild that had nothing to show, {Cooling} refused a guild for having lately left one"
                  + $"; {BotRoster.Describe()}"
                  + $"; {BotCharter.Describe()}";

            var rule = $"{Least}-{Most} to a guild, one maker each";
            var short_ = makerless == 0 ? "" : $", {makerless} of them with nobody to make anything";

            return $"{_guilds.Count} guilds holding the population ({rule}{short_}), a guildmate worth ×{Kinship:F2}: {say.ToString()}{stood}";
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
        _dealt = false;

        BotRoster.Forget();
    }
}
