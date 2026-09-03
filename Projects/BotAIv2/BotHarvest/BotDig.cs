using System.Collections.Generic;
using Server.Engines.Harvest;
using Server.Items;
using Server.Mobiles;

namespace Server.BotAI.V2;

/// <summary>
/// Dig, melt, put away. One undertaking with three stages, and the second and third are the point of it.
///
/// <para>
/// <b>Ore is worth nothing to anybody.</b> No counter buys it, no bot wants it, and a miner that comes home
/// with a pack full of rock has produced exactly nothing — which is what the first version's miners did all
/// night, because their goal ended at the vein. So the work is not "mine": it is <em>mine, carry it to a
/// fire, and put the metal where it is safe</em>, and it is not finished until the last of those has
/// happened.
/// </para>
///
/// <para>
/// <b>Its stages are its own business.</b> The decision layer asks what to do now and is told a place, or
/// "work here", or that it is over; it never learns what ore is. That is the whole reason adding a trade to
/// this shard does not touch <c>BotWill/</c>.
/// </para>
/// </summary>
public sealed class BotDig : BotDeed
{
    /// <summary>
    /// The ledger's key for this kind of work. A kind, not an instance: "mine" learns from thirty trips,
    /// while "mine-the-vein-at-2144-891" would learn from one and forget it when the row was evicted.
    /// </summary>
    public const string Trade = "mine";

    /// <summary>
    /// What a mining trip is expected to come to, in gold-equivalent per minute, before this bot's own
    /// experience of a given seam is taken into account.
    ///
    /// <para>
    /// Forty-five, from arithmetic rather than taste: about twenty ingots a trip at
    /// <see cref="GoldPerIngot"/>, plus roughly half a point of mining across the swings, which at the
    /// project's exchange rate is worth several times the metal — and eight minutes of walking, digging and
    /// banking to earn it. That the skill dominates is not an accident of the numbers; it is what this
    /// population is for.
    /// </para>
    /// </summary>
    public static double Prior { get; set; } = 45.0;

    /// <summary>
    /// How long a trip is expected to take. Used to weigh the walk against the work: half an hour of
    /// digging is worth a five-minute walk and five minutes of digging is not.
    /// </summary>
    public static double WorkMinutes { get; set; } = 8.0;

    /// <summary>
    /// What an ingot is taken to be worth when the shard has nothing to say about it.
    ///
    /// <para>
    /// <b>It is now the last resort rather than the answer.</b> <see cref="BotAuction.Worth"/> is asked
    /// first: what somebody is actually offering for metal, with the money down, and failing that what metal
    /// has really changed hands for on a stall. This six is what is left when neither has ever happened —
    /// which on a fresh shard is the first trip and nothing after it.
    /// </para>
    ///
    /// <para>
    /// That ordering is the whole of how a shortage reaches a miner. Nobody tells it to dig; a want for metal
    /// raises what its metal counts for, the takings go into the ledger, and the ledger raises its estimate
    /// of digging next time. Which is the same trick as everything else here: arithmetic from a shared fact,
    /// not a message.
    /// </para>
    /// </summary>
    public static int GoldPerIngot { get; set; } = 6;

    /// <summary>How much of what the bot can carry may be filled with rock before it goes to the fire.</summary>
    public static double FillFraction { get; set; } = 0.8;

    /// <summary>Ore enough to head for a fire whatever the scales say.</summary>
    public static int TargetOre { get; set; } = 20;

    /// <summary>
    /// How many swings without anything new in the pack before the tile is given up on.
    ///
    /// <para>
    /// A backstop rather than the main measure. Emptiness itself is read from the engine — see
    /// <see cref="BotOre.Left"/> — so an exhausted block is never chosen in the first place; what this
    /// catches is the other reason nothing appears, which is a run of failed skill rolls on rock the bot is
    /// not good enough for. Six is enough to ride out ordinary bad luck and few enough not to spend a minute
    /// on a hole.
    /// </para>
    /// </summary>
    public static int DryLimit { get; set; } = 6;

