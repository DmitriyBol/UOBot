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

    /// <summary>
    /// The menu, rebuilt only when the shard's set of proposers changes.
    ///
    /// Replaced whole rather than cleared and refilled, because a mind's answer arrives seconds after the
    /// question and the callback still holds this list: a list mutated in place under a pending answer is a
    /// menu that changed its mind while somebody was reading it.
    /// </summary>
    private static IReadOnlyList<string> _trades = [];

    private static Timer _timer;

    private static long _saidTick;

    /// <summary>
    /// How often the two of them are summed up in the shard's own log.
    ///
    /// One line, every case counted separately and no branch called "other": decisions asked for, taken up,
    /// passed over, predictions too high against not too high. A summary with a catch-all bucket hides
    /// exactly the thing it is being read for.
    /// </summary>
    public static int SayEveryMs { get; set; } = 300000;

    /// <summary>How often each mind is given the chance to think. The thinking itself is far rarer.</summary>
    public static int BeatMs { get; set; } = 2000;

    /// <summary>What each mind is called, and therefore whose rules are whose.</summary>
    public static string WarriorName { get; set; } = "Aldric";

    /// <summary>The architect. Named for the office rather than the build: it is a smith who thinks.</summary>
    public static string ArchitectName { get; set; } = "Godric";

    public static string SageName { get; set; } = "Cedric";

    /// <summary>
    /// The Baron. Named for the office like the other two, and the office is the only one on the shard whose
    /// subject is the island rather than a trade.
    /// </summary>
    public static string BaronName { get; set; } = "Baldric";

    /// <summary>Whether the beat is running.</summary>
    public static bool Running => _timer != null;

    public static IReadOnlyList<BotMind> All => _minds;

    /// <summary>Bodies claimed, of the four wanted.</summary>
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
        _minds.Add(new BotMind(WarriorName, "warrior"));

        // <b>The minds are offices, and none of them is a build.</b> They were a blade, a bow and a book —
        // three ways of fighting, which between them can only answer one question and answer it three times.
        // Every bot on the shard fights; almost none of them decides anything that outlives the fight. So the
        // rest are offices whose whole subject is the population rather than the moment: what gets made and
        // how well the shard is equipped, and what the casters know.
        _minds.Add(new BotMind(ArchitectName, "architect"));
        _minds.Add(new BotMind(SageName, "sage"));

        // <b>Back on the shard from 02.09.2026, by instruction.</b> He was stood down for a day while the
        // debugger took his slot; the code was left standing precisely so that bringing him back would be
        // this line and one entry in bot-population.json, and it was.
        //
        // Kept from before:
        // He was the fourth mind and the only one that is not a supplement to something the arithmetic
        // already does well: the other three choose among trades the shard would have offered anyway and are
        // judged on choosing better, while he is offered two things written for him alone and is paid
        // nothing, so there is no wage to compare and the judgement is the whole of it. His class, his
        // harrowings, his stipend and the paragraph in BotMindSight.Calling are all still here and still
        // correct; putting this line back and adding "Baron" to bot-population.json is the whole of bringing
        // him out again. What took his slot is the debugger — see the debugger folder — which is not a mind
        // at all: it takes no work, joins no auction and only watches.
        //
        _minds.Add(new BotMind(BaronName, "baron"));

        Load();
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

    /// <summary>
    /// Finds a body of the right class for each mind and puts the mind behind it.
    ///
    /// <para>
    /// Re-runnable: a body that has died and been replaced leaves its mind without one, and the next beat
    /// picks up whatever the population raised in its place. The mind, its rules and its record of what its
    /// choices came to all survive that — they belong to the name, not to the corpse.
    /// </para>
    /// </summary>
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

            // <b>The warrior mind takes the captain's body, and only a mind may.</b> A captain is the one
            // office on this shard that is about other bots rather than about itself — where to take a
            // company, whose square is killing people, which of the young ones is worth an hour of drill —
            // and none of those is a question the arithmetic can be asked. The class exists as a body the
            // population raises like any other; what makes it a captain is that something is thinking in it.
            // If the population is ever configured without one, this falls back to an ordinary warrior and
            // says so, rather than leaving a mind with no body at all.
            //
            // In order of preference, and the second entry is a fallback rather than a second choice.
            //
            // <b>The Baron has none, and that is deliberate rather than an omission.</b> Every other mind
            // here falls back to an ordinary build because a thinking warrior in a warrior's body is still a
            // thinking warrior. A Baron mind in a warrior's body would be a disaster of a quieter kind: the
            // whole of what makes the office work — the sworn trades, the stipend, the share he stands out
            // of — lives on the class, so the mind would sit in an ordinary bot reading a prompt about
            // harrowings it can never be offered, and would spend every decision predicting work that is not
            // on its menu. Better to have no body and say so.
            string[] wanted = mind.Trade switch
            {
                "architect" => ["Architect", "Crafter"],
                "sage" => ["Sage", "Mage"],
                "baron" => ["Baron"],
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

                // <b>Marked, not titled.</b> The model's name used to go into Title on the reasoning that a
                // title is the slot for exactly this — but Title is the engine's <em>custom</em> title: it
                // hangs over the bot's head in the world, where no other bot wears anything, and a non-empty
                // one makes Titles.ComputeTitle skip the skill-title branch, which is the branch that writes
                // the comma. So a thinking bot read "Aldric of qwen3.5:9b" in the field and lost
                // "Aldric, Grandmaster Swordsman" on the paperdoll, both at once.
                //
                // Which of the sixteen is thinking is a fact for whoever is reading the dashboard, and that
                // is where it now shows, as "(AI)". The model itself is already named once at startup and in
                // every line of bot-minds.log, which is where a version number belongs.
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

    /// <summary>Whether some mind already has this body.</summary>
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

    /// <summary>
    /// The trades a mind may choose between: whatever the shard is actually proposing, minus the rungs a
    /// mind has no business on.
    ///
    /// <para>
    /// <b>Read from the live list rather than written down here.</b> A list of trade names in this file
    /// would be right on the day it was written and wrong the first time somebody adds a proposer — and
    /// wrong silently, because a menu that is missing an option looks exactly like a mind that never picks
    /// it. Survival work is left out on purpose: mending and fleeing belong to the rungs below Free, they
    /// fire without anybody deciding anything, and offering them here would let a mind choose to bleed.
    /// </para>
    /// </summary>
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

    /// <summary>
    /// Which of these trades has real work in it for this bot at this moment.
    ///
    /// <para>
    /// <b>Read from the auction's own last round of questions, never asked again.</b> Asking the proposers
    /// directly was the obvious way to do this and it is the wrong one: every proposer counts its own
    /// refusals by reason, and that tally is the instrumentation this whole shard is read through. A
    /// speculative round of questions three times a minute would have inflated all of it silently, so that
    /// "the smith had no metal four hundred times" stopped meaning what it says. The auction asks every one
    /// of them on every review anyway and now keeps the names; this is a read of a list.
    /// </para>
    ///
    /// <para>
    /// It is the whole list as the auction found it, not what the barren memory has left of it: a trade
    /// written off four minutes ago and refilled since should come back the moment it has anything, and the
    /// memory exists to break a loop rather than to be believed over the world.
    /// </para>
    /// </summary>
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
            // The mind's own proposer is on that list too, and offering a mind its own last answer back is a
            // loop. Anything the mind is not allowed to choose is left out the same way.
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

    /// <summary>The proposer that hands a mind's choice to the auction, or null if it has none.</summary>
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
            // The trade the mind wanted has nothing to offer right now — no ore in reach, no shopkeeper, no
            // quarry. The choice is thrown away rather than held: asking again in fifteen seconds with the
            // same stale answer would be a mind that stopped thinking without saying so. Thrown away and not
            // spent, because it was never taken up, and a tally that counts refusals as successes is worse
            // than no tally.
            mind.Discard();

            return null;
        }

        // The choice is not spent here. It is handed to the deed, and the deed claims it if and only if the
        // auction actually starts it — see BotMind.Began. An offer that is weighed and beaten leaves the
        // choice standing for the next review, which is what being outbid is.
        return new BotMindDeed(mind, work, choice, bot.Self);
    }

    /// <summary>The mind belonging to this bot, or null for the other thirteen.</summary>
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

            logger.Information("Minds: {What}", $"{Describe()}; {BotOllama.Describe()}; {BotMindTalk.Lines} lines said between them");
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

    /// <summary>Rules from the last session, by name.</summary>
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

    /// <summary>Written the moment a rule is added rather than at shutdown: a shard is usually killed.</summary>
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

    /// <summary>What the two of them have done, each counted separately.</summary>
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
