using System;
using System.Collections.Generic;
using Server.Engines.Spawners;
using Server.Items;
using Server.Logging;
using Server.Mobiles;

namespace Server.BotAI.V2;

/// <summary>
/// The spinning wheels and looms of the towns, read from the world once.
///
/// <para>
/// A wheel and a loom are addons — the tailor's shop has both — and an addon is a set of components on the map. One pass
/// over the map's components finds every <c>ISpinningWheel</c> and every <c>ILoom</c>; where each stands is what a weaver
/// walks to. Nothing is written down, by the rule the forges and the dungeons' rooms are found by.
/// </para>
/// </summary>
public static class BotLooms
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotLooms));

    private static readonly List<(Point3D Where, ISpinningWheel Wheel)> _wheels = [];

    private static readonly List<(Point3D Where, ILoom Loom)> _looms = [];

    private static Map _map;

    public static bool Surveyed { get; private set; }

    public static int Wheels => _wheels.Count;

    public static int Looms => _looms.Count;

    public static void Survey(Map map)
    {
        if (Surveyed || map == null || map == Map.Internal)
        {
            return;
        }

        Surveyed = true;
        _map = map;

        foreach (var component in map.GetItemsInBounds<AddonComponent>(new Rectangle2D(0, 0, map.Width, map.Height)))
        {
            if (component is not { Deleted: false } || component.Addon is not { Deleted: false } addon)
            {
                continue;
            }

            var where = component.GetWorldLocation();

            switch (addon)
            {
                case ISpinningWheel wheel:
                    {
                        if (!Known(_wheels, where))
                        {
                            _wheels.Add((where, wheel));
                        }

                        break;
                    }
                case ILoom loom:
                    {
                        if (!Known(_looms, where))
                        {
                            _looms.Add((where, loom));
                        }

                        break;
                    }
            }
        }

        logger.Information("Looms: {Wheels} spinning wheels and {Looms} looms on {Map}", _wheels.Count, _looms.Count, map.Name);
    }

    private static bool Known<T>(List<(Point3D Where, T Thing)> list, Point3D where)
    {
        for (var i = 0; i < list.Count; i++)
        {
            if (Math.Max(Math.Abs(list[i].Where.X - where.X), Math.Abs(list[i].Where.Y - where.Y)) <= 2)
            {
                return true;
            }
        }

        return false;
    }

    public static (Point3D Where, ISpinningWheel Wheel) NearestWheel(Point3D at, int reach)
    {
        var best = (Point3D.Zero, (ISpinningWheel)null);
        var nearest = reach + 1;

        for (var i = 0; i < _wheels.Count; i++)
        {
            var away = Math.Max(Math.Abs(_wheels[i].Where.X - at.X), Math.Abs(_wheels[i].Where.Y - at.Y));

            if (away < nearest && _wheels[i].Wheel is Item { Deleted: false })
            {
                nearest = away;
                best = _wheels[i];
            }
        }

        return best;
    }

    public static (Point3D Where, ILoom Loom) NearestLoom(Point3D at, int reach)
    {
        var best = (Point3D.Zero, (ILoom)null);
        var nearest = reach + 1;

        for (var i = 0; i < _looms.Count; i++)
        {
            var away = Math.Max(Math.Abs(_looms[i].Where.X - at.X), Math.Abs(_looms[i].Where.Y - at.Y));

            if (away < nearest && _looms[i].Loom is Item { Deleted: false })
            {
                nearest = away;
                best = _looms[i];
            }
        }

        return best;
    }

    public static void Forget()
    {
        _wheels.Clear();
        _looms.Clear();
        _map = null;
        Surveyed = false;
    }
}

