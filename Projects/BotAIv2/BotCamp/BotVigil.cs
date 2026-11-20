using System;
using System.Collections.Generic;
using Server.Logging;

namespace Server.BotAI.V2;

public static class BotVigil
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotVigil));

    public static int KeptMs { get; set; } = 3600000;

    public static int Most { get; set; } = 16;

    public static long Fell { get; private set; }

    public static long Vigils { get; private set; }

    private static readonly List<(string Name, string What, long At)> _fallen = [];

    public static void Died(BotMobile victim, Mobile killer)
    {
        if (victim == null)
        {
            return;
        }

        Fell++;

        var what = killer == null
            ? "something"
            : killer is BotMobile
                ? killer.Name
                : BotVoice.Words(killer.Name) ?? "something";

        _fallen.Add((victim.Name, what, Core.TickCount));

        while (_fallen.Count > Math.Max(4, Most))
        {
            _fallen.RemoveAt(0);
        }
    }

    public static bool Toast(Dictionary<string, string> fill)
    {
        var fresh = Core.TickCount - Math.Max(60000, KeptMs);
        (string Name, string What, long At) pick = default;
        var found = false;

        for (var i = _fallen.Count - 1; i >= 0; i--)
        {
            if (_fallen[i].At >= fresh)
            {
                pick = _fallen[i];
                found = true;

                break;
            }
        }

        if (!found)
        {
            return false;
        }

        fill["name"] = pick.Name;
        fill["what"] = pick.What;
        Vigils++;

        return true;
    }

    public static string Describe() => $"{Fell} of ours kept in the hour's glass, {Vigils} vigils drunk to them";

    public static void Forget()
    {
        Fell = 0;
        Vigils = 0;
        _fallen.Clear();
    }
}
