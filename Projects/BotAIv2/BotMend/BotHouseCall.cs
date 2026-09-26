using System;
using System.Collections.Generic;
using Server.Logging;

namespace Server.BotAI.V2;

/// <summary>
/// A healer with nobody to mend going to where our fighters are.
///
/// <para>
/// <b>Every piece of a healer's work is reactive, and a healer standing where nothing happens gets none of it.</b>
/// Mending wants somebody hurt within reach, a hire wants a fighter going afield within a hundred tiles, standing by
/// wants a fight within sixty, and the herbs come round once in half an hour. At 07:1x on 16.09.2026 six of the eleven
/// healers held "nothing" — Gwendra at (1418, 1812), three hundred tiles from the nearest fight, "47 proposers asked,
/// not one of them had anything to offer". This is the walk that puts a healer where the other offers can find it:
/// to the nearest of ours that is out fighting, and then it stops, and the attendant and the surgeon take over.
/// </para>
///
/// <para>
/// Unpaid and cheap, so it never outbids a hire or a patient and is only ever taken when there is nothing else — which
/// is exactly the state it is for.
/// </para>
/// </summary>
public sealed class BotHouseCall : BotDeed
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotHouseCall));

    public const string Trade = "housecall";

    public static double Prior { get; set; } = 8.0;

    public static double WorkMinutes { get; set; } = 4.0;

    public static int Stay { get; set; } = 10;

    public static int CapMs { get; set; } = 300000;

    public static long Arrived { get; private set; }

    public static long Lost { get; private set; }

    public static long Capped { get; private set; }

    private static readonly HashSet<Serial> _called = [];

    public static bool CalledOn(BotMobile fighter) => fighter != null && _called.Contains(fighter.Serial);

    private readonly BotMobile _fighter;

    private readonly Map _map;

    private readonly Point3D _found;

    private readonly long _began;

    public BotHouseCall(BotMobile fighter, Map map)
    {
        _fighter = fighter;
        _map = map;
        _found = fighter?.Location ?? Point3D.Zero;
        _began = Core.TickCount;
    }

    public override string Kind => Trade;

    public override Map Map => _map;

    public override Point3D Where => _found;

    public override double Expects => Prior;

    public override double Minutes => WorkMinutes;

    public override bool Unpaid => true;

    public override SkillName? Trains => null;

    public override int Outlay => 0;

    public override string Stage => $"going to where {_fighter?.Name ?? "somebody"} was fighting";

    public override bool Hurries => false;

    public override void Taken(IBotWilful bot)
    {
        if (_fighter != null)
        {
            _called.Add(_fighter.Serial);
        }
    }

    public override void Drop(IBotWilful bot) => Release();

    private void Release()
    {
        if (_fighter != null)
        {
            _called.Remove(_fighter.Serial);
        }
    }

    public override BotDoing Advance(IBotWilful bot)
    {
        var body = bot?.Self;

        if (body == null || _map == null || _map == Map.Internal)
        {
            return BotDoing.Failed("no body");
        }

        if (_found == Point3D.Zero)
        {
            Release();
            Lost++;

            return BotDoing.Done("nowhere to go");
        }

        if (Core.TickCount - _began >= CapMs)
        {
            Release();
            Capped++;

            return BotDoing.Done($"{CapMs / 60000} minutes on the road to where {_fighter?.Name} was fighting was enough");
        }

        if (body.InRange(_found, Stay))
        {
            Release();
            Arrived++;

            var still = _fighter is { Deleted: false, Alive: true } && _fighter.Map == _map && body.InRange(_fighter, BotAttendant.Reach);

            if (!still && body is BotMobile arrived)
            {
                BotHouseCalls.Rest(arrived);
            }

            logger.Information(
                "{Name} has come to where {Fighter} was fighting at ({X}, {Y}), {Still}; the mending and the hire are offered from here",
                body.Name,
                _fighter?.Name,
                _found.X,
                _found.Y,
                still ? "and it is still within reach" : "and it has moved on"
            );

            return BotDoing.Done($"came to where {_fighter?.Name} was fighting");
        }

        return BotDoing.Walk(_map, _found, BotArrival.Within(Stay), $"to where {_fighter?.Name} was fighting");
    }

    public static string Describe() => $"{Arrived} house calls arrived, {Lost} lost the fighter on the way, {Capped} ran out of road";

    public static void Forget()
    {
        Arrived = 0;
        Lost = 0;
        Capped = 0;
        _called.Clear();
    }
}

