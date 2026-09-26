using System;

namespace Server.BotAI.V2;

/// <summary>
/// What counts as having got there. One definition, used by the planner and by the walker.
///
/// <para>
/// <b>It is a type rather than an <c>int</c> because in the first version it was an <c>int</c>, and that
/// cost.</b> Arrival was decided in two places — <c>BotBrain.Arrived</c> and <c>BotNav.AtGoal</c> — each
/// with its own copy of the rule, and they had to be kept in step by hand. The tolerance itself was a
/// bare number threaded through six layers of call, meaning something different at each: a doorway, a
/// market stall, a creature, a leg of a road. Long journeys quietly used two tiles while everything else
/// used one, and nothing anywhere said why.
/// </para>
///
/// <para>
/// So: the number has a name, the rule has one implementation, and both the search and the step ask the
/// same object the same question. A plan that thinks it has arrived and a bot that thinks it has not is
/// the shape of an infinite loop.
/// </para>
/// </summary>
public readonly struct BotArrival
{
    public const int PersonHeight = 16;

    private BotArrival(int tiles) => Tiles = Math.Max(0, tiles);

    public int Tiles { get; }

    public static BotArrival Exactly => new(0);

    public static BotArrival Beside => new(1);

    public static BotArrival Within(int tiles) => new(tiles);

    public bool Reached(Point3D at, Point3D goal)
    {
        if (Math.Abs(at.X - goal.X) > Tiles || Math.Abs(at.Y - goal.Y) > Tiles)
        {
            return false;
        }

        return Math.Abs(at.Z - goal.Z) < PersonHeight;
    }

    public override string ToString() =>
        Tiles switch
        {
            0 => "on the tile",
            1 => "beside it",
            _ => $"within {Tiles} tiles"
        };
}
