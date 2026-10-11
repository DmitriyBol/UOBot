using System;
using System.Collections.Generic;

namespace Server.BotAI.V2;

/// <summary>
/// Carrying what somebody said it wants to where it is, and handing it over: the maker's walk to the one who ordered, the
/// hunter's walk to the cook, the miner's walk to the smith.
///
/// <para>
/// <b>What a fill became (01.10.2026, Patrick's order of 30.09: "every order and every purchase must now be hand to hand;
/// our pathfinding works very well").</b> A fill used to be one line of arithmetic wherever the supplier stood, and the goods
/// then waited in the market for the buyer. Now the supplier walks: the buyer is followed rather than aimed at — a buyer is at
/// work and moves — and at <see cref="BotHandover.MeetTiles"/> the goods go pack to pack and the coin comes back
/// (<see cref="BotHandover.Exchange"/>). The units carried are spoken for on the want from the moment the delivery is taken
/// (<see cref="BotWant.Reserve"/>), so a second carrier does not set out for the same five ribs.
/// </para>
///
/// <para>
/// <b>Seen through, and given a clock.</b> Steadfast like any trip with goods in the pack, put down for a flight and taken up
/// again; but a buyer who keeps walking away is not caught by walking after it for ever, so after <see cref="GiveUpMs"/> of
/// walking the delivery fails and says so, and the buyer's want is left for somebody nearer.
/// </para>
/// </summary>
public sealed class BotDeliver : BotDeed, IBotWantCarrier
{
    public const string Trade = "deliver";

    public static double WorkMinutes { get; set; } = 0.5;

    public static int GiveUpMs { get; set; } = 480000;

    public static int ReleaseDownMs { get; set; } = 180000;

    public static long Takes { get; private set; }

    public static long Delivered { get; private set; }

    public static long DeliveredUnits { get; private set; }

    public static long DeliveredGold { get; private set; }

    public static long Tiles { get; private set; }

    public static long BuyerGone { get; private set; }

    public static long BuyerDead { get; private set; }

    public static long OtherLand { get; private set; }

    public static long Answered { get; private set; }

    public static long Unloaded { get; private set; }

    public static long Late { get; private set; }

    public static long Refused { get; private set; }

    public static long Blocked { get; private set; }

    private readonly BotWant _want;

    private readonly Type _kind;

    private readonly int _price;

    private readonly double _claim;

    private readonly Map _map;

    private readonly Point3D _where;

    private readonly string _buyerName;

    private int _units;

    private long _resumedTick;

    private long _walkedMs;

    private Point3D _from;

    private int _handed;

    private int _paid;

    private int _fromBox;

    public static long Boxed { get; private set; }

    public static long Reopened { get; private set; }

    public static long Overtaken { get; private set; }

    public static long Unafforded { get; private set; }

    private bool _everReopened;

    private long _downMs;

    private bool _reopened;

    private readonly bool _far;

    private readonly bool _tail;

    private long _giveUpMs = GiveUpMs;

    public static long FarTakes { get; private set; }

    public static long FarDelivered { get; private set; }

    public static long FarFailed { get; private set; }

    public static long TailTakes { get; private set; }

    public static long TailDelivered { get; private set; }

    public BotDeliver(BotWant want, Type kind, int units, int price, double claim, Map map, Point3D where, int fromBox = 0, bool far = false, bool tail = false)
    {
        _far = far;
        _tail = tail;
        _fromBox = Math.Max(0, fromBox);
        _want = want;
        _kind = kind;
        _units = Math.Max(1, units);
        _price = Math.Max(1, price);
        _claim = Math.Max(0.01, claim);
        _map = map;
        _where = where;
        _buyerName = want?.Buyer?.Self?.Name ?? "somebody";

        _resumedTick = Core.TickCount;
    }

    public BotWant Want => _want;

    public bool Carries(BotWant want) => ReferenceEquals(_want, want);

    public Type Goods => _kind;

    public override string Kind => Trade;

    public override Map Map => _map;

    public override Point3D Where => _where;

    public override double Expects => _claim;

    public override double Minutes => WorkMinutes;

    public override SkillName? Trains => null;

    public override int Outlay => 0;

    public override double Coin => 1.0;

    public override int Made => 0;

    public override bool Steadfast => true;

