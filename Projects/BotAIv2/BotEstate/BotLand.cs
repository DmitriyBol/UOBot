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
    public static bool Running { get; set; } = true;

    public static int Reach { get; set; } = 40;

    public static double Home { get; set; } = 1.25;

    public static double Abroad { get; set; } = 0.7;

    public static long AtHome { get; private set; }

    public static long Away { get; private set; }

    public static long Open { get; private set; }

    public static string Holder(Map map, Point3D where)
    {
        if (!Running || map == null || map == Map.Internal)
        {
            return null;
        }

        string held = null;

        var here = BotQuad.Key(map, where);

        foreach (var (name, hall) in BotEstate.Held)
        {
            if (hall is { Deleted: false } && hall.Map == map && BotQuad.Key(map, hall.Location) == here)
            {
                held = name;

                break;
            }
        }

        if (held == null)
        {
            foreach (var (name, post) in BotOutpost.Held)
            {
                if (post is { Deleted: false } && post.Map == map && BotQuad.Key(map, post.Location) == here)
                {
                    held = name;

                    break;
                }
            }
        }

        return held ?? BotClaim.Owner(map, where);
    }

    public static Guild Holding(Map map, Point3D where) => BaseGuild.FindByName(Holder(map, where)) as Guild;

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

    public static bool Trespassing(Mobile bot, out string whose)
    {
        whose = bot == null ? null : Holder(bot.Map, bot.Location);

        if (whose == null || bot.Guild?.Name == whose)
        {
            return false;
        }

        return !BotRegard.AreAllied(bot.Guild?.Name, whose);
    }

    public static string Describe() =>
        !Running
            ? "halls make no land"
            : $"land reaches {Reach} tiles from a hall, worth ×{Home:F2} to its own and ×{Abroad:F2} to anybody else; "
              + $"{AtHome} pieces of work weighed at home, {Away} on somebody else's ground, {Open} on ground nobody claims; "
              + BotClaim.Describe();

    public static void Forget()
    {
        AtHome = 0;
        Away = 0;
        Open = 0;
    }
}
