using System.Collections.Generic;
using Server.Engines.Harvest;
using Server.Items;
using Server.Mobiles;
using Server.Multis;

namespace Server.BotAI.V2;

/// <summary>
/// Dig, melt, put away. One undertaking with three stages, and the second and third are the point of it.
///
/// <para>
/// <b>Ore is worth nothing to anybody.</b> No counter buys it, no bot wants it, and a miner that comes home
/// with a pack full of rock has produced exactly nothing — which is what the first version's miners did all
/// night, because their goal ended at the vein. So the work is not "mine": it is <em>mine, carry it to a
/// fire, and put the metal where it is safe</em>, and it is not finished until the last of those has
/// happened.
/// </para>
///
/// <para>
/// <b>Its stages are its own business.</b> The decision layer asks what to do now and is told a place, or
/// "work here", or that it is over; it never learns what ore is. That is the whole reason adding a trade to
/// this shard does not touch <c>BotWill/</c>.
/// </para>
/// </summary>
public sealed class BotDig : BotDeed
{
    public const string Trade = "mine";

    public static double Prior { get; set; } = 45.0;

    public static double WorkMinutes { get; set; } = 8.0;

    public static int GoldPerIngot { get; set; } = 6;

    public static double FillFraction { get; set; } = 0.8;

    public static int TargetOre { get; set; } = 20;

    public static int DryLimit { get; set; } = 6;

    public static int MostDry { get; set; } = 24;

    public static int CounterReach { get; set; } = 6;

    public static int MaxBends { get; set; } = 3;

    public static bool ListGoods { get; set; } = true;

    public static int MaxSpent { get; set; } = 8;

    public static int SwingMs { get; set; } = 2000;

    private enum Leg
    {
        Seam,
        Fire,
        Counter
    }

    private readonly Map _map;

    private IBotWilful _bot;

    private BotSeam _seam;

    private Leg _leg;

    private IPoint3D _tile;

    private HarvestSystem _system;

    public override bool Afoot => _leg == Leg.Seam && _tile != null;

    public static long FarSide { get; private set; }

    public static int RepickLimit { get; set; } = 2;

    public static long Repicked { get; private set; }

    public static long Beaten { get; private set; }

    public static long WorkedOut { get; private set; }

    private Point3D _fire;

    private Point3D _counter;

    private int _swings;

    private int _approaches;

    private Point3D _standRock;

    private Point3D _standAt;

    private int _standRing;

    public static int SeamStandTiles { get; set; } = 4;

    public static long Standless { get; private set; }

    private static Point3D Stand(Map map, Point3D point, int most, Point3D toward, out int ring, bool level = false)
    {
        if (level)
        {
            var chosen = Point3D.Zero;
            var cheapest = int.MaxValue;
            var chosenRing = -1;

            for (var dx = -most; dx <= most; dx++)
            {
                for (var dy = -most; dy <= most; dy++)
                {
                    var x = point.X + dx;
                    var y = point.Y + dy;

                    if (!BotStep.Settle(map, x, y, out var z) || BotFooting.Footless(map, x, y) || BotStep.Wet(map, x, y))
                    {
                        continue;
                    }

                    var climb = System.Math.Max(0, System.Math.Abs(z - toward.Z) - 4);
                    var cost = climb * 3 + System.Math.Max(System.Math.Abs(toward.X - x), System.Math.Abs(toward.Y - y));

                    if (cost < cheapest)
                    {
                        cheapest = cost;
                        chosen = new Point3D(x, y, z);
                        chosenRing = System.Math.Max(System.Math.Abs(dx), System.Math.Abs(dy));
                    }
                }
            }

            ring = chosenRing;

            return chosen;
        }

        for (ring = 0; ring <= most; ring++)
        {
            var best = Point3D.Zero;
            var least = int.MaxValue;

            for (var dx = -ring; dx <= ring; dx++)
            {
                for (var dy = -ring; dy <= ring; dy++)
                {
                    if (System.Math.Max(System.Math.Abs(dx), System.Math.Abs(dy)) != ring)
                    {
                        continue;
                    }

                    var x = point.X + dx;
                    var y = point.Y + dy;

                    if (!BotStep.Settle(map, x, y, out var z) || BotFooting.Footless(map, x, y) || BotStep.Wet(map, x, y))
                    {
                        continue;
                    }

                    var away = System.Math.Max(System.Math.Abs(toward.X - x), System.Math.Abs(toward.Y - y));

                    if (away < least)
                    {
                        least = away;
                        best = new Point3D(x, y, z);
                    }
                }
            }

            if (best != Point3D.Zero)
            {
                return best;
            }
        }

        ring = -1;

        return Point3D.Zero;
    }

