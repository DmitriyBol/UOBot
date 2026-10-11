using System;
using System.Collections.Generic;
using Server.Items;
using Server.Logging;

namespace Server.BotAI.V2;

/// <summary>
/// The ambush on the road: two or three of The Shadow lie in wait ahead of a lone carrier with goods worth taking, out of any
/// guarded town, and fall on it as it passes.
///
/// <para>
/// <b>Patrick, 07.10.2026: "ambushes on lone carriers with goods on the road … this gives a living reason for guards, escorts and
/// caravan guards."</b> A carrier is a bot on a delivery (<see cref="BotDeliver"/>) or a trader's haul (<see cref="BotHaul"/>)
/// whose goods are worth <see cref="LeastCargo"/> or more by the market's worth; lone means nobody of ours who could fight
/// within <see cref="EscortWithin"/> tiles, so an escort or a caravan guard makes it no target at all. One party at a time
/// (<see cref="EveryMs"/> between them), never in a guarded town, and only when the party is no weaker than the carrier
/// (<see cref="ChooseOdds"/>), the companies' own choosing rule.
/// </para>
///
/// <para>
/// <b>The fight is the engine's, and so is the carrier's answer.</b> The bandits close with <see cref="BotBrawl"/>'s robbery; the
/// carrier's reflexes (react14 on) fight, flee or cry for help as against anything; beaten to a quarter it chooses by
/// <see cref="BotQuarter"/>'s rule — help near or still a match: fight on; a ward in reach: run; otherwise throw half its pack
/// down — and the bandits take what is thrown and go. They withdraw as a company does, when what stands against them on the
/// field passes <c>BotSquad.WithdrawOdds</c> times their own strength. A carrier killed is a murder on the killer's record (red,
/// the Baron's patrol, the inquest), and its corpse is gone through. A carrier that lives reports the whole party: posted at once.
/// What is taken is stolen goods (<see cref="BotCrime.Heat"/>) and goes to the fence.
/// </para>
/// </summary>
public static class BotHighway
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotHighway));

    public static bool Running { get; set; } = true;

    public static int EveryMs { get; set; } = 1200000;

    public static int SweepMs { get; set; } = 30000;

    public static int LeastCargo { get; set; } = 100;

    public static double CargoShare { get; set; } = 0.25;

    public static int CargoFloor { get; set; } = 40;

    public static int CargoSeen { get; set; } = 40;

    public const int CargoWindow = 256;

    public static double Lead { get; set; } = 1.3;

    public static int LeadSlack { get; set; } = 20;

    public static int OnRoad { get; set; } = 12;

    public static int MostRelays { get; set; } = 3;

    public static int LeastParty { get; set; } = 2;

    public static int MostParty { get; set; } = 3;

    public static int PartyReach { get; set; } = 250;

    public static int AheadTiles { get; set; } = 24;

    public static int MusterMs { get; set; } = 180000;

    public static int WaitMs { get; set; } = 240000;

    public static int CapMs { get; set; } = 900000;

    public static int StrikeWithin { get; set; } = 12;

    public static int EscortWithin { get; set; } = 12;

    public static double ChooseOdds { get; set; } = 1.0;

    public static double Prior { get; set; } = 1.0;

    public static bool Press { get; set; } = true;

    public static int MostAhead { get; set; } = 300;

    public static bool StrikeTogether { get; set; } = true;

    public static double ThrowBelow { get; set; } = 40.0;

    public static bool HireHands { get; set; } = true;

    public static long Sweeps { get; private set; }

    public static long Carriers { get; private set; }

    public static long Escorted { get; private set; }

    public static long Light { get; private set; }

    public static long InTown { get; private set; }

    public static long Short { get; private set; }

    public static long Outmatched { get; private set; }

    public static long Laid { get; private set; }

    public static long Joined { get; private set; }

    public static long Unmustered { get; private set; }

    public static long Struck { get; private set; }

    public static long Missed { get; private set; }

    public static long Paid { get; private set; }

    public static long Fled { get; private set; }

    public static long FoughtOn { get; private set; }

    public static long Killed { get; private set; }

    public static long Withdrawn { get; private set; }

    public static long BanditDeaths { get; private set; }

    public static long TakenGold { get; private set; }

    public static long TakenThings { get; private set; }

    public static long TakenWorth { get; private set; }

    public static long NoCatch { get; private set; }

    public static long Relaid { get; private set; }

    public static long LostTrail { get; private set; }

    public static long Unloaded { get; private set; }

    public static long Recruits { get; private set; }

    public static long Pressed { get; private set; }

    public static long FromTurn { get; private set; }

    public static long PutDown { get; private set; }

    public static long Thrown { get; private set; }

    public static long Hired { get; private set; }

    private static readonly Dictionary<string, long> _thrownKinds = [];

    public static long TooFew { get; private set; }

    public static long FellApart { get; private set; }

    public static long HeldStrike { get; private set; }

    public static long KeptOnGoal { get; private set; }

    public static long TooFarAhead { get; private set; }

    private static readonly Dictionary<string, long> _refused = [];

    private static readonly int[] _cargo = new int[CargoWindow];

    private static int _cargoCount;

    private static int _cargoNext;

    private static readonly int[] _cargoSort = new int[CargoWindow];

    /// <summary>One ambush: who lies in wait, for whom, where, and how it has gone.</summary>
    public sealed class Party
    {
        public readonly List<BotMobile> Members = [];

        public BotMobile Carrier;

        public Map Map;

        public Point3D Spot;

        public int SpotStamp;

        public int Relays;

        public string Town;

        public int Cargo;

        public long Formed;

        public long StruckTick;

        public bool Hit;

        public bool Chosen;

        public bool HeldOnce;

        public bool Withdraw;

        public bool Quay;

        public bool CarrierDown;

        public BotMobile Killer;

        public string Outcome;

        public long OddsTick;

        public bool Closed;

        public int Gold;

        public int Things;

        public int Worth;

        public readonly HashSet<Serial> Took = [];

        public readonly Dictionary<Serial, int> Made = [];

        public readonly HashSet<Serial> Ended = [];

        public Point3D Goal;

        public int Committed()
        {
            var count = 0;

            for (var i = 0; i < Members.Count; i++)
            {
                if (Members[i] is { Deleted: false, Alive: true } member && Took.Contains(member.Serial) && !Ended.Contains(member.Serial))
                {
                    count++;
                }
            }

            return count;
        }

        public int AtHand(Point3D at, int range)
        {
            var count = 0;

            for (var i = 0; i < Members.Count; i++)
            {
                if (Members[i] is { Deleted: false, Alive: true } member && member.Map == Map && Took.Contains(member.Serial)
                    && !Ended.Contains(member.Serial) && member.InRange(at, range))
                {
                    count++;
                }
            }

            return count;
        }

        public bool Holds(Mobile m)
        {
            for (var i = 0; i < Members.Count; i++)
            {
                if (ReferenceEquals(Members[i], m))
                {
                    return true;
                }
            }

            return false;
        }
    }

    private static Party _party;

    private static long _sweptTick;

    private static long _laidTick;

    private static bool _clock;

    public static Party Now => _party is { Closed: false } ? _party : null;

    public static void Sweep(long now)
    {
        if (now - _sweptTick < SweepMs)
        {
            return;
        }

        _sweptTick = now;

        if (_party is { Closed: false } party)
        {
            if (!Running)
            {
                Close(party, "the ambushes are switched off");
            }
            else if (now - party.Formed >= CapMs)
            {
                Close(party, "it ran to its cap");
            }
            else if (party.Took.Count == 0 && now - party.Formed >= MusterMs)
            {
                Unmustered++;
                Close(party, "nobody of the band took the call");
            }
            else if (party.Took.Count > 0 && party.Ended.Count >= party.Took.Count)
            {
                Close(party, party.Outcome ?? "every bandit is done");
            }
            else if (Press && !party.Hit && party.Took.Count > 0 && party.Committed() < Math.Max(1, LeastParty))
            {
                FellApart++;
                Close(party, $"fewer than {Math.Max(1, LeastParty)} of the band were left on it before the carrier came");
            }
            else if (!party.Hit)
            {
                Follow(party);
            }

            return;
        }

        if (!Running || !BotCrime.Running || !BotUnderworld.Exists)
        {
            return;
        }

        if (!_clock)
        {
            _clock = true;
            _laidTick = now;

            return;
        }

        if (now - _laidTick < EveryMs)
        {
            return;
        }

        Sweeps++;
        Lay(now);
    }

    private static readonly List<BotMobile> _band = [];

    private static void Lay(long now)
    {
        var map = BotPopulation.Home;

        if (map == null || map == Map.Internal)
        {
            return;
        }

        _band.Clear();
        _free.Clear();

        var bots = BotPopulation.Bots;

        for (var i = 0; i < bots.Count; i++)
        {
            if (bots[i] is { Deleted: false, Alive: true } bot && bot.Map == map && (Bandit(bot) || Press && HireHands && Hireling(bot)) && !BotFence.Is(bot)
                && !BotOutlaw.Jailed(bot) && !BotDungeon.Under(bot.Location) && (Press || bot.Squad == null && !bot.Tired))
            {
                _band.Add(bot);

                if (!Press || Callable(bot) == null)
                {
                    _free.Add(bot.Serial);
                }
            }
        }

        if (_band.Count < Math.Max(1, LeastParty))
        {
            Short++;

            return;
        }

        BotMobile carrier = null;
        var cargo = 0;
        var nearest = int.MaxValue;
        var bar = Bar();

        BotMobile busyCarrier = null;
        var busyCargo = 0;
        var busyNearest = int.MaxValue;

        for (var i = 0; i < bots.Count; i++)
        {
            if (bots[i] is not { Deleted: false, Alive: true, Squad: null } bot || bot.Map != map || Bandit(bot)
                || Goods(bot) is not { } goods || BotOutlaw.Outlaw(bot) || BotDungeon.Under(bot.Location))
            {
                continue;
            }

            Carriers++;

            if (BotOutlaw.Guarded(map, bot.X, bot.Y, bot.Z))
            {
                InTown++;

                continue;
            }

            var worth = Cargo(bot, goods);

            Measure(worth);

            if (worth < bar)
            {
                Light++;

                continue;
            }

            if (Escort(bot, map))
            {
                Escorted++;

                continue;
            }

            var any = Nearest(bot, false);

            if (any <= PartyReach && any < busyNearest)
            {
                busyNearest = any;
                busyCarrier = bot;
                busyCargo = worth;
            }

            var away = Press ? Nearest(bot, true) : any;

            if (away > PartyReach || away >= nearest)
            {
                continue;
            }

            nearest = away;
            carrier = bot;
            cargo = worth;
        }

        if (carrier == null)
        {
            carrier = busyCarrier;
            cargo = busyCargo;
        }

        if (carrier == null)
        {
            return;
        }

        _band.Sort((a, b) => a.GetDistanceToSqrt(carrier.Location).CompareTo(b.GetDistanceToSqrt(carrier.Location)));

        var party = new Party { Carrier = carrier, Map = map, Cargo = cargo, Formed = now, Goal = carrier.Resolve?.Deed?.Where ?? Point3D.Zero };
        var power = 0.0;
        _passed.Clear();
        _passedWho.Clear();

        for (var i = 0; i < _band.Count && party.Members.Count < Math.Max(1, MostParty); i++)
        {
            if (!Utility.InRange(_band[i].Location, carrier.Location, PartyReach))
            {
                break;
            }

            if (!Bandit(_band[i]) && _band[i].Guild is Server.Guilds.Guild own && carrier.Guild is Server.Guilds.Guild theirs
                && (ReferenceEquals(own, theirs) || own.IsAlly(theirs)))
            {
                _passed.Add("a hired hand of the carrier's guild or an ally");
                _passedWho.Add($"{_band[i].Name} (of the carrier's guild or an ally)");

                continue;
            }

            if (Press && Callable(_band[i]) is { } why)
            {
                _passed.Add(why);
                _passedWho.Add($"{_band[i].Name} ({why})");

                continue;
            }

            party.Members.Add(_band[i]);
            power += BotThreat.Now(_band[i]);
        }

        if (party.Members.Count < Math.Max(1, LeastParty))
        {
            Short++;

            if (Press)
            {
                TooFew++;

                for (var i = 0; i < _passed.Count; i++)
                {
                    _refused[_passed[i]] = _refused.GetValueOrDefault(_passed[i]) + 1;
                }
            }

            return;
        }

        for (var i = 0; i < _passed.Count; i++)
        {
            _refused[_passed[i]] = _refused.GetValueOrDefault(_passed[i]) + 1;
        }

        var against = BotThreat.Now(carrier);

        if (power < against * ChooseOdds)
        {
            Outmatched++;

            logger.Information(
                "The Shadow lets {Carrier} pass with {Cargo}gp of goods: {Count} of the band, {Power:F0} of strength, against its {Against:F0}",
                carrier.Name,
                cargo,
                party.Members.Count,
                power,
                against
            );

            return;
        }

        party.Spot = Ahead(map, carrier, party.Members);

        if (party.Spot == Point3D.Zero)
        {
            NoCatch++;

            logger.Information(
                "The Shadow lets {Carrier} pass with {Cargo}gp of goods: no point of its road ahead, out of the guards, that it reaches after the band",
                carrier.Name,
                cargo
            );

            return;
        }

        for (var i = 0; i < party.Members.Count; i++)
        {
            if (!BotUnderworld.Member(party.Members[i]))
            {
                Recruits++;
            }
        }

        party.Town = BotCrime.TownAt(party.Spot);
        _party = party;
        _laidTick = now;
        Laid++;

        List<string> names = [];

        for (var i = 0; i < party.Members.Count; i++)
        {
            names.Add(party.Members[i].Name);
        }

        logger.Information(
            "The Shadow lays an ambush for {Carrier} the {Class}, carrying {Cargo}gp of goods alone (the bar {Bar}gp), at ({X}, {Y}) near {Town}, {Ahead} tiles ahead on its road: {Names}, {Power:F0} of strength against its {Against:F0}{Passed}",
            carrier.Name,
            carrier.Class?.Name,
            cargo,
            bar,
            party.Spot.X,
            party.Spot.Y,
            party.Town,
            Along(carrier, party.Spot),
            string.Join(", ", names),
            power,
            against,
            _passedWho.Count > 0 ? $"; passed over: {string.Join(", ", _passedWho)}" : ""
        );

        if (!Press)
        {
            return;
        }

        for (var i = 0; i < party.Members.Count; i++)
        {
            if (party.Members[i] is not IBotWilful wilful)
            {
                continue;
            }

            var turn = wilful.Resolve?.Deed is BotPickpocket;
            var held = wilful.Resolve?.Deed != null;
            var thrownKind = wilful.Resolve is { Deed: { } heldDeed } heldResolve
                             && (!BotWill.Resume || !heldDeed.Resumes || heldResolve.Paused != null)
                ? heldDeed.Kind
                : null;
            var hired = !Bandit(party.Members[i]);

            if (BotWill.Press(wilful, new BotLieInWait(party), $"called to lie in wait for {carrier.Name}'s {cargo}gp of goods"))
            {
                Pressed++;

                if (thrownKind != null)
                {
                    Thrown++;
                    _thrownKinds[thrownKind] = _thrownKinds.GetValueOrDefault(thrownKind) + 1;
                }
                else if (held)
                {
                    PutDown++;
                }

                if (hired)
                {
                    Hired++;
                }

                if (turn)
                {
                    FromTurn++;
                }

                BotVoice.Say(party.Members[i], "local", i == 0 ? $"{carrier.Name} is on the road with goods. With me." : "Coming.", true);
            }
        }

        if (party.Committed() < Math.Max(1, LeastParty))
        {
            FellApart++;
            Close(party, "the band could not be called together");
        }
    }

    public static bool Lay(Party party, string why)
    {
        if (!Running || party == null || party.Members.Count == 0 || party.Carrier == null || _party is { Closed: false })
        {
            return false;
        }

        _party = party;
        _laidTick = Core.TickCount;
        _clock = true;
        Laid++;

        for (var i = 0; i < party.Members.Count; i++)
        {
            if (party.Members[i] is IBotWilful wilful && BotWill.Press(wilful, new BotLieInWait(party), $"called to lie in wait for {why}"))
            {
                Pressed++;

                BotVoice.Say(party.Members[i], "local", i == 0 ? "A ship's coming in heavy. To the quay, with me." : "Coming.", true);
            }
        }

        if (party.Committed() < Math.Max(1, LeastParty))
        {
            FellApart++;
            Close(party, "the band could not be called together on the quay");

            return false;
        }

        logger.Information("The Shadow lays a band on the quay at ({X}, {Y}) near {Town} for {Why}", party.Spot.X, party.Spot.Y, party.Town, why);

        return true;
    }

    private static bool Hireling(BotMobile bot) =>
        !Bandit(bot) && !BotOutlaw.Outlaw(bot) && Goods(bot) == null && bot.Class is { } klass
        && klass.Role is not (BotRole.Producer or BotRole.Medic) && !klass.Unpaid && !klass.Leads && !klass.Neutral && klass is not (BotSage or BotArchitect)
        && (BotTraits.Has(bot, BotTrait.Greedy) || BotTraits.Has(bot, BotTrait.Reckless)) && !BotGuilds.IsHead(bot)
        && BotYield.Wealth(bot) < BotCrime.PoorBelow;

    private static readonly List<string> _passed = [];

    private static readonly List<string> _passedWho = [];

    private static string Callable(BotMobile bot)
    {
        if (bot.Squad != null)
        {
            return "in a company";
        }

        if (bot.Tired)
        {
            return "tired";
        }

        if (bot.HitsMax > 0 && bot.Hits < bot.HitsMax * 0.7)
        {
            return "hurt";
        }

        if (bot.Combatant != null)
        {
            return "at a fight";
        }

        var resolve = bot.Resolve;

        if (resolve?.Deed is not { } deed)
        {
            return null;
        }

        if (deed is BotLieInWait)
        {
            return "on another ambush";
        }

        if (deed.Alongside)
        {
            return "answering a company or its class";
        }

        if (deed.Foe != null || BotSquads.Fighting(bot))
        {
            return "at a fight";
        }

        if (BotSquads.Carrying(bot))
        {
            return "carrying another's goods";
        }

        if (deed.Summons)
        {
            return "summoned";
        }

        if ((!BotWill.Resume || !deed.Resumes || resolve.Paused != null) && (ThrowBelow <= 0.0 || deed.Expects >= ThrowBelow || deed.Committed))
        {
            return "on work that would be thrown away";
        }

        return null;
    }

    public static bool Bandit(Mobile m) => BotUnderworld.Member(m) || BotCrime.IsThief(m);

    private static Type Goods(BotMobile bot) =>
        bot.Resolve?.Deed switch
        {
            IBotWantCarrier carrying => carrying.Goods,
            BotCaravan caravan => caravan.Goods,

            BotColonyRun run => run.Goods,
            _ => null
        };

    private static void Measure(int worth)
    {
        if (worth <= 0)
        {
            return;
        }

        _cargo[_cargoNext] = Math.Max(0, worth);
        _cargoNext = (_cargoNext + 1) % CargoWindow;
        _cargoCount = Math.Min(CargoWindow, _cargoCount + 1);
    }

    public static int Bar()
    {
        if (_cargoCount < Math.Max(1, CargoSeen))
        {
            return Math.Max(CargoFloor, LeastCargo);
        }

        return Math.Max(CargoFloor, Quantile(1.0 - Math.Clamp(CargoShare, 0.01, 1.0)));
    }

    private static int Quantile(double q)
    {
        if (_cargoCount == 0)
        {
            return 0;
        }

        Array.Copy(_cargo, _cargoSort, _cargoCount);
        Array.Sort(_cargoSort, 0, _cargoCount);

        return _cargoSort[Math.Clamp((int)(q * _cargoCount), 0, _cargoCount - 1)];
    }

    private static int Cargo(BotMobile carrier, Type goods)
    {
        if (goods == null || carrier.Backpack is not { } pack)
        {
            return 0;
        }

        return pack.GetAmount(goods) * BotCrime.Worth(goods);
    }

    private static bool Escort(BotMobile carrier, Map map)
    {
        foreach (var other in map.GetMobilesInRange<BotMobile>(carrier.Location, Math.Max(1, EscortWithin)))
        {
            if (other == carrier || other is not { Deleted: false, Alive: true } || Bandit(other) || BotOutlaw.Outlaw(other)
                || other.Class is not { } klass || klass.Role is BotRole.Producer || klass.Neutral)
            {
                continue;
            }

            return true;
        }

        return false;
    }

    private static readonly HashSet<Serial> _free = [];

    private static int Nearest(BotMobile carrier, bool free)
    {
        var nearest = int.MaxValue;

        for (var i = 0; i < _band.Count; i++)
        {
            if (free && !_free.Contains(_band[i].Serial))
            {
                continue;
            }

            var away = (int)_band[i].GetDistanceToSqrt(carrier.Location);

            if (away < nearest)
            {
                nearest = away;
            }
        }

        return nearest;
    }

    private static readonly List<Point3D> _road = [];

    private static List<Point3D> Road(BotMobile carrier)
    {
        _road.Clear();

        var errand = carrier.Journey?.Current;

        if (errand != null && !errand.RouteSpent && errand.Route.Count > 0 && errand.Leg < errand.Route.Count)
        {
            for (var i = Math.Max(0, errand.Leg); i < errand.Route.Count; i++)
            {
                _road.Add(errand.Route[i]);
            }

            if (errand.Gate == Point3D.Zero && errand.Follow == null)
            {
                _road.Add(errand.Target);
            }

            return _road;
        }

        var goal = carrier.Resolve?.Deed?.Where ?? Point3D.Zero;

        if (goal == Point3D.Zero || carrier.Map == null)
        {
            return _road;
        }

        var from = carrier.Location;
        var dx = goal.X - from.X;
        var dy = goal.Y - from.Y;
        var pieces = Math.Max(1, (int)(Math.Sqrt(dx * (double)dx + dy * (double)dy) / 16));

        for (var k = 1; k <= pieces; k++)
        {
            var x = from.X + dx * k / pieces;
            var y = from.Y + dy * k / pieces;

            _road.Add(k == pieces ? goal : new Point3D(x, y, carrier.Map.GetAverageZ(x, y)));
        }

        return _road;
    }

    private static double Away(Point3D a, Point3D b) => Math.Sqrt((a.X - b.X) * (double)(a.X - b.X) + (a.Y - b.Y) * (double)(a.Y - b.Y));

    private static Point3D Ahead(Map map, BotMobile carrier, List<BotMobile> band)
    {
        var road = Road(carrier);
        var along = 0.0;
        var at = carrier.Location;

        for (var i = 0; i < road.Count; i++)
        {
            var point = road[i];

            along += Away(at, point);
            at = point;

            if (MostAhead > 0 && along > MostAhead)
            {
                TooFarAhead++;

                return Point3D.Zero;
            }

            if (along < AheadTiles || BotOutlaw.Guarded(map, point.X, point.Y, point.Z) || BotDungeon.Under(point))
            {
                continue;
            }

            var nearest = double.MaxValue;

            for (var b = 0; b < band.Count; b++)
            {
                if (band[b] is { Deleted: false, Alive: true } bandit && bandit.Map == map)
                {
                    nearest = Math.Min(nearest, Away(bandit.Location, point));
                }
            }

            if (nearest < double.MaxValue && along >= nearest * Math.Max(1.0, Lead) + LeadSlack)
            {
                return point;
            }
        }

        return Point3D.Zero;
    }

    private static double Off(Point3D p, Point3D a, Point3D b)
    {
        double dx = b.X - a.X;
        double dy = b.Y - a.Y;
        var length = dx * dx + dy * dy;
        var t = length <= 0.0 ? 0.0 : Math.Clamp(((p.X - a.X) * dx + (p.Y - a.Y) * dy) / length, 0.0, 1.0);
        var x = a.X + t * dx - p.X;
        var y = a.Y + t * dy - p.Y;

        return Math.Sqrt(x * x + y * y);
    }

    private static int Along(BotMobile carrier, Point3D spot)
    {
        var road = Road(carrier);
        var along = 0.0;
        var at = carrier.Location;

        for (var i = 0; i < road.Count; i++)
        {
            if (Off(spot, at, road[i]) <= OnRoad)
            {
                return (int)(along + Away(at, road[i]));
            }

            along += Away(at, road[i]);
            at = road[i];
        }

        return -1;
    }

    private static void Follow(Party party)
    {
        var carrier = party.Carrier;

        if (carrier is not { Deleted: false, Alive: true } || carrier.Map != party.Map)
        {
            Close(party, "the carrier is gone");

            return;
        }

        if (Goods(carrier) == null)
        {
            Unloaded++;
            Close(party, $"{carrier.Name} set its load down before it came by");

            return;
        }

        if (carrier.InRange(party.Spot, StrikeWithin) || Along(carrier, party.Spot) >= 0)
        {
            return;
        }

        if (party.Goal != Point3D.Zero && carrier.Resolve?.Deed?.Where is { } goal && goal == party.Goal
            && Away(carrier.Location, goal) > Away(party.Spot, goal) + StrikeWithin)
        {
            KeptOnGoal++;

            return;
        }

        var spot = party.Relays < Math.Max(0, MostRelays) ? Ahead(party.Map, carrier, party.Members) : Point3D.Zero;

        if (spot == Point3D.Zero)
        {
            LostTrail++;
            Close(party, $"{carrier.Name} left the road the band lay on, and the band lost it");

            return;
        }

        party.Spot = spot;
        party.Relays++;
        party.SpotStamp++;
        party.Town = BotCrime.TownAt(spot);
        Relaid++;

        logger.Information(
            "The Shadow moves its ambush for {Carrier}: it left the road the band lay on; now ({X}, {Y}) near {Town}, {Ahead} tiles ahead on its road (move {Relays} of {Most})",
            carrier.Name,
            spot.X,
            spot.Y,
            party.Town,
            Along(carrier, spot),
            party.Relays,
            MostRelays
        );
    }

    private static void Close(Party party, string why)
    {
        if (party.Closed)
        {
            return;
        }

        party.Closed = true;

        if (party.Hit && party.Carrier is { Deleted: false, Alive: true } carrier && !party.CarrierDown)
        {
            for (var i = 0; i < party.Members.Count; i++)
            {
                if (party.Members[i] is { Deleted: false } bandit)
                {
                    var sheet = BotCrime.Of(bandit.Name, true);

                    sheet.Ambushes++;
                    BotCrime.Report(bandit, carrier, party.Town, "an ambush on the road", i == 0 ? party.Worth : 0, Math.Max(1, BotCrime.ReportsToPost));
                }
            }
        }

        logger.Information(
            "The ambush on {Carrier} near {Town} is over: {Why}; {Gold}gp and {Things} things worth {Worth}gp taken, the carrier {Fate}",
            party.Carrier?.Name,
            party.Town,
            why,
            party.Gold,
            party.Things,
            party.Worth,
            party.CarrierDown ? "dead" : party.Hit ? "alive and telling" : "never struck"
        );
    }

    public static void Fell(BotMobile fallen, Mobile killer)
    {
        var party = _party;

        if (party == null || party.Closed && Core.TickCount - party.Formed > CapMs)
        {
            return;
        }

        if (ReferenceEquals(fallen, party.Carrier) && party.Hit && !party.CarrierDown)
        {
            party.CarrierDown = true;
            Killed++;

            var by = killer as BotMobile;

            if (by == null || !party.Holds(by))
            {
                by = party.Members.Count > 0 ? party.Members[0] : null;
            }

            party.Killer = by;

            if (by != null)
            {
                BotOutlaw.Murdered(by, fallen);
                BotUnderworld.Murder(by, false);
            }

            return;
        }

        if (party.Holds(fallen) && party.Hit)
        {
            BanditDeaths++;
        }
    }

    public static string Over(IBotWilful bot, BotBrawl brawl)
    {
        var party = _party;

        if (party == null || party.Closed)
        {
            return "the ambush is over";
        }

        if (party.Withdraw)
        {
            return "the band withdrew";
        }

        if (party.Outcome != null)
        {
            return party.Outcome;
        }

        var carrier = party.Carrier;

        if (carrier is not { Deleted: false, Alive: true })
        {
            return null;
        }

        var now = Core.TickCount;

        if (now - party.OddsTick >= 3000)
        {
            party.OddsTick = now;

            if (Overmatched(party, carrier))
            {
                party.Withdraw = true;
                Withdrawn++;

                if (bot?.Self is { } self)
                {
                    BotVoice.Say(self, "local", "Too many of them! Scatter!", true);
                }

                logger.Information("The ambush on {Carrier} is called off: too many stand with it, and the band withdraws", carrier.Name);

                return "the band withdrew";
            }
        }

        if (!party.Chosen && BotQuarter.Beaten(carrier) && bot?.Self is BotMobile robber)
        {
            party.Chosen = true;

            var before = BotCrime.Census(robber);
            var answer = BotQuarter.Choose(robber, carrier);

            switch (answer)
            {
                case BotQuarter.Answer.Pay:
                {
                    Paid++;
                    Took(party, robber, before);
                    party.Outcome = $"{carrier.Name} threw down its goods";

                    return party.Outcome;
                }
                case BotQuarter.Answer.Flee:
                {
                    Fled++;
                    party.Outcome = $"{carrier.Name} ran for it";

                    return party.Outcome;
                }
                default:
                {
                    FoughtOn++;

                    break;
                }
            }
        }

        return null;
    }

    private static bool Overmatched(Party party, BotMobile carrier)
    {
        var map = carrier.Map;

        if (map == null)
        {
            return false;
        }

        var theirs = 0.0;

        foreach (var other in map.GetMobilesInRange<BotMobile>(carrier.Location, Math.Max(1, EscortWithin)))
        {
            if (other is { Deleted: false, Alive: true } && !party.Holds(other) && !Bandit(other) && !BotOutlaw.Outlaw(other))
            {
                theirs += BotThreat.Now(other);
            }
        }

        var ours = 0.0;

        for (var i = 0; i < party.Members.Count; i++)
        {
            if (party.Members[i] is { Deleted: false, Alive: true } bandit && bandit.Map == map
                && bandit.InRange(carrier.Location, EscortWithin * 2))
            {
                ours += BotThreat.Now(bandit);
            }
        }

        return ours <= 0.0 || theirs > ours * Math.Max(1.0, BotSquad.WithdrawOdds);
    }

    public static void Took(Party party, BotMobile bandit, Dictionary<Type, int> before)
    {
        List<(Type Kind, int Amount)> goods = [];
        var gold = BotCrime.Gained(before, BotCrime.Census(bandit), goods);
        var things = 0;
        var worth = gold;

        for (var i = 0; i < goods.Count; i++)
        {
            things += goods[i].Amount;
            worth += goods[i].Amount * BotCrime.Worth(goods[i].Kind);
            BotCrime.Heat(bandit, goods[i].Kind, goods[i].Amount, party.Town, party.Carrier?.Name);
        }

        party.Made[bandit.Serial] = party.Made.GetValueOrDefault(bandit.Serial) + (int)((worth - gold) * BotFencer.FenceShare);
        party.Gold += gold;
        party.Things += things;
        party.Worth += worth;
        TakenGold += gold;
        TakenThings += things;
        TakenWorth += worth;

        if (gold > 0 || things > 0)
        {
            BotUnderworld.Robbed(bandit);
            BotEvents.Post("crime", bandit, $"took {gold}gp and {things} things off {party.Carrier?.Name} on the road near {party.Town}");
        }
    }

    public static void Taking(Party party, Mobile bandit)
    {
        if (party.Took.Add(bandit.Serial))
        {
            Joined++;
        }
    }

    public static void Done(Party party, Mobile bandit) => party.Ended.Add(bandit.Serial);

    public static void Strike(Party party, BotMobile by)
    {
        if (party.Hit)
        {
            return;
        }

        party.Hit = true;
        party.StruckTick = Core.TickCount;
        Struck++;

        BotPiracy.Boarded(party);

        BotVoice.Say(by, "local", $"Stand and deliver, {party.Carrier?.Name}! The road is ours tonight.", true);
        BotEvents.Post("crime", by, $"springs an ambush on {party.Carrier?.Name} near {party.Town}");

        logger.Information(
            "{By} of The Shadow springs the ambush on {Carrier} at ({X}, {Y}) near {Town}",
            by.Name,
            party.Carrier?.Name,
            by.X,
            by.Y,
            party.Town
        );
    }

    public static void Miss() => Missed++;

    public static bool Together(Party party, BotMobile carrier)
    {
        if (!Press || !StrikeTogether || party == null || carrier == null)
        {
            return true;
        }

        var need = Math.Min(Math.Max(1, LeastParty), Math.Max(1, party.Committed()));

        if (party.AtHand(carrier.Location, StrikeWithin * 2) >= need)
        {
            return true;
        }

        if (!party.HeldOnce)
        {
            party.HeldOnce = true;
            HeldStrike++;
        }

        return false;
    }

    public static string Describe() =>
        !Running
            ? "the roads: off"
            : $"the roads: {Sweeps} looks for a carrier ({Carriers} carriers seen, {InTown} in a guarded town, {Light} carrying under the bar, {Escorted} escorted, {Short} looks with the band too few, {Outmatched} let pass as too strong, {NoCatch} the band could not get ahead of on their road; the bar {Bar()}gp, {CargoBook()}), {Laid} ambushes laid ({Recruits} recruits called beside The Shadow), {Joined} bandits took the call and {Unmustered} ambushes nobody came to; {Mustered()}; {Relaid} moved after a carrier that turned, {LostTrail} carriers lost, {Unloaded} set their load down first; {Struck} sprung and {Missed} waits the carrier never came by; the carrier threw its goods down {Paid} times, ran {Fled}, fought on {FoughtOn} and died {Killed}; {Withdrawn} withdrawals at the odds; {BanditDeaths} bandits died at it; {TakenGold}gp and {TakenThings} things worth {TakenWorth}gp taken";

    private static string Mustered()
    {
        if (!Press)
        {
            return "the muster: off (the ambush offered at auction)";
        }

        var text = $"the muster: {Pressed} bandits pressed into an ambush ({Hired} of them hired hands, greedy or reckless and poor; {FromTurn} off a turn in a crowd, {PutDown} putting work down to take up again, {Thrown} throwing work under {ThrowBelow:F0}gp a minute away{ThrownKinds()}), {TooFew} calls with fewer than {Math.Max(1, LeastParty)} free in {PartyReach} tiles, {FellApart} ambushes closed with fewer than {Math.Max(1, LeastParty)} left on them, {HeldStrike} strikes held for a second bandit, {KeptOnGoal} sweeps kept the place for a carrier off its drawn road but bound the same way, {TooFarAhead} spots over {MostAhead} tiles along refused";

        if (_refused.Count == 0)
        {
            return $"{text}; no bandit within reach passed over";
        }

        List<string> parts = [];

        foreach (var (why, count) in _refused)
        {
            parts.Add($"{why} {count}");
        }

        return $"{text}; bandits within reach passed over by why: {string.Join(", ", parts)}";
    }

    private static string ThrownKinds()
    {
        if (_thrownKinds.Count == 0)
        {
            return "";
        }

        List<string> parts = [];

        foreach (var (kind, count) in _thrownKinds)
        {
            parts.Add($"{kind} {count}");
        }

        return $": {string.Join(", ", parts)}";
    }

    public static void Forget()
    {
        _party = null;
        _clock = false;
        Thrown = 0;
        Hired = 0;
        _thrownKinds.Clear();
        Pressed = 0;
        FromTurn = 0;
        PutDown = 0;
        TooFew = 0;
        FellApart = 0;
        HeldStrike = 0;
        KeptOnGoal = 0;
        TooFarAhead = 0;
        _refused.Clear();
        Sweeps = 0;
        Carriers = 0;
        Escorted = 0;
        Light = 0;
        InTown = 0;
        Short = 0;
        Outmatched = 0;
        Laid = 0;
        Joined = 0;
        Unmustered = 0;
        Struck = 0;
        Missed = 0;
        Paid = 0;
        Fled = 0;
        FoughtOn = 0;
        Killed = 0;
        Withdrawn = 0;
        BanditDeaths = 0;
        TakenGold = 0;
        TakenThings = 0;
        TakenWorth = 0;
        NoCatch = 0;
        Relaid = 0;
        LostTrail = 0;
        Unloaded = 0;
        Recruits = 0;
        _cargoCount = 0;
        _cargoNext = 0;
    }

    private static string CargoBook() =>
        _cargoCount == 0
            ? "no cargo measured on the roads yet"
            : $"cargo of the last {_cargoCount} carriers on the roads: {Quantile(0.5)}gp at the median, {Quantile(0.75)} at the 75th percentile, {Quantile(0.9)} at the 90th";
}

