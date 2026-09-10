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
            var elsewhere = Hunting(bot, body, map, out var outmatched);

            return elsewhere == Point3D.Zero ? null : new BotProwl(map, elsewhere, outmatched);
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
            var ground = Hunting(bot, body, map, out var company);

            return ground == Point3D.Zero ? null : new BotProwl(map, ground, company);
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
    /// <param name="company">
    /// Whether the ground that came back is ground this bot cannot take alone. True means the strength gate
    /// refused it and the bots standing nearby would clear it together — so whatever walks there is obliged
    /// to raise a company first. See BotQuad.Together.
    /// </param>
    private static Point3D Hunting(IBotWilful bot, Mobile body, Map map, out bool company)
    {
        company = false;

        var ledger = bot?.Resolve?.Ledger;
        var home = BotPopulation.Where;
        // Half the ground the population may want things on, and not because a prowl is less entitled to it.
        // The far edge of the roam is where the bad terrain is — the bots carried home by the rescue were all
        // picked up out there — and a walk that ends in being rescued is worse than a shorter walk.
        //
        // <b>And never further than a path is paid to look, which is the half that was missing.</b> Two
        // numbers sat on one shelf and had never met: this one says how far a dart may be thrown, and
        // BotPath's ceiling says how far a road is ever searched for. On 08.09.2026 they read five hundred
        // and two hundred and forty. In the ten minutes measured, 432 of the shard's 495 refused roads were
        // prowls, their destinations averaged 392 tiles from home, and 324 of them lay beyond three hundred
        // and fifty — a band no search on this shard is funded to cross. Those errands did not sometimes
        // fail; they could not succeed, and every one of them paid for a full search before finding out.
        //
        // Nothing else needed changing to see it: Roam is a dial, and halving it live moved the whole of the
        // shard's refused-road count within minutes.
        //
        // <b>It cannot be fixed by remembering places.</b> BotRefused files a square eight tiles across and
        // the far edge of a five-hundred-tile box is thousands of them, so a dart lands on a fresh square
        // every time and the record it should have consulted is always empty. A note about a place is no
        // answer to a sample taken over an area.
        var roam = Math.Clamp(BotPopulation.Roam / 2, BotQuarry.Reach, Walkable);

        var best = Point3D.Zero;
        var bestPaid = -1.0;
        var bestKnown = -1.0;
        var bestWanted = false;

        // How much of this sample the quietness rule threw away. Counted so that "nowhere to walk" can be
        // told apart from "nowhere quiet enough to be worth walking to" — see Stranded.
        var refused = 0;

        // Whether the ground that wins will need a company raised for it. Held beside the winner rather than
        // recomputed, because the answer belongs to the square that was chosen and not to the last one looked at.
        var needsCompany = false;
        var bestNeedsCompany = false;

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

            // Per candidate, or the first square that wanted a company would mark every square after it.
            needsCompany = false;

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

            // <b>And near enough that a road to it would ever be searched for — measured from the bot, which
            // is the correction that makes the rule work at all.</b> The candidates are reckoned around the
            // population's home and not around the asker, on purpose, so that a company setting out sets out
            // to the same place. That is right, and it means clamping the throw says nothing about the length
            // of the walk: a bot that has itself wandered four hundred tiles out is handed a destination
            // beside the house, and the road it needs is four hundred tiles long whatever the throw was.
            //
            // The search is funded by distance — MsPerTile a tile up to CeilingMs — so beyond Walkable no
            // road is ever found, from anywhere, for anybody. Asked here, of this bot and this square, rather
            // than of the box the darts were thrown into. See Walkable.
            if (!Utility.InRange(body.Location, where, Walkable))
            {
                Distant++;

                continue;
            }

            // <b>And ground somebody has actually stood on, which is the only cheap promise that a road to it
            // exists.</b> Everything this shard knows about bad ground is a record of failures — the reach
            // ledger's pockets, the quadrant baulks, BotRefused's squares — and all three are the wrong shape
            // for a dart. A refusal describes one square; the island has hundreds of thousands of them, so a
            // random throw lands on a fresh one every time and the record it should have consulted is always
            // empty. Measured on 08.09.2026: with the throw already clamped to what a search is funded to
            // cross, 249 of 350 failures in five minutes were still prowls, their destinations averaging 177
            // tiles away — well inside the budget. Remembering where nobody could get to is no answer to a
            // sample taken over an area.
            //
            // <b>The positive record is the one that scales, and it was already written down.</b> BotQuad
            // marks a square trodden when a bot has stood in it, keeps it across restarts, and this shard
            // read 4096 of them back at boot — some three and a half million tiles. Its own Frontier says the
            // reasoning in as many words, about exploration: a candidate beside ground somebody has walked
            // can be got to, and a square picked off a blank map cannot promise that. Frontier was the only
            // thing reading it.
            //
            // Its own square or one beside it, so the hunt may still push a ring outwards rather than pace
            // what it already knows. Off by a dial, because a rule that narrows where the population may go
            // has to be answerable on a running shard.
            if (TroddenOnly && !Walked(map, where))
            {
                Untrodden++;

                continue;
            }

            if (Region.Find(where, map)?.IsPartOf<TownRegion>() == true)
            {
                continue;
            }

            // <b>And here, where every candidate passes, rather than only in the two named pickers.</b>
            // Paying and Noisy each ask this and the darts did not, so the one path that produces most of the
            // prowls on this shard was the one path that could not learn: 1009 prowls in forty-four minutes on
            // 08.09.2026, 676 of them over inside fifteen seconds, GOODS nought and GOLD -2840. A dart lands
            // on a rooftop, the road is refused, the square is written down where everybody can read it, and
            // the next dart lands on it again because nobody here was reading.
            //
            // Free, and it has to be: this is inside the sampling loop. One dictionary lookup, and arriving
            // anywhere clears the square again.
            if (BotRefused.Refusing(map, where))
            {
                Darted++;

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

            // <b>And whether whoever is going is strong enough for it, which is a wall and not a price.</b>
            // Patrick's order of 03.09.2026: below the threshold a bot does not go, whatever the ground is
            // worth. A bot in a company brings the company's strength, so the same square that refuses one
            // brawler accepts the four of them — which is the whole point of the rule and the reason it is
            // asked here, where the ground is chosen, rather than at the moment of stepping into it.
            if (!BotQuad.Dares(body, map, where))
            {
                // <b>Or bring people, which is the other half of the order.</b> Ground this bot cannot take
                // alone is still ground it may take with the bots standing beside it, and refusing it here
                // would leave the population permanently unable to work anywhere worth working. Nothing is
                // formed at this point — a proposer weighs and an undertaking acts — so the ground is
                // accepted and the company is raised by the prowl itself on its first beat.
                if (!BotQuad.Together(body, map, where, BotMuster.Reach))
                {
                    Overmatched++;

                    continue;
                }

                // <b>Somebody is already raising one for this square, so this bot is not.</b> Refused here
                // rather than by the errand, and that distinction is the whole of it: a proposer that skips
                // a candidate costs nothing, while an errand that fails on its first beat is taken again on
                // the next — 1728 of those inside five minutes when this same test lived one layer down.
                if (BotProwl.Raising(map, where))
                {
                    Claimed++;

                    continue;
                }

                needsCompany = true;
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
                bestNeedsCompany = needsCompany;
                company = needsCompany;

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

        // The same three tests every sampled candidate has to pass: somewhere else, not resting after
        // refusing somebody, and reachable.
        //
        // <b>The middle one was written down and never read.</b> BotProwl.Bend files a baulk on ground it
        // could not get through, and until 07.09.2026 the only list that consulted those marks was the one
        // companies are sent to. This picker asked nothing, so the square that had just refused four bots was
        // handed to a fifth - 618 prowls failed on "no way through" in fifty minutes, and every one of them
        // paid for a full path search first.
        if (Utility.InRange(body.Location, where, BotQuarry.Reach))
        {
            return Point3D.Zero;
        }

        if (BotQuad.Baulking(map, where) || BotRefused.Refusing(map, where))
        {
            Rested++;

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

        // The same tests as the noisy picker above, including the baulk, and for the same reason.
        if (Utility.InRange(body.Location, where, BotQuarry.Reach)
            || Region.Find(where, map)?.IsPartOf<TownRegion>() == true
            || BotReach.Ask(map, body.Location, where, BotArrival.Within(BotQuarry.Reach)) == BotReachVerdict.Sealed)
        {
            return Point3D.Zero;
        }

        if (BotQuad.Baulking(map, where) || BotRefused.Refusing(map, where))
        {
            Rested++;

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
    /// Candidate hunting grounds passed over because that square is resting after refusing somebody.
    ///
    /// Its own tally rather than folded into the others: this is the shard declining to spend a path search
    /// on ground it has already been turned away from, and if it ever grows to swallow the whole map that is
    /// a different fault from the one it was written for.
    /// </summary>
    public static long Rested { get; private set; }

    /// <summary>
    /// Whether a dart may only land on ground the population has stood on, or beside it.
    ///
    /// A dial, not a constant: it narrows where the population is willing to go, and anything that does that
    /// has to be answerable without a rebuild. Set it false and the darts go back to the blank map.
    /// </summary>
    public static bool TroddenOnly { get; set; } = true;

    /// <summary>Sampled grounds passed over because nobody has ever stood in that square or beside it.</summary>
    public static long Untrodden { get; private set; }

    /// <summary>
    /// Whether this square, or one of the eight around it, is ground a bot has actually stood in.
    ///
    /// The ring is what keeps the hunt able to grow: the middle alone would pace what the population already
    /// knows for ever, and one square out is a walk of at most <see cref="BotQuad.Side"/> tiles past the last
    /// place somebody proved a road to.
    /// </summary>
    private static bool Walked(Map map, Point3D where)
    {
        if (BotQuad.Trodden(map, where))
        {
            return true;
        }

        for (var dx = -1; dx <= 1; dx++)
        {
            for (var dy = -1; dy <= 1; dy++)
            {
                if ((dx != 0 || dy != 0)
                    && BotQuad.Trodden(map, new Point3D(where.X + dx * BotQuad.Side, where.Y + dy * BotQuad.Side, where.Z)))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Hunting grounds passed over for lying further from the asker than any road is searched for.
    ///
    /// Its own bucket, and it wants watching against <see cref="Stranded"/>: the day this reads high and
    /// Stranded reads high with it is the day the population has walked so far from home that nothing near
    /// it is on offer any more, and the answer to that is to send it home, not to widen this.
    /// </summary>
    public static long Distant { get; private set; }

    /// <summary>
    /// Sampled hunting grounds passed over for having refused somebody. Its own bucket rather than
    /// <see cref="Rested"/>'s: the two named pickers ask the same question about squares somebody chose on
    /// purpose, and pouring the darts into that number would hide which of the four candidate paths the
    /// saving came from.
    /// </summary>
    public static long Darted { get; private set; }

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

    /// <summary>
    /// How far a hunting ground may be from home before no road to it is ever searched for.
    ///
    /// <para>
    /// Not a preference: it is <see cref="BotPath.CeilingMs"/> divided by <see cref="BotPath.MsPerTile"/>,
    /// which is the shard's own statement of how much distance one search is funded to cross. Reading it
    /// rather than writing a second number down is the point — a copy of it here would drift from the
    /// original the first time either moved, and the whole of this defect was two numbers that had never
    /// been compared.
    /// </para>
    ///
    /// <para>
    /// <b>It is a price, not a wall, and the first version of this note said the opposite.</b> The evidence
    /// for "no road exists past here" — 85% of refused roads lying beyond the line — was gathered while
    /// every bot on the shard was being refused every step by an exhausted mount, so those failures belonged
    /// to the engine and not to the search. Tested properly the same afternoon, on a healthy shard, by
    /// raising this to 900 with a dial and watching for fifteen minutes:
    /// </para>
    ///
    /// <para>
    /// refused roads stayed flat at one or two a minute — so distance does <em>not</em> make roads vanish —
    /// while the searching bill went from 2191 searches at 3.36ms each with 8% partial, to 6325 at 13.36ms
    /// with <b>51% partial</b>: eleven times the game loop for the same work. So the clamp stays, and it
    /// stays as what it is: the distance past which a search stops being worth what it costs. Errands that
    /// are rare enough to pay that bill — scouting is one Baron a few times an hour — should not use it, and
    /// BotScout says so at length.
    /// </para>
    /// </summary>
    public static int Walkable
    {
        get => _walkable > 0 ? _walkable : Default;
        set => _walkable = value;
    }

    /// <summary>
    /// How far a dart may be thrown from the asker, when nothing has been dialled.
    ///
    /// <para>
    /// <b>Five hundred, and the derived 240 turned out to be a cage.</b> The derivation — CeilingMs over
    /// MsPerTile — is where the search budget stops growing, and using it here penned the whole population
    /// into a circle of that radius around wherever it was standing, which is to say around home. Measured
    /// 08.09.2026: 16151 candidates thrown away in twenty-five minutes as too far, and every target the
    /// population chose that hour fell inside x 1200-1680, y 1230-1710 — the circle, drawn on the map by the
    /// bots themselves. Patrick asked why they never go south to the marsh; that is the answer.
    /// </para>
    ///
    /// <para>
    /// Five hundred is <c>Roam / 2</c>, which is what the dart sampler was written to throw at in the first
    /// place, so this stops being a second opinion about distance and goes back to being one. It is not free
    /// — at 900 the searching bill went from 3.36ms and 8% partial to 13.4ms and 51% — so it stays a dial,
    /// and the two numbers to watch when moving it are the average search cost and the partial share in
    /// "Getting about".
    /// </para>
    /// </summary>
    public static int Default { get; set; } = 500;

    private static int _walkable;

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

    /// <summary>Grounds passed over because whoever asked was not strong enough for them.</summary>
    public static long Overmatched { get; private set; }

    /// <summary>Grounds passed over because another bot is already raising a company for them.</summary>
    public static long Claimed { get; private set; }

    /// <summary>Times a hunting ground was picked because it is ground that has hurt somebody.</summary>
    public static long Sought { get; private set; }

    public static string Describe() =>
        $"{Sworn} answers went to classes that only defend; {Quiet} hunting grounds passed over as too quiet (above {BotQuad.TooQuiet:F2}), {Sought} picked for having hurt somebody (at or below {BotQuad.Wanted:F2}), {Stranded} hunters left with nowhere to walk at all because every ground they looked at was too quiet, {Overmatched} grounds passed over for asking more strength than whoever looked had, {Claimed} for somebody already raising a company for them, {Overrun} quarry passed over for the crowd already round it, {Rested} named grounds passed over as resting after refusing somebody and {Darted} sampled ones, {Distant} further from the asker than {Walkable} tiles, which is as far as a road is ever searched for, {Untrodden} on squares nobody has ever stood in or beside, {BotProwl.Baulked} prowls given up for getting no nearer, {BotProwl.Raised} companies raised for ground one bot could not take, {BotProwl.Unraised} given up for not raising one";

    public static void Forget()
    {
        _saidNoQuarry = false;
        Sworn = 0;
        Rested = 0;
        Darted = 0;
        Distant = 0;
        Untrodden = 0;
        Quiet = 0;
        Stranded = 0;
        Overmatched = 0;
        Claimed = 0;
        Overrun = 0;
        Sought = 0;
    }
}
