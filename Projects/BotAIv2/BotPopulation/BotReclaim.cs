using System;
using System.Collections.Generic;
using Server.Items;
using Server.Logging;

namespace Server.BotAI.V2;

/// <summary>
/// Going back for what death took. The bot own corpse, and nobody else.
///
/// <para>
/// <b>Only the weapon and the book are bound; everything else is lying where the bot fell.</b> The kit was
/// deliberately narrowed to that — tools wear out and are bought again, coin is meant to move — which makes
/// dying cost a hammer, a pickaxe, a sewing kit, the bandages, the herbs and whatever was in the purse. One
/// warrior-archer died holding four hundred gold and the ledger recorded it plainly: <c>-400 coin</c>. All of
/// it was still there, twenty tiles away, and nothing in the population had any reason to go and get it.
/// </para>
///
/// <para>
/// So this is not a new mechanic so much as the missing half of an old one. The corpse is remembered on the
/// bot at the moment of death — no sweeping the world for it — and it stops being remembered the moment it is
/// emptied, decays, or turns out to hold nothing.
/// </para>
/// </summary>
public sealed class BotReclaim : BotDeed
{
    public const string Trade = "reclaim";

    public static double Prior { get; set; } = 20.0;

    public static double WorkMinutes { get; set; } = 1.5;

    public static int Reach { get; set; } = 2;

    private readonly Corpse _corpse;

    private readonly Map _map;

    private readonly Point3D _where;

    private int _taken;

    private int _coins;

    private readonly int _worth;

    public BotReclaim(Corpse corpse)
    {
        _corpse = corpse;
        _map = corpse?.Map;
        _where = corpse?.Location ?? Point3D.Zero;
    }

    public BotReclaim(Corpse corpse, int worth) : this(corpse)
    {
        _worth = worth;
    }

    public override string Kind => Trade;

    public override Map Map => _map;

    public override Point3D Where => _where;

    public override double Expects => Math.Max(Prior, _worth / Math.Max(0.1, WorkMinutes));

    public override double Minutes => WorkMinutes;

    public override SkillName? Trains => null;

    public override int Outlay => 0;

    public override double Coin => 1.0;

    public override bool Steadfast => true;

    public override double HoldsFor => HoldMinutes;

    public static double HoldMinutes { get; set; } = 15.0;

    public override int Made => 0;

    public override string Stage =>
        _taken > 0 ? $"recovered {_taken} things and {_coins}gp" : "going back for its own things";

    public override BotDoing Advance(IBotWilful bot)
    {
        var body = bot?.Self;

        if (body == null || _map == null || _map == Map.Internal)
        {
            return BotDoing.Failed("no body");
        }

        if (_corpse == null || _corpse.Deleted || _corpse.Map != _map)
        {
            Forget(bot);

            return BotDoing.Done("there was nothing left of it");
        }

        if (!body.InRange(_corpse.Location, Reach))
        {
            return BotDoing.Walk(_map, _corpse.Location, BotArrival.Within(Reach), "back for its own things");
        }

        Empty(body);

        (bot as BotMobile)?.Rearm();

        Forget(bot);

        if (BotUndertaker.Below(_where))
        {
            BotUndertaker.Reached(_taken, _coins);
        }

        return BotDoing.Done($"{_taken} things and {_coins}gp back off its own corpse");
    }

    private void Empty(Mobile body)
    {
        Strip(body, _corpse, out var taken, out var coins);

        _taken += taken;
        _coins += coins;
    }

    public static void Strip(Mobile body, Corpse corpse, out int taken, out int coins)
    {
        taken = 0;
        coins = 0;

        var pack = body?.Backpack;

        if (pack == null || corpse is not { Deleted: false })
        {
            return;
        }

        var ceiling = BotLadder.Ceiling(body) * 0.8;

        List<Item> lying = [.. corpse.Items];

        for (var i = 0; i < lying.Count; i++)
        {
            var item = lying[i];

            if (item == null || item.Deleted || !item.Movable)
            {
                continue;
            }

            if (item is Gold coin)
            {
                coins += coin.Amount;

                pack.DropItem(coin);

                continue;
            }

            if (BotLadder.Load(body) >= ceiling)
            {
                break;
            }

            pack.DropItem(item);

            taken++;
        }
    }

    private void Forget(IBotWilful bot)
    {
        if (bot is BotMobile mobile && ReferenceEquals(mobile.Remains, _corpse))
        {
            mobile.Remains = null;
        }
    }
}

