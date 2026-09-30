using System;
using System.Collections.Generic;
using Server.Guilds;
using Server.Logging;

namespace Server.BotAI.V2;

/// <summary>
/// Offers the maker of a guild a dungeon its band could actually come back out of.
///
/// <para>
/// <b>Only a guild's maker, by order.</b> A delve is called by the head of a band, which on this shard is
/// the crafter every guild is founded on and the one bot in it with a mind. It is the same office that
/// charges the band with a trade and decides who is in it — see <c>BotCharter</c> and <c>BotRoster</c> —
/// and a delve is the third thing that office can do.
/// </para>
///
/// <para>
/// <b>Which dungeon is a measurement, not a preference.</b> Patrick's order names the reason in one line:
/// a demon is plainly not a harpy. Every dungeon has been measured — see <see cref="BotDungeon"/>, which
/// builds one of each creature its spawners name and reads its strength off the engine — and what this
/// does with those numbers is pick the richest one whose <i>worst</i> inhabitant the party can still
/// answer. The worst rather than the average, because a party is killed by the strongest thing it meets
/// and not by the mean of what lives there.
/// </para>
///
/// <para>
/// <b>And the strength is the party's, not the leader's.</b> Asked of the bodies that would actually be
/// called up — the fighters standing within reach of the muster, best first, as many as would go down —
/// because a leader is a crafter and a crafter's own strength says nothing about what its band can take.
/// </para>
///
/// <para>
/// <b>Every refusal has a name.</b> A delve needs a maker, one not already leading a company, one fit to
/// walk into a cave, a surveyed dungeon and a band strong enough for one. Which of those was missing is
/// the only question worth being able to answer on an evening when nobody went anywhere.
/// </para>
/// </summary>
public sealed class BotDelver : IBotProposer
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotDelver));

    public static double Odds { get; set; } = 1.5;

    public static int Reach { get; set; } = 200;

    public static int HeadsPerParty { get; set; } = 12;

    public static int BetweenMs { get; set; } = 600000;

    public string Name => "Delver";

    public BotStanding Rung => BotStanding.Free;

    public static long Asked { get; private set; }

    public static long NotALeader { get; private set; }

    public static long Held { get; private set; }

    public static long Unfit { get; private set; }

    public static long Unsupplied { get; private set; }

    public static long Resting { get; private set; }

    public static long TooSoon { get; private set; }

    public static long Unsurveyed { get; private set; }

    public static long Outmatched { get; private set; }

    public static long Crowded { get; private set; }

    public static long Mouthless { get; private set; }

    public static long Offered { get; private set; }

    private static readonly Dictionary<string, long> _last = [];

    private static readonly Dictionary<string, long> _claims = [];

    private static bool _said;

    public BotDeed Propose(IBotWilful bot)
    {
        var body = bot?.Self;
        var map = body?.Map;

        if (map == null || map == Map.Internal || !body.Alive)
        {
            return null;
        }

        if (body is not BotMobile who || who.Guild is not Guild guild)
        {
            NotALeader++;

            return null;
        }

        Asked++;

        if (BotDelve.Resting(body))
        {
            Resting++;

            return null;
        }

        if (_last.TryGetValue(guild.Name, out var when) && Core.TickCount - (when + BetweenMs) < 0)
        {
            TooSoon++;

            return null;
        }

        if (!BotSquads.Running)
        {
            return null;
        }

        if (bot is not IBotSquadMember { Squad: null })
        {
            Held++;

            return null;
        }

        if (body.HitsMax <= 0 || body.Hits < body.HitsMax * BotHunter.FitAt)
        {
            Unfit++;

            return null;
        }

        if (!BotProvision.Fit(body, out _))
        {
            Unsupplied++;

            return null;
        }

        BotDungeon.Survey(map);

        Gather(map, body);

        var deep = Deepest();

        if (deep == null)
        {
            if (BotDungeon.Rooms == 0)
            {
                Unsurveyed++;
            }
            else if (Deepest(alone: false, full: true) != null)
            {
                Crowded++;
            }
            else
            {
                Outmatched++;

                var easiest = Easiest();

                if (easiest is { Worst: > 0 })
                {
                    var have = Strength(easiest);
                    var need = easiest.Worst * Odds;

                    if (have / need > _nearest)
                    {
                        _nearest = have / need;
                        _nearestSaid = $"{(body as BotMobile)?.Guild?.Name ?? body.Name} with {_band.Count} in reach, {have:F0} of the {need:F0} {easiest.Name} asks";
                    }
                }

                if (BotProving.Judges && Deepest(alone: false, full: true, formula: true) != null)
                {
                    HeldBack++;
                }
            }

            return null;
        }

        var least = BotDelve.Least(map, deep);

        if (_band.Count + 1 < least && Raisable(map, body, BotDelve.Reach) + 1 < least)
        {
            Unraisable++;

            return null;
        }

        if (BotDelve.CallingNear(body, BotDelve.Reach))
        {
            Answering++;

            return null;
        }

        BotDelve.Offering(body);

        Offered++;

        var strength = Strength(deep);

        Compare(body, deep, strength);
        Once(body, deep, strength);

        return new BotDelve(map, deep);
    }

    public static long Unraisable { get; private set; }

    public static long Answering { get; private set; }

    private static int Raisable(Map map, Mobile leader, int reach)
    {
        var count = 0;

        foreach (var mobile in map.GetMobilesInRange<Mobile>(leader.Location, reach))
        {
            if (mobile != leader && mobile is BotMobile other && Eligible(other) && !BotRegard.Hostile(leader, other))
            {
                count++;
            }
        }

        return count;
    }

    private static bool Eligible(BotMobile other) =>
        other is IBotAlly { AbleToFight: true } && other.Class is { } klass && klass.Role != BotRole.Producer && other.Squad == null
        && !BotDelveParty.Delving(other) && other.Resolve?.Deed is not { Alongside: true } && !BotProvision.Short(other);

    private static void Gather(Map map, Mobile leader)
    {
        _band.Clear();
        _leader = leader as BotMobile;

        foreach (var mobile in map.GetMobilesInRange<Mobile>(leader.Location, Reach))
        {
            if (mobile == leader || mobile is not BotMobile other)
            {
                continue;
            }

            if (other is not IBotAlly { AbleToFight: true } || other.Class is not { } klass)
            {
                continue;
            }

            if (klass.Role == BotRole.Producer || other.Squad != null || BotDelveParty.Delving(other))
            {
                continue;
            }

            if (other.Resolve?.Deed is { Alongside: true })
            {
                continue;
            }

            if (BotProvision.Short(other))
            {
                continue;
            }

            if (BotRegard.Hostile(leader, other))
            {
                continue;
            }

            _band.Add(other);
        }

        _band.Sort(
            (a, b) =>
            {
                var mine = (BotGuilds.Same(leader, b) ? 1 : 0).CompareTo(BotGuilds.Same(leader, a) ? 1 : 0);

                return mine != 0 ? mine : leader.GetDistanceToSqrt(a).CompareTo(leader.GetDistanceToSqrt(b));
            }
        );
    }

    private static readonly List<BotMobile> _band = [];

    private static BotMobile _leader;

    private static string _partyGuild;

    public static double WeakestShare { get; set; } = 0.2;

    public static long Weakest { get; private set; }

    private static bool Holds(BotMobile member, BotDungeon.Deep deep) =>
        member == null || deep == Easiest() || BotProving.Against(member, deep) >= deep.Worst * WeakestShare;

    private static double _nearest;

    private static string _nearestSaid = "no band refused yet";

    public static long Unproven { get; private set; }

    private static bool Proven(string guild, BotDungeon.Deep deep)
    {
        if (!BotProving.Judges || !BotProving.RoomGate || deep == Easiest() || BotProving.RoomCleared(guild, deep))
        {
            return true;
        }

        Unproven++;

        return false;
    }

    private static BotDungeon.Deep Easiest()
    {
        BotDungeon.Deep easiest = null;
        var all = BotDungeon.All;

        for (var i = 0; i < all.Count; i++)
        {
            if (all[i].Ready && (easiest == null || all[i].Worst < easiest.Worst))
            {
                easiest = all[i];
            }
        }

        return easiest;
    }

    private static readonly HashSet<int> _picked = [];

    private static double Strength(BotDungeon.Deep deep, bool formula = false)
    {
        if (deep == Easiest())
        {
            formula = true;
        }

        if (formula)
        {
            _might.Clear();

            for (var i = 0; i < _band.Count; i++)
            {
                _might.Add(BotThreat.Power(_band[i]));
            }

            _might.Sort(static (a, b) => b.CompareTo(a));

            var sum = 0.0;

            var top = Math.Min(_might.Count, Math.Max(1, Want(deep) - 1));

            for (var i = 0; i < top; i++)
            {
                sum += _might[i];
            }

            return sum;
        }

        var want = Math.Max(1, Want(deep) - 1);
        var total = 0.0;
        var taken = 0;

        if (!Proven((_leader?.Guild as Guild)?.Name, deep))
        {
            return 0.0;
        }

        if (_leader != null)
        {
            if (!Holds(_leader, deep))
            {
                Weakest++;

                return 0.0;
            }

            total += BotProving.Against(_leader, deep);
        }

        _picked.Clear();

        for (var i = 0; i < _band.Count && taken < Math.Min(BotDelve.Fighters, want); i++)
        {
            if (_band[i].Class?.Role == BotRole.Melee)
            {
                if (!Holds(_band[i], deep))
                {
                    Weakest++;

                    return 0.0;
                }

                total += BotProving.Against(_band[i], deep);
                taken++;
                _picked.Add(i);
            }
        }

        for (var i = 0; i < _band.Count && taken < want; i++)
        {
            if (_picked.Contains(i))
            {
                continue;
            }

            if (!Holds(_band[i], deep))
            {
                Weakest++;

                return 0.0;
            }

            total += BotProving.Against(_band[i], deep);
            taken++;
        }

        return total;
    }

    private static readonly List<double> _might = [];

    private static int Want(BotDungeon.Deep deep) => BotDelve.PartyFor(_leader?.Map, deep);

    private static bool Ringed(BotDungeon.Deep deep) =>
        BotProving.Judges && BotProving.RoomGate && deep != Easiest();

    private static bool Enough(BotDungeon.Deep deep, bool formula) =>
        !formula && Ringed(deep) ? Cored(deep) : Strength(deep, formula) >= deep.Worst * Odds;

    public static long Uncompanied { get; private set; }

    private static bool Cored(BotDungeon.Deep deep)
    {
        var guild = _leader?.Guild as Guild;

        if (!Proven(guild?.Name, deep))
        {
            return false;
        }

        if (_leader != null && !Holds(_leader, deep))
        {
            Weakest++;

            return false;
        }

        var need = RingCompany(_leader?.Map, guild, deep);
        var mates = _leader is { Class.Role: not BotRole.Producer } ? 1 : 0;

        for (var i = 0; i < _band.Count && mates < need; i++)
        {
            if (!BotGuilds.Same(_leader, _band[i]))
            {
                continue;
            }

            if (!Holds(_band[i], deep))
            {
                Weakest++;

                return false;
            }

            mates++;
        }

        if (mates < need)
        {
            Uncompanied++;

            return false;
        }

        return true;
    }

    public static int RingCompany(Map map, Guild guild, BotDungeon.Deep deep) =>
        guild == null ? BotDelve.PartyFor(map, deep) : Math.Min(BotDelve.PartyFor(map, deep), BotProving.Fighters(guild).Count);

    private static BotDungeon.Deep Deepest()
    {
        var empty = Deepest(alone: true);

        return empty ?? Deepest(alone: false);
    }

    private static BotDungeon.Deep Deepest(bool alone, bool full = false, bool formula = false)
    {
        BotDungeon.Deep best = null;

        var all = BotDungeon.All;

        for (var i = 0; i < all.Count; i++)
        {
            var deep = all[i];

            if (!deep.Ready || !Enough(deep, formula))
            {
                continue;
            }

            if (BotGates.Ready && _leader?.Map is { } map && !BotGates.Reaches(map, _leader.Location, deep))
            {
                Mouthless++;

                continue;
            }

            if (!full)
            {
                var parties = BotDelveParty.Inside(deep) + (Spoken(deep) ? 1 : 0);

                if (parties >= (alone ? 1 : Holds(deep)))
                {
                    continue;
                }
            }

            if (best == null || deep.Power > best.Power)
            {
                best = deep;
            }
        }

        return best;
    }

    public static long Taken { get; private set; }

    public static void Stamp(Mobile body, BotDungeon.Deep deep)
    {
        if (body is BotMobile { Guild: Guild guild })
        {
            _last[guild.Name] = Core.TickCount;
        }

        if (deep != null)
        {
            _claims[deep.Name] = Core.TickCount;
        }

        Taken++;
    }

    private static int Holds(BotDungeon.Deep deep) =>
        Math.Max(1, deep.Heads / Math.Max(1, HeadsPerParty));

    private static bool Spoken(BotDungeon.Deep deep) =>
        _claims.TryGetValue(deep.Name, out var when) && Core.TickCount - (when + BotDelve.MusterMs) < 0;

    public static long Rechosen { get; private set; }

    private static void Gather(BotSquad squad, Mobile leader)
    {
        _band.Clear();
        _leader = null;

        var members = squad.Members;

        for (var i = 0; i < members.Count; i++)
        {
            if (members[i]?.Self is BotMobile { Deleted: false, Alive: true } member)
            {
                _band.Add(member);
            }
        }

        if (leader is BotMobile { Deleted: false, Alive: true } head && !_band.Contains(head))
        {
            _band.Add(head);
        }

        _partyGuild = (leader?.Guild as Guild)?.Name;
        _raisedGuild = leader?.Guild as Guild;
        _raisedMap = leader?.Map;
    }

    public static double Worth(BotSquad squad, Mobile leader, BotDungeon.Deep deep)
    {
        if (squad == null || deep == null)
        {
            return 0.0;
        }

        Gather(squad, leader);

        return Party(deep);
    }

    public static BotDungeon.Deep Recheck(BotSquad squad, Mobile leader, BotDungeon.Deep planned, out double strength, out string shortfall)
    {
        strength = 0.0;
        shortfall = null;

        if (squad == null || planned == null || !BotProving.Judges)
        {
            return planned;
        }

        Gather(squad, leader);

        if (Fits(planned, out strength, out shortfall))
        {
            return planned;
        }

        BotDungeon.Deep best = null;
        BotDungeon.Deep weakest = null;
        var all = BotDungeon.All;

        for (var i = 0; i < all.Count; i++)
        {
            var deep = all[i];

            if (!deep.Ready)
            {
                continue;
            }

            if (weakest == null || deep.Worst < weakest.Worst)
            {
                weakest = deep;
            }

            if (Fits(deep, out _, out _) && (best == null || deep.Power > best.Power))
            {
                best = deep;
            }
        }

        var chosen = best ?? weakest ?? planned;

        if (chosen != planned)
        {
            Rechosen++;
        }

        return chosen;
    }

    private static bool Fits(BotDungeon.Deep deep, out double strength, out string shortfall)
    {
        strength = Party(deep);
        shortfall = null;

        if (!Ringed(deep))
        {
            if (strength >= deep.Worst * Odds)
            {
                return true;
            }

            shortfall = $"{deep.Worst * Odds:F0} wanted";

            return false;
        }

        if (strength <= 0.0)
        {
            shortfall = "its guild's company has not cleared the worst room lately, or one of it is worth under a fifth of the worst";

            return false;
        }

        var need = RingCompany(_raisedMap, _raisedGuild, deep);
        var mates = 0;

        for (var i = 0; i < _band.Count; i++)
        {
            if (_band[i].Guild is Guild guild && guild.Name == _partyGuild && _band[i].Class is { Role: not BotRole.Producer })
            {
                mates++;
            }
        }

        if (mates >= need)
        {
            return true;
        }

        shortfall = $"{mates} of its guild's own fighters stood up of the {need} its company cleared the worst room with";

        return false;
    }

    private static Guild _raisedGuild;

    private static Map _raisedMap;

    private static double Party(BotDungeon.Deep deep)
    {
        var total = 0.0;

        if (deep == Easiest())
        {
            for (var i = 0; i < _band.Count; i++)
            {
                total += BotThreat.Power(_band[i]);
            }

            return total;
        }

        if (!Proven(_partyGuild, deep))
        {
            return 0.0;
        }

        for (var i = 0; i < _band.Count; i++)
        {
            if (!Holds(_band[i], deep))
            {
                return 0.0;
            }

            total += BotProving.Against(_band[i], deep);
        }

        return total;
    }

    public static long Lifted { get; private set; }

    public static long Lowered { get; private set; }

    public static long HeldBack { get; private set; }

    private static int _compared;

    public static int SayDifferences { get; set; } = 8;

    private static void Compare(Mobile body, BotDungeon.Deep deep, double strength)
    {
        if (!BotProving.Judges)
        {
            return;
        }

        var proven = Deepest(alone: false, full: true);
        var formula = Deepest(alone: false, full: true, formula: true);
        var provenWorst = proven?.Worst ?? 0.0;
        var formulaWorst = formula?.Worst ?? 0.0;

        if (provenWorst > formulaWorst)
        {
            Lifted++;
        }
        else if (provenWorst < formulaWorst)
        {
            Lowered++;
        }
        else
        {
            return;
        }

        if (_compared++ >= SayDifferences)
        {
            return;
        }

        logger.Information(
            "Proving moved a delve offer: {Name}'s band of {Band} is {Proven} of strength against {Deep}'s worst by the proving ground and {Formula} by the old formula, so it can reach {Reach} where the formula said {Said}",
            body.Name,
            _band.Count,
            strength.ToString("F0"),
            deep.Name,
            Strength(deep, formula: true).ToString("F0"),
            proven?.Name ?? "nowhere",
            formula?.Name ?? "nowhere"
        );
    }

    private static void Once(Mobile body, BotDungeon.Deep deep, double strength)
    {
        if (_said)
        {
            return;
        }

        _said = true;

        logger.Information(
            "{Name} has been offered the first delve on this shard: {Deep}, worth {Power} a head and {Worst} at worst, against {Strength} of band",
            body.Name,
            deep.Name,
            deep.Power.ToString("F0"),
            deep.Worst.ToString("F0"),
            strength.ToString("F0")
        );
    }

    public static string Describe() =>
        Asked == 0
            ? $"no guild member has ever been offered a delve ({NotALeader} answers went to bots in no guild)"
            : $"{Asked} times a guild member was asked: {Offered} were offered a dungeon and {Taken} took one, {Held} were already leading a company, "
            + $"{Unfit} were too hurt, {Unsupplied} lacked their supplies, {Resting} came too soon after a call that failed, {TooSoon} too soon after their band's last delve, "
            + $"{Unsurveyed} found nothing surveyed, {Crowded} found every dungeon they could survive already full, {Unraisable} could not raise the party's floor within the call's reach, {Answering} left to a call already gathering near them, "
            + $"{Outmatched} found nothing their band could come back out of at ×{Odds:F2} the worst of it (the nearest came to {_nearest:P0} of it: {_nearestSaid}); "
            + $"judged by the proving ground: {Lifted} offers went deeper than the old formula would have sent them, {Lowered} less deep, "
            + $"{HeldBack} bands kept on the island that the formula would have sent down, {Rechosen} parties sent elsewhere once raised, "
            + $"{Weakest} dungeons closed to a band by its weakest member (under {WeakestShare:P0} of the worst), {Unproven} by its guild's company not having cleared the worst room lately, "
            + $"{Uncompanied} by fewer of that company in reach than cleared it (past the easiest the cleared room is the bar, not ×{Odds:F2} the worst); "
            + $"{BotDungeon.Describe()}; {BotDelve.Describe()}; {BotProvision.Describe()}";

    public static void Forget()
    {
        _said = false;
        Asked = 0;
        NotALeader = 0;
        Held = 0;
        Unfit = 0;
        Unsupplied = 0;
        Resting = 0;
        TooSoon = 0;
        Unsurveyed = 0;
        Outmatched = 0;
        Crowded = 0;
        Offered = 0;
        Taken = 0;
        Lifted = 0;
        Lowered = 0;
        HeldBack = 0;
        Rechosen = 0;
        Weakest = 0;
        Unproven = 0;
        Uncompanied = 0;
        _compared = 0;
        _band.Clear();
        _last.Clear();
        _claims.Clear();
        _might.Clear();

        BotDelve.Forget();
        BotDungeon.Forget();
        BotHalls.Forget();
    }
}
