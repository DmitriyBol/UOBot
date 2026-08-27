using Server.Items;
using Server.Logging;
using Server.Mobiles;

namespace Server.BotAI.V2;

/// <summary>
/// The student's half of a class: pay, walk to the field, find your place in the block, and stay in it.
///
/// <para>
/// <b>A separate undertaking on a separate bot, and it has to be.</b> The captain could not simply be given
/// a list of pupils to improve: a bot that is being taught has stopped mining, stopped hunting and stopped
/// spending, for a quarter of an hour, and on this shard that is a decision it makes for itself against
/// everything else it could be doing. So attendance goes through the same auction as every other piece of
/// work and loses to a good ore vein exactly as often as it deserves to. The one place this project has ever
/// modelled being told what to do, it removed it again.
/// </para>
///
/// <para>
/// <b>Paid up front, and paid once.</b> The fee is taken when the bot arrives and enrols rather than when
/// the lesson ends, for the same reason a shop takes money at the counter: an undertaking can be dropped for
/// something better at any beat, and a debt that has to be collected from a bot that has wandered off to a
/// mine is a debt nothing on this shard can collect.
/// </para>
/// </summary>
public sealed class BotAttend : BotDeed
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotAttend));

    /// <summary>The ledger's key.</summary>
    public const string Trade = "drill-in";

    /// <summary>
    /// What an hour on the training field is reckoned at per minute before experience corrects it.
    ///
    /// <para>
    /// <b>Worked out from the shard's own numbers rather than chosen.</b> A beat of drill is worth roughly
    /// <c>Rate</c> points, beats come every <c>BeatMs</c>, and this shard has always valued a skill point at
    /// <c>BotYield.GoldPerSkillPoint</c>. That is a real gold-equivalent rate and it is a large one — which
    /// is correct and is the whole reason a bot would ever choose to stand still for a quarter of an hour.
    /// The fee comes off it through <see cref="Outlay"/>, where the auction can see it.
    /// </para>
    /// </summary>
    public static double PerMinute =>
        BotSchool.Rate * (60000.0 / BotSchool.BeatMs) * BotYield.GoldPerSkillPoint * Discount;

    /// <summary>
    /// What the estimate above is multiplied by to allow for everything that will go wrong with it.
    ///
    /// A third. The full arithmetic assumes the captain is standing over this bot on every single beat and
    /// that it is at the bottom of the curve, and neither will be true: attention is shared between six and
    /// the curve tapers. An estimate that is wrong in a knowable direction should be corrected here rather
    /// than left for the ledger to discover over an evening.
    /// </summary>
    public static double Discount { get; set; } = 0.33;

    private readonly Map _map;

    private readonly BotMobile _student;

    private readonly int _bill;

    private bool _enrolled;

    private bool _paid;

    private double _learned;

    /// <summary>Which skill the mark below belongs to, so moving on to a second one does not count it twice.</summary>
    private SkillName? _marked;

    private double _mark;

    private long _began;

    public BotAttend(Map map, BotMobile student, int bill)
    {
        _map = map;
        _student = student;
        _bill = bill;
        _began = Core.TickCount;
    }

    public override string Kind => Trade;

    public override Map Map => _map;

    public override Point3D Where => BotSchool.Ground;

    public override double Expects => PerMinute;

    public override double Minutes => BotSchool.LessonMs / 60000.0;

    /// <summary>
    /// The skill being taught, so the shard's own reckoning of what this work came to counts the points.
    ///
    /// Read live rather than fixed at the moment the offer was made: a student that reaches the captain's
    /// ceiling in one skill during the lesson is taught the next one it is furthest behind in, and the
    /// takings should follow the teaching.
    /// </summary>
    public override SkillName? Trains => BotSchool.Lacking(_student);

    /// <summary>The fee, named where the auction can see it: a bot that cannot afford this is not offered it.</summary>
    public override int Outlay => _bill;

    /// <summary>Nothing here is coin. A bot that is short of money should go and earn some instead.</summary>
    public override double Coin => 0.0;

    public override bool Alongside => true;

    public override string Stage =>
        !_enrolled
            ? $"going to the training field, {_bill}gp in hand"
            : $"being drilled at ({BotSchool.Ground.X}, {BotSchool.Ground.Y}), {_learned:F1} points so far";

    public override BotDoing Advance(IBotWilful bot)
    {
        if (bot?.Self is not BotMobile body || !ReferenceEquals(body, _student))
        {
            return BotDoing.Failed("no body");
        }

        var master = BotSchool.Master;

        if (master is not { Deleted: false, Alive: true })
        {
            return _enrolled
                ? BotDoing.Done($"the class ended — {_learned:F1} points")
                : BotDoing.Failed("the class was over before it got there");
        }

        if (!body.InRange(BotSchool.Ground, BotSchool.Pace * BotSchool.Rank + BotSchool.Pace))
        {
            if (_enrolled)
            {
                // Shoved out of the block, or walked out of it. Its own tile is still its own tile: the
                // station is derived from the roster, so going back to it is going back to the same place.
                return BotDoing.Walk(_map, BotSchool.Station(body), BotArrival.Within(0), "back to my place in the ranks");
            }

            return BotDoing.Walk(_map, BotSchool.Ground, BotArrival.Within(BotSchool.Pace * BotSchool.Rank), "going to be taught");
        }

        if (!_enrolled)
        {
            if (!BotSchool.Gathering)
            {
                return BotDoing.Failed("the roll had already been closed");
            }

            // Money first, and only then a place. The other order gives a bot a station it has not paid for
            // and a captain a pupil it cannot charge.
            if (!_paid)
            {
                if (!BotAuction.Charge(body, _bill))
                {
                    return BotDoing.Failed($"could not find the {_bill}gp for a lesson");
                }

                _paid = true;

                // Into the captain's account, which is where every other seller on this shard is paid.
                Banker.Deposit(master, _bill);

                BotSchool.Paid(_bill);

                // Teaching pays the captain in coin, and coin is what its contentment is short of when it
                // has spent the morning patrolling for nothing.
                master.Resolve.Urges.Paid(_bill);
            }

            if (!BotSchool.Enrol(body))
            {
                return BotDoing.Failed("there was no room left in the class");
            }

            _enrolled = true;
            _began = Core.TickCount;

            logger.Information(
                "{Name} paid {Bill}gp to be taught {Skill} by {Master}",
                body.Name,
                _bill,
                BotSchool.Lacking(body)?.ToString() ?? "something",
                master.Name
            );
        }

        var station = BotSchool.Station(body);

        // Standing in the right square is the whole of a student's job, and it is not a metaphor: the
        // captain's own arithmetic asks how near he is to this bot, so a pupil in the wrong place learns
        // less, measurably.
        if (!body.InRange(station, 0))
        {
            return BotDoing.Walk(_map, station, BotArrival.Within(0), "taking my place in the ranks");
        }

        // <b>The clock the answer below needs.</b> Work is the one reply nothing judges, so a student that
        // stood in a field being taught nothing by a captain that had wandered off would stand there for
        // ever. The lesson's own length is the fence.
        if (Core.TickCount - _began >= BotSchool.LessonMs)
        {
            return BotDoing.Done($"the lesson ran its course — {_learned:F1} points");
        }

        // <b>Eyes on the captain, every beat, for as long as it is standing there.</b> Ordered on 25.08.2026
        // and it is not decoration: the block is derived arithmetic and the captain's circuit is derived
        // arithmetic, so without this a rank of six stares off in six directions it happened to arrive
        // facing, and a drill field looks like a queue. Set on the student rather than by the captain
        // because facing is a fact about the bot, and a captain turning six other people's heads by hand is
        // six writes a beat that can go stale the moment anybody steps.
        //
        // Cheap and idempotent: the engine's Direction setter does nothing when the value is unchanged, and
        // a bot that is standing still keeps whatever it was last given. Walking overwrites it, which is
        // correct — a student on its way back to its place should face where it is going.
        var facing = body.GetDirectionTo(master);

        if (body.Direction != facing)
        {
            body.Direction = facing;
        }

        // Nothing left this captain can give. Said as finished rather than failed: the fee bought everything
        // that was for sale.
        if (BotSchool.Lacking(body) == null)
        {
            return BotDoing.Done($"there is nothing more {master.Name} can teach me — {_learned:F1} points");
        }

        Learned(body);

        return BotDoing.Work($"being drilled, {_learned:F1} points so far");
    }

    /// <summary>
    /// What this bot has picked up, measured as the difference from where it stood when it enrolled.
    ///
    /// <para>
    /// The captain adds the points and this only reads them, because a second counter on this side would be
    /// a second opinion about the same event and the two would disagree the first time a beat was missed.
    /// The mark has to be taken per skill: a student that reaches the ceiling in one and is moved on to the
    /// next would otherwise report the whole of the second skill as this afternoon's work.
    /// </para>
    /// </summary>
    private double Learned(BotMobile body)
    {
        var which = Trains;

        if (which == null)
        {
            return _learned;
        }

        if (_marked != which)
        {
            _marked = which;
            _mark = body.Skills[which.Value].Base;

            return _learned;
        }

        // <b>The gain since the mark is taken and the mark moves with it, and the version that did not was
        // counting the same points on every beat.</b> This used to return the running total plus the gap
        // between the skill and a mark that never moved, and the caller assigned that back into the running
        // total — so a student a tenth of a point above its mark was credited a tenth of a point several
        // times a second. One warrior stood on the field reporting 1992.9 points learned, which is two
        // hundred times what a whole lesson can give and about sixty times a grandmaster's entire career.
        //
        // It also broke the one thing that made the number worth printing: at the master's ceiling the gap
        // becomes nought, so the total froze — and "he is learning nothing" and "he has stopped learning"
        // read identically, from a figure that was wrong in both cases.
        var now = body.Skills[which.Value].Base;

        _learned += now - _mark;
        _mark = now;

        return _learned;
    }

    public override void Drop(IBotWilful bot)
    {
        if (bot?.Self is BotMobile body)
        {
            BotSchool.Leave(body);
        }
    }
}
