using System;
using System.Collections.Generic;
using Server.Mobiles;

namespace Server.BotAI.V2;

public sealed partial class BotSquad
{

    public static bool Spares { get; set; } = true;

    public static double SpareShare { get; set; } = 0.5;

    public static int SpareReach { get; set; } = 24;

    public static int MostSpared { get; set; } = 2;

    public static int SpareMs { get; set; } = 30000;

    public static double SpareDyingShare { get; set; } = 0.35;

    public static long Spared { get; private set; }

    public static long SparedTo { get; private set; }

    public static long SpareRefusedFight { get; private set; }

    public static long SpareRefusedDying { get; private set; }

    public static long SpareRefusedBleeding { get; private set; }

    public static long SpareRefusedNobody { get; private set; }

    public static long SpareRefusedWar { get; private set; }

    public static long SpareRefusedKept { get; private set; }

    public static long SpareRefusedShort { get; private set; }

    public static long SparingBeats { get; private set; }

    private Mobile _spareFoe;

    private Mobile _spareFor;

    private long _spareTick;

    private static readonly List<(IBotSquadMember Member, BotMobile Body, int OffFoe, int OffCrier)> _spareable = [];

    internal bool WellInHand()
    {
        if (!Spares || Disbanded || Recovering || Mustering)
        {
            SpareRefusedBleeding++;

            return false;
        }

        if (Warring)
        {
            SpareRefusedWar++;

            return false;
        }

        if (Focus is { Deleted: false, Alive: true } foe && Reckon(foe) > SpareShare * Math.Max(1.0, Able()))
        {
            SpareRefusedFight++;

            return false;
        }

        for (var i = 0; i < _members.Count; i++)
        {
            if (_members[i].Self is { Deleted: false, Alive: true, HitsMax: > 0 } body && body.Hits < body.HitsMax * SpareDyingShare)
            {
                SpareRefusedDying++;

                return false;
            }
        }

        return true;
    }

    internal int Spare(Mobile crier, BaseCreature foe, long now, double need, out double brought)
    {
        brought = 0.0;

        if (foe is not { Deleted: false, Alive: true } || foe.Map != Map || Leader?.Self is not { } lead || crier == null)
        {
            return 0;
        }

        if (_spareFoe is { Deleted: false, Alive: true } && now - _spareTick < SpareMs)
        {
            return 0;
        }

        _spareable.Clear();

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
            var at = _spareable.Count;

            while (at > 0 && _spareable[at - 1].OffFoe > off)
            {
                at--;
            }

            if (at >= MostSpared)
            {
                continue;
            }

            _spareable.Insert(at, (member, body, off, Math.Max(Math.Abs(body.X - crier.X), Math.Abs(body.Y - crier.Y))));

            if (_spareable.Count > MostSpared)
            {
                _spareable.RemoveAt(_spareable.Count - 1);
            }
        }

        if (_spareable.Count == 0)
        {
            SpareRefusedNobody++;

            return 0;
        }

        var enemy = Focus is { Deleted: false, Alive: true } focus ? Reckon(focus) : 0.0;
        var kept = Able();
        var count = 0;

        for (var i = 0; i < _spareable.Count; i++)
        {
            var power = BotThreat.Power(_spareable[i].Body);

            if (enemy > SpareShare * Math.Max(1.0, kept - power))
            {
                break;
            }

            kept -= power;
            count++;

            if (_spareable[i].OffCrier > BotMobile.NoticeRange || BotThreat.Held(_spareable[i].Body, foe))
            {
                brought += power;
            }

            if (brought >= need)
            {
                break;
            }
        }

        if (count == 0)
        {
            SpareRefusedKept++;

            return 0;
        }

        if (brought < need)
        {
            SpareRefusedShort++;
            brought = 0.0;

            return 0;
        }

        var sent = 0;

        for (var i = 0; i < count; i++)
        {
            sent += Send(_spareable[i].Member, foe);
        }

        if (sent > 0)
        {
            _spareFoe = foe;
            _spareFor = crier;
            _spareTick = now;
            SparedTo++;
            Spared += sent;

            BotReflex.Spared(crier);
        }
        else
        {
            brought = 0.0;
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

    public static void ForgetSpares()
    {
        _spareable.Clear();
        Spared = 0;
        SparedTo = 0;
        SpareRefusedFight = 0;
        SpareRefusedDying = 0;
        SpareRefusedBleeding = 0;
        SpareRefusedNobody = 0;
        SpareRefusedWar = 0;
        SpareRefusedKept = 0;
        SpareRefusedShort = 0;
        SparingBeats = 0;
    }

    public static string DescribeSpares() =>
        $"{SparedTo} dire cries answered by a company sparing {Spared} members from a fight well in hand (its enemy under {SpareShare:P0} of its strength, nobody under {SpareDyingShare:P0}), {SparingBeats} beats the press left them on it; companies asked that would not: {SpareRefusedFight} with a fight not in hand, {SpareRefusedKept} whose fight would not stay in hand without the member nearest, {SpareRefusedDying} with a member under {SpareDyingShare:P0}, {SpareRefusedBleeding} pulling back or mustering, {SpareRefusedWar} at war, {SpareRefusedNobody} with nobody fit within {SpareReach} tiles of the thing, {SpareRefusedShort} with too little to spare for what the thing asks (members already on the crier's side adding nothing){(Spares ? "" : " (off)")}";
}
