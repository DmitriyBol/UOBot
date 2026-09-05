using System;
using System.Collections.Generic;
using Server.Items;
using Server.Mobiles;

namespace Server.BotAI.V2;

/// <summary>
/// Finding something worth fighting, and finding what it left behind.
///
/// <para>
/// <b>Every judgement here is already written.</b> <c>BotThreat</c> knows what fighting power is (how long a
/// thing lasts multiplied by how hard it hits), what counts as hostile, and what our side comes to including
/// whoever happens to be standing nearby. This file adds one question to that: of the things I could beat,
/// which is the biggest. Nothing about combat is re-decided.
/// </para>
///
/// <para>
/// <b>The biggest I can beat, not the nearest.</b> Distance is already priced by the appraisal's nearness
/// factor, so a hunter that preferred the closest rat would be paid twice for being lazy. And the whole shape
/// of the first version's death loop was fighting things it could not beat, so the test is the same tolerance
/// the flight decision uses — it must lose by the same arithmetic it would flee by.
/// </para>
///
/// <para>
/// <b>No memory of where the monsters are.</b> Unlike a vein or a shop, a creature walks and respawns, so a
/// remembered one is a lie within a minute. The sweep is fresh every time and that is the cost of this
/// proposer; which patches of ground pay is remembered instead, by the ledger, which is the right place for a
/// fact about ground.
/// </para>
/// </summary>
public static class BotQuarry
{
    /// <summary>
    /// How far a hunter looks for something to fight.
    ///
    /// Thirty tiles: about a screen and a half, far enough that a bot standing in town can see the field
    /// outside it, near enough that the walk is a walk rather than an expedition. Hunting further afield is a
    /// different mechanic and it is the one that killed the first version.
    /// </summary>
    public static int Reach { get; set; } = 30;

    /// <summary>
    /// How close something has to be before it is worth putting other work down for.
    ///
    /// <para>
    /// <b>The reach and this are two different questions and were being answered by one number.</b> "What
    /// could I go and fight" is a thirty-tile question — a bot standing in town can see the field outside it
    /// — but "is this worth abandoning a dig for" is not, and using the reach for both meant either a bot
    /// that ignored the ogre beside it for half a minute, or one that dropped everything for a rat three
    /// screens away. Twelve tiles: outside a bow's ten and well outside a caster's eight, so anything this
    /// close is already close enough to be a fight whether the bot agrees to it or not.
    /// </para>
    /// </summary>
    public static int Notice { get; set; } = 12;

    /// <summary>
    /// How much stronger than itself a bot will deliberately set out after.
    ///
    /// <para>
    /// <b>This number was upside down and it is why ogres were invisible.</b> The test read
    /// <c>power × Tolerance &gt; ours</c> — that is, a bot would only go after something it was one and a half
    /// times <em>stronger</em> than — while the standing decision it claimed to be copying,
    /// <see cref="BotThreat.Decide"/>, commits to anything up to one and a half times stronger than the bot.
    /// The two differ by a factor of two and a quarter, and an ogre sits squarely in the gap: 1080 against a
    /// bot's 1116, so the flight rule says fight and the hunting rule demanded 1620 and found nothing. The
    /// population spent its evenings on rats and sheep because the only things it was allowed to want were
    /// things it outclassed two to one.
    /// </para>
    ///
    /// <para>
    /// One, rather than the tolerance itself, and the difference is deliberate: going out looking for
    /// something is not the same act as refusing to abandon a fight already happening, and a bot that walks
    /// half a screen to pick a fight it can only just survive has no margin left for the second creature.
    /// At one, anything up to the bot's own strength is fair game — which is the ogre, and is not the
    /// graveyard spectre at 2120. Move it in <c>bot-hunt.json</c> if the population should be braver.
    /// </para>
    /// </summary>
    public static double Daring { get; set; } = 1.0;

    /// <summary>How near the corpse a bot has to be to go through it.</summary>
    public static int LootReach { get; set; } = 2;

