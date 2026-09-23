using System.Collections.Generic;
using Server.Items;

namespace Server.BotAI.V2;

/// <summary>
/// Going back for what death took. The bot own corpse, and nobody else.
///
/// <para>
/// <b>Only the weapon and the book are bound; everything else is lying where the bot fell.</b> The kit was
/// deliberately narrowed to that — tools wear out and are bought again, coin is meant to move — which makes
/// dying cost a hammer, a pickaxe, a sewing kit, the bandages, the herbs and whatever was in the purse. One
/// warrior-archer died holding four hundred gold and the ledger recorded it plainly: <c>-400 coin</c>. All of
/// it was still there, twenty tiles away, and nothing in the population had any reason to go and get it.
/// </para>
///
/// <para>
/// So this is not a new mechanic so much as the missing half of an old one. The corpse is remembered on the
/// bot at the moment of death — no sweeping the world for it — and it stops being remembered the moment it is
/// emptied, decays, or turns out to hold nothing.
/// </para>
/// </summary>
public sealed class BotReclaim : BotDeed
{
    public const string Trade = "reclaim";

    public static double Prior { get; set; } = 20.0;

    public static double WorkMinutes { get; set; } = 1.5;

    public static int Reach { get; set; } = 2;

    private readonly Corpse _corpse;

    private readonly Map _map;

    private readonly Point3D _where;

    private int _taken;

    private int _coins;

    public BotReclaim(Corpse corpse)
    {
        _corpse = corpse;
        _map = corpse?.Map;
        _where = corpse?.Location ?? Point3D.Zero;
    }

    public override string Kind => Trade;

    public override Map Map => _map;

    public override Point3D Where => _where;

    public override double Expects => Prior;

    public override double Minutes => WorkMinutes;

    public override SkillName? Trains => null;

    public override int Outlay => 0;

    public override double Coin => 1.0;

    public override int Made => 0;

    public override string Stage =>
        _taken > 0 ? $"recovered {_taken} things and {_coins}gp" : "going back for its own things";

    public override BotDoing Advance(IBotWilful bot)
    {
        var body = bot?.Self;

        if (body == null || _map == null || _map == Map.Internal)
        {
            return BotDoing.Failed("no body");
        }

        if (_corpse == null || _corpse.Deleted || _corpse.Map != _map)
        {
            Forget(bot);

            return BotDoing.Done("there was nothing left of it");
        }

        if (!body.InRange(_corpse.Location, Reach))
        {
            return BotDoing.Walk(_map, _corpse.Location, BotArrival.Within(Reach), "back for its own things");
        }

        Empty(body);

        (bot as BotMobile)?.Rearm();

        Forget(bot);

        return BotDoing.Done($"{_taken} things and {_coins}gp back off its own corpse");
    }

    private void Empty(Mobile body)
    {
        var pack = body.Backpack;

        if (pack == null)
        {
            return;
        }

        var ceiling = BotLadder.Ceiling(body) * 0.8;

        List<Item> lying = [.. _corpse.Items];

        for (var i = 0; i < lying.Count; i++)
        {
            var item = lying[i];

            if (item == null || item.Deleted || !item.Movable)
            {
                continue;
            }

            if (item is Gold coin)
            {
                _coins += coin.Amount;

                pack.DropItem(coin);

                continue;
            }

            if (BotLadder.Load(body) >= ceiling)
            {
                break;
            }

            pack.DropItem(item);

            _taken++;
        }
    }

    private void Forget(IBotWilful bot)
    {
        if (bot is BotMobile mobile && ReferenceEquals(mobile.Remains, _corpse))
        {
            mobile.Remains = null;
        }
    }
}

/// <summary>
/// Offers a bot the trip back to its own corpse, and only its own.
///
/// <para>
/// On the ordinary rung, competing on the same arithmetic as everything else, which is right: a pickaxe and
/// four hundred gold lying in a field is worth going for, and a corpse holding a handful of bandages is not
/// worth a walk across Britain. The ledger settles which is which from what actually comes back.
/// </para>
/// </summary>
public sealed class BotUndertaker : IBotProposer
{
    public string Name => "Undertaker";

    public BotStanding Rung => BotStanding.Free;

    public BotDeed Propose(IBotWilful bot)
    {
        var body = bot?.Self;

        if (body is not { Deleted: false, Alive: true } || bot is not BotMobile mobile)
        {
            return null;
        }

        var corpse = mobile.Remains;

        if (corpse is not { Deleted: false } || corpse.Map != body.Map || corpse.Map == Map.Internal)
        {
            mobile.Remains = null;

            return null;
        }

        if (corpse.Items.Count == 0)
        {
            mobile.Remains = null;

            return null;
        }

        return !BotPopulation.Within(body.Map, corpse.Location) ? null : new BotReclaim(corpse);
    }
}
