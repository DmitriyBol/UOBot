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
    public const string Trade = "mend";

    public static double Prior { get; set; } = 30.0;

    public static double WorkMinutes { get; set; } = 1.0;

    public static int TryMs { get; set; } = 1500;

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

    public override bool Summons => true;

    public override Map Map => _map;

    public override Point3D Where => _found;

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

    public override SkillName? Trains => _trains;

    public override int Outlay => 0;

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

        if (BotMend.Whole(_patient))
        {
            return _casts + _cloths + _draughts > 0
                ? Ending("mended")
                : BotDoing.Done("there was nothing left to mend");
        }

        if (_onSelf)
        {
            var bottle = BotMend.Draught(body);

            if (bottle != null && BotMend.Swallow(body, bottle))
            {
                _draughts++;

                return BotDoing.Work("drinking");
            }
        }

        var cloth = BotMend.UnderFire(bot) || BotMend.Spell(body, _patient) < 0;
        var near = cloth ? BotMend.Touch : BotMend.Cast;

        if (!_onSelf && !body.InRange(_patient.Location, near))
        {
            return BotDoing.Walk(_map, _patient, BotArrival.Within(near), $"to {_patient.Name}");
        }

        if (_awaiting && body.Target != null)
        {
            _awaiting = false;

            if (BotMend.Aim(body, _patient))
            {
                _casts++;
            }

            return BotDoing.Work("healing");
        }

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

        return Ending("nothing left to mend with");
    }

    private BotDoing Ending(string why) =>
        _casts + _cloths + _draughts > 0
            ? BotDoing.Done($"{why} after {_casts} casts, {_cloths} bandages and {_draughts} bottles")
            : BotDoing.Failed(why);
}
