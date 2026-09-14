using Server.Mobiles;

namespace Server.BotAI.V2;

/// <summary>
/// Going to somebody's aid, or hitting back at whatever is hitting you.
///
/// <para>
/// <b>It is a hunt with a different name and a different price, and both differences are the point.</b> The
/// fighting itself is <see cref="BotSlay"/>'s — closing at the right distance for the weapon, giving ground
/// when something gets too near, the flight rule, the caps on a fight that is going nowhere, the corpse
/// afterwards. Writing a second one of those would be writing a second set of the same bugs. What this adds
/// is that the work is worth dropping other work for, and that it is filed under its own name so the ledger
/// never averages "went to help Orin" together with "went to kill a rat".
/// </para>
///
/// <para>
/// <b>Pressing, which almost nothing is.</b> The dwell exists so bots finish what they start, and it is right
/// nearly always — a vein is still there in half a minute. A bot being eaten is not: half a minute is the
/// whole of the event. This is the case the pressing flag was written for.
/// </para>
/// </summary>
public sealed class BotRescue : BotDeed
{
    public const string Trade = "rescue";

    public static double Prior { get; set; } = 400.0;

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

    public override string Kind => Trade;

    public override bool Summons => true;

    public override Map Map => _fight.Map;

    public override Point3D Where => _fight.Where;

    public override double Expects => (Failing ? Prior : Steady) * BotGuilds.Worth(_friend, _rescuer);

    private Mobile _rescuer;

    public static double Steady { get; set; } = 150.0;

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

        var doing = _fight.Advance(bot);

        if (!_thanked && !_own && doing.Kind == BotDoingKind.Done
            && _rescuer?.Guild is Guilds.Guild ours && _friend?.Guild is Guilds.Guild theirs && ours != theirs)
        {
            _thanked = true;
            BotRegard.Helped(ours.Name, theirs.Name);
        }

        return doing;
    }

    private bool _thanked;

    public override void Drop(IBotWilful bot)
    {
        _fight.Drop(bot);

        if (!_own)
        {
            BotCry.Quiet(_friend);
        }
    }
}