    /// <summary>
    /// How near a counter a bot has to stand to bank and to put goods out.
    ///
    /// <para>
    /// <b>Nothing in the engine gates this, and that is the fact the number was missing.</b>
    /// <c>Banker.Deposit</c> credits the account or the bank box from anywhere on the map — it does not so
    /// much as look at where the depositor is standing — and this market is placeless, so a stall can be set
    /// up from a field. The walk to a counter is verisimilitude: a bot puts its takings away at a bank,
    /// because that is what a bank is for. The distance was two here and three in <c>BotUnload</c>, both
    /// copied across from <c>BotShops.CounterReach</c> — which is three because a <em>vendor</em> refuses to
    /// trade further off than that, a rule about a conversation that never happens here.
    /// </para>
    ///
    /// <para>
    /// So the errand was demanding that a bot lean over the counter to do something it could have done from
    /// the doorway, and a banker stands <em>behind</em> a counter: the tiles against it are furniture. With
    /// twenty bots and exactly two bankers on the shard, the ones who could reach four tiles and not three
    /// spent twelve plans proving it and then failed — 25 of the 58 refused walks in the hour to 16:25 on
    /// 26.08.2026, on those two bankers alone, while 44 other trips to the very same counters succeeded.
    /// </para>
    ///
    /// <para>
    /// <b>Six is a judgement and not a derivation</b>, and it is worth saying so plainly: it is about the
    /// size of a bank, so the bot stands inside the building or in its doorway rather than in the street or
    /// on the clerk's toes. What is <em>not</em> a judgement is that there should be one of these numbers
    /// rather than two — see <see cref="BotUnload.Reach"/>, which now asks this one.
    /// </para>
    /// </summary>
    public static int CounterReach { get; set; } = 6;

    /// <summary>How many times this undertaking may pick somewhere else before giving up.</summary>
    public static int MaxBends { get; set; } = 3;

    /// <summary>
    /// Whether the metal goes to the bots' own market rather than into the bank box.
    ///
    /// <para>
    /// On, because metal in a box is wealth nobody else can reach, and the whole point of producing more than
    /// you need is that somebody else needs it. The opening ask is <see cref="GoldPerIngot"/> — the same
    /// stand-in the takings are counted with, so a bot asks what it reckons the thing is worth — and from
    /// there the price is the market's business, not this file's.
    /// </para>
    /// </summary>
    public static bool ListGoods { get; set; } = true;

    /// <summary>
    /// How many emptied rocks one trip may work through before it takes what it has and goes.
    ///
    /// A trip, not a bot: this is not knowledge worth keeping, because the engine refills a bank of ore in
    /// time and a rock written off for ever would be a mine that shrinks every session.
    /// </summary>
    public static int MaxSpent { get; set; } = 8;

    /// <summary>
    /// How long a miner rests between swings.
    ///
    /// <para>
    /// A second, by order of 24.08.2026. There is no engine rule wanting one — the harvest system takes a
    /// swing whenever it is offered — so without a number here the rate was simply however often the
    /// population's clock came round to this bot, which is several times a second. Every other trade on the
    /// shard already had this and mining was the one that did not.
    /// </para>
    /// </summary>
    public static int SwingMs { get; set; } = 1000;

    private enum Leg
    {
        Seam,
        Fire,
        Counter
    }

    private readonly Map _map;

    private IBotWilful _bot;

    private BotSeam _seam;

    private Leg _leg;

    private IPoint3D _tile;

    private HarvestSystem _system;

    private Point3D _fire;

    private Point3D _counter;

    private int _swings;

    /// <summary>Beats spent walking at the current rock without getting inside swinging reach.</summary>
    private int _approaches;

    /// <summary>The closest this bot has come to the seam on the long walk, and how long since that improved.</summary>
    private int _nearest = int.MaxValue;

    private int _stalled;

    /// <summary>Rocks written off in a row for being unreachable. See <see cref="WalledLimit"/>.</summary>
    private int _walled;

    /// <summary>
    /// How many beats a miner will spend closing on one rock before writing it off.
    ///
    /// Forty, which at the population's own beat is a few seconds of honest walking and well short of the
    /// four minutes it used to take somebody watching to notice.
    /// </summary>
    public static int ApproachLimit { get; set; } = 40;

    /// <summary>
    /// How many beats the long walk to a seam may go without getting any nearer before the seam is struck off.
    ///
    /// <para>
    /// Two hundred, which at a turn every two hundred milliseconds is about forty seconds of not closing the
    /// distance. Generous on purpose: the walk itself is meant to be long, and the thing being caught is not
    /// slowness but a destination that cannot be reached at all.
    /// </para>
    /// </summary>
    public static int TrekLimit { get; set; } = 200;

