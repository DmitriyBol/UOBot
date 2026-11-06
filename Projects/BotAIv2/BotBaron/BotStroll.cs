namespace Server.BotAI.V2;

public sealed class BotStroll : IBotProposer
{
    public string Name => "Stroll";

    public BotStanding Rung => BotStanding.Free;

    public static long Asked { get; private set; }

    public static long NotABaron { get; private set; }

    public static long Held { get; private set; }

    public static long Townless { get; private set; }

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

        if (bot is not IBotSquadMember { Squad: null })
        {
            Held++;

            return null;
        }

        var town = BotGround.Counter(map, BotPopulation.Where);

        if (town == Point3D.Zero)
        {
            Townless++;

            return null;
        }

        Offered++;

        return new BotRounds(map, town);
    }

    public static string Describe() =>
        Asked == 0
            ? $"no Baron has ever been offered a walk ({NotABaron} answers went to bots that are not Barons)"
            : $"{Asked} times a Baron was asked: {Offered} were offered a town, {Held} were leading a company, {Townless} had no counter surveyed to walk around; {BotRounds.Describe()}";

    public static void Forget()
    {
        Asked = 0;
        NotABaron = 0;
        Held = 0;
        Townless = 0;
        Offered = 0;

        BotRounds.Forget();
    }
}
