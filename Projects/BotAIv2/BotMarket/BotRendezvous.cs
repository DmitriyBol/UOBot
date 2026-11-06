using System;
using System.Collections.Generic;
using Server.Logging;

namespace Server.BotAI.V2;

public static class BotRendezvous
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotRendezvous));

    public static bool Running { get; set; } = true;

    public static int CallTiles { get; set; } = 60;

    public static int StaleMs { get; set; } = 45000;

    public static int MeetMs { get; set; } = 180000;

    public static int WaitMs { get; set; } = 20000;

    public static int SafeReach { get; set; } = 160;

    public static double Prior { get; set; } = 150.0;

    public sealed class Approach
    {
        public IBotWilful Carrier;

        public IBotWilful Buyer;

        public BotWant Want;

        public int Units;

        public Point3D Place;

        public bool Safe;

        public long CalledTick;

        public long Tick;

        public bool Taken;
    }

    private static readonly Dictionary<Serial, Approach> _approaches = [];

    public static long Arranged { get; private set; }

    public static long OnSafeGround { get; private set; }

    public static long TakenUp { get; private set; }

    public static long Met { get; private set; }

    public static long Missed { get; private set; }

    public static long BuyerWaited { get; private set; }

    public static long StoodApart { get; private set; }

    public static long CarrierGaveUp { get; private set; }

    public static long Doubled { get; private set; }

    public static int StandTiles { get; set; } = 10;

    public static int StandMs { get; set; } = 1500;

    public static int StandMostMs { get; set; } = 10000;

    public static long Stood { get; private set; }

    public static long StoodHanded { get; private set; }

    public static long StandSpent { get; private set; }

    public static long StandPassed { get; private set; }

    public sealed class StandClock
    {
        internal bool Began;

        internal bool Spent;

        internal long Since;
    }

    public static bool Stand(Mobile carrier, Mobile buyer, int apart, StandClock clock)
    {
        if (!Running || StandTiles <= 0 || clock == null || clock.Spent || apart > StandTiles
            || buyer is not BotMobile { Deleted: false, Alive: true } bot || carrier == null || bot.Map != carrier.Map)
        {
            return false;
        }

        var journey = bot.Journey;

        if (journey is not { Active: true } || journey.Current is not { } walk || !journey.Moving && !journey.Held)
        {
            return false;
        }

        if (walk.Interruption || bot.Squad != null || bot.Combatant != null || bot.Hidden
            || walk.Reason != null && walk.Reason.StartsWith("away from", StringComparison.Ordinal)
            || bot.Resolve?.Deed is BotMeet || bot.Resolve?.Deed is { } deed && (deed.Foe != null || deed.Braves))
        {
            StandPassed++;

            return false;
        }

        var now = Core.TickCount;

        if (!clock.Began)
        {
            clock.Began = true;
            clock.Since = now;
            Stood++;
        }
        else if (now - clock.Since >= StandMostMs)
        {
            clock.Spent = true;
            StandSpent++;

            return false;
        }

        journey.Hold(StandMs);

        return true;
    }

    internal static void HandedAfterStand() => StoodHanded++;

    public static Approach Call(IBotWilful carrier, IBotWilful buyer, BotWant want, int units)
    {
        var body = carrier?.Self;
        var other = buyer?.Self;

        if (!Running || body is not { Deleted: false, Alive: true } || other is not { Deleted: false, Alive: true } || body.Map != other.Map)
        {
            return null;
        }

        var now = Core.TickCount;

        if (BotRoom.Shut(other) || want != null && !BotRoom.Receives(other, want.Kind))
        {
            BotRoom.Note(other);
            BotRoom.MeetNotCalled();

            if (_approaches.TryGetValue(other.Serial, out var held) && ReferenceEquals(held.Carrier, carrier))
            {
                Gone(carrier, buyer);
            }

            return null;
        }

        if (_approaches.TryGetValue(other.Serial, out var standing) && now - standing.Tick < StaleMs)
        {
            if (!ReferenceEquals(standing.Carrier, carrier))
            {
                Doubled++;

                return null;
            }

            standing.Tick = now;
            standing.Units = units;

            return standing;
        }

        if (_approaches.Count >= 1024)
        {
            _approaches.Clear();
        }

        var approach = new Approach
        {
            Carrier = carrier,
            Buyer = buyer,
            Want = want,
            Units = units,
            CalledTick = now,
            Tick = now
        };

        approach.Place = SafePlace(body, other, out approach.Safe);

        _approaches[other.Serial] = approach;
        Arranged++;

        if (!approach.Safe)
        {
            OnSafeGround++;
        }

        logger.Information(
            "{Carrier} is {Tiles} tiles from {Buyer} with {Units} {Label} and calls the meeting{Place}",
            body.Name,
            Math.Max(Math.Abs(body.X - other.X), Math.Abs(body.Y - other.Y)),
            other.Name,
            units,
            want?.Label ?? "goods",
            approach.Place == Point3D.Zero
                ? ": they walk towards each other"
                : $" at ({approach.Place.X}, {approach.Place.Y}), guarded ground, since where they stand is not safe"
        );

        return approach;
    }

    private static Point3D SafePlace(Mobile carrier, Mobile buyer, out bool safe)
    {
        var map = carrier.Map;

        safe = !Unsafe(map, carrier) && !Unsafe(map, buyer);

        if (safe)
        {
            return Point3D.Zero;
        }

        var middle = new Point3D((carrier.X + buyer.X) / 2, (carrier.Y + buyer.Y) / 2, 0);
        var place = BotHaven.Nearest(map, middle, out _, out _);

        if (place == Point3D.Zero
            || Math.Max(Math.Abs(place.X - carrier.X), Math.Abs(place.Y - carrier.Y)) > SafeReach
            || Math.Max(Math.Abs(place.X - buyer.X), Math.Abs(place.Y - buyer.Y)) > SafeReach)
        {

            return Point3D.Zero;
        }

        if (BotWayless.Shut(map, place, true))
        {
            BotWayless.MovedMeeting();

            return Point3D.Zero;
        }

        return place;
    }

    private static bool Unsafe(Map map, Mobile body)
    {
        if (BotHaven.Guarded(map, body.Location))
        {
            return false;
        }

        if (BotZones.LevelAt(map, body.X, body.Y) is BotZoneLevel.Deadly or BotZoneLevel.Hostile)
        {
            return true;
        }

        return BotQuad.Muscle(map, body.Location) > BotThreat.Power(body);
    }

    public static Approach Standing(Mobile buyer)
    {
        if (buyer == null || !_approaches.TryGetValue(buyer.Serial, out var approach))
        {
            return null;
        }

        if (Core.TickCount - approach.Tick >= StaleMs)
        {
            _approaches.Remove(buyer.Serial);

            if (approach.Taken)
            {
                Missed++;
                FellOutOfCall++;
            }

            return null;
        }

        return approach;
    }

    public static long FellOutOfCall { get; private set; }

    public static void TookUp(Approach approach)
    {
        if (approach is { Taken: false })
        {
            approach.Taken = true;
            TakenUp++;
        }
    }

    public static void Handed(IBotWilful buyer)
    {
        var body = buyer?.Self;

        if (body == null || !_approaches.Remove(body.Serial, out var approach))
        {
            return;
        }

        if (approach.Taken)
        {
            Met++;
        }
    }

    public static void Gone(IBotWilful carrier, IBotWilful buyer)
    {
        var body = buyer?.Self;

        if (body == null || !_approaches.TryGetValue(body.Serial, out var approach) || !ReferenceEquals(approach.Carrier, carrier))
        {
            return;
        }

        _approaches.Remove(body.Serial);

        if (approach.Taken)
        {
            Missed++;
            CarrierGaveUp++;
        }
    }

    public static void Waited(Mobile buyer, bool apart)
    {
        if (buyer != null && _approaches.Remove(buyer.Serial, out var approach) && approach.Taken)
        {
            Missed++;

            if (apart)
            {
                BuyerWaited++;
            }
            else
            {
                StoodApart++;
            }
        }
    }

    public static string Describe() =>
        !Running
            ? "meetings are off; the carrier follows the buyer"
            : $"meetings: {Arranged} called by a carrier within {CallTiles} tiles ({OnSafeGround} of them moved to guarded ground for the ground one stood on), {TakenUp} taken up by the buyer, {Met} met with the goods handed over, {Missed} missed ({BuyerWaited} the buyer waited {MeetMs / 60000} minutes and the carrier never came, {StoodApart} stood together {WaitMs / 1000}s and nothing changed hands, {CarrierGaveUp} the carrier gave the delivery up first, {FellOutOfCall} the carrier fell out of call for {StaleMs / 1000}s), {Doubled} calls for a buyer already meeting another carrier; "
              + $"buyers stood for their carrier within {StandTiles} tiles: {Stood} walks ({StoodHanded} of them handed over, {StandSpent} stood {StandMostMs / 1000}s and walked on), {StandPassed} beats a walking buyer was not stood (a company, a fight, a flight, a meeting)";

    public static void Forget()
    {
        _approaches.Clear();
        Arranged = 0;
        OnSafeGround = 0;
        TakenUp = 0;
        Met = 0;
        Missed = 0;
        BuyerWaited = 0;
        StoodApart = 0;
        CarrierGaveUp = 0;
        FellOutOfCall = 0;
        Doubled = 0;
        Stood = 0;
        StoodHanded = 0;
        StandSpent = 0;
        StandPassed = 0;
    }
}