/// <summary>
/// The flocks of the map, read off the spawners that keep them: where each pasture is, and which of its sheep have their wool
/// grown.
///
/// <para>
/// <b>Patrick's word of 29.09.2026: the weavers walk to the sheep; the world is not bent to the bots.</b> Build 336 asked 1331
/// times to weave in its first 105 minutes and 872 of the answers were "no shorn sheep near": the only question asked was whether a
/// sheep with its wool stood within <see cref="BotWeave.SheepReach"/> of wherever the weaver happened to be. The needle's nine live in
/// Britain (five), Minoc (one) and Yew (three). Britain's bank has no sheep that close — the nearest are single wild ones in the
/// WildLife spawners two and three hundred tiles out — and the flocks are on the farms by Yew: three spawners of ten and three of
/// fifteen round (571–676, 939–1179), with smaller ones by Skara Brae and Jhelom. In the same window the population bought 1871
/// cloth from shopkeepers for 3773gp, the largest single thing it bought.
/// </para>
///
/// <para>
/// <b>Read off the spawners rather than swept, so the question costs a list and not a map.</b> A spawner keeps its own creatures
/// (<c>SpawnerEntry.Spawned</c>) and stands where it keeps them. One pass over the map's spawners when the first weaver asks — the
/// rule the looms above are found by — keeps those with an entry that raises sheep, and from then on "how many sheep with their wool
/// grown has that flock" is a walk over a handful of references, where the old question was a sweep of every sector within a
/// hundred and fifty tiles on every ask. Nothing is written down: a spawner that stops raising sheep answers nought.
/// </para>
///
/// <para>
/// <b>Grown by the engine's clock, not by the body.</b> <c>Sheep.Carve</c> refuses by <c>NextWoolTime</c>, and the shorn body is put
/// back only in <c>OnThink</c>, which only a running AI calls. A sheep in a sector nobody has woken (<see cref="BotWake"/>) has none,
/// so a flock whose wool grew back while nobody was near read as shorn by its body for as long as it slept.
/// </para>
/// </summary>
public static class BotPastures
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotPastures));

    /// <summary>One spawner that raises sheep, and the entries on it that do.</summary>
    public sealed class Pasture
    {
        public Pasture(BaseSpawner spawner, List<SpawnerEntry> flocks)
        {
            Spawner = spawner;
            Where = spawner.Location;
            Flocks = flocks;
        }

        public BaseSpawner Spawner { get; }

        public Point3D Where { get; }

        public List<SpawnerEntry> Flocks { get; }

        public long RestedAt { get; set; }

        public long RestFor { get; set; }

        public bool Rested { get; set; }
    }

    private static readonly List<Pasture> _pastures = [];

    private static Map _map;

    public static bool Surveyed { get; private set; }

    public static IReadOnlyList<Pasture> All => _pastures;

    public static int Count => _pastures.Count;

    public static int RestLeastMs { get; set; } = 600000;

    public static int RestMostMs { get; set; } = 10800000;

    public static long FoundShorn { get; private set; }

    public static void Survey(Map map)
    {
        if (Surveyed || map == null || map == Map.Internal)
        {
            return;
        }

        Surveyed = true;
        _map = map;

        foreach (var spawner in map.GetItemsInBounds<BaseSpawner>(new Rectangle2D(0, 0, map.Width, map.Height)))
        {
            if (spawner is not { Deleted: false } || spawner.Entries == null)
            {
                continue;
            }

            List<SpawnerEntry> flocks = null;

            for (var i = 0; i < spawner.Entries.Count; i++)
            {
                var entry = spawner.Entries[i];

                if (string.Equals(entry?.SpawnedName?.Trim(), nameof(Server.Mobiles.Sheep), StringComparison.OrdinalIgnoreCase))
                {
                    (flocks ??= []).Add(entry);
                }
            }

            if (flocks != null)
            {
                _pastures.Add(new Pasture(spawner, flocks));
            }
        }

        var sheep = 0;
        var grown = 0;

        for (var i = 0; i < _pastures.Count; i++)
        {
            sheep += Kept(_pastures[i]);
            grown += Ready(_pastures[i]);
        }

        logger.Information(
            "Pastures: {Count} spawners on {Map} raise sheep, keeping {Sheep} now, {Grown} of them with their wool grown",
            _pastures.Count,
            map.Name,
            sheep,
            grown
        );
    }

    public static bool Grown(Sheep sheep) =>
        sheep is { Deleted: false, Alive: true, Controlled: false } && Core.Now >= sheep.NextWoolTime;

    public static int Kept(Pasture pasture)
    {
        var count = 0;
        var flocks = pasture?.Flocks;

        for (var i = 0; flocks != null && i < flocks.Count; i++)
        {
            var spawned = flocks[i]?.Spawned;

            for (var j = 0; spawned != null && j < spawned.Count; j++)
            {
                if (spawned[j] is Sheep { Deleted: false, Alive: true, Controlled: false } sheep && sheep.Map == _map)
                {
                    count++;
                }
            }
        }

        return count;
    }

    public static int Ready(Pasture pasture)
    {
        var count = 0;
        var flocks = pasture?.Flocks;

        for (var i = 0; flocks != null && i < flocks.Count; i++)
        {
            var spawned = flocks[i]?.Spawned;

            for (var j = 0; spawned != null && j < spawned.Count; j++)
            {
                if (spawned[j] is Sheep sheep && sheep.Map == _map && Grown(sheep))
                {
                    count++;
                }
            }
        }

        return count;
    }

    public static Sheep Nearest(Pasture pasture, Point3D from, HashSet<Serial> shunned = null)
    {
        Sheep best = null;
        var nearest = int.MaxValue;
        var flocks = pasture?.Flocks;

        for (var i = 0; flocks != null && i < flocks.Count; i++)
        {
            var spawned = flocks[i]?.Spawned;

            for (var j = 0; spawned != null && j < spawned.Count; j++)
            {
                if (spawned[j] is not Sheep sheep || sheep.Map != _map || !Grown(sheep) || shunned?.Contains(sheep.Serial) == true)
                {
                    continue;
                }

                var away = Math.Max(Math.Abs(sheep.X - from.X), Math.Abs(sheep.Y - from.Y));

                if (away < nearest)
                {
                    nearest = away;
                    best = sheep;
                }
            }
        }

        return best;
    }

    public static Pasture Next(Point3D from, int reach, Pasture but)
    {
        Pasture best = null;
        var nearest = reach + 1;

        for (var i = 0; i < _pastures.Count; i++)
        {
            var pasture = _pastures[i];
            var away = Math.Max(Math.Abs(pasture.Where.X - from.X), Math.Abs(pasture.Where.Y - from.Y));

            if (away >= nearest || ReferenceEquals(pasture, but) || Resting(pasture) || Ready(pasture) <= 0)
            {
                continue;
            }

            nearest = away;
            best = pasture;
        }

        return best;
    }

    public static bool Resting(Pasture pasture) =>
        pasture is { Rested: true } && Core.TickCount - pasture.RestedAt < pasture.RestFor;

    public static void Rest(Pasture pasture)
    {
        if (pasture == null)
        {
            return;
        }

        var soonest = long.MaxValue;
        var flocks = pasture.Flocks;

        for (var i = 0; flocks != null && i < flocks.Count; i++)
        {
            var spawned = flocks[i]?.Spawned;

            for (var j = 0; spawned != null && j < spawned.Count; j++)
            {
                if (spawned[j] is Sheep { Deleted: false, Alive: true } sheep)
                {
                    soonest = Math.Min(soonest, (long)(sheep.NextWoolTime - Core.Now).TotalMilliseconds);
                }
            }
        }

        FoundShorn++;
        pasture.Rested = true;
        pasture.RestedAt = Core.TickCount;
        pasture.RestFor = Math.Clamp(soonest == long.MaxValue ? RestLeastMs : soonest, RestLeastMs, Math.Max(RestLeastMs, RestMostMs));
    }

    public static string Describe() =>
        !Surveyed
            ? "no pasture surveyed yet"
            : $"{_pastures.Count} pastures on the map, {FoundShorn} times a flock was found or left shorn and let be";

    public static void Forget()
    {
        _pastures.Clear();
        _map = null;
        Surveyed = false;
        FoundShorn = 0;
    }
}

