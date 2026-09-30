using System;
using System.Collections.Generic;
using Server.Logging;
using Server.Mobiles;
using Server.Multis;

namespace Server.BotAI.V2;

/// <summary>
/// A voyage: a ship bought if the bot has none, a walk to the dock, the ship put in the water and boarded, the sea, and the
/// walk from the far dock into the town — or home.
///
/// <para>
/// <b>Patrick's point 7 of 29.09.2026: "teach the bots to travel by sea, on boats."</b> The journey (<see cref="BotTravel"/>)
/// walks to a town a road reaches; this sails to one no road reaches, and it is offered the same way, by the traveller
/// (<see cref="BotSea.Travel"/>), at the journey's price. The same deed takes a bot home from an island when there is
/// nothing there worth doing (<see cref="BotSeafarer"/>), at the walk home's price and unpaid like it.
/// </para>
///
/// <para>
/// <b>The stages are the deed's; the sea is the helm's.</b> Buying, walking to the dock and boarding are asked on the bot's
/// own beat. Once aboard, <see cref="BotHelm"/> sails the ship on its own clock and puts the bot ashore, and the deed only
/// watches: work while the ship is at sea, a failure with the helm's reason if it was lost, and on landing a walk from the
/// dock into the town. The deed is committed at sea — nothing but what will not wait takes the bot off it — and if it is
/// taken off anyway the ship still sails and still lands it.
/// </para>
///
/// <para>
/// <b>What it is worth and where it is.</b> Its minutes are the voyage's, honestly: a lane's tiles at the helm's speed, and
/// the appraisal's nearness (work against walk) then gives a voyage from a dock round the corner a fair hearing. Where it
/// is moves with it: the dock it sets out from until it is aboard (the appraisal vetoes work on ground no gate joins to the
/// bot, and the far town is exactly such ground), the town it is bound for from then on.
/// </para>
/// </summary>
public sealed class BotVoyage : BotDeed
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotVoyage));

    public const string Trade = "voyage";

    public const string HomeTrade = "sailhome";

    public static double Prior { get; set; } = 8.0;

    public static double HomeWorth { get; set; } = 6.0;

    public static int BerthWaitMs { get; set; } = 120000;

    public static int AshoreMs { get; set; } = 600000;

    public static int Arrival { get; set; } = 10;

    public static long Voyages { get; private set; }

    public static long Homeward { get; private set; }

    public static long Bought { get; private set; }

    public static long BoughtGold { get; private set; }

    public static long Granted { get; private set; }

    public static long Unafforded { get; private set; }

    public static long NoShipwright { get; private set; }

    public static long Berthless { get; private set; }

    public static long Shifted { get; private set; }

    public static long Waited { get; private set; }

    public static long Landed { get; private set; }

    public static long Reached { get; private set; }

    public static long Lost { get; private set; }

    private enum Step
    {
        Buy,
        ToDock,
        Board,
        AtSea,
        Ashore
    }

    private readonly Map _map;

    private readonly BotDock _from;

    private readonly BotDock _to;

    private readonly BotSeaLane _lane;

    private readonly BotTowns.Town _town;

    private readonly Point3D _goal;

    private readonly string _whence;

    private readonly bool _home;

    private readonly double _minutes;

    private readonly IReadOnlyList<Mobile> _company;

    private Step _step;

    private BotSeaVoyage _voyage;

    private BaseVendor _vendor;

    private long _waiting;

    private BotBerth _berth;

    private int _tried;

    private string _blocked;

    private bool _counted;

    private bool _noted;

    private long _landed;

    private long _began;

    public BotVoyage(Map map, BotDock from, BotDock to, BotSeaLane lane, BotTowns.Town town, Point3D goal, string whence, bool home, IReadOnlyList<Mobile> company = null)
    {
        _map = map;
        _from = from;
        _to = to;
        _lane = lane;
        _town = town;
        _goal = goal;
        _whence = whence;
        _home = home;
        _company = company;
        _berth = from?.First;

        var sail = lane.Tiles * (double)BotHelm.StrokeMs / Math.Max(1, BotHelm.Speed) / 60000.0;
        var ashore = BotHelm.Away(to.Shore, goal) * (double)BotWalk.StepDelayMs(BotMobile.Runs) / 60000.0;

        _minutes = Math.Max(1.0, sail + ashore + 1.0);
    }

    public BotDock From => _from;

    public BotDock To => _to;

    public override string Kind => _home ? HomeTrade : Trade;

    public override Map Map => _map;

    public override Point3D Where => _step < Step.AtSea ? _berth?.Shore ?? _from.Shore : _goal;

    public override double Expects => _home ? HomeWorth : Prior;

    public override double Minutes => _minutes;

    public override SkillName? Trains => null;

    public override int Outlay => 0;

    public override double Coin => 1.0;

    public override int Made => 0;

    public override bool Steadfast => true;

    public override bool Resumes => true;

    public override bool Unpaid => _home;

    private bool Afloat => _voyage is { State: BotSeaVoyage.Tide.Sailing or BotSeaVoyage.Tide.Rerouting or BotSeaVoyage.Tide.Landing };

    public override bool Committed => Afloat || _step is Step.ToDock or Step.Board;

    public override double HoldsFor => _minutes;

    public override bool Still => Afloat;

    public override string Stage =>
        _step switch
        {
            Step.Buy    => _vendor != null ? $"buying a ship from {_vendor.Name} to sail to {_to.Name}" : $"looking for a ship to sail to {_to.Name}",
            Step.ToDock => $"walking to the dock at {_from.Name} to sail to {_to.Name}",
            Step.Board  => _blocked != null ? $"waiting at {_from.Name} for the berth: {_blocked}" : $"at the dock at {_from.Name}, putting the ship in",
            Step.AtSea  => _voyage != null ? $"at sea from {_from.Name} to {_to.Name}, {_voyage.Left(_voyage.Boat?.Location ?? _to.Berth)} tiles to go" : $"at sea for {_to.Name}",
            _           => $"ashore at {_to.Name}, walking {(_home ? "home" : $"into {_town?.Name}")}"
        };

    public override void Taken(IBotWilful bot)
    {
        _began = Core.TickCount;

        if (_home)
        {
            Homeward++;
        }
        else
        {
            Voyages++;
            BotTraveller.Went(bot?.Self);
        }
    }

    public override BotDoing Advance(IBotWilful bot)
    {
        var body = bot?.Self as BotMobile;

        if (body == null || _map == null || _from == null || _to == null || !body.Alive)
        {
            return BotDoing.Failed("no body, or nowhere to sail");
        }

        if (_step == Step.Buy)
        {
            if (BotHelm.ShipOf(body) != null)
            {
                _step = Step.ToDock;
            }
            else
            {
                return Buy(bot, body);
            }
        }

        if (_step == Step.ToDock)
        {
            if (BotHelm.ShipOf(body) == null)
            {
                return BotDoing.Failed("the ship is gone from its pack");
            }

            if (_berth == null)
            {
                return BotDoing.Failed($"{_from.Name} has no berth");
            }

            if (!body.InRange(_berth.Shore, 2) || Math.Abs(body.Z - _berth.Shore.Z) > 16)
            {
                return BotDoing.Walk(_map, _berth.Shore, BotArrival.Within(1), $"to the dock at {_from.Name}");
            }

            _step = Step.Board;
            _waiting = Core.TickCount;
            _counted = false;
            _noted = false;
        }

        if (_step == Step.Board)
        {
            var voyage = BotHelm.Embark(body, _from, _berth, _to, _lane, _company, out var why, out var block);

            if (voyage == null)
            {
                return Refused(body, why, block);
            }

            _voyage = voyage;
            _step = Step.AtSea;
        }

        if (_step == Step.AtSea)
        {
            switch (_voyage.State)
            {
                case BotSeaVoyage.Tide.Landed:
                    {
                        Landed++;
                        _landed = Core.TickCount;
                        _step = Step.Ashore;

                        break;
                    }
                case BotSeaVoyage.Tide.Lost:
                    {
                        Lost++;

                        return BotDoing.Failed($"the voyage from {_from.Name} to {_to.Name} was lost: {_voyage.Why}");
                    }
                default:
                    {
                        return BotDoing.Work($"at sea for {_to.Name}");
                    }
            }
        }

        var near = _home ? BotHomeward.Arrived : Arrival;

        if (body.InRange(_goal, near) || Core.TickCount - _landed >= AshoreMs)
        {
            Reached++;

            var minutes = (Core.TickCount - _began) / 60000.0;

            logger.Information(
                "{Name} the {Class} is {Where} after {Minutes:F1} minutes, by sea from {From} by way of {Dock}",
                body.Name,
                body.Class?.Name,
                _home ? "home" : $"in {_town?.Name}",
                minutes,
                _whence,
                _to.Name
            );

            if (_town != null)
            {
                _town.Arrivals++;
            }

            return BotDoing.Done($"{(_home ? "home" : $"in {_town?.Name}")} after {minutes:F0} minutes, by sea from {_from.Name}");
        }

        return BotDoing.Walk(_map, _goal, BotArrival.Within(near), _home ? "home from the sea" : $"into {_town?.Name} from the sea");
    }

    private BotDoing Refused(BotMobile body, string why, BotBerthBlock block)
    {
        _blocked = why;

        if (!_noted && block != BotBerthBlock.None)
        {
            _noted = true;
            BotHelm.Count(block);
        }

        if (block == BotBerthBlock.None)
        {
            Berthless++;

            return BotDoing.Failed($"could not put to sea from {_from.Name}: {why}");
        }

        var berths = _from.Berths;
        var at = berths.IndexOf(_berth);

        if (at is >= 0 and < 31)
        {
            _tried |= 1 << at;
        }

        for (var i = 0; i < berths.Count && i < 31; i++)
        {
            if ((_tried & (1 << i)) != 0 || !BotHelm.Free(_map, berths[i]))
            {
                continue;
            }

            Shifted++;

            logger.Information(
                "{Name} goes to {Dock}'s berth {Berth} at ({X}, {Y}): {Why}",
                body.Name,
                _from.Name,
                i + 1,
                berths[i].Berth.X,
                berths[i].Berth.Y,
                why
            );

            _berth = berths[i];
            _step = Step.ToDock;

            return BotDoing.Walk(_map, _berth.Shore, BotArrival.Within(1), $"to the dock at {_from.Name}");
        }

        if (block == BotBerthBlock.Ship && Core.TickCount - _waiting < BerthWaitMs)
        {
            if (!_counted)
            {
                _counted = true;
                Waited++;
            }

            _tried = 0;

            return BotDoing.Work($"waiting at {_from.Name}: {why}");
        }

        Berthless++;

        return BotDoing.Failed($"could not put to sea from {_from.Name}: {why}");
    }

    private BotDoing Buy(IBotWilful bot, BotMobile body)
    {
        var wanted = typeof(SmallBoatDeed);

        if (BotSea.Grant)
        {
            return Grant(body);
        }

        if (_vendor == null || _vendor.Deleted)
        {
            _vendor = BotShops.Nearest(bot, wanted);
        }

        if (_vendor == null)
        {
            NoShipwright++;

            return BotDoing.Failed("nobody within reach sells a ship");
        }

        if (!body.InRange(_vendor.Location, BotShops.CounterReach))
        {
            return BotDoing.Walk(_map, _vendor, BotArrival.Within(BotShops.CounterReach), $"to {_vendor.Name} for a ship");
        }

        var price = BotShops.Price(_vendor, wanted);

        if (price <= 0)
        {
            NoShipwright++;

            return BotDoing.Failed($"{_vendor.Name} has no ship to sell");
        }

        if (BotYield.Wealth(body) < price && BotSea.GuildPays)
        {
            BotProvision.Fund(body, price);
        }

        if (BotYield.Wealth(body) < price)
        {
            Unafforded++;

            return BotDoing.Failed($"{BotYield.Wealth(body)}gp will not buy a ship at {price}gp");
        }

        var bought = BotShops.Buy(bot, _vendor, wanted, 1, out var refused);

        if (bought <= 0)
        {
            return BotDoing.Failed($"no ship bought: {refused}");
        }

        BotYield.Aside(body, price);
        Bought++;
        BoughtGold += price;
        BotHelm.Keep(body, BotHelm.ShipOf(body));

        logger.Information("{Name} the {Class} bought a ship from {Vendor} for {Price}gp to sail to {To}", body.Name, body.Class?.Name, _vendor.Name, price, _to.Name);

        _step = Step.ToDock;

        return BotDoing.Walk(_map, _berth?.Shore ?? _from.Shore, BotArrival.Within(1), $"to the dock at {_from.Name}");
    }

    private BotDoing Grant(BotMobile body)
    {
        var deed = new SmallBoatDeed();

        if (body.Backpack == null || !body.Backpack.TryDropItem(body, deed, false))
        {
            deed.Delete();

            return BotDoing.Failed("no room in the pack for a ship");
        }

        Granted++;
        BotHelm.Keep(body, deed);

        logger.Information("{Name} the {Class} was handed a ship for nothing to sail to {To} (BotSea.Grant)", body.Name, body.Class?.Name, _to.Name);

        _step = Step.ToDock;

        return BotDoing.Walk(_map, _berth?.Shore ?? _from.Shore, BotArrival.Within(1), $"to the dock at {_from.Name}");
    }

    public static string Describe() =>
        Voyages + Homeward == 0
            ? "nobody has set out by sea"
            : $"{Voyages} voyages to another town and {Homeward} home begun, {Landed} landed, {Reached} reached the town, {Lost} lost at sea, {Berthless} could not put to sea, {Shifted} went to another berth, {Waited} waited for a ship to leave one; "
              + $"{Bought} ships bought for {BoughtGold}gp, {Granted} handed over, {Unafforded} could not afford one, {NoShipwright} found nobody selling one";

    public static void Forget()
    {
        Voyages = 0;
        Homeward = 0;
        Bought = 0;
        BoughtGold = 0;
        Granted = 0;
        Unafforded = 0;
        NoShipwright = 0;
        Berthless = 0;
        Shifted = 0;
        Waited = 0;
        Landed = 0;
        Reached = 0;
        Lost = 0;
    }
}
