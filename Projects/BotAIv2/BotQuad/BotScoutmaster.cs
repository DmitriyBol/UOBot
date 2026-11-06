using System;
using Server.Logging;

namespace Server.BotAI.V2;

public sealed class BotScoutmaster : IBotProposer
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotScoutmaster));

    public string Name => "Scoutmaster";

    public BotStanding Rung => BotStanding.Free;

    public static long NotACaptain { get; private set; }

    public static long Asked { get; private set; }

    public static long Held { get; private set; }

    public static long Unfit { get; private set; }

    public static long Poor { get; private set; }

    public static long Richest { get; private set; }

    public static long Charted { get; private set; }

    public static long Resurveyed { get; private set; }

    public static long Sealed { get; private set; }

    public static long TooFewNear { get; private set; }

    public static long Offered { get; private set; }

    public static double VetMs { get; set; } = 300.0;

    public static long Cramped { get; private set; }

    public BotDeed Propose(IBotWilful bot)
    {
        var body = bot?.Self;
        var map = body?.Map;

        if (map == null || map == Map.Internal || !body.Alive)
        {
            return null;
        }

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

        var wealth = BotYield.Spendable(body);

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
        var again = false;

        if (where == Point3D.Zero)
        {
            where = BotQuad.Stalest(map, body.Location, BotScout.Range, at => Reachable(map, body.Location, at));
            again = where != Point3D.Zero;
        }

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

        var cramped = BotPath.Starved;

        if (!BotPath.CanReach(map, body.Location, where, BotArrival.Within(BotQuad.Side / 3), VetMs))
        {
            if (BotPath.Starved != cramped)
            {
                Cramped++;

                return null;
            }

            BotQuad.Seen(map, where);
            Sealed++;

            return null;
        }

        Offered++;

        if (again)
        {
            Resurveyed++;
        }

        return new BotScout(map, where);
    }

    private static Point3D Unknown(Map map, Point3D from, int within) =>
        BotQuad.Frontier(map, from, within, at => BotScout.Roadworthy(map, at) && Reachable(map, from, at));

    private static bool Reachable(Map map, Point3D from, Point3D at)
    {

        if (BotReach.Ask(map, from, at, BotArrival.Within(BotQuad.Side / 3)) != BotReachVerdict.Sealed)
        {
            return true;
        }

        Sealed++;

        return false;
    }

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
              + $"{Charted} found everything within {BotScout.Range} tiles already walked and counted lately, {Resurveyed} were sent back to ground uncounted for {BotQuad.StaleMs / 3600000} hours, {Sealed} found no way through to it and were struck off the frontier, {Cramped} could not be looked at for want of clock, "
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
        Resurveyed = 0;
        Sealed = 0;
        Cramped = 0;
        TooFewNear = 0;
        Offered = 0;

        BotScout.Forget();
    }
}
