namespace Server.BotAI.V2;

/// <summary>
/// Ground a single plan is to keep out of. Tactical and short-lived, never a statement about the world.
///
/// Two shapes, because the population produced exactly two reasons:
///
/// <para>
/// <b>One tile, because somebody is standing on it.</b> The measurement was unambiguous: (1371, 1477, 10)
/// is the gate to the Britain graveyard, and two bots parked on it accounted for seventy-seven refused
/// steps in two minutes — twice, on two different days, with two different bots. A bot at an auction or
/// going through a corpse can stand still for minutes, and if it happens to be in a doorway then nobody
/// gets through the doorway. Excluding the tile from the next plan is the difference between walking round
/// a person and asking them to move for the twentieth time.
/// </para>
///
/// <para>
/// <b>One square, because something in it nearly killed the bot.</b> This is what makes a route bend
/// without the destination moving: the goal is untouchable, the way to it is not.
/// </para>
///
/// <para>
/// Neither may ever be recorded as knowledge about the ground. A search that failed because of an avoid
/// has proved nothing — which is why <see cref="BotPath"/> refuses to write a pocket into
/// <see cref="BotReach"/> from any search that carried one.
/// </para>
/// </summary>
public readonly struct BotAvoid
{
    private readonly int _tileX;
    private readonly int _tileY;
    private readonly int _x1;
    private readonly int _y1;
    private readonly int _x2;
    private readonly int _y2;
    private readonly Map _towns;

    private BotAvoid(int tileX, int tileY, int x1, int y1, int x2, int y2, Map towns = null)
    {
        _tileX = tileX;
        _tileY = tileY;
        _x1 = x1;
        _y1 = y1;
        _x2 = x2;
        _y2 = y2;
        _towns = towns;

        HasTile = tileX >= 0;
        HasSquare = x2 >= x1 && x1 >= 0;
    }

    public static BotAvoid None => new(-1, -1, -1, -1, -1, -1);

    public bool HasTile { get; }

    public bool HasSquare { get; }

    public bool Empty => !HasTile && !HasSquare && _towns == null;

    public bool HasTowns => _towns != null;

    public static BotAvoid Tile(Point3D where) => new(where.X, where.Y, -1, -1, -1, -1);

    public static BotAvoid Square(int x1, int y1, int x2, int y2) => new(-1, -1, x1, y1, x2, y2);

    public BotAvoid And(Point3D tile) => new(tile.X, tile.Y, _x1, _y1, _x2, _y2, _towns);

    public BotAvoid And(int x1, int y1, int x2, int y2) => new(_tileX, _tileY, x1, y1, x2, y2, _towns);

    public BotAvoid Towns(Map map) => new(_tileX, _tileY, _x1, _y1, _x2, _y2, map);

    public bool Blocks(int x, int y)
    {
        if (HasTile && _tileX == x && _tileY == y)
        {
            return true;
        }

        if (HasSquare && x >= _x1 && x <= _x2 && y >= _y1 && y <= _y2)
        {
            return true;
        }

        return _towns != null && BotOutlaw.Guarded(_towns, x, y);
    }
}
