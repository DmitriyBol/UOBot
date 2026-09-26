using System;
using System.Collections.Generic;
using Server.Items;
using Server.Logging;

namespace Server.BotAI.V2;

/// <summary>
/// Carrying the takings to the band's chest at the hideout.
///
/// <para>
/// <b>Patrick's order of 17.09.2026, night.</b> "They must keep what they have taken somewhere, since the towns are shut
/// to them and a catch takes everything off them." A thief's coin used to ride in its pack until a patrol took it: the
/// towns are barred to a red (build 111b), the counters are all in towns, and so a robbery's whole profit was a wager on
/// never being caught. Now a member with more than <see cref="Least"/> in its pack walks it out to the chest by the fire
/// and puts down everything it is not carrying for itself.
/// </para>
///
/// <para>
/// Unpaid and claimed at <see cref="Prior"/>, like the band's other work: it earns the thief nothing at the moment it
/// happens, and the auction would price a walk of three hundred tiles with no wage at the bottom of every list.
/// </para>
/// </summary>
public sealed class BotStash : BotDeed
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotStash));

    public const string Trade = "stash";

    public static int Least { get; set; } = 200;

    public static double Prior { get; set; } = 350.0;

    public static int EveryMs { get; set; } = 120000;

    public static long Runs { get; private set; }

    public static long Done { get; private set; }

    public static long Empty { get; private set; }

    public static long Chestless { get; private set; }

    public static long Gold { get; private set; }

    public static long Things { get; private set; }

    private readonly Map _map;

    private Point3D _at;

    private bool _counted;

    public BotStash(Map map, Point3D at)
    {
        _map = map;
        _at = at;
    }

    public override string Kind => Trade;

    public override bool Committed => true;

    public override bool Unpaid => true;

    public override bool Steadfast => true;

    public override bool Repeats(BotDeed other) => other is BotStash;

    public override bool Hurries => true;

    public override Map Map => _map;

    public override Point3D Where => _at;

    public override double Expects => Prior;

    public override double Minutes => 3.0;

    public override int Outlay => 0;

    public override double Coin => 0.0;

    public override int Made => 0;

    public override string Stage => "carrying the takings to the chest";

    public override BotDoing Advance(IBotWilful bot)
    {
        var body = bot?.Self;

        if (body == null || _map == null || body.Map != _map)
        {
            return BotDoing.Failed("no body");
        }

        if (!_counted)
        {
            _counted = true;
            Runs++;
        }

        if (BotLair.Chest is not { } chest || chest.Map != _map)
        {
            Chestless++;

            return BotDoing.Failed("the band has no chest");
        }

        _at = chest.GetWorldLocation();

        if (!body.InRange(_at, 2))
        {
            var step = BotLair.Doorstep();

            return BotDoing.Walk(_map, step == Point3D.Zero ? _at : step, BotArrival.Within(1), "out to the band's chest");
        }

        var pack = body.Backpack;

        if (pack == null)
        {
            return BotDoing.Failed("no pack");
        }

        var coin = 0;
        var things = 0;
        var keeps = BotPurse.Keeps(body);
        var spare = pack.GetAmount(typeof(Gold)) - keeps;

        if (spare > 0 && pack.ConsumeTotal(typeof(Gold), spare))
        {
            if (chest.TryDropItem(body, new Gold(spare), false))
            {
                coin = spare;
            }
            else
            {
                body.AddToBackpack(new Gold(spare));
            }
        }

        var kept = BotUnload.Keeps(bot);
        List<Item> spared = [];
        var goods = !BotFence.Is(body);

        for (var i = 0; goods && i < pack.Items.Count; i++)
        {
            var item = pack.Items[i];

            if (item is not { Deleted: false, Movable: true } || item is Gold || BotBinding.IsBound(item, bot.Bond)
                || kept.ContainsKey(item.GetType()))
            {
                continue;
            }

            spared.Add(item);
        }

        for (var i = 0; i < spared.Count; i++)
        {
            if (chest.TryDropItem(body, spared[i], false))
            {
                things++;
            }
        }

        var stocked = 0;

        if (BotFence.Is(body) && BotFence.Wants() is { Count: > 0 } asked)
        {
            var owed = BotFence.Spare(body as BotMobile, asked);
            List<(Item Stack, int Give)> parcels = [];

            for (var i = 0; i < pack.Items.Count; i++)
            {
                if (pack.Items[i] is not { Deleted: false, Movable: true } item || item is Gold
                    || BotBinding.IsBound(item, bot.Bond) || !owed.TryGetValue(item.GetType(), out var left)
                    || left <= 0)
                {
                    continue;
                }

                var give = Math.Min(left, Math.Max(1, item.Amount));
                owed[item.GetType()] = left - give;

                if (give > 0)
                {
                    parcels.Add((item, give));
                }
            }

            for (var i = 0; i < parcels.Count; i++)
            {
                var (stack, give) = parcels[i];
                var held = Math.Max(1, stack.Amount);

                var parcel = give >= held ? stack : Mobile.LiftItemDupe(stack, held - give);

                if (parcel != null && chest.TryDropItem(body, parcel, false))
                {
                    stocked += give;
                    things++;
                }
            }

            BotFence.Stocked(stocked);

            logger.Information(
                "{Name} at the chest: the band asked for {Kinds} kinds, it held {Owed} of them spare, made up {Parcels} parcels and put down {Stocked}",
                body.Name,
                asked.Count,
                owed.Count,
                parcels.Count,
                stocked
            );
        }

        Gold += coin;
        Things += things;

        if (coin <= 0 && things <= 0)
        {
            Empty++;

            return BotDoing.Done("nothing worth putting down");
        }

        Done++;
        BotLair.Took(things, coin);

        logger.Information(
            "{Name} put {Gold}gp and {Things} things in the band's chest at ({X}, {Y}){Order}",
            body.Name,
            coin,
            things,
            _at.X,
            _at.Y,
            stocked > 0 ? $", {stocked} of them the band's order" : ""
        );

        return BotDoing.Done($"{coin}gp and {things} things into the chest");
    }

    public static string Describe() =>
        $"{Runs} walks out to the band's chest: {Done} put something down ({Gold}gp and {Things} things), {Empty} arrived with nothing, {Chestless} found no chest";

    public static void Forget()
    {
        Runs = 0;
        Done = 0;
        Empty = 0;
        Chestless = 0;
        Gold = 0;
        Things = 0;
    }
}