    private int _nearest = int.MaxValue;

    private int _routeStamp = -1;

    private int _setOut;

    private int _stalled;

    private int _repicks;

    private int _walled;

    public static int ApproachLimit { get; set; } = 40;

    public static int TrekLimit { get; set; } = 200;

    public static int StrikeWithin { get; set; } = 48;

    public static int WalledLimit { get; set; } = 3;

    public static long Unwalkable { get; private set; }

    public static long Unreachable { get; private set; }

    public static long Drained { get; private set; }

    public static long Fumbled { get; private set; }

    public static long Allowanced { get; private set; }

    public static double FumbledChance { get; private set; }

    public static long Locked { get; private set; }

    public static long Stirred { get; private set; }

    public static long Adrift { get; private set; }

    public static long Laden { get; private set; }

    public static long NextPickaxe { get; private set; }

    public static long Spurned { get; private set; }

    public static long Nearer { get; private set; }

    public static long Forsaken { get; private set; }

    public static int MostRefusals { get; set; } = 2;

    public static int NearingMs { get; set; } = 1800000;

    private static readonly Dictionary<Point3D, long> _nearing = [];

    private bool _closer;

    private int _refusals;

    private Point3D _swungFrom;

    private bool _swung;

    private long _swungTick;

    private int _dry;

    private int _seen;

    private int _made;

    private int _raw;

    private int _rawWorth;

    private int _worth;

    private int _stored;

    private int _bends;

    private readonly List<Point3D> _spent = [];

    private int _ranDry;

    private int _missed;

    public BotDig(BotSeam seam)
    {
        _seam = seam;
        _map = seam.Map;
    }

    public override string Kind => Trade;

    public override bool Steadfast => true;

    public override void Resumed(IBotWilful bot)
    {
        _swung = false;
        _dry = 0;

        if (bot?.Self is { } body)
        {
            _seen = System.Math.Min(_seen, BotOre.Carried(body));
        }
    }

    public override Map Map => _map;

    public override Point3D Where => _seam.Where;

    public override double Expects => Prior;

    public override double Minutes => WorkMinutes;

    public override SkillName? Trains => SkillName.Mining;

    public override double Coin => 0.0;

    public override int Made => _made + _raw * _rawWorth;

    public override string Stage => _leg switch
    {
        Leg.Seam => $"digging {_seam.Ore} ({_swings} swings)",
        Leg.Fire => "carrying ore to a fire",
        _ => "putting the metal away"
    };

    public override BotDoing Advance(IBotWilful bot)
    {
        var body = bot?.Self;

        if (body == null)
        {
            return BotDoing.Failed("no body");
        }

        _bot = bot;

        for (var guard = 0; guard < 6; guard++)
        {
            var doing = _leg switch
            {
                Leg.Seam => Digging(bot, body),
                Leg.Fire => Melting(body),
                _ => Banking(bot)
            };

            if (doing.Kind != BotDoingKind.None)
            {
                return doing;
            }
        }

        return BotDoing.Failed("could not settle on a next step");
    }

