using System;
using System.Collections.Generic;
using Server.Logging;
using Server.Mobiles;
using Server.Text;

namespace Server.BotAI.V2;

/// <summary>
/// The decision. Reads the ladder, holds what it has taken on, and runs an auction when it is free to want
/// something.
///
/// <para>
/// <b>It does not know what work exists.</b> It knows proposers — see <see cref="IBotProposer"/> — and they
/// belong to the subsystems that own the work. That is the structural answer to the first version's brain:
/// <c>ChooseGoal</c> was 1209 lines inside a file of 7985 which referenced 57 other modules, so every
/// change to any behaviour was a change to that file, and nobody could touch mining without touching
/// trade. Adding a kind of work here is a new folder and one line of registration; nothing in this file
/// changes.
/// </para>
///
/// <para>
/// <b>It does not re-decide every tick.</b> A decision is reviewed on a clock and switched only against a
/// margin, with what is already being done given a bonus for being underway. In the first version a bot in
/// state <em>Trade</em> was seen walking a graveyard: it was trading honestly, one tick at a time — two
/// steps towards town, a skeleton noticed ten tiles away, back to hunting, town noticed again. Any
/// intention longer than a second was impossible in principle, and the only reason it did not look broken
/// is that a bot walking in circles looks busy.
/// </para>
///
/// <para>
/// <b>Every decision is recorded in words and numbers.</b> The first version's brain took 85 of the 135
/// plans its own slow tier offered and nothing anywhere said so; the tier spent the night learning from
/// noise. A choice nobody can read is a choice nobody can correct.
/// </para>
/// </summary>
public static class BotWill
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotWill));

    public static bool Deciding { get; internal set; }

    public static int ReviewMs { get; set; } = 15000;

    public static int IdleMs { get; set; } = 2000;

    public static int DwellMs { get; set; } = 30000;

    public static int DwellCapMs { get; set; } = 120000;

    private static int Dwell(BotDeed deed) =>
        Math.Clamp((int)Math.Round((deed?.Minutes ?? 0.0) * 60000.0), DwellMs, DwellCapMs);

    public static int AsideCapMs { get; set; } = 600000;

    public static int TrekLimit { get; set; } = 600;

    public static long Trudges { get; private set; }

    public static int LabourMs { get; set; } = 900000;

    public static double SwitchMargin { get; set; } = 1.25;

    public static double CommitStretch { get; set; } = 1.5;

    public static int CommitCapMs { get; set; } = 480000;

    public static double TroubleShare { get; set; } = 0.33;

    public static bool Resume { get; set; } = true;

    public static double ResumeHealth { get; set; } = 0.5;

    public static int ReturnMs { get; set; } = 600000;

    public static long Jumped { get; private set; }

    public static long Summoned { get; private set; }

    public static long Outbid { get; private set; }

    public static long Held { get; private set; }

    public static long Troubled { get; private set; }

    public static long Paused { get; private set; }

    public static long Resumed { get; private set; }

    public static long Unresumed { get; private set; }

    public static long Returned { get; private set; }

    /// <summary>What happened to one kind of work, for the census and the debugger. Written on a decision, read every five minutes.</summary>
    private sealed class Tally
    {
        public long Taken;

        public long Finished;

        public long Failed;

        public long Dropped;

        public long Died;

        public long Jumped;

        public long Outbid;

        public long Held;

        public long Paused;

        public long Resumed;

        public long Returned;
    }

    private static readonly Dictionary<string, Tally> _tallies = new(StringComparer.OrdinalIgnoreCase);

    private static Tally TallyOf(string kind)
    {
        kind ??= "?";

        if (!_tallies.TryGetValue(kind, out var tally))
        {
            tally = new Tally();
            _tallies[kind] = tally;
        }

        return tally;
    }

    public static int CensusMs { get; set; } = 300000;

    public static bool Chatty { get; set; } = true;

    private static readonly List<IBotProposer> _proposers = [];

    private static readonly List<BotDeed> _offers = [];

    private static readonly Dictionary<string, int> _busy = new(StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<BotStanding> _mute = [];

    private static long _censusTick;

    private static bool _censused;

    public static long Taken { get; private set; }

    public static long Finished { get; private set; }

    public static long Failed { get; private set; }

    public static long Dropped { get; private set; }

    public static long Deaths { get; private set; }

    public static long Barren { get; private set; }

    public static long Unsworn { get; private set; }

    public static long Grounded { get; private set; }

    public static long Dislodged { get; private set; }

    public static long Kept { get; private set; }

    private static bool Sworn(IBotWilful bot, BotStanding rung, IBotProposer proposer)
    {
        if (rung != BotStanding.Free)
        {
            return true;
        }

        var only = (bot?.Self as BotMobile)?.Class?.Sworn;

        if (only is not { Length: > 0 })
        {
            return true;
        }

        for (var i = 0; i < only.Length; i++)
        {
            if (string.Equals(only[i], proposer.Name, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    public static void Offer(IBotProposer proposer)
    {
        if (proposer == null)
        {
            return;
        }

        for (var i = 0; i < _proposers.Count; i++)
        {
            if (!string.Equals(_proposers[i].Name, proposer.Name, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            logger.Error(
                "Two proposers are both called {Name} ({First} and {Second}); the second is ignored",
                proposer.Name,
                _proposers[i].GetType().Name,
                proposer.GetType().Name
            );

            return;
        }

        _proposers.Add(proposer);

        logger.Information(
            "Proposer {Name} offers work on the {Rung} rung",
            proposer.Name,
            proposer.Rung
        );
    }

    public static IReadOnlyList<IBotProposer> Proposers => _proposers;

    public static double SpentMs { get; private set; }

    public static long Decisions { get; private set; }

    public static void Spent(long ticks)
    {
        SpentMs += ticks * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
        Decisions++;
    }

    public static void Decide(IBotWilful bot)
    {
        if (!Deciding)
        {
            return;
        }

        var resolve = bot?.Resolve;
        var body = bot?.Self;

        if (resolve == null || body == null || body.Deleted)
        {
            return;
        }

        if ((body as BotMobile)?.Class is { Bidding: false })
        {
            return;
        }

        var now = Core.TickCount;

        Census(now);

        var minutes = resolve.Urges.Since(now);
        var standing = BotLadder.Standing(bot);

        resolve.Standing = standing;

        if (standing == BotStanding.Dead)
        {
            Settle(bot, BotEnding.Died);

            return;
        }

        if (standing < BotStanding.Busy)
        {
            Aside(bot, resolve, standing, now, minutes);

            return;
        }

        resolve.Aside = false;

        if (resolve.Deed != null)
        {
            resolve.Urges.Held(minutes);

            Advance(bot, resolve);
        }
        else
        {
            resolve.Urges.Idle(minutes);
        }

        if (resolve.Due || now - resolve.ReviewedTick >= (resolve.Deed == null ? IdleMs : ReviewMs))
        {
            Auction(bot, resolve, BotStanding.Free, now);
        }
    }

    public static long Unreachable { get; private set; }

    public static long Unpriced { get; private set; }

    public static long Unblamed { get; private set; }

    public static Action<Mobile, string> Completed { get; set; }

    public static void Note(IBotWilful bot, BotWalkResult result)
    {
        if (!Deciding || result is not (BotWalkResult.Refused or BotWalkResult.GaveUp or BotWalkResult.Stalled))
        {
            return;
        }

        var resolve = bot?.Resolve;
        var deed = resolve?.Deed;

        if (deed == null)
        {
            return;
        }

        var refused = resolve.Sent;

        resolve.Sent = default;

        var bent = false;

        try
        {
            bent = deed.Bend(bot);
        }
        catch (Exception e)
        {
            logger.Error(e, "Undertaking {Kind} threw when told its way was blocked; it is given up", deed.Kind);
        }

        if (bent)
        {
            resolve.Bent = true;

            if (Chatty)
            {
                logger.Information(
                    "{Name} could not get to {Where} and is trying elsewhere for {Deed}",
                    bot.Self?.Name,
                    refused.Follow != null
                        ? refused.Follow.Name
                        : refused.Where == Point3D.Zero
                            ? "whatever it was following, which is gone"
                            : refused.Where.ToString(),
                    deed
                );
            }

            return;
        }

        var could = result switch
        {
            BotWalkResult.GaveUp => "could not get nearer to",
            BotWalkResult.Stalled => "could not take a step towards",
            _ => "no way through to"
        };

        var where = refused.Follow != null
            ? $"{could} {refused.Follow.Name} at {refused.Follow.Location}"
              + (refused.Follow.Region?.Name is { Length: > 0 } region ? $" in {region}" : "")
              + (Server.Multis.BaseHouse.FindHouseAt(refused.Follow) != null ? " inside a house" : "")
            : refused.Where == Point3D.Zero
                ? $"{could} what it was following, which is gone"
                : $"{could} {refused.Where}";

        if (refused.Follow is BaseCreature)
        {
            BotQuarry.Shun(refused.Follow);
            Unreachable++;
        }
        else if (refused.Follow != null)
        {
            BotMend.Beyond(refused.Follow);
            Unreachable++;
        }

        var ground = refused.Follow == null && result != BotWalkResult.Stalled;

        if (!ground)
        {
            Unblamed++;
        }

        Settle(bot, BotEnding.Failed, where, unreached: true, ground);
    }

    public static void Hurt(IBotWilful bot)
    {
        var resolve = bot?.Resolve;

        if (resolve != null)
        {
            resolve.HurtTick = Core.TickCount;
            resolve.Struck = true;
        }
    }

    public static void Died(IBotWilful bot) => Settle(bot, BotEnding.Died);

    public static void Forget(IBotWilful bot)
    {
        var resolve = bot?.Resolve;
        var deed = resolve?.Deed;

        if (deed == null)
        {
            return;
        }

        Count(deed.Kind, -1);

        resolve.Deed = null;
        resolve.Sent = default;
        resolve.Paused = null;
    }

    private static void Aside(
        IBotWilful bot,
        BotResolve resolve,
        BotStanding standing,
        long now,
        double minutes
    )
    {
        resolve.Urges.Held(minutes);

        if (standing != BotStanding.Bound && Auction(bot, resolve, standing, now))
        {
            return;
        }

        if (resolve.Deed == null)
        {
            if (standing == BotStanding.Hunted)
            {
                Auction(bot, resolve, BotStanding.Free, now);
            }

            return;
        }

        if (standing == BotStanding.Hunted
            || resolve.Took <= standing
            || standing == BotStanding.Bound && resolve.Deed.Alongside)
        {
            resolve.Aside = false;

            Advance(bot, resolve);

            return;
        }

        if (!resolve.Aside)
        {
            resolve.Aside = true;
            resolve.AsideTick = now;
            resolve.StirredTick = now;

            return;
        }

        if (now - resolve.AsideTick < AsideCapMs)
        {
            resolve.StirredTick = now;

            return;
        }

        logger.Information(
            "{Name} gave up {Deed}: set aside {Minutes:F0} minutes while {Standing}",
            bot.Self?.Name,
            resolve.Deed,
            (now - resolve.AsideTick) / 60000.0,
            standing
        );

        Settle(bot, BotEnding.Dropped);
    }

    private static void Advance(IBotWilful bot, BotResolve resolve)
    {
        var deed = resolve.Deed;
        BotDoing doing;

        try
        {
            doing = deed.Advance(bot);
        }
        catch (Exception e)
        {
            logger.Error(e, "Undertaking {Kind} threw while being advanced; it is given up", deed.Kind);

            Settle(bot, BotEnding.Failed);

            return;
        }

        switch (doing.Kind)
        {
            case BotDoingKind.Walk:
                if (doing.Map == null)
                {
                    logger.Error("Undertaking {Kind} asked for a walk to nowhere; it is given up", deed.Kind);

                    Settle(bot, BotEnding.Failed);

                    return;
                }

                var journey = bot.Journey;

                if (journey == null)
                {
                    logger.Error(
                        "{Name} has no journey, so {Kind} cannot be walked; it is given up",
                        bot.Self?.Name,
                        deed.Kind
                    );

                    Settle(bot, BotEnding.Failed);

                    return;
                }

                if (!doing.Matches(resolve.Sent))
                {
                    if (doing.Follow != null)
                    {
                        journey.Rebase(doing.Map, doing.Follow, doing.Arrival, doing.Note ?? deed.Kind);
                    }
                    else
                    {
                        journey.Rebase(doing.Map, doing.Where, doing.Arrival, doing.Note ?? deed.Kind);
                    }

                    resolve.Sent = doing;
                    resolve.Nearest = int.MaxValue;
                    resolve.Trudged = 0;
                    resolve.Started = -1;

                    resolve.StirredTick = Core.TickCount;

                    return;
                }

                if (doing.Follow == null && bot.Self is { Deleted: false } walker && walker.Map == doing.Map)
                {
                    var away = Math.Max(
                        Math.Abs(walker.Location.X - doing.Where.X),
                        Math.Abs(walker.Location.Y - doing.Where.Y)
                    );

                    if (resolve.Started < 0)
                    {
                        resolve.Started = away;
                    }

                    if (away < resolve.Nearest)
                    {
                        resolve.Nearest = away;
                        resolve.Trudged = 0;
                    }
                    else if (++resolve.Trudged >= TrekLimit)
                    {
                        Trudges++;

                        Settle(
                            bot,
                            BotEnding.Failed,
                            $"the walk stopped closing {away} tiles short of ({doing.Where.X}, {doing.Where.Y}) after {TrekLimit} beats, having set out {resolve.Started} off and got no nearer than {resolve.Nearest}"
                        );
                    }
                }

                return;

            case BotDoingKind.Work:
                if (Core.TickCount - resolve.StirredTick >= LabourMs)
                {
                    Settle(bot, BotEnding.Failed, $"nothing has come of this in {LabourMs / 60000} minutes");

                    return;
                }

                return;

            case BotDoingKind.Done:
                Settle(bot, BotEnding.Done, doing.Note);

                return;

            case BotDoingKind.Failed:
                Settle(bot, BotEnding.Failed, doing.Note);

                return;

            default:
                logger.Error("Undertaking {Kind} said nothing when asked what to do; it is given up", deed.Kind);

                Settle(bot, BotEnding.Failed);

                return;
        }
    }

    private static bool Auction(IBotWilful bot, BotResolve resolve, BotStanding rung, long now)
    {
        var held = resolve.Deed;

        var fresh = held != null && rung == BotStanding.Free && now - resolve.SinceTick < Dwell(held);

        resolve.ReviewedTick = now;
        resolve.Due = false;

        _offers.Clear();

        var keep = rung == BotStanding.Free;

        if (keep)
        {
            resolve.Offered.Clear();
            resolve.OfferedTick = now;
        }

        var asked = 0;
        var largestOutlay = 0;

        for (var i = 0; i < _proposers.Count; i++)
        {
            var proposer = _proposers[i];

            if (proposer.Rung != rung)
            {
                continue;
            }

            if (!Sworn(bot, rung, proposer))
            {
                Unsworn++;

                continue;
            }

            asked++;

            BotDeed offer = null;

            try
            {
                offer = proposer.Propose(bot);
            }
            catch (Exception e)
            {
                logger.Error(e, "Proposer {Name} threw while offering work; it is skipped", proposer.Name);
            }

            if (offer?.Map == null)
            {
                continue;
            }

            if (offer.Outlay > largestOutlay)
            {
                largestOutlay = offer.Outlay;
            }

            if (keep)
            {
                resolve.Offered.Add(proposer.Name);
            }

            _offers.Add(offer);
        }

        if (asked == 0)
        {
            if (rung == BotStanding.Free && held == null)
            {
                Nothing(resolve, now, "nobody answers this rung at all");
            }

            Unserved(rung);

            return false;
        }

        resolve.Urges.Weigh(BotYield.Wealth(bot.Self), largestOutlay);

        var grounded = BotLadder.Overloaded(bot.Self);

        BotDeed best = null;
        var bestScore = 0.0;
        var bestRank = -1;
        var bestWeigh = default(BotWeigh);
        BotDeed second = null;
        var secondScore = 0.0;
        var secondRank = -1;
        var viable = 0;
        string firstVeto = null;

        for (var i = 0; i < _offers.Count; i++)
        {
            var offer = _offers[i];
            var score = BotAppraisal.Weigh(bot, offer, Share(offer.Kind), out var weigh, out var veto);

            if (score <= 0.0)
            {
                firstVeto ??= veto;

                continue;
            }

            viable++;

            var rank = grounded && offer.Standing ? 1 : 0;

            if (rank > bestRank || rank == bestRank && score > bestScore)
            {
                second = best;
                secondScore = bestScore;
                secondRank = bestRank;
                best = offer;
                bestScore = score;
                bestRank = rank;
                bestWeigh = weigh;
            }
            else if (rank > secondRank || rank == secondRank && score > secondScore)
            {
                second = offer;
                secondScore = score;
                secondRank = rank;
            }
        }

        if (bestRank > 0 && secondScore > bestScore)
        {
            Grounded++;
        }

        if (best == null)
        {
            if (held == null && rung == BotStanding.Free)
            {
                Nothing(
                    resolve,
                    now,
                    _offers.Count == 0
                        ? $"{asked} proposers asked, not one of them had anything to offer"
                        : $"{asked} proposers asked, {_offers.Count} offered work, and it was refused: {firstVeto}"
                );
            }

            return false;
        }

        string how = null;
        var heldNow = 0.0;

        if (held != null)
        {
            var heldRank = grounded && held.Standing ? 1 : 0;

            if (bestRank < heldRank)
            {
                return false;
            }

            if (bestRank > heldRank)
            {
                Dislodged++;

                how = "dislodged";
                heldNow = BotAppraisal.Weigh(bot, held, Share(held.Kind), out _);
            }
            else
            {
                var jumps = best.Pressing(bot) && !held.Kind.InsensitiveEquals(best.Kind);

                if (held.Committed && !jumps)
                {
                    Kept++;

                    return false;
                }

                if (fresh && !jumps)
                {
                    return false;
                }

                heldNow = BotAppraisal.Weigh(bot, held, Share(held.Kind), out _);

                var heldScore = heldNow * (jumps ? 1.0 : BotAppraisal.Inertia);

                if (bestScore <= heldScore * (jumps ? 1.0 : SwitchMargin))
                {
                    return false;
                }

                var summoned = !jumps && best.Summons && !held.Kind.InsensitiveEquals(best.Kind);

                if (!jumps && !summoned && Holding(resolve, held, rung, now))
                {
                    Held++;
                    TallyOf(held.Kind).Held++;

                    return false;
                }

                how = (rung, jumps, summoned) switch
                {
                    (not BotStanding.Free, _, _) => "interrupted",
                    (_, true, _) => "jumped",
                    (_, _, true) => "summoned",
                    _ => "outbid"
                };
            }
        }

        Commit(bot, resolve, rung, best, bestWeigh, second, secondScore, now, _offers.Count, viable, firstVeto, how, heldNow);

        return true;
    }

    private static void Commit(
        IBotWilful bot,
        BotResolve resolve,
        BotStanding rung,
        BotDeed deed,
        BotWeigh weigh,
        BotDeed instead,
        double insteadScore,
        long now,
        int table,
        int viable,
        string refused,
        string how = null,
        double heldNow = 0.0
    )
    {
        var dropped = resolve.Deed;
        var gone = "";

        if (dropped != null)
        {
            var heldFor = (now - resolve.SinceTick) / 60000.0;
            var tally = TallyOf(dropped.Kind);

            switch (how)
            {
                case "jumped":
                    Jumped++;
                    tally.Jumped++;

                    break;

                case "summoned":
                    Summoned++;
                    tally.Jumped++;

                    break;

                case "interrupted":
                    Interrupted++;
                    tally.Jumped++;

                    break;

                case "outbid":
                    Outbid++;
                    tally.Outbid++;

                    break;
            }

            var account =
                $"{how ?? "replaced"} by {deed.Kind} at {weigh.Score:F0}/min after {heldFor:F1} of {resolve.Expected:F1} minutes reckoned, taken at {resolve.TakenAt:F0}/min and worth {heldNow:F0}/min by then";

            var pause = Resume
                        && dropped.Steadfast
                        && resolve.Paused == null
                        && how is "jumped" or "summoned" or "dislodged" or "interrupted";

            if (pause)
            {
                Pause(bot, resolve, deed.Kind, now);

                gone = $"; put down {dropped} to take up again ({account})";
            }
            else
            {
                resolve.DroppedKind = dropped.Kind;
                resolve.DroppedTick = now;

                if (how == "outbid" && resolve.Paused != null)
                {
                    DropPaused(bot, resolve, $"the bot changed its mind to {deed.Kind}");
                }

                Settle(bot, BotEnding.Dropped, account, unpause: false);

                gone = $"; dropped {dropped}";
            }
        }

        if (resolve.DroppedKind != null
            && deed.Kind.InsensitiveEquals(resolve.DroppedKind)
            && now - resolve.DroppedTick < ReturnMs
            && dropped?.Kind.InsensitiveEquals(deed.Kind) != true)
        {
            Returned++;
            TallyOf(deed.Kind).Returned++;
            resolve.DroppedKind = null;
        }

        resolve.Deed = deed;
        resolve.Took = rung;
        resolve.Stake = BotYield.Take(bot, deed);
        resolve.SinceTick = now;
        resolve.ReviewedTick = now;
        resolve.StirredTick = now;
        resolve.Due = false;
        resolve.Aside = false;
        resolve.Sent = default;
        resolve.TakenAt = weigh.Score;
        resolve.Bent = false;
        resolve.Expected = Math.Max(0.1, deed.Minutes)
                           + (bot.Self is { } walker && walker.Map == deed.Map
                               ? BotAppraisal.Travel(walker.Location, deed.Where)
                               : 0.0);
        resolve.Because = refused == null
            ? $"{weigh.Describe()}; {viable} of {table} offers worth anything"
            : $"{weigh.Describe()}; {viable} of {table} offers worth anything; refused: {refused}";

        resolve.Empty = null;
        resolve.Urges.Fruitful();

        Count(deed.Kind, 1);
        Taken++;
        TallyOf(deed.Kind).Taken++;

        if (!Chatty)
        {
            return;
        }

        logger.Information(
            "{Name} took on {Deed}: {Why}{Instead}{Dropped}",
            bot.Self?.Name,
            deed,
            resolve.Because,
            instead == null ? "" : $"; over {instead} at {insteadScore:F0}/min",
            gone
        );
    }

    public static void Abandon(IBotWilful bot, string why, bool unreached = true)
    {
        if (bot?.Resolve?.Deed == null)
        {
            return;
        }

        Settle(bot, BotEnding.Failed, why, unreached);
    }

    private static void Settle(
        IBotWilful bot, BotEnding ending, string why = null, bool unreached = false, bool ground = true,
        bool unpause = true
    )
    {
        var resolve = bot?.Resolve;
        var deed = resolve?.Deed;

        if (deed == null)
        {
            return;
        }

        var takings = BotYield.Settle(bot, deed, resolve.Stake, ending);

        if (unreached)
        {
            Unpriced++;

            if (ground)
            {
                BotRefused.Refuse(deed.Map, deed.Where);
            }
        }
        else
        {
            resolve.Ledger.Note(deed.Kind, deed.Map, deed.Where, takings.PerMinute);
        }

        var told = bot.Self is BotMobile { Minded: true };

        if (!unreached)
        {
            BotCommons.Note(deed.Kind, deed.Map, deed.Where, takings.PerMinute, told);

            BotCommons.Claimed(deed.Kind, deed.Expects, takings.PerMinute, told);
        }

        if (ending == BotEnding.Died || (ending == BotEnding.Failed && takings.Worth <= 0))
        {
            resolve.Ledger.Beware(deed.Kind, deed.Map, deed.Where);
        }
        else if (ending == BotEnding.Done)
        {
            Completed?.Invoke(bot.Self, deed.Kind);

            BotRefused.Arrived(deed.Map, deed.Where);

            resolve.Ledger.Worked(deed.Kind, deed.Map, deed.Where);

            var whose = BotLand.Holder(deed.Map, deed.Where);

            if (whose != null && whose != bot.Self?.Guild?.Name)
            {
                BotRegard.Trespassed(bot.Self?.Guild?.Name, whose);
            }
        }

        resolve.Urges.Paid(takings.Worth);

        BotCalling.Worked(bot.Self, deed, takings.Minutes);

        Count(deed.Kind, -1);

        var tallied = TallyOf(deed.Kind);

        switch (ending)
        {
            case BotEnding.Done:
                Finished++;
                tallied.Finished++;

                break;

            case BotEnding.Failed:
                Failed++;
                tallied.Failed++;

                break;

            case BotEnding.Dropped:
                Dropped++;
                tallied.Dropped++;

                break;

            case BotEnding.Died:
                Deaths++;
                tallied.Died++;

                break;
        }

        resolve.Deed = null;
        resolve.Sent = default;
        resolve.Aside = false;

        if (bot.Journey is { Active: true } road && resolve.Deed == null)
        {
            road.Finish();
        }

        resolve.Due = true;

        try
        {
            deed.Drop(bot);
        }
        catch (Exception e)
        {
            logger.Error(e, "Undertaking {Kind} threw while being let go", deed.Kind);
        }

        if (Chatty)
        {
            logger.Information(
                "{Name} {Ending} {Deed}: {Takings}{Why}",
                bot.Self?.Name,
                Word(ending),
                deed,
                takings,
                why == null ? "" : $" — {why}"
            );
        }

        resolve.Remember(
            why == null
                ? $"{Word(ending)} {deed.Kind} after {takings.Minutes:F1} min at {takings.PerMinute:F0}/min"
                : $"{Word(ending)} {deed.Kind} after {takings.Minutes:F1} min at {takings.PerMinute:F0}/min — {(why.Length > 140 ? why[..140] : why)}"
        );

        if (unpause && resolve.Paused != null && resolve.Deed == null)
        {
            Unpause(bot, resolve, ending, deed.Kind);
        }
    }

    public static long Interrupted { get; private set; }

    private static bool Holding(BotResolve resolve, BotDeed held, BotStanding rung, long now)
    {
        if (CommitStretch <= 0.0 || rung != BotStanding.Free || resolve.Took != BotStanding.Free || !held.Steadfast)
        {
            return false;
        }

        if (resolve.Bent || resolve.Trudged >= TrekLimit * TroubleShare)
        {
            Troubled++;

            return false;
        }

        return now - resolve.SinceTick < HoldMs(resolve);
    }

    private static long HoldMs(BotResolve resolve) =>
        (long)Math.Clamp(resolve.Expected * CommitStretch * 60000.0, DwellMs, CommitCapMs);

    private static void Pause(IBotWilful bot, BotResolve resolve, string forKind, long now)
    {
        var deed = resolve.Deed;
        var body = bot.Self;

        resolve.Paused = new BotPause
        {
            Deed = deed,
            Took = resolve.Took,
            Stake = resolve.Stake,
            Since = resolve.SinceTick,
            At = now,
            Because = resolve.Because,
            TakenAt = resolve.TakenAt,
            Expected = resolve.Expected,
            Wealth = BotYield.Standing(body),
            Skill = BotYield.SkillOf(body, deed.Trains),
            For = forKind
        };

        Count(deed.Kind, -1);
        Paused++;
        TallyOf(deed.Kind).Paused++;

        resolve.Deed = null;
        resolve.Sent = default;
    }

    private static BotStake Moved(BotPause pause, Mobile body, long away) =>
        new(
            pause.Stake.Tick + away,
            pause.Stake.Skill + (BotYield.SkillOf(body, pause.Deed.Trains) - pause.Skill),
            pause.Stake.Wealth + (BotYield.Standing(body) - pause.Wealth),
            pause.Stake.Made
        );

    private static void Unpause(IBotWilful bot, BotResolve resolve, BotEnding ending, string after)
    {
        var pause = resolve.Paused;

        resolve.Paused = null;

        var body = bot.Self;
        var deed = pause.Deed;
        var now = Core.TickCount;
        var away = now - pause.At;

        string refusal = null;

        if (ending == BotEnding.Died || body is not { Deleted: false, Alive: true })
        {
            refusal = "the bot died while it was put down";
        }
        else if (away >= AsideCapMs)
        {
            refusal = $"it was put down for {away / 60000.0:F0} minutes";
        }
        else if (body.HitsMax > 0 && body.Hits < body.HitsMax * ResumeHealth)
        {
            refusal = $"the bot came back from {after} with {body.Hits} of {body.HitsMax} health";
        }
        else if (deed.Map != body.Map)
        {
            refusal = "the bot is on another map now";
        }

        resolve.Deed = deed;
        resolve.Took = pause.Took;
        resolve.Stake = Moved(pause, body, away);
        resolve.SinceTick = pause.Since + away;
        resolve.TakenAt = pause.TakenAt;
        resolve.Expected = pause.Expected;
        resolve.Because = pause.Because;
        resolve.ReviewedTick = now;
        resolve.StirredTick = now;
        resolve.Sent = default;
        resolve.Nearest = int.MaxValue;
        resolve.Trudged = 0;
        resolve.Started = -1;
        resolve.Bent = false;
        resolve.Aside = false;
        resolve.Due = false;

        Count(deed.Kind, 1);

        if (refusal != null)
        {
            Unresumed++;

            Settle(bot, BotEnding.Dropped, $"not taken up again after {after}: {refusal}", unpause: false);

            return;
        }

        try
        {
            deed.Resumed(bot);
        }
        catch (Exception e)
        {
            logger.Error(e, "Undertaking {Kind} threw when it was taken up again; it is given up", deed.Kind);

            Settle(bot, BotEnding.Failed, unpause: false);

            return;
        }

        Resumed++;
        TallyOf(deed.Kind).Resumed++;

        if (Chatty)
        {
            logger.Information(
                "{Name} took up {Deed} again after {After}, {Seconds:F0}s after putting it down",
                body.Name,
                deed,
                after,
                away / 1000.0
            );
        }
    }

    private static void DropPaused(IBotWilful bot, BotResolve resolve, string why)
    {
        var pause = resolve.Paused;

        if (pause == null)
        {
            return;
        }

        resolve.Paused = null;
        Unresumed++;

        var body = bot.Self;
        var deed = pause.Deed;
        var takings = BotYield.Settle(bot, deed, Moved(pause, body, Core.TickCount - pause.At), BotEnding.Dropped);
        var told = body is BotMobile { Minded: true };

        resolve.Ledger.Note(deed.Kind, deed.Map, deed.Where, takings.PerMinute);
        BotCommons.Note(deed.Kind, deed.Map, deed.Where, takings.PerMinute, told);
        BotCommons.Claimed(deed.Kind, deed.Expects, takings.PerMinute, told);
        resolve.Urges.Paid(takings.Worth);

        Dropped++;
        TallyOf(deed.Kind).Dropped++;

        try
        {
            deed.Drop(bot);
        }
        catch (Exception e)
        {
            logger.Error(e, "Undertaking {Kind} threw while being let go", deed.Kind);
        }

        resolve.Remember($"dropped {deed.Kind} while it was put down — {why}");

        if (Chatty)
        {
            logger.Information(
                "{Name} dropped {Deed}: {Takings} — put down for {For}, and {Why}",
                body?.Name,
                deed,
                takings,
                pause.For,
                why
            );
        }
    }

    public static string Explain(BotMobile bot)
    {
        var resolve = bot?.Resolve;

        if (resolve == null)
        {
            return "that bot has no resolve to read.";
        }

        var now = Core.TickCount;
        var deed = resolve.Deed;

        using var line = ValueStringBuilder.Create(512);

        if (deed == null)
        {
            line.Append($"{bot.Name} holds nothing ({resolve.Empty ?? "nothing recorded about why"})");
        }
        else
        {
            var age = (now - resolve.SinceTick) / 60000.0;
            var worth = BotAppraisal.Weigh(bot, deed, Share(deed.Kind), out _);
            var left = (HoldMs(resolve) - (now - resolve.SinceTick)) / 60000.0;

            string hold;

            if (!deed.Steadfast)
            {
                hold = "not steadfast work, so any offer that clears the margin takes it once the first half minute is up";
            }
            else if (CommitStretch <= 0.0)
            {
                hold = "steadfast, but holds are switched off";
            }
            else if (resolve.Took != BotStanding.Free)
            {
                hold = "handed out by a rung above the bot's own business, which holds are not for";
            }
            else if (resolve.Bent || resolve.Trudged >= TrekLimit * TroubleShare)
            {
                hold = "in trouble on its walk, so its hold is lifted";
            }
            else if (left > 0.0)
            {
                hold = $"held against ordinary offers for {left:F1} more minutes";
            }
            else
            {
                hold = "past its hold, so any offer that clears the margin takes it";
            }

            line.Append(
                $"{bot.Name} holds {deed}: {age:F1} of {resolve.Expected:F1} minutes reckoned; {hold}; taken at {resolve.TakenAt:F0}/min, worth {worth:F0}/min now"
            );
        }

        if (resolve.Paused is { } pause)
        {
            line.Append($"; put down {pause.Deed} for {pause.For} {(now - pause.At) / 1000} seconds ago");
        }

        var first = true;

        foreach (var ending in resolve.Recent())
        {
            line.Append(first ? "; lately: " : " | ");
            line.Append(ending);
            first = false;
        }

        return line.ToString();
    }

    private static int Percent(long part, long whole) =>
        whole <= 0 ? 0 : (int)Math.Round(100.0 * part / whole);

    public static string DescribeResolve()
    {
        using var line = ValueStringBuilder.Create(1024);

        var ended = Finished + Failed + Dropped + Deaths;

        line.Append(
            $"{Finished} of {ended} endings finished ({Percent(Finished, ended)}%), {Failed} failed, {Dropped} dropped, {Deaths} died; of the drops {Jumped} for something that would not wait, {Summoned} for a call from outside, {Interrupted} for a rung above, {Dislodged} for a full pack and {Outbid} outbid; {Held} better offers refused inside a hold and {Troubled} holds lifted for trouble; {Paused} put down, {Resumed} taken up again and {Unresumed} not; {Returned} kinds of work taken back within {ReturnMs / 60000} minutes of being dropped; by trade, taken: finished/dropped %, held, put down/taken up:"
        );

        var rows = new List<KeyValuePair<string, Tally>>(_tallies);

        rows.Sort((a, b) => b.Value.Taken.CompareTo(a.Value.Taken));

        for (var i = 0; i < rows.Count && i < 16; i++)
        {
            var (kind, t) = rows[i];
            var ends = t.Finished + t.Failed + t.Dropped + t.Died;

            line.Append($" {kind} {t.Taken}: {Percent(t.Finished, ends)}/{Percent(t.Dropped, ends)}");

            if (t.Held > 0 || t.Paused > 0)
            {
                line.Append($", held {t.Held}, {t.Paused}/{t.Resumed}");
            }

            line.Append(i + 1 < rows.Count && i + 1 < 16 ? ";" : "");
        }

        return line.ToString();
    }

    private static string Word(BotEnding ending) =>
        ending switch
        {
            BotEnding.Done => "finished",
            BotEnding.Failed => "failed at",
            BotEnding.Dropped => "dropped",
            _ => "died doing"
        };

    private static double Share(string kind)
    {
        if (kind == null || !_busy.TryGetValue(kind, out var mine) || mine <= 0)
        {
            return 0.0;
        }

        var total = 0;

        foreach (var (_, count) in _busy)
        {
            if (count > 0)
            {
                total += count;
            }
        }

        return total <= 0 ? 0.0 : (double)mine / total;
    }

    private static void Nothing(BotResolve resolve, long now, string why)
    {
        if (!resolve.Urges.IsBarren)
        {
            Barren++;
        }

        resolve.Empty = why;
        resolve.Urges.Barren(now);
    }

    private static void Count(string kind, int by)
    {
        if (kind == null)
        {
            return;
        }

        _busy.TryGetValue(kind, out var count);

        count += by;

        if (count <= 0)
        {
            _busy.Remove(kind);

            return;
        }

        _busy[kind] = count;
    }

    private static void Unserved(BotStanding rung)
    {
        if (rung is not (BotStanding.Free or BotStanding.Failing) || !_mute.Add(rung))
        {
            return;
        }

        if (rung == BotStanding.Free)
        {
            logger.Error(
                "Nothing proposes any work at all, so every bot will find nothing worth doing. A kind of work is a folder with an IBotProposer in it, handed to BotWill.Offer"
            );

            return;
        }

        logger.Error(
            "Nothing proposes work for the {Rung} rung; bots on it will keep what they have and wait it out",
            rung
        );
    }

    private static void Census(long now)
    {
        if (!_censused)
        {
            _censused = true;
            _censusTick = now;

            return;
        }

        if (now - _censusTick < CensusMs)
        {
            return;
        }

        _censusTick = now;

        logger.Information("Will: {State}", Describe());
        logger.Information("Resolve: {State}", DescribeResolve());
        logger.Information("Roles: {State}", BotCalling.Describe());
    }

    public static string Describe()
    {
        using var line = ValueStringBuilder.Create(256);

        line.Append(
            $"{Taken} taken on, {Finished} finished, {Failed} failed, {Dropped} dropped, {Deaths} died doing it; {Barren} times nothing was worth doing, {Unsworn} offers withheld from classes sworn elsewhere, {Trudges} given up for a walk that stopped closing; holding now:"
        );

        var kinds = 0;

        foreach (var (kind, count) in _busy)
        {
            if (count <= 0)
            {
                continue;
            }

            if (kinds > 0)
            {
                line.Append(",");
            }

            line.Append($" {kind} {count}");
            kinds++;
        }

        if (kinds == 0)
        {
            line.Append(" nothing");
        }

        if (Unpriced > 0)
        {
            line.Append($"; {Unpriced} endings were not allowed to price the ground because the bot never got there, {Unblamed} of which blamed nothing because what they could not reach was somebody rather than somewhere");
        }

        if (BotAppraisal.Stopped > 0)
        {
            line.Append(
                $"; {BotAppraisal.Stopped} offers marked down because the bot was carrying more than it can walk with and the work needed a step"
            );
        }

        if (Grounded > 0 || Dislodged > 0)
        {
            line.Append(
                $"; {Grounded} times a full pack put the errand that needs no step ahead of better-paid work that does, and {Dislodged} times it took what the bot was holding away for it"
            );
        }

        if (Kept > 0)
        {
            line.Append($"; {Kept} better offers were turned down because the work in hand had already been paid for");
        }

        if (BotAppraisal.Unpaid > 0)
        {
            line.Append($"; {BotAppraisal.Unpaid} times work that is paid nothing on purpose was let past the earnings veto");
        }

        return line.ToString();
    }

    public static void Reset()
    {
        _offers.Clear();
        _busy.Clear();
        _mute.Clear();

        _censused = false;

        Taken = 0;
        Finished = 0;
        Failed = 0;
        Dropped = 0;
        Deaths = 0;
        Barren = 0;
        Unsworn = 0;
        Trudges = 0;
        Grounded = 0;
        Dislodged = 0;
        Kept = 0;
        Jumped = 0;
        Summoned = 0;
        Interrupted = 0;
        Outbid = 0;
        Held = 0;
        Troubled = 0;
        Paused = 0;
        Resumed = 0;
        Unresumed = 0;
        Returned = 0;

        _tallies.Clear();
        BotCalling.Forget();
    }
}