/// <summary>Offers a bandit called to the standing ambush its place in it. See <see cref="BotHighway"/>.</summary>
public sealed class BotHighwayman : IBotProposer
{
    public string Name => "Highwayman";

    public BotStanding Rung => BotStanding.Free;

    public BotDeed Propose(IBotWilful bot)
    {
        if (BotHighway.Now is not { } party || bot?.Self is not BotMobile { Deleted: false, Alive: true } body || !party.Holds(body)
            || party.Took.Contains(body.Serial) || party.Ended.Contains(body.Serial) || body.Map != party.Map)
        {
            return null;
        }

        return new BotLieInWait(party);
    }
}

/// <summary>One bandit's part in an ambush: walk to the spot, hide, wait for the carrier, strike, and go through what is left.</summary>
public sealed class BotLieInWait : BotDeed
{
    public const string Trade = "ambush";

    private readonly BotHighway.Party _party;

    private BotBrawl _fight;

    private long _waitFrom;

    private long _hideTick;

    private bool _looted;

    private bool _over;

    private Mobile _body;

    private int _stamp;

    public BotLieInWait(BotHighway.Party party)
    {
        _party = party;
    }

    public override string Kind => Trade;

    public override Map Map => _party.Map;

    public override Point3D Where => _party.Spot;

