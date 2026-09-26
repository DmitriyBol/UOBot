using System;
using System.Collections.Generic;
using System.IO;
using Server.BotAI.V2;
using Server.Json;
using Server.Logging;

namespace Server.BotAI.Mind;

/// <summary>What is kept between sessions: one bot's name and the rules it has written for itself.</summary>
public sealed class BotMindMemory
{
    public Dictionary<string, List<string>> Lessons { get; set; } = [];
}

/// <summary>
/// The minds, the bodies they belong to, and the beat they think on.
///
/// <para>
/// <b>Named, and the naming is what makes learning possible at all.</b> Bots do not survive a restart — the
/// population deletes whatever came back from the world save and raises a fresh set — so anything keyed to a
/// body is gone every morning. A mind's rules are keyed to a name instead, and the bodies are renamed on the
/// way in so that the name is the same one tomorrow. Without that, a thinking bot starts every session
/// knowing nothing, and "it learns" is a claim nothing can support.
/// </para>
///
/// <para>
/// <b>It claims bodies rather than making them.</b> Raising more bots would be a second population with its
/// own outfitting, spawning and revival, all of it a copy of code that already works. The four here are four
/// of the ones the shard already raises: everything about them is ordinary except that something is thinking
/// about what they should do next.
/// </para>
/// </summary>
public static class BotMinds
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotMinds));

    private const string MemoryPath = "Configuration/bot-minds.json";

    private static readonly List<BotMind> _minds = [];

    private static IReadOnlyList<string> _trades = [];

    private static Timer _timer;

    private static long _saidTick;

    public static int SayEveryMs { get; set; } = 300000;

    public static int BeatMs { get; set; } = 2000;

    public static string WarriorName { get; set; } = "Aldric";

    public static string ArchitectName { get; set; } = "Godric";

    public static string SageName { get; set; } = "Cedric";

    public static string BaronName { get; set; } = "Baldric";

    public static string[] CrafterNames { get; set; } = [];

    public static bool Running => _timer != null;

    public static IReadOnlyList<BotMind> All => _minds;

    public static int Embodied
    {
        get
        {
            var count = 0;

            for (var i = 0; i < _minds.Count; i++)
            {
                if (_minds[i].Body is { Deleted: false })
                {
                    count++;
                }
            }

            return count;
        }
    }

    public static void Start()
    {
        Stop();

        _minds.Clear();

        BotPopulation.Reserve(CrafterNames, "the minds");

        for (var i = 0; i < CrafterNames.Length; i++)
        {
            var mind = new BotMind(CrafterNames[i], "crafter");

            mind.Stagger(i * BotMind.ThinkEveryMs / Math.Max(1, CrafterNames.Length));

            _minds.Add(mind);
        }

        Load();

        if (_minds.Count == 0)
        {
            logger.Information(
                "No minds are running: the Captain, the Architect, the Sage and the Baron are ordinary bots and the auction chooses for them. Their names and the lessons they learned are kept in {Path}; putting one back is one line in BotMinds.Start",
                MemoryPath
            );

            return;
        }

        BotMindLog.Open();

        Claim();

        _saidTick = Core.TickCount;

        _timer = new MindTimer(TimeSpan.FromMilliseconds(BeatMs));
        _timer.Start();
    }

    public static void Stop()
    {
        _timer?.Stop();
        _timer = null;
    }

    private static void Claim()
    {
        var bots = BotPopulation.Bots;

        for (var i = 0; i < _minds.Count; i++)
        {
            var mind = _minds[i];

            if (mind.Body is { Deleted: false })
            {
                continue;
            }

            string[] wanted = mind.Trade switch
            {
                "architect" => ["Architect", "Crafter"],
                "sage" => ["Sage", "Mage"],
                "baron" => ["Baron"],

                "crafter" => ["Crafter"],
                _ => ["Captain", "Warrior"]
            };

            for (var j = 0; j < bots.Count * wanted.Length; j++)
            {
                var body = bots[j % bots.Count];

                if (body is not { Deleted: false })
                {
                    continue;
                }

                if (!string.Equals(body.Class?.Name, wanted[j / bots.Count], StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (Held(body))
                {
                    continue;
                }

                mind.Body = body;

                var was = body.Name;
                body.Name = mind.Name;

                body.Minded = true;

                logger.Information(
                    "{Name} has a mind of its own now: it was {Was}, a {Class}, and is thinking with {Model}",
                    mind.Name,
                    was,
                    body.Class?.Name,
                    BotOllama.Model
                );

                BotMindLog.Write(mind.Name, $"took the body of {was}, a {body.Class?.Name}", null);

                break;
            }
        }
    }

    private static bool Held(BotMobile body)
    {
        for (var i = 0; i < _minds.Count; i++)
        {
            if (ReferenceEquals(_minds[i].Body, body))
            {
                return true;
            }
        }

        return false;
    }

    private static void Trades()
    {
        var proposers = BotWill.Proposers;
        List<string> fresh = [];

        for (var i = 0; i < proposers.Count; i++)
        {
            var proposer = proposers[i];

            if (proposer.Rung != BotStanding.Free || proposer is BotMindProposer)
            {
                continue;
            }

            fresh.Add(proposer.Name);
        }

        if (fresh.Count != _trades.Count)
        {
            _trades = fresh;
        }
    }

    internal static IReadOnlyList<string> Working(BotMobile body, IReadOnlyList<string> trades)
    {
        List<string> live = [];

        var offered = body is { Deleted: false } ? body.Resolve?.Offered : null;

        if (offered == null)
        {
            return live;
        }

        for (var i = 0; i < offered.Count; i++)
        {
            if (Listed(trades, offered[i]) && !live.Contains(offered[i]))
            {
                live.Add(offered[i]);
            }
        }

        return live;
    }

    private static bool Listed(IReadOnlyList<string> trades, string name)
    {
        for (var i = 0; i < trades.Count; i++)
        {
            if (string.Equals(trades[i], name, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    public static BotDeed Offer(IBotWilful bot)
    {
        var mind = Of(bot);

        var choice = mind?.Choice;

        if (choice == null || bot?.Self == null)
        {
            return null;
        }

        var proposer = Proposer(choice.Intent);

        if (proposer == null)
        {
            return null;
        }

        var work = proposer.Propose(bot);

        if (work == null)
        {
            mind.Discard();

            return null;
        }

        return new BotMindDeed(mind, work, choice, bot.Self);
    }

    public static BotMind Of(IBotWilful bot)
    {
        if (bot?.Self is not BotMobile body)
        {
            return null;
        }

        for (var i = 0; i < _minds.Count; i++)
        {
            if (ReferenceEquals(_minds[i].Body, body))
            {
                return _minds[i];
            }
        }

        return null;
    }

    private static IBotProposer Proposer(string name)
    {
        var proposers = BotWill.Proposers;

        for (var i = 0; i < proposers.Count; i++)
        {
            if (proposers[i] is not BotMindProposer
                && string.Equals(proposers[i].Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return proposers[i];
            }
        }

        return null;
    }

    private static void Update()
    {
        Claim();
        Trades();

        if (Core.TickCount - _saidTick >= SayEveryMs)
        {
            _saidTick = Core.TickCount;

            logger.Information("Minds: {What}", $"{Describe()}; {BotWill.MindsMet()}; {BotOllama.Describe()}; {BotMindTalk.Lines} lines said between them; {BotMindClaims.Describe()}; the state they read last ran to {BotMindSight.LastChars} characters");
        }

        if (_trades.Count == 0)
        {
            return;
        }

        for (var i = 0; i < _minds.Count; i++)
        {
            _minds[i].Beat(_trades);
        }
    }

    public static void Load()
    {
        var path = Path.Combine(Core.BaseDirectory, MemoryPath);
        var memory = JsonConfig.Deserialize<BotMindMemory>(path);

        if (memory?.Lessons == null)
        {
            return;
        }

        for (var i = 0; i < _minds.Count; i++)
        {
            if (memory.Lessons.TryGetValue(_minds[i].Name, out var lessons))
            {
                _minds[i].Restore(lessons);
            }
        }
    }

    public static void Save()
    {
        var memory = new BotMindMemory();

        for (var i = 0; i < _minds.Count; i++)
        {
            memory.Lessons[_minds[i].Name] = [.. _minds[i].Lessons];
        }

        try
        {
            JsonConfig.Serialize(Path.Combine(Core.BaseDirectory, MemoryPath), memory);
        }
        catch (Exception e)
        {
            logger.Warning("The minds' rules could not be written down: {Message}", e.Message);
        }
    }

    public static string Describe()
    {
        if (_minds.Count == 0)
        {
            return "no minds are running";
        }

        var lines = new string[_minds.Count];

        for (var i = 0; i < _minds.Count; i++)
        {
            lines[i] = _minds[i].Describe();
        }

        return string.Join("; ", lines);
    }

    private sealed class MindTimer : Timer
    {
        public MindTimer(TimeSpan interval) : base(interval, interval)
        {
        }

        protected override void OnTick() => Update();
    }
}
