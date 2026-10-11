using System;
using System.Collections.Generic;
using Server.Mobiles;

namespace Server.BotAI.V2;

/// <summary>Where a haul buys its goods.</summary>
public enum BotHaulFrom
{
    Stock,

    Box,

    Shop,

    Counter,

    Gather
}

/// <summary>One place to buy a kind, and what it asks.</summary>
public sealed class BotHaulSource
{
    public BotHaulFrom From { get; init; }

    public BaseVendor Vendor { get; init; }

    public BotStorefront Front { get; init; }

    public Point3D Where { get; init; }

    public int Cost { get; init; }

    public int Shelf { get; init; }

    public bool Buys => From is BotHaulFrom.Shop or BotHaulFrom.Counter or BotHaulFrom.Gather;

    public string Name =>
        From switch
        {
            BotHaulFrom.Stock   => "from its own pack",
            BotHaulFrom.Box     => "from its own bank box",
            BotHaulFrom.Shop    => $"at {Front?.Keeper?.Name}'s shop in {Front?.Town}",
            BotHaulFrom.Gather  => $"from the bots of {BotTowns.Of(Where)?.Name ?? "the town"} at {Cost}gp",
            _                   => $"from {Vendor?.Name} in {BotTowns.Of(Where)?.Name ?? "the country"}"
        };
}

/// <summary>What a trader means to do: one kind, one place to buy it, up to <see cref="BotTrading.MostOrders"/> buyers.</summary>
public sealed class BotHaulPlan
{
    public Type Kind { get; init; }

    public BotHaulSource Source { get; set; }

    public List<(BotWant Want, int Units, int Price)> Orders { get; } = [];

    public double ToSource { get; set; }

    public double After { get; set; }

    public int Profit { get; set; }

    public int Spend { get; set; }

    public double Rate { get; set; }

    public int Units
    {
        get
        {
            var units = 0;

            for (var i = 0; i < Orders.Count; i++)
            {
                units += Orders[i].Units;
            }

            return units;
        }
    }
}

/// <summary>
/// The trader's proposer: of the wants the island has said aloud, the one this trader can buy somewhere and bring to its buyer
/// at the best profit a minute — and, riding on the same purchase, up to two more buyers of the same kind near the first.
///
/// <para>
/// <b>Only open wants, only what the trader can pay for, only where there is a margin (01.10.2026, build 385).</b> A want left
/// to the carriers within reach for its first <see cref="BotTrading.FreshMs"/>; a buyer that is a trader never (no carousel of
/// traders buying from each other); a buyer on deadly ground or beyond any way through never; across water only for a trader
/// well off enough to sail (<see cref="BotTrading.CanSail"/>). Sources weighed: its own pack, its own box at a bank, a bot's
/// shop within reach (coin to a bot stays on the island), a shopkeeper in any town — the one that makes goods and road
/// cheapest together at the shard's price of a minute. Asked of traders only; a trader that found nothing rests its look for
/// <see cref="BotTrading.LookMs"/>.
/// </para>
/// </summary>
public sealed class BotHauler : IBotProposer
{
    public string Name => "Trader";

    public BotStanding Rung => BotStanding.Free;

    private static readonly Dictionary<Serial, long> _quiet = [];