    public override double Expects =>
        Math.Max(BotHighway.Prior, _party.Cargo * BotFencer.FenceShare / Math.Max(1, _party.Members.Count) / Math.Max(1.0, Minutes));

    public override double Minutes => 6.0;

    public override bool Committed => true;

    public override bool Steadfast => true;

    public override bool Braves => true;

    public override Mobile Foe => _fight != null ? _party.Carrier : null;

    public override SkillName? Trains => null;

    public override int Made => _body != null ? _party.Made.GetValueOrDefault(_body.Serial) : 0;

    public override string Stage =>
        _fight != null ? $"ambush: {_fight.Stage}" : $"lying in wait for {_party.Carrier?.Name} near {_party.Town}";

    public override void Taken(IBotWilful bot)
    {
        if (bot?.Self is { } body)
        {
            _body = body;
            BotHighway.Taking(_party, body);
        }
    }

    public override void Drop(IBotWilful bot)
    {
        _fight?.Drop(bot);

        if (bot?.Self is { } body)
        {
            BotHighway.Done(_party, body);
        }
    }

    public override BotDoing Advance(IBotWilful bot)
    {
        if (bot?.Self is not BotMobile { Deleted: false, Alive: true } body || body.Map != _party.Map)
        {
            return End(bot, BotDoing.Failed("no body"));
        }

        if (_party.Closed)
        {
            return End(bot, BotDoing.Done("the ambush is over"));
        }

        var carrier = _party.Carrier;

        if (_fight == null)
        {
            if (carrier is not { Deleted: false, Alive: true } || carrier.Map != _party.Map)
            {
                return End(bot, BotDoing.Failed("the carrier is gone"));
            }

            if (!_party.Hit && body.InRange(carrier.Location, BotHighway.StrikeWithin) && body.InLOS(carrier) && !BotHighway.Together(_party, carrier))
            {
                return BotDoing.Work($"lying in wait for {carrier.Name}, waiting for the rest of the band");
            }

            if (_party.Hit || body.InRange(carrier.Location, BotHighway.StrikeWithin) && body.InLOS(carrier))
            {
                BotHighway.Strike(_party, body);

                var trains = body.Bond?.Weapon?.Skill ?? SkillName.Wrestling;

                _fight = new BotBrawl(carrier, BotBrawl.Robbery, trains, BotHighway.Over);
            }
            else if (_stamp != _party.SpotStamp || !body.InRange(_party.Spot, 3))
            {
                if (_stamp != _party.SpotStamp)
                {
                    _stamp = _party.SpotStamp;
                    _waitFrom = 0;
                }

                if (!body.InRange(_party.Spot, 3))
                {
                    return BotDoing.Walk(_party.Map, _party.Spot, BotArrival.Within(2), $"to lie in wait for {carrier.Name}");
                }

                return BotDoing.Work($"lying in wait for {carrier.Name}");
            }
            else
            {
                var now = Core.TickCount;

                if (_waitFrom == 0)
                {
                    _waitFrom = now;
                }

                if (now - _waitFrom >= BotHighway.WaitMs)
                {
                    BotHighway.Miss();

                    return End(bot, BotDoing.Failed($"{carrier.Name} never came by"));
                }

                if (!body.Hidden && now - _hideTick >= 10000)
                {
                    _hideTick = now;
                    BotShadow.Hide(body);
                }

                return BotDoing.Work($"lying in wait for {carrier.Name}");
            }
        }

        if (!_over)
        {
            var doing = _fight.Advance(bot);

            if (doing.Kind is not (BotDoingKind.Done or BotDoingKind.Failed))
            {
                return doing;
            }

            _over = true;

            if (!_party.CarrierDown || !ReferenceEquals(_party.Killer, body) && _party.Killer is { Deleted: false, Alive: true })
            {
                return End(bot, doing.Kind == BotDoingKind.Failed ? doing : BotDoing.Done($"ambush: {doing.Note}"));
            }
        }

        return Loot(bot, body);
    }

