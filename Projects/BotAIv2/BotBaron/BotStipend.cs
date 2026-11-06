using System;
using System.Collections.Generic;
using Server.Items;
using Server.Logging;
using Server.Mobiles;

namespace Server.BotAI.V2;

public static class BotStipend
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotStipend));

    public static int Floor { get; set; } = 1000;

    public static int Float { get; set; } = 600;

    public static int EveryMs { get; set; } = 30000;

    public static long Grants { get; private set; }

    public static long Minted { get; private set; }

    public static long Drawn { get; private set; }

    private static readonly Dictionary<Serial, long> _looked = [];

    public static void Keep(BotMobile bot)
    {
        var grant = bot?.Class?.Stipend ?? 0;

        if (grant <= 0 || bot.Deleted || !bot.Alive)
        {
            return;
        }

        var pack = bot.Backpack;

        if (pack == null)
        {
            return;
        }

        var now = Core.TickCount;

        if (_looked.TryGetValue(bot.Serial, out var last) && now - last < EveryMs)
        {
            return;
        }

        _looked[bot.Serial] = now;

        Grant(bot, grant);
        Draw(bot, pack);
    }

    private static void Grant(BotMobile bot, int grant)
    {
        var balance = Banker.GetBalance(bot);

        if (balance >= Floor)
        {
            return;
        }

        var paid = grant - balance;

        if (paid <= 0 || !Banker.Deposit(bot, paid))
        {
            return;
        }

        Grants++;
        Minted += paid;

        logger.Information(
            "{Name}'s account was down to {Balance}gp and the crown made it up to {Grant}gp; {Minted}gp has been paid out in all",
            bot.Name,
            balance,
            grant,
            Minted
        );
    }

    private static void Draw(BotMobile bot, Container pack)
    {
        var purse = pack.GetAmount(typeof(Gold));

        if (purse >= Float)
        {
            return;
        }

        var wanted = Float - purse;
        var take = Math.Min(wanted, Banker.GetBalance(bot));

        if (take <= 0 || !Banker.Withdraw(bot, take))
        {
            return;
        }

        var coins = new Gold(take);

        if (!pack.TryDropItem(bot, coins, false))
        {
            coins.Delete();
            Banker.Deposit(bot, take);

            return;
        }

        Drawn += take;
    }

    public static string Describe() =>
        Grants == 0
            ? "the crown has not had to pay anybody yet"
            : $"{Grants} payments worth {Minted}gp, and {Drawn}gp drawn from his own account into his pocket";

    public static void Forget()
    {
        Grants = 0;
        Minted = 0;
        Drawn = 0;
        _looked.Clear();
    }
}
