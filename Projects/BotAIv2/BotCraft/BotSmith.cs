using System;
using System.Collections.Generic;
using Server.Logging;

namespace Server.BotAI.V2;

/// <summary>
/// Offers a bot with a hammer and some metal a turn at an anvil, and offers it the board's orders first.
///
/// <para>
/// <b>Orders before speculation, and the reason is arithmetic rather than politeness.</b> Something on the
/// board has money already down against it — <see cref="BotMarket.Ask"/> takes the payment when the want is
/// raised — so filling one is a sale that has already happened. Making something on spec is making something
/// that might sell. The auction is told the difference through <see cref="BotForge.Expects"/> and settles it
/// the same way it settles everything else, so a smith with nothing on the board still smiths.
/// </para>
///
/// <para>
/// <b>What it will not do is take an order it cannot fill.</b> A recipe beyond the bot's skill, or one
/// needing a material it has not got, is refused here rather than discovered at the anvil — because a taken
/// order that fails is a bot's coin held in escrow for nothing while the bot that paid it goes on fighting
/// with a broken sword.
/// </para>
/// </summary>
public sealed class BotSmith : IBotProposer
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotSmith));

    public static int LeastMetal { get; set; } = 6;

    public static int OrderPieces { get; set; } = 2;

    private static bool _saidNoSystem;

    private static bool _saidNoForge;

    private static bool _said;

    public static long Asked { get; private set; }

    public static long NoHammer { get; private set; }

    public static long NoMetal { get; private set; }

    public static long NoForge { get; private set; }

    public static bool WorkshopsInBritain { get; set; } = true;

    public static long NotBritain { get; private set; }

    public static long ToOrder { get; private set; }

    public static long ShortOfMetal { get; private set; }

    public static long Fetched { get; private set; }

    public static long NothingAffordable { get; private set; }

    public static long OnSpec { get; private set; }

    public static int ShortMs { get; set; } = 600000;

    public static int MostForOrder { get; set; } = 50;

    private static readonly Dictionary<Serial, (int Need, long Tick)> _short = [];

    public static long Remembered { get; private set; }

    public static long FetchedForOrder { get; private set; }

    public static int Short(Mobile body)
    {
        if (body == null || !_short.TryGetValue(body.Serial, out var known))
        {
            return 0;
        }

        if (Core.TickCount - known.Tick >= ShortMs)
        {
            _short.Remove(body.Serial);

            return 0;
        }

        return known.Need;
    }

    public string Name => "Smith";

    public BotStanding Rung => BotStanding.Free;

    public BotDeed Propose(IBotWilful bot)
    {
        var body = bot?.Self;
        var map = body?.Map;

        if (map == null || map == Map.Internal || !body.Alive)
        {
            return null;
        }

        if (BotAnvil.Kit(body) == null)
        {
            NoHammer++;

            return null;
        }

        Asked++;

        if (BotAnvil.System == null)
        {
            if (!_saidNoSystem)
            {
                _saidNoSystem = true;

                logger.Error("The blacksmithing system does not exist yet, so nobody can forge");
            }

            return null;
        }

        if (BotAnvil.Ingots(body, BotAnvil.Best(body, LeastMetal)) < LeastMetal)
        {
            Fetched += BotAnvil.Fetch(bot, body, LeastMetal);
        }

        if (BotAnvil.Ingots(body, BotAnvil.Best(body, LeastMetal)) < LeastMetal)
        {
            NoMetal++;

            return null;
        }

        var square = WorkshopsInBritain ? BotMarket.Square(map) : Point3D.Zero;
        var smithy = BotGround.Fire(bot, square != Point3D.Zero ? square : body.Location);

        if (WorkshopsInBritain && smithy != Point3D.Zero && !BotMarket.InBritain(smithy))
        {
            NotBritain++;

            return null;
        }

        if (smithy == Point3D.Zero)
        {
            NoForge++;

            if (!_saidNoForge)
            {
                _saidNoForge = true;

                logger.Error("No forge with an anvil beside it is known within reach of the bots on {Map}", map);
            }

            return null;
        }

        var order = Order(bot, body);

        if (order != null)
        {
            ToOrder++;
            order.Offered();
            Once(body, order);

            return new BotForge(map, smithy, order);
        }

        if (BotRoom.SpecShut(body))
        {
            return null;
        }

        var spec = BotAnvil.Choose(body);

        if (spec == null)
        {
            NothingAffordable++;

            return null;
        }

        OnSpec++;

        BotGlut.Spec(spec.ItemType);

        return new BotForge(map, smithy, null, spec.ItemType);
    }

    private static BotWant Order(IBotWilful bot, Mobile body)
    {
        var wants = BotMarket.Wants;

        BotWant best = null;
        var bestWorth = 0;

        var starved = false;
        var starvedNeed = 0;
        var starvedWorth = 0;

        for (var i = 0; i < wants.Count; i++)
        {
            var want = wants[i];

            if (!want.IsOpen || ReferenceEquals(want.Buyer, bot))
            {
                continue;
            }

            var recipe = BotAnvil.Recipe(body, want.Kind);

            if (want.Worth <= bestWorth || recipe == null)
            {
                continue;
            }

            if (!BotCourier.Answerable(bot, want) || BotCourier.Holding(bot, want))
            {
                continue;
            }

            var cost = BotCraftwork.Cost(recipe) * System.Math.Max(1, OrderPieces);

            if (BotAnvil.Ingots(body, BotAnvil.Best(body, cost)) < cost)
            {
                starved = true;
                want.Passed(BotPass.Material);

                if (want.Worth > starvedWorth)
                {
                    starvedWorth = want.Worth;
                    starvedNeed = cost;
                }

                continue;
            }

            best = want;
            bestWorth = want.Worth;
        }

        if (starved && best == null)
        {
            ShortOfMetal++;
            Remember(bot, body, starvedNeed);

            BotCraftFirst.Short(body, BotAnvil.Best(body, starvedNeed), Math.Min(starvedNeed, MostForOrder));
        }

        return best;
    }

    private static void Remember(IBotWilful bot, Mobile body, int need)
    {
        need = Math.Min(need, MostForOrder);

        if (need <= BotBullion.Enough)
        {
            return;
        }

        if (_short.Count > 4096)
        {
            _short.Clear();
        }

        _short[body.Serial] = (need, Core.TickCount);
        Remembered++;

        var metal = BotAnvil.Best(body, need);
        var lack = need - BotAnvil.Ingots(body, metal);

        if (lack > 0)
        {
            FetchedForOrder += BotMarket.FromBank(bot, metal, lack);
        }
    }

    private static void Once(Mobile body, BotWant order)
    {
        if (_said)
        {
            return;
        }

        _said = true;

        logger.Information(
            "{Name} is the first smith to take an order off the board: {Buyer} wants {Item} and has {Worth}gp down",
            body.Name,
            order.Buyer?.Self?.Name ?? "somebody",
            order.Label,
            order.Worth
        );
    }

    public static string Describe() =>
        Asked == 0
            ? "nobody with a hammer has been asked to forge"
            : $"{Asked} asked: {ToOrder} took an order off the board, {ShortOfMetal} passed one over for want of iron, {OnSpec} forged on spec, "
              + $"{NoMetal} short of metal ({Fetched} ingots taken out of their own bank box at a bank to stop being it), {NothingAffordable} with metal but not enough for any recipe they can work, {NoForge} with no forge in reach, {NotBritain} with no forge in Britain within their reach; {BotCraftwork.Unlikely} recipes passed over across the crafts for less than {BotCraftwork.LeastChance:P0} of making them; the board's metal reads {BotAnvil.Reading}, so an empty smith orders {BotAnvil.Metal.Name}; "
              + $"{Remembered} times a smith short of metal for the dearest order it passed over asked the island for that much (up to {MostForOrder} ingots, kept {ShortMs / 60000} minutes; {FetchedForOrder} ingots taken out of its own bank box for it), {_short.Count} smiths remembering one now; "
              + $"{BotForge.Describe()}";

    public static void Forget()
    {
        _said = false;
        _saidNoSystem = false;
        _saidNoForge = false;
        Asked = 0;
        NoHammer = 0;
        NoMetal = 0;
        NoForge = 0;
        NotBritain = 0;
        ToOrder = 0;
        ShortOfMetal = 0;
        Fetched = 0;
        NothingAffordable = 0;
        OnSpec = 0;
        Remembered = 0;
        FetchedForOrder = 0;
        _short.Clear();
        BotForge.Forget();
    }
}