/// <summary>
/// Offers a healer with nothing to do the walk to the nearest of ours that is out fighting — further off than the
/// attendant looks, because inside that reach the attendant itself has an offer. Every gate is counted.
/// </summary>
public sealed class BotHouseCalls : IBotProposer
{
    public static bool Running { get; set; } = true;

    public static int Range { get; set; } = 800;

    public static long Asked { get; private set; }

    public static long Offered { get; private set; }

    public static long Held { get; private set; }

    public static long Unfit { get; private set; }

    public static long Pressed { get; private set; }

    public static long Bare { get; private set; }

    public static long Near { get; private set; }

    public static long Nobody { get; private set; }

    public static long Sealed { get; private set; }

    public static long Resting { get; private set; }

    public static int RestMs { get; set; } = 180000;

    private static readonly Dictionary<Serial, long> _restUntil = [];

    public static void Rest(BotMobile healer)
    {
        if (healer != null)
        {
            _restUntil[healer.Serial] = Core.TickCount + RestMs;
        }
    }

    public string Name => "HouseCalls";

    public BotStanding Rung => BotStanding.Free;

    public BotDeed Propose(IBotWilful bot)
    {
        if (!Running || bot?.Self is not BotMobile { Deleted: false, Alive: true } body || body.Class?.Role != BotRole.Medic)
        {
            return null;
        }

        var map = body.Map;

        if (map == null || map == Map.Internal)
        {
            return null;
        }

        Asked++;

        if (body.Squad != null)
        {
            Held++;

            return null;
        }

        if (body.HitsMax <= 0 || body.Hits < body.HitsMax * BotHunter.FitAt)
        {
            Unfit++;

            return null;
        }

        if (BotThreat.Hunter(body, BotDefender.Reach) != null)
        {
            Pressed++;

            return null;
        }

        if (!BotAccompany.Means(body))
        {
            Bare++;

            return null;
        }

        if (_restUntil.TryGetValue(body.Serial, out var until) && Core.TickCount - until < 0)
        {
            Resting++;

            return null;
        }

        BotMobile best = null;
        var closest = int.MaxValue;
        var bots = BotPopulation.Bots;

        for (var i = 0; i < bots.Count; i++)
        {
            var m = bots[i];

            if (m == body || m is not { Deleted: false, Alive: true } || m.Map != map || m.Class?.Role == BotRole.Medic)
            {
                continue;
            }

            if (!BotRetainer.Afield(m) || BotRegard.AtWar(body, m) || BotHouseCall.CalledOn(m))
            {
                continue;
            }

            var away = Math.Max(Math.Abs(m.X - body.X), Math.Abs(m.Y - body.Y));

            if (away <= BotRetainer.Reach)
            {
                Near++;

                return null;
            }

            if (away > Range || away >= closest)
            {
                continue;
            }

            if (BotMend.Abetting(body, m) != null)
            {
                continue;
            }

            if (BotReach.Ask(map, body.Location, m.Location, BotArrival.Within(BotHouseCall.Stay)) == BotReachVerdict.Sealed)
            {
                Sealed++;

                continue;
            }

            best = m;
            closest = away;
        }

        if (best == null)
        {
            Nobody++;

            return null;
        }

        Offered++;

        return new BotHouseCall(best, map);
    }

    public static string Describe() =>
        !Running
            ? "house calls are never offered"
            : Asked == 0
                ? "no healer has been offered a house call"
                : $"{Asked} times a healer was asked for a house call: {Offered} sent towards the fighting, {Near} already had fighting within {BotRetainer.Reach} tiles, {Nobody} found nobody of ours afield within {Range}, {Held} in a company, {Unfit} too hurt, {Pressed} with something on it, {Bare} with nothing to heal with, {Resting} resting after an empty arrival, {Sealed} fighters passed over for having no road to them; {BotHouseCall.Describe()}";

    public static void Forget()
    {
        Asked = 0;
        Offered = 0;
        Held = 0;
        Unfit = 0;
        Pressed = 0;
        Bare = 0;
        Near = 0;
        Nobody = 0;
        Sealed = 0;
        Resting = 0;
        _restUntil.Clear();
        BotHouseCall.Forget();
    }
}
