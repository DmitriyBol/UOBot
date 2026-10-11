using System;
using System.Collections.Generic;
using Server.Logging;
using Server.Mobiles;
using Server.Regions;

namespace Server.BotAI.V2;

/// <summary>
/// The nearest guarded town a beaten bot can realistically run to, and whether the guards there can do anything about
/// what follows it.
///
/// <para>
/// <b>Patrick's order of 02.10.2026: "if a bot is beaten on the road and understands it cannot win the fight, it must run
/// to town, because the guards are supposed to kill hostile creatures that fight within the town limits."</b> What the
/// engine actually does, read from <c>GuardedRegion</c> and <c>BaseGuard</c> in this era (UOR, <c>AllowReds</c> off):
/// </para>
///
/// <para>
/// A guard is spawned for a <em>guard candidate</em> only: a mobile that is <c>Criminal</c> or a murderer
/// (<c>GuardedRegion.IsGuardCandidate</c>). In the stock engine nothing looks at whether a thing is a monster (see the shard rule below). A creature becomes criminal by
/// harming an <em>innocent</em> (<c>Mobile.DoHarmful</c> → <c>IsHarmfulCriminal</c> → <c>Notoriety.Compute</c> answering
/// <c>Innocent</c>), and a bot is innocent to a creature unless the bot has struck that creature within the last two minutes
/// (<c>AggressorInfo.ExpireDelay</c>; <c>Notoriety.MobileNotoriety</c> answers <c>CanBeAttacked</c> through
/// <c>CheckAggressor(source.Aggressors, target)</c>). So: a serpent that bites a bot which never swung at it is criminal on
/// the first bite, for two minutes; a serpent that bites a bot which swung back is not criminal for that bite, and the
/// region never puts it on its list. Once a criminal act happens with the victim inside a guarded region
/// (<c>OnAggressed</c>) or the criminal inside it (<c>OnCriminalAction</c>), the region remembers the criminal for fifteen
/// seconds; if a human NPC of the town stands within eight tiles, that NPC calls the guards at once ("fakeCall"); otherwise
/// anybody saying a sentence with the <em>guards</em> keyword (0x0007) within fourteen tiles calls them
/// (<c>GuardedRegion.OnSpeech</c> → <c>CallGuards</c>). A guard teleports to the candidate and kills it outright
/// (<c>guards.instantKill</c>, true by default), leaving no corpse and no loot.
/// </para>
///
/// <para>
/// <b>The honest lever, then, is the one a player has: say "Guards!" standing in the town with the thing on you</b>
/// (<c>Mobile.DoSpeech</c> with the keyword, which is exactly what a client sends), and <em>do not swing at it</em>, because
/// the swing is what makes the guards blind to it for two minutes. Both are done by <see cref="BotBolt"/> on a run for a
/// town; the swing is held by <c>BotMobile.OnDamage</c> (<see cref="BotBolt.HoldsSwing"/>). What this class decides is
/// whether a town is a realistic escape at all: near enough for the time the run will take, the pursuer no faster than
/// the runner unless the town is a few steps off, and no deadlier ground on the way (<see cref="BotDanger.Road"/>).
/// </para>
///
/// <para>
/// <b>Why the guards killed none of 78 pursuers (build 429, 07-08.10.2026), and the shard rule that answers it.</b> Two
/// walls, both in the engine. First, a bot that swung once at the thing before it ran is never innocent to it again while
/// the chase goes on: every bite the creature lands refreshes the bot's own entry in the creature's aggressors
/// (<c>Mobile.AggressiveAction</c>, the loop over <c>aggressor.Aggressors</c>), so the two-minute expiry
/// <see cref="BotBolt.HavenMs"/> waited for never came. 37 of 78 calls (93 of 141 by 01:35) were made so, Orophin's
/// solen and Wilhelm's skeleton among them. Second, a creature made criminal by a bite on the road is not on the town's
/// list (<c>CallGuards</c> takes only listed candidates, or reds standing inside), and only a fresh criminal bite in town
/// lists it. And most deaths in town came after the run had ended (a plain flight that stopped inside the walls, a mend
/// there: Wilhelm, Ysolde, Norbert), when nobody called at all. So <c>GuardedRegion.IsMenace</c> (a wild creature that
/// attacks on sight, fighting a player) is a guard candidate whoever struck first, and <see cref="CallGuards"/> is called
/// by every bot a creature strikes on guarded ground, not only by a runner.
/// </para>
/// </summary>
public static class BotHaven
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotHaven));

    public static bool Running { get; set; } = true;

    public static int Reach { get; set; } = 300;

    public static int MostSeconds { get; set; } = 90;

    public static int NearTiles { get; set; } = 40;

    public static int Stride { get; set; } = 8;

    public static int Towns { get; set; } = 3;

    public static long Asked { get; private set; }

    public static long Chosen { get; private set; }

    public static long NoTown { get; private set; }

    public static long TooFar { get; private set; }

    public static long TooFast { get; private set; }

    public static long DeadlyRoad { get; private set; }

    public static long Unreachable { get; private set; }

    public static Point3D Nearest(Map map, Point3D from, out string town, out int tiles)
    {
        town = null;
        tiles = int.MaxValue;

        if (map == null || map == Map.Internal || !BotTowns.Ensure(map))
        {
            return Point3D.Zero;
        }

        var best = Point3D.Zero;
        var tried = 0;

        Span<(int Away, int Index)> order = stackalloc (int, int)[Math.Min(32, BotTowns.All.Count)];
        var count = 0;

        for (var i = 0; i < BotTowns.All.Count && count < order.Length; i++)
        {
            var candidate = BotTowns.All[i];

            if (!candidate.FromHome && !candidate.BySea && !candidate.Home)
            {
                continue;
            }

            var away = Math.Max(Math.Abs(candidate.Square.X - from.X), Math.Abs(candidate.Square.Y - from.Y));

            if (away > Reach)
            {
                continue;
            }

            var at = count;

            while (at > 0 && order[at - 1].Away > away)
            {
                order[at] = order[at - 1];
                at--;
            }

            order[at] = (away, i);
            count++;
        }

        for (var o = 0; o < count && tried < Math.Max(1, Towns); o++)
        {
            var candidate = BotTowns.All[order[o].Index];

            tried++;

            if (!BotGates.Joined(map, from, candidate.Square))
            {
                continue;
            }

            var edge = Edge(map, from, candidate.Square, out var walked);

            if (edge == Point3D.Zero || walked >= tiles)
            {
                continue;
            }

            best = edge;
            tiles = walked;
            town = Region.Find(edge, map)?.GetRegion<GuardedRegion>()?.Name ?? candidate.Name;
        }

        return best;
    }

    private static Point3D Edge(Map map, Point3D from, Point3D square, out int tiles)
    {
        tiles = 0;

        var dx = square.X - from.X;
        var dy = square.Y - from.Y;
        var span = Math.Max(Math.Abs(dx), Math.Abs(dy));
        var stride = Math.Max(2, Stride);

        if (span <= 0)
        {
            return Point3D.Zero;
        }

        var found = Point3D.Zero;
        var foundAt = 0;

        for (var out_ = stride; ; out_ += stride)
        {
            var step = Math.Min(out_, span);
            var x = from.X + dx * step / span;
            var y = from.Y + dy * step / span;

            if (x < 0 || y < 0 || x >= map.Width || y >= map.Height)
            {
                break;
            }

            var standing = BotStep.Settle(map, x, y, out var z);
            var here = new Point3D(x, y, z);
            var guarded = standing && Guarded(map, here) && !BotReachMap.RefusesTile(map, from, here);

            if (guarded && BotWayless.Shut(map, here, true))
            {
                BotWayless.PassedByHaven();

                guarded = false;

                if (found == Point3D.Zero)
                {
                    if (step >= span)
                    {
                        break;
                    }

                    continue;
                }
            }

            if (guarded)
            {
                var inside = found != Point3D.Zero;

                found = here;
                foundAt = step;

                if (inside)
                {
                    break;
                }
            }
            else if (found != Point3D.Zero)
            {
                break;
            }

            if (step >= span)
            {
                break;
            }
        }

        tiles = foundAt;

        return found;
    }

    public static bool Guarded(Map map, Point3D where) =>
        map != null && Region.Find(where, map)?.GetRegion<GuardedRegion>() is { } guarded && !guarded.IsDisabled();

    public static bool Choose(Mobile body, BaseCreature pursuer, out Point3D haven, out string town, out int tiles)
    {
        haven = Point3D.Zero;
        town = null;
        tiles = 0;

        var map = body?.Map;

        if (!Running || map == null || map == Map.Internal || !body.Alive)
        {
            return false;
        }

        if (BotDungeon.Under(body.Location))
        {
            return false;
        }

        Asked++;

        haven = Nearest(map, body.Location, out town, out tiles);

        if (haven == Point3D.Zero)
        {
            NoTown++;

            return false;
        }

        var stepMs = StepMs(body);
        var seconds = tiles * stepMs / 1000.0;

        if (seconds > Math.Max(5, MostSeconds))
        {
            TooFar++;
            haven = Point3D.Zero;

            return false;
        }

        if (pursuer != null && tiles > NearTiles && pursuer.ActiveSpeed * 1000.0 < stepMs)
        {
            TooFast++;
            haven = Point3D.Zero;

            return false;
        }

        if (BotDanger.Road(body, map, body.Location, haven, BotThreat.Power(body), out _, out _, out _))
        {
            DeadlyRoad++;
            haven = Point3D.Zero;

            return false;
        }

        Chosen++;

        return true;
    }

    public static int StepMs(Mobile body)
    {
        var running = body is BotMobile { Running: true };
        var step = body.Mounted ? BotSlay.MountStepMs : BotSlay.FootStepMs;

        return running ? step : step * 2;
    }

    public static bool Aggressed(Mobile bot, Mobile creature)
    {
        if (bot == null || creature == null)
        {
            return false;
        }

        var list = creature.Aggressors;

        for (var i = 0; i < list.Count; i++)
        {
            var info = list[i];

            if (info.Attacker == bot && !info.Expired)
            {
                return true;
            }
        }

        list = creature.Aggressed;

        for (var i = 0; i < list.Count; i++)
        {
            var info = list[i];

            if (info.Defender == bot && !info.CriminalAggression && !info.Expired)
            {
                return true;
            }
        }

        return false;
    }

    public static void Refused() => Unreachable++;

    public static int CallEveryMs { get; set; } = 3000;

    public static int WatchMs { get; set; } = 60000;

    public const int CallReach = 14;

    public static long Calls { get; private set; }

    public static long CallsOnCriminal { get; private set; }

    public static long CallsOnMenace { get; private set; }

    public static long CallsOnAnother { get; private set; }

    public static long CallsRefused { get; private set; }

    public static long RefusedFar { get; private set; }

    public static long RefusedGuardOn { get; private set; }

    public static long RefusedUnlisted { get; private set; }

    public static long RefusedBlind { get; private set; }

    public static long RefusedNotCriminal { get; private set; }

    public static long RefusedOther { get; private set; }

    public static long GuardKills { get; private set; }

    public static long GuardKillsAll { get; private set; }

    public static long QuarryTaken { get; private set; }

    public static long Arrived { get; private set; }

    public static long Saved { get; private set; }

    public static long DiedInTown { get; private set; }

    public static long DeathsGuarded { get; private set; }

    public static long DeathsGuardedToMenace { get; private set; }

    private static readonly Dictionary<string, int> _guardedKillers = new();

    private static readonly Dictionary<Mobile, long> _lastCall = new();

    private static readonly Dictionary<Mobile, long> _lastRefused = new();

    private static readonly Dictionary<Mobile, (Mobile Bot, long Tick)> _calledOn = new();

    private static readonly Dictionary<Mobile, (string Town, long Tick)> _arrivals = new();

    private static readonly List<Mobile> _sweep = [];

    private static Timer _timer;

    private enum Found
    {
        Criminal,
        Menace,
        Another,
        Far,
        GuardOn,
        Unlisted,
        Blind,
        NotCriminal,
        Other
    }

    public static void Configure() => BaseGuard.Killing += OnGuardKilling;

    public static bool CallGuards(Mobile body, Mobile foe, string why)
    {
        if (body?.Map == null || foe == null || foe.Deleted || !body.Alive
            || body.Region.GetRegion<GuardedRegion>() is not { } region || region.IsDisabled())
        {
            return false;
        }

        var now = Core.TickCount;

        if (_lastCall.TryGetValue(body, out var last) && now - last < CallEveryMs)
        {
            return false;
        }

        var found = Judge(body, foe, region);

        if (found is not (Found.Criminal or Found.Menace) && Another(body, foe, region))
        {
            found = Found.Another;
        }

        if (found is not (Found.Criminal or Found.Menace or Found.Another))
        {
            if (_lastRefused.TryGetValue(body, out var refused) && now - refused < CallEveryMs)
            {
                return false;
            }

            _lastRefused[body] = now;
            Watch();
            CallsRefused++;

            switch (found)
            {
                case Found.Far:
                    RefusedFar++;
                    break;
                case Found.GuardOn:
                    RefusedGuardOn++;
                    break;
                case Found.Unlisted:
                    RefusedUnlisted++;
                    break;
                case Found.Blind:
                    RefusedBlind++;
                    break;
                case Found.NotCriminal:
                    RefusedNotCriminal++;
                    break;
                default:
                    RefusedOther++;
                    break;
            }

            return false;
        }

        _lastCall[body] = now;
        Calls++;

        switch (found)
        {
            case Found.Criminal:
                CallsOnCriminal++;
                break;
            case Found.Menace:
                CallsOnMenace++;
                break;
            default:
                CallsOnAnother++;
                break;
        }

        _calledOn[foe] = (body, now);
        Watch();

        body.DoSpeech("Guards! Help!", [0x0007], MessageType.Regular, body.SpeechHue);

        logger.Information(
            "{Name} called the guards of {Town} on {Foe} ({Tiles} tiles off) {Why}: {Found}",
            body.Name,
            region.Name ?? "a town",
            foe.Name,
            (int)body.GetDistanceToSqrt(foe),
            why,
            found switch
            {
                Found.Criminal => "a criminal",
                Found.Menace => "a hostile creature on a bot",
                _ => "not this one, but another within reach"
            }
        );

        return true;
    }

    private static Found Judge(Mobile body, Mobile foe, GuardedRegion region)
    {
        if (foe.Map != body.Map || !body.InRange(foe, CallReach))
        {
            return Found.Far;
        }

        if (region.IsCallable(foe))
        {
            return foe.Criminal || foe.Murderer ? Found.Criminal : Found.Menace;
        }

        foreach (var guard in foe.GetMobilesInRange<BaseGuard>(15))
        {
            if (guard.Focus == foe)
            {
                return Found.GuardOn;
            }
        }

        if (region.IsGuardCandidate(foe))
        {
            return Found.Unlisted;
        }

        if (foe is not BaseCreature || GuardedRegion.IsPlayerSide(foe))
        {
            return Found.Other;
        }

        return Aggressed(body, foe) ? Found.Blind : Found.NotCriminal;
    }

    private static bool Another(Mobile body, Mobile foe, GuardedRegion region)
    {
        foreach (var m in body.Map.GetMobilesInRange(body.Location, CallReach))
        {
            if (m != foe && m != body && region.IsCallable(m))
            {
                return true;
            }
        }

        return false;
    }

    private static void OnGuardKilling(BaseGuard guard, Mobile target)
    {
        if (target is not BaseCreature creature)
        {
            return;
        }

        GuardKillsAll++;

        if (_calledOn.Remove(creature, out var call))
        {
            GuardKills++;

            logger.Information(
                "The guards cut down {Foe}, called on by {Name} {Seconds}s before",
                creature.Name,
                call.Bot.Name,
                (Core.TickCount - call.Tick) / 1000
            );
        }

        var list = creature.Aggressors;

        for (var i = 0; i < list.Count; i++)
        {
            if (list[i].Attacker is BotMobile { Resolve.Deed: BotSlay slay } hunter && slay.Quarry == creature)
            {
                QuarryTaken++;

                logger.Information(
                    "The guards took {Name}'s quarry {Foe} at ({X}, {Y}): no corpse, no loot",
                    hunter.Name,
                    creature.Name,
                    creature.X,
                    creature.Y
                );

                break;
            }
        }
    }

    public static void Reached(Mobile body, string town)
    {
        if (body == null || _arrivals.ContainsKey(body))
        {
            return;
        }

        Arrived++;
        _arrivals[body] = (town, Core.TickCount);
        Watch();
    }

    public static void Died(Mobile body, Point3D at, Map map)
    {
        if (body == null)
        {
            return;
        }

        var killer = body.LastKiller;

        if (Guarded(map, at))
        {
            DeathsGuarded++;

            var menace = GuardedRegion.IsHostileCreature(killer);

            if (menace)
            {
                DeathsGuardedToMenace++;
            }

            var kind = killer?.GetType().Name ?? "nothing named";
            _guardedKillers[kind] = _guardedKillers.GetValueOrDefault(kind) + 1;

            logger.Information(
                "{Name} died on guarded ground in {Town} at ({X}, {Y}), killed by {Killer} ({Kind}, {Sort}), doing {Doing}",
                body.Name,
                Region.Find(at, map)?.GetRegion<GuardedRegion>()?.Name ?? "a town",
                at.X,
                at.Y,
                killer?.Name ?? "nothing named",
                kind,
                menace ? "a creature the guards take" : "not one the guards take for a bot",
                (body as BotMobile)?.Resolve?.Deed?.Kind ?? "nothing"
            );
        }

        if (!_arrivals.Remove(body, out var arrival))
        {
            return;
        }

        DiedInTown++;

        logger.Information(
            "{Name} died {Seconds}s after reaching {Town}, killed by {Killer}",
            body.Name,
            (Core.TickCount - arrival.Tick) / 1000,
            arrival.Town,
            killer?.Name ?? "nothing named"
        );
    }

    private static void Watch() => _timer ??= Timer.DelayCall(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2), Sweep);

    private static void Sweep()
    {
        var now = Core.TickCount;

        _sweep.Clear();

        foreach (var (foe, call) in _calledOn)
        {
            if (foe.Deleted || !foe.Alive || now - call.Tick >= WatchMs)
            {
                _sweep.Add(foe);
            }
        }

        foreach (var foe in _sweep)
        {
            _calledOn.Remove(foe);
        }

        _sweep.Clear();

        foreach (var (body, arrival) in _arrivals)
        {
            if (body.Deleted)
            {
                _sweep.Add(body);
            }
            else if (now - arrival.Tick >= WatchMs)
            {
                Saved++;
                _sweep.Add(body);
            }
        }

        foreach (var body in _sweep)
        {
            _arrivals.Remove(body);
        }

        Expire(_lastCall, now);
        Expire(_lastRefused, now);

        if (_calledOn.Count == 0 && _arrivals.Count == 0 && _lastCall.Count == 0 && _lastRefused.Count == 0)
        {
            _timer?.Stop();
            _timer = null;
        }
    }

    private static void Expire(Dictionary<Mobile, long> clocks, long now)
    {
        _sweep.Clear();

        foreach (var (body, tick) in clocks)
        {
            if (body.Deleted || now - tick >= WatchMs)
            {
                _sweep.Add(body);
            }
        }

        foreach (var body in _sweep)
        {
            clocks.Remove(body);
        }

        _sweep.Clear();
    }

    private static string TopKillers()
    {
        if (_guardedKillers.Count == 0)
        {
            return "none";
        }

        var all = new List<KeyValuePair<string, int>>(_guardedKillers);
        all.Sort((a, b) => b.Value.CompareTo(a.Value));

        var parts = new List<string>();

        for (var i = 0; i < all.Count && i < 5; i++)
        {
            parts.Add($"{all[i].Key} {all[i].Value}");
        }

        return string.Join(", ", parts);
    }

    public static string DescribeGuards() =>
        $"guards called by any bot on guarded ground: {Calls} calls said ({CallsOnCriminal} on a criminal, {CallsOnMenace} on a hostile creature on a bot, {CallsOnAnother} for another within {CallReach} tiles), "
        + $"{CallsRefused} not said as the guards would not come ({RefusedFar} the thing beyond {CallReach} tiles, {RefusedGuardOn} a guard already on it, {RefusedUnlisted} a criminal not on the town's list, {RefusedBlind} blind for the bot's own quarrel with something no menace, {RefusedNotCriminal} not criminal, {RefusedOther} something the guards never take), "
        + $"{GuardKills} of the things called on cut down by the guards, {GuardKillsAll} creatures cut down by the guards in all, {QuarryTaken} of them a bot's quarry; "
        + $"{Arrived} runners reached a town with something on them, {Saved} alive {WatchMs / 1000}s later, {DiedInTown} dead inside that; "
        + $"{DeathsGuarded} bot deaths on guarded ground ({DeathsGuardedToMenace} to a creature the guards take; by kind: {TopKillers()})"
        + $"{(GuardedRegion.GuardsHostileCreatures ? "" : "; the hostile-creature rule is off")}";

    public static string Describe() =>
        $"towns weighed as a refuge for a beaten bot: {Asked} asked, {Chosen} chosen, {NoTown} with no guarded town within {Reach} tiles, {TooFar} further than {MostSeconds}s of running, {TooFast} against a pursuer quicker than the runner beyond {NearTiles} tiles, {DeadlyRoad} over a road asking more than the runner brings, {Unreachable} turned back to the plain flight for a refused road{(Running ? "" : "; off")}; {DescribeGuards()}";

    public static void Forget()
    {
        Asked = 0;
        Chosen = 0;
        NoTown = 0;
        TooFar = 0;
        TooFast = 0;
        DeadlyRoad = 0;
        Unreachable = 0;
        Calls = 0;
        CallsOnCriminal = 0;
        CallsOnMenace = 0;
        CallsOnAnother = 0;
        CallsRefused = 0;
        RefusedFar = 0;
        RefusedGuardOn = 0;
        RefusedUnlisted = 0;
        RefusedBlind = 0;
        RefusedNotCriminal = 0;
        RefusedOther = 0;
        GuardKills = 0;
        GuardKillsAll = 0;
        QuarryTaken = 0;
        Arrived = 0;
        Saved = 0;
        DiedInTown = 0;
        DeathsGuarded = 0;
        DeathsGuardedToMenace = 0;
        _guardedKillers.Clear();
    }
}
