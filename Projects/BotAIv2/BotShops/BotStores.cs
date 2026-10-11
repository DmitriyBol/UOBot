using System;
using System.Collections.Generic;
using Server.Items;
using Server.Logging;

namespace Server.BotAI.V2;

/// <summary>
/// A crafter short of the raw material of its trade, putting the order to the population.
///
/// <para>
/// <b>Two materials, and they are the two that no counter on this island sells at any price.</b> A feather
/// exists because somebody killed a bird; a hide exists because somebody killed a beast. Everything else a
/// crafter eats — cloth, logs, blank scrolls, reagents — is on a shelf, so a bot short of those has an
/// errand rather than a problem. Metal is the third of this kind and has had its own since 24.08.2026; see
/// <see cref="BotBullion"/>, which this is deliberately a copy of. The smith's question is <em>which</em>
/// metal and needs a file of its own; these two only ask whether.
/// </para>
///
/// <para>
/// <b>Without this the arrow chain has no seed and cannot start at all.</b> The links were all built and
/// every one of them was waiting on the one before it: a fletcher will not work without feathers, a hunter
/// values a bird only when the board is asking for what it carries — <see cref="BotQuarry.Sought"/> — and
/// the board only ever asked for arrows when nobody sold one, which is never, because every bowyer in
/// Britain keeps a stack. So the chain was a ring: 84 fletchers passed over for want of feathers in eleven
/// minutes, 194 woodcutters told nobody was asking, and in seven hours of logs the word "feather" does not
/// appear once. A crafter that can say "I need feathers" out loud is the cut in that ring.
/// </para>
///
/// <para>
/// <b>On the beat and not in the auction, and that is the difference between working and not.</b> Written
/// first as a proposer, it made the offer 62 times in six minutes and was chosen none of them: an order is
/// half a minute of paperwork worth perhaps twenty gold a minute, and it stood against a tailor's own bench
/// at three hundred and forty. That is the auction being right — Calla should sew — and it is also the
/// reason the trade she is sewing for can never be supplied. The same lesson is written out in
/// <see cref="BotUpkeep"/> about collecting goods already paid for, and in <see cref="BotOrder"/> in as many
/// words: <em>an errand that costs nothing and takes no time cannot win an auction against work that pays,
/// and it never did.</em> This lives on the population's beat for that reason.
/// </para>
///
/// <para>
/// <b>Logs were left out on purpose and the measurement took it back.</b> The reasoning was that a carpenter
/// sells them, so a fletcher short of wood has a shopping trip rather than a problem. It was wrong about this
/// island: once the feathers began arriving, 94 of 138 fletchers were passed over at 11:31 on 04.09.2026 with
/// "could not find wood" — <c>BotShops.Nearest(bot, typeof(Log))</c> answering null, because no carpenter
/// stands within any of their reach. So wood is the third thing nobody sells here, and it is asked for the
/// same way. It also opens the one trade on the shard that has never done a day's work: 323 woodcutters in
/// the same half hour, every one of them told that nobody was asking for wood or arrows.
/// </para>
///
/// <para>
/// Feathers first and wood second, for a fletcher that carries both gates. A fletcher with wood and no
/// feathers has nothing to make; a fletcher with feathers and no wood is one errand from working.
/// </para>
/// </summary>
public static class BotStores
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotStores));

    public static int Reserve
    {
        get => _reserve ?? BotPurse.KeepBack;
        set => _reserve = value;
    }

    private static int? _reserve;

    public static int Enough { get; set; } = 20;

    public static int Batch { get; set; } = 20;

    public static int MeatBatch { get; set; } = 5;

    public static bool SupplyGated { get; set; } = true;

    public static double StockShare { get; set; } = 0.5;

    public static int StockFloor { get; set; } = 30;

    public static int StockRoom(Type kind) => Math.Max(StockFloor, (int)(BotMarket.Supply(kind) * Math.Max(0.0, StockShare)));

    public static int MeatLeast { get; set; } = 3;

    public static double ThinShare { get; set; } = 0.5;

    public static bool Thin(Type kind) => ThinShare > 0 && BotMarket.Pace(kind) < BotMarket.Brought(kind) * ThinShare;

    public static long SizedToRoom { get; private set; }

    public static long SizedThin { get; private set; }

    public static long SizedUnits { get; private set; }

    public static bool TakeBackSated { get; set; } = true;

    public static long Sated { get; private set; }

    public static long SatedEarly { get; private set; }

    public static long SatedUnits { get; private set; }

    private static readonly Dictionary<Serial, long> _satedLook = [];

    private static void TakeBack(IBotWilful bot, Mobile body)
    {
        if (!TakeBackSated || BotOven.Kit(body) == null)
        {
            return;
        }

        var ribs = BotOven.Amount(body, typeof(RawRibs));

        if (ribs < BotOven.Worthwhile)
        {
            return;
        }

        var now = Core.TickCount;

        if (_satedLook.TryGetValue(body.Serial, out var last) && now - last < 60000)
        {
            return;
        }

        if (_satedLook.Count > 4096)
        {
            _satedLook.Clear();
        }

        _satedLook[body.Serial] = now;

        var want = BotMarket.Wanted(bot, typeof(RawRibs));

        if (want == null || want.Coming > 0 || ribs < want.Amount)
        {
            return;
        }

        var early = now - want.PostedTick < BotLines.GraceMs;
        var units = BotMarket.Withdrawn(bot, typeof(RawRibs), BotWithdraw.Have);

        if (units <= 0)
        {
            return;
        }

        Sated++;
        SatedUnits += units;

        if (early)
        {
            SatedEarly++;
        }

        logger.Information(
            "{Name} took back its want for {Units} Raw Ribs: it holds {Ribs} of its own, and its ribs are for other buyers now",
            body.Name,
            units,
            ribs
        );
    }

    public static long Outrun { get; private set; }

    public static long OutrunBots { get; private set; }

    public static long OutrunUnits { get; private set; }

    public static int HoldLapseMs { get; set; } = 300000;

    private static readonly Dictionary<(Serial Bot, Type Kind), long> _holding = [];

    public static int HeldNow()
    {
        var now = Core.TickCount;
        var held = 0;

        foreach (var (_, tick) in _holding)
        {
            if (now - tick < HoldLapseMs)
            {
                held++;
            }
        }

        return held;
    }

    public static long MealCapped { get; private set; }

    public static int Least { get; set; } = 5;

    public static int GuessFeather { get; set; } = 3;

    public static int GuessLeather { get; set; } = 6;

    public static int GuessLog { get; set; } = 3;

    public static int GuessGlass { get; set; } = 5;

    public static int GuessMeat { get; set; } = 3;

    public static double Prior { get; set; } = 20.0;

    private static bool _said;

    private static readonly Type[] _wanted = new Type[5];

    public static long Asked { get; private set; }

    public static long NoTrade { get; private set; }

    public static long Soon { get; private set; }

    public static long Stocked { get; private set; }

    public static long Shelved { get; private set; }

    public static long Standing { get; private set; }

    public static long Poor { get; private set; }

    public static long Ordered { get; private set; }

    public static long Richest { get; private set; }

    public static void Keep(IBotWilful bot)
    {
        var body = bot?.Self;
        var map = body?.Map;

        if (map == null || map == Map.Internal || !body.Alive)
        {
            return;
        }

        TakeBack(bot, body);

        var count = Materials(body, _wanted);

        if (count == 0)
        {
            NoTrade++;

            return;
        }

        if (!BotNeeds.Due(body, "stores"))
        {
            Soon++;

            return;
        }

        for (var i = 0; i < count; i++)
        {
            if (Ask(bot, body, _wanted[i]))
            {
                return;
            }
        }
    }

    private static bool Ask(IBotWilful bot, Mobile body, Type kind)
    {
        Asked++;

        if ((body.Backpack?.GetAmount(kind) ?? 0) >= Enough)
        {
            Stocked++;

            return false;
        }

        if (BotMarket.FromBank(bot, kind, Enough - (body.Backpack?.GetAmount(kind) ?? 0)) > 0)
        {
            Shelved++;

            return false;
        }

        if (BotMarket.Wanted(bot, kind) != null)
        {
            Standing++;

            return false;
        }

        var offer = BotMarket.Worth(kind, Guess(kind));

        var meal = BotOven.MealWorth(kind);
        var batch = Batch;
        var least = Least;
        var capped = false;
        var thin = false;

        if (meal > 0)
        {
            batch = MeatBatch;

            if (SupplyGated && MeatLeast < batch && Thin(kind))
            {
                batch = Math.Max(1, MeatLeast);
                least = Math.Min(least, batch);
                thin = true;
            }

            capped = offer > meal;

            if (capped)
            {
                offer = meal;
            }
        }

        var wealth = BotYield.Spendable(body);

        var afford = offer <= 0 ? 0 : (wealth - Reserve) / offer;
        var units = Math.Min(batch, afford);

        if (units < least)
        {
            Poor++;

            if (wealth > Richest)
            {
                Richest = wealth;
            }

            return false;
        }

        var left = meal > 0 && SupplyGated ? StockRoom(kind) - BotMarket.Wanting(kind) : int.MaxValue;
        var asked = units;

        if (units > left && left >= Math.Max(1, MeatLeast))
        {
            units = left;
        }

        if (meal > 0 && SupplyGated && units > left)
        {
            Outrun++;

            var key = (body.Serial, kind);
            var now = Core.TickCount;

            if (!_holding.TryGetValue(key, out var last) || now - last >= HoldLapseMs)
            {
                OutrunBots++;
                OutrunUnits += units;
            }

            if (_holding.Count > 4096)
            {
                _holding.Clear();
            }

            _holding[key] = now;

            return false;
        }

        _holding.Remove((body.Serial, kind));

        if (capped)
        {
            MealCapped++;
        }

        if (BotMarket.Ask(bot, kind, units, offer) == null)
        {
            Standing++;

            return false;
        }

        Ordered++;
        Once(body, kind);

        if (units < asked)
        {
            SizedToRoom++;
        }

        if (thin)
        {
            SizedThin++;
        }

        if (meal > 0 && units < MeatBatch && (thin || units < asked))
        {
            SizedUnits += MeatBatch - units;
        }

        return true;
    }

    private static int Materials(Mobile body, Type[] into)
    {
        var count = 0;

        if (BotFletching.Kit(body) != null)
        {
            into[count++] = typeof(Feather);

            if (BotFletching.Logs(body) + BotFletching.Shafts(body) < Enough)
            {
                into[count++] = typeof(Log);
            }
        }

        if (BotThread.Kit(body) != null)
        {
            into[count++] = typeof(Leather);
        }

        if (BotOven.Kit(body) != null && BotOven.Amount(body, typeof(RawRibs)) < BotOven.Worthwhile)
        {
            into[count++] = typeof(RawRibs);
        }

        return count;
    }

    private static int Guess(Type kind)
    {
        if (kind == typeof(Feather))
        {
            return GuessFeather;
        }

        if (kind == typeof(Log))
        {
            return GuessLog;
        }

        if (kind == typeof(RawRibs))
        {
            return GuessMeat;
        }

        return kind == typeof(Bottle) ? GuessGlass : GuessLeather;
    }

    private static void Once(Mobile body, Type kind)
    {
        if (_said)
        {
            return;
        }

        _said = true;

        logger.Information(
            "{Name} is the first crafter on this shard ever to ask the population for {Material}: until now the only way one could enter the world was a bot happening to kill the right animal",
            body.Name,
            kind.Name
        );
    }

    public static string Describe() =>
        Asked == 0
            ? $"no crafter has been asked about its materials ({NoTrade} answers went to bots with neither kit)"
            : $"{Asked} asked about a feather, a log, a hide or a rib: {Ordered} put the order to the population, {Standing} already have one out, "
              + $"{Stocked} have {Enough} already, {Shelved} took their own out of their bank box at a bank instead, "
              + $"{Poor} cannot afford one (the fattest purse among them held {Richest}gp); {Soon} crafters came again inside the minute (Asked counts materials, this counts bots); "
              + $"{MealCapped} asks for raw meat brought down to what the meal is worth (a cook orders {MeatBatch} at a time), "
              + $"{OutrunBots} bots held back from asking for {OutrunUnits} units ({Outrun} asks counting each minute's retry, {HeldNow()} held now) while the board already asked for more than {StockShare:P0} of what the island brings an hour (RawRibs now: {BotMarket.Wanting(typeof(RawRibs))} asked by buyers in the world against room for {StockRoom(typeof(RawRibs))}, {BotMarket.Brought(typeof(RawRibs))} brought in the hour, {BotMarket.Pace(typeof(RawRibs))} an hour at the pace of the last quarter-hour (half-life {BotMarket.SupplyHalfLifeMs / 60000} min; the room is sized to the lesser{(Thin(typeof(RawRibs)) ? ", and the pace is thin" : "")}), floor {StockFloor}{(SupplyGated ? "" : "; off")}); "
              + $"{SizedToRoom + SizedThin} raw meat lines said smaller than {MeatBatch} ({SizedToRoom} to the room left on the board, {SizedThin} to {MeatLeast} while the pace ran under {ThinShare:P0} of the hour's; {SizedUnits} units fewer); "
              + $"{Sated} raw meat wants taken back because their buyer held what they asked for from its own hunting ({SatedUnits} units; {SatedEarly} inside the want's first {BotLines.GraceMs / 60000} min, kept out, the rest counted failed{(TakeBackSated ? "" : "; off")})";

    public static void Forget()
    {
        _said = false;
        Asked = 0;
        NoTrade = 0;
        Stocked = 0;
        Shelved = 0;
        Standing = 0;
        Poor = 0;
        Soon = 0;
        Ordered = 0;
        Richest = 0;
        MealCapped = 0;
        Outrun = 0;
        OutrunBots = 0;
        OutrunUnits = 0;
        _holding.Clear();
        SizedToRoom = 0;
        SizedThin = 0;
        SizedUnits = 0;
        Sated = 0;
        SatedEarly = 0;
        SatedUnits = 0;
        _satedLook.Clear();
    }
}
