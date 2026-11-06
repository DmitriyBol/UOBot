using System;
using Server.Items;
using Server.Logging;

namespace Server.BotAI.V2;

public sealed class BotUpkeep : IBotProposer
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotUpkeep));

    public static double Worn { get; set; } = 0.34;

    public static int Reserve
    {
        get => _reserve ?? BotPurse.KeepBack;
        set => _reserve = value;
    }

    private static int? _reserve;

    public static double Prior { get; set; } = 30.0;

    public static int Guess { get; set; } = 60;

    private static bool _said;

    public static long Asked { get; private set; }

    public static long Sound { get; private set; }

    public static long Broke { get; private set; }

    public static long Standing { get; private set; }

    public static long Raised { get; private set; }

    public static long Waiting { get; private set; }

    public static long Soon { get; private set; }

    public static double Worst { get; private set; } = 1.0;

    public static string WorstKind { get; private set; }

    public static long Looked { get; private set; }

    public string Name => "Upkeep";

    public BotStanding Rung => BotStanding.Free;

    public BotDeed Propose(IBotWilful bot)
    {
        var body = bot?.Self;
        var map = body?.Map;

        if (map == null || map == Map.Internal || !body.Alive)
        {
            return null;
        }

        Asked++;

        if (!BotNeeds.Due(body, "gear"))
        {
            Soon++;

            return null;
        }

        if (Coming(bot))
        {
            Waiting++;

            return null;
        }

        var tired = Failing(body);

        if (tired == null)
        {
            Sound++;

            return null;
        }

        var kind = tired.GetType();

        if (BotMarket.Wanted(bot, kind) != null)
        {
            Standing++;

            return null;
        }

        var offer = BotMarket.Worth(kind, Guess);

        if ((body.Backpack?.TotalGold ?? 0) - offer <= Reserve)
        {
            Broke++;

            return null;
        }

        Raised++;
        Once(body, tired);

        return BotOrder.For(map, body.Location, bot, kind, offer);
    }

    private static bool Coming(IBotWilful bot)
    {
        var wants = BotMarket.Wants;

        for (var i = 0; i < wants.Count; i++)
        {
            if (ReferenceEquals(wants[i].Buyer, bot) && wants[i].Coming > 0)
            {
                return true;
            }
        }

        return false;
    }

    private static Item Failing(Mobile body)
    {
        Item worst = null;
        var least = 1.0;

        foreach (var item in body.Items)
        {
            var (now, max) = Life(item);

            if (max <= 0)
            {
                continue;
            }

            Looked++;

            var left = now / (double)max;

            if (left < Worst)
            {
                Worst = left;
                WorstKind = $"{item.GetType().Name} at {now} of {max}";
            }

            if (left > Worn || left >= least)
            {
                continue;
            }

            least = left;
            worst = item;
        }

        return worst;
    }

    private static (int Now, int Max) Life(Item item) =>
        item switch
        {
            BaseWeapon weapon => (weapon.HitPoints, weapon.MaxHitPoints),
            BaseArmor armor => (armor.HitPoints, armor.MaxHitPoints),
            _ => (0, 0)
        };

    private static void Once(Mobile body, Item tired)
    {
        if (_said)
        {
            return;
        }

        _said = true;

        var (now, max) = Life(tired);

        logger.Information(
            "{Name} is the first to put an order on the board: its {Item} is down to {Now} of {Max} and it wants another",
            body.Name,
            tired.GetType().Name,
            now,
            max
        );
    }

    public static string Describe() =>
        Asked == 0
            ? "nobody has been asked about their gear"
            : $"{Asked} asked: {Raised} ordered a replacement, {Waiting} already had one on the way, {Standing} already have one on order, "
              + $"{Broke} could not afford one, {Soon} asked again inside the minute, {Sound} are carrying nothing worn out (of {Looked} pieces read, "
              + $"the most worn was {WorstKind ?? "nothing at all"} — {(int)(Worst * 100)}% of its life left, against a bar of {(int)(Worn * 100)}%)";

    public static void Forget()
    {
        _said = false;
        Asked = 0;
        Sound = 0;
        Broke = 0;
        Standing = 0;
        Raised = 0;
        Waiting = 0;
        Soon = 0;
        Worst = 1.0;
        WorstKind = null;
        Looked = 0;
    }
}
