using System;

namespace Server.BotAI.V2;

/// <summary>
/// A shift keeping shop: walk to a pitch beside the bank, open, call out what is for sale, serve whoever comes, and shut when the
/// goods or the shift run out.
///
/// <para>
/// <b>Standing still is the work, once the shop is open</b> (<see cref="BotDeed.Still"/>). The stall watch takes work off a bot
/// that has neither moved nor changed its stage for four minutes, which is right for a walk into a wall and wrong for a keeper
/// behind a counter; the shift's own clock (<see cref="BotShopkeep.ShiftMinutes"/>) ends it instead, as the drill field's roll
/// ends a class. On the way to the bank it is a walk like any other and the watch keeps its eye on it.
/// </para>
///
/// <para>
/// <b>Held, from the first step, against the next thing on offer</b> (<see cref="BotDeed.Steadfast"/>). A shop is a promise to
/// the bots already walking to it: a keeper that left its counter for a dig worth a little more would send every one of them
/// on to a shopkeeper and teach them that the walk to a bot is not worth making. Put down, rather than dropped, for what will
/// not wait — a flight, a friend in trouble — and the shop is shut while the keeper is away (<see cref="Paused"/>) and opened
/// again when it is back on its pitch, the shift's clock counting only the minutes it was open.
/// </para>
///
/// <para>
/// <b>Honest endings.</b> Finished when the shift has run its course or the goods are gone, with what it sold and to how many in
/// the note — a shift that sold nothing is still a shift kept, and says so, and the ledger reads the nought. Failed when the
/// pitch cannot be reached, the bank will not take another shop, or the goods were gone before the shop could open. The takings
/// are measured as coin (<see cref="Coin"/> one, <see cref="Made"/> nought): the goods were the keeper's before the shift and the
/// coin is what the shift turned them into, which is the peddler's arithmetic.
/// </para>
/// </summary>
public sealed class BotKeepShop : BotDeed
{
    public const string Trade = "shopkeep";

    private readonly Map _map;

    private readonly Point3D _bank;

    private readonly double _claim;

    private readonly int _shown;

    private Point3D _stand;

    private BotStorefront _front;

    private double _openBefore;

    private long _openedTick;

    private long _cryTick;

    private int _repitched;

    private int _sold;

    private int _takings;

    private int _customers;

    private int _firstShown = -1;

    public static int MostRepitches { get; set; } = 2;

    public BotKeepShop(Map map, Point3D stand, Point3D bank, double claim, int shown)
    {
        _map = map;
        _stand = stand;
        _bank = bank;
        _claim = Math.Max(0.01, claim);
        _shown = shown;
    }

    public override string Kind => Trade;

    public override Map Map => _map;

    public override Point3D Where => _stand;

    public override double Expects => _claim;

    public override double Minutes => BotShopkeep.ShiftMinutes;

    public override SkillName? Trains => null;

    public override int Outlay => 0;

    public override double Coin => 1.0;

    public override int Made => 0;

    public override bool Still => _front != null;

    public override bool Committed => _front != null;

    public override bool Steadfast => true;

    public override double HoldsFor => _front != null ? Math.Max(1.0, BotShopkeep.ShiftMinutes - OpenMinutes(Core.TickCount)) + 1.0 : 0.0;

    public override bool Hurries => _front == null;

    public override string Stage =>
        _front != null
            ? $"keeping shop by the bank in {_front.Town}: {_front.Units} things of {_front.Kinds} kinds, sold {Sold} to {Customers} for {Takings}gp"
            : $"going to the bank at ({_bank.X}, {_bank.Y}) to keep shop with {_shown}gp of goods the island is asking for";

    private int Sold => _sold + (_front?.Sold ?? 0);

    private int Takings => _takings + (_front?.Takings ?? 0);

    private int Customers => _customers + (_front?.Customers ?? 0);

    private double OpenMinutes(long now) => _openBefore + (_front != null ? (now - _openedTick) / 60000.0 : 0.0);

