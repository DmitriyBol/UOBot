using System;
using System.Collections.Generic;
using Server.Items;

namespace Server.BotAI.V2;

public sealed class BotGlean : BotDeed
{

    public const string Trade = "glean";

    public static double Prior { get; set; } = 14.0;

    public static double WorkMinutes { get; set; } = 1.0;

    public static int Reach { get; set; } = 20;

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

    public override bool Resumes => true;

    public override Map Map => _map;

    public override Point3D Where => _where;

    public override double Expects => Prior;

    public override double Minutes => WorkMinutes;

    public override SkillName? Trains => null;

    public override int Outlay => 0;

    public override double Coin => 0.0;

    public override bool Bend(IBotWilful bot)
    {
        if (_where != Point3D.Zero)
        {
            bot?.Resolve?.Ledger?.Beware(Trade, _map, _where);
        }

        return false;
    }

    public override int Made => _gathered * BotMarket.Worth(_kind, 1);

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

        BotUnload.IntoPack(body, pack, lying);

        return BotDoing.Work("gathering");
    }

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

public sealed class BotGleaner : IBotProposer
{

    public static long Sealed { get; private set; }

    public static long Baulked { get; private set; }

    public static void Forget()
    {
        Sealed = 0;
        Baulked = 0;
    }

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

        var granted = BotBinding.BoundCount(kind, bond);
        var carried = body.Backpack?.GetAmount(kind) ?? 0;

        if (granted <= 0 || carried >= granted)
        {
            return null;
        }

        var lying = BotGlean.Nearest(body, kind, BotGlean.Reach);

        if (lying == null)
        {
            return null;
        }

        var where = lying.GetWorldLocation();

        if (bot.Resolve?.Ledger?.Cautious(BotGlean.Trade, map, where) == true)
        {
            Baulked++;

            return null;
        }

        if (BotReach.Ask(map, body.Location, where, BotArrival.Within(BotGlean.Touch)) == BotReachVerdict.Sealed)
        {
            Sealed++;

            return null;
        }

        return new BotGlean(kind, map, where);
    }
}
