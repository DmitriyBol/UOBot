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

    /// <summary>Whether the population is organised into guilds at all.</summary>
    public static bool Running { get; set; } = true;

    /// <summary>
    /// How much more a guildmate's cry, or a guildmate's company, is worth than a stranger's.
    ///
    /// <para>
    /// A multiplier and a small one. Loyalty should tilt a choice, not decide it: at two, a bot walks past a
    /// stranger bleeding in front of it to help somebody of its own three screens away, and that is not what
    /// was asked for. At <see cref="Kinship"/> it prefers its own among equals, which is what a guild is.
    /// </para>
    /// </summary>
    public static double Kinship { get; set; } = 1.35;

    /// <summary>
    /// The fewest a guild may be founded with. Patrick's number.
    ///
    /// Below five a guild is a pair of friends: it cannot hold a hall, cannot field a company, and cannot
    /// survive one of its members dying. The rule bites at formation only — a guild that falls below five
    /// later is not disbanded, because disbanding it would take a hall off the island for a bad afternoon.
    /// </summary>
    public static int Least { get; set; } = 5;

    /// <summary>
    /// The most a guild may hold. Patrick's number, and the reason for it was on the shard in front of him:
    /// the Blade held thirty-five of forty-nine bots and would have won any quarrel before it started.
    /// </summary>
    public static int Most { get; set; } = 15;

    /// <summary>
    /// The size a guild is aimed at when the roster is dealt out. Between <see cref="Least"/> and
    /// <see cref="Most"/>, and nearer the top of that range than the bottom: a band of ten can lose two
    /// members to a bad night and still be a guild.
    /// </summary>
    public static int Band { get; set; } = 10;

    /// <summary>
    /// The classes that count as being able to make things.
    ///
    /// <para>
    /// <b>A maker rather than a producer, and the difference matters.</b> <c>BotRole.Producer</c> also holds
    /// the gatherer, which digs ore and cuts wood and cannot turn either into a breastplate. Patrick's rule
    /// is that a guild must have somebody who can <em>equip</em> it, so the test is the smaller one: five
    /// bots on this shard qualify, which is what sets the ceiling on how many guilds there can be.
    /// </para>
    /// </summary>
    public static readonly string[] Makers = ["Crafter", "Architect"];

    /// <summary>
    /// The names a guild may have, in the order they are used.
    ///
    /// <para>
    /// <b>A fixed pool rather than invented names, and a hall is the reason.</b> A hall is found again after
    /// a restart by the name written on its sign (<c>BotEstate.Adopt</c>), so a population that invented
    /// fresh names every start would orphan every building it has ever raised. The four at the head of the
    /// list are the four the old caste system used, so the halls already standing are inherited rather than
    /// abandoned.
    /// </para>
    /// </summary>
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

    /// <summary>Bots enrolled.</summary>
    public static long Enrolled { get; private set; }

    /// <summary>Bots the rules could not place: no room, or no guild to put them in.</summary>
    public static long Unplaced { get; private set; }

    /// <summary>Guilds standing.</summary>
    public static int Count => _guilds.Count;

    /// <summary>The guilds themselves, for anything that has business with all of them at once.</summary>
    public static IEnumerable<Guild> Standing => _guilds.Values;

    /// <summary>
    /// The guild of this name, or null.
    ///
    /// Asked by name because a name is what survives: a hall's sign carries it across restarts, which is
    /// how <see cref="BotEstate"/> knows whose house it found. See <c>BotEstate.Adopt</c>.
    /// </summary>
    public static Guild Find(string name) =>
        !string.IsNullOrEmpty(name) && _guilds.TryGetValue(name, out var guild) && !guild.Disbanded
            ? guild
            : null;

    private static readonly Dictionary<string, Guild> _guilds = [];

    /// <summary>Whether the roster has been dealt out yet. Until it has, <see cref="Enrol"/> does nothing.</summary>
    private static bool _dealt;

    /// <summary>Whether this bot's class can make things. See <see cref="Makers"/>.</summary>
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

    /// <summary>
    /// Deals the whole population out into guilds. Called once, after the population has been raised.
    ///
    /// <para>
    /// <b>Once, and with the whole roster in view, because the rules cannot be applied one bot at a time.</b>
    /// "At least five, one of whom can make things" is a statement about a group; a bot arriving on its own
    /// cannot be told whether it satisfies it. The old version enrolled each bot as it was born, which is
    /// exactly why it could only ever sort them by a property each one had on its own — its trade.
    /// </para>
    /// </summary>
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

        // How many bands the rules allow, and it is the tightest of four numbers rather than the first one
        // that came to mind. Aim for Band each; never more bands than there are makers or names; never so
        // many that one would be under Least; and never so few that one would be over Most.
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

        // Dealt by role rather than in roster order, so every band comes out mixed: fighters, a medic, a
        // caster. Roster order is birth order, which is class order, which would put every archer in one
        // guild — the caste system again, wearing the new rules.
        rest.Sort((a, b) => ((int)(a.Class?.Role ?? BotRole.Producer)).CompareTo((int)(b.Class?.Role ?? BotRole.Producer)));

        var bench = new List<BotMobile>[bands];

        for (var i = 0; i < bands; i++)
        {
            bench[i] = [makers[i]];
        }

        // The makers left over are dealt like anybody else: a second smith in a guild is a good thing, it is
        // only the first one that is a rule.
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
                // Every band is full. Honest rather than silent: these bots keep to themselves and the
                // number is reported, because a population with a fifth of it outside every guild is a fact
                // about the ceiling rather than about the bots.
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
            "Guilds mustered: {Bands} of them out of {All} bots, {Makers} makers, {Unplaced} left out — {What}",
            bands,
            all,
            makers.Count,
            Unplaced,
            Describe()
        );
    }

    /// <summary>
    /// Makes one guild and puts the band in it.
    ///
    /// <para>
    /// <b>An existing guild of the same name is reused, and that is the whole of surviving a restart.</b>
    /// Guilds are world objects: they outlive the population, and the population is rebuilt from nothing
    /// every start. Making a fresh "The Hammer" each time would leave a graveyard of empty guilds in the
    /// save, one per restart, for ever. The engine counts a guild disbanded when its leader is deleted — so
    /// the first bot of a returning guild is made its leader, which revives it.
    /// </para>
    /// </summary>
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
            // Back from the save with a leader who no longer exists, and a membership list full of bots that
            // were deleted with the last population. Both are cleared out before the new band moves in.
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
            Enrolled++;
        }
    }

    /// <summary>
    /// One bot arriving after the roster was dealt: it joins the smallest guild with room.
    ///
    /// <para>
    /// Does nothing at all until <see cref="Muster"/> has run, and that is deliberate: during the opening
    /// burst every bot in the population is born within a second of every other, and a rule about groups
    /// cannot be applied to the first of them.
    /// </para>
    /// </summary>
    public static void Enrol(BotMobile bot)
    {
        if (!Running || !_dealt || bot is not { Deleted: false } || bot.Guild != null)
        {
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
        Enrolled++;
    }

    /// <summary>
    /// Whether these two are of the same guild.
    ///
    /// Asked of the engine's own record rather than of a table here: a bot's guild can be changed by
    /// anything, and two answers to one question is how this shard's oldest defects were built.
    /// </summary>
    public static bool Same(Mobile a, Mobile b) =>
        Running && a?.Guild != null && ReferenceEquals(a.Guild, b?.Guild);

    /// <summary>
    /// What a piece of work about <paramref name="other"/> is worth to <paramref name="bot"/>, given whose
    /// company they keep. One for a stranger, <see cref="Kinship"/> for one of your own.
    /// </summary>
    public static double Worth(Mobile bot, Mobile other) => Same(bot, other) ? Kinship : 1.0;

    /// <summary>
    /// The best maker in this guild — the one whose trade is to keep the rest of it equipped.
    ///
    /// <para>
    /// Patrick's order of 09.09.2026: <i>the crafter in a guild lives to equip its guildmates as well as it
    /// can</i>. This is who that is. Chosen by skill rather than by seniority, so a guild with two smiths
    /// sends orders to the better one.
    /// </para>
    /// </summary>
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

    /// <summary>
    /// What a member keeps back when its guild stands a cost for somebody else. See <see cref="Stand"/>.
    /// </summary>
    public static int Keep { get; set; } = 300;

    /// <summary>Coin the guilds have put into their own members' hands, and how often.</summary>
    public static long Stood { get; private set; }

    public static long Standings { get; private set; }

    /// <summary>Times a guild was asked to stand a cost and could not.</summary>
    public static long Cannot { get; private set; }

    /// <summary>
    /// The guild pays what one of its members cannot, so the member can buy what it needs.
    ///
    /// <para>
    /// <b>Patrick's order of 09.09.2026 — "the crafter in a guild lives to equip its guildmates" — carried
    /// out through the market rather than by hand.</b> The machinery for equipping a bot already exists and
    /// works end to end: <c>BotArmourer</c> asks what piece a bot most needs, raises an order for it, a
    /// crafter fills it, and <c>Rearm</c> puts it on. The only thing that ever stopped it was money — half
    /// this population has never held more than the four hundred it was born with, and armour is the first
    /// thing a bot buys that it does not need <em>today</em>.
    /// </para>
    ///
    /// <para>
    /// So a guild stands the difference. The coin goes into the member's account and the member buys its own
    /// armour, which means the order goes on the same board every crafter reads — and the crafter most
    /// likely to fill it is the guild's own, because a guildmate's order is worth <see cref="Kinship"/> more
    /// to it. The guild's money ends in the guild's maker's purse, having become a breastplate on the way.
    /// That is the whole of it, and not a line of new economy was needed for it.
    /// </para>
    ///
    /// <para>
    /// Taken richest first, each keeping <see cref="Keep"/> back, exactly as the hall levy does — and it
    /// never takes from the bot it is paying for, which would be a bot lending itself money.
    /// </para>
    /// </summary>
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
            // Hand back whatever was taken. A guild that half-pays for a hauberk has bought nothing and is
            // poorer, which is the one outcome worth more than the armour.
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

    /// <summary>One line for the shard's own summary.</summary>
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
                : $"; {Stood}gp stood for members {Standings} times, {Cannot} times a guild could not";

            var rule = $"{Least}-{Most} to a guild, one maker each";
            var short_ = makerless == 0 ? "" : $", {makerless} of them with nobody to make anything";

            return $"{_guilds.Count} guilds holding the population ({rule}{short_}), a guildmate worth ×{Kinship:F2}: {say.ToString()}{stood}";
        }
        finally
        {
            say.Dispose();
        }
    }

    /// <summary>
    /// A world reload is a different population.
    ///
    /// The guilds themselves are left alone: they are world objects, they will be found by name and revived
    /// when the new population is dealt out, and deleting them here would throw away the one thing about
    /// them worth keeping — that a guild on this island is older than any bot in it.
    /// </summary>
    public static void Forget()
    {
        _guilds.Clear();
        Enrolled = 0;
        Unplaced = 0;
        Stood = 0;
        Standings = 0;
        Cannot = 0;
        _dealt = false;
    }
}
