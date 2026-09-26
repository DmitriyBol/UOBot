namespace Server.BotAI.V2;

/// <summary>
/// The supply side of the auction: something that knows about one kind of work and can offer a bot a piece
/// of it.
///
/// <para>
/// <b>The brain does not hold a list of goals.</b> It holds a list of proposers, and they are registered by
/// the subsystems that own the work — mining by the mining folder, orders by the crafting folder, a muster
/// by the squad folder. Adding trade to this shard must not be an edit to the decision layer, and this
/// interface is the whole of why it is not.
/// </para>
///
/// <para>
/// <b>It is also how the slow tier gets a vote by construction.</b> The first version put a language model
/// behind the brain as an advisor, and the brain took 85 of the 135 plans it managed to review — the model's
/// suggestion lost to any errand the brain had of its own, and nothing recorded that it had lost, so the
/// model spent the night learning from noise and finished with 0 of 119 predictions borne out. A model that
/// proposes through this interface is offering in the same units as everybody else, wins or loses on the
/// same arithmetic, and has its actual takings written into the same ledger.
/// </para>
/// </summary>
public interface IBotProposer
{
    string Name { get; }

    BotStanding Rung { get; }

    BotDeed Propose(IBotWilful bot);
}
