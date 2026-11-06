using System;

namespace Server.BotAI.V2;

public sealed class BotDraw : BotDeed
{

    public const string Trade = "draw";

    public static int Near { get; set; } = 4;

    private readonly Map _map;

    private readonly Point3D _bank;

    private readonly Type _kind;

    private readonly int _units;

    private int _taken;

    public BotDraw(Map map, Point3D bank, Type kind, int units)
    {
        _map = map;
        _bank = bank;
        _kind = kind;
        _units = Math.Max(1, units);
    }

    public override string Kind => Trade;

    public override Map Map => _map;

    public override Point3D Where => _bank;

    public override double Expects => BotCraftFirst.DrawClaim;

    public override double Minutes => 0.5;

    public override double Coin => 0.0;

    public override int Made => 0;

    public override bool Unpaid => true;

    public override bool Steadfast => true;

    public override string Stage =>
        _taken > 0 ? $"took {_taken} {_kind?.Name} out of its bank box" : $"to the bank for its own {_units} {_kind?.Name}";

    public override void Taken(IBotWilful bot) => BotCraftFirst.Walked();

    public override bool Bend(IBotWilful bot)
    {
        bot?.Resolve?.Ledger?.Beware(Trade, _map, _bank);

        return false;
    }

    public override BotDoing Advance(IBotWilful bot)
    {
        var body = bot?.Self;

        if (body == null || _kind == null)
        {
            return BotDoing.Failed("no body");
        }

        if (!BotMarket.AtBank(body))
        {
            return BotDoing.Walk(_map, _bank, BotArrival.Within(Near), $"to the bank for its own {_units} {_kind.Name}");
        }

        _taken = BotMarket.FromBank(bot, _kind, _units);

        BotCraftFirst.Served(body, _taken);

        if (_taken <= 0)
        {
            return BotDoing.Failed($"its bank box held no {_kind.Name} it could take");
        }

        return BotDoing.Done($"took {_taken} {_kind.Name} out of its bank box for an order it had passed over");
    }
}