    /// <summary>
    /// How many rocks in a row may be found unreachable before the seam itself is struck off.
    ///
    /// Three. One is bad luck, two is a coincidence, and three of them behind the same wall is the wall.
    /// </summary>
    public static int WalledLimit { get; set; } = 3;

    /// <summary>Seams struck off because nobody could walk to them. See <see cref="TrekLimit"/>.</summary>
    public static long Unwalkable { get; private set; }

    /// <summary>Rocks written off because nothing could get within swinging reach of them.</summary>
    public static long Unreachable { get; private set; }

    /// <summary>Whether a swing has been taken, and when. See <see cref="SwingMs"/>.</summary>
    private bool _swung;

    private long _swungTick;

    private int _dry;

    private int _seen;

    private int _made;

    /// <summary>What a bar was reckoned at when the takings were counted. Kept so the same number comes back
    /// off <see cref="Made"/> if one is sold into a want a moment later.</summary>
    private int _worth;

    private int _stored;

    private int _bends;

    /// <summary>Rocks this trip has emptied. See <see cref="MaxSpent"/> for why it dies with the trip.</summary>
    private readonly List<Point3D> _spent = [];

    public BotDig(BotSeam seam)
    {
        _seam = seam;
        _map = seam.Map;
    }

    public override string Kind => Trade;

    public override Map Map => _map;

    /// <summary>
    /// The seam, for the whole life of the undertaking — <b>never the stage's own destination</b>.
    ///
    /// This is what the ledger files the outcome under, and the thing being learned is whether mining this
    /// patch of ground pays. Returning the bank's location while banking would file a mining trip under the
    /// bank, and the bot would slowly learn that banks are rich in ore.
    /// </summary>
    public override Point3D Where => _seam.Where;

    public override double Expects => Prior;

    public override double Minutes => WorkMinutes;

    /// <summary>Mining, named outright. Smelting is a mining check too — the fire does the rest.</summary>
    public override SkillName? Trains => SkillName.Mining;

    /// <summary>
    /// Nothing at all arrives as coin, and that stays true even though the metal now goes to a market.
    ///
    /// A stall is not a sale: the gold appears whenever a buyer does, which is minutes or hours later and
    /// belongs to whatever the bot is doing then. Counting the asking price as takings here would be counting
    /// the same metal twice — once as produced and once as sold.
    /// </summary>
    public override double Coin => 0.0;

    public override int Made => _made;

    public override string Stage => _leg switch
    {
        Leg.Seam => $"digging {_seam.Ore} ({_swings} swings)",
        Leg.Fire => "carrying ore to a fire",
        _ => "putting the metal away"
    };

    /// <summary>
    /// What to do now.
    ///
    /// A bounded loop rather than recursion: a stage that has finished says so by returning nothing, and the
    /// next stage is asked in the same beat — so a bot that fills its pack walks towards the fire
    /// immediately instead of standing in the mine for a beat first. Bounded, because a stage that cannot
    /// decide must not be able to spin.
    /// </summary>
    public override BotDoing Advance(IBotWilful bot)
    {
        var body = bot?.Self;

        if (body == null)
        {
            return BotDoing.Failed("no body");
        }

        // Kept for the legs that need to ask the ground on this bot's behalf. Melting is handed only the body,
        // and which forges this particular bot has failed to reach is a fact about the bot.
        _bot = bot;

        // Six: one more than a stage transition per leg plus the instruction that follows. Exactly enough
        // would be four, and exactly enough is what turns adding a fourth leg into a silent "could not
        // settle" rather than a working trip.
        for (var guard = 0; guard < 6; guard++)
        {
            var doing = _leg switch
            {
                Leg.Seam => Digging(body),
                Leg.Fire => Melting(body),
                _ => Banking(bot)
            };

            if (doing.Kind != BotDoingKind.None)
            {
                return doing;
            }
        }

        return BotDoing.Failed("could not settle on a next step");
    }

