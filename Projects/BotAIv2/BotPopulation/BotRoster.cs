using System;
using System.Collections.Generic;
using Server.Guilds;
using Server.Items;

namespace Server.BotAI.V2;

public static class BotRoster
{

    public static bool Running { get; set; } = true;

    public static int EveryMs { get; set; } = 1800000;

    public static int Shortlist { get; set; } = 5;

    public static int StopAt { get; set; }

    private static readonly Dictionary<string, long> _changed = [];

    public static long Changes { get; private set; }

    public static long TooSoon { get; private set; }

    public static long Gone { get; private set; }

    public static bool Free(Guild guild)
    {
        if (!Running || guild == null)
        {
            return false;
        }

        return !_changed.TryGetValue(guild.Name, out var when) || Core.TickCount - (when + EveryMs) >= 0;
    }

    public static int Waits(Guild guild)
    {
        if (guild == null || !_changed.TryGetValue(guild.Name, out var when))
        {
            return 0;
        }

        var left = EveryMs - (int)(Core.TickCount - when);

        return left <= 0 ? 0 : left / 60000;
    }

    public static List<BotMobile> Candidates(Guild guild, int most)
    {
        List<BotMobile> found = [];

        if (!Running || guild == null || guild.Disbanded)
        {
            return found;
        }

        var roll = BotPopulation.Bots;

        for (var i = 0; i < roll.Count; i++)
        {
            if (roll[i] is not BotMobile { Deleted: false, Alive: true } bot)
            {
                continue;
            }

            if (bot.Guild != null || BotGuilds.Outside(bot) || BotGuilds.Spurned(bot, guild))
            {
                continue;
            }

            found.Add(bot);
        }

        found.Sort(static (a, b) => Promise(b).CompareTo(Promise(a)));

        if (most > 0 && found.Count > most)
        {
            found.RemoveRange(most, found.Count - most);
        }

        return found;
    }

    public static List<BotMobile> Weakest(Guild guild, int most)
    {
        List<BotMobile> found = [];

        if (!Running || guild?.Members == null)
        {
            return found;
        }

        for (var i = 0; i < guild.Members.Count; i++)
        {

            if (guild.Members[i] is BotMobile { Deleted: false } member && !BotGuilds.IsHead(member))
            {
                found.Add(member);
            }
        }

        found.Sort(static (a, b) => Promise(a).CompareTo(Promise(b)));

        if (most > 0 && found.Count > most)
        {
            found.RemoveRange(most, found.Count - most);
        }

        return found;
    }

    public static double Promise(BotMobile bot)
    {
        if (bot is not { Deleted: false })
        {
            return 0.0;
        }

        return bot.Progress + Math.Min(1.0, (bot.Backpack?.GetAmount(typeof(Gold)) ?? 0) / 5000.0) * 0.01;
    }

    public static string Change(Guild guild, BotMobile putOut, BotMobile takeIn, string why)
    {
        if (!Running || guild == null || (putOut == null && takeIn == null))
        {
            return null;
        }

        if (!Free(guild))
        {
            TooSoon++;

            return null;
        }

        var put = putOut != null && ReferenceEquals(putOut.Guild, guild) && BotGuilds.Expel(guild, putOut, why);
        var took = takeIn is { Deleted: false, Alive: true, Guild: null } && BotGuilds.Recruit(guild, takeIn, why);

        if (!put && !took)
        {
            Gone++;

            return null;
        }

        _changed[guild.Name] = Core.TickCount;
        Changes++;

        if (put && took)
        {
            return $"put {putOut.Name} out and took {takeIn.Name} on";
        }

        return put ? $"put {putOut.Name} out" : $"took {takeIn.Name} on";
    }

    public static string Describe() =>
        !Running
            ? "no leader may change its roster"
            : $"{Changes} rosters changed by their leaders ({BotGuilds.Hired} taken on, {BotGuilds.Expelled} put out), "
            + $"one change per guild per {EveryMs / 60000} minutes: {TooSoon} asks came too soon, {Gone} named somebody "
            + $"who was gone by the time it was asked for, {BotGuilds.Crowded} found the guild full, "
            + $"{BotGuilds.HeldElsewhere} named somebody already in a guild, {BotGuilds.Spared} tried to put out their own maker";

    public static void Forget()
    {
        _changed.Clear();
        Changes = 0;
        TooSoon = 0;
        Gone = 0;
    }
}
