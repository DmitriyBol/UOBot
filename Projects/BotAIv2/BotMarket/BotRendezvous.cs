using System;
using System.Collections.Generic;
using Server.Logging;

namespace Server.BotAI.V2;

/// <summary>
/// A delivery is a meeting: once the carrier is near, the buyer comes to it — or both go to safe ground — instead of the
/// carrier walking after a buyer that is at work and moving.
///
/// <para>
/// <b>Patrick's order of 02.10.2026: "Today I saw a couple of times the customer of the goods running away from the
/// supplier. The result of the trade must be that they meet: either they walk towards each other, or they meet on safe
/// ground."</b> Measured on build 388's first nine hours: 654 deliveries ended, 555 handed over, median 26 seconds from
/// taking to handing — and 195 took over a minute, 108 over two, 39 over five; 28 were dropped and 13 put down on the
/// way, most of them walks after a buyer that took up a prowl, a hunt or a counter and kept going (Liriel after Dorin for
/// 304 s, dropped; Berengar after Norbert 184 s, dropped). The "could not catch up in 8 minutes" ending never fired once
/// in nine hours, because the auction took the carrier off the chase first. The carrier followed the buyer by its body
/// (<c>BotDoing.Walk(map, buyer, …)</c>) and the buyer knew nothing of it.
/// </para>
///
/// <para>
/// <b>Now the buyer is told.</b> A carrier within <see cref="CallTiles"/> of its buyer calls the meeting
/// (<see cref="Call"/>), and the buyer's own proposer (<see cref="BotMeeter"/>, on the free rung, pressing) offers it a
/// <see cref="BotMeet"/>: walk towards the carrier, stand when it is within <see cref="BotHandover.MeetTiles"/>, and let
/// the carrier's own beat hand the goods over. Where either stands on ground that is not safe — a hostile or deadly patrol
/// zone, or a square whose record asks more than the bot brings — the place is the nearest guarded ground to the point
/// between them within <see cref="SafeReach"/> (<see cref="BotHaven.Nearest"/>: a town's edge or a bank), and both walk
/// there. One meeting a buyer at a time; a second carrier for the same buyer follows as before until the first is done.
/// </para>
///
/// <para>
/// Counted: meetings arranged, met, missed — and why missed: the buyer waited and the carrier never came, the carrier
/// gave the delivery up with a meeting standing, the two stood together and nothing changed hands.
/// </para>
/// </summary>
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
            : $"meetings: {Arranged} called by a carrier within {CallTiles} tiles ({OnSafeGround} of them moved to guarded ground for the ground one stood on), {TakenUp} taken up by the buyer, {Met} met with the goods handed over, {Missed} missed ({BuyerWaited} the buyer waited {MeetMs / 60000} minutes and the carrier never came, {StoodApart} stood together {WaitMs / 1000}s and nothing changed hands, {CarrierGaveUp} the carrier gave the delivery up first, {FellOutOfCall} the carrier fell out of call for {StaleMs / 1000}s), {Doubled} calls for a buyer already meeting another carrier";

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
    }
}

/// <summary>Offers a buyer whose carrier is near the walk to meet it. See <see cref="BotRendezvous"/>.</summary>
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

/// <summary>
/// The buyer's half of a hand-to-hand delivery: going to meet the carrier, and standing while the goods are handed over.
///
/// <para>
/// Pressing, because the carrier is standing a screen away now and the buyer's next review may be half a minute off; a
/// summons, because a buyer at a seam is still a buyer, and the seam is put down and taken up again. The exchange itself
/// is the carrier's (<see cref="BotDeliver"/>, <see cref="BotHaul"/>): this only puts the buyer where it can happen and
/// keeps it there, and ends when the want has been filled, or the carrier is gone, or the clocks run out.
/// </para>
/// </summary>
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

        return BotDoing.Walk(_map, carrier, BotArrival.Within(BotHandover.MeetTiles), $"towards {carrier.Name} to meet it");
    }
}
