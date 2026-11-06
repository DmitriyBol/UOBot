using Server.Items;
using Server.Logging;
using Server.Mobiles;

namespace Server.BotAI.V2;

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

    private bool _stayPut;

    private int _nearest = int.MaxValue;

    private int _stalled;

    public static int NearEnough { get; set; } = 3;

    public static int StalledBeats { get; set; } = 40;

    public static long Unstationed { get; private set; }

    public BotAttend(Map map, BotMobile student, int bill)
    {
        _map = map;
        _student = student;
        _bill = bill;
        _began = Core.TickCount;
    }

    public override string Kind => Trade;

    public override bool Summons => true;

    public override void Taken(IBotWilful bot) => BotSchool.Promise(_student);

    public override Map Map => _map;

    public override Point3D Where => BotSchool.Ground;

    public override double Expects => PerMinute;

    public override double Minutes => BotSchool.LessonMs / 60000.0;

    public override SkillName? Trains => _taught ?? BotSchool.Lacking(_student);

    private SkillName? _taught;

    public override int Outlay => _bill;

    public override double Coin => 0.0;

    public override bool Alongside => true;

    public override bool Still => _enrolled;

    public override bool Steadfast => _enrolled;

    public override bool BendIsTrouble => !_enrolled;

    public override double HoldsFor => _enrolled ? Minutes + 1.0 : 0.0;

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

                return _stayPut
                    ? BotDoing.Walk(_map, BotSchool.Ground, BotArrival.Within(BotSchool.Pace * BotSchool.Rank), "back into the ranks")
                    : BotDoing.Walk(_map, BotSchool.Station(body), BotArrival.Within(0), "back to my place in the ranks");
            }

            BotSchool.Promise(body);

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
                if (!BotMarket.Charge(body, _bill))
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
            _taught = BotSchool.Lacking(body);

            logger.Information(
                "{Name} paid {Bill}gp to be taught {Skill} by {Master}",
                body.Name,
                _bill,
                BotSchool.Lacking(body)?.ToString() ?? "something",
                master.Name
            );
        }

        var station = BotSchool.Station(body);

        if (!_stayPut && !body.InRange(station, 1))
        {
            var gap = System.Math.Max(System.Math.Abs(body.X - station.X), System.Math.Abs(body.Y - station.Y));

            if (gap < _nearest)
            {
                _nearest = gap;
                _stalled = 0;
            }

            else if (body is not BotMobile { Journey.Held: true } && ++_stalled >= StalledBeats && gap <= NearEnough)
            {
                _stayPut = true;
                Unstationed++;

                return BotDoing.Work($"being drilled from {gap} tiles off my place, which has no footing");
            }

            return BotDoing.Walk(_map, station, BotArrival.Within(1), "taking my place in the ranks");
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

    public override bool Bend(IBotWilful bot)
    {

        if (!_enrolled)
        {
            return false;
        }

        if (!_stayPut)
        {
            _stayPut = true;
            Unstationed++;
        }

        return true;
    }

    public override void Drop(IBotWilful bot)
    {
        if (bot?.Self is BotMobile body)
        {
            BotSchool.Leave(body);
        }
    }
}
