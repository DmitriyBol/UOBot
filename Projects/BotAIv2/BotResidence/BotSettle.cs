using System;
using System.Collections.Generic;
using Server.Guilds;
using Server.Logging;
using Server.Mobiles;

namespace Server.BotAI.V2;

/// <summary>
/// Which towns a bot can live in, and which one it settles in.
///
/// <para>
/// <b>A town is fit to live in when everything a bot does at home can be done there.</b> The walk from it to the rest of the
/// island must exist (a road in the book, <see cref="BotRoadbook"/>), and in it a bot must be able to put its coin away and
/// buy its kit: a bank and at least <see cref="LeastShops"/> shopkeepers. Counted off the town's own region, once, when the
/// towns are first read. The bank matters most, and it is measured rather than assumed: The Needle lives at Cove, which the
/// spawn files give five shopkeepers, two healers and no bank, and on 29.09.2026 three of its bots died on the eight
/// hundred tiles to Britain in one hour, each walking there to buy a pair of scissors (build 301). The home town is fit by
/// definition — it is where the population was built to live.
/// </para>
///
/// <para>
/// <b>The choice is weighed, not dealt.</b> Each fit town wants a share of the population — the home town
/// <see cref="HomeShare"/>, the others the rest in equal parts — and a town is weighed by its share over one plus
/// <see cref="SpreadWeight"/> times how full of it it already is: an empty town weighs its whole share, one at its share a
/// quarter of it, one at twice its share a seventh. So the towns fill towards their shares and none is ever closed, and a
/// bot on its way to a town is counted there, so a hundred choices made in one second do not all see the same empty town.
/// A town where the bot's guildmates live weighs more by <see cref="GuildPull"/>: a company or a delve is raised from
/// whoever of the guild stands near, and a guild spread evenly over six towns raises neither.
/// </para>
///
/// <para>
/// <b>A bot that has to walk there is measured against the road, as a traveller is</b> (<see cref="BotTraveller.Odds"/>): the
/// worst the road's ground asks (<see cref="BotRoadbook.Asks"/>) times one and a half against the bot's strength. On the
/// evening of 29.09.2026, 952 of 2667 asks to travel found the bot too weak for every road left. A newborn does not walk: it is
/// born in its town, and its leash (<c>BotPopulation.Leash</c>, three hundred tiles for a novice) keeps its work round it.
/// </para>
/// </summary>
public static class BotSettle
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotSettle));

    /// <summary>What a town has in it for a bot, counted once off its region.</summary>
    public sealed class Amenities
    {
        public int Shops;

        public int Banks;

        public int Healers;

        public Point3D Hearth;

        public bool Fit;

        public string Why;
    }

    public static double HomeShare { get; set; } = 0.35;

    public static double SpreadWeight { get; set; } = 3.0;

    public static double GuildPull { get; set; } = 1.0;

    public static int LeastShops { get; set; } = 5;

    public static bool NeedsBank { get; set; } = true;

    public static bool GateTowns { get; set; } = false;

    private static readonly Dictionary<string, Amenities> _towns = new(StringComparer.OrdinalIgnoreCase);

    public static bool Reckoned { get; private set; }

    public static BotTowns.Town HomeTown { get; private set; }

    public static int Fit { get; private set; }

    public static long Chosen { get; private set; }

    public static long ChosenAway { get; private set; }

    public static long Barred { get; private set; }

    public static Amenities Of(BotTowns.Town town)
    {
        if (town == null)
        {
            return null;
        }

        if (_towns.TryGetValue(town.Name, out var known))
        {
            return known;
        }

        var map = BotPopulation.Home;
        var info = new Amenities { Hearth = town.Square };

        if (map == null || map == Map.Internal)
        {
            return info;
        }

        var nearest = int.MaxValue;
        var bank = Point3D.Zero;

        foreach (var vendor in map.GetMobilesInBounds<BaseVendor>(town.Bounds))
        {
            if (vendor.Deleted)
            {
                continue;
            }

            if (vendor is Banker)
            {
                info.Banks++;

                var away = Math.Max(Math.Abs(vendor.X - town.Square.X), Math.Abs(vendor.Y - town.Square.Y));

                if (away < nearest)
                {
                    nearest = away;
                    bank = vendor.Location;
                }

                continue;
            }

            if (vendor is BaseHealer)
            {
                info.Healers++;

                continue;
            }

            info.Shops++;
        }

        if (bank != Point3D.Zero && BotTowns.Place(map, bank.X, bank.Y, 8, out var spot))
        {
            info.Hearth = spot;
        }

        _towns[town.Name] = info;

        return info;
    }

    public static Point3D Hearth(BotTowns.Town town) =>
        town == null ? BotPopulation.Where : town.Home || ReferenceEquals(town, HomeTown) ? BotPopulation.Where : Of(town).Hearth;

    public static bool IsHearth(Point3D at)
    {
        foreach (var (_, info) in _towns)
        {
            if (info.Hearth == at)
            {
                return true;
            }
        }

        return false;
    }

    public static bool Reckon(Map map)
    {
        if (Reckoned)
        {
            return true;
        }

        if (map == null || !BotTowns.Ensure(map) || !BotRoadbook.Drawn)
        {
            return false;
        }

        Reckoned = true;
        Fit = 0;

        var towns = BotTowns.All;

        HomeTown = null;

        for (var i = 0; i < towns.Count && HomeTown == null; i++)
        {
            if (towns[i].Home)
            {
                HomeTown = towns[i];
            }
        }

        HomeTown ??= BotTowns.Nearest(BotPopulation.Where);

        using var fit = Server.Text.ValueStringBuilder.Create(512);
        using var unfit = Server.Text.ValueStringBuilder.Create(512);

        for (var i = 0; i < towns.Count; i++)
        {
            var town = towns[i];
            var info = Of(town);

            info.Why = ReferenceEquals(town, HomeTown) ? null : Unfit(town, info);
            info.Fit = info.Why == null;

            if (info.Fit)
            {
                Fit++;
                fit.Append(fit.Length > 0 ? ", " : "");
                fit.Append($"{town.Name} ({info.Shops} shops, {info.Banks} banks, {info.Healers} healers)");
            }
            else
            {
                unfit.Append(unfit.Length > 0 ? ", " : "");
                unfit.Append($"{town.Name} ({info.Why})");
            }
        }

        logger.Information(
            "Residences: {Fit} of {Towns} towns are fit to live in: {Names}; passed over: {Unfit}",
            Fit,
            towns.Count,
            fit.ToString(),
            unfit.Length > 0 ? unfit.ToString() : "none"
        );

        return true;
    }

    private static string Unfit(BotTowns.Town town, Amenities info)
    {
        if (!town.FromHome)
        {
            return "not reachable from home";
        }

        if (!GateTowns && BotRoadbook.Between(HomeTown?.Name, town.Name) is not { Steps: > 0 })
        {
            return "no road in the book";
        }

        if (NeedsBank && info.Banks == 0)
        {
            return "no bank";
        }

        return info.Shops < LeastShops ? $"{info.Shops} shops" : null;
    }

    public static int HinterlandTiles { get; set; } = 60;

    public static long Hinterland { get; private set; }

    public static BotTowns.Town Choose(BotMobile bot, BotTowns.Town from, bool walks, BotTowns.Town except, string avoid, out int outmatched)
    {
        outmatched = 0;

        var map = BotPopulation.Home;

        if (!Reckoned || map == null || map == Map.Internal)
        {
            return null;
        }

        var towns = BotTowns.All;
        Span<int> residents = stackalloc int[towns.Count];
        Span<int> mates = stackalloc int[towns.Count];
        Span<double> weights = stackalloc double[towns.Count];
        var present = BotResidence.Tally(towns, residents, mates, bot?.Guild as Guild, out var guildSize);
        var others = Math.Max(1, Fit - 1);
        var power = walks && bot != null ? BotThreat.Power(bot) : 0.0;
        var own = bot != null ? BotThreat.Power(bot) : 0.0;
        var total = 0.0;

        for (var i = 0; i < towns.Count; i++)
        {
            var town = towns[i];
            var info = Of(town);

            weights[i] = 0.0;

            if (!info.Fit || ReferenceEquals(town, except) || string.Equals(town.Name, avoid, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (BotBarred.Holds(map, info.Hearth))
            {
                Barred++;

                continue;
            }

            if (BotBurgh.Turns(bot, town))
            {
                continue;
            }

            if (bot != null && own > 0.0 && !ReferenceEquals(town, HomeTown)
                && BotQuad.MuscleNear(map, info.Hearth, HinterlandTiles) * BotTraveller.Odds > own)
            {
                Hinterland++;

                continue;
            }

            if (walks && bot != null && !ReferenceEquals(town, from))
            {
                var road = from == null ? null : BotRoadbook.Between(from.Name, town.Name);

                if (road is not { Steps: > 0 })
                {
                    if (!GateTowns)
                    {
                        continue;
                    }
                }
                else if (BotRoadbook.Asks(road) * BotTraveller.Odds > power)
                {
                    outmatched++;

                    continue;
                }

                if (!BotGates.Joined(map, bot.Location, info.Hearth))
                {
                    continue;
                }
            }

            var home = ReferenceEquals(town, HomeTown);
            var want = home ? Math.Clamp(HomeShare, 0.0, 1.0) : (1.0 - Math.Clamp(HomeShare, 0.0, 1.0)) / others;
            var fair = Math.Max(1.0, want * present);
            var weight = want / (1.0 + Math.Max(0.0, SpreadWeight) * residents[i] / fair);

            if (guildSize > 0)
            {
                weight *= 1.0 + Math.Max(0.0, GuildPull) * mates[i] / guildSize;
            }

            weights[i] = weight;
            total += weight;
        }

        if (total <= 0.0)
        {
            return null;
        }

        var roll = Utility.RandomDouble() * total;
        BotTowns.Town picked = null;

        for (var i = 0; i < towns.Count; i++)
        {
            if (weights[i] <= 0.0)
            {
                continue;
            }

            picked = towns[i];
            roll -= weights[i];

            if (roll <= 0.0)
            {
                break;
            }
        }

        if (picked != null)
        {
            Chosen++;

            if (!ReferenceEquals(picked, HomeTown))
            {
                ChosenAway++;
            }
        }

        return picked;
    }

    public static string Describe() =>
        !Reckoned
            ? "the towns have not been judged yet"
            : $"{Fit} towns fit to live in, the home town wanting {HomeShare:P0} (spread ×{SpreadWeight:F1}, guild pull ×{GuildPull:F1}); {Chosen} choices made, {ChosenAway} of them away from home, {Barred} towns passed over for a bar or a quarantine, {Hinterland} for ground within {HinterlandTiles} tiles past the bot";

    public static void Forget()
    {
        _towns.Clear();
        Reckoned = false;
        HomeTown = null;
        Fit = 0;
        Chosen = 0;
        ChosenAway = 0;
        Barred = 0;
    }
}
