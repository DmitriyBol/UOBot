using System;
using System.Collections.Generic;
using Server.BotAI.V2;
using Server.Items;
using Server.Logging;
using Server.Mobiles;

namespace Server.BotAI.Mind;

/// <summary>
/// The championship: every hour Argus's herald calls the strongest of the ranks to a ring, sets them against each other one
/// pair at a time for the right to challenge the champion, and the one left standing meets the champion for the title.
///
/// <para>
/// <b>Patrick's order of 15.09.2026.</b> Thirty entrants, one against one, the winner goes on; the champion's name
/// turns yellow and it is handed a weapon or a piece of armour fit for its class. Held by the calendar, and by hand
/// through the door (<c>do tourney</c>) whenever somebody wants to see one.
/// </para>
///
/// <para>
/// <b>Patrick, 02.10.2026: "I have never once seen a fight for the champion's title; it seems to me this mechanic is
/// completely broken."</b> It had no such fight to show. Every hour was a fresh knockout and the champion one more entrant,
/// seeded by power like anybody: of the 14 championships between the wipe of 01.10.2026 11:32 and build 389, not one ended
/// with the champion meeting a challenger. The champion entered only 6 of the 13 that followed a crown — the other seven it
/// was with a company or resting — and lost before any final in all six. Six of the fourteen finals were never fought at all:
/// one finalist "with a company" forfeited, and twice the bot crowned was the one with the company ("Miriel is with a company
/// and forfeits; Miriel goes on", crowned at 07:27), because a pair with both sides busy sent the busy one on. The record of who held the
/// title was a file under <c>Saves/</c>, which every world save moves into the backups, so each of the five boots since the
/// wipe began "champion nobody" with the hour's clock restarted. And nothing was said where a player could hear it: the
/// session log, and one local line over the winner's head at the ring.
/// </para>
///
/// <para>
/// <b>So now: the ranks fight for the shot, the champion defends, the title changes hands in the ring.</b> The champion is
/// kept out of the bracket. When one challenger is left, the title bout is called: champion against challenger, in the ring,
/// announced in the world channel before and after. With the title vacant the bracket's final is the title bout. A champion
/// who cannot come — resting, with a company, down a dungeon, dead — is waited for <see cref="TitleWaitMs"/>; still away, it
/// is stripped and the challenger crowned, and a challenger who cannot come leaves the champion the title. Every outcome has
/// its own counter in the summary. The record — the champion, when its reign began, its defences, when the last was held —
/// is kept by the world save (<see cref="BotTourneyStore"/>), so a deploy no longer forgets the champion.
/// </para>
///
/// <para>
/// <b>Driven from the watcher's own beat, one pair at a time, and nothing here holds the game loop.</b> Each beat
/// looks at one thing: whether the next pair should be called, whether the pair in the ring has been decided, whether the
/// pause between fights is over, or whether the champion and its challenger are both free for the title bout. The duel itself
/// is <see cref="BotDuel"/> — the pair, the rule, and the two brawls pressed on the fighters — and the fighters' own bodies do
/// the fighting. Argus's part is the summons, the healing, the bracket and the prize: exactly a referee's.
/// </para>
///
/// <para>
/// <b>Only the pair in the ring is summoned.</b> The other entrants go on with their day and are lifted to the ring
/// when their turn comes, so the shard is not stood still for half an hour with thirty bots in a field; a loser walks
/// home from the ring like anybody sent anywhere. Bots in a company are not called — a company owns where its members
/// stand — and neither is anybody with a class paid nothing, which is the Baron. The ring is chosen among the towns each
/// championship (<see cref="BotVenues"/>), weighted by where the entrants are.
/// </para>
///
/// <para>
/// <b>Patrick, 07.10.2026: "Still NOT ONCE have I seen a championship where a champion is chosen in battle", and then: "a minimum of 50
/// participants, 25 sparrings, and only once A DAY".</b> By default the championship is now held at the round arenas east of Papua
/// (<see cref="Arena"/>, the other half of this class in <c>BotTourneyArena.cs</c>): once a real day, at least
/// <see cref="MinEntrants"/> free bots, ported there in batches, a batch's pairs fought at once one pair to a ring, the referee shown at
/// the main ring. What follows in this file is the hourly town-ring knockout, kept behind the dial, and the parts both share: the
/// title, the prize, the record.
/// </para>
/// </summary>
public static partial class BotTourney
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotTourney));

    public static bool Running { get; set; } = true;

    public static long EveryMs { get; set; } = 3600000L;

    public static long LeastGapMs { get; set; } = 3600000L;

    public static int BootGraceMs { get; set; } = 300000;

    public static int TitleWaitMs { get; set; } = 900000;

    public static bool Ready => Running && !Arena && !ArenaOn && _stage == Stage.Idle && (DateTime.Now - _held).TotalMilliseconds >= LeastGapMs;

    public static long TooSoon { get; private set; }

    public static int Entrants { get; set; } = 30;

    public static Point3D Ring { get; set; } = new(1458, 1500, 0);

    public static int Apart { get; set; } = 3;

    public static int BetweenMs { get; set; } = 8000;

    public static int SlackMs { get; set; } = 30000;

    public static long Held { get; private set; }

    public static int RepeatPurse { get; set; } = 1000;

    public static long AtFair { get; private set; }

    public static long Repeats { get; private set; }

    public static long Duplicates { get; private set; }

    private static bool _swept;

    public static long Called { get; private set; }

    public static long Fights { get; private set; }

    public static long Byes { get; private set; }

    public static long Unset { get; private set; }

    public static long CalledOff { get; private set; }

    public static long TitleBouts { get; private set; }

    public static long Defences { get; private set; }

    public static long Changes { get; private set; }

    public static long VacantCrowns { get; private set; }

    public static long VacantByDefault { get; private set; }

    public static long Stripped { get; private set; }

    public static long Walkovers { get; private set; }

    public static long Unfought { get; private set; }

    public static long Deferred { get; private set; }

    public static long DoubleForfeits { get; private set; }

    private static readonly Dictionary<string, int> _away = [];

    public static string Champion { get; private set; }

    private static DateTime _reignSince;

    private static int _reignDefences;

    private enum Stage
    {
        Idle,
        Calling,
        Fighting,
        Pausing,
        Title
    }

    private static Stage _stage = Stage.Idle;

    private static readonly List<BotMobile> _round = [];

    private static readonly List<BotMobile> _next = [];

    private static readonly HashSet<Serial> _deferred = [];

    private static BotMobile _a;

    private static BotMobile _b;

    private static bool _title;

    private static bool _vacantFought;

    private static BotMobile _challenger;

    private static long _titleTick;

    private static bool _waitSaid;

    private static bool _ringUnset;

    private static long _stageTick;

    private static int _roundNo;

    private static int _entered;

    private static DateTime _held = DateTime.MinValue;

    private static long _hueTick;

    private static string _by;

    private static long _bootTick;

    private static long _turnedTick = long.MinValue / 2;

    private static bool _restored;

    public static BotVenues.Venue Venue { get; private set; }

    private static Point3D RingNow => Venue?.At ?? Ring;

    private static Map RingMap => Venue?.Map ?? BotPopulation.Home ?? Map.Felucca;

    private static string RingName => Venue?.Name ?? "the drill ground";

    public static bool InProgress => _stage != Stage.Idle;

    public static void Load()
    {
        _bootTick = Core.TickCount;
        Clear();

        ArenaLoad();

        if (!_restored)
        {
            _held = DateTime.Now;
            Champion = null;
            _reignDefences = 0;

            logger.Information("The championship has no record in the save; the hour starts now and the title is vacant");

            return;
        }

        logger.Information(
            "The championship was last held {Held}; the champion is {Champion}{Reign}",
            _held,
            Champion ?? "nobody",
            Champion == null ? "" : $", holding it since {_reignSince:yyyy-MM-dd HH:mm} with {_reignDefences} defences"
        );
    }

    public static void Save(IGenericWriter writer)
    {
        writer.WriteEncodedInt(2);
        writer.Write(_held.ToUniversalTime().Ticks);
        writer.Write(Champion ?? "");
        writer.Write(_reignSince.ToUniversalTime().Ticks);
        writer.WriteEncodedInt(_reignDefences);
        BotVenues.Save(writer);
        writer.Write(_dayHeld.Ticks);
    }

    public static bool Load(IGenericReader reader)
    {
        var shape = reader.ReadEncodedInt();

        if (shape is not (1 or 2))
        {
            return false;
        }

        _held = new DateTime(reader.ReadLong(), DateTimeKind.Utc).ToLocalTime();

        var champion = reader.ReadString();

        Champion = string.IsNullOrWhiteSpace(champion) ? null : champion;
        _reignSince = new DateTime(reader.ReadLong(), DateTimeKind.Utc).ToLocalTime();
        _reignDefences = reader.ReadEncodedInt();
        BotVenues.Load(reader);

        _dayHeld = shape >= 2 ? new DateTime(reader.ReadLong(), DateTimeKind.Utc) : DateTime.MinValue;
        _restored = true;

        return true;
    }

    public static bool Due(DateTime now) =>
        Running && (now - _held).TotalMilliseconds >= EveryMs && Core.TickCount - _bootTick >= BootGraceMs;

    public static string Start(string by)
    {
        if (!Running)
        {
            return "championships are switched off (BotTourney.Running).";
        }

        if (Arena || ArenaOn)
        {
            return ArenaStart(by, false);
        }

        if (_stage != Stage.Idle)
        {
            return _stage == Stage.Title
                ? $"a championship is waiting for its title bout: {Champion} against {_challenger?.Name}."
                : $"a championship is already running: round {_roundNo}, {_round.Count + _next.Count} still in it.";
        }

        if (by != "the keyboard" && (DateTime.Now - _held).TotalMilliseconds < LeastGapMs)
        {
            TooSoon++;

            return $"too soon: the last championship ended {(DateTime.Now - _held).TotalMinutes:F0} minutes ago, and they are held once an hour.";
        }

        var fighters = Fighters();

        var least = Champion != null ? 1 : 2;

        if (fighters.Count < least)
        {
            return $"only {fighters.Count} fit fighters could be called; a championship {(Champion != null ? $"for {Champion}'s title" : "for the vacant title")} wants {least} at least.";
        }

        List<Mobile> who = [..fighters];

        if (Find(Champion) is { } holder)
        {
            who.Add(holder);
        }

        Venue = (BotFair.TownName is { } fair ? BotVenues.InTown(BotVenues.Use.Ring, BotPopulation.Home ?? Map.Felucca, fair) : null)
                ?? BotVenues.Choose(BotVenues.Use.Ring, BotPopulation.Home ?? Map.Felucca, who);

        if (BotFair.TownName != null && Venue?.Town == BotFair.TownName)
        {
            AtFair++;
        }

        _round.Clear();
        _next.Clear();
        _deferred.Clear();
        _round.AddRange(fighters);
        _roundNo = 1;
        Called++;
        _entered = fighters.Count;
        _by = by;
        _title = false;
        _vacantFought = false;
        _challenger = null;
        _stage = Stage.Calling;
        _stageTick = Core.TickCount;

        logger.Information(
            "Tourney: a championship is called by {By} at {Venue} ({X},{Y}) for {Title} with {Count} entrants, the strongest first: {Names}",
            by,
            RingName,
            RingNow.X,
            RingNow.Y,
            Champion != null ? $"the right to challenge {Champion}" : "the vacant title",
            fighters.Count,
            string.Join(", ", fighters.ConvertAll(f => $"{f.Name} ({BotThreat.Power(f):F0})"))
        );

        BotVenues.Herald(Venue, BotVenues.HeraldHoldMs);
        BotVenues.Announce(
            Champion != null
                ? $"The championship is called {Where()}: {fighters.Count} from the ranks fight for the right to challenge {Champion}, the champion."
                : $"The championship is called {Where()}: {fighters.Count} from the ranks fight for the vacant title."
        );

        return $"the championship is on {Where()}: {fighters.Count} entrants, {fighters[0].Name} the strongest at {BotThreat.Power(fighters[0]):F0}{(Champion != null ? $", for the right to challenge {Champion}" : ", for the vacant title")}. Watch the log for \"Tourney:\".";
    }

    private static string Where() => $"at {RingName}";

    public static string Stop()
    {
        if (ArenaOn)
        {
            return ArenaStop("called off by hand");
        }

        if (_stage == Stage.Idle)
        {
            return "no championship is running.";
        }

        Release();
        _round.Clear();
        _next.Clear();
        _challenger = null;
        _title = false;
        _stage = Stage.Idle;
        BotVenues.Release();

        logger.Information("Tourney: the championship was called off");

        return "the championship is called off.";
    }

    private static List<BotMobile> Fighters()
    {
        var bots = BotPopulation.Bots;
        List<BotMobile> fit = [];

        for (var i = 0; i < bots.Count; i++)
        {
            var bot = bots[i];

            if (bot is not { Deleted: false, Alive: true, Fallen: false } || bot.Class is not { } klass)
            {
                continue;
            }

            if (klass.Role == BotRole.Producer || klass.Unpaid || bot.Squad != null || bot.Murderer || BotDuel.Duelling(bot))
            {
                continue;
            }

            if (bot.Map == null || bot.Map == Map.Internal)
            {
                continue;
            }

            if (Champion != null && string.Equals(bot.Name, Champion, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            fit.Add(bot);
        }

        fit.Sort((x, y) => BotThreat.Power(y).CompareTo(BotThreat.Power(x)));

        if (fit.Count > Entrants)
        {
            fit.RemoveRange(Entrants, fit.Count - Entrants);
        }

        return fit;
    }

    public static void Beat(long now)
    {
        if (!Running)
        {
            return;
        }

        if (Champion != null && now - _hueTick >= 60000)
        {
            _hueTick = now;
            Crowned(Find(Champion));
            Regalia();
        }

        StraySweep(now);

        if (Arena || ArenaOn)
        {
            ArenaBeat(now);

            return;
        }

        switch (_stage)
        {
            case Stage.Idle:
                if (now - _turnedTick >= 60000 && Due(DateTime.Now))
                {
                    var before = Called;

                    Start("the calendar");

                    if (Called == before)
                    {
                        _turnedTick = now;
                    }
                }

                return;

            case Stage.Calling:
                Call(now);

                return;

            case Stage.Fighting:
                Judge(now);

                return;

            case Stage.Pausing:
                if (now - _stageTick >= BetweenMs)
                {
                    _stage = Stage.Calling;
                    _stageTick = now;
                }

                return;

            case Stage.Title:
                Title(now);

                return;
        }
    }

    private static bool Busy(BotMobile bot) =>
        bot != null && (bot.Squad != null || BotDelveParty.Delving(bot) || bot.Resolve?.Deed is BotDelve or BotEnlist);

    public static long Forfeits { get; private set; }

    private static string Away(BotMobile bot, string name)
    {
        if (bot == null)
        {
            var away = BotPopulation.Away;

            for (var i = 0; i < away.Count; i++)
            {
                if (away[i] is { Deleted: false } resting && string.Equals(resting.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    return "resting";
                }
            }

            return "gone";
        }

        if (bot.Deleted)
        {
            return "gone";
        }

        if (!bot.Alive || bot.Fallen)
        {
            return "dead";
        }

        if (bot.Map == null || bot.Map == Map.Internal)
        {
            return "resting";
        }

        if (bot.Squad != null || bot.Resolve?.Deed is BotEnlist)
        {
            return "with a company";
        }

        if (BotDelveParty.Delving(bot) || bot.Resolve?.Deed is BotDelve)
        {
            return "down a dungeon";
        }

        return BotDuel.InBout(bot) ? "in another duel" : null;
    }

    private static void Call(long now)
    {
        _round.RemoveAll(m => m is not { Deleted: false, Alive: true });
        _next.RemoveAll(m => m is not { Deleted: false, Alive: true });

        if (_round.Count == 0)
        {
            if (_next.Count == 0)
            {
                logger.Information("Tourney: nobody is left standing at {Venue}; no challenger", RingName);
                Finish(
                    Champion != null ? $"nobody from the ranks was left standing; {Champion} keeps the title." : "nobody was left standing; the title stays vacant.",
                    true
                );

                return;
            }

            if (_next.Count == 1)
            {
                Contender(_next[0], now);

                return;
            }

            _round.AddRange(_next);
            _next.Clear();
            _roundNo++;

            logger.Information("Tourney: round {Round} with {Count} left", _roundNo, _round.Count);
        }

        if (_round.Count == 1 && _next.Count == 0)
        {
            var last = _round[0];

            _round.Clear();
            Contender(last, now);

            return;
        }

        if (_round.Count == 1)
        {
            Byes++;
            _next.Add(_round[0]);
            _round.RemoveAt(0);

            logger.Information("Tourney: {Name} has a bye", _next[^1].Name);

            return;
        }

        var a = _round[0];
        var b = _round[1];

        var busyA = Busy(a);
        var busyB = Busy(b);

        if (busyA && _deferred.Add(a.Serial))
        {
            Deferred++;
            _round.RemoveAt(0);
            _round.Add(a);

            logger.Information("Tourney: {Name} is with a company; asked again at the end of round {Round}", a.Name, _roundNo);

            return;
        }

        if (busyB && _deferred.Add(b.Serial))
        {
            Deferred++;
            _round.RemoveAt(1);
            _round.Add(b);

            logger.Information("Tourney: {Name} is with a company; asked again at the end of round {Round}", b.Name, _roundNo);

            return;
        }

        if (busyA || busyB)
        {
            Unset++;
            Forfeits++;
            _round.RemoveRange(0, 2);

            if (busyA && busyB)
            {
                DoubleForfeits++;

                logger.Information("Tourney: {A} and {B} are both still with a company; both forfeit and nobody goes on", a.Name, b.Name);

                return;
            }

            var goes = busyA ? b : a;

            logger.Information("Tourney: {Busy} is still with a company and forfeits; {Goes} goes on", busyA ? a.Name : b.Name, goes.Name);

            _next.Add(goes);

            return;
        }

        var final = Champion == null && _round.Count == 2 && _next.Count == 0;

        _round.RemoveRange(0, 2);

        if (!Bring(a, b))
        {
            Unset++;
            logger.Warning("Tourney: {A} or {B} could not be put in the ring at {Venue}; the first goes on by default", a.Name, b.Name, RingName);
            _next.Add(a);

            return;
        }

        _a = a;
        _b = b;
        _title = final;
        Fights++;
        _stage = Stage.Fighting;
        _stageTick = now;

        if (final)
        {
            TitleBouts++;

            BotVenues.Herald(Venue, BotDuel.CapMs + SlackMs);
            BotVenues.Announce($"Title bout {Where()}, for the vacant crown: {Billing(a)} against {Billing(b)}!");
        }

        logger.Information(
            "Tourney: round {Round}{Title}, {A} ({PowerA:F0}) against {B} ({PowerB:F0}) in the ring at {Venue} ({X},{Y})",
            _roundNo,
            final ? " — the title bout for the vacant crown" : "",
            a.Name,
            BotThreat.Power(a),
            b.Name,
            BotThreat.Power(b),
            RingName,
            RingNow.X,
            RingNow.Y
        );
    }

    private static bool Bring(BotMobile a, BotMobile b) => Bring(a, b, RingMap, RingNow);

    private static bool Bring(BotMobile a, BotMobile b, Map map, Point3D ring)
    {

        if (!Spot(map, ring, ring.X - Apart, ring.Y, out var at) || !Spot(map, ring, ring.X + Apart, ring.Y, out var bt))
        {
            return false;
        }

        if (ArenaOn)
        {
            Hold(a);
            Hold(b);
        }

        if (!Put(a, map, at) || !Put(b, map, bt))
        {
            return false;
        }

        Heal(a);
        Heal(b);

        if (!BotDuel.Begin(a, b, Trains(a), Trains(b), ArenaOn))
        {
            logger.Warning("Tourney: {A} and {B} could not be set against each other", a.Name, b.Name);

            return false;
        }

        return true;
    }

    private static string Billing(BotMobile bot) => bot?.Class?.Name is { } klass ? $"{bot.Name} the {klass}" : bot?.Name ?? "nobody";

    private static void Contender(BotMobile winner, long now)
    {
        if (Champion == null)
        {
            VacantCrowns++;

            if (!_vacantFought)
            {
                VacantByDefault++;
            }

            Crown(
                winner,
                _vacantFought ? "won the vacant title in the ring" : "took the vacant title with nobody left to fight",
                _vacantFought
                    ? $"{Billing(winner)} wins the vacant title {Where()} and is the champion of the island!"
                    : $"{Billing(winner)} is the last of the ranks standing {Where()} and takes the vacant title."
            );

            return;
        }

        _challenger = winner;
        _stage = Stage.Title;
        _titleTick = now;
        _waitSaid = false;
        _ringUnset = false;

        logger.Information(
            "Tourney: {Name} came through {Entered} from the ranks over {Rounds} rounds and challenges {Champion} for the title",
            winner.Name,
            _entered,
            _roundNo,
            Champion
        );

        BotVenues.Announce($"{Billing(winner)} has come through {_entered} from the ranks and challenges {Champion} for the title {Where()}.");
    }

    private static void Title(long now)
    {
        var holder = Find(Champion);
        var away = Away(holder, Champion);
        var challenger = _challenger;
        var late = challenger is { Deleted: false } ? Away(challenger, challenger.Name) : "gone";

        if (away == null && late == null)
        {
            if (Bring(holder, challenger))
            {
                _ringUnset = false;
                _a = holder;
                _b = challenger;
                _title = true;
                Fights++;
                TitleBouts++;
                _stage = Stage.Fighting;
                _stageTick = now;

                logger.Information(
                    "Tourney: the title bout at {Venue} ({X},{Y}): {Champion} ({PowerA:F0}), champion since {Since:HH:mm} with {Defences} defences, against {Challenger} ({PowerB:F0}){Waited}",
                    RingName,
                    RingNow.X,
                    RingNow.Y,
                    holder.Name,
                    BotThreat.Power(holder),
                    _reignSince,
                    _reignDefences,
                    challenger.Name,
                    BotThreat.Power(challenger),
                    now - _titleTick >= 5000 ? $", after waiting {(now - _titleTick) / 1000}s for them both to be free" : ""
                );

                BotVenues.Herald(Venue, BotDuel.CapMs + SlackMs);
                BotVenues.Announce(
                    $"Title bout {Where()}: {Billing(holder)}, champion{(_reignDefences > 0 ? $" with {_reignDefences} defences" : "")}, defends against {Billing(challenger)}!"
                );

                return;
            }

            if (!_ringUnset)
            {
                _ringUnset = true;
                Unset++;
                logger.Warning("Tourney: {Champion} and {Challenger} could not be put in the ring at {Venue}; tried again each beat", Champion, challenger.Name, RingName);
            }
        }

        if (now - _titleTick < TitleWaitMs && !(away == "gone" && late == null) && late != "gone")
        {
            if (!_waitSaid && (away != null || late != null))
            {
                _waitSaid = true;

                logger.Information(
                    "Tourney: the title bout waits up to {Minutes} minutes: {Who} is {Why}",
                    TitleWaitMs / 60000,
                    away != null ? Champion : challenger?.Name,
                    away ?? late
                );

                BotVenues.Announce(
                    away != null
                        ? $"The champion {Champion} is {away}; the title bout against {challenger?.Name} waits up to {TitleWaitMs / 60000} minutes."
                        : $"The challenger {challenger?.Name} is {late}; the title bout waits up to {TitleWaitMs / 60000} minutes."
                );
            }

            return;
        }

        if (away != null && late == null && (away == "gone" || now - _titleTick >= TitleWaitMs))
        {
            Stripped++;
            _away[away] = _away.TryGetValue(away, out var had) ? had + 1 : 1;

            var was = Champion;

            logger.Information("Tourney: {Champion} did not come to defend the title ({Why}); it passes to {Challenger}", was, away, challenger.Name);

            Crown(
                challenger,
                $"took the title from {was}, who did not come to defend it ({away})",
                $"{was} did not come to defend the title ({away}); {Billing(challenger)} is the champion of the island."
            );

            return;
        }

        if (away == null && late != null)
        {
            Walkovers++;

            logger.Information("Tourney: the challenger {Challenger} did not come ({Why}); {Champion} keeps the title", challenger?.Name, late, Champion);
            Finish($"the challenger {challenger?.Name} did not come ({late}); {Champion} keeps the title.", false);

            return;
        }

        if (away == null)
        {
            logger.Information("Tourney: no title bout: the ring at {Venue} could not take {Champion} and {Challenger}; the title stands", RingName, Champion, challenger?.Name);
            Finish($"no title bout this time: the ring {Where()} could not be set; {Champion} keeps the title.", true);

            return;
        }

        logger.Information("Tourney: no title bout: {Champion} is {Why} and {Challenger} is {Late}; the title stands", Champion, away, challenger?.Name, late);
        Finish($"no title bout this time: {Champion} is {away} and {challenger?.Name} is {late}; the title stands.", true);
    }

    private static void Finish(string said, bool unfought)
    {
        if (unfought)
        {
            Unfought++;
        }

        Held++;
        _held = DateTime.Now;
        _stage = Stage.Idle;
        _challenger = null;
        _title = false;

        BotVenues.Announce(char.ToUpperInvariant(said[0]) + said[1..]);
    }

    private static void Judge(long now)
    {
        if (_a == null || _b == null)
        {
            _stage = Stage.Calling;

            return;
        }

        var winner = BotDuel.Winner(_a);

        if (winner == null && now - _stageTick >= BotDuel.CapMs + SlackMs)
        {
            CalledOff++;
            BotDuel.Call(_a, "the referee called it");
            winner = BotDuel.Winner(_a);
        }

        if (winner == null)
        {
            if (!BotDuel.InBout(_a) && !BotDuel.InBout(_b))
            {
                winner = _a is { Deleted: false, Alive: true } ? _a : _b;
            }
            else
            {
                return;
            }
        }

        var loser = ReferenceEquals(winner, _a) ? _b : _a;
        var title = _title;
        var holder = _a;

        BotDuel.End(_a, _b);
        Heal(_a);
        Heal(_b);

        _a = null;
        _b = null;
        _title = false;

        if (title && _stage == Stage.Fighting && _challenger != null && Champion != null)
        {
            BotVenues.Release();

            if (ReferenceEquals(winner, holder))
            {
                Defences++;
                _reignDefences++;

                logger.Information(
                    "Tourney: {Champion} defends the title against {Challenger} at {Venue}: {Defences} defences since {Since:HH:mm}",
                    winner.Name,
                    loser?.Name,
                    RingName,
                    _reignDefences,
                    _reignSince
                );

                Finish($"{Billing(winner)} defends the title against {loser?.Name} — {_reignDefences} {(_reignDefences == 1 ? "defence" : "defences")} now.", false);
                winner.Say($"The title stays with me, {loser?.Name}.");

                return;
            }

            Changes++;

            var was = Champion;

            logger.Information("Tourney: {Challenger} takes the title from {Champion} at {Venue}", winner.Name, was, RingName);

            Crown(winner, $"took the title from {was} in the ring", $"{Billing(winner)} takes the title from {was} {Where()} and is the champion of the island!");

            return;
        }

        if (title)
        {
            _vacantFought = true;
            BotVenues.Release();
        }

        _next.Add(winner);

        logger.Information("Tourney: {Winner} goes on, {Loser} is out", winner.Name, loser?.Name);

        _stage = Stage.Pausing;
        _stageTick = now;
    }

    private static void Crown(BotMobile winner, string how, string line)
    {
        if (ArenaOn && _forced && !string.IsNullOrEmpty(Champion))
        {
            _stage = Stage.Idle;
            _challenger = null;
            _title = false;
            logger.Information("Tourney: {Name} wins the forced test ({How}); the title stays with {Champion}", winner?.Name, how, Champion);
            BotVenues.Announce($"{winner?.Name} wins the test bouts at the arenas; {Champion} is still the champion.");
            return;
        }

        Sweep();

        Held++;
        _held = DateTime.Now;
        Champion = winner?.Name;
        _reignSince = DateTime.Now;
        _reignDefences = 0;
        _stage = Stage.Idle;
        _challenger = null;
        _title = false;

        Uncrown(Champion);
        Crowned(winner);

        var test = ArenaOn && _forced;
        var prize = test ? "nothing: a forced test is not a championship" : Prize(winner);

        logger.Information(
            "Tourney: {Name} the {Class} is the champion of the shard: {How}, at {Venue}, from {Entered} entrants over {Rounds} rounds, called by {By}; the prize is {Prize}",
            winner?.Name,
            winner?.Class?.Name,
            how,
            RingName,
            _entered,
            _roundNo,
            _by,
            prize
        );

        BotVenues.Announce(test ? $"{line} (A test bout: no prize.)" : line);

        winner?.Say($"I am the champion of this island, and my name is {winner.Name}!");
    }

    public static void Forget()
    {
        Clear();
        AtFair = 0;
        Champion = null;
        _held = DateTime.Now;
        _reignDefences = 0;
        _swept = false;
        _away.Clear();
        Venue = null;
        Uncrown(null);
        ArenaForget();
        BotVenues.Forget();
    }

    private static void Clear()
    {
        _stage = Stage.Idle;
        _round.Clear();
        _next.Clear();
        _deferred.Clear();
        _a = null;
        _b = null;
        _challenger = null;
        _title = false;
    }

    private static void Uncrown(string except)
    {
        var bots = BotPopulation.Bots;

        for (var i = 0; i < bots.Count; i++)
        {
            var bot = bots[i];

            if (bot is { Deleted: false } && bot.NameHue == BotMobile.ChampionHue
                && !string.Equals(bot.Name, except, StringComparison.OrdinalIgnoreCase))
            {
                bot.NameHue = -1;
            }
        }
    }

    private static void Crowned(BotMobile bot)
    {
        if (bot is { Deleted: false } && bot.NameHue != BotMobile.ChampionHue)
        {
            bot.NameHue = BotMobile.ChampionHue;
            bot.InvalidateProperties();
        }
    }

    private static string Prize(BotMobile bot)
    {
        var pack = bot?.Backpack;

        if (bot == null || pack == null)
        {
            return "nothing, there was no pack to put it in";
        }

        var total = 0;

        for (var i = 0; i < Draws.Length; i++)
        {
            total += Draws[i].Weight;
        }

        var roll = Utility.Random(total);
        var drawn = Draws.Length - 1;

        for (var i = 0; i < Draws.Length; i++)
        {
            if (roll < Draws[i].Weight)
            {
                drawn = i;

                break;
            }

            roll -= Draws[i].Weight;
        }

        var draw = Draws[drawn];

        Drawn[drawn]++;

        string won;

        if (draw.Level > 0)
        {
            var top = draw.Level == TopLevel;
            var role = bot.Class?.Role ?? BotRole.Melee;

            won = role is BotRole.Caster or BotRole.Medic
                ? (top ? Robe(bot, pack) : null) ?? Leather(bot, pack, draw.Level) ?? (top ? Steed(bot, pack) : null)
                : Weapon(bot, pack, draw.Level) ?? Armour(bot, pack, draw.Level) ?? (top ? Steed(bot, pack) : null);

            if (won == null)
            {
                Repeats++;
                Pay(bot, pack, RepeatPurse);

                won = $"{RepeatPurse}gp, holding every prize of that rung its kind may choose";
            }
        }
        else if (draw.Gold > 0)
        {
            Pay(bot, pack, draw.Gold);

            won = $"{draw.Gold}gp";
        }
        else
        {
            won = Supplies(bot, pack);
        }

        return $"{won} (the draw fell on {draw.Name}, {draw.Weight} in {total})";
    }

    private const int TopLevel = 5;

    private static readonly (int Weight, int Level, int Gold, string Name)[] Draws =
    [
        (1, 5, 0, "the top: vanquishing or invulnerability"),
        (2, 4, 0, "power or fortification"),
        (3, 3, 0, "force or hardening"),
        (5, 2, 0, "might or guarding"),
        (8, 1, 0, "ruin or defense"),
        (13, 0, 0, "a pack of supplies"),
        (21, 0, 1000, "a purse of 1000"),
        (34, 0, 500, "a purse of 500"),
        (55, 0, 250, "a purse of 250")
    ];

    private static readonly long[] Drawn = new long[9];

    private static readonly string[] WeaponWords = ["", "ruin", "might", "force", "power", "vanquishing"];

    private static readonly string[] ArmourWords = ["", "defense", "guarding", "hardening", "fortification", "invulnerability"];

    public static int SupplyBandages { get; set; } = 40;

    public static int SupplyHeals { get; set; } = 3;

    public static int SupplyCures { get; set; } = 2;

    public static int SupplyReagents { get; set; } = 20;

    public static int SupplyAmmo { get; set; } = 150;

    private static string Supplies(BotMobile bot, Container pack)
    {
        var said = $"a pack of supplies: {SupplyBandages} bandages, {SupplyHeals} heal and {SupplyCures} cure potions";

        pack.TryDropItem(bot, new Bandage(SupplyBandages), false);
        pack.TryDropItem(bot, new HealPotion { Amount = SupplyHeals }, false);
        pack.TryDropItem(bot, new CurePotion { Amount = SupplyCures }, false);

        if (bot.Class?.Role is BotRole.Caster or BotRole.Medic || bot.Skills.Magery.Base >= 30.0)
        {
            Item[] herbs =
            [
                new BlackPearl(SupplyReagents), new Bloodmoss(SupplyReagents), new Garlic(SupplyReagents), new Ginseng(SupplyReagents),
                new MandrakeRoot(SupplyReagents), new Nightshade(SupplyReagents), new SpidersSilk(SupplyReagents), new SulfurousAsh(SupplyReagents)
            ];

            for (var i = 0; i < herbs.Length; i++)
            {
                pack.TryDropItem(bot, herbs[i], false);
            }

            said += $", {SupplyReagents} of each reagent";
        }

        if (bot.Bond?.Weapon is { Weapon: { } kind } && typeof(BaseRanged).IsAssignableFrom(kind)
            && kind.CreateInstance<Item>() is BaseRanged bow)
        {
            var ammo = bow.AmmoType;

            bow.Delete();

            if (ammo != null && ammo.CreateInstance<Item>() is { } shot)
            {
                shot.Amount = SupplyAmmo;
                pack.TryDropItem(bot, shot, false);

                said += $", {SupplyAmmo} {ammo.Name.ToLowerInvariant()}s";
            }
        }

        return said;
    }

    public static int RobeInt { get; set; } = 15;

    public const int RobeHue = 0x0555;

    public const int SteedHue = 0x0501;

    private const string RobeMod = "champion's robe";

    private static string Weapon(BotMobile bot, Container pack, int level)
    {
        if (bot.Class?.Role is not (BotRole.Melee or BotRole.Ranged) || bot.Bond?.Weapon is not { Weapon: { } kind })
        {
            return null;
        }

        var owned = Owned(bot, kind);

        for (var i = 0; i < owned.Count; i++)
        {
            if (owned[i] is BaseWeapon held && (int)held.DamageLevel >= level)
            {
                return null;
            }
        }

        if (kind.CreateInstance<Item>() is not BaseWeapon copy)
        {
            return null;
        }

        copy.DamageLevel = (WeaponDamageLevel)level;
        copy.DurabilityLevel = (WeaponDurabilityLevel)level;

        var said = Give(bot, pack, copy, $"{kind.Name} of {WeaponWords[level]}, bound");

        if (said != null)
        {
            Replace(bot, owned);
        }

        return said;
    }

    private static void Replace(BotMobile bot, List<Item> lesser)
    {
        for (var i = 0; i < lesser.Count; i++)
        {
            if (lesser[i] is { Deleted: false } old)
            {
                bot.Bond?.Items.Remove(old.Serial);
                old.Delete();
            }
        }
    }

    private static string Armour(BotMobile bot, Container pack, int level)
    {
        Layer[] order = [Layer.InnerTorso, Layer.Pants, Layer.Arms, Layer.Gloves, Layer.Neck, Layer.Helm];

        for (var i = 0; i < order.Length; i++)
        {
            if (bot.FindItemOnLayer(order[i]) is not BaseArmor worn || (int)worn.ProtectionLevel >= level)
            {
                continue;
            }

            var given = Piece(bot, pack, worn.GetType(), level);

            if (given != null)
            {
                return given;
            }
        }

        return null;
    }

    private static string Piece(BotMobile bot, Container pack, Type type, int level)
    {
        var owned = Owned(bot, type);

        for (var i = 0; i < owned.Count; i++)
        {
            if (owned[i] is BaseArmor held && (int)held.ProtectionLevel >= level)
            {
                return null;
            }
        }

        if (type.CreateInstance<Item>() is not BaseArmor copy)
        {
            return null;
        }

        copy.ProtectionLevel = (ArmorProtectionLevel)level;
        copy.Durability = (ArmorDurabilityLevel)level;

        var said = Give(bot, pack, copy, $"{type.Name} of {ArmourWords[level]}, bound");

        if (said != null)
        {
            Replace(bot, owned);
        }

        return said;
    }

    private static string Leather(BotMobile bot, Container pack, int level)
    {
        Type[] set = [typeof(LeatherChest), typeof(LeatherLegs), typeof(LeatherArms), typeof(LeatherGloves), typeof(LeatherGorget), typeof(LeatherCap)];

        for (var i = 0; i < set.Length; i++)
        {
            var given = Piece(bot, pack, set[i], level);

            if (given != null)
            {
                return given;
            }
        }

        return null;
    }

    private static string Robe(BotMobile bot, Container pack)
    {
        if (RobeOf(bot) != null)
        {
            return null;
        }

        var robe = new Robe(RobeHue) { Name = "a robe of the arcane mind" };
        var said = Give(bot, pack, robe, $"a robe of the arcane mind (+{RobeInt} Intelligence while worn), bound");

        if (said != null && !robe.Deleted)
        {
            if (bot.FindItemOnLayer(Layer.OuterTorso) is { } over)
            {
                pack.DropItem(over);
            }

            bot.EquipItem(robe);
            Regalia(bot);
        }

        return said;
    }

    private static Robe RobeOf(BotMobile bot)
    {
        if (bot?.Bond == null)
        {
            return null;
        }

        if (bot.FindItemOnLayer(Layer.OuterTorso) is Robe { Hue: RobeHue } worn && BotBinding.IsBound(worn, bot.Bond))
        {
            return worn;
        }

        if (bot.Backpack is not { } pack)
        {
            return null;
        }

        foreach (var item in pack.FindItemsByType(typeof(Robe), true))
        {
            if (item is Robe { Hue: RobeHue } robe && BotBinding.IsBound(robe, bot.Bond))
            {
                return robe;
            }
        }

        return null;
    }

    private static string Steed(BotMobile bot, Container pack)
    {
        if (bot.Mount is BotChampionSteed || pack.FindItemByType<BotChampionSteed>() != null)
        {
            return null;
        }

        var (regular, mounted, kind) = bot.Class?.Role switch
        {
            BotRole.Caster => (0x25A0, 0x3E9C, "kirin"),
            BotRole.Medic  => (0x25CE, 0x3E9B, "unicorn"),
            BotRole.Melee  => (0x2619, 0x3E98, "swamp dragon"),
            BotRole.Ranged => (0x2135, 0x3EAC, "ostard"),
            _              => (0x2615, 0x3E9A, "ridgeback")
        };

        var steed = new BotChampionSteed
        {
            RegularID = regular,
            MountedID = mounted,
            Hue = SteedHue,
            Name = $"{bot.Name}'s {kind}"
        };

        if (!pack.TryDropItem(bot, steed, false))
        {
            steed.Delete();

            return null;
        }

        if (pack.FindItemByType<BotSteed>() is { } bought && bought is not BotChampionSteed)
        {
            bought.Delete();
            Banker.Deposit(bot, BotSteed.Price);
        }

        return $"a golden {kind} of its own";
    }

    private static string Give(BotMobile bot, Container pack, Item prize, string said)
    {
        if (!pack.TryDropItem(bot, prize, false))
        {
            prize.Delete();

            return null;
        }

        BotBinding.Bind(prize, bot.Bond);

        return said;
    }

    private static void Regalia(BotMobile bot)
    {
        if (bot is not { Deleted: false })
        {
            return;
        }

        var wearing = bot.FindItemOnLayer(Layer.OuterTorso) is Robe { Hue: RobeHue } robe && BotBinding.IsBound(robe, bot.Bond);
        var lent = bot.GetStatMod(RobeMod);

        if (wearing && lent == null)
        {
            bot.AddStatMod(new StatMod(StatType.Int, RobeMod, RobeInt, TimeSpan.Zero));
        }
        else if (!wearing && lent != null)
        {
            bot.RemoveStatMod(RobeMod);
        }
    }

    private static void Regalia()
    {
        var bots = BotPopulation.Bots;

        for (var i = 0; i < bots.Count; i++)
        {
            if (bots[i] is { Deleted: false } bot && (bot.GetStatMod(RobeMod) != null || RobeOf(bot) != null))
            {
                Regalia(bot);
            }
        }
    }

    private static bool IsPrize(Item item) =>
        item is BaseWeapon { DamageLevel: not WeaponDamageLevel.Regular } or BaseArmor { ProtectionLevel: not ArmorProtectionLevel.Regular };

    private static List<Item> Owned(BotMobile bot, Type type)
    {
        List<Item> owned = [];

        if (bot?.Bond == null || type == null)
        {
            return owned;
        }

        for (var i = 0; i < bot.Items.Count; i++)
        {
            var item = bot.Items[i];

            if (item.GetType() == type && IsPrize(item) && BotBinding.IsBound(item, bot.Bond))
            {
                owned.Add(item);
            }
        }

        if (bot.Backpack is { } pack)
        {
            foreach (var item in pack.FindItemsByType(type, true))
            {
                if (IsPrize(item) && BotBinding.IsBound(item, bot.Bond) && !owned.Contains(item))
                {
                    owned.Add(item);
                }
            }
        }

        return owned;
    }

    private static void Pay(BotMobile bot, Container pack, int amount)
    {
        if (!Banker.Deposit(bot, amount))
        {
            pack?.TryDropItem(bot, new Gold(amount), false);
        }
    }

    private static void Sweep()
    {
        if (_swept)
        {
            return;
        }

        _swept = true;

        var exchanged = Sweep(BotPopulation.Bots) + Sweep(BotPopulation.Away);

        if (exchanged > 0)
        {
            logger.Information("Tourney: {Count} duplicate prizes the population held were exchanged for prizes of their owners' choosing", exchanged);
        }
    }

    private static int Sweep(IReadOnlyList<BotMobile> bots)
    {
        var exchanged = 0;

        for (var i = 0; i < bots.Count; i++)
        {
            if (bots[i] is not { Deleted: false, Bond: { } bond } bot)
            {
                continue;
            }

            List<Type> seen = [];

            foreach (var serial in new List<Serial>(bond.Items))
            {
                if (World.FindItem(serial) is not { Deleted: false } item || !IsPrize(item) || seen.Contains(item.GetType()))
                {
                    continue;
                }

                seen.Add(item.GetType());

                var owned = Owned(bot, item.GetType());

                for (var k = 1; k < owned.Count; k++)
                {
                    bond.Items.Remove(owned[k].Serial);
                    owned[k].Delete();
                    Duplicates++;
                    exchanged++;

                    logger.Information("Tourney: {Name} exchanged a duplicate {Kind} for {Prize}", bot.Name, item.GetType().Name, Prize(bot));
                }
            }
        }

        return exchanged;
    }

    public static bool Spot(Map map, Point3D ring, int x, int y, out Point3D at)
    {
        at = Point3D.Zero;

        if (map == null || map == Map.Internal)
        {
            return false;
        }

        for (var attempt = 0; attempt < 32; attempt++)
        {
            var spread = attempt < 16 ? 3 : 7;
            var sx = x + Utility.RandomMinMax(-spread, spread);
            var sy = y + Utility.RandomMinMax(-spread, spread);

            if (!map.CanSpawnMobile(sx, sy, ring.Z - 12, ring.Z + 12, false, false, out var z))
            {
                continue;
            }

            if (!map.LineOfSight(new Point3D(sx, sy, z + 10), new Point3D(ring.X, ring.Y, ring.Z + 10)))
            {
                continue;
            }

            at = new Point3D(sx, sy, z);

            return true;
        }

        return false;
    }

    private static bool Put(BotMobile bot, Map map, Point3D at)
    {
        if (bot is not { Deleted: false, Alive: true } || map == null)
        {
            return false;
        }

        if (ArenaOn)
        {
            Flash(bot.Map, bot.Location);
        }

        bot.MoveToWorld(at, map);
        bot.Journey?.Finish();
        bot.Combatant = null;
        bot.Warmode = false;

        if (ArenaOn)
        {
            Flash(map, at);
        }

        return true;
    }

    private static void Heal(BotMobile bot)
    {
        if (bot is not { Deleted: false, Alive: true })
        {
            return;
        }

        if (bot.Poisoned)
        {
            bot.CurePoison(bot);
        }

        bot.Hits = bot.HitsMax;
        bot.Stam = bot.StamMax;
        bot.Mana = bot.ManaMax;
    }

    private static void Release()
    {
        if (_a != null && _b != null)
        {
            BotDuel.Call(_a, "the championship was called off");
            BotDuel.End(_a, _b);
        }

        Heal(_a);
        Heal(_b);
        _a = null;
        _b = null;
    }

    private static SkillName Trains(BotMobile bot) => bot?.Bond?.Weapon?.Skill ?? SkillName.Wrestling;

    private static BotMobile Find(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var bots = BotPopulation.Bots;

        for (var i = 0; i < bots.Count; i++)
        {
            if (bots[i] is { Deleted: false } bot && string.Equals(bot.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return bot;
            }
        }

        return null;
    }

    public static string Describe()
    {
        var stage = _stage switch
        {
            Stage.Idle     => "no championship running",
            Stage.Calling  => $"calling the next pair of round {_roundNo} at {RingName}",
            Stage.Fighting => $"round {_roundNo} at {RingName}: {_a?.Name} against {_b?.Name} in the ring{(_title ? ", for the title" : "")}",
            Stage.Title    => $"{_challenger?.Name} waits to challenge {Champion} at {RingName}",
            _              => "between fights"
        };

        var away = _away.Count == 0 ? "" : $" (the champion was {string.Join(", ", Reasons())})";
        var title = Champion == null
            ? "the title is vacant"
            : $"{Champion} holds the title since {_reignSince:dd.MM HH:mm} with {_reignDefences} defences";

        if (Arena || ArenaOn)
        {
            return $"the championship at the arenas: {ArenaState()}; {title}; title: {Called} championships called, {TitleBouts} title bouts fought — {Defences} defences, {Changes} changes of hands in the ring, {VacantCrowns} vacant titles filled, {Stripped} champions stripped for not coming{away}, {Walkovers} challengers who did not come; {ArenaTally()}; {BotDuel.Describe()}";
        }

        return $"the championship: {stage}; {title}; title: {Called} championships called, {TitleBouts} title bouts fought — {Defences} defences, {Changes} changes of hands in the ring, {VacantCrowns} vacant titles filled ({VacantByDefault} without a bout), {Stripped} champions stripped for not coming{away}, {Walkovers} challengers who did not come, {Unfought} ended with no title bout; {Held} held, {Fights} fights, {Byes} byes, {Unset} pairs passed over ({Forfeits} forfeited by a bot with a company, {DoubleForfeits} with both still busy, {Deferred} asked again later first), {CalledOff} called by the referee, {Repeats} champions paid in gold for holding every prize and {Duplicates} duplicate prizes exchanged; draws since the boot {string.Join("/", Drawn)} (top first), {TooSoon} calls refused inside the hour, {AtFair} held at a fair; {BotDuel.Describe()}; next by the calendar {(_held + TimeSpan.FromMilliseconds(EveryMs)):yyyy-MM-dd HH:mm}";
    }

    private static IEnumerable<string> Reasons()
    {
        foreach (var (why, n) in _away)
        {
            yield return $"{why} {n}";
        }
    }
}

/// <summary>
/// Keeps the championship's record in the world save: when the last one was held, who holds the title, since when and with how
/// many defences, and the last places the watchers held their events (<see cref="BotVenues"/>).
///
/// <para>
/// <b>A file of its own under <c>Saves/</c> is not kept.</b> The record was <c>Saves/BotTourney/tourney.txt</c>, written at each
/// crown; every world save moves the whole folder into the backups, so each boot found no file, the champion was nobody and the
/// hour began again — five boots between the wipe of 01.10.2026 and build 389, and Kael (crowned 19:21) and Cirdan (09:50) each lost a
/// title nobody had taken from them. A persistence is written into the save itself, as the other records of this shard are.
/// </para>
/// </summary>
public sealed class BotTourneyStore : GenericPersistence
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotTourneyStore));

    private static BotTourneyStore _store;

    public static void Configure() => _store ??= new BotTourneyStore();

    public BotTourneyStore() : base("BotTourney", 21)
    {
    }

    public override void Serialize(IGenericWriter writer) => BotTourney.Save(writer);

    public override void Deserialize(IGenericReader reader)
    {
        if (!BotTourney.Load(reader))
        {
            logger.Warning("The championship's saved record is of a shape this build does not read; the title starts vacant");

            return;
        }

        logger.Information("The championship's record read back from the save: the champion is {Champion}", BotTourney.Champion ?? "nobody");
    }
}
