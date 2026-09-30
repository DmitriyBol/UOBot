using System;
using System.Collections.Generic;
using Server.Engines.Spawners;
using Server.Items;
using Server.Logging;
using Server.Mobiles;
using Server.Regions;

namespace Server.BotAI.V2;

/// <summary>
/// The inns and taverns of the map, and what a bot gets for sleeping in one.
///
/// <para>
/// <b>Patrick's order of 29.09.2026, point two: "bots cannot simply go offline; they should leave from a tavern or an
/// inn. Whoever goes to rest from an inn and pays for it gets a bonus to positive thought and an extra regeneration buff
/// for a third of the time spent resting — rest sixty minutes, the buff lasts twenty."</b> Until now a tired bot logged
/// out where it stood (<see cref="BotRest.Leave"/>), which is what a player does when the connection drops and not what
/// a player does at the end of an evening.
/// </para>
///
/// <para>
/// <b>The inns are read from the world, two ways.</b> The engine's own word for an inn is a region with
/// <c>NoLogoutDelay</c> — the room a player logs out of at once — and every town has one; and every tavern has a
/// <c>TavernKeeper</c> or an <c>Innkeeper</c> standing in it, put there by a spawner whose own tile is the floor. Both
/// are read once, the way the dungeons' rooms are read from their spawners: a table of inns would be a promise about a
/// map. Britain has four taverns and one inn by this count; Trinsic, Minoc and Vesper one or two each.
/// </para>
///
/// <para>
/// <b>Paying is what earns the bonus, and the price is a night's, not a fortune.</b> <see cref="PricePerHour"/> gold for
/// each hour of the rest, taken from the purse and then the bank; a bot that cannot pay still sleeps in the inn and gets
/// nothing for it but the roof. The bonus is two things: the day's boredom falls by <see cref="Cheer"/> (the same relief
/// a hundred gold of takings gives, times two — see <c>BotUrges.Paid</c>), so the mood the dashboard shows rises; and
/// for <see cref="BuffShare"/> of the rest, hits, stamina and mana come back <see cref="RegenPoints"/> every
/// <see cref="RegenEveryMs"/> on top of the engine's own rate (<see cref="Regen"/>, from the bot's beat).
/// </para>
/// </summary>
public static class BotInns
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotInns));

    /// <summary>One inn or tavern: a floor tile inside it, and what it is called for the log.</summary>
    public sealed class Inn
    {
        public Point3D Spot;

        public string Name;

        public bool NoLogoutDelay;

        public long Lodged;

        public override string ToString() => $"{Name} at ({Spot.X}, {Spot.Y})";
    }

    public static bool Running { get; set; } = true;

    public static int Reach { get; set; } = 400;

    public static int PricePerHour { get; set; } = 5;

    public static double BuffShare { get; set; } = 1.0 / 3.0;

    public static double Cheer { get; set; } = 200.0;

    public static int RegenPoints { get; set; } = 2;

    public static int RegenEveryMs { get; set; } = 4000;

    public static int Near { get; set; } = 8;

    public static int RetryMs { get; set; } = 600000;

    private static readonly List<Inn> _inns = [];

    private static Map _map;

    private static readonly Dictionary<Serial, (Inn Inn, int Paid, double Hours)> _paid = [];

    private static readonly Dictionary<Serial, long> _sent = [];

    private static readonly Dictionary<Serial, long> _regenTick = [];

    public static bool Surveyed { get; private set; }

    public static int Inns => _inns.Count;

    public static long SentToInn { get; private set; }

    public static long LodgedPaid { get; private set; }

    public static long LodgedUnpaid { get; private set; }

    public static long LeftOutside { get; private set; }

    public static long AtHome { get; private set; }

    public static void NotedAtHome() => AtHome++;

    public static long NoInnNear { get; private set; }

    public static long GoldPaid { get; private set; }

    public static long Buffed { get; private set; }

    public static long RegenPointsGiven { get; private set; }

    public static IReadOnlyList<Inn> All => _inns;

    public static void Survey(Map map)
    {
        if (Surveyed || map == null || map == Map.Internal)
        {
            return;
        }

        Surveyed = true;
        _map = map;

        var regions = 0;
        var keepers = 0;

        foreach (var region in Region.Regions)
        {
            if (region is not BaseRegion { NoLogoutDelay: true } inn || inn.Map != map || inn.Area == null)
            {
                continue;
            }

            for (var i = 0; i < inn.Area.Length; i++)
            {
                var box = inn.Area[i];
                var x = box.Start.X + box.Width / 2;
                var y = box.Start.Y + box.Height / 2;

                if (Place(map, x, y, out var spot))
                {
                    var town = !string.IsNullOrWhiteSpace(inn.Name) ? inn.Name : inn.Parent?.Name ?? Region.Find(spot, map)?.Name ?? "the town";

                    _inns.Add(new Inn { Spot = spot, Name = $"the inn of {town}", NoLogoutDelay = true });
                    regions++;
                }
            }
        }

        foreach (var spawner in map.GetItemsInBounds<BaseSpawner>(new Rectangle2D(0, 0, map.Width, map.Height)))
        {
            if (spawner is not { Deleted: false } || spawner.Map != map || spawner.Entries == null)
            {
                continue;
            }

            var keeps = false;

            for (var i = 0; i < spawner.Entries.Count && !keeps; i++)
            {
                var named = spawner.Entries[i]?.SpawnedName;

                keeps = named is "TavernKeeper" or "Innkeeper";
            }

            if (!keeps || Nearest(spawner.Location, Near) != null || !Place(map, spawner.X, spawner.Y, out var spot))
            {
                continue;
            }

            var town = Region.Find(spot, map)?.Name;

            _inns.Add(new Inn { Spot = spot, Name = town == null ? "a tavern" : $"a tavern in {town}" });
            keepers++;
        }

        logger.Information(
            "Inns: {Inns} on {Map} — {Regions} from the engine's inn regions and {Keepers} from tavern keepers' spawners; a tired bot within {Reach} tiles of one goes there to rest, {Price}gp an hour, and a paid night buys {Share:P0} of the rest as regeneration ({Points} points every {Every}s) and {Cheer} of cheer",
            _inns.Count,
            map.Name,
            regions,
            keepers,
            Reach,
            PricePerHour,
            BuffShare,
            RegenPoints,
            RegenEveryMs / 1000,
            Cheer
        );
    }

    private static bool Place(Map map, int x, int y, out Point3D spot)
    {
        for (var r = 0; r <= 3; r++)
        {
            for (var dx = -r; dx <= r; dx++)
            {
                for (var dy = -r; dy <= r; dy++)
                {
                    if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != r)
                    {
                        continue;
                    }

                    if (BotStep.Settle(map, x + dx, y + dy, out var z) && map.CanSpawnMobile(x + dx, y + dy, z))
                    {
                        spot = new Point3D(x + dx, y + dy, z);

                        return true;
                    }
                }
            }
        }

        spot = Point3D.Zero;

        return false;
    }

    public static Inn Nearest(Point3D at, int reach)
    {
        Inn best = null;
        var nearest = int.MaxValue;

        for (var i = 0; i < _inns.Count; i++)
        {
            var inn = _inns[i];
            var away = Math.Max(Math.Abs(inn.Spot.X - at.X), Math.Abs(inn.Spot.Y - at.Y));

            if (away <= reach && away < nearest)
            {
                best = inn;
                nearest = away;
            }
        }

        return best;
    }

    public static Inn At(Mobile bot) => bot?.Map == null || bot.Map == Map.Internal ? null : Nearest(bot.Location, Near);

    public static bool ToInn(BotMobile bot, double hours)
    {
        if (!Running || bot?.Map == null || bot.Map == Map.Internal)
        {
            return false;
        }

        Survey(bot.Map);

        if (At(bot) is { } here)
        {
            if (!_paid.ContainsKey(bot.Serial))
            {
                var paid = Pay(bot, hours);

                if (paid == 0)
                {
                    BotLodge.NotedUnpaid();
                }

                Paid(bot, here, paid, hours);
            }

            return false;
        }

        if (_sent.TryGetValue(bot.Serial, out var sent) && Core.TickCount - sent < RetryMs)
        {
            return false;
        }

        var inn = Nearest(bot.Location, Reach);

        if (inn == null)
        {
            NoInnNear++;

            return false;
        }

        if (!BotGates.Joined(bot.Map, bot.Location, inn.Spot))
        {
            NoInnNear++;

            return false;
        }

        if (!BotWill.Press(bot, new BotLodge(inn, hours), $"tired, and going to {inn.Name} to rest"))
        {
            return false;
        }

        _sent[bot.Serial] = Core.TickCount;
        SentToInn++;

        return true;
    }

    public static void Paid(Mobile bot, Inn inn, int paid, double hours)
    {
        if (bot == null || inn == null)
        {
            return;
        }

        _paid[bot.Serial] = (inn, paid, hours);
    }

    public static Inn Settle(BotMobile bot, out int paid)
    {
        paid = 0;

        if (bot == null)
        {
            return null;
        }

        _sent.Remove(bot.Serial);

        var at = At(bot);

        if (_paid.Remove(bot.Serial, out var bed) && at != null)
        {
            paid = bed.Paid;
        }

        if (at == null)
        {
            LeftOutside++;

            return null;
        }

        at.Lodged++;

        if (paid > 0)
        {
            LodgedPaid++;
        }
        else
        {
            LodgedUnpaid++;
        }

        return at;
    }

    public static int Pay(Mobile bot, double hours)
    {
        var price = Math.Max(1, (int)Math.Ceiling(hours * PricePerHour));
        var pack = bot?.Backpack;

        if (pack == null)
        {
            return 0;
        }

        if (pack.ConsumeTotal(typeof(Gold), price) || Banker.Withdraw(bot, price))
        {
            GoldPaid += price;

            return price;
        }

        return 0;
    }

    public static void Rested(BotMobile bot, double restedHours, out DateTime until)
    {
        until = default;

        if (bot == null || restedHours <= 0.0)
        {
            return;
        }

        until = Core.Now + TimeSpan.FromHours(restedHours * BuffShare);
        bot.WellRestedUntil = until;
        bot.Resolve?.Urges?.Paid(Cheer);
        Buffed++;
    }

    public static void Regen(BotMobile bot)
    {
        if (bot is not { Deleted: false, Alive: true } || bot.WellRestedUntil == default)
        {
            return;
        }

        if (Core.Now >= bot.WellRestedUntil)
        {
            bot.WellRestedUntil = default;
            _regenTick.Remove(bot.Serial);

            return;
        }

        var now = Core.TickCount;

        if (_regenTick.TryGetValue(bot.Serial, out var last) && now - last < RegenEveryMs)
        {
            return;
        }

        _regenTick[bot.Serial] = now;

        var given = 0;

        if (bot.Hits < bot.HitsMax)
        {
            bot.Hits = Math.Min(bot.HitsMax, bot.Hits + RegenPoints);
            given++;
        }

        if (bot.Stam < bot.StamMax)
        {
            bot.Stam = Math.Min(bot.StamMax, bot.Stam + RegenPoints);
            given++;
        }

        if (bot.Mana < bot.ManaMax)
        {
            bot.Mana = Math.Min(bot.ManaMax, bot.Mana + RegenPoints);
            given++;
        }

        RegenPointsGiven += given * RegenPoints;
    }

    public static string Describe() =>
        !Running
            ? "inns are off: tired bots leave where they stand"
            : !Surveyed
                ? "the inns have not been read"
                : $"{_inns.Count} inns and taverns; {SentToInn} tired bots sent to one, {LodgedPaid} slept there paid and {LodgedUnpaid} unpaid, {AtHome} slept in a house of their own, {LeftOutside} left the world outside an inn, {NoInnNear} had none within {Reach}; {GoldPaid}gp paid for beds, {Buffed} rested buffs given, {RegenPointsGiven} points regained by them";

    public static void Forget()
    {
        _inns.Clear();
        _paid.Clear();
        _sent.Clear();
        _regenTick.Clear();
        _map = null;
        Surveyed = false;
        SentToInn = 0;
        LodgedPaid = 0;
        LodgedUnpaid = 0;
        LeftOutside = 0;
        AtHome = 0;
        NoInnNear = 0;
        GoldPaid = 0;
        Buffed = 0;
        RegenPointsGiven = 0;
    }
}

