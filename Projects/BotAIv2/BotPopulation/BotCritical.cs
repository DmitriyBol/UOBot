using System;
using System.Collections.Generic;
using Server.Logging;
using Server.Text;

namespace Server.BotAI.V2;

public static class BotCritical
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotCritical));

    public static bool Running { get; set; } = true;

    public static int EarlyMinutes { get; set; } = 480;

    private static readonly (string Word, string Class, SkillName Skill)[] Trades =
    [
        ("Smith", "Crafter", SkillName.Blacksmith),
        ("Tailor", "Crafter", SkillName.Tailoring),
        ("Fletcher", "Crafter", SkillName.Fletching),
        ("Miner", "Gatherer", SkillName.Mining),
        ("Woodsman", "Gatherer", SkillName.Lumberjacking)
    ];

    private static readonly List<string> _posts = [];

    private static readonly Dictionary<string, int> _floors = new(StringComparer.OrdinalIgnoreCase);

    private static readonly Dictionary<string, int> _present = new(StringComparer.OrdinalIgnoreCase);

    private static readonly Dictionary<string, int> _island = new(StringComparer.OrdinalIgnoreCase);

    public static long Held { get; internal set; }

    public static long Brought { get; internal set; }

    public static long ByPost { get; internal set; }

    static BotCritical()
    {
        (string Post, int Floor)[] written =
        [
            ("Healer", 3), ("Crafter", 4), ("Gatherer", 4), ("Mage", 3), ("Trader", 3), ("Captain", 1), ("Baron", 1),
            ("Smith", 1), ("Tailor", 1), ("Fletcher", 1), ("Miner", 1), ("Woodsman", 1)
        ];

        for (var i = 0; i < written.Length; i++)
        {
            _posts.Add(written[i].Post);
            _floors[written[i].Post] = written[i].Floor;
        }
    }

    public static IReadOnlyList<string> Posts => _posts;

    public static int Floor(string post) => post != null && _floors.TryGetValue(post, out var floor) ? floor : 0;

    public static int Present(string post) => post != null && _present.TryGetValue(post, out var n) ? n : 0;

    public static int OnIsland(string post) => post != null && _island.TryGetValue(post, out var n) ? n : 0;

    public static int Short(string post) => Math.Max(0, Floor(post) - Present(post));

    private static int TradeIndex(string word)
    {
        for (var i = 0; i < Trades.Length; i++)
        {
            if (Trades[i].Word.InsensitiveEquals(word))
            {
                return i;
            }
        }

        return -1;
    }

    public static int Set(IReadOnlyDictionary<string, int> floors)
    {
        if (floors == null)
        {
            return _posts.Count;
        }

        _posts.Clear();
        _floors.Clear();

        foreach (var (post, floor) in floors)
        {
            if (string.IsNullOrWhiteSpace(post))
            {
                continue;
            }

            if (floor <= 0)
            {
                continue;
            }

            if (TradeIndex(post) < 0 && BotClasses.Find(post) == null)
            {
                logger.Warning("The critical post {Post} is neither a class nor a trade (Smith, Tailor, Fletcher, Miner, Woodsman) and is dropped", post);
                continue;
            }

            _posts.Add(post);
            _floors[post] = floor;
        }

        return _posts.Count;
    }

    public static string Trade(BotMobile bot)
    {
        var klass = bot?.Class?.Name;

        if (klass == null)
        {
            return null;
        }

        string best = null;
        var highest = double.MinValue;

        for (var i = 0; i < Trades.Length; i++)
        {
            if (!Trades[i].Class.InsensitiveEquals(klass))
            {
                continue;
            }

            var skill = bot.Skills[Trades[i].Skill];
            var value = skill?.Base ?? 0.0;

            if (value > highest)
            {
                highest = value;
                best = Trades[i].Word;
            }
        }

        return best;
    }

    private static readonly string[] _fill = new string[2];

    private static int Fill(BotMobile bot)
    {
        var n = 0;
        var klass = bot?.Class?.Name;

        if (klass == null)
        {
            return 0;
        }

        _fill[n++] = klass;

        var trade = Trade(bot);

        if (trade != null)
        {
            _fill[n++] = trade;
        }

        return n;
    }

    public static bool Fills(BotMobile bot, string post)
    {
        var n = Fill(bot);

        for (var i = 0; i < n; i++)
        {
            if (_fill[i].InsensitiveEquals(post))
            {
                return true;
            }
        }

        return false;
    }

    private static void Add(Dictionary<string, int> counts, string post, int by)
    {
        if (_floors.ContainsKey(post))
        {
            counts[post] = counts.GetValueOrDefault(post) + by;
        }
    }

    public static void Count()
    {
        _present.Clear();
        _island.Clear();

        var bots = BotPopulation.Bots;

        for (var i = 0; i < bots.Count; i++)
        {
            if (bots[i] is not { Deleted: false } bot || bot.Map == null || bot.Map == Map.Internal)
            {
                continue;
            }

            var n = Fill(bot);

            for (var k = 0; k < n; k++)
            {
                Add(_island, _fill[k], 1);

                if (!bot.Tired)
                {
                    Add(_present, _fill[k], 1);
                }
            }
        }

        var away = BotPopulation.Away;

        for (var i = 0; i < away.Count; i++)
        {
            if (away[i] is not { Deleted: false } bot)
            {
                continue;
            }

            var n = Fill(bot);

            for (var k = 0; k < n; k++)
            {
                Add(_island, _fill[k], 1);
            }
        }
    }

    public static bool Holds(BotMobile bot, out string post)
    {
        post = null;

        if (!Running)
        {
            return false;
        }

        var n = Fill(bot);

        for (var i = 0; i < n; i++)
        {
            var floor = Floor(_fill[i]);

            if (floor > 0 && Present(_fill[i]) <= floor)
            {
                post = _fill[i];

                return true;
            }
        }

        return false;
    }

    public static void Tired(BotMobile bot)
    {
        var n = Fill(bot);

        for (var i = 0; i < n; i++)
        {
            Add(_present, _fill[i], -1);
        }
    }

    public static void Back(BotMobile bot)
    {
        var n = Fill(bot);

        for (var i = 0; i < n; i++)
        {
            Add(_present, _fill[i], 1);
        }
    }

    public static void Born(BotMobile bot)
    {
        var n = Fill(bot);

        for (var i = 0; i < n; i++)
        {
            Add(_present, _fill[i], 1);
            Add(_island, _fill[i], 1);
        }
    }

    public static BotClass Wanting(out string post, Func<BotClass, bool> passOver = null)
    {
        post = null;

        if (!Running)
        {
            return null;
        }

        for (var i = 0; i < _posts.Count; i++)
        {
            var name = _posts[i];

            if (OnIsland(name) >= Floor(name))
            {
                continue;
            }

            var trade = TradeIndex(name);
            var klass = BotClasses.Find(trade >= 0 ? Trades[trade].Class : name);

            if (klass == null || passOver?.Invoke(klass) == true)
            {
                continue;
            }

            post = name;

            return klass;
        }

        return null;
    }

    public static string Describe()
    {
        if (!Running)
        {
            return "critical floors OFF";
        }

        if (_posts.Count == 0)
        {
            return "critical floors: none configured";
        }

        using var line = ValueStringBuilder.Create(384);

        var held = 0;

        line.Append("critical floors: ");

        for (var i = 0; i < _posts.Count; i++)
        {
            var post = _posts[i];
            var present = Present(post);
            var floor = Floor(post);

            if (i > 0)
            {
                line.Append(", ");
            }

            line.Append(post);
            line.Append(' ');
            line.Append(present);
            line.Append('/');
            line.Append(floor);

            if (present >= floor)
            {
                held++;
                continue;
            }

            var island = OnIsland(post);

            line.Append(island == 0 ? " (none on the island)" : island < floor ? $" (only {island} on the island)" : $" ({island - present} resting or tired)");
        }

        line.Append($" — {held} of {_posts.Count} held; this session {Held} times a bot past its hours was kept in the world for a post, ");
        line.Append($"{Brought} brought back from rest for one (up to {EarlyMinutes} minutes early), {ByPost} newcomers born into a post the island lacked");

        return line.ToString();
    }
}
