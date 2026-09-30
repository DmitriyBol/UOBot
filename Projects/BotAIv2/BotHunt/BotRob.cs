using System;
using System.Collections.Generic;
using Server.Items;
using Server.Logging;
using Server.Mobiles;
using Server.Regions;

namespace Server.BotAI.V2;

/// <summary>
/// Setting on one of our own for what is in its pack: kill it, go through the corpse, and go red for it.
///
/// <para>
/// <b>Patrick's order of 15.09.2026, and the rarest thought on the shard.</b> The fight is <see cref="BotBrawl"/>'s;
/// what is here is the two ends of it — the victim chosen for its purse and its solitude, and the corpse gone through
/// for coin — and the one consequence that makes it a crime rather than a hunt: the robber is red for an hour, by
/// <see cref="BotOutlaw"/>, from the moment the victim falls, whether or not it ever reaches the corpse.
/// </para>
///
/// <para>
/// A robber that finds itself losing gives up rather than dies for a purse; a victim that runs is let go. The trade
/// pays what it took, so a ledger will learn what robbery is worth — and a population in which that number is high
/// is a population carrying too much coin about, which is a fact worth having.
/// </para>
/// </summary>
public sealed class BotRob : BotDeed
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotRob));

    public const string Trade = "rob";

    public const string ExtortTrade = "extort";

    public static double DemandShare { get; set; } = 0.3;

    public static int LeastDemand { get; set; } = 20;

    public static int DemandWithin { get; set; } = 2;

    public static long Demands { get; private set; }

    public static long Paid { get; private set; }

    public static long Refused { get; private set; }

    public static long Punished { get; private set; }

    public static long Extorted { get; private set; }

    public static double Prior { get; set; } = 400.0;

    public static double WorkMinutes { get; set; } = 3.0;

    public static int LootReach { get; set; } = 2;

    public static long Done { get; private set; }

    public static long GivenUp { get; private set; }

    public static long Bought { get; private set; }

    public static long Slain { get; private set; }

    public static int HideAt { get; set; } = 14;

    public static int AmbushWithin { get; set; } = 2;

    public static int StrikeWithin { get; set; } = 2;

    public static int AmbushMs { get; set; } = 12000;

    public static int HideTries { get; set; } = 6;

    public static long Stalked { get; private set; }

    public static long Ambushed { get; private set; }

    public static long Unhidden { get; private set; }

    public static long Spotted { get; private set; }

    public static int PatienceMs { get; set; } = 45000;

    public static int OpenWithin { get; set; } = 16;

    public static int Gaining { get; set; } = 2;

    public static long Impatient { get; private set; }

    public static long Outwalked { get; private set; }

    public static long Watched { get; private set; }

    public static long Pounced { get; private set; }

    public static long Bared { get; private set; }

    public static long Outpaced { get; private set; }

    private enum Leg
    {
        Stalk,
        Demand,
        Fight,
        Loot
    }

    private readonly bool _demand;

    private readonly BotMobile _victim;

    private readonly SkillName _trains;

    private BotBrawl _fight;

    private Leg _leg = Leg.Stalk;

    private int _hideFailures;

    private bool _hidOnce;

    private long _hidTick;

    private bool _stalking;

    private bool _unseen;

    private bool _backstab;

    private Point3D _creep = Point3D.Zero;

    private Point3D _fell;

    private int _coins;

    private bool _murdered;

    private bool _quartered;

    private readonly bool _sprang;

    private long _held;

    private long _gained;

    private double _closest = double.MaxValue;

    private int _opened = -1;

    public BotRob(BotMobile victim, SkillName trains, bool demand) : this(victim, trains)
    {
        _demand = demand;
    }

    public BotRob(BotMobile victim, SkillName trains, bool demand, bool sprang) : this(victim, trains, demand)
    {
        _sprang = sprang;
    }

    public BotRob(BotMobile victim, SkillName trains)
    {
        _victim = victim;
        _trains = trains;
        _fight = new BotBrawl(victim, BotBrawl.Robbery, trains, Over);
        _fell = victim?.Location ?? Point3D.Zero;
        _gained = Core.TickCount;
    }

    private Point3D Creep()
    {
        if (_victim == null)
        {
            return _creep;
        }

        if (_creep == Point3D.Zero || !Utility.InRange(_victim.Location, _creep, BotBrawl.Restride))
        {
            _creep = _victim.Location;
        }

        return _creep;
    }

    private bool Lonely(Map map, Mobile robber)
    {
        if (_victim == null || map == null)
        {
            return false;
        }

        foreach (var third in map.GetMobilesInRange<BotMobile>(_victim.Location, Math.Max(1, BotRobber.Alone)))
        {
            if (third != _victim && third != robber && third.Alive)
            {
                return false;
            }
        }

        return true;
    }

    private static string Over(IBotWilful bot, BotBrawl brawl)
    {
        var body = bot?.Self;
        var victim = brawl?.Foe;

        if (victim is not { Deleted: false, Alive: true })
        {
            return "the victim is down";
        }

        if (body != null && body.HitsMax > 0 && body.Hits < body.HitsMax * BotSlay.FleeAt)
        {
            return "the robber lost its nerve";
        }

        if (BotDuel.Duelling(victim) || BotOutlaw.Jailed(victim))
        {
            return "the victim is spoken for";
        }

        if (victim.Region?.GetRegion<GuardedRegion>() is { } guarded && !guarded.IsDisabled())
        {
            return "the victim reached the town";
        }

        return null;
    }

    public override string Kind => _demand ? ExtortTrade : Trade;

    public override bool Committed => true;

    public override bool Braves => true;

    public override bool Repeats(BotDeed other) => other is BotRob;

    public override Mobile Foe => _victim;

    public override Map Map => _fight.Map;

    public override Point3D Where => _leg switch
    {
        Leg.Loot  => _fell,
        Leg.Stalk or Leg.Demand => _victim?.Location ?? _fell,
        _         => _fight.Where
    };

    public override bool Hurries => _leg != Leg.Stalk;

    public override bool Afoot => _leg == Leg.Stalk && _stalking;

    public override double Expects => Prior;

    public override double Minutes => WorkMinutes;

    public override SkillName? Trains => _fight.Trains;

    public override int Outlay => 0;

    public override double Coin => 1.0;

    public override int Made => 0;

    public override string Stage => _leg switch
    {
        Leg.Stalk  => _stalking ? $"stalking {_victim?.Name}" : $"lying in wait for {_victim?.Name}",
        Leg.Demand => $"demanding {_victim?.Name}'s purse",
        Leg.Fight  => $"setting on {_victim?.Name}",
        _         => $"going through {_victim?.Name}'s corpse"
    };

    public override BotDoing Advance(IBotWilful bot)
    {
        var body = bot?.Self;
        var map = _fight.Map;

        if (body == null || map == null || map == Map.Internal)
        {
            return BotDoing.Failed("no body");
        }

        if (_leg == Leg.Stalk)
        {
            if (Stalk(bot, body, map, out var stalking))
            {
                return stalking;
            }

            if (_demand)
            {
                _leg = Leg.Demand;
            }
            else
            {
                if (_unseen)
                {
                    var stun = BotAmbush.Strike(body, _victim, _backstab);

                    _held = stun > 0 ? Core.TickCount + stun : 0;
                }

                _fight = new BotBrawl(_victim, BotBrawl.Robbery, _trains, Over);
                _leg = Leg.Fight;
            }
        }

        if (_leg == Leg.Demand && Demand(bot, body, map, out var demanding))
        {
            return demanding;
        }

        if (_leg == Leg.Fight)
        {
            if (!_quartered && (BotQuarter.Beaten(_victim) || _held > 0 && Core.TickCount >= _held))
            {
                _quartered = true;

                if (BotQuarter.Choose(body as BotMobile, _victim) == BotQuarter.Answer.Pay)
                {
                    Bought++;

                    return BotDoing.Done($"{_victim.Name} bought its life");
                }
            }

            var doing = _fight.Advance(bot);

            if (doing.Kind != BotDoingKind.Done)
            {
                return doing;
            }

            if (_victim is { Deleted: false, Alive: true } || _victim == null)
            {
                GivenUp++;

                return BotDoing.Failed(doing.Note ?? "the victim got away");
            }

            if (!_murdered && ReferenceEquals(_victim.LastKiller, body))
            {
                _murdered = true;
                BotOutlaw.Murdered(body as BotMobile, _victim);
                BotUnderworld.Murder(body, _unseen);
            }

            _fell = _victim.Corpse?.Location ?? _victim.Location;
            _leg = Leg.Loot;
        }

        if (!body.InRange(_fell, LootReach))
        {
            return BotDoing.Walk(map, _fell, BotArrival.Within(LootReach), "to the corpse");
        }

        var corpse = _victim?.Corpse as Corpse ?? BotQuarry.Remains(map, _fell, _victim);
        var pack = body.Backpack;

        if (corpse == null || corpse.Deleted || pack == null)
        {
            Done++;
            BotUnderworld.Robbed(body);

            return BotDoing.Done($"{_victim?.Name} is dead and there was nothing left to take");
        }

        List<Item> lying = [.. corpse.Items];

        for (var i = 0; i < lying.Count; i++)
        {
            if (lying[i] is Gold coin && !coin.Deleted && coin.Movable)
            {
                _coins += coin.Amount;
                pack.DropItem(coin);
            }
        }

        BotOutlaw.Took(_coins);
        Done++;
        BotUnderworld.Robbed(body);

        logger.Information("{Name} went through {Victim}'s corpse and took {Coins}gp", body.Name, _victim?.Name, _coins);

        if (_murdered && BotShadow.Running && BotShadow.CanHide(body))
        {
            BotInquest.LieLow(body as BotMobile, map, _fell);
        }

        return BotDoing.Done($"killed {_victim?.Name} and took {_coins}gp");
    }

    private bool Demand(IBotWilful bot, Mobile body, Map map, out BotDoing doing)
    {
        doing = default;

        if (Over(bot, _fight) is { } over)
        {
            GivenUp++;
            doing = BotDoing.Failed(over);

            return true;
        }

        if (!body.InRange(_victim, DemandWithin))
        {
            doing = BotDoing.Walk(map, Creep(), BotArrival.Within(DemandWithin), $"up to {_victim.Name}");

            return true;
        }

        if (body.Hidden)
        {
            body.RevealingAction();
        }

        Demands++;

        var coins = _victim.Backpack?.GetAmount(typeof(Gold)) ?? 0;
        var ours = BotThreat.Power(body) + Brothers(body);
        var theirs = Math.Max(1.0, BotThreat.Power(_victim));
        var ratio = ours / theirs;

        BotVoice.Aloud(body, $"Your purse, {_victim.Name}, or your life.");

        if (coins > 0 && Utility.RandomDouble() < Math.Clamp(ratio - 0.5, 0.0, 1.0))
        {
            var amount = Math.Min(coins, Math.Max(LeastDemand, (int)(coins * DemandShare)));

            _victim.Backpack.ConsumeTotal(typeof(Gold), amount);
            body.AddToBackpack(new Gold(amount));
            BotVoice.Aloud(_victim, "Take it, and go.");

            Paid++;
            Extorted += amount;
            BotUnderworld.Extorted(body);
            BotOutlaw.Want(body as BotMobile, _victim.Name, $"taking {amount}gp from {_victim.Name} under threat");

            if (BotShadow.Running && BotShadow.CanHide(body))
            {
                BotInquest.LieLow(body as BotMobile, map, _victim.Location);
            }

            logger.Information(
                "{Name} made {Victim} pay {Amount}gp of {Coins} under threat, {Ours:F0} of strength against {Theirs:F0}",
                body.Name,
                _victim.Name,
                amount,
                coins,
                ours,
                theirs
            );

            doing = BotDoing.Done($"{_victim.Name} paid {amount}gp");

            return true;
        }

        BotVoice.Aloud(_victim, "Not one coin.");
        Refused++;
        BotOutlaw.Want(body as BotMobile, _victim.Name, $"threatening {_victim.Name}");

        if (ratio >= 1.0)
        {
            Punished++;

            logger.Information(
                "{Victim} would not pay {Name}, who sets on it, {Ours:F0} of strength against {Theirs:F0}",
                _victim.Name,
                body.Name,
                ours,
                theirs
            );

            _fight = new BotBrawl(_victim, BotBrawl.Robbery, _trains, Over);
            _leg = Leg.Fight;

            return false;
        }

        GivenUp++;

        logger.Information(
            "{Victim} would not pay {Name}, who goes away, {Ours:F0} of strength against {Theirs:F0}",
            _victim.Name,
            body.Name,
            ours,
            theirs
        );

        doing = BotDoing.Failed($"{_victim.Name} would not pay");

        return true;
    }

    private static double Brothers(Mobile body)
    {
        if (!BotUnderworld.Member(body) || body.Map is not { } map)
        {
            return 0.0;
        }

        var sum = 0.0;

        foreach (var m in map.GetMobilesInRange<BotMobile>(body.Location, 12))
        {
            if (m != body && m.Alive && BotUnderworld.Member(m))
            {
                sum += BotThreat.Power(m);
            }
        }

        return sum;
    }

    private bool Stalk(IBotWilful bot, Mobile body, Map map, out BotDoing doing)
    {
        doing = default;

        if (!BotShadow.Running || !BotShadow.CanHide(body))
        {
            return false;
        }

        if (Over(bot, _fight) is { } over)
        {
            GivenUp++;

            if (body.Hidden)
            {
                body.RevealingAction();
            }

            doing = BotDoing.Failed(over);

            return true;
        }

        var now = Core.TickCount;
        var away = body.GetDistanceToSqrt(_victim);

        if (_opened < 0)
        {
            _opened = (int)away;
        }

        if (away < _closest - Gaining)
        {
            _closest = away;
            _gained = now;
        }

        if (PatienceMs > 0 && now - _gained > PatienceMs)
        {
            if (away > OpenWithin)
            {
                Outwalked++;
                GivenUp++;
                Outpaced += _victim.Mounted && !body.Mounted ? 1 : 0;

                if (body.Hidden)
                {
                    body.RevealingAction();
                }

                doing = BotDoing.Failed(
                    $"could not get near {_victim.Name}, {(int)away} tiles off and gaining nothing, set on from {_opened}"
                    + (_victim.Mounted ? body.Mounted ? ", both mounted" : ", and it is mounted" : "")
                );

                return true;
            }

            Impatient++;

            if (body.Hidden)
            {
                body.RevealingAction();
            }

            logger.Information(
                "{Name} runs out of patience {Away} tiles from {Victim} and sets on it in the open",
                body.Name,
                (int)away,
                _victim.Name
            );

            return false;
        }

        _stalking = BotShadow.CanStalk(body) || _stalking && body.Hidden;

        if (_sprang && _leg == Leg.Stalk && away <= BotRobber.Pounce)
        {
            if (!Lonely(map, body))
            {
                Watched++;
                GivenUp++;

                doing = BotDoing.Failed($"somebody else stood by {_victim.Name}");

                return true;
            }

            Pounced++;

            if (body.Hidden)
            {
                Ambushed++;
                _unseen = true;
                _backstab = _stalking;
            }
            else
            {
                Bared++;
            }

            return false;
        }

        if (_stalking && body.Mounted)
        {
            BotStable.Alight(body);
        }

        if (!body.Hidden)
        {
            if (_hidOnce && (away <= StrikeWithin || _hideFailures >= HideTries))
            {
                Spotted++;

                return false;
            }

            var hideAt = _stalking ? HideAt : AmbushWithin;

            if (away > hideAt)
            {
                doing = BotDoing.Walk(map, Creep(), BotArrival.Within(hideAt), $"creeping up on {_victim.Name}");

                return true;
            }

            if (!BotShadow.Ready(body))
            {
                doing = BotDoing.Work($"waiting to hide near {_victim.Name}");

                return true;
            }

            if (!BotShadow.Hide(body))
            {
                if (++_hideFailures >= HideTries)
                {
                    Unhidden++;
                    GivenUp++;

                    doing = BotDoing.Failed($"could not hide near {_victim.Name}");

                    return true;
                }

                doing = BotDoing.Work($"trying to hide near {_victim.Name}");

                return true;
            }

            _hidOnce = true;
            _hidTick = now;
            doing = BotDoing.Work(_stalking ? $"hidden, {(int)away} tiles from {_victim.Name}" : $"lying in wait for {_victim.Name}");

            return true;
        }

        if (!_stalking)
        {
            if (now - _hidTick < AmbushMs)
            {
                doing = BotDoing.Work($"lying in wait for {_victim.Name}");

                return true;
            }

            Ambushed++;
            _unseen = true;

            return false;
        }

        if (away <= StrikeWithin)
        {
            Stalked++;
            _unseen = true;

            return false;
        }

        if (body.AllowedStealthSteps <= 0)
        {
            BotShadow.Quiet(body);
            doing = BotDoing.Work($"moving quietly towards {_victim.Name}");

            return true;
        }

        doing = BotDoing.Walk(map, Creep(), BotArrival.Within(StrikeWithin), $"stalking {_victim.Name}");

        return true;
    }

    public override bool Bend(IBotWilful bot) => _leg == Leg.Fight && _fight.Bend(bot);

    public override void Drop(IBotWilful bot)
    {
        _fight.Drop(bot);

        if (_leg == Leg.Stalk && bot?.Self is { Hidden: true } hidden)
        {
            hidden.RevealingAction();
        }

        if (_leg == Leg.Fight && bot?.Self is { Alive: false } body)
        {
            Slain++;

            logger.Information("{Name}'s robbery of {Victim} ended with its own death at ({X}, {Y})", body.Name, _victim?.Name, body.X, body.Y);
        }
    }

    public static string Describe() =>
        $"{Done} robberies carried through, {GivenUp} given up and {Slain} ended with the robber's death; {Demands} demands made ({Paid} paid, {Extorted}gp in all; {Refused} refused, {Punished} of those answered with a blow), {Bought} marks bought their lives, {Pounced} sprung on a mark that walked into the wait ({Bared} of them already in plain sight) and {Watched} let pass for somebody standing by, {Impatient} set on the mark openly when patience ran out and {Outwalked} were outwalked ({Outpaced} of them by a mounted mark), {Stalked} struck after stalking hidden, {Ambushed} from lying in wait beside the victim, {Spotted} in the open after being brought out of hiding, {Unhidden} given up for want of hiding; {BotAmbush.Describe()}; {BotQuarter.Describe()}; {BotShadow.Describe()}; {BotSkulk.Describe()}";

    public static void Forget()
    {
        Done = 0;
        GivenUp = 0;
        Slain = 0;
        Stalked = 0;
        Ambushed = 0;
        Demands = 0;
        Paid = 0;
        Refused = 0;
        Punished = 0;
        Extorted = 0;
        Unhidden = 0;
        Spotted = 0;
        Impatient = 0;
        Outwalked = 0;
        Watched = 0;
        Pounced = 0;
        Bared = 0;
        Outpaced = 0;
        Bought = 0;
        BotAmbush.Forget();
        BotQuarter.Forget();
        BotFence.Forget();
        BotSilence.Forget();
        BotLair.Forget();
        BotStash.Forget();
        BotFetch.Forget();
        BotShadow.Forget();
        BotSkulk.Forget();
    }
}

