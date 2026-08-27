using System;
using Server.Misc;

namespace Server.BotAI.V2;

/// <summary>
/// Which rung the bot is on, from facts only. No opinions, no weighing, no memory.
///
/// <para>
/// <b>Facts the ladder can actually produce, and nothing else.</b> Every rung below corresponds to a
/// question the engine can answer right now — is it alive, will it move, how much health is left, is it in
/// a squad. The rungs the first version had that could not be answered that way were the ones that
/// misfired: its "danger" rung was a utility score over things it could see, so a caster striking from
/// eight tiles never triggered it.
/// </para>
/// </summary>
public static class BotLadder
{
    /// <summary>
    /// The share of maximum health below which a bot is running out. A third: past it, a second bad exchange
    /// is fatal.
    /// </summary>
    public static double FailingFraction { get; set; } = 0.35;

    /// <summary>
    /// How long after being hit a bot still counts as under attack.
    ///
    /// Eight seconds, and it is a window rather than a state because being attacked has no end event. The
    /// thing that was hitting a bot does not announce that it has stopped; it wanders off, or dies to
    /// somebody else, or loses interest.
    /// </summary>
    public static int HuntedMs { get; set; } = 8000;

    /// <summary>
    /// Everything the engine weighs.
    ///
    /// <see cref="Mobile.BodyWeight"/> is part of it: the movement handler adds it before comparing, so a
    /// check that leaves it out passes a bot the engine has already decided is overloaded. Both of the first
    /// version's guards left it out.
    /// </summary>
    public static int Load(Mobile bot) => Mobile.BodyWeight + bot.TotalWeight;

    /// <summary>
    /// The weight past which every step costs stamina. The allowance is the engine's own slack and is read
    /// from it rather than assumed, because it is a server setting.
    /// </summary>
    /// <para>
    /// The guard against overflow is not decoration. <see cref="Mobile.MaxWeight"/> is
    /// <see cref="int.MaxValue"/> on the base class — only <c>PlayerMobile</c> narrows it to
    /// <c>40 + 3.5 × Str</c> — so adding the allowance to it wraps negative, and a negative ceiling makes
    /// every bot read as permanently full. That is a silent behavioural failure of the exact kind this
    /// project keeps paying for: a miner would set out, decide instantly that it was carrying too much, and
    /// bank nothing, all night, without one line in the log looking wrong.
    /// </para>
    public static int Ceiling(Mobile bot)
    {
        var most = bot.MaxWeight;
        var allowance = StaminaSystem.StonesOverweightAllowance;

        return most > int.MaxValue - allowance ? int.MaxValue : Math.Max(1, most + allowance);
    }

    /// <summary>Whether the engine is already charging this bot to walk.</summary>
    public static bool Overloaded(Mobile bot) => bot != null && Load(bot) > Ceiling(bot);

    /// <summary>Whether health has run down far enough to be the only thing that matters.</summary>
    public static bool Failing(Mobile bot) =>
        bot != null && bot.HitsMax > 0 && bot.Hits <= bot.HitsMax * FailingFraction;

    /// <summary>
    /// Whether something has hit this bot recently enough for it to still be a fight.
    ///
    /// The "has it ever been hit" half is a flag rather than a look at the stamp, because on some hosts a
    /// tick count is the physical machine's uptime counter passed straight through: it starts enormous and
    /// can wrap negative, so zero is a real reading and not a way of saying "never".
    /// </summary>
    public static bool Hunted(BotResolve resolve) =>
        resolve is { Struck: true } && Core.TickCount - resolve.HurtTick < HuntedMs;

    /// <summary>
    /// The rung, worst first. Read once per decision.
    /// </summary>
    public static BotStanding Standing(IBotWilful bot)
    {
        var body = bot?.Self;
        var resolve = bot?.Resolve;

        if (body == null || resolve == null || body.Deleted || !body.Alive)
        {
            return BotStanding.Dead;
        }

        // Being overloaded is deliberately not a rung. See BotStanding: the cure is to take the load
        // somewhere, which is the next stage of whatever the bot is already doing, so a rung that suspended
        // the work would suspend the cure. It stays readable as a fact for whoever wants to offer work
        // about it.
        if (Failing(body))
        {
            return BotStanding.Failing;
        }

        if (Hunted(resolve))
        {
            return BotStanding.Hunted;
        }

        // A squad member's place is the squad's business: it rebases the bottom of the journey every beat, so
        // a private errand somewhere else would be overwritten within the second. Joining will itself be an
        // undertaking once somebody proposes one — nothing calls BotSquads.Form yet, and that is a decision
        // rather than an omission.
        if (bot.Squad != null)
        {
            return BotStanding.Bound;
        }

        return resolve.Deed != null ? BotStanding.Busy : BotStanding.Free;
    }
}