    private BotDoing Digging(IBotWilful bot, Mobile body)
    {
        var carried = BotOre.Carried(body);

        if (carried >= TargetOre || Loaded(body))
        {
            _leg = Leg.Fire;

            return default;
        }

        var tool = BotOre.Tool(body);

        if (tool == null)
        {
            if (carried >= BotOre.WorthSmelting)
            {
                _leg = Leg.Fire;

                return default;
            }

            return BotDoing.Failed("nothing to dig with");
        }

        if (_tile == null && !body.InRange(_seam.Where, BotOre.Reach))
        {
            if (!BotGround.Free(body, _seam.Where))
            {
                var other = _repicks < RepickLimit ? BotGround.Seam(bot, _seam.Where) : default;

                if (!other.Exists)
                {
                    Beaten++;

                    return BotDoing.Failed($"another miner holds the {_seam.Ore}, and no other seam is free");
                }

                _repicks++;
                Repicked++;

                _seam = other;
                _tile = null;
                _dry = 0;
                _spent.Clear();
                _nearest = int.MaxValue;
                _stalled = 0;
            }

            BotGround.Working(body, _seam.Where);

            var gap = System.Math.Max(System.Math.Abs(body.X - _seam.Where.X), System.Math.Abs(body.Y - _seam.Where.Y));

            if (body is BotMobile { Journey: { } journey })
            {
                gap = journey.RoadLeft(body.Location, _seam.Where);

                if (_routeStamp != journey.RouteStamp)
                {
                    _routeStamp = journey.RouteStamp;
                    _nearest = int.MaxValue;
                    _stalled = 0;
                }
            }

            if (_nearest == int.MaxValue)
            {
                _setOut = gap;
            }

            if (gap < _nearest)
            {
                _nearest = gap;
                _stalled = 0;
            }
            else if (++_stalled >= TrekLimit)
            {
                BotAppraisal.Becalm(bot.Resolve, body.Location, _setOut, _nearest);

                if (gap > StrikeWithin)
                {
                    var third = BotGround.Shy(_map, _seam.Where);

                    if (third)
                    {
                        Unwalkable++;
                    }

                    return BotDoing.Failed(
                        third
                            ? $"the walk to the {_seam.Ore} at ({_seam.Where.X}, {_seam.Where.Y}) stopped closing {gap} tiles short, the {BotGround.ShiedLimit}rd to do so, and the seam is struck off"
                            : $"the walk to the {_seam.Ore} at ({_seam.Where.X}, {_seam.Where.Y}) stopped closing {gap} tiles short, which says nothing of the seam; it rests and stays on the board"
                    );
                }

                var struck = BotGround.Barren(_seam.Where);

                if (struck)
                {
                    Unwalkable++;
                }

                return BotDoing.Failed(
                    struck
                        ? $"no way through to the {_seam.Ore} in {gap} tiles, and the seam is struck off"
                        : $"no way through to the {_seam.Ore} in {gap} tiles; somebody had already struck it off"
                );
            }

            var seamStand = Stand(_map, _seam.Where, SeamStandTiles, _seam.Where, out _);

            return BotDoing.Walk(_map, seamStand == Point3D.Zero ? _seam.Where : seamStand, BotArrival.Within(2), $"to the {_seam.Ore}");
        }

        if (_tile == null && _spent.Count < MaxSpent)
        {
            _tile = BotOre.Find(body, out _system, _spent, _seam.Where, BotOre.Reach);

            if (_tile == null && !body.InRange(_seam.Where, 2))
            {
                _tile = BotOre.Find(body, out _system, _spent, _seam.Where, BotOre.Reach, _seam.Where);

                if (_tile != null)
                {
                    FarSide++;
                }
            }
        }

        if (_tile == null || _system == null)
        {
            if (carried >= BotOre.WorthSmelting)
            {
                _leg = Leg.Fire;

                return default;
            }

            if (_spent.Count > 0)
            {
                if (_spent.Count >= MaxSpent)
                {
                    Allowanced++;
                }

                if (_ranDry >= _missed)
                {
                    BotGround.Drained(_seam.Where);

                    return Made > 0
                        ? BotDoing.Done($"worked the seam out: emptied {_ranDry} of {_spent.Count} rocks, and it rests")
                        : BotDoing.Failed($"emptied {_ranDry} of {_spent.Count} rocks and found no more, and the seam rests");
                }

                bot?.Resolve?.Ledger?.Beware(Trade, _map, _seam.Where);

                return BotDoing.Failed(
                    $"missed too often on {_missed} of {_spent.Count} rocks that still hold ore, at Mining {body.Skills[SkillName.Mining].Value:F1} (base {body.Skills[SkillName.Mining].Base:F1}); the seam keeps its place"
                );
            }

            var off = System.Math.Max(System.Math.Abs(body.X - _seam.Where.X), System.Math.Abs(body.Y - _seam.Where.Y));

            if (BotOre.LastRocks > 0 && BotOre.LastEmpty >= BotOre.LastRocks)
            {
                BotGround.Drained(_seam.Where);
                WorkedOut++;

                return BotDoing.Failed(
                    $"every one of the {BotOre.LastRocks} rocks in reach of the seam at ({_seam.Where.X}, {_seam.Where.Y}) is worked out, and the seam rests (looked from {off} tiles off it)"
                );
            }

            return BotDoing.Failed(
                BotGround.Barren(_seam.Where)
                    ? $"no rock worth swinging at, and the seam at ({_seam.Where.X}, {_seam.Where.Y}) is struck off (looked from {off} tiles off it, {BotOre.LastRocks} rocks seen)"
                    : $"no rock worth swinging at; somebody had already struck the seam off (looked from {off} tiles off it)"
            );
        }

        var at = new Point3D(_tile.X, _tile.Y, _tile.Z);

        if (!body.InRange(at, BotOre.SwingReach))
        {
            if (_standRock != at)
            {
                _standRock = at;
                _standAt = Stand(_map, at, BotOre.SwingReach, body.Location, out _standRing, true);
            }

            var rockStand = _standAt;
            var ring = _standRing;

            if (rockStand == Point3D.Zero)
            {
                Standless++;
                _approaches = ApproachLimit;
            }
            else if (++_approaches < ApproachLimit)
            {
                return BotDoing.Walk(_map, rockStand, BotArrival.Within(System.Math.Max(0, BotOre.SwingReach - ring)), "up to the rock");
            }

            _spent.Add(at);
            _tile = null;
            _approaches = 0;
            Unreachable++;

            if (++_walled >= WalledLimit)
            {
                var struck = BotGround.Barren(_seam.Where);

                if (struck)
                {
                    Unwalkable++;
                }

                return BotDoing.Failed(
                    struck
                        ? $"{_walled} rocks of the {_seam.Ore} could not be reached, and the seam is struck off"
                        : $"{_walled} rocks of the {_seam.Ore} could not be reached; somebody had already struck the seam off"
                );
            }

            return default;
        }

        _approaches = 0;
        _walled = 0;

        var word = BotHeard.Last(body, Server.Engines.Harvest.Mining.System?.OreAndStone, out _);

        switch (word)
        {
            case BotHeard.Word.Empty:
            case BotHeard.Word.Taken:
                {
                    BotHeard.Clear(body);
                    Drained++;
                    _ranDry++;
                    _spent.Add(at);
                    _tile = null;
                    _dry = 0;

                    return default;
                }

            case BotHeard.Word.Full:
                {
                    BotHeard.Clear(body);
                    Laden++;
                    _dry = 0;
                    _leg = BotOre.Carried(body) >= BotOre.WorthSmelting ? Leg.Fire : Leg.Counter;

                    return default;
                }

            case BotHeard.Word.Adrift:
                {
                    BotHeard.Clear(body);
                    Adrift++;
                    _dry = _dry > 0 ? _dry - 1 : 0;

                    break;
                }

            case BotHeard.Word.Broken:
                {
                    BotHeard.Clear(body);

                    if (BotOre.Tool(body) != null)
                    {
                        NextPickaxe++;

                        break;
                    }

                    return Made > 0
                        ? BotDoing.Done($"the pickaxe wore out, {_raw} ore still to smelt")
                        : BotDoing.Failed("the pickaxe wore out");
                }
        }

        if (_swung && BotOre.Left(_map, at.X, at.Y) <= 0)
        {
            Drained++;
            _ranDry++;
            _spent.Add(at);
            _tile = null;
            _dry = 0;

            return default;
        }

        BotGround.Working(body, _seam.Where);

        if (_swung && Core.TickCount - _swungTick < SwingMs)
        {
            return BotDoing.Work($"digging {_seam.Ore}");
        }

        var patience = System.Math.Clamp(
            (int)System.Math.Ceiling(DryLimit / System.Math.Max(0.05, BotOre.Chance(body, _map, at.X, at.Y))),
            DryLimit,
            MostDry
        );

        if (carried > _seen)
        {
            var raised = carried - _seen;

            _seen = carried;
            _dry = 0;

            BotQuad.Harvested(_map, body.Location);

            if (_rawWorth <= 0)
            {
                _rawWorth = System.Math.Max(
                    1,
                    BotAuction.Worth(typeof(IronOre), System.Math.Max(1, GoldPerIngot / 2))
                );
            }

            _raw += raised;
        }
        else if (_swung && ++_dry >= patience)
        {
            if (BotOre.Left(_map, at.X, at.Y) > 0)
            {
                Fumbled++;
                _missed++;

                FumbledChance += BotOre.Chance(body, _map, at.X, at.Y);
            }
            else
            {
                Drained++;
                _ranDry++;
            }

            _spent.Add(at);
            _tile = null;
            _dry = 0;

            return default;
        }

        _swung = true;
        _swungTick = Core.TickCount;
        _swings++;

        if (!body.CanBeginAction(tool))
        {
            Locked++;
        }

        if (_swung && _dry > 0 && body.Location != _swungFrom)
        {
            Stirred++;
        }

        _swungFrom = body.Location;

        if (body.Mounted)
        {
            BotStable.Alight(body);
        }

        BotOre.Swing(body, tool, _system, _tile);

        Note(body, _seam.Where);

        return BotDoing.Work($"digging {_seam.Ore}");
    }

