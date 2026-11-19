using System;
using System.Collections.Generic;
using Server.Logging;
using Server.Regions;

namespace Server.BotAI.V2;

/// <summary>
/// The town cry (the ideas ledger's first, 18.11.2026): once in a while, a bot standing in a guarded town calls the
/// day's chief news out loud — the Chronicle's own heaviest entry of today, in the local channel, where any player
/// about hears the island's history being made. Nobody walks anywhere and nobody is stopped: the crier is whoever
/// happens to be in town, the way a cry goes up.
///
/// <para>
/// <b>Only what the Chronicle truly holds.</b> The entry is picked by weight from today's page — not composed, not
/// imagined — and a day with nothing on its page is a day the cry keeps quiet, which is itself the truth.
/// </para>
/// </summary>
public static class BotTownCry
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotTownCry));

    public static bool Running { get; set; } = true;

    /// <summary>How often the cry goes up. Forty-five minutes: news stays news, and a cry every minute is noise.</summary>
    public static int EveryMs { get; set; } = 2700000;

    public static long Cries { get; private set; }

    private static long _next;

    /// <summary>The society's second: called often, acting rarely.</summary>
    public static void Tick(long now)
    {
        if (!Running || _next != 0 && now - _next < 0)
        {
            return;
        }

        _next = now + Math.Max(600000, EveryMs);

        var key = DateTime.Now.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

        if (!BotChronicle.Days.TryGetValue(key, out var day))
        {
            return;
        }

        BotChronicleEntry chief = null;

        for (var i = 0; i < day.Entries.Count; i++)
        {
            if (day.Entries[i] != null && (chief == null || day.Entries[i].Weight > chief.Weight))
            {
                chief = day.Entries[i];
            }
        }

        if (chief == null || string.IsNullOrEmpty(chief.Text))
        {
            return;
        }

        var bots = BotPopulation.Bots;
        BotMobile crier = null;
        var seen = 0;

        for (var i = 0; i < bots.Count; i++)
        {
            if (bots[i] is not { Deleted: false, Alive: true } bot || bot.Map == null || bot.Map == Map.Internal)
            {
                continue;
            }

            if (!Region.Find(bot.Location, bot.Map).IsPartOf<GuardedRegion>())
            {
                continue;
            }

            if (Utility.Random(++seen) == 0)
            {
                crier = bot;
            }
        }

        if (crier == null)
        {
            return;
        }

        var fill = new Dictionary<string, string> { ["tale"] = chief.Text };

        if (!BotCampTalk.Say(crier, "crier:call", null, null, fill))
        {
            return;
        }

        Cries++;

        logger.Information("{Who} cries the day's news in town: {News}", crier.Name, chief.Text);
    }

    public static string Describe() =>
        !Running
            ? "no town cry"
            : $"{Cries} cries of the day's news raised in town";

    public static void Forget()
    {
        Cries = 0;
        _next = 0;
    }
}
