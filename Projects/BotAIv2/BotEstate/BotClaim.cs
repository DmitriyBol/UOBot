using System;
using System.Collections.Generic;
using Server.Guilds;
using Server.Logging;

namespace Server.BotAI.V2;

/// <summary>
/// What a guild has claimed of the island itself, square by square, and how a claim is won.
///
/// <para>
/// <b>Patrick's order of 10.09.2026.</b> A guild may lay claim to a quadrant — four of them for nothing and
/// five thousand gold for each one after that. Having declared, it gathers there; if no other guild comes
/// and beats it, the square passes into that guild's zone of influence. Another guild may lay claim to a
/// square somebody already holds, and chooses as it does: five thousand to take the square for itself, or
/// two and a half to strike the holder's name off it and leave it to nobody. A square somebody took with one
/// of their free claims may be claimed against for nothing at all.
/// </para>
///
/// <para>
/// <b>Why a square rather than a circle.</b> <c>BotQuad</c> already cuts the island into thirty-tile squares
/// and the population already has an opinion about each one — how safe it is, what lives there, how often
/// somebody was hit. Land a guild can own had to be the same shape as land the population can talk about, or
/// the map would have carried two different griddings of the same island and every question would have had
/// to say which it meant.
/// </para>
///
/// <para>
/// <b>Held is a register and land near a hall is not.</b> <c>BotLand</c> computes its claim from where the
/// halls stand, deliberately, so it can never go stale. This cannot be computed from anything — it is the
/// record of something that happened — so it is kept, and it is cleared on a world reload like every other
/// board on this shard.
/// </para>
///
/// <para>
/// <b>Claiming somebody's square is an act against them and is filed as one.</b> See
/// <c>BotRegard.Claimed</c>: it costs the holder's opinion of the claimant about half what a killing costs,
/// so two claims against the same guild is roughly a war. That is the whole of how a contested claim comes
/// to be fought over — the fighting itself belongs to <c>BotFeud</c>, which needs a war and will have one.
/// </para>
/// </summary>
public static class BotClaim
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotClaim));

    public static bool Running { get; set; } = true;

    public static int Free { get; set; } = 4;

    public static int Price { get; set; } = 5000;

    public static int Ousting { get; set; } = 5000;

    public static int Stripping { get; set; } = 2500;

    public static int MusterMs { get; set; } = 300000;

    public static int Gather { get; set; } = 3;

    public static int LookMs { get; set; } = 5000;

    public static long Declared { get; private set; }

    public static long Won { get; private set; }

    public static long Unmustered { get; private set; }

    public static long Beaten { get; private set; }

    public static long Stripped { get; private set; }

    public static long Paid { get; private set; }

    /// <summary>What a claim is for.</summary>
    public enum Kind
    {
        Settle,

        Oust,

        Strip
    }

    /// <summary>A square somebody holds.</summary>
    private sealed class Holding
    {
        public string Guild;

        public bool Bought;
    }

    /// <summary>A claim in progress.</summary>
    public sealed class Bid
    {
        public string Guild;

        public Kind Want;

        public Map Map;

        public Point3D Middle;

        public (int Map, int X, int Y) Key;

        public long Began;

        public int Peak;

        public int Felled;

        public int Fallen;

        public string From;
    }

    private static readonly Dictionary<(int Map, int X, int Y), Holding> _held = [];

    private static readonly Dictionary<(int Map, int X, int Y), Bid> _bids = [];

    private static readonly Dictionary<string, Bid> _byGuild = [];

    private static long _looked;

    public static string Owner(Map map, Point3D where)
    {
        if (!Running || map == null || map == Map.Internal || _held.Count == 0)
        {
            return null;
        }

        return _held.TryGetValue(BotQuad.Key(map, where), out var held) ? held.Guild : null;
    }

    public static bool Bought(Map map, Point3D where) =>
        _held.TryGetValue(BotQuad.Key(map, where), out var held) && held.Bought;

    public static int Holds(string guild)
    {
        var many = 0;

        foreach (var held in _held.Values)
        {
            if (held.Guild == guild)
            {
                many++;
            }
        }

        return many;
    }

    public static Bid Making(Guild guild) =>
        guild != null && _byGuild.TryGetValue(guild.Name, out var bid) ? bid : null;

    public static Bid On(Map map, Point3D where) =>
        map == null ? null : _bids.GetValueOrDefault(BotQuad.Key(map, where));

    public static int Cost(string guild, (int Map, int X, int Y) key, Kind want)
    {
        if (!_held.TryGetValue(key, out var held))
        {
            return Holds(guild) < Free ? 0 : Price;
        }

        if (!held.Bought)
        {
            return 0;
        }

        return want == Kind.Strip ? Stripping : Ousting;
    }

    public static Bid Open(Guild guild, Map map, Point3D middle, Kind want)
    {
        if (!Running || guild == null || map == null || map == Map.Internal)
        {
            return null;
        }

        var key = BotQuad.Key(map, middle);

        if (_bids.ContainsKey(key) || _byGuild.ContainsKey(guild.Name))
        {
            return null;
        }

        var held = _held.GetValueOrDefault(key);

        if (held?.Guild == guild.Name)
        {
            return null;
        }

        var price = Cost(guild.Name, key, want);

        if (price > 0)
        {
            List<BotEstate.Contribution> purse = [];

            if (BotEstate.Levy(guild, price, purse) < price)
            {
                BotEstate.Refund(purse);

                return null;
            }

            Paid += price;
        }

        var bid = new Bid
        {
            Guild = guild.Name,
            Want = want,
            Map = map,
            Middle = middle,
            Key = key,
            Began = Core.TickCount,
            From = held?.Guild
        };

        _bids[key] = bid;
        _byGuild[guild.Name] = bid;
        Declared++;

        if (held != null)
        {
            BotRegard.Claimed(guild.Name, held.Guild);
        }

        logger.Warning(
            "{Guild} lays claim to the square at {X},{Y}{From} for {Price}gp; it has {Minutes} minutes to gather {Gather} there",
            guild.Name,
            middle.X,
            middle.Y,
            held == null ? "" : $", which {held.Guild} holds",
            price,
            MusterMs / 60000,
            Gather
        );

        return bid;
    }

    public static void Bled(Map map, Point3D where, string killer, string fallen)
    {
        var bid = On(map, where);

        if (bid == null || killer == null || fallen == null)
        {
            return;
        }

        if (killer == bid.Guild)
        {
            bid.Felled++;
        }
        else if (fallen == bid.Guild)
        {
            bid.Fallen++;
        }
    }

    public static void Look()
    {
        if (!Running)
        {
            return;
        }

        var now = Core.TickCount;

        if (now - (_looked + LookMs) < 0)
        {
            return;
        }

        _looked = now;

        Sweep();

        var bots = BotPopulation.Bots;
        List<Bid> over = [];

        foreach (var bid in _bids.Values)
        {
            var here = 0;

            for (var i = 0; i < bots.Count; i++)
            {
                var bot = bots[i];

                if (bot is not { Deleted: false, Alive: true } || bot.Guild?.Name != bid.Guild || bot.Map != bid.Map)
                {
                    continue;
                }

                if (BotQuad.Key(bot.Map, bot.Location) == bid.Key)
                {
                    here++;
                }
            }

            if (here > bid.Peak)
            {
                bid.Peak = here;
            }

            if (now - (bid.Began + MusterMs) >= 0)
            {
                over.Add(bid);
            }
        }

        for (var i = 0; i < over.Count; i++)
        {
            Settle(over[i]);
        }
    }

    private static void Settle(Bid bid)
    {
        _bids.Remove(bid.Key);
        _byGuild.Remove(bid.Guild);

        if (bid.Fallen > bid.Felled)
        {
            Beaten++;

            logger.Information(
                "{Guild} was driven off the square at {X},{Y}: {Fallen} of theirs down to {Felled}",
                bid.Guild,
                bid.Middle.X,
                bid.Middle.Y,
                bid.Fallen,
                bid.Felled
            );

            return;
        }

        if (bid.Peak < Gather)
        {
            Unmustered++;

            logger.Information(
                "{Guild} never gathered on the square at {X},{Y}: {Peak} of them at most, {Gather} wanted",
                bid.Guild,
                bid.Middle.X,
                bid.Middle.Y,
                bid.Peak,
                Gather
            );

            return;
        }

        if (bid.Want == Kind.Strip)
        {
            _held.Remove(bid.Key);
            Stripped++;

            logger.Warning(
                "{Guild} has struck {From} off the square at {X},{Y}; it belongs to nobody now",
                bid.Guild,
                bid.From ?? "nobody",
                bid.Middle.X,
                bid.Middle.Y
            );

            return;
        }

        _held[bid.Key] = new Holding
        {
            Guild = bid.Guild,
            Bought = Cost(bid.Guild, bid.Key, bid.Want) > 0 || bid.Want != Kind.Settle
        };

        Won++;

        logger.Warning(
            "The square at {X},{Y} is {Guild}'s{From}",
            bid.Middle.X,
            bid.Middle.Y,
            bid.Guild,
            bid.From == null ? "" : $", taken from {bid.From}"
        );
    }

    public static Point3D Middle(Map map, Point3D where)
    {
        var key = BotQuad.Key(map, where);
        var x = key.X * BotQuad.Side + BotQuad.Side / 2;
        var y = key.Y * BotQuad.Side + BotQuad.Side / 2;

        return BotStep.Settle(map, x, y, out var z) ? new Point3D(x, y, z) : Point3D.Zero;
    }

    public static string Pin(Map map, Point3D where) =>
        map == null ? null : Short(_held.GetValueOrDefault(BotQuad.Key(map, where))?.Guild);

    public static string Claiming(Map map, Point3D where) =>
        map == null ? null : Short(_bids.GetValueOrDefault(BotQuad.Key(map, where))?.Guild);

    public static void Restore(int facet, int x, int y, string guild, bool bought)
    {
        if (string.IsNullOrEmpty(guild))
        {
            return;
        }

        _held[(facet, x, y)] = new Holding { Guild = guild, Bought = bought };
    }

    public static bool Reopen(int facet, int x, int y, string guild, Kind want, string from)
    {
        if (string.IsNullOrEmpty(guild) || _bids.ContainsKey((facet, x, y)) || _byGuild.ContainsKey(guild))
        {
            return false;
        }

        var map = Map.Maps[facet is >= 0 and < 0x100 ? facet : 0];

        if (map == null || map == Map.Internal)
        {
            return false;
        }

        var middle = Middle(map, new Point3D(x * BotQuad.Side + BotQuad.Side / 2, y * BotQuad.Side + BotQuad.Side / 2, 0));

        if (middle == Point3D.Zero)
        {
            return false;
        }

        var bid = new Bid
        {
            Guild = guild,
            Want = want,
            Map = map,
            Middle = middle,
            Key = (facet, x, y),
            Began = Core.TickCount,
            From = from
        };

        _bids[bid.Key] = bid;
        _byGuild[guild] = bid;

        return true;
    }

    private static void Sweep()
    {
        if (_held.Count == 0 || BotGuilds.Count == 0)
        {
            return;
        }

        List<(int Map, int X, int Y)> gone = [];

        foreach (var (key, held) in _held)
        {
            if (BaseGuild.FindByName(held.Guild) is not Guild)
            {
                gone.Add(key);
            }
        }

        for (var i = 0; i < gone.Count; i++)
        {
            logger.Information("The square at {X},{Y} belonged to a guild that is gone; it belongs to nobody now", gone[i].X * BotQuad.Side, gone[i].Y * BotQuad.Side);
            _held.Remove(gone[i]);
        }
    }

    public static IEnumerable<Bid> Bids => _bids.Values;

    public static int Left(Bid bid) =>
        bid == null ? 0 : Math.Max(0, MusterMs - (int)(Core.TickCount - bid.Began));

    public static string Short(string guild)
    {
        if (string.IsNullOrEmpty(guild))
        {
            return null;
        }

        var name = guild.StartsWith("The ", StringComparison.OrdinalIgnoreCase) ? guild[4..] : guild;

        return name.ToUpperInvariant();
    }

    public static IEnumerable<((int Map, int X, int Y) Key, string Guild, bool Bought)> Owned()
    {
        foreach (var (key, held) in _held)
        {
            yield return (key, held.Guild, held.Bought);
        }
    }

    public static string Describe() =>
        !Running
            ? "guilds claim no ground"
            : $"{_held.Count} squares are spoken for and {_bids.Count} being claimed now ({Free} free to a guild, "
            + $"then {Price}gp; {Ousting}gp to take one, {Stripping}gp to strike a name off it); {Declared} claims made, "
            + $"{Won} won, {Stripped} that only struck a name off, {Unmustered} where the guild never gathered "
            + $"{Gather} of itself, {Beaten} driven off by somebody who came; {Paid}gp paid for ground";

    public static void Forget()
    {
        _bids.Clear();
        _byGuild.Clear();
        Declared = 0;
        Won = 0;
        Unmustered = 0;
        Beaten = 0;
        Stripped = 0;
        Paid = 0;
        _looked = 0;
    }
}
