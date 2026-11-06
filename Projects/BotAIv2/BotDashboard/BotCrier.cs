namespace Server.BotAI.V2;

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

public static class BotCrier
{

    public static System.Func<BotNotice> Posted { get; set; }

    public static BotNotice Read() => Posted?.Invoke();

    public static System.Func<System.Collections.Generic.IReadOnlyList<(string Name, Map Map, Point3D At)>> Watchers { get; set; }

    public static System.Collections.Generic.IReadOnlyList<(string Name, Map Map, Point3D At)> Squad() =>
        Watchers?.Invoke() ?? System.Array.Empty<(string, Map, Point3D)>();
}
