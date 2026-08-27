using System.Collections.Generic;
using Server.Mobiles;

namespace Server.BotAI.V2;

/// <summary>
/// Somebody of ours is being killed and has said so out loud.
///
/// <para>
/// <b>This is the one thing in the project that is genuinely a message.</b> Everything else a company does is
/// arithmetic every member repeats for itself — stations, patches, shares — precisely so that nothing has to
/// be sent, received or kept in step. A cry cannot be arithmetic: what makes it urgent is that it happened at
/// a moment, to one bot, and nobody else can derive it from where they are standing. The squad's own note
/// allows exactly two such events, and names this as one of them.
/// </para>
///
/// <para>
/// <b>It is a fact, not a summons.</b> Nothing here sends a bot anywhere. A cry is posted, it expires on its
/// own, and whoever is free enough to care picks it up through the ordinary auction as a piece of work worth
/// more than digging — see <see cref="BotRescuer"/>. That matters because the first version's help system was
/// an order: a bot posted a call, the call found nobody able, it disbanded in the same tick, and the bot
/// posted it again dozens of times over. An offer that nobody takes is silence; an order that nobody can obey
/// is a loop.
/// </para>
/// </summary>
public static class BotCry
{
    /// <summary>
    /// How long a cry is worth answering.
    ///
    /// Short. Help that arrives half a minute after a fight is help that arrives at a corpse, and a stale cry
    /// on the board sends bots across the map to stand in an empty field. Renewed every beat the bot is still
    /// in trouble, so a long fight keeps its cry alive without anybody repeating themselves.
    /// </summary>
    public static int HoldsMs { get; set; } = 20000;

    /// <summary>How far a cry carries. Wider than a company forms across: this is the whole point of it.</summary>
    public static int Carries { get; set; } = 40;

    private static readonly Dictionary<Serial, (Mobile Who, Mobile What, long Tick)> _cries = [];

    /// <summary>Cries raised and cries answered. Both, because neither number means anything alone.</summary>
    public static long Raised { get; private set; }

    public static long Answered { get; private set; }

    /// <summary>
    /// Says that this bot is being set upon by that. Renewing an existing cry costs nothing and is expected.
    /// </summary>
    public static void Raise(Mobile who, Mobile what)
    {
        if (who is not { Deleted: false, Alive: true } || what is not { Deleted: false, Alive: true })
        {
            return;
        }

        if (!_cries.ContainsKey(who.Serial))
        {
            Raised++;
        }

        _cries[who.Serial] = (who, what, Core.TickCount);
    }

    /// <summary>Over, one way or the other. Called when the bot is clear, or dead, or has been helped.</summary>
    public static void Quiet(Mobile who)
    {
        if (who != null)
        {
            _cries.Remove(who.Serial);
        }
    }

    /// <summary>
    /// The nearest of ours who is calling for help within reach of this one, and what is on them.
    ///
    /// <para>
    /// Nearest rather than worst off, deliberately. Whoever is closest can be reached soonest, and a rescue
    /// that arrives is worth more than a better-chosen one that arrives too late — the same reasoning the
    /// mending subsystem uses about who a medic goes to.
    /// </para>
    /// </summary>
    public static (Mobile Who, BaseCreature What) Nearest(Mobile helper, int range)
    {
        if (helper?.Map is not { } map || map == Map.Internal)
        {
            return (null, null);
        }

        Mobile found = null;
        BaseCreature onThem = null;
        var closest = int.MaxValue;

        List<Serial> stale = null;

        foreach (var (serial, cry) in _cries)
        {
            var (who, what, tick) = cry;

            if (Core.TickCount - tick >= HoldsMs
                || who is not { Deleted: false, Alive: true }
                || what is not BaseCreature { Deleted: false, Alive: true } creature)
            {
                (stale ??= []).Add(serial);

                continue;
            }

            if (who == helper || who.Map != map)
            {
                continue;
            }

            var away = (int)helper.GetDistanceToSqrt(who.Location);

            if (away > range || away >= closest)
            {
                continue;
            }

            closest = away;
            found = who;
            onThem = creature;
        }

        if (stale != null)
        {
            for (var i = 0; i < stale.Count; i++)
            {
                _cries.Remove(stale[i]);
            }
        }

        return (found, onThem);
    }

    /// <summary>Counted where the help is actually taken on, so the tally is of rescues and not of offers.</summary>
    public static void Noted() => Answered++;

    public static string Describe() =>
        Raised == 0 ? "nobody has called for help" : $"{Raised} cried for help, {Answered} were gone to";

    public static void Forget()
    {
        _cries.Clear();
        Raised = 0;
        Answered = 0;
    }
}
