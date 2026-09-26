namespace Server.BotAI.V2;

/// <summary>
/// What the watcher has announced, in a shape this assembly can read.
///
/// <para>
/// <b>A seam, for the same reason <see cref="BotAppraisal.Revelry"/> is one.</b> Argus lives in the minds
/// assembly, which references this one and must never be referenced back — so the dashboard cannot ask him
/// anything. He posts here instead, and the window reads a notice rather than a subsystem.
/// </para>
///
/// <para>
/// Everything on it is a copy taken at the moment it was asked for. A notice that held live references would
/// be a window showing a revel that ended while somebody was reading it.
/// </para>
/// </summary>
public sealed class BotNotice
{
    public bool Running { get; init; }

    public string Kind { get; init; }

    public int Prize { get; init; }

    public double Bonus { get; init; }

    public string Said { get; init; }

    public string Why { get; init; }

    public long EndsIn { get; init; }

    public long NextIn { get; init; }

    public int Entered { get; init; }

    public string Leading { get; init; }

    public int LeadingDid { get; init; }

    public Map Map { get; init; }

    public Point3D Where { get; init; }

    public bool Camp { get; init; }

    public Map WatcherMap { get; init; }

    public Point3D Watcher { get; init; }

    public string WatcherName { get; init; }

    public long Declared { get; init; }

    public long Won { get; init; }

    public long Ignored { get; init; }

    public long Paid { get; init; }

    public int Treasury { get; init; }

    public int Purse { get; init; }

    public long Collected { get; init; }

    public long Bands { get; init; }

    public string[] Past { get; init; }

    public int Wave { get; init; }

    public int Standing { get; init; }

    public string Waves { get; init; }

    public string Ledger { get; init; }
}

/// <summary>
/// Where the watcher pins its notices, and the dashboard reads them.
///
/// <para>
/// Null until the minds assembly is running, which is the honest state of a shard with no watcher on it:
/// the tab then says so rather than showing an empty table that could equally mean "nothing declared".
/// </para>
/// </summary>
public static class BotCrier
{
    public static System.Func<BotNotice> Posted { get; set; }

    public static BotNotice Read() => Posted?.Invoke();

    public static System.Func<System.Collections.Generic.IReadOnlyList<(string Name, Map Map, Point3D At)>> Watchers { get; set; }

    public static System.Collections.Generic.IReadOnlyList<(string Name, Map Map, Point3D At)> Squad() =>
        Watchers?.Invoke() ?? System.Array.Empty<(string, Map, Point3D)>();
}
