using System;
using System.Collections.Generic;
using Server.Items;
using Server.Logging;
using Server.Mobiles;
using Server.Regions;

namespace Server.BotAI.V2;

/// <summary>
/// Fires in the woods: who has lit one, who sits round it, and what an hour beside one is worth to a tired bot.
///
/// <para>
/// <b>Patrick's point 6 of 29.09.2026: "More wandering, more chance meetings, more talk. Add socialisation: some bots
/// may light a campfire in the woods and others join them; this counts as rest. Let them stand and chat with each
/// other."</b> Until now a bot met another only in a company, a war or a queue at a counter, and said something only
/// when work began or ended. A fire is the one place on the island a bot goes to be with others rather than to get
/// something, and it is the engine's own: the kindling is the camping skill's <c>Kindling</c>, lit by its own
/// double-click and roll, and what burns is the engine's <c>Campfire</c>, which puts itself out after a hundred seconds
/// unless somebody feeds it (<see cref="TryLight"/>).
/// </para>
///
/// <para>
/// <b>Rest by a fire is rest at the inn's rate.</b> <see cref="BotRest"/> counts play only while a bot is in the world,
/// and gives a whole session back for a rest of five to eight hours; a bot sitting at a lit fire has its play counted
/// backwards at that same rate (<see cref="Played"/>), so ten minutes at a fire give back about eight of the evening
/// (<see cref="RestShare"/>). And a tired bot that has sat at a fire for the thirty seconds the engine takes to call a
/// camp secure (<see cref="SecureMs"/>, <c>Campfire.OnTick</c>) goes offline there, as it would at an inn: unpaid, so
/// no cheer, and a regeneration buff for <see cref="FireBuffShare"/> of its rest, half the paid bed's (<see cref="Rested"/>).
/// Patrick's rule of the morning — "offline only at an inn or one's own house" — gains this third place and no fourth.
/// </para>
///
/// <para>
/// <b>The fire list is not saved.</b> The engine's campfire skips serialization and burns out in a hundred seconds, so a
/// restart finds no fire to go back to; a bot that went offline by one has the fire's name in its rest record
/// (<c>BotRestStore</c>), which is all that has to survive.
/// </para>
/// </summary>
public static class BotCamp
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotCamp));

    public const string FirePrefix = "a campfire";

    public static bool Running { get; set; } = true;

    public static double LighterShare { get; set; } = 0.5;

    public static int JoinReach { get; set; } = 40;

    public static int MostFires { get; set; } = 6;

    public static int MostAtFire { get; set; } = 6;

    public static int FireSpacing { get; set; } = 30;

    public static double SitMinutes { get; set; } = 6.0;

    public static int LonelyMs { get; set; } = 180000;

    public static int PrepareMs { get; set; } = 240000;

    public static int SecureMs { get; set; } = 30000;

    public static int BedGraceMs { get; set; } = 60000;

    public static int FireGraceMs { get; set; } = 30000;

    public static double RestShare { get; set; } = 1.0;

    public static double FireBuffShare { get; set; } = 1.0 / 6.0;

    public static bool SleepByFire { get; set; } = true;

    public static int ToFireRetryMs { get; set; } = 600000;

    public static int HostileReach { get; set; } = 12;

    public static int ThreatMs { get; set; } = 4000;

    public static double LeastSafety { get; set; } = 0.0;

    public static int TendTryMs { get; set; } = 1500;

    public static bool RelightWhenDim { get; set; } = true;

    public static double WearyFrom { get; set; } = 0.6;

    public static double WearyWorth { get; set; } = 40.0;

    public static double BoredWorth { get; set; } = 30.0;

    public static long Lit { get; private set; }

    public static long Relit { get; private set; }

    public static long Fumbles { get; private set; }

    public static long Joined { get; private set; }

    public static long Slept { get; private set; }

    public static long FireBuffs { get; private set; }

    public static long SentToFire { get; private set; }

    public static long NoFireToSleepBy { get; private set; }

    public static long Lonely { get; private set; }

    public static long Scattered { get; private set; }

    public static long BurnedDown { get; private set; }

    public static long NeverLit { get; private set; }

    public static long Handed { get; private set; }

    public static double RestedMinutes { get; private set; }

    public static double GivenBack { get; private set; }

    /// <summary>Where a place lies, for the question "may a fire be lit here".</summary>
    public enum Land
    {
        Wild,
        Town,
        Roof,
        Under,
        Barred,
        Bloodied
    }

    /// <summary>One seat round a fire: where it is, who holds it, and what they have done there.</summary>
    public sealed class Place
    {
        public Point3D Spot;

        public BotMobile Bot;

        public bool Seated;

        public bool Greeted;

        public int Lines;

        public long SatTick;
    }

    /// <summary>One fire: where, whose, the seats round it, and the engine's flame burning on it now.</summary>
    public sealed class Fire
    {
        public Fire(BotMobile keeper, Map map, Point3D centre, List<Point3D> seats, long now)
        {
            Keeper = keeper;
            KeeperName = keeper?.Name ?? "somebody";
            Map = map;
            Centre = centre;
            Places = new Place[seats.Count];

            for (var i = 0; i < seats.Count; i++)
            {
                Places[i] = new Place { Spot = seats[i] };
            }

            Places[0].Bot = keeper;

            CreatedTick = now;
            LitTick = now;
            BurnedTick = now;
            TendTick = now;
            ThreatTick = now;
            NextLineTick = now;
            SitMs = (long)(Math.Max(1.0, SitMinutes) * 60000 * (0.75 + 0.5 * Utility.RandomDouble()));
        }

        public Map Map { get; }

        public Point3D Centre { get; }

        public BotMobile Keeper { get; internal set; }

        public string KeeperName { get; }

        public Place[] Places { get; }

        public string Name => $"{FirePrefix} lit by {KeeperName}";

        public Campfire Flame { get; internal set; }

        public Kindling Stick { get; internal set; }

        public bool Lit { get; internal set; }

        public bool Banked { get; internal set; }

        public string BankedWhy { get; internal set; }

        public bool Closed { get; internal set; }

        public string Ended { get; internal set; }

        public long CreatedTick { get; }

        public long LitTick { get; internal set; }

        public long BurnedTick { get; internal set; }

        public long TendTick { get; internal set; }

        public long ThreatTick { get; internal set; }

        public long NextLineTick { get; internal set; }

        public long SitMs { get; }

        public int Guests { get; internal set; }

        public int Lines { get; internal set; }

        public int SleptHere { get; internal set; }

        public Mobile LastSpeaker { get; internal set; }

        public (Mobile Who, Mobile To, string Occasion) Owed { get; internal set; }

        public bool Burning => Flame is { Deleted: false } && Flame.Status != CampfireStatus.Off;

        public bool Bright => Flame is { Deleted: false } && Flame.Status == CampfireStatus.Burning;

        public bool Warm(long now) => Lit && !Closed && (Burning || now - BurnedTick < FireGraceMs);

        public bool Open(long now) => Warm(now) && !Banked && Keeper != null;

        public bool Has(Mobile bot)
        {
            if (bot == null)
            {
                return false;
            }

            for (var i = 0; i < Places.Length; i++)
            {
                if (ReferenceEquals(Places[i].Bot, bot))
                {
                    return true;
                }
            }

            return false;
        }

        public Place PlaceOf(Mobile bot)
        {
            if (bot == null)
            {
                return null;
            }

            for (var i = 0; i < Places.Length; i++)
            {
                if (ReferenceEquals(Places[i].Bot, bot))
                {
                    return Places[i];
                }
            }

            return null;
        }

        public int Free(Point3D from)
        {
            var best = -1;
            var nearest = int.MaxValue;

            for (var i = 0; i < Places.Length; i++)
            {
                if (Places[i].Bot != null)
                {
                    continue;
                }

                var away = Tiles(Places[i].Spot, from);

                if (away < nearest)
                {
                    nearest = away;
                    best = i;
                }
            }

            return best;
        }

        public int SeatedCount()
        {
            var n = 0;

            for (var i = 0; i < Places.Length; i++)
            {
                if (Places[i] is { Seated: true, Bot: not null })
                {
                    n++;
                }
            }

            return n;
        }

        public override string ToString() => $"{Name} at ({Centre.X}, {Centre.Y})";
    }

    /// <summary>Where a bot last sat at a fire, and for how long: what the rest clock and the leaving ask.</summary>
    private sealed class Sitting
    {
        public Fire Fire;

        public long Since;

        public long Seen;
    }

    private static readonly List<Fire> _fires = [];

    private static readonly Dictionary<Serial, Sitting> _sitting = [];

    private static readonly Dictionary<Serial, long> _sentToFire = [];

    private static readonly List<Serial> _stale = [];

    private static long _sweptTick;

    private static bool _swept;

    public static IReadOnlyList<Fire> Fires => _fires;

    public static bool Lighter(Mobile bot)
    {
        if (bot?.Name == null)
        {
            return false;
        }

        var hash = 2166136261u;
        var name = bot.Name;

        for (var i = 0; i < name.Length; i++)
        {
            hash ^= char.ToLowerInvariant(name[i]);
            hash *= 16777619u;
        }

        return hash % 1000 < Math.Clamp(LighterShare, 0.0, 1.0) * 1000;
    }

    public static int Sticks(Mobile bot) => bot?.Backpack?.GetAmount(typeof(Kindling)) ?? 0;

    public static Item Blade(Mobile bot)
    {
        if (bot == null)
        {
            return null;
        }

        if (bot.FindItemOnLayer(Layer.OneHanded) is BaseKnife or BaseSword)
        {
            return bot.FindItemOnLayer(Layer.OneHanded);
        }

        if (bot.FindItemOnLayer(Layer.TwoHanded) is BaseKnife or BaseSword)
        {
            return bot.FindItemOnLayer(Layer.TwoHanded);
        }

        var pack = bot.Backpack;

        return pack == null ? null : (Item)pack.FindItemByType<BaseKnife>() ?? pack.FindItemByType<BaseSword>();
    }

    public static bool Lights(Mobile bot)
    {
        var skill = bot?.Skills[SkillName.Camping];

        return skill != null && (skill.Value > 0.0 || skill.Lock == SkillLock.Up && bot.Skills.Total < bot.Skills.Cap);
    }

    public static double Weariness(Mobile bot)
    {
        if (bot?.Name == null || !BotRest.Records.TryGetValue(bot.Name, out var record) || record.Hours <= 0.0)
        {
            return 0.0;
        }

        return Math.Clamp(record.PlayedMinutes / (record.Hours * 60.0), 0.0, 1.0);
    }

    public static double Claim(BotMobile bot, double prior)
    {
        var weary = Math.Clamp((Weariness(bot) - WearyFrom) / Math.Max(0.01, 1.0 - WearyFrom), 0.0, 1.0);
        var bored = bot?.Resolve?.Urges?.Boredom ?? 0.0;

        return prior + weary * WearyWorth + bored * BoredWorth;
    }

    public static Land LandOf(Map map, Point3D where)
    {
        var region = Region.Find(where, map);

        if (region.IsPartOf<GuardedRegion>())
        {
            return Land.Town;
        }

        if (region.IsPartOf<HouseRegion>())
        {
            return Land.Roof;
        }

        if (region.IsPartOf<DungeonRegion>() || BotDungeon.Under(where))
        {
            return Land.Under;
        }

        if (BotBarred.Holds(map, where))
        {
            return Land.Barred;
        }

        return BotQuad.Safety(map, where) < LeastSafety ? Land.Bloodied : Land.Wild;
    }

    public static bool Crowded(Map map, Point3D where)
    {
        for (var i = 0; i < _fires.Count; i++)
        {
            var fire = _fires[i];

            if (!fire.Closed && fire.Map == map && Tiles(fire.Centre, where) < FireSpacing)
            {
                return true;
            }
        }

        return false;
    }

    public static int Count()
    {
        var n = 0;

        for (var i = 0; i < _fires.Count; i++)
        {
            if (!_fires[i].Closed)
            {
                n++;
            }
        }

        return n;
    }

    /// <summary>Why no seat was found, for the proposer's buckets.</summary>
    public enum Refusal
    {
        None,
        Far,
        Full,
        Unwelcome
    }

    public static Fire Nearest(BotMobile bot, long now, out int seat, out Refusal refusal)
    {
        seat = -1;
        refusal = Refusal.Far;

        Fire best = null;
        var nearest = int.MaxValue;

        for (var i = 0; i < _fires.Count; i++)
        {
            var fire = _fires[i];

            if (!fire.Open(now) || fire.Map != bot.Map || fire.Has(bot))
            {
                continue;
            }

            var away = Tiles(fire.Centre, bot.Location);

            if (away > JoinReach || away >= nearest)
            {
                continue;
            }

            if (!Welcome(bot, fire.Keeper))
            {
                refusal = Refusal.Unwelcome;

                continue;
            }

            var free = fire.Free(bot.Location);

            if (free < 0)
            {
                refusal = Refusal.Full;

                continue;
            }

            best = fire;
            nearest = away;
            seat = free;
        }

        if (best != null)
        {
            refusal = Refusal.None;
        }

        return best;
    }

    public static bool Welcome(Mobile one, Mobile other)
    {
        if (one == null || other == null || ReferenceEquals(one, other))
        {
            return true;
        }

        var there = Notoriety.Compute(one, other);
        var back = Notoriety.Compute(other, one);

        return there is Notoriety.Innocent or Notoriety.Ally && back is Notoriety.Innocent or Notoriety.Ally;
    }

    public static bool Hearth(Mobile body, Map map, int reach, int tries, out Point3D centre, out List<Point3D> seats, out string why)
    {
        centre = Point3D.Zero;
        seats = null;
        why = "no ground was looked at";

        for (var t = 0; t <= tries; t++)
        {
            var x = t == 0 ? body.X : body.X + Utility.RandomMinMax(-reach, reach);
            var y = t == 0 ? body.Y : body.Y + Utility.RandomMinMax(-reach, reach);

            if (!BotStep.Settle(map, x, y, out var z))
            {
                why = "no footing";

                continue;
            }

            var here = new Point3D(x, y, z);
            var land = LandOf(map, here);

            if (land != Land.Wild)
            {
                why = $"the ground is {Words(land)}";

                continue;
            }

            if (Crowded(map, here))
            {
                why = $"another fire burns within {FireSpacing} tiles";

                continue;
            }

            if (!map.CanFit(x, y, z, 16, false, false))
            {
                why = "nothing can be laid there";

                continue;
            }

            var ring = Ring(map, here, body.Location);

            if (ring.Count < BotKindle.LeastSeats)
            {
                why = $"only {ring.Count} places to sit round it";

                continue;
            }

            centre = here;
            seats = ring;

            return true;
        }

        return false;
    }

    private static readonly (int X, int Y)[] RingOffsets =
    [
        (0, -2), (2, -2), (2, 0), (2, 2), (0, 2), (-2, 2), (-2, 0), (-2, -2),
        (1, -2), (2, -1), (2, 1), (1, 2), (-1, 2), (-2, 1), (-2, -1), (-1, -2)
    ];

    private static List<Point3D> Ring(Map map, Point3D centre, Point3D keeper)
    {
        List<Point3D> seats = [];
        var most = Math.Clamp(MostAtFire, 2, RingOffsets.Length);

        for (var i = 0; i < RingOffsets.Length && seats.Count < most; i++)
        {
            var x = centre.X + RingOffsets[i].X;
            var y = centre.Y + RingOffsets[i].Y;

            if (!BotStep.Settle(map, x, y, out var z) || Math.Abs(z - centre.Z) > 8 || !map.CanFit(x, y, z, 16, false, false))
            {
                continue;
            }

            var spot = new Point3D(x, y, z);
            var apart = true;

            for (var k = 0; k < seats.Count && apart; k++)
            {
                apart = Tiles(seats[k], spot) >= 2;
            }

            if (apart)
            {
                seats.Add(spot);
            }
        }

        var first = 0;

        for (var i = 1; i < seats.Count; i++)
        {
            if (Tiles(seats[i], keeper) < Tiles(seats[first], keeper))
            {
                first = i;
            }
        }

        if (first > 0)
        {
            (seats[0], seats[first]) = (seats[first], seats[0]);
        }

        return seats;
    }

    public static Fire Lay(BotMobile keeper, Map map, Point3D centre, List<Point3D> seats)
    {
        var fire = new Fire(keeper, map, centre, seats, Core.TickCount);

        _fires.Add(fire);

        return fire;
    }

    internal static bool TryLight(Fire fire, Mobile tender, out bool caught)
    {
        caught = false;

        if (fire?.Map == null || tender?.Backpack == null)
        {
            return false;
        }

        var map = fire.Map;
        var stick = fire.Stick;

        if (stick is not { Deleted: false } || stick.Parent != null || stick.Map != map || stick.X != fire.Centre.X || stick.Y != fire.Centre.Y)
        {
            var carried = tender.Backpack.FindItemByType<Kindling>();

            if (carried == null)
            {
                return false;
            }

            if (carried.Amount > 1)
            {
                carried.Consume(1);
                stick = new Kindling();
            }
            else
            {
                stick = carried;
            }

            stick.MoveToWorld(fire.Centre, map);
            fire.Stick = stick;
        }

        var old = fire.Flame;

        stick.OnDoubleClick(tender);

        if (!stick.Deleted)
        {
            Fumbles++;

            return true;
        }

        fire.Stick = null;

        Campfire flame = null;

        foreach (var item in map.GetItemsAt<Campfire>(fire.Centre))
        {
            if (item is { Deleted: false } && !ReferenceEquals(item, old) && (flame == null || item.Created > flame.Created))
            {
                flame = item;
            }
        }

        if (flame == null)
        {
            return true;
        }

        if (old is { Deleted: false })
        {
            old.Delete();
        }

        fire.Flame = flame;
        fire.BurnedTick = Core.TickCount;
        caught = true;

        return true;
    }

    internal static void Kindled(Fire fire, Mobile keeper)
    {
        var now = Core.TickCount;

        fire.Lit = true;
        fire.LitTick = now;
        fire.BurnedTick = now;
        fire.NextLineTick = now;
        Lit++;

        logger.Information(
            "{Name} the {Class} has lit a campfire at ({X}, {Y}) with {Seats} seats round it (Camping {Skill:F1}); it means to keep it {Minutes:F0} minutes",
            keeper?.Name,
            (keeper as BotMobile)?.Class?.Name,
            fire.Centre.X,
            fire.Centre.Y,
            fire.Places.Length,
            keeper?.Skills[SkillName.Camping].Value ?? 0.0,
            fire.SitMs / 60000.0
        );

        BotEvents.Post("camp", keeper, $"lit a campfire at ({fire.Centre.X}, {fire.Centre.Y})");
    }

    internal static bool Hold(Fire fire, BotMobile bot, ref int seat)
    {
        if (fire == null || bot == null || fire.Closed)
        {
            return false;
        }

        if (fire.PlaceOf(bot) is { } held)
        {
            seat = Array.IndexOf(fire.Places, held);

            return true;
        }

        if (seat < 0 || seat >= fire.Places.Length || fire.Places[seat].Bot != null)
        {
            seat = fire.Free(bot.Location);
        }

        if (seat < 0)
        {
            return false;
        }

        fire.Places[seat].Bot = bot;
        fire.Places[seat].Seated = false;
        fire.Places[seat].Greeted = false;
        fire.Places[seat].Lines = 0;

        return true;
    }

    internal static void Sat(Fire fire, BotMobile bot)
    {
        if (fire?.PlaceOf(bot) is not { } place || place.Seated)
        {
            return;
        }

        place.Seated = true;
        place.SatTick = Core.TickCount;

        if (!ReferenceEquals(bot, fire.Keeper))
        {
            fire.Guests++;
            Joined++;

            BotEvents.Post("camp", bot, $"sat down at {fire.Name}");
        }
    }

    internal static void Seated(Fire fire, BotMobile bot, long now)
    {
        if (fire == null || bot == null || !fire.Warm(now))
        {
            return;
        }

        if (!_sitting.TryGetValue(bot.Serial, out var sitting) || !ReferenceEquals(sitting.Fire, fire) || now - sitting.Seen > BedGraceMs)
        {
            _sitting[bot.Serial] = new Sitting { Fire = fire, Since = now, Seen = now };

            return;
        }

        sitting.Seen = now;
    }

    public static bool Secure(Mobile bot) =>
        bot != null && _sitting.TryGetValue(bot.Serial, out var sitting) && sitting.Seen - sitting.Since >= SecureMs
        && Core.TickCount - sitting.Seen <= BedGraceMs;

    internal static void Let(Fire fire, BotMobile bot, string why)
    {
        if (fire == null || bot == null)
        {
            return;
        }

        var place = fire.PlaceOf(bot);

        if (place != null)
        {
            place.Bot = null;
            place.Seated = false;
        }

        if (!ReferenceEquals(fire.Keeper, bot) || fire.Closed)
        {
            return;
        }

        if (!fire.Lit)
        {
            Close(fire, $"its keeper went before it was lit: {why}");
            fire.Keeper = null;

            return;
        }

        fire.Keeper = null;

        for (var i = 0; i < fire.Places.Length; i++)
        {
            var other = fire.Places[i];

            if (other is { Seated: true, Bot: { Deleted: false, Alive: true } heir } && Sticks(heir) > 0 && Lights(heir))
            {
                fire.Keeper = heir;
                Handed++;

                logger.Information("{Keeper} leaves {Fire}, and {Heir} keeps it going", bot.Name, fire.Name, heir.Name);

                return;
            }
        }

        Bank(fire, $"{bot.Name} left it ({why}) and nobody had kindling to keep it");
    }

    internal static void Bank(Fire fire, string why)
    {
        if (fire == null || fire.Banked || fire.Closed)
        {
            return;
        }

        fire.Banked = true;
        fire.BankedWhy = why;
    }

    internal static void Close(Fire fire, string why)
    {
        if (fire == null || fire.Closed)
        {
            return;
        }

        fire.Closed = true;
        fire.Ended = why;

        if (fire.Stick is { Deleted: false, Parent: null } stick)
        {
            var keeper = fire.Keeper;

            if (keeper is { Deleted: false, Alive: true } && keeper.Map == stick.Map && keeper.InRange(stick.Location, 2))
            {
                keeper.Backpack?.DropItem(stick);
            }

            fire.Stick = null;
        }

        if (fire.Lit)
        {
            logger.Information(
                "{Fire} at ({X}, {Y}) is out after {Minutes:F1} minutes — {Why}; {Guests} came to it, {Lines} lines were said and {Slept} slept by it",
                fire.Name,
                fire.Centre.X,
                fire.Centre.Y,
                (Core.TickCount - fire.LitTick) / 60000.0,
                why,
                fire.Guests,
                fire.Lines,
                fire.SleptHere
            );
        }
    }

    public static void Tick(long now)
    {
        for (var i = _fires.Count - 1; i >= 0; i--)
        {
            var fire = _fires[i];

            try
            {
                Keep(fire, now);
            }
            catch (Exception e)
            {
                logger.Error(e, "{Fire} threw on its beat and is put out", fire.Name);
                Close(fire, "a fault on its beat");
            }

            if (fire.Closed)
            {
                _fires.RemoveAt(i);
            }
        }

        if (!_swept || now - _sweptTick >= 60000)
        {
            _swept = true;
            _sweptTick = now;
            _stale.Clear();

            foreach (var (serial, sitting) in _sitting)
            {
                if (now - sitting.Seen > 5 * BedGraceMs)
                {
                    _stale.Add(serial);
                }
            }

            for (var k = 0; k < _stale.Count; k++)
            {
                _sitting.Remove(_stale[k]);
            }
        }
    }

    private static void Keep(Fire fire, long now)
    {
        for (var i = 0; i < fire.Places.Length; i++)
        {
            var place = fire.Places[i];

            if (place.Bot is { } bot && !AtFire(bot, fire))
            {
                Let(fire, bot, "it went to other work");

                if (fire.Closed)
                {
                    return;
                }
            }
        }

        if (!fire.Lit)
        {
            if (now - fire.CreatedTick >= PrepareMs)
            {
                NeverLit++;
                Close(fire, $"it had not caught in {PrepareMs / 60000} minutes");
            }

            return;
        }

        if (fire.Burning)
        {
            fire.BurnedTick = now;
        }

        if (now - fire.ThreatTick >= ThreatMs)
        {
            fire.ThreatTick = now;

            var anchor = Anchor(fire);

            if (anchor != null && BotThreat.Strongest(anchor, HostileReach) is { } foe)
            {
                Scattered++;
                Close(fire, $"scattered by {foe.Name} coming within {HostileReach} tiles");

                return;
            }
        }

        if (!fire.Banked)
        {
            if (now - fire.LitTick >= fire.SitMs)
            {
                Bank(fire, "the evening is over");
            }
            else if (fire.Guests == 0 && now - fire.LitTick >= LonelyMs)
            {
                Lonely++;
                Bank(fire, $"nobody came in {LonelyMs / 60000} minutes");
            }
        }

        var dim = RelightWhenDim ? !fire.Bright : !fire.Burning;

        if (!fire.Banked && dim && now - fire.TendTick >= TendTryMs)
        {
            fire.TendTick = now;

            var tender = Tender(fire);

            if (tender != null && TryLight(fire, tender, out var caught) && caught)
            {
                Relit++;
            }
        }

        if (!fire.Warm(now))
        {
            BurnedDown++;
            Close(fire, fire.Banked ? $"it burned down: {fire.BankedWhy}" : "it went out and nobody could feed it");

            return;
        }

        BotCampTalk.Speak(fire, now);
    }

    private static bool AtFire(BotMobile bot, Fire fire)
    {
        if (bot.Deleted || !bot.Alive || bot.Map != fire.Map)
        {
            return false;
        }

        return Of(bot.Resolve?.Deed) == fire || Of(bot.Resolve?.Paused?.Deed) == fire;
    }

    private static Fire Of(BotDeed deed) =>
        deed switch
        {
            BotKindle kindle => kindle.Fire,
            BotFireside fireside => fireside.Fire,
            _ => null
        };

    private static Mobile Anchor(Fire fire)
    {
        if (fire.PlaceOf(fire.Keeper) is { Seated: true })
        {
            return fire.Keeper;
        }

        for (var i = 0; i < fire.Places.Length; i++)
        {
            if (fire.Places[i] is { Seated: true, Bot: { Deleted: false, Alive: true } bot })
            {
                return bot;
            }
        }

        return null;
    }

    private static Mobile Tender(Fire fire)
    {
        Mobile best = null;

        for (var i = 0; i < fire.Places.Length; i++)
        {
            if (fire.Places[i] is not { Seated: true, Bot: { Deleted: false, Alive: true } bot })
            {
                continue;
            }

            var away = Tiles(bot.Location, fire.Centre);

            if (away is < 1 or > 2 || Sticks(bot) <= 0 && fire.Stick == null || !Lights(bot))
            {
                continue;
            }

            if (ReferenceEquals(bot, fire.Keeper))
            {
                return bot;
            }

            best ??= bot;
        }

        return best;
    }

    public static double Played(BotMobile bot, double minutes, double hours)
    {
        if (!Running || bot == null || bot.Tired || minutes <= 0.0 || !_sitting.TryGetValue(bot.Serial, out var sitting))
        {
            return minutes;
        }

        var now = Core.TickCount;

        if (now - sitting.Seen > 5000 || !sitting.Fire.Warm(now))
        {
            return minutes;
        }

        var rest = Math.Max(0.1, (BotRest.LeastRestHours + BotRest.MostRestHours) / 2.0);
        var back = minutes * Math.Max(0.0, hours) / rest * Math.Max(0.0, RestShare);

        RestedMinutes += minutes;
        GivenBack += back;

        return -back;
    }

    public static bool Beds(BotMobile bot) => Running && SleepByFire && Asleep(bot, out _);

    private static bool Asleep(BotMobile bot, out Sitting sitting)
    {
        sitting = null;

        if (bot?.Map == null || !_sitting.TryGetValue(bot.Serial, out sitting))
        {
            return false;
        }

        return Core.TickCount - sitting.Seen <= BedGraceMs && sitting.Seen - sitting.Since >= SecureMs
               && sitting.Fire.Map == bot.Map && bot.InRange(sitting.Fire.Centre, Campfire.SecureRange);
    }

    public static bool ToFire(BotMobile bot)
    {
        if (!Running || !SleepByFire || bot is not { Deleted: false, Alive: true } || bot.Map == null || bot.Map == Map.Internal)
        {
            return false;
        }

        if (BotInns.At(bot) != null)
        {
            return false;
        }

        var now = Core.TickCount;

        if (_sentToFire.TryGetValue(bot.Serial, out var sent) && now - sent < ToFireRetryMs)
        {
            return false;
        }

        var fire = Nearest(bot, now, out var seat, out _);
        BotDeed deed = null;
        string why = null;

        if (fire != null)
        {
            deed = new BotFireside(fire, seat, BotFireside.Prior, true);
            why = $"tired, and going to sleep by {fire.Name}";
        }
        else if (Count() < MostFires && LandOf(bot.Map, bot.Location) == Land.Wild && !Crowded(bot.Map, bot.Location)
                 && BotKindle.Means(bot) && Lights(bot) && !BotThreat.Anything(bot, HostileReach))
        {
            deed = new BotKindle(bot.Map, bot.Location, BotKindle.Prior, true);
            why = "tired, and making camp to sleep by a fire";
        }

        if (deed == null)
        {
            NoFireToSleepBy++;

            return false;
        }

        if (!BotWill.Press(bot, deed, why))
        {
            return false;
        }

        _sentToFire[bot.Serial] = now;
        SentToFire++;

        return true;
    }

    public static string Settle(BotMobile bot)
    {
        if (!Running || !Asleep(bot, out var sitting))
        {
            return null;
        }

        _sitting.Remove(bot.Serial);
        _sentToFire.Remove(bot.Serial);
        Slept++;
        sitting.Fire.SleptHere++;

        BotEvents.Post("camp", bot, $"went to sleep by {sitting.Fire.Name}");

        return sitting.Fire.Name;
    }

    public static bool Rested(BotMobile bot, string inn, double hours, out DateTime until)
    {
        until = default;

        if (bot == null || hours <= 0.0 || FireBuffShare <= 0.0 || inn == null || !inn.StartsWith(FirePrefix, StringComparison.Ordinal))
        {
            return false;
        }

        until = Core.Now + TimeSpan.FromHours(hours * FireBuffShare);
        bot.WellRestedUntil = until;
        FireBuffs++;

        return true;
    }

    internal static int Tiles(Point3D a, Point3D b) => Math.Max(Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));

    public static string Words(Land land) =>
        land switch
        {
            Land.Town => "a town's",
            Land.Roof => "under a roof",
            Land.Under => "underground",
            Land.Barred => "barred",
            Land.Bloodied => "bloodied",
            _ => "wild"
        };

    public static string Describe()
    {
        var now = Core.TickCount;
        var burning = 0;
        var sitting = 0;

        for (var i = 0; i < _fires.Count; i++)
        {
            var fire = _fires[i];

            if (fire.Warm(now))
            {
                burning++;
                sitting += fire.SeatedCount();
            }
        }

        return !Running
            ? "fires are off"
            : $"{Lit} fires lit, {Joined} joined one, {Slept} went offline at a fire, {BotMeetings.Met} chats ({burning} burning now, {sitting} sitting at them); "
              + $"{Relit} fed as they dimmed and {Fumbles} strikes that did not catch, {Handed} handed on by a keeper who left; "
              + $"ended: {BurnedDown} burned down ({Lonely} of them put out with nobody come in {LonelyMs / 60000} minutes), {Scattered} scattered by something hostile, {NeverLit} never lit; "
              + $"{RestedMinutes:F0} minutes sat at a fire gave back {GivenBack:F0} minutes of play; {SentToFire} tired bots sent to a fire to sleep, {NoFireToSleepBy} had none near and could make none, "
              + $"{FireBuffs} rested buffs from a night by one ({FireBuffShare:P0} of the rest); "
              + $"lighting: {BotCamper.Describe()}; {BotKindle.Describe()}; joining: {BotBeckon.Describe()}; {BotFireside.Describe()}; talk: {BotCampTalk.Describe()}; meetings: {BotMeetings.Describe()}";
    }

    public static void Forget()
    {
        _fires.Clear();
        _sitting.Clear();
        _sentToFire.Clear();
        Lit = 0;
        Relit = 0;
        Fumbles = 0;
        Joined = 0;
        Slept = 0;
        FireBuffs = 0;
        SentToFire = 0;
        NoFireToSleepBy = 0;
        Lonely = 0;
        Scattered = 0;
        BurnedDown = 0;
        NeverLit = 0;
        Handed = 0;
        RestedMinutes = 0;
        GivenBack = 0;
        BotCamper.Forget();
        BotBeckon.Forget();
        BotCampTalk.Forget();
        BotMeetings.Forget();
    }
}
