using System;
using System.Collections.Generic;
using Server.Guilds;
using Server.Items;
using Server.Logging;
using Server.Multis;
using Server.Regions;

namespace Server.BotAI.V2;

/// <summary>
/// The island's criminal record, and The Shadow: the sixth guild, of thieves and brigands, founded by murderers.
///
/// <para>
/// <b>Patrick's order of 17.09.2026, evening:</b> the most skilled and cunning murderers may found their own guild of
/// thieves and brigands, no more than five, whose thoughts and work are robbery, hiding, blackmail and killing; and the price
/// of it is prison and pursuit. His choices when asked: it is founded when two bots have killed from hiding, its master is
/// the better at hiding, and the master takes in other killers up to five; it has a secret hideout far outside the towns,
/// where its members rise and hide, and which the Baron raids once a caught thief gives it away (build 114, with the
/// blackmail); it is The Shadow [SHD].
/// </para>
///
/// <para>
/// <b>Not one of <see cref="BotGuilds"/>' guilds, and that is deliberate.</b> Those are dealt out round the crafters at every
/// boot, fifteen at most and five at least, led by their maker, and a guild with nothing to show loses a member a minute to
/// <c>BotGuilds.Review</c>; a band of five murderers with no hall and no crafter would be emptied by the machinery meant to
/// keep a trade guild honest. So The Shadow is an engine guild kept here: made at founding, re-made at every boot after the
/// muster from the names this keeps (<see cref="BotUnderworldStore"/>), its members taken out of whatever guild the muster
/// dealt them. The engine's guild is what makes them allies to each other and keeps a robber off its brother
/// (<c>BotRobber.Prey</c>).
/// </para>
///
/// <para>
/// <b>The record is kept by name</b>, because the population is raised afresh at every boot: murders, how many of them struck
/// from hiding, robberies carried through, times caught. Nothing else on the shard remembered a murder past its red hour.
/// </para>
/// </summary>
public static class BotUnderworld
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotUnderworld));

    public const string GuildName = "The Shadow";

    public const string GuildAbbreviation = "SHD";

    public static bool Running { get; set; } = true;

    public static int Founders { get; set; } = 2;

    public static int MostMembers { get; set; } = 5;

    public static int LeastUnseen { get; set; } = 0;

    public static int SweepMs { get; set; } = 60000;

    public static int RecruitEveryMs { get; set; } = 1800000;

    public static int HideoutLeastRoad { get; set; } = 250;

    public static int HideoutMostRoad { get; set; } = 520;

    public static double MemberRobChance { get; set; } = 0.5;

    public static int BanditEveryMs { get; set; } = 300000;

    public static long Outcasts { get; private set; }

    public static long Enmities { get; private set; }

    public static double ExtortShare { get; set; } = 0.5;

    public static double ExtortEdge { get; set; } = 0.6;

    public static int RaidTimeoutMs { get; set; } = 900000;

    public static int HeadFirst { get; set; } = 1000;

    public static int HeadNext { get; set; } = 500;

    public static int MemberPracticeEveryMs { get; set; } = 900000;

    /// <summary>One bot's record.</summary>
    public sealed class Sheet
    {
        public string Name;

        public int Murders;

        public int Unseen;

        public int Robberies;

        public int Extortions;

        public int Jailed;
    }

    private static readonly Dictionary<string, Sheet> _sheets = new(StringComparer.Ordinal);

    private static readonly List<string> _members = [];

    private static bool _founded;

    private static Point3D _hideout = Point3D.Zero;

    private static long _stashedTick;

    private static readonly Dictionary<string, int> _paid = [];

    private static long _fetchedTick;

    private static Guild _guild;

    private static long _sweptTick;

    private static long _recruitedTick;

    private static bool _recruitClock;

    private static bool _hideoutKnown;

    private static string _givenAwayBy;

    private static bool _raiding;

    private static long _raidTick;

    public static long GivenAway { get; private set; }

    public static long Burned { get; private set; }

    public static bool HideoutKnown => _hideoutKnown;

    public static long Founded { get; private set; }

    public static long Recruited { get; private set; }

    public static long Reformed { get; private set; }

    public static long NoHideout { get; private set; }

    public static bool Exists => _founded;

    public static Point3D Hideout => _hideout;

    public static bool Band(Guild guild) => _founded && _guild is { Disbanded: false } && ReferenceEquals(guild, _guild);

    public static bool Member(Mobile m) => _founded && _guild is { Disbanded: false } && m?.Guild != null && ReferenceEquals(m.Guild, _guild);

    public static bool Outlawed(Mobile m) => m != null && Of(m.Name) is { } sheet && sheet.Murders + sheet.Robberies > 0;

    public static BotMobile Near(Mobile from, int range)
    {
        if (!_founded || _guild is not { Disbanded: false } || from?.Map == null || _members.Count == 0 || Member(from))
        {
            return null;
        }

        for (var i = 0; i < _members.Count; i++)
        {
            if (Named(_members[i]) is not { Deleted: false, Alive: true, Hidden: false } one || one.Map != from.Map
                || !from.InRange(one.Location, range) || !from.CanSee(one) || BotOutlaw.Jailed(one))
            {
                continue;
            }

            return one;
        }

        return null;
    }

    public static long Dissolved { get; private set; }

    private static bool Anybody()
    {
        for (var i = 0; i < _members.Count; i++)
        {
            if (Named(_members[i]) != null)
            {
                return true;
            }

            var away = BotPopulation.Away;

            for (var k = 0; k < away.Count; k++)
            {
                if (away[k] is { Deleted: false } resting && resting.Name == _members[i])
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static int Bandits()
    {
        var count = 0;

        foreach (var (_, sheet) in _sheets)
        {
            if (sheet.Murders + sheet.Robberies > 0)
            {
                count++;
            }
        }

        return count;
    }

    public static Sheet Of(string name) => name != null && _sheets.TryGetValue(name, out var sheet) ? sheet : null;

    /// <summary>One bandit as the dashboard shows it: what is proved against it, what it is worth, and where it stands.</summary>
    public readonly record struct Wanted(
        string Name,
        string Trade,
        int Murders,
        int Unseen,
        int Robberies,
        int Extortions,
        int Price,
        bool Member,
        bool Fence,
        bool Red,
        bool Hunted,
        bool Known,
        Point3D Where,
        Map Map
    );

    public static List<Wanted> AtLarge()
    {
        List<Wanted> roll = [];
        var bots = BotPopulation.Bots;

        for (var i = 0; i < bots.Count; i++)
        {
            var bot = bots[i];

            if (bot is not { Deleted: false } || BotOutlaw.Jailed(bot))
            {
                continue;
            }

            var member = Member(bot);

            if (!member && !BotOutlaw.Outlaw(bot))
            {
                continue;
            }

            var sheet = Of(bot.Name);

            roll.Add(
                new Wanted(
                    bot.Name,
                    bot.Class?.Name ?? "?",
                    sheet?.Murders ?? 0,
                    sheet?.Unseen ?? 0,
                    sheet?.Robberies ?? 0,
                    sheet?.Extortions ?? 0,
                    Price(bot),
                    member,
                    BotFence.Is(bot),
                    BotOutlaw.IsRed(bot),
                    BotOutlaw.IsWanted(bot),
                    BotOutlaw.Known(bot),
                    bot.Location,
                    bot.Map
                )
            );
        }

        roll.Sort((a, b) => b.Price != a.Price ? b.Price.CompareTo(a.Price) : b.Murders.CompareTo(a.Murders));

        return roll;
    }

    public static int Price(Mobile m)
    {
        var murders = Of(m?.Name)?.Murders ?? 0;

        if (m?.Name != null && _paid.TryGetValue(m.Name, out var already) && murders <= already)
        {
            return 0;
        }

        return murders <= 0 ? 0 : HeadFirst + HeadNext * (murders - 1);
    }

    public static void Sold(Mobile m)
    {
        if (m?.Name is { } name)
        {
            _paid[name] = Of(name)?.Murders ?? 0;
        }
    }

    private static Sheet Sheeted(Mobile m)
    {
        if (m?.Name is not { } name)
        {
            return null;
        }

        if (!_sheets.TryGetValue(name, out var sheet))
        {
            _sheets[name] = sheet = new Sheet { Name = name };
        }

        return sheet;
    }

    public static void Murder(Mobile killer, bool unseen)
    {
        if (Sheeted(killer) is not { } sheet)
        {
            return;
        }

        sheet.Murders++;

        if (unseen)
        {
            sheet.Unseen++;
        }

        Outcast(killer);
    }

    public static void Robbed(Mobile robber)
    {
        if (Sheeted(robber) is { } sheet)
        {
            sheet.Robberies++;

            Outcast(robber);
        }
    }

    private static void Outcast(Mobile m)
    {
        if (!Running || m is not BotMobile { Deleted: false } bot || Member(bot))
        {
            return;
        }

        if (bot.Guild is Guild old && !ReferenceEquals(old, _guild))
        {
            Leave(bot);
            Outcasts++;

            logger.Information("{Bot} walks out of {Guild}: a bandit keeps no honest company", bot.Name, old.Name);
        }

        if (!_founded || !Anybody())
        {
            TryFound();

            return;
        }

        if (_guild is { Disbanded: false } && Thieves < MostMembers && Fit(bot))
        {
            Join(bot);
            Recruited++;

            logger.Information("{Bot} is taken into The Shadow at once, {Count} thieves of {Most}", bot.Name, Thieves, MostMembers);
        }
    }

    public static void EnemyOfAll()
    {
        if (!Running || !_founded || _guild is not { Disbanded: false } band)
        {
            return;
        }

        var made = 0;

        foreach (var guild in BotGuilds.Standing)
        {
            if (guild == null || guild.Disbanded || ReferenceEquals(guild, band) || band.IsWar(guild))
            {
                continue;
            }

            band.AddEnemy(guild);
            made++;
        }

        if (made > 0)
        {
            Enmities += made;

            logger.Information("The Shadow is set against {Count} guilds as their enemy; every guild on the island now counts it one", made);
        }
    }

    public static void Burgled(Mobile burglar)
    {
        if (Sheeted(burglar) is { } sheet)
        {
            sheet.Robberies++;

            Outcast(burglar);
        }
    }

    public static void Jailed(Mobile bot)
    {
        if (Sheeted(bot) is { } sheet)
        {
            sheet.Jailed++;
        }

        if (Member(bot) && !BotFence.Is(bot) && _hideout != Point3D.Zero && !_hideoutKnown)
        {
            _hideoutKnown = true;
            _givenAwayBy = bot.Name;
            GivenAway++;

            logger.Information(
                "{Name}, caught, gives The Shadow's hideout at ({X}, {Y}) away to the Baron",
                bot.Name,
                _hideout.X,
                _hideout.Y
            );
        }
    }

    public static bool RaidDue(out Point3D at)
    {
        at = _hideout;

        return Running && _founded && _hideoutKnown && _hideout != Point3D.Zero
               && !(_raiding && Core.TickCount - _raidTick < RaidTimeoutMs);
    }

    public static void RaidBegun()
    {
        _raiding = true;
        _raidTick = Core.TickCount;
    }

    public static void RaidEnded(string by, int found)
    {
        logger.Information(
            "{By}'s raid searched The Shadow's hideout at ({X}, {Y}), given away by {Informer}, and found {Found} there; the hideout is burned, and The Shadow will find another",
            by,
            _hideout.X,
            _hideout.Y,
            _givenAwayBy ?? "somebody",
            found
        );

        _raiding = false;
        _hideoutKnown = false;
        _givenAwayBy = null;
        _hideout = Point3D.Zero;
        Burned++;
        BotLair.Burn();
    }

    public static void Extorted(Mobile thief)
    {
        if (Sheeted(thief) is { } sheet)
        {
            sheet.Extortions++;
        }
    }

    public static void Forget()
    {
        _sheets.Clear();
        _members.Clear();
        _paid.Clear();

        _founded = false;
        _guild = null;

        BotBurgle.Forget();
    }

    public static void Reform()
    {
        if (!Running || !_founded || _members.Count == 0)
        {
            return;
        }

        List<BotMobile> band = [];

        for (var i = 0; i < _members.Count; i++)
        {
            if (Named(_members[i]) is { } bot)
            {
                band.Add(bot);
            }
        }

        if (band.Count == 0)
        {
            logger.Information("None of The Shadow's {Count} were raised this boot; the guild waits for them", _members.Count);

            return;
        }

        Form(band);
        Reformed += band.Count;

        logger.Information(
            "The Shadow is back together: {Members}, led by {Master}; its hideout at ({X}, {Y})",
            string.Join(", ", _members),
            band[0].Name,
            _hideout.X,
            _hideout.Y
        );
    }

    public static void Beat(long now)
    {
        if (!Running || now - _sweptTick < SweepMs)
        {
            return;
        }

        _sweptTick = now;

        if (!_founded)
        {
            TryFound();

            return;
        }

        if (!Anybody())
        {
            _founded = false;
            _guild = null;
            _members.Clear();
            Dissolved++;

            logger.Information("The Shadow has nobody left on its roll and is dissolved; the next {Founders} bandits found it again", Math.Max(1, Founders));

            TryFound();

            return;
        }

        EnemyOfAll();

        if (_hideout == Point3D.Zero)
        {
            ChooseHideout();
        }
        else if (BotLair.Fire == null && !BotLair.Rebind(BotPopulation.Home, _hideout))
        {
            BotLair.Pitch(BotPopulation.Home, _hideout);
        }

        if (now - _stashedTick >= BotStash.EveryMs)
        {
            _stashedTick = now;
            Stash();
        }

        if (now - _fetchedTick >= BotFetch.EveryMs)
        {
            _fetchedTick = now;
            Fetch();
            Draw();
        }

        if (!_recruitClock)
        {
            _recruitClock = true;
            _recruitedTick = now;

            return;
        }

        if (now - _recruitedTick >= RecruitEveryMs)
        {
            _recruitedTick = now;
            TryRecruit();
        }
    }

    public static long Sweeps { get; private set; }

    public static long NoChest { get; private set; }

    public static long Unfit { get; private set; }

    public static long Busy { get; private set; }

    public static long Light { get; private set; }

    public static long Unready { get; private set; }

    public static long Sent { get; private set; }

    private static void Stash()
    {
        Sweeps++;

        if (BotLair.Chest is not { } chest || chest.Map is not { } map || map == Map.Internal)
        {
            NoChest++;

            return;
        }

        var at = chest.GetWorldLocation();
        var bots = BotPopulation.Bots;

        for (var i = 0; i < bots.Count; i++)
        {
            var bot = bots[i];

            if (bot is not { Deleted: false, Alive: true, Fallen: false } || bot.Map != map || !Member(bot)
                || BotOutlaw.Jailed(bot) || bot.Squad != null)
            {
                Unfit += Member(bot) ? 1 : 0;

                continue;
            }

            if (bot.Resolve?.Deed is BotStash or BotRob or BotBrawl or BotManhunt or BotSentence or BotLieLow)
            {
                Busy++;

                continue;
            }

            if ((bot.Backpack?.GetAmount(typeof(Gold)) ?? 0) - BotPurse.Keeps(bot) < BotStash.Least
                && !BotFence.Carrying(bot))
            {
                Light++;

                continue;
            }

            if (BotLadder.Standing(bot) is not (BotStanding.Free or BotStanding.Busy))
            {
                Unready++;

                continue;
            }

            Sent++;

            BotWill.Press(bot, new BotStash(map, at), BotFence.Is(bot) ? "the band's order to put in the chest" : "the takings are heavy");
        }
    }

    private static void Fetch()
    {
        if (BotLair.Chest is not { } chest || chest.Map is not { } map || map == Map.Internal)
        {
            return;
        }

        if (BotFence.Who is not { Deleted: false, Alive: true, Fallen: false } fence || fence.Map != map
            || fence.Squad != null || BotOutlaw.Jailed(fence))
        {
            return;
        }

        if (fence.Resolve?.Deed is BotFetch or BotStash or BotBrawl or BotSilence or BotHoleUp)
        {
            return;
        }

        if (BotOutlaw.IsWanted(fence) || BotOutlaw.IsRed(fence))
        {
            return;
        }

        var goods = 0;

        for (var i = 0; i < chest.Items.Count; i++)
        {
            if (chest.Items[i] is { Deleted: false, Movable: true } and not Gold)
            {
                goods++;
            }
        }

        if (goods < BotFetch.Least || BotLadder.Standing(fence) is not (BotStanding.Free or BotStanding.Busy))
        {
            return;
        }

        BotWill.Press(fence, new BotFetch(map, chest.GetWorldLocation()), $"{goods} things of the band's to sell");
    }

    private static void Draw()
    {
        if (BotLair.Chest is not { } chest || chest.Map is not { } map || map == Map.Internal || chest.Items.Count == 0)
        {
            return;
        }

        var at = chest.GetWorldLocation();
        var bots = BotPopulation.Bots;

        for (var i = 0; i < bots.Count; i++)
        {
            var bot = bots[i];

            if (bot is not { Deleted: false, Alive: true, Fallen: false } || bot.Map != map || !Member(bot)
                || BotFence.Is(bot) || BotOutlaw.Jailed(bot) || bot.Squad != null)
            {
                continue;
            }

            if (bot.Resolve?.Deed is BotFetch or BotStash or BotRob or BotBrawl or BotManhunt or BotSentence or BotLieLow)
            {
                continue;
            }

            if (BotLadder.Standing(bot) is not (BotStanding.Free or BotStanding.Busy))
            {
                continue;
            }

            var wanting = 0;
            var kept = BotUnload.Keeps(bot);
            var pack = bot.Backpack;

            if (pack == null)
            {
                continue;
            }

            for (var j = 0; j < chest.Items.Count && wanting == 0; j++)
            {
                if (chest.Items[j] is { Deleted: false, Movable: true } item && item is not Gold
                    && kept.TryGetValue(item.GetType(), out var wants) && pack.GetAmount(item.GetType()) < wants)
                {
                    wanting++;
                }
            }

            if (wanting == 0)
            {
                continue;
            }

            BotWill.Press(bot, new BotFetch(map, at, true), "short of its kit, and the towns are shut to it");
        }
    }

    private static bool Fit(BotMobile bot)
    {
        if (bot is not { Deleted: false } || bot.Class is not { } klass || Of(bot.Name) is not { } sheet
            || sheet.Murders + sheet.Robberies < 1 || sheet.Unseen < LeastUnseen)
        {
            return false;
        }

        if (klass is BotSage or BotArchitect)
        {
            return false;
        }

        return klass.Role is not (BotRole.Producer or BotRole.Medic) && !klass.Unpaid && !klass.Leads
               && bot.Squad == null;
    }

    private static void TryFound()
    {
        List<BotMobile> fit = [];
        var bots = BotPopulation.Bots;

        for (var i = 0; i < bots.Count; i++)
        {
            if (Fit(bots[i]))
            {
                fit.Add(bots[i]);
            }
        }

        if (fit.Count < Math.Max(1, Founders))
        {
            return;
        }

        if (!ChooseHideout())
        {
            NoHideout++;

            return;
        }

        fit.Sort((a, b) => b.Skills.Hiding.Base.CompareTo(a.Skills.Hiding.Base));

        if (fit.Count > MostMembers)
        {
            fit.RemoveRange(MostMembers, fit.Count - MostMembers);
        }

        _founded = true;
        Founded++;
        Form(fit);

        List<string> founders = [];

        for (var i = 0; i < fit.Count; i++)
        {
            founders.Add($"{fit[i].Name} (Hiding {fit[i].Skills.Hiding.Base:F1}, {Of(fit[i].Name)?.Unseen} killed from hiding)");
        }

        logger.Information(
            "The Shadow is founded by {Founders}, led by {Master}; its hideout is at ({X}, {Y}), {Road} steps of road from home",
            string.Join(", ", founders),
            fit[0].Name,
            _hideout.X,
            _hideout.Y,
            BotRoads.FromHome(BotPopulation.Home, _hideout.X, _hideout.Y)
        );
    }

    private static void TryRecruit()
    {
        if (Thieves >= MostMembers || Named(_members.Count > 0 ? _members[0] : null) is not { Alive: true } master)
        {
            return;
        }

        BotMobile best = null;
        var bots = BotPopulation.Bots;

        for (var i = 0; i < bots.Count; i++)
        {
            var bot = bots[i];

            if (Member(bot) || !Fit(bot))
            {
                continue;
            }

            if (best == null || bot.Skills.Hiding.Base > best.Skills.Hiding.Base)
            {
                best = bot;
            }
        }

        if (best == null)
        {
            return;
        }

        Join(best);
        Recruited++;

        logger.Information(
            "{Master} takes {Bot} into The Shadow (Hiding {Hiding:F1}, {Unseen} killed from hiding), {Count} thieves of {Most}",
            master.Name,
            best.Name,
            best.Skills.Hiding.Base,
            Of(best.Name)?.Unseen,
            Thieves,
            MostMembers
        );
    }

    private static void Form(List<BotMobile> band)
    {
        for (var i = 0; i < band.Count; i++)
        {
            Leave(band[i]);
        }

        var master = band[0];
        var guild = BaseGuild.FindByName(GuildName) as Guild;

        if (guild == null || guild.Disbanded)
        {
            guild = new Guild(master, GuildName, GuildAbbreviation)
            {
                Type = GuildType.Regular,
                Charter = "Nobody's business."
            };
        }
        else
        {
            guild.Leader = master;

            for (var i = guild.Members.Count - 1; i >= 0; i--)
            {
                if (guild.Members[i] is not BotMobile { Deleted: false })
                {
                    guild.RemoveMember(guild.Members[i]);
                }
            }
        }

        BotGuilds.Stone(guild);

        _guild = guild;
        _members.Clear();

        for (var i = 0; i < band.Count; i++)
        {
            guild.AddMember(band[i]);
            band[i].DisplayGuildTitle = true;
            _members.Add(band[i].Name);
            Picks(band[i]);
        }

        EnemyOfAll();
    }

    public static bool Take(BotMobile bot)
    {
        if (!_founded || _guild is not { Disbanded: false } || bot is not { Deleted: false, Alive: true }
            || Member(bot) || _members.Contains(bot.Name))
        {
            return false;
        }

        Join(bot);

        return true;
    }

    public static int Thieves
    {
        get
        {
            var count = _members.Count;

            return BotFence.Who != null ? Math.Max(0, count - 1) : count;
        }
    }

    private static void Join(BotMobile bot)
    {
        Leave(bot);
        _guild.AddMember(bot);
        bot.DisplayGuildTitle = true;
        _members.Add(bot.Name);
        Picks(bot);
    }

    private static void Picks(BotMobile bot)
    {
        if (bot?.Backpack == null || BotBurgle.PicksIssued <= 0 || BotBurgle.HasPick(bot))
        {
            return;
        }

        var picks = new Lockpick(BotBurgle.PicksIssued);

        if (!bot.Backpack.TryDropItem(bot, picks, false))
        {
            picks.Delete();
        }
    }

    private static void Leave(BotMobile bot)
    {
        if (bot.Guild is Guild old && !ReferenceEquals(old, _guild))
        {
            old.RemoveMember(bot);
            bot.DisplayGuildTitle = false;
        }
    }

    private static bool ChooseHideout()
    {
        if (_hideout != Point3D.Zero)
        {
            return true;
        }

        var map = BotPopulation.Home;

        if (map == null || map == Map.Internal || !BotRoads.Ready)
        {
            return false;
        }

        var home = BotPopulation.Where;

        for (var i = 0; i < 400; i++)
        {
            var angle = Utility.RandomDouble() * Math.PI * 2.0;
            var span = HideoutLeastRoad * 0.7 + Utility.RandomDouble() * (HideoutMostRoad - HideoutLeastRoad * 0.7);
            var x = home.X + (int)Math.Round(Math.Cos(angle) * span);
            var y = home.Y + (int)Math.Round(Math.Sin(angle) * span);

            if (x < 0 || y < 0 || x >= map.Width || y >= map.Height || !BotStep.Settle(map, x, y, out var z))
            {
                continue;
            }

            var road = BotRoads.FromHome(map, x, y);

            if (road < HideoutLeastRoad || road > HideoutMostRoad)
            {
                continue;
            }

            var region = Region.Find(new Point3D(x, y, z), map);

            if (region.IsPartOf<GuardedRegion>() || region.IsPartOf<HouseRegion>() || region.IsPartOf<DungeonRegion>()
                || region.IsPartOf<JailRegion>())
            {
                continue;
            }

            _hideout = new Point3D(x, y, z);
            BotLair.Pitch(map, _hideout);

            return true;
        }

        return false;
    }

    private static BotMobile Named(string name)
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
        var murderers = 0;
        var unseen = 0;

        foreach (var (_, sheet) in _sheets)
        {
            if (sheet.Murders > 0)
            {
                murderers++;
            }

            if (sheet.Unseen > 0)
            {
                unseen++;
            }
        }

        var extortions = 0;

        foreach (var (_, sheet) in _sheets)
        {
            extortions += sheet.Extortions;
        }

        var guild = !_founded
            ? $"The Shadow is not founded ({Bandits()} of the {Math.Max(1, Founders)} bandits it wants)"
            : $"The Shadow, the enemy of every guild ({Enmities} set against it): {string.Join(", ", _members)}, hideout at ({_hideout.X}, {_hideout.Y}){(_hideoutKnown ? $", given away by {_givenAwayBy}" : "")}; founded {Founded} times, {Recruited} taken in, {Reformed} put back at a boot, {GivenAway} hideouts given away and {Burned} burned";

        return $"{_sheets.Count} bots on record, {murderers} of them murderers and {unseen} killers from hiding, {extortions} demands paid on record, {Outcasts} walked out of their guilds for it; {guild}; {BotFence.Describe()}; {BotSilence.Describe()}; {BotLair.Describe()}; {BotStash.Describe()}; {Sweeps} sweeps for a walk out ({NoChest} found no chest, {Busy} members at the band's own business, {Light} carrying nothing worth the walk, {Unready} not free to go, {Sent} sent); {BotFetch.Describe()}; {BotRaid.Describe()}; {BotBurgle.Describe()}";
    }

    internal static void Save(IGenericWriter writer)
    {
        writer.Write(_founded);
        writer.Write(_hideout);
        writer.Write(_hideoutKnown);
        writer.Write(_givenAwayBy ?? "");
        writer.WriteEncodedInt(_members.Count);

        for (var i = 0; i < _members.Count; i++)
        {
            writer.Write(_members[i]);
        }

        writer.WriteEncodedInt(_sheets.Count);

        foreach (var (_, sheet) in _sheets)
        {
            writer.Write(sheet.Name);
            writer.WriteEncodedInt(sheet.Murders);
            writer.WriteEncodedInt(sheet.Unseen);
            writer.WriteEncodedInt(sheet.Robberies);
            writer.WriteEncodedInt(sheet.Extortions);
            writer.WriteEncodedInt(sheet.Jailed);
        }
    }

    internal static void Load(IGenericReader reader)
    {
        _founded = reader.ReadBool();
        _hideout = reader.ReadPoint3D();
        _hideoutKnown = reader.ReadBool();
        _givenAwayBy = reader.ReadString();

        if (string.IsNullOrEmpty(_givenAwayBy))
        {
            _givenAwayBy = null;
        }

        var members = reader.ReadEncodedInt();

        _members.Clear();

        for (var i = 0; i < members; i++)
        {
            _members.Add(reader.ReadString());
        }

        var sheets = reader.ReadEncodedInt();

        _sheets.Clear();

        for (var i = 0; i < sheets; i++)
        {
            var sheet = new Sheet
            {
                Name = reader.ReadString(),
                Murders = reader.ReadEncodedInt(),
                Unseen = reader.ReadEncodedInt(),
                Robberies = reader.ReadEncodedInt(),
                Extortions = reader.ReadEncodedInt(),
                Jailed = reader.ReadEncodedInt()
            };

            if (!string.IsNullOrEmpty(sheet.Name))
            {
                _sheets[sheet.Name] = sheet;
            }
        }
    }
}
