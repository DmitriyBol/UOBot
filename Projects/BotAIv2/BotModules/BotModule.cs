namespace Server.BotAI.V2;

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
