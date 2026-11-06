using System;

namespace Server.Engines.Pathing.Tiered;

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
