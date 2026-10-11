using System;
using System.Collections.Generic;

namespace Server.BotAI.V2;

/// <summary>
/// Offers a bot carrying what somebody has said it wants the walk to that somebody, and the sale at the other end.
///
/// <para>
/// <b>The supplier's half of hand to hand (01.10.2026).</b> Under the auction a want was filled from wherever the supplier
/// stood: the porter at a counter, the miner at its forge, the tailor at its bench — 421 fills for some 1,200 things in the
/// hour of 16:42–17:42 on 30.09.2026, leather 369, iron ingots 365, raw ribs 210, feathers 157, logs 40 and 93 pieces of
/// leather and plate armour. Every one of those is now a walk. Anything in the pack over what the bot keeps for itself
/// (<see cref="BotUnload.Keeps"/>) — and, for a bot standing at a bank, anything in its bank box — is weighed against every open
/// want for its kind, and the best by what it pays over the minutes of the walk is offered as a <see cref="BotDeliver"/>.
/// </para>
///
/// <para>
/// <b>Priced as a sale, and corrected by what sales paid.</b> The claim is the gold the delivery would bring over the walk and
/// the handing over. The ledger learns what deliveries really paid, as it learns everything else, so a carrier whose buyers
/// keep walking away stops being sent after them. A delivery worth less than <see cref="LeastGold"/>, or on another land, or
/// past <see cref="Reach"/>, or to a buyer who could not pay for one at its own offer, is not offered — each counted apart.
/// </para>
///
/// <para>
/// <b>Three walls made prices or questions (06.10.2026, needs15; Patrick: "95 on work and on Needs orders").</b> Build 400 failed 591
/// Needs lines in 11.3 hours, and three causes in this file held 472 of them. The tail of a part-handed want is offered under
/// <see cref="LeastGold"/> (<see cref="TailExempt"/>). A buyer past <see cref="Reach"/> nobody within it has answered is walked to
/// out to <see cref="FarReach"/>, the buyer paying carriage (<see cref="FarOpen"/>, <see cref="FarPrice"/>). And the straight road
/// the auction judges the walk by is asked here, of the want that would win, so the carrier offers the best walk its auction will
/// take rather than one it will refuse (<see cref="BotPass.Road"/>).
/// </para>
/// </summary>
public sealed class BotCourier : IBotProposer
{
    public string Name => "Courier";

    public BotStanding Rung => BotStanding.Free;

    public static int Reach { get; set; } = 400;

    public static int LeastGold { get; set; } = 8;

    public static double LeastClaim { get; set; } = 4.0;

    public static double MostClaim { get; set; } = 300.0;

    public static long Looks { get; private set; }

    public static long Carrying { get; private set; }

    public static long Offered { get; private set; }

    public static long Own { get; private set; }

    public static long Covered { get; private set; }

    public static long Far { get; private set; }

    public static long Unjoined { get; private set; }

    public static long Sealed { get; private set; }

    public static long Broke { get; private set; }

    public static long Petty { get; private set; }

    public static long Stranded { get; private set; }

    public static long Unreached { get; private set; }

    private static readonly Dictionary<Type, int> _surplus = [];

    private static readonly Dictionary<Type, int> _boxed = [];

    public static long FromBox { get; private set; }

    public BotDeed Propose(IBotWilful bot)
    {
        if (!BotMarket.Running || !BotMarket.AnyBespoke)
        {
            return null;
        }

        var body = bot?.Self;
        var map = body?.Map;
        var pack = body?.Backpack;

        if (map == null || map == Map.Internal || pack == null || !body.Alive)
        {
            return null;
        }

        if (BotLadder.Load(body) > BotLadder.Ceiling(body))
        {
            return null;
        }

        Looks++;

        var any = false;
        var items = pack.Items;

        for (var i = 0; i < items.Count && !any; i++)
        {
            any = items[i] is { Deleted: false } item && BotMarket.Bespoke(item.GetType());
        }

        _boxed.Clear();

        var box = BotMarket.AtBank(body) ? body.FindBankNoCreate() : null;

        if (box != null)
        {
            var held = box.Items;

            for (var i = 0; i < held.Count; i++)
            {
                if (held[i] is { Deleted: false } item && BotMarket.Bespoke(item.GetType()) && BotShopkeep.Merchandise(item, bot.Bond))
                {
                    _boxed[item.GetType()] = _boxed.GetValueOrDefault(item.GetType()) + Math.Max(1, item.Amount);
                    any = true;
                }
            }
        }

        if (!any)
        {
            return null;
        }

        _surplus.Clear();

        var keep = BotUnload.Keeps(bot);
        var bond = bot.Bond;

        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];

