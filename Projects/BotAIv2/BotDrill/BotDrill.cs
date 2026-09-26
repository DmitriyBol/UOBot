using System;
using Server.Logging;

namespace Server.BotAI.V2;

/// <summary>
/// Offers a captain an afternoon of teaching, when there is anybody on the island worth teaching.
///
/// <para>
/// <b>It asks whether there are pupils before it offers a class, and that ordering is the whole design.</b>
/// A captain that opened a school whenever it was idle would spend its life standing on an empty field:
/// nobody is obliged to come, and a class with no pupils costs the shard a captain for a quarter of an hour
/// and returns nothing. So the question asked here is the same one a student will ask itself — is there
/// somebody who is behind this captain in a skill they both care about, and can they pay for it — and it is
/// asked of the whole population, cheaply, once per review.
/// </para>
/// </summary>
public sealed class BotDrill : IBotProposer
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotDrill));

    public static int Range { get; set; } = 400;

    public static int Least { get; set; } = 1;

    public string Name => "Drill";

    public BotStanding Rung => BotStanding.Free;

    public static long Asked { get; private set; }

    public static long NotACaptain { get; private set; }

    public static long Held { get; private set; }

    public static long Busy { get; private set; }

    public static long Nobody { get; private set; }

    public static long TooFar { get; private set; }

    public static long Called { get; private set; }

    public BotDeed Propose(IBotWilful bot)
    {
        var body = bot?.Self;
        var map = body?.Map;

        if (map == null || map == Map.Internal || !body.Alive)
        {
            return null;
        }

        if (body is not BotMobile { Class: { } klass } captain || !klass.Leads && !klass.Tutors)
        {
            NotACaptain++;

            return null;
        }

        Asked++;

        if (bot is IBotSquadMember { Squad: not null })
        {
            Held++;

            return null;
        }

        if (BotSchool.Master is { Deleted: false })
        {
            Busy++;

            return null;
        }

        BotSchool.Standing(map);

        if (!body.InRange(BotSchool.Ground, Range))
        {
            TooFar++;

            return null;
        }

        if (Pupils(captain) < Least)
        {
            Nobody++;

            return null;
        }

        Called++;

        return new BotLesson(map);
    }

    private static int Pupils(BotMobile captain)
    {
        var counted = 0;
        var bots = BotPopulation.Bots;

        for (var i = 0; i < bots.Count; i++)
        {
            var other = bots[i];

            if (other?.Map != captain.Map || !BotSchool.Teachable(captain, other))
            {
                continue;
            }

            if (BotYield.Wealth(other) < BotSchool.Bill(captain, other))
            {
                continue;
            }

            counted++;
        }

        return counted;
    }

    public static string Describe() =>
        Asked == 0
            ? "no captain has ever been offered a class to hold"
            : $"{Asked} offers to a captain: {Called} classes called for, {Held} were in a company, {Busy} found the field already held, {TooFar} were too far from it, {Nobody} found nobody worth teaching who could pay; {BotLesson.Skipped} posts of the ring walked past for having no road, {BotAttend.Unstationed} students taught where they stood because their station had none; {BotSchool.Describe()}";

    public static void Forget()
    {
        Asked = 0;
        NotACaptain = 0;
        Held = 0;
        Busy = 0;
        Nobody = 0;
        TooFar = 0;
        Called = 0;
    }
}

/// <summary>
/// Offers a warrior or an archer a place in whatever class is being called.
///
/// <para>
/// <b>Every refusal is a different fact and every one of them is counted.</b> "No bot ever goes to be
/// taught" has at least five distinct causes — no class is open, the roll is closed, this bot is the wrong
/// sort, it has nothing left to learn from this captain, it cannot afford the fee — and a single silent
/// nought would be equally consistent with all of them. That is the failure this shard has paid for more
/// often than any other.
/// </para>
/// </summary>
public sealed class BotStudent : IBotProposer
{
    public static double Punctual { get; set; } = 1.33;

    public static long Belated { get; private set; }

    public string Name => "Student";

    public BotStanding Rung => BotStanding.Free;

    public static long Asked { get; private set; }

    public static long NoClass { get; private set; }

    public static long Closed { get; private set; }

    public static long WrongSort { get; private set; }

    public static long NothingToLearn { get; private set; }

    public static long Broke { get; private set; }

    public static long Richest { get; private set; }

    public static long Full { get; private set; }

    public static long Spoken { get; private set; }

    public static long Came { get; private set; }

    public static long Sealed { get; private set; }

    public BotDeed Propose(IBotWilful bot)
    {
        var body = bot?.Self;
        var map = body?.Map;

        if (map == null || map == Map.Internal || !body.Alive)
        {
            return null;
        }

        Asked++;

        var master = BotSchool.Master;

        if (master is not { Deleted: false, Alive: true })
        {
            NoClass++;

            return null;
        }

        if (!BotSchool.Gathering)
        {
            Closed++;

            return null;
        }

        var coming = BotSchool.Coming(body as BotMobile);

        if (BotSchool.Students.Count + coming >= BotSchool.Most)
        {
            if (BotSchool.Students.Count < BotSchool.Most)
            {
                Spoken++;
            }

            Full++;

            return null;
        }

        if (body is not BotMobile student || student.Map != master.Map)
        {
            WrongSort++;

            return null;
        }

        if (!BotSchool.Suits(master, student.Class))
        {
            WrongSort++;

            return null;
        }

        if (BotSchool.Lacking(student) == null)
        {
            NothingToLearn++;

            return null;
        }

        if (!BotSchool.Rested(student))
        {
            BotSchool.Rested_Away++;

            return null;
        }

        var bill = BotSchool.Bill(student);
        var wealth = BotYield.Wealth(student);

        if (wealth < bill)
        {
            Broke++;

            if (wealth > Richest)
            {
                Richest = wealth;
            }

            return null;
        }

        if (BotReach.Ask(map, body.Location, BotSchool.Ground, BotArrival.Within(BotSchool.Pace * BotSchool.Rank))
            == BotReachVerdict.Sealed)
        {
            Sealed++;

            return null;
        }

        var dx = Math.Abs(body.Location.X - BotSchool.Ground.X);
        var dy = Math.Abs(body.Location.Y - BotSchool.Ground.Y);
        var walk = (dx > dy ? dx : dy) * BotWalk.StepDelayMs(false) * Punctual;

        if (walk > BotSchool.Left)
        {
            Belated++;

            return null;
        }

        Came++;

        return new BotAttend(map, student, bill);
    }

    public static string Describe() =>
        Asked == 0
            ? "nobody has been offered a place in a class"
            : $"{Asked} asked: {Came} offered a place, {NoClass} found no class open, {Closed} came after the roll closed, {Full} found it full ({Spoken} of them with every place left spoken for by bots on their way), {WrongSort} were neither warrior nor archer, {NothingToLearn} had nothing left to learn from the master, {Broke} could not afford the fee (the fattest purse among them held {Richest}gp), {Sealed} could not have walked there at all, {Belated} could not have got there before the roll closed";

    public static void Forget()
    {
        Asked = 0;
        NoClass = 0;
        Closed = 0;
        WrongSort = 0;
        NothingToLearn = 0;
        Broke = 0;
        Belated = 0;
        Richest = 0;
        Full = 0;
        Came = 0;
        Sealed = 0;
    }
}
