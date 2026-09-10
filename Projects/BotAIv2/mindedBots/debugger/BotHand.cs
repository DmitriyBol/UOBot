using System;
using System.Collections.Generic;
using System.IO;
using Server.BotAI.V2;
using Server.Items;
using Server.Logging;
using Server.Mobiles;
using Server.Multis;
using Server.Network;
using Server.Text;

namespace Server.BotAI.Mind;

/// <summary>
/// The debugger's hands: the handful of things a person with an administrator's account would type at a
/// stuck shard, made available to Argus by name, bounded, and written down every single time.
///
/// <para>
/// <b>Why a set of our own rather than the engine's command system.</b> Almost every GM command in ModernUO
/// ends in a target cursor — <c>[props</c>, <c>[tele</c>, <c>[set</c> all wait for a click — and Argus has
/// no client to click with. Handing him <c>CommandSystem.Handle</c> would hand him a door that opens onto a
/// prompt nobody can answer. So each verb here does what the command of that name does, addressed by the one
/// thing he can name reliably: a bot off the roster.
/// </para>
///
/// <para>
/// <b>Six of the ten verbs only look, and the proportion is on purpose.</b> A watcher's first duty is to find
/// out, and this project's own record says the watcher is wrong far more often than the shard is — ten false
/// alarms against two real defects in the first day of it. Verbs that change the world are for the cases
/// where looking has already produced the answer: a bot in a pocket, a bot dead in a field, a creature
/// nothing can reach.
/// </para>
///
/// <para>
/// <b>What is absent is not an oversight.</b> Nothing here deletes, nothing sets a property, nothing touches
/// an account or an access level, and nothing acts on a mobile that is not one of ours. The world save holds
/// a real person's character; a model that can be talked round by its own previous sentence must not be able
/// to reach it. The engine's own guards sit underneath as well — a bot is only ever put down on ground the
/// engine agrees a body fits on.
/// </para>
///
/// <para>
/// <b>Every use goes in its own file.</b> <c>logs/bot-debugger-commands.log</c>, beside the log the polls and
/// the conclusions go in, because those are read forwards for what the shard is like and this is read
/// backwards from "why did that bot move" — and a hand whose uses are mixed in with its own observations is
/// a hand that can quietly alter what it is observing without the record showing it.
/// </para>
/// </summary>
public static class BotHand
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotHand));

    /// <summary>
    /// The verbs, and <c>none</c> heads them for the same reason <c>nothing</c> heads the kinds of finding:
    /// a hand with no way to stay in its pocket is a hand that is used every time it is offered.
    /// </summary>
    public static readonly string[] Verbs =
    [
        "none",
        "props",
        "sight",
        "where",
        "pack",
        "tile",
        "tele",
        "home",
        "res",
        "free",
        "shun",
        "camp",
        "summon",
        "call",
        "near"
    ];

    /// <summary>One clause each, for the prompt. Kept short because the prompt has a hard ceiling.</summary>
    public const string Manual =
        "none — do nothing, and it is the right answer most minutes. props <bot> — the engine's own view of it. "
        + "sight <bot> — whether it can see and lawfully strike what it is fighting, and from how far. "
        + "where <bot> — its tile, its region, and who is standing on top of it. pack <bot> — what it is "
        + "carrying, what it is allowed to keep of each, and what is surplus. tile <x> <y> — whether a body "
        + "fits there and what is on it. tele <bot> <x> <y> — lift it onto that tile, for a pocket it cannot "
        + "walk out of. home <bot> — lift it back to where the population lives. res <bot> — raise it if it is "
        + "dead. free <bot> — make it forget its plan and choose again. shun <bot> — leave whatever it is "
        + "fighting alone for a while, for something nothing can reach. camp [<x> <y>] — put an orc camp on the "
        + "ground, here or there, to see whether the population loots it and frees its prisoner. summon <bot> "
        + "— bring one to where I stand, to watch it work. call — bring Patrick to me, when he is on the shard "
        + "and there is something he should see with his own eyes. near <x> <y> — everything alive standing "
        + "around that spot, with its name, health and whether it would fight us.";

    /// <summary>
    /// Verbs the console may use and the model may not. <see cref="Run"/> accepts one of these when, and
    /// only when, it is told the order came in by hand.
    ///
    /// <para>
    /// <b>The model's choice is bounded by <see cref="Verbs"/> — <c>BotDebugNote</c> writes that array into
    /// its schema as an enumeration — so a verb left out of it is a verb no mind can reach.</b> That is
    /// where anything permanent belongs: razing the halls destroys buildings that outlive the whole
    /// population, and an idle thought at three in the morning is not a good enough reason for it.
    /// </para>
    /// </summary>
    public static readonly string[] HandVerbs = ["halls", "raze", "revel"];

    /// <summary>One clause each for the two above, for whoever is holding the keyboard.</summary>
    public const string ByHand =
        "halls — what the guilds own and where it stands. raze — take every guild hall off the island, "
        + "which is how an evening's building is undone. revel <trade> [<prize>] [<x> <y>] — declare one "
        + "this second instead of waiting a quarter of an hour for the watcher to think of it; naming a spot "
        + "raises a camp there, pulled into the ring around the population if it is too near or too far. "
        + "None is offered to the minds.";

    /// <summary>How many of each verb this session. For the summary, and for reading the hand's own habits.</summary>
    private static readonly Dictionary<string, long> _used = [];

    /// <summary>Times a verb was asked for and refused. A refusal is a measurement, so it is counted.</summary>
    public static long Refused { get; private set; }

    /// <summary>Times a verb was carried out.</summary>
    public static long Used { get; private set; }

    private static string _path;

    private static bool _broken;

    /// <summary>
    /// How far a bot may be lifted in one go, in tiles. Wide enough to cross Britain and out of a pocket on
    /// its far side, narrow enough that a hallucinated pair of coordinates moves somebody across a field
    /// rather than across a continent.
    /// </summary>
    public static int Reach { get; set; } = 600;

    public static void Open(string who)
    {
        _broken = false;
        _used.Clear();
        Used = 0;
        Refused = 0;

        try
        {
            var folder = Path.GetFullPath(Path.Combine(Core.BaseDirectory, "..", "logs"));

            Directory.CreateDirectory(folder);

            _path = Path.Combine(folder, "bot-debugger-commands.log");

            Write(new string('=', 96));
            Write($"{who} has hands from this moment: {string.Join(", ", Verbs)} — every use of them is on this file");
            Write(new string('=', 96));
        }
        catch (Exception e)
        {
            _broken = true;

            logger.Warning("The debugger's command log could not be opened, so it will have no hands: {Message}", e.Message);
        }
    }

    private static void Write(string line)
    {
        if (_broken || _path == null || line == null)
        {
            return;
        }

        try
        {
            File.AppendAllText(_path, $"[{DateTime.Now:HH:mm:ss}] {line}{Environment.NewLine}");
        }
        catch (Exception e)
        {
            _broken = true;

            logger.Warning("The debugger's command log stopped taking lines: {Message}", e.Message);
        }
    }

    /// <summary>
    /// Runs one verb and says what came of it in one line.
    ///
    /// <para>
    /// <b>Nothing happens unless the log is working.</b> The order is deliberate: a hand whose record can
    /// fail open is a hand nobody can audit, and this file is the whole reason the hand is safe to have.
    /// </para>
    /// </summary>
    /// <param name="who">Who asked — the model, the console, or the roll-call.</param>
    /// <param name="verb">One of <see cref="Verbs"/>.</param>
    /// <param name="tail">Whatever followed it.</param>
    /// <param name="why">The reason given. Written beside the use, because a use without one is a twitch.</param>
    public static string Run(string who, string verb, string tail, string why, bool byHand = false)
    {
        verb = (verb ?? "").Trim().ToLowerInvariant();
        tail = (tail ?? "").Trim();

        if (verb.Length == 0 || verb == "none" || verb == "-")
        {
            return null;
        }

        // Two lists, and which one is consulted depends on who is asking. A mind may only ever pick out of
        // Verbs — BotDebugNote writes that array into its schema as an enumeration — so anything that
        // destroys something permanent lives in HandVerbs, where no model can reach it.
        if (Array.IndexOf(Verbs, verb) < 0 && !(byHand && Array.IndexOf(HandVerbs, verb) >= 0))
        {
            Refused++;
            Write($"REFUSED {who}: \"{verb} {tail}\" — no such verb");

            return $"I have no verb \"{verb}\".";
        }

        if (_broken)
        {
            Refused++;

            return "my command log is not writable, so I will not use my hands.";
        }

        string answer;

        try
        {
            answer = Do(verb, tail);
        }
        catch (Exception e)
        {
            Refused++;
            Write($"THREW {who}: \"{verb} {tail}\" — {e.Message}");

            return $"{verb} threw: {e.Message}";
        }

        Used++;
        _used[verb] = _used.TryGetValue(verb, out var count) ? count + 1 : 1;

        Write($"{who}: {verb} {tail}");

        if (!string.IsNullOrWhiteSpace(why))
        {
            Write($"    because: {why}");
        }

        Write($"    -> {answer}");

        return answer;
    }

    private static string Do(string verb, string tail)
    {
        switch (verb)
        {
            case "tile":
                return Tile(tail);

            case "tele":
                return Tele(tail);

            case "camp":
                return Camp(tail);

            case "near":
                return Near(tail);

            case "call":
                return Call();

            // By hand only. See BotHand.ByHand: neither of these is in Verbs, so no mind can ask for them.
            case "halls":
                return BotEstate.Describe();

            case "revel":
                return Revelry(tail);

            case "raze":
                {
                    var gone = BotEstate.Raze();

                    return gone == 0
                        ? "there are no guild halls standing to take down."
                        : $"{gone} guild halls are off the island. What was inside them went with them.";
                }
        }

        var (name, _) = First(tail);
        var bot = Find(name);

        if (bot == null)
        {
            Refused++;

            return $"there is no bot called \"{name}\".";
        }

        return verb switch
        {
            "props" => Props(bot),
            "sight" => Sight(bot),
            "where" => Whereabouts(bot),
            "pack" => Packed(bot),
            "home" => Homeward(bot),
            "res" => Raise(bot),
            "free" => Free(bot),
            "shun" => Leave(bot),
            "summon" => Summon(bot),
            _ => $"I have no verb \"{verb}\"."
        };
    }

    /// <summary>
    /// Puts an orc camp on the ground: three orcs, a captain, a chest, a locked crate and a prisoner.
    ///
    /// <para>
    /// <b>The one verb here that adds to the world, and it earns that by being the only way to test two
    /// whole subsystems.</b> Looting chests and freeing prisoners were written on 08.09.2026 against a
    /// world that contains no camps unless somebody spawns one, and a camp spawned by hand is gone before
    /// anybody can walk to it: <c>OrcCamp</c> sets its own decay to five minutes and, alone among the camps,
    /// refuses to refresh that timer when somebody walks in. A camp 129 tiles from home therefore expires
    /// while the population is still on its way, which is exactly what happened to Patrick's.
    /// </para>
    ///
    /// <para>
    /// So the camp this puts down is given <see cref="CampMinutes"/> instead — long enough for a bot to
    /// notice it, walk to it, fight through it and walk a prisoner home. Everything else about it is the
    /// engine's own: the same class the world spawns, with the same contents and the same rules.
    /// </para>
    /// </summary>
    private static string Camp(string tail)
    {
        var here = BotVigil.Body;
        var map = here?.Map;

        if (map == null || map == Map.Internal)
        {
            return "I am nowhere, so there is nowhere to put a camp.";
        }

        var (xs, rest) = First(tail);
        var (ys, _) = First(rest);

        var where = here.Location;

        if (int.TryParse(xs, out var x) && int.TryParse(ys, out var y))
        {
            if (!BotStep.Settle(map, x, y, out var z))
            {
                Refused++;

                return $"({x}, {y}) has no floor a camp could stand on.";
            }

            where = new Point3D(x, y, z);
        }

        var camp = new OrcCamp();

        camp.MoveToWorld(where, map);
        camp.DecayDelay = TimeSpan.FromMinutes(CampMinutes);

        Camps++;

        return $"an orc camp is standing at ({where.X}, {where.Y}, {where.Z}) and will last {CampMinutes} minutes: "
            + "three orcs, a captain, an unlocked chest, a locked crate and a prisoner who wants to go to Britain.";
    }

    /// <summary>
    /// Everything alive around a spot: who it is, how hurt it is, and whether it would fight the population.
    ///
    /// <para>
    /// <b>Written because "is the camp still garrisoned" had no answer.</b> The tile verb reports items and
    /// floors; nothing reported creatures, so a chest standing unopened could not be told apart from a chest
    /// nobody had reached — and on 08.09.2026 that was exactly the question, with 300 refusals filed under
    /// "still held by a garrison" and no way to see what was holding it.
    /// </para>
    /// </summary>
    private static string Near(string tail)
    {
        var map = BotPopulation.Home;

        if (map == null || map == Map.Internal)
        {
            return "the population has no map to look at.";
        }

        var (xs, rest) = First(tail);
        var (ys, _) = First(rest);

        if (!int.TryParse(xs, out var x) || !int.TryParse(ys, out var y))
        {
            Refused++;

            return "near wants two numbers: near <x> <y>.";
        }

        BotStep.Settle(map, x, y, out var z);

        var at = new Point3D(x, y, z);
        var say = ValueStringBuilder.Create(512);
        var seen = 0;

        try
        {
            foreach (var who in map.GetMobilesInRange<Mobile>(at, NearReach))
            {
                if (who == null || who.Deleted || !who.Alive)
                {
                    continue;
                }

                if (seen++ >= 12)
                {
                    say.Append(", and more");

                    break;
                }

                if (seen > 1)
                {
                    say.Append("; ");
                }

                var beast = who as BaseCreature;
                var hostile = beast is { Controlled: false } && who.Karma < 0;

                say.Append(who.GetType().Name);
                say.Append(' ');
                say.Append(who.Name ?? "unnamed");
                say.Append(" at (");
                say.Append(who.X);
                say.Append(", ");
                say.Append(who.Y);
                say.Append(") ");
                say.Append(who.Hits);
                say.Append('/');
                say.Append(who.HitsMax);
                say.Append(hostile ? " HOSTILE" : beast != null ? " harmless" : " one of ours");
            }

            return seen == 0
                ? $"nothing alive within {NearReach} tiles of ({x}, {y})."
                : $"within {NearReach} tiles of ({x}, {y}): {say.ToString()}.";
        }
        finally
        {
            say.Dispose();
        }
    }

    /// <summary>How far around a spot the near verb looks. A camp is about a dozen tiles across.</summary>
    public static int NearReach { get; set; } = 14;

    /// <summary>How long a camp put down by hand lasts. The engine's own five minutes is shorter than the walk to it.</summary>
    public static int CampMinutes { get; set; } = 45;

    /// <summary>Camps put on the ground.</summary>
    public static long Camps { get; private set; }

    /// <summary>Bots brought to where the watcher stands.</summary>
    public static long Summoned { get; private set; }

    /// <summary>Times Patrick was called over.</summary>
    public static long Called { get; private set; }

    /// <summary>
    /// Brings one bot to where the watcher is standing, so it can be watched doing whatever it is doing.
    ///
    /// The same lift <see cref="Tele"/> makes and with the same warning: it is not travel, nothing prices it,
    /// and a bot moved this way has learned nothing about the road it did not walk.
    /// </summary>
    private static string Summon(BotMobile bot)
    {
        var here = BotVigil.Body;
        var map = here?.Map;

        if (map == null || map == Map.Internal)
        {
            return "I am nowhere, so there is nowhere to bring it.";
        }

        var was = bot.Location;

        for (var attempt = 0; attempt < 16; attempt++)
        {
            var x = here.X + Utility.RandomMinMax(-2, 2);
            var y = here.Y + Utility.RandomMinMax(-2, 2);

            if (!map.CanSpawnMobile(x, y, here.Z - 8, here.Z + 8, false, false, out var z))
            {
                continue;
            }

            bot.MoveToWorld(new Point3D(x, y, z), map);
            bot.Journey?.Finish();
            Summoned++;

            return $"{bot.Name} is beside me now, lifted from ({was.X}, {was.Y}); its errand has been let go.";
        }

        Refused++;

        return $"there is no room beside me for {bot.Name}.";
    }

    /// <summary>
    /// Brings Patrick to the watcher, when he is logged in and there is something worth his own eyes.
    ///
    /// <para>
    /// <b>Only a real person, and only one who is already here.</b> The population is made of PlayerMobiles
    /// — that is what lets a bot die properly — so "a player" is not a test. What is a test is a network
    /// connection: bots have none. Nothing is summoned, nothing is created; somebody already playing is
    /// moved a few tiles, and told why.
    /// </para>
    /// </summary>
    /// <summary>
    /// Declares a revel by hand, on the same terms the model gets: the trade must be one the ledger knows,
    /// and the prize comes out of the same treasury.
    ///
    /// It exists because the watcher thinks about this once a quarter of an hour, and a thing that can only
    /// be watched every fifteen minutes cannot be debugged at all.
    /// </summary>
    private static string Revelry(string tail)
    {
        var (kind, rest) = First(tail);

        if (string.IsNullOrWhiteSpace(kind))
        {
            return $"revel wants a trade: one of {string.Join(", ", BotRevel.Trades)}.";
        }

        if (!BotRevel.Known(kind))
        {
            return $"\"{kind}\" is not a trade this shard measures. It knows: {string.Join(", ", BotRevel.Trades)}.";
        }

        if (BotRevel.Running)
        {
            return $"the {BotRevel.Kind} revel is still running; one at a time.";
        }

        // "revel mine 500 1450 1500" — the prize, then optionally where the camp stands. Parsed
        // positionally because that is how every other verb on this console reads its tail, and a spot
        // without a camp is accepted and ignored rather than refused: the hand-called revel raises none.
        var parts = (rest ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var prize = parts.Length > 0 && int.TryParse(parts[0], out var asked) ? asked : BotRevel.MostPrize / 2;
        var camp = parts.Length >= 3;
        var spot = camp && int.TryParse(parts[1], out var sx) && int.TryParse(parts[2], out var sy)
            ? new Point3D(sx, sy, 0)
            : Point3D.Zero;

        return BotRevel.Declare(
            kind,
            prize,
            $"A {kind} revel! Whoever does most of it before the hour is out takes the purse.",
            "called by hand, from the console",
            spot != Point3D.Zero,
            spot
        );
    }

    private static string Call()
    {
        var here = BotVigil.Body;
        var map = here?.Map;

        if (map == null || map == Map.Internal)
        {
            return "I am nowhere, so there is nowhere to call anybody to.";
        }

        foreach (var state in NetState.Instances)
        {
            var who = state?.Mobile;

            if (who == null || who.Deleted || who is BotMobile or BotDebugger || who.Map == Map.Internal)
            {
                continue;
            }

            who.MoveToWorld(here.Location, map);
            who.SendMessage($"{here.Name} has called you over: {BotDebugMemory.Recite() ?? "come and look at this"}");
            Called++;

            return $"{who.Name} is standing beside me now and has been told why.";
        }

        Refused++;

        return "nobody is on the shard to call.";
    }

    /// <summary>The engine's own view of a bot, which is what every argument about one eventually needs.</summary>
    /// <summary>The skill this bot's class is actually measured in, and where it has got to.</summary>
    private static string Trade(BotMobile bot)
    {
        var wanted = bot.Class?.Skills;

        if (wanted == null || wanted.Count == 0)
        {
            return "no trade skill";
        }

        var first = wanted[0];

        return $"{first.Skill} {bot.Skills[first.Skill].Base:F0}/{first.Target:F0}";
    }

    private static string Props(BotMobile bot)
    {
        var weapon = bot.Weapon as BaseWeapon;
        var region = bot.Map == null ? null : Region.Find(bot.Location, bot.Map);

        return
            $"{bot.Name} the {bot.Class?.Name ?? "bot"}: {bot.Hits}/{bot.HitsMax} hits, {bot.Stam}/{bot.StamMax} stamina, "
            + $"{bot.Mana}/{bot.ManaMax} mana; str {bot.Str} dex {bot.Dex} int {bot.Int}; "
            + $"holding {(weapon == null ? "nothing" : weapon.GetType().Name)} reaching {weapon?.MaxRange ?? 0}; "
            + $"warmode {bot.Warmode}, fighting {bot.Combatant?.Name ?? "nobody"}; "
            + $"{bot.TotalWeight} of {bot.MaxWeight} stones; alive {bot.Alive}, frozen {bot.Frozen}; "
            + $"at ({bot.X}, {bot.Y}, {bot.Z}) on {bot.Map} in {region?.Name ?? "no named region"}; "
            // Its trade's own skill and the two everybody has. A claim that a population "knows how to
            // appraise" is a claim until something reads it back off a living bot, and nothing here did.
            + $"skills: {Trade(bot)}, ItemID {bot.Skills[SkillName.ItemID].Base:F0}; "
            + $"rank {bot.BotRank ?? "none"}; "
            + $"guild {(bot.Guild == null ? "none" : $"{bot.Guild.Name} [{bot.Guild.Abbreviation}]")}.";
    }

    /// <summary>
    /// The one question that decides whether a bot standing next to something is fighting it or watching it.
    ///
    /// <para>
    /// The engine gates every swing on <c>InLOS</c> and returns without so much as advancing the swing clock
    /// when the line is broken — see <c>Mobile.CheckCombatTime</c>. So "adjacent" and "able to hit" are two
    /// different facts, and until 03.09.2026 nothing on this shard could tell them apart from the outside.
    /// </para>
    /// </summary>
    private static string Sight(BotMobile bot)
    {
        var foe = bot.Combatant;

        if (foe == null)
        {
            return $"{bot.Name} is not fighting anything.";
        }

        var weapon = bot.Weapon as BaseWeapon;
        var away = (int)bot.GetDistanceToSqrt(foe.Location);
        var los = bot.InLOS(foe);
        var lawful = bot.CanBeHarmful(foe, false);
        var range = weapon?.MaxRange ?? 1;

        var verdict = (away <= range, los, lawful) switch
        {
            (false, _, _) => $"too far: {away} tiles against a reach of {range}",
            (true, false, _) => "near enough and no line to it — the engine will not let the blow leave",
            (true, true, false) => "near enough and in plain sight, and the engine refuses the blow",
            _ => "near enough, in sight, and swinging"
        };

        return $"{bot.Name} against {foe.Name} at ({foe.X}, {foe.Y}, {foe.Z}): {verdict}.";
    }

    /// <summary>
    /// Everything in a bot's pack, beside what it is allowed to keep of each.
    ///
    /// <para>
    /// <b>Written because the question could not be asked.</b> Patrick found a bot carrying ten sewing kits
    /// and eight heal potions on 09.09.2026, and there was no way to see a pack from outside the shard at
    /// all: <c>props</c> gives the weight and not what makes it up, and the table that decides what a bot
    /// keeps lived private in one file. The cause turned out to be two lines of <c>int.MaxValue</c> where a
    /// count belonged - and the only reason it survived a month is that nothing could print it.
    /// </para>
    ///
    /// <para>
    /// The surplus is marked rather than the total, because the total is not the interesting half. A pack
    /// holding forty cloth is fine if forty is what the trade eats and a fault if the allowance is five, and
    /// the two look identical in any list that gives only counts.
    /// </para>
    /// </summary>
    private static string Packed(BotMobile bot)
    {
        var pack = bot.Backpack;

        if (pack == null)
        {
            return $"{bot.Name} has no pack at all, which is a fault in itself.";
        }

        var keep = BotUnload.Keeps(bot);
        Dictionary<Type, int> held = [];

        for (var i = 0; i < pack.Items.Count; i++)
        {
            var item = pack.Items[i];

            if (item == null || item.Deleted)
            {
                continue;
            }

            // <b>The same two exclusions the sale itself makes, and leaving them out made this lie on its
            // first run.</b> It reported "Gold x340 (keeps 0, 340 surplus)" and a surplus spellbook on a
            // mage - neither of which BotUnload would ever touch, because Sell skips coin outright and
            // BotBinding protects what a bot is bound to. An instrument that reports surplus the seller
            // will never take is an instrument that raises an alarm about nothing, which is the failure
            // this shard has paid for more often than any other.
            if (item is Gold || BotBinding.IsBound(item, bot.Bond))
            {
                continue;
            }

            var kind = item.GetType();

            held[kind] = (held.TryGetValue(kind, out var many) ? many : 0) + Math.Max(1, item.Amount);
        }

        if (held.Count == 0)
        {
            return $"{bot.Name} is carrying nothing it could part with, at {BotLadder.Load(bot)} of {BotLadder.Ceiling(bot)} stones.";
        }

        List<string> lines = [];
        var over = 0;

        foreach (var (kind, many) in held)
        {
            var allowed = keep.TryGetValue(kind, out var cap) ? cap : 0;

            // A kind with no entry is merchandise outright, and saying "keeps 0" of it reads as a fault
            // rather than as the ordinary state of a thing a bot is carrying to sell.
            if (allowed >= int.MaxValue)
            {
                lines.Add($"{kind.Name} x{many} (kept without limit)");

                continue;
            }

            if (many > allowed)
            {
                over++;
                lines.Add($"{kind.Name} x{many} (keeps {allowed}, {many - allowed} surplus)");
            }
            else
            {
                lines.Add($"{kind.Name} x{many} (keeps {allowed})");
            }
        }

        lines.Sort(StringComparer.Ordinal);

        return $"{bot.Name} at {BotLadder.Load(bot)} of {BotLadder.Ceiling(bot)} stones, "
               + $"{held.Count} kinds and {over} of them over the allowance: {string.Join("; ", lines)}";
    }

    private static string Whereabouts(BotMobile bot)
    {
        var map = bot.Map;
        var region = map == null ? null : Region.Find(bot.Location, map);
        var crowd = 0;

        if (map != null && map != Map.Internal)
        {
            foreach (var other in map.GetMobilesInRange<Mobile>(bot.Location, 2))
            {
                if (other != bot && !other.Deleted)
                {
                    crowd++;
                }
            }
        }

        var standable = map != null && BotStep.Ground(map, bot.X, bot.Y, bot.Z, BotStep.StandingReach, out _);

        return
            $"{bot.Name} at ({bot.X}, {bot.Y}, {bot.Z}) on {map} in {region?.Name ?? "no named region"}; "
            + $"{crowd} others within 2 tiles; the tile itself {(standable ? "holds a body" : "does not hold a body")}; "
            + $"{(BotPopulation.Within(map, bot.Location) ? "inside" : "outside")} the ground the population may want anything on.";
    }

    /// <summary>What is on a tile and whether anybody could be. The question behind every pocket on this shard.</summary>
    private static string Tile(string tail)
    {
        var (xs, rest) = First(tail);
        var (ys, _) = First(rest);

        if (!int.TryParse(xs, out var x) || !int.TryParse(ys, out var y))
        {
            Refused++;

            return "tile wants two numbers: tile <x> <y>.";
        }

        var map = BotPopulation.Home;

        if (map == null)
        {
            return "the population has no map, so there is no tile to look at.";
        }

        var near = BotPopulation.Where;
        var stands = BotStep.Ground(map, x, y, near.Z, BotStep.StandingReach, out var z);
        var settles = BotStep.Settle(map, x, y, out var floor);
        var at = new Point3D(x, y, stands ? z : floor);
        var region = Region.Find(at, map);

        var items = ValueStringBuilder.Create(256);
        var seen = 0;

        try
        {
            foreach (var item in map.GetItemsInRange(at, 0))
            {
                if (seen++ >= 6)
                {
                    items.Append(", and more");

                    break;
                }

                if (seen > 1)
                {
                    items.Append(", ");
                }

                items.Append(item.GetType().Name);
                items.Append(" at z ");
                items.Append(item.Z);
            }

            var lying = seen == 0 ? "nothing lying on it" : $"on it: {items.ToString()}";

            return
                $"({x}, {y}): {(stands ? $"a body fits, standing at z {z}" : "no body fits at the height asked")}; "
                + $"the floor {(settles ? $"is at z {floor}" : "could not be found")}; "
                + $"region {region?.Name ?? "none named"}; "
                + $"{lying}.";
        }
        finally
        {
            items.Dispose();
        }
    }

    /// <summary>
    /// The GM's cure for a pocket: lift the bot out rather than argue with the pathfinder.
    ///
    /// <para>
    /// Bounded three ways, and each bound is one way a wrong pair of coordinates could do harm. Only
    /// somewhere a body actually fits, so nobody is posted into rock. Only within <see cref="Reach"/> of
    /// where the bot already is. And only ever a bot of ours.
    /// </para>
    /// </summary>
    private static string Tele(string tail)
    {
        var (name, rest) = First(tail);
        var (xs, more) = First(rest);
        var (ys, _) = First(more);

        var bot = Find(name);

        if (bot == null)
        {
            Refused++;

            return $"there is no bot called \"{name}\".";
        }

        if (!int.TryParse(xs, out var x) || !int.TryParse(ys, out var y))
        {
            Refused++;

            return "tele wants a bot and two numbers: tele <bot> <x> <y>.";
        }

        var map = bot.Map;

        if (map == null || map == Map.Internal)
        {
            return $"{bot.Name} is not on a map.";
        }

        if (!Utility.InRange(bot.Location, new Point3D(x, y, bot.Z), Reach))
        {
            Refused++;

            return $"({x}, {y}) is further than {Reach} tiles from {bot.Name}; I will not throw anybody that far.";
        }

        if (!BotStep.Ground(map, x, y, bot.Z, BotStep.StandingReach, out var z))
        {
            Refused++;

            return $"no body fits on ({x}, {y}) anywhere near {bot.Name}'s own height; nobody was moved.";
        }

        var from = bot.Location;

        bot.MoveToWorld(new Point3D(x, y, z), map);

        // The plan it was holding was a plan from somewhere else. Left alone, the bot walks straight back
        // into the pocket it has just been lifted out of.
        bot.Journey?.Discard();

        return $"{bot.Name} lifted from ({from.X}, {from.Y}, {from.Z}) to ({x}, {y}, {z}); its plan was torn up with it.";
    }

    private static string Homeward(BotMobile bot)
    {
        var map = BotPopulation.Home;
        var where = BotPopulation.Where;

        if (map == null)
        {
            return "the population has no home to send anybody to.";
        }

        // <b>The configured home tile is not necessarily a tile.</b> Asked on 04.09.2026 what was at
        // (1440, 1470) — this shard's own home — the answer was "no body fits and the floor could not be
        // found", while all eight of its neighbours held one: a static sitting on the exact point somebody
        // typed into the config. The population never noticed because its own birth scatters over six tiles
        // and takes the first spot that holds a body. Anything that walks to the literal point does notice,
        // so this does the same scatter rather than trusting the number.
        var at = Point3D.Zero;

        for (var ring = 0; ring <= BotPopulation.Spread && at == Point3D.Zero; ring++)
        {
            for (var dx = -ring; dx <= ring && at == Point3D.Zero; dx++)
            {
                for (var dy = -ring; dy <= ring; dy++)
                {
                    if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != ring)
                    {
                        continue;
                    }

                    if (BotStep.Ground(map, where.X + dx, where.Y + dy, where.Z, BotStep.StandingReach, out var found))
                    {
                        at = new Point3D(where.X + dx, where.Y + dy, found);

                        break;
                    }
                }
            }
        }

        if (at == Point3D.Zero)
        {
            Refused++;

            return $"nothing within {BotPopulation.Spread} tiles of home holds a body; nobody was moved.";
        }

        var from = bot.Location;

        bot.MoveToWorld(at, map);
        bot.Journey?.Discard();

        return $"{bot.Name} brought home from ({from.X}, {from.Y}, {from.Z}) to ({at.X}, {at.Y}, {at.Z}).";
    }

    private static string Raise(BotMobile bot)
    {
        if (bot.Alive)
        {
            return $"{bot.Name} is not dead.";
        }

        bot.Resurrect();

        return bot.Alive
            ? $"{bot.Name} is back on its feet."
            : $"{bot.Name} is still down; the engine refused it.";
    }

    private static string Free(BotMobile bot)
    {
        // The roll-call's own two steps, in the same order and for the same reason: the plan first, so that
        // whatever the work has learned about where it was going is written down before the work is ended.
        // Ending it first is how the debugger spent a night deleting the shard's own lessons. See BotAudit.
        var held = bot.Resolve?.Deed?.ToString() ?? "nothing";

        bot.Journey?.Discard();
        BotWill.Abandon(bot, "the debugger shook it loose by name");

        return $"{bot.Name} was holding {held}; its plan is torn up and the work is off it.";
    }

    private static string Leave(BotMobile bot)
    {
        var foe = bot.Combatant;

        if (foe == null)
        {
            return $"{bot.Name} is not fighting anything, so there is nothing to leave alone.";
        }

        BotQuarry.Shun(foe);

        return $"{foe.Name} is left alone for a while; nobody of ours will be offered it.";
    }

    /// <summary>
    /// A bot of ours by name, and nothing else, ever.
    ///
    /// <para>
    /// The whole safety of this file is one line long and it is this one: the roster is the only place a
    /// target may come from. A player's character, a shopkeeper, a staff member and a spawned creature are
    /// all simply not findable from here, whatever the model writes.
    /// </para>
    /// </summary>
    private static BotMobile Find(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var asked = name.Trim();
        var joint = asked.IndexOf(" the ", StringComparison.OrdinalIgnoreCase);

        if (joint > 0)
        {
            asked = asked[..joint].Trim();
        }

        var bots = BotPopulation.Bots;

        for (var i = 0; i < bots.Count; i++)
        {
            var bot = bots[i];

            if (bot is { Deleted: false } && string.Equals(bot.Name, asked, StringComparison.OrdinalIgnoreCase))
            {
                return bot;
            }
        }

        return null;
    }

    /// <summary>
    /// First word and the rest — except that a name may hold a space now (see <c>BotPopulation.Christen</c>),
    /// so the two-word reading is tried against the roster before the one-word split is trusted.
    /// </summary>
    private static (string Head, string Tail) First(string text)
    {
        text = (text ?? "").Trim();

        var space = text.IndexOf(' ');

        if (space < 0)
        {
            return (text, "");
        }

        var second = text.IndexOf(' ', space + 1);
        var two = second < 0 ? text : text[..second];

        if (Find(two) != null)
        {
            return (two, second < 0 ? "" : text[(second + 1)..].Trim());
        }

        return (text[..space], text[(space + 1)..].Trim());
    }

    public static string Describe()
    {
        if (Used == 0 && Refused == 0)
        {
            return "the debugger has not used its hands at all";
        }

        var sb = ValueStringBuilder.Create(256);

        try
        {
            sb.Append(Used);
            sb.Append(" commands run and ");
            sb.Append(Refused);
            sb.Append(" refused");

            if (_used.Count > 0)
            {
                sb.Append(": ");

                var first = true;

                foreach (var (verb, count) in _used)
                {
                    if (!first)
                    {
                        sb.Append(", ");
                    }

                    sb.Append(count);
                    sb.Append(' ');
                    sb.Append(verb);
                    first = false;
                }
            }

            return sb.ToString();
        }
        finally
        {
            sb.Dispose();
        }
    }
}
