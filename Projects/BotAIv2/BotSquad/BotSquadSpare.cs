using System;
using Server.Mobiles;

namespace Server.BotAI.V2;

/// <summary>
/// <b>A company whose fight is well in hand spares a member or two to a dire cry near it (06.10.2026, react18).</b>
///
/// <para>
/// At prime time nearly everybody near a cry is in a company: on build 410 (11:44–12:35) 84 of 167 dire cries had "nobody free,
/// fit and friendly within 60 tiles", and 607 of the bots near them were members. A company came only when it was idle
/// (<c>BotCry.Brings</c>: 5 brought, 144 refused for a fight of their own). Here the company keeps its enemy and its line, and lends
/// what it can spare: the one or two members nearest the thing, who turn on it and walk to it as the blow's reflex would have them
/// (<c>BotMobile.Engage</c>: the combatant set, the walk put on top of the road), and whom the press leaves on it
/// (<see cref="Sparing"/>) until the crier is safe or dead, the thing is down, or <see cref="SpareMs"/> is up — then the press
/// turns them back and the stations walk them home. No deed, no roster change, nothing dropped: the company's own machinery.
/// </para>
///
/// <para>
/// Well in hand: the company's enemy reckoned at no more than <see cref="SpareShare"/> of what the company can bring, no member
/// under <c>DyingShare</c>, no pull-back running, no muster under way. Spared: not the leader, not a medic, fit, within
/// <see cref="SpareReach"/> of the thing, not running or raising.
/// </para>
/// </summary>
public sealed partial class BotSquad
{
    public static bool Spares { get; set; } = true;

    public static double SpareShare { get; set; } = 0.5;

    public static int SpareReach { get; set; } = 24;

    public static int MostSpared { get; set; } = 2;

    public static int SpareMs { get; set; } = 30000;

    public static long Spared { get; private set; }

    public static long SparedTo { get; private set; }

    public static long SpareRefusedFight { get; private set; }

    public static long SpareRefusedDying { get; private set; }

    public static long SpareRefusedBleeding { get; private set; }

    public static long SpareRefusedNobody { get; private set; }

    public static long SparingBeats { get; private set; }

    private Mobile _spareFoe;

    private Mobile _spareFor;

    private long _spareTick;

    internal bool WellInHand()
    {
        if (!Spares || Disbanded || Recovering || Mustering)
        {
            SpareRefusedBleeding++;

            return false;
        }

        if (Focus is { Deleted: false, Alive: true } foe && Reckon(foe) > SpareShare * Math.Max(1.0, Able()))
        {
            SpareRefusedFight++;

            return false;
        }

        for (var i = 0; i < _members.Count; i++)
        {
            if (_members[i].Self is { Deleted: false, Alive: true, HitsMax: > 0 } body && body.Hits < body.HitsMax * DyingShare)
            {
                SpareRefusedDying++;

                return false;
            }
        }

        return true;
    }

    internal int Spare(Mobile crier, BaseCreature foe, long now)
    {
        if (foe is not { Deleted: false, Alive: true } || foe.Map != Map || Leader?.Self is not { } lead)
        {
            return 0;
        }

        if (_spareFoe is { Deleted: false, Alive: true } && now - _spareTick < SpareMs)
        {
            return 0;
        }

        IBotSquadMember first = null;
        IBotSquadMember second = null;
        var firstOff = int.MaxValue;
        var secondOff = int.MaxValue;

        for (var i = 0; i < _members.Count; i++)
        {
            var member = _members[i];

            if (ReferenceEquals(member, Leader) || BotFormation.RoleOf(member) == BotRole.Medic
                || member.Self is not BotMobile { Deleted: false, Alive: true, HitsMax: > 0 } body
                || body.Hits < body.HitsMax * BotRescuer.FitAt || body.Map != Map || Raising(body)
                || body.Resolve?.Deed is BotBolt || !body.InRange(foe.Location, SpareReach) || !body.CanBeHarmful(foe, false))
            {
                continue;
            }

            var off = Math.Max(Math.Abs(body.X - foe.X), Math.Abs(body.Y - foe.Y));

            if (off < firstOff)
            {
                second = first;
                secondOff = firstOff;
                first = member;
                firstOff = off;
            }
            else if (off < secondOff)
            {
                second = member;
                secondOff = off;
            }
        }

        if (first == null)
        {
            SpareRefusedNobody++;

            return 0;
        }

        var sent = Send(first, foe);

        if (second != null && MostSpared > 1)
        {
            sent += Send(second, foe);
        }

        if (sent > 0)
        {
            _spareFoe = foe;
            _spareFor = crier;
            _spareTick = now;
            SparedTo++;
            Spared += sent;
        }

        return sent;
    }

    private int Send(IBotSquadMember member, BaseCreature foe)
    {
        if (member.Self is not BotMobile body)
        {
            return 0;
        }

        body.Warmode = true;
        body.Combatant = foe;

        if (!ReferenceEquals(body.Journey?.Current?.Follow, foe))
        {
            body.Journey?.Interrupt(Map, foe, BotArrival.Beside, "spare");
        }

        return 1;
    }

    private bool Sparing(Mobile body)
    {
        if (!Spares || _spareFoe is not { Deleted: false, Alive: true } foe || !ReferenceEquals(body.Combatant, foe))
        {
            return false;
        }

        var now = Core.TickCount;

        if (now - _spareTick >= SpareMs || _spareFor is not { Deleted: false, Alive: true } || !BotCry.Crying(_spareFor))
        {
            (body as BotMobile)?.Journey?.Shed();

            return false;
        }

        SparingBeats++;

        return true;
    }

    public static string DescribeSpares() =>
        $"{SparedTo} dire cries answered by a company sparing {Spared} members from a fight well in hand (its enemy under {SpareShare:P0} of its strength, nobody under {DyingShare:P0}), {SparingBeats} beats the press left them on it; companies asked that would not: {SpareRefusedFight} with a fight not in hand, {SpareRefusedDying} with a member dying, {SpareRefusedBleeding} pulling back or mustering, {SpareRefusedNobody} with nobody fit within {SpareReach} tiles of the thing";
}
