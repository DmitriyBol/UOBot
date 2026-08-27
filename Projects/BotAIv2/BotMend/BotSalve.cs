using System;
using Server.Mobiles;

namespace Server.BotAI.V2;

/// <summary>
/// Patching somebody up — itself or somebody else, by spell if it can and by cloth if it cannot.
///
/// <para>
/// <b>This is the undertaking the ladder has been missing since it was written.</b> The rung for a bot that is
/// losing has always been there and has always had nothing on it, so the brain's answer to failing health was
/// to hold on to whatever the bot was already doing. That was survivable while nothing fought; the moment
/// hunting existed it meant a bot at two hit points going back to a skeleton, which is the first version's
/// worst night in one sentence.
/// </para>
///
/// <para>
/// <b>The same undertaking serves both, and the rung is what makes them different.</b> Mending itself is
/// offered above everything — a bot on the floor does not weigh options. Mending somebody else is offered as
/// ordinary work, competing on the same arithmetic as digging: it pays in real Healing and real Magery, which
/// at five hundred a point is a living, and it pays nothing at all in coin. That ordering is not tidiness. The
/// first version put "shout for help" <em>above</em> "I am dying", so a bot on its last few points announced a
/// company it could not join, found nobody able, and posted it again — dozens of times in a row. Looking after
/// somebody else must never outrank looking after yourself.
/// </para>
///
/// <para>
/// <b>Nothing here is a state that can wait indefinitely.</b> Out of mana, out of cloth, patient healed,
/// patient dead, patient walked away — every one of them ends the undertaking on the same beat it becomes true.
/// A healer standing over a corpse with no bandages is the shape of bug this project keeps finding.
/// </para>
/// </summary>
public sealed class BotSalve : BotDeed
{
    /// <summary>The ledger's key. One kind of work whoever the patient is.</summary>
    public const string Trade = "mend";

    /// <summary>
    /// What mending is reckoned at per minute before experience corrects it.
    ///
    /// <para>
    /// It produces nothing and earns nothing, so everything it is worth is skill — and that is real: a heal
    /// cast trains Magery and a bandage trains Healing, both by the engine's own check. Thirty puts it above
    /// an errand to the shops and below every trade, which is the right place for looking after each other on
    /// a shard where nobody has yet been paid to do it.
    /// </para>
    /// </summary>
    public static double Prior { get; set; } = 30.0;

    /// <summary>How long a patch-up is expected to take.</summary>
    public static double WorkMinutes { get; set; } = 1.0;

    /// <summary>How often another attempt is made once one has been begun.</summary>
    public static int TryMs { get; set; } = 1500;

    /// <summary>
    /// How much more urgent mending is at death's door than at the threshold.
    ///
    /// <para>
    /// <b>Without this there was a band where nobody healed at all.</b> A bot drops onto <c>Failing</c> at
    /// thirty-five per cent and looks after itself; above seventy it does not want mending; and in between the
    /// estimate was a flat thirty a minute, which loses to a mining trip at forty-five. So a bot at forty per
    /// cent went and dug ore.
    /// </para>
    ///
    /// <para>
    /// Three, meaning a bot on its last legs reckons mending at four times what a barely-scratched one does —
    /// which beats every trade on the shard, and should. It is the same trick used everywhere here rather than a
    /// new mechanism: the number is made to reflect the fact instead of a rung being added to carry it.
    /// </para>
    /// </summary>
    public static double Urgency { get; set; } = 3.0;

    private readonly Mobile _patient;

    private readonly Map _map;

    private readonly Point3D _found;

    private readonly bool _onSelf;

    private readonly SkillName _trains;

    private int _casts;

    private int _cloths;

    private int _draughts;

    private bool _tried;

    private bool _awaiting;

    private long _triedTick;

    public BotSalve(Mobile patient, Map map, bool onSelf, SkillName trains)
    {
        _patient = patient;
        _map = map;
        _found = patient?.Location ?? Point3D.Zero;
        _onSelf = onSelf;
        _trains = trains;
    }

    public override string Kind => Trade;

    public override Map Map => _map;

    public override Point3D Where => _found;

    /// <summary>
    /// Worth what the wound is worth: the same prior, multiplied by how far past caring the patient is.
    ///
    /// Live rather than fixed at proposal, and that is right — a patient that got worse while the healer walked
    /// is a more urgent job than the one it set out on.
    /// </summary>
    public override double Expects
    {
        get
        {
            var share = BotMend.Share(_patient);
            var past = Math.Clamp((BotMend.Hurt - share) / Math.Max(0.01, BotMend.Hurt), 0.0, 1.0);

            return Prior * (1.0 + past * Urgency);
        }
    }

    public override double Minutes => WorkMinutes;

    /// <summary>
    /// Magery for a caster, Healing for everybody else, decided by the proposer from what this bot can do.
    ///
    /// Named rather than inferred, like every other undertaking's skill — and it is the entire payment for this
    /// one, which makes getting it right worth a line.
    /// </summary>
    public override SkillName? Trains => _trains;

    public override int Outlay => 0;

    /// <summary>Not a coin either way. Bandages were paid for at a counter long before this.</summary>
    public override double Coin => 0.0;

    public override int Made => 0;

