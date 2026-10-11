using System;
using System.Collections.Generic;

namespace Server.BotAI.V2;

/// <summary>One remembered event of a bot's life, told in its own words: what happened, who to, and how much it weighed.</summary>
public sealed class BotTale
{
    public string Kind;

    public string Other;

    public string Text;

    public double Weight;

    public int Number;

    public DateTime At;
}

/// <summary>What one bot thinks of one other bot, by name, from −100 to +100. See <see cref="BotTies"/>.</summary>
public sealed class BotTie
{
    public string Name;

    public double Regard;

    public int Saved;

    public int Wronged;

    public int Beside;
}

/// <summary>
/// Everything one bot has become as a person rather than as a worker: its nature, its life's story and the bots it
/// loves and hates. Keyed by name, because a bot is raised afresh with a new serial at every boot and its name is the
/// one thing that carries over (see <c>BotProgress</c>). Persisted by <see cref="BotCharacterStore"/>.
/// </summary>
public sealed class BotLife
{
    public BotLife(string name) => Name = name;

    public string Name { get; }

    public BotTrait Traits;

    public bool Drawn;

    public bool Fresh;

    public double Kills;

    public int Deaths;

    public int SavedTimes;

    public int RaisedTimes;

    public int Robbed;

    public int WarKills;

    public int DuelsWon;

    public int DuelsLost;

    public int BestSale;

    public bool Veteran;

    public DateTime VeteranAt;

    public readonly List<BotTale> Tales = new(3);

    public readonly List<BotTie> Ties = new(12);

    public int ToldIndex = -1;

    public double Worst;

    public BotTie Tie(string other)
    {
        for (var i = 0; i < Ties.Count; i++)
        {
            if (string.Equals(Ties[i].Name, other, StringComparison.Ordinal))
            {
                return Ties[i];
            }
        }

        return null;
    }

    public void Rewound()
    {
        var worst = 0.0;

        for (var i = 0; i < Ties.Count; i++)
        {
            if (Ties[i].Regard < worst)
            {
                worst = Ties[i].Regard;
            }
        }

        Worst = worst;
    }
}

/// <summary>
/// The register of <see cref="BotLife"/> records: by name for the store and for other bots' ties, and by serial for the
/// auction, which asks once per offer and must not hash a string to do it.
/// </summary>
public static class BotLives
{
    private static readonly Dictionary<string, BotLife> _byName = new(StringComparer.Ordinal);

    private static readonly Dictionary<Serial, BotLife> _bySerial = [];

    private static readonly Dictionary<string, BotMobile> _bodies = new(StringComparer.Ordinal);

    public static IEnumerable<BotLife> All => _byName.Values;

    public static int Count => _byName.Count;

    public static long Drawn { get; private set; }

    public static BotLife Of(Mobile m)
    {
        if (m is not BotMobile bot || string.IsNullOrEmpty(bot.Name))
        {
            return null;
        }

        if (_bySerial.TryGetValue(bot.Serial, out var life) && string.Equals(life.Name, bot.Name, StringComparison.Ordinal))
        {
            return life;
        }

        life = Named(bot.Name);
        _bySerial[bot.Serial] = life;
        _bodies[bot.Name] = bot;

        return life;
    }

    public static BotLife Named(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return null;
        }

        if (!_byName.TryGetValue(name, out var life))
        {
            life = new BotLife(name);
            _byName[name] = life;
        }

        if (!life.Drawn)
        {
            life.Traits = BotTraits.Draw(name);
            life.Drawn = true;
            life.Fresh = true;
            Drawn++;
        }

        return life;
    }

    public static BotLife Find(string name) =>
        name != null && _byName.TryGetValue(name, out var life) ? life : null;

    public static void Put(BotLife life)
    {
        if (life?.Name != null)
        {
            _byName[life.Name] = life;
        }
    }

    public static BotMobile Body(string name) =>
        name != null && _bodies.TryGetValue(name, out var bot) && bot is { Deleted: false } ? bot : null;

    public static void Refresh()
    {
        _bodies.Clear();

        var bots = BotPopulation.Bots;

        for (var i = 0; i < bots.Count; i++)
        {
            if (bots[i] is { Deleted: false } bot && !string.IsNullOrEmpty(bot.Name))
            {
                _bodies[bot.Name] = bot;
            }
        }

        if (_bySerial.Count > bots.Count * 2 + 64)
        {
            _bySerial.Clear();
        }
    }

    public static void Forget()
    {
        _byName.Clear();
        _bySerial.Clear();
        _bodies.Clear();
        Drawn = 0;
    }

    public static int Wipe()
    {
        var lives = _byName.Count;

        Forget();
        BotTraits.Forget();
        BotBiography.Forget();
        BotTies.Forget();
        BotGrudges.Forget();

        return lives;
    }
}
