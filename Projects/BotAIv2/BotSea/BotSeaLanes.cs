using System;
using System.Collections.Generic;
using Server.Logging;

namespace Server.BotAI.V2;

/// <summary>A way by sea between two docks: its course, how long it is, and how often it has been sailed.</summary>
public sealed class BotSeaLane
{
    public BotDock A;

    public BotDock B;

    public List<int> Turns;

    public int Tiles;

    public long Sailed;

    public BotDock Other(BotDock one) => one == A ? B : A;

    public List<Point3D> Course(BotDock from, out int sea, out int harbour) => Course(from, null, out sea, out harbour);

    public List<Point3D> Course(BotDock from, BotBerth berth, out int sea, out int harbour)
    {
        var to = Other(from);
        List<Point3D> course = [];

        course.AddRange(berth?.Harbour ?? from.Harbour);
        sea = course.Count;

        if (berth?.Link != null)
        {
            for (var i = 0; i < berth.Link.Count - 1; i++)
            {
                course.Add(BotSeaChart.Middle(berth.Link[i]));
            }
        }

        if (from == A)
        {
            for (var i = 0; i < Turns.Count; i++)
            {
                course.Add(BotSeaChart.Middle(Turns[i]));
            }
        }
        else
        {
            for (var i = Turns.Count - 1; i >= 0; i--)
            {
                course.Add(BotSeaChart.Middle(Turns[i]));
            }
        }

        harbour = course.Count;

        for (var i = to.Harbour.Count - 1; i >= 0; i--)
        {
            course.Add(to.Harbour[i]);
        }

        return course;
    }

    public override string ToString() => $"{A.Name}–{B.Name}";
}

