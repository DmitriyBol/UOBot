using System;
using Server.Items;
using Server.Logging;

namespace Server.BotAI.V2;

public sealed class BotBullion : IBotProposer
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotBullion));

    public static int Reserve
    {
        get => _reserve ?? BotPurse.KeepBack;
        set => _reserve = value;
    }

    private static int? _reserve;

    public static int Enough { get; set; } = 20;

    public static int Batch { get; set; } = 20;

    public static int Least { get; set; } = 6;

    public static int Guess { get; set; } = 8;

    public static double Prior { get; set; } = 20.0;

    private static bool _said;

    public static long Asked { get; private set; }

    public static long Poor { get; private set; }

    public static long Soon { get; private set; }

    public static long Stocked { get; private set; }

    public static long Standing { get; private set; }

    public static long Shelved { get; private set; }

    public static long Ordered { get; private set; }

    public static long GlutBids { get; private set; }

    public static long FromBoxes { get; private set; }

    public static long ForOrders { get; private set; }

    public string Name => "Bullion";

    public BotStanding Rung => BotStanding.Free;

    public BotDeed Propose(IBotWilful bot)
    {
        var body = bot?.Self;
        var map = body?.Map;

        if (map == null || map == Map.Internal || !body.Alive)
        {
            return null;
        }

        if (!Smiths(bot.Class))
        {
            return null;
        }

        Asked++;

        if (!BotNeeds.Due(body, "metal"))
        {
            Soon++;

            return null;
        }

        var kind = BotAnvil.Order(body, Batch, out var boxed);

        var carried = body.Backpack?.GetAmount(kind) ?? 0;

        var forOrder = BotSmith.Short(body);
        var enough = Math.Max(Enough, forOrder);

        if (carried >= enough)
        {
            Stocked++;

            return null;
        }

        if (BotMarket.FromBank(bot, kind, enough - carried) > 0)
        {
            Shelved++;

            return null;
        }

        if (boxed && BotAnvil.Fetch(bot, body, enough - carried) > 0)
        {
            Shelved++;

            return null;
        }

        if (BotMarket.Wanted(bot, kind) != null)
        {
            Standing++;

            return null;
        }

        var offer = BotMarket.Worth(kind, Math.Max(Guess, BotAnvil.Reckon(kind)));

        var factor = BotGlut.Enabled ? BotGlut.Factor(kind) : 1.0;

        if (factor < 1.0)
        {
            offer = Math.Max(1, (int)Math.Round(offer * factor));
            GlutBids++;
        }

        var wealth = BotYield.Spendable(body);
        var afford = offer <= 0 ? 0 : (wealth - Reserve) / offer;
        var units = Math.Min(Math.Max(Batch, enough - carried), afford);

        if (units < Least)
        {
            Poor++;

            return null;
        }

        if (enough > Enough && carried + Batch < enough)
        {
            ForOrders++;
        }

        Ordered++;

        if (boxed)
        {
            FromBoxes++;
        }

        Once(body);

        return BotOrder.For(map, body.Location, bot, kind, offer, units);
    }

    private static bool Smiths(BotClass klass)
    {
        var tools = klass?.Kit?.Tools;

        for (var i = 0; tools != null && i < tools.Count; i++)
        {
            if (tools[i] == typeof(SmithHammer))
            {
                return true;
            }
        }

        return false;
    }

    private static void Once(Mobile body)
    {
        if (_said)
        {
            return;
        }

        _said = true;

        logger.Information(
            "{Name} has money enough to buy its metal rather than dig it, and has put the order to the miners",
            body.Name
        );
    }

    public static string Describe() =>
        Asked == 0
            ? "no crafter has been asked about metal"
            : $"{Asked} asked: {Ordered} ordered metal ({FromBoxes} of them in a coloured metal the bank boxes hold {BotAnvil.BoxedLeast} bars or more of, {GlutBids} bid at a glut's surplus price, {ForOrders} sized to an armour order the smith had passed over), {Standing} already have an order out, {Stocked} have {Enough} ingots already (or what the order they passed over takes), {Shelved} took their own out of their bank box at a bank, {Poor} cannot afford to buy it, {Soon} asked again inside the minute";

    public static void Forget()
    {
        _said = false;
        GlutBids = 0;
        FromBoxes = 0;
        ForOrders = 0;
        Asked = 0;
        Poor = 0;
        Soon = 0;
        Stocked = 0;
        Shelved = 0;
        Standing = 0;
        Ordered = 0;
    }
}