    public override string Stage
    {
        get
        {
            var who = _onSelf ? "itself" : _patient?.Name ?? "somebody";

            return $"mending {who} ({_casts} casts, {_cloths} bandages, {_draughts} bottles)";
        }
    }

    public override BotDoing Advance(IBotWilful bot)
    {
        var body = bot?.Self;

        if (body == null || _map == null)
        {
            return BotDoing.Failed("no body");
        }

        if (_patient == null || _patient.Deleted || !_patient.Alive || _patient.Map != _map)
        {
            return Ending("the patient is past mending");
        }

        // <b>Arriving to find nobody hurt is not a failure, and calling it one had a price.</b> Ending()
        // reports Failed whenever nothing was administered — right for a patient who died on the way, wrong
        // for one who got better — and a failure writes caution against the ground under the trade's name.
        // So a healer that walked to somebody who recovered, or whom a second healer reached first, was
        // taught to avoid that spot. The sentence was worse than the ending: "failed at mend — mended" is two
        // words contradicting each other in six characters.
        //
        // Finished, and it took nothing, which the ledger reads as a trade that paid nothing here. That is
        // the honest signal and it needs no caution to carry it.
        if (BotMend.Whole(_patient))
        {
            return _casts + _cloths + _draughts > 0
                ? Ending("mended")
                : BotDoing.Done("there was nothing left to mend");
        }

        // A bottle first when it is nearly over, and only then: it is the one mending that works while
        // something is hitting you, and there are two of them.
        if (_onSelf)
        {
            var bottle = BotMend.Draught(body);

            if (bottle != null && BotMend.Swallow(body, bottle))
            {
                _draughts++;

                return BotDoing.Work("drinking");
            }
        }

        // <b>Which means decides how close to stand, and standing too close is what stops the means working.</b>
        // A heal reaches eight tiles; cloth reaches one. Walking to the cloth distance to cast put the healer
        // inside melee range of whatever was hitting the patient, and a caster that is being hit cannot cast.
        var cloth = BotMend.UnderFire(bot) || BotMend.Spell(body, _patient) < 0;
        var near = cloth ? BotMend.Touch : BotMend.Cast;

        if (!_onSelf && !body.InRange(_patient.Location, near))
        {
            // Following the patient rather than the place it was standing: hurt things move, usually away
            // from whatever hurt them.
            return BotDoing.Walk(_map, _patient, BotArrival.Within(near), $"to {_patient.Name}");
        }

        // A cast of <em>ours</em> that has come round to its target. This is the click a bot has no client to
        // make.
        //
        // <b>Guarded by having started one.</b> A cursor on a bot is not necessarily this undertaking's: mining
        // puts one up to point at rock, and pointing a harvest target at a wounded friend would be this file
        // reaching into somebody else's work through a field they happen to share.
        if (_awaiting && body.Target != null)
        {
            _awaiting = false;

            if (BotMend.Aim(body, _patient))
            {
                _casts++;
            }

            return BotDoing.Work("healing");
        }

        // Mid-cast. The engine is holding the delay and movement already knows to stand still for it.
        if (body.Spell != null)
        {
            return BotDoing.Work("casting");
        }

        if (_tried && Core.TickCount - _triedTick < TryMs)
        {
            return BotDoing.Work("healing");
        }

        _tried = true;
        _triedTick = Core.TickCount;

        // <b>Cloth under fire, spell out of it.</b> Not a preference: a blow destroys a cast outright and only
        // makes a bandage slip. Out of a fight the order is the other way round for three reasons of its own —
        // two seconds against ten, mana against money, and herbs a caster walks to town for anyway.
        if (BotMend.Winding(body))
        {
            return BotDoing.Work("bandaging");
        }

        if (cloth)
        {
            if (BotMend.Wind(body, _patient))
            {
                _cloths++;

                return BotDoing.Work("bandaging");
            }

            // Out of cloth. A cast under fire is mostly wasted, but wasted beats nothing at all.
            var last = BotMend.Spell(body, _patient);

            if (last >= 0 && BotMend.Begin(body, last))
            {
                _awaiting = true;

                return BotDoing.Work("casting");
            }
        }
        else
        {
            var spell = BotMend.Spell(body, _patient);

            if (spell >= 0 && BotMend.Begin(body, spell))
            {
                _awaiting = true;

                return BotDoing.Work("casting");
            }

            if (BotMend.Wind(body, _patient))
            {
                _cloths++;

                return BotDoing.Work("bandaging");
            }
        }

        // No mana, no herbs, no cloth. Ending rather than waiting: what is missing is bought at a counter, and
        // the errand that does that is somebody else's to offer.
        return Ending("nothing left to mend with");
    }

    /// <summary>
    /// Over, and never as a failure when something was actually done.
    ///
    /// <b>A failure marks the place with caution</b>, and the place a bot mends itself is wherever it was
    /// standing when it got hurt — usually its own work. Teaching the ledger that the mine is dangerous because
    /// a bot bandaged itself at the mouth of it would be the mining trip paying for the fight it survived.
    /// </summary>
    private BotDoing Ending(string why) =>
        _casts + _cloths + _draughts > 0
            ? BotDoing.Done($"{why} after {_casts} casts, {_cloths} bandages and {_draughts} bottles")
            : BotDoing.Failed(why);
}