    public BotDeed Propose(IBotWilful bot)
    {
        if (!BotTrading.Running || !BotMarket.Running || !BotMarket.AnyBespoke)
        {
            return null;
        }

        if (bot?.Self is not BotMobile { Deleted: false, Alive: true } body || body.Class is not { Neutral: true })
        {
            return null;
        }

        var map = body.Map;

        if (map == null || map == Map.Internal || body.Backpack == null || bot.Squad != null || BotLadder.Overloaded(body))
        {
            return null;
        }

        if (bot.Resolve?.Deed is BotHaul || bot.Resolve?.Paused?.Deed is BotHaul)
        {
            return null;
        }

        var now = Core.TickCount;

        if (_quiet.TryGetValue(body.Serial, out var until) && now - until < 0)
        {
            return null;
        }

        BotTrading.Looks++;
        BotTrading.Survey(map);

        BotTrading.PutAway(body);

        var capital = BotYield.Spendable(body) - BotTrading.LeastCapital;
        var wants = BotMarket.Wants;
        var own = OwnKinds(bot);
        var looked = 0;
        BotHaulPlan best = null;

        for (var i = 0; i < wants.Count && looked < BotTrading.MostLooked; i++)
        {
            var want = wants[i];

            if (!Answerable(bot, body, map, want, now, own))
            {
                continue;
            }

            looked++;

            var plan = Weigh(bot, body, map, want, capital, own);

            if (plan != null && (best == null || plan.Rate > best.Rate))
            {
                best = plan;
            }
        }

        if (best is { Source.From: BotHaulFrom.Counter } && !Confirm(bot, body, map, best, capital))
        {
            best = null;
        }

        if (best == null)
        {
            if (capital <= 0)
            {
                BotTrading.Poor++;
            }

            BotTrading.Barren++;
            Quiet(body, now);

            return null;
        }

        Batch(bot, body, map, best, capital, now, own);

        BotTrading.Offered++;

        for (var i = 0; i < best.Orders.Count; i++)
        {
            best.Orders[i].Want?.Offered();
        }

        return new BotHaul(best, map);
    }

    private static void Quiet(Mobile body, long now)
    {
        if (_quiet.Count > 4096)
        {
            _quiet.Clear();
        }

        _quiet[body.Serial] = now + Math.Max(1000, BotTrading.LookMs);
    }

    private static readonly HashSet<Type> _own = [];

    private static readonly Dictionary<(Serial Trader, int Want), long> _shunned = [];

    private static readonly Dictionary<Type, long> _gatherShunned = [];

    internal static void ShunGathering(Type kind)
    {
        if (kind == null || BotTrading.GatherShunMs <= 0)
        {
            return;
        }

        if (_gatherShunned.Count > 512)
        {
            _gatherShunned.Clear();
        }

        _gatherShunned[kind] = Core.TickCount + BotTrading.GatherShunMs;
    }

    internal static bool Deadly(BotMobile body, Map map, Point3D from, Point3D to)
    {
        var step = Math.Max(8, BotTrading.RoadStep);
        var dare = BotTrading.RoadDare;

        if (dare <= 0.0 || map == null)
        {
            return false;
        }

        var dx = to.X - from.X;
        var dy = to.Y - from.Y;
        var length = Math.Max(Math.Abs(dx), Math.Abs(dy));

        if (length <= step)
        {
            return false;
        }

        var brought = BotQuad.Strength(body) * dare;
        var samples = length / step;

        for (var i = 1; i < samples; i++)
        {
            var at = new Point3D(from.X + dx * i / samples, from.Y + dy * i / samples, 0);

            if (BotQuad.Muscle(map, at) > brought)
            {
                return true;
            }
        }

        return false;
    }

    internal static void Shun(IBotWilful bot, BotWant want)
    {
        if (bot?.Self is not { } body || want == null || BotTrading.ShunMs <= 0)
        {
            return;
        }

        if (_shunned.Count > 4096)
        {
            _shunned.Clear();
        }

        _shunned[(body.Serial, want.Id)] = Core.TickCount + BotTrading.ShunMs;
    }

    internal static HashSet<Type> OwnKinds(IBotWilful bot)
    {
        _own.Clear();

        var wants = BotMarket.Wants;

        for (var i = 0; i < wants.Count; i++)
        {
            if (ReferenceEquals(wants[i].Buyer, bot) && wants[i].Kind != null)
            {
                _own.Add(wants[i].Kind);
            }
        }

        return _own;
    }

