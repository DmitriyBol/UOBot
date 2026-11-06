using System;
using Server.Guilds;
using Server.Mobiles;

namespace Server.BotAI.V2;

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

        var claimed = BotClaim.Owner(map, where);

        if (claimed != null)
        {
            return claimed;
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

        return held;
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
              + BotClaim.Describe() + "; " + BotBounds.Describe();

    public static void Forget()
    {
        AtHome = 0;
        Away = 0;
        Open = 0;
    }
}