/// <summary>
/// From a sheep to a bolt of cloth: shear, spin, weave, cut — and cut some of the cloth into bandages.
///
/// <para>
/// <b>Patrick's sixth point of the night of 29.09.2026: make the economy self-sufficient.</b> In twenty-five minutes of build
/// 282 the population bought 435 things over counters, and 334 of them were cloth (180) and bandages (154): the tailors'
/// cloth and everybody's bandages, and neither had a source on the island. The engine has had the whole chain since the
/// first day: a sheep with its wool grown back gives two wool to scissors (<c>Sheep.Carve</c>), a spinning wheel turns one
/// wool into three yarn (<c>Wool.OnSpun</c>), a loom takes five yarn for a bolt (<c>BaseClothMaterial</c>, phases 0–4),
/// scissors cut a bolt into fifty cloth (<c>BoltOfCloth.Scissor</c>) and a cloth into a bandage (<c>Cloth.Scissor</c>). So
/// two wool are a bolt, and a bolt is fifty cloth or fifty bandages. Britain's sheep graze by its farms; the tailor's shop
/// has the wheel and the loom.
/// </para>
///
/// <para>
/// <b>Everything here is the engine's own verb, said by the bot.</b> The shearing is the sheep's method with the bot's
/// scissors; the spinning is the wheel's timer with the wool's callback; the loom's phases are stepped exactly as the
/// yarn's target steps them; the cutting is the bolt's and the cloth's own <c>Scissor</c>. Nothing is made out of nothing.
/// </para>
/// </summary>
public sealed class BotWeave : BotDeed
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotWeave));

    public const string Trade = "weave";

    public static double Prior { get; set; } = 140.0;

    public static double WorkMinutes { get; set; } = 6.0;

    public static int WoolWanted { get; set; } = 2;

    public static int SheepReach { get; set; } = 150;

    public static int LoomReach { get; set; } = 800;

    public static int StepMs { get; set; } = 1500;

    public static double BandageShare { get; set; } = 0.2;

    public static int CapMs { get; set; } = 900000;

    public static int WoolAfield { get; set; } = 10;

    public static int AfieldCapMs { get; set; } = 1800000;

    public static int MostBends { get; set; } = 3;

    public static int ClothGuess { get; set; } = 2;

    public static long Stints { get; private set; }

    public static long Sheared { get; private set; }

    public static long Bolts { get; private set; }

    public static long ClothMade { get; private set; }

    public static long BandagesMade { get; private set; }

    public static long NoSheep { get; private set; }

    public static long NoLoom { get; private set; }

    public static long Afield { get; private set; }

    public static long ClothListed { get; private set; }

    public static long ClothOrdered { get; private set; }

    public static long ClothUnlisted { get; private set; }

    public static long Unreached { get; private set; }

    private enum Leg
    {
        Shear,
        Spin,
        Weave,
        Cut,
        Done
    }

    private readonly Map _map;

    private readonly Point3D _where;

    private readonly bool _bandages;

    private readonly long _began;

    private Leg _leg = Leg.Shear;

    private long _stepTick;

    private int _sheared;

    private int _bolts;

    private int _cloth;

    private int _bandaged;

    private Sheep _sheep;

    private BotPastures.Pasture _pasture;

    private readonly int _woolWanted;

    private readonly int _cap;

    private readonly int _clothWorth;

    private readonly int _bandageWorth;

    private HashSet<Serial> _shunned;

    private int _bends;

    public BotWeave(Map map, Point3D where, bool bandages) : this(map, where, bandages, null, 0, false)
    {
    }

    public BotWeave(Map map, Point3D where, bool bandages, BotPastures.Pasture pasture, int wool, bool far)
    {
        _map = map;
        _where = where;
        _bandages = bandages;
        _began = Core.TickCount;
        _pasture = pasture;
        _woolWanted = Math.Clamp(wool, WoolWanted, Math.Max(WoolWanted, WoolAfield));
        _far = far;
        _cap = far ? Math.Max(CapMs, AfieldCapMs) : CapMs;
        _clothWorth = Math.Max(1, BotAuction.Worth(typeof(Cloth), ClothGuess));
        _bandageWorth = Math.Max(1, BotAuction.Worth(typeof(Bandage), 5));
    }

    private readonly bool _far;

    public override string Kind => Trade;

    public override Map Map => _map;

    public override Point3D Where => _where;

    public override double Expects => Prior;

    public override double Minutes => WorkMinutes * Math.Max(1.0, _woolWanted / (double)Math.Max(1, WoolWanted));

    public override SkillName? Trains => SkillName.Tailoring;

    public override double Coin => 0.6;

    public override int Made => _cloth * _clothWorth + _bandaged * _bandageWorth;

    public override bool Bend(IBotWilful bot)
    {
        if (_leg != Leg.Shear)
        {
            return false;
        }

        if (_sheep != null)
        {
            (_shunned ??= []).Add(_sheep.Serial);
            _sheep = null;
        }

        Unreached++;

        if (++_bends <= MostBends)
        {
            return true;
        }

        if (_pasture != null)
        {
            bot?.Resolve?.Ledger?.Beware(Trade, _map, _pasture.Where);
        }

        return false;
    }

    public override bool Steadfast => true;

    public override string Stage => _leg switch
    {
        Leg.Shear => $"shearing sheep ({_sheared} wool)",
        Leg.Spin => "spinning wool at the wheel",
        Leg.Weave => $"weaving at the loom ({_bolts} bolts)",
        Leg.Cut => "cutting the bolt into cloth",
        _ => "putting the cloth away"
    };

    private static Scissors ScissorsOf(Mobile body) => BotOutfit.Oldest<Scissors>(body?.Backpack);

    private static int Wool(Mobile body) => body?.Backpack?.GetAmount(typeof(Wool)) ?? 0;

    private static int Yarn(Mobile body) => body?.Backpack?.GetAmount(typeof(BaseClothMaterial), true) ?? 0;

    public override BotDoing Advance(IBotWilful bot)
    {
        var body = bot?.Self;

        if (body == null || _map == null || _map == Map.Internal)
        {
            return BotDoing.Failed("no body");
        }

        if (Core.TickCount - _began >= _cap)
        {
            return Finish(bot, body, "the stint ran out of time");
        }

        var scissors = ScissorsOf(body);

        if (scissors == null)
        {
            return _cloth > 0 ? Finish(bot, body, "the scissors wore out") : BotDoing.Failed("no scissors");
        }

        for (var guard = 0; guard < 4; guard++)
        {
            var doing = _leg switch
            {
                Leg.Shear => Shearing(body, scissors),
                Leg.Spin => Spinning(body),
                Leg.Weave => Weaving(body),
                Leg.Cut => Cutting(body, scissors),
                _ => Finish(bot, body, null)
            };

            if (doing.Kind != BotDoingKind.Work || doing.Note != "next")
            {
                return doing;
            }
        }

        return BotDoing.Work(Stage);
    }

    private BotDoing Shearing(Mobile body, Scissors scissors)
    {
        if (Wool(body) >= _woolWanted || Yarn(body) >= 5 * Math.Max(1, _woolWanted / Math.Max(1, WoolWanted)))
        {
            _leg = Leg.Spin;

            return BotDoing.Work("next");
        }

        if (_sheep is not { Deleted: false, Alive: true } || _sheep.Map != _map || !BotPastures.Grown(_sheep))
        {
            _sheep = _pasture != null ? Flock(body) : Nearest(body);

            if (_sheep == null)
            {
                NoSheep++;

                if (Wool(body) > 0 || Yarn(body) > 0)
                {
                    return Next(Leg.Spin);
                }

                return _pasture != null
                    ? BotDoing.Failed($"the flock by ({_pasture.Where.X}, {_pasture.Where.Y}) had no sheep with its wool grown")
                    : BotDoing.Failed($"no sheep with its wool grown within {SheepReach} tiles");
            }
        }

        if (!body.InRange(_sheep.Location, 1))
        {
            return BotDoing.Walk(_map, _sheep, BotArrival.Within(1), "up to a sheep");
        }

        var before = Wool(body);

        _sheep.Carve(body, scissors);

        var got = Wool(body) - before;

        if (got > 0)
        {
            _sheared += got;
            Sheared += got;
        }

        _sheep = null;

        return BotDoing.Work($"shearing sheep ({_sheared} wool)");
    }

    private Sheep Flock(Mobile body)
    {
        for (var tries = 0; tries < 3 && _pasture != null; tries++)
        {
            var sheep = BotPastures.Nearest(_pasture, body.Location, _shunned);

            if (sheep != null)
            {
                return sheep;
            }

            BotPastures.Rest(_pasture);
            _pasture = BotPastures.Next(body.Location, SheepReach, _pasture);
        }

        return null;
    }

    private Sheep Nearest(Mobile body)
    {
        Sheep best = null;
        var nearest = int.MaxValue;

        foreach (var sheep in _map.GetMobilesInRange<Sheep>(body.Location, SheepReach))
        {
            if (!BotPastures.Grown(sheep) || _shunned?.Contains(sheep.Serial) == true)
            {
                continue;
            }

            var away = Math.Max(Math.Abs(sheep.X - body.X), Math.Abs(sheep.Y - body.Y));

            if (away < nearest)
            {
                nearest = away;
                best = sheep;
            }
        }

        return best;
    }

    private BotDoing Next(Leg leg)
    {
        _leg = leg;

        return BotDoing.Work("next");
    }

    private BotDoing Spinning(Mobile body)
    {
        if (Wool(body) <= 0)
        {
            return Next(Yarn(body) > 0 ? Leg.Weave : Leg.Done);
        }

        var (where, wheel) = BotLooms.NearestWheel(body.Location, LoomReach);

        if (wheel == null)
        {
            NoLoom++;

            return BotDoing.Failed($"no spinning wheel within {LoomReach} tiles");
        }

        if (!body.InRange(where, 2))
        {
            return BotDoing.Walk(_map, where, BotArrival.Within(2), "to the spinning wheel");
        }

        if (wheel.Spinning || Core.TickCount - _stepTick < StepMs)
        {
            return BotDoing.Work("spinning wool at the wheel");
        }

        if (body.Backpack?.FindItemByType<Wool>() is not { Deleted: false } wool)
        {
            return Next(Leg.Weave);
        }

        _stepTick = Core.TickCount;

        var hue = wool.Hue;

        wool.Consume();
        wheel.BeginSpin(wool.OnSpun, body, hue);

        return BotDoing.Work("spinning wool at the wheel");
    }

    private BotDoing Weaving(Mobile body)
    {
        if (Yarn(body) <= 0)
        {
            return Next(body.Backpack?.GetAmount(typeof(BoltOfCloth)) > 0 ? Leg.Cut : Wool(body) > 0 ? Leg.Spin : Leg.Done);
        }

        var (where, loom) = BotLooms.NearestLoom(body.Location, LoomReach);

        if (loom == null)
        {
            NoLoom++;

            return BotDoing.Failed($"no loom within {LoomReach} tiles");
        }

        if (!body.InRange(where, 2))
        {
            return BotDoing.Walk(_map, where, BotArrival.Within(2), "to the loom");
        }

        if (Core.TickCount - _stepTick < StepMs)
        {
            return BotDoing.Work($"weaving at the loom ({_bolts} bolts)");
        }

        if (body.Backpack?.FindItemByType<BaseClothMaterial>() is not { Deleted: false } yarn)
        {
            return Next(Leg.Cut);
        }

        _stepTick = Core.TickCount;

        if (loom.Phase < 4)
        {
            yarn.Consume();
            loom.Phase++;
        }
        else
        {
            var bolt = new BoltOfCloth { Hue = yarn.Hue };

            yarn.Consume();
            loom.Phase = 0;
            body.AddToBackpack(bolt);
            _bolts++;
            Bolts++;
        }

        return BotDoing.Work($"weaving at the loom ({_bolts} bolts)");
    }

    private BotDoing Cutting(Mobile body, Scissors scissors)
    {
        var pack = body.Backpack;
        var bolt = pack?.FindItemByType<BoltOfCloth>();

        if (bolt is { Deleted: false })
        {
            var before = pack.GetAmount(typeof(Cloth));

            if (!bolt.Scissor(body, scissors))
            {
                return BotDoing.Failed("the bolt would not cut");
            }

            var cut = pack.GetAmount(typeof(Cloth)) - before;

            _cloth += Math.Max(0, cut);
            ClothMade += Math.Max(0, cut);
            BotCraftwork.Produced(typeof(Cloth), Math.Max(0, cut));

            return BotDoing.Work("cutting the bolt into cloth");
        }

        if (_bandages && _cloth > 0)
        {
            _bandaged += Bandages(body, (int)Math.Ceiling(_cloth * BandageShare));
        }

        return Next(Leg.Done);
    }

    public static int Bandages(Mobile body, int many)
    {
        var pack = body?.Backpack;
        var scissors = ScissorsOf(body);

        if (pack == null || scissors == null || many <= 0)
        {
            return 0;
        }

        var cloth = pack.FindItemByType<Cloth>();

        if (cloth is not { Deleted: false })
        {
            return 0;
        }

        var take = Math.Min(many, cloth.Amount);

        if (take <= 0)
        {
            return 0;
        }

        if (take < cloth.Amount && Mobile.LiftItemDupe(cloth, take) == null)
        {
            return 0;
        }

        var piece = cloth;
        var before = pack.GetAmount(typeof(Bandage));

        if (!piece.Scissor(body, scissors))
        {
            return 0;
        }

        var made = Math.Max(0, pack.GetAmount(typeof(Bandage)) - before);

        BandagesMade += made;
        BotCraftwork.Produced(typeof(Bandage), made);

        return made;
    }

    private BotDoing Finish(IBotWilful bot, Mobile body, string because)
    {
        Stints++;

        if (_far)
        {
            Afield++;
        }

        var listed = Spare(bot, body);

        logger.Information(
            "{Name} sheared {Wool} wool, wove {Bolts} bolts and cut {Cloth} cloth and {Bandages} bandages, {Listed} cloth to the market at {Price}gp{Because}",
            body.Name,
            _sheared,
            _bolts,
            _cloth,
            _bandaged,
            listed,
            Ask(bot),
            because == null ? "" : $"; stopped because {because}"
        );

        return _cloth > 0 || _bandaged > 0
            ? BotDoing.Done($"{_cloth} cloth and {_bandaged} bandages from {_sheared} wool")
            : BotDoing.Failed($"nothing came of it{(because == null ? "" : $": {because}")}");
    }

    public static int Ask(IBotWilful bot)
    {
        var worth = BotAuction.Worth(typeof(Cloth), ClothGuess);
        var shelf = BotShops.Shelf(bot, typeof(Cloth), worth);

        return Math.Max(BotAuction.Floor, Math.Min(worth, shelf));
    }

    private static int Spare(IBotWilful bot, Mobile body)
    {
        if (!BotDig.ListGoods || body?.Backpack == null)
        {
            return 0;
        }

        var spare = BotThread.Amount(body, typeof(Cloth)) - BotSew.Bolt * 2;
        var price = Ask(bot);
        var went = 0;

        List<Item> carried = [.. body.Backpack.Items];

        for (var i = 0; i < carried.Count && spare > 0; i++)
        {
            if (carried[i] is not Cloth { Deleted: false, Movable: true } cloth)
            {
                continue;
            }

            var give = Math.Min(spare, cloth.Amount);

            if (give < cloth.Amount && Mobile.LiftItemDupe(cloth, give) == null)
            {
                continue;
            }

            var want = BotAuction.Demand(bot, typeof(Cloth));
            var ordered = want == null ? 0 : BotAuction.Fill(bot, want, cloth);

            ClothOrdered += ordered;

            var listed = cloth.Deleted || ordered >= give ? 0
                : BotAuction.List(bot, cloth, price, true, true) == null ? 0 : give - ordered;

            if (!cloth.Deleted && ordered < give && listed == 0)
            {
                ClothUnlisted += give - ordered;
            }

            went += ordered + listed;
            spare -= give;
        }

        ClothListed += went;

        return went;
    }

    public static string Describe() =>
        Stints == 0
            ? $"no weaving has ended yet; {BotPastures.Describe()}"
            : $"{Stints} weaving stints ended ({Afield} of them sent past {SheepReach} tiles to the flock or on to the loom): {Sheared} wool sheared, {Bolts} bolts woven, {ClothMade} cloth and {BandagesMade} bandages cut, {ClothListed} cloth put out ({ClothOrdered} to orders, {ClothUnlisted} the market would not take); {NoSheep} looks found no sheep, {Unreached} sheep could not be walked up to and {NoLoom} looks found no wheel or loom; {BotPastures.Describe()}";

    public static void Forget()
    {
        Stints = 0;
        Sheared = 0;
        Bolts = 0;
        ClothMade = 0;
        BandagesMade = 0;
        NoSheep = 0;
        NoLoom = 0;
        Afield = 0;
        ClothListed = 0;
        ClothOrdered = 0;
        ClothUnlisted = 0;
        Unreached = 0;
    }
}

