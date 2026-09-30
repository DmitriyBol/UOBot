using System;
using System.Collections.Generic;
using Server.Guilds;
using Server.Logging;

namespace Server.BotAI.V2;

/// <summary>
/// Where each bot lives: the town it was born in or chose, and what "home" means to it there.
///
/// <para>
/// <b>Patrick's order of 29.09.2026, point 5: "teach the bots to choose a town of residence, live there, hunt there, do
/// their routine — all the same things they do in Britain. The result: the bots spread over the world."</b> Until now the
/// population had one home, <c>BotPopulation.Where</c> by Britain's graveyard, and every per-bot "home" in the code asked
/// it: the walk home, the flight's last resort, the herb woods, the hunting darts, where a newborn appears and where the
/// dead rise. At the boot of 18:38 on 29.09.2026 sixty of sixty-three bots stood in four guilds, three of the four had no
/// seat ("The Crown has no seat, The Hammer has no seat, The Blade has no seat, The Needle at 2248,1204"), so everybody but
/// The Needle's fifteen lived, was born and rose within 350 tiles of one tile.
/// </para>
///
/// <para>
/// <b>One question, one answer: <c>BotPopulation.HomeOf</c>, which asks <see cref="Home"/>.</b> A member of a guild seated
/// abroad lives by its guild (<see cref="BotRelocate.Forced"/>) as it did before; everybody else has a town of its own,
/// chosen at birth or at first sight (<see cref="BotSettle.Choose"/>) and changed only by walking there
/// (<see cref="BotResettle"/>). In the population's home town "home" is exactly what it was — the guild's hall or seat,
/// else the population's home — so a bot that lives in Britain is the bot it was before this file existed. In any other
/// town home is the town's bank (<see cref="BotSettle.Hearth"/>), or the guild's own hall or seat when that stands in the
/// same town.
/// </para>
///
/// <para>
/// <b>Filed by name, as what a bot has learned is filed (<c>BotProgress</c>), and kept across restarts
/// (<see cref="BotResidenceStore"/>)</b>: a bot that lives in Trinsic still lives there after a deploy, and a bot raised
/// under a name the store knows is born in that name's town, as it is handed that name's learning. The record is also held
/// on the body (<c>BotMobile.Residence</c>), so asking where a bot lives — which every sampler does per candidate through
/// the leash — costs a field read and nothing else.
/// </para>
/// </summary>
public static class BotResidence
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotResidence));

    /// <summary>What one bot's residence is. Filed by the bot's name; the session half is never saved.</summary>
    public sealed class Record
    {
        public string Name;

        public string Town;

        public DateTime Since;

        public int Moves;

        public string Left;

        internal BotTowns.Town Resolved;

        internal BotTowns.Town Pending;

        internal string PendingWhy;

        internal BotRelocate.Cause PendingCause;

        internal long PendingTick;

        internal long WindowTick;

        internal int WindowWealth;

        internal double WindowProgress;

        internal long SeenTick;

        internal bool Watched;

        internal readonly List<long> Deaths = [];

        internal bool HomeKnown;

        internal long HomeTick;

        internal Point3D HomeAt;
    }

    public static bool Running { get; set; } = true;

    public static bool Active { get; private set; }

    public static int CacheMs { get; set; } = 5000;

    public static int TownScatter { get; set; } = 40;

    public static long PlacedInTown { get; private set; }

    public static long Born { get; private set; }

    public static long BornAbroad { get; private set; }

    public static int ReadBack { get; private set; }

    public static long Adopted { get; private set; }

    public static long DiedAtHome { get; private set; }

    private static readonly Dictionary<string, Record> _byName = new(StringComparer.OrdinalIgnoreCase);

    private static readonly List<(BotMobile Bot, long Tick)> _unhomed = [];

    internal static List<(BotMobile Bot, long Tick)> Unhomed => _unhomed;

    private static readonly List<Point3D> _occupied = [];

    public static Point3D Home(BotMobile bot)
    {
        if (!Active || bot?.Residence is not { Town: not null } rec)
        {
            return BotSeat.Home(bot);
        }

        var now = Core.TickCount;
        var alive = bot.Alive;

        if (alive && rec.HomeKnown && now - rec.HomeTick < CacheMs)
        {
            return rec.HomeAt;
        }

        var seat = BotSeat.Home(bot);
        var town = TownOf(rec);
        Point3D at;

        if (town == null || town.Home || ReferenceEquals(town, BotSettle.HomeTown) || BotUnderworld.Member(bot))
        {
            at = seat;
        }
        else if (seat != BotPopulation.Where && BotTowns.Nearest(seat) == town)
        {
            at = seat;
        }
        else
        {
            at = BotSettle.Hearth(town);
        }

        if (alive)
        {
            rec.HomeAt = at;
            rec.HomeTick = now;
            rec.HomeKnown = true;
        }

        return at;
    }

    public static BotTowns.Town Of(BotMobile bot) => Active ? TownOf(bot?.Residence) : null;

    public static string NameOf(BotMobile bot) => Active ? bot?.Residence?.Town : null;

    internal static BotTowns.Town TownOf(Record rec)
    {
        if (rec?.Town == null || !BotTowns.Surveyed)
        {
            return null;
        }

        if (rec.Resolved == null || !string.Equals(rec.Resolved.Name, rec.Town, StringComparison.OrdinalIgnoreCase))
        {
            rec.Resolved = BotTowns.Find(rec.Town);
        }

        return rec.Resolved;
    }

    internal static Record Adopt(BotMobile bot)
    {
        if (bot == null)
        {
            return null;
        }

        if (bot.Residence != null)
        {
            return bot.Residence;
        }

        if (string.IsNullOrEmpty(bot.Name) || !_byName.TryGetValue(bot.Name, out var rec))
        {
            return null;
        }

        bot.Residence = rec;
        Adopted++;

        return rec;
    }

    internal static Record File(BotMobile bot, BotTowns.Town town)
    {
        var rec = Adopt(bot);

        if (rec == null)
        {
            rec = new Record { Name = bot.Name };
            bot.Residence = rec;

            if (!string.IsNullOrEmpty(bot.Name))
            {
                _byName[bot.Name] = rec;
            }
        }

        rec.Town = town?.Name;
        rec.Resolved = town;
        rec.Since = Core.Now;
        rec.HomeKnown = false;

        return rec;
    }

    public static void Raised(BotMobile bot)
    {
        if (bot == null)
        {
            return;
        }

        var rec = Adopt(bot);

        if (!Active || !BotSettle.Reckoned)
        {
            _unhomed.Add((bot, Core.TickCount));

            return;
        }

        if (rec?.Town != null && TownOf(rec) != null)
        {
            return;
        }

        var town = BotRelocate.Forced(bot) ?? BotSettle.Choose(bot, null, false, null, null, out _) ?? BotSettle.HomeTown;

        if (town == null)
        {
            return;
        }

        File(bot, town);
        Born++;

        if (!ReferenceEquals(town, BotSettle.HomeTown))
        {
            BornAbroad++;
        }

        logger.Information(
            "{Name} the {Class} is born in {Town}, where {Residents} others of the population live",
            bot.Name,
            bot.Class?.Name,
            town.Name,
            Residents(town)
        );
    }

    public static void Fell(BotMobile bot, Point3D at)
    {
        if (!Active || bot?.Residence is not { Town: not null } rec)
        {
            return;
        }

        if (!Utility.InRange(Home(bot), at, BotPopulation.Leash(bot)))
        {
            return;
        }

        rec.Deaths.Add(Core.TickCount);
        DiedAtHome++;
    }

    public static void Placed(BotMobile bot, Point3D at)
    {
        if (at == BotSeat.Home(bot))
        {
            BotSeat.Placed();

            return;
        }

        PlacedInTown++;
    }

    public static int Span(Point3D at, int otherwise) =>
        Active && BotSettle.IsHearth(at) ? TownScatter

        : BotGuildHouses.IsHeart(at) ? BotGuildHouses.Spread
        : otherwise;

    public static bool Around(Point3D where)
    {
        if (!Active)
        {
            return false;
        }

        for (var i = 0; i < _occupied.Count; i++)
        {
            if (Utility.InRange(_occupied[i], where, BotTowns.Roam))
            {
                return true;
            }
        }

        return false;
    }

    public static bool Homely(Point3D where, int half)
    {
        if (!Active)
        {
            return false;
        }

        for (var i = 0; i < _occupied.Count; i++)
        {
            var at = _occupied[i];

            if (Math.Max(Math.Abs(at.X - where.X), Math.Abs(at.Y - where.Y)) <= half)
            {
                return true;
            }
        }

        return false;
    }

    internal static int Tally(IReadOnlyList<BotTowns.Town> towns, Span<int> residents, Span<int> mates, Guild guild, out int guildSize)
    {
        var present = 0;

        guildSize = 0;
        residents.Clear();
        mates.Clear();

        var bots = BotPopulation.Bots;

        for (var i = 0; i < bots.Count; i++)
        {
            Count(bots[i], towns, residents, mates, guild, ref present, ref guildSize);
        }

        var away = BotPopulation.Away;

        for (var i = 0; i < away.Count; i++)
        {
            Count(away[i], towns, residents, mates, guild, ref present, ref guildSize);
        }

        return present;
    }

    private static void Count(
        BotMobile bot, IReadOnlyList<BotTowns.Town> towns, Span<int> residents, Span<int> mates, Guild guild, ref int present,
        ref int guildSize
    )
    {
        if (bot is not { Deleted: false })
        {
            return;
        }

        present++;

        var mine = guild != null && ReferenceEquals(bot.Guild, guild);

        if (mine)
        {
            guildSize++;
        }

        var rec = bot.Residence;
        var town = rec?.Pending ?? TownOf(rec);

        if (town == null)
        {
            return;
        }

        for (var i = 0; i < towns.Count; i++)
        {
            if (!ReferenceEquals(towns[i], town))
            {
                continue;
            }

            residents[i]++;

            if (mine)
            {
                mates[i]++;
            }

            return;
        }
    }

    public static int Residents(BotTowns.Town town)
    {
        if (!Active || town == null)
        {
            return 0;
        }

        var many = 0;
        var bots = BotPopulation.Bots;

        for (var i = 0; i < bots.Count; i++)
        {
            if (bots[i] is { Deleted: false } bot && ReferenceEquals(TownOf(bot.Residence), town))
            {
                many++;
            }
        }

        var away = BotPopulation.Away;

        for (var i = 0; i < away.Count; i++)
        {
            if (away[i] is { Deleted: false } bot && ReferenceEquals(TownOf(bot.Residence), town))
            {
                many++;
            }
        }

        return many;
    }

    internal static void Refresh()
    {
        _occupied.Clear();

        var bots = BotPopulation.Bots;

        for (var i = 0; i < bots.Count; i++)
        {
            Note(bots[i]);
        }

        var away = BotPopulation.Away;

        for (var i = 0; i < away.Count; i++)
        {
            Note(away[i]);
        }
    }

    private static void Note(BotMobile bot)
    {
        if (bot is not { Deleted: false, Residence: { } rec })
        {
            return;
        }

        if (!string.IsNullOrEmpty(bot.Name) && !string.Equals(rec.Name, bot.Name, StringComparison.OrdinalIgnoreCase))
        {
            if (rec.Name != null && _byName.TryGetValue(rec.Name, out var old) && ReferenceEquals(old, rec))
            {
                _byName.Remove(rec.Name);
            }

            rec.Name = bot.Name;
            _byName[bot.Name] = rec;
        }

        var town = TownOf(rec);

        if (town == null || town.Home)
        {
            return;
        }

        var hearth = BotSettle.Hearth(town);

        if (!_occupied.Contains(hearth))
        {
            _occupied.Add(hearth);
        }
    }

    internal static void Start()
    {
        Active = Running;

        if (!Active)
        {
            _unhomed.Clear();

            return;
        }

        var bots = BotPopulation.Bots;

        for (var i = 0; i < bots.Count; i++)
        {
            Adopt(bots[i]);
        }

        var away = BotPopulation.Away;

        for (var i = 0; i < away.Count; i++)
        {
            Adopt(away[i]);
        }

        Refresh();
    }

    public static string Describe()
    {
        if (!Active)
        {
            return "every bot lives where its guild or the population does";
        }

        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var none = 0;

        Gather(BotPopulation.Bots, counts, ref none);
        Gather(BotPopulation.Away, counts, ref none);

        var sorted = new List<KeyValuePair<string, int>>(counts);
        sorted.Sort((a, b) => b.Value != a.Value ? b.Value.CompareTo(a.Value) : string.CompareOrdinal(a.Key, b.Key));

        using var line = Server.Text.ValueStringBuilder.Create(512);

        for (var i = 0; i < sorted.Count; i++)
        {
            line.Append(i > 0 ? ", " : "");
            line.Append($"{sorted[i].Key} {sorted[i].Value}");
        }

        if (none > 0)
        {
            line.Append(sorted.Count > 0 ? ", " : "");
            line.Append($"{none} with no town yet");
        }

        line.Append(
            $"; {Born} born with a town ({BornAbroad} away from home), {PlacedInTown} put down in a town of their own, {Adopted} records carried back to their names ({ReadBack} read off the save); {DiedAtHome} deaths where the bot lives; "
        );
        line.Append(BotRelocate.Describe());
        line.Append("; ");
        line.Append(BotMover.Describe());
        line.Append("; ");
        line.Append(BotSettle.Describe());

        return line.ToString();
    }

    private static void Gather(IReadOnlyList<BotMobile> bots, Dictionary<string, int> counts, ref int none)
    {
        for (var i = 0; i < bots.Count; i++)
        {
            if (bots[i] is not { Deleted: false } bot)
            {
                continue;
            }

            var town = TownOf(bot.Residence);

            if (town == null)
            {
                none++;

                continue;
            }

            counts[town.Name] = counts.GetValueOrDefault(town.Name) + 1;
        }
    }

    public static void Forget()
    {
        Active = false;
        PlacedInTown = 0;
        Born = 0;
        BornAbroad = 0;
        Adopted = 0;
        DiedAtHome = 0;
        _unhomed.Clear();
        _occupied.Clear();

        foreach (var (_, rec) in _byName)
        {
            rec.Resolved = null;
            rec.Pending = null;
            rec.HomeKnown = false;
            rec.Watched = false;
            rec.Deaths.Clear();
        }
    }

    public static int Wipe()
    {
        var gone = _byName.Count;

        _byName.Clear();

        var bots = BotPopulation.Bots;

        for (var i = 0; i < bots.Count; i++)
        {
            if (bots[i] != null)
            {
                bots[i].Residence = null;
            }
        }

        var away = BotPopulation.Away;

        for (var i = 0; i < away.Count; i++)
        {
            if (away[i] != null)
            {
                away[i].Residence = null;
            }
        }

        _unhomed.Clear();
        _occupied.Clear();

        return gone;
    }

    internal static void Save(IGenericWriter writer)
    {
        var many = 0;

        foreach (var (_, rec) in _byName)
        {
            if (rec.Town != null)
            {
                many++;
            }
        }

        writer.WriteEncodedInt(many);

        foreach (var (name, rec) in _byName)
        {
            if (rec.Town == null)
            {
                continue;
            }

            writer.Write(name);
            writer.Write(rec.Town);
            writer.Write(rec.Since);
            writer.WriteEncodedInt(rec.Moves);
            writer.Write(rec.Left);
        }
    }

    internal static int Load(IGenericReader reader)
    {
        _byName.Clear();

        var many = reader.ReadEncodedInt();

        for (var i = 0; i < many; i++)
        {
            var name = reader.ReadString();
            var town = reader.ReadString();
            var since = reader.ReadDateTime();
            var moves = reader.ReadEncodedInt();
            var left = reader.ReadString();

            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(town))
            {
                continue;
            }

            _byName[name] = new Record { Name = name, Town = town, Since = since, Moves = moves, Left = left };
        }

        ReadBack = _byName.Count;

        return ReadBack;
    }
}
