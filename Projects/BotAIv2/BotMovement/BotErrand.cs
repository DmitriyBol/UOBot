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

    /// <summary>Where, when the destination stands still.</summary>
    public Point3D Where { get; init; }

    /// <summary>
    /// What, when the destination walks. Null for an ordinary journey.
    ///
    /// A reference rather than a copied position: a monster that is being chased is a monster that is
    /// moving, and a plan drawn to where it was is a plan to an empty tile.
    /// </summary>
    public Mobile Follow { get; init; }

    public BotArrival Arrival { get; init; }

    /// <summary>Why, in words. For the log and for the diagnostics gump — never branched on.</summary>
    public string Reason { get; init; }

    /// <summary>
    /// Whether this errand interrupted another rather than being chosen for its own sake.
    ///
    /// Movement uses it for one thing only: when the queue is full, the deepest <em>ordinary</em> errand is
    /// the one dropped. An interruption is by definition the thing happening now.
    /// </summary>
    public bool Interruption { get; init; }

    /// <summary>Where to walk, this instant.</summary>
    public Point3D Target => Follow != null ? Follow.Location : Where;

    /// <summary>
    /// Whether this errand still means anything.
    ///
    /// A followed mobile that has died or been deleted takes its errand with it — which is precisely how a
    /// "kill that thing" interruption ends and the road underneath it resumes. Movement reports the fact; it
    /// does not decide what follows from it.
    /// </summary>
    public bool Lapsed => Follow != null && (Follow.Deleted || !Follow.Alive || Follow.Map != Map);

    public override string ToString() =>
        Follow != null
            ? $"{Reason} after {Follow.Name} at {Target} ({Arrival})"
            : $"{Reason} to {Where} ({Arrival})";
}
