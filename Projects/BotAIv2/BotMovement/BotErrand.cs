using System.Collections.Generic;

namespace Server.BotAI.V2;

/// <summary>
/// One thing a bot is trying to get to. A fixed point, or something that walks.
///
/// <para>
/// Movement does not know or care <em>why</em>. An errand to a market stall and an errand to a skeleton
/// that just hit the bot are the same object with different fields, and that is what lets the decision
/// layer put one on top of the other without movement needing an opinion about combat.
/// </para>
/// </summary>
public sealed class BotErrand
{
    public Map Map { get; init; }

    public Point3D Where { get; init; }

    public Mobile Follow { get; init; }

    public BotArrival Arrival { get; init; }

    public string Reason { get; init; }

    public bool Interruption { get; init; }

    public Point3D Target => Follow != null ? Follow.Location : Where;

    internal List<Point3D> Route { get; } = [];

    internal int Leg;

    internal Point3D RouteGoal;

    internal int Reroutes;

    internal bool RouteSpent;

    internal bool Redraw;

    internal Point3D Aim;

    internal bool Unreachable;

    internal bool FromChart;

    internal bool NoWaySaid;

    public bool Lapsed => Follow != null && (Follow.Deleted || !Follow.Alive || Follow.Map != Map);

    public override string ToString() =>
        Follow != null
            ? $"{Reason} after {Follow.Name} at {Target} ({Arrival})"
            : $"{Reason} to {Where} ({Arrival})";
}
