namespace Server.BotAI.V2;

public interface IBotWilful : IBotSquadMember
{

    BotResolve Resolve { get; }

    BotBond Bond { get; }
}
