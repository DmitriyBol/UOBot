using System;
using System.Collections.Generic;
using Server.Items;

namespace Server.BotAI.V2;

/// <summary>
/// Where each member of a squad ought to be standing, worked out rather than assigned.
///
/// <para>
/// <b>Nobody is told anything.</b> Every member computes every station from the same three facts — the
/// roster, the anchor and the threat axis — in the same order, and therefore arrives at the same answer.
/// This is the one genuinely good idea the first version had about collective behaviour, and it came out of
/// spreading bots over a hunting ground: order by serial, cut the ground into a grid, take the cell at your
/// own index. No messages, so no desynchronisation, no orphaned assignments, and two bots never pick the
/// same patch. A shared mind does not need a shared mailbox; it needs shared arithmetic.
/// </para>
///
/// <para>
/// <b>The shape is not fixed — the order is.</b> A formation can be any shape at all, and trying to specify
/// one would be specifying the wrong thing. What must hold is the ordering along the line to the threat:
/// blades in front, bows behind them, casters and healers behind those, everybody else at the back. That is
/// a single number per role, and every arrangement that satisfies it is acceptable.
/// </para>
///
/// <para>
/// <b>Built from the leader, forward of the leader.</b> If the leader is an archer, the melee ring is still
/// in front — in front of <em>him</em>. The anchor is whoever the squad is organised around, not whoever
/// happens to be closest to the enemy.
/// </para>
/// </summary>
public static class BotFormation
{
    public static int RingFor(BotRole role) =>
        role switch
        {
            BotRole.Melee => 2,
            BotRole.Ranged => 0,
            BotRole.Caster => -1,
            BotRole.Medic => -1,
            _ => -2
        };

    public const int FileSpacing = 2;

    public const int MaxSpread = 5;

    public static int PressRingFor(BotRole role) =>
        role switch
        {
            BotRole.Melee => Contact,
            BotRole.Ranged => 5,
            BotRole.Caster => 7,
            BotRole.Medic => 7,
            _ => 9
        };

    public const int Contact = 1;

    public static BotRole RoleOf(IBotSquadMember member)
    {
        var role = member?.Class?.Role ?? BotRole.Melee;

        if (role != BotRole.Producer)
        {
            return role;
        }

        var held = member.Self?.Weapon;

        if (held is null or Fists)
        {
            return role;
        }

        return held.MaxRange > Contact ? BotRole.Ranged : BotRole.Melee;
    }

    private static readonly (int X, int Y)[] Compass =
    [
        (0, -1), (1, -1), (1, 0), (1, 1), (0, 1), (-1, 1), (-1, 0), (-1, -1)
    ];

    private static readonly int[] Fan = [0, 1, -1, 2, -2, 3, -3, 4];

    private static readonly List<IBotSquadMember> _peers = [];

    public static Point3D StationFor(BotSquad squad, IBotSquadMember member)
    {
        if (squad == null || member?.Self == null)
        {
            return Point3D.Zero;
        }

        var map = squad.Map;

        if (map == null || map == Map.Internal)
        {
            return Point3D.Zero;
        }

        var anchor = squad.Anchor;
        var role = RoleOf(member);

        _peers.Clear();

        var members = squad.Members;

        for (var i = 0; i < members.Count; i++)
        {
            if (RoleOf(members[i]) == role)
            {
                _peers.Add(members[i]);
            }
        }

        _peers.Sort(static (a, b) => a.Self.Serial.Value.CompareTo(b.Self.Serial.Value));

        var file = _peers.IndexOf(member);

        if (file < 0)
        {
            return Point3D.Zero;
        }

        if (squad.Stance == BotSquadStance.Fighting && squad.Focus is { Deleted: false, Alive: true })
        {
            return PressStation(map, squad, member, role, file);
        }

        var lateral = (file + 1) / 2 * (file % 2 == 0 ? 1 : -1) * FileSpacing;
        var ring = RingFor(role);

        var (fx, fy) = squad.Axis;

        var (rx, ry) = (-fy, fx);

        var x = anchor.X + fx * ring + rx * lateral;
        var y = anchor.Y + fy * ring + ry * lateral;

        return Reachable(map, x, y, anchor, member);
    }

