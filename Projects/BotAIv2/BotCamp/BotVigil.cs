using System;
using System.Collections.Generic;
using Server.Logging;

namespace Server.BotAI.V2;

/// <summary>
/// The hour's fallen, kept for a fire's remembering (the ideas ledger's fourth, 18.11.2026): every bot death is one
/// line in a short ring, and now and then a fire raises its glass to one of them — "To {name}, whom {what} took this
/// hour" — instead of the small talk. The island's deaths stop being numbers and become a word said out loud over the
/// people they were; the comic ones ("a rat did for them") and the war dead go into the same glass, which is how a
/// fire actually talks.
/// </summary>
public static class BotVigil
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotVigil));

    /// <summary>How long a death stays in the glass. One hour: longer is a graveyard, not a vigil.</summary>
    public static int KeptMs { get; set; } = 3600000;

    /// <summary>The most fallen kept at once. Enough for a bad hour; the ring forgets by itself after.</summary>
    public static int Most { get; set; } = 16;

    public static long Fell { get; private set; }

    public static long Vigils { get; private set; }

    private static readonly List<(string Name, string What, long At)> _fallen = [];

    /// <summary>One of ours died. Called from <see cref="BotBiography.Died"/>, before any tale is kept.</summary>
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

    /// <summary>
    /// The freshest of the hour's fallen, for a fire to drink to: fills <c>{name}</c> and <c>{what}</c> and returns
    /// true, or false when nobody has fallen within the hour — a fire that drinks to nobody is making it up.
    /// </summary>
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
