using System;
using Server.Items;
using Server.Mobiles;

namespace Server.BotAI.V2;

/// <summary>How a piece of work ended. Four endings, and each is treated differently on purpose.</summary>
public enum BotEnding
{
    /// <summary>Finished, by its own definition of finished. The only ending that credits skill.</summary>
    Done,

    /// <summary>It will not happen — nowhere to walk, nothing there, the order withdrawn. The place is
    /// treated with suspicion afterwards.</summary>
    Failed,

    /// <summary>Something better came along, or it was set aside so long its reason went stale. No blame
    /// attaches to the place: the takings are recorded, and that is all.</summary>
    Dropped,

    /// <summary>The bot died doing it. Failure, plus the cost of the walk back.</summary>
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

    /// <summary>The one skill this work claims to train, at the moment it started. Zero when it trains none.</summary>
    public double Skill { get; }

    /// <summary>Purse and account together.</summary>
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

    /// <summary>Everything it came to, in gold-equivalent.</summary>
    public double Worth { get; }

    /// <summary>The figure the ledger keeps: gold-equivalent per minute of the bot's life.</summary>
    public double PerMinute { get; }

    /// <summary>Points of the trained skill gained.</summary>
    public double Skill { get; }

    /// <summary>Change in purse and account. Negative when the work cost more than it brought in.</summary>
    public int Coin { get; }

    /// <summary>Declared value of goods produced.</summary>
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
    /// <summary>
    /// What one full point of skill is worth in gold.
    ///
    /// <para>
    /// <b>The most consequential number in this folder.</b> It is the exchange rate between the two things
    /// the population is for — getting better and getting paid — and every comparison between a smith's
    /// afternoon and a miner's passes through it. Five hundred means a tenth of a point, which is what a
    /// successful check gains, is worth fifty gold: a little more than an ore trip, a little less than a
    /// good corpse. It wants retuning once the shard's money supply is not running at a loss, and it should
    /// be retuned by watching what the population does, not by argument.
    /// </para>
    /// </summary>
    public static double GoldPerSkillPoint { get; set; } = 500.0;

    /// <summary>What dying costs, expressed as minutes of the bot's life. Roughly the walk back.</summary>
    public static double DeathMinutes { get; set; } = 3.0;

    /// <summary>
    /// What a point of skill is worth when it is <b>not</b> one this bot's class is for.
    ///
    /// <para>
    /// Three tenths. Without it the two things this project measures quietly become one: a warrior who spends
    /// the night mining gains Mining at the same rate a gatherer does, earns the same takings for it, and the
    /// ledger reports a thriving bot whose own trade has not moved a point. The dashboard's vector column
    /// would say one thing and the brain's arithmetic another.
    /// </para>
    ///
    /// <para>
    /// Not zero, and that matters: a warrior who learns to mine <em>has</em> learned something, and work that
    /// pays is still work. It simply loses to work that also makes the bot what it is trying to become.
    /// </para>
    /// </summary>
    public static double StrayFactor { get; set; } = 0.3;

    /// <summary>
    /// The shortest a piece of work is allowed to have taken, in minutes.
    ///
    /// A floor rather than an accuracy measure: dividing by a few milliseconds turns any trivial success
    /// into an enormous rate, and a proposer that declares its work finished the instant it starts would
    /// otherwise be the most attractive thing on the shard.
    /// </summary>
    public static double LeastMinutes { get; set; } = 0.25;

    /// <summary>
    /// The most, and the least, one settlement may claim per minute. The second guard on the same hole: no
    /// single freak measurement gets to dominate a row of the ledger, and smoothing handles the rest.
    /// </summary>
    public static double MostPerMinute { get; set; } = 2000.0;

    /// <summary>
    /// Everything the bot could pay with: coin in the pack and the balance behind it.
    ///
    /// Both halves, which the first version got wrong in the deciding half only. It weighed every want
    /// against the coin in the pack alone, so a bot with four thousand banked and a hundred in its pocket
    /// judged itself too poor for a chest plate and went back to the graveyard — while the shopkeeper, all
    /// along, would have taken the difference out of its account.
    /// </summary>
    public static int Wealth(Mobile bot)
    {
        if (bot == null)
        {
            return 0;
        }

        var purse = bot.Backpack?.GetAmount(typeof(Gold)) ?? 0;

        return purse + Banker.GetBalance(bot);
    }

    /// <summary>The named skill, or zero when the work names none. Never a total: see <see cref="BotDeed.Trains"/>.</summary>
    public static double SkillOf(Mobile bot, SkillName? which) =>
        bot == null || which == null ? 0.0 : bot.Skills[which.Value].Base;

    /// <summary>Where the bot stands now. Taken once, when work is taken on.</summary>
    public static BotStake Take(IBotWilful bot, BotDeed deed)
    {
        var body = bot?.Self;

        return new BotStake(
            Core.TickCount,
            SkillOf(body, deed?.Trains),
            Wealth(body),
            deed?.Made ?? 0
        );
    }

    /// <summary>
    /// What the work came to, and this is the whole of the measure.
    ///
    /// <para>
    /// Skill counts only when the work finished, which is the guard against the metric's most obvious
    /// exploit. Reward the rate of skill gain by itself and the strongest available behaviour is the
    /// cheapest twitch that gains skill — a training dummy, a spell cast at nothing, two bots sparring in a
    /// field until the shard restarts. Requiring the work to have finished means the gain has to arrive
    /// beside ore, a corpse or a delivery.
    /// </para>
    /// </summary>
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

        var coin = body == null ? 0 : Wealth(body) - stake.Wealth;
        var made = Math.Max(0, (deed?.Made ?? 0) - stake.Made);

        var skill = 0.0;

        if (ending == BotEnding.Done)
        {
            skill = SkillOf(body, deed?.Trains) - stake.Skill;

            // A loss is neither a reward nor a punishment. Some shards take skill on death, and a negative
            // gain here would be a bot punishing itself for the one thing it is meant to be doing.
            if (skill < 0.0)
            {
                skill = 0.0;
            }

            // Off its own trade, the same tenth of a point is worth less. See StrayFactor.
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
