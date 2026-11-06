namespace Server.BotAI.V2;

public enum BotDoingKind
{

    None,

    Walk,

    Work,

    Done,

    Failed
}

public readonly struct BotDoing
{
    private BotDoing(BotDoingKind kind, Map map, Point3D where, Mobile follow, BotArrival arrival, string note, bool keepsUp = false)
    {
        Kind = kind;
        Map = map;
        Where = where;
        Follow = follow;
        Arrival = arrival;
        Note = note;
        KeepsUp = keepsUp;
    }

    public BotDoingKind Kind { get; }

    public Map Map { get; }

    public Point3D Where { get; }

    public Mobile Follow { get; }

    public BotArrival Arrival { get; }

    public string Note { get; }

    public bool KeepsUp { get; }

    public static BotDoing Walk(Map map, Point3D where, BotArrival arrival, string note) =>
        new(BotDoingKind.Walk, map, where, null, arrival, note);

    public static BotDoing Walk(Map map, Mobile follow, BotArrival arrival, string note) =>
        new(BotDoingKind.Walk, map, Point3D.Zero, follow, arrival, note);

    public static BotDoing WalkAfter(Map map, Mobile follow, BotArrival arrival, string note) =>
        new(BotDoingKind.Walk, map, Point3D.Zero, follow, arrival, note, true);

    public static BotDoing Work(string note) =>
        new(BotDoingKind.Work, null, Point3D.Zero, null, BotArrival.Beside, note);

    public static BotDoing Done(string note) =>
        new(BotDoingKind.Done, null, Point3D.Zero, null, BotArrival.Beside, note);

    public static BotDoing Failed(string note) =>
        new(BotDoingKind.Failed, null, Point3D.Zero, null, BotArrival.Beside, note);

    public bool Matches(BotDoing other) =>
        Kind == other.Kind
        && Map == other.Map
        && Where == other.Where
        && ReferenceEquals(Follow, other.Follow)
        && Arrival.Tiles == other.Arrival.Tiles
        && KeepsUp == other.KeepsUp;

    public override string ToString() =>
        Kind switch
        {
            BotDoingKind.Walk => Follow != null
                ? $"walk after {Follow.Name} ({Note})"
                : $"walk to {Where} ({Note})",
            BotDoingKind.Work => $"work here ({Note})",
            BotDoingKind.Done => $"done ({Note})",
            BotDoingKind.Failed => $"failed ({Note})",
            _ => "nothing"
        };
}
