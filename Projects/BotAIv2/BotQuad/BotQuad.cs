using System;
using System.Collections.Generic;
using Server.Logging;
using Server.Mobiles;

namespace Server.BotAI.V2;

/// <summary>
/// The island cut into squares thirty tiles across, each carrying one number: how safe the population has
/// found it to be.
///
/// <para>
/// <b>This is a different question from <see cref="BotPeril"/>'s and the two must not be merged.</b> Peril
/// answers "where is it dangerous <em>now</em>" — a decaying frequency of blows, right for a captain deciding
/// where a company should be standing this minute, and deliberately forgetful, because a graveyard that was
/// terrible an hour ago and quiet since is not where anybody should be sent. This answers "what sort of
/// ground is that" — a standing reputation that a place earns slowly, in both directions, and does not
/// forget on its own. A quiet meadow and a graveyard nobody has visited since the last massacre read the
/// same on Peril's map and must never read the same here.
/// </para>
///
/// <para>
/// <b>Both directions, which is what makes it a reputation rather than a scar.</b> Blows and deaths push a
/// square down; bots walking through it and coming out the other side push it back up. So ground that was
/// cleared genuinely recovers — by being walked, which is evidence — rather than by a clock running out,
/// which is not. A square nothing has happened in for a day reads exactly what it read yesterday, and that
/// is the point: the population's memory of the island should outlive any one session's worth of walking.
/// </para>
///
/// <para>
/// <b>Shared by everybody, kept nowhere else.</b> There is one map and every bot reads and writes the same
/// squares, the way the squad's stations and the market's prices are shared: nothing is messaged anywhere,
/// and two bots asking the same question of the same ground get the same answer by construction.
/// </para>
/// </summary>
public static class BotQuad
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotQuad));

    /// <summary>
    /// How many tiles across one quadrant is.
    ///
    /// <para>
    /// Thirty, by order. It is the scale at which "that part of the island" means something to a person
    /// looking at a map: wide enough that a bot crossing it is genuinely somewhere, narrow enough that a
    /// company sent to one is not being sent to a county. A screen at this era's resolution is about this
    /// wide, so a quadrant is roughly "what a bot can see the far side of".
    /// </para>
    /// </summary>
    public const int Side = 30;

    /// <summary>The safest a square may ever read. Nothing has gone wrong here in a long while.</summary>
    public const double Safest = 1.0;

    /// <summary>The worst a square may ever read.</summary>
    public const double Bleakest = -1.0;

    /// <summary>Where a square starts before anything is known about it. Neither trusted nor feared.</summary>
    public const double Fresh = 0.0;

    /// <summary>
    /// The five bands the reading is spoken in, by Patrick's table of 03.09.2026.
    ///
    /// <para>
    ///   <c>1.00</c> safe · <c>0.01</c> positive · <c>0.00</c> neutral · <c>-0.01</c> unsafe · <c>-1.00</c>
    ///   dangerous.
    /// </para>
    ///
    /// <para>
    /// <b>It also settles where fear begins, which the strength table on its own did not.</b> That table
    /// starts at minus a hundredth and the order that came with it said bots do not fear anything above five
    /// hundredths, leaving the band between charged nothing and named nothing. Here it has a name: it is
    /// positive ground, and positive ground asks nobody for anything. Fear starts exactly where the word
    /// "unsafe" does.
    /// </para>
    /// </summary>
    public const double Positive = 0.01;

    public const double Neutral = 0.0;

    public const double Unsafe = -0.01;

    /// <summary>What to call a reading, in one word, for anybody reading a map or a log line.</summary>
    public static string Band(double safety) =>
        safety >= Safest ? "safe"
        : safety >= Positive ? "positive"
        : safety > Unsafe ? "neutral"
        : safety > Bleakest ? "unsafe"
        : "dangerous";

    /// <summary>
    /// How many uneventful crossings it takes to earn a square a little credit.
    ///
    /// Three, by order, and counted rather than timed: a crossing is a bot going in and coming out with
    /// nothing having happened to it, which is evidence about the ground. Time passing is not.
    /// </summary>
    public static int PerPass { get; set; } = 25;

    /// <summary>What those crossings are worth.</summary>
    public static double PassWorth { get; set; } = 0.05;

    /// <summary>
    /// How many blows landed on bots here before the square is marked down.
    ///
    /// <para>
    /// Two, by Patrick's order on 02.09.2026, and it was five. The population had just been sent to the worst
    /// ground the map knew — (2025, 975), the bog between Britain and Minoc — and what is out there is far
    /// stronger than they are. At five blows a square, the record moved a hundredth at a time while the
    /// company was being taken apart; at two it says so while there is still somebody to tell.
    /// </para>
    /// </summary>
    public static int PerBlows { get; set; } = 5;

    /// <summary>What that many blows costs a square.</summary>
    public static double BlowsWorth { get; set; } = -0.01;

    /// <summary>
    /// How many undisturbed harvests it takes to earn a square a little credit.
    ///
    /// <para>
    /// Fifty, by Patrick's order of 03.09.2026, and it is a second kind of evidence rather than more of the
    /// first. A crossing says a bot walked through and nothing happened; a harvest says a bot <em>stood
    /// still</em> here for a minute with its back to the field and nothing happened, which is a stronger
    /// claim about the ground and rarer, so it is worth a fifth as much and asked for twenty times as often.
    /// </para>
    /// </summary>
    public static int PerHarvest { get; set; } = 50;

    /// <summary>What that many undisturbed harvests are worth.</summary>
    public static double HarvestWorth { get; set; } = 0.01;

    /// <summary>
    /// What one aggressive creature living in a square costs it.
    ///
    /// <para>
    /// <b>The only term here that is about the present rather than the past.</b> Crossings, blows and deaths
    /// are history and stay in the record; this is a count of what is standing in the square now, so it is
    /// held apart from the earned reading and applied when the reading is asked for. Ground the population
    /// has walked a hundred times is still ground with four ogres on it, and the record alone could not say
    /// so — the old map's worst square on the whole island read minus nought point one six.
    /// </para>
    ///
    /// <para>
    /// A tenth each, so two of them cancel every crossing credit a square can hold and ten make it as
    /// dangerous as the map can say.
    /// </para>
    /// </summary>
    public static double MobWorth { get; set; } = -0.1;

    /// <summary>
    /// The reading at or below which a bot has to think about whether it is strong enough to go.
    ///
    /// <para>
    /// Above it there is nothing to weigh: a square this quiet is walked without a thought, which is what
    /// "bots are not afraid of anything above 0.05" means. At or below it the answer comes from
    /// <see cref="Muscle"/>, and it is a wall rather than a discount — a bot that cannot meet it does not go
    /// alone, whatever the work there is worth.
    /// </para>
    /// </summary>
    public static double Fearless { get; set; } = Neutral;

    /// <summary>
    /// What one death costs a square.
    ///
    /// A tenth, which is ten times what two blows cost and is meant to be. Being hit is what happens all
    /// day to a population that fights for a living. A bot that did not come back is the only unambiguous
    /// evidence that ground is beyond whoever went there — the same reasoning, and very nearly the same
    /// ratio, as <see cref="BotPeril.PerDeath"/>.
    /// </summary>
    public static double DeathWorth { get; set; } = -0.05;

    /// <summary>
    /// What the death of a Baron costs a square.
    ///
    /// <para>
    /// Half, by order, and five times an ordinary death. The reasoning is about evidence rather than about
    /// rank: he is the best-armed and best-trained thing this population can put on a field, he is sent to
    /// ground precisely because it is bad, and he is the one who never leaves. Ground that killed <em>him</em>
    /// is ground that will kill anybody, and one such death should say so at once rather than after five.
    /// </para>
    ///
    /// <para>
    /// It is also sized to clear <see cref="Dire"/> in a single step from a square that read nothing at all,
    /// which is deliberate: the square where a Baron fell may have a great hunt raised for it immediately,
    /// with no further evidence needed and nobody having to wait for the map to catch up.
    /// </para>
    /// </summary>
    public static double BaronWorth { get; set; } = -0.5;

    // ---- What the King's Rangers' own evidence is worth, which is not what anybody else's is. ------
    //
    // <b>They are elite, so their survival proves less and their deaths prove more.</b> Five bots in gold
    // plate with a grandmaster surgeon behind them walking out of a quadrant unharmed says nothing about
    // whether a miner could — and it is exactly that inference an ordinary bot's crossing is credited for.
    // So a clean sweep by rangers moves the square not at all: it is marked read and left where it was.
    // Blows landed on them are worth a fifth of an ordinary bot's, because they are in armour and are hit
    // constantly by design. A whole company dying in one square is the strongest single reading this map
    // takes from anybody.

    /// <summary>What a clean ranger sweep is worth. Nothing, by order: see the note above.</summary>
    public static double SweptWorth { get; set; }

    /// <summary>How many blows landed on rangers before the square is marked down.</summary>
    public static int PerRangerBlows { get; set; } = 50;

    /// <summary>What that many blows on rangers costs a square.</summary>
    public static double RangerBlowsWorth { get; set; } = -0.05;

    /// <summary>What one ranger's death costs a square.</summary>
    public static double RangerDeathWorth { get; set; } = -0.1;

    /// <summary>
    /// What the loss of a whole ranger company in one square costs it.
    ///
    /// Half, and it is applied on top of the deaths themselves rather than instead of them: five bodies is
    /// five readings, and the fact that all five fell on the same ground is a sixth reading of its own. It is
    /// sized, like the Baron's, to carry a square past <see cref="Dire"/> from nothing in a single step.
    /// </summary>
    public static double WipedWorth { get; set; } = -0.5;

    /// <summary>Squares the rangers have swept clean, and squares that took a whole company.</summary>
    public static long Sweeps { get; private set; }

    public static long Wiped { get; private set; }

    /// <summary>
    /// Above this a square is too quiet to hunt in.
    ///
    /// <para>
    /// By order, and it is a rule about the population rather than about the ground: a square that has been
    /// walked through fifty times without incident has nothing living in it worth killing, and a hunter
    /// standing in it is a hunter earning nothing. It is the same idea as the crowding discount in
    /// <c>BotAppraisal</c> — a want whose value does not fall as it is satisfied is a want everybody ends up
    /// having — applied to ground instead of to work.
    /// </para>
    /// </summary>
    public static double TooQuiet { get; set; } = 0.5;

    /// <summary>Below this a square is where hunters should be going first.</summary>
    public static double Wanted { get; set; } = -0.1;

    /// <summary>Below this the Baron raises a great hunt for it. See <c>BotHarrow</c>.</summary>
    public static double Dire { get; set; } = -0.3;

    /// <summary>
    /// How many more the crown sends after a company is lost whole in a square.
    ///
    /// <para>
    /// Five, by Patrick's order on 02.09.2026: the Baron calls fighters into a quadrant, and if they all die
    /// he may call five more than that, and again, until the victory is won. A square that eats a company is
    /// not a square to send the same company back to.
    /// </para>
    /// </summary>
    public static int Reinforcement { get; set; } = 5;

    /// <summary>
    /// A loss this size damns the ground outright: the square is set to <see cref="Bleakest"/> and nothing
    /// but a company of grandmasters may be sent to it afterwards. Thirty, by the same order.
    /// </summary>
    public static int DireLoss { get; set; } = 30;

    /// <summary>What that ground reads at, and it is the floor of the scale rather than a step down it.</summary>
    public static double Damned { get; set; } = Bleakest;

    /// <summary>How many the crown should send against this square: the ordinary company, or what it has earned.</summary>
    public static int Levy(Map map, Point3D where, int ordinary)
    {
        var quad = Known(map, where);

        return quad == null ? ordinary : Math.Max(ordinary, quad.Levied);
    }

    /// <summary>Whether this ground may be walked only by a company of grandmasters. See <see cref="DireLoss"/>.</summary>
    public static bool Damning(Map map, Point3D where) => Safety(map, where) <= Damned;

    /// <summary>
    /// A company of the crown was lost whole in this square, and what the crown does about it.
    ///
    /// <para>
    /// Two answers, and they are different sizes of the same one. The next levy is what was lost plus
    /// <see cref="Reinforcement"/>, so the ladder climbs by itself until something comes back alive. And a
    /// loss of <see cref="DireLoss"/> or more damns the ground to <see cref="Damned"/>, from where nothing
    /// but grandmasters may be sent — a square that swallowed thirty is not a square anybody learns about
    /// by sending thirty-five.
    /// </para>
    /// </summary>
    public static void LostCompany(Map map, Point3D where, int lost)
    {
        if (map == null || map == Map.Internal || lost <= 0)
        {
            return;
        }

        var quad = At(map, where);

        if (quad == null)
        {
            return;
        }

        quad.Wipes++;
        quad.Levied = Math.Max(quad.Levied, lost) + Reinforcement;
        quad.Tick = Core.TickCount;

        Wiped++;

        if (lost >= DireLoss)
        {
            quad.Safety = Damned;

            logger.Warning(
                "A company of {Lost} was lost whole around ({X}, {Y}); the ground is damned at {Safety:F2} and only grandmasters may be sent to it",
                lost,
                quad.Middle.X,
                quad.Middle.Y,
                quad.Safety
            );

            return;
        }

        quad.Safety = Math.Clamp(quad.Safety + WipedWorth, Bleakest, Safest);

        logger.Warning(
            "A company of {Lost} was lost whole around ({X}, {Y}); the ground now reads {Safety:F2} and the next levy is {Levy}",
            lost,
            quad.Middle.X,
            quad.Middle.Y,
            quad.Safety,
            quad.Levied
        );
    }

    /// <summary>What a square reads after a great hunt has been through it: neither trusted nor feared.</summary>
    public static double Harrowed { get; set; } = 0.0;

    /// <summary>
    /// How long a square nobody could get near is left off the hunting list, per proof.
    ///
    /// <para>
    /// Multiplied by how many times it has been proved, so a square that beats one bot is rested and a square
    /// that beats five is effectively retired. That is the whole of the tuning here: the first baulk is
    /// ambiguous — a bot may simply have been interrupted — and the fifth is a fact about the island.
    /// </para>
    ///
    /// <para>
    /// <b>Ten minutes is what BotPeril uses for the same idea and it is far too short for this one.</b> A
    /// trek to the far edge of the roam takes several minutes, so within one such window the whole population
    /// sets out, walks three hundred tiles, and gives up one after another. On 03.09.2026 between 12:54 and
    /// 13:15 the quadrant record answered (1005, 1335) to every hunter that asked, because it answers from
    /// the population's home rather than from the bot's own feet, and 119 prowls ended "got no nearer than
    /// 156 tiles" to that one square. Hunting finished 158 times in the previous run of the shard and 52 in
    /// that one.
    /// </para>
    /// </summary>
    public static int BaulkMs { get; set; } = 600000;

    /// <summary>The most that multiplies to, so nothing is retired for ever on evidence that can go stale.</summary>
    public static int MostBaulks { get; set; } = 12;

    /// <summary>Squares rested because nobody could get near them. For the summary.</summary>
    public static long Baulked { get; private set; }

    /// <summary>
    /// Somebody set out for this square and could not get near it.
    ///
    /// <para>
    /// The reading is <b>not</b> touched, exactly as BotPeril.Baulked does not touch its own: the square is
    /// as dangerous as it was, and saying otherwise would be a lie told to every bot that reads this map.
    /// What is learned is that the road there does not work, which is a different fact and belongs in a
    /// different field.
    /// </para>
    /// </summary>
    public static void Baulk(Map map, Point3D where)
    {
        var quad = At(map, where);

        if (quad == null)
        {
            return;
        }

        if (quad.Baulks < MostBaulks)
        {
            quad.Baulks++;
        }

        quad.BaulkedTick = Core.TickCount;
        Baulked++;
    }

    /// <summary>Whether this square is resting after somebody failed to reach it.</summary>
    private static bool Resting(Quad quad, long now) =>
        quad.Baulks > 0 && now - quad.BaulkedTick < (long)BaulkMs * quad.Baulks;

    /// <summary>
    /// Most squares remembered at once.
    ///
    /// Felucca is 6144 by 4096, which is 205 by 137 quadrants — twenty-eight thousand of them, and a
    /// population of thirty will never stand in most of them. The cap is a backstop against a bug, not a
    /// budget: at four thousand it holds every square this shard's bots have ever been near, several times
    /// over, and each is a handful of numbers.
    /// </summary>
    public static int Most { get; set; } = 4096;

    /// <summary>One square of the island, and everything the population knows about it.</summary>
    public sealed class Quad
    {
        public Map Map;

        /// <summary>The square's own coordinates, in quadrants rather than tiles.</summary>
        public int X;

        public int Y;

        /// <summary>How safe it has been found to be, from <see cref="Bleakest"/> to <see cref="Safest"/>.</summary>
        public double Safety;

        /// <summary>Crossings that came to nothing, all told.</summary>
        public int Passes;

        /// <summary>
        /// How many the crown should send against this square next time, or nought for the ordinary company.
        ///
        /// <para>
        /// By Patrick's order on 02.09.2026: "the Baron calls fighters into a quadrant; if they all die he
        /// may call five more than that, and again, until the victory is won."
        /// </para>
        /// </summary>
        public int Levied;

        /// <summary>Companies of the crown lost whole in this square.</summary>
        public int Wipes;

        /// <summary>Crossings counted towards the next step up. Reset each time one is awarded.</summary>
        public int Towards;

        /// <summary>Blows landed on bots here, all told.</summary>
        public int Blows;

        /// <summary>How many times somebody set out for this square and could not get near it.</summary>
        public int Baulks;

        /// <summary>When the last of those was.</summary>
        public long BaulkedTick;

        /// <summary>Blows counted towards the next step down.</summary>
        public int Bruising;

        /// <summary>Harvests this square has seen finished on it.</summary>
        public int Harvests;

        /// <summary>How many of those since the last credit, or since the last blow landed here.</summary>
        public int Reaping;

        /// <summary>
        /// Aggressive creatures counted in this square the last time anybody looked.
        ///
        /// <para>
        /// Not history and never added up: it is replaced by each look, because what is wanted is how many
        /// things are living here now. Unique by the count itself — one sweep of the square cannot see the
        /// same creature twice — which is what "each unique enemy is written into the square's count" asks
        /// for without a set of serials per square and the bookkeeping that would need.
        /// </para>
        /// </summary>
        public int Mobs;

        /// <summary>When that count was taken, so a stale one can be let go of.</summary>
        public long Sighted;

        /// <summary>Blows landed on the crown's rangers here, counted towards their own coarser step.</summary>
        public int RangerBruising;

        /// <summary>Rangers who died here.</summary>
        public int RangersLost;

        /// <summary>Whether the King's Rangers have swept this square and come out of it clean.</summary>
        public bool Swept;

        /// <summary>Bots that died here.</summary>
        public int Deaths;

        /// <summary>Whether anybody has ever actually stood in it. A square can be talked about before that.</summary>
        public bool Trodden;

        /// <summary>When it was last touched by anything at all.</summary>
        public long Tick;

        /// <summary>When a great hunt last finished here, or nought.</summary>
        public long HarrowedTick;

        /// <summary>The middle of the square, in tiles. The height is settled, never invented.</summary>
        public Point2D Middle => new(X * Side + Side / 2, Y * Side + Side / 2);

        public override string ToString() =>
            $"({Middle.X}, {Middle.Y}) at {Safety:F2} on {Passes} crossings, {Blows} blows and {Deaths} dead";
    }

    private static readonly Dictionary<(int Map, int X, int Y), Quad> _quads = [];

    /// <summary>Every square the population has anything to say about.</summary>
    public static IReadOnlyCollection<Quad> All => _quads.Values;

    /// <summary>The squares that have hurt somebody, worst first. Rebuilt on demand, rarely. See <see cref="WorstNear"/>.</summary>
    private static readonly List<Quad> _feared = [];

    private static long _fearedTick;

    private static bool _fearedEver;

    /// <summary>How long the short list of feared squares stands before it is built again.</summary>
    public static int FearedRefreshMs { get; set; } = 15000;

    /// <summary>Most feared squares kept on that list.</summary>
    public static int MostFeared { get; set; } = 16;

    /// <summary>
    /// The worst square the population knows of within reach of somewhere, or nought if it knows of none.
    ///
    /// <para>
    /// <b>This map was answering only half the question it was built for.</b> Until 02.09.2026 a hunter asked
    /// it one thing — "is this square I happened to think of too quiet" — and it was consulted nowhere else,
    /// so a map that knew twelve squares had hurt somebody and three were dire could not send anybody to one
    /// of them. The beat said it in a number every window and nobody had the second half to compare it to:
    /// "15125 hunting grounds passed over as too quiet, <b>0 picked for having hurt somebody</b>". The squares
    /// were found by throwing eight darts into a box around home and were then judged; the ones that mattered
    /// were never on the list to be judged.
    /// </para>
    ///
    /// <para>
    /// Patrick's reading of it, which is the reason this exists: "they take too small a distance — nearer the
    /// swamps between Britain and Minoc there is a great deal that is dangerous, and they only walk the safe
    /// ground and hang about there. We brought in the quadrants for exactly this, so they would move from the
    /// safe towards the dangerous and gather into companies."
    /// </para>
    ///
    /// <para>
    /// The short list is rebuilt at most every <see cref="FearedRefreshMs"/> — the full record is thousands of
    /// squares and this is asked on the population's own beat, once per hunter per decision.
    /// </para>
    /// </summary>
    public static Point2D WorstNear(Map map, Point3D from, int within)
    {
        if (map == null || map == Map.Internal)
        {
            return Point2D.Zero;
        }

        var now = Core.TickCount;

        if (!_fearedEver || now - _fearedTick >= FearedRefreshMs)
        {
            _fearedEver = true;
            _fearedTick = now;
            _feared.Clear();

            foreach (var quad in _quads.Values)
            {
                if (quad.Safety <= Wanted)
                {
                    _feared.Add(quad);
                }
            }

            _feared.Sort(static (a, b) => a.Safety.CompareTo(b.Safety));

            if (_feared.Count > MostFeared)
            {
                _feared.RemoveRange(MostFeared, _feared.Count - MostFeared);
            }
        }

        for (var i = 0; i < _feared.Count; i++)
        {
            var quad = _feared[i];

            if (quad.Map != map)
            {
                continue;
            }

            // Somebody has already walked at this one and could not get near it. The list is answered from
            // the population's home rather than from any one bot's feet, so without this every hunter on the
            // shard is handed the same unreachable square, one after another, for as long as it stays the
            // worst thing on the map.
            if (Resting(quad, now))
            {
                continue;
            }

            var middle = quad.Middle;

            // Worst first, so the first one near enough is the answer. Distance is measured from wherever the
            // caller reckons its ground from, which for a hunter is the population's home rather than its own
            // feet: a company that walks out together starts from the same place.
            //
            // The height is deliberately not returned. A square's middle is two numbers and the third has to
            // come from the map itself — see BotStep.Settle, and the evening this project spent walking bots
            // to a Z somebody had worked out with arithmetic.
            if (Math.Abs(middle.X - from.X) <= within && Math.Abs(middle.Y - from.Y) <= within)
            {
                return middle;
            }
        }

        return Point2D.Zero;
    }

    public static int Count => _quads.Count;

    // ---- What has happened, for the summary. ------------------------------------------------------

    /// <summary>Squares that have been raised a step by being walked through.</summary>
    public static long Credited { get; private set; }

    /// <summary>Squares marked down for blows.</summary>
    public static long Marked { get; private set; }

    /// <summary>Squares marked down for a death.</summary>
    public static long Mourned { get; private set; }

    /// <summary>Squares set foot in for the very first time.</summary>
    public static long Discovered { get; private set; }

    /// <summary>Squares a great hunt has finished with.</summary>
    public static long Cleansed { get; private set; }

    /// <summary>Which square a tile falls in.</summary>
    public static (int Map, int X, int Y) Key(Map map, Point3D where) =>
        (map?.MapID ?? -1, Floor(where.X), Floor(where.Y));

    /// <summary>
    /// Division that keeps going the same way below nought.
    ///
    /// C# truncates towards zero, so tiles -1 and +1 would both land in quadrant 0 and the square either side
    /// of an axis would be twice as wide as every other. No bot walks negative coordinates on Felucca today,
    /// which is exactly why this would never be noticed until something did.
    /// </summary>
    private static int Floor(int tile) => (int)Math.Floor(tile / (double)Side);

    /// <summary>The square this tile is in, made if it is new to the population.</summary>
    public static Quad At(Map map, Point3D where)
    {
        if (map == null || map == Map.Internal)
        {
            return null;
        }

        var key = Key(map, where);

        if (_quads.TryGetValue(key, out var quad))
        {
            return quad;
        }

        if (_quads.Count >= Most)
        {
            return null;
        }

        quad = new Quad
        {
            Map = map,
            X = key.X,
            Y = key.Y,
            Safety = Fresh,
            Tick = Core.TickCount
        };

        _quads[key] = quad;

        return quad;
    }

    /// <summary>The square this tile is in, or nothing when the population has never touched it.</summary>
    public static Quad Known(Map map, Point3D where) =>
        map == null || map == Map.Internal ? null : _quads.GetValueOrDefault(Key(map, where));

    /// <summary>
    /// What this ground reads, or <see cref="Fresh"/> where nothing is known.
    ///
    /// <para>
    /// <b>Two things added together, and they are kept apart on purpose.</b> The earned reading is history —
    /// crossings, harvests, blows, deaths — and belongs in the record. What is standing in the square right
    /// now is not history and must not be written into it, or a square would carry the ghosts of every ogre
    /// that ever walked through. So the creatures are counted separately, kept only as long as the count is
    /// fresh, and applied here.
    /// </para>
    /// </summary>
    public static double Safety(Map map, Point3D where)
    {
        var quad = Known(map, where);

        if (quad == null)
        {
            return Fresh;
        }

        return Math.Clamp(quad.Safety + MobWorth * Living(quad), Bleakest, Safest);
    }

    /// <summary>
    /// How long a count of creatures stands before the square is treated as empty again.
    ///
    /// Five minutes. Creatures wander and die; a count nobody has refreshed since before that is a statement
    /// about a square that has since had time to empty, and holding it would be the same mistake as writing
    /// the creatures into the record.
    /// </summary>
    public static int SightMs { get; set; } = 300000;

    /// <summary>
    /// What one square reads, creatures and all. For anybody holding the square already.
    ///
    /// The pins had been printing the earned reading alone, so the one term that says a square has four
    /// ogres living in it was the one term the map did not show.
    /// </summary>
    public static double Reading(Quad quad) =>
        quad == null ? Fresh : Math.Clamp(quad.Safety + MobWorth * Living(quad), Bleakest, Safest);

    /// <summary>What was counted in this square, if anybody has looked lately.</summary>
    private static int Living(Quad quad) =>
        quad.Mobs > 0 && Core.TickCount - quad.Sighted < SightMs ? quad.Mobs : 0;

    /// <summary>What the record alone says, with nothing living counted. For the summary and the pins.</summary>
    public static double Earned(Map map, Point3D where) => Known(map, where)?.Safety ?? Fresh;

    /// <summary>
    /// How many aggressive creatures are standing in this square, as somebody has just counted them.
    ///
    /// <para>
    /// Replaces rather than adds. One sweep cannot see the same creature twice, so the count it hands over
    /// is already unique, and two sweeps of the same square are two answers to one question rather than two
    /// halves of it.
    /// </para>
    /// </summary>
    public static void Sighted(Map map, Point3D where, int mobs)
    {
        var quad = At(map, where);

        if (quad == null)
        {
            return;
        }

        quad.Mobs = Math.Max(0, mobs);
        quad.Sighted = Core.TickCount;

        Counted++;
    }

    /// <summary>Squares whose creatures have been counted. For the summary.</summary>
    public static long Counted { get; private set; }

    /// <summary>
    /// How often one bot counts what is standing in its own square.
    ///
    /// Ten seconds. Creatures move at a walk, so a count this old is still about the same square-full of
    /// them, and thirty-four bots at this rate is three sweeps a second across the whole shard — against a
    /// spatial query of fifteen tiles, which is what every bot does several times a second anyway to decide
    /// what to fight.
    /// </summary>
    public static int LookEveryMs { get; set; } = 10000;

    /// <summary>Sweeps made. A denominator for <see cref="Counted"/>.</summary>
    public static long Looks { get; private set; }

    /// <summary>
    /// Counts the aggressive creatures standing in this bot's square and writes the number down.
    ///
    /// <para>
    /// <b>The one term in the reading that is about now rather than about what happened.</b> Everything else
    /// here is earned over hundreds of crossings; this says what is living in the square at this moment, and
    /// it is what lets the map distinguish ground that has been quiet because nothing lives there from
    /// ground that has been quiet because nobody has been back since the ogres moved in.
    /// </para>
    ///
    /// <para>
    /// Unique by construction: one spatial query cannot return the same creature twice, so the count it
    /// hands over needs no set of serials and no bookkeeping to keep one honest. Hostility is asked of
    /// BotThreat, which is the same question every fight on this shard is decided by.
    /// </para>
    /// </summary>
    public static void Look(Mobile body)
    {
        if (body is not { Deleted: false, Alive: true } || body.Map == null || body.Map == Map.Internal)
        {
            return;
        }

        var quad = At(body.Map, body.Location);

        if (quad == null || Core.TickCount - quad.Sighted < LookEveryMs)
        {
            return;
        }

        var mobs = 0;

        foreach (var creature in body.Map.GetMobilesInRange<BaseCreature>(body.Location, Side / 2))
        {
            if (creature is { Deleted: false, Alive: true } && BotThreat.Hostile(body, creature))
            {
                mobs++;
            }
        }

        Looks++;

        Sighted(body.Map, body.Location, mobs);
    }

    /// <summary>
    /// How much strength a square asks of whoever walks into it.
    ///
    /// <para>
    /// Patrick's table of 03.09.2026, in the units <c>BotThreat.Power</c> already speaks: a thousand at
    /// minus a hundredth, three thousand at minus a twentieth, four and a half at minus a tenth, and five
    /// hundred more for every further twentieth. Above <see cref="Fearless"/> it asks nothing at all.
    /// </para>
    ///
    /// <para>
    /// <b>The band between the two is not in the table and is charged nothing.</b> From 0.05 down to just
    /// above -0.01 the order says only that fear begins somewhere in it, and inventing a number to fill a
    /// gap in an instruction is how a shard ends up with thresholds nobody can account for. It reads as
    /// free, and it is the one number here worth telling Patrick about rather than guessing at.
    /// </para>
    /// </summary>
    public static double Muscle(double safety)
    {
        if (safety > Fearless)
        {
            return 0.0;
        }

        if (safety > -0.01)
        {
            return 0.0;
        }

        if (safety > -0.05)
        {
            return 1000.0;
        }

        if (safety > -0.10)
        {
            return 3000.0;
        }

        // Every further twentieth of danger asks another five hundred. Floored rather than rounded so that a
        // square exactly on a step is charged that step and not the next one.
        var steps = (int)Math.Floor((-safety - 0.10) / 0.05 + 1e-9);

        return 4500.0 + steps * 500.0;
    }

    /// <summary>What this ground asks of whoever walks into it, in strength.</summary>
    public static double Muscle(Map map, Point3D where) => Muscle(Safety(map, where));

    /// <summary>
    /// What is going: this bot, or the whole company it is in.
    ///
    /// <para>
    /// "Of one or more participants", by the order, so a company's strength is its members added together
    /// and a bot on its own is a company of one. Only members that can still fight are counted — a corpse
    /// and a bot on two hits of health are both worth nothing to whoever is deciding whether to walk into an
    /// ogre, and counting them is how a company of five arrives as a company of two.
    /// </para>
    /// </summary>
    public static double Strength(Mobile body)
    {
        if (body is not { Deleted: false, Alive: true })
        {
            return 0.0;
        }

        if (body is not IBotSquadMember { Squad: not null } member)
        {
            return BotThreat.Power(body);
        }

        var members = member.Squad.Members;
        var strength = 0.0;

        for (var i = 0; i < members.Count; i++)
        {
            if (members[i] is IBotAlly { AbleToFight: true } && members[i].Self is { Deleted: false, Alive: true } self)
            {
                strength += BotThreat.Power(self);
            }
        }

        return strength;
    }

    /// <summary>Squares refused to somebody not strong enough for them. For the summary.</summary>
    public static long Feared { get; private set; }

    /// <summary>
    /// Whether whoever is going is strong enough for this ground.
    ///
    /// <para>
    /// <b>A wall, not a discount.</b> Everything else on this map is a number the auction weighs against
    /// other numbers, and this one is not: a bot below the threshold does not go, whatever the work there is
    /// worth, and the only way past it is to be stronger or to bring people. That is the whole of Patrick's
    /// order of 03.09.2026 and it is the reason this returns a yes or a no rather than a factor.
    /// </para>
    /// </summary>
    public static bool Dares(Mobile body, Map map, Point3D where)
    {
        var asked = Muscle(map, where);

        if (asked <= 0.0)
        {
            return true;
        }

        if (Strength(body) >= asked)
        {
            return true;
        }

        Feared++;

        return false;
    }

    /// <summary>Whether a bot has ever actually stood in the square this tile is in.</summary>
    public static bool Trodden(Map map, Point3D where) => Known(map, where)?.Trodden == true;

    /// <summary>
    /// A bot crossed out of one square and into another with nothing having happened to it.
    ///
    /// <para>
    /// Credited to the square it <em>left</em>, and that is the whole of what makes a crossing evidence. A
    /// bot that has just walked into a square knows nothing about it yet; a bot that has walked out of one
    /// has been through it and is still standing. Crediting the square being entered would hand credit to
    /// the very ground a bot is about to be killed on.
    /// </para>
    /// </summary>
    public static void Crossed(Map map, Point3D left, Point3D entered)
    {
        var into = At(map, entered);

        if (into != null && !into.Trodden)
        {
            into.Trodden = true;
            into.Tick = Core.TickCount;

            Discovered++;
        }

        var quad = Known(map, left);

        if (quad == null)
        {
            return;
        }

        quad.Passes++;
        quad.Towards++;
        quad.Tick = Core.TickCount;

        if (quad.Towards < PerPass)
        {
            return;
        }

        quad.Towards = 0;

        Raise(quad, PassWorth);
        Credited++;
    }

    /// <summary>
    /// Something was taken out of the ground here and nothing interrupted it.
    ///
    /// <para>
    /// <b>A different kind of evidence from a crossing, and the reason it is worth having separately.</b> A
    /// bot walking through a square is exposed for a few seconds and is looking where it is going. A bot
    /// harvesting stands in one place for a minute with a pickaxe in its hands, and comes away untouched —
    /// which is a far stronger thing to be able to say about ground, and far rarer, so it earns a fifth as
    /// much and is asked for twenty times as often. See <see cref="PerHarvest"/>.
    /// </para>
    ///
    /// <para>
    /// Counted where the bot is standing rather than where the rock is: the square that was safe is the one
    /// the body was in.
    /// </para>
    /// </summary>
    public static void Harvested(Map map, Point3D where)
    {
        var quad = Known(map, where);

        if (quad == null)
        {
            return;
        }

        quad.Harvests++;
        quad.Reaping++;
        quad.Tick = Core.TickCount;

        if (quad.Reaping < PerHarvest)
        {
            return;
        }

        quad.Reaping = 0;

        Raise(quad, HarvestWorth);
        Reaped++;
    }

    /// <summary>Squares credited for undisturbed harvests. For the summary.</summary>
    public static long Reaped { get; private set; }

    /// <summary>
    /// A bot is standing here, and that is all this says.
    ///
    /// For a crossing that was interrupted: the square becomes known and counts as trodden, but earns no
    /// credit, because a bot that was being hit while it walked has learned nothing good about the ground.
    /// </summary>
    public static void Seen(Map map, Point3D where)
    {
        var quad = At(map, where);

        if (quad == null)
        {
            return;
        }

        quad.Tick = Core.TickCount;

        if (quad.Trodden)
        {
            return;
        }

        quad.Trodden = true;
        Discovered++;
    }

    /// <summary>Something hit a bot here.</summary>
    /// <param name="ranger">Whether the bot struck was one of the crown's rangers. Their blows count coarser.</param>
    public static void Struck(Map map, Point3D where, bool ranger = false)
    {
        var quad = At(map, where);

        if (quad == null)
        {
            return;
        }

        quad.Blows++;
        quad.Tick = Core.TickCount;

        if (ranger)
        {
            quad.RangerBruising++;

            if (quad.RangerBruising < PerRangerBlows)
            {
                return;
            }

            quad.RangerBruising = 0;

            Raise(quad, RangerBlowsWorth);
            Marked++;

            return;
        }

        // <b>"Twenty-five crossings without blows", and until now the two counters never spoke.</b> A square
        // could bank a crossing credit out of runs interrupted by every kind of violence, because nothing
        // here reset the run. Both quiet runs end when something lands a blow, which is what makes them runs.
        quad.Towards = 0;
        quad.Reaping = 0;

        quad.Bruising++;

        if (quad.Bruising < PerBlows)
        {
            return;
        }

        quad.Bruising = 0;

        Raise(quad, BlowsWorth);
        Marked++;
    }

    /// <summary>
    /// The King's Rangers walked out of this square without losing anybody.
    ///
    /// <para>
    /// Marked read and left exactly where it was, which is <see cref="SweptWorth"/> and is nought by order.
    /// Five bots in gold plate coming through unharmed is not evidence that a miner could, and crediting the
    /// ground for it would quietly raise every square they walk past the bar at which anybody else is
    /// allowed to hunt in it.
    /// </para>
    /// </summary>
    public static void Swept(Map map, Point3D where)
    {
        var quad = At(map, where);

        if (quad == null)
        {
            return;
        }

        quad.Swept = true;
        quad.Tick = Core.TickCount;

        if (!quad.Trodden)
        {
            quad.Trodden = true;
            Discovered++;
        }

        if (SweptWorth != 0.0)
        {
            Raise(quad, SweptWorth);
        }

        Sweeps++;
    }

    /// <summary>
    /// One of the crown's rangers died here.
    ///
    /// <para>
    /// Worth an ordinary death on its own — they are better armed, but a body is a body. What is not ordinary
    /// is <paramref name="wiped"/>: the whole company falling in one square is a separate reading applied on
    /// top of the five, because "five bots died here over an afternoon" and "a company of five was destroyed
    /// here" are different facts about the same ground and only the second one means nobody should go.
    /// </para>
    /// </summary>
    public static void FellRanger(Map map, Point3D where, bool wiped)
    {
        Fell(map, where, RangerDeathWorth);

        var quad = Known(map, where);

        if (quad == null)
        {
            return;
        }

        quad.RangersLost++;

        if (!wiped)
        {
            return;
        }

        Raise(quad, WipedWorth);
        Wiped++;

        logger.Warning(
            "The King's Rangers were destroyed around ({X}, {Y}); the ground now reads {Safety:F2}",
            quad.Middle.X,
            quad.Middle.Y,
            quad.Safety
        );
    }

    /// <summary>A bot died here.</summary>
    /// <param name="worth">What this particular death costs the square. See <see cref="BaronWorth"/>.</param>
    public static void Fell(Map map, Point3D where, double worth)
    {
        var quad = At(map, where);

        if (quad == null)
        {
            return;
        }

        quad.Deaths++;
        quad.Tick = Core.TickCount;

        Raise(quad, worth);
        Mourned++;

        logger.Information(
            "The ground around ({X}, {Y}) has taken somebody and now reads {Safety:F2}, on {Deaths} dead",
            quad.Middle.X,
            quad.Middle.Y,
            quad.Safety,
            quad.Deaths
        );
    }

    /// <summary>The same, at what an ordinary death is worth.</summary>
    public static void Fell(Map map, Point3D where) => Fell(map, where, DeathWorth);

    /// <summary>
    /// A great hunt has finished here. The square is neither trusted nor feared afterwards.
    ///
    /// <para>
    /// Set rather than raised, and set to nothing rather than to safe. What a company killed everything in
    /// is not <em>safe</em> — nobody has walked it since, and whatever wanders back in is unaccounted for.
    /// It is simply no longer known to be dire, which is what a clearance actually establishes. Earning its
    /// way up from there is the same walking every other square does.
    /// </para>
    /// </summary>
    public static void Cleared(Quad quad)
    {
        if (quad == null)
        {
            return;
        }

        var was = quad.Safety;

        quad.Safety = Harrowed;
        quad.Bruising = 0;
        quad.Towards = 0;
        quad.HarrowedTick = Core.TickCount;
        quad.Tick = Core.TickCount;

        Cleansed++;

        logger.Information(
            "The ground around ({X}, {Y}) has been harrowed: it read {Was:F2} and is now {Now:F2}",
            quad.Middle.X,
            quad.Middle.Y,
            was,
            quad.Safety
        );
    }

    /// <summary>
    /// Moves a square's reading, and counts the moment it stops being ground anybody may hunt on.
    ///
    /// <para>
    /// <b>The level was reported and the drift was not, and the drift is the thing.</b> The island's line has
    /// always said how many squares read too quiet, which is a stock; what it could not say is whether that
    /// number is going anywhere. On the night of 02-03.09.2026 it was: in one five-minute beat 1129 squares
    /// were raised for crossings against 96 marked down for blows, a ratchet of nearly twelve to one, and the
    /// share of the island closed to hunting had reached 364 of 1226. A square earns credit every time a bot
    /// walks through it without incident, and bots walk everywhere; blows are rarer than walking by
    /// construction. Whether that balance is right is a question for whoever sets the numbers — this only
    /// makes the direction visible, which it was not.
    /// </para>
    /// </summary>
    private static void Raise(Quad quad, double by)
    {
        var was = quad.Safety;

        quad.Safety = Math.Clamp(quad.Safety + by, Bleakest, Safest);

        if (was <= TooQuiet && quad.Safety > TooQuiet)
        {
            Hushed++;
        }
        else if (was > TooQuiet && quad.Safety <= TooQuiet)
        {
            Roused++;
        }
    }

    /// <summary>
    /// Squares that crossed above <see cref="TooQuiet"/> since the shard came up, and back below it.
    ///
    /// Counted the same way as every other tally on this line — from the world load, not per report — so
    /// that the two are read against each other rather than against a window nobody can see.
    /// </summary>
    public static long Hushed { get; private set; }

    public static long Roused { get; private set; }

    /// <summary>
    /// The squares that read worst, worst first. For the board and for whoever is deciding where to send
    /// people.
    /// </summary>
    public static List<Quad> Worst(int most, Map map = null)
    {
        List<Quad> found = [];

        foreach (var quad in _quads.Values)
        {
            if (map != null && quad.Map != map)
            {
                continue;
            }

            found.Add(quad);
        }

        found.Sort(static (a, b) => a.Safety.CompareTo(b.Safety));

        if (most > 0 && found.Count > most)
        {
            found.RemoveRange(most, found.Count - most);
        }

        return found;
    }

    /// <summary>
    /// The eight squares around this one, and this one. What a Baron walks and what a great hunt spills into.
    /// </summary>
    public static List<Quad> Around(Quad quad, bool madeIfNew)
    {
        List<Quad> found = [];

        if (quad?.Map == null)
        {
            return found;
        }

        for (var dx = -1; dx <= 1; dx++)
        {
            for (var dy = -1; dy <= 1; dy++)
            {
                var key = (quad.Map.MapID, quad.X + dx, quad.Y + dy);

                if (_quads.TryGetValue(key, out var near))
                {
                    found.Add(near);

                    continue;
                }

                if (!madeIfNew || _quads.Count >= Most)
                {
                    continue;
                }

                near = new Quad
                {
                    Map = quad.Map,
                    X = quad.X + dx,
                    Y = quad.Y + dy,
                    Safety = Fresh,
                    Tick = Core.TickCount
                };

                _quads[key] = near;
                found.Add(near);
            }
        }

        return found;
    }

    /// <summary>
    /// The nearest square on the frontier: never stood in, but next to somewhere that has been.
    ///
    /// <para>
    /// <b>Nearest rather than worst, which is the opposite of how every other errand picks its ground.</b> A
    /// patrol goes where it is most dangerous and a great hunt where it is most dire, because both answer
    /// "where is the trouble". This answers "what do we not know", and unknown ground is all equally
    /// unknown — there is nothing to rank it by, so the tiebreak is the walk. The map then fills outwards
    /// from where the population actually lives, which is also the order in which knowing is worth anything.
    /// </para>
    ///
    /// <para>
    /// <b>Candidates are the ring just past what is known, never a search of the island.</b> This table only
    /// holds squares something has happened in or beside, so every candidate is by construction beside
    /// ground somebody has walked — which means it can be got to, and a square picked off a blank map cannot
    /// promise that. The frontier then advances a ring at a time under its own steam.
    /// </para>
    /// </summary>
    /// <param name="fit">Asked of each candidate. Null takes the first with ground under it.</param>
    public static Point3D Frontier(Map map, Point3D from, int within, Func<Point3D, bool> fit)
    {
        if (map == null || map == Map.Internal)
        {
            return Point3D.Zero;
        }

        var closest = int.MaxValue;
        var best = Point3D.Zero;

        foreach (var quad in _quads.Values)
        {
            if (quad.Map != map || !quad.Trodden)
            {
                continue;
            }

            for (var dx = -1; dx <= 1; dx++)
            {
                for (var dy = -1; dy <= 1; dy++)
                {
                    if (dx == 0 && dy == 0)
                    {
                        continue;
                    }

                    if (_quads.TryGetValue((map.MapID, quad.X + dx, quad.Y + dy), out var near) && near.Trodden)
                    {
                        continue;
                    }

                    var x = (quad.X + dx) * Side + Side / 2;
                    var y = (quad.Y + dy) * Side + Side / 2;
                    var away = Math.Max(Math.Abs(x - from.X), Math.Abs(y - from.Y));

                    if (away > within || away >= closest)
                    {
                        continue;
                    }

                    // The height is settled from the map, never arithmetic. A square's middle is a corner
                    // plus half a side and has no height of its own: invented as nought it works on the
                    // plains north of Britain and is thirty units under the grass on a hill, where no walk
                    // can ever arrive. This project has paid for that once already.
                    if (!BotStep.Settle(map, x, y, out var z))
                    {
                        continue;
                    }

                    var at = new Point3D(x, y, z);

                    if (fit != null && !fit(at))
                    {
                        continue;
                    }

                    closest = away;
                    best = at;
                }
            }
        }

        return best;
    }

    /// <summary>
    /// The worst square within reach that reads at or below <see cref="Dire"/>, or nothing.
    ///
    /// What the Baron is looking for. Ranked by the reading rather than by the dead, which is the whole
    /// difference between this map and <see cref="BotPeril"/>: a square earns its way down here by hurting
    /// people repeatedly and does not climb back out on its own.
    /// </summary>
    /// <param name="fit">Asked of each candidate in turn. Null takes the first with ground under it.</param>
    public static Quad Direst(Map map, Point3D from, int within, Func<Point3D, bool> fit)
    {
        if (map == null || map == Map.Internal)
        {
            return null;
        }

        Quad best = null;
        var lowest = Dire;

        foreach (var quad in _quads.Values)
        {
            if (quad.Map != map || quad.Safety > lowest)
            {
                continue;
            }

            var middle = quad.Middle;

            if (Math.Max(Math.Abs(middle.X - from.X), Math.Abs(middle.Y - from.Y)) > within)
            {
                continue;
            }

            if (!BotStep.Settle(map, middle.X, middle.Y, out var z))
            {
                continue;
            }

            if (fit != null && !fit(new Point3D(middle.X, middle.Y, z)))
            {
                continue;
            }

            lowest = quad.Safety;
            best = quad;
        }

        return best;
    }

    /// <summary>Where a square's middle is, with the ground settled under it. Zero when nothing can stand there.</summary>
    public static Point3D Stand(Quad quad)
    {
        if (quad?.Map == null)
        {
            return Point3D.Zero;
        }

        var middle = quad.Middle;

        return BotStep.Settle(quad.Map, middle.X, middle.Y, out var z)
            ? new Point3D(middle.X, middle.Y, z)
            : Point3D.Zero;
    }

    public static string Describe()
    {
        if (_quads.Count == 0)
        {
            return "no ground has been walked yet";
        }

        var trodden = 0;
        var quiet = 0;
        var wanted = 0;
        var dire = 0;
        Quad worst = null;

        foreach (var quad in _quads.Values)
        {
            if (quad.Trodden)
            {
                trodden++;
            }

            if (quad.Safety > TooQuiet)
            {
                quiet++;
            }

            if (quad.Safety <= Wanted)
            {
                wanted++;
            }

            if (quad.Safety <= Dire)
            {
                dire++;
            }

            if (worst == null || quad.Safety < worst.Safety)
            {
                worst = quad;
            }
        }

        return $"{_quads.Count} quadrants of {Side} tiles, {trodden} of them stood in: {quiet} too quiet to hunt "
            + $"({Hushed} shut and {Roused} reopened since the shard came up, which is the direction rather than the level) "
               + $"(above {TooQuiet:F2}), {wanted} worth going to (at or below {Wanted:F2}), {dire} dire (at or below {Dire:F2}); "
               + $"worst is {worst}; {Discovered} first set foot in, {Credited} raised for crossings, "
               + $"{Marked} marked for blows, {Mourned} for a death, {Cleansed} harrowed, {Sweeps} swept by rangers, {Wiped} took a whole company, {Baulked} rested because nobody could get near them, {Reaped} credited for undisturbed harvests, {Counted} counts of what lives in a square over {Looks} sweeps, {Feared} refused to somebody not strong enough";
    }

    /// <summary>
    /// Puts one quadrant back as it was read off disk. Called only by <see cref="BotQuadStore"/>.
    ///
    /// <para>
    /// The facet is looked up here rather than stored: a <c>Map</c> belongs to the world that was replaced,
    /// and a record holding one is a record holding a deleted object. An id that no longer names a facet is
    /// dropped rather than guessed at — a shard reconfigured from Felucca to somewhere else should lose the
    /// old island's reputation, not silently apply it to the new one.
    /// </para>
    /// </summary>
    public static void Restore(
        int facet,
        int x,
        int y,
        double safety,
        int passes,
        int blows,
        int deaths,
        int rangersLost,
        bool trodden,
        bool swept,
        bool harrowed,
        int levied = 0,
        int wipes = 0
    )
    {
        var map = Map.Maps is { Length: > 0 } && facet >= 0 && facet < Map.Maps.Length ? Map.Maps[facet] : null;

        if (map == null || map == Map.Internal || _quads.Count >= Most)
        {
            return;
        }

        _quads[(facet, x, y)] = new Quad
        {
            Map = map,
            X = x,
            Y = y,
            Safety = Math.Clamp(safety, Bleakest, Safest),
            Passes = passes,
            Blows = blows,
            Deaths = deaths,
            RangersLost = rangersLost,
            Trodden = trodden,
            Swept = swept,
            Levied = levied,
            Wipes = wipes,

            // Stamped with a real reading rather than with the one from the last process: these counters can
            // be the machine's uptime, so a tick from yesterday is not a smaller number, it is a nonsense one.
            Tick = Core.TickCount,
            HarrowedTick = harrowed ? Core.TickCount : 0
        };
    }

    /// <summary>A world reload is a different world. The ground is not, but the Maps in these records are.</summary>
    public static void Forget()
    {
        _quads.Clear();

        Credited = 0;
        Marked = 0;
        Mourned = 0;
        Discovered = 0;
        Cleansed = 0;
        Reaped = 0;
        Counted = 0;
        Looks = 0;
        Feared = 0;
        Baulked = 0;
        Sweeps = 0;
        Wiped = 0;
        Hushed = 0;
        Roused = 0;
    }
}
