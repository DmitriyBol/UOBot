using System;

namespace Server.Engines.Pathing.Tiered;

/// <summary>
/// The one cost model every tier of the navigation shares, in integer "ticks" of a step.
///
/// <para>
/// A step takes the same time straight or diagonal (the engine's movement delay does not depend on the direction), so
/// the distance a walker covers in a given time is Chebyshev, not octile. A diagonal costs one tick more than a straight
/// step only to break ties: at equal time the straighter path zig-zags less. Every tier prices a step the same way, or
/// the tiers argue and the path jitters between them.
/// </para>
/// </summary>
public static class NavCost
{
    public const int Step = 100;

    public const int Unaffordable = int.MaxValue / 4;

    public const int DiagonalTie = 1;

    public const int Door = 200;

    public static int Of(int direction) => (direction & 1) == 0 ? Step : Step + DiagonalTie;

    public static int Estimate(int x1, int y1, int x2, int y2) =>
        Step * Math.Max(Math.Abs(x1 - x2), Math.Abs(y1 - y2));
}
