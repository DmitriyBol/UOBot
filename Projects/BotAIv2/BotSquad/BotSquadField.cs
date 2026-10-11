using System;
using System.Collections.Generic;
using Server.Mobiles;
using Server.Text;

namespace Server.BotAI.V2;

/// <summary>
/// <b>The company fights on the strength that is on the field, not on the map (06.10.2026, company16).</b>
///
/// <para>
/// Measured over 429 company deaths on builds 405–409 (`an-enlist16.md`): 89 % fell in fights priced under the company's
/// strength over 1.5 — fights the company "should" have won — and 75 % to a killer priced under the dead bot alone. The dead bot
/// had no other member within 12 tiles in 36 % of them and stood more than 40 tiles from its leader in 43 %; <see cref="Able"/>
/// summed every living member on the map, so a company of twelve spread over a hundred tiles engaged a reaper on the strength
/// of twelve and met it with two. The median death came 18 s after the engagement, 42 % inside 15 s; 35 % of deaths followed
/// another in the same company inside a minute, and only 20 companies withdrew in four builds against 233 that lost somebody:
/// companies were wiped, not withdrawn (company 364 lost seven in 67 s to a reaper reckoned at 0.3 of its sum). Deaths per
/// fight rose from 0.013 at R/A under 0.15 to 0.29 at R/A 1.0–1.5, the worst band, which held 4 % of the fights, 27 % of the
/// deaths and 10 % of the company gold. Casters killed 60 %; 42 % of deaths were to something other than the company's enemy.
/// </para>
///
/// <para>
/// Four rules, each behind a dial and counted on the Companies line:
/// <list type="number">
/// <item><b>The choice gate is stricter than the withdrawal gate.</b> A company chooses, joins a cry to, or is raised at a creature
/// only up to <see cref="ChooseOdds"/> (1.0) times what it brings (<see cref="Beyond"/>); the withdrawal stays at
/// <see cref="WithdrawOdds"/> (1.5), and the protection of a member and the enlistment into a fight already on are judged at the
/// withdrawal's odds, because they are defence, not choice.</item>
/// <item><b>A chosen fight musters at the enemy first.</b> When the company chooses a fight with less than <see cref="MusterShare"/>
/// of its strength within <see cref="FieldReach"/> of the enemy, it forms up at a rally <see cref="MusterOff"/> tiles short of it —
/// the leader and every member stationed there, nobody pressed — until that share has come up or <see cref="MusterMs"/> is out; then
/// it engages with what is on the field, or lets the creature go if what came up is not enough at <see cref="ChooseOdds"/>. A blow on
/// a member ends the muster at once.</item>
/// <item><b>A company that bleeds pulls back.</b> At the <see cref="BleedDeaths"/>th death inside <see cref="BleedMs"/> the company
/// lets its enemy go, shuns it and the killer for <see cref="WithdrawShunMs"/> (casters too: this is the company's results, not the
/// odds), stands its members' flight (<see cref="Withdrawing"/>) and holds together on its leader for <see cref="BleedRestMs"/> with
/// no fight of its own choosing. A delve's party comes up on it (<c>BotDelve</c>).</item>
/// <item><b>The thing killing a member is fought by whoever can reach it.</b> A member within <see cref="PressReach"/> of an attacker
/// that has one of ours under <see cref="DyingShare"/> for its combatant turns on that attacker instead of the company's enemy
/// (<see cref="Peel"/>), whatever the attacker's price; the company's enemy itself prefers a caster that is attacking it
/// (<see cref="CastersFirst"/>, in <see cref="Strongest"/>).</item>
/// </list>
/// </para>
/// </summary>
public sealed partial class BotSquad
{
    public static double ChooseOdds { get; set; } = 1.0;

    public static double ChoiceOdds => ChooseOdds > 0.0 && ChooseOdds < WithdrawOdds ? ChooseOdds : WithdrawOdds;

    public static long ChoiceRefused { get; private set; }

    private Mobile _choiceRefused;

    public static bool Musters { get; set; } = true;