    /// <summary>
    /// How long a claim on a quarry stands before anybody else may take it.
    ///
    /// <b>Short, and renewed by pursuit rather than granted once.</b> A claim is made when a bot decides to go
    /// for something — that is the whole point, since a claim made on contact comes too late to stop everybody
    /// converging — and it is refreshed on every beat the bot is still walking towards it or hitting it. So a
    /// bot that gets there keeps its claim for as long as the fight lasts, and one that wanders off, dies or
    /// changes its mind stops renewing and the thing is free again within the window.
    /// </summary>
    public static int ClaimMs { get; set; } = 45000;

    /// <summary>
    /// Who is fighting what.
    ///
    /// <para>
    /// <b>The one thing in this project that is keyed by serial rather than held on the bot</b>, and it has to
    /// be: it is a fact about a creature, and we do not own creatures. It is kept small by construction —
    /// entries go the moment the fight ends, and a stale one expires — rather than by a sweep.
    /// </para>
    /// </summary>
    private static readonly Dictionary<Serial, (Serial Hunter, long Tick)> _claims = [];

    /// <summary>
    /// Claims a quarry for this bot, or renews a claim it already holds. The first to set about something owns
    /// what comes off it — and, more to the point, is the only one who goes for it at all.
    /// </summary>
    public static void Claim(Mobile hunter, Mobile quarry)
    {
        if (hunter == null || quarry == null)
        {
            return;
        }

        if (!Held(quarry, out var by) || by == hunter.Serial)
        {
            _claims[quarry.Serial] = (hunter.Serial, Core.TickCount);
        }
    }

    /// <summary>Whether this bot may take what comes off this quarry: its own claim, or nobody's.</summary>
    public static bool Ours(Mobile hunter, Mobile quarry) =>
        hunter != null && quarry != null && (!Held(quarry, out var by) || by == hunter.Serial);

    /// <summary>The fight is over, however it ended. The claim goes with it.</summary>
    public static void Release(Mobile quarry)
    {
        if (quarry != null)
        {
            _claims.Remove(quarry.Serial);
        }
    }

    /// <summary>Whether somebody holds a standing claim on this, and who.</summary>
    private static bool Held(Mobile quarry, out Serial by)
    {
        by = Serial.Zero;

        if (!_claims.TryGetValue(quarry.Serial, out var claim))
        {
            return false;
        }

        if (Core.TickCount - claim.Tick >= ClaimMs)
        {
            _claims.Remove(quarry.Serial);

            return false;
        }

        by = claim.Hunter;

        return true;
    }

    /// <summary>
    /// How long a quarry nobody could reach is left alone.
    ///
    /// A creature can be perfectly visible and completely unreachable — across water, behind a fence, on a
    /// roof, on the far side of a wall — and the sweep that finds it has no idea. Without this the population
    /// rediscovers it every few seconds: one bot claims it, walks at it until the journey gives up, drops it,
    /// the claim lapses, and the next bot does the same. Two minutes is long enough for the thing to wander
    /// somewhere reachable and short enough that a temporary obstacle is forgiven.
    /// </summary>
    public static int ShunMs { get; set; } = 120000;

    /// <summary>
    /// How long something a whole company could not kill is left alone.
    ///
    /// <para>
    /// <b>Much longer than a walk that failed, because a different thing was learned.</b> A quarry nobody
    /// could reach may have wandered somewhere reachable a minute later; a quarry four bots stood round for
    /// ninety seconds without moving its health has told the population something about itself that two
    /// minutes does not undo. Left at two, the shard spent the evening of 23.08.2026 re-forming companies
    /// against the same wraith every two minutes — thirty-five attempts, no kill, and every bot involved
    /// bound to the Bound rung for the duration each time.
    /// </para>
    /// </summary>
    public static int HopelessMs { get; set; } = 900000;

    /// <summary>When each shunned quarry may be looked at again. The expiry, not the moment it was set.</summary>
    private static readonly Dictionary<Serial, long> _shunned = [];

    /// <summary>Nobody could get to it. Leave it alone for a while.</summary>
    public static void Shun(Mobile quarry) => Shun(quarry, ShunMs);