/// <summary>
/// The walk to an inn to sleep there: in at the door, the bed paid for, and the rest itself is <see cref="BotRest"/>'s,
/// which takes the bot out of the world from where it stands. Pressed, like the walk home to rest (BotRepose).
/// </summary>
public sealed class BotLodge : BotDeed
{
    public const string Trade = "lodge";

    public static double Prior { get; set; } = 150.0;

    public static long Arrived { get; private set; }

    public static long CouldNotPay { get; private set; }

    public static void NotedUnpaid() => CouldNotPay++;

    private readonly BotInns.Inn _inn;

    private readonly double _hours;

    public BotLodge(BotInns.Inn inn, double hours)
    {
        _inn = inn;
        _hours = hours;
    }

    public override string Kind => Trade;

    public override Map Map => BotPopulation.Home;

    public override Point3D Where => _inn?.Spot ?? Point3D.Zero;

    public override double Expects => Prior;

    public override double Minutes => 5.0;

    public override SkillName? Trains => null;

    public override int Outlay => 0;

    public override double Coin => 0.0;

    public override bool Unpaid => true;

    public override bool Steadfast => true;

    public override string Stage => $"to {_inn?.Name} to rest";

    public override BotDoing Advance(IBotWilful bot)
    {
        var body = bot?.Self;

        if (body == null || _inn == null || body.Map == null || body.Map == Map.Internal)
        {
            return BotDoing.Failed("no inn to go to");
        }

        if (!body.InRange(_inn.Spot, 2))
        {
            return BotDoing.Walk(body.Map, _inn.Spot, BotArrival.Within(2), $"to {_inn.Name}");
        }

        var paid = BotInns.Pay(body, _hours);

        if (paid == 0)
        {
            CouldNotPay++;
        }

        BotInns.Paid(body, _inn, paid, _hours);
        Arrived++;

        return BotDoing.Done(paid > 0 ? $"at {_inn.Name}, a bed paid for at {paid}gp" : $"at {_inn.Name}, with nothing to pay for a bed");
    }

    public static string Describe() => $"{Arrived} reached an inn to rest, {CouldNotPay} of them could not pay for the bed";
}
