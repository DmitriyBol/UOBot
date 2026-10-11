using System;
using System.Collections.Generic;
using Server.Items;
using Server.Logging;

namespace Server.BotAI.V2;

/// <summary>One tavern while it is open: the cook keeping it, the inn it stands in, the table round it, and what it has sold.</summary>
public sealed class BotTavern
{
    public BotTavern(BotMobile keeper, BotInns.Inn inn, Map map, Point3D stand)
    {
        Keeper = keeper;
        Inn = inn;
        Map = map;
        Stand = stand;
        OpenedTick = Core.TickCount;
        NextLineTick = OpenedTick;
    }

    public BotMobile Keeper { get; }

    public BotInns.Inn Inn { get; }

    public Map Map { get; }

    public Point3D Stand { get; }

    public long OpenedTick { get; }

    public int Meals { get; internal set; }

    public long CountedTick { get; internal set; }

    public int Sold { get; internal set; }

    public int Takings { get; internal set; }

    public int Served { get; internal set; }

    public bool Shut { get; internal set; }

    public long NextLineTick { get; internal set; }

    public Mobile LastSpeaker { get; internal set; }

    public int Lines { get; internal set; }

    internal readonly Dictionary<Serial, (BotMobile Bot, long Seen, bool Greeted)> Table = [];

    public string Name => $"{Keeper?.Name}'s table at {Inn?.Name}";

    public int SeatedCount(long now)
    {
        var n = 0;

        foreach (var (_, seat) in Table)
        {
            if (now - seat.Seen <= 5000 && seat.Bot is { Deleted: false, Alive: true })
            {
                n++;
            }
        }

        return n;
    }
}

