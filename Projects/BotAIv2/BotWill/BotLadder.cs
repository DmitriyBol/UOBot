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
    public static double FailingFraction { get; set; } = 0.35;

    public static int HuntedMs { get; set; } = 8000;

    public static int Load(Mobile bot) => Mobile.BodyWeight + bot.TotalWeight;

    public static int Ceiling(Mobile bot)
    {
        var most = bot.MaxWeight;
        var allowance = StaminaSystem.StonesOverweightAllowance;

        return most > int.MaxValue - allowance ? int.MaxValue : Math.Max(1, most + allowance);
    }

    public static bool Overloaded(Mobile bot) => bot != null && Load(bot) > Ceiling(bot);

    public static bool Failing(Mobile bot) =>
        bot != null && bot.HitsMax > 0 && bot.Hits <= bot.HitsMax * Fraction(bot);

    public static double NoviceSkill { get; set; } = 50.0;

    public static double NoviceFraction { get; set; } = 0.5;

    public static bool Novice(Mobile bot)
    {
        if (bot is not BotMobile { Class: { } klass } body)
        {
            return false;
        }

        var judged = klass.Role == BotRole.Producer ? body.Bond?.Weapon?.Skill ?? klass.MainSkill : klass.MainSkill;

        return judged is { } main && body.Skills[main].Value < NoviceSkill;
    }

    public static double Fraction(Mobile bot) => Novice(bot) ? NoviceFraction : FailingFraction;

    public static bool Hunted(BotResolve resolve) =>
        resolve is { Struck: true } && Core.TickCount - resolve.HurtTick < HuntedMs
        || resolve is { Menaced: true } && Core.TickCount - resolve.MenaceTick < MenacedMs;

    public static int MenacedMs { get; set; } = 3000;

    public static bool Forewarned(BotResolve resolve) =>
        resolve is { Menaced: true } && Core.TickCount - resolve.MenaceTick < MenacedMs
        && !(resolve.Struck && Core.TickCount - resolve.HurtTick < HuntedMs);

    public static BotStanding Standing(IBotWilful bot)
    {
        var body = bot?.Self;
        var resolve = bot?.Resolve;

        if (body == null || resolve == null || body.Deleted || !body.Alive)
        {
            return BotStanding.Dead;
        }

        if (Failing(body))
        {
            return BotStanding.Failing;
        }

        if (Hunted(resolve))
        {
            return BotStanding.Hunted;
        }

        if (bot.Squad != null)
        {
            return BotStanding.Bound;
        }

        return resolve.Deed != null ? BotStanding.Busy : BotStanding.Free;
    }
}
