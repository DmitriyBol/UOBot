using System;
using System.Collections.Generic;
using Server.Logging;
using Server.Multis;

namespace Server.BotAI.V2;

public static class BotWarming
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotWarming));

    public static bool Running { get; set; } = true;

    public static int InviteReach { get; set; } = 400;

    public static int MostGuests { get; set; } = 4;

    public static int WaitMs { get; set; } = 600000;

    public static int AtDoor { get; set; } = 10;

    public static int HostHome { get; set; } = 60;

    public static int EveryMs { get; set; } = 14400000;

    public static int RestMs { get; set; } = 1800000;

    public static double Chance { get; set; } = 0.1;

    public static long WarmingsCalled { get; private set; }

    public static long GuestsInvited { get; private set; }

    public static long GuestsArrived { get; private set; }

    public static long Completed { get; private set; }

    public static long NoGuests { get; private set; }

    public static long CancelledOwnerAway { get; private set; }

    public static long StoriesTold { get; private set; }

    public static long Refused { get; private set; }

    private static readonly List<Evening> _evenings = [];

    private static readonly Dictionary<string, long> _last = new(StringComparer.Ordinal);

    private static int _cursor;

    private static int _beat;

    public static void Tick(long now)
    {
        if (!Running)
        {
            return;
        }

        if (++_beat >= 10)
        {
            _beat = 0;

            Consider(now);
        }

        for (var i = _evenings.Count - 1; i >= 0; i--)
        {
            if (_evenings[i].Keep(now))
            {
                continue;
            }

            _evenings.RemoveAt(i);
        }
    }

    private static void Consider(long now)
    {
        var bots = BotPopulation.Bots;

        if (bots.Count == 0)
        {
            return;
        }

        if (_cursor >= bots.Count)
        {
            _cursor = 0;
        }

        if (bots[_cursor++] is not { Deleted: false, Alive: true } bot)
        {
            return;
        }

        var house = BotAbode.Of(bot);

        if (house == null || bot.Map == null || bot.Map != house.Map || !bot.InRange(house.Location, HostHome))
        {
            return;
        }

        if (_last.TryGetValue(bot.Name, out var when) && now - when < Math.Max(60000, RestMs))
        {
            return;
        }

        var life = BotLives.Of(bot);

        if (life == null || bot.Resolve is not { } resolve
            || resolve.Deed is { Committed: true } || resolve.Standing is not (BotStanding.Free or BotStanding.Busy))
        {
            return;
        }

        _last[bot.Name] = now;

        if (_last.Count > 1024)
        {
            _last.Clear();
        }

        if (Utility.RandomDouble() >= Chance)
        {
            return;
        }

        var guests = new List<BotMobile>();

        for (var i = 0; i < life.Ties.Count && guests.Count < MostGuests; i++)
        {
            var tie = life.Ties[i];

            if (tie == null || tie.Saved + tie.Beside <= 0 || string.IsNullOrEmpty(tie.Name))
            {
                continue;
            }

            for (var k = 0; k < bots.Count; k++)
            {
                if (bots[k] is not { Deleted: false, Alive: true } guest
                    || !string.Equals(guest.Name, tie.Name, StringComparison.Ordinal))
                {
                    continue;
                }

                if (guest.Map == house.Map && guest.InRange(house.Location, InviteReach))
                {
                    guests.Add(guest);
                }

                break;
            }
        }

        WarmingsCalled++;

        if (guests.Count == 0)
        {
            NoGuests++;

            return;
        }

        var evening = new Evening(bot, house);

        var names = "";

        for (var i = 0; i < guests.Count; i++)
        {
            if (!BotWill.Press(guests[i], new BotVisit(evening), $"called to {bot.Name}'s house-warming"))
            {
                Refused++;

                continue;
            }

            evening.Invited++;
            GuestsInvited++;

            names += (names.Length == 0 ? "" : ", ") + guests[i].Name;
        }

        if (evening.Invited == 0)
        {
            NoGuests++;

            return;
        }

        _evenings.Add(evening);

        BotVoice.Aloud(bot, $"A roof of my own at last! {names} — come and warm it with me!");

        logger.Information("{Host} calls a house-warming at ({X}, {Y}) for {Guests}", bot.Name, house.X, house.Y, names);
    }

    public static string Describe() =>
        !Running
            ? "no house is warmed"
            : $"{WarmingsCalled} warmings called ({NoGuests} nobody to call or nobody came), {GuestsInvited} guests asked ({Refused} could not be stopped), "
              + $"{GuestsArrived} arrived, {Completed} evenings seen through ({StoriesTold} stories told at them), {CancelledOwnerAway} called off, the host walking off";

    public static void Forget()
    {
        WarmingsCalled = 0;
        GuestsInvited = 0;
        GuestsArrived = 0;
        Completed = 0;
        NoGuests = 0;
        CancelledOwnerAway = 0;
        StoriesTold = 0;
        Refused = 0;
        _evenings.Clear();
        _last.Clear();
        _cursor = 0;
        _beat = 0;
    }

    public sealed class Evening
    {
        public Evening(BotMobile host, BaseHouse house)
        {
            Host = host;
            House = house;
            BeganTick = Core.TickCount;
            NextLineTick = Core.TickCount + 10000;
        }

        public BotMobile Host { get; }

        public BaseHouse House { get; }

        public int Invited { get; set; }

        public string Ended { get; private set; }

        private long BeganTick { get; }

        private long NextLineTick { get; set; }

        private int Line { get; set; }

        private bool _arrivedOnce;

        private bool _over;

        public bool Standing() => !_over && Host is { Deleted: false, Alive: true } && Host.Map == House.Map && Host.InRange(House.Location, HostHome * 2);

        public bool Keep(long now)
        {
            if (_over)
            {
                return false;
            }

            if (!Standing())
            {
                CancelledOwnerAway++;
                Ended = "the host walked off";
                _over = true;

                return false;
            }

            var arrived = Guests();

            if (arrived.Count > 0 && !_arrivedOnce)
            {
                _arrivedOnce = true;
                GuestsArrived += arrived.Count;
            }

            if (!_arrivedOnce)
            {
                if (now - BeganTick > Math.Max(60000, WaitMs))
                {
                    NoGuests++;
                    Ended = "nobody came";
                    _over = true;

                    return false;
                }

                return true;
            }

            if (now - NextLineTick < 0)
            {
                return true;
            }

            NextLineTick = now + 25000;

            var fill = new Dictionary<string, string>();

            switch (Line++)
            {
                case 0:
                {
                    BotCampTalk.Say(Host, "warming:welcome", null, arrived.Count > 0 ? arrived[0] : null, fill);

                    return true;
                }
                case 1:
                {
                    if (arrived.Count > 0)
                    {
                        BotCampTalk.Say(arrived[0], "warming:guest", null, Host, fill);
                    }

                    return true;
                }
                case 2:
                {
                    if (arrived.Count > 0)
                    {
                        BotCampTalk.Say(arrived[arrived.Count - 1], "warming:guest2", null, Host, fill);
                    }

                    return true;
                }
                default:
                {

                    var topic = BotCampTalk.Topic(Host, Host.Map, Host.Location, fill, "warming:thanks", out var told, arrived);

                    if (BotCampTalk.Say(Host, topic, "warming:thanks", arrived.Count > 0 ? arrived[0] : null, fill) && told != null)
                    {
                        StoriesTold++;

                        var place = BotBiography.Place(Host.Map, House.Location);

                        for (var i = 0; i < arrived.Count; i++)
                        {
                            BotBiography.Heard(arrived[i], Host, told, place);
                        }
                    }

                    Completed++;
                    Ended = "the warming was seen through";
                    _over = true;

                    return false;
                }
            }
        }

        private List<BotMobile> Guests()
        {
            var list = new List<BotMobile>();
            var bots = BotPopulation.Bots;

            for (var i = 0; i < bots.Count; i++)
            {
                if (bots[i] is { Deleted: false, Alive: true } near && near != Host && near.Map == House.Map
                    && near.InRange(House.Location, AtDoor) && near.Resolve?.Deed is BotVisit { Evening: { } visit }
                    && ReferenceEquals(visit, this))
                {
                    list.Add(near);
                }
            }

            return list;
        }
    }
}

