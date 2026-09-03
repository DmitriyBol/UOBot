using System;
using Server.Items;
using Server.Logging;
using Server.Mobiles;
using Server.Regions;

namespace Server.BotAI.V2;

/// <summary>
/// Six bots taken to ground that has killed people, and kept there until it has been emptied or the
/// afternoon is gone.
///
/// <para>
/// <b>This is the shard's third reason for a company, and it is not a bigger patrol.</b> A muster forms
/// against one creature and ends when that creature is dead. A patrol — see <see cref="BotSweep"/> — is sent
/// to a square that reads dangerous <em>lately</em> and finishes when the reading falls, which is a
/// statement about frequency and can be satisfied by the trouble simply wandering off. A harrowing is
/// answerable to neither: it goes where people have actually died, it is finished by a count of corpses or
/// by a clock, and when it ends the square comes off the board altogether. Nothing else on this shard clears
/// anything — everything else knocks a number down and lets it climb back.
/// </para>
///
/// <para>
/// <b>Everything alive inside the box, and the exclusions are the interesting half.</b> Not "hostile", which
/// is what every other fight on the shard asks: hostility is a notoriety judgement and it lets a field of
/// harmless things stand between a company and the thing it came for. Bots are excluded structurally rather
/// than by a rule — they are players as far as the engine is concerned and this only ever looks at
/// creatures — and guarded ground is excluded outright, so a chase that leads into a town ends at the gate.
/// </para>
///
/// <para>
/// <b>It is worth what it hands to the five who came.</b> The Baron takes no share, so measured the ordinary
/// way — coin in his own pack — this work pays nothing and the ledger would have learned, correctly by its
/// own arithmetic, that leading companies into deadly ground is worthless. See <see cref="BotSquad.Won"/>:
/// the company keeps the figure, and this reads it.
/// </para>
/// </summary>
public sealed class BotHarrow : BotDeed
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotHarrow));

    /// <summary>The ledger's key.</summary>
    public const string Trade = "harrow";

    /// <summary>
    /// How many tiles across the ground the company walks.
    ///
    /// <para>
    /// Seventy-five, by order, and it is three times a peril square on purpose. The square is where the
    /// deaths were recorded and it is only twenty-four tiles across — the resolution at which "a place"
    /// means something to a map, not the resolution at which a wood empties. What actually killed people
    /// there lives in the wood around it, so the errand is the neighbourhood and the square is only its
    /// centre.
    /// </para>
    /// </summary>
    public static int Side { get; set; } = 75;

    /// <summary>How many bodies march, the Baron included. The floor: a square that has eaten a company asks
    /// for more. See <see cref="BotQuad.Levy"/>.</summary>
    public static int Company { get; set; } = 6;

    /// <summary>
    /// How many grandmasters a damned square asks for before anybody marches on it.
    ///
    /// <para>
    /// Fifteen, by Patrick's order on 02.09.2026: ground that has swallowed thirty may be gathered against
    /// "only from fifteen grandmasters and from four thousand of strength". It is a different kind of rule
    /// from the levy above — that one is about how many, this one is about who — and a square earns it by
    /// killing a company whole rather than by any amount of ordinary bad luck.
    /// </para>
    /// </summary>
    public static int Grandmasters { get; set; } = 15;

    /// <summary>
    /// The fighting power each of them must have, not the company's between them.
    ///
    /// <para>
    /// Four thousand a head, by Patrick's correction on 02.09.2026: "four thousand per person, that is,
    /// properly trained and equipped bots, and the mages must have their reagents." Power on this shard is
    /// health times what a body hits for — see <c>BotThreat.Power</c> — so four thousand is not a figure a
    /// bot reaches by being handed a sword: it is a bot that has grown and has been outfitted. Which is the
    /// point. Damned ground is ground the population is not yet good enough for, and it should say so by
    /// standing empty rather than by swallowing another company.
    /// </para>
    /// </summary>
    public static double Might { get; set; } = 4000.0;

    /// <summary>
    /// Reagents a caster must be carrying to count towards the company.
    ///
    /// <para>
    /// By the same correction. A mage with an empty pack casts nothing from its book — <c>BotStrike.Ready</c>
    /// refuses the spell — so on damned ground it is a body with a staff, and counting it is how a company
    /// of fifteen arrives as a company of nine. Anybody carrying a spellbook is asked; everybody else is not.
    /// </para>
    /// </summary>
    public static int Reagents { get; set; } = 10;

    /// <summary>The skill at which a bot counts as a grandmaster. The era's own ceiling.</summary>
    public static double GrandmasterAt { get; set; } = 100.0;

    /// <summary>
    /// Fewest worth setting out with once the muster has run its course.
    ///
    /// <para>
    /// <b>Three, and the number moved because the rule around it did.</b> It was the full six, and six was
    /// read as a gate: no six standing near, no harrowing, refused on the spot. That produced a Baron who
    /// never left town while the graveyard filled up — the volunteers a shard has at any one instant are
    /// whoever happens to be idle in that second, and asking for five of them at once is asking for a
    /// coincidence. The order is now a muster: he stands in the square and calls for <see cref="MusterMs"/>,
    /// and what this number does is decide whether what turned up is a company or an escort.
    /// </para>
    ///
    /// <para>
    /// Still separate from <see cref="Company"/>, which is what the squad will hold. One is the target, the
    /// other is the floor, and they are different statements even when they happen to agree.
    /// </para>
    /// </summary>
    public static int Least { get; set; } = 3;

    /// <summary>
    /// How long he stands in the square calling for volunteers before setting out with whoever came.
    ///
    /// <para>
    /// Five minutes, by order. It ends early the moment <see cref="Company"/> have gathered — a muster that
    /// waited out its clock with a full company would be five bots standing about for no reason.
    /// </para>
    /// </summary>
    public static int MusterMs { get; set; } = 300000;

    /// <summary>
    /// Where the company forms up, or nought for the population's own home.
    ///
    /// <para>
    /// <b>It was the bank counter, and a bank counter is not a square.</b> <c>BotGround.Counter</c> is the
    /// only thing on this shard that knows where a town is at all, so it was the obvious anchor — and what it
    /// actually answers is "where is the nearest place to put money", which put a Baron in full plate
    /// standing in the queue at the bank while he called for a company. The two questions look alike and are
    /// not: one is about coin and one is about assembly.
    /// </para>
    ///
    /// <para>
    /// The population's home is the honest default. It is where every bot on the island was raised, it is
    /// what <c>bot-population.json</c> already calls the middle of things, and it needs no survey to be
    /// known. Configuration may name somewhere else — the drill field was moved the same way.
    /// </para>
    /// </summary>
    public static Point3D Square { get; set; }

    /// <summary>
    /// How near the muster point he has to be to count as standing in the square.
    ///
    /// Not called <c>Post</c>, which is the name of the method that picks corners of the box a few dozen
    /// lines down. Two things in one class that are both "a place to stand" and mean different places is the
    /// kind of name collision the compiler catches today and a reader trips over for ever.
    /// </summary>
    public static int Station { get; set; } = 2;

    /// <summary>
    /// How close a volunteer has to be standing to the muster point to count as having turned up.
    ///
    /// <para>
    /// By Patrick's order on 02.09.2026: the Baron gathers the party in the square, waits until everybody has
    /// come, and only then sets out for the quadrant. Until now the call ended when enough had <em>joined</em>
    /// — and joining is instant while walking across Britain is not, so the company set off as a list of
    /// names and reached the ground in single file, which is how six bots get killed one at a time by
    /// something six of them together could beat.
    /// </para>
    ///
    /// <para>
    /// The five-minute call is still the backstop: when it runs out he marches with whoever is actually
    /// standing there, which is the old rule applied to bodies instead of to names.
    /// </para>
    /// </summary>
    public static int Assembly { get; set; } = 8;

    /// <summary>Corpses that finish the errand.</summary>
    public static int Quota { get; set; } = 20;

    /// <summary>
    /// The longest one harrowing may last, when the quota is never reached.
    ///
    /// Forty minutes, by order — a third again as long as a patrol's half hour, which is right for an errand
    /// that has a count to fill rather than a reading to wait out.
    /// </summary>
    public static int CapMs { get; set; } = 1800000;

    /// <summary>
    /// How far around the muster point he calls people up.
    ///
    /// <para>
    /// <b>Forty tiles was a shout, and what is wanted is a summons.</b> At forty he was calling to whoever
    /// happened to be crossing the same street, and on 27.08.2026 he stood in the square for the full five
    /// minutes and nobody came at all. The population is spread over five hundred tiles by design; a call
    /// that only reaches the next block is a call to an empty street.
    /// </para>
    /// </summary>
    public static int Reach { get; set; } = 200;

    /// <summary>
    /// How often the island is swept for bodies while the call is open.
    ///
    /// Two seconds. The sweep is a spatial query over two hundred tiles and the errand is asked ten times a
    /// second; done every beat it would be two hundred sweeps for every one that could possibly find anybody
    /// new, which is the shape of cost this project has paid for twice in the movement budget alone.
    /// </summary>
    public static int SweepMs { get; set; } = 2000;

    /// <summary>
    /// The company he wants, by role: two who stand in the line, two who shoot, one who mends.
    ///
    /// <para>
    /// <b>A want, not a requirement, and the difference is what keeps this from being another gate that
    /// never opens.</b> The quota is filled first and by distance, so a Baron with three warriors and no
    /// healer within reach still marches with three warriors — the alternative is the rule that stopped him
    /// leaving town in the first place. What it buys is that the nearest five are not five of the same thing.
    /// </para>
    /// </summary>
    public static int Melee { get; set; } = 2;

    public static int Ranged { get; set; } = 2;

    public static int Medics { get; set; } = 1;

    /// <summary>How far the company looks for something to kill, from wherever the Baron is standing.</summary>
    public static int Sight { get; set; } = 20;

    /// <summary>
    /// How long the company spends walking to one corner of the box before trying the next.
    ///
    /// Longer than a patrol's, because the box is three times as wide and a corner is twenty-five tiles out
    /// rather than eight.
    /// </summary>
    public static int RoundMs { get; set; } = 90000;

    /// <summary>How many corners may turn out to be unwalkable before the ground is given up.</summary>
    public static int MaxBends { get; set; } = 4;

    /// <summary>
    /// What a harrowing is reckoned at per minute before experience corrects it.
    ///
    /// <para>
    /// A company of six working ground that has been killing people, and the figure it is measured against
    /// is what the five of them actually carried away. High, and it is meant to be: this is six bots' worth
    /// of hunting concentrated on the one place that has proved it holds something. Nothing else the Baron
    /// may take comes near it, which is what makes him walk the town only when there is nowhere to go.
    /// </para>
    /// </summary>
    public static double Prior { get; set; } = 150.0;

    /// <summary>How long a harrowing is expected to take, walk and all.</summary>
    public static double WorkMinutes { get; set; } = 25.0;

    /// <summary>Companies that formed and set out.</summary>
    public static long Marches { get; private set; }

    /// <summary>Musters that ran their full course and still could not raise <see cref="Least"/> bodies.</summary>
    public static long Undermanned { get; private set; }

    /// <summary>Musters called. Not marches: see <see cref="Undermanned"/> for the ones that came to nothing.</summary>
    public static long Musters { get; private set; }

    /// <summary>Squares finished by filling the quota, and squares finished by the clock. Never one number.</summary>
    public static long Emptied { get; private set; }

    public static long Timedout { get; private set; }

    public static long Killed { get; private set; }

    private readonly Map _map;

    private readonly Point3D _square;

    private readonly int _dead;

    private BotSquad _squad;

    private long _won;

    private long _began;

    private int _called;

    private int _kills;

    private bool _standing;

    private long _steppedTick;

    private int _round;

    private Point3D _post;

    private int _bends;

    private Mobile _quarry;

    /// <summary>Whether the company has been raised and is on its way. Never "is the squad null".</summary>
    private bool _marching;

    /// <summary>How many this square asks for. See <see cref="BotQuad.Levy"/> — nought until the muster sets it.</summary>
    private int _wanted;

    /// <summary>The levy, or the ordinary company before the muster has asked the map.</summary>
    private int Wanted => _wanted > 0 ? _wanted : Company;

    /// <summary>How many marched, so that a company lost whole can be reported as the size it was.</summary>
    private int _marched;

    /// <summary>How many of the called are standing in the square this moment. See <see cref="Assembly"/>.</summary>
    private int _here;

    /// <summary>
    /// Whether the call has been opened. A flag rather than "is the squad null", because the squad may be
    /// replaced underneath this errand and the call has still been running the whole time — which is the
    /// distinction the first version of this got wrong and paid for in every march it made.
    /// </summary>
    private bool _mustering;

    private long _musteredTick;

    private Point3D _muster;

    /// <summary>When the island was last swept for bodies to call up. See <see cref="SweepMs"/>.</summary>
    private long _sweptTick;

    public BotHarrow(Map map, Point3D square, int dead)
    {
        _map = map;
        _square = square;
        _dead = dead;
        _began = Core.TickCount;
    }

    public static string Describe() =>
        $"{Musters} musters called and {Called} bots called up, {Marches} of them marched, {Undermanned} could not raise {Least} bodies in {MusterMs / 60000} minutes of calling, {Emptied} grounds emptied and {Timedout} run out of time, {Killed} things killed on them";

    public override string Kind => Trade;

    public override Map Map => _map;

    public override Point3D Where => _square;

    public override double Expects => Prior;

    public override double Minutes => WorkMinutes;

    /// <summary>
    /// Nothing. The fights inside a harrowing are the shard's ordinary combat and are already credited where
    /// they happen; naming a skill here would be claiming the same gain twice.
    /// </summary>
    public override SkillName? Trains => null;

    public override int Outlay => 0;

    /// <summary>
    /// What the five of them have carried away since the company formed.
    ///
    /// See the note at the top: the Baron's own pack is empty at the end of every harrowing by design, and a
    /// measure that read his pack would teach the ledger that this work is worthless.
    /// </summary>
    public override int Made => _squad == null ? 0 : (int)Math.Max(0, _squad.Won - _won);

    public override bool Alongside => true;

    public override string Stage =>
        !_marching
            ? $"calling for volunteers to harrow ({_square.X}, {_square.Y}), where {_dead} have died: {_called} so far"
            : !_standing
                ? $"marching {_called} of us on ({_square.X}, {_square.Y}), where {_dead} have died"
                : $"harrowing ({_square.X}, {_square.Y}) with {_called} of us, {_kills} of {Quota} down";

    public override BotDoing Advance(IBotWilful bot)
    {
        var body = bot?.Self;

        if (body == null || _map == null || _map == Map.Internal)
        {
            return BotDoing.Failed("no body");
        }

        if (bot is not IBotSquadMember member)
        {
            return BotDoing.Failed("not the sort of thing that leads companies");
        }

        return _marching ? Harrowing(member, body) : Calling(member, body);
    }

    /// <summary>
    /// Standing in the square and calling, for five minutes, and then going with whoever came.
    ///
    /// <para>
    /// <b>A muster, not a headcount, and the difference is the whole of why this class does anything at
    /// all.</b> The first version of this asked how many free bots were standing within forty tiles at the
    /// instant the offer was weighed, and refused outright if that was fewer than six. Read as arithmetic
    /// that is correct; read as behaviour it is a Baron who never leaves town, because "five idle bots in one
    /// place in one second" is a coincidence and not a state a working population is often in. Nobody was
    /// asked. Nobody was given the chance to finish what they were doing and come.
    /// </para>
    ///
    /// <para>
    /// So he goes to the square, and he calls, and the population walks past him — to the bank, to the
    /// shops, home from a hunt — and whoever is free when they pass falls in. Five minutes of that is worth
    /// more than any instant's census. It ends early on a full company, because five bots standing about
    /// waiting for a clock is exactly the waste this was meant to avoid.
    /// </para>
    /// </summary>
    private BotDoing Calling(IBotSquadMember member, Mobile body)
    {
        var squad = member.Squad ?? BotSquads.Form(member);

        if (squad == null)
        {
            return BotDoing.Failed("could not call a company together");
        }

        var now = Core.TickCount;

        // <b>Re-adopted every beat rather than remembered once.</b> A squad is an object, and the one this
        // errand started with can be thrown away underneath it — that is what happened for the whole of
        // 27.08.2026, and the symptom was a march that ended in the same second it began. Holding the
        // reference from the first beat is the bug even now that the cause is fixed: an errand that can only
        // work while nothing else touches the world is an errand waiting for the next thing that does.
        if (!ReferenceEquals(squad, _squad))
        {
            // A different company means a different tally of what has been divided, so the mark it is
            // measured from moves with it. Left behind, Made would read a new squad's takings against an old
            // squad's baseline.
            _squad = squad;
            _won = squad.Won;
        }

        // What this square asks for, which is the ordinary company until one has been lost here. The ladder
        // is the square's own and it is kept by the map, so it survives a night and a restart.
        _wanted = BotQuad.Levy(_map, _square, Company);

        squad.Ceiling = _wanted;
        squad.Charged = true;

        if (!_mustering)
        {
            _mustering = true;
            _musteredTick = now;

            _muster = Rally(body);

            Musters++;

            logger.Information(
                "{Name} is calling for volunteers at ({MX}, {MY}) to harrow ({X}, {Y}), where {Dead} have died",
                body.Name,
                _muster.X,
                _muster.Y,
                _square.X,
                _square.Y,
                _dead
            );
        }

        if (now - _sweptTick >= SweepMs)
        {
            _sweptTick = now;

            Levy(squad, body);
        }

        _called = squad.Count;
        _here = Gathered(squad);

        var full = _here >= _wanted;

        if (!full && now - _musteredTick < MusterMs)
        {
            // Stand in the square while the call is open. A walk rather than Work, so that the fifteen-minute
            // jam detector can see this errand doing something — and so that a person watching sees a Baron
            // waiting in a square rather than a Baron frozen wherever the offer found him.
            return BotDoing.Walk(_map, _muster, BotArrival.Within(Station), $"calling for volunteers at ({_muster.X}, {_muster.Y})");
        }

        if (_here < Least)
        {
            Undermanned++;

            // Let go rather than held. A Baron standing about with one volunteer is two bots not working, and
            // the ground is still on the board for the next review.
            BotSquads.Leave(member);

            _squad = null;

            return BotDoing.Failed(
                $"only {_here} of the {Least} were standing in the square for ({_square.X}, {_square.Y}), of {_called} called"
            );
        }

        // <b>Damned ground, and who may walk on it.</b> By order: a square that has swallowed thirty is
        // gathered against only by fifteen grandmasters and four thousand of strength between them. Checked
        // here rather than at the muster because it is a fact about who turned up, and the call stays open
        // while it is not met — a Baron who cannot raise the company he needs waits for it, and the ground is
        // still on the board either way.
        if (BotQuad.Damning(_map, _square) && !Fit(squad, out var ready, out var unarmed))
        {
            Unfit++;

            if (now - _musteredTick < MusterMs)
            {
                return BotDoing.Walk(_map, _muster, BotArrival.Within(Station), $"calling for grandmasters at ({_muster.X}, {_muster.Y})");
            }

            BotSquads.Leave(member);

            _squad = null;

            return BotDoing.Failed(
                $"({_square.X}, {_square.Y}) is damned ground and {ready} of the {Grandmasters} answered fit for it,"
                + $" each needing a skill at {GrandmasterAt:F0} and {Might:F0} of strength"
                + (unarmed > 0 ? $"; {unarmed} more were casters with fewer than {Reagents} reagents" : "")
            );
        }

        _marching = true;
        _marched = _here;

        Marches++;

        // Its own finder, because the ordinary one refuses anything a single bot could handle — which is
        // correct for a muster and is the exact opposite of this errand. Set now rather than at the muster,
        // so that a company standing in a town square is not quietly hunting the town.
        squad.Quarry = Prey;

        // Stamped when the company actually sets out. The forty minutes are the harrowing's, and a clock that
        // started when the call opened would spend a eighth of them standing in a square.
        _began = now;

        // <b>Both numbers, because the rule between them is the one that changed.</b> The Baron now waits for
        // bodies standing in the square rather than for names on a list — see Assembly — and a line that
        // printed only one of the two could not show whether he waited or whether nobody was late. On
        // 02.09.2026 the first march after the change read "6 of them" and told nobody that all six had
        // spawned on the muster point a second earlier, which is a true sentence about an untested rule.
        logger.Information(
            "{Name} is marching {Count} of the {Called} called on ({X}, {Y}), where {Dead} have died, after {Waited:F1} minutes of calling",
            body.Name,
            _here,
            _called,
            _square.X,
            _square.Y,
            _dead,
            (now - _musteredTick) / 60000.0
        );

        return BotDoing.Walk(_map, _square, BotArrival.Within(Side / 3), $"marching on ({_square.X}, {_square.Y})");
    }

    private BotDoing Harrowing(IBotSquadMember member, Mobile body)
    {
        var squad = member.Squad;

        if (squad == null || !ReferenceEquals(squad, _squad))
        {
            return BotDoing.Done("the company broke up on the road");
        }

        _called = squad.Count;

        if (_called < 2)
        {
            // <b>Lost whole, and the map is told the size of what it took.</b> The next levy climbs by
            // BotQuad.Reinforcement and a loss of BotQuad.DireLoss damns the ground outright — Patrick's
            // order of 02.09.2026, that the crown keeps calling five more until the victory is won. Counted
            // from what marched rather than from what is standing, because what is standing is the point.
            if (_marched > 0)
            {
                BotQuad.LostCompany(_map, _square, _marched);
            }

            return Finish(squad, "there was nobody left to harrow with", cleared: false);
        }

        Count(squad);

        if (_kills >= Quota)
        {
            Emptied++;

            return Finish(squad, $"{_kills} of them are dead and ({_square.X}, {_square.Y}) is done", cleared: true);
        }

        var now = Core.TickCount;

        if (now - _began >= CapMs)
        {
            Timedout++;

            // Cleared even so, and that is the order rather than an oversight. Half an hour of six bots
            // walking a box seventy-five tiles across is the ground having been dealt with as thoroughly as
            // this shard knows how; leaving the dead on the count afterwards would send the same company
            // back to the same coordinates for ever, because the dead are the one number that never fades.
            return Finish(squad, $"{CapMs / 60000} minutes on ({_square.X}, {_square.Y}) was enough at {_kills} down", cleared: true);
        }

        // Two distances rather than one, and the gap is what keeps the company on the ground. Arriving is a
        // third of a side; leaving is the whole of it, because a Baron who steps out of the box after
        // something that hit him has not abandoned the errand. Judged on one number this flaps, and the
        // patrol paid for that lesson on 26.08.2026 — see BotSweep.
        var away = _standing ? Side : Side / 2;

        if (!body.InRange(_square, away))
        {
            _standing = false;

            return BotDoing.Walk(_map, _square, BotArrival.Within(Side / 3), $"marching on ({_square.X}, {_square.Y})");
        }

        if (!_standing)
        {
            _standing = true;
            _steppedTick = now;
            _round = 0;
            _post = Post(_round);

            logger.Information(
                "{Name}'s company is on ({X}, {Y}) and has begun walking it",
                body.Name,
                _square.X,
                _square.Y
            );
        }

        // It walks rather than standing in the middle, for the two reasons the patrol has: a box this wide is
        // not covered by a company parked at its centre, and an errand that only ever answers Work is
        // invisible to BotWill.LabourMs, which fails anything that has done nothing else for a quarter of an
        // hour. This errand's own cap is forty minutes, so the two clocks could not both be obeyed — and a
        // failure marks the ground with caution, which would teach the whole population to avoid the very
        // square the company was sent to empty.
        if (now - _steppedTick >= RoundMs || body.InRange(_post, 1))
        {
            _steppedTick = now;
            _bends = 0;
            _post = Post(++_round);
        }

        return BotDoing.Walk(
            _map,
            _post,
            BotArrival.Within(1),
            $"harrowing ({_square.X}, {_square.Y}), {_kills} of {Quota} down"
        );
    }

    /// <summary>
    /// Corpses, counted off the company's own focus.
    ///
    /// <para>
    /// <b>A kill is the thing the company was fighting ceasing to exist, and nothing else.</b> Counted per
    /// beat while in contact, one skirmish reads as hundreds — the patrol reported 1064 fights for a single
    /// half hour on 25.08.2026 and the number could not be acted on. Counted on the focus changing, a
    /// creature that simply walked away counts as dead. So the remembered focus is only ever credited when
    /// it is gone or down, and a focus that is replaced while still alive is replaced silently.
    /// </para>
    /// </summary>
    private void Count(BotSquad squad)
    {
        if (_quarry != null && (_quarry.Deleted || !_quarry.Alive))
        {
            _kills++;
            Killed++;
            _quarry = null;
        }

        var focus = squad.Focus;

        if (focus is { Deleted: false, Alive: true } && !ReferenceEquals(focus, _quarry))
        {
            _quarry = focus;
        }
    }

    /// <summary>
    /// Where the company forms up: the named square, or the population's home when none is named.
    ///
    /// Not called <c>Where</c>: that name is already the undertaking's own — the place the work happens —
    /// and for the whole of the muster the two are different places.
    ///
    /// Settled on the ground rather than taken as written, because a coordinate out of a configuration file
    /// is two numbers and a guess at the third — the fault that put <c>(x, y, 0)</c> across the whole peril
    /// map and failed ten patrols in a night.
    /// </summary>
    private Point3D Rally(Mobile body)
    {
        // <b>The edge of the town in the direction of the ground, before the town square.</b> Patrick's
        // order of 03.09.2026, and the reason is the march: calling the levy on Britain's square puts six
        // bots in the middle of a town they must then cross before they have gone anywhere. The gate on the
        // right side is the shortest honest place to meet, and more of the population passes it. Outside a
        // town Gate answers with nothing and the named square stands, exactly as it did.
        var gate = BotPopulation.Gate(_map, body.Location, _square);

        if (gate != Point3D.Zero)
        {
            return gate;
        }

        var named = Square != Point3D.Zero ? Square : BotPopulation.Where;

        if (BotStep.Settle(_map, named.X, named.Y, out var z))
        {
            return new Point3D(named.X, named.Y, z);
        }

        return named != Point3D.Zero ? named : body.Location;
    }

    /// <summary>
    /// Calls up the nearest bodies that can fight, whatever they were doing.
    ///
    /// <para>
    /// <b>Nobody is asked, and this is the one place on the shard where that is true.</b> Every other company
    /// here is made of bots that were free and came because they were free — the patrol says so at length and
    /// it is right to. A harrowing cannot be built that way and the evening of 27.08.2026 proved it: the
    /// Baron stood in the square for the full five minutes and not one bot was idle at the moment he looked.
    /// A population that is working well is a population with no volunteers in it.
    /// </para>
    ///
    /// <para>
    /// So it is a levy. What the bot was doing is set aside rather than thrown away — being in a company puts
    /// it on the <c>Bound</c> rung and its own errand is still underneath when the company ends — which is
    /// the same thing that happens to anybody who joins a muster, and is why this costs the population an
    /// interruption rather than a job.
    /// </para>
    ///
    /// <para>
    /// <b>The producers are exempt, by order, and it is the right exemption.</b> A smith, a miner and the
    /// Architect are the three trades whose work is a chain — ore to ingots to armour, on the board, for
    /// somebody else — and breaking one link idles everything downstream of it. They are also the three worst
    /// at the thing they would be called up to do.
    /// </para>
    /// </summary>
    private void Levy(BotSquad squad, Mobile body)
    {
        if (squad.Count >= Wanted)
        {
            return;
        }

        _muster = _muster != Point3D.Zero ? _muster : Rally(body);

        // The quota first, nearest of each, then anybody at all to fill the rest. Two passes over one
        // gathered list rather than four spatial sweeps: the sweep is the expensive half.
        _called0.Clear();

        foreach (var mobile in _map.GetMobilesInRange<Mobile>(_muster, Reach))
        {
            if (mobile == body || mobile is not BotMobile other)
            {
                continue;
            }

            if (other.Squad != null || other is not IBotAlly { AbleToFight: true })
            {
                continue;
            }

            if (other.Class is not { } klass || klass.Role == BotRole.Producer)
            {
                continue;
            }

            _called0.Add(other);
        }

        _called0.Sort((a, b) => Apart(a, body).CompareTo(Apart(b, body)));

        Take(squad, BotRole.Melee, Melee);
        Take(squad, BotRole.Ranged, Ranged);
        Take(squad, BotRole.Medic, Medics);
        Take(squad, null, Wanted);
    }

    /// <summary>
    /// Whether this company may walk on ground the map has damned: grandmasters enough, and strength enough.
    ///
    /// <para>
    /// A grandmaster is a bot with any skill at the era's own ceiling, which is what the word means on this
    /// shard and is not a number this file gets to invent. Strength is the population's own reckoning of
    /// fighting power — see <c>BotThreat.Power</c>, health times what it hits for — added up across whoever
    /// answered, because that is the thing the ground will be measured against.
    /// </para>
    /// </summary>
    private static bool Fit(BotSquad squad, out int ready, out int unarmed)
    {
        ready = 0;
        unarmed = 0;

        if (squad == null)
        {
            return false;
        }

        var members = squad.Members;

        for (var i = 0; i < members.Count; i++)
        {
            var body = members[i]?.Self;

            if (body is not { Deleted: false, Alive: true })
            {
                continue;
            }

            // Each of the three, of the same bot. A company that averages the requirement is a company where
            // six carry the other nine, and the ground has already shown what it does to those nine.
            if (!Master(body) || BotThreat.Power(body) < Might)
            {
                continue;
            }

            if (BotGrimoire.Book(body) != null && Herbs(body) < Reagents)
            {
                unarmed++;

                continue;
            }

            ready++;
        }

        return ready >= Grandmasters;
    }

    /// <summary>How many of the company are actually standing in the square. See <see cref="Assembly"/>.</summary>
    private int Gathered(BotSquad squad)
    {
        if (squad == null || _muster == Point3D.Zero)
        {
            return 0;
        }

        var here = 0;
        var members = squad.Members;

        for (var i = 0; i < members.Count; i++)
        {
            var body = members[i]?.Self;

            if (body is { Deleted: false, Alive: true } && body.Map == _map && body.InRange(_muster, Assembly))
            {
                here++;
            }
        }

        return here;
    }

    /// <summary>Whether any of this bot's skills stands at the era's ceiling.</summary>
    private static bool Master(Mobile body)
    {
        var skills = body?.Skills;

        if (skills == null)
        {
            return false;
        }

        for (var i = 0; i < skills.Length; i++)
        {
            if (skills[i].Base >= GrandmasterAt)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>How many reagents of any kind are in the pack. One type check covers every herb in the era.</summary>
    private static int Herbs(Mobile body)
    {
        var pack = body?.Backpack;

        if (pack == null)
        {
            return 0;
        }

        var held = 0;

        foreach (var item in pack.Items)
        {
            if (item is BaseReagent { Deleted: false } herb)
            {
                held += herb.Amount;
            }
        }

        return held;
    }

    /// <summary>Musters turned away from damned ground for want of grandmasters. See <see cref="Fit"/>.</summary>
    public static long Unfit { get; private set; }

    /// <summary>Fills up to <paramref name="most"/> places from the gathered list, nearest first.</summary>
    private void Take(BotSquad squad, BotRole? role, int most)
    {
        var taken = 0;

        for (var i = 0; i < _called0.Count && taken < most && squad.Count < Wanted; i++)
        {
            var other = _called0[i];

            if (other.Squad != null || role != null && other.Class?.Role != role)
            {
                continue;
            }

            if (BotSquads.Join(squad, other))
            {
                taken++;
                Called++;
            }
        }
    }

    private static int Apart(Mobile a, Mobile b) =>
        Math.Max(Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));

    /// <summary>
    /// Bodies gathered by one sweep. Static and reused: the levy runs on the population's beat and a fresh
    /// list every two seconds is garbage for nothing.
    /// </summary>
    private static readonly System.Collections.Generic.List<BotMobile> _called0 = [];

    /// <summary>Bots called up, all told. What a harrowing costs the rest of the island.</summary>
    public static long Called { get; private set; }

    /// <summary>
    /// Everything alive inside the box that is not a bot, a shopkeeper, somebody's pet or standing on
    /// guarded ground.
    ///
    /// <para>
    /// <b>Nearest, not strongest, and that is the order as given.</b> Everything that moves, so there is
    /// nothing to rank: the company works outwards from wherever it stands and the box empties from the
    /// inside. Bots need no exclusion of their own — they are players to the engine and this only ever asks
    /// about creatures — which is a far better guarantee than a rule somebody has to remember to write.
    /// </para>
    ///
    /// <para>
    /// <b>The town is excluded on the creature rather than on the leader.</b> Asked of the Baron, the answer
    /// would be "he is outside a town, so anything goes" — and a thing standing three tiles inside the gate
    /// would be attacked by six bots in front of the guards. Asked of the creature, the gate is the line.
    /// </para>
    /// </summary>
    private BaseCreature Prey(Mobile leader)
    {
        var map = leader?.Map;

        if (map == null || map == Map.Internal || map != _map)
        {
            return null;
        }

        BaseCreature nearest = null;
        var closest = int.MaxValue;
        var edge = Side / 2;

        foreach (var creature in map.GetMobilesInRange<BaseCreature>(leader.Location, Sight))
        {
            if (creature is not { Deleted: false, Alive: true } or BaseVendor)
            {
                continue;
            }

            if (creature.Controlled || creature.Summoned || creature.IsDeadBondedPet)
            {
                continue;
            }

            // Outside the ground the company was sent to. Without this the box is a suggestion: one thing
            // running north takes six bots with it and the square is never walked.
            if (Math.Abs(creature.X - _square.X) > edge || Math.Abs(creature.Y - _square.Y) > edge)
            {
                continue;
            }

            if (creature.Region?.IsPartOf<GuardedRegion>() == true)
            {
                continue;
            }

            if (!leader.CanBeHarmful(creature, false))
            {
                continue;
            }

            var apart = Math.Max(Math.Abs(creature.X - leader.X), Math.Abs(creature.Y - leader.Y));

            if (apart >= closest)
            {
                continue;
            }

            closest = apart;
            nearest = creature;
        }

        return nearest;
    }

    /// <summary>
    /// The next place inside the box to walk to: the middle and the four corners, in turn, each on its own
    /// ground.
    ///
    /// A corner's height is looked up rather than carried sideways from the middle — a box this wide crosses
    /// hillsides, and a walk to a corner at the middle's height is a walk to nowhere. That fault put
    /// <c>(x, y, 0)</c> on the whole peril map and failed ten patrols in one night.
    /// </summary>
    private Point3D Post(int round)
    {
        var reach = Math.Max(2, Side / 3);

        var (dx, dy) = (round % 5) switch
        {
            0 => (0, 0),
            1 => (-reach, -reach),
            2 => (reach, -reach),
            3 => (reach, reach),
            _ => (-reach, reach)
        };

        var x = _square.X + dx;
        var y = _square.Y + dy;

        return BotStep.Settle(_map, x, y, out var z) ? new Point3D(x, y, z) : _square;
    }

    private BotDoing Finish(BotSquad squad, string why, bool cleared)
    {
        if (cleared)
        {
            // The whole box, not the cell at its middle. The company walked all of it; clearing one cell
            // would offer the Baron the next cell of the ground he has just spent the afternoon on.
            BotPeril.Cleared(_map, _square, Side / 2);

            // <b>And the standing reputation of the ground, which is the map the Baron is now sent by.</b>
            // The square he was sent to and the eight around it, because the box he walks is Side tiles
            // across and a quadrant is BotQuad.Side — so the neighbours are ground his company genuinely
            // covered, not ground being cleared on his behalf. Set to nothing rather than to safe: what a
            // company killed everything in is not safe, nobody has walked it since. See BotQuad.Cleared.
            var quad = BotQuad.Known(_map, _square);

            if (quad != null)
            {
                var around = BotQuad.Around(quad, madeIfNew: false);

                for (var i = 0; i < around.Count; i++)
                {
                    BotQuad.Cleared(around[i]);
                }
            }
        }

        Release(squad);

        return BotDoing.Done($"{why} — {_called} of us, {Made}gp between them");
    }

    /// <summary>The way to the next corner does not exist. Somewhere else inside the same box, or give up.</summary>
    public override bool Bend(IBotWilful bot)
    {
        if (!_standing || ++_bends > MaxBends)
        {
            BotPeril.Baulked(_map, _square);

            return false;
        }

        _steppedTick = Core.TickCount;
        _post = Post(++_round);

        return true;
    }

    /// <summary>
    /// Everything the charge switched on, switched off, whichever way the errand ended.
    ///
    /// Left set, a company whose Baron died would stand in a wood until the world was reloaded: the quiet
    /// clock is the only thing that disbands a squad nobody is fighting, and the charge is exactly what
    /// turns it off.
    /// </summary>
    public override void Drop(IBotWilful bot)
    {
        Release(_squad);

        if (bot is IBotSquadMember member && member.Squad != null && ReferenceEquals(member.Squad, _squad))
        {
            BotSquads.Leave(member);
        }

        _squad = null;
        _marching = false;
        _mustering = false;
    }

    private void Release(BotSquad squad)
    {
        if (squad == null)
        {
            return;
        }

        squad.Charged = false;
        squad.Quarry = null;
        squad.Ceiling = BotSquad.MaxSize;
    }

    public static void Forget()
    {
        Marches = 0;
        Musters = 0;
        Called = 0;
        Undermanned = 0;
        Emptied = 0;
        Timedout = 0;
        Killed = 0;
    }
}
