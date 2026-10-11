using System;
using System.Collections.Generic;
using Server.Items;

namespace Server.BotAI.V2;

/// <summary>
/// A buyer whose pack refused a hand-over: marked "no room", passed over by every carrier while it stays so, and pressed to make
/// room (06.10.2026, deliver16).
///
/// <para>
/// <b>Measured on build 407 (<c>session-2026-10-06_06-12.log</c>, 06:12–09:08, an-deliver16).</b> 267 of the 444 failed
/// deliveries (60 %) ended "&lt;buyer&gt;'s pack would not take &lt;kind&gt;", from only 14 buyers (Brokk 72, Hrolf 46, Dorin 43, Greta
/// 35): a pack at the engine's 125 things (Adela, live at 09:20: "at 125 of 125 things", 352 of 390 stones) refuses every lot that
/// needs a place of its own. The refusal was counted (<see cref="BotHandover.PackFull"/>) and nothing read it: the want stayed
/// open, and the next carrier took the same walk 22 seconds later (the median gap between two refusals at one buyer; 202 of 253
/// gaps under two minutes). 160 of the 267 were followed within 20 minutes by an accepted hand-over of the same kind to the same
/// buyer, so the state passes: a mark that lasts <see cref="MarkMs"/>, not an exile.
/// </para>
///
/// <para>
/// <b>What a mark does.</b> The courier passes the buyer over while it is marked and its pack still shows no room
/// (<see cref="Shut"/>; <see cref="BotPass.Room"/> on the want, so a line that fails names it). The buyer is pressed to make room:
/// its peddler weighs the pack at once, lets go of what was held for a shift at the shop, most places first
/// (<c>BotPeddler</c>), and its porter takes the trip to the bank when the box can take anything (<c>BotPorter</c>). The mark
/// clears the moment the pack shows <see cref="Places"/> free places and room for the refused lot's weight
/// (<see cref="MadeRoom"/>), or when <see cref="MarkMs"/> runs out (<see cref="Lapsed"/>). The first refusal of each episode is
/// still a failed delivery; nothing here changes what counts.
/// </para>
/// </summary>
public static class BotRoom
{
    public static bool Running { get; set; } = true;

    public static int MarkMs { get; set; } = 600000;

    public static int Places { get; set; } = 3;

    private sealed class Mark
    {
        public long Tick;

        public int Weight;
    }

    private static readonly Dictionary<Serial, Mark> _marks = [];

    private static readonly List<Serial> _gone = [];

    public static long Marks { get; private set; }

    public static long HeldOff { get; private set; }

    public static long MadeRoom { get; private set; }

    public static long Lapsed { get; private set; }

    public static long Fell { get; private set; }

    public static long Peddles { get; private set; }

    public static long Trips { get; private set; }

    public static int Standing => _marks.Count;

    internal static void Refused(Mobile buyer, int weight)
    {
        if (!Running || buyer == null)
        {
            return;
        }

        if (_marks.Count > 512)
        {
            Sweep(Core.TickCount);
        }

        if (!_marks.TryGetValue(buyer.Serial, out var mark))
        {
            mark = new Mark();
            _marks[buyer.Serial] = mark;
        }

        mark.Tick = Core.TickCount;
        mark.Weight = Math.Max(mark.Weight, Math.Max(1, weight));
        Marks++;
    }

    public static bool Shut(Mobile buyer)
    {
        if (!Running || buyer == null || _marks.Count == 0 || !_marks.TryGetValue(buyer.Serial, out var mark))
        {
            return false;
        }

        return !Clear(buyer, mark, Core.TickCount);
    }

    internal static void Held() => HeldOff++;

    public static bool Pressing(Mobile bot) => Shut(bot);

    internal static void Peddled() => Peddles++;

    internal static void Tripped() => Trips++;

    private static bool Clear(Mobile buyer, Mark mark, long now)
    {
        if (buyer.Deleted || !buyer.Alive)
        {
            _marks.Remove(buyer.Serial);
            Fell++;

            return true;
        }

        if (HasRoom(buyer, mark.Weight))
        {
            _marks.Remove(buyer.Serial);
            MadeRoom++;

            return true;
        }

        if (now - mark.Tick >= MarkMs)
        {
            _marks.Remove(buyer.Serial);
            Lapsed++;

            return true;
        }

        return false;
    }

    public static bool HasRoom(Mobile buyer, int weight)
    {
        var pack = buyer?.Backpack;

        if (pack == null)
        {
            return false;
        }

        var most = pack.MaxItems;

        if (most > 0 && pack.TotalItems + Math.Max(1, Places) > most)
        {
            return false;
        }

        var heaviest = pack.MaxWeight;

        return heaviest <= 0 || pack.TotalWeight + Math.Max(0, weight) <= heaviest;
    }

