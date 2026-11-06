using Server.Mobiles;

namespace Server.BotAI.V2;

public sealed class BotRescue : BotDeed
{

    public const string Trade = "rescue";

    public static double Prior { get; set; } = 200.0;

    private readonly BotSlay _fight;

    private readonly Mobile _friend;

    private readonly BaseCreature _foe;

    private readonly bool _own;

    public BotRescue(BotSlay fight, Mobile friend, BaseCreature foe, bool own, Mobile rescuer = null)
    {
        _fight = fight;
        _friend = friend;
        _foe = foe;
        _own = own;

        _rescuer = rescuer ?? (own ? friend : null);
    }

    public bool Own => _own;

    public Mobile Friend => _friend;

    public bool ToTheEnd => _fight.ToTheEnd;

    public override Mobile Foe => _foe;

    public override bool Resumes => true;

    public override string Kind => Trade;

    public override bool Summons => true;

    public override Map Map => _fight.Map;

    public override Point3D Where => _fight.Where;

    public override double Expects => (Failing ? Prior : Steady) * BotGuilds.Worth(_friend, _rescuer);

    private Mobile _rescuer;

    public static double Steady { get; set; } = 100.0;

    private bool Failing =>
        _friend is { Deleted: false, Alive: true, HitsMax: > 0 } friend
        && friend.Hits < friend.HitsMax * BotSlay.FleeAt;

    public override double Minutes => _fight.Minutes;

    public override SkillName? Trains => _fight.Trains;

    public override int Outlay => 0;

    public override double Coin => _fight.Coin;

    public override int Made => _fight.Made;

    public override bool Pressing(IBotWilful bot) => Failing;

    public override string Stage =>
        _own
            ? $"hitting back at {_foe?.Name ?? "it"}"
            : $"{_friend?.Name ?? "somebody"} is being set upon by {_foe?.Name ?? "something"}";

    public override bool Bend(IBotWilful bot) => _fight.Bend(bot);

    public override BotDoing Advance(IBotWilful bot)
    {
        _rescuer ??= bot?.Self;

        if (!_own && _friend is not { Deleted: false, Alive: true })
        {
            return BotDoing.Done($"{_friend?.Name ?? "they"} are past helping");
        }

        if (!_own && _friend.Hits < _lowest)
        {
            _lowest = _friend.Hits;
        }

        var doing = _fight.Advance(bot);

        if (!_told && !_own && doing.Kind == BotDoingKind.Done && _friend is { Deleted: false, Alive: true })
        {
            _told = true;
            BotBiography.Rescued(_rescuer, _friend, _foe, _lowest == int.MaxValue ? _friend.Hits : _lowest, _friend.HitsMax);
        }

        if (!_thanked && !_own && doing.Kind == BotDoingKind.Done
            && _rescuer?.Guild is Guilds.Guild ours && _friend?.Guild is Guilds.Guild theirs && ours != theirs)
        {
            _thanked = true;
            BotRegard.Helped(ours.Name, theirs.Name);
        }

        return doing;
    }

    private bool _thanked;

    private bool _told;

    private int _lowest = int.MaxValue;

    public override void Taken(IBotWilful bot)
    {
        if (_own && ToTheEnd)
        {
            BotDefender.Stood(bot?.Self, _foe);
        }
    }

    public override void Drop(IBotWilful bot)
    {
        _fight.Drop(bot);

        if (!_own)
        {
            BotCry.Quiet(_friend);
        }
    }
}
