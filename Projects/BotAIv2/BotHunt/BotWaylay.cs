using System;
using Server.Logging;

namespace Server.BotAI.V2;

public sealed class BotWaylay : BotDeed
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotWaylay));

    public const string Trade = "waylay";

    public static bool Running { get; set; } = true;

    public static double Prior { get; set; } = 380.0;

    public static int WaitMs { get; set; } = 300000;

    public static int HideTries { get; set; } = 4;

    public static long Begun { get; private set; }

    public static long Sprung { get; private set; }

    public static long Barren { get; private set; }

    public static int HoldMs { get; set; } = 5000;

    public static long Lapsed { get; private set; }

    public static long Open { get; private set; }

    private readonly Map _map;

    private readonly Point3D _at;

    private bool _counted;

    private bool _there;

    private bool _hid;

    private int _tries;

    private long _since;

    private long _saw;

    public BotWaylay(Map map, Point3D at)
    {
        _map = map;
        _at = at;
    }

    public override string Kind => Trade;

    public override bool Committed => true;

    public override bool Unpaid => true;

    public override bool Steadfast => true;

    public override bool Still => _there;

    public override bool Repeats(BotDeed other) => other is BotWaylay;

    public override Map Map => _map;

    public override Point3D Where => _at;

    public override double Expects => Prior;

    public override double Minutes => WaitMs / 60000.0 + 2.0;

    public override int Outlay => 0;

    public override double Coin => 0.0;

    public override int Made => 0;

    public override string Stage => _there ? "lying in wait for a mark" : "off to where the work is, to lie in wait";

    public override BotDoing Advance(IBotWilful bot)
    {
        var body = bot?.Self;

        if (body is not BotMobile thief || _map == null || body.Map != _map)
        {
            return BotDoing.Failed("no body");
        }

        if (!_counted)
        {
            _counted = true;
            Begun++;
        }

        if (BotOutlaw.Jailed(thief))
        {
            return BotDoing.Done("taken before it could rob");
        }

        var now = Core.TickCount;

        if (!_there)
        {
            if (!body.InRange(_at, 3))
            {
                return BotDoing.Walk(_map, _at, BotArrival.Within(3), "to where the work is");
            }

            _there = true;
            _since = now;
        }

        if (!_hid && BotShadow.Running)
        {
            if (!BotShadow.Ready(body))
            {
                return BotDoing.Work("catching its breath");
            }

            if (BotShadow.Hide(body))
            {
                _hid = true;
            }
            else if (++_tries >= HideTries)
            {
                Open++;
                _hid = true;
            }
            else
            {
                return BotDoing.Work("trying to hide");
            }
        }

        if (_saw != 0)
        {
            if (now - _saw < HoldMs)
            {
                return BotDoing.Work("a mark has come, and the blow is a beat away");
            }

            Lapsed++;

            return BotDoing.Done("a mark came by and nothing was made of it");
        }

        if (BotRobber.Mark(thief, _map) is { } mark)
        {
            Sprung++;
            _saw = now;
            BotRobber.Spring(thief, mark);

            logger.Information(
                "{Name} sees {Mark} come by, {Away} tiles off, and holds still at ({X}, {Y})",
                body.Name,
                mark.Name,
                (int)body.GetDistanceToSqrt(mark),
                body.X,
                body.Y
            );

            return BotDoing.Work("a mark has come, and the blow is a beat away");
        }

        if (now - _since >= WaitMs)
        {
            Barren++;

            if (body.Hidden)
            {
                body.RevealingAction();
            }

            return BotDoing.Done($"waited {WaitMs / 60000} minutes and nobody came");
        }

        return BotDoing.Work("lying in wait");
    }

    public static string Describe() =>
        $"{Begun} waits by the road: {Sprung} saw a mark ({Lapsed} of them never set on), {Barren} ran out with nobody coming, {Open} could not hide and waited in the open";

    public static void Forget()
    {
        Begun = 0;
        Sprung = 0;
        Barren = 0;
        Lapsed = 0;
        Open = 0;
    }
}