    internal static bool Answerable(IBotWilful bot, BotMobile body, Map map, BotWant want, long now, HashSet<Type> own)
    {
        if (want?.Kind == null || !want.IsOpen || !BotTrading.Goods(want.Kind) || ReferenceEquals(want.Buyer, bot))
        {
            return false;
        }

        if (want.Buyer?.Self is not BotMobile { Deleted: false, Alive: true } buyer || buyer.Map != map)
        {
            return false;
        }

        if (buyer.Class is { Neutral: true })
        {
            BotTrading.ToTraders++;

            return false;
        }

        if (BotRoom.Shut(buyer) || !BotRoom.Receives(buyer, want.Kind))
        {
            BotRoom.Note(buyer);
            BotRoom.HaulPassed();
            want.Passed(BotPass.Room);

            return false;
        }

        if (want.Raises == 0 && now - want.PostedTick < BotTrading.FreshMs)
        {
            BotTrading.Fresh++;

            return false;
        }

        if (_shunned.TryGetValue((body.Serial, want.Id), out var until) && now - until < 0)
        {
            BotTrading.Shunned++;

            return false;
        }

        return true;
    }

    internal static BotHaulPlan Weigh(IBotWilful bot, BotMobile body, Map map, BotWant want, int capital, HashSet<Type> own)
    {
        var kind = want.Kind;
        var buyer = want.Buyer.Self;
        var wanted = Math.Min(want.Payable, Math.Max(1, BotTrading.MostUnits));
        var weight = BotTrading.Weight(kind);

        var mine = own.Contains(kind);

        if (mine)
        {
            BotTrading.OwnWant++;
        }

        if (weight > 0.0)
        {
            wanted = Math.Min(wanted, (int)((body.MaxWeight - body.TotalWeight) / weight));
        }

        if (wanted <= 0)
        {
            BotTrading.Heavy++;
            want.Passed(BotPass.Margin);

            return null;
        }

        if (bot.Resolve?.Ledger?.Cautious(BotHaul.Trade, map, buyer.Location) == true)
        {
            BotTrading.Sealed++;
            want.Passed(BotPass.Sealed);

            return null;
        }

        if (BotUnreached.Refuses(map, buyer, body.Location))
        {
            BotTrading.Unreached++;
            want.Passed(BotPass.Sealed);

            return null;
        }

        if (BotUnreached.Shut(map, body.Location, buyer.Location))
        {
            BotTrading.Stranded++;
            want.Passed(BotPass.Sealed);

            return null;
        }

        if (BotPeril.Overwhelms(map, buyer.Location, body.Location, BotQuad.Strength(body), out _, false))
        {
            BotTrading.Perilous++;
            want.Passed(BotPass.Peril);

            return null;
        }

        BotHaulPlan best = null;
        var any = false;

        var carried = mine ? 0 : BotHandover.Carried(bot, kind);

        if (carried > 0)
        {
            any = true;

            var stock = new BotHaulSource
            {
                From = BotHaulFrom.Stock, Where = body.Location, Cost = BotTrading.BookValue(kind, want.Anchor), Shelf = carried
            };

            Better(ref best, Plan(bot, body, map, want, stock, wanted, capital));
        }

        if (!mine && BotMarket.AtBank(body) && body.FindBankNoCreate() is { } box)
        {
            var boxed = 0;
            var items = box.Items;

            for (var i = 0; i < items.Count; i++)
            {
                if (items[i] is { Deleted: false } item && item.GetType() == kind && BotShopkeep.Merchandise(item, bot.Bond))
                {
                    boxed += Math.Max(1, item.Amount);
                }
            }

            if (boxed > 0)
            {
                any = true;

                var banked = new BotHaulSource
                {
                    From = BotHaulFrom.Box, Where = body.Location, Cost = BotTrading.BookValue(kind, want.Anchor), Shelf = boxed
                };

                Better(ref best, Plan(bot, body, map, want, banked, wanted, capital));
            }
        }

        if (capital > 0)
        {
            var front = BotShopkeep.Best(bot, kind, wanted, out var ask, out var held, out _);

            if (front != null && ask > 0 && held > 0)
            {
                any = true;

                var shop = new BotHaulSource
                {
                    From = BotHaulFrom.Shop, Front = front, Where = front.Location, Cost = ask, Shelf = held
                };

                Better(ref best, Plan(bot, body, map, want, shop, wanted, capital));
            }

            var gather = Gathering(body, map, want);

            if (gather != null)
            {
                any = true;

                Better(ref best, Plan(bot, body, map, want, gather, wanted, capital));
            }

            if (BotGlut.Glutted(kind))
            {
                BotTrading.Glutted++;
            }
            else
            {
                var vendor = Cheapest(body, map, kind, buyer.Location, wanted, out var price, out var shelf);

                if (vendor != null)
                {
                    any = true;

                    var counter = new BotHaulSource
                    {
                        From = BotHaulFrom.Counter, Vendor = vendor, Where = vendor.Location, Cost = price, Shelf = shelf
                    };

                    Better(ref best, Plan(bot, body, map, want, counter, wanted, capital));
                }
            }
        }

        if (!any && capital > 0)
        {
            BotTrading.NoSource++;
        }

        if (best == null && capital > 0)
        {
            want.Passed(any ? BotPass.Margin : BotPass.Source);
        }

        return best;
    }

