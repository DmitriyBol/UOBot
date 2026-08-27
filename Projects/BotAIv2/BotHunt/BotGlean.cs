using System;
using System.Collections.Generic;
using Server.Items;

namespace Server.BotAI.V2;

/// <summary>
/// Picking spent ammunition up off the ground.
///
/// <para>
/// <b>Where an arrow goes when it misses is the whole reason this exists.</b> On this era's rules a miss puts
/// the arrow back into the world — <c>Ammo.MoveToWorld</c>, a tile or two from whatever was being shot at —
/// with a four in ten chance, and a hit drops it into the target instead, which is to say into the corpse. The
/// corpse half is already collected by whoever loots it. The ground half was collected by nobody at all, so an
/// archer's quiver drained one way: out.
/// </para>
///
/// <para>
/// <b>It is priced as the errand it is.</b> Arrows are worth a few coppers each, so this wins only when there
/// is nothing better to do — which is exactly what was asked for, and exactly where it belongs: a bot with a
/// paying trade in front of it should not be crouching in a field over three arrows. What makes it worth
/// having at all is that ammunition is otherwise bought, and an archer that has to buy every arrow it fires
/// spends its takings on being able to earn them.
/// </para>
/// </summary>
public sealed class BotGlean : BotDeed
{
    /// <summary>The ledger key.</summary>
    public const string Trade = "glean";

    /// <summary>What gathering is reckoned at per minute before experience corrects it. Deliberately small.</summary>
    public static double Prior { get; set; } = 14.0;

    public static double WorkMinutes { get; set; } = 1.0;

    /// <summary>How far around itself an archer looks for what it has shot away.</summary>
    public static int Reach { get; set; } = 20;

    /// <summary>How near a bot has to be to pick something up off the floor.</summary>
    public static int Touch { get; set; } = 2;

    private readonly Type _kind;

    private readonly Map _map;

    private readonly Point3D _where;

    private int _gathered;

    public BotGlean(Type kind, Map map, Point3D where)
    {
        _kind = kind;
        _map = map;
        _where = where;
    }

    public override string Kind => Trade;

    public override Map Map => _map;

    public override Point3D Where => _where;

    public override double Expects => Prior;

    public override double Minutes => WorkMinutes;

    /// <summary>Nothing. Bending down teaches a bot nothing at all.</summary>
    public override SkillName? Trains => null;

    public override int Outlay => 0;

    /// <summary>No coin changes hands; what comes back is goods, and goods it would otherwise have bought.</summary>
    public override double Coin => 0.0;

    /// <summary>What was picked up, at whatever the shard reckons an arrow is worth.</summary>
    public override int Made => _gathered * BotAuction.Worth(_kind, 1);

    public override string Stage =>
        _gathered > 0 ? $"gathered {_gathered} {_kind?.Name}" : $"after spent {_kind?.Name}";

    public override BotDoing Advance(IBotWilful bot)
    {
        var body = bot?.Self;

        if (body == null || _map == null || _map == Map.Internal || _kind == null)
        {
            return BotDoing.Failed("no body");
        }

        var lying = Nearest(body, _kind, Reach);

        if (lying == null)
        {
            // Nothing left within reach. Finished rather than failed: the ground was worth a look, and what
            // was there is now in the quiver.
            return _gathered > 0
                ? BotDoing.Done($"{_gathered} {_kind.Name} back off the ground")
                : BotDoing.Done("nothing left lying about");
        }

        if (!body.InRange(lying.GetWorldLocation(), Touch))
        {
            return BotDoing.Walk(_map, lying.GetWorldLocation(), BotArrival.Within(Touch), $"after spent {_kind.Name}");
        }

        var pack = body.Backpack;

        if (pack == null)
        {
            return BotDoing.Failed("nowhere to put it");
        }

        _gathered += Math.Max(1, lying.Amount);

        pack.DropItem(lying);

        return BotDoing.Work("gathering");
    }

    /// <summary>
    /// The nearest of this kind lying loose on the ground, or null.
    ///
    /// <para>
    /// On the ground specifically — <c>Parent == null</c> — because ammunition inside a container belongs to
    /// somebody: a corpse being looted, a bot's own pack, a shopkeeper's stock. Only what was dropped in the
    /// world is free to pick up.
    /// </para>
    /// </summary>
    public static Item Nearest(Mobile bot, Type kind, int range)
    {
        var map = bot?.Map;

        if (map == null || map == Map.Internal || kind == null)
        {
            return null;
        }

        Item best = null;
        var bestAway = double.MaxValue;

        foreach (var item in map.GetItemsInRange(bot.Location, range))
        {
            if (item.Deleted || item.Parent != null || !item.Movable || item.GetType() != kind)
            {
                continue;
            }

            var away = bot.GetDistanceToSqrt(item.Location);

            if (away >= bestAway)
            {
                continue;
            }

            best = item;
            bestAway = away;
        }

        return best;
    }
}

/// <summary>
/// Offers an archer the job of picking its arrows back up, when it is short of them and some are lying about.
///
/// <para>
/// Only a bot that shoots, and only one that is actually short: the quiver it was born with is the measure,
/// so a full archer walks past its own spent arrows rather than tidying the field for its own sake.
/// </para>
/// </summary>
public sealed class BotGleaner : IBotProposer
{
    public string Name => "Gleaner";

    public BotStanding Rung => BotStanding.Free;

    public BotDeed Propose(IBotWilful bot)
    {
        var body = bot?.Self;
        var map = body?.Map;
        var bond = bot?.Bond;

        if (map == null || map == Map.Internal || bond == null || !body.Alive)
        {
            return null;
        }

        var kind = bond.Weapon?.Ammunition;

        if (kind == null)
        {
            return null;
        }

        // Short of what it was issued. Bound ammunition is a ceiling, so this is the honest measure of "should
        // be carrying more".
        var granted = BotBinding.BoundCount(kind, bond);
        var carried = body.Backpack?.GetAmount(kind) ?? 0;

        if (granted <= 0 || carried >= granted)
        {
            return null;
        }

        var lying = BotGlean.Nearest(body, kind, BotGlean.Reach);

        return lying == null ? null : new BotGlean(kind, map, lying.GetWorldLocation());
    }
}