    private BotDoing Melting(Mobile body)
    {
        if (BotOre.Carried(body) < BotOre.WorthSmelting)
        {
            _leg = Leg.Counter;

            return default;
        }

        if (BotOre.Carried(body) <= 0)
        {
            _leg = Leg.Counter;

            return default;
        }

        if (_fire == Point3D.Zero)
        {
            _fire = BotGround.Fire(_bot, body.Location);

            if (_fire == Point3D.Zero)
            {
                return BotDoing.Failed("nowhere known to melt it");
            }
        }

        if (_closer ? !body.InRange(_fire, 1) : !body.InRange(_fire, BotOre.FireReach))
        {
            return _closer
                ? BotDoing.Walk(_map, _fire, BotArrival.Beside, "closer to the fire")
                : BotDoing.Walk(_map, _fire, BotArrival.Within(BotOre.FireReach), "to a fire");
        }

        var before = BotOre.Carried(body);
        var made = BotOre.Melt(body);

        if (made <= 0 && BotOre.Carried(body) >= before)
        {
            Spurned++;

            var house = BaseHouse.FindHouseAt(_fire, _map, 16);
            var lately = _nearing.TryGetValue(_fire, out var when) && Core.TickCount - when < NearingMs;

            if (!_closer && !lately && (house == null || BaseHouse.FindHouseAt(body) == house))
            {
                _closer = true;
                _nearing[_fire] = Core.TickCount;
                Nearer++;

                return BotDoing.Walk(_map, _fire, BotArrival.Beside, "closer to the fire");
            }

            BotGround.Unfit(_fire);
            Forsaken++;

            _fire = Point3D.Zero;
            _closer = false;

            if (++_refusals >= MostRefusals)
            {
                _leg = Leg.Counter;
            }

            return default;
        }

        if (made > 0 && _closer)
        {
            _nearing.Remove(_fire);
        }

        if (made <= 0)
        {
            _raw = 0;

            _leg = Leg.Counter;

            return BotDoing.Done($"the ore burned away, {_swings} swings");
        }

        _worth = BotAuction.Worth(typeof(IronIngot), GoldPerIngot);
        _made += made * _worth;

        _raw = 0;

        _leg = Leg.Counter;

        return default;
    }