    private static Point3D PressStation(Map map, BotSquad squad, IBotSquadMember member, BotRole role, int file)
    {
        var at = squad.Focus.Location;
        var (ax, ay) = squad.Axis;

        var turn = file + squad.Attempt;

        var face = Bearing(ax, ay);

        if (role == BotRole.Melee)
        {
            for (var i = 0; i < Compass.Length; i++)
            {
                var (dx, dy) = Compass[((face + Fan[Math.Abs(turn + i) % Fan.Length]) % 8 + 8) % 8];

                if (Stand(map, at.X + dx, at.Y + dy, at, out var spot))
                {
                    return spot;
                }
            }

            return Point3D.Zero;
        }

        var ring = PressRingFor(role);

        var hand = turn % 2 == 0 ? 2 : -2;
        var (fx, fy) = Compass[((face + hand) % 8 + 8) % 8];

        var wider = turn / 2;
        var (wx, wy) = Compass[((face + hand + (hand > 0 ? wider : -wider)) % 8 + 8) % 8];

        for (var back = 0; ring - back >= 2; back++)
        {
            var r = ring - back;

            if (Stand(map, at.X + wx * r, at.Y + wy * r, at, out var spot))
            {
                return spot;
            }

            if (Stand(map, at.X + fx * r, at.Y + fy * r, at, out spot))
            {
                return spot;
            }
        }

        return Point3D.Zero;
    }

    private const int Eye = 14;

    private static bool Sighted(Map map, int x, int y, int z, Point3D at) =>
        map.LineOfSight(new Point3D(x, y, z + Eye), new Point3D(at.X, at.Y, at.Z + Eye));

    public static int PressReach { get; set; } = 8;

    private static bool Stand(Map map, int x, int y, Point3D near, out Point3D spot)
    {
        if (BotStep.Ground(map, x, y, near.Z, PressReach, out var z) && Sighted(map, x, y, z, near))
        {
            spot = new Point3D(x, y, z);

            return true;
        }

        spot = Point3D.Zero;

        return false;
    }

    private static int Bearing(int x, int y)
    {
        for (var i = 0; i < Compass.Length; i++)
        {
            if (Compass[i].X == x && Compass[i].Y == y)
            {
                return i;
            }
        }

        return 0;
    }

    public static long Uncomponented { get; private set; }

    private static bool OtherComponent(Map map, Point3D from, Point3D to)
    {
        if (!Server.Engines.Pathing.Tiered.NavigationService.ComponentsCounted(map))
        {
            return false;
        }

        var a = Server.Engines.Pathing.Tiered.NavigationService.ComponentOf(map, from);
        var b = Server.Engines.Pathing.Tiered.NavigationService.ComponentOf(map, to);

        return a >= 0 && b >= 0 && a != b;
    }

    internal static Point3D Reachable(Map map, int x, int y, Point3D anchor, IBotSquadMember member)
    {
        var steps = Math.Max(Math.Abs(x - anchor.X), Math.Abs(y - anchor.Y));

        for (var back = 0; back <= steps; back++)
        {
            var cx = x + Math.Sign(anchor.X - x) * back;
            var cy = y + Math.Sign(anchor.Y - y) * back;

            if (!BotStep.Ground(map, cx, cy, anchor.Z, BotStep.StandingReach, out var z))
            {
                continue;
            }

            var candidate = new Point3D(cx, cy, z);

            if (BotReach.Ask(map, member.Self.Location, candidate, BotArrival.Within(2)) == BotReachVerdict.Sealed)
            {
                continue;
            }

            if (OtherComponent(map, member.Self.Location, candidate))
            {
                Uncomponented++;

                continue;
            }

            if (BotPath.CanReach(map, member.Self.Location, candidate, BotArrival.Exactly))
            {
                return candidate;
            }
        }

        if (BotPath.CanReach(map, member.Self.Location, anchor, BotArrival.Within(1)))
        {
            return anchor;
        }

        Unanchored++;

        return member.Self.Location;
    }

    public static long Unanchored { get; private set; }

    public static bool OutranksFor(IBotSquadMember asker, IBotSquadMember holder) =>
        asker?.Class != null
        && holder?.Class != null
        && RingFor(asker.Class.Role) > RingFor(holder.Class.Role);
}