    private BotDoing Digging(Mobile body)
    {
        var carried = BotOre.Carried(body);

        // Enough, or as much as it can walk with. The load test is the one that usually fires: a pack holds
        // about twelve ore before the engine starts charging stamina for every step, and past that a bot
        // digs itself to a standstill. The first version lost three bots to exactly that for a session —
        // the log insisted six hundred times that the step was allowed, and it was; stamina was not in the
        // message.
        if (carried >= TargetOre || Loaded(body))
        {
            _leg = Leg.Fire;

            return default;
        }

        var tool = BotOre.Tool(body);

        if (tool == null)
        {
            return BotDoing.Failed("nothing to dig with");
        }

        // The long walk. Only when it is out of looking range: the seam is a remembered patch of ground,
        // not a particular tile, and what is actually swung at is chosen on arrival.
        //
        // <b>And only while nothing has been chosen to swing at.</b> A bot that has picked its rock is walking
        // to the rock, and a second destination arguing for the seam behind it is not a fallback — it is the
        // other half of a bot pacing between two tiles. The rock is inside the seam by construction now (see
        // the leash below), so the two never really disagreed; this makes it impossible for them to.
        if (_tile == null && !body.InRange(_seam.Where, BotOre.Reach))
        {
            // <b>And a seam that cannot be walked to is not a seam either.</b> The rock leg below has said so
            // since the day two gatherers stood four minutes each on "digging ShadowIronOre (0 swings)"; this
            // leg, the longer walk that comes before it, had no such limit and could ask for the same journey
            // for ever. On 03.09.2026 Calla the Crafter held mine for ten minutes at 1360,1559, walking to
            // 1361,1574 for seven of them and never getting nearer than the fifteen tiles it started at, with
            // not one line in the log the whole time: the journey never fails, so the work never ends, so
            // nothing is ever written down. Measured as progress rather than as attempts, because the walk
            // itself is legitimate and long — what is not legitimate is a walk that stops getting closer.
            var gap = System.Math.Max(System.Math.Abs(body.X - _seam.Where.X), System.Math.Abs(body.Y - _seam.Where.Y));

            if (gap < _nearest)
            {
                _nearest = gap;
                _stalled = 0;
            }
            else if (++_stalled >= TrekLimit)
            {
                // Struck off for everybody, exactly as an empty one is and for the reason given on Barren:
                // what one bot proves by standing on the ground is true for all of them.
                BotGround.Barren(_seam.Where);
                Unwalkable++;

                return BotDoing.Failed($"no way through to the {_seam.Ore} in {gap} tiles, and the seam is struck off");
            }

            return BotDoing.Walk(_map, _seam.Where, BotArrival.Within(2), $"to the {_seam.Ore}");
        }

        if (_tile == null && _spent.Count < MaxSpent)
        {
            // Kept on the seam's lead. Looking outwards from the bot while judging arrival from the seam is
            // what let a rock be chosen that the bot had to leave the seam to reach, and then leave the rock
            // to return to the seam. See BotOre.Find's leash.
            _tile = BotOre.Find(body, out _system, _spent, _seam.Where, BotOre.Reach);
        }

        if (_tile == null || _system == null)
        {
            // Either there was never anything workable here — it happens, the sweep samples every fourth
            // tile — or everything within reach has been emptied. Carrying home what there is beats carrying
            // home nothing.
            if (carried >= BotOre.WorthSmelting)
            {
                _leg = Leg.Fire;

                return default;
            }

            if (_spent.Count > 0)
            {
                return BotDoing.Failed($"emptied {_spent.Count} rocks and found no more");
            }

            // Nothing was ever found here, which is a fact about the ground rather than about this bot's
            // afternoon — so it is written where every miner reads it. See BotGround.Barren.
            BotGround.Barren(_seam.Where);

            return BotDoing.Failed("no rock worth swinging at, and the seam is struck off");
        }

        var at = new Point3D(_tile.X, _tile.Y, _tile.Z);

        if (!body.InRange(at, BotOre.SwingReach))
        {
            // <b>A rock that cannot be walked to is a rock, not an errand.</b> Asked plainly, this leg answers
            // "still walking" for ever: the journey gives the route up, the work asks for the same walk on the
            // next beat, and the pair of them will do that until the world reloads — two gatherers stood four
            // minutes each on "digging ShadowIronOre (0 swings)" that way, which is what it looks like from a
            // client. The tile is written off exactly as a depleted one is, for the same reason: unreachable
            // and empty are different facts about a rock and identical facts about this afternoon.
            if (++_approaches < ApproachLimit)
            {
                return BotDoing.Walk(_map, at, BotArrival.Within(BotOre.SwingReach - 1), "up to the rock");
            }

            _spent.Add(at);
            _tile = null;
            _approaches = 0;
            Unreachable++;

            // <b>Three rocks in a row that cannot be walked to are a fact about the seam, not about the
            // rocks.</b> Written off one at a time, this costs ApproachLimit beats each and MaxSpent of them
            // before the seam is given up — which on 03.09.2026 at 06:17 was Godric the Architect and Alden
            // the Gatherer standing three minutes apiece at 1361,1559, fifteen tiles from a seam at
            // 1361,1574, treading a patch two tiles across and changing tile 49 times inside it. Calla the
            // Crafter had spent ten minutes at the same coordinates six hours earlier. The seam is behind
            // something, and the way to find that out is not to try eight rocks behind the same something.
            if (++_walled >= WalledLimit)
            {
                BotGround.Barren(_seam.Where);
                Unwalkable++;

                return BotDoing.Failed($"{_walled} rocks of the {_seam.Ore} could not be reached, and the seam is struck off");
            }

            return default;
        }

        _approaches = 0;
        _walled = 0;

        // What the last swing produced, judged before the next one is taken.
        if (carried > _seen)
        {
            _seen = carried;
            _dry = 0;
        }
        else if (++_dry >= DryLimit)
        {
            // Emptied, or it never held anything. It has to be written down, not merely dropped: a depleted
            // vein looks exactly like a full one from outside — depletion lives in the engine's own bank of
            // resources, while what a bot can read is the tile's definition — so looking again without
            // remembering hands the bot the same rock for the rest of its life.
            _spent.Add(at);
            _tile = null;
            _dry = 0;

            return default;
        }

        // Held while it is actually being worked, and renewed each swing: a claim that is not being used
        // lapses by itself, which is what keeps it from fencing off rock nobody is digging.
        BotGround.Working(body, _seam.Where);

        // <b>A swing a beat is not mining, it is a pickaxe on a trigger.</b> Sewing and writing have had a
        // rest between attempts since they were written — three seconds each — and digging never got one, so
        // it ran at whatever rate the population's clock happened to offer, two to five times a second. Seen
        // from a client it is unmistakable and it is not what a bot doing a day's work looks like. The engine
        // does not mind, which is exactly why nothing complained.
        if (_swung && Core.TickCount - _swungTick < SwingMs)
        {
            return BotDoing.Work($"digging {_seam.Ore}");
        }

        _swung = true;
        _swungTick = Core.TickCount;
        _swings++;

        BotOre.Swing(body, tool, _system, _tile);

        // <b>What came out of this hillside, written down for everybody.</b> The seam list already says what
        // a vein asks of a miner; this says what it has actually paid, which is the fact a miner would want
        // and the one nobody kept. Recorded against the patch rather than the tile, so a mountainside is one
        // place and not two hundred, and shared, because where the bronze is is not a private opinion.
        Note(body, _seam.Where);

        return BotDoing.Work($"digging {_seam.Ore}");
    }