    public override BotDoing Advance(IBotWilful bot)
    {
        if (bot?.Self is not BotMobile body || _map == null || _map == Map.Internal)
        {
            return BotDoing.Failed("no body");
        }

        var now = Core.TickCount;

        if (!BotShopkeep.Running)
        {
            Shut(false, now);

            return BotDoing.Done("the shops were shut by order");
        }

        if (_front == null)
        {
            return Opening(body, now);
        }

        if (_front.Shut)
        {
            Carry(now);
            _front = null;

            return Opening(body, now);
        }

        if (now - _front.StockedTick >= BotShopkeep.StockMs)
        {
            BotShopkeep.Refresh(_front);
        }

        if (_front.Units <= 0)
        {
            var note = Summary(now, "sold out");

            Shut(true, now, soldOut: true);

            return BotDoing.Done(note);
        }

        if (OpenMinutes(now) >= BotShopkeep.ShiftMinutes)
        {
            var note = Summary(now, "the shift is over");

            Shut(true, now);

            return BotDoing.Done(note);
        }

        if (body.Map != _map || !body.InRange(_stand, BotShopkeep.StandReach + BotShopkeep.ServeReach))
        {
            return BotDoing.Walk(_map, _stand, BotArrival.Within(BotShopkeep.StandReach), "back to its shop by the bank");
        }

        if (now - _cryTick >= BotShopkeep.CryMs)
        {
            _cryTick = now;
            BotShopkeep.Cry(body, _front, "shop:cry");
        }

        return BotDoing.Work(Stage);
    }

    private BotDoing Opening(BotMobile body, long now)
    {
        if (body.Map != _map || !body.InRange(_stand, BotShopkeep.StandReach))
        {
            return BotDoing.Walk(_map, _stand, BotArrival.Within(BotShopkeep.StandReach), $"to the bank at ({_bank.X}, {_bank.Y}) to keep shop");
        }

        if (BotShopkeep.Taken(_map, _stand, body) && _repitched < MostRepitches)
        {
            var next = BotShopkeep.Pitch(_map, _bank, body);

            if (next != Point3D.Zero && next != _stand)
            {
                _repitched++;
                _stand = next;

                BotShopkeep.Reserve(body, _map, _stand, _bank);

                return BotDoing.Walk(_map, _stand, BotArrival.Within(BotShopkeep.StandReach), "to another pitch at the bank");
            }
        }

        _front = BotShopkeep.OpenShop(body, this, _map, _stand, _bank, _firstShown >= 0, out var why);

        if (_front == null)
        {
            return BotDoing.Failed(why ?? "the bank would not take another shop");
        }

        if (_front.Units <= 0)
        {
            Shut(false, now);

            return BotDoing.Failed("nothing left to sell by the time it reached the bank");
        }

        _openedTick = now;
        _cryTick = now;

        if (_firstShown < 0)
        {
            _firstShown = _front.Shown;
        }

        BotShopkeep.Cry(body, _front, "shop:open");

        return BotDoing.Work(Stage);
    }

    private string Summary(long now, string why)
    {
        var minutes = OpenMinutes(now);

        return Sold > 0
            ? $"{why}: sold {Sold} things to {Customers} customers for {Takings}gp in {minutes:F1} minutes by the bank in {_front?.Town}"
            : $"{why}: kept shop {minutes:F1} minutes by the bank in {_front?.Town} and nobody came to buy";
    }

    private void Carry(long now)
    {
        if (_front == null)
        {
            return;
        }

        _openBefore += (now - _openedTick) / 60000.0;
        _sold += _front.Sold;
        _takings += _front.Takings;
        _customers += _front.Customers;
    }

    private void Shut(bool learn, long now, bool soldOut = false)
    {
        if (_front == null)
        {
            return;
        }

        var front = _front;
        var minutes = OpenMinutes(now);

        Carry(now);
        _front = null;

        if (learn && front.Keeper != null)
        {
            BotShopkeep.Cry(front.Keeper, front, "shop:close");
        }

        BotShopkeep.Close(front, learn, soldOut, minutes, Math.Max(0, _firstShown), _sold, _takings);
    }

    public override bool Bend(IBotWilful bot)
    {
        bot?.Resolve?.Ledger?.Beware(Trade, _map, _stand);

        return false;
    }

    public override void Taken(IBotWilful bot) => BotShopkeep.Reserve(bot?.Self, _map, _stand, _bank);

    public override void Paused(IBotWilful bot)
    {
        Shut(false, Core.TickCount);
        BotShopkeep.Release(bot?.Self);
    }

    public override void Resumed(IBotWilful bot) => BotShopkeep.Reserve(bot?.Self, _map, _stand, _bank);

    public override void Drop(IBotWilful bot)
    {
        var now = Core.TickCount;

        Shut(OpenMinutes(now) >= BotShopkeep.ShiftMinutes * 0.5, now);
        BotShopkeep.Release(bot?.Self);
    }
}
