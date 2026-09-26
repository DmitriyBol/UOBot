namespace Server.BotAI.V2;

/// <summary>What an undertaking wants of the bot at this moment. Four answers and no fifth.</summary>
public enum BotDoingKind
{
    None,

    Walk,

    Work,

    Done,

    Failed
}

/// <summary>
/// One instruction from an undertaking to the decision layer.
///
/// <para>
/// A struct with named constructors rather than four methods on <see cref="BotDeed"/>, so that an
/// undertaking's whole conversation with the brain is one return value. What the brain does with each is
/// fixed and short: walk becomes the bottom of the journey, work becomes nothing at all, and the other two
/// end the undertaking.
/// </para>
/// </summary>
public readonly struct BotDoing
{
    private BotDoing(BotDoingKind kind, Map map, Point3D where, Mobile follow, BotArrival arrival, string note)
    {
        Kind = kind;
        Map = map;
        Where = where;
        Follow = follow;
        Arrival = arrival;
        Note = note;
    }

    public BotDoingKind Kind { get; }

    public Map Map { get; }

    public Point3D Where { get; }

    public Mobile Follow { get; }

    public BotArrival Arrival { get; }

    public string Note { get; }

    public static BotDoing Walk(Map map, Point3D where, BotArrival arrival, string note) =>
        new(BotDoingKind.Walk, map, where, null, arrival, note);

    public static BotDoing Walk(Map map, Mobile follow, BotArrival arrival, string note) =>
        new(BotDoingKind.Walk, map, Point3D.Zero, follow, arrival, note);

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
        && Arrival.Tiles == other.Arrival.Tiles;

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