    private static void Better(ref BotHaulPlan best, BotHaulPlan plan)
    {
        if (plan != null && (best == null || plan.Rate > best.Rate))
        {
            best = plan;
        }
    }

    private static BotHaulSource Gathering(BotMobile body, Map map, BotWant want)
    {
        if (BotTrading.GatherMs <= 0)
        {
            return null;
        }

        var buyer = want.Buyer.Self;

        if (Utility.InRange(body.Location, buyer.Location, BotCourier.Reach) && BotGates.Joined(map, body.Location, buyer.Location))
        {
            return null;
        }

        if (BotGlut.Seen(want.Kind) <= 0)
        {
            BotTrading.GatherBarren++;

            return null;
        }

        if (_gatherShunned.TryGetValue(want.Kind, out var until) && Core.TickCount - until < 0)
        {
            BotTrading.GatherShunned++;

            return null;
        }

        var town = BotTowns.Of(body.Location);
        var bank = town == null ? Point3D.Zero : BotShopkeep.BankNear(map, town.Square);

        if (bank == Point3D.Zero || !BotGates.Joined(map, body.Location, bank))
        {
            return null;
        }

        var bid = Math.Max(BotMarket.Floor, (int)Math.Floor(want.Offer / (1.0 + BotTrading.LeastMarkup)));

        if (bid >= want.Offer)
        {
            return null;
        }

        return new BotHaulSource { From = BotHaulFrom.Gather, Where = bank, Cost = bid, Shelf = want.Payable };
    }

    private static BaseVendor Cheapest(BotMobile body, Map map, Type kind, Point3D buyer, int units, out int price, out int shelf)
    {
        price = 0;
        shelf = 0;

        BaseVendor best = null;
        var bestLanded = double.MaxValue;
        var minute = BotShopkeep.WalkGold;
        var vendors = BotTrading.Sellers(kind);

        for (var i = 0; i < vendors.Count; i++)
        {
            var vendor = vendors[i];

            if (vendor.Deleted || vendor.Map != map || !BotShops.Sells(vendor, kind, out var entry) || entry.Price <= 0 || entry.Amount <= 0)
            {
                continue;
            }

            var n = Math.Min(units, entry.Amount);
            var road = BotAppraisal.Travel(body.Location, vendor.Location) + BotAppraisal.Travel(vendor.Location, buyer);
            var landed = BotBurgh.Dear(body, vendor, entry.Price) * (double)n + road * minute;

            if (landed < bestLanded)
            {
                bestLanded = landed;
                best = vendor;
                price = entry.Price;
                shelf = entry.Amount;
            }
        }

        return best;
    }

