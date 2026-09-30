using System;

namespace Server.BotAI.V2;

/// <summary>
/// The numbers a build works by, copied off the dials when its input is gathered, so a build running off the loop never reads
/// a property the door may be changing under it.
/// </summary>
public sealed class BotZoneSettings
{
    public int DeclaredMost { get; init; }

    public int LiveRadius { get; init; }

    public int LooseRadius { get; init; }

    public int PassiveRadius { get; init; }

    public int WaterRadius { get; init; }

    public int WaterGroup { get; init; }

    public int ObservedMargin { get; init; }

    public double ObservedWins { get; init; }

    public int PerceptionMost { get; init; }

    public int WaterMeleeReach { get; init; }

    public int WaterCasterReach { get; init; }

    public int RootedReach { get; init; }

    public int ConvergeTiles { get; init; }

    public double Secondary { get; init; }

    public double DeadlyVsBot { get; init; }

    public double EmptyWeight { get; init; }

    public int DeathsPromote { get; init; }

    public int DeathsWindowHours { get; init; }

    public int ConfidentSamples { get; init; }

    public int ClearLeastLive { get; init; }

    public int MostPoints { get; init; }

    public int ClearMostPoints { get; init; }

    public int SeaMostPoints { get; init; }

    public int MostRings { get; init; }

    public int MostKinds { get; init; }

    public double SimplifyTiles { get; init; }

    public int SmoothPasses { get; init; }
}

/// <summary>A creature kind as the build sees it: plain values, no type and no creature.</summary>
public readonly record struct BotZoneKindIn(
    string Name, double Power, bool Aggressive, bool Caster, int Perception, bool Water, bool Rooted
);

/// <summary>One haunt as the build sees it.</summary>
public sealed class BotZoneHauntIn
{
    public int X { get; init; }

    public int Y { get; init; }

    public int Z { get; init; }

    public int Radius { get; init; }

    public int Count { get; init; }

    public bool Running { get; init; }

    public bool InTown { get; init; }

    public int[] Kinds { get; init; } = [];

    public double[] Expected { get; init; } = [];

    public double Confidence { get; init; }

    public double AwakeSeconds { get; init; }

    public long Samples { get; init; }

    public int ObservedRange { get; init; }

    public DateTime LastAwake { get; init; }
}

/// <summary>One living creature where it stands now, with its own traits (an instance may differ from its kind).</summary>
public readonly record struct BotZoneLiveIn(
    int X, int Y, int Kind, int Haunt, bool Awake, float Power, bool Aggressive, bool Caster, int Perception, bool Water, bool Rooted
);

/// <summary>A named place: a town (a point) or a dungeon (a box).</summary>
public readonly record struct BotZonePlaceIn(string Name, int X1, int Y1, int X2, int Y2);

/// <summary>
/// Everything one build needs, gathered on the loop and never touched by it again: plain values and fresh arrays, the
/// previous state (immutable) for matching numbers, and the one back buffer no published state refers to.
/// </summary>
public sealed class BotZoneInput
{
    public Map Map { get; init; }

    public int Width { get; init; }

    public int Height { get; init; }

    public BotZoneSettings Settings { get; init; }

    public BotZoneKindIn[] Kinds { get; init; } = [];

    public BotZoneHauntIn[] Haunts { get; init; } = [];

    public BotZoneLiveIn[] Live { get; init; } = [];

    public int[] Observed { get; init; } = [];

    public float[] ObservedWeight { get; init; } = [];

    public (int X, int Y)[] Deaths { get; init; } = [];

    public BotZonePlaceIn[] Towns { get; init; } = [];

    public BotZonePlaceIn[] Deeps { get; init; } = [];

    public double RefBotPower { get; init; }

    public BotZoneState Previous { get; init; }

    public ushort[] Buffer { get; init; }

    public DateTime At { get; init; }

    public bool OffLoop { get; init; }

    public int Generation { get; init; }
}
