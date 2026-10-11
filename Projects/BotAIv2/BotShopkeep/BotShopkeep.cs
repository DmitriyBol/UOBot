using System;
using System.Collections.Generic;
using System.Text.Json;
using Server.Items;
using Server.Logging;
using Server.Mobiles;
using Server.Multis;
using Server.Multis.Deeds;
using Server.Text;

namespace Server.BotAI.V2;

/// <summary>
/// Bots' own shops: a bot with goods to spare stands beside the bank of the town it lives in, calls out what it sells, and
/// the bots that are short of those goods walk over and buy them out of its pack. Patrick's order of 29.09.2026, evening.
///
/// <para>
/// <b>Why a shop and not a stall.</b> The auction was placeless: a stall held its goods out of the world and anybody anywhere
/// bought from it without taking a step, so nothing about it could be seen and a town was not a place trade happened. A shop
/// is the other way round. The goods stay in the keeper's own pack and bank box, where the engine keeps them and the save keeps
/// them; a customer walks to the keeper and the goods move pack to pack at the till, paid for in coin that moves pack to pack
/// (<see cref="Serve"/>). <b>Since 01.10.2026 it is the market</b> (Patrick's order of 30.09: the auction removed, all trade in
/// Britain, guild traders at home, every purchase hand to hand): the stalls are gone, and a shop by Britain's bank — or a guild
/// member's by its guild's own town's — is where one bot sells to another that comes to it.
/// </para>
///
/// <para>
/// <b>A shop wins where it is better and nowhere else.</b> A bot short of a supply (<c>BotShopper</c>) weighs a shop against a
/// shopkeeper on price <em>and</em> the walk (<see cref="Beats"/>): the keeper's ask plus the minutes of walking to it, priced
/// at what the shard reckons a shopping minute is worth (<see cref="WalkGold"/>, which follows <c>BotRestock.Prior</c>),
/// against the shopkeeper's price plus the walk to the shopkeeper. A want is weighed the same way against its own offer
/// (<see cref="BotPatron"/>). Ties go to the shop: coin paid to a bot stays in the population.
/// </para>
///
/// <para>
/// <b>Prices start where the island already is and move by the market's own steps.</b> A keeper opens a kind under what the
/// nearest shopkeeper asks for it, or at what the market reckons it worth when that is less (<see cref="Opening"/>). A kind
/// that sells again inside <c>BotMarket.BriskMs</c> goes up by <c>BotMarket.RaiseStep</c>; a kind nobody bought during a whole
/// shift comes down by <c>BotMarket.CutStep</c>, both inside ×0.25…×4 of the opening ask.
/// </para>
/// </summary>
public static class BotShopkeep
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotShopkeep));

    public const string Kind = "botshop";

    public static bool Running { get; set; } = true;

    public static double ShiftMinutes { get; set; } = 8.0;

    public static int RestMs { get; set; } = 600000;

    public static int OpenWorth { get; set; } = 40;

    public static double ShelfShare { get; set; } = 0.8;

    public static double SellShare { get; set; } = 0.5;

    public static int LeastShifts { get; set; } = 5;

    public static double LeastClaim { get; set; } = 2.0;

    public static double MostClaim { get; set; } = 120.0;

    public static int BankTiles { get; set; } = 240;

    public static int BankMemoryMs { get; set; } = 600000;

    public static int PitchNear { get; set; } = 2;

    public static int PitchFar { get; set; } = 6;

    public static int PitchClimb { get; set; } = 6;

    public static int Apart { get; set; } = 2;

    public static int PerBank { get; set; } = 8;

    public static int MostOpen { get; set; } = 24;

    public static int StandReach { get; set; } = 1;

    public static int ServeReach { get; set; } = 2;

    public static int SeekTiles { get; set; } = 240;

    public static double WalkGold
    {
        get => _walkGold ?? BotRestock.Prior;
        set => _walkGold = value;
    }

    private static double? _walkGold;

    public static int CryMs { get; set; } = 45000;

    public static int StockMs { get; set; } = 3000;

    public static int LookMs { get; set; } = 20000;

    public static int ShelfMs { get; set; } = 60000;

    public static int DemandMs { get; set; } = 5000;

    public static int MostPrices { get; set; } = 8192;

    public static long Opened { get; private set; }

    public static long Reopened { get; private set; }

    public static long Closed { get; private set; }

    public static long SoldOut { get; private set; }

    public static long TimeUp { get; private set; }

    public static long Empty { get; private set; }

    public static long Suspended { get; private set; }

    public static long Refused { get; private set; }

    public static long Sales { get; private set; }

    public static long Units { get; private set; }

    public static long Takings { get; private set; }

    public static long SupplySales { get; private set; }

    public static long SupplyUnits { get; private set; }

    public static long WantSales { get; private set; }

    public static long WantUnits { get; private set; }

    public static long Released { get; private set; }

    public static long Weighed { get; private set; }

    public static long ToFront { get; private set; }

    public static long LostToCounter { get; private set; }

    public static long Sealed { get; private set; }

    public static long Far { get; private set; }

    public static long Arrived { get; private set; }

    public static long SoldOutOnArrival { get; private set; }

    public static long Gone { get; private set; }

    public static long FellThrough { get; private set; }

    public static long PackFull { get; private set; }

    public static long Broke { get; private set; }

    public static long FilledMeanwhile { get; private set; }

    public static long Raised { get; private set; }

    public static long Cut { get; private set; }

    public static long Cries { get; private set; }

    public static long ShownTotal { get; private set; }

    public static long TakenTotal { get; private set; }

    public static long Shifts { get; private set; }

    private static readonly List<BotStorefront> _open = [];

    private static readonly Dictionary<Type, long> _soldKinds = [];

    public static IReadOnlyList<BotStorefront> Open => _open;

    public static int OpenCount => _open.Count;

    public static double Share =>
        Shifts >= LeastShifts && ShownTotal > 0 ? Math.Clamp(TakenTotal / (double)ShownTotal, 0.05, 1.0) : SellShare;

    public static double Claim(int demanded) =>
        Math.Clamp(demanded * Share / Math.Max(1.0, ShiftMinutes), LeastClaim, MostClaim);

    public static bool Live(BotStorefront front) =>
        front is { Shut: false, Keeper: { Deleted: false, Alive: true } keeper }
        && keeper.Map == front.Map
        && ReferenceEquals(keeper.Resolve?.Deed, front.Deed);

    private static void Sweep()
    {
        for (var i = _open.Count - 1; i >= 0; i--)
        {
            if (!Live(_open[i]))
            {
                _open[i].Shut = true;
                _open.RemoveAt(i);
                Suspended++;
            }
        }
    }

    internal static BotStorefront OpenShop(BotMobile keeper, BotDeed deed, Map map, Point3D stand, Point3D bank, bool again, out string why)
    {
        why = null;

        Sweep();
        Release(keeper);

        for (var i = _open.Count - 1; i >= 0; i--)
        {
            if (ReferenceEquals(_open[i].Keeper, keeper))
            {
                _open[i].Shut = true;
                _open.RemoveAt(i);
            }
        }

        if (_open.Count >= MostOpen)
        {
            Refused++;
            why = $"the island already has {MostOpen} shops open";

            return null;
        }

        if (Crowded(map, bank, keeper))
        {
            Refused++;
            why = $"the bank at ({bank.X}, {bank.Y}) already has {PerBank} shops beside it";

            return null;
        }

        SelfSupply(keeper);

        var town = BotTowns.Of(stand)?.Name;
        var front = new BotStorefront(keeper, deed, map, stand, bank, town);

        Count(keeper, _units, _asks, true, out _, out var demanded);
        front.Restock(_units, _asks, demanded);
        front.Shown = front.Demanded;

        _open.Add(front);

        if (again)
        {
            Reopened++;
        }
        else
        {
            Opened++;
        }

        logger.Information(
            "{Name} opened a shop by the bank at ({X}, {Y}) in {Town}: {Kinds} kinds, {Units} things worth {Worth}gp, {Demanded}gp of it what the island is short of or has money down for",
            keeper.Name,
            stand.X,
            stand.Y,
            front.Town,
            front.Kinds,
            front.Units,
            front.Worth,
            front.Demanded
        );

        BotEvents.Post("market", keeper, $"opened a shop in {front.Town}: {front.Units} things worth {front.Worth}gp");

        return front;
    }

    internal static void Close(BotStorefront front, bool learn, bool soldOut, double minutes, int shown, int sold, int takings)
    {
        if (front == null || front.Shut)
        {
            return;
        }

        front.Shut = true;
        _open.Remove(front);

        var keeper = front.Keeper;

        if (!learn)
        {
            Suspended++;

            return;
        }

        Closed++;

        if (soldOut)
        {
            SoldOut++;
        }
        else
        {
            TimeUp++;
        }

        if (sold <= 0)
        {
            Empty++;
        }

        if (soldOut || minutes >= ShiftMinutes * 0.5)
        {
            Shifts++;
            ShownTotal += Math.Max(0, shown);
            TakenTotal += Math.Max(0, takings);

            BotCapital.Shifted(front.Town, sold, takings);

            MarkDown(front);
        }

        if (keeper != null)
        {
            _restUntil[keeper.Serial] = Core.TickCount + RestMs;
        }

        logger.Information(
            "{Name} shut its shop in {Town} after {Minutes:F1} minutes: {Sold} things for {Takings}gp; {Left} things worth {Worth}gp left",
            keeper?.Name,
            front.Town,
            minutes,
            sold,
            takings,
            front.Units,
            front.Worth
        );
    }

    private static readonly Dictionary<Serial, long> _restUntil = [];

    public static bool Resting(Mobile keeper) =>
        keeper != null && _restUntil.TryGetValue(keeper.Serial, out var until) && Core.TickCount - until < 0;

    private static readonly Dictionary<Point3D, (Point3D Bank, long Tick)> _banks = [];

    private static readonly Dictionary<Point3D, (List<Point3D> Pitches, long Tick)> _pitches = [];

    public static Point3D BankNear(Map map, Point3D home)
    {
        if (map == null || map == Map.Internal || home == Point3D.Zero)
        {
            return Point3D.Zero;
        }

        var now = Core.TickCount;

        if (_banks.TryGetValue(home, out var known) && now - known.Tick < BankMemoryMs)
        {
            return known.Bank;
        }

        var best = Point3D.Zero;
        var nearest = int.MaxValue;

        foreach (var banker in map.GetMobilesInRange<Banker>(home, BankTiles))
        {
            if (banker.Deleted)
            {
                continue;
            }

            var away = Math.Max(Math.Abs(banker.X - home.X), Math.Abs(banker.Y - home.Y));

            if (away < nearest)
            {
                nearest = away;
                best = banker.Location;
            }
        }

        if (_banks.Count > 512)
        {
            _banks.Clear();
        }

        _banks[home] = (best, now);

        return best;
    }

    private static List<Point3D> Pitches(Map map, Point3D bank)
    {
        var now = Core.TickCount;

        if (_pitches.TryGetValue(bank, out var known) && now - known.Tick < BankMemoryMs)
        {
            return known.Pitches;
        }

        List<Point3D> found = [];

        for (var r = Math.Max(1, PitchNear); r <= Math.Max(PitchNear, PitchFar); r++)
        {
            for (var dx = -r; dx <= r; dx++)
            {
                for (var dy = -r; dy <= r; dy++)
                {
                    if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != r)
                    {
                        continue;
                    }

                    var x = bank.X + dx;
                    var y = bank.Y + dy;

                    if (!BotStep.Settle(map, x, y, out var z) || Math.Abs(z - bank.Z) > PitchClimb)
                    {
                        continue;
                    }

                    if (!map.CanFit(x, y, z, 16, false, false))
                    {
                        continue;
                    }

                    if (Doorway(map, x, y, z))
                    {
                        PitchesLeftDoor++;

                        continue;
                    }

                    if (!map.LineOfSight(new Point3D(bank.X, bank.Y, bank.Z + Eye), new Point3D(x, y, z + Eye)))
                    {
                        PitchesLeftWall++;

                        continue;
                    }

                    found.Add(new Point3D(x, y, z));
                }
            }
        }

        if (_pitches.Count > 256)
        {
            _pitches.Clear();
        }

        _pitches[bank] = (found, now);

        return found;
    }

    public static Point3D Pitch(Map map, Point3D bank, IBotWilful bot)
    {
        var body = bot?.Self;

        if (map == null || bank == Point3D.Zero || body == null)
        {
            return Point3D.Zero;
        }

        var pitches = Pitches(map, bank);
        var ledger = bot.Resolve?.Ledger;

        for (var i = 0; i < pitches.Count; i++)
        {
            var spot = pitches[i];

            if (Taken(map, spot, body))
            {
                continue;
            }

            if (ledger?.Cautious(BotKeepShop.Trade, map, spot) == true)
            {
                continue;
            }

            if (Unflagged(map, spot))
            {
                PitchesPassedFlag++;

                continue;
            }

            if (Struck(map, spot))
            {
                PitchesPassedStruck++;

                continue;
            }

            if (Occupied(map, spot, body))
            {
                PitchesPassedOccupied++;

                continue;
            }

            if (BotReach.Ask(map, body.Location, spot, BotArrival.Within(StandReach), false) == BotReachVerdict.Sealed)
            {
                continue;
            }

            return spot;
        }

        return Point3D.Zero;
    }

    public static int StrikeMs { get; set; } = 1800000;

    public static int StrikeSpread { get; set; } = 1;

    public static long PitchesStruck { get; private set; }

    public static long PitchesPassedStruck { get; private set; }

    public static long PitchesPassedOccupied { get; private set; }

    public static int StripTiles { get; set; } = 6;

    public static long StripsStruck { get; private set; }

    public static long PitchesPassedFlag { get; private set; }

    public static long OpenedWhereStood { get; private set; }

    internal static void OpenedStanding() => OpenedWhereStood++;

    private static readonly List<(Map Map, int X1, int Y1, int X2, int Y2, long Until)> _struck = [];

    public static void Strike(Map map, Point3D spot)
    {
        if (map == null || spot == Point3D.Zero || StrikeMs <= 0)
        {
            return;
        }

        var now = Core.TickCount;
        var spread = Math.Max(0, StrikeSpread);
        var x1 = spot.X - spread;
        var y1 = spot.Y - spread;
        var x2 = spot.X + spread;
        var y2 = spot.Y + spread;
        var strip = false;

        for (var i = _struck.Count - 1; i >= 0; i--)
        {
            var (m, a1, b1, a2, b2, until) = _struck[i];

            if (now - until >= 0)
            {
                _struck.RemoveAt(i);

                continue;
            }

            if (m != map)
            {
                continue;
            }

            var dx = Math.Max(0, Math.Max(a1 - spot.X, spot.X - a2));
            var dy = Math.Max(0, Math.Max(b1 - spot.Y, spot.Y - b2));

            if (Math.Max(dx, dy) <= Math.Max(0, StripTiles))
            {
                x1 = Math.Min(x1, a1);
                y1 = Math.Min(y1, b1);
                x2 = Math.Max(x2, a2);
                y2 = Math.Max(y2, b2);
                strip = true;
                _struck.RemoveAt(i);
            }
        }

        if (_struck.Count >= 256)
        {
            _struck.RemoveAt(0);
        }

        _struck.Add((map, x1, y1, x2, y2, now + StrikeMs));
        PitchesStruck++;

        if (strip)
        {
            StripsStruck++;
        }
    }

    private static bool Struck(Map map, Point3D spot)
    {
        if (_struck.Count == 0)
        {
            return false;
        }

        var now = Core.TickCount;

        for (var i = 0; i < _struck.Count; i++)
        {
            var (m, x1, y1, x2, y2, until) = _struck[i];

            if (m == map && now - until < 0 && spot.X >= x1 && spot.X <= x2 && spot.Y >= y1 && spot.Y <= y2)
            {
                return true;
            }
        }

        return false;
    }

    private static bool Unflagged(Map map, Point3D spot)
    {
        var kind = BotReachMap.Class(map, spot.X, spot.Y, spot.Z);

        return kind != BotReachClass.Unknown && kind != BotReachClass.Open;
    }

    public static bool StandsFit(BotMobile keeper, Point3D bank)
    {
        if (keeper?.Map == null || bank == Point3D.Zero)
        {
            return false;
        }

        var map = keeper.Map;
        var at = keeper.Location;
        var away = Math.Max(Math.Abs(at.X - bank.X), Math.Abs(at.Y - bank.Y));

        if (away < Math.Max(1, PitchNear) || away > Math.Max(PitchNear, PitchFar) || Math.Abs(at.Z - bank.Z) > PitchClimb
            || Unflagged(map, at) || Taken(map, at, keeper) || Occupied(map, at, keeper))
        {
            return false;
        }

        if (Doorway(map, at.X, at.Y, at.Z))
        {
            StoodInDoorway++;

            return false;
        }

        return map.LineOfSight(new Point3D(bank.X, bank.Y, bank.Z + Eye), new Point3D(at.X, at.Y, at.Z + Eye));
    }

    private const int Eye = 14;

    public static bool Doorway(Map map, int x, int y, int z)
    {
        if (map == null || !KeepOffDoors)
        {
            return false;
        }

        foreach (var door in map.GetItemsInRange<BaseDoor>(new Point3D(x, y, z), 2))
        {
            if (door.Deleted || Math.Abs(door.Z - z) > 16)
            {
                continue;
            }

            var shut = door.Open ? new Point3D(door.X - door.Offset.X, door.Y - door.Offset.Y, door.Z) : door.Location;

            if (Math.Max(Math.Abs(shut.X - x), Math.Abs(shut.Y - y)) <= 1
                || Math.Max(Math.Abs(door.X - x), Math.Abs(door.Y - y)) <= 1)
            {
                return true;
            }
        }

        return false;
    }

    public static bool KeepOffDoors { get; set; } = true;

    public static long PitchesLeftDoor { get; private set; }

    public static long PitchesLeftWall { get; private set; }

    public static long StoodInDoorway { get; private set; }

    public static int OpenStandingMs { get; set; } = 30000;

    public static long OpenedWhereStoodLate { get; private set; }

    internal static void OpenedStandingLate()
    {
        OpenedWhereStood++;
        OpenedWhereStoodLate++;
    }

    public static long WalksEndedOnOpening { get; private set; }

    internal static void EndedWalkOnOpening() => WalksEndedOnOpening++;

    public static long OpenWalksSpared { get; private set; }

    internal static void SparedOpenWalk() => OpenWalksSpared++;

    private static bool Occupied(Map map, Point3D spot, Mobile except)
    {
        foreach (var m in map.GetMobilesAt(spot))
        {
            if (!ReferenceEquals(m, except) && m is { Deleted: false, Alive: true } && Math.Abs(m.Z - spot.Z) < 16)
            {
                return true;
            }
        }

        return false;
    }

    public static bool Taken(Map map, Point3D spot, Mobile except)
    {
        for (var i = 0; i < _open.Count; i++)
        {
            var front = _open[i];

            if (front.Shut || front.Map != map || ReferenceEquals(front.Keeper, except))
            {
                continue;
            }

            if (Utility.InRange(front.Location, spot, Apart))
            {
                return true;
            }
        }

        Expire();

        for (var i = 0; i < _held.Count; i++)
        {
            var hold = _held[i];

            if (hold.Map == map && !ReferenceEquals(hold.Keeper, except) && Utility.InRange(hold.Stand, spot, Apart))
            {
                return true;
            }
        }

        return false;
    }

    public static bool Crowded(Map map, Point3D bank, Mobile except)
    {
        var here = 0;

        for (var i = 0; i < _open.Count; i++)
        {
            var front = _open[i];

            if (!front.Shut && front.Map == map && front.Bank == bank && !ReferenceEquals(front.Keeper, except))
            {
                here++;
            }
        }

        Expire();

        for (var i = 0; i < _held.Count; i++)
        {
            var hold = _held[i];

            if (hold.Map == map && hold.Bank == bank && !ReferenceEquals(hold.Keeper, except))
            {
                here++;
            }
        }

        return here >= PerBank;
    }

    /// <summary>A pitch a keeper has taken the shift for and is walking to.</summary>
    private sealed class Hold
    {
        public Mobile Keeper;

        public Map Map;

        public Point3D Stand;

        public Point3D Bank;

        public long Tick;
    }

    private static readonly List<Hold> _held = [];

    public static int HeldMs { get; set; } = 300000;

    public static int Showing(Type kind)
    {
        var held = 0;

        for (var i = 0; i < _open.Count; i++)
        {
            if (!_open[i].Shut)
            {
                held += _open[i].Held(kind);
            }
        }

        return held;
    }

    public static List<(BotStorefront Front, Type Kind, int Units, int Ask)> Lots()
    {
        List<(BotStorefront, Type, int, int)> lots = [];

        for (var i = 0; i < _open.Count; i++)
        {
            var front = _open[i];

            if (front.Shut)
            {
                continue;
            }

            foreach (var (kind, units, ask) in front.Ranked())
            {
                if (kind != null && units > 0 && ask > 0)
                {
                    lots.Add((front, kind, units, ask));
                }
            }
        }

        return lots;
    }

    public static int Spoken
    {
        get
        {
            Expire();

            return _open.Count + _held.Count;
        }
    }

    internal static void Reserve(Mobile keeper, Map map, Point3D stand, Point3D bank)
    {
        if (keeper == null)
        {
            return;
        }

        Release(keeper);

        _held.Add(new Hold { Keeper = keeper, Map = map, Stand = stand, Bank = bank, Tick = Core.TickCount });
    }

    internal static void Release(Mobile keeper)
    {
        for (var i = _held.Count - 1; i >= 0; i--)
        {
            if (ReferenceEquals(_held[i].Keeper, keeper))
            {
                _held.RemoveAt(i);
            }
        }
    }

    private static void Expire()
    {
        var now = Core.TickCount;

        for (var i = _held.Count - 1; i >= 0; i--)
        {
            var hold = _held[i];

            if (hold.Keeper is not { Deleted: false, Alive: true } || now - hold.Tick >= HeldMs)
            {
                _held.RemoveAt(i);
            }
        }
    }

    public static bool Merchandise(Item item, BotBond bond)
    {
        if (item is not { Deleted: false, Movable: true } || item is Gold or BankCheck or Container)
        {
            return false;
        }

        if (item is Key or KeyRing or RecallRune or Runebook or Spellbook or BaseBoatDeed or HouseDeed)
        {
            return false;
        }

        if (item.GetType().Namespace?.StartsWith("Server.BotAI", StringComparison.Ordinal) == true)
        {
            return false;
        }

        return !BotBinding.IsBound(item, bond);
    }

    private static readonly Dictionary<Type, int> _units = [];

    private static readonly Dictionary<Type, int> _asks = [];

    private static readonly Dictionary<Type, Item> _samples = [];

    private static readonly Dictionary<Type, int> _seen = [];

    private static readonly HashSet<Type> _ownWants = [];

    private static readonly List<Type> _kinds = [];

    private static void Count(BotMobile keeper, Dictionary<Type, int> units, Dictionary<Type, int> asks, bool remember, out int worth, out int demanded)
    {
        units.Clear();
        asks.Clear();
        _samples.Clear();
        worth = 0;
        demanded = 0;

        var bond = keeper.Bond;
        var box = keeper.FindBankNoCreate();

        if (box != null)
        {
            var held = box.Items;

            for (var i = 0; i < held.Count; i++)
            {
                var item = held[i];

                if (Merchandise(item, bond))
                {
                    Add(units, item, Math.Max(1, item.Amount));
                }
            }
        }

        var pack = keeper.Backpack;

        if (pack != null)
        {
            var keep = BotUnload.Keeps(keeper);

            _seen.Clear();

            var carried = pack.Items;

            for (var i = 0; i < carried.Count; i++)
            {
                var item = carried[i];

                if (!Merchandise(item, bond))
                {
                    continue;
                }

                var over = Over(keep, _seen, item);

                if (over > 0)
                {
                    Add(units, item, over);
                }
            }
        }

        if (units.Count == 0)
        {
            return;
        }

        OwnWants(keeper);

        _kinds.Clear();
        _kinds.AddRange(units.Keys);

        for (var i = 0; i < _kinds.Count; i++)
        {
            var kind = _kinds[i];

            if (_ownWants.Contains(kind))
            {
                units.Remove(kind);

                continue;
            }

            var ask = Quote(keeper, kind, _samples.TryGetValue(kind, out var sample) ? sample : null, remember);

            if (ask <= 0)
            {
                units.Remove(kind);

                continue;
            }

            asks[kind] = ask;

            var value = ask * units[kind];

            worth += value;

            if (Demanded(kind))
            {
                demanded += value;
            }
        }
    }

    private static void Add(Dictionary<Type, int> units, Item item, int amount)
    {
        var kind = item.GetType();

        units[kind] = (units.TryGetValue(kind, out var had) ? had : 0) + amount;
        _samples.TryAdd(kind, item);
    }

    private static void OwnWants(Mobile keeper)
    {
        _ownWants.Clear();

        var wants = BotMarket.Wants;

        for (var i = 0; i < wants.Count; i++)
        {
            var want = wants[i];

            if (want?.Kind != null && ReferenceEquals(want.Buyer?.Self, keeper))
            {
                _ownWants.Add(want.Kind);
            }
        }
    }

    private static int Over(Dictionary<Type, int> keep, Dictionary<Type, int> seen, Item item)
    {
        var kind = item.GetType();
        var many = Math.Max(1, item.Amount);
        var before = seen.TryGetValue(kind, out var had) ? had : 0;

        seen[kind] = before + many;

        if (!keep.TryGetValue(kind, out var allowed))
        {
            return many;
        }

        if (allowed >= int.MaxValue)
        {
            return 0;
        }

        return Math.Clamp(before + many - allowed, 0, many);
    }

    public static long SelfSupplied { get; private set; }

    private static void SelfSupply(BotMobile keeper)
    {
        var box = keeper?.FindBankNoCreate();
        var pack = keeper?.Backpack;

        if (box == null || pack == null || box.Items.Count == 0)
        {
            return;
        }

        var keep = BotUnload.Keeps(keeper);
        var bond = keeper.Bond;

        foreach (var (kind, allowed) in keep)
        {
            if (kind == null || allowed <= 0 || allowed >= int.MaxValue)
            {
                continue;
            }

            var short_ = allowed - pack.GetAmount(kind);

            for (var i = box.Items.Count - 1; i >= 0 && short_ > 0; i--)
            {
                var item = box.Items[i];

                if (item?.GetType() != kind || !Merchandise(item, bond))
                {
                    continue;
                }

                var part = Math.Min(short_, Math.Max(1, item.Amount));

                if (part < Math.Max(1, item.Amount) && (!item.Stackable || Mobile.LiftItemDupe(item, part) == null))
                {
                    continue;
                }

                var moved = Math.Max(1, item.Amount);

                if (!pack.TryDropItem(keeper, item, false))
                {
                    break;
                }

                short_ -= moved;
                SelfSupplied += moved;
            }
        }
    }

    internal static void Refresh(BotStorefront front)
    {
        if (front?.Keeper == null)
        {
            return;
        }

        Count(front.Keeper, _units, _asks, true, out _, out var demanded);
        front.Restock(_units, _asks, demanded);
    }

    /// <summary>What one bot has to sell, reckoned for the proposer and kept for <see cref="LookMs"/>.</summary>
    public readonly record struct BotStock(int Units, int Kinds, int Worth, int Demanded);

    private static readonly Dictionary<Serial, (BotStock Stock, long Tick)> _looks = [];

    public static BotStock Look(BotMobile keeper)
    {
        if (keeper == null)
        {
            return default;
        }

        var now = Core.TickCount;

        if (_looks.TryGetValue(keeper.Serial, out var known) && now - known.Tick < LookMs)
        {
            return known.Stock;
        }

        Count(keeper, _units, _asks, false, out var worth, out var demanded);

        var units = 0;

        foreach (var (_, count) in _units)
        {
            units += count;
        }

        var stock = new BotStock(units, _units.Count, worth, demanded);

        if (_looks.Count > 4096)
        {
            _looks.Clear();
        }

        _looks[keeper.Serial] = (stock, now);

        return stock;
    }

    /// <summary>One kind's price in one keeper's shop, and what it has learned from selling it.</summary>
    private sealed class Price
    {
        public int Anchor;

        public int Asking;

        public long SoldTick;

        public bool Traded;
    }

    private static readonly Dictionary<(Serial Keeper, Type Kind), Price> _book = [];

    private static int Quote(BotMobile keeper, Type kind, Item sample, bool remember)
    {
        if (_book.TryGetValue((keeper.Serial, kind), out var known))
        {
            return known.Asking;
        }

        var open = Opening(keeper, kind, sample);

        if (open > 0 && remember)
        {
            if (_book.Count >= MostPrices)
            {
                Prune();
            }

            _book[(keeper.Serial, kind)] = new Price { Anchor = open, Asking = open };
        }

        return open;
    }

    public static int Opening(BotMobile keeper, Type kind, Item sample)
    {
        if (keeper == null || kind == null)
        {
            return 0;
        }

        var shelf = ShelfOf(keeper, kind);

        if (shelf > 0)
        {
            var open = Math.Min(BotMarket.Worth(kind, shelf), (int)Math.Round(shelf * ShelfShare));

            return open >= BotMarket.FloorFor(kind) ? open : 0;
        }

        if (BotMarket.Worthless(kind))
        {
            return 0;
        }

        var paid = PaidFor(keeper, kind, sample);
        var worth = BotMarket.Worth(kind, paid > 0 ? paid : BotAnvil.Reckon(kind));

        return worth >= BotMarket.FloorFor(kind) ? worth : 0;
    }

    private static readonly Dictionary<Type, (int Price, long Tick)> _shelf = [];

    private static readonly Dictionary<Type, (int Price, long Tick)> _paid = [];

    private static int ShelfOf(BotMobile keeper, Type kind)
    {
        var now = Core.TickCount;

        if (_shelf.TryGetValue(kind, out var known) && now - known.Tick < ShelfMs)
        {
            return known.Price;
        }

        var price = BotShops.Shelf(keeper, kind, 0);

        _shelf[kind] = (price, now);

        return price;
    }

    private static int PaidFor(BotMobile keeper, Type kind, Item sample)
    {
        var now = Core.TickCount;

        if (_paid.TryGetValue(kind, out var known) && now - known.Tick < ShelfMs)
        {
            return known.Price;
        }

        if (sample == null)
        {
            return 0;
        }

        var price = BotShops.Buyer((Mobile)keeper, sample, out var offered) != null ? offered : 0;

        _paid[kind] = (price, now);

        return price;
    }

    public static int PriceFor(BotStorefront front, Mobile buyer, Type kind) =>
        front == null ? 0 : BotPact.Price(buyer, front.Keeper, front.Ask(kind));

    private static void Prune()
    {
        HashSet<Serial> open = [];

        for (var i = 0; i < _open.Count; i++)
        {
            if (_open[i].Keeper != null)
            {
                open.Add(_open[i].Keeper.Serial);
            }
        }

        List<(Serial, Type)> gone = [];

        foreach (var (key, _) in _book)
        {
            if (!open.Contains(key.Keeper))
            {
                gone.Add(key);
            }
        }

        for (var i = 0; i < gone.Count; i++)
        {
            _book.Remove(gone[i]);
        }
    }

    private static void Learn(BotStorefront front, Type kind)
    {
        var keeper = front.Keeper;

        if (keeper == null || !_book.TryGetValue((keeper.Serial, kind), out var price))
        {
            return;
        }

        var now = Core.TickCount;
        var brisk = price.Traded && now - price.SoldTick < BotMarket.BriskMs;

        price.Traded = true;
        price.SoldTick = now;

        if (!brisk)
        {
            return;
        }

        var ceiling = Math.Max(1, (int)(price.Anchor * BotMarket.MostMultiple));
        var asking = Math.Min(ceiling, Math.Max(price.Asking + 1, (int)(price.Asking * (1.0 + BotMarket.RaiseStep))));

        if (asking <= price.Asking)
        {
            return;
        }

        price.Asking = asking;
        front.Priced(kind, asking);
        Raised++;

        logger.Information(
            "{Name} put {Item} up to {Price}gp in its shop after selling it again soon",
            keeper.Name,
            kind.Name,
            asking
        );
    }

    private static void MarkDown(BotStorefront front)
    {
        var keeper = front.Keeper;

        if (keeper == null)
        {
            return;
        }

        foreach (var kind in front.Goods)
        {
            if (front.SoldAny(kind) || !_book.TryGetValue((keeper.Serial, kind), out var price))
            {
                continue;
            }

            var floor = Math.Max(BotMarket.FloorFor(kind), (int)(price.Anchor * BotMarket.LeastMultiple));
            var asking = Math.Max(floor, Math.Min(price.Asking - 1, (int)(price.Asking * (1.0 - BotMarket.CutStep))));

            if (asking < price.Asking)
            {
                price.Asking = asking;
                Cut++;
            }
        }
    }

    private static readonly HashSet<Type> _demanded = [];

    private static long _demandTick;

    private static bool _demandRead;

    public static bool Demanded(Type kind)
    {
        if (kind == null)
        {
            return false;
        }

        var now = Core.TickCount;

        if (!_demandRead || now - _demandTick >= DemandMs)
        {
            _demandRead = true;
            _demandTick = now;
            _demanded.Clear();

            var shortages = BotShopper.Shortages();

            for (var i = 0; i < shortages.Count; i++)
            {
                _demanded.Add(shortages[i].Kind);
            }

            var wants = BotMarket.Wants;

            for (var i = 0; i < wants.Count; i++)
            {
                if (wants[i] is { IsOpen: true, Kind: { } wanted })
                {
                    _demanded.Add(wanted);
                }
            }
        }

        return _demanded.Contains(kind);
    }

    public static BotStorefront Best(IBotWilful bot, Type kind, int amount, out int unit, out int units, out double landed)
    {
        unit = 0;
        units = 0;
        landed = double.MaxValue;

        var body = bot?.Self;
        var map = body?.Map;

        if (!Running || _open.Count == 0 || kind == null || map == null || map == Map.Internal || amount <= 0)
        {
            return null;
        }

        var ledger = bot.Resolve?.Ledger;
        BotStorefront best = null;

        for (var i = 0; i < _open.Count; i++)
        {
            var front = _open[i];

            if (!Live(front) || front.Map != map || ReferenceEquals(front.Keeper, body))
            {
                continue;
            }

            var held = front.Held(kind);
            var ask = PriceFor(front, body, kind);

            if (held <= 0 || ask <= 0)
            {
                continue;
            }

            if (!Utility.InRange(body.Location, front.Location, SeekTiles) || !BotPopulation.Within(map, front.Location, body))
            {
                Far++;

                continue;
            }

            if (ledger?.Cautious(Kind, map, front.Location) == true
                || BotReach.Ask(map, body.Location, front.Location, BotArrival.Within(ServeReach)) == BotReachVerdict.Sealed)
            {
                Sealed++;

                continue;
            }

            var n = Math.Min(amount, held);
            var per = ask + BotAppraisal.Travel(body.Location, front.Location) * WalkGold / n;

            if (per < landed)
            {
                best = front;
                landed = per;
                unit = ask;
                units = n;
            }
        }

        return best;
    }

    public static BotStorefront Beats(IBotWilful bot, Type wanted, int amount, BaseVendor shop, int counter, out int unit, out int units)
    {
        unit = 0;
        units = 0;

        if (!Running || _open.Count == 0)
        {
            return null;
        }

        var front = Best(bot, wanted, amount, out var ask, out var n, out var landed);

        if (front == null)
        {
            return null;
        }

        Weighed++;

        var body = bot.Self;

        if (shop != null && counter > 0)
        {
            var walk = BotAppraisal.Travel(body.Location, shop.Location) * WalkGold / Math.Max(1, amount);

            if (landed > counter + walk)
            {
                LostToCounter++;

                return null;
            }
        }

        ToFront++;
        unit = ask;
        units = n;

        return front;
    }

    internal static BotDoing Visit(
        IBotWilful bot, BotStorefront front, Type kind, int amount, int quoted, bool forWant, out int bought, out int paid,
        out BaseVendor onward
    )
    {
        bought = 0;
        paid = 0;
        onward = null;

        var body = bot?.Self;

        if (body == null || kind == null)
        {
            return BotDoing.Failed("no body");
        }

        if (!Live(front))
        {
            Gone++;

            if (!forWant && (onward = BotShops.Nearest(bot, kind)) != null)
            {
                FellThrough++;

                return default;
            }

            return BotDoing.Failed($"{front?.Keeper?.Name ?? "the keeper"} had shut up shop");
        }

        var keeper = front.Keeper;

        if (body.Map != keeper.Map || !body.InRange(keeper.Location, ServeReach))
        {
            return BotDoing.Walk(front.Map, keeper, BotArrival.Within(ServeReach), $"to {keeper.Name}'s shop in {front.Town} for {kind.Name}");
        }

        Arrived++;

        var want = forWant ? BotMarket.Wanted(bot, kind) : null;

        if (forWant && want is not { IsOpen: true })
        {
            FilledMeanwhile++;

            return BotDoing.Done($"its want for {kind.Name} was met while it walked");
        }

        var payable = want?.Payable ?? 0;
        var offer = want?.Offer ?? 0;
        var units = forWant ? Math.Min(amount, payable) : amount;

        if (forWant && offer > 0 && (quoted <= 0 || quoted > offer))
        {
            quoted = offer;
        }

        if (Available(keeper, kind) <= 0)
        {
            SoldOutOnArrival++;
            BotShopBook.Missed(front.Town, kind);

            if (!forWant && (onward = BotShops.Nearest(bot, kind)) != null)
            {
                FellThrough++;

                return default;
            }

            return BotDoing.Failed($"{keeper.Name} had sold the last of its {kind.Name}");
        }

        bought = Serve(bot, front, kind, units, quoted, forWant, out paid, out var refused);

        if (forWant && bought > 0)
        {
            Released += bought;
            BotMarket.Received(want, bought, paid, keeper, BotRoute.Shop);
        }

        if (bought <= 0)
        {
            return BotDoing.Failed(refused ?? $"{keeper.Name} would not sell it");
        }

        return BotDoing.Done(
            forWant
                ? $"{bought} {kind.Name} at {keeper.Name}'s shop in {front.Town} for {paid}gp, against its want at {offer}gp"
                : $"{bought} {kind.Name} at {keeper.Name}'s shop in {front.Town} for {paid}gp"
        );
    }

    private static int Available(BotMobile keeper, Type kind)
    {
        var total = 0;

        Gather(keeper, kind, _lots);

        for (var i = 0; i < _lots.Count; i++)
        {
            total += _lots[i].Take;
        }

        return total;
    }

    private static readonly List<(Item Item, int Take)> _lots = [];

    private static void Gather(BotMobile keeper, Type kind, List<(Item Item, int Take)> lots)
    {
        lots.Clear();

        if (keeper == null || kind == null)
        {
            return;
        }

        if (BotMarket.Wanted(keeper, kind) != null)
        {
            return;
        }

        var bond = keeper.Bond;
        var box = keeper.FindBankNoCreate();

        if (box != null)
        {
            var held = box.Items;

            for (var i = 0; i < held.Count; i++)
            {
                var item = held[i];

                if (item?.GetType() == kind && Merchandise(item, bond))
                {
                    lots.Add((item, Math.Max(1, item.Amount)));
                }
            }
        }

        var pack = keeper.Backpack;

        if (pack == null)
        {
            return;
        }

        var keep = BotUnload.Keeps(keeper);

        _seen.Clear();

        var carried = pack.Items;

        for (var i = 0; i < carried.Count; i++)
        {
            var item = carried[i];

            if (item?.GetType() != kind || !Merchandise(item, bond))
            {
                continue;
            }

            var over = Over(keep, _seen, item);

            if (over > 0)
            {
                lots.Add((item, over));
            }
        }
    }

    internal static int Serve(IBotWilful buyer, BotStorefront front, Type kind, int wanted, int quoted, bool forWant, out int paid, out string refused)
    {
        paid = 0;
        refused = null;

        var body = buyer?.Self;
        var keeper = front?.Keeper;
        var pack = body?.Backpack;

        if (pack == null || keeper == null || kind == null || wanted <= 0)
        {
            refused = "nobody to buy from";

            return 0;
        }

        var price = PriceFor(front, body, kind);

        if (price <= 0)
        {
            price = Quote(keeper, kind, null, false);
        }

        if (quoted > 0 && (price <= 0 || quoted < price))
        {
            price = quoted;
        }

        if (price <= 0)
        {
            refused = $"{keeper.Name} had stopped selling {kind.Name}";

            return 0;
        }

        Gather(keeper, kind, _lots);

        var available = 0;

        for (var i = 0; i < _lots.Count; i++)
        {
            available += _lots[i].Take;
        }

        if (available <= 0)
        {
            SoldOutOnArrival++;
            refused = $"{keeper.Name} had sold the last of its {kind.Name}";

            return 0;
        }

        var units = Math.Min(wanted, available);

        var against = forWant ? kind : null;
        var purse = (int)Math.Min(BotYield.Wealth(body), (long)BotYield.Spendable(body) + (forWant ? BotMarket.ReservedFor(body, kind) : 0));

        if (purse < price)
        {
            Broke++;
            refused = $"{purse}gp will not buy one {kind.Name} at {price}gp";

            return 0;
        }

        units = Math.Min(units, purse / price);

        var bill = units * price;

        if (!BotMarket.Charge(body, bill, out _, against))
        {
            Broke++;
            refused = $"could not put {bill}gp down for {units} {kind.Name}";

            return 0;
        }

        var given = 0;

        for (var i = 0; i < _lots.Count && given < units; i++)
        {
            var (item, take) = _lots[i];

            if (item == null || item.Deleted)
            {
                continue;
            }

            var part = Math.Min(take, units - given);

            if (part < Math.Max(1, item.Amount) && (!item.Stackable || Mobile.LiftItemDupe(item, part) == null))
            {
                continue;
            }

            var moved = Math.Max(1, item.Amount);

            if (!pack.TryDropItem(body, item, false))
            {
                PackFull++;

                break;
            }

            given += moved;
        }

        if (given < units)
        {
            BotMarket.Refund(body, (units - given) * price);
        }

        if (given <= 0)
        {
            refused = $"the pack would not take {kind.Name}";

            return 0;
        }

        var takings = given * price;

        BotMarket.Pay(keeper, takings - BotCharters.Split(keeper, front.Town, kind, given, takings));
        BotMarket.Traded(kind, given, takings);

        BotPact.Bought(front.Ask(kind), price, given);

        paid = takings;

        front.Took(body, kind, given, takings);
        Learn(front, kind);
        BotShopBook.Sold(front.Town, kind, given, takings);

        BotRegard.Traded((body.Guild as Guilds.Guild)?.Name, (keeper.Guild as Guilds.Guild)?.Name);
        BotBurgh.Supplied(keeper, kind, given);

        Sales++;
        Units += given;
        Takings += takings;
        _soldKinds[kind] = (_soldKinds.TryGetValue(kind, out var before) ? before : 0) + given;

        if (forWant)
        {
            WantSales++;
            WantUnits += given;
        }
        else
        {
            SupplySales++;
            SupplyUnits += given;
        }

        logger.Information(
            "{Buyer} bought {Units} {Item} at {Keeper}'s shop in {Town} for {Paid}gp, {Price} each — {Why}",
            body.Name,
            given,
            kind.Name,
            keeper.Name,
            front.Town,
            takings,
            price,
            forWant ? "against its own spoken want" : "short of it for its kit"
        );

        BotEvents.Post("market", keeper, $"sold {given} {kind.Name} to {body.Name} for {takings}gp");

        var thanks = BotVoice.Phrase(
            "shop:sold",
            null,
            keeper,
            new Dictionary<string, string> { ["buyer"] = body.Name, ["amount"] = given.ToString(), ["item"] = Words(kind) }
        );

        BotVoice.Say(keeper, BotVoice.HandOver, thanks ?? $"There you are, {body.Name}.");

        return given;
    }

    public static long CityLots { get; private set; }

    public static long CityUnits { get; private set; }

    public static long CityGold { get; private set; }

    public static (int Lots, int Units, int Paid) CityBuy(Type kind, int wanted, int most, int budget, double share, int lots, string who)
    {
        if (!Running || _open.Count == 0 || wanted <= 0 || budget <= 0 || lots <= 0 || share <= 0.0)
        {
            return (0, 0, 0);
        }

        var took = 0;
        var things = 0;
        var paid = 0;

        List<BotStorefront> open = [.. _open];

        for (var i = 0; i < open.Count && took < lots && things < wanted && paid < budget; i++)
        {
            var front = open[i];

            if (!Live(front))
            {
                continue;
            }

            var keeper = front.Keeper;
            var ranked = front.Ranked();

            for (var k = 0; k < ranked.Count && took < lots && things < wanted && paid < budget; k++)
            {
                var (lotKind, _, ask) = ranked[k];

                if (kind != null && lotKind != kind || ask <= 0)
                {
                    continue;
                }

                if (most > 0 && ask > most)
                {
                    continue;
                }

                var price = Math.Max(1, (int)Math.Round(ask * share));
                var afford = (budget - paid) / price;
                var units = Math.Min(Math.Min(Available(keeper, lotKind), wanted - things), afford);

                if (units <= 0)
                {
                    continue;
                }

                var gone = Remove(keeper, lotKind, units);

                if (gone <= 0)
                {
                    continue;
                }

                var bill = gone * price;

                BotMarket.Pay(keeper, bill - BotCharters.Split(keeper, front.Town, lotKind, gone, bill));
                BotMarket.Traded(lotKind, gone, bill);

                front.Took(null, lotKind, gone, bill);
                Learn(front, lotKind);
                BotShopBook.Sold(front.Town, lotKind, gone, bill);

                Sales++;
                Units += gone;
                Takings += bill;
                _soldKinds[lotKind] = _soldKinds.GetValueOrDefault(lotKind) + gone;

                took++;
                things += gone;
                paid += bill;

                logger.Information(
                    "The city bought {Units} {Item} at {Keeper}'s shop in {Town} for {Paid}gp, {Price} each — {Who}",
                    gone,
                    lotKind.Name,
                    keeper.Name,
                    front.Town,
                    bill,
                    price,
                    who ?? "the city"
                );

                BotEvents.Post("market", keeper, $"sold {gone} {lotKind.Name} to the city for {bill}gp");
            }
        }

        CityLots += took;
        CityUnits += things;
        CityGold += paid;

        return (took, things, paid);
    }

    private static int Remove(BotMobile keeper, Type kind, int units)
    {
        Gather(keeper, kind, _lots);

        var gone = 0;

        for (var i = 0; i < _lots.Count && gone < units; i++)
        {
            var (item, take) = _lots[i];

            if (item == null || item.Deleted)
            {
                continue;
            }

            var part = Math.Min(take, units - gone);
            var many = Math.Max(1, item.Amount);

            if (part >= many)
            {
                item.Delete();
                gone += many;

                continue;
            }

            if (!item.Stackable)
            {
                continue;
            }

            item.Amount = many - part;
            gone += part;
        }

        return gone;
    }

    internal static void Cry(BotMobile keeper, BotStorefront front, string occasion)
    {
        if (keeper == null || front == null)
        {
            return;
        }

        var ranked = front.Ranked();

        if (ranked.Count == 0)
        {
            return;
        }

        using var goods = ValueStringBuilder.Create(96);

        for (var i = 0; i < ranked.Count && i < 3; i++)
        {
            if (i > 0)
            {
                goods.Append(i == Math.Min(3, ranked.Count) - 1 ? " and " : ", ");
            }

            goods.Append(Words(ranked[i].Kind));
        }

        var fill = new Dictionary<string, string>
        {
            ["goods"] = goods.ToString(),
            ["item"] = Words(ranked[0].Kind),
            ["price"] = ranked[0].Ask.ToString(),
            ["amount"] = ranked[0].Units.ToString(),
            ["town"] = front.Town
        };

        var line = BotVoice.Phrase(occasion, "shop:cry", keeper, fill) ?? $"{fill["goods"]} for sale here, by the bank.";

        if (BotVoice.Say(keeper, "trade", line))
        {
            Cries++;
        }
    }

    public static string Words(Type kind)
    {
        var raw = kind?.Name ?? "goods";

        using var spaced = ValueStringBuilder.Create(raw.Length + 8);

        for (var i = 0; i < raw.Length; i++)
        {
            var c = raw[i];

            if (i > 0 && char.IsUpper(c))
            {
                spaced.Append(' ');
            }

            spaced.Append(char.ToLowerInvariant(c));
        }

        return spaced.ToString();
    }

    public static void Web(Utf8JsonWriter w)
    {
        w.WritePropertyName("shops");
        w.WriteStartArray();

        for (var i = 0; i < _open.Count; i++)
        {
            var front = _open[i];

            if (!Live(front))
            {
                continue;
            }

            w.WriteStartObject();
            w.WriteString("keeper", front.Keeper.Name);
            w.WriteString("guild", (front.Keeper.Guild as Guilds.Guild)?.Abbreviation);
            w.WriteString("town", front.Town);
            w.WriteNumber("x", front.Location.X);
            w.WriteNumber("y", front.Location.Y);
            w.WriteNumber("units", front.Units);
            w.WriteNumber("worth", front.Worth);
            w.WriteNumber("sold", front.Sold);
            w.WriteNumber("takings", front.Takings);
            w.WriteNumber("customers", front.Customers);
            w.WriteNumber("minutes", Math.Round((Core.TickCount - front.OpenedTick) / 60000.0, 1));

            w.WritePropertyName("goods");
            w.WriteStartArray();

            var ranked = front.Ranked();

            for (var g = 0; g < ranked.Count && g < 6; g++)
            {
                w.WriteStartArray();
                w.WriteStringValue(ranked[g].Kind.Name);
                w.WriteNumberValue(ranked[g].Units);
                w.WriteNumberValue(ranked[g].Ask);
                w.WriteEndArray();
            }

            w.WriteEndArray();
            w.WriteEndObject();
        }

        w.WriteEndArray();
    }

    private static string Commonest(int most)
    {
        if (_soldKinds.Count == 0)
        {
            return "nothing yet";
        }

        List<(Type Kind, long Units)> ranked = [];

        foreach (var (kind, units) in _soldKinds)
        {
            ranked.Add((kind, units));
        }

        ranked.Sort(static (a, b) => b.Units.CompareTo(a.Units));

        using var say = ValueStringBuilder.Create(128);

        for (var i = 0; i < ranked.Count && i < most; i++)
        {
            if (i > 0)
            {
                say.Append(", ");
            }

            say.Append(ranked[i].Kind.Name);
            say.Append(' ');
            say.Append(ranked[i].Units.ToString());
        }

        return say.ToString();
    }

    public static string Describe()
    {
        Sweep();

        var units = 0;
        var worth = 0;

        for (var i = 0; i < _open.Count; i++)
        {
            units += _open[i].Units;
            worth += _open[i].Worth;
        }

        return $"{_open.Count} of {MostOpen} open now holding {units} things worth {worth}gp; "
            + $"{BotShopkeeper.Describe()}; "
            + $"{Opened} shifts opened ({Reopened} more openings after the keeper was called away), {Closed} ended ({SoldOut} sold out, {TimeUp} ran their {ShiftMinutes:F0} minutes, {Empty} of them sold nothing), {Suspended} shut while the keeper was away or taken off, {Refused} could not open at the bank, {BotKeepShop.Rewalked} sent on to another pitch when the road to theirs could not be walked ({PitchesStruck} pitches struck for every keeper after one could not get to them, {StripsStruck} of them widened to the strip between two within {StripTiles} tiles, {PitchesPassedStruck} looks passed a struck pitch, {PitchesPassedOccupied} a pitch a body stood on, {PitchesPassedFlag} a pitch the reach map does not call open; {PitchesLeftDoor} tiles left out of the pitches as a doorway and {PitchesLeftWall} as out of the banker's sight; {OpenedWhereStood} shifts opened where the keeper stood when the last steps could not be walked, {OpenedWhereStoodLate} of them after {OpenStandingMs / 1000}s of walking, {StoodInDoorway} times a keeper stood in a doorway and walked on; {WalksEndedOnOpening} walks to the pitch ended when the shop opened, {OpenWalksSpared} walks back to it that failed with the shop open and the keeper beside it kept the shift); "
            + $"{Sales} sales of {Units} things for {Takings}gp, every one to a bot that came for it: {SupplySales} sales ({SupplyUnits} things) to bots short of their kit and {WantSales} ({WantUnits} things) against the buyer's own spoken want ({Released} of them taken off its want); most sold {Commonest(5)}; "
            + $"supplies: {Weighed} looks found a bot's shop holding the thing, {ToFront} chose it, {LostToCounter} a shopkeeper was cheaper counting the walk, {Sealed} shops passed over as out of reach and {Far} as too far off; "
            + $"{BotPatron.Describe()}; "
            + $"at the till {Arrived} customers arrived: {SoldOutOnArrival} found the thing gone and {Gone} set out for a shop that shut, {FellThrough} of those going on to a shopkeeper; {PackFull} could not carry it all, {Broke} could not pay, {FilledMeanwhile} found their want met on the board meanwhile; "
            + $"{Raised} prices raised after selling again soon, {Cut} cut after a shift nobody bought them in; {SelfSupplied} things keepers took out of their own boxes for their own kits; {Cries} cries; sell-through {Share:P0} of demanded stock over {Shifts} measured shifts ({ShownTotal}gp shown, {TakenTotal}gp taken, {SellShare:P0} assumed until {LeastShifts}); "
            + $"the city bought {CityUnits} things in {CityLots} lots at the shops for {CityGold}gp; "
            + BotShopBook.Describe(4);
    }

    public static void Forget()
    {
        for (var i = 0; i < _open.Count; i++)
        {
            _open[i].Shut = true;
        }

        _open.Clear();
        _held.Clear();
        _book.Clear();
        _looks.Clear();
        _shelf.Clear();
        _paid.Clear();
        _banks.Clear();
        _pitches.Clear();
        _restUntil.Clear();
        _soldKinds.Clear();
        _demanded.Clear();
        _demandRead = false;

        Opened = 0;
        Reopened = 0;
        Closed = 0;
        SoldOut = 0;
        TimeUp = 0;
        Empty = 0;
        Suspended = 0;
        Refused = 0;
        _struck.Clear();
        PitchesStruck = 0;
        StripsStruck = 0;
        PitchesPassedStruck = 0;
        PitchesPassedOccupied = 0;
        PitchesPassedFlag = 0;
        OpenedWhereStood = 0;
        PitchesLeftDoor = 0;
        PitchesLeftWall = 0;
        StoodInDoorway = 0;
        OpenedWhereStoodLate = 0;
        WalksEndedOnOpening = 0;
        OpenWalksSpared = 0;
        Sales = 0;
        Units = 0;
        Takings = 0;
        SupplySales = 0;
        SupplyUnits = 0;
        WantSales = 0;
        WantUnits = 0;
        Released = 0;
        Weighed = 0;
        ToFront = 0;
        LostToCounter = 0;
        CityLots = 0;
        CityUnits = 0;
        CityGold = 0;
        Sealed = 0;
        Far = 0;
        Arrived = 0;
        SoldOutOnArrival = 0;
        Gone = 0;
        FellThrough = 0;
        PackFull = 0;
        Broke = 0;
        FilledMeanwhile = 0;
        Raised = 0;
        Cut = 0;
        Cries = 0;
        SelfSupplied = 0;
        ShownTotal = 0;
        TakenTotal = 0;
        Shifts = 0;

        BotShopkeeper.Forget();
        BotPatron.Forget();
        BotShopBook.Forget();
    }
}
