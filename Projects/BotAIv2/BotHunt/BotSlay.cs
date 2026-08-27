using System;
using Server.Logging;
using System.Collections.Generic;
using Server.Items;
using Server.Mobiles;

namespace Server.BotAI.V2;

/// <summary>
/// Close, fight, go through what is left. The fighter's chain, and the first work in this project that brings
/// new gold into the world.
///
/// <para>
/// <b>Its stages are its own business, and the third one is the point of it.</b> A hunt that ends when the
/// thing falls over leaves a bot standing next to everything it earned. Mining learned this first — an
/// undertaking that ends at the vein leaves a bot underground holding rock — and it is the same lesson: the
/// work is not the fight, it is the fight <em>and getting paid for it</em>.
/// </para>
///
/// <para>
/// <b>The fighting itself is entirely the engine's.</b> Setting <c>Combatant</c> starts a server-side timer
/// that swings the weapon, rolls to hit, applies damage and wears the blade down — no client involved and
/// nothing simulated. So this file never decides a blow. What it decides is when to stop, and that is the one
/// judgement it must not get wrong.
/// </para>
///
/// <para>
/// <b>It runs away, and it has to, because nothing above it will.</b> The rung for a bot that is losing has no
/// proposer, so the brain's answer to failing health is to hold on to whatever the bot is already doing — and
/// what this bot is already doing is being killed. The first version's sharpest lesson was that flight must
/// outrank everything social; here it also has to outrank the undertaking's own stubbornness. Giving up marks
/// the place with caution, which is exactly the right record: this patch of ground kills me.
/// </para>
/// </summary>
public sealed class BotSlay : BotDeed
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotSlay));

    /// <summary>The ledger's key.</summary>
    public const string Trade = "hunt";

    /// <summary>
    /// What a fight is reckoned at per minute before experience corrects it.
    ///
    /// <para>
    /// Deliberately conservative. A fight ought to pay well — coin off the corpse plus real weapon skill,
    /// which at five hundred a point dominates the coin — and a first guess at what a monster carries is a
    /// guess about content nobody here has measured. Sixty puts it level with writing scrolls rather than
    /// above everything, so the ledger gets to raise it from evidence instead of the whole population
    /// stampeding into the field on the strength of a number I chose.
    /// </para>
    /// </summary>
    public static double Prior { get; set; } = 60.0;

    /// <summary>How long one hunt is expected to take, walk and all.</summary>
    public static double WorkMinutes { get; set; } = 3.0;

    /// <summary>
    /// The share of its health at which a hunter gives the fight up.
    ///
    /// <para>
    /// Forty per cent, and the number matters less than that it exists at all. The first version died four
    /// hundred and forty-three times in a night, a hundred and four of them the same bot in the same tile,
    /// because nothing ever decided that a fight was going badly. A bot that walks away at forty per cent
    /// keeps its gear, its skills and the three minutes that dying costs the ledger.
    /// </para>
    /// </summary>
    public static double FleeAt { get; set; } = 0.4;

    /// <summary>How full a pack may get with loot before the rest is left on the corpse.</summary>
    public static double FillFraction { get; set; } = 0.8;

    /// <summary>
    /// How often, mid-fight, a bot works out whether it is losing on numbers rather than on health.
    ///
    /// A second. The check is a spatial sweep and this runs on the bot's own turn, which comes round two to
    /// five times a second — so unthrottled it would be the most expensive thing a fighting bot does, for an
    /// answer that cannot change meaningfully inside a swing.
    /// </summary>
    public static int OddsMs { get; set; } = 1000;

    /// <summary>
    /// How long one bot keeps at a quarry whose health will not fall.
    ///
    /// <para>
    /// <b>A squad has had this exit since it was written and a lone hunter never had one at all.</b> The
    /// company gives up after ninety seconds without the target's health moving, and caps a fight at four
    /// minutes, because anything it cannot kill would otherwise hold five bots for ever — that lesson is
    /// written out at length on <see cref="BotSquad.NoProgressMs"/>, and it was never carried across to the
    /// undertaking that holds <em>one</em> bot. So the only ways out of a solo fight were victory, forty per
    /// cent health, or being outnumbered; a quarry that could neither hurt the bot nor be hurt by it matched
    /// none of them. Joss took a zombie at 21:08:51 on 24.08.2026 and was still holding it twenty-two minutes
    /// later, having finished one piece of work all evening — not idle, bound, and reading in every summary
    /// as a bot busy hunting.
    /// </para>
    ///
    /// <para>
    /// Shorter than the squad's, because one bot has less to bring: what five of them cannot shift in ninety
    /// seconds, one of them will not shift in that time either.
    /// </para>
    /// </summary>
    public static int NoProgressMs { get; set; } = 45000;

    /// <summary>
    /// The ceiling on one solo hunt, walking and fighting together.
    ///
    /// Measured from the moment the undertaking was taken rather than from first contact, because the other
    /// way a bot can spend it all walking and still be entitled to the whole of it again on arrival.
    /// </summary>
    public static int CapMs { get; set; } = 150000;

    /// <summary>
    /// How long a bot stands in a fight it cannot land a blow in before it gives the quarry up.
    ///
    /// <para>
    /// <b>"Near enough" and "able to hit it" are different questions, and only the first was ever asked.</b>
    /// Arrival is <c>InRange</c>, which is a flat box on X and Y; whether a blow may land is the engine's,
    /// and it wants line of sight in three dimensions. On a graveyard full of crypts those come apart
    /// constantly: a bot standing on a roof at z=20 is one tile from a skeleton on the ground below it,
    /// takes the fight, sets its combatant, and swings at nothing. Three of Merrick's fights on 24.08.2026
    /// ended at a minute with the target on a hundred per cent of its health, and every number in the line
    /// was correct — the distance, the combatant, the elapsed time. Nothing measured the one thing that was
    /// wrong.
    /// </para>
    ///
    /// <para>
    /// A few seconds rather than instantly, because a creature stepping behind a tombstone breaks the line
    /// for a moment and is not a reason to abandon anything.
    /// </para>
    /// </summary>
    public static int BlindMs { get; set; } = 6000;

    /// <summary>
    /// How long a shooter must have been standing before the engine will let the bow go off.
    ///
    /// <para>
    /// Not a choice — <c>BaseRanged.OnSwing</c> refuses the swing unless <c>LastMoveTime</c> is this old, and
    /// on this era that is a full second. It is written down here because a kite that does not know this
    /// number is a bot that moves for ever and never fires.
    /// </para>
    /// </summary>
    public static int StillMs { get; set; } = 1000;

    /// <summary>
    /// Slack on top of the stillness, to cover the walk itself and the beat it is decided on. A step is a
    /// fifth of a second at a run and the bot is asked for a decision two to five times a second, so a
    /// shooter that starts moving with exactly a second left arrives late and loses the shot it was
    /// repositioning for.
    /// </summary>
    public static int KiteSlackMs { get; set; } = 700;

    /// <summary>
    /// How far above the ground a pair of eyes sits, for asking whether a place a bot is not standing in yet
    /// could see the quarry.
    ///
    /// <para>
    /// Not a choice either: <c>Map.LineOfSight</c> raises both ends by this before tracing, and a line traced
    /// from the floor instead answers a different question — it clips on the first step of ground it crosses
    /// and reports every open field as blind. Taken from the engine so the two agree.
    /// </para>
    /// </summary>
    private const int Eye = 14;

    /// <summary>
    /// How close something may come before a bot that shoots gives ground.
    ///
    /// <para>
    /// <b>An archer had no reason to stand anywhere in particular, so it stood in arm's reach.</b> A bow
    /// carries ten tiles and a crossbow eight, and the approach was written as "beside it" — one tile — so a
    /// bot walked the whole way in and then shot from a distance at which its bow is worth nothing and
    /// everything can hit it back. Watched from a client it reads exactly as what it is: archers firing point
    /// blank.
    /// </para>
    ///
    /// <para>
    /// Three tiles, because two is where a creature is already swinging and four is close enough that a step
    /// backwards is not worth the beat it costs.
    /// </para>
    /// </summary>
    public static int TooClose { get; set; } = 3;

    /// <summary>
    /// Where a bot with this reach would rather stand: comfortably inside its own range and outside anybody
    /// else's. Two short of the weapon's own maximum, so a target drifting a tile does not put it out of the
    /// fight. For a melee weapon this is one, which is where it always was.
    /// </summary>
    private static int Standoff(int reach) => Math.Max(1, reach - 2);

    private enum Leg
    {
        Close,
        Fight,
        Spoils
    }

    private readonly BaseCreature _quarry;

    private readonly Map _map;

    private readonly Point3D _found;

    private readonly SkillName _trains;

    private Leg _leg;

    private Point3D _fell;

    private int _made;

    private int _taken;

    private int _coins;

    private int _casts;

    /// <summary>How much leather came off the last thing this undertaking killed.</summary>
    private int _hides;

    /// <summary>
    /// The spot this bot is presently backing away to, held until it gets there.
    ///
    /// <para>
    /// <b>Recomputed every beat, it was an order that never survived long enough to be obeyed.</b> The
    /// retreat point is worked out from where the quarry is standing, and the quarry moves — so every beat
    /// produced a slightly different destination, BotWill saw an order that did not match the last one,
    /// rebased the journey and threw the plan away. The bot shuffled on the spot for as long as the fight
    /// lasted. That is why a mage with something in its face never seemed to cast: casting comes after the
    /// retreat in this method, and the retreat never finished.
    /// </para>
    ///
    /// <para>
    /// Kept until it is reached or goes bad, so the order is identical beat after beat and the journey lives.
    /// The same lesson as the archer's line-seeking walk, and the same one BotWill's own comment gives beside
    /// the rebase: an undertaking that re-words its walk every beat never gets anywhere.
    /// </para>
    /// </summary>
    private Point3D _backing;

    /// <summary>Whether a cast of ours is waiting for something to be pointed at.</summary>
    private bool _aiming;

    private bool _cast;

    private long _castTick;

    /// <summary>When the odds were last worked out, and whether they ever have been. See <see cref="OddsMs"/>.</summary>
    private long _oddsTick;

    private bool _weighed;

    private bool _complained;

    /// <summary>The lowest health this quarry has been seen at, and when that was. See <see cref="NoProgressMs"/>.</summary>
    private int _lowest = int.MaxValue;

    /// <summary>When the quarry was last somewhere a blow could actually reach. See <see cref="BlindMs"/>.</summary>
    private long _seenTick;

    /// <summary>When this bot's hands were last looked at. See <see cref="BotArms.EveryMs"/>.</summary>
    private long _armedTick;

    private long _progressTick;

    private long _tookTick;

    public BotSlay(BaseCreature quarry, SkillName trains)
    {
        _quarry = quarry;
        _map = quarry.Map;
        _found = quarry.Location;
        _fell = quarry.Location;
        _trains = trains;

        // Seeded from a real tick, never left at zero: these counters can start enormous and wrap.
        _tookTick = Core.TickCount;
        _progressTick = Core.TickCount;
        _seenTick = Core.TickCount;
        _armedTick = Core.TickCount - BotArms.EveryMs;
    }

    public override string Kind => Trade;

    public override Map Map => _map;

    /// <summary>
    /// Where it was found, for the whole life of the undertaking — never where it has wandered to since.
    ///
    /// This is what the ledger files the outcome under, and the thing being learned is whether hunting this
    /// patch of ground pays. A quarry that led a bot half a mile away and died there would otherwise teach it
    /// that the place it ended up is rich in monsters.
    /// </summary>
    public override Point3D Where => _found;

    public override double Expects => Prior;

    public override double Minutes => WorkMinutes;

    /// <summary>
    /// Whatever this bot actually swings, handed in by the proposer out of the bond.
    ///
    /// The class offered six blades and the roll picked one, <em>together with the skill that swings it</em> —
    /// so the answer is a fact about this bot, not about its class. Empty hands are the brawler's whole point,
    /// and wrestling is what those train.
    /// </summary>
    public override SkillName? Trains => _trains;

    public override int Outlay => 0;

    /// <summary>Mostly coin, and this is the only work in the project of which that has ever been true.</summary>
    public override double Coin => 1.0;

    /// <summary>Goods off the corpse that went to the market. The gold is counted as gold.</summary>
    public override int Made => _made;

    /// <summary>
    /// A creature this close will not keep, so this offer may jump the dwell.
    ///
    /// <para>
    /// <b>The whole of the answer to "why did it run straight past that".</b> A bot that has taken something
    /// on is protected from changing its mind for half a minute, and rightly — but the protection was being
    /// read as "do not look", so an ogre could walk across a miner's path and the miner would not be asked
    /// about it until it had finished the vein. A place keeps for half a minute. A creature does not: it is
    /// somewhere else, claimed, or standing over the bot by then.
    /// </para>
    ///
    /// <para>
    /// Bounded by <see cref="BotQuarry.Notice"/> rather than by the reach, because this is the "already in my
    /// lap" question and not the "worth a walk" one, and only while the fight has not started — once it has,
    /// the deed is the bot's own work and needs no help jumping anything.
    /// </para>
    /// </summary>
    public override bool Pressing(IBotWilful bot)
    {
        var body = bot?.Self;

        return body != null && !Down() && body.InRange(_quarry.Location, BotQuarry.Notice);
    }

    public override string Stage => _leg switch
    {
        Leg.Close => $"after {_quarry?.Name ?? "something"}",
        Leg.Fight => _casts > 0
            ? $"fighting {_quarry?.Name ?? "something"} ({_casts} spells)"
            : $"fighting {_quarry?.Name ?? "something"}",
        _ => _taken > 0 ? $"took {_taken} things and {_coins}gp" : "going through the corpse"
    };

    public override BotDoing Advance(IBotWilful bot)
    {
        var body = bot?.Self;

        if (body == null || _map == null || _map == Map.Internal)
        {
            return BotDoing.Failed("no body");
        }

        return _leg switch
        {
            Leg.Close => Closing(bot, body),
            Leg.Fight => Fighting(bot, body),
            _ => Looting(bot, body)
        };
    }

    private BotDoing Closing(IBotWilful bot, Mobile body)
    {
        if (Down())
        {
            _leg = Leg.Spoils;

            return Looting(bot, body);
        }

        // The ceiling applies to the chase as well, and the chase is where it is most needed: a quarry that
        // walks away as fast as the bot walks after it never fails a journey and never comes into reach, so
        // nothing else here would ever end the undertaking.
        if (Core.TickCount - _tookTick >= CapMs)
        {
            BotQuarry.Crowd(_quarry);

            return BotDoing.Failed($"could not catch {_quarry.Name}");
        }

        _fell = _quarry.Location;

        // A caster's reach is its spells, not the stick in its hands.
        var reach = Math.Max(body.Weapon?.MaxRange ?? 1, BotStrike.Can(body) ? BotStrike.Range : 0);
        var standoff = Standoff(reach);

        if (!body.InRange(_quarry.Location, standoff))
        {
            // Following the thing rather than the place it was: a quarry walks, and a plan aimed at where it
            // used to be is a bot arriving somewhere empty. Stopped at the weapon's own distance rather than
            // at arm's length — walking a bow into melee throws away the only advantage it has.
            return BotDoing.Walk(_map, _quarry, BotArrival.Within(standoff), $"after {_quarry.Name}");
        }

        // <b>The blind clock starts here, and starting it in the constructor cost two nights.</b> It is
        // seeded when the undertaking is created but only ever read once the fight has begun — and between
        // those two moments sits the whole walk to the quarry, which a hunt looking fifty tiles out routinely
        // spends more than BlindMs on. So a bot arrived with its grace period already gone and was judged
        // blind on the first beat of the fight, before it had been given one chance to look.
        //
        // That is why the failures all read the standoff distance and never a tile less: there was no grace
        // left to close the distance in, and the branch that closes it could not be reached at all. It is
        // also why only the bots that keep their distance showed it — a melee bot stops at one tile, where a
        // line practically always exists, so the very next line refreshes this and the stale clock never
        // bites. Seeded at the fight rather than at the intention, the six seconds are six seconds of
        // fighting, which is what they were always meant to be.
        _seenTick = Core.TickCount;
        _leg = Leg.Fight;

        return Fighting(bot, body);
    }

    private BotDoing Fighting(IBotWilful bot, Mobile body)
    {
        if (Down())
        {
            _leg = Leg.Spoils;

            return Looting(bot, body);
        }

        // <b>The exit a solo fight never had.</b> Measured in damage rather than in time, and against the
        // lowest health seen rather than the current health — a creature that regenerates between blows would
        // otherwise read as progress for as long as it lived. See NoProgressMs for what this cost.
        if (_quarry.Hits < _lowest)
        {
            _lowest = _quarry.Hits;
            _progressTick = Core.TickCount;
        }

        // Nothing fights with its fists but the brawler. Checked here rather than at birth because a weapon
        // is a condition and not an event — see BotArms — and throttled, because this is asked several times
        // a second and the answer changes about once a session.
        if (Core.TickCount - _armedTick >= BotArms.EveryMs)
        {
            _armedTick = Core.TickCount;

            BotArms.Check(body, bot?.Class);
        }

        // Whether a blow could land at all, asked of the engine rather than of the distance. See BlindMs.
        //
        // <b>Two questions, and printed as one they cost a night.</b> "Cannot land a blow from here" is true
        // of a bow with a tombstone in the way and equally true of a bot the engine will not let swing at
        // all, and those have opposite cures — one is a place to stand, the other is not. On 25.08.2026 the
        // sentence was read as the first, the standoff was taught to close on a lost line, and the next
        // window came back with eighty-one of them: Godric 45 of 48 fights, Merrick 36 of 38, every melee
        // bot nought. A fix aimed at the wrong half of an "and". They are counted apart now, with the
        // distance beside them, because at one tile a broken line is nearly impossible and a refusal is not.
        var sighted = body.InLOS(_quarry);
        var lawful = body.CanBeHarmful(_quarry, false);

        if (sighted && lawful)
        {
            _seenTick = Core.TickCount;
        }
        else if (Core.TickCount - _seenTick >= BlindMs)
        {
            body.Combatant = null;
            body.Warmode = false;

            var apart = (int)body.GetDistanceToSqrt(_quarry.Location);

            var why = (sighted, lawful) switch
            {
                (false, false) => "out of sight and the engine refuses it too",
                (false, true)  => "nothing of it in sight",
                _              => "in plain sight and the engine refuses the blow"
            };

            // <b>"Cured by walking away and being offered it again from somewhere else" — and nothing here
            // ever made it walk away.</b> That sentence stood where the shun call should have been, and it
            // describes a cure nobody administers: the undertaking ends, the very same quarry is the best
            // thing on offer on the very next beat, and the bot takes it again from the very same tile. On
            // 25.08.2026 Joss did that thirty-eight times in one window on one zombie, every refusal from one
            // or two tiles away, with the repetition penalty grinding down by a hundredth a turn while the
            // circle turned every six seconds.
            //
            // Shunned rather than crowded, and the list's own definition settles which: a crowd is one bot
            // being outnumbered, which is an argument for calling a company. This is not that. A creature
            // that cannot be seen from the tile beside it is inside something or on top of something, and
            // "no way to lay a blow on it" is as true for the next bot along as for this one — which is
            // precisely what the shared list means. It lapses in two minutes, so this defers the quarry
            // rather than abandoning it.
            BotQuarry.Shun(_quarry);

            return BotDoing.Failed($"cannot land a blow on {_quarry.Name} at {apart} tiles: {why}");
        }
        else
        {
            // <b>Waiting cannot cure a blocked line, and waiting was the whole of the answer.</b> A bot that
            // keeps its distance stops where the distance tells it to and nothing asked whether it could see
            // what it came for — so an archer parked eight tiles from a skeleton with a tombstone between
            // them sat out the blind clock, gave the quarry up, took it again and walked to another blind
            // spot. On 25.08.2026 Godric lost ten fights out of ten exactly this way while every melee bot in
            // the population lost none, because at one tile there is always a line.
            //
            // Closing is what the waiting was standing in for. BlindMs stays underneath for the case closing
            // cannot fix: a quarry on a roof that nothing on the floor can see.
            //
            // <b>One fixed destination, and the tile-at-a-time version that stood here did nothing at
            // all.</b> It asked for the current distance less one, which is a different destination on every
            // beat — and BotWill rebases the journey whenever the order differs, arrival distance included,
            // throwing the plan away and buying a fresh search each time. The comment beside that rebase warns
            // of exactly this: an undertaking that re-words its walk every beat never gets further than a few
            // tiles. It never got even that far. Every blind failure for two nights reported the standoff
            // distance to the tile, unchanged, and the line "looking for a line" was never once printed
            // against a journey that had gone anywhere.
            //
            // Asking for contact and asking once keeps the order identical beat after beat, so the journey
            // survives and actually carries the bot in. Nothing is given away by aiming at contact: the line
            // check above refreshes the moment one exists, and the standoff and the kite take the bot back
            // out to its proper distance on the very next beat.
            if (!body.InRange(_quarry.Location, 1))
            {
                return BotDoing.Walk(_map, _quarry, BotArrival.Within(1), $"looking for a line on {_quarry.Name}");
            }
        }

        var stalled = Core.TickCount - _progressTick >= NoProgressMs;

        if (stalled || Core.TickCount - _tookTick >= CapMs)
        {
            body.Combatant = null;
            body.Warmode = false;

            // <b>The two exits are two different findings and said so in one sentence.</b> Both printed "would
            // not go down", which is true of a creature nothing could scratch and false of one that was dying
            // steadily when the clock ran out — and the ledger line beside it says "0 coin, 0 made", which is
            // takings and not damage, so nothing anywhere distinguished them. Two casters were cut off at
            // exactly two and a half minutes on a skeleton and a troll, and whether that was a caster doing no
            // damage or a ceiling set too low could not be told from the log at all. How much of it was left
            // is the fact that separates them, so it is the fact that gets printed.
            var left = _quarry.HitsMax > 0 ? _lowest * 100 / _quarry.HitsMax : 0;

            if (stalled)
            {
                // Left to companies rather than to nobody: what has been learned is that one bot cannot shift
                // it, which is an argument for a squad and not against one. Only on this branch — a creature
                // that was steadily dying when the clock ran out is exactly the sort a lone hunter should be
                // offered again.
                BotQuarry.Crowd(_quarry);

                return BotDoing.Failed($"{_quarry.Name} would not go down: {left}% of it left and not a scratch in {NoProgressMs / 1000}s");
            }

            return BotDoing.Failed($"ran out of time on {_quarry.Name} with {left}% of it left");
        }

        // Losing. This is the one decision this undertaking makes that nothing else will make for it.
        if (body.HitsMax > 0 && body.Hits < body.HitsMax * FleeAt)
        {
            body.Combatant = null;
            body.Warmode = false;

            return BotDoing.Failed($"{_quarry.Name} was winning");
        }

        // <b>Losing on numbers, which is a different fact and used to have nowhere to be noticed.</b> The
        // quarry was weighed once, alone, at the moment it was chosen; what walked in afterwards was never
        // weighed at all. So a bot picked a fight it could win, two more creatures joined in, and the only
        // rule that could end it was health — meaning the bot stood there at full health being surrounded,
        // and started thinking about leaving after it had already lost sixty per cent of itself. This asks
        // the same question the road asks: is this several times over, counting everything here and everybody
        // of ours standing in it.
        if (!_weighed || Core.TickCount - _oddsTick >= OddsMs)
        {
            _weighed = true;
            _oddsTick = Core.TickCount;

            if (BotThreat.Decide(body, BotMobile.NoticeRange) == BotStand.Outmatched)
            {
                body.Combatant = null;
                body.Warmode = false;

                // <b>Written down, or the bot walks straight back.</b> A quarry is weighed alone — its own
                // power against ours — and the fight is called off on the numbers standing round it, which
                // is a fact nobody learns until the bot has arrived. So the same creature is chosen again
                // on the next review, walked to again, and refused again: on 24.08.2026 the population
                // finished three hundred and thirty-six walks to look for a fight against twenty-nine
                // fights, and the log reads prowl, hunt, "too many of them", prowl, four times a second.
                //
                // <b>Crowd and not Shun, and the difference cost an evening's companies to find.</b> Put on
                // the shared shun list this also hid the creature from BotQuarry.Company — so the population
                // stopped calling squads entirely, against the one kind of target squads exist for. What was
                // learned here is that <em>one</em> bot cannot have it, which is an argument for a company
                // rather than against one.
                BotQuarry.Crowd(_quarry);

                return BotDoing.Failed($"too many of them around {_quarry.Name}");
            }
        }

        _fell = _quarry.Location;

        // <b>Without this a mage was given the reach of a quarterstaff — one tile — and walked into contact,
        // which is the one place its whole build cannot work: a blow disturbs every cast a player attempts,
        // and every bot is one. So a caster fights at the distance its spells carry, and backs off when
        // something closes, exactly as an archer does and for a sharper reason.</b>
        var casting = BotStrike.Can(body);

        // <b>Being a caster and having something to cast are two different facts, and the standoff was built
        // on the wrong one.</b> Can() asks whether this bot is the sort that throws spells — a permanent
        // property of its class and its book. Whether it can throw one <em>now</em> is a question about mana
        // and about what is in its pack, and it changes minute to minute. Built on the first, a mage whose
        // book has nothing it can pay for keeps station six tiles from a troll, out of reach of its own
        // staff, and contributes precisely nothing: on 24.08.2026 two fights ended at forty-five seconds with
        // the target on a hundred per cent of its health, which is a bot standing in a fight not being in it.
        //
        // Asked every beat rather than latched, so the moment reagents are back in the pack the bot gives
        // ground again and fights the way it is built to.
        var armed = casting && BotStrike.Best(body) >= 0;

        var reach = Math.Max(body.Weapon?.MaxRange ?? 1, armed ? BotStrike.Range : 0);
        var standoff = Standoff(reach);

        if (!body.InRange(_quarry.Location, reach))
        {
            _leg = Leg.Close;

            return BotDoing.Walk(_map, _quarry, BotArrival.Within(standoff), $"after {_quarry.Name}");
        }

        // <b>Kiting, and the sentence below it is wrong.</b> "The engine keeps swinging the bow while the bot
        // moves — combat is its own timer and does not care that the shooter is walking" is true of a blade
        // and false of a bow on this era: BaseRanged.OnSwing refuses outright unless the shooter has been
        // still for a full second. So an archer that gives ground whenever something is near never fires at
        // all, and one that stands still is a melee fighter holding the wrong weapon.
        //
        // The engine says exactly how long the bow will be idle — NextCombatTime — so the two halves can be
        // taken in turn: spend the idle part opening the distance, and be standing again well before the
        // shot is due. Ordered on 24.08.2026, and it is the only way a bow is worth carrying here.
        if (Kiting(body, reach))
        {
            var opening = Away(body, _quarry, reach);

            if (opening != Point3D.Zero)
            {
                body.Warmode = true;
                body.Combatant = _quarry;

                BotQuarry.Claim(body, _quarry);

                return BotDoing.Walk(_map, opening, BotArrival.Within(1), $"keeping {_quarry.Name} at arm's length");
            }
        }

        // <b>A caster gives ground when it has nothing to throw, and only then.</b>
        //
        // <c>Best</c> is one question with three gates behind it — the book holds the spell, the pool pays for
        // it, and the herbs are in the pack — so "no mana for an attack, or no reagents to cast it" is not two
        // conditions here, it is this one being false. That matters: a mage in contact <em>with</em> mana is
        // trading blows for spells and getting the better of it, while a mage with a dry pool in contact is
        // simply being hit, and the way out of that is distance and a minute.
        //
        // <b>And it had to be written as its own gate, because the arithmetic had quietly closed it.</b>
        // The stand-off is two short of the bot's reach, and a caster with nothing to throw has the reach of
        // the stick in its hand — one tile. One is not greater than three, so the branch below could not be
        // entered by the one bot that most needed it: "backing off" and "keeping at arm's length" both stood
        // at nought across whole sessions, and a mage stood in melee being beaten while the code that would
        // have moved it sat right here.
        var dry = casting && !armed;

        // How far it wants to be from whatever has closed. A dry caster opens to the distance its spells will
        // carry when the mana comes back, not to whatever its stick reaches — backing off one tile is not
        // backing off.
        var yield = dry ? Standoff(BotStrike.Range) : standoff;

        // An archer's arithmetic is untouched: it is not a caster, so it reads the same test it always did.
        var yields = casting ? dry : standoff > TooClose;

        // Something has closed on a bot that would rather it did not. Give ground, keep shooting.
        //
        // <b>The first thing in this project that walks away from something on purpose.</b> Everything else
        // treats a fight as a place to stand; an archer's whole case for existing is that it is somewhere
        // else.
        if (yields && yield > TooClose && body.InRange(_quarry.Location, TooClose))
        {
            // <b>A class that closes answers this moment the other way round.</b> Everything above is an
            // archer's arithmetic and it is right for an archer: something is inside the bow's useful
            // distance, so open it again. A captain reads the same fact and draws its sword, and from the
            // line below it is fighting by the shard's ordinary melee code with nothing special about it —
            // the standoff recomputes from the weapon now in its hand, comes out at one tile, and this whole
            // branch stops applying by arithmetic rather than by exception.
            //
            // Asked of the class rather than of the bot's build, because it is a decision about how this
            // sort of fighter answers being closed on, and it must read the same for every captain the
            // population ever raises.
            var closing = BotArms.Suit(body, _quarry, TooClose);

            if (closing)
            {
                // Nowhere to back off to any more, and the held point would otherwise be walked to on the
                // next beat by a bot that has just decided to stand.
                _backing = Point3D.Zero;

                Closed++;
            }
            else
            {
                // Held rather than recomputed: see _backing. A point is dropped once the bot is standing on
                // it, or once it has stopped being far enough from the quarry to be worth walking to.
                if (_backing != Point3D.Zero
                    && (body.InRange(_backing, 1) || !Utility.InRange(_backing, _quarry.Location, yield)))
                {
                    _backing = Point3D.Zero;
                }

                var back = _backing != Point3D.Zero ? _backing : Away(body, _quarry, yield);

                if (back != Point3D.Zero)
                {
                    _backing = back;

                    body.Warmode = true;
                    body.Combatant = _quarry;

                    BotQuarry.Claim(body, _quarry);

                    return BotDoing.Walk(_map, back, BotArrival.Within(1), $"backing off {_quarry.Name}");
                }
            }
        }

        // Set every beat, and the setter is a no-op when it has not changed. Combat itself is the engine's
        // from here: one server timer, swinging on the weapon's own delay.
        body.Warmode = true;
        body.Combatant = _quarry;

        // Whoever can throw something, throws it.
        //
        // <b>Melee goes on underneath, deliberately.</b> The engine swings on its own timer whatever else the
        // mobile is doing, so a caster loses nothing by also being in a fight — and a caster out of mana is
        // simply a bot with a stick again, which is the right fallback rather than a special case.
        if (casting)
        {
            // A cast of ours has come round to its target: the click a bot has no client to make. Guarded by
            // having started one, because a cursor on a bot is not necessarily this work's — mining puts one
            // up to point at rock.
            if (_aiming && body.Target != null)
            {
                _aiming = false;

                if (BotStrike.Aim(body, _quarry))
                {
                    _casts++;
                }

                return BotDoing.Work($"casting at {_quarry.Name}");
            }

            // Mid-cast. The engine is holding the delay and movement already knows to stand still for it.
            if (body.Spell != null)
            {
                return BotDoing.Work("casting");
            }

            // Not before the engine will have it. Asking again inside the recovery is refused, and asking
            // every beat is asking eight times a second.
            if (_cast && Core.TickCount - _castTick < BotStrike.CastMs)
            {
                return BotDoing.Work($"between spells at {_quarry.Name}");
            }

            var spell = BotStrike.Best(body);

            if (spell >= 0 && BotStrike.Begin(body, spell))
            {
                _aiming = true;
                _cast = true;
                _castTick = Core.TickCount;

                return BotDoing.Work($"casting at {_quarry.Name}");
            }

            // Said once per fight, because a caster that silently declines to cast is the shape of fault this
            // project has been bitten by all evening: the refusal is a message to a client, and a bot has none.
            //
            // <b>And it has to name the gate that closed, which it did not.</b> The line read "best -1, 35 of
            // 35 mana, book holds 5" — a full pool and a stocked book, both of them fine, and silence about
            // the only thing that was not. That is the rule this project keeps relearning: a message that
            // lists the reasons it checked and finds them all in order is a message whose real reason is not
            // in the list. There are three gates in BotStrike.Ready and the answer is always one of them.
            if (!_complained)
            {
                _complained = true;

                logger.Information(
                    "{Name} could not throw a spell at {What}: {Why} — best {Spell}, {Mana} of {Pool} mana, book holds {Known}",
                    body.Name,
                    _quarry.Name,
                    BotStrike.Why(body),
                    spell,
                    body.Mana,
                    body.ManaMax,
                    BotGrimoire.Count(body)
                );
            }
        }

        // Whoever sets about a thing owns what comes off it. Claimed here rather than when the work was
        // chosen, because choosing is not fighting: a bot that picked a quarry and never reached it has no
        // business locking it away from everybody who could.
        BotQuarry.Claim(body, _quarry);

        return BotDoing.Work($"fighting {_quarry.Name}");
    }

    private BotDoing Looting(IBotWilful bot, Mobile body)
    {
        body.Combatant = null;
        body.Warmode = false;

        if (!body.InRange(_fell, BotQuarry.LootReach))
        {
            return BotDoing.Walk(_map, _fell, BotArrival.Within(BotQuarry.LootReach), "to the corpse");
        }

        // The claim outlives the creature: this is exactly the moment it decides something. A bot that came
        // late to somebody else's fight has fought honestly and earned the skill for it, and it does not get
        // the purse.
        if (!BotQuarry.Ours(body, _quarry))
        {
            return BotDoing.Done($"{_quarry?.Name} was somebody else's fight");
        }

        var corpse = BotQuarry.Remains(_map, _fell, _quarry);

        if (corpse == null)
        {
            // Killed and nothing to show for it: somebody else went through it, or it left nothing. Finished
            // rather than failed — the fight happened and the skill it taught is real.
            return BotDoing.Done("nothing left on it");
        }

        // Before anything is picked up or divided, so that the hide is on the corpse by the time either
        // happens and travels with the rest of the takings instead of needing a trip of its own.
        _hides = Skin(body, corpse);

        // In company, what came off it is divided by worth rather than grabbed. Solo — which is every fight
        // on this shard today, because nothing musters a squad yet — it all goes in the one pack.
        var squad = (bot as IBotSquadMember)?.Squad;

        if (squad != null)
        {
            _taken = BotSpoils.Share(squad, (IBotSquadMember)bot, corpse);

            BotQuarry.Release(_quarry);

            return BotDoing.Done($"{_quarry?.Name} split {_taken} ways with the squad");
        }

        Take(bot, body, corpse);

        // What this kind of creature actually carried, written down for everybody. A goat priced at nothing
        // stops being chosen; a lizardman priced at fifty starts being sought out.
        BotQuarry.Paid(_quarry?.GetType(), _coins);

        BotQuarry.Release(_quarry);

        // The spells are in the sentence because otherwise casting is invisible: a cast is reported through
        // BotDoing.Work, and Work is the one answer the decision layer deliberately never writes down.
        // Leather is said separately from the thing-count it is part of, because it is the one thing coming
        // off a corpse that somebody else is waiting on: a leather order on the needs board is filled or not
        // filled by this number, and "eleven things" does not say whether it was.
        return BotDoing.Done(
            (_casts > 0, _hides > 0) switch
            {
                (true, true) =>
                    $"{_taken} things and {_coins}gp off {_quarry?.Name}, {_hides} leather, {_casts} spells thrown",
                (true, false) => $"{_taken} things and {_coins}gp off {_quarry?.Name}, {_casts} spells thrown",
                (false, true) => $"{_taken} things and {_coins}gp off {_quarry?.Name}, {_hides} leather",
                (false, false) => $"{_taken} things and {_coins}gp off {_quarry?.Name}"
            }
        );
    }

    /// <summary>
    /// Empties what is worth taking into the pack, and puts the goods straight onto the market.
    ///
    /// <para>
    /// <b>Everything goes to the population first, whatever it is.</b> There is no test here for whether a
    /// rusty blade is treasure or rubbish, and there does not need to be: a stall that nobody buys from in
    /// half an hour is taken to a shopkeeper by <see cref="BotPeddler"/>, which is the same road produced
    /// goods travel. One bot's junk is another's material, and the market is the only thing that can tell the
    /// difference — so it is asked, every time, before a counter is.
    /// </para>
    ///
    /// <para>
    /// Weight is the limit, not value. Loot is not bound, so it weighs, and a bot that empties a corpse into
    /// an already full pack is a bot that cannot walk home.
    /// </para>
    /// </summary>
    /// <summary>
    /// Take the hide off it and cut that down to leather, both onto the corpse, and answer how many pieces
    /// came out. Nothing, and no complaint, when the creature carries no hide or there is nothing to cut with.
    ///
    /// <para>
    /// <b>Leather had no way into this world at all.</b> A tailor's orders for anything made of it stood on
    /// the board unfillable at any price while the material walked around Felucca on the backs of everything
    /// the population was already killing — because carving is a thing a player does with a blade and no bot
    /// had been taught the gesture. The engine charges nothing for it: no skill roll, no roll to fail, no
    /// wear on the blade, and on Felucca it doubles what comes off. That is why this is folded into going
    /// through a corpse rather than made an undertaking a bot has to choose over hunting — there is no
    /// decision here worth a bot's time to make.
    /// </para>
    ///
    /// <para>
    /// <b>Two steps and not one, because the hide is not the thing anybody wants.</b> On this era carving
    /// yields hides, tailoring consumes leather, and scissors turn one into the other piece for piece — so a
    /// population that carved and stopped there would have filled the market with a commodity no crafter can
    /// use and left the leather orders exactly as unfillable as before. Cut here, while it is still on the
    /// corpse, because <see cref="Take"/> lists what it lifts the moment it lifts it: leather offered to the
    /// market is an order filled, hides offered to the market is a second problem.
    /// </para>
    ///
    /// <para>
    /// Carving somebody else's kill is a criminal act and the engine says so. This only ever runs on a kill
    /// already established as ours, and asks anyway rather than trusting that.
    /// </para>
    /// </summary>
    public static int Skin(Mobile body, Corpse corpse)
    {
        if (corpse.Carved || corpse.IsCriminalAction(body))
        {
            return 0;
        }

        var blade = Blade(body);

        if (blade == null)
        {
            return 0;
        }

        corpse.Carve(body, blade);

        var shears = body.Backpack?.FindItemByType<Scissors>();

        if (shears == null)
        {
            return 0;
        }

        var cut = 0;

        // A snapshot: cutting replaces the hides in the corpse, which mutates the list being read.
        List<Item> lying = [.. corpse.Items];

        for (var i = 0; i < lying.Count; i++)
        {
            if (lying[i] is not (BaseHides and IScissorable hide))
            {
                continue;
            }

            var was = lying[i].Amount;

            if (hide.Scissor(body, shears))
            {
                cut += was;
            }
        }

        return cut;
    }

    /// <summary>
    /// Something to carve with, in a hand or in the pack, or nothing.
    ///
    /// <para>
    /// The engine's own notion of what is bladed rather than a list of item names kept here to drift: a knife
    /// and a sword are precisely the two things that put up a <c>BladedItemTarget</c> when a player
    /// double-clicks them. A swordsman is holding one already for reasons of its own; everybody else is
    /// carrying the skinning knife birth hands out — see <c>BotOutfit.ToolsFor</c>.
    /// </para>
    /// </summary>
    private static Item Blade(Mobile body)
    {
        if (body.Weapon is Item held and (BaseKnife or BaseSword))
        {
            return held;
        }

        var pack = body.Backpack;

        if (pack == null)
        {
            return null;
        }

        List<Item> carried = pack.Items;

        for (var i = 0; i < carried.Count; i++)
        {
            var item = carried[i];

            if (item is BaseKnife or BaseSword && !item.Deleted)
            {
                return item;
            }
        }

        return null;
    }

    private void Take(IBotWilful bot, Mobile body, Corpse corpse)
    {
        var (taken, coins, made) = Rifle(bot, body, corpse);

        _taken += taken;
        _coins += coins;
        _made += made;
    }

    /// <summary>
    /// Goes through a corpse and says what came off it. The whole of going through a corpse, for anybody.
    ///
    /// <para>
    /// <b>Lifted out of the hunt because a kill is no longer the only way a corpse appears.</b> This used to
    /// be an instance method writing straight into a hunt's own tally, on the reasoning that the only bot
    /// standing over a body is the one whose undertaking killed it. That stopped being true the day
    /// self-defence became a reflex: a bot now kills whatever walks up and hits it, without ever having taken
    /// on a fight, and the body it leaves has nobody whose errand includes emptying it. A sage killed a harpy
    /// and walked away from it on 26.08.2026, which is what sent anybody looking.
    /// </para>
    ///
    /// <para>
    /// Static and returning its counts rather than writing them, so the same code answers for the hunt that
    /// planned the kill and for the scavenger that only survived one. Two copies of the weight rule would
    /// have been the more familiar mistake.
    /// </para>
    /// </summary>
    public static (int Taken, int Coins, int Made) Rifle(IBotWilful bot, Mobile body, Corpse corpse)
    {
        var taken = 0;
        var coins = 0;
        var made = 0;

        var pack = body.Backpack;

        if (pack == null)
        {
            return (0, 0, 0);
        }

        var ceiling = BotLadder.Ceiling(body) * FillFraction;

        // A snapshot: moving things out mutates the list being read.
        List<Item> lying = [.. corpse.Items];

        for (var i = 0; i < lying.Count; i++)
        {
            var item = lying[i];

            if (item == null || item.Deleted || !item.Movable || !corpse.CheckLoot(body, item))
            {
                continue;
            }

            if (item is Gold coin)
            {
                coins += coin.Amount;

                pack.DropItem(coin);

                continue;
            }

            if (BotLadder.Load(body) >= ceiling)
            {
                // Full. What is left stays on the corpse for whoever comes past, which is a better answer than
                // a hunter that cannot carry its own takings home.
                break;
            }

            pack.DropItem(item);

            taken++;

            // Its own ammunition stays in the quiver.
            //
            // <b>Four hits in ten put the arrow into the target, which means into this corpse</b> — so an
            // archer going through what it has just killed is very often picking up its own arrows, and
            // listing them for sale is an archer selling the means of its trade and then buying it back at a
            // counter. Everything else off a corpse is goods; this is kit.
            if (item.GetType() == bot.Bond?.Weapon?.Ammunition)
            {
                continue;
            }

            // Offered to the population at whatever the shard reckons one is worth. Nobody has to want it: a
            // stall that sits for half an hour is taken to a counter instead.
            //
            // <b>The fallback was a flat one gold, and a flat one gold is what strangled the leather
            // trade.</b> Worth answers with the market's own price the moment anybody has bid or bought —
            // but until then it hands back whatever the caller guessed, and every kind of loot in the world
            // was guessed at one. So a bear's four-and-twenty hides went out at 24gp when the same bear's
            // purse held fifty, and the tailor's floor, which multiplies up from the material, priced a pair
            // of leather shoes at twelve. Nothing was broken anywhere in that chain: it was simply valued at
            // nothing, so a crafter weighing leather against cloth rationally chose cloth every time, and in
            // eight hours not one leather piece was ever made.
            //
            // A shopkeeper's own offer is the honest floor, and the engine already knows it: a tanner pays
            // five for a leather, a furtrader two for a hide. Measured rather than declared, like everything
            // else here — no table to go stale when somebody edits a loot pack — and it only sets the
            // opening ask, after which the market moves the price the way it moves every other.
            var floor = BotShops.Buyer(body, item, out var offered) != null ? offered : 1;
            var worth = BotAuction.Worth(item.GetType(), Math.Max(1, floor));

            if (BotAuction.List(bot, item, worth) != null)
            {
                made += worth * (item.Amount > 0 ? item.Amount : 1);
            }
        }

        return (taken, coins, made);
    }

    /// <summary>
    /// The way to it turned out not to exist. Say so about <em>this creature</em> rather than about the
    /// ground, and give up.
    ///
    /// The ledger's caution is keyed by patch of ground, which is the wrong grain for this: a wraith on the
    /// far side of a river makes a bot wary of a whole field it could otherwise hunt perfectly well, while
    /// leaving the wraith itself as attractive as ever to the next bot that looks. What could not be reached
    /// is the wraith.
    /// </summary>
    public override bool Bend(IBotWilful bot)
    {
        BotQuarry.Shun(_quarry);

        return false;
    }

    /// <summary>Over, however it ended. Whatever was reserved goes back.</summary>
    public override void Drop(IBotWilful bot)
    {
        BotQuarry.Release(_quarry);

        // <b>The bow comes back up the moment the fight is over, and forgetting this would have been silent.</b>
        // A captain that closes ends every fight holding a sword, and the next one would then open at one
        // tile — so the class that exists to shoot first would have shot first exactly once, on the day it
        // was born. Nothing would have logged it: the bot is armed, it is fighting, it is winning some of
        // them. It would simply never have been an archer again.
        if (bot?.Self is BotMobile { Class.Closes: true } closer)
        {
            closer.Draw(melee: false);
        }

    }

    /// <summary>
    /// Whether this bot should be opening the distance right now rather than standing to shoot.
    ///
    /// <para>
    /// Three conditions, and every one of them has to hold. It must be carrying something that shoots, or a
    /// blade would walk away from the fight it is supposed to be in. The quarry must be nearer than the
    /// weapon carries, or there is nothing to gain and a step outwards throws the shot away. And the bow must
    /// have long enough left on its own clock to move and be still again before the shot is due — which is
    /// the whole trick, and the engine is the only thing that knows it.
    /// </para>
    /// </summary>
    /// <summary>Times the kite was considered, and which of its three conditions turned it down.</summary>
    public static long Asked { get; private set; }

    public static long Handless { get; private set; }

    public static long Distant { get; private set; }

    public static long Rushed { get; private set; }

    public static long Kited { get; private set; }

    /// <summary>The longest the weapon's own clock was ever seen to have left, in milliseconds.</summary>
    public static long Longest { get; private set; }

    /// <summary>
    /// How often a bot answered being closed on by drawing its blade instead of giving ground.
    ///
    /// <para>
    /// Counted beside the kite rather than inside it, because the two are opposite answers to the identical
    /// fact and a single number covering both would say nothing about either. A shard where this rises and
    /// <see cref="Kited"/> does not has one captain in a lot of trouble; the other way round has a captain
    /// carrying a sword it never draws.
    /// </para>
    /// </summary>
    public static long Closed { get; private set; }

    /// <summary>
    /// Why the bow never once gave ground.
    ///
    /// <para>
    /// <b>Two nights of "kites: 0" and no way to tell which of three conditions was the one saying no.</b>
    /// Counted apart, with the longest clock ever seen beside them, because "the bow is never idle long
    /// enough" and "the bow is never in a hand" are different faults and were producing the same silence.
    /// </para>
    ///
    /// <para>
    /// <b>The first thing it found was that there was nothing wrong.</b> The kite fires — three hundred and
    /// eight times in one five-minute window, on a clock running out to five seconds against the seventeen
    /// hundred milliseconds it needs. What was being counted against it all along was a grep for the phrase
    /// this work walks under, and a walk's reason reaches the log <em>only when the journey fails</em>. Every
    /// successful kite is silent by construction, so the measure could report nothing but disasters and was
    /// read as though nought meant never. A metric that can only go up when something breaks is not a metric.
    /// </para>
    ///
    /// <para>
    /// The melee bucket is named for what it is rather than counted as a refusal: every bot in every fight is
    /// asked this question, and a swordsman having no bow is not the bow failing. Left unlabelled it read as
    /// a thousand archers with empty hands.
    /// </para>
    /// </summary>
    public static string Bows() =>
        Asked == 0
            ? "nobody has been offered a kite"
            : $"{Asked} asked: {Handless} were melee and have no kite to give, {Kited} gave ground, {Distant} were already at the weapon's edge, {Rushed} had the shot too near, {Closed} drew steel instead — longest clock seen {Longest}ms against {StillMs + KiteSlackMs}ms needed";

    /// <summary>Zeroes the kite's tally.</summary>
    public static void ForgetBows()
    {
        Asked = 0;
        Handless = 0;
        Distant = 0;
        Rushed = 0;
        Kited = 0;
        Longest = 0;
        Closed = 0;
    }

    private bool Kiting(Mobile body, int reach)
    {
        Asked++;

        if (reach <= 1 || (body.Weapon?.MaxRange ?? 1) <= 1)
        {
            Handless++;

            return false;
        }

        if (!body.InRange(_quarry.Location, reach - 1))
        {
            Distant++;

            return false;
        }

        var left = body.NextCombatTime - Core.TickCount;

        if (left > Longest)
        {
            Longest = left;
        }

        if (left <= StillMs + KiteSlackMs)
        {
            Rushed++;

            return false;
        }

        Kited++;

        return true;
    }

    /// <summary>
    /// A place to stand at most <paramref name="want"/> tiles from the quarry, on the far side of the bot from
    /// it, or nothing if no such place both holds a body and can see the quarry from where it stands.
    ///
    /// <para>
    /// Straight back along the line the creature came in on, and no cleverness beyond that: a bot that tries
    /// to be clever about where it retreats to is a bot computing a second pathfinder. If the ground behind it
    /// will not take it, it stands and fights, which is the honest fallback.
    /// </para>
    ///
    /// <para>
    /// <b>Sight is a condition of the place, and leaving it out is most of why archers never fired.</b>
    /// Retreat was written as a distance and nothing else, so a bow gave ground until something came between
    /// it and the quarry — and from there it could not shoot, because the engine refuses a swing without a
    /// line. The weapon clock then never advances, and <see cref="Kiting"/> waits on exactly that clock, so
    /// the kite dies with the shot it was supposed to be buying time for. The far point is tried first and
    /// the line walked inwards a tile at a time, so a bot gives up as much of its reach as the ground costs
    /// it and not one tile more.
    /// </para>
    /// </summary>
    private static Point3D Away(Mobile body, Mobile from, int want)
    {
        var map = body.Map;

        if (map == null)
        {
            return Point3D.Zero;
        }

        var dx = body.X - from.X;
        var dy = body.Y - from.Y;

        if (dx == 0 && dy == 0)
        {
            return Point3D.Zero;
        }

        var step = Math.Max(Math.Abs(dx), Math.Abs(dy));

        var mark = from.Location;

        mark.Z += Eye;

        for (var d = want; d >= 1; d--)
        {
            // Normalised to a unit direction, then thrown out to the distance being tried.
            var x = from.X + dx * d / step;
            var y = from.Y + dy * d / step;

            if (!BotStep.Settle(map, x, y, out var z))
            {
                continue;
            }

            if (map.LineOfSight(new Point3D(x, y, z + Eye), mark))
            {
                return new Point3D(x, y, z);
            }
        }

        return Point3D.Zero;
    }

    /// <summary>Whether the quarry is out of the fight, however it got there.</summary>
    private bool Down() =>
        _quarry == null || _quarry.Deleted || !_quarry.Alive || _quarry.Map != _map;
}