/// <summary>
/// Offers a bot with scissors and the tailor's trade a weaving stint when its cloth is short: at the flock that gives the most wool
/// for the walk, as far off as the bot's leash allows.
///
/// <para>
/// <b>Asked of the flocks, not of the bot's surroundings (30.09.2026).</b> Until build 336 this asked whether a sheep with its wool
/// stood within <see cref="BotWeave.SheepReach"/> of the bot and refused the stint otherwise — 872 of 1331 asks — so a weaver in
/// Britain never wove at all and the island bought its cloth. Now every pasture the spawners keep (<see cref="BotPastures"/>) is
/// weighed as the woods and the seams are: within the bot's leash and <see cref="PastureReach"/>, on the bot's own land, not refused
/// to the population or lately to this bot, not on ground that asks more than the weaver brings, not sealed, and with a wheel and a
/// loom within <see cref="BotWeave.LoomReach"/> of it. Of those, the one with the least road per sheep ready to shear. The walk
/// there is priced by the auction like every other walk: a flock three hundred tiles off wins only when its wool is worth the road.
/// </para>
/// </summary>
public sealed class BotWeaver : IBotProposer
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotWeaver));

    public static int LeastOnMarket { get; set; } = 20;

    public static int PastureReach { get; set; } = 600;

    public static int ArriveWithin { get; set; } = 8;

    public static long Asked { get; private set; }

    public static long NoScissors { get; private set; }

    public static long Stocked { get; private set; }

    public static long NoSheep { get; private set; }

    public static long NoLoom { get; private set; }

    public static long Offered { get; private set; }

    public static long Afield { get; private set; }

    public static long AfieldTiles { get; private set; }

    public static long Looked { get; private set; }

    public static long Shorn { get; private set; }

    public static long Rested { get; private set; }

    public static long Leashed { get; private set; }

    public static long Unjoined { get; private set; }

    public static long Refused { get; private set; }

    public static long Perilous { get; private set; }

    public static long Sealed { get; private set; }

    public static long LoomFar { get; private set; }

    private static bool _said;

    public string Name => "Weaver";

    public BotStanding Rung => BotStanding.Free;

    public BotDeed Propose(IBotWilful bot)
    {
        var body = bot?.Self;
        var map = body?.Map;

        if (map == null || map == Map.Internal || !body.Alive || body is not BotMobile who)
        {
            return null;
        }

        if (who.Class?.Skills == null || !Wants(who.Class))
        {
            return null;
        }

        Asked++;

        if (BotOutfit.Oldest<Scissors>(body.Backpack) == null)
        {
            NoScissors++;

            return null;
        }

        BotLooms.Survey(map);

        if (BotLooms.Wheels == 0 || BotLooms.Looms == 0)
        {
            NoLoom++;

            return null;
        }

        var bandages = Demanded(typeof(Bandage));

        if (BotThread.Amount(body, typeof(Cloth)) >= BotSew.Bolt && OnMarket(typeof(Cloth)) >= LeastOnMarket && !bandages)
        {
            Stocked++;

            return null;
        }

        var wool = body.Backpack?.GetAmount(typeof(Wool)) ?? 0;

        if (wool >= BotWeave.WoolWanted)
        {
            if (BotLooms.NearestWheel(body.Location, BotWeave.LoomReach).Wheel == null
                || BotLooms.NearestLoom(body.Location, BotWeave.LoomReach).Loom == null)
            {
                NoLoom++;

                return null;
            }

            return Offer(body, who, new BotWeave(map, body.Location, bandages));
        }

        BotPastures.Survey(map);

        if (BotPastures.Count == 0)
        {
            if (BotLooms.NearestLoom(body.Location, BotWeave.LoomReach).Loom == null)
            {
                NoLoom++;

                return null;
            }

            if (!SheepNear(map, body))
            {
                NoSheep++;

                return null;
            }

            return Offer(body, who, new BotWeave(map, body.Location, bandages));
        }

        var pasture = Choose(bot, body, map, out var ready, out var loomAway);

        if (pasture == null)
        {
            NoSheep++;

            return null;
        }

        var away = Math.Max(Math.Abs(pasture.Where.X - body.X), Math.Abs(pasture.Where.Y - body.Y));

        if (away > BotWeave.SheepReach)
        {
            Afield++;
            AfieldTiles += away;
        }

        var far = away > BotWeave.SheepReach || loomAway > BotWeave.SheepReach;

        return Offer(body, who, new BotWeave(map, pasture.Where, bandages, pasture, ready * WoolPerSheep(map), far));
    }

    private static BotWeave Offer(Mobile body, BotMobile who, BotWeave stint)
    {
        Offered++;

        if (!_said)
        {
            _said = true;

            logger.Information("{Name} the {Class} is the first to go weaving: {Wheels} wheels and {Looms} looms are known", body.Name, who.Class?.Name, BotLooms.Wheels, BotLooms.Looms);
        }

        return stint;
    }

    private static int WoolPerSheep(Map map) => map == Map.Felucca ? 2 : 1;

    private static BotPastures.Pasture Choose(IBotWilful bot, Mobile body, Map map, out int ready, out int loomAway)
    {
        ready = 0;
        loomAway = 0;

        var ledger = bot?.Resolve?.Ledger;
        var power = BotThreat.Power(body);
        var full = Math.Max(1, BotWeave.WoolAfield / WoolPerSheep(map));
        var pastures = BotPastures.All;

        BotPastures.Pasture best = null;
        var bestCost = double.MaxValue;

        for (var i = 0; i < pastures.Count; i++)
        {
            var pasture = pastures[i];
            var where = pasture.Where;

            if (Math.Max(Math.Abs(where.X - body.X), Math.Abs(where.Y - body.Y)) > PastureReach)
            {
                continue;
            }

            Looked++;

            if (BotPastures.Resting(pasture))
            {
                Rested++;

                continue;
            }

            var grown = BotPastures.Ready(pasture);

            if (grown <= 0)
            {
                Shorn++;

                continue;
            }

            if (!BotPopulation.Within(map, where, body))
            {
                Leashed++;

                continue;
            }

            if (!BotPopulation.Reachable(map, body.Location, where))
            {
                Unjoined++;

                continue;
            }

            if (BotRefused.Refusing(map, where) || ledger?.Cautious(BotWeave.Trade, map, where) == true)
            {
                Refused++;

                continue;
            }

            if (BotGround.MineOdds > 0.0 && BotQuad.Muscle(map, where) > power * BotGround.MineOdds)
            {
                Perilous++;

                continue;
            }

            if (BotReach.Ask(map, body.Location, where, BotArrival.Within(ArriveWithin)) == BotReachVerdict.Sealed)
            {
                Sealed++;

                continue;
            }

            var wheel = BotLooms.NearestWheel(where, BotWeave.LoomReach);
            var loom = BotLooms.NearestLoom(where, BotWeave.LoomReach);

            if (wheel.Wheel == null || loom.Loom == null)
            {
                LoomFar++;

                continue;
            }

            var toLoom = Math.Max(
                Math.Max(Math.Abs(wheel.Where.X - where.X), Math.Abs(wheel.Where.Y - where.Y)),
                Math.Max(Math.Abs(loom.Where.X - where.X), Math.Abs(loom.Where.Y - where.Y))
            );

            var sheep = Math.Min(grown, full);
            var road = body.GetDistanceToSqrt(where) + Math.Max(0, BotRoads.Behind(map, body.Location, where)) + toLoom;
            var cost = road / sheep;

            if (cost < bestCost)
            {
                best = pasture;
                bestCost = cost;
                ready = sheep;
                loomAway = toLoom;
            }
        }

        return best;
    }

    private static bool SheepNear(Map map, Mobile body)
    {
        foreach (var sheep in map.GetMobilesInRange<Sheep>(body.Location, BotWeave.SheepReach))
        {
            if (BotPastures.Grown(sheep))
            {
                return true;
            }
        }

        return false;
    }

    private static bool Demanded(Type kind)
    {
        var wants = BotAuction.Wants;

        for (var i = 0; i < wants.Count; i++)
        {
            if (wants[i].IsOpen && wants[i].Kind == kind)
            {
                return true;
            }
        }

        return false;
    }

    private static int OnMarket(Type kind)
    {
        var listings = BotAuction.Listings;
        var have = 0;

        for (var i = 0; i < listings.Count; i++)
        {
            if (listings[i]?.Kind == kind)
            {
                have += listings[i].Amount;
            }
        }

        return have;
    }

    private static bool Wants(BotClass klass)
    {
        var skills = klass.Skills;

        for (var i = 0; i < skills.Count; i++)
        {
            if (skills[i].Skill == SkillName.Tailoring)
            {
                return true;
            }
        }

        return false;
    }

    public static string Describe() =>
        Asked == 0
            ? "nobody with the needle has been asked to weave"
            : $"{Asked} asked to weave: {Offered} offered a stint ({Afield} to a flock past {BotWeave.SheepReach} tiles, {(Afield == 0 ? 0 : AfieldTiles / Afield)} tiles off on average), {Stocked} with cloth enough and the stalls stocked, {NoSheep} with no flock they could get to with wool grown, {NoLoom} with no wheel or loom within reach, {NoScissors} without scissors; "
              + $"{Looked} pastures within {PastureReach} tiles looked at: {Shorn} shorn, {Rested} let be after a weaver found them shorn, {Leashed} past the weaver's leash, {Unjoined} on another land, {Refused} refused, {Perilous} on ground asking more than the weaver brings, {Sealed} with no way there and {LoomFar} with no wheel and loom within {BotWeave.LoomReach}; {BotWeave.Describe()}";

    public static void Forget()
    {
        Asked = 0;
        NoScissors = 0;
        Stocked = 0;
        NoSheep = 0;
        NoLoom = 0;
        Offered = 0;
        Afield = 0;
        AfieldTiles = 0;
        Looked = 0;
        Shorn = 0;
        Rested = 0;
        Leashed = 0;
        Unjoined = 0;
        Refused = 0;
        Perilous = 0;
        Sealed = 0;
        LoomFar = 0;
        _said = false;
        BotWeave.Forget();
    }
}
