using System;
using System.Collections.Generic;
using Server.Items;
using Server.Mobiles;
using Server.Regions;

namespace Server.BotAI.V2;

/// <summary>
/// Going through a chest, a crate or a barrel standing out in the world.
///
/// <para>
/// <b>Ordered by Patrick on 08.09.2026, and the world already provides the case.</b> An <c>OrcCamp</c>
/// spawns three orcs, a captain, a prisoner, an unlocked chest filled the way a treasure-map chest is
/// filled, and a locked crate holding a hundred to four hundred gold. Until now the population could kill
/// everything standing in that camp and walk away from the chest, because nothing on this shard had any
/// notion of a container that was not a corpse, a stall or its own pack.
/// </para>
///
/// <para>
/// <b>What it takes and at what price is not decided here.</b> That is <see cref="BotSlay.Rifle"/>, which
/// already knows to keep an archer's own arrows, to hold back a cook's meat, to price what it lists off a
/// shopkeeper's own offer rather than a flat gold, and to stop when the pack is full. A second copy of
/// those rules written for chests would have disagreed with the first inside a week — this file has the
/// scars to prove it.
/// </para>
///
/// <para>
/// <b>Two things it will not touch, each with its own bucket.</b> A chest whose camp is still garrisoned —
/// by Patrick's order of 08.09.2026 the chest is what the fight is for, so it opens when the last hostile
/// is dead and not before — and anything standing inside a town, which is the same rule the woodsman and
/// the miner keep and what stops a population from emptying the crates in Britain's shops and calling it
/// work. Locks and traps are counted but no longer refuse: the garrison is the lock.
/// </para>
/// </summary>
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
        + $"{Trapped} trapped; passed over: {Held} still held by a garrison, {Townbound} standing inside a town";
}

/// <summary>
/// Offers a bot the job of going through a chest standing near it.
///
/// <para>
/// The sweep is a spatial query over <see cref="BotPlunder.Reach"/> tiles and it is rationed by
/// <c>BotNeeds</c> to once a minute per bot, for the reason every sweep on this shard is rationed: the
/// auction asks every proposer on every free review, and a query that looks at forty-eight tiles of world
/// per bot per review is a query the population runs thousands of times a minute for nothing.
/// </para>
/// </summary>
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