    private BotDoing Melting(Mobile body)
    {
        // Too little to be worth a fire. Two rocks smelt to nothing — the engine wants more ore than that per
        // ingot — and a bot arrives here with two whenever its pack filled up with something else, which now
        // happens routinely because hunters carry loot. Straight to the counter with what it has.
        if (BotOre.Carried(body) < BotOre.WorthSmelting)
        {
            _leg = Leg.Counter;

            return default;
        }

        if (BotOre.Carried(body) <= 0)
        {
            // Nothing to melt. Whatever metal is in hand still wants putting away.
            _leg = Leg.Counter;

            return default;
        }

        if (_fire == Point3D.Zero)
        {
            _fire = BotGround.Fire(_bot, body.Location);

            if (_fire == Point3D.Zero)
            {
                return BotDoing.Failed("nowhere known to melt it");
            }
        }

        if (!body.InRange(_fire, BotOre.FireReach))
        {
            // The distance the work itself asks for, on the line above. See BotArrival.Beside.
            return BotDoing.Walk(_map, _fire, BotArrival.Within(BotOre.FireReach), "to a fire");
        }

        var made = BotOre.Melt(body);

        if (made <= 0)
        {
            // The ore went in and nothing came out: the smelt is a skill roll, and a poor one burns the rock.
            //
            // <b>Finished rather than failed, and the distinction is not bookkeeping.</b> A failure marks the
            // <em>place</em> with caution, and the place this undertaking is filed under is the seam — so a
            // bad roll at a forge in town would teach the bot that a perfectly good vein two hundred tiles
            // away is not worth digging. The trip really did happen and really did earn its mining checks;
            // what it did not do is produce metal.
            _leg = Leg.Counter;

            return BotDoing.Done($"the ore burned away, {_swings} swings");
        }

        // What the shard says a bar is worth, not what this file guesses. A funded want for metal is what
        // makes a mining trip worth more than it was yesterday, and this is the line the signal comes in
        // through: the takings go into the ledger, and the ledger raises its estimate of digging here next
        // time. Iron stands for the batch because iron is what an ordinary seam gives and what a population
        // of smiths will be asking for; a coloured vein prices its own bars at the counter below.
        _worth = BotAuction.Worth(typeof(IronIngot), GoldPerIngot);
        _made += made * _worth;
        _leg = Leg.Counter;

        return default;
    }

