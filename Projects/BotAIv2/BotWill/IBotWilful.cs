namespace Server.BotAI.V2;

/// <summary>
/// What deciding needs a bot to be. Implemented by the bot, like the squad's contract, and deliberately
/// built on top of it rather than beside it.
///
/// <para>
/// <b>Why it extends <see cref="IBotSquadMember"/> instead of repeating it.</b> Three of the four things
/// deciding needs — the body, the class, the journey — are the same three a squad needs, and the fourth,
/// squad membership, is something deciding must be able to read: a bot whose place is the squad's business
/// must not be given somewhere else to be. Reading another subsystem's data is allowed here; asking it to
/// decide something is not. Nothing in this folder calls into <c>BotSquad</c>.
/// </para>
/// </summary>
public interface IBotWilful : IBotSquadMember
{
    BotResolve Resolve { get; }

    BotBond Bond { get; }
}
