namespace Server.BotAI.V2;

public readonly struct BotAvoid
{
    private readonly int _tileX;
    private readonly int _tileY;
    private readonly int _x1;
    private readonly int _y1;
    private readonly int _x2;
    private readonly int _y2;
    private readonly Map _towns;
    private readonly BotReaches _reaches;

    private BotAvoid(int tileX, int tileY, int x1, int y1, int x2, int y2, Map towns = null, BotReaches reaches = null)
    {
        _tileX = tileX;
        _tileY = tileY;
        _x1 = x1;
        _y1 = y1;
        _x2 = x2;
        _y2 = y2;
        _towns = towns;
        _reaches = reaches is { Count: > 0 } ? reaches : null;

        HasTile = tileX >= 0;
        HasSquare = x2 >= x1 && x1 >= 0;
    }

    public static BotAvoid None => new(-1, -1, -1, -1, -1, -1);

    public bool HasTile { get; }

    public bool HasSquare { get; }

    public bool Empty => !HasTile && !HasSquare && _towns == null && _reaches == null;

    public bool HasReaches => _reaches != null;

    public bool HasTowns => _towns != null;

    public static BotAvoid Tile(Point3D where) => new(where.X, where.Y, -1, -1, -1, -1);

    public static BotAvoid Square(int x1, int y1, int x2, int y2) => new(-1, -1, x1, y1, x2, y2);

    public BotAvoid And(Point3D tile) => new(tile.X, tile.Y, _x1, _y1, _x2, _y2, _towns, _reaches);

    public BotAvoid And(int x1, int y1, int x2, int y2) => new(_tileX, _tileY, x1, y1, x2, y2, _towns, _reaches);

    public BotAvoid Reaches(BotReaches reaches) => new(_tileX, _tileY, _x1, _y1, _x2, _y2, _towns, reaches);

    public int Penalty(int x, int y) => _reaches?.Penalty(x, y) ?? 0;

    public bool InReach(int x, int y) => _reaches?.Within(x, y, 0) == true;

    public BotAvoid Towns(Map map) => new(_tileX, _tileY, _x1, _y1, _x2, _y2, map, _reaches);

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
