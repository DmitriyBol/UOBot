using System;
using System.Collections.Generic;
using Server.Guilds;
using Server.Logging;

namespace Server.BotAI.V2;

/// <summary>
/// What a guild's maker has told the guild to be doing: a trade to gather at, a trade to make at, and
/// somewhere to be.
///
/// <para>
/// <b>Patrick's order of 11.09.2026.</b> The crafters are the guilds' makers and the only bots on this shard
/// with a mind; until now that mind chose work for one body. This gives it the guild: it may say what the
/// band should be collecting, what it should be making, and where it should all be. Everything else about a
/// guild — its hall, its ground, its wars — is already somebody's business; what nobody had was the ability
/// to point fifteen bots at one thing.
/// </para>
///
/// <para>
/// <b>An order is a price and never a command, which is the rule this whole shard is built on.</b> A
/// charter multiplies what the named work is worth to a member, exactly as a revel does and exactly as a
/// guild's own ground does. A bot with something better in front of it goes on doing that, and a charter
/// nobody follows is a fact about the order rather than a fault in the bots — it shows up as a charter that
/// expired with nothing done under it.
/// </para>
///
/// <para>
/// <b>Named in trades, not in materials, and that is deliberate.</b> The mind already answers with a trade
/// out of an enum of the trades this shard actually has; letting it name a material instead would mean a
/// second vocabulary, a second way to be wrong, and a mapping from goods to work that nothing else needs.
/// "Gather at mining" and "make at the forge" are the same orders in the words the shard already speaks.
/// </para>
/// </summary>
public static class BotCharter
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotCharter));

    public static bool Running { get; set; } = true;

    public static double Boost { get; set; } = 1.5;

    public static int HoldsMs { get; set; } = 900000;

    public static int Reach { get; set; } = 60;

    public static long Gathering { get; private set; }

    public static long Making { get; private set; }

    public static long Marching { get; private set; }

    public static long Followed { get; private set; }

    /// <summary>What one guild has standing.</summary>
    private sealed class Charter
    {
        public string Gather;

        public string Make;

        public Map Map;

        public Point3D March;

        public long Set;

        public string Why;
    }

    private static readonly Dictionary<string, Charter> _charters = [];

    private static Charter Of(string guild)
    {
        if (!Running || guild == null || !_charters.TryGetValue(guild, out var charter))
        {
            return null;
        }

        if (Core.TickCount - (charter.Set + HoldsMs) >= 0)
        {
            _charters.Remove(guild);

            return null;
        }

        return charter;
    }

    public static void Order(Guild guild, string gather, string make, Map map, Point3D march, string why)
    {
        if (!Running || guild == null)
        {
            return;
        }

        if (gather == null && make == null && march == Point3D.Zero)
        {
            _charters.Remove(guild.Name);

            return;
        }

        _charters[guild.Name] = new Charter
        {
            Gather = gather,
            Make = make,
            Map = map,
            March = march,
            Set = Core.TickCount,
            Why = why
        };

        if (gather != null)
        {
            Gathering++;
        }

        if (make != null)
        {
            Making++;
        }

        if (march != Point3D.Zero)
        {
            Marching++;
        }

        logger.Information(
            "{Guild} is charged: gather at {Gather}, make at {Make}, {Muster} — {Why}",
            guild.Name,
            gather ?? "anything",
            make ?? "anything",
            march == Point3D.Zero ? "nowhere in particular" : $"muster at {march.X},{march.Y}",
            why ?? "no reason given"
        );
    }

    public static double Worth(Mobile body, BotDeed deed)
    {
        if (!Running || body == null || deed == null || body.Guild is not Guild guild)
        {
            return 1.0;
        }

        var charter = Of(guild.Name);

        if (charter == null)
        {
            return 1.0;
        }

        var wanted = string.Equals(deed.Kind, charter.Gather, StringComparison.OrdinalIgnoreCase)
            || string.Equals(deed.Kind, charter.Make, StringComparison.OrdinalIgnoreCase);

        if (!wanted && charter.March != Point3D.Zero && deed.Map == charter.Map)
        {
            var gap = Math.Max(
                Math.Abs(deed.Where.X - charter.March.X),
                Math.Abs(deed.Where.Y - charter.March.Y)
            );

            wanted = deed.Where != Point3D.Zero && gap <= Reach;
        }

        if (!wanted)
        {
            return 1.0;
        }

        Followed++;

        return Boost;
    }

    public static string Says(Guild guild)
    {
        var charter = guild == null ? null : Of(guild.Name);

        if (charter == null)
        {
            return "nothing";
        }

        List<string> said = [];

        if (charter.Gather != null)
        {
            said.Add($"gather at {charter.Gather}");
        }

        if (charter.Make != null)
        {
            said.Add($"make at {charter.Make}");
        }

        if (charter.March != Point3D.Zero)
        {
            said.Add($"muster at {charter.March.X},{charter.March.Y}");
        }

        var left = Math.Max(0, HoldsMs - (int)(Core.TickCount - charter.Set)) / 60000;

        return $"{string.Join(", ", said)} ({left} min left)";
    }

    public static string Describe() =>
        !Running
            ? "no guild charges its band with anything"
            : $"{_charters.Count} guilds have standing orders ({Gathering} to gather, {Making} to make, "
            + $"{Marching} to muster somewhere), worth ×{Boost:F2} for {HoldsMs / 60000} minutes; "
            + $"{Followed} pieces of work were weighed under one";

    public static void Forget()
    {
        _charters.Clear();
        Gathering = 0;
        Making = 0;
        Marching = 0;
        Followed = 0;
    }
}
