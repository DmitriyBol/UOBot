using System;
using System.Collections.Generic;
using Server.Items;
using Server.Logging;
using Server.Mobiles;
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
public sealed class BotSquad
{
    public static long Sundered { get; private set; }

    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotSquad));

    public static int NoProgressMs => SlowestBlowMs * Blows;

    public static int SlowestBlowMs { get; set; } = 4000;

    public static int Blows { get; set; } = 3;

    public static int FightCapMs { get; set; } = 240000;

    public static int MaxSize { get; set; } = 5;

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
        }
    }

    internal bool Detach(IBotSquadMember member) => _members.Remove(member);

    internal void Promote(IBotSquadMember member) => Leader = member;

    public bool Holds(IBotSquadMember member) => _members.Contains(member);

    public void Engage(Mobile focus, IBotSquadMember contact)
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

        Tally();

        Focus = focus;
        Contact = contact ?? Leader;
        Stance = BotSquadStance.Fighting;
        Attempt = 0;
        _focusLowest = focus.Hits;
        _focusProgressTick = Core.TickCount;
        _focusSinceTick = Core.TickCount;
        _answered.Clear();
        _engagedBy = (Leader as IBotWilful)?.Resolve?.Deed?.Kind ?? "nothing";
        _ableTick = Core.TickCount;
        _closedTick = Core.TickCount;

        logger.Information(
            "Squad {Id} of {Count} is dealing with {What} ({Power:F0})",
            Id,
            Count,
            focus.Name,
            BotThreat.Power(focus)
        );
    }

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
        }

        Tally();

        Focus = null;
        Contact = null;
        Attempt = 0;
        Stance = BotSquadStance.Marching;
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

    internal string Update()
    {
        Prune();
        Release();

        if (_members.Count == 0)
        {
            return "the last of them is gone";
        }

        if (Charged && !Warring && Leader?.Self is BotMobile head && head.Resolve?.Deed is not { Alongside: true })
        {
            Charged = false;
            Unowned++;
        }

        if (_members.Count < 2 && !Charged)
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

        if (Stance == BotSquadStance.Fighting)
        {
            Judge();
        }

        Settle();
        Station();

        if (Stance == BotSquadStance.Fighting)
        {
            Press();
        }
        else
        {
            Hunt();
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

        var quarry = Quarry != null ? Quarry(leader) : BotQuarry.Company(leader, OnTheMarch ? MarchHuntReach : BotMuster.Reach);

        if (quarry == null)
        {
            return;
        }

        Engage(quarry, Leader);
    }

    private void Press()
    {
        var focus = Focus;

        if (focus is not { Deleted: false, Alive: true })
        {
            return;
        }

        for (var i = 0; i < _members.Count; i++)
        {
            var body = _members[i].Self;

            if (body is not { Deleted: false, Alive: true } || body.Map != focus.Map)
            {
                continue;
            }

            if (!body.InRange(focus.Location, PressReach))
            {
                continue;
            }

            if (body is BotMobile { Resolve.Deed: BotBolt })
            {
                continue;
            }

            if (Defending(body, focus))
            {
                Defended++;
                body.Warmode = true;

                if (BotFormation.RoleOf(_members[i]) == BotRole.Medic)
                {
                    Mend(body);
                }

                continue;
            }

            var blow = Strike(_members[i], out _);

            switch (blow)
            {
                case BotBlow.Blind:
                    Blinded++;

                    if (NearestToLeader(body) is { } near)
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

            switch (BotFormation.RoleOf(_members[i]))
            {
                case BotRole.Medic:
                    if (!Mend(body))
                    {
                        Conjure(body, focus);
                    }

                    break;

                case BotRole.Caster:
                    Conjure(body, focus);

                    break;
            }
        }
    }

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

                say.Append($"{kind} {a.Count} over {a.Fights} fights (a mean of {mean:F1}s; {a.Quick} inside {QuickMs / 1000}s, {a.Slow} past {SlowMs / 1000}s; {a.Never} members never had one)");
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
        && ReferenceEquals(striker.Combatant, body) && striker.Map == body.Map && body.InRange(striker.Location, 2)
        && !Has(striker);

    private bool Mend(Mobile body)
    {
        if (body.Target != null)
        {
            var at = Worst(body) ?? body;

            if (BotMend.Aim(body, at))
            {
                Mended++;
            }

            return true;
        }

        if (body.Spell != null)
        {
            return true;
        }

        var patient = Worst(body);

        if (patient == null)
        {
            return false;
        }

        var spell = BotMend.Spell(body, patient);

        if (spell < 0)
        {
            return false;
        }

        return BotMend.Begin(body, spell);
    }

    private Mobile Worst(Mobile medic)
    {
        Mobile worst = null;
        var lowest = MendAbove;

        for (var i = 0; i < _members.Count; i++)
        {
            var body = _members[i].Self;

            if (body is not { Deleted: false, Alive: true } || body.Map != medic.Map || body.HitsMax <= 0)
            {
                continue;
            }

            if (!medic.InRange(body.Location, BotSurgeon.Reach) || !medic.InLOS(body))
            {
                continue;
            }

            var share = body.Hits / (double)body.HitsMax;

            if (share >= lowest)
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

        if (Stance == BotSquadStance.Fighting)
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
    }

    private void Release()
    {
        if (Charged)
        {
            _resting = false;

            return;
        }

        if (Stance == BotSquadStance.Fighting)
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

            if (bot.Journey is { Moving: true } || bot.Resolve?.Deed is { Alongside: true })
            {
                continue;
            }

            _members.RemoveAt(i);
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

        var able = InReach(out _, out var blind, out var refused, out var unsteady);
        var arrived = able + blind + refused + unsteady;

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

        Stance = leader != null && Leader.Journey?.Active != true && !OnTheMarch
            ? BotSquadStance.Scouting
            : BotSquadStance.Marching;
    }

    private static readonly Dictionary<Serial, long> _stationRefused = [];

    public static int StationRestMs { get; set; } = 15000;

    public static long StationRested { get; private set; }

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
        if (Stance != BotSquadStance.Fighting || Focus is not { Deleted: false, Alive: true })
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