    public static int SpecPlaces { get; set; } = 20;

    public static double PackedShare { get; set; } = 0.8;

    public static long Seen { get; private set; }

    public static long Unasked { get; private set; }

    public static long RestockHeld { get; private set; }

    public static long SpecHeld { get; private set; }

    public static long HaulHeld { get; private set; }

    public static long HaulStruck { get; private set; }

    public static long FirstHeld { get; private set; }

    public static long MeetHeld { get; private set; }

    public static long PackedLoads { get; private set; }

    public static int Free(Mobile body)
    {
        var pack = body?.Backpack;

        if (pack == null)
        {
            return 0;
        }

        return pack.MaxItems <= 0 ? int.MaxValue : Math.Max(0, pack.MaxItems - pack.TotalItems);
    }

    public static bool Receives(Mobile body, Type kind, bool coin = false)
    {
        var pack = body?.Backpack;

        if (pack == null)
        {
            return false;
        }

        var free = Free(body);

        if (free >= (coin ? 2 : 1))
        {
            return true;
        }

        var joins = false;
        var piled = false;
        var items = pack.Items;

        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];

            if (item == null || item.Deleted || !item.Stackable)
            {
                continue;
            }

            if (kind != null && item.GetType() == kind && item.Amount < 60000)
            {
                joins = true;
            }
            else if (item is Gold && item.Amount < 60000)
            {
                piled = true;
            }
        }

        var needed = (joins ? 0 : 1) + (coin && !piled ? 1 : 0);

        return free >= needed;
    }

    public static void Note(Mobile body)
    {
        if (!Running || body == null)
        {
            return;
        }

        if (_marks.Count > 512)
        {
            Sweep(Core.TickCount);
        }

        if (_marks.ContainsKey(body.Serial))
        {
            return;
        }

        _marks[body.Serial] = new Mark { Tick = Core.TickCount, Weight = 1 };
        Seen++;
    }

    public static bool Packed(Mobile body)
    {
        var pack = body?.Backpack;

        return pack != null && pack.MaxItems > 0 && pack.TotalItems >= pack.MaxItems * PackedShare;
    }

    public static bool SpecShut(Mobile body)
    {
        if (!Running || SpecPlaces <= 0)
        {
            return false;
        }

        var free = Free(body);

        if (free >= SpecPlaces)
        {
            return false;
        }

        SpecHeld++;

        if (free <= 0)
        {
            Note(body);
        }

        return true;
    }

    internal static void NotAsked() => Unasked++;

    internal static void RestockNotOffered() => RestockHeld++;

    internal static void HaulPassed() => HaulHeld++;

    internal static void HaulOrderStruck() => HaulStruck++;

    internal static void FirstPassed() => FirstHeld++;

    internal static void MeetNotCalled() => MeetHeld++;

    internal static void PackedLoad() => PackedLoads++;

    private static void Sweep(long now)
    {
        _gone.Clear();

        foreach (var (serial, mark) in _marks)
        {
            if (now - mark.Tick >= MarkMs || World.FindMobile(serial) is not { Deleted: false, Alive: true })
            {
                _gone.Add(serial);
            }
        }

        for (var i = 0; i < _gone.Count; i++)
        {
            _marks.Remove(_gone[i]);
            Lapsed++;
        }
    }

    public static string Describe() =>
        !Running
            ? "full packs are not marked (off)"
            : $"full packs: {Marks} refused hand-overs marked their buyer for up to {MarkMs / 60000} min ({Standing} marked now), {HeldOff} carrier looks held off a marked buyer, "
              + $"{Peddles} loads its peddler and {Trips} trips its porter offered to make room, {MadeRoom} marks cleared when the pack showed {Places} free places, {Lapsed} ran out with no room made, {Fell} dropped with the buyer dead or gone; "
              + $"packs at their count read before the walk: {Seen} bots marked by a look, {FirstHeld} carrier looks and {HaulHeld} trader looks passed such a buyer over, {HaulStruck} haul orders struck before the purchase or the walk, {MeetHeld} meeting calls refused, "
              + $"{Unasked} wants not said and {RestockHeld} restocks not offered for a pack that could not take the thing, {SpecHeld} spec stints held for a pack under {SpecPlaces} free places, {PackedLoads} peddler loads out of a pack at {PackedShare:P0} of its places";

    public static void Forget()
    {
        _marks.Clear();
        Marks = 0;
        HeldOff = 0;
        MadeRoom = 0;
        Lapsed = 0;
        Fell = 0;
        Peddles = 0;
        Trips = 0;
        Seen = 0;
        Unasked = 0;
        RestockHeld = 0;
        SpecHeld = 0;
        HaulHeld = 0;
        HaulStruck = 0;
        FirstHeld = 0;
        MeetHeld = 0;
        PackedLoads = 0;
    }
}
