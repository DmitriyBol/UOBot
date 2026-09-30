using System;

namespace Server.BotAI.V2;

/// <summary>
/// How dangerous a piece of ground is to a bot walking across it, in the four words the planners, the hunt and the
/// dashboard share.
///
/// <para>
/// <b>Ordered, and the order is the meaning.</b> A comparison (<c>level &gt;= Hostile</c>) is how every caller asks "may
/// something attack me here", so the values must never be renumbered.
/// </para>
/// </summary>
public enum BotZoneLevel : byte
{
    Clear = 0,

    Edge = 1,

    Hostile = 2,

    Deadly = 3
}

/// <summary>
/// One zone as the last build left it: where it is, what lives in it, how strong that is against a bot, how sure the shard
/// is of its shape, and who has died in it. Immutable: a build makes new ones and the old ones are dropped whole, so a zone
/// read by a planner on the loop, or by the dashboard's thread, never changes under the reader.
/// </summary>
public sealed class BotZone
{
    public int Id { get; init; }

    public BotZoneLevel Level { get; init; }

    public string Label { get; init; }

    public string Why { get; init; }

    public bool Empty { get; init; }

    public bool Water { get; init; }

    public int CenterX { get; init; }

    public int CenterY { get; init; }

    public int X1 { get; init; }

    public int Y1 { get; init; }

    public int X2 { get; init; }

    public int Y2 { get; init; }

    public int CoreCells { get; init; }

    public int AggroCells { get; init; }

    public int Live { get; init; }

    public int Max { get; init; }

    public int Aggressive { get; init; }

    public int Casters { get; init; }

    public int Converge { get; init; }

    public BotZoneKindCount[] Kinds { get; init; } = [];

    public int OtherKinds { get; init; }

    public double PowerTotal { get; init; }

    public double Worst { get; init; }

    public double Threat { get; init; }

    public double VsBot { get; init; }

    public int DeclaredRange { get; init; }

    public int ObservedRange { get; init; }

    public int AggroRange { get; init; }

    public double Confidence { get; init; }

    public long Samples { get; init; }

    public double AwakeSeconds { get; init; }

    public DateTime LastSeen { get; init; }

    public int Deaths { get; init; }

    public int Haunts { get; init; }

    public Point3D Stand { get; init; }

    public bool StandIsReal { get; init; }

    internal int[] CellList { get; init; }

    public override string ToString() => $"zone {Id} ({BotZones.Word(Level)}, {Label})";
}

/// <summary>One kind of creature in a zone.</summary>
public readonly record struct BotZoneKindCount(string Kind, int Live, int Max, double Power, bool Aggressive, bool Caster);

/// <summary>
/// Everything one build published, swapped in whole through <see cref="BotZones"/>'s one volatile reference. The raster is
/// the answer to "what is here", read per node by the planners; the zones are the answer to "what is that".
/// </summary>
public sealed class BotZoneState
{
    public const int Shift = 2;

    public const int Cell = 1 << Shift;

    public const ushort Fringe = 0x8000;

    public const ushort IndexMask = 0x7FFF;

    public Map Map { get; init; }

    public int Width { get; init; }

    public int Height { get; init; }

    public ushort[] Cells { get; init; }

    public BotZone[] Zones { get; init; } = [];

    public int NextId { get; init; }

    public DateTime At { get; init; }

    public double RefBotPower { get; init; }

    public int Clear { get; init; }

    public int Edge { get; init; }

    public int Hostile { get; init; }

    public int Deadly { get; init; }

    public int WaterZones { get; init; }

    public int Spawners { get; init; }

    public int Creatures { get; init; }

    public int ObservedCells { get; init; }

    public int Kept { get; init; }

    public int Born { get; init; }

    public int DeathsDeadly { get; init; }

    public int DeathsHostile { get; init; }

    public int DeathsEdge { get; init; }

    public int DeathsClear { get; init; }

    public byte[] ZonesUtf8 { get; init; } = "[]"u8.ToArray();

    public double BuildMs { get; init; }

    public double ShapesMs { get; init; }

    public string Phases { get; init; } = "";

    public bool OffLoop { get; init; }

    public int Generation { get; init; }

    public BotZone Owner(int x, int y)
    {
        var cx = x >> Shift;
        var cy = y >> Shift;

        if ((uint)cx >= (uint)Width || (uint)cy >= (uint)Height)
        {
            return null;
        }

        var v = Cells[cy * Width + cx];

        return v == 0 ? null : Zones[(v & IndexMask) - 1];
    }
}
