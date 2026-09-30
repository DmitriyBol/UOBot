using System.Collections.Generic;

namespace Server.BotAI.V2;

/// <summary>
/// One meeting: two guilds, or a guild and a town, a place, an envoy, a host, a witness, what was said and what came of it.
///
/// <para>
/// <b>A meeting is a thing with a place and a clock, not a function call.</b> Until 29.09.2026 a war was declared the instant
/// an opinion crossed a number; this object is the time between the crossing and the war — the walk to the other guild's
/// town, the wait for somebody to answer, the hearing and the word — and every stage of it is a field somebody can read on the
/// page and in the log. It is kept in memory only: a restart ends every meeting in flight, and the grievance that called it
/// calls it again at its next drift (<c>BotRegard.Drift</c>).
/// </para>
/// </summary>
public sealed class BotHearing
{
    public int Id { get; init; }

    public BotTopic Topic { get; init; }

    public string Guest { get; init; }

    public string Host { get; init; }

    public BotTowns.Town Town { get; init; }

    public Map Map { get; init; }

    public Point3D Place { get; init; }

    public string Why { get; init; }

    public BotHearingStage Stage { get; set; }

    public long CalledTick { get; init; }

    public long StageTick { get; set; }

    public long ArrivedTick { get; set; }

    public long HearingTick { get; set; }

    public BotMobile Envoy { get; set; }

    public long EnvoyTick { get; set; }

    public bool HostWanted { get; set; }

    public BotMobile HostRep { get; set; }

    public long HostTick { get; set; }

    public bool HostAbsent { get; set; }

    public HashSet<Serial> Turned { get; } = [];

    public Mobile Witness { get; set; }

    public string WitnessName { get; set; }

    public bool Unwitnessed { get; set; }

    public List<(BotSpeaker Who, string Line)> Lines { get; } = [];

    public int Said { get; set; }

    public long SaidTick { get; set; }

    public string[] Allowed { get; set; }

    public string Brief { get; set; }

    public List<BotTownTask> Tasks { get; set; }

    public BotTownReport Report { get; set; }

    public bool Asked { get; set; }

    public bool ModelFailed { get; set; }

    public BotVerdict Verdict { get; set; }

    public string JudgedBy { get; set; }

    public bool Heard { get; set; }

    public string Ending { get; set; }

    public string Applied { get; set; }

    public long OverTick { get; set; }

    public bool Between => Topic != BotTopic.Town;

    public string Tell() =>
        $"#{Id} {Guest} {(Between ? $"to {Host}" : "to the town")} at {Town?.Name ?? "the wild"} ({Place.X},{Place.Y}) over {BotDuke.Word(Topic)}: {Stage.ToString().ToLowerInvariant()}"
        + (Envoy != null ? $", envoy {Envoy.Name}" : "")
        + (HostRep != null ? $", host {HostRep.Name}" : "")
        + (WitnessName != null ? $", witness {WitnessName}" : Unwitnessed ? ", unwitnessed" : "")
        + (Ending != null ? $" — {Ending}" : "");
}

/// <summary>What a meeting is about, which decides who walks, who waits, and what the duke may answer.</summary>
public enum BotTopic
{
    Grievance,

    Safety,

    Friendship,

    Commerce,

    Town
}

/// <summary>Where a meeting has got to.</summary>
public enum BotHearingStage
{
    Called,

    Walking,

    Gathered,

    Hearing,

    Awaiting,

    Verdict,

    Over
}

/// <summary>What the duke — or the town — answered: one outcome out of those allowed, and the words said for it.</summary>
public sealed class BotVerdict
{
    public string Outcome { get; init; }

    public string Speech { get; init; }

    public string Reason { get; init; }

    public string Report { get; init; }
}

/// <summary>Who says a line of a meeting.</summary>
public enum BotSpeaker
{
    Witness,
    Envoy,
    Host
}
