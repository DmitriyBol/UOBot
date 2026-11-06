using System;
using System.Collections.Generic;
using Server.Guilds;
using Server.Logging;
using Server.Multis;

namespace Server.BotAI.V2;

public static class BotExile
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotExile));

    public static bool Running { get; set; } = true;

    public static int Clear => BotLand.Reach;

    public static int ClaimMs { get; set; } = 240000;

    public const string Office = "remover";

    public static long Judged { get; private set; }

    public static long Drawn { get; private set; }

    public static long Groundless { get; private set; }

    public static long Moved { get; private set; }

    private static readonly Dictionary<string, string> _owed = [];

    public static void Draw(string one, string other)
    {
        if (!Running || one == null || other == null || one == other)
        {
            return;
        }

        Judged++;
        Drawn++;

        logger.Information("The war between {One} and {Other} ended with nobody ahead; nobody moves", one, other);
    }

    public static void Sentence(string winner, string loser)
    {
        if (!Running || winner == null || loser == null || winner == loser)
        {
            return;
        }

        if (!AfterWar)
        {
            Superseded++;

            return;
        }

        Judged++;

        if (BaseGuild.FindByName(winner) is not Guild won || BaseGuild.FindByName(loser) is not Guild lost ||
            BotEstate.Hall(won) is not { Deleted: false } || BotEstate.Hall(lost) is not { Deleted: false })
        {
            Groundless++;

            return;
        }

        _owed[loser] = winner;

        logger.Warning(
            "{Loser} lost its war with {Winner} and must move its hall outside the winner's yard",
            loser,
            winner
        );
    }

    public static IEnumerable<KeyValuePair<string, string>> Owing => _owed;

    public static void Restore(string loser, string winner)
    {
        if (loser != null && winner != null)
        {
            _owed[loser] = winner;
        }
    }

    public static BaseHouse Owed(Guild guild)
    {
        if (!Running || !AfterWar || guild == null || !_owed.TryGetValue(guild.Name, out var winner))
        {
            return null;
        }

        if (BaseGuild.FindByName(winner) is not Guild won || BotEstate.Hall(won) is not { Deleted: false } hall)
        {

            _owed.Remove(guild.Name);

            return null;
        }

        return hall;
    }

    public static bool AfterWar { get; set; }

    public static long Superseded { get; private set; }

    public static void Paid(Guild guild)
    {
        if (guild != null && _owed.Remove(guild.Name))
        {
            Moved++;
        }
    }

    public static string Describe()
    {
        if (!Running)
        {
            return "a lost war costs no ground";
        }

        return Judged == 0
            ? "no war has been judged"
            : $"{Judged} wars judged, {Drawn} of them drawn, {Groundless} with no hall to move or none to move away from; "
            + $"{Moved} halls carried outside the winner's yard, {_owed.Count} still owing a move";
    }

    public static void Forget()
    {
        _owed.Clear();
        Judged = 0;
        Drawn = 0;
        Groundless = 0;
        Moved = 0;
    }
}
