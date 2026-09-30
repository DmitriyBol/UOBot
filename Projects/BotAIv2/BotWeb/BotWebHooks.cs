using System;

namespace Server.BotAI.V2;

/// <summary>What the thinking layer says about itself, for the page. Filled in by the minds assembly, which this one may not reference.</summary>
public sealed record BotMindsReport(
    string Model,
    string Endpoint,
    int Awake,
    bool Reachable,
    long Asked,
    long Answered,
    long Refused,
    long LastMs
);

/// <summary>
/// Where the assemblies that depend on this one hang what the dashboard shows about them.
///
/// The same shape as <see cref="BotCrier"/>: a function that is null until somebody sets it, so an absent
/// thinking layer reads as absent rather than as a row of zeros.
/// </summary>
public static class BotWebHooks
{
    public static Func<BotMindsReport> Minds { get; set; }
}
