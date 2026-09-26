using System;
using System.Collections.Generic;
using Server.Items;
using Server.Logging;
using Server.Multis;

namespace Server.BotAI.V2;

/// <summary>
/// A bandit breaks into a guild's hall: creeps up hidden, picks the lock of the guild's chest, and carries off a share of what
/// the guild has put by.
///
/// <para>
/// <b>Patrick, 26.09.2026, beside the order that made The Shadow every guild's enemy:</b> "since The Shadow is the enemy of
/// every guild, bandits may rob guild houses — by stealth and lockpicking." So it is the band's trade, and a bandit out of
/// every guild for its crimes (<c>BotUnderworld.Outlawed</c>): chosen instead of a person when the thought of robbery comes
/// (<see cref="Share"/> of the times), against the hall whose guild has the most put by and has not been broken into within
/// <see cref="AgainMs"/>.
/// </para>
///
/// <para>
/// <b>The chest is the guild's treasury, and the lock is a skill roll.</b> A guild's money is a number kept by guild name
/// (<see cref="BotChest"/>), and the wooden chest in its hall is its furniture; the engine's own lockpick refuses a locked-down
/// container to anybody not a co-owner, and the hall's chest is not even locked. So the pick is the engine's skill check
/// (<c>Mobile.CheckSkill</c>, Lockpicking, which also trains it) against a lock that is harder in a bigger hall (<see cref="Lock"/>),
/// one try every <see cref="PickMs"/>, <see cref="MostPicks"/> tries in all. A pick that opens it takes <see cref="TakeShare"/>
/// of the treasury, at most <see cref="MostTake"/>. A failed try makes a noise now and then (<see cref="NoiseChance"/>): the
/// burglar is revealed, and a guild fighter that can see one of the band sets on it (<c>BotFeuder</c>).
/// </para>
///
/// <para>
/// Hidden on the way in when it can hide at all (<c>BotShadow.Hide</c>, within <see cref="HideAt"/> of the hall), and moving
/// quietly when its Stealth lets it (<c>BotShadow.Quiet</c>) — a bot with neither walks in openly and is seen doing it.
/// </para>
/// </summary>
public sealed class BotBurgle : BotDeed
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotBurgle));

    public const string Trade = "burgle";

    public static bool Running { get; set; } = true;

    public static double Share { get; set; } = 0.3;

    public static int Least { get; set; } = 200;

    public static double TakeShare { get; set; } = 0.25;

    public static int MostTake { get; set; } = 2000;

    public static int AgainMs { get; set; } = 3600000;

    public static int Reach { get; set; } = 800;

    public static int PickMs { get; set; } = 3000;

    public static int MostPicks { get; set; } = 6;

    public static double NoiseChance { get; set; } = 0.25;

    public static int HideAt { get; set; } = 14;

    public static int CapMs { get; set; } = 600000;

    public static double Prior { get; set; } = 300.0;

    public static long Taken { get; private set; }

    public static long Opened { get; private set; }

    public static long Stuck { get; private set; }

    public static long Heard { get; private set; }

    public static long Gold { get; private set; }

    private static readonly Dictionary<BaseHouse, long> _tried = [];

    private readonly BaseHouse _hall;

    private readonly string _guild;

    private readonly long _began;

    private long _pickTick;

    private int _picks;

    private bool _counted;

    public BotBurgle(BaseHouse hall, string guild)
    {
        _hall = hall;
        _guild = guild;
        _began = Core.TickCount;
        _pickTick = _began - PickMs;
    }

    public static BaseHouse Target(Mobile body, Map map, out string guild)
    {
        guild = null;

        if (!Running || body == null || map == null)
        {
            return null;
        }

        BaseHouse best = null;
        var most = Least - 1;
        var now = Core.TickCount;

        foreach (var (name, hall) in BotEstate.Held)
        {
            if (hall is not { Deleted: false } || hall.Map != map || !body.InRange(hall.BanLocation, Reach))
            {
                continue;
            }

            if (_tried.TryGetValue(hall, out var tried) && now - tried < AgainMs)
            {
                continue;
            }

            var gold = BotChest.Holds(name);

            if (gold > most)
            {
                most = gold;
                best = hall;
                guild = name;
            }
        }

        return best;
    }

    private static (double Low, double High) Lock(BaseHouse hall) =>
        BotHallKind.Of(hall).Size switch
        {
            1 => (20.0, 70.0),
            2 => (30.0, 80.0),
            3 => (40.0, 90.0),
            _ => (50.0, 100.0)
        };

    public override string Kind => Trade;

    public override bool Committed => true;

    public override bool Steadfast => true;

    public override bool Braves => true;

    public override bool Unpaid => true;

    public override Map Map => _hall?.Map;

    public override Point3D Where => _hall?.BanLocation ?? Point3D.Zero;

    public override double Expects => Prior;

    public override double Minutes => CapMs / 60000.0;

    public override SkillName? Trains => SkillName.Lockpicking;

    public override int Outlay => 0;

    public override double Coin => 0.0;

    public override string Stage => _picks == 0 ? $"creeping up on the hall of {_guild}" : $"picking the lock of {_guild}'s chest ({_picks} tries)";

    public override BotDoing Advance(IBotWilful bot)
    {
        var body = bot?.Self;

        if (body == null || _hall is not { Deleted: false })
        {
            return BotDoing.Failed("the hall is gone");
        }

        if (!_counted)
        {
            _counted = true;
            Taken++;
            _tried[_hall] = Core.TickCount;
        }

        var now = Core.TickCount;

        if (now - _began >= CapMs)
        {
            Stuck++;

            return BotDoing.Failed("the break-in took too long");
        }

        var chest = BotAbode.Chest(_hall);

        if (chest == null)
        {
            return BotDoing.Failed($"there is no chest in the hall of {_guild}");
        }

        var at = chest.GetWorldLocation();

        if (!body.InRange(at, 1))
        {
            if (!body.Hidden && body.InRange(_hall.BanLocation, HideAt) && BotShadow.Running)
            {
                BotShadow.Hide(body);
            }

            if (body.Hidden && BotShadow.MayStealth(body))
            {
                BotShadow.Quiet(body);
            }

            return BotDoing.Walk(_hall.Map, at, BotArrival.Within(1), $"into the hall of {_guild}");
        }

        if (now - _pickTick < PickMs)
        {
            return BotDoing.Work($"picking the lock of {_guild}'s chest");
        }

        _pickTick = now;
        _picks++;

        var (low, high) = Lock(_hall);

        if (body.CheckSkill(SkillName.Lockpicking, low, high))
        {
            Opened++;

            var take = (int)Math.Min(MostTake, BotChest.Holds(_guild) * TakeShare);
            var got = take <= 0 ? 0 : BotChest.Steal(_guild, take);

            if (got <= 0)
            {
                return BotDoing.Done($"picked the lock of {_guild}'s chest and found it empty");
            }

            body.AddToBackpack(new Gold(got));
            Gold += got;
            BotUnderworld.Burgled(body);

            logger.Information(
                "{Name} broke into the hall of {Guild} and carried {Gold}gp out of its chest (Lockpicking {Skill:F1}, {Tries} tries{Hidden})",
                body.Name,
                _guild,
                got,
                body.Skills.Lockpicking.Base,
                _picks,
                body.Hidden ? ", unseen" : ", in plain sight"
            );

            return BotDoing.Done($"carried {got}gp out of the hall of {_guild}");
        }

        if (Utility.RandomDouble() < NoiseChance && body.Hidden)
        {
            Heard++;
            body.RevealingAction();
        }

        if (_picks >= MostPicks)
        {
            Stuck++;

            return BotDoing.Failed($"the lock of {_guild}'s chest would not give (Lockpicking {body.Skills.Lockpicking.Base:F1})");
        }

        return BotDoing.Work($"picking the lock of {_guild}'s chest");
    }

    public static string Describe() =>
        Taken == 0
            ? "no hall has been broken into"
            : $"{Taken} break-ins tried: {Opened} chests opened for {Gold}gp, {Stuck} locks that would not give or break-ins that ran out of time, {Heard} burglars heard and revealed";

    public static void Forget()
    {
        Taken = 0;
        Opened = 0;
        Stuck = 0;
        Heard = 0;
        Gold = 0;
        _tried.Clear();
    }
}
