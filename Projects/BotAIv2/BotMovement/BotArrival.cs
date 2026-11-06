using System;

namespace Server.BotAI.V2;

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
