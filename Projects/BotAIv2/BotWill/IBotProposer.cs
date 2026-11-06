namespace Server.BotAI.V2;

public interface IBotProposer
{

    string Name { get; }

    BotStanding Rung { get; }

    BotDeed Propose(IBotWilful bot);
}
