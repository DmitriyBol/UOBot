using System;
using Server.Items;
using Server.Logging;
using Server.Mobiles;

namespace Server.BotAI.V2;

/// <summary>
/// What a bot keeps in its pocket, and what it puts away the moment it is standing somewhere it can.
///
/// <para>
/// <b>Carrying a fortune is two costs, and a bot had no way of noticing either.</b> Coin weighs — a thousand
/// pieces is twenty stones against a mage's ceiling of a hundred and twenty-seven — and coin is not bound, so
/// every piece of it drops into the corpse when something finally wins. A hunter that has been paid by forty
/// skeletons was walking around wearing its whole career.
/// </para>
///
/// <para>
/// <b>Banked as a side effect rather than as an errand, and that is deliberate.</b> Everything a bot chooses
/// is weighed in takings per minute, and moving coin from a pocket to an account produces nothing by that
/// measure — purse and account are both counted as wealth, so the trip would score zero and never be chosen,
/// however sensible it is. What it is instead is something a bot does while it happens to be at a counter,
/// which it already is several times an hour: the shops, the forge and the bank are the same few streets.
/// </para>
///
/// <para>
/// The float is what it keeps for its own errands — cloth, paper, herbs, bandages, a replacement blade — so
/// banking never leaves a bot unable to afford the work it was about to do.
/// </para>
/// </summary>
public static class BotPurse
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotPurse));

    public static int Float { get; set; } = 100;

    public static int KeepBack { get; set; } = 100;

    public static int Keeps(Mobile bot) =>
        BotFence.Is(bot)
            ? Math.Max(Float, BotFence.Most)
            : bot is BotMobile rider && BotStable.Wants(rider)
                ? Math.Max(Float, BotSteed.Price + BotStable.Reserve)
                : Float;

    public static int Reach { get; set; } = 3;

    public static long Banked { get; private set; }

    public static long Deposits { get; private set; }

    public static void Reset()
    {
        Banked = 0;
        Deposits = 0;
    }

    public static string Describe() => $"{Deposits} deposits worth {Banked}gp; {Wealthy()}; {BotYield.DescribeAside()}";

    public static string Wealthy()
    {
        var bots = BotPopulation.Bots;

        if (bots.Count == 0)
        {
            return "nobody alive to have any money";
        }

        var purses = new int[bots.Count];
        var counted = 0;

        Mobile richest = null;
        var kept = 0;
        var minded = 0;
        long pack = 0;
        long bank = 0;

        foreach (var bot in bots)
        {
            if (bot == null || bot.Deleted || !bot.Alive)
            {
                continue;
            }

            if (bot.Class is { Stipend: > 0 } or { Provisioned: true })
            {
                kept++;

                continue;
            }

            if (bot.Minded)
            {
                minded++;

                continue;
            }

            var pocket = bot.Backpack?.GetAmount(typeof(Gold)) ?? 0;
            var account = Banker.GetBalance(bot);

            pack += pocket;
            bank += account;
            if (richest == null || pocket + account > (richest.Backpack?.GetAmount(typeof(Gold)) ?? 0) + Banker.GetBalance(richest))
            {
                richest = bot;
            }

            purses[counted++] = pocket + account;
        }

        var apart = kept + minded == 0
            ? ""
            : $"; {kept} kept by the crown and {minded} that think were left out of it";

        if (counted == 0)
        {
            return $"nobody who earns their own living is alive to have any money{apart}";
        }

        Array.Sort(purses, 0, counted);

        var poorest = purses[0];
        var middle = purses[counted / 2];
        var richest2 = purses[counted - 1];

        var name = richest == null
            ? "nobody"
            : $"{richest.Name} the {(richest as BotMobile)?.Class?.Name ?? "bot"}";

        return $"{counted} purses that were earned: poorest {poorest}gp, middling {middle}gp, fattest {richest2}gp held by {name}, "
               + $"{pack + bank}gp between them with {pack}gp of it in pockets and {bank}gp in accounts{apart}";
    }

    public static int Bank(Mobile bot)
    {
        var pack = bot?.Backpack;
        var map = bot?.Map;

        if (pack == null || map == null || map == Map.Internal || !bot.Alive)
        {
            return 0;
        }

        if (bot is BotMobile { Class.Stipend: > 0 })
        {
            return 0;
        }

        var floor = Keeps(bot);
        var purse = pack.GetAmount(typeof(Gold));
        var excess = purse - floor;

        if (excess <= 0)
        {
            return 0;
        }

        var counter = BotGround.Counter(map, bot.Location);

        if (counter == Point3D.Zero || !bot.InRange(counter, Reach))
        {
            return 0;
        }

        if (!pack.ConsumeTotal(typeof(Gold), excess))
        {
            return 0;
        }

        if (!Banker.Deposit(bot, excess))
        {
            pack.DropItem(new Gold(excess));

            return 0;
        }

        Banked += excess;
        Deposits++;

        logger.Information("{Name} put {Gold}gp away and kept {Float}", bot.Name, excess, floor);

        return excess;
    }
}