public sealed class BotMeeter : IBotProposer
{
    public string Name => "Meeter";

    public BotStanding Rung => BotStanding.Free;

    public static long Asked { get; private set; }

    public static long Offered { get; private set; }

    public BotDeed Propose(IBotWilful bot)
    {
        var body = bot?.Self;

        if (!BotRendezvous.Running || body?.Map == null || body.Map == Map.Internal || !body.Alive)
        {
            return null;
        }

        var approach = BotRendezvous.Standing(body);

        if (approach == null)
        {
            return null;
        }

        Asked++;

        if (bot.Squad != null || approach.Carrier?.Self is not { Deleted: false, Alive: true } carrier || carrier.Map != body.Map)
        {
            return null;
        }

        if (bot.Resolve?.Deed is BotMeet)
        {
            return null;
        }

        Offered++;

        return new BotMeet(approach);
    }

    public static void Forget()
    {
        Asked = 0;
        Offered = 0;
    }
}

public sealed class BotMeet : BotDeed
{
    public const string Trade = "meet";

    private readonly BotRendezvous.Approach _approach;

    private readonly Map _map;

    private readonly int _filled;

    private long _begun;

    private long _besideTick;

    private bool _started;

    public BotMeet(BotRendezvous.Approach approach)
    {
        _approach = approach;
        _map = approach?.Carrier?.Self?.Map;
        _filled = approach?.Want?.Filled ?? 0;
    }