    private BotDoing Banking(IBotWilful bot)
    {
        var body = bot.Self;

        if (_counter == Point3D.Zero)
        {
            _counter = BotGround.Counter(bot, body.Location);

            if (_counter == Point3D.Zero)
            {
                return BotDoing.Failed("nowhere known to put it away");
            }
        }

        if (!body.InRange(_counter, CounterReach))
        {
            return BotDoing.Walk(_map, _counter, BotArrival.Within(CounterReach), "to a counter");
        }

        var (stored, ordered) = Store(bot);

        _stored = stored;

        _made -= ordered * _worth;

        return BotDoing.Done($"{_stored} ingots away, {ordered} of them to order, {_swings} swings");
    }

    private void Note(Mobile body, Point3D where)
    {
        var pack = body?.Backpack;

        if (pack == null)
        {
            return;
        }

        var best = CraftResource.Iron;
        var found = false;

        for (var i = 0; i < pack.Items.Count; i++)
        {
            if (pack.Items[i] is not BaseOre ore)
            {
                continue;
            }

            found = true;

            if (ore.Resource > best)
            {
                best = ore.Resource;
            }
        }

        if (found)
        {
            BotCommons.Dug(_map, where, best);
        }
    }

    public override void Drop(IBotWilful bot) => BotGround.Leave(_seam.Where);