/// <summary>
/// Puts, very rarely, the thought of robbing somebody into a fighter's head: once every <see cref="EveryMs"/> a fighter
/// rolls, and with <see cref="Chance"/> it looks for a victim — alone, outside any town, with coin in its pack and
/// weaker than itself — and is pressed into the robbery on the spot. Nearly every answer is nothing, and every gate is
/// counted.
///
/// <para>
/// <b>Pressed, not offered, and the first hour of build 70 is why.</b> As a proposer this marked two victims and won
/// nothing: the thought came up once, went into the auction at 400 over three minutes against a hunt at 459, lost,
/// and was gone — a mark is not kept, and the next roll was ten minutes off. A temptation is not an opportunity to be
/// weighed against a seam; it is what the bot does next, which is what <see cref="BotWill.Press"/> is for. The rarity
/// stays in <see cref="Chance"/>, where Patrick put it.
/// </para>
/// </summary>
public static class BotRobber
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotRobber));

    public static bool Running { get; set; } = true;

    public static int EveryMs { get; set; } = 600000;

    public static double Chance { get; set; } = 0.02;

    public static int Reach { get; set; } = 60;

    public static int Alone { get; set; } = 4;

    public static int Purse { get; set; } = 50;

    public static int FromTown { get; set; } = 24;

    public static bool Heading { get; set; } = true;

    public static int HomeWithin { get; set; } = 80;

    public static long Townbound { get; private set; }

    public static long Homing { get; private set; }

    public static long Roofed { get; private set; }

    public static double Edge { get; set; } = 1.0;

    public static long Rolled { get; private set; }

    public static long Burgling { get; private set; }

    public static long Tempted { get; private set; }

    public static long NoVictim { get; private set; }

    public static long Pressed { get; private set; }

    public static long Interrupted { get; private set; }

    public static long Practising { get; private set; }

    public static long NotFree { get; private set; }

    public static long Holding { get; private set; }

    public static int SweepMs { get; set; } = 10000;

    public static int SettleMs { get; set; } = 120000;

    public static long Settling { get; private set; }

    private static readonly Dictionary<Serial, long> _rolledTick = [];

    private static long _sweptTick;

    private static long? _woke;

    public static void Beat(long now)
    {
        if (!Running || now - _sweptTick < SweepMs)
        {
            return;
        }

        _sweptTick = now;

        _woke ??= now;

        if (now - _woke.Value < SettleMs)
        {
            Settling++;

            return;
        }

        var bots = BotPopulation.Bots;

        BotSkulk.Rank(bots);

        for (var i = 0; i < bots.Count; i++)
        {
            Roll(bots[i], now);
            BotSkulk.Urge(bots[i], now);
        }
    }

    private static void Roll(BotMobile body, long now)
    {
        if (body is not { Deleted: false, Alive: true, Fallen: false } || body.Class is not { } klass)
        {
            return;
        }

        var map = body.Map;

        if (map == null || map == Map.Internal || body.Squad != null || BotDuel.Duelling(body) || BotOutlaw.Jailed(body))
        {
            return;
        }

        if (klass.Role is BotRole.Producer or BotRole.Medic || klass.Unpaid || klass.Leads)
        {
            return;
        }

        if (BotFence.Is(body))
        {
            return;
        }

        var sprung = _sprung.Remove(body.Serial, out var seen);

        var bandit = BotUnderworld.Member(body) || BotUnderworld.Outlawed(body);

        if (!sprung && _rolledTick.TryGetValue(body.Serial, out var last) && now - last < (bandit ? BotUnderworld.BanditEveryMs : EveryMs))
        {
            return;
        }

        _rolledTick[body.Serial] = now;
        Rolled++;

        if (!sprung && Utility.RandomDouble() >= (bandit ? BotUnderworld.MemberRobChance : Chance))
        {
            return;
        }

        Tempted++;

        if (body is not IBotWilful bot || BotLadder.Standing(bot) is not (BotStanding.Free or BotStanding.Busy))
        {
            NotFree++;

            return;
        }

        if (!sprung && bot.Resolve?.Deed is { Steadfast: true })
        {
            Holding++;

            return;
        }

        if (BotShadow.Running && !BotShadow.CanHide(body))
        {
            if (!BotWill.Press(bot, new BotSkulk(map, body.Location), $"tempted, but it hides at {body.Skills.Hiding.Base:F1} of the {BotShadow.RobHiding:F0} a robbery wants"))
            {
                NotFree++;

                return;
            }

            Practising++;

            logger.Information(
                "{Name} the {Class} is tempted to rob, but hides at only {Hiding:F1} of the {Wanted:F0} it would need, and goes off to practise hiding instead",
                body.Name,
                klass.Name,
                body.Skills.Hiding.Base,
                BotShadow.RobHiding
            );

            return;
        }

        if (bandit && !sprung && Utility.RandomDouble() < BotBurgle.Share && BotBurgle.Target(body, map, out var whose) is { } hall)
        {
            if (BotWill.Press(bot, new BotBurgle(hall, whose), $"tempted by what {whose} has put by"))
            {
                Burgling++;

                logger.Information(
                    "{Name} the {Class} is tempted by the chest of {Guild} ({Gold}gp put by) and creeps off to break into its hall at {X},{Y}",
                    body.Name,
                    klass.Name,
                    whose,
                    BotChest.Holds(whose),
                    hall.X,
                    hall.Y
                );

                return;
            }
        }

        if (body.Region?.GetRegion<GuardedRegion>() is { } guarded && !guarded.IsDisabled())
        {
            NoVictim++;
            Wait(thief: body, klass, map, bot);

            return;
        }

        var demand = !sprung && BotUnderworld.Member(body) && Utility.RandomDouble() < BotUnderworld.ExtortShare;

        var victim = sprung && seen is { Deleted: false, Alive: true } && !BotOutlaw.Jailed(seen)
            ? seen
            : Prey(body, map, demand ? BotUnderworld.ExtortEdge : Edge);

        if (victim == null)
        {
            NoVictim++;
            Wait(thief: body, klass, map, bot);

            return;
        }

        var coins = victim.Backpack?.GetAmount(typeof(Gold)) ?? 0;
        var held = bot.Resolve?.Deed?.Kind;
        var deed = new BotRob(victim, bot.Bond?.Weapon?.Skill ?? SkillName.Wrestling, demand, sprung);

        if (!BotWill.Press(bot, deed, $"tempted by {victim.Name}'s {coins}gp"))
        {
            NotFree++;

            return;
        }

        Pressed++;

        if (held != null)
        {
            Interrupted++;
        }

        logger.Information(
            "{Name} the {Class} is tempted and sets on {Victim} the {VictimClass}, alone with {Coins}gp in its pack, for {Crime}{Dropping}",
            body.Name,
            klass.Name,
            victim.Name,
            victim.Class?.Name,
            coins,
            demand ? "blackmail" : "robbery",
            held == null ? "" : $", dropping {held}"
        );
    }

    private static bool Nearing(Map map, Point3D at) => Nearing(map, at, FromTown);

    private static bool Nearing(Map map, Point3D at, int span)
    {
        for (var i = 0; i < 8; i++)
        {
            var x = at.X + (i is 0 or 1 or 7 ? span : i is 3 or 4 or 5 ? -span : 0);
            var y = at.Y + (i is 1 or 2 or 3 ? span : i is 5 or 6 or 7 ? -span : 0);

            if (BotOutlaw.Guarded(map, x, y, at.Z))
            {
                return true;
            }
        }

        return false;
    }

    public static int WaitRoom { get; set; } = 1;

    public static long Waiting { get; private set; }

    public static long Nowhere { get; private set; }

    private static readonly Dictionary<Serial, BotMobile> _sprung = [];

    public static int Pounce { get; set; } = 15;

    public static BotMobile Mark(BotMobile robber, Map map) =>
        Running && robber is { Deleted: false, Alive: true } && map != null && map != Map.Internal
            ? Prey(robber, map, Edge, Math.Max(1, Pounce))
            : null;

    public static void Spring(BotMobile robber, BotMobile mark)
    {
        if (robber is { Deleted: false })
        {
            _sprung[robber.Serial] = mark;
        }
    }

    private static void Wait(BotMobile thief, BotClass klass, Map map, IBotWilful bot)
    {
        if (!BotWaylay.Running || bot.Resolve?.Deed is BotWaylay or BotRob)
        {
            return;
        }

        var at = Ambush(thief, map);

        if (at == Point3D.Zero)
        {
            Nowhere++;

            return;
        }

        if (!BotWill.Press(bot, new BotWaylay(map, at), "tempted, with nobody about to rob"))
        {
            NotFree++;

            return;
        }

        Waiting++;

        logger.Information(
            "{Name} the {Class} is tempted with nobody about, and goes to lie in wait at ({X}, {Y})",
            thief.Name,
            klass?.Name,
            at.X,
            at.Y
        );
    }

    private static Point3D Ambush(BotMobile thief, Map map)
    {
        var places = BotCommons.Best(32);

        return Ambush(thief, map, places, Math.Max(1, WaitRoom)) is { } room && room != Point3D.Zero
            ? room
            : Ambush(thief, map, places, 1);
    }

    public static int Tried { get; set; } = 4;

    public static long Sealed { get; private set; }

    private static readonly List<(Point3D At, double Span)> _fit = [];

    private static Point3D Ambush(
        BotMobile thief,
        Map map,
        List<(string Kind, Map Map, Point3D Where, double PerMinute, int Settled, int Minded)> places,
        int room
    )
    {
        _fit.Clear();

        for (var i = 0; i < places.Count; i++)
        {
            var (_, where, at, _, _, _) = places[i];

            if (where != map || at == Point3D.Zero || Nearing(map, at, FromTown * room))
            {
                continue;
            }

            if (Region.Find(at, map)?.GetRegion<GuardedRegion>() is { } guarded && !guarded.IsDisabled())
            {
                continue;
            }

            _fit.Add((at, thief.GetDistanceToSqrt(at)));
        }

        _fit.Sort((x, y) => x.Span.CompareTo(y.Span));

        for (var i = 0; i < _fit.Count && i < Math.Max(1, Tried); i++)
        {
            if (BotReach.Ask(map, thief.Location, _fit[i].At, BotArrival.Within(3)) == BotReachVerdict.Sealed)
            {
                Sealed++;

                continue;
            }

            return _fit[i].At;
        }

        return Point3D.Zero;
    }

    private static BotMobile Prey(BotMobile robber, Map map, double edge) => Prey(robber, map, edge, Reach);

    private static BotMobile Prey(BotMobile robber, Map map, double edge, int reach)
    {
        BotMobile best = null;
        var richest = 0;
        var mine = BotThreat.Power(robber);

        foreach (var other in map.GetMobilesInRange<BotMobile>(robber.Location, reach))
        {
            if (other == robber || !other.Alive || other.Squad != null || BotOutlaw.IsRed(other) || BotDuel.Duelling(other)
                || BotOutlaw.Jailed(other))
            {
                continue;
            }

            if (robber.Guild != null && ReferenceEquals(other.Guild, robber.Guild))
            {
                continue;
            }

            var coins = other.Backpack?.GetAmount(typeof(Gold)) ?? 0;

            if (coins < Purse || coins <= richest)
            {
                continue;
            }

            if (other.Region?.GetRegion<GuardedRegion>() is { } guarded && !guarded.IsDisabled())
            {
                continue;
            }

            if (FromTown > 0 && Nearing(map, other.Location))
            {
                Townbound++;

                continue;
            }

            if (other.Region?.IsPartOf<HouseRegion>() == true)
            {
                Roofed++;

                continue;
            }

            if (Heading && other.Resolve?.Deed is { } work && work.Map == map && work.Where != Point3D.Zero
                && other.GetDistanceToSqrt(work.Where) <= HomeWithin
                && (BotOutlaw.Guarded(map, work.Where.X, work.Where.Y, work.Where.Z) || Nearing(map, work.Where)))
            {
                Homing++;

                continue;
            }

            if (mine < BotThreat.Power(other) * edge)
            {
                continue;
            }

            var alone = true;

            foreach (var third in map.GetMobilesInRange<BotMobile>(other.Location, Alone))
            {
                if (third != other && third != robber && third.Alive)
                {
                    alone = false;

                    break;
                }
            }

            if (!alone)
            {
                continue;
            }

            best = other;
            richest = coins;
        }

        return best;
    }

    public static string Describe() =>
        !Running
            ? "the thought of robbery is never offered"
            : $"{Settling} sweeps passed over while the island settled after a boot, {Rolled} rolls for the thought of robbery at {Chance:P1} every {EveryMs / 60000} min, {Tempted} came up, {NotFree} on a bot not its own master, {Holding} on a bot holding steadfast work, {NoVictim} found nobody alone with {Purse}gp outside a town, {Townbound} stood too near a town to be worth it, {Homing} were walking into one, {Roofed} stood under a roof, {Practising} sent to practise hiding first, {Waiting} sent to lie in wait where the work is ({Nowhere} found nowhere worth waiting at, {Sealed} places passed over for having no road to them), {Pressed} pressed into a robbery ({Interrupted} off work in hand); {BotWaylay.Describe()}; {BotRob.Describe()}";

    public static void Forget()
    {
        _rolledTick.Clear();
        Rolled = 0;
        Tempted = 0;
        NoVictim = 0;
        NotFree = 0;
        Holding = 0;
        Pressed = 0;
        Interrupted = 0;
        Practising = 0;
        Settling = 0;
        Townbound = 0;
        Homing = 0;
        Roofed = 0;
        _sweptTick = 0;
        BotRob.Forget();
    }
}
