using System;
using System.Collections.Generic;

namespace Server.BotAI.V2;

public static class BotScatter
{

    public static int KnotSize { get; set; } = 3;

    public static int Spread { get; set; } = 24;

    private const int WithinKnot = 2;

    private static readonly (int X, int Y)[] Compass =
    [
        (0, -1),
        (1, -1),
        (1, 0),
        (1, 1),
        (0, 1),
        (-1, 1),
        (-1, 0),
        (-1, -1)
    ];

    private static readonly List<IBotSquadMember> _order = [];

    private static readonly Dictionary<IBotSquadMember, int> _seats = [];

    private static BotSquad _orderedFor;

    private static int _orderedAt;

    public static Point3D PatchFor(BotSquad squad, IBotSquadMember member)
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

        if (!ReferenceEquals(_orderedFor, squad) || _orderedAt != squad.Roster)
        {
            _orderedFor = squad;
            _orderedAt = squad.Roster;
            _order.Clear();
            _seats.Clear();

            var members = squad.Members;

            for (var i = 0; i < members.Count; i++)
            {
                if (members[i]?.Self != null)
                {
                    _order.Add(members[i]);
                }
            }

            _order.Sort(static (a, b) => a.Self.Serial.Value.CompareTo(b.Self.Serial.Value));

            for (var i = 0; i < _order.Count; i++)
            {
                _seats[_order[i]] = i;
            }
        }

        if (!_seats.TryGetValue(member, out var seat))
        {
            return Point3D.Zero;
        }

        var size = Math.Max(2, KnotSize);
        var knots = (_order.Count + size - 1) / size;
        var knot = seat / size;
        var within = seat % size;

        var anchor = squad.Anchor;

        var line = Compass[knot * Compass.Length / Math.Max(1, knots) % Compass.Length];

        var x = anchor.X + line.X * Spread;
        var y = anchor.Y + line.Y * Spread;

        if (within > 0)
        {
            var side = within % 2 == 1 ? 1 : -1;
            var step = (within + 1) / 2 * WithinKnot * side;

            x += -line.Y * step;
            y += line.X * step;
        }

        return BotFormation.Reachable(map, x, y, anchor, member);
    }
}
