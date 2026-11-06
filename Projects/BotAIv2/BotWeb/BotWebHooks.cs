using System;
using System.Collections.Generic;

namespace Server.BotAI.V2;

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

public sealed record BotDebuggerLine(DateTime At, string Text);

public sealed record BotDebuggerReport(
    string Name,
    int Hue,
    string Charge,
    bool Organiser,
    string Map,
    int X,
    int Y,
    bool Shown,
    string Doing,
    string DoingWhat,
    string Subject,
    DateTime? ThoughtAt,
    string ThoughtKind,
    string Question,
    string Thought,
    string Answer,
    string About,
    double Sure,
    string Asking,
    DateTime? AskingSince,
    long Asked,
    long Findings,
    long Quiet,
    long Reflections,
    long Labels,
    long Echoes,
    long Hops,
    IReadOnlyList<BotDebuggerLine> Feed
);

public static class BotWebHooks
{
    public static Func<BotMindsReport> Minds { get; set; }

    public static Func<IReadOnlyList<BotDebuggerReport>> Debuggers { get; set; }
}
