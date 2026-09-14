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

    public const string Trade = "drill-in";

    public static double PerMinute =>
        BotSchool.Rate * (60000.0 / BotSchool.BeatMs) * BotYield.GoldPerSkillPoint * Discount;

    public static double Discount { get; set; } = 0.33;

    private readonly Map _map;

    private readonly BotMobile _student;

    private readonly int _bill;

    private bool _enrolled;

    private bool _paid;

    private double _learned;

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

    public override bool Summons => true;

    public override Map Map => _map;

    public override Point3D Where => BotSchool.Ground;

    public override double Expects => PerMinute;

    public override double Minutes => BotSchool.LessonMs / 60000.0;

    public override SkillName? Trains => BotSchool.Lacking(_student);

    public override int Outlay => _bill;

    public override double Coin => 0.0;

    public override bool Alongside => true;

    public override bool Still => _enrolled;

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

            if (!_paid)
            {
                if (!BotAuction.Charge(body, _bill))
                {
                    return BotDoing.Failed($"could not find the {_bill}gp for a lesson");
                }

                _paid = true;

                Banker.Deposit(master, _bill);

                BotSchool.Paid(_bill);

                BotSchool.Learned(body);

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

        if (!body.InRange(station, 0))
        {
            return BotDoing.Walk(_map, station, BotArrival.Within(0), "taking my place in the ranks");
        }

        if (Core.TickCount - _began >= BotSchool.LessonMs)
        {
            return BotDoing.Done($"the lesson ran its course — {_learned:F1} points");
        }

        var facing = body.GetDirectionTo(master);

        if (body.Direction != facing)
        {
            body.Direction = facing;
        }

        if (BotSchool.Lacking(body) == null)
        {
            return BotDoing.Done($"there is nothing more {master.Name} can teach me — {_learned:F1} points");
        }

        Learned(body);

        return BotDoing.Work($"being drilled, {_learned:F1} points so far");
    }

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
