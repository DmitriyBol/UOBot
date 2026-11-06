using Server.Logging;

namespace Server.BotAI.V2;

public sealed class BotWarden : IBotProposer
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotWarden));

    public string Name => "Warden";

    public BotStanding Rung => BotStanding.Free;

    public static int Wage { get; set; }

    public static int Least { get; set; } = 1;

    public static int Rounds { get; set; } = 20;

    public static long NotABaron { get; private set; }

    public static long Asked { get; private set; }

    public static long Held { get; private set; }

    public static long Unfit { get; private set; }

    public static long Charted { get; private set; }

    public static long Sealed { get; private set; }

    public static long Wanted { get; private set; }

    public static long Offered { get; private set; }

    public BotDeed Propose(IBotWilful bot)
    {
        var body = bot?.Self;
        var map = body?.Map;

        if (map == null || map == Map.Internal || !body.Alive)
        {
            return null;
        }

        if (body is not BotMobile { Class: BotBaron })
        {
            NotABaron++;

            return null;
        }

        Asked++;

        if (!BotSquads.Running)
        {
            return null;
        }

        if (bot is IBotSquadMember member && member.Squad != null
            && (!ReferenceEquals(member.Squad.Leader, member) || bot.Resolve?.Deed != null))
        {
            Held++;

            return null;
        }

        if (body.HitsMax <= 0 || body.Hits < body.HitsMax * BotHunter.FitAt)
        {
            Unfit++;

            return null;
        }

        if (BotQuad.Direst(map, body.Location, BotHarrower.Range, BotHarrower.Takeable(map, body.Location)) != null)
        {
            Wanted++;

            return null;
        }

        var where = BotQuad.Frontier(map, body.Location, BotScout.Range, at => BotScout.Roadworthy(map, at) && Reachable(map, body.Location, at));

        if (where == Point3D.Zero)
        {
            Charted++;

            return null;
        }

        Offered++;

        return new BotScout(map, where, Wage, Least, Rounds);
    }

    private static bool Reachable(Map map, Point3D from, Point3D at)
    {
        if (BotReach.Ask(map, from, at, BotArrival.Within(BotQuad.Side / 3)) != BotReachVerdict.Sealed)
        {
            return true;
        }

        Sealed++;

        return false;
    }

    public static string Describe() =>
        Asked == 0
            ? $"no Baron has ever been offered his rounds ({NotABaron} answers went to bots that are not Barons)"
            : $"{Asked} times a Baron was asked to walk his rounds: {Offered} were offered unknown ground, "
              + $"{Held} were already leading a company, {Unfit} were too hurt, "
              + $"{Charted} found everything within {BotScout.Range} tiles already walked, {Sealed} found no way through to it, {Wanted} stood aside for ground dire enough to harrow";

    public static void Forget()
    {
        Asked = 0;
        NotABaron = 0;
        Held = 0;
        Unfit = 0;
        Charted = 0;
        Sealed = 0;
        Wanted = 0;
        Offered = 0;
    }
}
