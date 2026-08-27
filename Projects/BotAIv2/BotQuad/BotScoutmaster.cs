using System;
using Server.Logging;

namespace Server.BotAI.V2;

/// <summary>
/// Offers a captain the nearest ground nobody has ever stood in.
///
/// <para>
/// <b>Nearest rather than worst, which is the opposite of how every other errand on this shard picks its
/// destination.</b> A patrol goes where it is most dangerous and a great hunt goes where it is most dire,
/// because both are answers to "where is the trouble". This is an answer to "what do we not know", and
/// unknown ground is all equally unknown — there is nothing to rank it by. So the tiebreak is the walk:
/// the map fills outwards from where the population actually lives, which is also the order in which the
/// knowledge is worth having.
/// </para>
///
/// <para>
/// <b>And the candidates are ground the population has been <em>near</em>, never a search of the island.</b>
/// The quadrant table only holds squares something has happened in or beside, so the unknown squares this
/// can offer are the fringe of what is known — the ring just past the frontier. That keeps the question
/// cheap, keeps parties from being sent across the map, and means the frontier advances a ring at a time
/// under its own steam.
/// </para>
/// </summary>
public sealed class BotScoutmaster : IBotProposer
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotScoutmaster));

    public string Name => "Scoutmaster";

    public BotStanding Rung => BotStanding.Free;

    /// <summary>Asked of a bot that is not a captain. Not a refusal — most answers are this.</summary>
    public static long NotACaptain { get; private set; }

    public static long Asked { get; private set; }

    public static long Held { get; private set; }

    public static long Unfit { get; private set; }

    /// <summary>Captains too poor to pay a party and still stand on their feet.</summary>
    public static long Poor { get; private set; }

    /// <summary>The fattest purse among those. See <c>BotStable.Richest</c> for why this is kept.</summary>
    public static long Richest { get; private set; }

    /// <summary>Nothing unknown within reach. The island around the population is read.</summary>
    public static long Charted { get; private set; }

    /// <summary>Unknown ground with no way through to it that anybody has found.</summary>
    public static long Sealed { get; private set; }

    /// <summary>Too few free bodies about to be worth calling.</summary>
    public static long TooFewNear { get; private set; }

    public static long Offered { get; private set; }

    public BotDeed Propose(IBotWilful bot)
    {
        var body = bot?.Self;
        var map = body?.Map;

        if (map == null || map == Map.Internal || !body.Alive)
        {
            return null;
        }

        // Counted before the class check so "nobody is a captain" and "the captain never gets an offer" are
        // different numbers rather than the same silence.
        if (body is not BotMobile { Class.Leads: true })
        {
            NotACaptain++;

            return null;
        }

        Asked++;

        if (!BotSquads.Running)
        {
            return null;
        }

        if (bot is not IBotSquadMember { Squad: null })
        {
            Held++;

            return null;
        }

        if (body.HitsMax <= 0 || body.Hits < body.HitsMax * BotHunter.FitAt)
        {
            Unfit++;

            return null;
        }

        // Asked before the ground is looked for rather than after: a captain who cannot pay is not going,
        // and walking the whole frontier to find that out would be work done for a refusal.
        var wealth = BotYield.Wealth(body);

        if (wealth - BotScout.Wage < BotScout.Solvent)
        {
            Poor++;

            if (wealth > Richest)
            {
                Richest = wealth;
            }

            return null;
        }

        var where = Unknown(map, body.Location, BotScout.Range);

        if (where == Point3D.Zero)
        {
            Charted++;

            return null;
        }

        if (Free(body, BotScout.Reach) < BotScout.Least - 1)
        {
            TooFewNear++;

            return null;
        }

        Offered++;

        return new BotScout(map, where);
    }

    /// <summary>The nearest unknown ground this captain could actually get to.</summary>
    private static Point3D Unknown(Map map, Point3D from, int within) =>
        BotQuad.Frontier(map, from, within, at => Reachable(map, from, at));

    /// <summary>Whether the ground between here and there is not already known to be closed.</summary>
    private static bool Reachable(Map map, Point3D from, Point3D at)
    {
        // A dictionary lookup against pockets already proved closed by searches that failed — never a fresh
        // search. See BotHunter.Hunting for what a real search per candidate per beat costs this shard.
        if (BotReach.Ask(map, from, at, BotArrival.Within(BotQuad.Side / 3)) != BotReachVerdict.Sealed)
        {
            return true;
        }

        Sealed++;

        return false;
    }

    /// <summary>Bots near enough to be called on, who can fight and are not already in a company.</summary>
    private static int Free(Mobile body, int range)
    {
        var map = body.Map;
        var free = 0;

        foreach (var mobile in map.GetMobilesInRange<Mobile>(body.Location, range))
        {
            if (mobile == body || mobile is not IBotSquadMember { Squad: null })
            {
                continue;
            }

            if (mobile is IBotAlly { AbleToFight: true })
            {
                free++;
            }
        }

        return free;
    }

    public static string Describe() =>
        Asked == 0
            ? $"no captain has ever been offered a scouting party ({NotACaptain} answers went to bots that are not captains)"
            : $"{Asked} times a captain was asked to scout: {Offered} were offered unknown ground, {Held} were already in a company, "
              + $"{Unfit} were too hurt, {Poor} could not pay {BotScout.Wage}gp and keep {BotScout.Solvent} (the fattest purse among them held {Richest}gp), "
              + $"{Charted} found everything within {BotScout.Range} tiles already walked, {Sealed} found no way through to it, "
              + $"{TooFewNear} had too few free bots near; {BotScout.Describe()}";

    public static void Forget()
    {
        Asked = 0;
        NotACaptain = 0;
        Held = 0;
        Unfit = 0;
        Poor = 0;
        Richest = 0;
        Charted = 0;
        Sealed = 0;
        TooFewNear = 0;
        Offered = 0;

        BotScout.Forget();
    }
}