    public Mobile Carrier => _approach?.Carrier?.Self;

    public override string Kind => Trade;

    public override Map Map => _map;

    public override Point3D Where => _approach?.Place != Point3D.Zero ? _approach.Place : Carrier?.Location ?? Point3D.Zero;

    public override double Expects => BotRendezvous.Prior;

    public override double Minutes => 1.0;

    public override double Coin => 1.0;

    public override int Made => _approach?.Want == null ? 0 : Math.Max(0, _approach.Want.Filled - _filled) * Math.Max(1, _approach.Want.Offer);

    public override bool Unpaid => true;

    public override bool Pressing(IBotWilful bot) => true;

    public override bool Summons => true;

    public override bool Resumes => true;

    public override bool Repeats(BotDeed other) => other is BotMeet meet && ReferenceEquals(meet._approach, _approach);

    public override string Stage =>
        _approach?.Place != Point3D.Zero
            ? $"meeting {Carrier?.Name ?? "a carrier"} at ({_approach.Place.X}, {_approach.Place.Y}) for {_approach.Units} {_approach.Want?.Label}"
            : $"going to meet {Carrier?.Name ?? "a carrier"} for {_approach?.Units} {_approach?.Want?.Label}";

    public override void Taken(IBotWilful bot) => BotRendezvous.TookUp(_approach);

    public override void Resumed(IBotWilful bot) => _besideTick = 0;

    public override BotDoing Advance(IBotWilful bot)
    {
        var body = bot?.Self;

        if (body == null || _approach == null || _map == null)
        {
            return BotDoing.Failed("no body");
        }

        var now = Core.TickCount;

        if (!_started)
        {
            _started = true;
            _begun = now;
        }

        var want = _approach.Want;

        if (want != null && want.Filled > _filled)
        {
            return BotDoing.Done($"met {Carrier?.Name ?? "the carrier"} and took {want.Filled - _filled} {want.Label}");
        }

        var standing = BotRendezvous.Standing(body);

        if (!ReferenceEquals(standing, _approach))
        {
            return BotDoing.Failed($"{Carrier?.Name ?? "the carrier"} gave the delivery up before the meeting");
        }

        var carrier = Carrier;

        if (carrier is not { Deleted: false, Alive: true } || carrier.Map != body.Map)
        {
            BotRendezvous.Waited(body, apart: true);

            return BotDoing.Failed($"{carrier?.Name ?? "the carrier"} is gone");
        }

        if (body.InRange(carrier.Location, BotHandover.MeetTiles))
        {
            if (_besideTick == 0)
            {
                _besideTick = now;
            }
            else if (now - _besideTick >= BotRendezvous.WaitMs)
            {
                BotRendezvous.Waited(body, apart: false);

                return BotDoing.Failed($"stood {BotRendezvous.WaitMs / 1000}s beside {carrier.Name} and nothing changed hands");
            }

            return BotDoing.Work($"waiting for {carrier.Name} to hand over {_approach.Units} {want?.Label}");
        }

        _besideTick = 0;

        if (now - _begun >= BotRendezvous.MeetMs)
        {
            BotRendezvous.Waited(body, apart: true);

            return BotDoing.Failed($"{carrier.Name} did not come to the meeting in {BotRendezvous.MeetMs / 60000} minutes");
        }

        var place = _approach.Place;

        if (place != Point3D.Zero && !body.InRange(place, 2))
        {
            return BotDoing.Walk(_map, place, BotArrival.Within(2), $"to the meeting place for {carrier.Name}");
        }

        if (place != Point3D.Zero)
        {
            return BotDoing.Work($"at the meeting place, waiting for {carrier.Name}");
        }

        return BotDoing.WalkAfter(_map, carrier, BotArrival.Within(BotHandover.MeetTiles), $"towards {carrier.Name} to meet it");
    }
}
