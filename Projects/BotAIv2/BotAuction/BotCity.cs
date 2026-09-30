using System;
using System.Collections.Generic;
using Server.Items;
using Server.Logging;
using Server.Text;

namespace Server.BotAI.V2;

/// <summary>
/// The city's treasury: the one purse on the shard that mints coin, and everything the city does is paid from it.
///
/// <para>
/// <b>A lever with a budget is a lever a model may hold.</b> Until 16.09.2026 the city was one button on the
/// administrator's dashboard — buy ten lots from afar, paid for out of nothing, without limit — and for that reason it
/// could be given to nobody else. Patrick's order: whatever the city can do must be in Argus's and the debuggers'
/// hands. So the city has a purse. It fills at <see cref="MintPerHour"/> up to <see cref="Cap"/> and never faster,
/// every purchase comes out of it, and a purchase it cannot pay for is refused and counted. A watcher that buys
/// everything on the market at three in the morning has spent the treasury, not printed one.
/// </para>
///
/// <para>
/// <b>Two ways the city spends.</b> A buy takes whole lots off the stalls, the stalls that have stood longest first
/// (see <see cref="BotAuction.Stuck"/>: goods nobody wants are held for the life of the shard at a quarter of their
/// opening price, taking up room), and pays what was asked. A standing order names a thing, an amount and a price,
/// and the clock takes that thing off the stalls whenever it is on offer at or under the price — the outside demand
/// a producer sees as goods that sell as fast as they are made. Both are booked as sales, so the market learns from
/// them like from any other.
/// </para>
///
/// <para>
/// The purse is not kept across restarts: a restart is not a payday, and the mint refills it in an hour anyway.
/// </para>
/// </summary>
public static class BotCity
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotCity));

    public static bool Running { get; set; } = true;

    public static int Opening { get; set; } = 1000;

    public static int MintPerHour { get; set; } = 3000;

    public static int Cap { get; set; } = 20000;

    public static int EveryMs { get; set; } = 60000;

    public static bool StuckFirst { get; set; } = true;

    public static int MostWants { get; set; } = 8;

    public static int Purse { get; private set; }

    public static long Minted { get; private set; }

    public static long Spent { get; private set; }

    public static long Lots { get; private set; }

    public static long Units { get; private set; }

    public static long Served { get; private set; }

    public static long Filled { get; private set; }

    public static long Refused { get; private set; }

    public static long Taxed { get; private set; }

    public static void Tax(int coin)
    {
        if (coin <= 0)
        {
            return;
        }

        Taxed += coin;
        Purse += Math.Min(coin, Math.Max(0, Cap - Purse));
    }

    public static long CapitalPaid { get; private set; }

    public static long CapitalSales { get; private set; }

    public static bool Holds(int reserve) => Running && Purse > Math.Max(0, reserve);

    public static int Draw(int gold, int reserve)
    {
        if (!Running || gold <= 0)
        {
            return 0;
        }

        var paid = Math.Min(gold, Math.Max(0, Purse - Math.Max(0, reserve)));

        if (paid <= 0)
        {
            return 0;
        }

        Purse -= paid;
        Spent += paid;
        CapitalPaid += paid;
        CapitalSales++;

        return paid;
    }

    public static long Escrowed { get; private set; }

    public static bool Escrow(int gold)
    {
        if (gold <= 0 || Purse < gold)
        {
            return false;
        }

        Purse -= gold;
        Escrowed += gold;

        return true;
    }

    public static void Held(int gold)
    {
        if (gold > 0)
        {
            Escrowed += gold;
        }
    }

    public static void Refund(int gold)
    {
        if (gold <= 0)
        {
            return;
        }

        Purse += gold;
        Escrowed = Math.Max(0, Escrowed - gold);
    }

    public static void Disbursed(int gold)
    {
        if (gold <= 0)
        {
            return;
        }

        Escrowed = Math.Max(0, Escrowed - gold);
        Spent += gold;
    }

    private sealed class Order
    {
        public Type Kind;

        public string Name;

        public int Amount;

        public int Price;

        public string By;
    }

    private static readonly List<Order> _wants = [];

    /// <summary>A bounty on ground: paid, split evenly, to the company that clears the square it is in.</summary>
    private sealed class Prize
    {
        public Map Map;

        public Point3D Where;

        public int Gold;

        public string By;

        public long SetTick;
    }

    /// <summary>A bounty on a red's head: paid to whoever catches it.</summary>
    private sealed class Price
    {
        public Serial Who;

        public string Name;

        public int Gold;

        public string By;
    }

    private static readonly List<Prize> _bounties = [];

    private static readonly List<Price> _heads = [];

    public static int BountyMs { get; set; } = 7200000;

    public static int MostBounties { get; set; } = 6;

    public static long BountiesPaid { get; private set; }

    public static long BountyGold { get; private set; }

    public static long HeadsPaid { get; private set; }

    public static long BloodPaid { get; private set; }

    public static long BloodGold { get; private set; }

    public static long BloodShort { get; private set; }

    public static long Lapsed { get; private set; }

    public static long HeadsRebound { get; private set; }

    private static Clock _timer;

    public static int FairMs { get; set; } = 3600000;

    public static double FairShare { get; set; } = 0.8;

    public static int FairLots { get; set; } = 20;

    public static int FairEveryMs { get; set; } = 21600000;

    public static int FairFloor { get; set; } = 5000;

    public static int FairReserve { get; set; } = 2000;

    private static long _fairUntil;

    private static long _fairDeclaredTick;

    private static long _bornTick;

    private static double _fairShare = 0.8;

    private static int _fairStalls;

    private static int _fairUnits;

    private static int _fairGold;

    public static bool Fairing => _fairUntil != 0 && _fairUntil - Core.TickCount > 0;

    public static long Fairs { get; private set; }

    public static long Changes { get; private set; }

    public static long FairStalls { get; private set; }

    public static long FairUnits { get; private set; }

    public static long FairGold { get; private set; }

    public static string Fair(int minutes, double share, string who)
    {
        if (!Running)
        {
            return "the city is not buying anything at the moment.";
        }

        if (minutes <= 0 || minutes > 1440)
        {
            return "a fair runs between 1 and 1440 minutes.";
        }

        if (share <= 0.0 || share > 1.0)
        {
            return "the fair's share of the asking price is between 1 and 100 percent.";
        }

        var now = Core.TickCount;

        _fairUntil = now + minutes * 60000L;
        _fairDeclaredTick = now;
        _fairShare = share;
        _fairStalls = 0;
        _fairUnits = 0;
        _fairGold = 0;
        Fairs++;
        Changes++;

        logger.Information(
            "{Who} declared a fair: for {Minutes} minutes the city takes what is on the stalls at {Share:P0} of the asking price, {Lots} lots a minute, out of {Purse}gp",
            who,
            minutes,
            share,
            FairLots,
            Purse
        );

        return $"the fair is on: for {minutes} minutes the city takes what is on the stalls at {share:P0} of the asking price, {FairLots} lots a minute; {Purse}gp in the treasury.";
    }

    private static void Trade()
    {
        var now = Core.TickCount;

        if (_fairUntil != 0 && _fairUntil - now <= 0)
        {
            _fairUntil = 0;

            logger.Information(
                "The fair is over: {Stalls} stalls and {Units} things taken for {Gold}gp; {Purse}gp left in the treasury",
                _fairStalls,
                _fairUnits,
                _fairGold,
                Purse
            );
        }

        if (_fairUntil == 0)
        {
            var since = _fairDeclaredTick != 0 ? now - _fairDeclaredTick : now - _bornTick;

            if (FairEveryMs > 0 && Purse >= FairFloor && since >= FairEveryMs)
            {
                Fair(Math.Max(1, FairMs / 60000), FairShare, "the clock");
            }

            return;
        }

        var left = Math.Max(1L, (_fairUntil - now + 59999) / 60000);
        var budget = (int)Math.Min(int.MaxValue, Math.Max(0L, Purse - FairReserve) / left);

        if (budget <= 0)
        {
            return;
        }

        var (stalls, units, paid) = BotAuction.Crown(FairLots, budget, true, _fairShare);

        if (stalls <= 0)
        {
            return;
        }

        Purse -= paid;
        Spent += paid;
        Lots += stalls;
        Units += units;
        _fairStalls += stalls;
        _fairUnits += units;
        _fairGold += paid;
        FairStalls += stalls;
        FairUnits += units;
        FairGold += paid;

        logger.Information(
            "The fair took {Units} things off {Stalls} stalls for {Paid}gp at {Share:P0}; {Minutes:F0} minutes left, {Purse}gp in the treasury",
            units,
            stalls,
            paid,
            _fairShare,
            (_fairUntil - now) / 60000.0,
            Purse
        );
    }

    private static string FairTell() =>
        Fairing
            ? $"a fair is on for another {(_fairUntil - Core.TickCount) / 60000.0:F0} minutes at {_fairShare:P0}"
            : "no fair on";

    private static bool _restored;

    public static void Start()
    {
        Stop();

        if (!_restored)
        {
            Purse = Opening;
        }

        _bornTick = Core.TickCount;
        _timer = new Clock();
        _timer.Start();
    }

    public static void Stop()
    {
        _timer?.Stop();
        _timer = null;
    }

    private static void Tick()
    {
        if (!Running)
        {
            return;
        }

        Mint();
        Serve();
        Trade();
        Lapse();
    }

    public static string Bounty(Map map, int x, int y, int gold, string who)
    {
        if (!Running)
        {
            return "the city is not paying anything at the moment.";
        }

        if (map == null || map == Map.Internal)
        {
            return "the city has no map to put a bounty on.";
        }

        if (gold <= 0 || gold > Cap)
        {
            return $"a bounty is between 1 and {Cap}gp.";
        }

        if (!BotStep.Settle(map, x, y, out var z))
        {
            return $"nothing can stand at ({x}, {y}), so no company could clear it.";
        }

        var at = new Point3D(x, y, z);
        var key = BotQuad.Key(map, at);

        for (var i = 0; i < _bounties.Count; i++)
        {
            if (BotQuad.Key(_bounties[i].Map, _bounties[i].Where) == key)
            {
                _bounties[i].Gold = gold;
                _bounties[i].By = who;
                _bounties[i].SetTick = Core.TickCount;
                Changes++;

                return $"the bounty on the square at ({x}, {y}) is now {gold}gp; the Baron goes there before anywhere else.";
            }
        }

        if (_bounties.Count >= MostBounties)
        {
            return $"the city holds at most {MostBounties} bounties on ground at once.";
        }

        _bounties.Add(new Prize { Map = map, Where = at, Gold = gold, By = who, SetTick = Core.TickCount });
        Changes++;

        logger.Information("{Who} had the city put {Gold}gp on the square at ({X}, {Y}); the Baron is sent there first", who, gold, x, y);

        return $"the city puts {gold}gp on the square at ({x}, {y}); the Baron goes there before anywhere else, and the company that clears it is paid. {Purse}gp in the treasury.";
    }

    public static Point3D Bountied(Map map, Point3D from, int within)
    {
        Prize best = null;
        var closest = int.MaxValue;

        for (var i = 0; i < _bounties.Count; i++)
        {
            var bounty = _bounties[i];

            if (bounty.Map != map)
            {
                continue;
            }

            var away = Math.Max(Math.Abs(bounty.Where.X - from.X), Math.Abs(bounty.Where.Y - from.Y));

            if (away > within || away >= closest)
            {
                continue;
            }

            closest = away;
            best = bounty;
        }

        return best?.Where ?? Point3D.Zero;
    }

    public static int Claim(Map map, Point3D where, IReadOnlyList<IBotSquadMember> company)
    {
        if (map == null || company == null)
        {
            return 0;
        }

        var key = BotQuad.Key(map, where);

        for (var i = 0; i < _bounties.Count; i++)
        {
            var bounty = _bounties[i];

            if (BotQuad.Key(bounty.Map, bounty.Where) != key)
            {
                continue;
            }

            _bounties.RemoveAt(i);

            var heads = 0;

            for (var j = 0; j < company.Count; j++)
            {
                if (company[j]?.Self is { Deleted: false, Alive: true })
                {
                    heads++;
                }
            }

            var pot = Math.Min(bounty.Gold, Purse);

            if (heads == 0 || pot <= 0)
            {
                logger.Information("The bounty of {Gold}gp on ({X}, {Y}) lapsed: {Why}", bounty.Gold, bounty.Where.X, bounty.Where.Y, heads == 0 ? "nobody left to pay" : "the treasury is empty");
                Lapsed++;

                return 0;
            }

            var share = pot / heads;
            var paid = 0;

            for (var j = 0; j < company.Count; j++)
            {
                if (company[j]?.Self is { Deleted: false, Alive: true } member && Pay(member, share))
                {
                    paid += share;
                }
            }

            Purse -= paid;
            Spent += paid;
            BountiesPaid++;
            BountyGold += paid;

            logger.Information(
                "The city paid its bounty on ({X}, {Y}): {Paid}gp to {Heads} of the company, {Share}gp each (put up by {By}); {Purse}gp left in the treasury",
                bounty.Where.X,
                bounty.Where.Y,
                paid,
                heads,
                share,
                bounty.By,
                Purse
            );

            return paid;
        }

        return 0;
    }

    public static string Head(BotMobile red, int gold, string who)
    {
        if (!Running)
        {
            return "the city is not paying anything at the moment.";
        }

        if (red is not { Deleted: false })
        {
            return "no such bot.";
        }

        if (!BotOutlaw.Outlaw(red))
        {
            return $"{red.Name} is neither red nor wanted; the city puts a price only on an outlaw's head.";
        }

        if (gold <= 0 || gold > Cap)
        {
            return $"a bounty is between 1 and {Cap}gp.";
        }

        for (var i = 0; i < _heads.Count; i++)
        {
            if (_heads[i].Who == red.Serial)
            {
                _heads[i].Gold = gold;
                _heads[i].By = who;
                Changes++;

                return $"the price on {red.Name}'s head is now {gold}gp.";
            }
        }

        _heads.Add(new Price { Who = red.Serial, Name = red.Name, Gold = gold, By = who });
        Changes++;

        logger.Information("{Who} had the city put {Gold}gp on {Name}'s head", who, gold, red.Name);

        return $"the city puts {gold}gp on {red.Name}'s head, paid to whoever catches it. {Purse}gp in the treasury.";
    }

    public static void Collect(BotMobile red, Mobile by)
    {
        if (red == null)
        {
            return;
        }

        for (var i = 0; i < _heads.Count; i++)
        {
            if (_heads[i].Who != red.Serial)
            {
                continue;
            }

            var head = _heads[i];

            _heads.RemoveAt(i);

            var pot = Math.Min(head.Gold, Purse);

            if (pot <= 0 || by is not { Deleted: false } || !Pay(by, pot))
            {
                logger.Information("The price of {Gold}gp on {Name}'s head lapsed: {Why}", head.Gold, head.Name, pot <= 0 ? "the treasury is empty" : "nobody to pay");
                Lapsed++;

                return;
            }

            Purse -= pot;
            Spent += pot;
            HeadsPaid++;
            BountyGold += pot;

            logger.Information("The city paid {By} {Gold}gp for {Name}'s head (put up by {Who}); {Purse}gp left in the treasury", by.Name, pot, head.Name, head.By, Purse);

            return;
        }
    }

    public static int Blood(Mobile red, IReadOnlyList<Mobile> hands, int gold)
    {
        if (!Running || red == null || gold <= 0 || hands == null || hands.Count == 0)
        {
            return 0;
        }

        var heads = 0;

        for (var i = 0; i < hands.Count; i++)
        {
            if (hands[i] is { Deleted: false, Alive: true })
            {
                heads++;
            }
        }

        var pot = Math.Min(gold, Purse);

        if (heads == 0 || pot <= 0)
        {
            BloodShort++;

            logger.Information(
                "The price of {Gold}gp on {Name}'s blood went unpaid: {Why}",
                gold,
                red.Name,
                heads == 0 ? "nobody left to pay" : "the treasury is empty"
            );

            return 0;
        }

        if (pot < gold)
        {
            BloodShort++;
        }

        var share = pot / heads;
        var paid = 0;

        for (var i = 0; i < hands.Count; i++)
        {
            if (hands[i] is { Deleted: false, Alive: true } hand && Pay(hand, share))
            {
                paid += share;
            }
        }

        Purse -= paid;
        Spent += paid;
        BloodPaid++;
        BloodGold += paid;

        logger.Information(
            "The city paid {Paid}gp for {Name}'s blood, {Share}gp to each of {Heads} (the price was {Gold}); {Purse}gp left in the treasury",
            paid,
            red.Name,
            share,
            heads,
            gold,
            Purse
        );

        return paid;
    }

    private static bool Pay(Mobile to, int coin)
    {
        if (coin <= 0 || to?.Backpack is not { } pack)
        {
            return false;
        }

        pack.DropItem(new Gold(coin));

        return true;
    }

    internal static void Save(IGenericWriter writer)
    {
        var now = Core.TickCount;

        writer.WriteEncodedInt(Purse);

        writer.WriteEncodedInt(_wants.Count);

        for (var i = 0; i < _wants.Count; i++)
        {
            writer.Write(_wants[i].Name ?? "");
            writer.WriteEncodedInt(_wants[i].Amount);
            writer.WriteEncodedInt(_wants[i].Price);
            writer.Write(_wants[i].By ?? "");
        }

        writer.WriteEncodedInt(_bounties.Count);

        for (var i = 0; i < _bounties.Count; i++)
        {
            var bounty = _bounties[i];

            writer.Write(bounty.Map);
            writer.Write(bounty.Where);
            writer.WriteEncodedInt(bounty.Gold);
            writer.Write(bounty.By ?? "");
            writer.Write(Math.Max(0L, now - bounty.SetTick));
        }

        writer.WriteEncodedInt(_heads.Count);

        for (var i = 0; i < _heads.Count; i++)
        {
            writer.Write(World.FindMobile(_heads[i].Who));
            writer.Write(_heads[i].Name ?? "");
            writer.WriteEncodedInt(_heads[i].Gold);
            writer.Write(_heads[i].By ?? "");
        }
    }

    internal static (int Purse, int Orders, int Bounties, int Heads) Load(IGenericReader reader)
    {
        var now = Core.TickCount;

        Purse = Math.Max(0, reader.ReadEncodedInt());
        _restored = true;

        _wants.Clear();

        var orders = reader.ReadEncodedInt();

        for (var i = 0; i < orders; i++)
        {
            var name = reader.ReadString();
            var amount = reader.ReadEncodedInt();
            var price = reader.ReadEncodedInt();
            var by = reader.ReadString();
            var kind = string.IsNullOrEmpty(name) ? null : AssemblyHandler.FindTypeByName(name);

            if (kind == null || !typeof(Item).IsAssignableFrom(kind) || amount <= 0 || price <= 0)
            {
                continue;
            }

            _wants.Add(new Order { Kind = kind, Name = kind.Name, Amount = amount, Price = price, By = string.IsNullOrEmpty(by) ? null : by });
        }

        _bounties.Clear();

        var bounties = reader.ReadEncodedInt();

        for (var i = 0; i < bounties; i++)
        {
            var map = reader.ReadMap();
            var where = reader.ReadPoint3D();
            var gold = reader.ReadEncodedInt();
            var by = reader.ReadString();
            var age = reader.ReadLong();

            if (map == null || map == Map.Internal || gold <= 0)
            {
                continue;
            }

            _bounties.Add(new Prize { Map = map, Where = where, Gold = gold, By = string.IsNullOrEmpty(by) ? null : by, SetTick = now - Math.Max(0L, age) });
        }

        _heads.Clear();

        var heads = reader.ReadEncodedInt();

        for (var i = 0; i < heads; i++)
        {
            var red = reader.ReadEntity<BotMobile>();
            var name = reader.ReadString();
            var gold = reader.ReadEncodedInt();
            var by = reader.ReadString();

            if (red is not { Deleted: false } || gold <= 0)
            {
                continue;
            }

            _heads.Add(new Price { Who = red.Serial, Name = string.IsNullOrEmpty(name) ? red.Name : name, Gold = gold, By = string.IsNullOrEmpty(by) ? null : by });
        }

        return (Purse, _wants.Count, _bounties.Count, _heads.Count);
    }

    private static void Lapse()
    {
        var now = Core.TickCount;

        for (var i = _bounties.Count - 1; i >= 0; i--)
        {
            if (now - _bounties[i].SetTick < BountyMs)
            {
                continue;
            }

            logger.Information("The bounty of {Gold}gp on ({X}, {Y}) lapsed after {Hours} hours unclaimed", _bounties[i].Gold, _bounties[i].Where.X, _bounties[i].Where.Y, BountyMs / 3600000);
            _bounties.RemoveAt(i);
            Lapsed++;
        }

        for (var i = _heads.Count - 1; i >= 0; i--)
        {
            var red = World.FindMobile(_heads[i].Who) as BotMobile;

            if (red is not { Deleted: false })
            {
                var again = Named(_heads[i].Name);

                if (again != null)
                {
                    _heads[i].Who = again.Serial;
                    HeadsRebound++;

                    continue;
                }

                if (BotPopulation.Count == 0)
                {
                    continue;
                }
            }
            else if (BotOutlaw.IsRed(red))
            {
                continue;
            }

            _heads.RemoveAt(i);
            Lapsed++;
        }
    }

    private static BotMobile Named(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return null;
        }

        var bots = BotPopulation.Bots;

        for (var i = 0; i < bots.Count; i++)
        {
            if (bots[i] is { Deleted: false } bot && bot.Name == name)
            {
                return bot;
            }
        }

        return null;
    }

    public static string Bounties()
    {
        if (_bounties.Count == 0 && _heads.Count == 0)
        {
            return "no bounties";
        }

        var say = ValueStringBuilder.Create(256);

        try
        {
            var shown = 0;

            for (var i = 0; i < _bounties.Count; i++)
            {
                if (shown++ > 0)
                {
                    say.Append("; ");
                }

                say.Append(_bounties[i].Gold);
                say.Append("gp on (");
                say.Append(_bounties[i].Where.X);
                say.Append(", ");
                say.Append(_bounties[i].Where.Y);
                say.Append(")");
            }

            for (var i = 0; i < _heads.Count; i++)
            {
                if (shown++ > 0)
                {
                    say.Append("; ");
                }

                say.Append(_heads[i].Gold);
                say.Append("gp on ");
                say.Append(_heads[i].Name);
                say.Append("'s head");
            }

            return say.ToString();
        }
        finally
        {
            say.Dispose();
        }
    }

    private static void Mint()
    {
        var coin = (int)((long)MintPerHour * EveryMs / 3600000L);

        if (coin <= 0 || Purse >= Cap)
        {
            return;
        }

        var added = Math.Min(coin, Cap - Purse);

        Purse += added;
        Minted += added;
    }

    public static (int Lots, int Units, int Paid) Buy(int lots, string who)
    {
        if (!Running || lots <= 0)
        {
            return (0, 0, 0);
        }

        if (Purse <= 0)
        {
            Refused++;

            return (0, 0, 0);
        }

        var (taken, units, paid) = BotAuction.Crown(lots, Purse, StuckFirst);

        Purse -= paid;
        Spent += paid;
        Lots += taken;
        Units += units;

        if (taken > 0)
        {
            logger.Information(
                "{Who} had the city buy {Units} things from {Lots} stalls for {Paid}gp; {Purse}gp left in the treasury",
                who,
                units,
                taken,
                paid,
                Purse
            );
        }

        return (taken, units, paid);
    }

    public static string Want(string name, int amount, int price, string who)
    {
        if (!Running)
        {
            return "the city is not buying anything at the moment.";
        }

        var kind = AssemblyHandler.FindTypeByName(name);

        if (kind == null || !typeof(Item).IsAssignableFrom(kind))
        {
            return $"the city does not know a thing called {name}.";
        }

        if (amount <= 0 || price <= 0)
        {
            return "a standing order needs an amount and a price, both above nought.";
        }

        for (var i = 0; i < _wants.Count; i++)
        {
            if (_wants[i].Kind == kind)
            {
                _wants[i].Amount = amount;
                _wants[i].Price = price;
                _wants[i].By = who;
                Changes++;

                return $"the city's standing order for {kind.Name} is now {amount} at up to {price}gp each; {Purse}gp in the treasury.";
            }
        }

        if (_wants.Count >= MostWants)
        {
            return $"the city holds at most {MostWants} standing orders; forget one first.";
        }

        _wants.Add(new Order { Kind = kind, Name = kind.Name, Amount = amount, Price = price, By = who });
        Changes++;

        logger.Information(
            "{Who} had the city put out a standing order for {Amount} {Kind} at up to {Price}gp each",
            who,
            amount,
            kind.Name,
            price
        );

        return $"the city now wants {amount} {kind.Name} at up to {price}gp each, off the stalls as they are offered; {Purse}gp in the treasury.";
    }

    public static string Forget(string name)
    {
        for (var i = 0; i < _wants.Count; i++)
        {
            if (_wants[i].Name.InsensitiveEquals(name))
            {
                _wants.RemoveAt(i);

                return $"the city's standing order for {name} is withdrawn.";
            }
        }

        return $"the city has no standing order for {name}.";
    }

    private static void Serve()
    {
        if (_wants.Count == 0 || Purse <= 0)
        {
            return;
        }

        for (var i = _wants.Count - 1; i >= 0; i--)
        {
            var want = _wants[i];
            var (units, paid) = BotAuction.CrownWant(want.Kind, want.Amount, want.Price, Purse);

            if (units <= 0)
            {
                continue;
            }

            Purse -= paid;
            Spent += paid;
            Units += units;
            Served += units;
            want.Amount -= units;

            logger.Information(
                "The city took {Units} {Kind} off the stalls for {Paid}gp against its standing order; {Left} still wanted, {Purse}gp in the treasury",
                units,
                want.Name,
                paid,
                Math.Max(0, want.Amount),
                Purse
            );

            if (want.Amount <= 0)
            {
                _wants.RemoveAt(i);
                Filled++;
            }
        }
    }

    public static string Wants()
    {
        if (_wants.Count == 0)
        {
            return "no standing orders";
        }

        var say = ValueStringBuilder.Create(256);

        try
        {
            for (var i = 0; i < _wants.Count; i++)
            {
                if (i > 0)
                {
                    say.Append("; ");
                }

                say.Append(_wants[i].Amount);
                say.Append(" ");
                say.Append(_wants[i].Name);
                say.Append(" at up to ");
                say.Append(_wants[i].Price);
                say.Append("gp (");
                say.Append(_wants[i].By ?? "somebody");
                say.Append(")");
            }

            return say.ToString();
        }
        finally
        {
            say.Dispose();
        }
    }

    public static string Describe() =>
        !Running
            ? "the city is not buying"
            : $"the treasury holds {Purse}gp of {Cap} (minted {Minted}, taxed {Taxed} off the guilds' ground, spent {Spent} on {Lots} lots and {Units} things, {Served} of them against standing orders, {Filled} orders filled, {Refused} buys refused for want of coin; {BountiesPaid} bounties on ground and {HeadsPaid} on heads paid, {BountyGold}gp in all, {Lapsed} lapsed, {BloodPaid} murderers paid for at {BloodGold}gp and {BloodShort} prices the treasury could not meet, {HeadsRebound} prices on heads moved onto a new body after a boot); {CapitalPaid}gp paid on top of the capital's counters over {CapitalSales} sales; {Escrowed}gp held for the board's errands; {FairTell()}, {Fairs} fairs held and {FairGold}gp spent at them; standing orders: {Wants()}; bounties: {Bounties()}";

    public static void Forget()
    {
        _restored = false;
        _fairUntil = 0;
        _fairDeclaredTick = 0;
        Fairs = 0;
        FairStalls = 0;
        FairUnits = 0;
        FairGold = 0;
        _wants.Clear();
        _bounties.Clear();
        _heads.Clear();
        BountiesPaid = 0;
        HeadsPaid = 0;
        BountyGold = 0;
        Lapsed = 0;
        HeadsRebound = 0;
        Taxed = 0;
        Minted = 0;
        Spent = 0;
        Lots = 0;
        Units = 0;
        Served = 0;
        Filled = 0;
        Refused = 0;
        CapitalPaid = 0;
        CapitalSales = 0;
    }

    private sealed class Clock : Timer
    {
        public Clock() : base(TimeSpan.FromMilliseconds(Math.Max(1000, EveryMs)), TimeSpan.FromMilliseconds(Math.Max(1000, EveryMs)))
        {
        }

        protected override void OnTick() => Tick();
    }
}
