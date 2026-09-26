using System;
using System.Collections.Generic;
using Server.Items;
using Server.Logging;
using Server.Mobiles;

namespace Server.BotAI.V2;

/// <summary>
/// The one purse on this shard that is not earned, and the argument for allowing exactly one.
///
/// <para>
/// <b>Gold enters this world in one place and that rule is load-bearing.</b> A monster's pocket is the only
/// faucet; everything else — the market, the crafters, the levy the Architect takes — moves coin about
/// rather than making it, and that is deliberate, because the first version drained its own faucet by
/// 110,900 in a night by letting bots sell loot to shopkeepers. Anything that mints is therefore a second
/// faucet however it is dressed, and the honest thing is to say so rather than to hide it inside a trade.
/// </para>
///
/// <para>
/// <b>This one is allowed because it buys nothing that competes.</b> The Baron takes no share of what his
/// company kills — see <c>BotSpoils</c> — sells nothing, banks nothing he was given by anybody, and spends
/// it on bandages and a handful of scrolls. The coin does not enter the economy as pressure on prices; it
/// leaves it at the first shopkeeper he passes, which is a <em>sink</em>. Netted over an evening this is
/// closer to a drain than a tap. What it actually buys is the thing it was ordered to buy: a bot who can be
/// relied upon to walk into ground that has killed people without first having to be solvent.
/// </para>
///
/// <para>
/// <b>The pocket is not minted, it is drawn.</b> Only the bank is ever topped up. What he carries comes out
/// of what he already has, wherever he is standing — no counter, no errand, no trip — because a stipend that
/// needed a walk to the bank would be a wage with extra steps, and this bot is precisely the one who must
/// never be standing in a queue while a square is killing people.
/// </para>
/// </summary>
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
