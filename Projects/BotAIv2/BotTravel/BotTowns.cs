using System;
using System.Collections.Generic;
using Server.Logging;
using Server.Regions;

namespace Server.BotAI.V2;

/// <summary>
/// The towns of the map, read from its regions, and which of them the population can walk to.
///
/// <para>
/// <b>Patrick's order of 29.09.2026: "as the limits arrive the bots can naturally spread over the world; give them
/// knowledge of the roads between the towns and the ways round. Some guilds may decide to settle by other towns, so as
/// not to be so dense round Britain. The bots should scatter over the world by themselves, travel, meet new horizons,
/// build roads to other towns, share what they find — the danger and the safety."</b> Until now the population had one
/// home, one roam of a thousand tiles round it, and no word for anywhere else.
/// </para>
///
/// <para>
/// <b>A town is a region the engine already draws.</b> Every town on Felucca is a <c>TownRegion</c> with a name and a box —
/// Britain, Trinsic, Minoc, Vesper, Yew, Cove, and the island towns — and the fields round Britain and Yew are town regions
/// too, which is why anything called "A Wheatfield in ..." is passed over. The middle of the box, settled on the ground, is
/// the town's square: where a traveller aims, where a guild that settles there is seated near, and what "in Trinsic" means.
/// </para>
///
/// <para>
/// <b>Whether a town can be reached is the gates' answer, not a table's.</b> Jhelom, Moonglow, Magincia, Nujel'm and Ocllo
/// are islands with no boat, and the graph's components say so (<see cref="BotGates.Joined"/>); the towns joined to home
/// by ground — or by a gate, should one ever be — are the ones travellers go to and guilds settle by.
/// </para>
/// </summary>
public static class BotTowns
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotTowns));

    public sealed class Town
    {
        public string Name;

        public Point3D Square;

        public Rectangle2D Bounds;

        public bool FromHome;

        public bool BySea;

        public bool Home;

        public int Seated;

        public long Arrivals;

        public override string ToString() => $"{Name} ({Square.X}, {Square.Y})";
    }

    public static int LeastSide { get; set; } = 60;

    public static int Roam { get; set; } = 600;

    private static readonly List<Town> _towns = [];

    private static Map _map;

    public static bool Surveyed { get; private set; }

    public static bool Reckoned { get; private set; }

    public static bool Ready => Surveyed && Reckoned;

    public static IReadOnlyList<Town> All => _towns;

    public static int Reachable { get; private set; }

    public static bool Ensure(Map map)
    {
        if (map == null || map == Map.Internal)
        {
            return false;
        }

        if (!Surveyed)
        {
            Survey(map);
        }

        if (Surveyed && !Reckoned && BotGates.Ready)
        {
            Reckon(map);
        }

        return Ready;
    }

    private static void Survey(Map map)
    {
        Surveyed = true;
        _map = map;

        var home = BotPopulation.Where;

        foreach (var region in Region.Regions)
        {
            if (region is not TownRegion town || town.Map != map || town.Area == null || town.Area.Length == 0 || string.IsNullOrWhiteSpace(town.Name))
            {
                continue;
            }

            if (town.Name.StartsWith("A ", StringComparison.Ordinal) || town.Name.StartsWith("An ", StringComparison.Ordinal))
            {
                continue;
            }

            var x1 = int.MaxValue;
            var y1 = int.MaxValue;
            var x2 = int.MinValue;
            var y2 = int.MinValue;

            for (var i = 0; i < town.Area.Length; i++)
            {
                var box = town.Area[i];

                x1 = Math.Min(x1, box.Start.X);
                y1 = Math.Min(y1, box.Start.Y);
                x2 = Math.Max(x2, box.End.X);
                y2 = Math.Max(y2, box.End.Y);
            }

            if (x2 - x1 < LeastSide || y2 - y1 < LeastSide)
            {
                continue;
            }

            if (!Place(map, (x1 + x2) / 2, (y1 + y2) / 2, 24, out var square))
            {
                continue;
            }

            var bounds = new Rectangle2D(x1, y1, x2 - x1 + 1, y2 - y1 + 1);

            _towns.Add(new Town { Name = town.Name, Square = square, Bounds = bounds, Home = bounds.Contains(home) });
        }

        logger.Information("Towns: {Towns} on {Map} read from its regions; which of them can be walked to from home is asked of the gates", _towns.Count, map.Name);
    }

    private static void Reckon(Map map)
    {
        Reckoned = true;
        Reachable = 0;

        var home = BotPopulation.Where;
        using var names = Server.Text.ValueStringBuilder.Create(256);

        for (var i = 0; i < _towns.Count; i++)
        {
            var town = _towns[i];

            town.FromHome = BotGates.Joined(map, home, town.Square);

            if (town.FromHome)
            {
                Reachable++;
                names.Append(names.Length > 0 ? ", " : "");
                names.Append(town.Name);
            }
        }

        logger.Information("Towns: {Reachable} of {Towns} can be walked to from home: {Names}", Reachable, _towns.Count, names.ToString());
    }

    public static bool Place(Map map, int x, int y, int sweep, out Point3D spot)
    {
        for (var r = 0; r <= sweep; r += r < 4 ? 1 : 4)
        {
            for (var dx = -r; dx <= r; dx += Math.Max(1, r))
            {
                for (var dy = -r; dy <= r; dy += Math.Max(1, r))
                {
                    if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != r)
                    {
                        continue;
                    }

                    if (BotStep.Settle(map, x + dx, y + dy, out var z) && map.CanSpawnMobile(x + dx, y + dy, z))
                    {
                        spot = new Point3D(x + dx, y + dy, z);

                        return true;
                    }
                }
            }
        }

        spot = Point3D.Zero;

        return false;
    }

    public static Town Of(Point3D where)
    {
        for (var i = 0; i < _towns.Count; i++)
        {
            if (_towns[i].Bounds.Contains(where))
            {
                return _towns[i];
            }
        }

        return null;
    }

    public static Town Nearest(Point3D where)
    {
        var holding = Of(where);

        if (holding != null)
        {
            return holding;
        }

        Town best = null;
        var nearest = int.MaxValue;

        for (var i = 0; i < _towns.Count; i++)
        {
            var town = _towns[i];
            var away = Math.Max(Math.Abs(town.Square.X - where.X), Math.Abs(town.Square.Y - where.Y));

            if (away < nearest)
            {
                nearest = away;
                best = town;
            }
        }

        return best;
    }

    public static Town Find(string name)
    {
        for (var i = 0; i < _towns.Count; i++)
        {
            if (string.Equals(_towns[i].Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return _towns[i];
            }
        }

        return null;
    }

    public static bool Within(Point3D where)
    {
        if (!Ready)
        {
            return false;
        }

        for (var i = 0; i < _towns.Count; i++)
        {
            var town = _towns[i];

            if ((town.FromHome || town.BySea) && Utility.InRange(town.Square, where, Roam))
            {
                return true;
            }
        }

        return false;
    }

    public static string Describe()
    {
        if (!Surveyed)
        {
            return "the towns have not been read";
        }

        using var line = Server.Text.ValueStringBuilder.Create(512);

        line.Append($"{_towns.Count} towns, {Reachable} reachable from home:");

        for (var i = 0; i < _towns.Count; i++)
        {
            var town = _towns[i];

            if (!town.FromHome && !town.BySea)
            {
                continue;
            }

            line.Append($" {town.Name} ({(town.BySea ? "by sea, " : "")}{town.Arrivals} arrivals, {town.Seated} guilds)");
        }

        return line.ToString();
    }

    public static void Forget()
    {
        _towns.Clear();
        _map = null;
        Surveyed = false;
        Reckoned = false;
        Reachable = 0;
    }
}