    private BotDoing Banking(IBotWilful bot)
    {
        var body = bot.Self;

        if (_counter == Point3D.Zero)
        {
            _counter = BotGround.Counter(bot, body.Location);

            if (_counter == Point3D.Zero)
            {
                return BotDoing.Failed("nowhere known to put it away");
            }
        }

        if (!body.InRange(_counter, CounterReach))
        {
            // The distance the work itself asks for, on the line above. See BotArrival.Beside.
            return BotDoing.Walk(_map, _counter, BotArrival.Within(CounterReach), "to a counter");
        }

        var (stored, ordered) = Store(bot);

        _stored = stored;

        // What went into a standing want was paid for in coin the moment it was handed over, and the coin is
        // already in the takings. Counting it here as well would pay the miner twice for one bar.
        _made -= ordered * _worth;

        return BotDoing.Done($"{_stored} ingots away, {ordered} of them to order, {_swings} swings");
    }

    /// <summary>
    /// The best metal in this pack, remembered against the ground it came out of.
    ///
    /// Read off the pack rather than off the swing, because the engine hands ore over on its own schedule and
    /// a swing that produced nothing looks exactly like one that has not landed yet. The best of what is
    /// carried is the honest summary of what this trip has been finding.
    /// </summary>
    private void Note(Mobile body, Point3D where)
    {
        var pack = body?.Backpack;

        if (pack == null)
        {
            return;
        }

        var best = CraftResource.Iron;
        var found = false;

        for (var i = 0; i < pack.Items.Count; i++)
        {
            if (pack.Items[i] is not BaseOre ore)
            {
                continue;
            }

            found = true;

            if (ore.Resource > best)
            {
                best = ore.Resource;
            }
        }

        if (found)
        {
            BotCommons.Dug(_map, where, best);
        }
    }

    /// <summary>Over, however it ended. The seam goes back to whoever wants it.</summary>
    public override void Drop(IBotWilful bot) => BotGround.Leave(_seam.Where);