public sealed class BotVisit : BotDeed
{
    public const string Trade = "visit";

    public static double Prior { get; set; } = 6.0;

    private readonly BotWarming.Evening _evening;

    private readonly BotMobile _host;

    private readonly Map _map;

    private readonly Point3D _door;

    private bool _stood;

    public BotVisit(BotWarming.Evening evening)
    {
        _evening = evening;
        _host = evening?.Host;
        _map = _host?.Map;
        _door = evening?.House?.Location ?? Point3D.Zero;
    }

    public BotWarming.Evening Evening => _evening;

    public override string Kind => Trade;

    public override Map Map => _map;

    public override Point3D Where => _door;

    public override double Expects => Prior;

    public override double Minutes => 0.5;

    public override SkillName? Trains => null;

    public override int Outlay => 0;

    public override double Coin => 1.0;

    public override int Made => 0;

    public override bool Unpaid => true;

    public override bool Committed => true;

    public override bool Still => true;

    public override string Stage => $"called to {_host?.Name}'s house-warming";

    public override BotDoing Advance(IBotWilful bot)
    {
        var body = bot?.Self;

        if (body == null || _evening == null || body.Deleted || !body.Alive)
        {
            return BotDoing.Failed("the guest never got there");
        }

        if (!_evening.Standing())
        {
            return BotDoing.Done(_evening.Ended ?? "the warming was called off");
        }

        if (body.InRange(_door, BotWarming.AtDoor))
        {
            _stood = true;

            if (_host != null && body.InRange(_host.Location, 14))
            {
                var facing = body.GetDirectionTo(_host.Location);

                if ((body.Direction & Direction.Mask) != facing)
                {
                    body.Direction = facing;
                }
            }

            return BotDoing.Work("a guest in the yard");
        }

        return BotDoing.Work(_stood ? "back to the yard" : "on the way to the warming");
    }

    public override void Drop(IBotWilful bot)
    {
    }
}
