using System;
using System.Collections.Generic;
using Server.Logging;
using Server.Mobiles;

namespace Server.BotAI.V2;

public sealed class BotProgress : GenericPersistence
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotProgress));

    private const int Shape = 2;

    private const int Oldest = 1;

    private static readonly Dictionary<(string Name, string Class), Learned> _saved = new(NameAndClass.Instance);

    private sealed class NameAndClass : IEqualityComparer<(string Name, string Class)>
    {
        public static readonly NameAndClass Instance = new();

        public bool Equals((string Name, string Class) a, (string Name, string Class) b) =>
            string.Equals(a.Name, b.Name, StringComparison.OrdinalIgnoreCase)
            && string.Equals(a.Class, b.Class, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode((string Name, string Class) key) =>
            HashCode.Combine(
                StringComparer.OrdinalIgnoreCase.GetHashCode(key.Name ?? ""),
                StringComparer.OrdinalIgnoreCase.GetHashCode(key.Class ?? "")
            );
    }

    private static BotProgress _store;

    public static void Configure() => _store ??= new BotProgress();

    public BotProgress() : base("BotProgress", 12)
    {
    }

    public static int Remembered => _saved.Count;

    public static bool Remembers(string name)
    {
        foreach (var (key, _) in _saved)
        {
            if (key.Name.InsensitiveEquals(name))
            {
                return true;
            }
        }

        return false;
    }

    public static int Restored { get; private set; }

    public static long Returned { get; private set; }

    public static bool Savings { get; set; }

    private static bool _wiped;

    public static int Wipe()
    {
        var gone = _saved.Count;

        _saved.Clear();
        _wiped = true;

        return gone;
    }

    public static bool Restore(BotMobile bot)
    {
        var name = bot?.Name;
        var calling = bot?.Class?.Name;

        if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(calling) || !_saved.TryGetValue((name, calling), out var learned))
        {
            return false;
        }

        for (var i = 0; i < learned.Skills.Count; i++)
        {
            var (which, value) = learned.Skills[i];

            if (which < 0 || which >= bot.Skills.Length)
            {
                continue;
            }

            var skill = bot.Skills[(SkillName)which];

            if (skill != null && value > skill.Base)
            {
                skill.Base = value;
            }
        }

        bot.Fame = Math.Max(bot.Fame, learned.Fame);
        bot.Karma = learned.Karma;

        var has = BotYield.Wealth(bot);

        if (Savings && learned.Purse > has)
        {
            var owed = learned.Purse - has;

            if (Banker.Deposit(bot, owed))
            {
                Returned += owed;
            }
        }

        Restored++;

        return true;
    }

    private static void Gather()
    {
        var bots = BotPopulation.Bots;

        for (var i = 0; i < bots.Count; i++)
        {
            var bot = bots[i];

            if (bot is not { Deleted: false } || bot.Class == null || string.IsNullOrEmpty(bot.Name))
            {
                continue;
            }

            var learned = new Learned
            {
                Class = bot.Class.Name,
                Fame = bot.Fame,
                Karma = bot.Karma,
                Purse = BotYield.Wealth(bot)
            };

            for (var s = 0; s < bot.Skills.Length; s++)
            {
                var skill = bot.Skills[s];

                if (skill is { Base: > 0.0 })
                {
                    learned.Skills.Add(((int)skill.SkillName, skill.Base));
                }
            }

            _saved[(bot.Name, bot.Class.Name)] = learned;
        }
    }

    public override void Serialize(IGenericWriter writer)
    {
        if (_wiped)
        {
            writer.WriteEncodedInt(Shape);
            writer.WriteEncodedInt(0);

            return;
        }

        Gather();

        writer.WriteEncodedInt(Shape);
        writer.WriteEncodedInt(_saved.Count);

        foreach (var (key, learned) in _saved)
        {
            writer.Write(key.Name);
            writer.Write(learned.Class);
            writer.WriteEncodedInt(learned.Fame);
            writer.WriteEncodedInt(learned.Karma);
            writer.WriteEncodedInt(learned.Purse);
            writer.WriteEncodedInt(learned.Skills.Count);

            for (var i = 0; i < learned.Skills.Count; i++)
            {
                var (which, value) = learned.Skills[i];

                writer.WriteEncodedInt(which);
                writer.Write(value);
            }
        }
    }

    public override void Deserialize(IGenericReader reader)
    {
        _saved.Clear();
        _wiped = false;

        var shape = reader.ReadEncodedInt();

        if (shape < Oldest || shape > Shape)
        {

            logger.Warning(
                "The saved progress is shape {Found} and this build reads {Oldest} to {Wanted}; it cannot be read, and the shard will stop on the engine's own prompt until Saves/BotProgress/BotProgress.bin is deleted",
                shape,
                Oldest,
                Shape
            );

            return;
        }

        var count = reader.ReadEncodedInt();

        for (var i = 0; i < count; i++)
        {
            var name = reader.ReadString();

            var learned = new Learned
            {
                Class = reader.ReadString(),
                Fame = reader.ReadEncodedInt(),
                Karma = reader.ReadEncodedInt(),

                Purse = shape >= 2 ? reader.ReadEncodedInt() : 0
            };

            var skills = reader.ReadEncodedInt();

            for (var s = 0; s < skills; s++)
            {
                learned.Skills.Add((reader.ReadEncodedInt(), reader.ReadDouble()));
            }

            if (!string.IsNullOrEmpty(name))
            {
                _saved[(name, learned.Class)] = learned;
            }
        }
    }

    private sealed class Learned
    {
        public string Class;

        public int Fame;

        public int Karma;

        public int Purse;

        public List<(int Which, double Base)> Skills { get; } = [];
    }
}