    /// <summary>
    /// Somewhere else, when the way to where it was going turned out not to exist. Only the seam can be
    /// swapped: a fire or a counter is picked as the nearest known one, so choosing again would choose the
    /// same one and the bot would walk into the same wall. Failing there is the honest answer, and the
    /// ledger will keep the bot off that trip for a while.
    /// </summary>
    public override bool Bend(IBotWilful bot)
    {
        if (++_bends > MaxBends)
        {
            return false;
        }

        var body = bot?.Self;

        // The forge could not be reached. Ask for a different one rather than giving up: there are several on
        // this shard, and a miner that cannot reach the nearest is holding a pack of rock it can do nothing
        // with. Left as it was, this loops — the undertaking fails, the proposer offers mining again because
        // the bot is still carrying ore, and the same unreachable forge is chosen. Measured at three failures
        // a second, for as long as nobody looked.
        if (_leg == Leg.Fire)
        {
            // Written down before another is asked for, or the bot simply alternates between two forges it
            // cannot reach — which is what it did. In its own ledger, not a shared list: another bot standing
            // on the other bank can reach this forge perfectly well.
            bot?.Resolve?.Ledger?.Beware(BotGround.FireKind, _map, _fire);

            var fire = BotGround.Fire(bot, body?.Location ?? _seam.Where, _fire);

            if (fire == Point3D.Zero)
            {
                return false;
            }

            _fire = fire;

            return true;
        }

        // And the same for the counter, for the same reason: the metal is made and the bot cannot put it
        // anywhere.
        if (_leg == Leg.Counter)
        {
            bot?.Resolve?.Ledger?.Beware(BotGround.CounterKind, _map, _counter);

            var counter = BotGround.Counter(bot, body?.Location ?? _seam.Where, _counter);

            if (counter == Point3D.Zero)
            {
                return false;
            }

            _counter = counter;

            return true;
        }

        // <b>Written down, exactly as an unreachable forge is, and for want of this one line a gatherer spent
        // a whole session on one rock.</b> The exception below only holds for this bend: once the undertaking
        // gives up, the miner proposer scores the seams again from scratch, the same unreachable one is still
        // the nearest, and it is offered again within seconds. Calla asked for (1316, 1544, 20) thirty-four
        // times on the evening of 23.08.2026, dug nothing, and supplied nobody. The ledger is where "I could
        // not get there" has to live, or the next decision cannot know it.
        bot?.Resolve?.Ledger?.Beware(Trade, _map, _seam.Where);

        var other = BotGround.Seam(bot, _seam.Where);

        if (!other.Exists)
        {
            return false;
        }

        _seam = other;
        _tile = null;
        _dry = 0;

        // Another seam is another set of rocks, so the allowance starts again.
        _spent.Clear();

        return true;
    }

    /// <summary>Whether the pack is full enough that the next step starts costing stamina.</summary>
    private static bool Loaded(Mobile body) =>
        BotLadder.Load(body) >= BotLadder.Ceiling(body) * FillFraction;

    /// <summary>
    /// Metal to whoever wants it, then to a stall, then into the box; coin into the account. Says how much
    /// metal was placed and how much of it went against a standing want.
    ///
    /// <para>
    /// <b>The coin is taken out of the pack before it is banked</b>, and that order is not a style choice:
    /// the engine's deposit adds to the account without touching what the depositor is carrying, so doing it
    /// the other way round mints money. If the deposit is refused the coin is handed straight back rather
    /// than destroyed.
    /// </para>
    /// </summary>
    private static (int Stored, int Ordered) Store(IBotWilful bot)
    {
        var body = bot.Self;
        var pack = body.Backpack;

        if (pack == null)
        {
            return (0, 0);
        }

        var box = body.BankBox;
        var stored = 0;
        var ordered = 0;

        // A snapshot: moving things out mutates the list being read.
        List<Item> carried = [.. pack.Items];

        for (var i = 0; i < carried.Count; i++)
        {
            if (carried[i] is not BaseIngot ingot || ingot.Deleted || !ingot.Movable)
            {
                continue;
            }

            var amount = ingot.Amount;
            var kind = ingot.GetType();
            var worth = BotAuction.Worth(kind, GoldPerIngot);

            // Somebody's standing order first, a stall second, the box last.
            //
            // A funded want is a buyer that has already put the money down, so filling one is the only way a
            // miner gets paid the moment it puts metal out rather than whenever somebody wanders past. How
            // much went that way is reported back, because it has to come off Made.
            var want = BotAuction.Demand(bot, kind);
            var sold = want == null ? 0 : BotAuction.Fill(bot, want, ingot);

            if (sold > 0)
            {
                stored += sold;
                ordered += sold;

                if (sold >= amount)
                {
                    continue;
                }

                amount -= sold;
            }

            // The market next, the box as the fallback. Metal in a box is wealth nobody else can reach, and
            // producing more than you need is only worth anything if somebody else can buy it.
            if (ListGoods && BotAuction.List(bot, ingot, worth) != null)
            {
                stored += amount;

                continue;
            }

            if (box == null)
            {
                continue;
            }

            stored += amount;
            box.DropItem(ingot);
        }

        var purse = pack.GetAmount(typeof(Gold));

        if (purse > 0 && pack.ConsumeTotal(typeof(Gold), purse) && !Banker.Deposit(body, purse))
        {
            pack.DropItem(new Gold(purse));
        }

        return (stored, ordered);
    }
}
