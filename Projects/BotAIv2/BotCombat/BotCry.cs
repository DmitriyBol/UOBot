using System;
using System.Collections.Generic;
using Server.Logging;
using Server.Mobiles;

namespace Server.BotAI.V2;

/// <summary>
/// Somebody of ours is being killed and has said so out loud.
///
/// <para>
/// <b>This is the one thing in the project that is genuinely a message.</b> Everything else a company does is
/// arithmetic every member repeats for itself — stations, patches, shares — precisely so that nothing has to
/// be sent, received or kept in step. A cry cannot be arithmetic: what makes it urgent is that it happened at
/// a moment, to one bot, and nobody else can derive it from where they are standing. The squad's own note
/// allows exactly two such events, and names this as one of them.
/// </para>
///
/// <para>
/// <b>It is a fact, not a summons.</b> Nothing here sends a bot anywhere. A cry is posted, it expires on its
/// own, and whoever is free enough to care picks it up through the ordinary auction as a piece of work worth
/// more than digging — see <see cref="BotRescuer"/>. That matters because the first version's help system was
/// an order: a bot posted a call, the call found nobody able, it disbanded in the same tick, and the bot
/// posted it again dozens of times over. An offer that nobody takes is silence; an order that nobody can obey
/// is a loop.
/// </para>
/// </summary>
public static class BotCry
{
    public static int HoldsMs { get; set; } = 20000;

    public static int Carries { get; set; } = 60;

    private static readonly Dictionary<Serial, (Mobile Who, Mobile What, long Tick)> _cries = [];

    public static long Raised { get; private set; }

    public static long Answered { get; private set; }

