using System;
using System.Collections.Generic;
using Server.Items;
using Server.Logging;
using Server.Mobiles;
using Server.Text;

namespace Server.BotAI.V2;

/// <summary>
/// The bots' market with the auction taken out of it: Britain's square by the bank, the wants the bots say aloud, the prices
/// the island has learned from goods that really changed hands, and the one correct way to take a bot's money.
///
/// <para>
/// <b>Patrick's order of 30.09.2026, night: "The auction is removed completely; all trade and all the workshops live in
/// Britain… Trade through talk and the global chat. Every order and every purchase must now be hand to hand."</b> The auction held
/// goods out of the world and money in escrow, and every trade on it happened between two bots that never met: 563 stall sales
/// and 421 fills for 24,482gp in the hour of 16:42–17:42 on 30.09.2026, 806 things crossed from a stall to a want by the
/// market's own beat, 171 deliveries fetched off the board by a reflex, against 20 sales for 526gp at the bots' shops by the
/// banks where both sides stood. None of that placeless half is left. What is here is what trade between two bots needs when
/// they must meet: a place (<see cref="Square"/>), a way to say what one wants (<see cref="Ask"/>, spoken in the world's
/// chat), a price that means something (<see cref="Worth"/>), and money that moves only with the goods
/// (<see cref="BotHandover"/>).
/// </para>
///
/// <para>
/// <b>Where the goods go now.</b> A seller keeps its goods in its own pack and bank box, where the engine and the save keep
/// them. It sells them standing by Britain's bank (<see cref="BotShopkeep"/>, a guild's in its own town), carries them to a
/// bot that has said it wants them (<see cref="BotCourier"/>, <see cref="BotDeliver"/>), or takes them to a shopkeeper
/// (<see cref="BotPeddler"/>). A buyer walks to a shop by the bank (<see cref="BotPatron"/>, <see cref="BotShopper"/>) or
/// says what it wants and waits for a carrier. Every one of those ends with the two standing within
/// <see cref="BotHandover.MeetTiles"/> of each other, goods pack to pack and coin pack to pack.
/// </para>
/// </summary>
public static class BotMarket
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotMarket));

    public static bool Running { get; set; } = true;

    public static double RaiseStep { get; set; } = 0.15;

    public static double CutStep { get; set; } = 0.10;

    public static int BriskMs { get; set; } = 600000;

    public static int StaleMs { get; set; } = 600000;

    public static int RaiseMs
    {
        get => _raiseMs ?? StaleMs;
        set => _raiseMs = value;
    }

    private static int? _raiseMs;

    public static double MostMultiple { get; set; } = 4.0;

    public static double LeastMultiple { get; set; } = 0.25;

    public static int Floor { get; set; } = 2;

    public static int MaxWants { get; set; } = 512;

    public static int BeatMs { get; set; } = 30000;

    public static int SayWantMs { get; set; } = 120000;

    public static int RisenGraceMs { get; set; } = 300000;

    public static int MostAgeMs { get; set; } = 6600000;

    public static int BankReach { get; set; } = 12;

    private static readonly Dictionary<Map, (Point3D Bank, long Tick)> _squares = [];

    public static Point3D Square(Map map)
    {
        if (map == null || map == Map.Internal)
        {
            return Point3D.Zero;
        }

        var now = Core.TickCount;

        if (_squares.TryGetValue(map, out var known) && known.Bank != Point3D.Zero && now - known.Tick < BotShopkeep.BankMemoryMs)
        {
            return known.Bank;
        }

        var town = BotTowns.Find(BotCapital.Name);
        var hearth = town == null ? Point3D.Zero : BotSettle.Of(town)?.Hearth ?? town.Square;
        var bank = hearth == Point3D.Zero ? Point3D.Zero : BotShopkeep.BankNear(map, hearth);

        _squares[map] = (bank, now);

        return bank;
    }

    public static bool InBritain(Point3D where)
    {
        var town = BotTowns.Of(where);

        return town != null && string.Equals(town.Name, BotCapital.Name, StringComparison.OrdinalIgnoreCase);
    }

    public static bool AtBank(Mobile body)
    {
        var map = body?.Map;

        if (map == null || map == Map.Internal)
        {
            return false;
        }

        var now = Core.TickCount;

        if (_atBank.TryGetValue(body.Serial, out var known) && known.Where == body.Location && known.Map == map
            && now - known.Tick < AtBankMs)
        {
            return known.At;
        }

        var at = false;

        foreach (var banker in map.GetMobilesInRange<Banker>(body.Location, BankReach))
        {
            if (banker is { Deleted: false })
            {
                at = true;

                break;
            }
        }

        if (_atBank.Count > 2048)
        {
            _atBank.Clear();
        }

        _atBank[body.Serial] = (map, body.Location, at, now);

        return at;
    }

    public static int AtBankMs { get; set; } = 10000;

    private static readonly Dictionary<Serial, (Map Map, Point3D Where, bool At, long Tick)> _atBank = [];

    public static long BoxTaken { get; private set; }

    public static int FromBank(IBotWilful bot, Type kind, int units)
    {
        var body = bot?.Self;
        var pack = body?.Backpack;
        var box = body?.FindBankNoCreate();

        if (pack == null || box == null || kind == null || units <= 0 || box.Items.Count == 0 || !AtBank(body))
        {
            return 0;
        }

        var moved = 0;

        for (var i = box.Items.Count - 1; i >= 0 && moved < units; i--)
        {
            var item = box.Items[i];

            if (item?.GetType() != kind || !BotShopkeep.Merchandise(item, bot.Bond))
            {
                continue;
            }

            var part = Math.Min(units - moved, Math.Max(1, item.Amount));

            if (part < Math.Max(1, item.Amount) && (!item.Stackable || Mobile.LiftItemDupe(item, part) == null))
            {
                continue;
            }

            var got = Math.Max(1, item.Amount);

            if (!pack.TryDropItem(body, item, false))
            {
                break;
            }

            moved += got;
        }

        BoxTaken += moved;

        return moved;
    }

    public static int Stock(Mobile maker, Type kind)
    {
        if (kind == null)
        {
            return 0;
        }

        var box = maker?.FindBankNoCreate();

        return BotShopkeep.Showing(kind) + (maker?.Backpack?.GetAmount(kind) ?? 0) + (box?.GetAmount(kind) ?? 0);
    }

    private static readonly HashSet<Type> _worthless = [];

    private static readonly HashSet<Type> _staples = [];

    private static readonly Dictionary<Type, (long Units, long Gold)> _dealt = [];

    public static long Cheap { get; private set; }

    public static long Unpriced { get; private set; }

    public static bool Worthless(Type kind) => kind != null && _worthless.Contains(kind);

    public static void Condemn(Type kind, bool measured)
    {
        if (kind == null)
        {
            return;
        }

        Cheap++;

        if (!measured)
        {
            Unpriced++;

            return;
        }

        if (!_staples.Contains(kind))
        {
            _worthless.Add(kind);
        }
    }

    public static void Staple(Type kind)
    {
        if (kind != null)
        {
            _staples.Add(kind);
            _worthless.Remove(kind);
        }
    }

    public static int FloorFor(Type kind) => kind != null && _staples.Contains(kind) ? 1 : Floor;

    public static string Condemned(int most)
    {
        if (_worthless.Count == 0)
        {
            return "none";
        }

        List<string> named = [];

        foreach (var kind in _worthless)
        {
            if (named.Count >= most)
            {
                named.Add("and more");

                break;
            }

            named.Add(kind.Name);
        }

        return string.Join(", ", named);
    }

    public static void Traded(Type kind, int units, int gold)
    {
        if (kind == null || units <= 0 || gold <= 0)
        {
            return;
        }

        var (u, g) = _dealt.GetValueOrDefault(kind);

        if (u + units > 1000)
        {
            u /= 2;
            g /= 2;
        }

        _dealt[kind] = (u + units, g + gold);

        BotGlut.Sold(kind, units);
    }

    public static int Priced => _dealt.Count;

    public static int Worth(Type kind, int fallback)
    {
        if (kind != null && _dealt.TryGetValue(kind, out var dealt) && dealt.Units > 0)
        {
            return (int)Math.Max(1, dealt.Gold / dealt.Units);
        }

        return fallback;
    }

    public static bool Ceilings { get; set; } = true;

    public static double MaterialShare { get; set; } = 2.0;

    public static int CeilingMs { get; set; } = 300000;

    private static readonly Func<Server.Engines.Craft.CraftSystem>[] _systems =
    [
        () => Server.Engines.Craft.DefBlacksmithy.CraftSystem,
        () => Server.Engines.Craft.DefTailoring.CraftSystem,
        () => Server.Engines.Craft.DefAlchemy.CraftSystem,
        () => Server.Engines.Craft.DefBowFletching.CraftSystem,
        () => Server.Engines.Craft.DefCooking.CraftSystem,
        () => Server.Engines.Craft.DefInscription.CraftSystem,
        () => Server.Engines.Craft.DefTinkering.CraftSystem,
        () => Server.Engines.Craft.DefCarpentry.CraftSystem
    ];

    private static readonly Dictionary<Type, (int Ceiling, Type Product, int Worth, long Tick)> _ceilings = [];

    public static long CappedAsks { get; private set; }

    public static long CappedGold { get; private set; }

    public static long CeilingHeld { get; private set; }

    public static long Unsourced { get; private set; }

    public static int Ceiling(Type kind) => Ceiling(kind, out _, out _);

    public static int Ceiling(Type kind, out Type product, out int worth)
    {
        product = null;
        worth = 0;

        if (!Ceilings || kind == null || BotProvision.Consumable(kind))
        {
            return 0;
        }

        var now = Core.TickCount;

        if (_ceilings.TryGetValue(kind, out var known) && now - known.Tick < (known.Ceiling > 0 ? CeilingMs : CeilingMs / 10))
        {
            product = known.Product;
            worth = known.Worth;

            return known.Ceiling;
        }

        var best = 0.0;

        for (var s = 0; s < _systems.Length; s++)
        {
            Server.Engines.Craft.CraftSystem system;

            try
            {
                system = _systems[s]();
            }
            catch
            {
                continue;
            }

            var recipes = system?.CraftItems;

            if (recipes == null)
            {
                continue;
            }

            for (var i = 0; i < recipes.Count; i++)
            {
                var recipe = recipes[i];
                var made = recipe?.ItemType;
                var needs = recipe?.Resources;

                if (made == null || needs == null || made == kind)
                {
                    continue;
                }

                for (var r = 0; r < needs.Count; r++)
                {
                    var res = needs[r];

                    if (res?.ItemType == null || res.Amount <= 0 || res.ItemType != kind && !res.ItemType.IsAssignableFrom(kind))
                    {
                        continue;
                    }

                    var value = Worth(made, BotShops.Lowest(made));

                    if (value <= 0)
                    {
                        break;
                    }

                    var per = (double)value / res.Amount;

                    if (per > best)
                    {
                        best = per;
                        product = made;
                        worth = value;
                    }

                    break;
                }
            }
        }

        var ceiling = best > 0.0 ? Math.Max(Floor, (int)Math.Ceiling(best * Math.Max(1.0, MaterialShare))) : 0;

        if (_ceilings.Count > 512)
        {
            _ceilings.Clear();
        }

        _ceilings[kind] = (ceiling, product, worth, now);

        return ceiling;
    }

    public static bool Sourced(Type kind, int ceiling)
    {
        if (ceiling <= 0 || kind == null)
        {
            return true;
        }

        var counter = BotShops.Lowest(kind);

        if (counter > 0 && counter <= ceiling)
        {
            return true;
        }

        if (BotGlut.Seen(kind) > 0)
        {
            return true;
        }

        for (var i = 0; i < BotOven.Raw.Count; i++)
        {
            if (BotOven.Raw[i] == kind)
            {
                return true;
            }
        }

        return kind == typeof(Feather) || kind == typeof(Wool) || typeof(BaseHides).IsAssignableFrom(kind)
               || typeof(BaseLeather).IsAssignableFrom(kind);
    }

    public static int MaterialCap(Type kind)
    {
        var cap = Ceiling(kind);

        if (BotOven.MealWorth(kind) is > 0 and var meal && (cap <= 0 || meal < cap))
        {
            cap = meal;
        }

        return cap;
    }

    public static int RecapMs { get; set; } = 600000;

    public static long Recapped { get; private set; }

    public static long BroughtDown { get; private set; }

    public static long BroughtDownGold { get; private set; }

    private static long _startedTick;

    internal static void Recap(BotWant want)
    {
        if (want?.Kind == null)
        {
            return;
        }

        var cap = MaterialCap(want.Kind);

        if (cap <= 0)
        {
            cap = want.Ceiling;
        }

        if (cap <= 0)
        {
            if (Core.TickCount - _startedTick >= RecapMs)
            {
                want.CeilingPending = false;
            }

            return;
        }

        cap = Math.Max(FloorFor(want.Kind), cap);
        want.Ceiling = cap;
        want.CeilingPending = false;
        Recapped++;

        var down = want.Bring(cap);

        if (down <= 0)
        {
            return;
        }

        BroughtDown++;
        BroughtDownGold += (long)down * want.Amount;

        logger.Information(
            "{Name}'s want for {Item}, read back from the save at {Was}gp, came down to its material's ceiling of {Offer}gp (want #{Id})",
            want.Buyer?.Self?.Name,
            want.Label,
            want.Offer + down,
            want.Offer,
            want.Id
        );
    }

    private static string DescribeCeilings()
    {
        if (!Ceilings)
        {
            return "off";
        }

        if (_ceilings.Count == 0)
        {
            return "none read yet";
        }

        List<string> named = [];

        foreach (var (kind, known) in _ceilings)
        {
            if (known.Ceiling <= 0 || named.Count >= 8)
            {
                continue;
            }

            named.Add($"{kind.Name} {known.Ceiling}gp ({known.Product?.Name} at {known.Worth})");
        }

        return named.Count == 0 ? "no material read has a priced product" : string.Join(", ", named);
    }

    public static int ReaskMs { get; set; } = 7200000;

    private static readonly Dictionary<(Serial Buyer, Type Kind), (int Opened, long Tick)> _gaveUp = [];

    public static long UnderDearer { get; private set; }

    public static long UnderDearerGold { get; private set; }

    public static long ShelfCapped { get; private set; }

    public static long ShelfCappedGold { get; private set; }

    public static long ShelfKnown { get; private set; }

    public static long Reasked { get; private set; }

    public static long ReaskCapped { get; private set; }

    public static long ReaskCappedGold { get; private set; }

    public static long Shelved { get; private set; }

    public static long ShelvedDown { get; private set; }

    public static long ShelvedDownGold { get; private set; }

    public static long ShelfHeld { get; private set; }

    /// <summary>What <see cref="Opening"/> did to one opening, counted only when the want is really said.</summary>
    private struct OpeningNote
    {
        public int Shelf;
        public int Dearer;
        public int ShelfCut;
        public bool Reask;
        public int ReaskCut;
    }

    private static int Opening(Mobile buyer, Type kind, int offer, out OpeningNote note)
    {
        note = default;

        note.Dearer = Best(kind);

        var shelf = BotShops.Lowest(kind);

        if (shelf > 0)
        {
            note.Shelf = shelf;

            var most = (int)Math.Min(int.MaxValue, shelf * (long)Math.Max(1.0, MostMultiple));

            if (offer > most)
            {
                note.ShelfCut = offer - most;
                offer = most;
            }
        }

        if (buyer != null && _gaveUp.TryGetValue((buyer.Serial, kind), out var gave))
        {
            if (Core.TickCount - gave.Tick >= ReaskMs)
            {
                _gaveUp.Remove((buyer.Serial, kind));
            }
            else
            {
                note.Reask = true;

                if (gave.Opened > 0 && offer > gave.Opened)
                {
                    note.ReaskCut = offer - gave.Opened;
                    offer = gave.Opened;
                }
            }
        }

        return offer;
    }

    private static void Opened(in OpeningNote note, int offer, int units)
    {
        if (note.Dearer > offer)
        {
            UnderDearer++;
            UnderDearerGold += (long)(note.Dearer - offer) * units;
        }

        if (note.Shelf > 0)
        {
            ShelfKnown++;
        }

        if (note.ShelfCut > 0)
        {
            ShelfCapped++;
            ShelfCappedGold += (long)note.ShelfCut * units;
        }

        if (note.Reask)
        {
            Reasked++;

            if (note.ReaskCut > 0)
            {
                ReaskCapped++;
                ReaskCappedGold += (long)note.ReaskCut * units;
            }
        }
    }

    private static void GaveUp(BotWant want)
    {
        if (want?.Buyer?.Self is not { } buyer || want.Kind == null)
        {
            return;
        }

        if (_gaveUp.Count > 4096)
        {
            _gaveUp.Clear();
        }

        _gaveUp[(buyer.Serial, want.Kind)] = (want.Opened, Core.TickCount);
    }

    internal static void Shelve(BotWant want)
    {
        if (want?.Kind == null)
        {
            return;
        }

        var shelf = BotShops.Lowest(want.Kind);

        if (shelf <= 0)
        {
            if (Core.TickCount - _startedTick >= RecapMs)
            {
                want.ShelfPending = false;
            }

            return;
        }

        want.Shelf = shelf;
        want.ShelfPending = false;
        Shelved++;

        var down = want.Bring((int)Math.Min(int.MaxValue, shelf * (long)Math.Max(1.0, MostMultiple)));

        if (down <= 0)
        {
            return;
        }

        ShelvedDown++;
        ShelvedDownGold += (long)down * want.Amount;

        logger.Information(
            "{Name}'s want for {Item} came down from {Was}gp to {Offer}gp, four times the {Shelf}gp a shopkeeper asks for one (want #{Id})",
            want.Buyer?.Self?.Name,
            want.Label,
            want.Offer + down,
            want.Offer,
            shelf,
            want.Id
        );
    }

    private static string DescribeDearest(int most)
    {
        if (_wants.Count == 0)
        {
            return "none standing";
        }

        Dictionary<Type, BotWant> dearest = [];

        for (var i = 0; i < _wants.Count; i++)
        {
            var want = _wants[i];

            if (want?.Kind == null || want.Amount <= 0)
            {
                continue;
            }

            if (!dearest.TryGetValue(want.Kind, out var known) || want.Offer > known.Offer)
            {
                dearest[want.Kind] = want;
            }
        }

        List<BotWant> ordered = [..dearest.Values];

        ordered.Sort(static (a, b) => ((double)b.Offer / Math.Max(1, b.Anchor)).CompareTo((double)a.Offer / Math.Max(1, a.Anchor)));

        List<string> named = [];

        for (var i = 0; i < ordered.Count && i < most; i++)
        {
            var want = ordered[i];
            var shelf = want.Shelf > 0 ? $", a shopkeeper's {want.Shelf}" : "";

            named.Add($"{want.Kind.Name} {want.Offer}gp against its {want.Anchor} (×{(double)want.Offer / Math.Max(1, want.Anchor):F1}{shelf})");
        }

        return string.Join(", ", named);
    }

    private static string DescribeOpenings() =>
        $"what a want opens at (what the thing sold for between bots, else the buyer's own estimate; never another buyer's standing offer): {UnderDearer} of {Posted} said while a dearer offer for the kind stood ({UnderDearerGold}gp a unit under it, summed over units — the old reckoning opened at that offer, to ×{MostMultiple:F0} of the buyer's estimate), "
        + $"{ShelfKnown} anchored where a shopkeeper sells the kind and so climbing no higher than ×{MostMultiple:F0} its price, {ShelfCapped} held to that at the opening ({ShelfCappedGold}gp less asked), {ShelfHeld} raises not taken there (the want waited at its price), "
        + $"{Reasked} said again within {ReaskMs / 60000} min of the same buyer's want for the kind giving up and {ReaskCapped} of them held to what that one opened at ({ReaskCappedGold}gp less asked); "
        + $"read back or said before the counters were read: {Shelved} given their shopkeeper's price on the beat, {ShelvedDown} brought down to ×{MostMultiple:F0} it ({ShelvedDownGold}gp less held); "
        + $"dearest standing offers against what they opened at: {DescribeDearest(6)}";

    public static string Label(Item item)
    {
        var name = item?.Name;

        return !string.IsNullOrWhiteSpace(name) ? name : Spaced(item?.GetType().Name ?? "goods");
    }

    public static string Spaced(string raw)
    {
        raw ??= "goods";

        using var spaced = ValueStringBuilder.Create(raw.Length + 8);

        for (var i = 0; i < raw.Length; i++)
        {
            var c = raw[i];

            if (i > 0 && char.IsUpper(c))
            {
                spaced.Append(' ');
            }

            spaced.Append(c);
        }

        return spaced.ToString();
    }

    private static readonly List<BotWant> _wants = [];

    private static int _nextWantId;

    public static IReadOnlyList<BotWant> Wants => _wants;

    public static int Asks => _wants.Count;

    public static bool Full => _wants.Count >= MaxWants;

    public static long Posted { get; private set; }

    public static long Topped { get; private set; }

    public static long Met { get; private set; }

    public static long Abandoned { get; private set; }

    public static long Unfunded { get; private set; }

    public static long Unpaid { get; private set; }

    public static long Orphaned { get; private set; }

    public static long Retracted { get; private set; }

    public static long Raises { get; private set; }

    public static long Cuts { get; private set; }

    public static long Answered { get; private set; }

    public static long AnsweredGold { get; private set; }

    public static long Said { get; private set; }

    public static long Swept { get; private set; }

    public static long Unreserved { get; private set; }

    public static long UnreservedGold { get; private set; }

    public static long HeldBack { get; private set; }

    public static long HeldBackGold { get; private set; }

    public static long Uncovered { get; private set; }

    public static long KeptThroughDeath { get; private set; }

    public static long KeptRising { get; private set; }

    public static long KeptAway { get; private set; }

    public static long UnpaidAfterDeath { get; private set; }

    public static long Held { get; private set; }

    public static long AgedOut { get; private set; }

    private static readonly long[] _retractedFor = new long[5];

    private static readonly Dictionary<Serial, long> _reserved = [];

    private static bool _reservedStale = true;

    internal static void Touched() => _reservedStale = true;

    public static int Reserved(Mobile body)
    {
        if (body == null || _wants.Count == 0)
        {
            return 0;
        }

        Refresh();

        return (int)Math.Min(int.MaxValue, _reserved.GetValueOrDefault(body.Serial));
    }

    private static void Refresh()
    {
        if (!_reservedStale)
        {
            return;
        }

        _reserved.Clear();

        for (var i = 0; i < _wants.Count; i++)
        {
            var want = _wants[i];

            if (want.Buyer?.Self is { } buyer)
            {
                _reserved[buyer.Serial] = _reserved.GetValueOrDefault(buyer.Serial) + Holds(want);
            }
        }

        _reservedStale = false;
    }

    private static long Holds(BotWant want) => Math.Max(0L, (long)want.Amount * want.Offer + want.Premium - BotAdvance.On(want));

    public static int Affords(BotWant want, int price, IBotWilful carrier = null)
    {
        if (want?.Buyer?.Self is not { } buyer || price <= 0 || want.Amount <= 0)
        {
            return 0;
        }

        var wealth = (long)BotYield.Wealth(buyer);
        var free = Math.Max(0L, wealth - Reserved(buyer));
        var over = price - want.Offer;
        long units = want.Amount;

        if (over > 0)
        {
            units = Math.Min(units, (free + (carrier == null ? 0 : want.PremiumOf(carrier))) / over);
        }

        units = Math.Min(units, (wealth + (carrier == null ? 0 : BotAdvance.Credit(carrier, want))) / price);

        return (int)Math.Max(0L, units);
    }

    public static BotWant Of(Mobile body, Type kind)
    {
        if (body == null || kind == null)
        {
            return null;
        }

        for (var i = 0; i < _wants.Count; i++)
        {
            var want = _wants[i];

            if (want.Kind == kind && ReferenceEquals(want.Buyer?.Self, body))
            {
                return want;
            }
        }

        return null;
    }

    public static bool SecuresReserve { get; set; } = true;

    public static long Secured { get; private set; }

    public static long SecuredGold { get; private set; }

    public static long SavedByBank { get; private set; }

    private static readonly HashSet<Serial> _securedBuyers = [];

    private static readonly HashSet<Serial> _securedThisBeat = [];

    public static int Secure(Mobile buyer)
    {
        if (!SecuresReserve || buyer is not BotMobile { Deleted: false, Alive: true } body || body.Class is { Stipend: > 0 } || BotFence.Is(body)
            || body.Backpack is not { } pack)
        {
            return 0;
        }

        var reserved = Reserved(body);

        if (reserved <= 0)
        {
            return 0;
        }

        var pocket = pack.GetAmount(typeof(Gold));

        if (pocket <= 0)
        {
            return 0;
        }

        var lacking = reserved - Banker.GetBalance(body);

        if (lacking <= 0)
        {
            return 0;
        }

        var put = Math.Min(pocket, lacking);

        if (body.FindBankNoCreate() == null)
        {
            _ = body.BankBox;
        }

        if (!pack.ConsumeTotal(typeof(Gold), put))
        {
            return 0;
        }

        if (!Banker.Deposit(body, put))
        {
            pack.DropItem(new Gold(put));

            return 0;
        }

        Secured++;
        SecuredGold += put;

        if (_securedBuyers.Count > 4096)
        {
            _securedBuyers.Clear();
        }

        _securedBuyers.Add(body.Serial);

        return put;
    }

    public static int ReservedFor(Mobile body, Type kind)
    {
        if (body == null || kind == null)
        {
            return 0;
        }

        for (var i = 0; i < _wants.Count; i++)
        {
            var want = _wants[i];

            if (want.Kind == kind && ReferenceEquals(want.Buyer?.Self, body))
            {
                return (int)Math.Min(int.MaxValue, Holds(want));
            }
        }

        return 0;
    }

    public static int Backing(BotWant want)
    {
        if (want?.Buyer?.Self is not { } buyer)
        {
            return 0;
        }

        var wealth = BotYield.Wealth(buyer);
        var free = Math.Max(0L, (long)wealth - Reserved(buyer));

        return (int)Math.Min(wealth, free + (long)want.Amount * want.Offer);
    }

    public static BotWant Ask(IBotWilful buyer, Type kind, int units, int offer)
    {
        var body = buyer?.Self;

        if (!Running || body == null || kind == null || units <= 0 || offer <= 0)
        {
            return null;
        }

        if (!BotRoom.Receives(body, kind))
        {
            BotRoom.Note(body);
            BotRoom.NotAsked();

            return null;
        }

        var want = Wanted(buyer, kind);

        var cap = 0;
        var scribeless = false;

        if (want == null)
        {
            cap = Ceiling(kind);

            if (BotOven.MealWorth(kind) is > 0 and var meal && (cap <= 0 || meal < cap))
            {
                cap = meal;
            }

            if (cap > 0)
            {
                cap = Math.Max(FloorFor(kind), cap);

                if (!Sourced(kind, cap))
                {
                    Unsourced++;

                    return null;
                }

                if (offer > cap)
                {
                    CappedAsks++;
                    CappedGold += (long)(offer - cap) * units;
                    offer = cap;
                }
            }
            else if (BotScribes.Ceiling(kind, offer, out scribeless) is > 0 and var writ)
            {
                if (offer > writ)
                {
                    BotScribes.Capped(units, offer - writ);
                    offer = writ;
                }

                cap = writ;
            }
        }

        var opening = default(OpeningNote);

        if (want == null)
        {
            offer = Opening(body, kind, offer, out opening);
        }

        _worthless.Remove(kind);

        var price = want?.Offer ?? offer;
        var owed = (want?.Amount ?? 0) + units;
        var wealth = BotYield.Wealth(body);
        var bill = (long)owed * price;

        if (wealth < bill)
        {
            Unfunded++;

            return null;
        }

        var others = Reserved(body) - (want == null ? 0L : (long)want.Amount * want.Offer);

        if (wealth - others < bill)
        {
            Unreserved++;
            UnreservedGold += bill;

            return null;
        }

        if (want != null)
        {
            want.Top(units);
            Topped++;
            BotLines.Topped(want, units);

            Secure(body);

            return want;
        }

        if (_wants.Count >= MaxWants)
        {
            logger.Error("The market already holds {Count} wants, so {Name} could not ask for {Item}", _wants.Count, body.Name, kind.Name);

            return null;
        }

        want = new BotWant(++_nextWantId, buyer, kind, units, offer) { Ceiling = cap, Shelf = opening.Shelf };

        want.ShelfPending = opening.Shelf <= 0 && Core.TickCount - _startedTick < RecapMs;

        _wants.Add(want);
        Touched();
        Posted++;

        Opened(opening, offer, units);

        var line = BotLines.Open(want, units);

        logger.Information(
            "{Name} wants {Units} {Item} at {Offer}gp each and has said so to the island (line #{Line}; {Held}gp of its {Wealth}gp now held for its wants)",
            body.Name,
            units,
            kind.Name,
            offer,
            line.Name,
            others + bill,
            wealth
        );

        Say(want, "market:want", true);

        if (scribeless)
        {
            BotScribes.SaidBeyond(kind, offer);
        }

        Secure(body);

        return want;
    }

    public static BotWant Wanted(IBotWilful buyer, Type kind)
    {
        for (var i = 0; i < _wants.Count; i++)
        {
            var want = _wants[i];

            if (ReferenceEquals(want.Buyer, buyer) && want.Kind == kind)
            {
                return want;
            }
        }

        return null;
    }

    public static bool Stands(BotWant want) => want != null && _wants.Contains(want);

    public static BotWant Demand(IBotWilful supplier, Type kind)
    {
        if (kind == null || Wanted(supplier, kind) != null)
        {
            return null;
        }

        BotWant best = null;
        var bestServed = false;

        for (var i = 0; i < _wants.Count; i++)
        {
            var want = _wants[i];

            if (want.Kind != kind || !want.IsOpen || ReferenceEquals(want.Buyer, supplier))
            {
                continue;
            }

            if (want.Buyer?.Self is not { Deleted: false } buyer)
            {
                continue;
            }

            if (!BotCourier.Answerable(supplier, want))
            {
                continue;
            }

            var served = BotPact.Serves(supplier?.Self, buyer);

            if (best == null || served && !bestServed || served == bestServed && want.Offer > best.Offer)
            {
                best = want;
                bestServed = served;
            }
        }

        return best;
    }

    public static int Best(Type kind)
    {
        var best = 0;

        for (var i = 0; i < _wants.Count; i++)
        {
            var want = _wants[i];

            if (want.Kind == kind && want.IsOpen && want.Offer > best)
            {
                best = want.Offer;
            }
        }

        return best;
    }

    public static int Answering(IBotWilful bot, Type kind, int units)
    {
        if (units <= 0 || !BotCourier.Deliverable(bot, kind))
        {
            return 0;
        }

        return Math.Min(units, Asked(kind));
    }

    public static int Holding(IBotWilful bot, Type kind, int units, Dictionary<Type, int> held)
    {
        if (units <= 0 || kind == null || !BotCourier.Deliverable(bot, kind))
        {
            return 0;
        }

        held.TryGetValue(kind, out var already);
        var hold = Math.Min(units, Math.Max(0, Asked(kind) - already));

        if (hold > 0)
        {
            held[kind] = already + hold;
        }

        return hold;
    }

    public static int Asked(Type kind)
    {
        var units = 0;

        for (var i = 0; i < _wants.Count; i++)
        {
            var want = _wants[i];

            if (want.Kind == kind)
            {
                units += want.Payable;
            }
        }

        return units;
    }

    private static readonly HashSet<Type> _bespoke = [];

    private static long _bespokeTick;

    private static bool _bespokeRead;

    public static int BespokeMs { get; set; } = 2000;

    public static bool Bespoke(Type kind)
    {
        if (kind == null)
        {
            return false;
        }

        var now = Core.TickCount;

        if (!_bespokeRead || now - _bespokeTick >= BespokeMs)
        {
            _bespokeRead = true;
            _bespokeTick = now;
            _bespoke.Clear();

            for (var i = 0; i < _wants.Count; i++)
            {
                if (_wants[i].IsOpen && _wants[i].Kind != null)
                {
                    _bespoke.Add(_wants[i].Kind);
                }
            }
        }

        return _bespoke.Contains(kind);
    }

    public static bool AnyBespoke => _wants.Count > 0;

    public static int Withdrawn(IBotWilful buyer, Type kind, BotWithdraw why)
    {
        var want = Wanted(buyer, kind);

        if (want == null)
        {
            return 0;
        }

        _wants.Remove(want);
        Touched();
        Retracted++;
        _retractedFor[Math.Clamp((int)why, 0, _retractedFor.Length - 1)]++;

        if (why == BotWithdraw.Unpaid)
        {
            BotLines.Ended(want, BotLineEnd.Unpaid, want.DiedWhileStanding ? "buyer could not pay after a death" : "buyer could not pay");
        }
        else
        {
            BotLines.Ended(want, BotLineEnd.Withdrawn, Named(why));
        }

        return want.Amount;
    }

    private static string Named(BotWithdraw why) =>
        why switch
        {
            BotWithdraw.Have => "had it another way",
            BotWithdraw.Walked => "nobody brought it, walked to a counter",
            BotWithdraw.Unpaid => "could not pay at the meeting",
            BotWithdraw.Closed => "a trader's gathering closed",
            _ => "other"
        };

    public static void Received(BotWant want, int units, int gold, Mobile seller, BotRoute route)
    {
        if (want == null || units <= 0)
        {
            return;
        }

        Answered += units;
        AnsweredGold += Math.Max(0, gold);
        Supplied(want.Kind, units);

        var brisk = want.Took(units, gold, BriskMs);

        BotLines.Filled(want, units, seller, route);
        BotScribes.Handover(want, units, seller);

        if (want.Amount <= 0)
        {
            _wants.Remove(want);
            Touched();
            Met++;
            BotLines.Ended(want, BotLineEnd.Met, null);

            return;
        }

        if (brisk && want.Cut(CutStep, LeastMultiple))
        {
            Cuts++;

            logger.Information(
                "{Name} dropped its offer for {Item} to {Offer}gp after being answered again soon",
                want.Buyer?.Self?.Name,
                want.Label,
                want.Offer
            );
        }
    }

    public static void Received(IBotWilful buyer, Type kind, int units, int gold, Mobile seller, BotRoute route) =>
        Received(Wanted(buyer, kind), units, gold, seller, route);

    public static int SupplyBucketMs { get; set; } = 300000;

    private const int SupplyBuckets = 12;

    private static readonly Dictionary<Type, (long[] Epoch, int[] Units)> _brought = [];

    private static void Supplied(Type kind, int units)
    {
        if (kind == null || units <= 0)
        {
            return;
        }

        if (!_brought.TryGetValue(kind, out var ring))
        {
            ring = (new long[SupplyBuckets], new int[SupplyBuckets]);
            _brought[kind] = ring;
        }

        var epoch = Core.TickCount / Math.Max(1000, SupplyBucketMs);
        var slot = (int)(epoch % SupplyBuckets);

        if (ring.Epoch[slot] != epoch)
        {
            ring.Epoch[slot] = epoch;
            ring.Units[slot] = 0;
        }

        ring.Units[slot] += units;

        _pace[kind] = (Decayed(kind, Core.TickCount) + units, Core.TickCount);
    }

    public static int SupplyHalfLifeMs { get; set; } = 900000;

    private static readonly Dictionary<Type, (double Units, long Tick)> _pace = [];

    private static double Decayed(Type kind, long now)
    {
        if (!_pace.TryGetValue(kind, out var p))
        {
            return 0.0;
        }

        var half = Math.Max(60000, SupplyHalfLifeMs);

        return p.Units * Math.Pow(0.5, Math.Max(0L, now - p.Tick) / (double)half);
    }

    public static int Pace(Type kind)
    {
        if (kind == null)
        {
            return 0;
        }

        var half = Math.Max(60000, SupplyHalfLifeMs);

        return (int)(Decayed(kind, Core.TickCount) * Math.Log(2.0) * 3600000.0 / half);
    }

    public static int Supply(Type kind) => Math.Min(Brought(kind), Pace(kind));

    public static int Brought(Type kind)
    {
        if (kind == null || !_brought.TryGetValue(kind, out var ring))
        {
            return 0;
        }

        var epoch = Core.TickCount / Math.Max(1000, SupplyBucketMs);
        var units = 0;

        for (var i = 0; i < SupplyBuckets; i++)
        {
            if (epoch - ring.Epoch[i] < SupplyBuckets)
            {
                units += ring.Units[i];
            }
        }

        return units;
    }

    public static int Wanting(Type kind)
    {
        var units = 0;

        for (var i = 0; i < _wants.Count; i++)
        {
            var want = _wants[i];

            if (want.Kind == kind && !want.Away && want.Buyer?.Self is { Map: { } map } && map != Map.Internal)
            {
                units += want.Amount;
            }
        }

        return units;
    }

    public static (int Open, int Worth) Asking(IBotWilful buyer)
    {
        var open = 0;
        var worth = 0;

        for (var i = 0; i < _wants.Count; i++)
        {
            var want = _wants[i];

            if (!ReferenceEquals(want.Buyer, buyer) || !want.IsOpen)
            {
                continue;
            }

            open++;
            worth += want.Worth;
        }

        return (open, worth);
    }

    public static (int Units, int Worth) Sought()
    {
        var units = 0;
        var worth = 0;

        for (var i = 0; i < _wants.Count; i++)
        {
            units += _wants[i].Amount;
            worth += _wants[i].Amount * _wants[i].Offer;
        }

        return (units, worth);
    }

    public static int Coming()
    {
        var units = 0;

        for (var i = 0; i < _wants.Count; i++)
        {
            units += _wants[i].Coming;
        }

        return units;
    }

    private static void Say(BotWant want, string occasion, bool force)
    {
        var body = want?.Buyer?.Self;

        if (body == null || !BotVoice.Enabled)
        {
            return;
        }

        var now = Core.TickCount;

        if (!force && want.Said && now - want.SaidTick < SayWantMs)
        {
            return;
        }

        var town = BotTowns.Of(body.Location)?.Name;

        var fill = new Dictionary<string, string>
        {
            ["amount"] = want.Amount.ToString(),
            ["item"] = BotShopkeep.Words(want.Kind),
            ["price"] = want.Offer.ToString(),
            ["town"] = town ?? "the road"
        };

        var line = BotVoice.Phrase(occasion, "market:want", body, fill)
                   ?? $"Buying {fill["amount"]} {fill["item"]} at {fill["price"]} each. Bring them to me.";

        if (BotVoice.Say(body, "trade", line, topic: want.Kind?.Name))
        {
            want.Spoken(now);
            Said++;
        }
    }

    private static long _unfundedSaidTick;

    private static bool _unfundedSaid;

    public static int UnfundedSayMs { get; set; } = 60000;

    public static bool Charge(Mobile buyer, int bill) => Charge(buyer, bill, out _);

    public static bool Charge(Mobile buyer, int bill, out int fromPack) => Charge(buyer, bill, out fromPack, null);

    private static long _heldBackSaidTick;

    private static bool _heldBackSaid;

    public static bool Charge(Mobile buyer, int bill, out int fromPack, Type against)
    {
        fromPack = 0;

        if (bill <= 0)
        {
            return true;
        }

        if (buyer == null)
        {
            return false;
        }

        if (_wants.Count > 0 && Reserved(buyer) > 0)
        {
            var free = (long)BotYield.Spendable(buyer) + (against == null ? 0 : ReservedFor(buyer, against));

            if (free < bill)
            {
                HeldBack++;
                HeldBackGold += bill;

                var tick = Core.TickCount;

                if (!_heldBackSaid || tick - _heldBackSaidTick >= UnfundedSayMs)
                {
                    _heldBackSaid = true;
                    _heldBackSaidTick = tick;

                    logger.Information(
                        "{Name} did not pay {Bill}gp: {Held}gp of its {Wealth}gp is held for its wants on the board, and {Free}gp may be spent on this",
                        buyer.Name,
                        bill,
                        Reserved(buyer),
                        BotYield.Wealth(buyer),
                        free
                    );
                }

                return false;
            }
        }

        var pack = buyer.Backpack;
        var purse = pack?.GetAmount(typeof(Gold)) ?? 0;
        var taken = 0;

        if (purse > 0)
        {
            taken = Math.Min(purse, bill);

            if (!pack.ConsumeTotal(typeof(Gold), taken))
            {
                taken = 0;
            }
        }

        var rest = bill - taken;

        if (rest <= 0 || Banker.Withdraw(buyer, rest))
        {
            fromPack = taken;

            return true;
        }

        if (taken > 0)
        {
            pack.DropItem(new Gold(taken));
        }

        var now = Core.TickCount;

        if (!_unfundedSaid || now - _unfundedSaidTick >= UnfundedSayMs)
        {
            _unfundedSaid = true;
            _unfundedSaidTick = now;

            logger.Warning(
                "{Name} could not pay {Bill}gp: the pack held {Purse} of which {Taken} were consumed, the bank held {Balance} and refused {Rest}",
                buyer.Name,
                bill,
                purse,
                taken,
                Banker.GetBalance(buyer),
                rest
            );
        }

        return false;
    }

    public static void Refund(Mobile who, int amount)
    {
        if (amount <= 0 || who == null || Banker.Deposit(who, amount))
        {
            return;
        }

        Force(who, amount);
    }

    public static void Pay(Mobile seller, int amount)
    {
        if (amount <= 0 || seller == null)
        {
            return;
        }

        var pack = seller.Backpack;

        if (pack != null && amount <= 60000)
        {
            var coin = new Gold(amount);

            if (pack.TryDropItem(seller, coin, false))
            {
                return;
            }

            coin.Delete();
        }

        if (Banker.Deposit(seller, amount))
        {
            return;
        }

        Force(seller, amount);
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

    private static MarketTimer _timer;

    public static bool Started => _timer != null;

    public static void Start()
    {
        if (_timer != null)
        {
            return;
        }

        _startedTick = Core.TickCount;

        Materialize();

        var interval = TimeSpan.FromMilliseconds(Math.Max(1000, BeatMs));

        _timer = new MarketTimer(interval);
        _timer.Start();
    }

    public static void Stop()
    {
        _timer?.Stop();
        _timer = null;
    }

    private static readonly Dictionary<Serial, long> _purses = [];

    private static void Beat()
    {
        var now = Core.TickCount;

        BotAdvance.Sweep(now);

        BotLines.Watched(now);

        _purses.Clear();
        _securedThisBeat.Clear();

        for (var i = 0; i < _wants.Count; i++)
        {
            var want = _wants[i];

            if (want.Buyer?.Self is not { Deleted: false } buyer)
            {
                continue;
            }

            if (want.CeilingPending)
            {
                Recap(want);
            }

            if (want.ShelfPending)
            {
                Shelve(want);
            }

            if (buyer.Alive && _securedThisBeat.Add(buyer.Serial))
            {
                Secure(buyer);
            }

            if (!_purses.TryGetValue(buyer.Serial, out var left))
            {
                left = BotYield.Wealth(buyer);
            }

            var need = Holds(want);

            want.Short = left + BotAdvance.On(want) < want.Offer;

            var uncovered = left < need;

            if (uncovered && !want.Uncovered && buyer.Alive)
            {
                Uncovered++;
            }

            want.Uncovered = uncovered;
            _purses[buyer.Serial] = Math.Max(0L, left - need);
        }

        for (var i = _wants.Count - 1; i >= 0; i--)
        {
            var want = _wants[i];
            var buyer = want.Buyer?.Self;

            if (!Running || buyer == null || buyer.Deleted)
            {
                _wants.RemoveAt(i);
                Touched();
                Orphaned++;
                BotLines.Ended(want, Running ? BotLineEnd.Orphaned : BotLineEnd.Stopped, null);

                continue;
            }

            Swept += want.Sweep();

            if (want.Amount <= 0)
            {
                _wants.RemoveAt(i);
                Touched();
                Met++;
                BotLines.Ended(want, BotLineEnd.Met, null);

                continue;
            }

            if (buyer.Map == null || buyer.Map == Map.Internal)
            {
                if (want.Left(now))
                {
                    KeptAway++;
                }

                BotLines.Aged(want, now);

                continue;
            }

            want.Returned(now);

            BotLines.Aged(want, now);

            if (!buyer.Alive)
            {
                if (want.Fell(now))
                {
                    KeptThroughDeath++;

                    if (!want.Uncovered && _securedBuyers.Contains(buyer.Serial))
                    {
                        SavedByBank++;
                    }
                }

                continue;
            }

            if (want.Rose(now))
            {
                KeptRising++;
            }

            if (want.Rising && now - want.RoseTick < RisenGraceMs)
            {
                continue;
            }

            want.Rising = false;

            if (want.Short)
            {
                var wealth = BotYield.Wealth(buyer);

                _wants.RemoveAt(i);
                Touched();
                Unpaid++;

                if (want.DiedWhileStanding)
                {
                    UnpaidAfterDeath++;
                }

                BotLines.Ended(want, BotLineEnd.Unpaid, want.DiedWhileStanding ? "buyer could not pay after a death" : "buyer could not pay");

                logger.Information(
                    "{Name} stood down its want for {Item}: {Wealth}gp will not pay for one at {Offer}gp (want #{Id}{Death})",
                    buyer.Name,
                    want.Label,
                    wealth,
                    want.Offer,
                    want.Id,
                    want.DiedWhileStanding ? ", after a death" : ""
                );

                continue;
            }

            if (want.Coming > 0 || now - want.TouchedTick < RaiseMs)
            {
                if (want.IsOpen)
                {
                    Say(want, "market:want", false);
                }

                continue;
            }

            var atMaterial = want.Ceiling > 0 && want.Offer >= want.Ceiling;

            if (atMaterial && now - want.PostedTick < MostAgeMs)
            {
                want.Waited();

                if (BotScribes.Writ(want.Kind))
                {
                    BotScribes.Held();
                }
                else
                {
                    CeilingHeld++;
                }

                if (want.IsOpen)
                {
                    Say(want, "market:want", false);
                }

                continue;
            }

            var atShelf = !atMaterial && want.Shelf > 0 && want.Offer >= want.Shelf * (long)Math.Max(1.0, MostMultiple);

            if (atShelf && now - want.PostedTick < MostAgeMs)
            {
                want.Waited();
                ShelfHeld++;

                if (want.IsOpen)
                {
                    Say(want, "market:want", false);
                }

                continue;
            }

            var stepped = want.Stepped(RaiseStep, MostMultiple);

            if (stepped > 0 && BotYield.Spendable(buyer) >= (long)want.Amount * (stepped - want.Offer))
            {
                want.Lift(stepped);
                Raises++;

                logger.Information(
                    "{Name} raised its offer for {Item} to {Offer}gp after {Amount} went unanswered",
                    buyer.Name,
                    want.Label,
                    want.Offer,
                    want.Amount
                );

                Say(want, "market:raise", true);

                continue;
            }

            if (stepped > 0 && now - want.PostedTick < MostAgeMs)
            {
                want.Waited();
                Held++;

                if (want.IsOpen)
                {
                    Say(want, "market:want", false);
                }

                continue;
            }

            _wants.RemoveAt(i);
            Touched();
            Abandoned++;

            if (stepped > 0)
            {
                AgedOut++;
            }

            BotLines.Ended(
                want,
                BotLineEnd.Abandoned,
                stepped > 0 ? "could not reserve a louder price, given up"
                : atMaterial ? BotScribes.Writ(want.Kind) ? "given up at the price of writing it" : "given up at its material's ceiling"
                : atShelf ? "given up at four times a shopkeeper's price"
                : "given up at its ceiling"
            );

            GaveUp(want);

            logger.Information(
                "{Name} gave up wanting {Item} at {Offer}gp of a possible {Ceiling}",
                buyer.Name,
                want.Label,
                want.Offer,
                want.Highest(MostMultiple)
            );
        }
    }

    public static string Board(int most = 6)
    {
        Dictionary<Type, int> wanted = [];
        Dictionary<Type, int> shown = [];

        for (var i = 0; i < _wants.Count; i++)
        {
            var want = _wants[i];

            if (want?.Kind != null && want.Amount > 0)
            {
                wanted[want.Kind] = wanted.GetValueOrDefault(want.Kind) + want.Amount;
            }
        }

        var open = BotShopkeep.Open;

        for (var i = 0; i < open.Count; i++)
        {
            var front = open[i];

            if (front.Shut)
            {
                continue;
            }

            foreach (var kind in front.Goods)
            {
                shown[kind] = shown.GetValueOrDefault(kind) + front.Held(kind);
            }
        }

        return $"most wanted: {Top(wanted, most)}; on show at the shops: {Top(shown, most)}";
    }

    private static string Top(Dictionary<Type, int> tally, int most)
    {
        if (tally.Count == 0)
        {
            return "nothing";
        }

        List<(Type Kind, int Amount)> ordered = [];

        foreach (var (kind, amount) in tally)
        {
            ordered.Add((kind, amount));
        }

        ordered.Sort(static (a, b) => b.Amount.CompareTo(a.Amount));

        using var say = ValueStringBuilder.Create(160);

        for (var i = 0; i < ordered.Count && i < most; i++)
        {
            if (i > 0)
            {
                say.Append(", ");
            }

            say.Append(ordered[i].Kind.Name);
            say.Append(' ');
            say.Append(ordered[i].Amount);
        }

        return say.ToString();
    }

    public static string Describe()
    {
        var (units, worth) = Sought();

        return $"{_wants.Count} of {MaxWants} wants for {units} things worth {worth}gp ({Coming()} of them being carried now); "
               + $"{Posted} wants said, {Topped} asked for more, {Said} said aloud in the trade chat, {Met} met to the last unit, {Abandoned} given up at their ceiling, {Unpaid} stood down when the buyer could no longer pay for one, {Unfunded} refused at the asking for want of money, {Retracted} taken back by their buyer, {Orphaned} gone with their buyer; {Raises} offers raised, {Cuts} cut after being answered again soon; "
               + $"{Answered} things handed over against wants for {AnsweredGold}gp; "
               + $"hand to hand: {BotHandover.Describe()}; "
               + $"carriers: {BotCourier.Describe()}; deliveries: {BotDeliver.Describe()}; {BotRoom.Describe()}; {BotWayless.Describe()}; {BotUnreached.Describe()}; {BotRendezvous.Describe()}; "
               + $"{BoxTaken} things taken out of bank boxes by their owners at a bank; {Swept} reservations of carriers swept; "
               + $"materials at most {MaterialShare:F0} times what they become: {CappedAsks} wants opened at their ceiling instead of above it ({CappedGold}gp less asked), {CeilingHeld} raises not taken at the ceiling (the want waited at its price), {Unsourced} wants not said for nothing selling or making the material under it, {BotTrading.OverCeiling} traders' sources passed over for costing more than the want's ceiling; ceilings now: {DescribeCeilings()}; "
               + $"{DescribeOpenings()}; "
               + $"reserve: {DescribeReserve()}; "
               + $"prices learned from goods that changed hands for {_dealt.Count} kinds; {Cheap} things were weighed under the {Floor}gp floor ({Unpriced} of them priced by nothing, which condemns no kind), and the condemned kinds are {Condemned(8)}";
    }

    private static (long Gold, int Buyers) HeldNow()
    {
        if (_wants.Count == 0)
        {
            return (0, 0);
        }

        Refresh();

        long gold = 0;

        foreach (var (_, held) in _reserved)
        {
            gold += held;
        }

        return (gold, _reserved.Count);
    }

    private static string DescribeReserve()
    {
        var (gold, buyers) = HeldNow();

        return $"{gold}gp held by {buyers} buyers for their wants; {Secured} deposits put {SecuredGold}gp of it from pockets into accounts{(SecuresReserve ? "" : " (off)")}, {SavedByBank} wants still covered by the account when their buyer fell; {Unreserved} wants not said for the money the buyer's other wants already held ({UnreservedGold}gp asked); "
               + $"{HeldBack} charges refused for reaching into held money ({HeldBackGold}gp); {Uncovered} wants found by the beat no longer covered by their buyer's purse; "
               + $"{KeptThroughDeath} wants kept through their buyer's death and {KeptRising} through its first {RisenGraceMs / 60000} minutes back, {UnpaidAfterDeath} of the stand-downs after a death, {KeptAway} kept while their buyer rested out of the world; "
               + $"{Held} raises the buyer could not reserve, the want waiting at its price, {AgedOut} given up at {MostAgeMs / 60000} minutes for it; "
               + $"taken back: {_retractedFor[(int)BotWithdraw.Have]} had it another way, {_retractedFor[(int)BotWithdraw.Walked]} walked to a counter, {_retractedFor[(int)BotWithdraw.Unpaid]} could not pay at the meeting, {_retractedFor[(int)BotWithdraw.Closed]} gatherings closed, {_retractedFor[(int)BotWithdraw.Other]} other; "
               + $"the board kept across restarts: {RestoredWants} wants and {RestoredLines} lines read back, {DroppedWants} dropped (buyer gone or kind unknown), {Recapped} given their material's ceiling on the beat and {BroughtDown} of them brought down to it ({BroughtDownGold}gp less held); "
               + $"carriage over the offer agreed with traders and held now: {Carriage()}gp";
    }

    private static long Carriage()
    {
        long gold = 0;

        for (var i = 0; i < _wants.Count; i++)
        {
            gold += _wants[i].Premium;
        }

        return gold;
    }

    internal static void WebReserve(System.Text.Json.Utf8JsonWriter w)
    {
        var (gold, buyers) = HeldNow();

        w.WritePropertyName("reserve");
        w.WriteStartObject();
        w.WriteNumber("gold", gold);
        w.WriteNumber("buyers", buyers);
        w.WriteNumber("wants", _wants.Count);
        w.WriteNumber("unreserved", Unreserved);
        w.WriteNumber("unfunded", Unfunded);
        w.WriteNumber("chargesRefused", HeldBack);
        w.WriteNumber("uncovered", Uncovered);
        w.WriteNumber("unpaid", Unpaid);
        w.WriteNumber("unpaidAfterDeath", UnpaidAfterDeath);
        w.WriteNumber("keptThroughDeath", KeptThroughDeath);
        w.WriteNumber("raisesHeld", Held);
        w.WriteNumber("restoredWants", RestoredWants);
        w.WriteNumber("secured", Secured);
        w.WriteNumber("securedGold", SecuredGold);
        w.WriteNumber("savedByBank", SavedByBank);
        w.WriteNumber("recapped", Recapped);
        w.WriteNumber("broughtDown", BroughtDown);
        w.WriteNumber("carriageHeld", Carriage());
        w.WriteNumber("cutToFree", BotHandover.CutToFree);
        w.WriteEndObject();
    }

    public static int RestoredWants { get; private set; }

    public static int RestoredLines { get; private set; }

    public static int DroppedWants { get; private set; }

    /// <summary>One want as the save holds it. Made into a <see cref="BotWant"/> at <see cref="Start"/>, once the world is in memory.</summary>
    private sealed class Saved
    {
        public int Id;
        public BotMobile Buyer;
        public Type Kind;
        public int Amount;
        public int Offer;
        public int Anchor;
        public long PostedAge;
        public long TouchedAge;
        public int Filled;
        public int Paid;
        public int Raises;
        public int Cuts;
        public bool Traded;
        public long FilledAge;
        public BotLine.Record[] Lines;
    }

    private static readonly List<Saved> _pending = [];

    internal static void Save(IGenericWriter writer)
    {
        var now = Core.TickCount;
        var count = 0;

        for (var i = 0; i < _wants.Count; i++)
        {
            if (Savable(_wants[i]))
            {
                count++;
            }
        }

        writer.WriteEncodedInt(_nextWantId);
        writer.WriteEncodedInt(count);

        for (var i = 0; i < _wants.Count; i++)
        {
            var want = _wants[i];

            if (!Savable(want))
            {
                continue;
            }

            writer.WriteEncodedInt(want.Id);
            writer.Write(want.Buyer.Self.Serial);
            writer.Write(want.Kind.FullName ?? want.Kind.Name);
            writer.WriteEncodedInt(want.Amount);
            writer.WriteEncodedInt(want.Offer);
            writer.WriteEncodedInt(want.Anchor);
            writer.Write(Math.Max(0L, now - want.PostedTick));
            writer.Write(Math.Max(0L, now - want.TouchedTick));
            writer.WriteEncodedInt(want.Filled);
            writer.WriteEncodedInt(want.Paid);
            writer.WriteEncodedInt(want.Raises);
            writer.WriteEncodedInt(want.Cuts);
            writer.Write(want.Traded);
            writer.Write(want.Traded ? Math.Max(0L, now - want.FilledTick) : 0L);

            var lines = want.Lines;

            writer.WriteEncodedInt(lines.Count);

            for (var j = 0; j < lines.Count; j++)
            {
                lines[j].Save(writer, now);
            }
        }
    }

    private static bool Savable(BotWant want) => want?.Kind != null && want.Amount > 0 && want.Buyer?.Self is BotMobile { Deleted: false };

    internal static (int Kept, int Dropped) Load(IGenericReader reader)
    {
        _pending.Clear();

        var next = reader.ReadEncodedInt();
        var count = reader.ReadEncodedInt();
        var dropped = 0;

        for (var i = 0; i < count; i++)
        {
            var saved = new Saved
            {
                Id = reader.ReadEncodedInt(),
                Buyer = reader.ReadEntity<BotMobile>()
            };

            var name = reader.ReadString();

            saved.Kind = string.IsNullOrEmpty(name) ? null : AssemblyHandler.FindTypeByName(name, true) ?? AssemblyHandler.FindTypeByName(name);
            saved.Amount = reader.ReadEncodedInt();
            saved.Offer = reader.ReadEncodedInt();
            saved.Anchor = reader.ReadEncodedInt();
            saved.PostedAge = reader.ReadLong();
            saved.TouchedAge = reader.ReadLong();
            saved.Filled = reader.ReadEncodedInt();
            saved.Paid = reader.ReadEncodedInt();
            saved.Raises = reader.ReadEncodedInt();
            saved.Cuts = reader.ReadEncodedInt();
            saved.Traded = reader.ReadBool();
            saved.FilledAge = reader.ReadLong();

            var lines = reader.ReadEncodedInt();

            saved.Lines = new BotLine.Record[Math.Max(0, lines)];

            for (var j = 0; j < saved.Lines.Length; j++)
            {
                saved.Lines[j] = BotLine.Read(reader);
            }

            if (saved.Buyer is not { Deleted: false } || saved.Kind == null || saved.Amount <= 0)
            {
                dropped++;

                continue;
            }

            _pending.Add(saved);
        }

        _nextWantId = Math.Max(_nextWantId, next);
        DroppedWants = dropped;

        return (_pending.Count, dropped);
    }

    private static void Materialize()
    {
        if (_pending.Count == 0)
        {
            return;
        }

        var now = Core.TickCount;
        var wants = 0;
        var lines = 0;

        for (var i = 0; i < _pending.Count; i++)
        {
            var saved = _pending[i];

            if (saved.Buyer is not { Deleted: false } buyer || _wants.Count >= MaxWants || Wanted(buyer, saved.Kind) != null)
            {
                DroppedWants++;

                continue;
            }

            var want = new BotWant(
                saved.Id,
                buyer,
                saved.Kind,
                saved.Amount,
                saved.Offer,
                saved.Anchor,
                now - Math.Max(0L, saved.PostedAge),
                now - Math.Max(0L, saved.TouchedAge)
            );

            want.Ceiling = MaterialCap(saved.Kind);
            want.CeilingPending = true;

            want.ShelfPending = true;

            want.Restore(saved.Filled, saved.Paid, saved.Raises, saved.Cuts, saved.Traded, now - Math.Max(0L, saved.FilledAge));

            for (var j = 0; j < saved.Lines.Length; j++)
            {
                want.AddLine(BotLine.Restore(want, saved.Lines[j], now));
                lines++;
            }

            if (want.Lines.Count == 0)
            {
                var line = want.OpenLine(want.Amount, want.PostedTick, BotTrading.Trader(buyer));

                line.Restored = true;
                lines++;
            }

            _wants.Add(want);
            wants++;
        }

        _pending.Clear();
        Touched();

        var open = 0;

        for (var i = 0; i < _wants.Count; i++)
        {
            var standing = _wants[i].Lines;

            for (var j = 0; j < standing.Count; j++)
            {
                if (standing[j].State == BotLineState.Open)
                {
                    open++;
                }
            }
        }

        RestoredWants = wants;
        RestoredLines = lines;
        BotLines.Carried(open);

        logger.Information(
            "The board was read back from the save: {Wants} wants with {Lines} lines ({Open} still inside their hour), {Dropped} dropped for a buyer or a kind that is gone; their clocks go on from where they stood",
            wants,
            lines,
            open,
            DroppedWants
        );
    }

    public static void Reset()
    {
        Stop();

        _wants.Clear();
        _worthless.Clear();
        _dealt.Clear();
        _bespoke.Clear();
        _bespokeRead = false;
        _squares.Clear();
        _atBank.Clear();
        _nextWantId = 0;
        _pending.Clear();
        _reserved.Clear();
        _reservedStale = true;
        _purses.Clear();
        Array.Clear(_retractedFor);

        Cheap = 0;
        Unpriced = 0;
        Posted = 0;
        Topped = 0;
        Met = 0;
        Abandoned = 0;
        Unfunded = 0;
        Unpaid = 0;
        Orphaned = 0;
        Retracted = 0;
        Raises = 0;
        Cuts = 0;
        Answered = 0;
        AnsweredGold = 0;
        Said = 0;
        Swept = 0;
        BoxTaken = 0;
        Unreserved = 0;
        UnreservedGold = 0;
        HeldBack = 0;
        HeldBackGold = 0;
        Uncovered = 0;
        KeptThroughDeath = 0;
        KeptRising = 0;
        KeptAway = 0;
        UnpaidAfterDeath = 0;
        Held = 0;
        AgedOut = 0;
        RestoredWants = 0;
        RestoredLines = 0;
        DroppedWants = 0;
        _ceilings.Clear();
        _securedBuyers.Clear();
        _securedThisBeat.Clear();
        Secured = 0;
        SecuredGold = 0;
        SavedByBank = 0;
        CappedAsks = 0;
        CappedGold = 0;
        CeilingHeld = 0;
        Unsourced = 0;
        Recapped = 0;
        BroughtDown = 0;
        BroughtDownGold = 0;
        _brought.Clear();
        _pace.Clear();
        _gaveUp.Clear();
        UnderDearer = 0;
        UnderDearerGold = 0;
        ShelfCapped = 0;
        ShelfCappedGold = 0;
        ShelfKnown = 0;
        Reasked = 0;
        ReaskCapped = 0;
        ReaskCappedGold = 0;
        Shelved = 0;
        ShelvedDown = 0;
        ShelvedDownGold = 0;
        ShelfHeld = 0;

        BotLines.Forget();

        BotHandover.Forget();
        BotCourier.Forget();
        BotDeliver.Forget();
        BotUnreached.Forget();
        BotRoom.Forget();
        BotWayless.Forget();
        BotGlut.Reset();
    }

    private sealed class MarketTimer : Timer
    {
        public MarketTimer(TimeSpan interval) : base(interval, interval)
        {
        }

        protected override void OnTick() => Beat();
    }
}
