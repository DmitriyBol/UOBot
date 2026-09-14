namespace Server.BotAI.V2;

/// <summary>
/// What a squad needs a bot to be. Implemented by the bot.
///
/// Four things and no more, and the shortness is the point: a squad decides where its people stand and
/// who gets what, and to do that it has to know where they are, what they are for, whether they can
/// fight, and how to send them somewhere. It has no business knowing anything else about them.
/// </summary>
public interface IBotSquadMember : IBotAlly
{
    Mobile Self { get; }

    BotClass Class { get; }

    BotJourney Journey { get; }

    BotSquad Squad { get; set; }
}