    public override string Stage =>
        _handed > 0
            ? $"handed {_handed} {_want?.Label} to {_buyerName} for {_paid}gp"
            : $"bringing {_units} {_want?.Label} to {_buyerName} at {_price}gp each";

    public override void Taken(IBotWilful bot)
    {
        _resumedTick = Core.TickCount;
        _from = bot?.Self?.Location ?? Point3D.Zero;
        _want?.Reserve(bot, _units, _price);
        Takes++;

        if (_from != Point3D.Zero)
        {
            _giveUpMs = Math.Max(GiveUpMs, (long)(2.0 * BotAppraisal.Travel(_from, _where) * 60000.0));
        }

        if (_far)
        {
            FarTakes++;
        }

        if (_tail)
        {
            TailTakes++;
        }

        var body = bot?.Self;
        var buyer = _want?.Buyer?.Self;

        if (body == null || buyer == null)
        {
            return;
        }

        var fill = new Dictionary<string, string>
        {
            ["buyer"] = buyer.Name,
            ["amount"] = _units.ToString(),
            ["item"] = BotShopkeep.Words(_kind),
            ["price"] = _price.ToString()
        };

        BotVoice.Say(
            body,
            "trade",
            BotVoice.Phrase("market:bring", null, body, fill) ?? $"{buyer.Name}, I have your {_units} {fill["item"]} at {_price} each. Coming to you."
        );
    }

    public override void Paused(IBotWilful bot)
    {
        _walkedMs += Math.Max(0L, Core.TickCount - _resumedTick);
        _paused = true;
        _pausedTick = Core.TickCount;
    }

    public override void Resumed(IBotWilful bot)
    {
        var now = Core.TickCount;

        if (_paused)
        {
            _downMs += Math.Max(0L, now - _pausedTick);
        }

        _resumedTick = now;
        _paused = false;

        if (_want != null && !_want.Carried(bot))
        {
            if (BotMarket.Stands(_want))
            {
                _reopened = true;

                if (!_everReopened)
                {
                    _everReopened = true;
                    Reopened++;
                }
            }

            return;
        }

        _want?.Reserve(bot, _units, _price);
    }

    private bool _paused;

    private bool _farEnded;

    private long _pausedTick;

    public bool Stale
    {
        get
        {
            if (!_paused)
            {
                return false;
            }

            var down = Core.TickCount - _pausedTick;

            return down >= GiveUpMs || ReleaseDownMs > 0 && _downMs + down >= ReleaseDownMs;
        }
    }

    internal bool Away
    {
        get
        {
            if (!_paused || ReleaseDownMs <= 0)
            {
                return false;
            }

            var down = Core.TickCount - _pausedTick;

            return down < GiveUpMs && _downMs + down >= ReleaseDownMs;
        }
    }

    public override void Drop(IBotWilful bot)
    {
        if (_far && _handed <= 0 && !_farEnded)
        {
            _farEnded = true;
            FarFailed++;
        }

        _want?.Release(bot);

        BotRendezvous.Gone(bot, _want?.Buyer);
    }

    public static long Meetings { get; private set; }

    public static long AtPlace { get; private set; }

    private bool _called;

    public override bool Bend(IBotWilful bot)
    {
        var buyer = _want?.Buyer?.Self;

        if (_place != Point3D.Zero)
        {
            BotWayless.NoWay(_map, _place, true);
        }

        if (_place != Point3D.Zero && !_placeBent)
        {
            _placeBent = true;
            BotWayless.Rejoin();

            if (BotRendezvous.Standing(buyer) is { } meeting && ReferenceEquals(meeting.Carrier, bot))
            {
                meeting.Place = Point3D.Zero;
            }

            _place = Point3D.Zero;

            return true;
        }

        Blocked++;

        if (buyer != null && _place == Point3D.Zero)
        {
            BotWayless.NoWay(buyer.Map, buyer.Location, false);
        }

        bot?.Resolve?.Ledger?.Beware(Trade, buyer?.Map ?? _map, buyer?.Location ?? _where);

        BotUnreached.NoWay(buyer?.Map ?? _map, buyer, _from, bot?.Self);

        return false;
    }

    public override bool BendIsTrouble => false;

    private Point3D _place;

    private bool _placeBent;

