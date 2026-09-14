using System;
using System.Collections.Generic;
using Server.Items;
using Server.Logging;

namespace Server.BotAI.V2;

/// <summary>
/// What the population as a whole has found out about what pays where.
///
/// <para>
/// <b>Thirty-three bots were learning one island thirty-three times.</b> <see cref="BotLedger"/> is per bot
/// and its own note says why — "that a hundred bots have exhausted a field says nothing about whether this
/// one has seen it" — which is exactly right about <em>seeing</em> and exactly wrong about the field. Two
/// different facts had been folded into one record: "I could not get there from here", which is a fact about
/// the bot and must stay private, and "there is nothing left in that seam", which is a fact about the seam
/// and belongs to everybody. This is the second of those, and only the second.
/// </para>
///
/// <para>
/// <b>It is shaped like <see cref="BotPeril"/> on purpose.</b> Squares rather than points, because a payout
/// at tile resolution is thirty facts each worth nothing; a frequency rather than a total, because last
/// week's gold is a fact about last week; and shard-wide, because the whole value of it is that bot number
/// thirty starts where bot number one finished. That map has worked for danger since the day it was written
/// and there was never a reason for money to be different.
/// </para>
///
/// <para>
/// <b>Own experience always wins, and that ordering is the whole safety of this.</b> A bot that has worked a
/// place knows more about it than the population does — more recent, and about this bot's own skill — so
/// <see cref="BotLedger.Expect"/> reaches here only when it has nothing of its own. What this replaces is not
/// a bot's judgement; it is the hand-written constant a bot would otherwise have used for its first guess.
/// </para>
///
/// <para>
/// <b>Nothing here is a decision.</b> It answers a question the auction asks and takes no view about what
/// anybody should do — the same contract the ledger keeps. A shared board that could compel would be the one
/// thing this project has refused since its first week.
/// </para>
/// </summary>
public static class BotCommons
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotCommons));

    public static int Band => BotLedger.BandSize;

    public static int HalfLifeMs { get; set; } = 3600000;

    public static double Smoothing { get; set; } = 0.2;

    public static int Confidence { get; set; } = 4;

    public static double PriorWeight { get; set; } = 2.0;

    public static int TradeConfidence { get; set; } = 25;

    public static double LeastShare { get; set; } = 0.25;

    public static int MostPatches { get; set; } = 4096;

    private sealed class Patch
    {
        public string Kind;

        public Map Map;

        public int X;

        public int Y;

        public double Measured;

        public int Settled;

        public long TouchedTick;

        public int Minded;
    }

    /// <summary>
    /// What one trade turned out to be worth across the whole island, against what it claims to be worth.
    ///
    /// <para>
    /// <b>Every <c>Prior</c> in this project is a number somebody typed.</b> A sweep asserts forty-five a
    /// minute, an unload a hundred and twenty, a prowl eight — nine-tenths of what the auction weighs is
    /// those constants, and not one of them has ever been checked against what the work actually paid. They
    /// were reasonable when written and the shard has changed underneath every one of them.
    /// </para>
    ///
    /// <para>
    /// <b>The three thinking bots found this out on their own, in words.</b> Cedric wrote itself the rule "if
    /// Scribe forecast exceeds 100 gold per minute, reduce expected value to 60% of forecast" — which is this
    /// record, discovered by a language model and applicable only to itself. What is kept here is the same
    /// discovery in a form the other thirty can read.
    /// </para>
    /// </summary>
    private sealed class Trade
    {
        public double Claimed;

        public double Measured;

        public int Settled;

        public int Minded;

        public long TouchedTick;
    }

    private static readonly Dictionary<string, Trade> _trades = new(StringComparer.OrdinalIgnoreCase);

    private static readonly Dictionary<(string Kind, int Map, int X, int Y), Patch> _patches = [];

    public static double MindWeight { get; set; } = 2.0;

    public static long Corrections { get; private set; }

    public static long Noted { get; private set; }

    public static long Taught { get; private set; }

    public static long Asked { get; private set; }

    public static int Patches => _patches.Count;

    public static void Note(string kind, Map map, Point3D where, double perMinute, bool told)
    {
        if (kind == null || map == null || map == Map.Internal)
        {
            return;
        }

        var now = Core.TickCount;
        var key = Key(kind, map, where);

        if (!_patches.TryGetValue(key, out var patch))
        {
            if (_patches.Count >= MostPatches)
            {
                Forget(now);
            }

            patch = new Patch { Kind = kind, Map = map, X = key.X, Y = key.Y, TouchedTick = now };
            _patches[key] = patch;
        }

        var held = Faded(patch, now);

        patch.Measured = patch.Settled == 0 ? perMinute : held + (perMinute - held) * Smoothing;
        patch.TouchedTick = now;

        if (patch.Settled < int.MaxValue)
        {
            patch.Settled++;
        }

        Noted++;

        if (told)
        {
            patch.Minded++;
            Taught++;
        }
    }

    public static double Expect(string kind, Map map, Point3D where, double prior)
    {
        if (prior <= 0.0 || kind == null || map == null)
        {
            return prior;
        }

        if (!_patches.TryGetValue(Key(kind, map, where), out var patch) || patch.Settled == 0)
        {
            return prior;
        }

        Asked++;

        var settled = Math.Min(patch.Settled, Confidence);

        return (prior * PriorWeight + Faded(patch, Core.TickCount) * settled) / (PriorWeight + settled);
    }

    public static void Claimed(string kind, double claim, double got, bool told)
    {
        if (kind == null || claim <= 0.0)
        {
            return;
        }

        var now = Core.TickCount;

        if (!_trades.TryGetValue(kind, out var trade))
        {
            trade = new Trade { TouchedTick = now };
            _trades[kind] = trade;
        }

        var weight = Smoothing * (told ? MindWeight : 1.0);

        trade.Claimed = trade.Settled == 0 ? claim : trade.Claimed + (claim - trade.Claimed) * Smoothing;
        trade.Measured = trade.Settled == 0 ? got : trade.Measured + (got - trade.Measured) * Math.Min(1.0, weight);
        trade.TouchedTick = now;

        if (trade.Settled < int.MaxValue)
        {
            trade.Settled++;
        }

        if (told)
        {
            trade.Minded++;
        }
    }

    public static double Corrected(string kind, double claim)
    {
        if (claim <= 0.0 || kind == null || !_trades.TryGetValue(kind, out var trade) || trade.Settled == 0)
        {
            return claim;
        }

        Corrections++;

        var settled = Math.Min(trade.Settled, TradeConfidence);
        var corrected = (claim * PriorWeight + trade.Measured * settled) / (PriorWeight + settled);

        return Math.Max(claim * LeastShare, corrected);
    }

    public static List<(string Kind, double Claimed, double Measured, int Settled, int Minded)> Gaps(int most)
    {
        List<(string Kind, double Claimed, double Measured, int Settled, int Minded)> found = [];

        foreach (var (kind, trade) in _trades)
        {
            found.Add((kind, trade.Claimed, trade.Measured, trade.Settled, trade.Minded));
        }

        found.Sort((left, right) => (right.Claimed - right.Measured).CompareTo(left.Claimed - left.Measured));

        if (most > 0 && found.Count > most)
        {
            found.RemoveRange(most, found.Count - most);
        }

        return found;
    }

    private static readonly Dictionary<(int Map, int X, int Y), (int Rank, int Loads)> _seams = [];

    public static void Dug(Map map, Point3D where, CraftResource resource)
    {
        if (map == null || map == Map.Internal)
        {
            return;
        }

        var rank = (int)resource;
        var key = (map.MapID, where.X / Band, where.Y / Band);

        if (_seams.TryGetValue(key, out var held))
        {
            _seams[key] = (Math.Max(held.Rank, rank), held.Loads + 1);

            return;
        }

        _seams[key] = (rank, 1);
        Loads++;
    }

    public static int Richest(Map map, Point3D where) =>
        map != null && _seams.TryGetValue((map.MapID, where.X / Band, where.Y / Band), out var held) ? held.Rank : 0;

    public static long Loads { get; private set; }

    public static double Reading(string kind, Map map, Point3D where) =>
        map != null && kind != null && _patches.TryGetValue(Key(kind, map, where), out var patch)
            ? Faded(patch, Core.TickCount)
            : 0.0;

    public static List<(string Kind, Map Map, Point3D Where, double PerMinute, int Settled, int Minded)> Best(int most)
    {
        var now = Core.TickCount;

        List<(string Kind, Map Map, Point3D Where, double PerMinute, int Settled, int Minded)> found = [];

        foreach (var patch in _patches.Values)
        {
            var worth = Faded(patch, now);

            if (worth <= 0.0)
            {
                continue;
            }

            found.Add((patch.Kind, patch.Map, Middle(patch), worth, patch.Settled, patch.Minded));
        }

        found.Sort((left, right) => right.PerMinute.CompareTo(left.PerMinute));

        if (most > 0 && found.Count > most)
        {
            found.RemoveRange(most, found.Count - most);
        }

        return found;
    }

    public static Point3D Richest(string kind, Map map, Point3D from, int within)
    {
        if (kind == null || map == null || map == Map.Internal)
        {
            return Point3D.Zero;
        }

        var now = Core.TickCount;
        var best = Point3D.Zero;
        var bestWorth = 0.0;

        foreach (var patch in _patches.Values)
        {
            if (patch.Map != map || !string.Equals(patch.Kind, kind, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var worth = Faded(patch, now);

            if (worth <= bestWorth)
            {
                continue;
            }

            var middle = Middle(patch);

            if (Math.Max(Math.Abs(middle.X - from.X), Math.Abs(middle.Y - from.Y)) > within)
            {
                continue;
            }

            bestWorth = worth;
            best = middle;
        }

        return best;
    }

    private static double Faded(Patch patch, long now)
    {
        var since = now - patch.TouchedTick;

        if (since <= 0 || patch.Measured <= 0.0)
        {
            return patch.Measured;
        }

        return patch.Measured * Math.Pow(0.5, since / (double)HalfLifeMs);
    }

    private static void Forget(long now)
    {
        (string Kind, int Map, int X, int Y) stalest = default;
        var oldest = long.MinValue;
        var found = false;

        foreach (var (key, patch) in _patches)
        {
            var idle = now - patch.TouchedTick;

            if (idle <= oldest)
            {
                continue;
            }

            oldest = idle;
            stalest = key;
            found = true;
        }

        if (found)
        {
            _patches.Remove(stalest);
        }
    }

    private static (string Kind, int Map, int X, int Y) Key(string kind, Map map, Point3D where) =>
        (kind, map?.MapID ?? -1, where.X / Band, where.Y / Band);

    private static Point3D Middle(Patch patch) => new(patch.X * Band + Band / 2, patch.Y * Band + Band / 2, 0);

    public static string Describe() =>
        _patches.Count == 0
            ? "the population has not learned anything about anywhere yet"
            : $"{_patches.Count} patches and {_trades.Count} trades known from {Noted} outcomes, {Taught} of them from a bot with a mind; asked {Asked} times by somebody who had never been there and {Corrections} claims corrected by what the work really paid; {_seams.Count} patches dug over";

    public static void Forget()
    {
        _patches.Clear();
        _trades.Clear();
        _seams.Clear();
        Loads = 0;
        Corrections = 0;
        Noted = 0;
        Taught = 0;
        Asked = 0;
    }
}