    /// <summary>Leave it alone for as long as whoever found that out thinks it is worth.</summary>
    public static void Shun(Mobile quarry, int ms)
    {
        if (quarry != null)
        {
            var until = Core.TickCount + ms;

            // A longer sentence is never shortened by a later, lesser one: the hunter's own two minutes
            // would otherwise overwrite a company's quarter of an hour the first time one bot walked past.
            if (!_shunned.TryGetValue(quarry.Serial, out var standing) || until - standing > 0)
            {
                _shunned[quarry.Serial] = until;
            }
        }
    }

    /// <summary>
    /// Whether this one is being left alone at the moment.
    ///
    /// <para>
    /// <b>The twin of <see cref="Crowded"/>, and it was hidden for the same reason and cost the same
    /// thing.</b> A fight that finds no way to its quarry writes this list — see <c>BotSlay.Bend</c> — and
    /// until now only the hunt's own target-picking ever read it. So a bot with something unreachable
    /// hitting it was handed that fight by the defender, failed to path to it, and was handed it again five
    /// seconds later: Gerda ran that circle eleven times on one skeleton, all of them printing "no way
    /// through to a skeleton", none of them able to remember the last one.
    /// </para>
    ///
    /// <para>
    /// Unlike a crowd, this one is honestly shard-wide: "nobody could get to it" is as true of a company as
    /// of one bot, which is exactly the distinction the two lists were separated to keep.
    /// </para>
    /// </summary>
    public static bool Shunned(Mobile quarry)
    {
        if (!_shunned.TryGetValue(quarry.Serial, out var until))
        {
            return false;
        }

        // Subtraction, never a bare comparison: these counters start enormous and wrap. See dev-docs/tick-counts.md.
        if (Core.TickCount - until < 0)
        {
            return true;
        }

        _shunned.Remove(quarry.Serial);

        return false;
    }

    /// <summary>
    /// How long something that turned out to be standing in a crowd is left to lone hunters.
    ///
    /// Short, because a crowd is the most temporary fact on this list: creatures wander apart, and the one
    /// that made the odds hopeless may be dead by the time this lapses.
    /// </summary>
    public static int CrowdMs { get; set; } = 120000;

    private static readonly Dictionary<Serial, long> _crowded = [];

    /// <summary>
    /// How long a fight stays handed over before a walking bot will stop for it again.
    ///
    /// <para>
    /// Ten seconds, and it is short because it is not a verdict about the creature — it is a note that this
    /// bot has already offered the auction this fight once and the auction did nothing with it.
    /// </para>
    /// </summary>
    public static int HandMs { get; set; } = 10000;

    private static readonly Dictionary<Serial, long> _handed = [];

    /// <summary>
    /// A walking bot stopped for this creature so that something else could deal with it.
    ///
    /// <para>
    /// <b>A hand-over with no receiver is a loop, and this is the note that makes it stop after one lap.</b>
    /// A prowl ends the moment something picks a fight with the bot, on the reasoning that a bot out looking
    /// for a fight does not get to walk past one. But the auction only offers a fight the bot would
    /// <em>choose</em>, and something that has chosen the bot is very often not that — so the errand ended,
    /// nothing took the fight, the auction handed the same errand straight back, and it ended again. On
    /// 02.09.2026 between 23:18 and 23:31 that came to 345 prowls taken and 189 of them ending this way,
    /// with no fight recorded in between at all: Edda went round it eight times inside three seconds.
    /// </para>
    ///
    /// <para>
    /// So the offer is made once. If the same creature is still on the bot when the walk resumes, walking on
    /// <em>is</em> the answer — a bot nobody will fight for is a bot that should be leaving.
    /// </para>
    /// </summary>
    public static void Hand(Mobile quarry)
    {
        if (quarry != null)
        {
            _handed[quarry.Serial] = Core.TickCount + HandMs;
        }
    }

    /// <summary>Whether this fight has already been offered to the auction and left there.</summary>
    public static bool Handed(Mobile quarry)
    {
        if (quarry == null || !_handed.TryGetValue(quarry.Serial, out var until))
        {
            return false;
        }

        if (Core.TickCount - until < 0)
        {
            return true;
        }

        _handed.Remove(quarry.Serial);

        return false;
    }

