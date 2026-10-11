using System;
using System.Collections.Generic;

namespace Server.BotAI.V2;

/// <summary>
/// Places a carrier's walk found no way to, shared by every carrier: a cell of <see cref="Grain"/> tiles that answered "no way"
/// <see cref="Strikes"/> times is struck for <see cref="RestMs"/>, so no third carrier walks there and no meeting is called there
/// (06.10.2026, deliver16).
///
/// <para>
/// <b>Measured on build 407 (an-deliver16).</b> 45 of the 86 deliveries that failed "no way through" were to one row of Britain
/// (x 1280–1440, y 1540–1555, z 20): 160 of 323 on 04.10, 42 of 92 on 405. Every one is a meeting place
/// (<c>BotRendezvous</c>): "Farrin is 60 tiles from Orsik … calls the meeting at (1326, 1549), guarded ground", then "Farrin has
/// dropped (1326, 1549, 20) because it planned 31 roads to one place in 18s without getting anywhere", and five carriers in a row
/// for Orsik's one rib between 07:30 and 07:42. The place is <c>BotHaven.Nearest</c>'s first guarded tile on the line to the
/// town: standable, not refused by the reach map, and not reached by the planner. The carrier's ledger kept the refusal
/// (<c>Beware</c>), and the ledger is the carrier's own; and 55 of 143 "no way"/"could not get nearer" failures went to a buyer
/// that had failed the same way within 30 minutes.
/// </para>
///
/// <para>
/// <b>What reads it.</b> <c>BotHaven.Edge</c> passes a struck tile by, so the haven (the meeting place, and a runner's town) is
/// the next guarded tile further in; <c>BotRendezvous</c> calls no meeting on a struck place (they walk towards each other); the
/// courier passes a buyer standing in a struck cell over (<see cref="BotPass.Sealed"/>, counted apart). A hand-over in a cell
/// clears it: the way exists.
/// </para>
/// </summary>
public static class BotWayless
{
    public static bool Running { get; set; } = true;

    public static int Grain { get; set; } = 8;

    public static int Strikes { get; set; } = 2;

    public static int RestMs { get; set; } = 1800000;

    private sealed class Cell
    {
        public int Count;

        public long Tick;

        public bool Struck;
    }

    private static readonly Dictionary<(int Map, int X, int Y, bool Place), Cell> _cells = [];

    private static readonly List<(int Map, int X, int Y, bool Place)> _gone = [];

    public static long Filed { get; private set; }

    public static long Struck { get; private set; }

    public static long Cleared { get; private set; }

    public static long CarrierPassed { get; private set; }

    public static long MeetingsMoved { get; private set; }

    public static long HavenPassed { get; private set; }

    public static long Rejoined { get; private set; }

    private static (int, int, int, bool) Key(Map map, Point3D where, bool place)
    {
        var grain = Math.Max(1, Grain);

        return (map.MapID, where.X / grain, where.Y / grain, place);
    }

    public static void NoWay(Map map, Point3D where, bool place)
    {
        if (!Running || map == null || map == Map.Internal || where == Point3D.Zero)
        {
            return;
        }

        var now = Core.TickCount;

        if (_cells.Count > 2048)
        {
            Sweep(now);
        }

        var key = Key(map, where, place);

        if (!_cells.TryGetValue(key, out var cell) || now - cell.Tick >= RestMs)
        {
            cell = new Cell();
            _cells[key] = cell;
        }

        Filed++;
        cell.Count++;
        cell.Tick = now;

        if (!cell.Struck && cell.Count >= Math.Max(1, Strikes))
        {
            cell.Struck = true;
            Struck++;
        }
    }

    public static bool Shut(Map map, Point3D where, bool place)
    {
        if (!Running || _cells.Count == 0 || map == null || map == Map.Internal)
        {
            return false;
        }

        var key = Key(map, where, place);

        if (!_cells.TryGetValue(key, out var cell) || !cell.Struck)
        {
            return false;
        }

        if (Core.TickCount - cell.Tick >= RestMs)
        {
            _cells.Remove(key);

            return false;
        }

        return true;
    }

    public static void Reached(Map map, Point3D where)
    {
        if (_cells.Count == 0 || map == null || map == Map.Internal)
        {
            return;
        }

        if (_cells.Remove(Key(map, where, false), out var cell) && cell.Struck)
        {
            Cleared++;
        }

        if (_cells.Remove(Key(map, where, true), out cell) && cell.Struck)
        {
            Cleared++;
        }
    }

    internal static void PassedByCarrier() => CarrierPassed++;

    internal static void MovedMeeting() => MeetingsMoved++;

    internal static void PassedByHaven() => HavenPassed++;

    internal static void Rejoin() => Rejoined++;

    private static void Sweep(long now)
    {
        _gone.Clear();

        foreach (var (key, cell) in _cells)
        {
            if (now - cell.Tick >= RestMs)
            {
                _gone.Add(key);
            }
        }

        for (var i = 0; i < _gone.Count; i++)
        {
            _cells.Remove(_gone[i]);
        }
    }

    public static string Describe() =>
        !Running
            ? "no-way places are not shared (off)"
            : $"no-way places: {Filed} answers filed, {Struck} cells of {Grain} tiles struck at {Strikes} answers for {RestMs / 60000} min, {Cleared} cleared by a hand-over in them; "
              + $"{CarrierPassed} carrier looks passed over a buyer standing in one, {MeetingsMoved} meetings not called on one (they walked towards each other), {HavenPassed} haven tiles passed by for one, "
              + $"{Rejoined} meeting walks that found no way went to the buyer instead of failing";

    public static void Forget()
    {
        _cells.Clear();
        Filed = 0;
        Struck = 0;
        Cleared = 0;
        CarrierPassed = 0;
        MeetingsMoved = 0;
        HavenPassed = 0;
        Rejoined = 0;
    }
}
