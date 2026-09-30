using System;
using Server.Items;
using Server.Logging;
using Server.Mobiles;

namespace Server.BotAI.V2;

/// <summary>
/// The first steps of a trade, bought from a shopkeeper who knows it — the engine's own teaching, paid for in gold.
///
/// <para>
/// <b>Nobody on the island had cooked a thing, and nothing on it could ever have started.</b> On 24.09.2026 the cook's line
/// read "12001 asked to cook: 0 put something on, 7757 had no meat worth cooking, 4244 had meat but no recipe their skill
/// would carry". Every meal is a recipe from nought to a hundred in a craft whose chance at the bottom is nought
/// (<c>DefCooking.GetChanceAtMin</c>), so a bot with no cooking has no chance at all and the engine will not let it try —
/// "you don't have the required skills to attempt this item" — and a skill that rises only by trying cannot rise from
/// there. The chain the population was built around, kill → carve → fire → meal, stopped at the fire the day the last
/// cook was reset to a novice.
/// </para>
///
/// <para>
/// A player in the same place does what the game offers: walks to a cook, a baker or a herbalist and pays to be shown how,
/// which the engine does for a third of the teacher's own skill, never more than forty-two points, at a gold coin a tenth
/// of a point (<see cref="BaseCreature.CheckTeachSkills"/>). This offers exactly that to a bot holding meat worth cooking
/// with less than <see cref="Below"/> points to cook it with, priced high on purpose — it is one lesson in a bot's life,
/// and the auction has no way to price what it unlocks — and no more than <see cref="AtOnce"/> on the road to a teacher
/// at a time, so the whole population does not queue at one stall.
/// </para>
/// </summary>
public sealed class BotTutor : IBotProposer
{
    public string Name => "Tutor";

    public BotStanding Rung => BotStanding.Free;

    public static bool Running { get; set; } = true;

    public static double Prior { get; set; } = 300.0;

    public static int AtOnce { get; set; } = 3;

    public static double Below { get; set; } = 20.0;

    public static int Reserve { get; set; } = 100;

    public static int Reach { get; set; } = 300;

    public static double Knows { get; set; } = 60.0;

    public static long Asked { get; private set; }

    public static long Offered { get; private set; }

    public static long NoTeacher { get; private set; }

    public static long Poor { get; private set; }

    public static long NoRoom { get; private set; }

    public static long Crowded { get; private set; }

    internal static int Walking
    {
        get
        {
            var walking = 0;
            var bots = BotPopulation.Bots;

            for (var i = 0; i < bots.Count; i++)
            {
                if (bots[i] is { Deleted: false } bot && bot.Resolve.Deed is BotTutoring)
                {
                    walking++;
                }
            }

            return walking;
        }
    }

    public BotDeed Propose(IBotWilful bot)
    {
        if (!Running || bot?.Self is not BotMobile { Deleted: false, Alive: true } body || body.Map == null
            || body.Map == Map.Internal)
        {
            return null;
        }

        var wanted = Wanted(body);

        if (wanted == null)
        {
            return null;
        }

        var skill = wanted.Value;

        Asked++;

        if (Walking >= AtOnce)
        {
            Crowded++;

            return null;
        }

        var teacher = Teacher(body, skill);

        if (teacher == null)
        {
            NoTeacher++;

            return null;
        }

        var points = 0;

        if (teacher.CheckTeachSkills(skill, body, 0, ref points, false) != BaseCreature.TeachResult.Success || points <= 0)
        {
            NoRoom++;

            return null;
        }

        if (BotYield.Wealth(body) < points + Reserve)
        {
            Poor++;

            return null;
        }

        Offered++;

        return new BotTutoring(teacher, skill, points);
    }

    private static SkillName? Wanted(BotMobile body)
    {
        var cooking = body.Skills[SkillName.Cooking];

        if (cooking != null && cooking.Base < Below && cooking.Lock == SkillLock.Up && BotOven.Larder(body, out _) != null)
        {
            return SkillName.Cooking;
        }

        var tinkering = body.Skills[SkillName.Tinkering];

        if (tinkering != null && tinkering.Base < Below && tinkering.Lock == SkillLock.Up && body.Class?.Wants(SkillName.Tinkering) == true)
        {
            return SkillName.Tinkering;
        }

        return null;
    }

