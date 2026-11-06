using System;
using Server.Items;

namespace Server.BotAI.V2;

public sealed class BotDriver
{
    public BotDriver(BotStandIn me, long now)
    {
        Me = me;
        _castTick = now - BotStrike.CastMs;
        _stepTick = now - BotProving.StepMs;
    }

    public BotStandIn Me { get; }

    public int Casts { get; private set; }

    public int Heals { get; private set; }

    public int Bandages { get; private set; }

    public int Bottles { get; private set; }

    public int Steps { get; private set; }

    private bool _aiming;

    private bool _healing;

    private long _castTick;

    private long _stepTick;

    public void Answer(Mobile foe, long now)
    {
        var me = Me;

        if (me is not { Deleted: false, Alive: true } || foe is not { Deleted: false, Alive: true })
        {
            return;
        }

        if (_aiming && me.Target != null)
        {
            _aiming = false;

            if (_healing ? BotMend.Aim(me, me) : BotStrike.Aim(me, foe))
            {
                if (_healing)
                {
                    Heals++;
                }
                else
                {
                    Casts++;
                }
            }

            return;
        }

        var bottle = BotMend.Draught(me);

        if (bottle != null && BotMend.Swallow(me, bottle))
        {
            Bottles++;
        }

        var hurt = BotMend.Share(me) < BotMend.Hurt || me.Poisoned;

        if (hurt && me.Spell == null && now - _castTick >= BotStrike.CastMs)
        {
            var mend = BotMend.Spell(me, me);

            if (mend >= 0 && BotMend.Begin(me, mend))
            {
                _aiming = true;
                _healing = true;
                _castTick = now;

                return;
            }
        }

        if (hurt && !BotMend.Winding(me) && BotMend.Cloth(me) > 0 && BotMend.Wind(me, me))
        {
            Bandages++;
        }

        var near = me.InRange(foe.Location, BotSlay.TooClose);
        var dry = me.Weapon is BaseRanged { AmmoType: { } ammo } && (me.Backpack?.GetAmount(ammo) ?? 0) <= 0;

        if (me.Closes || dry)
        {
            me.Draw(melee: dry || near);
        }
        else if (me.Shoots && me.Weapon is not BaseRanged)
        {

            me.Draw(melee: false);
        }

        me.Warmode = true;
        me.Combatant = foe;

        var casting = BotStrike.Can(me);
        var armed = casting && BotStrike.Best(me) >= 0;
        var reach = Math.Max(me.Weapon?.MaxRange ?? 1, armed ? BotStrike.Range : 0);

        if (!me.InRange(foe.Location, reach) || !me.InLOS(foe))
        {

            if (now - _stepTick >= BotProving.StepMs)
            {
                _stepTick = now;

                var direction = me.GetDirectionTo(foe, true);
                var before = me.Location;

                me.Direction = direction;

                if (me.Move(direction) && me.Location != before)
                {
                    Steps++;
                }
            }

            return;
        }

        if (!casting || me.Spell != null || now - _castTick < BotStrike.CastMs)
        {
            return;
        }

        var spell = BotStrike.Best(me);

        if (spell >= 0 && BotStrike.Begin(me, spell))
        {
            _aiming = true;
            _healing = false;
            _castTick = now;
        }
    }
}