    public override bool Bend(IBotWilful bot)
    {
        if (++_bends > MaxBends)
        {
            return false;
        }

        var body = bot?.Self;

        if (_leg == Leg.Fire)
        {
            bot?.Resolve?.Ledger?.Beware(BotGround.FireKind, _map, _fire);

            var fire = BotGround.Fire(bot, body?.Location ?? _seam.Where, _fire);

            if (fire == Point3D.Zero)
            {
                return false;
            }

            _fire = fire;

            return true;
        }

        if (_leg == Leg.Counter)
        {
            bot?.Resolve?.Ledger?.Beware(BotGround.CounterKind, _map, _counter);

            var counter = BotGround.Counter(bot, body?.Location ?? _seam.Where, _counter);

            if (counter == Point3D.Zero)
            {
                return false;
            }

            _counter = counter;

            return true;
        }

        bot?.Resolve?.Ledger?.Beware(Trade, _map, _seam.Where);

        var other = BotGround.Seam(bot, _seam.Where);

        if (!other.Exists)
        {
            return false;
        }

        _seam = other;
        _tile = null;
        _dry = 0;

        _spent.Clear();

        return true;
    }

    private static bool Loaded(Mobile body) =>
        BotLadder.Load(body) >= BotLadder.Ceiling(body) * FillFraction;

    public static bool Burdened(Mobile body) =>
        body != null && Loaded(body) && BotOre.Carried(body) < BotOre.WorthSmelting;

    private static (int Stored, int Ordered) Store(IBotWilful bot)
    {
        var body = bot.Self;
        var pack = body.Backpack;

        if (pack == null)
        {
            return (0, 0);
        }

        var box = body.BankBox;
        var stored = 0;
        var ordered = 0;

        List<Item> carried = [.. pack.Items];

        for (var i = 0; i < carried.Count; i++)
        {
            if (carried[i] is not BaseIngot ingot || ingot.Deleted || !ingot.Movable)
            {
                continue;
            }

            var amount = ingot.Amount;
            var kind = ingot.GetType();
            var worth = BotAuction.Worth(kind, GoldPerIngot);

            var want = BotAuction.Demand(bot, kind);
            var sold = want == null ? 0 : BotAuction.Fill(bot, want, ingot);

            if (sold > 0)
            {
                stored += sold;
                ordered += sold;

                if (sold >= amount)
                {
                    continue;
                }

                amount -= sold;
            }

            if (ListGoods && BotAuction.List(bot, ingot, worth) != null)
            {
                stored += amount;

                continue;
            }

            if (box == null)
            {
                continue;
            }

            stored += amount;
            box.DropItem(ingot);
        }

        var purse = pack.GetAmount(typeof(Gold));

        if (purse > 0 && pack.ConsumeTotal(typeof(Gold), purse) && !Banker.Deposit(body, purse))
        {
            pack.DropItem(new Gold(purse));
        }

        return (stored, ordered);
    }
}
