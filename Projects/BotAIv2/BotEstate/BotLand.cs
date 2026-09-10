using System;
using Server.Guilds;
using Server.Mobiles;

namespace Server.BotAI.V2;

/// <summary>
/// The ground round a guild's hall, and what belonging to a guild does to work done on it.
///
/// <para>
/// <b>Stage three of Patrick's order of 08.09.2026.</b> A hall makes land: every tile within
/// <see cref="Reach"/> of it is that guild's, its members work there by preference, and everybody else works
/// there at a discount. Nothing is fenced off — see below — so the island stays one place that anybody can
/// cross.
/// </para>
///
/// <para>
/// <b>A preference and never a veto, which is the rule this project has paid for most often.</b> Four guilds
/// each claiming ninety tiles is an island where every square belongs to somebody; if belonging meant
/// refusing, the population would stop working. So the claim is a multiplier with a floor, in exactly the
/// shape <c>BotGuilds.Kinship</c> and <c>BotAppraisal</c>'s other factors have: a bot on another guild's
/// ground still digs, still sews, still fights — it simply prefers the same work at home. The measure that
/// says this has gone wrong is the completion band falling while evictions rise.
/// </para>
///
/// <para>
/// <b>The claim is computed from the halls rather than kept.</b> A hall is destroyed, adopted or raised
/// while the shard is up, and a stored map of who owns what would be a second copy of that going stale — the
/// defect this project names most often. Five halls and a distance check is a loop of five, asked in the
/// appraisal's hot path a few thousand times a second, which is nothing.
/// </para>
/// </summary>
public static class BotLand
{
    /// <summary>Whether halls make land at all.</summary>
    public static bool Running { get; set; } = true;

    /// <summary>
    /// How far a hall's claim reaches, in tiles.
    ///
    /// <para>
    /// Forty. A hall stands somewhere between eighty and a hundred and eighty tiles from home and the halls
    /// are kept <c>BotPlot.Apart</c> from each other, so forty is a yard a guild can plausibly keep an eye
    /// on and small enough that the island does not become wholly spoken for. Chebyshev, like every other
    /// distance on this shard, because that is how a bot walks.
    /// </para>
    /// </summary>
    public static int Reach { get; set; } = 40;

    /// <summary>What work on your own guild's ground is worth, as a multiplier.</summary>
    public static double Home { get; set; } = 1.25;

    /// <summary>
    /// And on another guild's.
    ///
    /// <para>
    /// Seven tenths — a discount a bot will happily ignore for work worth doing, which is the point. It is
    /// well above the floor the other factors use, because the ground belonging to a neighbour is a much
    /// weaker objection than a full pack or an empty purse.
    /// </para>
    /// </summary>
    public static double Abroad { get; set; } = 0.7;

    /// <summary>Times work was weighed on its own guild's land.</summary>
    public static long AtHome { get; private set; }

    /// <summary>Times work was weighed on somebody else's.</summary>
    public static long Away { get; private set; }

    /// <summary>Times the ground belonged to nobody, which is most of the island.</summary>
    public static long Open { get; private set; }

    /// <summary>
    /// Which guild claims this ground, or nothing.
    ///
    /// <para>
    /// The nearest hall wins where two claims overlap, so a tile is only ever one guild's and the answer
    /// does not depend on the order the halls happen to be stored in.
    /// </para>
    /// </summary>
    public static string Holder(Map map, Point3D where)
    {
        if (!Running || map == null || map == Map.Internal)
        {
            return null;
        }

        string held = null;
        var closest = int.MaxValue;

        // <b>Answered as a name, and that is a hot-path decision rather than a stylistic one.</b> The
        // register is keyed by guild name, so turning a hall into a <c>Guild</c> means a search of the
        // engine's roll — inside a loop over the halls, inside the appraisal, which runs a few thousand
        // times a second. A name compares against <c>bot.Guild.Name</c> exactly as well and costs nothing.
        // See Holding for the times an actual guild is wanted, which are rare and none of them hot.
        foreach (var (name, hall) in BotEstate.Held)
        {
            if (hall is not { Deleted: false } || hall.Map != map)
            {
                continue;
            }

            var gap = Math.Max(Math.Abs(hall.X - where.X), Math.Abs(hall.Y - where.Y));

            if (gap > Reach || gap >= closest)
            {
                continue;
            }

            held = name;
            closest = gap;
        }

        return held;
    }

    /// <summary>The same question when the guild itself is wanted rather than its name. Never on a hot path.</summary>
    public static Guild Holding(Map map, Point3D where) => BaseGuild.FindByName(Holder(map, where)) as Guild;

    /// <summary>
    /// What this ground does to what a piece of work is worth to this bot.
    ///
    /// One on ground nobody claims, which is nearly all of it — so a shard with no halls behaves exactly as
    /// it did before this existed, and the factor costs nothing until a guild builds.
    /// </summary>
    public static double Worth(Mobile bot, Map map, Point3D where)
    {
        if (!Running || bot == null)
        {
            return 1.0;
        }

        var held = Holder(map, where);

        if (held == null)
        {
            Open++;

            return 1.0;
        }

        if (bot.Guild?.Name == held)
        {
            AtHome++;

            return Home;
        }

        Away++;

        return Abroad;
    }

    /// <summary>Whether this bot is standing on ground belonging to a guild that is not its own.</summary>
    public static bool Trespassing(Mobile bot, out string whose)
    {
        whose = bot == null ? null : Holder(bot.Map, bot.Location);

        return whose != null && bot.Guild?.Name != whose;
    }

    public static string Describe() =>
        !Running
            ? "halls make no land"
            : $"land reaches {Reach} tiles from a hall, worth ×{Home:F2} to its own and ×{Abroad:F2} to anybody else; "
              + $"{AtHome} pieces of work weighed at home, {Away} on somebody else's ground, {Open} on ground nobody claims";

    public static void Forget()
    {
        AtHome = 0;
        Away = 0;
        Open = 0;
    }
}