    /// <summary>
    /// One bot walked to it and found the odds against it. Kept away from lone hunters — and from nobody else.
    ///
    /// <para>
    /// <b>A list of its own, and merging it with <see cref="Shun"/> cost a whole evening's companies.</b> The
    /// two facts sound alike and are opposite in their consequence. "Nobody could get to it" is true of a
    /// company as much as of one bot, so it belongs on the shared list. "One bot is outnumbered around it" is
    /// the definition of what a <em>company</em> is for — and put on the shared list it hid, from
    /// <see cref="Company"/>, exactly the creatures <see cref="BotMuster"/> exists to call companies against.
    /// Squads formed fifteen times in twenty minutes before, and nought times in the ten minutes after. One
    /// list cannot answer two questions.
    /// </para>
    /// </summary>
    public static void Crowd(Mobile quarry)
    {
        if (quarry != null)
        {
            _crowded[quarry.Serial] = Core.TickCount + CrowdMs;
        }
    }

    /// <summary>
    /// Whether one bot should be left to find something else.
    ///
    /// <para>
    /// <b>Written by the fight and read by only one of the two things that pick targets, which is how a
    /// note becomes useless.</b> Choosing quarry to hunt asked this; going to somebody's aid did not, and
    /// nothing said so. On the night of 25.08.2026 that split the population's failures down the middle: on
    /// one and the same creature, at one and the same time, the hunt gave up 7 times and rescue 103 — and
    /// across the night rescue was 70 to 96 per cent of every failure on the shard, with three bots of
    /// sixteen doing nothing else for an hour. A fact worth recording is worth recording for everybody who
    /// acts on it.
    /// </para>
    /// </summary>
    public static bool Crowded(Mobile quarry)
    {
        if (!_crowded.TryGetValue(quarry.Serial, out var until))
        {
            return false;
        }

        if (Core.TickCount - until < 0)
        {
            return true;
        }

        _crowded.Remove(quarry.Serial);

        return false;
    }

    /// <summary>
    /// What a kind of creature has actually paid, per kill, averaged over every one this population has
    /// brought down.
    ///
    /// <para>
    /// <b>Strength is not worth, and picking the strongest thing you can beat quietly assumes it is.</b> A
    /// bull is tougher than a lizardman and carries nothing; a goat is tougher than a rat and carries nothing
    /// either. Left to raw power the population hunted the local farms — seven of nine kills in one stretch
    /// were sheep, goats, pigs and eagles — earned skill, and brought in no gold at all, which matters
    /// because a monster's purse is the only place gold enters this world.
    /// </para>
    ///
    /// <para>
    /// Measured rather than declared, like everything else here: nothing anywhere lists what a creature
    /// carries, and a table of it would be wrong the first time somebody edits a loot pack. An untried kind is
    /// given <see cref="Untried"/> so that it gets tried — which is the whole of exploration, and the same
    /// trick the ledger plays with unfamiliar ground.
    /// </para>
    /// </summary>
    private static readonly Dictionary<Type, (long Gold, int Kills)> _paid = [];

    /// <summary>
    /// What a kind nobody has killed yet is assumed to be worth.
    ///
    /// Above a farm animal's measured nothing and below a real monster's purse, so an unknown thing is worth
    /// one look and stops being chosen if that look comes back empty.
    /// </summary>
    public static double Untried { get; set; } = 25.0;

    /// <summary>Notes what came off one of these. Called by whoever went through the corpse.</summary>
    public static void Paid(Type kind, int gold)
    {
        if (kind == null)
        {
            return;
        }

        _paid.TryGetValue(kind, out var sofar);

        _paid[kind] = (sofar.Gold + Math.Max(0, gold), sofar.Kills + 1);
    }

    /// <summary>
    /// What a standing order for something a carcass yields adds to that creature's worth.
    ///
    /// <para>
    /// Two hundred, which is well above what any ordinary kill on this island pays, and deliberately so: the
    /// point is not to nudge the odds but to turn the population's attention. When the board is asking for
    /// feathers, birds stop being the worthless thing a hunter walks past.
    /// </para>
    /// </summary>
    public static double Bounty { get; set; } = 200.0;

    /// <summary>Kills chosen because the board was asking for what the carcass carries. For the summary.</summary>
    public static long Sent { get; private set; }

