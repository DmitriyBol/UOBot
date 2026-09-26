namespace Server.BotAI.V2;

/// <summary>
/// When a module is allowed to run. Two moments, because there are two, and the difference between
/// them is not a matter of taste.
///
/// The first version had no such distinction and paid for it more than once. Its clearest form: an
/// index of what is made of what, built while the server was reading settings, came out holding eleven
/// nulls — the craft tables it was reading are created by content initialisation, which had not run
/// yet. Nothing was wrong with the index. It had simply asked a question the world could not yet
/// answer, and the answer to a question asked too early is not an error, it is an empty list.
/// </summary>
public enum BotPhase
{
    Settings,

    World
}
