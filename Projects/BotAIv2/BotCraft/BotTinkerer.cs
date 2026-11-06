using System;
using Server.Logging;

namespace Server.BotAI.V2;

public sealed class BotTinkerer : IBotProposer
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotTinkerer));

    public static int LeastMetal { get; set; } = 8;

    public static int LeastOnMarket { get; set; } = 2;

    private static bool _saidNoSystem;

    private static bool _said;

    public static long Asked { get; private set; }

    public static long NoKit { get; private set; }

    public static long NoMetal { get; private set; }

    public static long ToOrder { get; private set; }

    public static long OnSpec { get; private set; }

    public static long Stocked { get; private set; }

    public string Name => "Tinkerer";

    public BotStanding Rung => BotStanding.Free;

    public BotDeed Propose(IBotWilful bot)
    {
        var body = bot?.Self;
        var map = body?.Map;

        if (map == null || map == Map.Internal || !body.Alive)
        {
            return null;
        }

        if (BotTinkering.Kit(body) == null && BotTinkering.Seed(body))
        {
            logger.Information("{Name} was given its tinker's tools: no shop within its reach sells them, and the class is born with them now", body.Name);
        }

        if (BotTinkering.Kit(body) == null)
        {
            NoKit++;

            return null;
        }

        Asked++;

        if (BotTinkering.System == null)
        {
            if (!_saidNoSystem)
            {
                _saidNoSystem = true;

                logger.Error("The tinkering system does not exist yet, so nobody can make tools");
            }

            return null;
        }

        if (BotTinkering.Ingots(body) < LeastMetal)
        {
            BotMarket.FromBank(bot, BotTinkering.Metal, LeastMetal * 2);
        }

        if (BotTinkering.Ingots(body) < LeastMetal)
        {
            NoMetal++;

            return null;
        }

        var order = Order(bot, body);
        var tool = order?.Kind ?? BotTinkering.Choose(body, LeastOnMarket, out _);

        if (tool == null)
        {
            Stocked++;

            return null;
        }

        if (order != null)
        {
            ToOrder++;
            order.Offered();
            Once(body, order.Kind.Name, "to order");

            return new BotTinker(map, body.Location, order.Kind, order);
        }

        if (BotRoom.SpecShut(body))
        {
            return null;
        }

        OnSpec++;
        BotGlut.Spec(tool);
        Once(body, tool.Name, "because the island is short of it");

        return new BotTinker(map, body.Location, tool, null);
    }

    private static BotWant Order(IBotWilful bot, Mobile body)
    {
        var wants = BotMarket.Wants;
        BotWant best = null;
        var bestWorth = 0;

        for (var i = 0; i < wants.Count; i++)
        {
            var want = wants[i];

            if (!want.IsOpen || ReferenceEquals(want.Buyer, bot) || !BotTinkering.IsTool(want.Kind) || want.Worth <= bestWorth)
            {
                continue;
            }

            if (BotTinkering.Recipe(body, want.Kind) == null)
            {
                continue;
            }

            if (!BotCourier.Answerable(bot, want) || BotCourier.Holding(bot, want))
            {
                continue;
            }

            best = want;
            bestWorth = want.Worth;
        }

        return best;
    }

    private static void Once(Mobile body, string tool, string why)
    {
        if (_said)
        {
            return;
        }

        _said = true;

        logger.Information("{Name} is the first to tinker: {Tool}, {Why}", body.Name, tool, why);
    }

    public static string Describe() =>
        Asked == 0
            ? "nobody with tinker's tools has been asked"
            : $"{Asked} asked to tinker: {ToOrder} to order, {OnSpec} for the island's stock, {Stocked} found every tool stocked, {NoMetal} had fewer than {LeastMetal} iron in the pack (or in the box at a bank); {NoKit} asked without tinker's tools; {BotTinker.Describe()}";

    public static void Forget()
    {
        Asked = 0;
        NoKit = 0;
        NoMetal = 0;
        ToOrder = 0;
        OnSpec = 0;
        Stocked = 0;
        _said = false;
    }
}
