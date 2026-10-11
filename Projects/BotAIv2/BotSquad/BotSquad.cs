using System;
using System.Collections.Generic;
using Server.Items;
using Server.Logging;
using Server.Mobiles;
using Server.Targeting;
using Server.Text;

namespace Server.BotAI.V2;

/// <summary>What a squad is doing. Three states, and none of them is "stand and wait".</summary>
public enum BotSquadStance
{
    Marching,

    Scouting,

    Fighting
}

/// <summary>
/// A standing company of bots: a leader, a few followers, a formation and a share-out.
///
/// <para>
/// <b>One kind of group, not two.</b> The first version had a <em>warband</em> — spontaneous, formed the
/// moment a bot met something it could not take alone — and separately a <em>band</em> raised once a minute
/// by the system, exactly five strong, a medic compulsory, aimed at a square of the map. Both were called
/// "the group", including in the documentation, and the two answered different questions with different
/// lifetimes. This is one thing: it forms, it lives, it walks, it scouts, it fights, it splits what it took,
/// and it goes on.
/// </para>
///
/// <para>
/// <b>No state here may be "stand and wait", and that is a rule paid for in bodies.</b> The first version's
/// mustering and loot-settling states both opened by setting <c>Warmode = false</c> and standing still — and
/// a rally point was almost always inside assembly range, so "go and assemble" degenerated into "stand and
/// look at the enemy". A lich strikes from eight tiles and never closes, so not one rung of the survival
/// ladder ever fired: six bots waited politely in a ring while it killed them one at a time. Every state
/// below is a state in which the bot is going somewhere.
/// </para>
///
/// <para>
/// <b>The collective mind is arithmetic, not messages.</b> Stations, scouting patches and shares are all
/// derived by every member from the same facts in the same order. What passes between bots is only what is
/// genuinely an event: <em>I am being attacked</em>, and <em>get off this tile</em>.
/// </para>
/// </summary>
public sealed partial class BotSquad
{
    public static long Sundered { get; private set; }

    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotSquad));

    public static int NoProgressMs => SlowestBlowMs * Blows;

    public static int SlowestBlowMs { get; set; } = 6000;

    public static int Blows { get; set; } = 3;

    public static int FightCapMs { get; set; } = 240000;

    public static int MaxSize { get; set; } = 20;

    public static int Usual { get; set; } = 5;

    public static double CallOdds { get; set; } = 2.0;

    public static int IdleCapMs { get; set; } = 8000;

    public static int HuntEveryMs { get; set; } = 3000;

    public static int PressReach { get; set; } = 12;

    public const int ReformDistance = 2;

    public static int RestationMs { get; set; } = 1500;

    public static int RotateAfterMs => Math.Max(RestationMs, SlowestBlowMs);

    private readonly List<IBotSquadMember> _members = [];

    private long _stationedTick;

    private int _focusLowest;

    private long _focusProgressTick;

    private long _focusSinceTick;

    private readonly HashSet<Serial> _answered = [];

    private string _engagedBy;

    private double _focusPower;

    private readonly Dictionary<Type, (long Tick, double Able, int Members)> _fought = [];

    private Mobile _letAt;

    private Mobile _stoodTo;

    private Point3D _stationedAt;

    private BotSquadStance _stationedStance;

    private int _stationedCount;

    private Point3D _fell;

    private long _quietTick;

    private long _huntedTick;

    private bool _quiet;

    public BotSquad(int id, IBotSquadMember leader)
    {
        Id = id;
        Leader = leader;
        Stance = BotSquadStance.Marching;

        _stationedTick = Core.TickCount;
    }

    public int Id { get; }

    public IBotSquadMember Leader { get; private set; }

    public IReadOnlyList<IBotSquadMember> Members => _members;

    public IBotSquadMember Straggler(int spread, out int distance)
    {
        distance = 0;
        var lead = Leader?.Self;

        if (lead == null)
        {
            return null;
        }

        IBotSquadMember worst = null;

        for (var i = 0; i < _members.Count; i++)
        {
            var member = _members[i];

            if (ReferenceEquals(member, Leader) || member?.Self is not { Deleted: false, Alive: true } self || self.Map != lead.Map)
            {
                continue;
            }

            var at = Math.Max(Math.Abs(self.X - lead.X), Math.Abs(self.Y - lead.Y));

            if (at > spread && at > distance)
            {
                distance = at;
                worst = member;
            }
        }

        return worst;
    }

    public static bool Coming(IBotSquadMember straggler, int behind) =>
        behind <= MarchFar && straggler?.Journey is { Moving: true } && straggler.Self is { Deleted: false, Alive: true };

    public static bool MarchUnstationed { get; set; } = true;

    public static int MarchFar { get; set; } = 200;

    public bool OnTheMarch { get; set; }

    public static int LagBeyond { get; set; } = 6;

    public static int PacedSteps { get; set; } = 1;

    public bool Lagging()
    {
        if (!OnTheMarch || Leader?.Self is not { Deleted: false, Alive: true } lead)
        {
            return false;
        }

        for (var i = 0; i < _members.Count; i++)
        {
            var member = _members[i];

            if (ReferenceEquals(member, Leader) || member.Self is not { Deleted: false, Alive: true } body || body.Map != lead.Map)
            {
                continue;
            }

            if (Math.Max(Math.Abs(body.X - lead.X), Math.Abs(body.Y - lead.Y)) <= BotEnlist.CloseUp + LagBeyond)
            {
                continue;
            }

            if ((member as IBotWilful)?.Resolve?.Deed is not BotEnlist)
            {
                UnpacedFor[(member as IBotWilful)?.Resolve?.Deed?.Kind ?? "nothing"] =
                    UnpacedFor.TryGetValue((member as IBotWilful)?.Resolve?.Deed?.Kind ?? "nothing", out var had) ? had + 1 : 1;

                continue;
            }

            Paced++;

            return true;
        }

        return false;
    }

    public static readonly Dictionary<string, long> UnpacedFor = [];

    public static string Unpaced()
    {
        if (UnpacedFor.Count == 0)
        {
            return "none";
        }

        var say = ValueStringBuilder.Create(128);

        try
        {
            foreach (var (kind, n) in UnpacedFor)
            {
                if (say.Length > 0)
                {
                    say.Append(", ");
                }

                say.Append($"{kind} {n}");
            }

            return say.ToString();
        }
        finally
        {
            say.Dispose();
        }
    }

    public static long Paced { get; private set; }

    public static long RoadSpared { get; private set; }

    public static int MarchHuntReach { get; set; } = 16;

    public static long Defended { get; private set; }

    public static long Excused { get; private set; }

    public bool Has(Mobile who)
    {
        if (who == null)
        {
            return false;
        }

        for (var i = 0; i < _members.Count; i++)
        {
            if (ReferenceEquals(_members[i].Self, who))
            {
                return true;
            }
        }

        return false;
    }

    public int Count => _members.Count;

    public long Won { get; set; }

    public BotSquadStance Stance { get; private set; }

    public bool Disbanded { get; private set; }

    internal void Bury() => Disbanded = true;

    public bool Charged { get; set; }

    public bool Warring { get; set; }

    public int Ceiling { get; set; } = MaxSize;

    public Func<Mobile, BaseCreature> Quarry { get; set; }

    public Mobile Focus { get; private set; }

    public IBotSquadMember Contact { get; private set; }

    public Map Map => Leader?.Self?.Map;

    public Point3D Anchor
    {
        get
        {
            if (Mustering)
            {
                return _rally;
            }

            if (Stance == BotSquadStance.Fighting && Focus is { Deleted: false })
            {
                return Focus.Location;
            }

            var on = Contact?.Self is { Deleted: false } && Stance == BotSquadStance.Fighting ? Contact : Leader;

            return on?.Self?.Location ?? Point3D.Zero;
        }
    }

    public int Attempt { get; private set; }

    public (int X, int Y) Axis
    {
        get
        {
            if (Mustering && Focus is { Deleted: false })
            {
                var towards = Unit(_rally, Focus.Location);

                if (towards != (0, 0))
                {
                    return towards;
                }
            }

            if (Stance == BotSquadStance.Fighting && Focus is { Deleted: false })
            {
                var side = (Contact?.Self is { Deleted: false } ? Contact : Leader)?.Self;

                var offset = side == null ? (0, 0) : Unit(Focus.Location, side.Location);

                if (offset != (0, 0))
                {
                    return offset;
                }
            }
            else if (Focus is { Deleted: false })
            {
                var offset = Unit(Anchor, Focus.Location);

                if (offset != (0, 0))
                {
                    return offset;
                }
            }

            var leader = Leader?.Self;

            return leader == null ? (0, -1) : Facing(leader.Direction);
        }
    }

    internal void Attach(IBotSquadMember member)
    {
        if (member != null && !_members.Contains(member))
        {
            _members.Add(member);
            Roster++;
        }
    }

    internal bool Detach(IBotSquadMember member)
    {
        if (!_members.Remove(member))
        {
            return false;
        }

        Roster++;

        if (member?.Self is BotMobile { Deleted: false } dead && (!dead.Alive || dead.Fallen))
        {
            KeepFallen(dead);

            BotUndertaker.FellIn(dead, this);
        }

        if (member?.Self is { } body && _dying.Remove(body.Serial, out var fell) && (!body.Alive || body is BotMobile { Fallen: true }))
        {
            if (fell.Helped)
            {
                DiedHelped++;
            }
            else
            {
                DiedUnhelped++;
            }
        }

        return true;
    }

    public int Roster { get; private set; }

    internal void Promote(IBotSquadMember member) => Leader = member;

    public bool Holds(IBotSquadMember member) => _members.Contains(member);

    public void Engage(Mobile focus, IBotSquadMember contact, bool chosen = false)
    {
        if (focus is not { Deleted: false, Alive: true } || Has(focus))
        {
            return;
        }

        if (ReferenceEquals(Focus, focus))
        {
            Contact = contact ?? Contact;

            return;
        }

        if (Bleeding(focus))
        {
            BledRefused++;

            return;
        }

        if (Mustering)
        {
            MusterStruck++;
            Mustering = false;
        }

        if (chosen && WantsMuster(focus))
        {
            BeginMuster(focus, contact);

            return;
        }

        if (chosen && Musters)
        {
            MusterUnneeded++;
        }

        Begin(focus, contact);
    }

    private void Begin(Mobile focus, IBotSquadMember contact)
    {
        Tally();

        if (Focus is { Deleted: false, Alive: true } && !ReferenceEquals(Focus, focus))
        {
            Switched++;

            if (Core.TickCount - _focusSinceTick < FocusHoldMs)
            {
                SwitchedSoon++;
            }
        }

        Focus = focus;
        Contact = contact ?? Leader;
        Stance = BotSquadStance.Fighting;

        BotReflex.CompanyOn(focus);

        BotSquads.Hurry(this);
        Attempt = 0;
        _focusLowest = focus.Hits;
        _focusProgressTick = Core.TickCount;
        _focusSinceTick = Core.TickCount;
        _answered.Clear();
        _engagedBy = (Leader as IBotWilful)?.Resolve?.Deed?.Kind ?? "nothing";
        _focusPower = BotThreat.Power(focus);
        Fought(focus, Able());
        _ableTick = Core.TickCount;
        _closedTick = Core.TickCount;
        _nearestSeen = int.MaxValue;
        Engaged++;

        logger.Information(
            "Squad {Id} of {Count} is dealing with {What} ({Power:F0}), reckoned {Reckoned:F0} against the {Ours:F0} the company brings",
            Id,
            Count,
            focus.Name,
            BotThreat.Power(focus),
            Reckon(focus),
            Able()
        );
    }

    public bool Unreachable(Mobile foe)
    {
        if (foe == null || !ReferenceEquals(Focus, foe))
        {
            return Focus == null;
        }

        for (var i = 0; i < _members.Count; i++)
        {
            if (_members[i]?.Self is { Deleted: false, Alive: true } self && self.Map == foe.Map && self.InRange(foe.Location, PressReach))
            {
                return false;
            }
        }

        UnreachedLet++;
        Disengage("no road any of us drew reached it", true);

        return true;
    }

    public static long UnreachedLet { get; private set; }

    public void Disengage(string why, bool hopeless = false)
    {
        if (Focus == null)
        {
            return;
        }

        if (hopeless)
        {
            var reach = InReach(out var nearest, out var blind, out var refused, out var unsteady);

            logger.Information(
                "Squad {Id} broke off from {What}: {Why} — {Reach} of {Count} able to strike ({Blind} with no line to it, {Refused} refused by the engine, {Unsteady} moved too recently to shoot), nearest {Nearest} tiles off, {Tries} tries at standing right",
                Id,
                Focus.Name,
                why,
                reach,
                Count,
                blind,
                refused,
                unsteady,
                nearest,
                Attempt
            );
        }
        else
        {
            logger.Information("Squad {Id} broke off from {What}: {Why}", Id, Focus.Name, why);
        }

        if (hopeless)
        {
            BotQuarry.Shun(Focus, BotQuarry.HopelessMs);

            Broken = Focus;
            BrokenWhy = why;
        }

        Tally();

        if (Focus != null && _fought.TryGetValue(Focus.GetType(), out var fought))
        {
            _fought[Focus.GetType()] = (Core.TickCount, fought.Able, fought.Members);
        }

        Focus = null;
        Contact = null;
        Attempt = 0;
        Mustering = false;
        Stance = BotSquadStance.Marching;
    }

    private void Fought(Mobile focus, double able)
    {
        if (focus is not BaseCreature)
        {
            return;
        }

        if (_fought.Count >= 64)
        {
            _fought.Clear();
        }

        _fought[focus.GetType()] = (Core.TickCount, able, Count);
    }

    internal void Fell(Mobile killer)
    {
        Lost(killer);

        if (killer is not BaseCreature { Controlled: false, Summoned: false } creature)
        {
            return;
        }

        var kind = creature.GetType();

        if (_fought.TryGetValue(kind, out var fought) && fought.Members >= 2
            && (Focus?.GetType() == kind || Core.TickCount - fought.Tick < BotCompanyRecord.FightMs))
        {
            BotCompanyRecord.Fell(kind, fought.Able / Math.Max(1.0, BotThreat.Power(creature)));
        }
        else if (Count >= 2)
        {
            BotCompanyRecord.FellElsewhere();
        }
    }

    /// <summary>
    /// What stops this member landing a blow on the focus from exactly where it stands, if anything.
    ///
    /// <para>
    /// <b>One rule, asked from the three places that were each answering it their own way.</b> The press set
    /// a combatant on anybody inside twelve tiles, the break-off line counted anybody inside its own ring,
    /// and the stationing asked neither — so a company could report itself fully engaged while not one arrow
    /// left it. Every one of the four refusals below is the engine's, taken from the engine, and each has a
    /// different cure: distance wants a walk, a broken line wants another tile, a refusal wants nothing at
    /// all, and stillness wants to be left alone.
    /// </para>
    /// </summary>
    private enum BotBlow
    {
        Far,

        Blind,

        Refused,

        Unsteady,

        Able
    }

    private static int Reach(IBotSquadMember member, Mobile body)
    {
        var role = BotFormation.RoleOf(member);
        var arm = body.Weapon?.MaxRange ?? 1;

        if (role is BotRole.Caster or BotRole.Medic)
        {
            arm = Math.Max(arm, BotStrike.Range);
        }

        return Math.Max(BotFormation.PressRingFor(role), arm);
    }

    public static long NearLeader { get; private set; }

    private Mobile NearestToLeader(Mobile body)
    {
        var lead = Leader?.Self;

        if (lead is not { Deleted: false, Alive: true } || lead.Map != body.Map)
        {
            return null;
        }

        Mobile best = null;
        var bestRange = double.MaxValue;

        foreach (var creature in lead.Map.GetMobilesInRange<BaseCreature>(lead.Location, PressReach))
        {
            if (creature is not { Deleted: false, Alive: true } || !BotThreat.Menacing(body, creature) || !body.InLOS(creature)
                || !body.CanBeHarmful(creature, false) || body.IsHarmfulCriminal(creature))
            {
                continue;
            }

            var range = lead.GetDistanceToSqrt(creature);

            if (range < bestRange)
            {
                bestRange = range;
                best = creature;
            }
        }

        return best;
    }

    private BotBlow Strike(IBotSquadMember member, out int away)
    {
        away = int.MaxValue;

        var focus = Focus;
        var body = member?.Self;

        if (body is not { Deleted: false, Alive: true } || focus is not { Deleted: false } || body.Map != focus.Map)
        {
            return BotBlow.Far;
        }

        away = Math.Max(
            Math.Abs(body.Location.X - focus.Location.X),
            Math.Abs(body.Location.Y - focus.Location.Y)
        );

        if (away > Reach(member, body))
        {
            return BotBlow.Far;
        }

        if (!body.InLOS(focus))
        {
            return BotBlow.Blind;
        }

        if (!body.CanBeHarmful(focus, false) || body.IsHarmfulCriminal(focus))
        {
            return BotBlow.Refused;
        }

        if (body.Weapon is BaseRanged && Core.TickCount - body.LastMoveTime < BotSlay.StillMs)
        {
            return BotBlow.Unsteady;
        }

        return BotBlow.Able;
    }

    private int InReach(out int nearest, out int blind, out int refused, out int unsteady)
    {
        nearest = int.MaxValue;
        blind = 0;
        refused = 0;
        unsteady = 0;

        var reach = 0;

        for (var i = 0; i < _members.Count; i++)
        {
            var verdict = Strike(_members[i], out var away);

            if (away < nearest)
            {
                nearest = away;
            }

            switch (verdict)
            {
                case BotBlow.Blind:
                    blind++;

                    break;

                case BotBlow.Refused:
                    refused++;

                    break;

                case BotBlow.Unsteady:
                    unsteady++;

                    break;

                case BotBlow.Able:
                    reach++;

                    break;
            }
        }

        if (nearest == int.MaxValue)
        {
            nearest = -1;
        }

        return reach;
    }

    internal bool Due;

    internal void AnswerNow()
    {
        Due = false;

        if (Disbanded || _members.Count == 0)
        {
            return;
        }

        if (Stance == BotSquadStance.Fighting && !Mustering)
        {
            Station();
            Press();
        }
        else if (Mustering)
        {
            Station();
            Tend();
        }
        else
        {
            Tend();
        }
    }

    internal string Update()
    {
        Prune();
        Release();

        if (_members.Count == 0)
        {
            return "the last of them is gone";
        }

        Raise();

        if (Charged && !Warring && Leader?.Self is BotMobile head && head.Resolve?.Deed is not { Alongside: true })
        {
            Charged = false;
            Unowned++;
        }

        if (_members.Count < 2 && !Charged && !RaiseUnderway)
        {
            return "one bot was left in it and nobody had charged it with anything";
        }

        if (Leader?.Self is not { Deleted: false, Alive: true } || !_members.Contains(Leader))
        {
            if (!Inherit())
            {
                return "it lost its leader and nobody in it could take over";
            }
        }

        if (Mustering)
        {
            MusterBeat();
        }

        if (Stance == BotSquadStance.Fighting && !Mustering)
        {
            Judge();
        }

        var withdrew = Overmatched();

        if (withdrew != null)
        {
            return withdrew;
        }

        Retarget();

        Settle();
        Station();
        Watch();

        if (Stance == BotSquadStance.Fighting && !Mustering)
        {
            Press();
        }
        else if (Mustering)
        {
            Tend();
        }
        else
        {
            Tend();

            if (!Recovering)
            {
                Hunt();
            }
        }

        return Quiet() ? null : "it went a while with nothing to fight and nobody holding it together";
    }

    private void Hunt()
    {
        var leader = Leader?.Self;

        if (leader is not { Deleted: false, Alive: true } || leader.Map != Map)
        {
            return;
        }

        var now = Core.TickCount;

        if (now - _huntedTick < HuntEveryMs)
        {
            return;
        }

        _huntedTick = now;

        if (OnTheMarch && (Leader as IBotWilful)?.Resolve?.Deed is BotDelve)
        {
            RoadSpared++;

            return;
        }

        var quarry = Quarry != null ? Quarry(leader) : BotQuarry.Company(leader, OnTheMarch ? MarchHuntReach : BotMuster.Reach, ChoiceOdds);

        if (quarry == null)
        {
            return;
        }

        if (Beyond(quarry) || Shuns(quarry, Able()) || Bleeding(quarry))
        {
            ShyOf(quarry);

            return;
        }

        Engage(quarry, Leader, true);
    }

    private void Retarget()
    {
        Gather();

        if (_attackers.Count == 0)
        {
            return;
        }

        var best = Strongest(out var power, out var called);

        if (best == null || ReferenceEquals(best, Focus) || Bleeding(best))
        {
            return;
        }

        var focus = Focus;

        if (focus is { Deleted: false, Alive: true } && focus.Map == Map)
        {
            var held = BotThreat.Power(focus);
            var need = _attackers.Contains(focus) ? BotSquads.Switch : TieShare;

            if (CastersFirst && Caster(best) && !Caster(focus))
            {
                need = TieShare;
            }

            if (power < held * need)
            {
                return;
            }

            if (Core.TickCount - _focusSinceTick < FocusHoldMs)
            {
                HeldFor++;

                return;
            }

            ToStronger++;
        }
        else
        {
            ToStrongest++;
        }

        if (called)
        {
            LeaderCalls++;
        }

        Engage(best, best.Combatant as IBotSquadMember ?? Leader);
    }

    internal Mobile StrongestAttacking(Mobile attacker)
    {
        Gather();

        if (Strongest(out _, out _) is { } best)
        {
            return best;
        }

        return attacker is { Deleted: false, Alive: true } && Leader?.Self is { Deleted: false } lead && attacker.Map == lead.Map
               && lead.InRange(attacker.Location, AnswerReach)
            ? attacker
            : null;
    }

    private bool Ours(Mobile body) => body is IBotSquadMember { Squad: { } squad } && ReferenceEquals(squad, this);

    private readonly List<Mobile> _attackers = [];

    private void Gather(bool all = false)
    {
        _attackers.Clear();

        var map = Map;
        var lead = Leader?.Self;

        if (map == null || map == Map.Internal || lead is not { Deleted: false })
        {
            return;
        }

        for (var i = 0; i < _members.Count; i++)
        {
            var body = _members[i].Self;

            if (body is not { Deleted: false, Alive: true } || body.Map != map)
            {
                continue;
            }

            var aggressors = body.Aggressors;

            for (var j = 0; j < aggressors.Count; j++)
            {
                Consider(body, lead, map, aggressors[j].Attacker, all);
            }

            var aggressed = body.Aggressed;

            for (var j = 0; j < aggressed.Count; j++)
            {
                Consider(body, lead, map, aggressed[j].Defender, all);
            }
        }
    }

    private void Consider(Mobile body, Mobile lead, Map map, Mobile a, bool all = false)
    {
        if (a is not { Deleted: false, Alive: true } || a.Map != map || !Ours(a.Combatant) || Ours(a) || _attackers.Contains(a))
        {
            return;
        }

        if (!body.InRange(a.Location, BotSquads.Reach) || !lead.InRange(a.Location, AnswerReach)
            || a is BotMobile && !BotRegard.AtWar(body, a) || !all && BotQuarry.Shunned(a))
        {
            return;
        }

        _attackers.Add(a);
    }

    private Mobile Strongest(out double power, out bool called)
    {
        Mobile best = null;
        power = 0.0;
        called = false;

        for (var i = 0; i < _attackers.Count; i++)
        {
            var p = BotThreat.Power(_attackers[i]);

            if (p > power)
            {
                power = p;
                best = _attackers[i];
            }
        }

        if (best != null && Leader?.Self?.Combatant is { } call && !ReferenceEquals(call, best) && _attackers.Contains(call))
        {
            var p = BotThreat.Power(call);

            if (p >= power * TieShare)
            {
                power = p;
                best = call;
                called = true;
            }
        }

        if (CastersFirst && best != null && !Caster(best))
        {
            Mobile caster = null;
            var strongest = 0.0;

            for (var i = 0; i < _attackers.Count; i++)
            {
                if (!Caster(_attackers[i]))
                {
                    continue;
                }

                var p = BotThreat.Power(_attackers[i]);

                if (p > strongest)
                {
                    strongest = p;
                    caster = _attackers[i];
                }
            }

            if (caster != null && strongest >= power * TieShare)
            {
                power = strongest;
                best = caster;
                called = false;
                CasterTaken++;
            }
        }

        return best;
    }

    private Mobile Seen(Mobile body)
    {
        Mobile best = null;
        var power = 0.0;

        for (var i = 0; i < _attackers.Count; i++)
        {
            var a = _attackers[i];

            if (ReferenceEquals(a, Focus) || a is not { Deleted: false, Alive: true } || a.Map != body.Map
                || !body.InRange(a.Location, PressReach))
            {
                continue;
            }

            var p = BotThreat.Power(a);

            if (p <= power || !body.InLOS(a) || !body.CanBeHarmful(a, false) || body.IsHarmfulCriminal(a))
            {
                continue;
            }

            power = p;
            best = a;
        }

        return best;
    }

    public static bool Withdraws { get; set; } = true;

    public static double WithdrawOdds { get; set; } = 1.5;

    public static int WithdrawRunMs { get; set; } = 60000;

    public static long Withdrawals { get; private set; }

    public static long WithdrewNear { get; private set; }

    public static long WithdrewFar { get; private set; }

    public static long Shied { get; private set; }

    private Mobile _shiedFrom;

    internal void ShyOf(Mobile foe)
    {
        if (!ReferenceEquals(_shiedFrom, foe))
        {
            _shiedFrom = foe;
            Shied++;
        }
    }

    public static int WithdrawShunMs { get; set; } = 120000;

    private static readonly Dictionary<Serial, (long Until, double Strength, long Last)> _shunned = [];

    public static long WithdrewAgain { get; private set; }

    public static bool Shuns(Mobile creature, double strength)
    {
        if (creature == null || !_shunned.TryGetValue(creature.Serial, out var shun))
        {
            return false;
        }

        return Core.TickCount - shun.Until < 0 && strength <= shun.Strength;
    }

    public static readonly Dictionary<string, long> WithdrewBy = [];

    public Mobile WithdrewFrom { get; private set; }

    private static readonly Dictionary<Serial, (Mobile From, long Tick)> _withdrawn = [];

    public static bool Caster(Mobile foe) => foe is BaseCreature { AI: AIType.AI_Mage };

    public static double Reckon(Mobile foe) =>
        foe is BaseCreature { Deleted: false, Alive: true, Controlled: false } creature
            ? BotCompanyRecord.Priced(creature) * Math.Clamp(creature.HitsMax > 0 ? creature.Hits / (double)creature.HitsMax : 1.0, 0.1, 1.0)
            : 0.0;

    public static double ReckonAlone(Mobile foe) =>
        foe is BaseCreature { Deleted: false, Alive: true, Controlled: false } creature
            ? BotQuarry.Priced(creature) * Math.Clamp(creature.HitsMax > 0 ? creature.Hits / (double)creature.HitsMax : 1.0, 0.1, 1.0)
            : 0.0;

    public double Able()
    {
        var lead = Leader?.Self;

        if (lead is not { Deleted: false, Alive: true })
        {
            return 0.0;
        }

        var sum = 0.0;

        for (var i = 0; i < _members.Count; i++)
        {
            if (_members[i] is IBotAlly { AbleToFight: true } && _members[i].Self is { Deleted: false, Alive: true } self
                && self.Map == lead.Map)
            {
                sum += BotThreat.Power(self);
            }
        }

        return sum;
    }

    public bool Beyond(Mobile foe, double extra = 0.0, double odds = 0.0)
    {
        if (!Withdraws || Warring || foe == null || Leader?.Self is not { Deleted: false } lead || BotDungeon.Under(lead.Location)
            || (Leader as IBotWilful)?.Resolve?.Deed is BotDelve)
        {
            return false;
        }

        var threat = Reckon(foe);
        var ours = Able() + extra;
        var bound = ours * WithdrawOdds;

        if (threat > 0.0 && threat > bound)
        {
            return true;
        }

        var at = odds > 0.0 ? Math.Min(odds, WithdrawOdds) : ChoiceOdds;

        if (threat > 0.0 && threat > ours * at)
        {
            if (!ReferenceEquals(_choiceRefused, foe))
            {
                _choiceRefused = foe;
                ChoiceRefused++;
            }

            return true;
        }

        if (threat > 0.0 && !ReferenceEquals(_letAt, foe) && ReckonAlone(foe) > bound)
        {
            _letAt = foe;
            BotCompanyRecord.Let(foe);
        }

        return false;
    }

    public static bool BeyondFor(Mobile lead, Mobile foe, double able)
    {
        if (!Withdraws || foe == null || lead == null || BotDungeon.Under(lead.Location))
        {
            return false;
        }

        var threat = Reckon(foe);

        return threat > 0.0 && threat > able * ChoiceOdds;
    }

    private string Overmatched()
    {
        var map = Map;

        if (!Withdraws || Warring || map == null || map == Map.Internal || Leader?.Self is not { Deleted: false, Alive: true } lead
            || BotDungeon.Under(lead.Location) || (Leader as IBotWilful)?.Resolve?.Deed is BotDelve)
        {
            return null;
        }

        Gather(all: true);

        Mobile worst = null;
        var threat = 0.0;

        Mobile worstAlone = null;
        var alone = 0.0;

        if (Focus is { Deleted: false, Alive: true } focus && focus.Map == map && !Caster(focus))
        {
            threat = Reckon(focus);
            worst = threat > 0.0 ? focus : null;
            alone = ReckonAlone(focus);
            worstAlone = alone > 0.0 ? focus : null;
        }

        for (var i = 0; i < _attackers.Count; i++)
        {
            if (Caster(_attackers[i]))
            {
                continue;
            }

            var reckoned = Reckon(_attackers[i]);

            if (reckoned > threat)
            {
                threat = reckoned;
                worst = _attackers[i];
            }

            var island = ReckonAlone(_attackers[i]);

            if (island > alone)
            {
                alone = island;
                worstAlone = _attackers[i];
            }
        }

        if (worst == null)
        {
            return null;
        }

        var ours = Able();

        if (ours <= 0.0 || threat <= ours * WithdrawOdds)
        {
            if (ours > 0.0 && alone > ours * WithdrawOdds && worstAlone != null && !ReferenceEquals(_stoodTo, worstAlone))
            {
                _stoodTo = worstAlone;
                BotCompanyRecord.Stand(worstAlone);
            }

            if (ours > 0.0 && !Mustering && !ReferenceEquals(_fieldOvermatched, worst) && threat > Afield(worst.Location, FieldReach) * WithdrawOdds)
            {
                _fieldOvermatched = worst;
                FieldOvermatched++;
            }

            return null;
        }

        return Withdraw(worst, threat, ours);
    }

    internal string Withdraw(Mobile from, double threat, double ours)
    {
        var map = Map;
        var now = Core.TickCount;

        Withdrawals++;
        WithdrewFrom = from;

        var by = (Leader as IBotWilful)?.Resolve?.Deed?.Kind ?? "nothing";

        WithdrewBy[by] = WithdrewBy.TryGetValue(by, out var had) ? had + 1 : 1;

        if (_shunned.TryGetValue(from.Serial, out var before) && now - before.Last < BotQuarry.HopelessMs)
        {
            WithdrewAgain++;
        }

        if (_shunned.Count >= 1024)
        {
            _shunned.Clear();
        }

        _shunned[from.Serial] = (now + WithdrawShunMs, ours, now);

        if (ReferenceEquals(Focus, from))
        {
            Disengage("we withdraw from what we cannot beat");
        }

        if (_withdrawn.Count >= 1024)
        {
            _withdrawn.Clear();
        }

        int near = 0, far = 0;

        for (var i = 0; i < _members.Count; i++)
        {
            if (_members[i].Self is not { Deleted: false, Alive: true } body || body.Map != map)
            {
                continue;
            }

            _withdrawn[body.Serial] = (from, now);

            if (body.InRange(from.Location, BotBolt.Watch) || ReferenceEquals(from.Combatant, body))
            {
                near++;
            }
            else
            {
                far++;
            }
        }

        WithdrewNear += near;
        WithdrewFar += far;

        logger.Information(
            "Squad {Id} of {Count} withdraws from {What}: reckoned {Threat:F0} against the {Ours:F0} the company brings, {Ratio:F1} times, past ×{Odds:F1} — {Near} within its reach left to run when it strikes, {Far} out of it; leader {Leader} was {By}",
            Id,
            Count,
            from.Name,
            threat,
            ours,
            threat / Math.Max(1.0, ours),
            WithdrawOdds,
            near,
            far,
            Leader?.Self?.Name ?? "nobody",
            by
        );

        return $"it withdrew from {from.Name}, reckoned {threat:F0} against its {ours:F0}";
    }

    public static bool Withdrawing(Mobile body)
    {
        if (body == null || !_withdrawn.TryGetValue(body.Serial, out var left))
        {
            return false;
        }

        if (Core.TickCount - left.Tick >= WithdrawRunMs || left.From is not { Deleted: false, Alive: true } from || from.Map != body.Map)
        {
            _withdrawn.Remove(body.Serial);

            return false;
        }

        return body.InRange(from.Location, BotBolt.Watch) || ReferenceEquals(from.Combatant, body);
    }

    public static string Withdrawn()
    {
        var say = ValueStringBuilder.Create(128);

        try
        {
            foreach (var (kind, n) in WithdrewBy)
            {
                if (say.Length > 0)
                {
                    say.Append(", ");
                }

                say.Append($"{kind} {n}");
            }

            return $"{Withdrawals} companies withdrew from something reckoned past ×{WithdrawOdds:F1} what they brought (by the leader's errand: {(say.Length > 0 ? say.ToString() : "none")}; {WithdrewNear} members within its reach, {WithdrewFar} out of it; {WithdrewAgain} from a creature a company had withdrawn from within {BotQuarry.HopelessMs / 60000} minutes){(Withdraws ? "" : " (off)")}, {Shied} creatures a company passed over for being beyond it (once a company each)";
        }
        finally
        {
            say.Dispose();
        }
    }

    public static double TieShare { get; set; } = 0.8;

    public static int FocusHoldMs { get; set; } = 4000;

    public static int AnswerReach { get; set; } = 40;

    public static long ToStrongest { get; private set; }

    public static long ToStronger { get; private set; }

    public static long LeaderCalls { get; private set; }

    public static long HeldFor { get; private set; }

    public static long Engaged { get; private set; }

    public static long Switched { get; private set; }

    public static long SwitchedSoon { get; private set; }

    public static long Downed { get; private set; }

    public int Kills { get; private set; }

    public Mobile Broken { get; private set; }

    public string BrokenWhy { get; private set; }

    private void Watch()
    {
        _dyingNow.Clear();

        if (Stance != BotSquadStance.Fighting)
        {
            if (_dying.Count > 0)
            {
                Ended();
            }

            return;
        }

        var now = Core.TickCount;

        for (var i = 0; i < _members.Count; i++)
        {
            var body = _members[i].Self;

            if (body is not { Deleted: false })
            {
                continue;
            }

            if (!body.Alive)
            {
                if (_dying.Remove(body.Serial, out var fell))
                {
                    if (fell.Helped)
                    {
                        DiedHelped++;
                    }
                    else
                    {
                        DiedUnhelped++;
                    }
                }

                continue;
            }

            var share = BotMend.Share(body);

            if (share < DyingShare)
            {
                _dyingNow.Add(body);

                if (!_dying.ContainsKey(body.Serial))
                {
                    _dying[body.Serial] = (now, false);
                    FellUnder++;
                }
            }
            else if (share >= DyingShare + Recovered && _dying.Remove(body.Serial, out var was) && !was.Helped)
            {
                RoseUnhelped++;
            }
        }
    }

    private void Helped(Mobile patient)
    {
        if (patient == null || !_dying.TryGetValue(patient.Serial, out var fell) || fell.Helped)
        {
            return;
        }

        _dying[patient.Serial] = (fell.Since, true);

        var ms = Core.TickCount - fell.Since;

        HelpedDying++;
        HelpedMs += ms;

        if (ms <= QuickHelpMs)
        {
            HelpedQuick++;
        }
    }

    private void Ended()
    {
        foreach (var (_, fell) in _dying)
        {
            if (!fell.Helped)
            {
                FightEndedFirst++;
            }
        }

        _dying.Clear();
        _dyingNow.Clear();
        _binders.Clear();
    }

    private readonly Dictionary<Serial, (long Since, bool Helped)> _dying = [];

    private readonly List<Mobile> _dyingNow = [];

    public static double Recovered { get; set; } = 0.15;

    public static int QuickHelpMs { get; set; } = 3000;

    public static long FellUnder { get; private set; }

    public static long HelpedDying { get; private set; }

    public static long HelpedMs { get; private set; }

    public static long HelpedQuick { get; private set; }

    public static long DiedHelped { get; private set; }

    public static long DiedUnhelped { get; private set; }

    public static long RoseUnhelped { get; private set; }

    public static long FightEndedFirst { get; private set; }

    public static long MendedDying { get; private set; }

    public static long MagesMended { get; private set; }

    public static long Bandaged { get; private set; }

    public static string Supported() =>
        $"{FellUnder} members fell under {DyingShare:P0} health in a company's fight: {HelpedDying} helped by another (a heal aimed or a bandage begun) in a mean of {(HelpedDying == 0 ? 0.0 : HelpedMs / 1000.0 / HelpedDying):F1}s ({HelpedQuick} inside {QuickHelpMs / 1000}s), {DiedHelped} died helped and {DiedUnhelped} unhelped, {RoseUnhelped} rose again unhelped, {FightEndedFirst} still waiting when the fight ended; {MendedDying} heals aimed at the dying, {MagesMended} by mages, {Bandaged} bandages wound by one member on another ({BindWalked} walks by a member with cloth to one of ours dying beyond its reach, {BindExcused} places not handed to a member on such a walk or winding a bandage), {Shielded} beats a caster threw at what was killing one of ours";

    private void Tend()
    {
        if (OnTheMarch)
        {
            return;
        }

        for (var i = 0; i < _members.Count; i++)
        {
            if (BotFormation.RoleOf(_members[i]) == BotRole.Medic && _members[i].Self is { Deleted: false, Alive: true } body
                && body is not BotMobile { Resolve.Deed: BotBolt or BotSalve } && !Raising(body))
            {
                if (body.Target is { } cursor && (cursor.Flags & TargetFlags.Harmful) == 0)
                {
                    var at = Worst(body, 1.0) ?? body;

                    if (BotMend.Aim(body, at))
                    {
                        Mended++;
                        Tended++;
                    }

                    continue;
                }

                Mend(body, MendAbove);
            }
        }
    }

    public static long Tended { get; private set; }

    private void Press()
    {
        var focus = Focus;

        if (focus is not { Deleted: false, Alive: true })
        {
            return;
        }

        _wound.Clear();

        for (var i = 0; i < _members.Count; i++)
        {
            var body = _members[i].Self;

            if (body is not { Deleted: false, Alive: true } || body.Map != focus.Map)
            {
                continue;
            }

            if (Raising(body))
            {
                continue;
            }

            if (Support(_members[i], body))
            {
                continue;
            }

            if (body is BotMobile { Resolve.Deed: BotBolt })
            {
                continue;
            }

            if (Peel(body) is { } killing)
            {
                BotArms.Check(body, _members[i].Class);
                BotArms.Suit(body, killing, BotFormation.PressRingFor(BotFormation.RoleOf(_members[i])));
                body.Warmode = true;

                if (!ReferenceEquals(body.Combatant, killing))
                {
                    body.Combatant = killing;
                }

                if (BotFormation.RoleOf(_members[i]) is BotRole.Medic or BotRole.Caster)
                {
                    Conjure(body, killing);
                }

                continue;
            }

            if (!body.InRange(focus.Location, PressReach))
            {
                continue;
            }

            if (Defending(body, focus))
            {
                Defended++;
                body.Warmode = true;

                continue;
            }

            if (Sparing(body))
            {
                body.Warmode = true;

                continue;
            }

            var blow = Strike(_members[i], out _);

            switch (blow)
            {
                case BotBlow.Blind:
                    Blinded++;

                    if ((Seen(body) ?? NearestToLeader(body)) is { } near)
                    {
                        NearLeader++;
                        body.Warmode = true;

                        if (!ReferenceEquals(body.Combatant, near))
                        {
                            body.Combatant = near;
                        }

                        continue;
                    }

                    break;

                case BotBlow.Refused:
                    Refused++;

                    continue;

                case BotBlow.Unsteady:
                    Unsteadied++;

                    break;
            }

            BotArms.Check(body, _members[i].Class);

            BotArms.Suit(body, focus, BotFormation.PressRingFor(BotFormation.RoleOf(_members[i])));

            body.Warmode = true;

            if (!ReferenceEquals(body.Combatant, focus))
            {
                body.Combatant = focus;
            }

            if (blow == BotBlow.Able)
            {
                Answer(_members[i], body);
            }

            if (BotFormation.RoleOf(_members[i]) is BotRole.Medic or BotRole.Caster)
            {
                Conjure(body, Shield(body) ?? focus);
            }
        }
    }

    private bool Support(IBotSquadMember member, Mobile body)
    {
        if (body is BotMobile { Resolve.Deed: BotBolt or BotSalve })
        {
            return false;
        }

        var role = BotFormation.RoleOf(member);

        if (role is BotRole.Medic or BotRole.Caster)
        {
            if (body.Target is { } cursor)
            {
                if ((cursor.Flags & TargetFlags.Harmful) != 0)
                {
                    return false;
                }

                var at = Worst(body, 1.0) ?? body;

                if (BotMend.Aim(body, at))
                {
                    Landed(role, at);
                }

                return true;
            }

            if (body.Spell != null)
            {
                return role == BotRole.Medic;
            }

            if (Mend(body, role == BotRole.Medic ? MendAbove : DyingShare))
            {
                return true;
            }
        }

        return Bind(member, body);
    }

    private bool Bind(IBotSquadMember member, Mobile body)
    {
        if (_dyingNow.Count == 0 || BotMend.Winding(body) || body.Skills[SkillName.Healing].Value < BindSkill)
        {
            return false;
        }

        for (var i = 0; i < _dyingNow.Count; i++)
        {
            var patient = _dyingNow[i];

            if (ReferenceEquals(patient, body) || patient is not { Deleted: false, Alive: true } || patient.Map != body.Map
                || !body.InRange(patient.Location, BotMend.Touch) || _wound.Contains(patient.Serial)
                || BotMend.Abetting(body, patient) != null)
            {
                continue;
            }

            if (!BotMend.Wind(body, patient))
            {
                return false;
            }

            _wound.Add(patient.Serial);
            _binders.Remove(body.Serial);
            Bandaged++;
            Helped(patient);

            return true;
        }

        return Approach(member, body);
    }

    private bool Approach(IBotSquadMember member, Mobile body)
    {
        if (!BindWalks || ReferenceEquals(member, Leader) || member.Journey is not { } journey || BotMend.Share(body) < DyingShare)
        {
            return false;
        }

        var now = Core.TickCount;

        if (_binders.TryGetValue(body.Serial, out var going))
        {
            if (now - going.Since < BindClaimMs)
            {
                return going.Patient is { Deleted: false, Alive: true } still && still.Map == body.Map
                       && BotMend.Share(still) < DyingShare && journey.Active;
            }

            _binders.Remove(body.Serial);
        }

        Mobile best = null;
        var nearest = BindReach + 1;

        for (var i = 0; i < _dyingNow.Count; i++)
        {
            var patient = _dyingNow[i];

            if (ReferenceEquals(patient, body) || patient is not { Deleted: false, Alive: true } || patient.Map != body.Map
                || _wound.Contains(patient.Serial) || Claimed(patient, now))
            {
                continue;
            }

            var away = Math.Max(Math.Abs(body.X - patient.X), Math.Abs(body.Y - patient.Y));

            if (away >= nearest || BotMend.Abetting(body, patient) != null)
            {
                continue;
            }

            nearest = away;
            best = patient;
        }

        if (best == null || body.Backpack?.FindItemByType<Bandage>() == null)
        {
            return false;
        }

        _binders[body.Serial] = (best, now);
        journey.Rebase(Map, best, BotArrival.Within(BotMend.Touch), "bind");
        BindWalked++;

        return true;
    }

    private bool Claimed(Mobile patient, long now)
    {
        foreach (var (_, claim) in _binders)
        {
            if (ReferenceEquals(claim.Patient, patient) && now - claim.Since < BindClaimMs)
            {
                return true;
            }
        }

        return false;
    }

    internal bool Binding(Mobile body) =>
        _binders.TryGetValue(body.Serial, out var claim) && Core.TickCount - claim.Since < BindClaimMs;

    private readonly Dictionary<Serial, (Mobile Patient, long Since)> _binders = [];

    public static bool BindWalks { get; set; } = true;

    public static int BindReach { get; set; } = 14;

    public static int BindClaimMs { get; set; } = 8000;

    public static long BindWalked { get; private set; }

    public static long BindExcused { get; private set; }

    private Mobile Shield(Mobile body)
    {
        if (_dyingNow.Count == 0)
        {
            return null;
        }

        Mobile best = null;
        var power = 0.0;

        for (var i = 0; i < _attackers.Count; i++)
        {
            var a = _attackers[i];

            if (ReferenceEquals(a, Focus) || a is not { Deleted: false, Alive: true } || a.Map != body.Map
                || a.Combatant is not Mobile victim || !_dyingNow.Contains(victim) || !body.InRange(a.Location, BotStrike.Range))
            {
                continue;
            }

            var p = BotThreat.Power(a);

            if (p <= power || !body.InLOS(a) || !body.CanBeHarmful(a, false) || body.IsHarmfulCriminal(a))
            {
                continue;
            }

            power = p;
            best = a;
        }

        if (best != null)
        {
            Shielded++;
        }

        return best;
    }

    public static long Shielded { get; private set; }

    private readonly HashSet<Serial> _wound = [];

    private void Landed(BotRole role, Mobile at)
    {
        Mended++;

        if (role == BotRole.Caster)
        {
            MagesMended++;
        }

        if (BotMend.Share(at) < DyingShare)
        {
            MendedDying++;
        }

        Helped(at);
    }

    public static double BindSkill { get; set; } = 20.0;

    public static double DyingShare { get; set; } = 0.5;

    public static double PoisonWeight { get; set; } = 0.3;

    private void Answer(IBotSquadMember member, Mobile body)
    {
        if (ReferenceEquals(member, Leader) || !_answered.Add(body.Serial))
        {
            return;
        }

        var answers = Answers(_engagedBy);
        var ms = Core.TickCount - _focusSinceTick;

        answers.Count++;
        answers.SumMs += ms;

        if (ms <= QuickMs)
        {
            answers.Quick++;
        }
        else if (ms > SlowMs)
        {
            answers.Slow++;
        }
    }

    private void Tally()
    {
        if (Focus == null || _engagedBy == null)
        {
            return;
        }

        var answers = Answers(_engagedBy);

        answers.Fights++;

        for (var i = 0; i < _members.Count; i++)
        {
            if (!ReferenceEquals(_members[i], Leader) && _members[i]?.Self is { Deleted: false, Alive: true } body
                && !_answered.Contains(body.Serial))
            {
                answers.Never++;

                switch (BotFormation.RoleOf(_members[i]))
                {
                    case BotRole.Ranged:
                        answers.NeverRanged++;

                        break;

                    case BotRole.Caster or BotRole.Medic:
                        answers.NeverBack++;

                        break;

                    default:
                        answers.NeverBlade++;

                        break;
                }
            }
        }
    }

    private static Answering Answers(string kind)
    {
        if (!_answers.TryGetValue(kind, out var answers))
        {
            _answers[kind] = answers = new Answering();
        }

        return answers;
    }

    public static int QuickMs { get; set; } = 2000;

    public static int SlowMs { get; set; } = 10000;

    private sealed class Answering
    {
        public long Fights;

        public long Count;

        public long SumMs;

        public long Quick;

        public long Slow;

        public long Never;

        public long NeverBlade;

        public long NeverRanged;

        public long NeverBack;
    }

    private static readonly Dictionary<string, Answering> _answers = [];

    public static string Responsiveness()
    {
        if (_answers.Count == 0)
        {
            return "no company has engaged anything yet";
        }

        var say = ValueStringBuilder.Create(256);

        try
        {
            foreach (var (kind, a) in _answers)
            {
                if (say.Length > 0)
                {
                    say.Append(", ");
                }

                var mean = a.Count == 0 ? 0.0 : a.SumMs / 1000.0 / a.Count;

                say.Append($"{kind} {a.Count} over {a.Fights} fights (a mean of {mean:F1}s; {a.Quick} inside {QuickMs / 1000}s, {a.Slow} past {SlowMs / 1000}s; {a.Never} members never had one: {a.NeverBlade} blades, {a.NeverRanged} shooters, {a.NeverBack} of the back ranks)");
            }

            return say.ToString();
        }
        finally
        {
            say.Dispose();
        }
    }

    private bool Defending(Mobile body, Mobile focus) =>
        body.Combatant is Mobile { Deleted: false, Alive: true } striker && !ReferenceEquals(striker, focus)
        && ReferenceEquals(striker.Combatant, body) && striker.Map == body.Map && body.InRange(striker.Location, DefendReach)
        && !(striker is IBotSquadMember && Has(striker));

    public static int DefendReach { get; set; } = 8;

    private bool Mend(Mobile body, double below)
    {
        if (body.Target != null || body.Spell != null)
        {
            return true;
        }

        var patient = Worst(body, below);

        if (patient == null)
        {
            return false;
        }

        var spell = BotMend.Spell(body, patient);

        if (spell < 0)
        {
            return false;
        }

        if (!BotMend.Begin(body, spell))
        {
            return false;
        }

        BotReflex.Casting(body, patient);

        return true;
    }

    public static bool Nurses { get; set; } = true;

    public static int GuestMs { get; set; } = 30000;

    public static long Guests { get; private set; }

    public static long GuestHeals { get; private set; }

    public Mobile Guest { get; private set; }

    private long _guestTick;

    internal void Nurse(Mobile patient)
    {
        if (!Nurses || patient is not { Deleted: false, Alive: true } || Focus is { Deleted: false, Alive: true } || OnTheMarch)
        {
            return;
        }

        Guest = patient;
        _guestTick = Core.TickCount;
        Guests++;

        BotSquads.Hurry(this);
    }

    private Mobile Worst(Mobile medic, double below)
    {
        Mobile worst = null;
        var lowest = below;

        var guest = Nurses && Guest is { Deleted: false, Alive: true } g && Core.TickCount - _guestTick < GuestMs
                    && Focus is not { Deleted: false, Alive: true } && !Has(g)
            ? g
            : null;

        for (var i = -1; i < _members.Count; i++)
        {
            var body = i < 0 ? guest : _members[i].Self;

            if (body is not { Deleted: false, Alive: true } || body.Map != medic.Map || body.HitsMax <= 0)
            {
                continue;
            }

            var share = body.Hits / (double)body.HitsMax - (body.Poisoned ? PoisonWeight : 0.0);

            if (share >= lowest)
            {
                continue;
            }

            if (!medic.InRange(body.Location, BotSurgeon.Reach) || !medic.InLOS(body))
            {
                continue;
            }

            if (BotMend.Abetting(medic, body) != null)
            {
                continue;
            }

            lowest = share;
            worst = body;
        }

        if (worst != null && ReferenceEquals(worst, guest))
        {
            GuestHeals++;
        }

        return worst;
    }

    public static double MendAbove { get; set; } = 0.8;

    private static void Conjure(Mobile body, Mobile focus)
    {
        if (!BotStrike.Can(body))
        {
            return;
        }

        if (body.Target != null)
        {
            BotStrike.Aim(body, focus);

            return;
        }

        if (body.Spell != null)
        {
            return;
        }

        var spell = BotStrike.Best(body);

        if (spell < 0)
        {
            Dry++;

            return;
        }

        if (BotStrike.Begin(body, spell))
        {
            Conjured++;
        }
    }

    private bool Quiet()
    {
        if (Charged)
        {
            _quiet = false;

            return true;
        }

        if (Stance == BotSquadStance.Fighting || RaiseUnderway || Recovering)
        {
            _quiet = false;

            return true;
        }

        if (!_quiet)
        {
            _quiet = true;
            _quietTick = Core.TickCount;

            return true;
        }

        return Core.TickCount - _quietTick < IdleCapMs;
    }

    public static int RestCapMs { get; set; } = 30000;

    public static long Released { get; private set; }

    public static long Blinded { get; private set; }

    public static long Refused { get; private set; }

    public static long Unsteadied { get; private set; }

    public static long Conjured { get; private set; }

    public static long Mended { get; private set; }

    public static long Unowned { get; private set; }

    public static long Dry { get; private set; }

    public static long Blindfights { get; private set; }

    public static void Forget()
    {
        Released = 0;
        Blinded = 0;
        Refused = 0;
        Unsteadied = 0;
        Conjured = 0;
        Mended = 0;
        Unowned = 0;
        Dry = 0;
        Blindfights = 0;
        Defended = 0;
        Excused = 0;
        _answers.Clear();
        ToStrongest = 0;
        ToStronger = 0;
        LeaderCalls = 0;
        HeldFor = 0;
        Engaged = 0;
        Switched = 0;
        SwitchedSoon = 0;
        Downed = 0;
        Closing = 0;
        FellUnder = 0;
        HelpedDying = 0;
        HelpedMs = 0;
        HelpedQuick = 0;
        DiedHelped = 0;
        DiedUnhelped = 0;
        RoseUnhelped = 0;
        FightEndedFirst = 0;
        MendedDying = 0;
        MagesMended = 0;
        Bandaged = 0;
        Tended = 0;
        Shielded = 0;
        BindWalked = 0;
        BindExcused = 0;
        Withdrawals = 0;
        WithdrewNear = 0;
        WithdrewFar = 0;
        Shied = 0;
        WithdrewAgain = 0;
        WithdrewBy.Clear();
        _withdrawn.Clear();
        _shunned.Clear();
        ForgetField();
    }

    private void Release()
    {
        if (Charged)
        {
            _resting = false;

            return;
        }

        if (Stance == BotSquadStance.Fighting || Recovering)
        {
            _resting = false;

            return;
        }

        if (!_resting)
        {
            _resting = true;
            _restTick = Core.TickCount;

            return;
        }

        if (Core.TickCount - _restTick < RestCapMs)
        {
            return;
        }

        for (var i = _members.Count - 1; i >= 0; i--)
        {
            var member = _members[i];

            if (ReferenceEquals(member, Leader) || member.Self is not BotMobile bot)
            {
                continue;
            }

            if (bot.Journey is { Moving: true } || bot.Resolve?.Deed is { Alongside: true } || Raising(bot))
            {
                continue;
            }

            _members.RemoveAt(i);
            Roster++;
            member.Squad = null;
            Released++;

            logger.Information(
                "Squad {Id} let {Name} go: it had nothing of its own and nowhere of ours to walk to",
                Id,
                bot.Name
            );
        }
    }

    private long _restTick;

    private bool _resting;

    private void Prune()
    {
        for (var i = _members.Count - 1; i >= 0; i--)
        {
            var body = _members[i].Self;

            if (body is { Deleted: false } && body.Map == Map && body.Map != Map.Internal)
            {
                continue;
            }

            _members.RemoveAt(i);
            Roster++;
        }
    }

    private bool Inherit()
    {
        IBotSquadMember heir = null;
        var best = 0.0;
        var heirHolds = false;

        for (var i = 0; i < _members.Count; i++)
        {
            var candidate = _members[i];

            if (candidate.Self is not { Deleted: false, Alive: true })
            {
                continue;
            }

            var holds = candidate.Self is BotMobile mobile && mobile.Resolve?.Deed is { Alongside: true };
            var power = BotThreat.Power(candidate.Self);

            if (heir != null && (heirHolds && !holds || heirHolds == holds && power <= best))
            {
                continue;
            }

            heir = candidate;
            best = power;
            heirHolds = holds;
        }

        if (heir == null)
        {
            return false;
        }

        Leader = heir;

        logger.Information("Squad {Id} lost its leader; {Name} has it now", Id, heir.Self.Name);

        return true;
    }

    private void Judge()
    {
        var focus = Focus;

        if (focus is not { Deleted: false, Alive: true })
        {
            Spoils(focus);

            Kills++;
            Downed++;

            BotBiography.CompanyKill(this);

            if (focus is BaseCreature && _fought.TryGetValue(focus.GetType(), out var fought) && fought.Members >= 2
                && _focusPower > 0.0)
            {
                BotCompanyRecord.Killed(focus.GetType(), fought.Able / _focusPower);
            }

            Disengage("it is down");

            return;
        }

        _fell = focus.Location;

        if (focus.Hits < _focusLowest)
        {
            _focusLowest = focus.Hits;
            _focusProgressTick = Core.TickCount;
        }

        if (Core.TickCount - _focusSinceTick >= FightCapMs)
        {
            Disengage("four minutes is long enough", true);

            return;
        }

        var able = InReach(out var nearest, out var blind, out var refused, out var unsteady);
        var arrived = able + blind + refused + unsteady;

        if (arrived == 0 && nearest >= 0 && nearest < _nearestSeen)
        {
            if (_nearestSeen != int.MaxValue)
            {
                Closing++;
            }

            _nearestSeen = nearest;
            _closedTick = Core.TickCount;
        }

        if (arrived == 0)
        {
            _focusProgressTick = Core.TickCount;
            _ableTick = Core.TickCount;

            if (Core.TickCount - _closedTick >= CloseMs)
            {
                Disengage("we never got near it", true);
            }

            return;
        }

        _closedTick = Core.TickCount;

        if (able > 0 || unsteady > 0 || blind + refused == 0)
        {
            _ableTick = Core.TickCount;
        }
        else if (Core.TickCount - _ableTick >= BlindMs)
        {
            Blindfights++;

            Disengage(
                blind > refused
                    ? "we are standing on it and there is no line to it"
                    : "we are standing on it and the engine refuses every blow",
                true
            );

            return;
        }

        if (Core.TickCount - _focusProgressTick >= NoProgressMs)
        {
            Disengage("its health has not moved", true);
        }
    }

    public static int BlindMs { get; set; } = 4000;

    private long _ableTick;

    public static int CloseMs { get; set; } = 30000;

    private long _closedTick;

    private int _nearestSeen = int.MaxValue;

    public static long Closing { get; private set; }

    private void Spoils(Mobile fallen)
    {
        var collector = Contact ?? Leader;
        var map = Map;

        if (fallen == null || collector?.Self is not { Deleted: false, Alive: true } || map == null)
        {
            return;
        }

        var corpse = BotQuarry.Remains(map, _fell, fallen);

        BotQuarry.Release(fallen);

        if (corpse == null)
        {
            return;
        }

        var coin = 0;
        var lying = corpse.Items;

        for (var i = 0; i < lying.Count; i++)
        {
            if (lying[i] is Gold gold)
            {
                coin += gold.Amount;
            }
        }

        BotSpoils.Share(this, collector, corpse);

        BotQuarry.Paid(fallen.GetType(), coin);
    }

    private void Settle()
    {
        if (Focus is { Deleted: false, Alive: true })
        {
            Stance = BotSquadStance.Fighting;

            return;
        }

        var leader = Leader?.Self;

        Stance = leader != null && Leader.Journey?.Active != true && !OnTheMarch && !Recovering
            ? BotSquadStance.Scouting
            : BotSquadStance.Marching;
    }

    private static readonly Dictionary<Serial, long> _stationRefused = [];

    public static int StationRestMs { get; set; } = 15000;

    public static long StationRested { get; private set; }

    public static int StationReach { get; set; } = 120;

    public static long StationFar { get; private set; }

    public static long StationShorted { get; private set; }

    public static void StationRefused(Mobile member)
    {
        if (member == null)
        {
            return;
        }

        if (_stationRefused.Count > 512)
        {
            _stationRefused.Clear();
        }

        _stationRefused[member.Serial] = Core.TickCount;
    }

    private void Station()
    {
        var map = Map;

        if (map == null)
        {
            return;
        }

        var anchor = Anchor;

        var moved = Math.Max(Math.Abs(anchor.X - _stationedAt.X), Math.Abs(anchor.Y - _stationedAt.Y));

        var all = moved >= ReformDistance || _stationedStance != Stance || _stationedCount != _members.Count;

        if (Core.TickCount - _stationedTick < RestationMs)
        {
            return;
        }

        if (all)
        {
            _stationedAt = anchor;
            _stationedStance = Stance;
            _stationedCount = _members.Count;
        }

        var sent = false;

        for (var i = 0; i < _members.Count; i++)
        {
            var member = _members[i];
            var journey = member.Journey;

            if (journey == null)
            {
                continue;
            }

            if (!all && !Stranded(member, journey))
            {
                continue;
            }

            if (ReferenceEquals(member, Leader) && Stance != BotSquadStance.Fighting)
            {
                continue;
            }

            if (member.Self is BotMobile { Resolve.Deed: BotBolt or BotSalve })
            {
                Excused++;

                continue;
            }

            if (member.Self is { } raiser && Raising(raiser))
            {
                Excused++;

                continue;
            }

            if (Stance == BotSquadStance.Fighting && member.Self is { } binder && (Binding(binder) || BotMend.Winding(binder)))
            {
                BindExcused++;

                continue;
            }

            if (Stance == BotSquadStance.Fighting && Strike(member, out _) is BotBlow.Able or BotBlow.Unsteady)
            {
                continue;
            }

            if (Stance == BotSquadStance.Fighting && member.Self is { Deleted: false, Alive: true } struck && Focus is { } enemy
                && Defending(struck, enemy))
            {
                continue;
            }

            if (Stance == BotSquadStance.Marching && !ReferenceEquals(member, Leader) && member.Self is { } far
                && !far.InRange(anchor, PressReach))
            {
                continue;
            }

            if (Stance == BotSquadStance.Marching && !ReferenceEquals(member, Leader) && MarchUnstationed)
            {
                continue;
            }

            if (Stance == BotSquadStance.Fighting && !ReferenceEquals(member, Leader) && member.Self is { } distant
                && !distant.InRange(anchor, StationReach))
            {
                StationFar++;

                continue;
            }

            if (member.Self is { } resting && _stationRefused.TryGetValue(resting.Serial, out var refusedAt)
                && Core.TickCount - refusedAt < StationRestMs)
            {
                StationRested++;

                continue;
            }

            if (member.Self is { } shorted && member.Journey is { Partial: true, Reason: "station" or "sweep" })
            {
                StationRefused(shorted);
                StationShorted++;

                continue;
            }

            var where = Stance == BotSquadStance.Scouting
                ? BotScatter.PatchFor(this, member)
                : BotFormation.StationFor(this, member);

            if (where == Point3D.Zero)
            {
                continue;
            }

            if (member.Self is { } self && !BotGates.Joined(map, self.Location, where))
            {
                Sundered++;

                continue;
            }

            var precision = Stance == BotSquadStance.Fighting ? BotArrival.Exactly : BotArrival.Beside;

            journey.Rebase(map, where, precision, Stance == BotSquadStance.Scouting ? "sweep" : "station");
            sent = true;
        }

        if (!all && !sent)
        {
            return;
        }

        _stationedTick = Core.TickCount;

        if (Stance == BotSquadStance.Fighting && Core.TickCount - _focusProgressTick >= RotateAfterMs)
        {
            Attempt++;
        }
    }

    private bool Stranded(IBotSquadMember member, BotJourney journey)
    {
        if (Stance != BotSquadStance.Fighting || Mustering || Focus is not { Deleted: false, Alive: true })
        {
            return false;
        }

        if (journey.Active && !journey.Hopeless)
        {
            return false;
        }

        var body = member.Self;

        if (body is not { Deleted: false, Alive: true } || body.Map != Focus.Map)
        {
            return false;
        }

        return Strike(member, out _) is BotBlow.Far or BotBlow.Blind;
    }

    private static (int X, int Y) Unit(Point3D from, Point3D to) =>
        (Math.Sign(to.X - from.X), Math.Sign(to.Y - from.Y));

    private static (int X, int Y) Facing(Direction direction) =>
        (direction & Direction.Mask) switch
        {
            Direction.North => (0, -1),
            Direction.Right => (1, -1),
            Direction.East => (1, 0),
            Direction.Down => (1, 1),
            Direction.South => (0, 1),
            Direction.Left => (-1, 1),
            Direction.West => (-1, 0),
            _ => (-1, -1)
        };

    public override string ToString() =>
        $"squad {Id}: {Count} under {Leader?.Self?.Name ?? "nobody"}, {Stance}{(Focus != null ? $" vs {Focus.Name}" : "")}";
}