/// <summary>
/// Offers a bot the trip back to its own corpse, and only its own.
///
/// <para>
/// On the ordinary rung, competing on the same arithmetic as everything else, which is right: a pickaxe and
/// four hundred gold lying in a field is worth going for, and a corpse holding a handful of bandages is not
/// worth a walk across Britain. The ledger settles which is which from what actually comes back.
/// </para>
/// </summary>
public sealed class BotUndertaker : IBotProposer
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotUndertaker));

    public string Name => "Undertaker";

    public BotStanding Rung => BotStanding.Free;

    public BotDeed Propose(IBotWilful bot)
    {
        var body = bot?.Self;

        if (body is not { Deleted: false, Alive: true } || bot is not BotMobile mobile)
        {
            return null;
        }

        var corpse = mobile.Remains;

        if (corpse is not { Deleted: false } || corpse.Map != body.Map || corpse.Map == Map.Internal)
        {
            mobile.Remains = null;

            return null;
        }

        if (corpse.Items.Count == 0)
        {
            mobile.Remains = null;

            return null;
        }

        if (!BotPopulation.Within(body.Map, corpse.Location))
        {
            if (Deep && Below(corpse.Location))
            {
                DeepNoWay++;
                LetGo(mobile, corpse, Worth(corpse), "it lies past the island's roam");
            }

            return null;
        }

        if (Deep && Below(corpse.Location))
        {
            return Underground(mobile, body, corpse);
        }

        if (BotDungeon.Holding(corpse.Location) is { } deep && !BotDelveParty.Delving(mobile)
            && BotThreat.Power(body) < deep.Worst * BotVenturer.Odds)
        {
            Outmatched++;

            return null;
        }

        var worth = Worth(corpse);

        if (worth < Least)
        {
            Petty++;
            mobile.Remains = null;

            return null;
        }

        if (Again(mobile, corpse))
        {
            Refused(corpse, ref _islandAgain);
            mobile.Remains = null;

            return null;
        }

        var held = Held(mobile, corpse);

        if (IslandRecord && !held)
        {
            if (BotQuad.Muscle(body.Map, corpse.Location) > BotThreat.Power(body) * BotVenturer.Odds)
            {
                Refused(corpse, ref _islandGround);

                return null;
            }
        }
        else if (Island(mobile, body, corpse, held))
        {
            return null;
        }

        if (held)
        {
            HeldGone++;
        }

        Gone++;
        Sought += worth;
        WalkedBack(mobile, corpse);

        return new BotReclaim(corpse, worth);
    }

    public static bool IslandRecord { get; set; } = false;

    public static int HeldReach { get; set; } = 24;

    public static int HeldMs { get; set; } = 840000;

    private static readonly Dictionary<Serial, (BotSquad Squad, long Tick)> _fellWith = [];

    private static readonly HashSet<Serial> _judged = [];

    private static long _islandGround;

    private static long _islandRoad;

    private static long _islandDust;

    private static long _islandAgain;

    private static readonly Dictionary<Serial, (Map Map, Point3D At, Serial Corpse, long Tick)> _walkedBack = [];

    private static void WalkedBack(BotMobile mobile, Corpse corpse)
    {
        if (_walkedBack.Count > 2048)
        {
            _walkedBack.Clear();
        }

        _walkedBack[mobile.Serial] = (corpse.Map, corpse.Location, corpse.Serial, Core.TickCount);
    }

    private static bool Again(BotMobile mobile, Corpse corpse)
    {
        if (!_walkedBack.TryGetValue(mobile.Serial, out var last))
        {
            return false;
        }

        if (Core.TickCount - last.Tick > HeldMs)
        {
            _walkedBack.Remove(mobile.Serial);

            return false;
        }

        return last.Corpse != corpse.Serial && last.Map == corpse.Map
               && Utility.InRange(last.At, corpse.Location, Math.Max(1, HeldReach));
    }

    public static long HeldGone { get; private set; }

    public static long RisenPressed { get; private set; }

    public static void FellIn(BotMobile dead, BotSquad squad)
    {
        if (dead == null || squad == null)
        {
            return;
        }

        if (_fellWith.Count > 2048)
        {
            _fellWith.Clear();
        }

        _fellWith[dead.Serial] = (squad, Core.TickCount);
    }

    private static bool Held(BotMobile mobile, Corpse corpse)
    {
        if (!_fellWith.TryGetValue(mobile.Serial, out var fell))
        {
            return false;
        }

        if (Core.TickCount - fell.Tick > HeldMs || fell.Squad is not { Disbanded: false } squad)
        {
            _fellWith.Remove(mobile.Serial);

            return false;
        }

        if (squad.Leader?.Self is not { Deleted: false, Alive: true } leader || ReferenceEquals(leader, mobile) || leader.Map != corpse.Map
            || !leader.InRange(corpse.Location, Math.Max(1, HeldReach)))
        {
            return false;
        }

        var foe = squad.Focus;

        return foe is not { Deleted: false, Alive: true } || foe.Map != corpse.Map
               || !Utility.InRange(foe.Location, corpse.Location, Math.Max(1, BotRaise.ClearReach));
    }

    private static bool Island(BotMobile mobile, Mobile body, Corpse corpse, bool held)
    {
        var map = body.Map;
        var at = corpse.Location;
        var tiles = Tiles(body.Location, at);
        var left = DustMinutes - (Core.Now - corpse.TimeOfDeath).TotalMinutes;

        if (tiles / Math.Max(0.1, TilesPerMinute) > left)
        {
            Refused(corpse, ref _islandDust, false);
            mobile.Remains = null;

            return true;
        }

        var brought = BotQuad.Strength(body);
        var bar = brought * BotVenturer.Odds;

        BotZones.LevelAt(map, at.X, at.Y, out _, out var fight);

        if (!held && fight > bar)
        {
            Refused(corpse, ref _islandGround);

            return true;
        }

        if (BotDanger.Road(body, map, body.Location, at, brought, out _, out _, out _))
        {
            Refused(corpse, ref _islandRoad);

            return true;
        }

        return false;
    }

    private static void Refused(Corpse corpse, ref long bucket, bool ground = true)
    {
        if (corpse == null || _judged.Contains(corpse.Serial))
        {
            return;
        }

        if (_judged.Count > 4096)
        {
            _judged.Clear();
        }

        _judged.Add(corpse.Serial);
        bucket++;

        if (ground)
        {
            Outmatched++;
        }
    }

    public static void Risen(BotMobile bot)
    {
        if (bot is not { Deleted: false, Alive: true } || bot.Remains is not { Deleted: false } corpse || corpse.Map != bot.Map
            || corpse.Items.Count == 0 || !Held(bot, corpse))
        {
            return;
        }

        var worth = Worth(corpse);

        if (worth < Least || Again(bot, corpse) || Island(bot, bot, corpse, true))
        {
            return;
        }

        if (!BotWill.Press(bot, new BotReclaim(corpse, worth), $"its company held the ground where it fell, {worth}gp in its corpse"))
        {
            return;
        }

        WalkedBack(bot, corpse);
        RisenPressed++;
        HeldGone++;
        Gone++;
        Sought += worth;
    }

    private static BotDeed Underground(BotMobile mobile, Mobile body, Corpse corpse)
    {
        var worth = Worth(corpse);

        if (worth < Least)
        {
            DeepPetty++;
            LetGo(mobile, corpse, worth, $"it holds {worth}gp, under the {Least}gp worth the walk");

            return null;
        }

        if (!BotDelveParty.Below(mobile) && Barred(body, corpse) is { } why)
        {
            LetGo(mobile, corpse, worth, why);

            return null;
        }

        Gone++;
        Sought += worth;
        DeepGone++;
        DeepSought += worth;

        return new BotReclaim(corpse, worth);
    }

    private static string Barred(Mobile body, Corpse corpse)
    {
        var map = body.Map;
        var at = corpse.Location;

        if (!BotGates.Joined(map, body.Location, at))
        {
            DeepNoWay++;

            return "there is no way down to it over the gates";
        }

        var mouth = BotGates.Mouth(map, body.Location, at);
        var tiles = mouth == null ? Tiles(body.Location, at) : Tiles(body.Location, mouth.From) + Tiles(mouth.To, at);
        var left = DustMinutes - (Core.Now - corpse.TimeOfDeath).TotalMinutes;

        if (tiles / Math.Max(0.1, TilesPerMinute) > left)
        {
            DeepDust++;

            return $"it lies {tiles} tiles off by the gates and will be gone in {Math.Max(0.0, left):F0} minutes";
        }

        var brought = BotQuad.Strength(body);
        var bar = brought * BotVenturer.Odds;
        var record = BotQuad.Muscle(map, at);

        if (record > bar)
        {
            DeepGround++;

            return $"the ground where it lies asks {record:F0} by the record of what has killed there, against the {brought:F0} it brings";
        }

        BotZones.LevelAt(map, at.X, at.Y, out _, out var fight);

        if (fight > bar)
        {
            DeepGround++;

            return $"the fight where it lies is {fight:F0} against the {brought:F0} it brings";
        }

        var road = mouth == null
            ? BotDanger.Road(body, map, body.Location, at, brought, out var asked, out var where, out var what)
            : BotDanger.Road(body, map, body.Location, mouth.From, brought, out asked, out where, out what)
              || BotDanger.Road(body, map, mouth.To, at, brought, out asked, out where, out what);

        if (road)
        {
            DeepRoad++;

            return $"the road down asks {asked:F0} at ({where.X}, {where.Y}) by {what}, against the {brought:F0} it brings";
        }

        return null;
    }

    private static int Tiles(Point3D a, Point3D b) => Math.Max(Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));

    private static void LetGo(BotMobile mobile, Corpse corpse, int worth, string why)
    {
        mobile.Remains = null;
        DeepLeft++;
        DeepLeftGold += worth;

        var where = corpse.Location;
        var deep = BotDungeon.Holding(where)?.Name ?? "the deep";

        logger.Information(
            "{Name} lets its corpse in {Deep} at ({X}, {Y}) go, {Worth}gp in it: {Why}",
            mobile.Name,
            deep,
            where.X,
            where.Y,
            worth,
            why
        );

        BotEvents.Post(
            "corpse",
            mobile,
            $"let its corpse in {deep} at ({where.X}, {where.Y}) go, {worth}gp in it: {why}",
            null,
            BotJson.Object(
                w =>
                {
                    w.WriteString("deep", deep);
                    w.WriteNumber("cx", where.X);
                    w.WriteNumber("cy", where.Y);
                    w.WriteNumber("worth", worth);
                    w.WriteString("why", why);
                }
            )
        );
    }

    public static bool Below(Point3D where) => BotDungeon.Under(where) || where.X >= BotGates.Edge;

    internal static void Reached(int taken, int coins)
    {
        DeepReached++;
        DeepThings += taken;
        DeepCoin += coins;
    }

    public static bool Deep { get; set; } = true;

    public static double DustMinutes { get; set; } = 14.0;

    public static double TilesPerMinute { get; set; } = 120.0;

    public static long DeepGone { get; private set; }

    public static long DeepSought { get; private set; }

    public static long DeepReached { get; private set; }

    public static long DeepThings { get; private set; }

    public static long DeepCoin { get; private set; }

    public static long DeepLeft { get; private set; }

    public static long DeepLeftGold { get; private set; }

    public static long DeepPetty { get; private set; }

    public static long DeepNoWay { get; private set; }

    public static long DeepDust { get; private set; }

    public static long DeepGround { get; private set; }

    public static long DeepRoad { get; private set; }

    private static int Worth(Corpse corpse)
    {
        var worth = 0;
        var lying = corpse.Items;

        for (var i = 0; i < lying.Count; i++)
        {
            var item = lying[i];

            if (item == null || item.Deleted || !item.Movable)
            {
                continue;
            }

            if (item is Gold coin)
            {
                worth += coin.Amount;

                continue;
            }

            worth += BotMarket.Worth(item.GetType(), 1) * Math.Max(1, item.Amount);
        }

        return worth;
    }

    public static int Least { get; set; } = 20;

    public static long Gone { get; private set; }

    public static long Sought { get; private set; }

    public static long Petty { get; private set; }

    public static long Outmatched { get; private set; }

    public static string Describe() =>
        $"reclaims: {Gone} corpses worth {Sought}gp gone back for, {Petty} left for holding under {Least}gp, {Outmatched} left for the ground round them; "
        + $"underground: {DeepGone} worth {DeepSought}gp gone back for through the gates and {DeepReached} reached ({DeepThings} things, {DeepCoin}gp back), "
        + $"{DeepLeft} let go and said so with {DeepLeftGold}gp in them ({DeepPetty} under {Least}gp, {DeepNoWay} with no way down, {DeepDust} to be gone before it got there, {DeepGround} for the ground where they lay, {DeepRoad} for the road down); "
        + $"on the island ({(IslandRecord ? "by the square's record" : "by the walker against the fight there now")}, corpses counted once): {_islandGround} too strong where they lie, {_islandRoad} for the road there, {_islandDust} let go to be dust before the walk arrived, {_islandAgain} let go for lying where it had died again within {HeldMs / 60000} minutes of being sent back; "
        + $"{HeldGone} walks back offered on ground the fallen's company held, as offers are counted above ({RisenPressed} pressed as the first thing it did on rising)";

    public static void Forget()
    {
        Gone = 0;
        Sought = 0;
        Petty = 0;
        Outmatched = 0;
        DeepGone = 0;
        DeepSought = 0;
        DeepReached = 0;
        DeepThings = 0;
        DeepCoin = 0;
        DeepLeft = 0;
        DeepLeftGold = 0;
        DeepPetty = 0;
        DeepNoWay = 0;
        DeepDust = 0;
        DeepGround = 0;
        DeepRoad = 0;
        HeldGone = 0;
        RisenPressed = 0;
        _islandGround = 0;
        _islandRoad = 0;
        _islandDust = 0;
        _islandAgain = 0;
        _walkedBack.Clear();
        _judged.Clear();
        _fellWith.Clear();
    }
}
