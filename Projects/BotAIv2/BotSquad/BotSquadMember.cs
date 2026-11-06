namespace Server.BotAI.V2;

public interface IBotSquadMember : IBotAlly
{

    Mobile Self { get; }

    BotClass Class { get; }

    BotJourney Journey { get; }

    BotSquad Squad { get; set; }
}