    /// <summary>
    /// How much this creature is worth over its purse because somebody has put money down for what it is
    /// made of.
    ///
    /// <para>
    /// <b>This is the last link of the chain Patrick asked for, and without it the chain is open at both
    /// ends.</b> An archer runs low and puts arrows on the board; a fletcher reads the board and can make
    /// them out of wood and feathers; wood is money, and a feather is a thing that only exists because
    /// somebody killed a bird. Nothing anywhere connected "the board wants feathers" to "go and hunt
    /// something with feathers on it", so the fletchers would have stood idle beside a full order book while
    /// the hunters walked past chickens all day looking for something that paid.
    /// </para>
    ///
    /// <para>
    /// Asked of the engine's own carcass properties rather than of a table of creatures — <c>Feathers</c>,
    /// <c>Hides</c> and <c>Wool</c> are on <c>BaseCreature</c> and every animal on the shard fills them in.
    /// A table would have been right on the day it was written and quietly wrong after the first new
    /// creature.
    /// </para>
    /// </summary>
    public static double Sought(BaseCreature creature)
    {
        if (creature == null || BotAuction.Wants.Count == 0)
        {
            return 0.0;
        }

        var worth = 0.0;

        // <b>A want for arrows is a want for feathers, one step on.</b> The woodcutter's proposer has read it
        // that way since the day it was written — "logs directly, or arrows, which are logs one step further
        // on, and which is what an archer actually asks for" — and this side never learned the same sentence.
        // It stopped mattering only while archers could not ask: on the evening of 04.09.2026 they began to,
        // and 179 arrow orders stood on the board at 18:02 while this counter had been frozen at 122 for
        // three half-hourly readings. An archer asking for arrows is the demand; nobody was carrying it back
        // to the one act that puts a feather into the world.
        if (creature.Feathers > 0 && (Demanded(typeof(Feather)) || Demanded(typeof(Arrow))))
        {
            worth += Bounty;
        }

        if (creature.Hides > 0 && Demanded(typeof(Hides)))
        {
            worth += Bounty;
        }

        if (creature.Wool > 0 && Demanded(typeof(Wool)))
        {
            worth += Bounty;
        }

        if (worth > 0.0)
        {
            Sent++;
        }

        return worth;
    }

