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

    /// <summary>The ledger's key.</summary>
    public const string Trade = "drill";

    /// <summary>
    /// What holding a class is reckoned at per minute before experience corrects it.
    ///
    /// A fee or two an hour, which is modest, and it is meant to be: teaching is not how a captain gets rich
    /// and the ledger will find that out inside a session. It exists so that the offer is not free —
    /// a captain that could hold classes at no cost would hold them instead of patrolling.
    /// </summary>
    public static double Prior { get; set; } = 35.0;

    private readonly Map _map;

    private long _openedTick;

    private long _beatTick;

    private int _turn;

    private bool _teaching;

    private bool _opened;

    private int _lessons;

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

    /// <summary>
    /// Nothing, and it is worth saying why: the captain is already at its own ceiling in everything it
    /// teaches, so an hour of drill improves it by definition nought. Claiming a skill here would be the
    /// ledger being told a lie it would take a session to unlearn.
    /// </summary>
    public override SkillName? Trains => null;

    public override int Outlay => 0;

    public override double Coin => 1.0;

    public override bool Alongside => true;

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

        // Somebody else has the field. Only one class runs at a time, and losing the race is not a failure
        // worth marking the ground with.
        if (_opened && !ReferenceEquals(BotSchool.Master, body))
        {
            return BotDoing.Done("somebody else has the field");
        }

        // Still walking to the ground. The field is not opened until the captain is standing on it, or bots
        // would set off for a class whose master is four hundred tiles away.
        if (!body.InRange(BotSchool.Ground, BotSchool.Voice))
        {
            // <b>Asked for the same distance that counts as having arrived, and the two were different.</b>
            // The field is opened by standing within Voice of the middle; the walk asked to be put within
            // two tiles of the exact tile — which on ground at z=20 is a tile a path may simply not exist to.
            // Aldric failed to call a class seven times in eleven minutes with "no way through to
            // (1479, 1629, 20)" while standing perfectly able to teach from four tiles off. One number, not
            // two: a walk should ask for what the work actually needs.
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

        // Everybody has learned everything this captain has to give. An honest ending, and a better one than
        // standing on an empty field until the clock runs out.
        if (BotSchool.Students.Count == 0)
        {
            BotSchool.Close();

            return BotDoing.Done($"the field emptied — {_lessons} lessons given");
        }

        if (Core.TickCount - _beatTick < BotSchool.BeatMs)
        {
            // Standing where the circuit last put him. Walking is how the captain gets there; the walk
            // itself is the journey's business and it is finished before the next beat is due.
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

            // Only bots actually standing in the block are taught. A student that took the fee and wandered
            // off is not being taught, and paying it points anyway would make the field a place bots visit
            // once and then ignore.
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

        // The captain is the better for it too: a class that is actually teaching somebody is work that is
        // paying, and this shard's contentment is exactly "work that pays".
        if (reached > 0)
        {
            body.Resolve.Urges.Paid(given * BotYield.GoldPerSkillPoint);
        }

        // Round the ring, one place a beat, and something said on arrival. The saying is not decoration:
        // which of the ranks he is standing over decides who learns most this beat, so a watcher can see the
        // arithmetic happening.
        _turn++;

        var post = BotSchool.Post(_turn, students.Count);

        if (_turn % 2 == 0)
        {
            body.Say(Line(_turn, reached));
        }

        return BotDoing.Walk(_map, post, BotArrival.Within(1), $"drilling {students.Count}");
    }

    /// <summary>Something to say. Rotated rather than random so a watcher can tell one circuit from the next.</summary>
    private static string Line(int turn, int reached) =>
        (turn / 2 % 5) switch
        {
            0 => "Feet apart. You are not standing, you are falling slowly.",
            1 => "Watch the shoulder, not the blade. The blade only tells you where it has been.",
            2 => "Again. It is not the twentieth one that saves you, it is the two hundredth.",
            3 => reached > 0 ? "Better. Do that when something is trying to kill you." : "Nobody is learning anything from over there.",
            _ => "Breathe out when you strike. You will live longer for it."
        };

    public override void Drop(IBotWilful bot)
    {
        // Whichever way this ended — finished, failed, or outbid by something the captain would rather do —
        // the field must not be left marked as held. A session nobody is running is a session no other
        // captain can open and every student can still see.
        if (bot?.Self is BotMobile body && ReferenceEquals(BotSchool.Master, body))
        {
            BotSchool.Close();
        }
    }
}
