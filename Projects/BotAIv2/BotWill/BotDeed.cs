namespace Server.BotAI.V2;

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

    public virtual bool ResumesHurt => Foe == null && !Braves;

    public virtual bool Resumes => Steadfast;

    public virtual bool Summons => false;

    public virtual bool Shelters => false;

    public virtual bool Keeps => false;

    public virtual bool Braves => false;

    public virtual double Brings(Mobile body) => BotQuad.Strength(body);

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

    public virtual string Stale(IBotWilful bot) => null;

    public virtual bool KeptOut => false;

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