    /// <summary>Whether anybody has money down on the board for this, right now.</summary>
    private static bool Demanded(Type kind)
    {
        var wants = BotAuction.Wants;

        for (var i = 0; i < wants.Count; i++)
        {
            if (wants[i].IsOpen && wants[i].Kind == kind)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>What one of these is worth, on the evidence. <see cref="Untried"/> when there is none.</summary>
    public static double Pays(Type kind) =>
        kind != null && _paid.TryGetValue(kind, out var known) && known.Kills > 0
            ? (double)known.Gold / known.Kills
            : Untried;

    /// <summary>Everything forgotten. The world these serials belong to is being replaced.</summary>
    public static void Forget()
    {
        _claims.Clear();
        _shunned.Clear();
        _crowded.Clear();
        _paid.Clear();
        Sent = 0;
    }

    /// <summary>What the population has learned about who is worth fighting. For the summary.</summary>
    public static string Describe()
    {
        var kinds = 0;
        var paying = 0;

        foreach (var (_, known) in _paid)
        {
            kinds++;

            if (known.Gold > 0)
            {
                paying++;
            }
        }

        return $"{kinds} kinds of creature killed and priced, {paying} of them worth the trouble";
    }

    /// <summary>
    /// The biggest thing within reach that this bot could take, or null.
    ///
    /// <para>
    /// <b>Judged against what this bot can do alone, and that had to change when claims arrived.</b> It used
    /// to be judged against <c>OurPower</c> — everybody able standing nearby — which was a fair sum while any
    /// number of bots might pile onto the same creature. Claims ended that on purpose: one bot goes, and the
    /// others are pointed at something else. So the company that the sum was counting is company that will
    /// not be there, and the arithmetic was writing cheques the fight could not cash — a spectre at 2120
    /// against three bots' 3348 reads as winnable and is then fought by one bot worth 1116. Five deaths in
    /// nine minutes, all of them at the graveyard, all of them alone against something chosen for a crowd.
    /// </para>
    ///
    /// <para>
    /// Standing beside somebody still helps and still costs nothing to arrange — <see cref="BotThreat.Decide"/>
    /// sums the neighbours when deciding whether to stand and fight something that has already attacked, and
    /// there the company is real, because it is being attacked too. What a bot may go looking for is a
    /// different question, and it is answered by what it can beat by itself.
    /// </para>
    /// </summary>
    public static BaseCreature Best(Mobile bot, int range)
    {
        var map = bot?.Map;

        if (map == null || map == Map.Internal)
        {
            return null;
        }

        var ours = BotThreat.Power(bot);

        if (ours <= 0.0)
        {
            return null;
        }

        BaseCreature best = null;
        var bestPower = 0.0;
        var bestPays = -1.0;

        foreach (var creature in map.GetMobilesInRange<BaseCreature>(bot.Location, range))
        {
            if (!BotThreat.Hostile(bot, creature) || !BotPopulation.Within(map, creature.Location))
            {
                continue;
            }

            // Somebody else got to it first. This is what stops six bots converging on one skeleton and five
            // of them arriving at an empty corpse — which is not merely wasteful, it is five bots that spent a
            // walk each to be paid nothing, and the ledger learning that hunting here does not pay.
            //
            // Crowded is asked here and nowhere else: a lone hunter has already walked to this one and found
            // the numbers against it. A company is still offered the same creature, and should be — see Crowd.
            if (!Ours(bot, creature) || Shunned(creature) || Crowded(creature))
            {
                continue;
            }

            var power = BotThreat.Power(creature);

            // Measured the same way round as the flight decision, which it used not to be: there the ratio
            // is the creature's power over ours, and here it was ours over the creature's. See
            // <see cref="Daring"/> — a bot must not walk towards something it would immediately run away
            // from, and it must not refuse everything it could actually take either.
            if (power <= 0.0 || power > ours * Daring)
            {
                continue;
            }

            // What it pays decides; how big it is only breaks a tie. That ordering is the whole of the fix:
            // among things a bot can beat, the useful question is which one carries something.
            var pays = Pays(creature.GetType()) + Sought(creature);

            if (best != null && (pays < bestPays || pays == bestPays && power <= bestPower))
            {
                continue;
            }

            best = creature;
            bestPower = power;
            bestPays = pays;
        }

        return best;
    }

    /// <summary>
    /// Whether there is something here this bot should be setting about rather than walking away from.
    ///
    /// <para>
    /// <b>One implementation, because two of them deadlocked the bot that had them.</b> The hunter refuses to
    /// offer a fight to a bot that is already outnumbered and offers it a walk instead; the walk ends itself
    /// the moment anything worth fighting is in reach. Written separately, those two are a bot that is told
    /// to leave and told there is no reason to leave, several times a second — measured at ten round trips a
    /// second in the log, every one of them a finished undertaking and a fresh auction. The rule is the same
    /// rule in both places, so it lives in one.
    /// </para>
    /// </summary>
    public static bool Worthwhile(Mobile bot) =>
        bot != null
        && BotThreat.Decide(bot, BotMobile.NoticeRange) != BotStand.Outmatched
        && Best(bot, Reach) != null;

    /// <summary>
    /// The biggest thing within reach that this bot could <b>not</b> take alone but a company standing here
    /// could, or null.
    ///
    /// <para>
    /// <b>The gap <see cref="Best"/> deliberately refuses is exactly what this returns.</b> That one judges
    /// against what a bot can do by itself, and it has to: claims send one bot at a thing, so counting the
    /// neighbours there was writing cheques the fight could not cash. But the creatures that fall in the gap
    /// — too much for one, comfortable for four — are the ones worth the most and the ones this population
    /// has never once fought. Nothing was wrong with refusing them one at a time. What was missing was
    /// anybody asking the other question.
    /// </para>
    ///
    /// <para>
    /// Sorted by strength rather than by what the kind has paid, which is the opposite of <see cref="Best"/>
    /// and for a plain reason: a company is expensive — five bots stop doing everything else — so the case
    /// for calling one is that the thing is big, and a rat that happens to have a good record is not a case
    /// for anything. What it turns out to carry is measured on the way past like everything else.
    /// </para>
    /// </summary>
    public static BaseCreature Company(Mobile bot, int range) => Company(bot, range, out _);

    /// <summary>Why no company could be called, when none could. See <see cref="Company"/>.</summary>
    public enum CompanyRefusal
    {
        /// <summary>One was found. Nothing was refused.</summary>
        None,

        /// <summary>Nobody of ours is near enough to make a company at all.</summary>
        Alone,

        /// <summary>Everything here is already one bot's work.</summary>
        AllSmall,

        /// <summary>Something is here and it is beyond what everybody standing here could take.</summary>
        AllTooBig,

        /// <summary>Nothing hostile is here, or it is claimed or being left alone.</summary>
        Nothing
    }

    /// <summary>
    /// The same, saying which gate closed when it hands back nothing.
    ///
    /// <para>
    /// <b>Three different findings shared one silence, and the middle one was the answer.</b> The plain
    /// overload returns null for "there is nobody with me", "everything here is small" and "the only thing
    /// here would kill all of us", and <see cref="BotMuster"/> filed all three under <em>found nothing beyond
    /// one bot</em> — nine hundred and eighty-one of them in twenty minutes, which I read out as proof that
    /// the ground held nothing worth a company. It was not proof of anything. The very first check returns
    /// before a single creature is looked at, so a field full of ogres and a field full of rats produce the
    /// identical number, and the population walked past ogres all evening while the log agreed there was
    /// nothing to fight.
    /// </para>
    /// </summary>
    public static BaseCreature Company(Mobile bot, int range, out CompanyRefusal why)
    {
        why = CompanyRefusal.Nothing;

        var map = bot?.Map;

        if (map == null || map == Map.Internal)
        {
            return null;
        }

        var alone = BotThreat.Power(bot);

        if (alone <= 0.0)
        {
            return null;
        }

        var together = BotThreat.OurPower(bot, range);

        // Nobody of ours is near enough to be counted. There is no company to call.
        if (together <= alone)
        {
            why = CompanyRefusal.Alone;

            return null;
        }

        BaseCreature best = null;
        var bestPower = 0.0;
        var small = 0;
        var big = 0;

        foreach (var creature in map.GetMobilesInRange<BaseCreature>(bot.Location, range))
        {
            if (!BotThreat.Hostile(bot, creature) || !BotPopulation.Within(map, creature.Location))
            {
                continue;
            }

            if (!Ours(bot, creature) || Shunned(creature))
            {
                continue;
            }

            var power = BotThreat.Power(creature);

            // Already one bot's work. Calling four more to a thing one of them was going to take anyway is
            // how the first version ended up with six bots declaring a band against a skeleton.
            if (power <= alone * Daring)
            {
                small++;

                continue;
            }

            // Beyond the company too. The tolerance is the one the standing decision uses, because that is
            // the decision the squad will actually be making once it is in the fight.
            if (power > together * BotThreat.Tolerance)
            {
                big++;

                continue;
            }

            if (power <= bestPower)
            {
                continue;
            }

            best = creature;
            bestPower = power;
        }

        if (best != null)
        {
            why = CompanyRefusal.None;
        }
        else if (big > 0)
        {
            why = CompanyRefusal.AllTooBig;
        }
        else if (small > 0)
        {
            why = CompanyRefusal.AllSmall;
        }

        return best;
    }

    /// <summary>
    /// The corpse of this creature, if it is lying where it fell.
    ///
    /// Found by looking rather than by being told, because the engine's kill hook lives on the creature and we
    /// do not own creatures. Cheap because the place is known: two tiles around where the thing was standing
    /// the last time it was alive.
    /// </summary>
    public static Corpse Remains(Map map, Point3D where, Mobile fallen)
    {
        if (map == null || map == Map.Internal || fallen == null)
        {
            return null;
        }

        foreach (var item in map.GetItemsInRange(where, LootReach))
        {
            if (item is Corpse corpse && !corpse.Deleted && corpse.Owner == fallen)
            {
                return corpse;
            }
        }

        return null;
    }
}
