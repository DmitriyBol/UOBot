using Server.Logging;

namespace Server.BotAI.V2;

/// <summary>
/// The Baron walking his own ground: the nearest square nobody has stood in, alone if need be.
///
/// <para>
/// <b>The same errand the captain's scoutmaster offers, on two different terms, and both differences are
/// the office rather than the errand.</b> A captain buys a party — he is taking bots away from work that
/// pays, so he pays for it, and he does not go at all if too few will come. The Baron pays nobody and goes
/// regardless: whoever wants to walk with him may, for nothing, and if nobody does he walks alone. That is
/// what the office is. He already takes no share of any corpse and lives on a stipend — see
/// <c>BotStipend</c> — so a Baron who paid for company would be spending the crown's money to buy the
/// crown's own subjects, which is not a thing this shard should learn to do.
/// </para>
///
/// <para>
/// <b>And it is deliberately the humblest thing he does.</b> Reckoned low, so a great hunt or a rescue
/// outbids it every time: walking the frontier is what a Baron does when the island has nothing worse to
/// offer, which is most of the time and is exactly when the map most needs filling in.
/// </para>
/// </summary>
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
