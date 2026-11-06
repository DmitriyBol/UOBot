using System;
using System.Collections.Generic;
using Server.Items;
using Server.Mobiles;
using Server.Regions;

namespace Server.BotAI.V2;

public sealed class BotPlunder : BotDeed
{

    public const string Trade = "plunder";

    public static double Prior { get; set; } = 160.0;

    public static double WorkMinutes { get; set; } = 1.0;

    public static int Reach { get; set; } = 48;

    public static int Touch { get; set; } = 2;

    public static int Danger { get; set; } = 8;

    public static int EmptiedMs { get; set; } = 600000;

    public static long Locked { get; private set; }

    public static long Trapped { get; private set; }

    public static long Held { get; private set; }

    public static int GuardReach { get; set; } = 12;

    public static long Townbound { get; private set; }

    public static long Theirs { get; private set; }

    public static long Bare { get; private set; }

    public static long Emptied { get; private set; }

    public static long Taken { get; private set; }

    public static long Coins { get; private set; }

    public static long Interrupted { get; private set; }

    private static readonly Dictionary<Serial, long> _emptied = [];

    private readonly Map _map;

    private readonly Container _box;

    private readonly Point3D _where;

    private int _taken;

    private int _coins;

    private int _made;

    public BotPlunder(Map map, Container box)
    {
        _map = map;
        _box = box;
        _where = box?.GetWorldLocation() ?? Point3D.Zero;
    }

    public override string Kind => Trade;

    public override bool Braves => true;

    public override Map Map => _map;

    public override Point3D Where => _where;

    public override double Expects => Prior;

    public override double Minutes => WorkMinutes;

    public override SkillName? Trains => null;

    public override int Outlay => 0;

    public override double Coin => 0.0;

    public override int Made => _made + _coins;

    public override string Stage =>
        _taken > 0 || _coins > 0
            ? $"took {_taken} things and {_coins}gp out of a {_box?.GetType().Name}"
            : $"at a {_box?.GetType().Name}";

    public override BotDoing Advance(IBotWilful bot)
    {
        var body = bot?.Self;

        if (body == null || _map == null || _map == Map.Internal)
        {
            return BotDoing.Failed("no body");
        }

        if (_box == null || _box.Deleted || _box.Map != _map)
        {
            return BotDoing.Failed("the chest is gone");
        }

        if (!body.InRange(_where, Touch))
        {
            return BotDoing.Walk(_map, _where, BotArrival.Within(Touch), $"to a {_box.GetType().Name}");
        }

        if (Threatened(body))
        {
            Interrupted++;

            return _taken > 0 || _coins > 0
                ? BotDoing.Done($"broke off with {_taken} things and {_coins}gp")
                : BotDoing.Failed("something living is standing over it");
        }

        var (taken, coins, made) = BotSlay.Rifle(bot, body, _box, null);

        _taken += taken;
        _coins += coins;
        _made += made;

        Taken += taken;
        Coins += coins;

        if (taken == 0 && coins == 0)
        {
            Forget(_box);

            if (_taken == 0 && _coins == 0)
            {
                Bare++;

                return BotDoing.Done("nothing in it worth carrying");
            }

            Emptied++;

            return BotDoing.Done($"{_taken} things and {_coins}gp out of a {_box.GetType().Name}");
        }

        return BotDoing.Work("going through it");
    }

    public static bool Guarded(Map map, Point3D where)
    {
        foreach (var near in map.GetMobilesInRange<BaseCreature>(where, GuardReach))
        {
            if (!near.Deleted && near.Alive && !near.Controlled && near.Karma < 0)
            {
                return true;
            }
        }

        return false;
    }

    private bool Threatened(Mobile body)
    {
        foreach (var near in _map.GetMobilesInRange<BaseCreature>(body.Location, Danger))
        {
            if (!near.Deleted && near.Alive && !near.Controlled && near.Karma < 0)
            {
                return true;
            }
        }

        return false;
    }

    private static void Forget(Container box)
    {
        if (box != null)
        {
            _emptied[box.Serial] = Core.TickCount;
        }
    }

    public static bool Spent(Container box) =>
        box != null
        && _emptied.TryGetValue(box.Serial, out var when)
        && Core.TickCount - when < EmptiedMs;

    public static Container Nearest(Mobile body, int range)
    {
        var map = body?.Map;

        if (map == null || map == Map.Internal)
        {
            return null;
        }

        Container best = null;
        var bestAway = double.MaxValue;

        foreach (var item in map.GetItemsInRange(body.Location, range))
        {
            if (item.Deleted || item.Parent != null || item is Corpse or BankBox || item is not Container box)
            {
                continue;
            }

            if (Spent(box) || Empty(box))
            {
                continue;
            }

            if (BotLair.Theirs(box) && !BotUnderworld.Member(body))
            {
                Theirs++;

                continue;
            }

            if (box is LockableContainer { Locked: true })
            {
                Locked++;
            }

            if (box is TrappableContainer { TrapType: not TrapType.None })
            {
                Trapped++;
            }

            if (Guarded(map, box.GetWorldLocation()))
            {
                Held++;

                continue;
            }

            if (Region.Find(box.GetWorldLocation(), map)?.IsPartOf<GuardedRegion>() == true)
            {
                Townbound++;

                continue;
            }

            var away = body.GetDistanceToSqrt(box.GetWorldLocation());

            if (away >= bestAway)
            {
                continue;
            }

            best = box;
            bestAway = away;
        }

        return best;
    }

    private static bool Empty(Container box)
    {
        var held = box.Items;

        for (var i = 0; i < held.Count; i++)
        {
            if (held[i]?.Deleted == false && held[i].Movable)
            {
                return false;
            }
        }

        return true;
    }

    public static void Forget()
    {
        Theirs = 0;
        _emptied.Clear();
        Locked = 0;
        Trapped = 0;
        Held = 0;
        Townbound = 0;
        Bare = 0;
        Emptied = 0;
        Taken = 0;
        Coins = 0;
        Interrupted = 0;
    }

    public static string Describe() =>
        $"{Emptied} chests emptied of {Taken} things and {Coins}gp, {Bare} held nothing worth carrying, "
        + $"{Interrupted} broken off for something living standing over them; {Locked} of them were locked and "
        + $"{Trapped} trapped; passed over: {Held} still held by a garrison, {Townbound} standing inside a town and {Theirs} belonging to The Shadow";
}

public sealed class BotPlunderer : IBotProposer
{
    public string Name => "Plunderer";

    public BotStanding Rung => BotStanding.Free;

    public static long Asked { get; private set; }

    public static long Soon { get; private set; }

    public static long None { get; private set; }

    public static long Offered { get; private set; }

    public BotDeed Propose(IBotWilful bot)
    {
        var body = bot?.Self;
        var map = body?.Map;

        if (map == null || map == Map.Internal || !body.Alive)
        {
            return null;
        }

        Asked++;

        if (!BotNeeds.Due(body, "plunder"))
        {
            Soon++;

            return null;
        }

        var box = BotPlunder.Nearest(body, BotPlunder.Reach);

        if (box == null)
        {
            None++;

            return null;
        }

        Offered++;

        return new BotPlunder(map, box);
    }

    public static string Describe() =>
        $"{Asked} asked to go through a chest: {Offered} sent to one, {None} had none in reach, {Soon} had looked too recently";

    public static void Reset()
    {
        Asked = 0;
        Soon = 0;
        None = 0;
        Offered = 0;
    }
}