    private BotDoing Loot(IBotWilful bot, BotMobile body)
    {
        if (_looted || _party.Carrier?.Remains is not { Deleted: false } corpse || corpse.Map != body.Map)
        {
            return End(bot, BotDoing.Done($"{_party.Carrier?.Name} is down"));
        }

        if (!body.InRange(corpse.GetWorldLocation(), 2))
        {
            return BotDoing.Walk(body.Map, corpse.GetWorldLocation(), BotArrival.Within(1), $"to {_party.Carrier.Name}'s body");
        }

        _looted = true;

        var before = BotCrime.Census(body);
        List<Item> lifted = [];

        for (var i = 0; i < corpse.Items.Count; i++)
        {
            if (corpse.Items[i] is { Deleted: false, Movable: true } item && (item is Gold || BotShopkeep.Merchandise(item, null)))
            {
                lifted.Add(item);
            }
        }

        for (var i = 0; i < lifted.Count; i++)
        {
            body.Backpack?.TryDropItem(body, lifted[i], false);
        }

        BotHighway.Took(_party, body, before);

        return End(bot, BotDoing.Done($"went through {_party.Carrier.Name}'s body"));
    }

    private BotDoing End(IBotWilful bot, BotDoing doing)
    {
        if (bot?.Self is { } body)
        {
            BotHighway.Done(_party, body);
        }

        return doing;
    }
}