    public static void Raise(Mobile who, Mobile what, bool dire = false)
    {
        if (who is not { Deleted: false, Alive: true } || what is not { Deleted: false, Alive: true })
        {
            return;
        }

        if (!_cries.ContainsKey(who.Serial))
        {
            Raised++;
        }

        _cries[who.Serial] = (who, what, Core.TickCount);

        BotReflex.Cried(who, what);

        if (Summons && who is BotMobile crier && (dire || crier.HitsMax > 0 && crier.Hits < crier.HitsMax * DireAt))
        {
            Ask(crier);
        }
    }

    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotCry));

    public static bool Summons { get; set; } = true;

    public static double DireAt { get; set; } = 0.7;

    public static int MostCalled { get; set; } = 3;

    public static int RecallMs { get; set; } = 5000;

    public static int MostRounds { get; set; } = 2;

    public static int HelperRestMs { get; set; } = 45000;

    public static int CarrierReach { get; set; } = 12;

    public static long CarriersSpared => _why[WhyCarrying];

    public static int Shortlist { get; set; } = 8;

    public static long Dire { get; private set; }

    public static long Called { get; private set; }

    public static long Callings { get; private set; }

    public static long DireCompany { get; private set; }

    public static long DireOutlaw { get; private set; }

    public static long DireBelow { get; private set; }

    public static long DireShunned { get; private set; }

    public static long DireNobody { get; private set; }

    public static long DireBeyond { get; private set; }

    public static long PassedHostile { get; private set; }

    public static long PassedOutlaw { get; private set; }

    private sealed class Calling
    {
        public long Tick;
        public int Rounds;
        public int Sent;
        public bool Pending;
    }

    private static readonly Dictionary<Serial, Calling> _callings = [];

    private static readonly List<Serial> _pending = [];

    private static readonly Dictionary<Serial, long> _helped = [];

    private static readonly List<(BotMobile Who, int Off)> _near = [];

    private static readonly List<BotMobile> _chosen = [];

    private static void Ask(BotMobile crier)
    {
        var now = Core.TickCount;

        if (!_callings.TryGetValue(crier.Serial, out var calling))
        {
            _callings[crier.Serial] = calling = new Calling();
        }
        else if (!calling.Pending && calling.Rounds > 0 && now - calling.Tick >= HoldsMs + RecallMs)
        {
            calling.Rounds = 0;
            calling.Sent = 0;
        }

        if (calling.Pending || calling.Sent > 0 || calling.Rounds >= MostRounds || calling.Rounds > 0 && now - calling.Tick < RecallMs)
        {
            return;
        }

        calling.Pending = true;
        _pending.Add(crier.Serial);
    }

    public static void Dispatch()
    {
        if (_pending.Count == 0)
        {
            return;
        }

        var now = Core.TickCount;

        for (var i = 0; i < _pending.Count; i++)
        {
            var serial = _pending[i];

            if (!_callings.TryGetValue(serial, out var calling))
            {
                continue;
            }

            calling.Pending = false;

            if (!_cries.TryGetValue(serial, out var cry)
                || cry.Who is not BotMobile { Deleted: false, Alive: true } who
                || cry.What is not BaseCreature { Deleted: false, Alive: true } foe)
            {
                continue;
            }

            calling.Tick = now;
            calling.Rounds++;

            try
            {
                calling.Sent += Summon(who, foe, calling.Rounds == 1, now);
            }
            catch (Exception e)
            {
                logger.Error(e, "Calling help for {Name} threw; the cry stands for whoever looks up", who.Name);
            }
        }

        _pending.Clear();

        if (_callings.Count > 1024)
        {
            _callings.Clear();
        }

        if (_helped.Count > 1024)
        {
            _helped.Clear();
        }

        if (_tended.Count > 1024)
        {
            _tended.Clear();
        }
    }

    private static int Summon(BotMobile who, BaseCreature foe, bool first, long now)
    {
        var map = who.Map;

        if (map == null || map == Map.Internal || foe.Map != map)
        {
            return 0;
        }

        if (first)
        {
            Dire++;
        }

        if (BotOutlaw.Outlaw(who) || BotOutlaw.Jailed(who) || who.Murderer)
        {
            Filed(first, 1);

            return 0;
        }

        if (who.Criminal && first)
        {
            DireCriminal++;
        }

        _near.Clear();
        _bandsSeen.Clear();
        _sparesSeen.Clear();

        BotSquad spare = null;
        var spareOff = int.MaxValue;

        var threat = BotThreat.ThreatAt(who, foe.Location, BotMobile.NoticeRange);

        BotMobile healer = null;
        var healerOff = int.MaxValue;
        BotSquad band = null;
        var bandOff = int.MaxValue;

        foreach (var m in map.GetMobilesInRange<BotMobile>(who.Location, BotRescuer.Reach))
        {
            if (m == who || m.Deleted || !m.Alive)
            {
                continue;
            }

            var off = Math.Max(Math.Abs(m.X - who.X), Math.Abs(m.Y - who.Y));
            var why = Unfree(m, who, now);

            if (why == WhyHealer && Healers && off < healerOff && (BotMend.Spell(m, who) >= 0 || BotMend.Cloth(m) > 0))
            {
                healer = m;
                healerOff = off;

                continue;
            }

            if (why == WhyCompany && Companies && m.Squad is { } theirs && Brings(theirs, m, who, foe, threat))
            {
                if (off < bandOff)
                {
                    band = theirs;
                    bandOff = off;
                }

                continue;
            }

            if (why == WhyCompany && BotSquad.Spares && off <= BotSquad.SpareReach && m.Squad is { } busy && !_sparesSeen.Contains(busy))
            {
                _sparesSeen.Add(busy);

                if (off < spareOff && busy.WellInHand())
                {
                    spare = busy;
                    spareOff = off;
                }
            }

            if (why != 0)
            {
                if (first)
                {
                    _why[why]++;
                }

                continue;
            }

            var at = _near.Count;

            var rank = BotTies.CryRank(m, who, off);

            while (at > 0 && BotTies.CryRank(_near[at - 1].Who, who, _near[at - 1].Off) > rank)
            {
                at--;
            }

            if (at >= Shortlist)
            {
                continue;
            }

            _near.Insert(at, (m, off));

            if (_near.Count > Shortlist)
            {
                _near.RemoveAt(_near.Count - 1);
            }
        }

        if (healer != null && who.HitsMax > 0 && who.Hits < who.HitsMax * DireAt && !BotDungeon.Under(who.Location)
            && (!_tended.TryGetValue(who.Serial, out var tended) || now - tended >= HelperRestMs))
        {
            var trains = BotMend.Spell(healer, who) >= 0 ? SkillName.Magery : SkillName.Healing;

            if (BotWill.Press(healer, new BotSalve(who, map, onSelf: false, trains), $"called to {who.Name} as a healer, {healerOff} tiles off", StackCalls))
            {
                _tended[who.Serial] = now;
                _helped[healer.Serial] = now;
                HealersCalled++;
            }
        }

        if (who.Squad is { } company && ReferenceEquals(company.Focus, foe))
        {
            Filed(first, 0);

            return 0;
        }

        if (BotDungeon.Under(foe.Location))
        {
            Filed(first, 2);

            return 0;
        }

        if (BotQuarry.Shunned(foe))
        {
            Filed(first, 3);

            return 0;
        }

        var ours = BotThreat.OurPower(who, BotMobile.NoticeRange);
        var need = threat / Math.Max(0.1, BotThreat.Tolerance) - ours;

        _chosen.Clear();

        var brought = 0.0;

        for (var i = 0; i < _near.Count && _chosen.Count < MostCalled; i++)
        {
            if (_chosen.Count >= 1 && brought >= need)
            {
                break;
            }

            var m = _near[i].Who;

            if (BotThreat.Decide(m, BotMobile.NoticeRange) == BotStand.Outmatched)
            {
                if (first)
                {
                    _why[WhyFighting]++;
                }

                continue;
            }

            _chosen.Add(m);

            if (_near[i].Off > BotMobile.NoticeRange)
            {
                brought += BotThreat.Power(m);
            }
        }

        var bring = band != null && (_chosen.Count == 0 || brought < need);

        if (bring)
        {
            brought += band.Able();
        }

        if (_chosen.Count == 0 && !bring && spare != null)
        {
            var spared = spare.Spare(who, foe, now);

            if (spared > 0)
            {
                Callings++;

                return spared;
            }
        }

        if (_chosen.Count == 0 && !bring)
        {
            Filed(first, 4);

            return 0;
        }

        if (brought < need)
        {
            Filed(first, 5);

            return 0;
        }

        var sent = 0;

        if (bring)
        {
            band.Engage(foe, band.Leader);
            CompaniesBrought++;
            sent++;

            logger.Information(
                "Squad {Id} of {Count} is brought to {Name}'s aid against {Foe}: no enemy of its own, its leader {Off} tiles off",
                band.Id,
                band.Count,
                who.Name,
                foe.Name,
                bandOff
            );
        }

        for (var i = 0; i < _chosen.Count; i++)
        {
            var m = _chosen[i];
            var trains = m.Bond?.Weapon?.Skill ?? SkillName.Wrestling;
            var rescue = new BotRescue(new BotSlay(foe, trains), who, foe, own: false, rescuer: m);

            if (BotWill.Press(m, rescue, $"called to {who.Name}'s aid against {foe.Name}, {_near.Count} of ours free within {BotRescuer.Reach} and {_chosen.Count} sent", StackCalls))
            {
                _helped[m.Serial] = now;
                Called++;
                sent++;

                BotTies.Answered(m, who);
            }
        }

        if (sent > 0)
        {
            Callings++;
        }

        return sent;
    }

    public static bool Healers { get; set; } = true;

    public static bool Companies { get; set; } = true;

    public static bool StackCalls { get; set; } = true;

    public static long HealersCalled { get; private set; }

    public static long CompaniesBrought { get; private set; }

    private static readonly Dictionary<Serial, long> _tended = [];

    private const int WhyCompany = 1;
    private const int WhyResting = 2;
    private const int WhyUnfit = 3;
    private const int WhyFighting = 4;
    private const int WhyCommitted = 5;
    private const int WhyNoReturn = 6;
    private const int WhyPutDown = 7;
    private const int WhyLately = 8;
    private const int WhyBelow = 9;
    private const int WhyHostile = 10;
    private const int WhyNovice = 11;
    private const int WhyHealer = 12;
    private const int WhyCarrying = 13;

    private static readonly string[] Whys =
    [
        "", "in a company", "played out or of a class that takes no offers", "under the fitness bar", "in a fight, a flight or a mending, or outmatched where it stood",
        "on work that may not be bid away", "on work that is not taken up again", "on work already over two put down", "called within the rest",
        "underground", "of a hostile guild", "novices", "healers with no means or not the nearest",
        "carrying another bot's goods further off than the carrier's reach"
    ];

    private static readonly long[] _why = new long[14];

    private static bool Brings(BotSquad squad, BotMobile m, BotMobile who, BaseCreature foe, double threat)
    {
        if (squad.Disbanded || _bandsSeen.Contains(squad))
        {
            return false;
        }

        _bandsSeen.Add(squad);

        if (squad.Leader?.Self is not BotMobile { Deleted: false, Alive: true } lead)
        {
            _whyBand[1]++;

            return false;
        }

        if (squad.Warring)
        {
            _whyBand[2]++;

            return false;
        }

        if (squad.Focus is { Deleted: false, Alive: true } focus && BotThreat.Power(focus) * BotSquads.Switch > threat)
        {
            _whyBand[3]++;

            return false;
        }

        var reaches = lead.InRange(foe.Location, BotSquad.AnswerReach)
                      || MemberReach && squad.Focus is not { Deleted: false, Alive: true } && m.InRange(foe.Location, BotSquad.AnswerReach);

        if (!reaches)
        {
            _whyBand[4]++;

            return false;
        }

        if (BotDungeon.Under(lead.Location) || BotRegard.Hostile(lead, who))
        {
            _whyBand[5]++;

            return false;
        }

        if (!BotSquad.Caster(foe) && (squad.Beyond(foe) || BotSquad.Shuns(foe, squad.Able())))
        {
            _whyBand[6]++;

            return false;
        }

        return true;
    }

    public static int CommittedReach { get; set; } = 20;

    private static readonly Dictionary<string, long> _committed = [];

    private static string DescribeCommitted()
    {
        var say = "";

        foreach (var (kind, n) in _committed)
        {
            say += $"{(say.Length > 0 ? ", " : "")}{kind} {n}";
        }

        return say.Length == 0 ? "none" : say;
    }

    public static bool MemberReach { get; set; } = true;

    private static readonly HashSet<BotSquad> _bandsSeen = [];

    private static readonly HashSet<BotSquad> _sparesSeen = [];

    private static readonly long[] _whyBand = new long[7];

    private static readonly string[] WhyBands =
        ["", "no leader standing", "at war", "in a fight of their own against something not weaker", "the leader beyond answering reach of the thing", "the leader underground or hostile", "the thing beyond them"];

    private static string DescribeBands()
    {
        var say = "";

        for (var i = 1; i < _whyBand.Length; i++)
        {
            if (_whyBand[i] > 0)
            {
                say += $"{(say.Length > 0 ? ", " : "")}{_whyBand[i]} {WhyBands[i]}";
            }
        }

        return say.Length == 0 ? "none" : say;
    }

    public static long DireCriminal { get; private set; }

    public static long PassedCriminal { get; private set; }

    private static void Filed(bool first, int what)
    {
        if (!first)
        {
            return;
        }

        switch (what)
        {
            case 0:
                DireCompany++;
                break;
            case 1:
                DireOutlaw++;
                break;
            case 2:
                DireBelow++;
                break;
            case 3:
                DireShunned++;
                break;
            case 4:
                DireNobody++;
                break;
            default:
                DireBeyond++;
                break;
        }
    }

    private static int Unfree(BotMobile m, BotMobile who, long now)
    {
        if (m.Squad != null)
        {
            return WhyCompany;
        }

        if (m.Tired || m.Class is not { Bidding: true } klass)
        {
            return WhyResting;
        }

        if (m.HitsMax <= 0 || m.Hits < m.HitsMax * BotRescuer.FitAt)
        {
            return WhyUnfit;
        }

        if (m.Resolve is not { } resolve || BotLadder.Hunted(resolve))
        {
            return WhyFighting;
        }

        var deed = resolve.Deed;

        if (deed != null)
        {
            if (deed.Foe != null || deed is BotBolt || deed is BotSalve || deed is BotBrawl || deed is BotAccompany)
            {
                return WhyFighting;
            }

            if (deed.Committed)
            {
                _committed[deed.Kind] = _committed.GetValueOrDefault(deed.Kind) + 1;

                if (!(CommittedReach > 0 && (deed is BotChat || deed is BotEnvoy || deed is BotStandGuard) && m.InRange(who.Location, CommittedReach)))
                {
                    return WhyCommitted;
                }
            }

            if (!deed.Resumes)
            {
                return WhyNoReturn;
            }

            if ((deed is BotDeliver || deed is BotHaul) && !m.InRange(who.Location, CarrierReach))
            {
                return WhyCarrying;
            }

            if (resolve.Paused != null && (!StackCalls || resolve.Under != null))
            {
                return WhyPutDown;
            }
        }

        if (_helped.TryGetValue(m.Serial, out var called) && now - called < HelperRestMs)
        {
            return WhyLately;
        }

        if (BotDungeon.Under(m.Location))
        {
            return WhyBelow;
        }

        if (BotRegard.Hostile(m, who))
        {
            return WhyHostile;
        }

        if (klass.Role == BotRole.Medic)
        {
            return WhyHealer;
        }

        return BotLadder.Novice(m) ? WhyNovice : 0;
    }

    private static string DescribeWhy()
    {
        var say = "";

        for (var i = 1; i < _why.Length; i++)
        {
            if (_why[i] > 0)
            {
                say += $"{(say.Length > 0 ? ", " : "")}{_why[i]} {Whys[i]}";
            }
        }

        return say.Length == 0 ? "none" : say;
    }

    public static string DescribeCalls() =>
        $"{Dire} cries turned dire (the crier under {DireAt:P0} of its health, or refused its fight for the odds): {Callings} called {Called} of ours to a rescue at once (work in hand put down, never dropped), "
        + $"{DireCompany} were a member's whose company already had the thing for its enemy, {DireNobody} had nobody free, fit and friendly within {BotRescuer.Reach} tiles (bots near a dire cry not free, by why: {DescribeWhy()}), {CompaniesBrought} had a company brought by its leader (companies near not brought, by why: {DescribeBands()}), {DireCriminal} wore the engine's criminal flag and were helped all the same, committed work near a dire cry by kind: {DescribeCommitted()}; {BotSquad.DescribeSpares()}, {HealersCalled} healers pressed to a hurt crier's mending, {DireBeyond} were beyond what those near could have brought together, "
        + $"{DireBelow} underground, {DireShunned} about a creature no road reaches, {DireOutlaw} from a criminal or a murderer; a free bot's own look passed over {PassedHostile} criers of a guild hostile to it and {PassedOutlaw} outlaws (red, wanted or jailed), and did not pass over {PassedCriminal} wearing the engine's criminal flag";

    public static bool Crying(Mobile who) =>
        who != null && _cries.TryGetValue(who.Serial, out var cry) && Core.TickCount - cry.Tick < HoldsMs;

    public static void Quiet(Mobile who)
    {
        if (who != null)
        {
            _cries.Remove(who.Serial);
        }
    }

    public static (Mobile Who, BaseCreature What) Nearest(Mobile helper, int range)
    {
        if (helper?.Map is not { } map || map == Map.Internal)
        {
            return (null, null);
        }

        Mobile found = null;
        BaseCreature onThem = null;
        var closest = int.MaxValue;

        List<Serial> stale = null;

        foreach (var (serial, cry) in _cries)
        {
            var (who, what, tick) = cry;

            if (Core.TickCount - tick >= HoldsMs
                || who is not { Deleted: false, Alive: true }
                || what is not BaseCreature { Deleted: false, Alive: true } creature)
            {
                (stale ??= []).Add(serial);

                continue;
            }

            if (who == helper || who.Map != map)
            {
                continue;
            }

            if (BotOutlaw.Outlaw(who) || BotOutlaw.Jailed(who) || who.Murderer)
            {
                PassedOutlaw++;

                continue;
            }

            if (who.Criminal)
            {
                PassedCriminal++;
            }

            if (BotRegard.Hostile(helper, who))
            {
                PassedHostile++;

                continue;
            }

            var away = (int)helper.GetDistanceToSqrt(who.Location);

            if (away > range || away >= closest)
            {
                continue;
            }

            closest = away;
            found = who;
            onThem = creature;
        }

        if (stale != null)
        {
            for (var i = 0; i < stale.Count; i++)
            {
                _cries.Remove(stale[i]);
            }
        }

        return (found, onThem);
    }

    public static void Noted() => Answered++;

    public static long ReactionMs { get; private set; }

    public static long Reactions { get; private set; }

    public static void Answering(Mobile who)
    {
        if (who == null || !_cries.TryGetValue(who.Serial, out var cry))
        {
            return;
        }

        ReactionMs += Core.TickCount - cry.Tick;
        Reactions++;

        _cries[who.Serial] = (cry.Who, cry.What, long.MinValue / 2);
    }

    public static string Describe() =>
        Raised == 0
            ? "nobody has called for help"
            : $"{Raised} cried for help, {Answered} were gone to, the first helper setting out {(Reactions > 0 ? ReactionMs / Reactions / 1000.0 : 0.0):F1}s after the cry on average over {Reactions} (rescues offered and the cry's last renewal, not rescues taken nor the wait: Reflexes: times the first cry of a bout to the first rescue taken); {DescribeCalls()}; {BotDefender.Already} blows not answered with a second errand because the work in hand was already that fight, {BotDefender.Outnumbered} not answered with a fight the odds round the bot already called off, {BotDefender.Leading} on a company's leader left to the company, {BotDefender.Below} fights underground left to the delve's company, {BotDefender.ToTown} answered with a run for a town by a bot outmatched where it stood";

    public static void Forget()
    {
        _cries.Clear();
        Raised = 0;
        Answered = 0;
        ReactionMs = 0;
        Reactions = 0;

        _callings.Clear();
        _pending.Clear();
        _helped.Clear();
        _tended.Clear();
        System.Array.Clear(_why);
        System.Array.Clear(_whyBand);
        _committed.Clear();
        _sparesSeen.Clear();
        _bandsSeen.Clear();
        DireCriminal = 0;
        PassedCriminal = 0;
        HealersCalled = 0;
        CompaniesBrought = 0;
        Dire = 0;
        Called = 0;
        Callings = 0;
        DireCompany = 0;
        DireOutlaw = 0;
        DireBelow = 0;
        DireShunned = 0;
        DireNobody = 0;
        DireBeyond = 0;
        PassedHostile = 0;
        PassedOutlaw = 0;

        BotReflex.Forget();
    }
}
