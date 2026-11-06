using System;
using System.Collections.Generic;

namespace Server.BotAI.V2;

public static class BotNeeds
{

    public static int EveryMs { get; set; } = 60000;

    public static int MostStamps { get; set; } = 4096;

    public static long Asked { get; private set; }

    public static long Passed { get; private set; }

    private static readonly Dictionary<(Serial Who, string What), long> _asked = new();

    public static bool Due(Mobile body, string what)
    {
        if (body == null || string.IsNullOrEmpty(what))
        {
            return true;
        }

        var now = Core.TickCount;
        var key = (body.Serial, what);

        if (_asked.TryGetValue(key, out var last) && now - last < EveryMs)
        {
            Passed++;

            return false;
        }

        if (_asked.Count > MostStamps)
        {
            Sweep(now);
        }

        _asked[key] = now;
        Asked++;

        return true;
    }

    private static void Sweep(long now)
    {
        List<(Serial, string)> lapsed = [];

        foreach (var (key, last) in _asked)
        {
            if (now - last >= EveryMs)
            {
                lapsed.Add(key);
            }
        }

        for (var i = 0; i < lapsed.Count; i++)
        {
            _asked.Remove(lapsed[i]);
        }
    }

    public static string Describe() =>
        Asked + Passed == 0
            ? "nobody has looked at what they are short of yet"
            : $"needs reviewed {Asked} times, {Passed} asks inside the {EveryMs / 1000}s between one bot's reviews";

    public static void Forget()
    {
        _asked.Clear();
        Asked = 0;
        Passed = 0;
    }
}
