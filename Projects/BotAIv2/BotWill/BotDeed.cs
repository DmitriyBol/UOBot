namespace Server.BotAI.V2;

/// <summary>
/// One undertaking: a piece of work with stages, held until it finishes, fails or is dropped.
///
/// <para>
/// <b>Its stages are its own business.</b> Mining is not "walk to a vein" — it is dig, then smelt, then bank
/// what came of it, and an undertaking that ends at the vein leaves a bot standing underground holding ore
/// nobody can buy. So the undertaking, not the brain, knows the sequence: it is asked what to do now, over
/// and over, and it answers with a place, with "work here", or with an ending. The brain never learns what
/// ore is.
/// </para>
///
/// <para>
/// <b>This is what replaces the first version's one big method.</b> There, choosing was a single function of
/// 1209 lines inside a file of 7985 that referenced 57 other modules, so every change to any behaviour was a
/// change to that file. Here a new kind of work is a new folder with a proposer and a subclass of this, and
/// nothing in <c>BotWill/</c> is touched at all.
/// </para>
///
/// <para>
/// <b>It is also where the bot's state lives while the work is in progress.</b> An undertaking is created per
/// bot by its proposer and held on that bot's <see cref="BotResolve"/> — so a proposer keeps no table keyed
/// by serial, and a deleted bot takes its half-finished business with it.
/// </para>
/// </summary>
public abstract class BotDeed
{
    public abstract string Kind { get; }

    public abstract Map Map { get; }

    public abstract Point3D Where { get; }

    public abstract double Expects { get; }

    public virtual SkillName? Trains => null;

    public virtual int Outlay => 0;

    public virtual bool AtCounter => false;

    public virtual double Coin => 1.0;

    public virtual double Minutes => 5.0;

    public virtual int Made => 0;

    public abstract BotDoing Advance(IBotWilful bot);

    public virtual bool Bend(IBotWilful bot) => false;

    public virtual bool BendIsTrouble => true;

    public virtual double HoldsFor => 0.0;

    public virtual bool Pressing(IBotWilful bot) => false;

    public virtual bool Repeats(BotDeed other) => false;

    public virtual bool Unpaid => false;

    public virtual bool Posted => false;

    public virtual bool Standing => false;

    public virtual bool Alongside => false;

    public virtual bool Committed => false;

    public virtual bool Paperwork => false;

    public virtual bool Guess => false;

    public virtual bool Still => false;

    public virtual bool Steadfast => false;

    public virtual bool Summons => false;

    public virtual bool Braves => false;

    public virtual Mobile Foe => null;

    public virtual bool Afoot => false;

    public virtual void Taken(IBotWilful bot)
    {
    }

    public virtual void Paused(IBotWilful bot)
    {
    }

    public virtual void Resumed(IBotWilful bot)
    {
    }

    public virtual void Drop(IBotWilful bot)
    {
    }

    public virtual bool Hurries => true;

    public virtual string Stage => null;

    public override string ToString()
    {
        var stage = Stage;

        return stage == null ? Kind : $"{Kind}: {stage}";
    }
}
