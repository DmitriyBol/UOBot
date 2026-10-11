using System;
using System.Collections.Generic;
using Server.Items;

namespace Server.BotAI.V2;

/// <summary>
/// One guild's colony on an island: the square it landed on, the store its colonists fill, who is on its land and since
/// when, and what its ship has carried home. See <see cref="BotColonies"/>.
/// </summary>
public sealed class BotColony
{
    public string Guild;

    public string Island;

    public Map Map;

    public Point3D Site;

    public Container Store;

    public Serial StoreSerial;

    public DateTime FoundedAt;

    public long Since;

    public bool Claimed;

    public long ClaimTried;

    public int ClaimTries;

    public long LastSeen;

    public long LastRun;

    public readonly Dictionary<Serial, long> Shift = [];

    public int Colonists;

    public long OreIn;

    public long IngotsIn;

    public long LogsIn;

    public long ColonistMs;

    public long ColonistMinutes => ColonistMs / 60000;

    public long Runs;

    public long CargoUnits;

    public long CargoGold;

    public long ChestGold;

    public long CrewGold;

    public long CaptainGold;

    public long Plundered;

    public string Last;

    public (int Map, int X, int Y) Key => BotQuad.Key(Map, Site);

    public int Stock()
    {
        if (Store is not { Deleted: false })
        {
            return 0;
        }

        return Store.GetAmount(typeof(BaseOre)) + Store.GetAmount(typeof(BaseIngot)) + Store.GetAmount(typeof(Log))
               + Store.GetAmount(typeof(Board));
    }

    public double YieldPerHour => ColonistMs <= 0 ? 0.0 : (OreIn + IngotsIn + LogsIn) * 3600000.0 / ColonistMs;

    public override string ToString() => $"{Guild}'s colony on {Island} at ({Site.X}, {Site.Y})";
}