/// <summary>
/// The taverns: a cook keeping one at an inn, the meals it sells there, the guests at its table, and the hooks by which the rest's
/// clock, the inns and the lodging errand learn that a tavern is open (07.10.2026, trades17).
///
/// <para>
/// <b>A tavern is a fire indoors, with supper.</b> A guest who has bought and eaten a meal at the table (the cook's own cooking,
/// pack to pack, at the market's price for it: <see cref="BotHandover.Exchange"/>) sits there, and while it sits its play is counted
/// backwards at <see cref="RestShare"/> of the inn's rate — more than a campfire's one, because it has eaten (the meal itself doubles
/// what it regenerates, <c>BotMeal</c>) — and it regains <see cref="RegenPoints"/> of hits, stamina and mana every
/// <see cref="RegenEveryMs"/> on top. Round the table, with two or more seated, the campfire's talk is said (<c>BotCampTalk</c>).
/// </para>
///
/// <para>
/// <b>And the tired eat before they sleep.</b> A tired bot sent to an inn is sent to an inn with an open tavern when one is within
/// <see cref="Detour"/> tiles of the nearest inn (<see cref="InnFor"/>); arriving, it buys supper at the table before it pays for the
/// bed (<see cref="Supper"/>), and a night after supper buys <see cref="SupperBuff"/> times the rested regeneration on its return
/// (<see cref="SupperFactor"/>, read off the rest record's inn name, which is saved, so a restart neither loses it nor grants it).
/// </para>
/// </summary>
public static class BotTaverns
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotTaverns));

    public const string SupperMark = " after supper";

    public static int MostOpen { get; set; } = 6;

    public static double ShiftMinutes { get; set; } = 20.0;

    public static int RestMs { get; set; } = 1200000;

    public static int LeastMeals { get; set; } = 4;

    public static int StockMeals { get; set; } = 15;

    public static int KeeperReach { get; set; } = 150;

    public static int Spacing { get; set; } = 40;

    public static int GuestReach { get; set; } = 100;

    public static int MostGuests { get; set; } = 8;

    public static double GuestMinutes { get; set; } = 5.0;

    public static int WalkMs { get; set; } = 240000;

    public static double Markup { get; set; } = 1.0;

    public static int GuestKeeps { get; set; } = 100;

    public static double RestShare { get; set; } = 1.5;

    public static int RegenPoints { get; set; } = 2;

    public static int RegenEveryMs { get; set; } = 4000;

    public static int Detour { get; set; } = 60;

    public static int SupperWalkMs { get; set; } = 60000;

    public static double SupperBuff { get; set; } = 1.5;

    public static int SupperWindowMs { get; set; } = 1800000;

    public static int CryMs { get; set; } = 90000;

    public static int LineMs { get; set; } = 9000;

    public static long Opened { get; private set; }

    public static long Closed { get; private set; }

    public static long SoldOut { get; private set; }

    public static long TimeUp { get; private set; }

    public static long ClosedAway { get; private set; }

    public static long Meals { get; private set; }

    public static long Gold { get; private set; }

    public static long Rests { get; private set; }

    public static double GivenBack { get; private set; }

    public static double SatMinutes { get; private set; }

    public static long RegenGiven { get; private set; }

    public static long Steered { get; private set; }

    public static long Lodgers { get; private set; }

    public static long LodgersNot { get; private set; }

    public static long SupperBuffs { get; private set; }

    public static long Lines { get; private set; }

    public static long Shifts { get; private set; }

    public static long Shown { get; private set; }

    public static long Taken { get; private set; }

    private static readonly List<BotTavern> _open = [];

    private static readonly Dictionary<Serial, BotTavern> _seated = [];

    private static readonly Dictionary<Serial, long> _supped = [];

    private static readonly Dictionary<Serial, long> _regenTick = [];

    private static readonly List<BotMobile> _talkers = [];

    private static readonly Type[] _kinds = [typeof(Ribs), typeof(LambLeg), typeof(CookedBird), typeof(ChickenLeg), typeof(FishSteak)];

    public static IReadOnlyList<BotTavern> Open => _open;

    public static double Share => Shifts < 5 || Shown <= 0 ? 0.5 : Math.Clamp((double)Taken / Shown, 0.05, 1.0);

    public static int MealsIn(Container box)
    {
        if (box == null)
        {
            return 0;
        }

        var n = 0;

        for (var i = 0; i < _kinds.Length; i++)
        {
            n += box.GetAmount(_kinds[i]);
        }

        return n;
    }

    public static Type MostHeld(Mobile cook, out int units)
    {
        units = 0;
        Type best = null;
        var pack = cook?.Backpack;

        if (pack == null)
        {
            return null;
        }

        for (var i = 0; i < _kinds.Length; i++)
        {
            var n = pack.GetAmount(_kinds[i]);

            if (n > units)
            {
                units = n;
                best = _kinds[i];
            }
        }

        return best;
    }

    public static IReadOnlyList<Type> Kinds => _kinds;

    public static int Price(Type kind) =>
        kind == null ? 0 : Math.Max(1, (int)Math.Round(BotMarket.Worth(kind, BotOven.Worth) * Math.Max(0.1, Markup)));

    public static double Claim(int meals, int price) =>
        Math.Clamp(Math.Min(meals, StockMeals) * Math.Max(1, price) * Share / Math.Max(1.0, ShiftMinutes), 4.0, 120.0);

    public static BotTavern At(BotInns.Inn inn)
    {
        if (inn == null)
        {
            return null;
        }

        for (var i = 0; i < _open.Count; i++)
        {
            if (ReferenceEquals(_open[i].Inn, inn) && !_open[i].Shut)
            {
                return _open[i];
            }
        }

        return null;
    }

    public static bool Near(Point3D at)
    {
        for (var i = 0; i < _open.Count; i++)
        {
            if (!_open[i].Shut && Utility.InRange(_open[i].Stand, at, Spacing))
            {
                return true;
            }
        }

        return false;
    }

    public static BotInns.Inn FreeInn(Mobile cook)
    {
        BotInns.Inn best = null;
        var nearest = int.MaxValue;
        var inns = BotInns.All;

        for (var i = 0; i < inns.Count; i++)
        {
            var inn = inns[i];
            var away = Math.Max(Math.Abs(inn.Spot.X - cook.X), Math.Abs(inn.Spot.Y - cook.Y));

            if (away > KeeperReach || away >= nearest || Near(inn.Spot))
            {
                continue;
            }

            best = inn;
            nearest = away;
        }

        return best;
    }

    internal static BotTavern OpenAt(BotMobile keeper, BotInns.Inn inn, Map map)
    {
        if (keeper == null || inn == null || At(inn) != null || Near(inn.Spot) || _open.Count >= MostOpen)
        {
            return null;
        }

        var tavern = new BotTavern(keeper, inn, map, keeper.Location);

        Count(tavern, Core.TickCount);
        _open.Add(tavern);
        Opened++;

        logger.Information(
            "{Name} opened a tavern at {Inn} ({X}, {Y}) with {Meals} meals, {Kind} at {Price}gp: a guest within {Reach} tiles is asked to supper, a tired bot sleeps there after it",
            keeper.Name,
            inn.Name,
            keeper.X,
            keeper.Y,
            tavern.Meals,
            MostHeld(keeper, out _)?.Name ?? "nothing",
            Price(MostHeld(keeper, out _)),
            GuestReach
        );

        BotEvents.Post("tavern", keeper, $"opened a tavern at {inn.Name} with {tavern.Meals} meals");
        Say(keeper, "tavern:open", null, tavern, "trade");

        return tavern;
    }

    internal static void Close(BotTavern tavern, string why, bool soldOut, bool timeUp, int shown)
    {
        if (tavern == null || tavern.Shut)
        {
            return;
        }

        tavern.Shut = true;
        _open.Remove(tavern);

        foreach (var (serial, _) in tavern.Table)
        {
            _seated.Remove(serial);
        }

        tavern.Table.Clear();
        Closed++;
        if (shown > 0)
        {
            Shifts++;
            Shown += shown;
            Taken += tavern.Sold;
        }

        if (soldOut)
        {
            SoldOut++;
        }
        else if (timeUp)
        {
            TimeUp++;
        }
        else
        {
            ClosedAway++;
        }

        var minutes = (Core.TickCount - tavern.OpenedTick) / 60000.0;

        logger.Information(
            "{Name} shut its tavern at {Inn} after {Minutes:F1} minutes — {Why}: {Sold} meals sold to {Served} guests for {Gold}gp, {Lines} lines said at the table",
            tavern.Keeper?.Name,
            tavern.Inn?.Name,
            minutes,
            why,
            tavern.Sold,
            tavern.Served,
            tavern.Takings,
            tavern.Lines
        );

        if (tavern.Keeper is { Deleted: false, Alive: true } keeper)
        {
            Say(keeper, "tavern:close", null, tavern);
        }
    }

    internal static void Count(BotTavern tavern, long now)
    {
        tavern.Meals = MealsIn(tavern.Keeper?.Backpack);
        tavern.CountedTick = now;
    }

    public static BotTavern Nearest(BotMobile guest, long now, out string refusal)
    {
        refusal = "far";
        BotTavern best = null;
        var nearest = int.MaxValue;

        for (var i = 0; i < _open.Count; i++)
        {
            var tavern = _open[i];

            if (tavern.Shut || tavern.Map != guest.Map || ReferenceEquals(tavern.Keeper, guest))
            {
                continue;
            }

            var away = Math.Max(Math.Abs(tavern.Stand.X - guest.X), Math.Abs(tavern.Stand.Y - guest.Y));

            if (away > GuestReach || away >= nearest)
            {
                continue;
            }

            if (tavern.SeatedCount(now) >= MostGuests)
            {
                refusal = "full";

                continue;
            }

            if (!BotCamp.Welcome(guest, tavern.Keeper))
            {
                refusal = "unwelcome";

                continue;
            }

            if (!BotGates.Joined(guest.Map, guest.Location, tavern.Stand))
            {
                continue;
            }

            best = tavern;
            nearest = away;
        }

        return best;
    }

    internal static int Serve(BotTavern tavern, BotMobile guest, out Type meal, out string refused)
    {
        meal = null;
        refused = null;

        if (tavern?.Keeper is not { Deleted: false, Alive: true } keeper || tavern.Shut)
        {
            refused = "the tavern was shut";

            return 0;
        }

        meal = MostHeld(keeper, out var units);

        if (meal == null || units <= 0)
        {
            refused = "the cook had no meal left";

            return 0;
        }

        var price = Price(meal);

        if (BotYield.Spendable(guest) < price)
        {
            refused = $"it could not pay {price}gp";

            return 0;
        }

        var given = BotHandover.Exchange(keeper, guest, meal, 1, price, false, $"supper at {tavern.Name}", out var paid, out refused);

        if (given <= 0)
        {
            return 0;
        }

        BotMeal.Keep(guest);

        BotCharters.After(keeper, tavern.Stand, meal, given, paid);

        _supped[guest.Serial] = Core.TickCount;

        tavern.Sold += given;
        tavern.Takings += paid;
        tavern.Served++;
        Meals += given;
        Gold += paid;

        Count(tavern, Core.TickCount);

        return paid;
    }

    internal static void Sit(BotTavern tavern, BotMobile guest, long now)
    {
        if (tavern == null || guest == null)
        {
            return;
        }

        tavern.Table[guest.Serial] = (guest, now, false);
        _seated[guest.Serial] = tavern;
        Rests++;
    }

    internal static void Seated(BotTavern tavern, BotMobile guest, long now)
    {
        if (tavern == null || guest == null || !tavern.Table.TryGetValue(guest.Serial, out var seat))
        {
            return;
        }

        tavern.Table[guest.Serial] = (guest, now, seat.Greeted);

        if (RegenPoints <= 0 || _regenTick.TryGetValue(guest.Serial, out var last) && now - last < RegenEveryMs)
        {
            return;
        }

        _regenTick[guest.Serial] = now;

        if (guest.Hits < guest.HitsMax)
        {
            guest.Hits = Math.Min(guest.HitsMax, guest.Hits + RegenPoints);
            RegenGiven += RegenPoints;
        }

        if (guest.Stam < guest.StamMax)
        {
            guest.Stam = Math.Min(guest.StamMax, guest.Stam + RegenPoints);
            RegenGiven += RegenPoints;
        }

        if (guest.Mana < guest.ManaMax)
        {
            guest.Mana = Math.Min(guest.ManaMax, guest.Mana + RegenPoints);
            RegenGiven += RegenPoints;
        }
    }

    internal static void Rise(BotTavern tavern, BotMobile guest, double minutes)
    {
        if (guest == null)
        {
            return;
        }

        if (tavern != null && tavern.Table.Remove(guest.Serial))
        {
            SatMinutes += Math.Max(0.0, minutes);
        }

        _seated.Remove(guest.Serial);
        _regenTick.Remove(guest.Serial);
    }

    public static double? Played(BotMobile bot, double minutes, double hours)
    {
        if (!TavernsOnAt(bot, out _) || bot.Tired || minutes <= 0.0)
        {
            return null;
        }

        var rest = Math.Max(0.1, (BotRest.LeastRestHours + BotRest.MostRestHours) / 2.0);
        var back = minutes * Math.Max(0.0, hours) / rest * Math.Max(0.0, RestShare);

        GivenBack += back;

        return -back;
    }

    private static bool TavernsOnAt(BotMobile bot, out BotTavern tavern)
    {
        tavern = null;

        if (bot == null || !_seated.TryGetValue(bot.Serial, out tavern) || tavern.Shut
            || !tavern.Table.TryGetValue(bot.Serial, out var seat))
        {
            return false;
        }

        return Core.TickCount - seat.Seen <= 5000;
    }

    public static BotInns.Inn InnFor(BotMobile bot, BotInns.Inn nearest, int reach)
    {
        if (!BotTrades.TavernsOn || bot == null || _open.Count == 0)
        {
            return null;
        }

        var limit = nearest == null ? reach : Math.Min(reach, Math.Max(Math.Abs(nearest.Spot.X - bot.X), Math.Abs(nearest.Spot.Y - bot.Y)) + Detour);
        BotTavern best = null;
        var closest = int.MaxValue;

        for (var i = 0; i < _open.Count; i++)
        {
            var tavern = _open[i];

            if (tavern.Shut || tavern.Map != bot.Map || ReferenceEquals(tavern.Keeper, bot) || !BotCamp.Welcome(bot, tavern.Keeper))
            {
                continue;
            }

            var away = Math.Max(Math.Abs(tavern.Stand.X - bot.X), Math.Abs(tavern.Stand.Y - bot.Y));

            if (away <= limit && away < closest)
            {
                best = tavern;
                closest = away;
            }
        }

        if (best == null || ReferenceEquals(best.Inn, nearest))
        {
            return null;
        }

        Steered++;

        return best.Inn;
    }

    public static BotMobile KeeperAt(BotInns.Inn inn) =>
        BotTrades.TavernsOn && At(inn) is { Keeper: { Deleted: false, Alive: true } keeper } ? keeper : null;

    public static bool Supper(BotMobile bot, BotInns.Inn inn)
    {
        var tavern = At(inn);

        if (tavern == null || bot == null)
        {
            return false;
        }

        if (BotMeal.IsFed(bot))
        {
            LodgersNot++;

            return false;
        }

        var paid = Serve(tavern, bot, out var meal, out var refused);

        if (paid <= 0)
        {
            LodgersNot++;

            logger.Information("{Name} went to bed at {Inn} without supper at {Tavern}: {Why}", bot.Name, inn?.Name, tavern.Name, refused);

            return false;
        }

        Lodgers++;

        logger.Information(
            "{Name} ate {Meal} at {Tavern} for {Paid}gp before bed, and will sleep the better for it",
            bot.Name,
            meal?.Name,
            tavern.Name,
            paid
        );

        Say(bot, "tavern:lodger", tavern.Keeper, tavern);

        return true;
    }

    public static void NoSupper() => LodgersNot++;

    public static string Supped(BotMobile bot, string inn)
    {
        if (bot == null || inn == null || !_supped.Remove(bot.Serial, out var when) || Core.TickCount - when > SupperWindowMs)
        {
            return inn;
        }

        return inn + SupperMark;
    }

    public static double SupperFactor(string inn)
    {
        if (inn == null || !inn.EndsWith(SupperMark, StringComparison.Ordinal))
        {
            return 1.0;
        }

        SupperBuffs++;

        return Math.Max(1.0, SupperBuff);
    }

    internal static void Talk(long now)
    {
        for (var i = 0; i < _open.Count; i++)
        {
            var tavern = _open[i];

            if (tavern.Shut || now - tavern.NextLineTick < 0)
            {
                continue;
            }

            tavern.NextLineTick = now + Math.Max(2000, LineMs) + Utility.Random(4000);

            _talkers.Clear();

            foreach (var (_, seat) in tavern.Table)
            {
                if (now - seat.Seen <= 5000 && seat.Bot is { Deleted: false, Alive: true })
                {
                    _talkers.Add(seat.Bot);
                }
            }

            if (_talkers.Count == 0)
            {
                continue;
            }

            var first = Utility.Random(_talkers.Count);
            BotMobile speaker = null;

            for (var k = 0; k < _talkers.Count && speaker == null; k++)
            {
                var pick = _talkers[(first + k) % _talkers.Count];

                if (!ReferenceEquals(pick, tavern.LastSpeaker) || _talkers.Count == 1)
                {
                    speaker = pick;
                }
            }

            BotMobile listener = null;

            for (var k = 0; k < _talkers.Count && listener == null; k++)
            {
                if (!ReferenceEquals(_talkers[k], speaker))
                {
                    listener = _talkers[k];
                }
            }

            listener ??= tavern.Keeper;

            var fill = new Dictionary<string, string>();
            var topic = BotCampTalk.Topic(speaker, tavern.Map, tavern.Stand, fill, "tavern:small");

            if (BotCampTalk.Say(speaker, topic, "tavern:small", listener, fill))
            {
                tavern.Lines++;
                tavern.LastSpeaker = speaker;
                Lines++;
            }
        }
    }

    internal static bool Say(Mobile who, string occasion, Mobile to, BotTavern tavern, string channel = "local")
    {
        if (who == null)
        {
            return false;
        }

        var meal = tavern?.Keeper == null ? null : MostHeld(tavern.Keeper, out _);
        var fill = new Dictionary<string, string>
        {
            ["inn"] = tavern?.Inn?.Name ?? "the inn",
            ["cook"] = tavern?.Keeper?.Name ?? "the cook",
            ["meal"] = meal == null ? "supper" : BotShopkeep.Words(meal),
            ["price"] = meal == null ? "a few" : Price(meal).ToString()
        };

        if (channel == "local")
        {
            return BotCampTalk.Say(who, occasion, null, to, fill);
        }

        fill["other"] = to?.Name ?? "friend";

        var line = BotVoice.Phrase(occasion, null, who, fill);

        return line != null && BotVoice.Say(who, channel, line, topic: "tavern");
    }

    public static Dictionary<string, string[]> Phrases() =>
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["took:tavern"] = ["Supper to serve tonight.", "Off to keep the tavern.", "The inn's kitchen is mine for the evening."],
            ["finished:tavern"] = ["That's the kitchen shut.", "A good night at the tavern.", "Washing up. Done for tonight."],
            ["took:supper"] = ["Supper at the inn, I think.", "I could eat. The tavern's open.", "A hot meal and a sit down."],
            ["finished:supper"] = ["Good supper, that. Back to it.", "Fed and rested. Thank you, cook.", "Right. On my way."],
            ["tavern:open"] = ["Supper's on at {inn}! {meal}, {price} gold.", "Kitchen's open at {inn}. Come and eat.", "Hot {meal} at {inn}, {price} gold a plate."],
            ["tavern:cry"] = ["Hot {meal}, {price} gold!", "Sit down, eat something.", "There's room at the table."],
            ["tavern:close"] = ["That's the kitchen closed. Safe roads.", "No more tonight, friends.", "Kitchen's shut. Thank you all."],
            ["tavern:greet"] = ["Evening, {other}. Something hot, please.", "A plate of whatever's good, {other}.", "I've walked a long way for this, {other}."],
            ["tavern:lodger"] = ["A bite to eat, then bed.", "Supper first, then sleep.", "Something warm before I turn in, {other}."],
            ["tavern:small"] = ["Good food, this.", "My feet thank me.", "Warm in here.", "Another day done.", "Pass the salt.", "Best meal I've had all week."]
        };

    public static string Describe()
    {
        var now = Core.TickCount;
        var seated = 0;

        for (var i = 0; i < _open.Count; i++)
        {
            seated += _open[i].SeatedCount(now);
        }

        return $"taverns {_open.Count} open now ({seated} at table): {Opened} opened, {Closed} shut ({SoldOut} sold out, {TimeUp} ran their {ShiftMinutes:F0} minutes, {ClosedAway} shut when the cook was taken away); "
               + $"{Meals} meals sold for {Gold}gp, sell-through {Share:P0} over {Shifts} shifts; {Rests} guests sat at a table after supper ({SatMinutes:F0} minutes, {GivenBack:F0} minutes of play given back, {RegenGiven} points regained); "
               + $"{Steered} tired bots sent to an inn with a tavern, {Lodgers} ate before bed and {LodgersNot} went without, {SupperBuffs} woke to the supper's longer buff; {Lines} lines said at table; "
               + $"{BotTaverner.Describe()}; {BotSupperer.Describe()}";
    }

    public static void Forget()
    {
        for (var i = _open.Count - 1; i >= 0; i--)
        {
            _open[i].Shut = true;
        }

        _open.Clear();
        _seated.Clear();
        _supped.Clear();
        _regenTick.Clear();
        Opened = 0;
        Closed = 0;
        SoldOut = 0;
        TimeUp = 0;
        ClosedAway = 0;
        Meals = 0;
        Gold = 0;
        Rests = 0;
        GivenBack = 0;
        SatMinutes = 0;
        RegenGiven = 0;
        Steered = 0;
        Lodgers = 0;
        LodgersNot = 0;
        SupperBuffs = 0;
        Lines = 0;
        Shifts = 0;
        Shown = 0;
        Taken = 0;
    }
}
