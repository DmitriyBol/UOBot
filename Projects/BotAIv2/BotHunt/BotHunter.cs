using System;
using Server.Logging;
using Server.Regions;

namespace Server.BotAI.V2;

/// <summary>
/// Offers a fight to any bot healthy enough to want one.
///
/// <para>
/// <b>Nothing decides who is a fighter.</b> No class list, no role check: whoever can beat the thing standing
/// in the field is offered it, and a mage with wrestling at thirty will find that the arithmetic says no. That
/// is the same rule as the pickaxe and the pen — the ability decides, not the name — and it means a crafter
/// caught in a lean patch can go and hit something rather than sitting in a census as "nothing was worth
/// doing".
/// </para>
///
/// <para>
/// <b>Only within the population's own ground, and that is the whole of the answer to the first version's
/// worst night.</b> Four hundred and forty-three deaths, a hundred and four of them one bot resurrecting in
/// the same tile every thirty seconds, all of it in the far zones the population had walked to. A hunt that
/// cannot begin more than a screen and a half from where the bot is standing, in a world already bounded to
/// two hundred tiles around the spawn, cannot build that loop: the bot is never far from where it gets up.
/// </para>
///
/// <para>
/// The cost of this proposer is a real spatial sweep, every time a free bot asks. That is unavoidable and it
/// is the honest exception to "ask the world cheaply": a vein stays where it is and can be remembered, a shop
/// keeper stands still, but a monster walks and respawns, so a remembered one is a lie inside a minute.
/// </para>
/// </summary>
public sealed class BotHunter : IBotProposer
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotHunter));

    /// <summary>
    /// The share of its health a bot needs before it will go looking for a fight.
    ///
    /// Higher than the share at which it runs away, on purpose, and the gap is what stops a bot bouncing:
    /// flee at forty per cent, set out again at eighty. Without the gap a bot that just escaped is
    /// immediately offered the same fight by the same arithmetic.
    /// </summary>
    public static double FitAt { get; set; } = 0.8;

    private static bool _saidNoQuarry;

    public string Name => "Hunter";

    public BotStanding Rung => BotStanding.Free;

    public BotDeed Propose(IBotWilful bot)
    {
        var body = bot?.Self;
        var map = body?.Map;

        if (map == null || map == Map.Internal || !body.Alive)
        {
            return null;
        }

        if (body.HitsMax <= 0 || body.Hits < body.HitsMax * FitAt)
        {
            return null;
        }

        // <b>A medic does not go looking.</b> See BotClass.DefendsOnly: its station is already at the back of
        // the formation, and this makes the same thing true of its instincts, so it does not walk forward
        // into a fight the company is winning and is then not there for the one it is losing. Hitting back
        // when something reaches it stays a reflex — BotMobile.OnDamage is not a decision and cannot be
        // talked out of — so this refuses the hunt, never the defence.
        if (body is BotMobile { Class.DefendsOnly: true })
        {
            Sworn++;

            return null;
        }

        // <b>Already in more of a fight than it can win.</b> Offering a quarry here offers a second front,
        // and it is not hypothetical: a bot that has just given a fight up for being outnumbered is standing
        // in the middle of those numbers with nothing in hand, and the nearest thing it can beat one-to-one
        // is one of them. Left alone it takes the same fight again, drops it again, and the pair of rules
        // sit there swapping the bot between them until something kills it. A prowl is scored at almost
        // nothing and is the right answer anyway: it is a walk to somewhere else.
        if (BotThreat.Decide(body, BotMobile.NoticeRange) == BotStand.Outmatched)
        {
            var elsewhere = Hunting(bot, body, map);

            return elsewhere == Point3D.Zero ? null : new BotProwl(map, elsewhere);
        }

        var quarry = BotQuarry.Best(body, BotQuarry.Reach);

        // <b>Asked before the walk instead of after it.</b> See BotThreat.Overrun: 202 hunts ended on "too
        // many of them around" over the night of 02-03.09.2026 and 184 of those fired inside the shortest
        // span the ledger records, so the crowd was standing there when the bot chose rather than gathering
        // while it walked. Only for quarry worth walking to — something already at arm's length costs no
        // journey, and the arrival test will settle it on the next beat anyway.
        if (quarry != null
            && !Utility.InRange(body.Location, quarry.Location, Near)
            && BotThreat.Overrun(body, quarry, BotMobile.NoticeRange))
        {
            // Crowded rather than shunned, and BotQuarry says why at length: one bot being outnumbered is the
            // argument for a company, not against the creature. BotQuarry.Best skips it for lone hunters from
            // here, so the next pick is a different one.
            BotQuarry.Crowd(quarry);
            Overrun++;

            quarry = null;
        }

        if (quarry == null)
        {
            Missing(map);

            // Nothing here. Offer to go and look instead — worth almost nothing, so it wins only when the
            // auction has nothing else at all, which is exactly when a fighter should be out walking rather
            // than standing in a square.
            var ground = Hunting(bot, body, map);

            return ground == Point3D.Zero ? null : new BotProwl(map, ground);
        }

        // Claimed the moment it is offered, not when the first blow lands.
        //
        // <b>This ordering is the whole of the difference between a fight and a crowd.</b> A claim made on
        // contact comes too late: every free bot in the field sees the same unclaimed skeleton in the same
        // beat, every one of them sets off for it, and only the two or three that can reach a tile beside it
        // ever swing — the rest orbit it for as long as it lives, which is what a pile of bots circling one
        // monster actually is. Claiming here means one bot goes, and the others are offered the next thing.
        BotQuarry.Claim(body, quarry);

        // The skill the roll actually handed this bot, not the one its class is named after. A bot with no
        // weapon is a bot with its hands, and hands train wrestling.
        var trains = bot.Bond?.Weapon?.Skill ?? SkillName.Wrestling;

        return new BotSlay(quarry, trains);
    }

    /// <summary>
    /// Somewhere within the population's ground that is worth walking to for a fight, or nothing.
    ///
    /// <para>
    /// Two conditions and no cleverness: a body can stand on it, and it is not a town — because a town is
    /// where fighting is forbidden outright, and offering a bot a walk to one would be offering it a walk to
    /// a place it cannot use. Sampled rather than searched: a handful of candidates costs a few surface
    /// probes, where anything exhaustive would cost a spatial sweep per idle bot per review.
    /// </para>
    ///
    /// <para>
    /// Whether the ground turns out to hold anything is not asked here and cannot be — that is what the walk
    /// is for. What the bot learns by arriving is filed by the ledger against that patch, so ground that
    /// never pays stops being chosen without anybody keeping a list of bad places.
    /// </para>
    /// </summary>
    private static Point3D Hunting(IBotWilful bot, Mobile body, Map map)
    {
        var ledger = bot?.Resolve?.Ledger;
        var home = BotPopulation.Where;
        // Half the ground the population may want things on, and not because a prowl is less entitled to it.
        // The far edge of the roam is where the bad terrain is — the bots carried home by the rescue were all
        // picked up out there — and a walk that ends in being rescued is worse than a shorter walk.
        var roam = Math.Max(BotQuarry.Reach, BotPopulation.Roam / 2);

        var best = Point3D.Zero;
        var bestPaid = -1.0;
        var bestKnown = -1.0;
        var bestWanted = false;

        // How much of this sample the quietness rule threw away. Counted so that "nowhere to walk" can be
        // told apart from "nowhere quiet enough to be worth walking to" — see Stranded.
        var refused = 0;

        // The least dull of what it threw away, in case it throws away all of it.
        var quietest = Point3D.Zero;
        var quietestSafety = 0.0;

        // The shard's own answers go in first, so that they are always on the list and are beaten only by
        // ground this bot has actually been paid on. See Noisy and Paying.
        //
        // <b>Two of them, and the second was missing.</b> Noisy is where blood is being spilt, which is the
        // right thing to name when nobody has been paid anywhere yet. Paying is where hunting has actually
        // come to something, which is a different question and the better one once the island has been
        // worked — and with the roam raised to five hundred on 27.08.2026, eight random darts into a box a
        // quarter of a million tiles across stopped finding either. Eighteen bots of thirty-four were
        // prowling at once, which is this shard's own way of saying that half the population had nothing to
        // do and was walking to prove it.
        var noisy = Noisy(body, map, roam);
        var paying = Paying(body, map, roam);

        // <b>And the map's own answer, which it had never been asked for.</b> Noisy is where blood is being
        // spilt now and forgets in twenty minutes; Paying is where this trade has come to something. Neither
        // is the quadrant record, which is the one thing on this shard that remembers for good which ground
        // has hurt people — and it was consulted only to veto a square somebody else had thought of. The beat
        // printed the consequence every window without a second number to compare it to: 15125 grounds passed
        // over as too quiet against 0 picked for having hurt somebody.
        //
        // At the whole roam rather than half of it, and that is the other half of the complaint. The halving
        // above is an argument about darts thrown into terrain nobody has seen — a walk that ends in being
        // rescued is worse than a shorter walk. It is not an argument about a square the population has stood
        // in and bled in: that is a known place, and on 02.09.2026 the known dangerous ones sat between
        // Britain and Minoc, about four hundred and ninety tiles from home, with the darts reaching 250.
        var feared = Feared(body, map);

        for (var tries = 0; tries <= Samples + 2; tries++)
        {
            Point3D where;

            if (tries == 0)
            {
                if (noisy == Point3D.Zero)
                {
                    continue;
                }

                where = noisy;
            }
            else if (tries == 1)
            {
                if (paying == Point3D.Zero)
                {
                    continue;
                }

                where = paying;
            }
            else if (tries == 2)
            {
                if (feared == Point3D.Zero)
                {
                    continue;
                }

                where = feared;
            }
            else
            {
                var x = home.X + Utility.RandomMinMax(-roam, roam);
                var y = home.Y + Utility.RandomMinMax(-roam, roam);

                if (!BotStep.Settle(map, x, y, out var z))
                {
                    continue;
                }

                where = new Point3D(x, y, z);
            }

            // Far enough to be somewhere else. A candidate under the bot's nose is the ground it is already
            // standing on, and walking to it proves nothing.
            if (Utility.InRange(body.Location, where, BotQuarry.Reach))
            {
                continue;
            }

            if (Region.Find(where, map)?.IsPartOf<TownRegion>() == true)
            {
                continue;
            }

            // Somewhere already proved impossible from here, asked for nothing.
            //
            // <b>A dictionary lookup, and it must stay one.</b> The first attempt at this ran a real path
            // search per candidate — four of them, per idle bot, per review — on the reasoning that a search
            // costs two milliseconds and this is the cheapest moment there is. It is not: the whole population
            // shares sixty milliseconds of searching a second, and fifteen bots vetting prowl points spent all
            // of it. Every other search on the shard then shrank to the floor of a quarter millisecond, came
            // back with nothing walkable, and the bots stopped being able to reach anything at all — a hundred
            // and fifty failures and eighteen bots "rescued" from a spot three tiles from home. The proposer
            // contract says this in as many words: the question may be real, it may not be expensive.
            //
            // What is free is what somebody has already proved. A pocket of ground walked to its edges answers
            // in one comparison, which is exactly the case worth excluding — across water, behind a wall.
            if (BotReach.Ask(map, body.Location, where, BotArrival.Within(BotQuarry.Reach)) == BotReachVerdict.Sealed)
            {
                continue;
            }

            // Of the places that pass, the one this bot has actually been paid at.
            //
            // <b>A prowl to a random point is a walk that usually finds nothing, and it showed.</b> Forty-six
            // of sixty-four finished undertakings in one stretch were prowls — bots spending their evening
            // walking to empty fields — against four hunts. The ledger already knows where fighting paid this
            // bot, because it files every finished hunt under the patch of ground it happened on; it simply
            // was not being asked. Nothing new is measured here and no new memory is kept.
            //
            // Asked with a prior of nothing, so unknown ground scores zero and known-bad ground scores what it
            // is worth. Early on everything ties at zero and the pick is the first sampled — which is the
            // exploration this needs — and the moment one patch pays, that patch wins.
            var paid = ledger?.Expect(BotSlay.Trade, map, where, 0.0) ?? 0.0;

            // <b>And where the shard as a whole knows there is fighting, for the bot that knows nothing.</b>
            // The ledger above is private, and a private memory cannot start itself: a scribe that has never
            // once been paid for a hunt scores every candidate at nought, takes the first sample, walks a
            // minute to an empty field and does it again for the rest of the evening. That is not a
            // hypothetical — Lysa did exactly that four times running on 25.08.2026 while two companies were
            // killing spectres and trolls two hundred tiles away, which she had no way to know.
            //
            // BotPeril is that fact, kept shard-wide and decaying, and it is already what a captain reads to
            // decide where to take a company. Used only to break the tie the comment above admits to — "early
            // on everything ties at zero and the pick is the first sampled" — so a bot with real experience
            // of its own still prefers its own ground, and a bot with none walks towards the noise instead of
            // at random.
            var known = BotPeril.Reading(map, where);

            // <b>Ground the population has walked through fifty times without incident, refused outright.</b>
            // By order, and it is the one rule here that throws a candidate away rather than ranking it: a
            // square that has earned its way above BotQuad.TooQuiet has nothing living in it worth killing,
            // and a hunter standing in it is a hunter earning nothing. Peril cannot express this — it forgets
            // — so "quiet because it was cleared an hour ago" and "quiet because there was never anything
            // here" read the same to it. See BotQuad on why the two maps are separate.
            var safety = BotQuad.Safety(map, where);

            if (safety > BotQuad.TooQuiet)
            {
                Quiet++;
                refused++;

                // <b>Kept as the answer of last resort, because a filter with nothing behind it is a ban.</b>
                // This candidate has already passed every other test — it is somewhere else, out of town and
                // reachable — and is being thrown away only for being dull. That is right while anything
                // better exists and is a refusal to walk when nothing does: the counter put in on 02.09.2026
                // to settle the argument came back with 1585 hunters in a single window left with nowhere to
                // go at all, which is the population's own way of saying that half of it was standing still
                // because every square it thought of was safe. The quietest ground on the island is still a
                // walk to somewhere else, which is what a prowl is for and what this shard calls "scored at
                // almost nothing and the right answer anyway".
                if (quietest == Point3D.Zero || safety < quietestSafety)
                {
                    quietest = where;
                    quietestSafety = safety;
                }

                continue;
            }

            // <b>And ground that has hurt somebody outranks ground that merely paid.</b> Also by order, and
            // it is a precedence rather than a bonus: any square at or below BotQuad.Wanted beats every
            // square above it, whatever the ledger says about takings. Inside each of the two groups the old
            // ordering stands untouched — what this bot was paid, then where the shard hears fighting — so a
            // hunter with real experience still prefers its own ground among equals.
            var wanted = safety <= BotQuad.Wanted;

            var better = best == Point3D.Zero
                || (wanted && !bestWanted)
                || (wanted == bestWanted && (paid > bestPaid || (paid >= bestPaid && known > bestKnown)));

            if (better)
            {
                best = where;
                bestPaid = paid;
                bestKnown = known;
                bestWanted = wanted;

                if (wanted)
                {
                    Sought++;
                }
            }
        }

        // <b>The one number that says whether the quietness rule is a preference or a veto.</b> On 02.09.2026
        // six warriors stood at 1420,1685 in Britain holding 304 to 446 gold while the record said "31
        // proposers asked, not one of them had anything to offer", and the beat said 15125 hunting grounds
        // had been passed over as too quiet against 0 picked. Those two facts are consistent with a rule
        // working as designed and equally consistent with a filter that leaves a hunter with nothing at all,
        // and nothing on the shard could tell them apart. This can: it counts only the times every last
        // candidate was thrown away for quietness and the hunter came back empty.
        if (best == Point3D.Zero && refused > 0)
        {
            Stranded++;

            // Still counted as stranded above, because it is: the count is what says how often the whole
            // sample was dull, and that stays worth knowing after the bot has somewhere to walk again.
            return quietest;
        }

        return best;
    }

    /// <summary>
    /// The ground the population has actually been paid for fighting on, if this bot could get there.
    ///
    /// <para>
    /// The same shape as <see cref="Noisy"/> and offered on the same terms — a candidate, never an answer,
    /// so a bot with real experience of its own still prefers its own ground. What it adds is the case Noisy
    /// cannot cover: a wood that pays well and has stopped hurting anybody reads as nothing on the peril map,
    /// because the peril map is a record of harm and decays in twenty minutes. Being good at somewhere is
    /// exactly what makes it disappear from the only shard-wide list there was.
    /// </para>
    /// </summary>
    private static Point3D Paying(Mobile body, Map map, int roam)
    {
        var rich = BotCommons.Richest(BotSlay.Trade, map, body.Location, roam);

        if (rich == Point3D.Zero || !BotStep.Settle(map, rich.X, rich.Y, out var z))
        {
            return Point3D.Zero;
        }

        var where = new Point3D(rich.X, rich.Y, z);

        // The same two tests every sampled candidate has to pass: somewhere else, and reachable.
        if (Utility.InRange(body.Location, where, BotQuarry.Reach))
        {
            return Point3D.Zero;
        }

        return BotReach.Ask(map, body.Location, where, BotArrival.Within(BotQuarry.Reach)) == BotReachVerdict.Sealed
            ? Point3D.Zero
            : where;
    }

    /// <summary>
    /// The worst ground the quadrant map knows of, if this bot could get to it.
    ///
    /// <para>
    /// A candidate on the same terms as the other two named ones — never an answer, so a hunter with real
    /// experience of its own still prefers its own ground. Reckoned from the population's home rather than
    /// from the bot's feet, exactly as the random darts are, so that a company setting out is setting out to
    /// the same place.
    /// </para>
    /// </summary>
    private static Point3D Feared(Mobile body, Map map)
    {
        var middle = BotQuad.WorstNear(map, BotPopulation.Where, FearedReach);

        if (middle == Point2D.Zero || !BotStep.Settle(map, middle.X, middle.Y, out var z))
        {
            return Point3D.Zero;
        }

        var where = new Point3D(middle.X, middle.Y, z);

        // The same two tests every candidate has to pass: somewhere else, and reachable.
        if (Utility.InRange(body.Location, where, BotQuarry.Reach))
        {
            return Point3D.Zero;
        }

        return BotReach.Ask(map, body.Location, where, BotArrival.Within(BotQuarry.Reach)) == BotReachVerdict.Sealed
            ? Point3D.Zero
            : where;
    }

    /// <summary>
    /// The square the shard has been bleeding in lately, if it is somewhere this bot could go.
    ///
    /// <para>
    /// Offered as a candidate rather than as an answer. Random sampling can look eight times and never land
    /// in the one square that matters — a graveyard is a couple of hundred tiles across at most and the roam
    /// is six hundred — so the place most likely to have a fight in it has to be put on the list by name or
    /// it will usually not be on the list at all.
    /// </para>
    /// </summary>
    private static Point3D Noisy(Mobile body, Map map, int roam)
    {
        var worst = BotPeril.Worst(map, body.Location, roam, out _);

        if (worst == Point3D.Zero || !BotStep.Settle(map, worst.X, worst.Y, out var z))
        {
            return Point3D.Zero;
        }

        var where = new Point3D(worst.X, worst.Y, z);

        // The same two tests every sampled candidate has to pass: somewhere else, and reachable.
        if (Utility.InRange(body.Location, where, BotQuarry.Reach)
            || Region.Find(where, map)?.IsPartOf<TownRegion>() == true
            || BotReach.Ask(map, body.Location, where, BotArrival.Within(BotQuarry.Reach)) == BotReachVerdict.Sealed)
        {
            return Point3D.Zero;
        }

        return where;
    }

    /// <summary>
    /// How many places to try before giving up for this beat. Each one is a surface probe and a lookup, both
    /// cheap; finding nowhere is not a failure, and the bot asks again in a few seconds.
    /// </summary>
    private const int Samples = 8;

    /// <summary>Close enough that there is no walk to save by weighing the odds first. See the check in Propose.</summary>
    public static int Near { get; set; } = 5;

    /// <summary>Quarry passed over because the crowd around it was already hopeless. See <c>BotThreat.Overrun</c>.</summary>
    public static long Overrun { get; private set; }

    /// <summary>
    /// How far from home a square the map calls dangerous may be and still be worth setting out for.
    ///
    /// <para>
    /// <b>Its own number, because the roam is an argument about somewhere else.</b> The roam bounds ground the
    /// population may want things on, and the hunt halves it again for random darts, on the grounds that a
    /// walk into unseen terrain can end in being carried home. Neither argument is about a square the
    /// population has already stood in and bled in.
    /// </para>
    ///
    /// <para>
    /// Eight hundred, and the figure comes from the island itself. On 02.09.2026 the worst square the map knew
    /// was (2025, 975) at -0.60 on 28 blows and one death — the swamps between Britain and Minoc, which is
    /// where Patrick said the danger was — and home is (1440, 1470), five hundred and eighty-five tiles off.
    /// The roam was five hundred and the darts reached two hundred and fifty, so the one square everybody
    /// should have been walking to was outside both, and the beat recorded it plainly: 12 squares worth going
    /// to, 0 ever picked, and 1585 hunters in a single window left with nowhere to walk at all.
    /// </para>
    /// </summary>
    public static int FearedReach { get; set; } = 800;

    private static void Missing(Map map)
    {
        if (_saidNoQuarry)
        {
            return;
        }

        _saidNoQuarry = true;

        // Once, by name. A population with nothing to fight and no gold coming in looks exactly like a
        // population that does not feel like fighting, and the difference is the whole economy.
        logger.Error(
            "Nothing within {Reach} tiles of the bots on {Map} is worth fighting, so no gold will enter the world",
            BotQuarry.Reach,
            map
        );
    }

    /// <summary>Lets the complaint be made again after a world reload.</summary>
    /// <summary>Answers that went to a class which only ever fights what reaches it first.</summary>
    public static long Sworn { get; private set; }

    /// <summary>Candidates thrown away for being ground the population has found nothing in.</summary>
    public static long Quiet { get; private set; }

    /// <summary>Times a hunter was left with nowhere to walk because every sampled ground was too quiet.</summary>
    public static long Stranded { get; private set; }

    /// <summary>Times a hunting ground was picked because it is ground that has hurt somebody.</summary>
    public static long Sought { get; private set; }

    public static string Describe() =>
        $"{Sworn} answers went to classes that only defend; {Quiet} hunting grounds passed over as too quiet (above {BotQuad.TooQuiet:F2}), {Sought} picked for having hurt somebody (at or below {BotQuad.Wanted:F2}), {Stranded} hunters left with nowhere to walk at all because every ground they looked at was too quiet, {Overrun} quarry passed over for the crowd already round it";

    public static void Forget()
    {
        _saidNoQuarry = false;
        Sworn = 0;
        Quiet = 0;
        Stranded = 0;
        Overrun = 0;
        Sought = 0;
    }
}
