using System.Collections.Generic;

namespace Server.BotAI.V2;

public sealed class BotErrand
{
    public Map Map { get; init; }

    public Point3D Where { get; init; }

    public Mobile Follow { get; init; }

    public BotArrival Arrival { get; init; }

    public string Reason { get; init; }

    public bool Interruption { get; init; }

    public bool KeepsUp { get; init; }

    public Point3D Target => Follow != null ? Follow.Location : Where;

    internal List<Point3D> Route { get; } = [];

    internal int Leg;

    internal Point3D RouteGoal;

    internal int Reroutes;

    internal bool RouteSpent;

    internal bool Redraw;

    internal Point3D Aim;

    internal bool Unreachable;

    internal Point3D Gate;

    internal Point3D GateTo;

    internal bool FromChart;

    internal bool NoWaySaid;

    public bool Lapsed => Follow != null && (Follow.Deleted || !Follow.Alive || Follow.Map != Map);

    public override string ToString() =>
        Follow != null
            ? $"{Reason} after {Follow.Name} at {Target} ({Arrival})"
            : $"{Reason} to {Where} ({Arrival})";
}