    private static BotHaulPlan Plan(IBotWilful bot, BotMobile body, Map map, BotWant want, BotHaulSource source, int wanted, int capital)
    {
        var buyer = want.Buyer.Self;
        var units = Math.Min(wanted, source.Shelf);

        if (BotTrading.OverItsCeiling(want, source.Cost))
        {
            return null;
        }

        if (source.Buys)
        {
            units = Math.Min(units, capital / Math.Max(1, source.Cost));
        }

        if (units <= 0)
        {
            BotTrading.Broke++;

            return null;
        }

        var price = BotTrading.Quote(source.Cost, want.Offer, BotTrading.Alternative(buyer, want.Kind, units, want));

        units = Math.Min(units, BotMarket.Affords(want, price));

        if (units <= 0)
        {
            BotTrading.Broke++;

            return null;
        }

        var profit = units * (price - source.Cost);

        if (profit < BotTrading.LeastProfit)
        {
            BotTrading.Thin++;

            return null;
        }

        if (!Road(body, map, body.Location, source.Where, out var toSource) || !Road(body, map, source.Where, buyer.Location, out var onward))
        {
            BotTrading.Unjoined++;

            return null;
        }

        if (BotUnreached.Shut(map, body.Location, source.Where) || BotUnreached.Shut(map, source.Where, buyer.Location))
        {
            BotTrading.Stranded++;

            return null;
        }

        if (Deadly(body, map, body.Location, source.Where) || Deadly(body, map, source.Where, buyer.Location))
        {
            BotTrading.DeadlyRoad++;

            return null;
        }

        if (BotGates.Joined(map, source.Where, buyer.Location)
            && BotReach.Ask(map, source.Where, buyer.Location, BotArrival.Within(BotHandover.MeetTiles), false) == BotReachVerdict.Sealed)
        {
            BotTrading.Sealed++;

            return null;
        }

        var after = onward + (source.Buys ? BotTrading.BuyMinutes : 0.0) + BotTrading.HandMinutes;

        var expected = (double)profit;

        if (source.From == BotHaulFrom.Gather)
        {
            after += BotTrading.GatherMs / 60000.0;
            expected *= Math.Clamp(BotTrading.GatherYield, 0.05, 1.0);
        }

        var plan = new BotHaulPlan
        {
            Kind = want.Kind,
            Source = source,
            ToSource = toSource,
            After = after,
            Profit = profit,
            Spend = source.Buys ? units * source.Cost : 0,
            Rate = expected / Math.Max(0.5, toSource + after)
        };

        plan.Orders.Add((want, units, price));

        return plan;
    }

    internal static bool Road(BotMobile body, Map map, Point3D from, Point3D to, out double minutes)
    {
        minutes = 0.0;

        if (from == to)
        {
            return true;
        }

        if (BotGates.Joined(map, from, to))
        {
            minutes = BotAppraisal.Travel(from, to);

            return true;
        }

        if (!BotTrading.CanSail(body))
        {
            return false;
        }

        var town = BotTowns.Of(to) ?? BotTowns.Nearest(to);
        var dock = town == null ? null : BotDocks.Of(town);

        if (dock == null || !BotSea.Plan(map, from, dock, out _, out _, out var tiles, true))
        {
            return false;
        }

        minutes = (tiles + BotHelm.Away(dock.Shore, to)) * (double)BotWalk.StepDelayMs(false) / 60000.0;

        return true;
    }

    private static bool Confirm(IBotWilful bot, BotMobile body, Map map, BotHaulPlan plan, int capital)
    {
        var chosen = plan.Source.Vendor;
        var vetted = BotShops.Nearest(bot, plan.Kind, chosen.Location);

        if (vetted == null)
        {
            BotTrading.NoSource++;

            return false;
        }

        if (ReferenceEquals(vetted, chosen))
        {
            return true;
        }

        if (!BotShops.Sells(vetted, plan.Kind, out var entry) || entry.Price <= 0 || entry.Amount <= 0)
        {
            BotTrading.NoSource++;

            return false;
        }

        var (want, _, _) = plan.Orders[0];
        var again = Plan(
            bot,
            body,
            map,
            want,
            new BotHaulSource
            {
                From = BotHaulFrom.Counter, Vendor = vetted, Where = vetted.Location, Cost = entry.Price, Shelf = entry.Amount
            },
            Math.Min(want.Payable, Math.Max(1, BotTrading.MostUnits)),
            capital
        );

        if (again == null)
        {
            return false;
        }

        plan.Source = again.Source;
        plan.Orders.Clear();
        plan.Orders.AddRange(again.Orders);
        plan.ToSource = again.ToSource;
        plan.After = again.After;
        plan.Profit = again.Profit;
        plan.Spend = again.Spend;
        plan.Rate = again.Rate;

        return true;
    }