            if (item == null || item.Deleted || !BotMarket.Bespoke(item.GetType()) || !BotShopkeep.Merchandise(item, bond))
            {
                continue;
            }

            var kind = item.GetType();

            _surplus[kind] = _surplus.GetValueOrDefault(kind) + Math.Max(1, item.Amount);
        }

        List<Type> kinds = [.. _surplus.Keys];

        for (var i = 0; i < kinds.Count; i++)
        {
            var kind = kinds[i];

            if (keep.TryGetValue(kind, out var allowed))
            {
                var over = allowed >= int.MaxValue ? 0 : Math.Max(0, _surplus[kind] - allowed);

                if (over <= 0)
                {
                    _surplus.Remove(kind);
                }
                else
                {
                    _surplus[kind] = over;
                }
            }
        }

        foreach (var (kind, units) in _boxed)
        {
            _surplus[kind] = _surplus.GetValueOrDefault(kind) + units;
        }

        if (_surplus.Count == 0)
        {
            return null;
        }

        Carrying++;

        var ledger = bot.Resolve?.Ledger;
        var wants = BotMarket.Wants;
        var now = Core.TickCount;
        var strength = BotQuad.Strength(body);

        BotWant best = null;
        var bestUnits = 0;
        var bestBoxed = 0;
        var bestRate = 0.0;
        var bestTo = Point3D.Zero;
        var bestPrice = 0;
        var bestFar = false;
        var bestTail = false;

        var refusedRate = 0.0;

        for (var i = 0; i < wants.Count; i++)
        {
            var want = wants[i];

            if (want?.Kind == null || !_surplus.TryGetValue(want.Kind, out var have) || !want.IsOpen)
            {
                continue;
            }

            if (ReferenceEquals(want.Buyer, bot) || BotMarket.Wanted(bot, want.Kind) != null)
            {
                Own++;

                continue;
            }

            if (want.Buyer?.Self is not { Deleted: false } buyer)
            {
                continue;
            }

            if (!buyer.Alive)
            {
                want.Passed(BotPass.Dead);

                continue;
            }

            if (buyer.Map != map)
            {
                want.Passed(BotPass.Land);

                continue;
            }

            if (BotRoom.Shut(buyer))
            {
                BotRoom.Held();
                want.Passed(BotPass.Room);

                continue;
            }

            if (!BotRoom.Receives(buyer, want.Kind))
            {
                BotRoom.Note(buyer);
                BotRoom.FirstPassed();
                want.Passed(BotPass.Room);

                continue;
            }

            if (BotWayless.Shut(map, buyer.Location, false))
            {
                BotWayless.PassedByCarrier();
                want.Passed(BotPass.Sealed);

                continue;
            }

            var tiles = Math.Max(Math.Abs(buyer.X - body.X), Math.Abs(buyer.Y - body.Y));
            var far = false;

            if (tiles > Reach)
            {
                if (!FarOpen(want, tiles, now))
                {
                    Far++;
                    want.Passed(BotPass.Reach);

                    continue;
                }

                far = true;
                FarOpened++;
            }

            if (!BotGates.Joined(map, body.Location, buyer.Location))
            {
                Unjoined++;
                want.Passed(BotPass.Land);

                continue;
            }

            if (BotUnreached.Shut(map, body.Location, buyer.Location))
            {
                Stranded++;
                want.Passed(BotPass.Sealed);

                continue;
            }

            if (BotUnreached.Refuses(map, buyer, body.Location))
            {
                Unreached++;
                want.Passed(BotPass.Sealed);

                continue;
            }

            if (ledger?.Cautious(BotDeliver.Trade, map, buyer.Location) == true
                || BotReach.Ask(map, body.Location, buyer.Location, BotArrival.Within(BotHandover.MeetTiles), false) == BotReachVerdict.Sealed)
            {
                Sealed++;
                want.Passed(BotPass.Sealed);

                continue;
            }

            var units = Math.Min(have, want.Payable);

            var price = far ? FarPrice(want, tiles, units) : want.Offer;

            var afford = BotMarket.Affords(want, price, bot);

            if (afford <= 0 && price > want.Offer)
            {
                price = want.Offer;
                afford = BotMarket.Affords(want, price, bot);
            }

            units = Math.Min(units, afford);

            if (units <= 0)
            {
                Broke++;
                want.Passed(BotPass.Poor);

                continue;
            }

            var gold = units * price;
            var tail = false;

            if (gold < LeastGold)
            {
                if (TailExempt && want.Filled > 0 && units >= want.Payable)
                {
                    tail = true;
                }
                else
                {
                    Petty++;
                    want.Passed(BotPass.Petty);

                    continue;
                }
            }

            var served = BotPact.Serves(body, buyer) ? 1.25 : 1.0;
            var minutes = BotAppraisal.Travel(body.Location, buyer.Location) + BotDeliver.WorkMinutes;
            var rate = gold * served / Math.Max(0.25, minutes);

            if (rate > bestRate)
            {
                if (BotDanger.Road(body, map, body.Location, buyer.Location, strength, out _, out _, out _))
                {
                    RoadRefused++;
                    refusedRate = Math.Max(refusedRate, rate);
                    want.Passed(BotPass.Road);

                    continue;
                }

                best = want;
                bestUnits = units;
                bestRate = rate;
                bestTo = buyer.Location;
                bestPrice = price;
                bestFar = far;
                bestTail = tail;

                var inBox = _boxed.GetValueOrDefault(want.Kind);
                bestBoxed = Math.Min(inBox, Math.Max(0, units - (have - inBox)));
            }
        }

        if (best == null)
        {
            Covered++;

            return null;
        }

        Offered++;

        if (refusedRate > bestRate)
        {
            RoadSecond++;
        }

        if (bestFar)
        {
            FarOffered++;
            best.OfferedFar();
        }
        else
        {
            best.Offered();
        }

        if (bestTail)
        {
            TailOffered++;
        }

        if (bestBoxed > 0)
        {
            FromBox++;
        }

        return new BotDeliver(best, best.Kind, bestUnits, bestPrice, Math.Clamp(bestRate, LeastClaim, MostClaim), map, bestTo, bestBoxed, bestFar, bestTail);
    }

    public static int FarReach { get; set; } = 1200;

    public static int FarQuietMs { get; set; } = 600000;

    public static int FarLeastGold { get; set; } = 30;

    public static double FarGoldPerTile { get; set; } = 0.1;

    public static bool TailExempt { get; set; } = true;

    internal static bool FarOpen(BotWant want, int tiles, long now) =>
        FarReach > Reach && tiles <= FarReach && want.Shelf <= 0 && !want.ShelfPending && want.Worth >= FarLeastGold
        && want.Quiet(now, FarQuietMs);

    private static int FarPrice(BotWant want, int tiles, int units)
    {
        if (FarGoldPerTile <= 0.0 || tiles <= Reach)
        {
            return want.Offer;
        }

        var carriage = (int)Math.Ceiling(FarGoldPerTile * (tiles - Reach) / Math.Max(1, units));
        var top = want.Highest(BotMarket.MostMultiple);

        return Math.Max(want.Offer, Math.Min(top, want.Offer + carriage));
    }

    public static long FarOpened { get; private set; }

    public static long FarOffered { get; private set; }

    public static long RoadRefused { get; private set; }

    public static long RoadSecond { get; private set; }

    public static long TailOffered { get; private set; }

    public static long FarAnswerable { get; private set; }

    public static bool Deliverable(IBotWilful bot, Type kind)
    {
        if (!BotMarket.Running || kind == null || !BotMarket.Bespoke(kind))
        {
            return false;
        }

        var body = bot?.Self;
        var map = body?.Map;

        if (map == null || map == Map.Internal || BotMarket.Wanted(bot, kind) != null)
        {
            return false;
        }

        var wants = BotMarket.Wants;

        for (var i = 0; i < wants.Count; i++)
        {
            var want = wants[i];

            if (want.Kind != kind || !want.IsOpen || ReferenceEquals(want.Buyer, bot))
            {
                continue;
            }

            if (want.Buyer?.Self is { Deleted: false, Alive: true } buyer && buyer.Map == map
                && Utility.InRange(body.Location, buyer.Location, Reach) && BotGates.Joined(map, body.Location, buyer.Location))
            {
                return true;
            }
        }

        return false;
    }

    public static bool Answerable(IBotWilful supplier, BotWant want)
    {
        var body = supplier?.Self;
        var map = body?.Map;

        if (want is not { IsOpen: true } || map == null || map == Map.Internal || ReferenceEquals(want.Buyer, supplier))
        {
            return false;
        }

        if (want.Buyer?.Self is not { Deleted: false, Alive: true } buyer || buyer.Map != map)
        {
            return false;
        }

        var tiles = Math.Max(Math.Abs(buyer.X - body.X), Math.Abs(buyer.Y - body.Y));
        var near = tiles <= Reach;

        if (!near && !FarOpen(want, tiles, Core.TickCount) || !BotGates.Joined(map, body.Location, buyer.Location))
        {
            OutOfReach++;
            want.Passed(BotPass.Reach);

            return false;
        }

        if (!near)
        {
            FarAnswerable++;
            want.FarAsked();
        }

        return true;
    }

    public static bool Holding(IBotWilful maker, BotWant want)
    {
        if (want?.Kind == null || maker == null)
        {
            return false;
        }

        var have = BotHandover.Carried(maker, want.Kind);

        if (have <= 0)
        {
            return false;
        }

        if (BotUnload.Keeps(maker).TryGetValue(want.Kind, out var allowed))
        {
            have = allowed >= int.MaxValue ? 0 : Math.Max(0, have - allowed);
        }

        if (have < want.Payable)
        {
            return false;
        }

        AlreadyHeld++;

        return true;
    }

    public static long OutOfReach { get; private set; }

    public static long AlreadyHeld { get; private set; }

    public static string Describe() =>
        Looks == 0
            ? "nobody has been looked at for a delivery"
            : $"{Looks} looks: {Carrying} carried something somebody had asked for, {Offered} deliveries offered ({FromBox} of them out of the carrier's own bank box at a bank; {FarOffered} to a buyer past {Reach} tiles that nobody within them had answered for {FarQuietMs / 60000} min, of {FarOpened} such looks; {TailOffered} the last units of a want already part handed, under {LeastGold}gp; {RoadSecond} went to a lesser want because the best one's road was refused); passed over: {Own} wanted by the carrier itself, {Far} buyers past {Reach} tiles (and not open to a far walk: past {FarReach}, sold at a counter, worth under {FarLeastGold}gp or answered within reach lately), {Unjoined} on another land, {Sealed} beyond a way through, {Stranded} from a carrier standing in the dungeon block to a buyer outside its land, {Unreached} down the dungeon block where a walk or a haul from outside lately found no way, {Broke} buyers who could not pay for one, {Petty} worth under {LeastGold}gp, {RoadRefused} whose straight road asked more strength than the carrier brings ×{BotDanger.RoadDare:F1}; {Covered} looks found nothing left to carry that was not on its way already; makers' looks passed over {OutOfReach} wants whose buyer no carrier from there could reach and {AlreadyHeld} whose piece was already made and waiting for the walk, and took {FarAnswerable} past reach by the far rule";

    public static void Forget()
    {
        Looks = 0;
        Carrying = 0;
        Offered = 0;
        FromBox = 0;
        Own = 0;
        Covered = 0;
        Far = 0;
        Unjoined = 0;
        Sealed = 0;
        Stranded = 0;
        Unreached = 0;
        Broke = 0;
        Petty = 0;
        OutOfReach = 0;
        AlreadyHeld = 0;
        FarOpened = 0;
        FarOffered = 0;
        RoadRefused = 0;
        RoadSecond = 0;
        TailOffered = 0;
        FarAnswerable = 0;
    }
}