    public static int FieldReach { get; set; } = 16;

    public static double MusterShare { get; set; } = 0.75;

    public static int MusterOff { get; set; } = 14;

    public static int MusterMs { get; set; } = 30000;

    public bool Mustering { get; private set; }

    private Point3D _rally;

    private long _musterTick;

    public static long Mustered { get; private set; }

    public static long MusterGathered { get; private set; }

    public static long MusterTimed { get; private set; }

    public static long MusterStruck { get; private set; }

    public static long MusterWent { get; private set; }

    public static long MusterShort { get; private set; }

    public static long MusterUnneeded { get; private set; }

    public static long FieldOvermatched { get; private set; }

    private Mobile _fieldOvermatched;

    public double Afield(Point3D at, int reach)
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
                && self.Map == lead.Map && self.InRange(at, reach))
            {
                sum += BotThreat.Power(self);
            }
        }

        return sum;
    }

    private bool WantsMuster(Mobile focus)
    {
        if (!Musters || focus is not { Deleted: false, Alive: true } || Leader?.Self is not { Deleted: false, Alive: true } lead
            || lead.Map != focus.Map)
        {
            return false;
        }

        var able = Able();

        return able > 0.0 && Afield(focus.Location, FieldReach) < able * MusterShare;
    }

    private static Point3D RallyFor(Mobile lead, Mobile focus)
    {
        var dx = lead.X - focus.X;
        var dy = lead.Y - focus.Y;
        var far = Math.Max(Math.Abs(dx), Math.Abs(dy));

        if (far <= MusterOff || BotDungeon.Under(lead.Location))
        {
            return lead.Location;
        }

        return new Point3D(focus.X + dx * MusterOff / far, focus.Y + dy * MusterOff / far, lead.Z);
    }

    private void BeginMuster(Mobile focus, IBotSquadMember contact)
    {
        var lead = Leader.Self;

        Tally();

        Focus = focus;
        Contact = contact ?? Leader;
        Stance = BotSquadStance.Fighting;
        Mustering = true;
        _rally = RallyFor(lead, focus);
        _musterTick = Core.TickCount;
        Attempt = 0;
        _fell = focus.Location;
        Mustered++;

        BotSquads.Hurry(this);

        logger.Information(
            "Squad {Id} of {Count} musters at ({X}, {Y}), {Off} tiles short of {What} ({Power:F0}): {Afield:F0} of the {Ours:F0} the company brings is on the field",
            Id,
            Count,
            _rally.X,
            _rally.Y,
            Math.Max(Math.Abs(_rally.X - focus.X), Math.Abs(_rally.Y - focus.Y)),
            focus.Name,
            BotThreat.Power(focus),
            Afield(_rally, FieldReach),
            Able()
        );
    }

    private void MusterBeat()
    {
        var focus = Focus;

        if (focus is not { Deleted: false, Alive: true } || focus.Map != Map)
        {
            MusterWent++;
            Mustering = false;
            Disengage("it went before we had come up to it");

            return;
        }

        var able = Able();
        var afield = Afield(_rally, FieldReach);

        if (able > 0.0 && afield >= able * MusterShare)
        {
            MusterGathered++;
            EndMuster("formed up");

            return;
        }

        if (Core.TickCount - _musterTick < MusterMs)
        {
            return;
        }

        var threat = Reckon(focus);

        if (threat > 0.0 && threat > afield * ChoiceOdds)
        {
            MusterShort++;
            Mustering = false;

            if (_shunned.Count >= 1024)
            {
                _shunned.Clear();
            }

            _shunned[focus.Serial] = (Core.TickCount + WithdrawShunMs, able, Core.TickCount);

            BotQuarry.Shun(focus, BotQuarry.ShunMs);

            logger.Information(
                "Squad {Id} of {Count} lets {What} go: {Afield:F0} of the {Ours:F0} it brings came up in {Seconds}s, and it is reckoned {Threat:F0}",
                Id,
                Count,
                focus.Name,
                afield,
                able,
                MusterMs / 1000,
                threat
            );

            Disengage("not enough of us came up to it");

            return;
        }

        MusterTimed++;
        EndMuster("the clock ran out");
    }

    internal void Struck(Mobile attacker)
    {
        if (!Mustering || attacker is not { Deleted: false } || Leader?.Self is not { Deleted: false } lead || attacker.Map != lead.Map
            || !lead.InRange(attacker.Location, AnswerReach))
        {
            return;
        }

        MusterStruck++;
        EndMuster("set upon");
    }

    private void EndMuster(string why)
    {
        var focus = Focus;

        Mustering = false;

        if (focus is not { Deleted: false, Alive: true })
        {
            Disengage("it went before we had come up to it");

            return;
        }

        logger.Information(
            "Squad {Id} of {Count} ends its muster against {What} ({Why}) with {Afield:F0} of {Ours:F0} on the field after {Seconds:F0}s",
            Id,
            Count,
            focus.Name,
            why,
            Afield(_rally, FieldReach),
            Able(),
            (Core.TickCount - _musterTick) / 1000.0
        );

        Begin(focus, Contact);
    }

    public static bool Bleeds { get; set; } = true;

    public static int BleedDeaths { get; set; } = 2;

    public static int BleedMs { get; set; } = 60000;

    public static int BleedRestMs { get; set; } = 45000;

    private readonly List<long> _lost = [];

    public bool Recovering => Core.TickCount - _recoverUntil < 0;

    private long _recoverUntil;

    private Mobile _bledFrom;

    private Mobile _bledBy;

    private long _bledUntil;

    public int Bled { get; private set; }

    public static long Bleedings { get; private set; }

    public static readonly Dictionary<string, long> BledBy = [];

    public static long BledRefused { get; private set; }

    public static long BledNear { get; private set; }

    public bool Bleeding(Mobile foe) =>
        foe != null && Core.TickCount - _bledUntil < 0 && (ReferenceEquals(foe, _bledFrom) || ReferenceEquals(foe, _bledBy));

    private void Lost(Mobile killer)
    {
        var now = Core.TickCount;

        _lost.Add(now);

        for (var i = _lost.Count - 1; i >= 0; i--)
        {
            if (now - _lost[i] > BleedMs)
            {
                _lost.RemoveAt(i);
            }
        }

        if (!Bleeds || Warring || _lost.Count < BleedDeaths)
        {
            return;
        }

        _lost.Clear();
        Bleed(killer);
    }

    private void Bleed(Mobile killer)
    {
        var now = Core.TickCount;
        var focus = Focus;
        var from = focus is { Deleted: false, Alive: true } ? focus : killer is { Deleted: false, Alive: true } ? killer : null;
        var by = (Leader as IBotWilful)?.Resolve?.Deed?.Kind ?? "nothing";

        Bled++;
        Bleedings++;
        BledBy[by] = BledBy.TryGetValue(by, out var had) ? had + 1 : 1;

        _bledFrom = focus;
        _bledBy = killer is BaseCreature ? killer : null;
        _bledUntil = now + WithdrawShunMs;
        _recoverUntil = now + BleedRestMs;

        var ours = Able();

        if (_shunned.Count >= 1024)
        {
            _shunned.Clear();
        }

        if (focus is { Deleted: false, Alive: true })
        {
            _shunned[focus.Serial] = (now + WithdrawShunMs, ours, now);
        }

        if (killer is BaseCreature { Deleted: false, Alive: true } && !ReferenceEquals(killer, focus))
        {
            _shunned[killer.Serial] = (now + WithdrawShunMs, ours, now);
        }

        if (_withdrawn.Count >= 1024)
        {
            _withdrawn.Clear();
        }

        var near = 0;
        var map = Map;

        if (from != null)
        {
            for (var i = 0; i < _members.Count; i++)
            {
                if (_members[i].Self is not { Deleted: false, Alive: true } body || body.Map != map)
                {
                    continue;
                }

                if (body.InRange(from.Location, BotBolt.Watch) || ReferenceEquals(from.Combatant, body))
                {
                    _withdrawn[body.Serial] = (from, now);
                    near++;
                }
            }
        }

        BledNear += near;

        logger.Information(
            "Squad {Id} of {Count} pulls back from {What}: {Deaths} of ours fell inside {Window}s, the last to {Killer}; {Near} within its reach left to run, the company holds on {Leader} for {Rest}s — leader was {By}",
            Id,
            Count,
            focus?.Name ?? "nothing",
            BleedDeaths,
            BleedMs / 1000,
            killer?.Name ?? "nothing",
            near,
            Leader?.Self?.Name ?? "nobody",
            BleedRestMs / 1000,
            by
        );

        Mustering = false;

        if (focus != null)
        {
            Disengage($"we pull back: {BleedDeaths} of ours fell inside a minute");
        }
    }

    public static bool Peels { get; set; } = true;

    public static long Peeled { get; private set; }

    public static bool CastersFirst { get; set; } = true;

    public static long CasterTaken { get; private set; }

    private Mobile Peel(Mobile body)
    {
        if (!Peels || _dyingNow.Count == 0)
        {
            return null;
        }

        Mobile best = null;
        var power = 0.0;

        for (var i = 0; i < _attackers.Count; i++)
        {
            var a = _attackers[i];

            if (ReferenceEquals(a, Focus) || a is not { Deleted: false, Alive: true } || a.Map != body.Map
                || a.Combatant is not Mobile victim || ReferenceEquals(victim, body) || !_dyingNow.Contains(victim)
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

        if (best != null)
        {
            Peeled++;
        }

        return best;
    }

    public static string Afielded()
    {
        var say = ValueStringBuilder.Create(128);

        try
        {
            foreach (var (kind, n) in BledBy)
            {
                if (say.Length > 0)
                {
                    say.Append(", ");
                }

                say.Append($"{kind} {n}");
            }

            return $"the field: {ChoiceRefused} creatures passed over between ×{ChoiceOdds:F1} and ×{WithdrawOdds:F1} what the company brought (the choice gate{(ChooseOdds > 0.0 && ChooseOdds < WithdrawOdds ? "" : ", off")}); "
                   + $"{Mustered} chosen fights mustered {MusterOff} tiles short of the enemy{(Musters ? "" : " (off)")}: {MusterGathered} engaged with {MusterShare:P0} of their strength within {FieldReach} tiles, {MusterTimed} on the {MusterMs / 1000}s clock, {MusterStruck} at a blow on a member, {MusterWent} found the enemy gone, {MusterShort} let it go for too few come up; {MusterUnneeded} chosen with the share already there; "
                   + $"{Bleedings} companies pulled back at {BleedDeaths} deaths inside {BleedMs / 1000}s{(Bleeds ? "" : " (off)")} (by the leader's errand: {(say.Length > 0 ? say.ToString() : "none")}; {BledNear} members within the thing's reach offered the run, {BledRefused} engagements refused for being what the company pulled back from); "
                   + $"{Peeled} beats a member turned on what was killing one of ours{(Peels ? "" : " (off)")}, {CasterTaken} beats a caster was named the company's enemy ahead of a stronger attacker inside the {TieShare:P0} tie{(CastersFirst ? "" : " (off)")}; "
                   + $"{FieldOvermatched} creatures stood to on the map's sum that the strength within {FieldReach} tiles of them would have withdrawn from (an instrument, not a gate)";
        }
        finally
        {
            say.Dispose();
        }
    }

    private static void ForgetField()
    {
        ChoiceRefused = 0;
        Mustered = 0;
        MusterGathered = 0;
        MusterTimed = 0;
        MusterStruck = 0;
        MusterWent = 0;
        MusterShort = 0;
        MusterUnneeded = 0;
        Bleedings = 0;
        BledBy.Clear();
        BledRefused = 0;
        BledNear = 0;
        Peeled = 0;
        CasterTaken = 0;
        FieldOvermatched = 0;
    }
}
