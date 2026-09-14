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
        "near",
        "resolve",
        "roles"
    ];

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
        + "around that spot, with its name, health and whether it would fight us. resolve <bot> — what it is "
        + "holding, how far into its own reckoning, whether a better offer could take it off that now and why "
        + "not, what it put down to come back to, and how its last few pieces of work ended. roles — how each "
        + "class spends its working minutes: its own trade, anybody's work, another class's trade.";

    public static readonly string[] HandVerbs = ["halls", "raze", "revel", "wars", "seats", "seat", "save", "road", "resolves"];

    public const string ByHand =
        "halls — what the guilds own and where it stands. raze — take every guild hall off the island, "
        + "which is how an evening's building is undone. revel <trade> [<prize>] [<x> <y>] — declare one "
        + "this second instead of waiting a quarter of an hour for the watcher to think of it; naming a spot "
        + "raises a camp there, pulled into the ring around the population if it is too near or too far. "
        + "wars — every war standing, with its score and its clock. seats — where each guild lives and how far "
        + "its hall is from it. seat <guild> <x> <y> — move a guild's seat; its hall is carried there and its "
        + "members are born and rise there from then on. save — write the world to disk now, before the shard is "
        + "stopped: a kill without one rolls the island back to the last autosave, five minutes of halls and moves. "
        + "road <x1> <y1> <x2> <y2> — ask the pathfinder for a way between two tiles with a generous clock, and say "
        + "what it found: the answer to \"why can nobody get there\". resolves — how much of what the population "
        + "takes on it sees through, what takes it off the rest, and the same per trade. None is offered to the minds.";

    private static readonly Dictionary<string, long> _used = [];

    public static long Refused { get; private set; }

    public static long Used { get; private set; }

    private static string _path;

    private static bool _broken;

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

    public static string Run(string who, string verb, string tail, string why, bool byHand = false)
    {
        verb = (verb ?? "").Trim().ToLowerInvariant();
        tail = (tail ?? "").Trim();

        if (verb.Length == 0 || verb == "none" || verb == "-")
        {
            return null;
        }

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

            case "roles":
                return BotCalling.Describe();

            case "halls":
                return BotEstate.Describe();

            case "revel":
                return Revelry(tail);

            case "wars":
                return BotWar.Describe();

            case "seats":
                return BotSeat.Tell();

            case "seat":
                return Seat(tail);

            case "road":
                return Road(tail);

            case "resolves":
                return BotWill.DescribeResolve();

            case "save":
                {
                    if (World.Saving)
                    {
                        return "the world is being saved already.";
                    }

                    World.Save();

                    return "the world is written to disk; it is safe to stop the shard now.";
                }

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
            "resolve" => BotWill.Explain(bot),
            _ => $"I have no verb \"{verb}\"."
        };
    }

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

        var asked = here.Location;

        if (int.TryParse(xs, out var x) && int.TryParse(ys, out var y))
        {
            asked = new Point3D(x, y, 0);
        }

        if (!BotRevel.Footing(map, asked, out var found, out var floor))
        {
            Refused++;

            return $"({asked.X}, {asked.Y}) has no dry ground a camp could stand on, and nor does anything "
                + $"within {BotRevel.CampSweep} tiles of it.";
        }

        var where = new Point3D(found.X, found.Y, floor);

        var camp = new OrcCamp();

        camp.MoveToWorld(where, map);
        camp.DecayDelay = TimeSpan.FromMinutes(CampMinutes);

        Camps++;

        return $"an orc camp is standing at ({where.X}, {where.Y}, {where.Z}) and will last {CampMinutes} minutes: "
            + "three orcs, a captain, an unlocked chest, a locked crate and a prisoner who wants to go to Britain.";
    }

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

    public static int NearReach { get; set; } = 14;

    public static int CampMinutes { get; set; } = 45;

    public static long Camps { get; private set; }

    public static long Summoned { get; private set; }

    public static long Called { get; private set; }

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
            + $"skills: {Trade(bot)}, ItemID {bot.Skills[SkillName.ItemID].Base:F0}; "
            + $"rank {bot.BotRank ?? "none"}; "
            + $"guild {(bot.Guild == null ? "none" : $"{bot.Guild.Name} [{bot.Guild.Abbreviation}]")}.";
    }

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

    private static string Seat(string tail)
    {
        var words = (tail ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (words.Length < 3 || !int.TryParse(words[^2], out var x) || !int.TryParse(words[^1], out var y))
        {
            Refused++;

            return "seat wants a guild and two numbers: seat <guild> <x> <y>.";
        }

        var named = string.Join(' ', words, 0, words.Length - 2);
        Server.Guilds.Guild found = null;

        foreach (var guild in BotGuilds.Standing)
        {
            if (string.Equals(guild.Name, named, StringComparison.OrdinalIgnoreCase)
                || string.Equals(BotClaim.Short(guild.Name), named.ToUpperInvariant(), StringComparison.Ordinal))
            {
                found = guild;

                break;
            }
        }

        if (found == null)
        {
            Refused++;

            return $"there is no guild called \"{named}\". Standing: {BotSeat.Tell()}";
        }

        var map = BotPopulation.Home;

        if (map == null || !BotStep.Settle(map, x, y, out var z))
        {
            Refused++;

            return $"no body could stand at {x},{y}; a seat has to be ground somebody can be put down on.";
        }

        BotSeat.Set(found.Name, new Point3D(x, y, z));

        return $"the seat of {found.Name} is {x},{y} now. {BotSeat.Tell()}";
    }

    private static string Road(string tail)
    {
        var words = (tail ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (words.Length < 4 || !int.TryParse(words[0], out var x1) || !int.TryParse(words[1], out var y1)
            || !int.TryParse(words[2], out var x2) || !int.TryParse(words[3], out var y2))
        {
            Refused++;

            return "road wants four numbers: road <x1> <y1> <x2> <y2>.";
        }

        var map = BotPopulation.Home;

        if (map == null)
        {
            return "the population has no map, so there is no road to look for.";
        }

        var stood1 = BotStep.Settle(map, x1, y1, out var z1);
        var stood2 = BotStep.Settle(map, x2, y2, out var z2);

        map.GetAverageZ(x1, y1, out _, out var avg1, out _);
        map.GetAverageZ(x2, y2, out _, out var avg2, out _);

        var from = new Point3D(x1, y1, stood1 ? z1 : avg1);
        var to = new Point3D(x2, y2, stood2 ? z2 : avg2);
        var footing = (stood1 ? "" : $" (nobody could be put down at {from}, the land's height is used)")
            + (stood2 ? "" : $" (nobody could be put down at {to}, the land's height is used)");
        var path = new List<Point3D>();
        var began = System.Diagnostics.Stopwatch.GetTimestamp();
        var outcome = BotPath.Find(map, from, to, BotArrival.Within(1), path, default, BotPath.CeilingMs * 5);
        var ms = (System.Diagnostics.Stopwatch.GetTimestamp() - began) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
        var end = path.Count > 0 ? path[^1] : from;

        return $"from {from} to {to}: {outcome}, {path.Count} tiles of plan ending at {end}, in {ms:F1}ms{footing}"
            + (BotPath.LastStarved ? " (starved of clock by the population's window; ask again)" : "")
            + $"; the far side says {BotPath.Enclose(map, to, BotArrival.Within(1))}.";
    }

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