    private static BaseVendor Teacher(Mobile body, SkillName skill)
    {
        BaseVendor best = null;
        var nearest = double.MaxValue;
        var shops = BotShops.Shops;

        for (var i = 0; i < shops.Count; i++)
        {
            var vendor = shops[i];

            if (vendor is not { Deleted: false, Alive: true } || vendor.Map != body.Map || vendor.Skills[skill].Base < Knows
                || !vendor.CheckTeach(skill, body))
            {
                continue;
            }

            var apart = body.GetDistanceToSqrt(vendor);

            if (apart <= Reach && apart < nearest)
            {
                nearest = apart;
                best = vendor;
            }
        }

        return best;
    }

    public static string Describe() =>
        $"lessons: {Asked} beginners asked (cooking with meat in hand, tinkering for the class that targets it), {Offered} offered a lesson, {BotTutoring.Taught} taught ({BotTutoring.Points / 10.0:0.0} points "
        + $"for {BotTutoring.Paid}gp), {BotTutoring.Failed} lessons failed, {NoTeacher} with no teacher in reach, {Poor} too poor, "
        + $"{NoRoom} with no room to learn, {Crowded} waiting for the road to a teacher to clear, {Walking} on it now";
}

/// <summary>Walking to a shopkeeper, paying, and being taught. See <see cref="BotTutor"/>.</summary>
public sealed class BotTutoring : BotDeed
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotTutoring));

    public const string Trade = "tutor";

    public static int CapMs { get; set; } = 300000;

    public static long Taught { get; private set; }

    public static long Failed { get; private set; }

    public static long Points { get; private set; }

    public static long Paid { get; private set; }

    private readonly BaseVendor _teacher;

    private readonly SkillName _skill;

    private readonly int _price;

    private readonly Map _map;

    private readonly long _began;

    public BotTutoring(BaseVendor teacher, SkillName skill, int price)
    {
        _teacher = teacher;
        _skill = skill;
        _price = price;
        _map = teacher?.Map;
        _began = Core.TickCount;
    }

    public override string Kind => Trade;

    public override Map Map => _map;

    public override Point3D Where => _teacher?.Location ?? Point3D.Zero;

    public override double Expects => BotTutor.Prior;

    public override double Minutes => 3.0;

    public override bool Unpaid => true;

    public override SkillName? Trains => _skill;

    public override int Outlay => _price;

    public override string Stage => $"to {_teacher?.Name ?? "a teacher"} to be taught {_skill}";

    public override BotDoing Advance(IBotWilful bot)
    {
        var body = bot?.Self;

        if (body == null)
        {
            return BotDoing.Failed("no body");
        }

        if (_teacher is not { Deleted: false, Alive: true } || _teacher.Map != _map)
        {
            Failed++;

            return BotDoing.Failed("the teacher is gone");
        }

        if (Core.TickCount - _began >= CapMs)
        {
            Failed++;

            return BotDoing.Failed($"could not reach {_teacher.Name} in {CapMs / 60000} minutes");
        }

        if (!body.InRange(_teacher, 2))
        {
            return BotDoing.Walk(_map, _teacher, BotArrival.Beside, $"to {_teacher.Name} to learn {_skill}");
        }

        var skill = body.Skills[_skill];
        var before = skill?.Base ?? 0.0;
        var points = 0;

        if (_teacher.CheckTeachSkills(_skill, body, 0, ref points, false) != BaseCreature.TeachResult.Success || points <= 0)
        {
            Failed++;

            return BotDoing.Failed($"{_teacher.Name} had nothing to teach it any more");
        }

        if (!BotAuction.Charge(body, points))
        {
            Failed++;

            return BotDoing.Failed($"could not pay {_teacher.Name} {points}gp");
        }

        if (!_teacher.Teach(_skill, body, points, true))
        {
            body.Backpack?.DropItem(new Gold(points));
            Failed++;

            return BotDoing.Failed($"{_teacher.Name} took the fee and then would not teach it");
        }

        var after = skill?.Base ?? before;

        Taught++;
        Points += (long)Math.Round((after - before) * 10);
        Paid += points;

        logger.Information(
            "{Name} paid {Teacher} {Price}gp and was taught {Skill} from {Before:0.0} to {After:0.0}",
            body.Name,
            _teacher.Name,
            points,
            _skill,
            before,
            after
        );

        return BotDoing.Done($"taught {_skill} from {before:0.0} to {after:0.0} for {points}gp");
    }
}
