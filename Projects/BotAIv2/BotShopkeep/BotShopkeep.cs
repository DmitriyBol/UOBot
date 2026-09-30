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
/// <b>Why a shop and not another stall.</b> The market (<c>BotAuction</c>) is placeless: a stall holds its goods out of the
/// world and anybody anywhere buys from it without taking a step. That made trade between bots possible at all, and it is
/// also why nothing about it can be seen — no bot stands anywhere, nobody walks to anybody, and a town is not a place trade
/// happens. A shop is the other way round. The goods stay in the keeper's own pack and bank box, where the engine keeps them
/// and the save keeps them; a customer walks to the keeper and the goods move pack to pack at the till, paid for in coin that
/// moves pack to pack (<see cref="Serve"/>). The auction is not removed: <see cref="BotAuction.LotsPerBot"/> cuts the kinds
/// one bot may hold out on it, and whatever it turns away stays in the pack, goes to the bank box with the rest of the
/// surplus (<c>BotUnload.Store</c>), and is sold from there by the bank.
/// </para>
///
/// <para>
/// <b>A shop wins where it is better and nowhere else.</b> A bot short of a supply (<c>BotShopper</c>) already weighs a
/// stall against a shopkeeper on price, and a shop is entered into that same weighing on price <em>and</em> the walk
/// (<see cref="Beats"/>): the keeper's ask plus the minutes of walking to it, priced at what the shard reckons a shopping
/// minute is worth (<see cref="WalkGold"/>, which follows <c>BotRestock.Prior</c>), against the shopkeeper's price plus the
/// walk to the shopkeeper and against the stall's price with no walk at all. A want on the board is weighed the same way
/// against its own offer (<see cref="BotPatron"/>). Ties go to the shop, as ties between a stall and a counter go to the
/// stall: coin paid to a bot stays in the population.
/// </para>
///
/// <para>
/// <b>Prices start where the island already is and move the way the auction's do.</b> A keeper opens a kind at what the
/// nearest shopkeeper asks for it, or what the market reckons it worth when that is less (<see cref="Opening"/>), so a bot
/// shop never opens above the shelf — the rule <c>BotShops.Shelf</c> already enforces on the stalls. A kind that sells
/// again inside <c>BotAuction.BriskMs</c> goes up by <c>BotAuction.RaiseStep</c>; a kind nobody bought during a whole shift
/// comes down by <c>BotAuction.CutStep</c>, both inside the auction's own ×0.25…×4 of the opening ask. The same numbers, so
/// a dial that makes the market bolder makes the shops bolder too.
/// </para>
/// </summary>
public static class BotShopkeep
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotShopkeep));

    public const string Kind = "botshop";

    public static bool Running { get; set; } = true;

    public static int AuctionLots { get; set; } = 3;

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

    public static int ServeReach { get; set; } = 3;

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

    public static long LostToStall { get; private set; }

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

            if (BotReach.Ask(map, body.Location, spot, BotArrival.Within(StandReach), false) == BotReachVerdict.Sealed)
            {
                continue;
            }

            return spot;
        }

        return Point3D.Zero;
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

        var wants = BotAuction.Wants;

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
            var open = Math.Min(BotAuction.Worth(kind, shelf), (int)Math.Round(shelf * ShelfShare));

            return open >= BotAuction.FloorFor(kind) ? open : 0;
        }

        if (BotAuction.Worthless(kind))
        {
            return 0;
        }

        var worth = BotAuction.Worth(kind, PaidFor(keeper, kind, sample));

        return worth >= BotAuction.FloorFor(kind) ? worth : 0;
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
        var brisk = price.Traded && now - price.SoldTick < BotAuction.BriskMs;

        price.Traded = true;
        price.SoldTick = now;

        if (!brisk)
        {
            return;
        }

        var ceiling = Math.Max(1, (int)(price.Anchor * BotAuction.MostMultiple));
        var asking = Math.Min(ceiling, Math.Max(price.Asking + 1, (int)(price.Asking * (1.0 + BotAuction.RaiseStep))));

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

            var floor = Math.Max(BotAuction.FloorFor(kind), (int)(price.Anchor * BotAuction.LeastMultiple));
            var asking = Math.Max(floor, Math.Min(price.Asking - 1, (int)(price.Asking * (1.0 - BotAuction.CutStep))));

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

            var wants = BotAuction.Wants;

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

    public static BotStorefront Beats(
        IBotWilful bot, Type wanted, int amount, BaseVendor shop, int counter, BotListing stall, out int unit, out int units
    )
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

        if (stall is { IsEmpty: false } && landed > stall.Price)
        {
            LostToStall++;

            return null;
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

        var want = forWant ? BotAuction.Wanted(bot, kind) : null;

        if (forWant && want is not { IsOpen: true })
        {
            FilledMeanwhile++;

            return BotDoing.Done($"its want for {kind.Name} was met on the board while it walked");
        }

        var payable = want?.Payable ?? 0;
        var offer = want?.Offer ?? 0;
        var units = forWant ? Math.Min(amount, payable) : amount;

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

        var released = 0;

        if (forWant)
        {
            released = BotAuction.Withdrawn(bot, kind);
        }

        bought = Serve(bot, front, kind, units, quoted, forWant, out paid, out var refused);

        if (forWant)
        {
            Released += released;

            var rest = payable - bought;

            if (rest > 0)
            {
                BotAuction.Ask(bot, kind, rest, offer);
            }
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

        if (BotAuction.Wanted(keeper, kind) != null)
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
        var purse = BotYield.Wealth(body);

        if (purse < price)
        {
            Broke++;
            refused = $"{purse}gp will not buy one {kind.Name} at {price}gp";

            return 0;
        }

        units = Math.Min(units, purse / price);

        var bill = units * price;

        if (!BotAuction.Charge(body, bill))
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
            Refund(body, (units - given) * price);
        }

        if (given <= 0)
        {
            refused = $"the pack would not take {kind.Name}";

            return 0;
        }

        var takings = given * price;

        Pay(keeper, takings);

        paid = takings;

        front.Took(body, kind, given, takings);
        Learn(front, kind);
        BotShopBook.Sold(front.Town, kind, given, takings);

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
            forWant ? "against its own want on the board" : "short of it for its kit"
        );

        BotEvents.Post("market", keeper, $"sold {given} {kind.Name} to {body.Name} for {takings}gp");

        var thanks = BotVoice.Phrase(
            "shop:sold",
            null,
            keeper,
            new Dictionary<string, string> { ["buyer"] = body.Name, ["amount"] = given.ToString(), ["item"] = Words(kind) }
        );

        BotVoice.Say(keeper, "local", thanks ?? $"There you are, {body.Name}.");

        return given;
    }

    private static void Pay(Mobile keeper, int amount)
    {
        if (amount <= 0 || keeper == null)
        {
            return;
        }

        var pack = keeper.Backpack;

        if (pack != null && amount <= 60000)
        {
            var coin = new Gold(amount);

            if (pack.TryDropItem(keeper, coin, false))
            {
                return;
            }

            coin.Delete();
        }

        if (Banker.Deposit(keeper, amount))
        {
            return;
        }

        Force(keeper, amount);
    }

    private static void Refund(Mobile buyer, int amount)
    {
        if (amount <= 0 || buyer == null || Banker.Deposit(buyer, amount))
        {
            return;
        }

        Force(buyer, amount);
    }

    private static void Force(Mobile who, int amount)
    {
        var pack = who?.Backpack;

        if (pack == null)
        {
            logger.Error("{Amount}gp owed to {Name} had nowhere to go and was lost", amount, who?.Name ?? "nobody");

            return;
        }

        while (amount > 0)
        {
            var part = Math.Min(amount, 60000);

            pack.DropItem(new Gold(part));
            amount -= part;
        }
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
            ["amount"] = ranked[0].Units.ToString()
        };

        var line = BotVoice.Phrase(occasion, "shop:cry", keeper, fill) ?? $"{fill["goods"]} for sale here, by the bank.";

        if (BotVoice.Say(keeper, "local", line))
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
            + $"{Opened} shifts opened ({Reopened} more openings after the keeper was called away), {Closed} ended ({SoldOut} sold out, {TimeUp} ran their {ShiftMinutes:F0} minutes, {Empty} of them sold nothing), {Suspended} shut while the keeper was away or taken off, {Refused} could not open at the bank; "
            + $"{Sales} sales of {Units} things for {Takings}gp, every one to a bot that came for it: {SupplySales} sales ({SupplyUnits} things) to bots short of their kit and {WantSales} ({WantUnits} things) against the buyer's own want on the board, {Released}gp of escrow taken back to buy them; most sold {Commonest(5)}; "
            + $"supplies: {Weighed} looks found a bot's shop holding the thing, {ToFront} chose it, {LostToCounter} a shopkeeper was cheaper counting the walk, {LostToStall} a stall was cheaper, {Sealed} shops passed over as out of reach and {Far} as too far off; "
            + $"{BotPatron.Describe()}; "
            + $"at the till {Arrived} customers arrived: {SoldOutOnArrival} found the thing gone and {Gone} set out for a shop that shut, {FellThrough} of those going on to a shopkeeper; {PackFull} could not carry it all, {Broke} could not pay, {FilledMeanwhile} found their want met on the board meanwhile; "
            + $"{Raised} prices raised after selling again soon, {Cut} cut after a shift nobody bought them in; {SelfSupplied} things keepers took out of their own boxes for their own kits; {Cries} cries; sell-through {Share:P0} of demanded stock over {Shifts} measured shifts ({ShownTotal}gp shown, {TakenTotal}gp taken, {SellShare:P0} assumed until {LeastShifts}); "
            + $"the auction kept {BotAuction.Capped} things in their sellers' hands at {BotAuction.LotsPerBot} kinds a bot; "
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
        LostToStall = 0;
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