    public static double HoldStretch { get; set; } = 1.5;

    private double _holdFor;

    private bool _roadCounted;

    public static long RoadLonger { get; private set; }

    public override double HoldsFor => _holdFor;

    private void Reckon(Mobile body, Mobile buyer, long walked)
    {
        if (HoldStretch <= 0.0)
        {
            _holdFor = 0.0;

            return;
        }

        var straight = Math.Max(Math.Abs(buyer.X - body.X), Math.Abs(buyer.Y - body.Y));
        var road = (body as BotMobile)?.Journey.RoadLeft(body.Location) ?? 0;

        if (!_roadCounted && straight > 0 && road * 5 >= straight * 6)
        {
            _roadCounted = true;
            RoadLonger++;
        }

        var left = Math.Max(road, straight) * (double)BotWalk.StepDelayMs(false) / 60000.0;

        _holdFor = Math.Min(walked / 60000.0 + left * HoldStretch + WorkMinutes, _giveUpMs / 60000.0);
    }

    public override BotDoing Advance(IBotWilful bot)
    {
        var body = bot?.Self;

        if (body == null)
        {
            return BotDoing.Failed("no body");
        }

        var buyer = _want?.Buyer?.Self;

        if (buyer == null || buyer.Deleted)
        {
            BuyerGone++;

            return BotDoing.Failed($"{_buyerName} is gone");
        }

        if (!buyer.Alive)
        {
            BuyerDead++;

            return BotDoing.Failed($"{_buyerName} is dead");
        }

        if (!BotMarket.Stands(_want) || _want.Amount <= 0)
        {
            Answered++;

            return BotDoing.Failed($"{_buyerName}'s want for {_want.Label} was met or withdrawn before it arrived");
        }

        if (_reopened)
        {
            _reopened = false;

            var open = Math.Min(_units, _want.Payable);

            if (open <= 0)
            {
                Overtaken++;

                return BotDoing.Failed($"another carrier took {_buyerName}'s {_want.Label} on while this walk was put down");
            }

            open = Math.Min(open, BotMarket.Affords(_want, _price, bot));

            if (open <= 0)
            {
                Unafforded++;

                return BotDoing.Failed($"{_buyerName}'s money no longer carried its {_want.Label} at {_price}gp when this walk was taken up again");
            }

            _units = open;
            _want.Reserve(bot, open, _price, true);
        }

        if (_fromBox > 0)
        {
            Boxed += BotMarket.FromBank(bot, _kind, _fromBox);
            _fromBox = 0;
        }

        var held = BotHandover.Carried(bot, _kind);

        if (held <= 0)
        {
            Unloaded++;

            return BotDoing.Failed($"no longer carrying any {_want.Label}");
        }

        _units = Math.Min(Math.Min(_units, held), _want.Amount);

        if (buyer.Map != body.Map || buyer.Map == null || buyer.Map == Map.Internal)
        {
            OtherLand++;

            return BotDoing.Failed($"{_buyerName} is on another land");
        }

        if (!body.InRange(buyer.Location, BotHandover.MeetTiles))
        {
            var walked = _walkedMs + Math.Max(0L, Core.TickCount - _resumedTick);

            if (walked >= _giveUpMs)
            {
                Late++;

                return BotDoing.Failed($"could not catch up with {_buyerName} in {walked / 60000.0:F1} minutes");
            }

            Reckon(body, buyer, walked);

            var apart = Math.Max(Math.Abs(buyer.X - body.X), Math.Abs(buyer.Y - body.Y));

            if (BotRendezvous.Running && apart <= BotRendezvous.CallTiles && BotRendezvous.Call(bot, _want.Buyer, _want, _units) is { } meeting)
            {
                if (!_called)
                {
                    _called = true;
                    Meetings++;
                }

                if (meeting.Place != Point3D.Zero)
                {
                    AtPlace++;
                    _place = meeting.Place;

                    return body.InRange(meeting.Place, 2)
                        ? BotDoing.Work($"waiting at the meeting place for {_buyerName} with {_units} {_want.Label}")
                        : BotDoing.Walk(buyer.Map, meeting.Place, BotArrival.Within(2), $"to the meeting place for {_buyerName} with {_units} {_want.Label}");
                }
            }

            _place = Point3D.Zero;

            return BotDoing.Walk(buyer.Map, buyer, BotArrival.Within(BotHandover.MeetTiles), $"to {_buyerName} with {_units} {_want.Label}");
        }

        var given = BotHandover.Exchange(bot, buyer, _kind, _units, _price, true, "against its want", out var paid, out var refused);

        if (given <= 0)
        {
            Refused++;

            if (buyer.Alive && BotMarket.Affords(_want, _want.Offer, bot) <= 0)
            {
                BotMarket.Withdrawn(_want.Buyer, _kind, BotWithdraw.Unpaid);
            }

            return BotDoing.Failed(refused ?? $"{_buyerName} would not take it");
        }

        _handed = given;
        _paid = paid;

        BotMarket.Received(_want, given, paid, body, BotRoute.Carrier);
        BotPact.Filled(body, buyer);

        BotUnreached.Reached(body.Map, buyer, _from);

        BotWayless.Reached(body.Map, buyer.Location);

        BotRendezvous.Handed(_want.Buyer);

        Delivered++;
        DeliveredUnits += given;
        DeliveredGold += paid;

        BotSurplus.CountIfCapped(bot, BotSurplus.ToBot, _kind, given);

        if (_far)
        {
            FarDelivered++;
        }

        if (_tail)
        {
            TailDelivered++;
        }

        if (_from != Point3D.Zero)
        {
            Tiles += Math.Max(Math.Abs(_from.X - body.X), Math.Abs(_from.Y - body.Y));
        }

        return BotDoing.Done($"handed {given} {_want.Label} to {_buyerName} for {paid}gp");
    }

