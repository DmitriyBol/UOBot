using System;
using System.Collections.Generic;
using Server.Items;
using Server.Logging;

namespace Server.BotAI.V2;

/// <summary>
/// A fighter with money hiring a healer to stand by it, and the wage that changes hands.
///
/// <para>
/// <b>Patrick's order of 15.09.2026: the well-off fighters take healers into their service.</b> Standing by a
/// fighter was unpaid work until now — a healer went to whoever was already fighting within sixty tiles, for
/// nothing but the skill of the wounds it bound — and unpaid work is refused by nobody and learned by nobody. A hire
/// is the same stint with coin in it: the fighter pays a wage out of its own pack for every minute the healer stands
/// by, the healer's ledger learns what escorting is worth, and the fighter walks into its next fight with somebody
/// behind it.
/// </para>
///
/// <para>
/// <b>Who hires is read off the fighter, not decided by it.</b> There is no errand on the fighter's side and no
/// negotiation: a fighter that is out — hunting, prowling, in a company or already in a fight — and can afford it
/// is hiring, and the nearest free healer that reads that fact takes the stint. The fighter never stops to think
/// about it, because the fighter's decisions are about fights, and this is the healer's decision about where to be.
/// </para>
///
/// <para>
/// <b>Paid out of the pack, a minute at a time, and booked aside for the fighter.</b> The engine pays from the
/// pack and so does this; a fighter whose pack runs dry ends the stint rather than running a debt. The wage is
/// booked through <see cref="BotYield.Aside"/> so the fighter's own work — the hunt it was on — is not taught that
/// hunting costs a healer's wage; the healer's side is the escort's own takings, which is the whole point.
/// </para>
/// </summary>
public static class BotRetainer
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotRetainer));

    public static bool Running { get; set; } = true;

    public static int Affluent { get; set; } = 300;

    public static int Wage { get; set; } = 5;

    public static int Minutes { get; set; } = 3;

    public static int Reach { get; set; } = 100;

    public static int PayEveryMs { get; set; } = 60000;

    public static int GraceMs { get; set; } = 60000;

    public static int Near { get; set; } = 15;

    public static long Unserved { get; set; }

    public static long Hired { get; private set; }

    public static long Paid { get; private set; }

    public static long Payments { get; private set; }

    public static long Broke { get; private set; }

    public static long Looked { get; private set; }

    public static long Idle { get; private set; }

    public static long Poor { get; private set; }

    private static readonly HashSet<string> _afield = new(StringComparer.OrdinalIgnoreCase)
    {
        BotSlay.Trade, BotProwl.Trade, BotBand.Trade, BotRescue.Trade, BotHarrow.Trade, BotSweep.Trade,
        BotDelve.Trade, BotRally.Trade, BotQuarrel.Trade, BotEnlist.Trade, BotFreedom.Trade, BotScout.Trade
    };

    public static bool Afield(BotMobile fighter) =>
        fighter is { Deleted: false, Alive: true }
        && (fighter.Squad != null || BotAccompany.Engaged(fighter) || _afield.Contains(fighter.Resolve?.Deed?.Kind ?? ""));

    public static bool Joinable(BotMobile fighter) =>
        Afield(fighter) && !string.Equals(fighter.Resolve?.Deed?.Kind, BotProwl.Trade, StringComparison.OrdinalIgnoreCase);

    public static int Hiring(BotMobile fighter)
    {
        if (!Running || fighter is not { Deleted: false, Alive: true, Fallen: false } || fighter.Class is not { } klass)
        {
            return 0;
        }

        if (klass.Role is BotRole.Medic or BotRole.Producer || klass.Unpaid)
        {
            return 0;
        }

        Looked++;

        if (!Joinable(fighter))
        {
            Idle++;

            return 0;
        }

        var pocket = fighter.Backpack?.GetAmount(typeof(Gold)) ?? 0;

        if (pocket < Wage * Minutes || BotYield.Wealth(fighter) < Affluent)
        {
            Poor++;

            return 0;
        }

        return Wage;
    }

    public static bool Pay(Mobile fighter, Mobile healer, int wage)
    {
        var pack = fighter?.Backpack;
        var purse = healer?.Backpack;

        if (pack == null || purse == null || wage <= 0)
        {
            return false;
        }

        if (pack.GetAmount(typeof(Gold)) < wage || !pack.ConsumeTotal(typeof(Gold), wage))
        {
            Broke++;

            return false;
        }

        purse.DropItem(new Gold(wage));

        BotYield.Aside(fighter, wage);

        Paid += wage;
        Payments++;

        return true;
    }

    public static void Began(Mobile healer, Mobile fighter, int wage)
    {
        Hired++;

        logger.Information(
            "{Healer} is hired by {Fighter} at {Wage}gp a minute, out of a pack holding {Pocket}gp",
            healer?.Name,
            fighter?.Name,
            wage,
            fighter?.Backpack?.GetAmount(typeof(Gold)) ?? 0
        );
    }

    public static string Describe() =>
        !Running
            ? "no fighter hires a healer"
            : $"{Hired} healers hired at {Wage}gp a minute by fighters worth {Affluent}gp or more, {Paid}gp paid in {Payments} payments, "
              + $"{Broke} stints ended for want of pay, {Unserved} minutes unpaid for the healer being further than {Near} tiles off; of {Looked} looks at a fighter {Idle} found it not out and {Poor} found it unable to pay";

    public static void Forget()
    {
        Hired = 0;
        Paid = 0;
        Payments = 0;
        Broke = 0;
        Looked = 0;
        Idle = 0;
        Poor = 0;
        Unserved = 0;
    }
}
