using System;
using Server.Items;
using Server.Mobiles;

namespace Server.BotAI.V2;

/// <summary>How a piece of work ended. Four endings, and each is treated differently on purpose.</summary>
public enum BotEnding
{
    Done,

    Failed,

    Dropped,

    Died
}

/// <summary>Where a bot stood when it took work on. Everything the takings are measured against.</summary>
public readonly struct BotStake
{
    public BotStake(long tick, double skill, int wealth, int made)
    {
        Tick = tick;
        Skill = skill;
        Wealth = wealth;
        Made = made;
    }

    public long Tick { get; }

    public double Skill { get; }

    public int Wealth { get; }

    public int Made { get; }
}

/// <summary>What a finished piece of work came to.</summary>
public readonly struct BotTakings
{
    public BotTakings(double minutes, double worth, double perMinute, double skill, int coin, int made)
    {
        Minutes = minutes;
        Worth = worth;
        PerMinute = perMinute;
        Skill = skill;
        Coin = coin;
        Made = made;
    }

    public double Minutes { get; }

    public double Worth { get; }

    public double PerMinute { get; }

    public double Skill { get; }

    public int Coin { get; }

    public int Made { get; }

    public override string ToString() =>
        $"{Worth:F0} in {Minutes:F1} min ({PerMinute:F0}/min): {Coin} coin, {Made} made, {Skill:F1} skill";
}

/// <summary>
/// What a piece of work was worth. One currency, and the exchange rate between the two things this
/// population is for.
///
/// <para>
/// <b>Points are given for change, never for state.</b> Having money is worth nothing; putting money in the
/// bank is worth what was put in. Having skill is worth nothing; gaining a tenth of a point is worth a
/// tenth of <see cref="GoldPerSkillPoint"/>. That is not a stylistic preference — it is the one condition
/// under which added-on rewards provably cannot change what the best behaviour is (Ng, Harada and Russell,
/// 1999: a shaping term has to be a difference of a potential), and the classic result of ignoring it is an
/// agent that discovers standing still in the right place scores well.
/// </para>
///
/// <para>
/// <b>Death has to be made expensive here, because the rest of this project made it cheap.</b> A bot's kit
/// is bound: it survives death, it is restored on resurrection, and it is not merchandise. That is right
/// for the kit and it leaves dying almost free — so if takings were skill and coin alone, the best way for
/// a young fighter to gain skill would be to attack something far too strong, over and over, dying every
/// time. So dying costs <see cref="DeathMinutes"/> of the divisor and marks the place, which is the honest
/// version of the same fact: what death actually costs a bot is the walk back.
/// </para>
/// </summary>
public static class BotYield
{
    public static double GoldPerSkillPoint { get; set; } = 500.0;

    public static double DeathMinutes { get; set; } = 3.0;

    public static double StrayFactor { get; set; } = 0.3;

    public static double LeastMinutes { get; set; } = 0.25;

    public static double MostPerMinute { get; set; } = 2000.0;

    public static int Wealth(Mobile bot)
    {
        if (bot == null)
        {
            return 0;
        }

        var purse = bot.Backpack?.GetAmount(typeof(Gold)) ?? 0;

        return purse + Banker.GetBalance(bot);
    }

    public static int Standing(Mobile bot) => bot == null ? 0 : Wealth(bot) + BotAuction.Escrowed(bot);

    public static double SkillOf(Mobile bot, SkillName? which) =>
        bot == null || which == null ? 0.0 : bot.Skills[which.Value].Base;

    public static BotStake Take(IBotWilful bot, BotDeed deed)
    {
        var body = bot?.Self;

        return new BotStake(
            Core.TickCount,
            SkillOf(body, deed?.Trains),
            Standing(body),
            deed?.Made ?? 0
        );
    }

    public static BotTakings Settle(IBotWilful bot, BotDeed deed, BotStake stake, BotEnding ending)
    {
        var body = bot?.Self;

        var minutes = (Core.TickCount - stake.Tick) / 60000.0;

        if (ending == BotEnding.Died)
        {
            minutes += DeathMinutes;
        }

        if (minutes < LeastMinutes)
        {
            minutes = LeastMinutes;
        }

        var coin = body == null ? 0 : Standing(body) - stake.Wealth;
        var made = Math.Max(0, (deed?.Made ?? 0) - stake.Made);

        var skill = 0.0;

        if (ending == BotEnding.Done)
        {
            skill = SkillOf(body, deed?.Trains) - stake.Skill;

            if (skill < 0.0)
            {
                skill = 0.0;
            }

            if (skill > 0.0 && deed?.Trains != null && bot?.Class?.Wants(deed.Trains.Value) == false)
            {
                skill *= StrayFactor;
            }
        }

        var worth = coin + made + skill * GoldPerSkillPoint;
        var perMinute = Math.Clamp(worth / minutes, -MostPerMinute, MostPerMinute);

        return new BotTakings(minutes, worth, perMinute, skill, coin, made);
    }
}
