using System;
using System.Collections.Generic;

namespace Server.BotAI.Mind;

public static class BotMindClaims
{

    public static int HoldsMs { get; set; } = 900000;

    private static readonly Dictionary<string, (string Who, long Tick)> _claims = new(StringComparer.OrdinalIgnoreCase);

    public static long Made { get; private set; }

    public static long Contested { get; private set; }

    public static bool Claim(string who, string row)
    {
        if (string.IsNullOrWhiteSpace(who) || string.IsNullOrWhiteSpace(row))
        {
            return false;
        }

        var (holder, _) = Holder(row);

        if (holder != null && !string.Equals(holder, who, StringComparison.OrdinalIgnoreCase))
        {
            Contested++;
        }

        _claims[row.Trim()] = (who, Core.TickCount);
        Made++;

        return true;
    }

    public static (string Who, int SecondsAgo) Holder(string row)
    {
        if (string.IsNullOrWhiteSpace(row) || !_claims.TryGetValue(row.Trim(), out var claim))
        {
            return (null, 0);
        }

        var since = Core.TickCount - claim.Tick;

        if (since >= HoldsMs)
        {
            _claims.Remove(row.Trim());

            return (null, 0);
        }

        return (claim.Who, (int)(since / 1000));
    }

    public static (string Row, int SecondsAgo) Of(string who)
    {
        foreach (var (row, claim) in _claims)
        {
            if (!string.Equals(claim.Who, who, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var since = Core.TickCount - claim.Tick;

            if (since < HoldsMs)
            {
                return (row, (int)(since / 1000));
            }
        }

        return (null, 0);
    }

    public static int Standing()
    {
        var n = 0;

        foreach (var (_, claim) in _claims)
        {
            if (Core.TickCount - claim.Tick < HoldsMs)
            {
                n++;
            }
        }

        return n;
    }

    public static string Describe() =>
        $"{Made} orders on the board claimed by a mind ({Contested} over somebody else's claim), {Standing()} claims standing";

    public static void Forget()
    {
        _claims.Clear();
        Made = 0;
        Contested = 0;
    }
}
