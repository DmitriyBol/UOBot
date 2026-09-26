using System;
using Server.Logging;

namespace Server.BotAI.V2;

/// <summary>
/// The captain's half of a class: take the field, wait for whoever comes, then pace the ranks for an hour
/// saying things.
///
/// <para>
/// <b>Nobody is summoned.</b> The captain opens the field and that is the whole of the invitation — students
/// arrive because <see cref="BotStudent"/> offered them a lesson and their own auction preferred it to
/// mining. A class with an empty field is a real outcome and is counted as one: it means nobody on the shard
/// wanted teaching enough to pay for it, which is a fact about the population's priorities and not a fault
/// in the captain.
/// </para>
///
/// <para>
/// <b>What the captain earns here is coin, and what the shard earns is a population that gets better at
/// something other than by surviving it.</b> Every other point of skill on this island comes from use: a bot
/// improves at swords by being in fights, which means the fastest learners are the ones taking the most
/// risk, and a young warrior's road to competence runs through a graveyard. This is the other road.
/// </para>
/// </summary>
public sealed class BotLesson : BotDeed
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotLesson));

    public const string Trade = "drill";

    public static double Prior { get; set; } = 35.0;

    private readonly Map _map;

    private long _openedTick;

    private long _beatTick;

    private int _turn;

    private bool _teaching;

    private bool _opened;

    private int _lessons;

    private int _bends;

    public static int MostBends { get; set; } = 8;

    public static long Skipped { get; private set; }

    public BotLesson(Map map)
    {
        _map = map;
        _openedTick = Core.TickCount;
        _beatTick = Core.TickCount;
    }

    public override string Kind => Trade;

    public override Map Map => _map;

    public override Point3D Where => BotSchool.Ground;

    public override double Expects => Prior;

    public override double Minutes => (BotSchool.GatherMs + BotSchool.LessonMs) / 60000.0;

    public override SkillName? Trains => null;

    public override int Outlay => 0;

    public override double Coin => 1.0;

    public override bool Alongside => true;

    public override bool Still => _opened;

    public override bool Steadfast => _opened;

    public override bool BendIsTrouble => !_teaching;

    public override double HoldsFor => _opened ? Minutes + 1.0 : 0.0;

    public override string Stage =>
        !_teaching
            ? $"calling a class at ({BotSchool.Ground.X}, {BotSchool.Ground.Y}), {BotSchool.Students.Count} come so far"
            : $"drilling {BotSchool.Students.Count} on the training field";

    public override BotDoing Advance(IBotWilful bot)
    {
        if (bot?.Self is not BotMobile body || _map == null || _map == Map.Internal)
        {
            return BotDoing.Failed("no body");
        }

        if (_opened && !ReferenceEquals(BotSchool.Master, body))
        {
            return BotDoing.Done("somebody else has the field");
        }

        if (!body.InRange(BotSchool.Ground, BotSchool.Voice))
        {
            return BotDoing.Walk(_map, BotSchool.Ground, BotArrival.Within(BotSchool.Voice), "going to the training field");
        }

        if (!_opened)
        {
            if (!BotSchool.Open(body))
            {
                return BotDoing.Done("somebody else has the field");
            }

            _opened = true;
            _openedTick = Core.TickCount;
            _beatTick = Core.TickCount;

            body.Say("Warriors and archers — form up, and I will make something of you.");
        }

        return _teaching ? Teaching(body) : Gathering(body);
    }

    private BotDoing Gathering(BotMobile body)
    {
        var waited = Core.TickCount - _openedTick;

        if (BotSchool.Students.Count >= BotSchool.Most || waited >= BotSchool.GatherMs)
        {
            BotSchool.Begin();

            if (BotSchool.Students.Count == 0)
            {
                BotSchool.Close();

                return BotDoing.Done("nobody came to be taught");
            }

            _teaching = true;
            _openedTick = Core.TickCount;
            _beatTick = Core.TickCount - BotSchool.BeatMs;

            logger.Information(
                "{Name} has closed the roll with {Count} on the field",
                body.Name,
                BotSchool.Students.Count
            );

            body.Say($"{BotSchool.Students.Count} of you. Take your places and keep them.");

            return BotDoing.Work($"drilling {BotSchool.Students.Count}");
        }

        return BotDoing.Work($"waiting for a class, {BotSchool.Students.Count} come so far");
    }

    private BotDoing Teaching(BotMobile body)
    {
        if (Core.TickCount - _openedTick >= BotSchool.LessonMs)
        {
            BotSchool.Close();

            body.Say("That is enough for today. Go and use it.");

            return BotDoing.Done($"the class is over — {_lessons} lessons given");
        }

        if (BotSchool.Students.Count == 0)
        {
            BotSchool.Close();

            return BotDoing.Done($"the field emptied — {_lessons} lessons given");
        }

        if (Core.TickCount - _beatTick < BotSchool.BeatMs)
        {
            return BotDoing.Work($"drilling {BotSchool.Students.Count}");
        }

        _beatTick = Core.TickCount;

        var given = 0.0;
        var reached = 0;

        var students = BotSchool.Students;

        for (var i = students.Count - 1; i >= 0; i--)
        {
            var student = students[i];

            if (student is not { Deleted: false, Alive: true } || student.Map != _map)
            {
                BotSchool.Leave(student);

                continue;
            }

            if (!student.InRange(BotSchool.Ground, BotSchool.Pace * BotSchool.Rank + BotSchool.Pace))
            {
                continue;
            }

            var gain = BotSchool.Teach(student);

            if (gain <= 0.0)
            {
                continue;
            }

            given += gain;
            reached++;
        }

        _lessons++;

        if (reached > 0)
        {
            body.Resolve.Urges.Paid(given * BotYield.GoldPerSkillPoint);
        }

        _turn++;

        var post = BotSchool.Post(_map, _turn, students.Count);

        if (_turn % 2 == 0)
        {
            body.Say(Line(_turn, reached));
        }

        return BotDoing.Walk(_map, post, BotArrival.Within(1), $"drilling {students.Count}");
    }

    private static string Line(int turn, int reached) =>
        (turn / 2 % 5) switch
        {
            0 => "Feet apart. You are not standing, you are falling slowly.",
            1 => "Watch the shoulder, not the blade. The blade only tells you where it has been.",
            2 => "Again. It is not the twentieth one that saves you, it is the two hundredth.",
            3 => reached > 0 ? "Better. Do that when something is trying to kill you." : "Nobody is learning anything from over there.",
            _ => "Breathe out when you strike. You will live longer for it."
        };

    public override bool Bend(IBotWilful bot)
    {
        if (!_teaching || _bends >= MostBends)
        {
            return false;
        }

        _bends++;
        _turn++;
        Skipped++;

        return true;
    }

    public override void Drop(IBotWilful bot)
    {
        if (bot?.Self is BotMobile body && ReferenceEquals(BotSchool.Master, body))
        {
            BotSchool.Close();
        }
    }
}
