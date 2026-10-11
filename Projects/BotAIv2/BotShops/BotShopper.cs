using System;
using System.Collections.Generic;
using Server.Engines.Craft;
using Server.Items;
using Server.Logging;

namespace Server.BotAI.V2;

/// <summary>
/// Offers a trip to the shops to any bot that has run out of something its class needs.
///
/// <para>
/// <b>Every bot, not a trade.</b> A caster with no reagents is not a caster, a bot with no bandages cannot
/// mend itself, and both facts are true of whoever happens to be standing there. What each one needs comes
/// from its own class's kit — the same list the world handed it at birth — so this needs no table of who
/// buys what and gains a class the moment one is added.
/// </para>
///
/// <para>
/// Supplies are deliberately not bound: they are meant to run out. That is what makes a shop worth walking
/// to, and later what makes another bot's production worth buying.
/// </para>
/// </summary>
public sealed class BotShopper : IBotProposer
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotShopper));

    public static double Short { get; set; } = 0.5;

    public static bool CutsBandages { get; set; } = true;

    public static long AsCloth { get; private set; }

    public static long ScissorsFirst { get; private set; }

    private static bool _cutting;

    private static readonly Type[] Reagents =
    [
        typeof(SulfurousAsh), typeof(BlackPearl), typeof(Garlic), typeof(Ginseng),
        typeof(SpidersSilk), typeof(Nightshade), typeof(Bloodmoss), typeof(MandrakeRoot)
    ];

    public static int Guess { get; set; } = 5;

    private static int Stand(Type wanted) => Math.Max(Guess, BotShops.Lowest(wanted));

    public static int Reserve
    {
        get => _reserve ?? BotPurse.KeepBack;
        set => _reserve = value;
    }

    private static int? _reserve;

    public static long Asked { get; private set; }

    private static bool _saidNoShops;

    public string Name => "Shopper";

    public BotStanding Rung => BotStanding.Free;

    public static long Looks { get; private set; }

    public static long Stocked { get; private set; }

    public static long ToCounter { get; private set; }

    public static long Spares { get; private set; }

    public static long ToHall { get; private set; }

    public static long HallWalled { get; private set; }

    public static long ToBoard { get; private set; }

    public static long Broke { get; private set; }

    public static long Unboarded { get; private set; }

    public static long Richest { get; private set; }

    private static readonly System.Collections.Generic.Dictionary<Type, long> _short = [];

    public static int ForgetMs { get; set; } = 900000;

    private static long _forgot;

    private static bool _everForgot;

    private static void Fade()
    {
        var now = Core.TickCount;

        if (!_everForgot)
        {
            _everForgot = true;
            _forgot = now;

            return;
        }

        if (now - _forgot < ForgetMs)
        {
            return;
        }

        _forgot = now;

        List<Type> gone = null;

        foreach (var (kind, times) in _short)
        {
            var left = times / 2;

            if (left <= 0)
            {
                (gone ??= []).Add(kind);
            }
            else
            {
                _short[kind] = left;
            }
        }

        if (gone == null)
        {
            return;
        }

        for (var i = 0; i < gone.Count; i++)
        {
            _short.Remove(gone[i]);
        }
    }

    private static bool Trade(BotClass klass, Type tool)
    {
        var own = klass.Kit.Tools;

        for (var i = 0; i < own.Count; i++)
        {
            if (own[i] == tool)
            {
                return true;
            }
        }

        return false;
    }

    public static long Provisioned { get; private set; }

    public static long FromOwnBox { get; private set; }

    public BotDeed Propose(IBotWilful bot)
    {
        var body = bot?.Self;
        var map = body?.Map;
        var klass = bot?.Class;
        var pack = body?.Backpack;

        if (map == null || map == Map.Internal || klass == null || pack == null)
        {
            return null;
        }

        BotShops.Survey(map, body.Location);

        Looks++;

        BotAskFirst.Review(bot, body, klass, pack);

        if (!Wanting(bot, klass, pack, out var wanted, out var amount, out var band, out var spare))
        {
            if (BotAskFirst.ParkedThisLook)
            {
                BotAskFirst.OnlyWaited();
            }
            else
            {
                Stocked++;
            }

            return null;
        }

        var own = 0;

        if (!band && body.FindBankNoCreate() is { Items.Count: > 0 }
            && BotUnload.Keeps(bot).TryGetValue(wanted, out var kept) && kept > 0)
        {
            own = BotMarket.FromBank(bot, wanted, Math.Min(amount, kept - pack.GetAmount(wanted)));
        }

        if (own > 0)
        {
            FromOwnBox += own;

            return null;
        }

        var needy = BotProvision.Consumable(wanted) && BotProvision.Short(body);

        if (needy)
        {
            spare = true;
            Provisioned++;
        }

        var askedKind = wanted;

        _cutting = false;

        if (wanted == typeof(Bandage) && CutsBandages && ClothCheaper(bot, amount))
        {
            if (BotOutfit.Oldest<Scissors>(pack) != null)
            {
                wanted = typeof(Cloth);
                _cutting = true;
                AsCloth++;
            }
            else if (BotShops.Nearest(bot, typeof(Scissors)) != null)
            {
                wanted = typeof(Scissors);
                amount = 1;
                spare = true;
                ScissorsFirst++;
            }
        }

        if (!BotRoom.Receives(body, wanted, true))
        {
            BotRoom.Note(body);
            BotRoom.RestockNotOffered();

            return null;
        }

        Fade();

        _short.TryGetValue(wanted, out var seen);
        _short[wanted] = seen + 1;

        var shop = BotShops.Nearest(bot, wanted);
        var counter = shop == null ? 0 : BotBurgh.Dear(body, shop, BotShops.Price(shop, wanted));

        var need = _cutting ? askedKind : wanted;

        if (BotAskFirst.Parked(body.Serial, need))
        {
            return null;
        }

        var front = BotShopkeep.Beats(bot, wanted, amount, shop, counter, out var frontUnit, out var frontUnits);

        if (front != null)
        {
            frontUnits = ByPurse(body, askedKind, wanted, frontUnits, frontUnit, needy, band, pack);

            if (frontUnits <= 0)
            {
                return null;
            }

            if (needy)
            {
                BotProvision.Fund(body, frontUnit * frontUnits);
            }

            return Ordered(new BotRestock(front, wanted, frontUnits, frontUnit), band, spare);
        }

        var merchant = BotShelf.Of(body);

        if (merchant != null
            && (bot.Resolve?.Ledger?.Cautious(BotShops.ShopKind, merchant.Map, merchant.Location) == true
                || BotReach.Ask(map, body.Location, merchant.Location, BotArrival.Within(BotShelf.Reach)) == BotReachVerdict.Sealed))
        {
            HallWalled++;
            merchant = null;
        }

        var lotPrice = 0;
        var ours = merchant == null ? null : BotShelf.Offer(merchant, wanted, out lotPrice);

        if (ours != null)
        {
            var unit = (int)Math.Ceiling(lotPrice / (double)Math.Max(1, ours.Amount));

            if ((counter <= 0 || unit <= counter) && BotYield.Spendable(body) >= lotPrice)
            {
                ToHall++;

                return Ordered(new BotRestock(merchant, wanted, Math.Max(1, ours.Amount), unit), band, spare);
            }
        }

        if (PileMs > 0 && !band && Patient(wanted) && BotProvision.Consumable(wanted) && BotGlut.Glutted(wanted)
            && pack.GetAmount(wanted) > 0 && !BotAskFirst.Lately(body.Serial, need))
        {
            var key = (body.Serial, wanted);
            var now = Core.TickCount;
            var offer = counter > 0 ? counter : BotMarket.Worth(wanted, Stand(wanted));

            if (_askedPile.TryGetValue(key, out var at) && now - at < PileMs * 3L)
            {
                if (now - at < PileMs && BotMarket.Wanted(bot, wanted) != null)
                {
                    PileWaited++;

                    return null;
                }

                if (now - at >= PileMs && BotMarket.Wanted(bot, wanted) is { } standing && !OwnAsk(standing, at))
                {
                    _askedPile[key] = standing.PostedTick;
                    ForeignKept++;

                    return null;
                }

                if (now - at >= PileMs && BotMarket.Withdrawn(bot, wanted, BotWithdraw.Walked) > 0)
                {
                    PileWalked++;
                }
            }
            else if (BotMarket.Wanted(bot, wanted) != null)
            {
                if (_askedPile.Count > 4096)
                {
                    _askedPile.Clear();
                }

                _askedPile[key] = now;
                PileWaited++;

                return null;
            }
            else if (BotYield.Spendable(body) - offer * amount > 0)
            {
                var order = BotOrder.For(map, body.Location, bot, wanted, offer, amount);

                if (order != null)
                {
                    if (_askedPile.Count > 4096)
                    {
                        _askedPile.Clear();
                    }

                    _askedPile[key] = now;
                    Piled++;

                    return order;
                }
            }
        }

        if (counter <= 0)
        {
            if (BotAskFirst.Asks(bot, body, klass, pack, need, wanted, amount, BotAskFirst.Price(wanted), band, _cutting, needy, Owned(body.Serial, wanted, need), true))
            {
                return null;
            }

            Missing(wanted, map, body);

            var order = Board(bot, body, map, wanted, amount);

            if (order != null)
            {
                ToBoard++;
            }
            else
            {
                var wealth = BotYield.Spendable(body);

                if (wealth - BotMarket.Worth(wanted, Stand(wanted)) * amount <= Reserve)
                {
                    Broke++;

                    if (wealth > Richest)
                    {
                        Richest = wealth;
                    }
                }
                else
                {
                    Unboarded++;
                }
            }

            return order;
        }

        if (AskFarMinutes > 0.0 && !needy && !band && Patient(wanted) && Makeable(wanted)
            && BotAppraisal.Travel(body.Location, shop.Location) >= AskFarMinutes)
        {
            var key = (body.Serial, wanted);
            var now = Core.TickCount;
            var asked = _askedFar.TryGetValue(key, out var at);

            if (asked && BotMarket.Wanted(bot, wanted) is { Coming: > 0 })
            {
                WaitedFar++;

                return null;
            }

            if (!asked && BotMarket.Wanted(bot, wanted) != null)
            {
                if (_askedFar.Count > 4096)
                {
                    _askedFar.Clear();
                }

                _askedFar[key] = now;
                WaitedFar++;

                return null;
            }

            if (!asked || now - at >= AskFarMs * 3L)
            {
                var order = BotOrder.For(map, body.Location, bot, wanted, counter, amount);

                if (order != null && BotYield.Spendable(body) - counter * amount > Reserve)
                {
                    if (_askedFar.Count > 4096)
                    {
                        _askedFar.Clear();
                    }

                    _askedFar[key] = now;
                    AskedFar++;

                    return order;
                }
            }
            else if (now - at < AskFarMs && BotMarket.Wanted(bot, wanted) != null)
            {
                WaitedFar++;

                return null;
            }
            else if (BotMarket.Wanted(bot, wanted) is { } standing && !OwnAsk(standing, at))
            {
                _askedFar[key] = standing.PostedTick;
                ForeignKept++;

                return null;
            }
            else if (BotMarket.Withdrawn(bot, wanted, BotWithdraw.Walked) > 0)
            {
                WalkedFar++;
            }
        }

        if (BotAskFirst.Asks(bot, body, klass, pack, need, wanted, amount, counter, band, _cutting, needy, Owned(body.Serial, wanted, need), false))
        {
            return null;
        }

        amount = ByPurse(body, askedKind, wanted, amount, counter, needy, band, pack);

        if (amount <= 0)
        {
            return null;
        }

        ToCounter++;

        if (needy)
        {
            BotProvision.Fund(body, counter * amount);
        }

        return Ordered(new BotRestock(shop, wanted, amount, counter), band, spare);
    }

    public static bool PurseSized { get; set; } = true;

    public static long Trimmed { get; private set; }

    public static long TrimmedUnits { get; private set; }

    public static long KeptBack { get; private set; }

    private static int ByPurse(Mobile body, Type asked, Type buying, int amount, int unit, bool needy, bool band, Container pack)
    {
        if (!PurseSized || band || amount <= 0 || unit <= 0 || body == null || !BotProvision.Consumable(asked)
            || buying != asked && buying != typeof(Cloth))
        {
            return amount;
        }

        var held = pack?.GetAmount(asked) ?? 0;
        var must = Math.Clamp(BotProvision.Floor(body, asked) - held, 0, amount);
        var free = Math.Max(0, BotYield.Spendable(body));

        var door = Math.Clamp(BotProvision.DoorFloor(body, asked) - held, 0, must);
        var mustUnits = Math.Min(must, free / unit);

        if (mustUnits < door && needy && BotProvision.Fund(body, door * unit))
        {
            free = Math.Max(0, BotYield.Spendable(body));
            mustUnits = Math.Min(must, Math.Max(door, free / unit));
        }

        var left = Math.Max(0, free - mustUnits * unit);
        var extraUnits = Math.Min(amount - must, Math.Max(0, left - Reserve) / unit);
        var units = mustUnits + extraUnits;

        if (units >= amount)
        {
            return amount;
        }

        if (units <= 0)
        {
            KeptBack++;

            return 0;
        }

        Trimmed++;
        TrimmedUnits += amount - units;

        return units;
    }

    public static double AskFarMinutes { get; set; } = 4.0;

    public static int AskFarMs { get; set; } = 600000;

    public static long AskedFar { get; private set; }

    public static long WaitedFar { get; private set; }

    public static long WalkedFar { get; private set; }

    private static readonly Dictionary<(Serial, Type), long> _askedFar = [];

    public static int PileMs { get; set; } = 180000;

    public static long Piled { get; private set; }

    public static long PileWaited { get; private set; }

    public static long PileWalked { get; private set; }

    public static long ForeignKept { get; private set; }

    private static bool OwnAsk(BotWant want, long at) => want.PostedTick - at <= 1000;

    private static readonly Dictionary<(Serial, Type), long> _askedPile = [];

    private static bool Owned(Serial who, Type wanted, Type need)
    {
        var now = Core.TickCount;

        return Clocked(_askedPile, (who, wanted), now, PileMs * 3L) || Clocked(_askedFar, (who, wanted), now, AskFarMs * 3L)
               || need != wanted && (Clocked(_askedPile, (who, need), now, PileMs * 3L) || Clocked(_askedFar, (who, need), now, AskFarMs * 3L));
    }

    private static bool Clocked(Dictionary<(Serial, Type), long> clock, (Serial, Type) key, long now, long span) =>
        clock.TryGetValue(key, out var at) && now - at < span;

    private static bool Patient(Type wanted) =>
        wanted != null && !typeof(BaseWeapon).IsAssignableFrom(wanted) && !typeof(BaseShield).IsAssignableFrom(wanted)
        && wanted != typeof(Arrow) && wanted != typeof(Bolt);

    private static bool ClothCheaper(IBotWilful bot, int amount)
    {
        var body = bot?.Self;
        var cloth = BotShops.Nearest(bot, typeof(Cloth));

        if (body == null || cloth == null)
        {
            return false;
        }

        var clothPrice = BotShops.Price(cloth, typeof(Cloth));

        if (clothPrice <= 0)
        {
            return false;
        }

        var bandages = BotShops.Nearest(bot, typeof(Bandage));
        var bandagePrice = bandages == null ? 0 : BotShops.Price(bandages, typeof(Bandage));

        if (bandagePrice <= 0)
        {
            return true;
        }

        var n = Math.Max(1, amount);
        var walk = BotShopkeep.WalkGold;
        var clothLanded = clothPrice + 2.0 * BotAppraisal.Travel(body.Location, cloth.Location) * walk / n;
        var bandageLanded = bandagePrice + 2.0 * BotAppraisal.Travel(body.Location, bandages.Location) * walk / n;

        if (clothLanded < bandageLanded)
        {
            return true;
        }

        if (clothPrice < bandagePrice)
        {
            ClothFarther++;
        }

        return false;
    }

    public static long ClothFarther { get; private set; }

    private static BotDeed Ordered(BotRestock errand, bool band, bool spare)
    {
        if (errand == null)
        {
            return null;
        }

        errand.Cuts = _cutting;
        _cutting = false;

        if (band)
        {
            errand.Claim = BotFence.Prior;
            BotFence.Sending();

            return errand;
        }

        if (spare)
        {
            errand.Claim = BotRestock.Spare;
            Spares++;
        }

        return errand;
    }

    public static List<(Type Kind, long Times)> Shortages()
    {
        Fade();

        List<(Type Kind, long Times)> found = [];

        foreach (var (kind, times) in _short)
        {
            found.Add((kind, times));
        }

        found.Sort((a, b) => b.Times.CompareTo(a.Times));

        return found;
    }

    private static string Commonest()
    {
        Type worst = null;
        long most = 0;

        foreach (var (kind, times) in _short)
        {
            if (times > most)
            {
                most = times;
                worst = kind;
            }
        }

        return worst == null ? "nothing" : $"{worst.Name} ({most} times)";
    }

    public static string Describe() =>
        Looks == 0
            ? "nobody has been looked at for supplies"
            : $"{Looks} looks for supplies: {Stocked} were short of nothing, {FromOwnBox} things taken out of the bot's own bank box at a bank instead of bought, {ToCounter} sent to a shopkeeper, "
              + $"{AskedFar} asked the island aloud instead of walking to a counter {AskFarMinutes:F0} minutes or more away ({WaitedFar} looks waited for the carrier, {WalkedFar} walked after all), {Piled} asked the island for a piled consumable before any counter ({PileWaited} looks waited for the carrier, {PileWalked} walked after nobody came; {ForeignKept} times either clock found a want another path had said since its ask and waited for it rather than taking it back), {BotShopkeep.ToFront} to a bot's shop by the bank, {ToHall} to their own guild's counter ({HallWalled} times it was passed over as out of reach), {ToBoard} put an order on the board, {Unmakeable} were left off it because nothing on this shard makes the thing, "
              + $"{Spares} offers were for a tool bought again before the last gave out, {BotRestock.ToolsBound} working tools bound as they were bought, {AsCloth} bandage trips sent for cloth to cut ({BotRestock.CutInto} bandages cut at the counter), {ClothFarther} kept to bandages for the cloth counter being the dearer by the road, and {ScissorsFirst} for scissors first, "
              + $"{Broke} wanted something nobody sells and could not afford one made (the fattest purse among them held {Richest}gp); "
              + $"by the purse{(PurseSized ? "" : " (off)")}: {Trimmed} restocks bought smaller than asked ({TrimmedUnits} units left unbought: the threshold whatever the purse, the rest only above the {Reserve}gp keep), {KeptBack} not offered for the keep; "
              + $"most often short of {Commonest()} lately";

    public static void ForgetCounts()
    {
        Trimmed = 0;
        TrimmedUnits = 0;
        KeptBack = 0;
        Looks = 0;
        Stocked = 0;
        FromOwnBox = 0;
        ToCounter = 0;
        AskedFar = 0;
        WaitedFar = 0;
        ClothFarther = 0;
        WalkedFar = 0;
        _askedFar.Clear();
        Piled = 0;
        PileWaited = 0;
        PileWalked = 0;
        ForeignKept = 0;
        _askedPile.Clear();
        Spares = 0;
        ToHall = 0;
        ToBoard = 0;
        Unmakeable = 0;
        Broke = 0;
        Richest = 0;
        _short.Clear();
        _everForgot = false;
    }

    private static bool Wanting(
        IBotWilful bot, BotClass klass, Container pack, out Type wanted, out int amount, out bool band, out bool spare
    )
    {
        band = false;
        spare = false;
        var kit = klass.Kit;
        var tools = BotOutfit.ToolsFor(klass);
        var body = bot.Self;
        var bond = bot.Bond;

        var rolled = bond?.Weapon;

        if (rolled?.Weapon != null && pack.GetAmount(rolled.Value.Weapon) <= 0 && !Held(body, rolled.Value.Weapon))
        {
            wanted = rolled.Value.Weapon;
            amount = 1;

            return true;
        }

        if (rolled?.Ammunition != null && bond != null)
        {
            var quiver = rolled.Value.Ammunition;
            var granted = BotBinding.BoundCount(quiver, bond);

            if (granted > 0 && Lacking(pack, quiver, granted, out amount) && !BotAskFirst.Parked(body.Serial, quiver))
            {
                wanted = quiver;

                return true;
            }
        }

        if (Lacking(pack, typeof(Bandage), kit.Bandages, out amount) && !BotAskFirst.Parked(body.Serial, typeof(Bandage)))
        {
            wanted = typeof(Bandage);

            return true;
        }

        var bottles = BotOutfit.PotionsFor(klass);

        for (var i = 0; i < bottles.Count; i++)
        {
            var (kind, count) = bottles[i];
            var held = pack.GetAmount(kind);

            if (held >= count)
            {
                continue;
            }

            wanted = kind;
            amount = count - held;

            return true;
        }

        for (var i = 0; i < tools.Count; i++)
        {
            if (Kept(body, pack, tools[i], out var spent) || BotAskFirst.Parked(body.Serial, tools[i]))
            {
                continue;
            }

            wanted = tools[i];
            amount = 1;

            spare = spent || Trade(klass, tools[i]) || tools[i] == typeof(Scissors) && Trade(klass, typeof(SewingKit));

            return true;
        }

        if (kit.Reagents > 0)
        {
            for (var i = 0; i < Reagents.Length; i++)
            {
                if (BotProvision.Floor(body, Reagents[i]) <= pack.GetAmount(Reagents[i])
                    || !Lacking(pack, Reagents[i], kit.Reagents, out amount) || BotAskFirst.Parked(body.Serial, Reagents[i]))
                {
                    continue;
                }

                wanted = Reagents[i];

                return true;
            }

            for (var i = 0; BotYield.Spendable(body) > Reserve && i < Reagents.Length; i++)
            {
                if (BotYield.Spendable(body) <= Reserve + Stand(Reagents[i])
                    || !Lacking(pack, Reagents[i], kit.Reagents, out amount) || BotAskFirst.Parked(body.Serial, Reagents[i]))
                {
                    continue;
                }

                wanted = Reagents[i];

                return true;
            }
        }

        if (BotFence.Is(body) && BotFence.Shortest(body, out var ordered, out var lot))
        {
            wanted = ordered;
            amount = lot;
            band = true;

            return true;
        }

        if (BotFlask.Kit(body) == null)
        {
            wanted = null;

            return false;
        }

        var brewing = BotFlask.Needs;

        for (var i = 0; i < brewing.Count; i++)
        {
            if (!Lacking(pack, brewing[i], BotFlask.Herbs, out amount) || BotAskFirst.Parked(body.Serial, brewing[i]))
            {
                continue;
            }

            wanted = brewing[i];

            return true;
        }

        wanted = null;
        amount = 0;

        return false;
    }

    private static bool Kept(Mobile body, Container pack, Type kind, out bool spent)
    {
        spent = false;

        foreach (var item in pack.FindItemsByType(kind))
        {
            if (!BotOutfit.Spent(item))
            {
                return true;
            }

            spent = true;
        }

        var worn = body?.Items;

        if (worn == null)
        {
            return false;
        }

        for (var i = 0; i < worn.Count; i++)
        {
            if (!kind.IsInstanceOfType(worn[i]))
            {
                continue;
            }

            if (!BotOutfit.Spent(worn[i]))
            {
                return true;
            }

            spent = true;
        }

        return false;
    }

    private static bool Held(Mobile bot, Type kind)
    {
        if (bot == null)
        {
            return false;
        }

        var worn = bot.Items;

        for (var i = 0; i < worn.Count; i++)
        {
            if (kind.IsInstanceOfType(worn[i]))
            {
                return true;
            }
        }

        return false;
    }

    private static bool Lacking(Container pack, Type kind, int born, out int wanted)
    {
        wanted = 0;

        if (born <= 0)
        {
            return false;
        }

        var held = pack.GetAmount(kind);

        if (held >= born * Short)
        {
            return false;
        }

        wanted = born - held;

        return wanted > 0;
    }

    /// <summary>
    /// Nothing on any shelf and nothing on any stall: ask the population for it.
    ///
    /// <para>
    /// <b>The end of this method used to be a shrug.</b> It wrote one error line and returned nothing, so a
    /// caster on a shard whose shopkeepers do not stock sulphurous ash simply stopped casting, for ever, and
    /// the only trace was a single line at boot. <see cref="BotSeeker"/> has ended the other way since it was
    /// written — no shop, no stall, so put it on the board — and there was never a reason for supplies to be
    /// the exception. A reagent nobody sells is a reagent somebody can pick: the foragers put them out at a
    /// few gold apiece, and a funded order is what reaches them.
    /// </para>
    ///
    /// <para>
    /// One order per bot per kind, as everywhere else: the want tops itself up and raises its own offer, and
    /// asking again would turn one order into nine.
    /// </para>
    /// </summary>
    /// <summary>
    /// Whether any craft system on this shard can make the thing at all.
    ///
    /// <para>
    /// <b>The caller's own comment has always said Board refuses when nobody can make the thing, and Board
    /// never asked.</b> An order for something only a shopkeeper produces cannot be filled by anybody, ever:
    /// it holds escrow, raises its own offer every StaleMs and charges the buyer more for it each time, and
    /// the goods it is waiting for do not exist. Patrick rolled back exactly this for glass on 04.09.2026 —
    /// see the note in that day's handover — and bandages walked into the same hole from the other side:
    /// nothing in Engines/Craft makes a Bandage, so when the healer's shelf ran dry at 08:00 on 05.09.2026
    /// the shopper put the shortage on the board instead, and the board raised it from five gold to
    /// forty-eight while 7,752 bots stood hurt with nothing to bind a wound with. Twenty-six thousand gold
    /// of escrow against thirteen thousand in every purse on the island.
    /// </para>
    ///
    /// <para>
    /// Asked of the shard's own craft systems rather than of a list kept here, for the reason every other
    /// file in this assembly gives for the same choice: a list is a second copy of the truth and it drifts.
    /// Cached by type, because the answer cannot change while the server is up.
    /// </para>
    /// </summary>
    /// <summary>What craft makes a thing, which skill it is made with, and how much of that skill it takes.</summary>
    /// <summary>
    /// What it takes to make one of a thing: the craft, the trade on a bot's menu, the skill and its
    /// threshold, and the material the recipe consumes.
    ///
    /// <c>Material</c> is the first resource line, which for every recipe on this shard is the one that
    /// matters — ingots for armour, leather for a bustier, boards for a shaft. The others are trimmings.
    /// </summary>
    public readonly record struct BotRecipeFact(
        string Craft,
        string Trade,
        SkillName Skill,
        double MinSkill,
        string Material,
        int Needs
    );

    private static readonly Dictionary<Type, BotRecipeFact?> _recipes = [];

    public static BotRecipeFact? MadeBy(Type wanted)
    {
        if (wanted == null)
        {
            return null;
        }

        if (_recipes.TryGetValue(wanted, out var known))
        {
            return known;
        }

        (string Craft, string Trade, CraftSystem System)[] systems =
        [
            ("smithing", "Smith", BotAnvil.System),
            ("tailoring", "Tailor", BotThread.System),
            ("alchemy", "Alchemist", BotFlask.System),
            ("fletching", "Fletcher", BotFletching.System),
            ("scribing", "Scribe", BotQuill.System)
        ];

        BotRecipeFact? found = null;

        for (var i = 0; i < systems.Length && found == null; i++)
        {
            var recipes = systems[i].System?.CraftItems;

            if (recipes == null)
            {
                continue;
            }

            for (var r = 0; r < recipes.Count; r++)
            {
                var recipe = recipes[r];

                if (recipe?.ItemType != wanted)
                {
                    continue;
                }

                var skill = recipe.Skills?.Count > 0 ? recipe.Skills[0] : null;

                var resource = recipe.Resources?.Count > 0 ? recipe.Resources[0] : null;

                found = new BotRecipeFact(
                    systems[i].Craft,
                    systems[i].Trade,
                    skill?.SkillToMake ?? SkillName.Blacksmith,
                    skill?.MinSkill ?? 0.0,
                    resource?.ItemType?.Name,
                    resource?.Amount ?? 0
                );

                break;
            }
        }

        if (systems[0].System != null)
        {
            _recipes[wanted] = found;
        }

        return found;
    }

    public static bool Makeable(Type wanted)
    {
        if (wanted == null)
        {
            return false;
        }

        if (_makeable.TryGetValue(wanted, out var known))
        {
            return known;
        }

        var systems = new[]
        {
            BotAnvil.System, BotThread.System, BotFlask.System, BotFletching.System, BotQuill.System
        };

        var made = false;

        for (var i = 0; i < systems.Length && !made; i++)
        {
            var recipes = systems[i]?.CraftItems;

            if (recipes == null)
            {
                continue;
            }

            for (var r = 0; r < recipes.Count; r++)
            {
                if (recipes[r]?.ItemType == wanted)
                {
                    made = true;

                    break;
                }
            }
        }

        if (systems[0] != null)
        {
            _makeable[wanted] = made;
        }

        return made;
    }

    private static readonly System.Collections.Generic.Dictionary<Type, bool> _makeable = [];

    public static long Unmakeable { get; private set; }

    private static BotDeed Board(IBotWilful bot, Mobile body, Map map, Type wanted, int amount)
    {
        if (BotMarket.Wanted(bot, wanted) != null)
        {
            return null;
        }

        if (!Makeable(wanted))
        {
            Unmakeable++;

            return null;
        }

        var offer = BotMarket.Worth(wanted, Stand(wanted));

        if (BotYield.Spendable(body) - offer * amount <= Reserve)
        {
            return null;
        }

        Asked++;

        return BotOrder.For(map, body.Location, bot, wanted, offer, amount);
    }

    private static void Missing(Type wanted, Map map, Mobile body)
    {
        if (_saidNoShops)
        {
            return;
        }

        _saidNoShops = true;

        logger.Error(
            "{Name} at {Where} on {Map} found no shopkeeper selling {Item} within its own reach; it cannot restock from a counter here",
            body?.Name ?? "a bot",
            body?.Location ?? Point3D.Zero,
            map,
            wanted.Name
        );
    }

    public static void Forget() => _saidNoShops = false;
}
