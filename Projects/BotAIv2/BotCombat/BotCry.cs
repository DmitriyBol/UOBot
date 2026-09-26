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
    public static int HoldsMs { get; set; } = 20000;

    public static int Carries { get; set; } = 60;

    private static readonly Dictionary<Serial, (Mobile Who, Mobile What, long Tick)> _cries = [];

    public static long Raised { get; private set; }

    public static long Answered { get; private set; }

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

    public static void Quiet(Mobile who)
    {
        if (who != null)
        {
            _cries.Remove(who.Serial);
        }
    }

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

    public static void Noted() => Answered++;

    public static long ReactionMs { get; private set; }

    public static long Reactions { get; private set; }

    public static void Answering(Mobile who)
    {
        if (who == null || !_cries.TryGetValue(who.Serial, out var cry))
        {
            return;
        }

        ReactionMs += Core.TickCount - cry.Tick;
        Reactions++;

        _cries[who.Serial] = (cry.Who, cry.What, long.MinValue / 2);
    }

    public static string Describe() =>
        Raised == 0
            ? "nobody has called for help"
            : $"{Raised} cried for help, {Answered} were gone to, the first helper setting out {(Reactions > 0 ? ReactionMs / Reactions / 1000.0 : 0.0):F1}s after the cry on average over {Reactions}; {BotDefender.Already} blows not answered with a second errand because the work in hand was already that fight, {BotDefender.Outnumbered} not answered with a fight the odds round the bot already called off, {BotDefender.Leading} on a company's leader left to the company, {BotDefender.Below} fights underground left to the delve's company";

    public static void Forget()
    {
        _cries.Clear();
        Raised = 0;
        Answered = 0;
        ReactionMs = 0;
        Reactions = 0;
    }
}
