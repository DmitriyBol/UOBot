namespace Server.BotAI.V2;

/// <summary>
/// One switchable, ordered piece of the population.
///
/// <para>
/// <b>A module is not the same thing as a folder.</b> A module has start-up work, or a switch worth
/// having, or both. A folder that only offers services to callers — the kit handout, for instance,
/// which does nothing until a bot is born and would break every bot if it were turned off — is not a
/// module, and making it one would be ceremony. The distinction is what keeps this from becoming
/// paperwork.
/// </para>
///
/// <para>
/// <b>Order is declared, not arranged.</b> A module names what it needs ready and the loader works out
/// the sequence. That is the whole point: in the first version the load order was a list of calls held
/// together by fifteen comments saying "this must come after that", and a mistake in it could not be
/// detected by reading — only by noticing, hours later, that something had quietly read an empty list.
/// A declared dependency that cannot be met is a named failure at boot.
/// </para>
/// </summary>
public abstract class BotModule
{
    public abstract string Name { get; }

    public abstract BotPhase Phase { get; }

    public virtual string[] Requires => [];

    public bool Ready { get; internal set; }

    public bool Enabled { get; internal set; } = true;

    public abstract void Start();

    public virtual void Reset()
    {
    }
}