/// <summary>
/// The ways by sea between the docks that no road joins, drawn at boot a slice at a time, and which towns the population
/// can therefore sail to from home.
///
/// <para>
/// <b>Only where no road goes.</b> A pair of docks the gates already join (<see cref="BotGates.Joined"/>) is walked, as it
/// was before; a lane is drawn for every other pair, so an island is joined to every mainland dock and to every other
/// island. Measured on map0 with the same rules (29.09.2026, Python): all 78 pairs of thirteen docks joined by the open sea,
/// Britain to Moonglow 3,814 tiles, to Jhelom 3,405, to Skara Brae 5,568 — round the south of the continent, which is why
/// a bot sets out from the dock that makes the whole trip shortest, walk and voyage together (<see cref="BotSea.Plan"/>),
/// and Yew's is 1,653 from Skara Brae's.
/// </para>
///
/// <para>
/// <b>A town becomes reachable by sea</b> (<c>BotTowns.Town.BySea</c>) when its dock has a lane to a dock that home can be
/// walked to: it is offered to travellers, and the ground round it is the population's to work (<c>BotTowns.Within</c>).
/// </para>
/// </summary>
public static class BotSeaLanes
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotSeaLanes));

    private static readonly List<BotSeaLane> _lanes = [];

    private static readonly List<(BotDock A, BotDock B, BotSeaCourse.Ask Ask)> _drawing = [];

    private static bool _asked;

    public static bool Ready { get; private set; }

    public static IReadOnlyList<BotSeaLane> All => _lanes;

    public static int Walked { get; private set; }

    public static int NoWay { get; private set; }

    public static int SeaTowns { get; private set; }

    public static bool Step(Map map)
    {
        if (Ready)
        {
            return true;
        }

        if (!BotDocks.Ready || !BotDocks.Link(map))
        {
            return false;
        }

        if (!_asked)
        {
            _asked = true;
            Ask(map);
        }

        for (var i = 0; i < _drawing.Count; i++)
        {
            if (!_drawing[i].Ask.Done)
            {
                return false;
            }
        }

        BotSeaLane longest = null;

        for (var i = 0; i < _drawing.Count; i++)
        {
            var (a, b, ask) = _drawing[i];

            if (!ask.Found)
            {
                NoWay++;

                logger.Information("Sea: no way by sea between {A} and {B}", a.Name, b.Name);

                continue;
            }

            var lane = new BotSeaLane { A = a, B = b, Turns = ask.Turns, Tiles = a.HarbourTiles + ask.Tiles + b.HarbourTiles };

            _lanes.Add(lane);

            if (longest == null || lane.Tiles > longest.Tiles)
            {
                longest = lane;
            }
        }

        _drawing.Clear();
        Ready = true;

        Mark(map);

        logger.Information(
            "Sea: {Lanes} lanes drawn between {Docks} docks, {Walked} pairs joined by road and left to it, {NoWay} with no way by sea; the longest {Longest}: {Tiles} tiles",
            _lanes.Count,
            BotDocks.All.Count,
            Walked,
            NoWay,
            longest?.ToString() ?? "none",
            longest?.Tiles ?? 0
        );

        return true;
    }

    private static void Ask(Map map)
    {
        var docks = BotDocks.All;

        for (var i = 0; i < docks.Count; i++)
        {
            for (var j = i + 1; j < docks.Count; j++)
            {
                if (BotGates.Joined(map, docks[i].Shore, docks[j].Shore))
                {
                    Walked++;

                    continue;
                }

                var ask = new BotSeaCourse.Ask { From = docks[i].Entry, To = docks[j].Entry, For = $"{docks[i].Name}–{docks[j].Name}" };

                _drawing.Add((docks[i], docks[j], ask));
                BotSeaCourse.Enqueue(ask);
            }
        }
    }

    private static void Mark(Map map)
    {
        var home = BotPopulation.Where;
        var towns = BotTowns.All;
        using var names = Server.Text.ValueStringBuilder.Create(256);

        SeaTowns = 0;

        for (var i = 0; i < towns.Count; i++)
        {
            var town = towns[i];
            var dock = BotDocks.Of(town);

            town.BySea = false;

            if (town.FromHome || dock == null)
            {
                continue;
            }

            for (var j = 0; j < _lanes.Count; j++)
            {
                var lane = _lanes[j];

                if ((lane.A == dock || lane.B == dock) && BotGates.Joined(map, lane.Other(dock).Shore, home))
                {
                    town.BySea = true;

                    break;
                }
            }

            if (!town.BySea)
            {
                continue;
            }

            SeaTowns++;
            names.Append(names.Length > 0 ? ", " : "");
            names.Append(town.Name);
        }

        logger.Information("Towns: {Sea} more can be sailed to from home: {Names}", SeaTowns, SeaTowns > 0 ? names.ToString() : "none");
    }

    public static BotSeaLane Between(BotDock a, BotDock b)
    {
        if (a == null || b == null || a == b)
        {
            return null;
        }

        for (var i = 0; i < _lanes.Count; i++)
        {
            var lane = _lanes[i];

            if (lane.A == a && lane.B == b || lane.A == b && lane.B == a)
            {
                return lane;
            }
        }

        return null;
    }

    public static string Describe()
    {
        if (!Ready)
        {
            return _asked ? $"lanes being drawn: {_drawing.Count} asked" : "no lanes drawn yet";
        }

        long sailed = 0;
        BotSeaLane most = null;

        for (var i = 0; i < _lanes.Count; i++)
        {
            sailed += _lanes[i].Sailed;

            if (most == null || _lanes[i].Sailed > most.Sailed)
            {
                most = _lanes[i];
            }
        }

        return $"{_lanes.Count} lanes, {NoWay} pairs with no way, {SeaTowns} towns reachable only by sea; sailed {sailed} times{(most is { Sailed: > 0 } ? $", {most} the most ({most.Sailed})" : "")}";
    }

    public static void Forget()
    {
        _lanes.Clear();
        _drawing.Clear();
        _asked = false;
        Ready = false;
        Walked = 0;
        NoWay = 0;
        SeaTowns = 0;
    }
}
