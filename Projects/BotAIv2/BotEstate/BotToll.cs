using System;
using System.Collections.Generic;
using Server.Guilds;
using Server.Logging;
using Server.Mobiles;
using Server.Text;

namespace Server.BotAI.V2;

/// <summary>How a guild charges strangers for hunting its land.</summary>
public enum BotTollWay
{
    None,

    Posted,

    Patrolled
}

/// <summary>
/// The toll on hunting a guild's land: which way each guild charges, who has been told, and what a stranger pays.
///
/// <para>
/// <b>Patrick's order of 26.09.2026, the first of the night:</b> "guilds may demand payment for hunting on their
/// territories — either by setting a percentage that is collected, or by actively patrolling, pointing out that the land
/// belongs to the guild and that the hunter's income will be taxed." Both are here, and a guild uses one or the other:
/// a guild with <see cref="PatrolFighters"/> fighters in the world patrols (<see cref="BotTollman"/> walks up to a stranger
/// hunting its land and says so, and a stranger told pays <see cref="PatrolRate"/>); a smaller guild posts its toll
/// (<see cref="PostedRate"/> of every stranger's hunting coin, told or not).
/// </para>
///
/// <para>
/// <b>Who pays.</b> A stranger: a bot of another guild or of none. Not the guild's own (they pay their dues into the same
/// chest at the old tithe, <see cref="BotChest.Rate"/>), not an ally, not an enemy — a guild at war settles with blood,
/// not coin — and not The Shadow, which pays nobody. Coin only, only coin still in the pack, only from work that braves
/// the ground (<see cref="BotChest.Tithe"/>), and a company's shares too (<c>BotSpoils</c>).
/// </para>
///
/// <para>
/// <b>What it does to the hunter is a price, not a wall.</b> The appraisal weighs hunting on tolled land at what is left
/// after the toll (<see cref="Factor"/>), outside the fifth root so it bites as a toll does, and never below
/// <c>1 − PatrolRate</c>. A hunter told once goes elsewhere if elsewhere pays better, and pays if it stays. Every toll paid
/// sours the payer's guild a little on the taker's and warms the taker's on the payer's (<see cref="Grudge"/>,
/// <see cref="Thanks"/>): a toll is how a quarrel over ground starts, and a quarrel is how the war the land costs starts.
/// </para>
/// </summary>
public static class BotToll
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotToll));

    public static bool Running { get; set; } = true;

    public static double PostedRate { get; set; } = 0.10;

    public static double PatrolRate { get; set; } = 0.20;

    public static int WarnedMs { get; set; } = 3600000;

    public static int PatrolFighters { get; set; } = 4;

    public static int LookMs { get; set; } = 10000;

    public static int ChooseMs { get; set; } = 300000;

    public static double Grudge { get; set; } = 0.3;

    public static double Thanks { get; set; } = 0.2;

    public static long PostedPaid { get; private set; }

    public static long PostedGold { get; private set; }

    public static long PatrolPaid { get; private set; }

    public static long PatrolGold { get; private set; }

    public static long Told { get; private set; }

    public static long Exempt { get; private set; }

    public static long Untold { get; private set; }

    public static long Changes { get; private set; }

    private static readonly Dictionary<string, BotTollWay> _ways = new(StringComparer.Ordinal);

    private static readonly Dictionary<(Serial Hunter, string Guild), long> _told = [];

    private static readonly List<(Serial Hunter, string Guild)> _lapsed = [];

    private static readonly Dictionary<string, List<BotMobile>> _strangers = new(StringComparer.Ordinal);

    private static long _lookedTick;

    private static long _choseTick;

    private static bool _looked;

    private static bool _chose;

    public static BotTollWay Way(string guild) =>
        guild == null ? BotTollWay.None : _ways.TryGetValue(guild, out var way) ? way : BotTollWay.Posted;

    public static bool Warned(Mobile hunter, string guild) =>
        hunter != null && guild != null && _told.TryGetValue((hunter.Serial, guild), out var until) && Core.TickCount - until < 0;

    public static void Warn(Mobile hunter, string guild)
    {
        if (hunter == null || guild == null)
        {
            return;
        }

        _told[(hunter.Serial, guild)] = Core.TickCount + WarnedMs;
        Told++;

        if (_strangers.TryGetValue(guild, out var list))
        {
            list.Remove(hunter as BotMobile);
        }
    }

    public static double Rate(string holder, Mobile payer)
    {
        if (!Running || holder == null || payer == null)
        {
            return 0.0;
        }

        var theirs = payer.Guild?.Name;

        if (theirs == holder)
        {
            return 0.0;
        }

        if (theirs != null)
        {
            if (BotWar.Of(holder, theirs) != null || BotUnderworld.Band(payer.Guild as Guild))
            {
                return 0.0;
            }

            if (BotGuilds.Named(holder) is { } held && BotGuilds.Named(theirs) is { } other && held.IsAlly(other))
            {
                return 0.0;
            }
        }

        return Way(holder) switch
        {
            BotTollWay.Posted => PostedRate,
            BotTollWay.Patrolled => Warned(payer, holder) ? PatrolRate : 0.0,
            _ => 0.0
        };
    }

    public static double Factor(Mobile body, Map map, BotDeed deed)
    {
        if (!Running || deed == null || !deed.Braves || map == null || map == Map.Internal)
        {
            return 1.0;
        }

        var holder = BotLand.Holder(map, deed.Where);

        if (holder == null)
        {
            return 1.0;
        }

        return 1.0 - Math.Clamp(Rate(holder, body), 0.0, Math.Max(PostedRate, PatrolRate));
    }

    public static void Paid(string holder, Mobile payer, int gold)
    {
        if (Way(holder) == BotTollWay.Patrolled)
        {
            PatrolPaid++;
            PatrolGold += gold;
        }
        else
        {
            PostedPaid++;
            PostedGold += gold;
        }

        var theirs = payer?.Guild?.Name;

        if (theirs != null && theirs != holder)
        {
            BotRegard.Move(theirs, holder, -Grudge, "a toll paid on their land");
            BotRegard.Move(holder, theirs, Thanks, "a toll paid on our land");
        }
    }

    public static void Owed(string holder, Mobile payer)
    {
        if (Way(holder) == BotTollWay.Patrolled && payer?.Guild?.Name != holder && Rate(holder, payer) <= 0.0
            && payer is not null && !Exempted(holder, payer))
        {
            Untold++;
        }
        else
        {
            Exempt++;
        }
    }

    private static bool Exempted(string holder, Mobile payer)
    {
        var theirs = payer.Guild?.Name;

        return theirs != null && (BotWar.Of(holder, theirs) != null || BotUnderworld.Band(payer.Guild as Guild)
            || BotGuilds.Named(holder) is { } held && BotGuilds.Named(theirs) is { } other && held.IsAlly(other));
    }

    public static BotMobile Nearest(string guild, Mobile from)
    {
        if (guild == null || from == null || !_strangers.TryGetValue(guild, out var list) || list.Count == 0)
        {
            return null;
        }

        BotMobile best = null;
        var bestRange = double.MaxValue;

        for (var i = 0; i < list.Count; i++)
        {
            var one = list[i];

            if (one is not { Deleted: false, Alive: true } || one.Map != from.Map || Warned(one, guild))
            {
                continue;
            }

            if (!string.Equals(BotLand.Holder(one.Map, one.Location), guild, System.StringComparison.Ordinal))
            {
                continue;
            }

            var range = from.GetDistanceToSqrt(one);

            if (range < bestRange)
            {
                bestRange = range;
                best = one;
            }
        }

        return best;
    }

    public static void Look()
    {
        if (!Running)
        {
            return;
        }

        var now = Core.TickCount;

        if (!_chose || now - _choseTick >= ChooseMs)
        {
            _chose = true;
            _choseTick = now;
            Choose();
        }

        if (_looked && now - _lookedTick < LookMs)
        {
            return;
        }

        _looked = true;
        _lookedTick = now;

        foreach (var list in _strangers.Values)
        {
            list.Clear();
        }

        _lapsed.Clear();

        foreach (var (key, until) in _told)
        {
            if (now - until >= 0)
            {
                _lapsed.Add(key);
            }
        }

        for (var i = 0; i < _lapsed.Count; i++)
        {
            _told.Remove(_lapsed[i]);
        }

        var bots = BotPopulation.Bots;

        for (var i = 0; i < bots.Count; i++)
        {
            var bot = bots[i];

            if (bot is not { Deleted: false, Alive: true } || bot.Map == null || bot.Map == Map.Internal)
            {
                continue;
            }

            if (bot.Resolve?.Deed is not { Braves: true })
            {
                continue;
            }

            var holder = BotLand.Holder(bot.Map, bot.Location);

            if (holder == null || Way(holder) != BotTollWay.Patrolled || bot.Guild?.Name == holder || Warned(bot, holder))
            {
                continue;
            }

            if (bot.Guild != null && Exempted(holder, bot))
            {
                continue;
            }

            if (!_strangers.TryGetValue(holder, out var strangers))
            {
                _strangers[holder] = strangers = [];
            }

            strangers.Add(bot);
        }
    }

    private static void Choose()
    {
        foreach (var guild in BotGuilds.Standing)
        {
            if (guild == null || guild.Disbanded)
            {
                continue;
            }

            var way = BotUnderworld.Band(guild) ? BotTollWay.None : Fighters(guild) >= PatrolFighters ? BotTollWay.Patrolled : BotTollWay.Posted;
            var had = Way(guild.Name);

            _ways[guild.Name] = way;

            if (had != way)
            {
                Changes++;

                logger.Information(
                    "{Guild} now {Way} for hunting its land",
                    guild.Name,
                    way switch
                    {
                        BotTollWay.Patrolled => $"patrols it and charges strangers it has told {PatrolRate:P0}",
                        BotTollWay.Posted => $"posts a toll of {PostedRate:P0} on strangers",
                        _ => "charges nothing"
                    }
                );
            }
        }
    }

    private static int Fighters(Guild guild)
    {
        var count = 0;
        var members = guild.Members;

        for (var i = 0; i < members.Count; i++)
        {
            if (members[i] is BotMobile { Deleted: false, Alive: true } bot && bot.Map != Map.Internal
                && bot.Class is { } klass && klass.Role != BotRole.Producer)
            {
                count++;
            }
        }

        return count;
    }

    public static string Describe()
    {
        if (!Running)
        {
            return "no tolls on hunting";
        }

        using var sb = ValueStringBuilder.Create(256);

        sb.Append($"tolls on hunting: {PostedPaid} posted tolls of {PostedGold}gp at {PostedRate:P0}, {PatrolPaid} of {PatrolGold}gp from strangers told at {PatrolRate:P0}, ");
        sb.Append($"{Told} strangers told by wardens, {Untold} hunts on patrolled land by strangers nobody had told, {Exempt} owing nothing (allies, enemies, the band); ways: ");

        var first = true;

        foreach (var (guild, way) in _ways)
        {
            if (!first)
            {
                sb.Append(", ");
            }

            first = false;
            sb.Append($"{guild} {way}");
        }

        if (first)
        {
            sb.Append("not chosen yet");
        }

        return sb.ToString();
    }

    public static void Forget()
    {
        _ways.Clear();
        _told.Clear();
        _strangers.Clear();
        _chose = false;
        _looked = false;
        PostedPaid = 0;
        PostedGold = 0;
        PatrolPaid = 0;
        PatrolGold = 0;
        Told = 0;
        Exempt = 0;
        Untold = 0;
        Changes = 0;
    }
}
