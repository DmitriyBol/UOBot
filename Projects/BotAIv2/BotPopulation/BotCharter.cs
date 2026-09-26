using System;
using System.Collections.Generic;
using Server.Guilds;
using Server.Logging;
using Server.Mobiles;
using Server.Text;

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

    public static long Named { get; private set; }

    public static readonly string[] Materials = ["ore", "logs", "herbs", "hides", "feathers", "meat", "wool"];

    private static readonly Dictionary<string, string[]> _brings = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ore"] = ["mine", "prospect"],
        ["logs"] = ["chop"],
        ["herbs"] = ["herbs", "forage"],
        ["hides"] = ["hunt", "band"],
        ["feathers"] = ["hunt", "band"],
        ["meat"] = ["hunt", "band"],
        ["wool"] = ["hunt", "band"]
    };

    public static int WantHoldsMs { get; set; } = 1800000;

    public static long Requested { get; private set; }

    public static long Fetched { get; private set; }

    /// <summary>One request on a guild's board.</summary>
    private sealed class Request
    {
        public string Material;

        public int Amount;

        public string By;

        public long Set;

        public string Why;
    }

    private static readonly Dictionary<string, List<Request>> _boards = [];

    public static readonly string[] Carvings = ["hides", "feathers", "meat", "wool"];

    public static int Bit(string material)
    {
        if (material == null)
        {
            return 0;
        }

        for (var i = 0; i < Materials.Length; i++)
        {
            if (Materials[i].InsensitiveEquals(material))
            {
                return 1 << i;
            }
        }

        return 0;
    }

    public static int Carved(BaseCreature creature)
    {
        var mask = 0;

        for (var i = 0; i < Carvings.Length; i++)
        {
            if (Yields(creature, Carvings[i]))
            {
                mask |= Bit(Carvings[i]);
            }
        }

        return mask;
    }

    public static string Names(int mask)
    {
        if (mask == 0)
        {
            return "nothing";
        }

        var say = ValueStringBuilder.Create(64);

        try
        {
            var shown = 0;

            for (var i = 0; i < Materials.Length; i++)
            {
                if ((mask & (1 << i)) == 0)
                {
                    continue;
                }

                if (shown++ > 0)
                {
                    say.Append(", ");
                }

                say.Append(Materials[i]);
            }

            return say.ToString();
        }
        finally
        {
            say.Dispose();
        }
    }

    public static string Carved(Mobile body)
    {
        for (var i = 0; i < Carvings.Length; i++)
        {
            if (Wants(body, Carvings[i]))
            {
                return Carvings[i];
            }
        }

        return null;
    }

    public static bool Yields(BaseCreature creature, string material)
    {
        if (creature == null || material == null)
        {
            return false;
        }

        return material.ToLowerInvariant() switch
        {
            "hides"    => creature.Hides > 0,
            "feathers" => creature.Feathers > 0,
            "meat"     => creature.Meat > 0,
            "wool"     => creature.Wool > 0,
            _          => false
        };
    }

    private static bool Carved(string material) =>
        material is not null && (material.Equals("hides", StringComparison.OrdinalIgnoreCase)
            || material.Equals("feathers", StringComparison.OrdinalIgnoreCase)
            || material.Equals("meat", StringComparison.OrdinalIgnoreCase)
            || material.Equals("wool", StringComparison.OrdinalIgnoreCase));

    private static List<Request> BoardOf(string guild)
    {
        if (!Running || guild == null || !_boards.TryGetValue(guild, out var board))
        {
            return null;
        }

        for (var i = board.Count - 1; i >= 0; i--)
        {
            if (Core.TickCount - (board[i].Set + WantHoldsMs) >= 0)
            {
                board.RemoveAt(i);
            }
        }

        if (board.Count == 0)
        {
            _boards.Remove(guild);

            return null;
        }

        return board;
    }

    public static void Want(Guild guild, string material, int amount, string by, string why)
    {
        if (!Running || guild == null || string.IsNullOrWhiteSpace(material) || amount <= 0
            || Array.FindIndex(Materials, m => m.Equals(material, StringComparison.OrdinalIgnoreCase)) < 0)
        {
            return;
        }

        if (!_boards.TryGetValue(guild.Name, out var board))
        {
            _boards[guild.Name] = board = [];
        }

        var kept = material.ToLowerInvariant();

        for (var i = board.Count - 1; i >= 0; i--)
        {
            if (board[i].Material == kept)
            {
                board.RemoveAt(i);
            }
        }

        board.Add(new Request { Material = kept, Amount = amount, By = by, Set = Core.TickCount, Why = why });
        Requested++;

        logger.Information(
            "{By} puts a request on {Guild}'s board: {Amount} {Material} — {Why}",
            by ?? "the maker",
            guild.Name,
            amount,
            kept,
            why ?? "no reason given"
        );
    }

    public static bool Wants(Mobile body, string material)
    {
        if (body?.Guild is not Guild guild || material == null)
        {
            return false;
        }

        var board = BoardOf(guild.Name);

        if (board == null)
        {
            return false;
        }

        for (var i = 0; i < board.Count; i++)
        {
            if (board[i].Material.Equals(material, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool Fetches(Guild guild, BotDeed deed)
    {
        var board = BoardOf(guild.Name);

        if (board == null || deed?.Kind == null)
        {
            return false;
        }

        for (var i = 0; i < board.Count; i++)
        {
            var material = board[i].Material;

            if (!_brings.TryGetValue(material, out var trades) || Array.IndexOf(trades, deed.Kind.ToLowerInvariant()) < 0)
            {
                continue;
            }

            if (Carved(material) && !(deed.Foe is BaseCreature creature && Yields(creature, material)))
            {
                continue;
            }

            return true;
        }

        return false;
    }

    public static string BoardSays(Guild guild)
    {
        var board = guild == null ? null : BoardOf(guild.Name);

        if (board == null)
        {
            return "nothing";
        }

        List<string> said = [];

        for (var i = 0; i < board.Count; i++)
        {
            var r = board[i];
            var ago = (int)((Core.TickCount - r.Set) / 60000);

            said.Add($"{r.Amount} {r.Material} ({r.By ?? "the maker"}, {ago} min ago)");
        }

        return string.Join("; ", said);
    }

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
            if (Fetches(guild, deed))
            {
                Fetched++;

                return Boost;
            }

            return 1.0;
        }

        var wanted = Names(deed.Kind, charter.Gather) || Names(deed.Kind, charter.Make);

        if (wanted)
        {
            Named++;
        }

        if (!wanted && charter.March != Point3D.Zero && deed.Map == charter.Map)
        {
            var gap = Math.Max(
                Math.Abs(deed.Where.X - charter.March.X),
                Math.Abs(deed.Where.Y - charter.March.Y)
            );

            wanted = deed.Where != Point3D.Zero && gap <= Reach;
        }

        if (!wanted && Fetches(guild, deed))
        {
            Fetched++;

            return Boost;
        }

        if (!wanted)
        {
            return 1.0;
        }

        Followed++;

        return Boost;
    }

    public static bool ByProposer { get; set; } = true;

    private static bool Names(string kind, string word) =>
        word != null && (string.Equals(kind, word, StringComparison.OrdinalIgnoreCase) || ByProposer && BotWill.OfferedAs(kind, word));

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
            + $"{Followed} pieces of work were weighed under one ({Named} for the trade it named, the rest for its muster); {_boards.Count} guild boards carry requests "
            + $"({Requested} put up, for {WantHoldsMs / 60000} minutes each) and {Fetched} pieces of work were weighed up for bringing one in";

    public static void Forget()
    {
        _charters.Clear();
        _boards.Clear();
        Gathering = 0;
        Making = 0;
        Marching = 0;
        Followed = 0;
        Named = 0;
        Requested = 0;
        Fetched = 0;
    }
}
