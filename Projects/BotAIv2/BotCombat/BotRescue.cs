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
    /// <summary>The ledger's key.</summary>
    public const string Trade = "rescue";

    /// <summary>
    /// What going to somebody's aid is reckoned at per minute before experience corrects it.
    ///
    /// <para>
    /// Above a lone hunt, and not because the corpse pays better — it is the same corpse. What is worth more
    /// is what does not happen: a bot that dies drops everything it carried, spends three minutes dead, and
    /// walks back for its own corpse afterwards. The ledger will pull this number towards what actually
    /// arrives in the pack, which will be less; that is correct and it should still be chosen, because the
    /// thing it is really buying is not in the pack.
    /// </para>
    /// </summary>
    public static double Prior { get; set; } = 140.0;

    private readonly BotSlay _fight;

    private readonly Mobile _friend;

    private readonly BaseCreature _foe;

    private readonly bool _own;

    public BotRescue(BotSlay fight, Mobile friend, BaseCreature foe, bool own)
    {
        _fight = fight;
        _friend = friend;
        _foe = foe;
        _own = own;
    }

    /// <summary>Whether this is hitting back on one's own behalf rather than going to somebody else's aid.</summary>
    public bool Own => _own;

    public override string Kind => Trade;

    public override Map Map => _fight.Map;

    public override Point3D Where => _fight.Where;

    public override double Expects => Prior;

    public override double Minutes => _fight.Minutes;

    public override SkillName? Trains => _fight.Trains;

    public override int Outlay => 0;

    public override double Coin => _fight.Coin;

    public override int Made => _fight.Made;

    /// <summary>The whole reason this exists as its own undertaking. See the note above.</summary>
    public override bool Pressing(IBotWilful bot) => true;

    public override string Stage =>
        _own
            ? $"hitting back at {_foe?.Name ?? "it"}"
            : $"{_friend?.Name ?? "somebody"} is being set upon by {_foe?.Name ?? "something"}";

    public override bool Bend(IBotWilful bot) => _fight.Bend(bot);

    public override BotDoing Advance(IBotWilful bot)
    {
        // The one who called is safe, or beyond saving. Either way this is over, and it is not a failure:
        // the fight happened or it did not, and nothing about the ground was proved bad.
        if (!_own && _friend is not { Deleted: false, Alive: true })
        {
            return BotDoing.Done($"{_friend?.Name ?? "they"} are past helping");
        }

        return _fight.Advance(bot);
    }

    public override void Drop(IBotWilful bot)
    {
        _fight.Drop(bot);

        // Whoever was crying has had somebody come; if they are still in trouble they will say so again on
        // their own next beat. Leaving the cry standing would send a second and a third bot at a fight that
        // is already finished.
        if (!_own)
        {
            BotCry.Quiet(_friend);
        }
    }
}