    private static void Batch(IBotWilful bot, BotMobile body, Map map, BotHaulPlan plan, int capital, long now, HashSet<Type> own)
    {
        if (BotTrading.MostOrders <= 1)
        {
            return;
        }

        var source = plan.Source;
        var first = plan.Orders[0].Want.Buyer.Self;
        var units = plan.Units;
        var shelf = source.Shelf - units;
        var spare = source.Buys ? capital - plan.Spend : int.MaxValue;
        var weight = BotTrading.Weight(plan.Kind);
        var room = weight > 0.0 ? (int)((body.MaxWeight - body.TotalWeight) / weight) - units : int.MaxValue;
        var wants = BotMarket.Wants;
        var last = first.Location;

        for (var i = 0; i < wants.Count && plan.Orders.Count < BotTrading.MostOrders && shelf > 0 && room > 0; i++)
        {
            var want = wants[i];

            if (want.Kind != plan.Kind || Holds(plan, want) || !Answerable(bot, body, map, want, now, own))
            {
                continue;
            }

            var buyer = want.Buyer.Self;

            if (!Utility.InRange(first.Location, buyer.Location, BotTrading.BatchTiles) || !BotGates.Joined(map, first.Location, buyer.Location)
                || source.From == BotHaulFrom.Gather && Utility.InRange(body.Location, buyer.Location, BotCourier.Reach)
                || BotPeril.Overwhelms(map, buyer.Location, body.Location, BotQuad.Strength(body), out _, false))
            {
                continue;
            }

            if (BotUnreached.Refuses(map, buyer, body.Location))
            {
                BotTrading.Unreached++;
                want.Passed(BotPass.Sealed);

                continue;
            }

            if (BotUnreached.Shut(map, first.Location, buyer.Location) || BotUnreached.Shut(map, source.Where, buyer.Location))
            {
                BotTrading.Stranded++;
                want.Passed(BotPass.Sealed);

                continue;
            }

            var n = Math.Min(Math.Min(want.Payable, shelf), room);

            if (BotTrading.OverItsCeiling(want, source.Cost))
            {
                continue;
            }

            if (source.Buys)
            {
                n = Math.Min(n, spare / Math.Max(1, source.Cost));
            }

            var price = BotTrading.Quote(source.Cost, want.Offer, BotTrading.Alternative(buyer, want.Kind, Math.Max(1, n), want));

            n = Math.Min(n, BotMarket.Affords(want, price));

            if (n <= 0 || price <= source.Cost)
            {
                continue;
            }

            plan.Orders.Add((want, n, price));
            plan.Profit += n * (price - source.Cost);
            plan.After += BotAppraisal.Travel(last, buyer.Location) + BotTrading.HandMinutes;
            plan.Spend += source.Buys ? n * source.Cost : 0;

            shelf -= n;
            room -= n;
            spare -= source.Buys ? n * source.Cost : 0;
            last = buyer.Location;
        }

        var expected = source.From == BotHaulFrom.Gather ? plan.Profit * Math.Clamp(BotTrading.GatherYield, 0.05, 1.0) : plan.Profit;

        plan.Rate = expected / Math.Max(0.5, plan.ToSource + plan.After);
    }

    private static bool Holds(BotHaulPlan plan, BotWant want)
    {
        for (var i = 0; i < plan.Orders.Count; i++)
        {
            if (ReferenceEquals(plan.Orders[i].Want, want) || ReferenceEquals(plan.Orders[i].Want.Buyer, want.Buyer))
            {
                return true;
            }
        }

        return false;
    }

    public static void Forget()
    {
        _quiet.Clear();
        _shunned.Clear();
        _gatherShunned.Clear();
    }
}
