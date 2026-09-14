using System;
using System.Collections.Generic;
using Server.BotAI.V2;
using Server.Logging;
using Server.Mobiles;
using Server.Regions;

namespace Server.BotAI.Mind;

/// <summary>
/// Something to fight, when the watcher has called a hunt and the island has nothing to offer.
///
/// <para>
/// <b>Patrick's order of 08.09.2026, and the shape of it is the whole design: waves that grow.</b> One weak
/// thing, then two, then three, then a strong one with two weak, and so on to two strong and five weak. Not
/// a horde dropped on the population at once — a shard where the answer to a revel is nine bots dead in a
/// field has taught them nothing and cost an evening of skills — and not one lone rat either, which is a
/// revel nobody notices.
/// </para>
///
/// <para>
/// <b>It spawns only when there is genuinely nothing to fight.</b> The island has its own spawns and they
/// are the right ones to use; this exists for the case the log has been shouting about since the first
/// evening — "nothing within 50 tiles of the bots is worth fighting, so no gold will enter the world". A
/// revel for hunting declared into an empty field is a price rise on nothing at all.
/// </para>
///
/// <para>
/// <b>Everything it makes, it takes away.</b> The wave is remembered by reference, the next one waits for
/// the last to be dead, and when the revel ends whatever is still standing is deleted. A shard that
/// accumulates ettins because a watcher had an idea an hour ago is a shard nobody can measure.
/// </para>
/// </summary>
public static class BotWaves
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotWaves));

    public static bool Running { get; set; } = true;

    private static readonly string[] _hunts = ["hunt", "prowl", "plunder"];

    private static readonly (int Weak, int Strong)[] _waves =
    [
        (1, 0), (2, 0), (3, 0), (2, 1), (3, 1), (4, 1), (4, 2), (5, 2)
    ];

    private static readonly Func<BaseCreature>[] _weak =
    [
        () => new Orc(),
        () => new Ratman(),
        () => new Skeleton(),
        () => new Zombie(),
        () => new HeadlessOne()
    ];

    private static readonly Func<BaseCreature>[] _strong =
    [
        () => new OrcCaptain(),
        () => new Troll(),
        () => new Ettin(),
        () => new Ogre()
    ];

    public static int Reach { get; set; } = 40;

    public static int Spread { get; set; } = 8;

    public static int RestMs { get; set; } = 12000;

    public static int MostAlive { get; set; } = 8;

    public static long Raised { get; private set; }

    public static long Weaklings { get; private set; }

    public static long Champions { get; private set; }

    public static long Cleared { get; private set; }

    public static long Unneeded { get; private set; }

    private static readonly List<BaseCreature> _alive = [];

    private static int _stage;

    private static long _clearedTick;

    public static int Wave => _alive.Count > 0 ? _stage : 0;

    public static int Standing => _alive.Count;

    public static void Muster()
    {
        if (!BotRevel.Running || !Running || !IsHunt(BotRevel.Kind))
        {
            Stand();

            return;
        }

        var map = BotRevel.Ground;
        var middle = BotRevel.Where;

        if (map == null || map == Map.Internal || middle == Point3D.Zero)
        {
            return;
        }

        Prune();

        if (_alive.Count > 0)
        {
            return;
        }

        var now = Core.TickCount;

        if (_stage > 0 && now - _clearedTick < RestMs)
        {
            return;
        }

        if (Busy(map, middle))
        {
            Unneeded++;
            _clearedTick = now;

            return;
        }

        var wave = _waves[Math.Min(_stage, _waves.Length - 1)];

        var weak = Put(map, middle, _weak, wave.Weak);
        var strong = Put(map, middle, _strong, wave.Strong);

        if (weak + strong == 0)
        {
            return;
        }

        _stage++;
        Raised++;
        Weaklings += weak;
        Champions += strong;

        logger.Information(
            "Wave {Stage} of the {Kind} revel stands at {X},{Y}: {Weak} weak and {Strong} strong",
            _stage,
            BotRevel.Kind,
            middle.X,
            middle.Y,
            weak,
            strong
        );

        if (strong > 0)
        {
            BotVigil.Body?.Say($"Wave {_stage}: {weak} of them, and {strong} that will not go down easily.");
        }
        else
        {
            BotVigil.Body?.Say($"Wave {_stage}: {weak} of them.");
        }
    }

    private static bool IsHunt(string kind)
    {
        if (kind == null)
        {
            return false;
        }

        for (var i = 0; i < _hunts.Length; i++)
        {
            if (string.Equals(kind, _hunts[i], StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool Busy(Map map, Point3D middle)
    {
        foreach (var creature in map.GetMobilesInRange<BaseCreature>(middle, Reach))
        {
            if (creature is { Deleted: false, Alive: true, Controlled: false, Summoned: false } &&
                creature.Karma < 0)
            {
                return true;
            }
        }

        return false;
    }

    private static int Put(Map map, Point3D middle, Func<BaseCreature>[] kinds, int many)
    {
        var made = 0;

        for (var i = 0; i < many && _alive.Count < MostAlive; i++)
        {
            if (!Somewhere(map, middle, out var at))
            {
                continue;
            }

            var creature = kinds[Utility.Random(kinds.Length)]();

            if (creature == null)
            {
                continue;
            }

            creature.Home = middle;
            creature.RangeHome = Reach;

            creature.MoveToWorld(at, map);
            _alive.Add(creature);
            made++;
        }

        return made;
    }

    private static bool Somewhere(Map map, Point3D middle, out Point3D at)
    {
        at = Point3D.Zero;

        for (var tries = 0; tries < 20; tries++)
        {
            var x = middle.X + Utility.RandomMinMax(-Spread, Spread);
            var y = middle.Y + Utility.RandomMinMax(-Spread, Spread);

            if (!BotStep.Settle(map, x, y, out var z))
            {
                continue;
            }

            var spot = new Point3D(x, y, z);

            if (Region.Find(spot, map)?.IsPartOf<GuardedRegion>() == true)
            {
                continue;
            }

            if (!map.CanFit(x, y, z, 16, false, false))
            {
                continue;
            }

            at = spot;

            return true;
        }

        return false;
    }

    private static void Prune()
    {
        for (var i = _alive.Count - 1; i >= 0; i--)
        {
            var creature = _alive[i];

            if (creature is not { Deleted: false, Alive: true })
            {
                _alive.RemoveAt(i);

                if (_alive.Count == 0)
                {
                    _clearedTick = Core.TickCount;
                }
            }
        }
    }

    public static void Stand()
    {
        if (_alive.Count == 0)
        {
            _stage = 0;

            return;
        }

        var gone = 0;

        for (var i = 0; i < _alive.Count; i++)
        {
            if (_alive[i] is { Deleted: false } creature)
            {
                creature.Delete();
                gone++;
            }
        }

        _alive.Clear();
        _stage = 0;
        Cleared += gone;

        if (gone > 0)
        {
            logger.Information("{Gone} things left over from the revel were taken off the island", gone);
        }
    }

    public static string Describe() =>
        Raised == 0
            ? "no waves have been called"
            : $"{Raised} waves called, {Weaklings} weak and {Champions} strong, {Cleared} taken away unkilled, {Unneeded} times the island had something to fight already"
            + (_alive.Count > 0 ? $"; wave {_stage} is standing, {_alive.Count} of it alive" : "");

    public static void Forget()
    {
        Stand();
        Raised = 0;
        Weaklings = 0;
        Champions = 0;
        Cleared = 0;
        Unneeded = 0;
        _stage = 0;
    }
}