    public static string Describe() =>
        Takes == 0
            ? "no delivery has been taken"
            : $"{Takes} taken, {Delivered} handed over ({DeliveredUnits} things for {DeliveredGold}gp, {(Delivered > 0 ? Tiles / Delivered : 0)} tiles on average from where the carrier set out; {Boxed} things taken out of a carrier's bank box for them); "
              + $"failed: {Answered} found the want met or withdrawn, {BuyerGone} the buyer gone, {BuyerDead} the buyer dead, {OtherLand} the buyer on another land, {Unloaded} the carrier no longer holding it, {Late} could not catch up in {GiveUpMs / 60000} minutes, {Refused} refused at the meeting, {Blocked} no way to the buyer, {Overtaken} another carrier had taken the units on while it was put down, {Unafforded} the buyer's money no longer carried them when it was taken up again; "
              + $"{Meetings} deliveries called a meeting once within {BotRendezvous.CallTiles} tiles of the buyer, {AtPlace} beats spent going to or waiting at a guarded meeting place; "
              + $"{Reopened} walks taken up again after being put down {ReleaseDownMs / 60000} minutes in all or {GiveUpMs / 60000} at once, their units opened to other carriers meanwhile (the {Overtaken} and {Unafforded} above among them; the rest held again what was still open); "
              + $"far walks past {BotCourier.Reach} tiles: {FarTakes} taken, {FarDelivered} handed over, {FarFailed} ended with nothing handed; tails under {BotCourier.LeastGold}gp: {TailTakes} taken, {TailDelivered} handed over; {RoadLonger} walks held by the planner's road, a fifth or more longer than the straight line (x{HoldStretch:F1}, within the give-up clock); {BotSquads.CarriersSpared} company calls passed a carrier holding another bot's goods by, {BotCry.CarriersSpared} bots carrying for a buyer left out of a dire cry's calling past {BotCry.CarrierReach} tiles";

    public static void Forget()
    {
        Takes = 0;
        Boxed = 0;
        Delivered = 0;
        DeliveredUnits = 0;
        DeliveredGold = 0;
        Tiles = 0;
        BuyerGone = 0;
        BuyerDead = 0;
        OtherLand = 0;
        Answered = 0;
        Unloaded = 0;
        Late = 0;
        Refused = 0;
        Blocked = 0;
        Meetings = 0;
        AtPlace = 0;
        Reopened = 0;
        Overtaken = 0;
        Unafforded = 0;
        FarTakes = 0;
        FarDelivered = 0;
        FarFailed = 0;
        TailTakes = 0;
        TailDelivered = 0;
        RoadLonger = 0;
    }
}
