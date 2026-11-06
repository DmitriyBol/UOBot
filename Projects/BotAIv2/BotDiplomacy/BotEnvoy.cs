using System;

namespace Server.BotAI.V2;

public sealed class BotEnvoy : BotDeed
{
    public const string Trade = "envoy";

    public static double Prior { get; set; } = 150.0;

    public static long Began { get; private set; }

    public static long Hosts { get; private set; }

    public static long Arrived { get; private set; }

    public static long Finished { get; private set; }

    public static long Lapsed { get; private set; }

    public static long Unreached { get; private set; }

    public static long Replaced { get; private set; }

    private readonly BotHearing _m;

    private readonly bool _host;

    private readonly Point3D _from;

    private long _began;

    private bool _there;

    private bool _unreached;

    private BotRecall _recall;

    public BotEnvoy(BotHearing meeting, bool asHost, Point3D from)
    {
        _m = meeting;
        _host = asHost;
        _from = from;
        _began = Core.TickCount;
    }

    public BotHearing Meeting => _m;

    public override string Kind => Trade;

    public override Map Map => _m.Map;

    public override Point3D Where => _m.Place;

    public override double Expects => Prior;

    public override bool Unpaid => true;

    public override bool Steadfast => true;

    public override bool Still => _there;

    public override bool Committed => _there;

    public override SkillName? Trains => null;

    public override double Minutes =>
        5.0 + Math.Max(0.2, Math.Sqrt(Math.Pow(_m.Place.X - _from.X, 2) + Math.Pow(_m.Place.Y - _from.Y, 2)) * BotWalk.StepDelayMs(BotMobile.Runs) / 60000.0);

    public override double HoldsFor => Minutes + 10.0;

    public override string Stage =>
        _there
            ? $"{(_host ? "answering for" : "envoy of")} {(_host ? _m.Host : _m.Guest)} at {_m.Town?.Name ?? "the seat"}: {_m.Stage.ToString().ToLowerInvariant()}"
            : $"walking to {_m.Town?.Name ?? "the seat"} {(_host ? "to answer" : "as envoy")} over {BotDuke.Word(_m.Topic)}";

    public override void Taken(IBotWilful bot)
    {
        _began = Core.TickCount;

        if (bot?.Self is not BotMobile body)
        {
            return;
        }

        BotParley.Took(_m, body, _host);

        if (_host)
        {
            Hosts++;
        }
        else
        {
            Began++;
        }
    }

    public override void Resumed(IBotWilful bot) => _began = Core.TickCount;

    public override BotDoing Advance(IBotWilful bot)
    {
        if (bot?.Self is not BotMobile body || !body.Alive)
        {
            return BotDoing.Failed("no body to go with");
        }

        if (_m.Stage == BotHearingStage.Over)
        {
            if (_m.Heard)
            {
                Finished++;

                return BotDoing.Done($"the word was given at {_m.Town?.Name ?? "the seat"}: {_m.Ending}");
            }

            Lapsed++;

            return BotDoing.Failed($"the meeting lapsed: {_m.Ending}");
        }

        if (_host ? !ReferenceEquals(_m.HostRep, body) : !ReferenceEquals(_m.Envoy, body))
        {
            Replaced++;

            return BotDoing.Failed(_host ? $"somebody else answers for {_m.Host}" : $"somebody else went for {_m.Guest}");
        }

        BotParley.Still(_m, body, _host);

        if (BotParley.There(_m, body))
        {
            if (!_there)
            {
                _there = true;
                Arrived++;
            }

            return BotDoing.Work(Stage);
        }

        _there = false;

        if (Core.TickCount - _began >= BotParley.MarchMs)
        {
            Unreached++;
            _unreached = true;

            return BotDoing.Failed($"could not reach {_m.Town?.Name ?? "the seat"} in {BotParley.MarchMs / 60000} minutes");
        }

        if (BotRecall.Instead(bot, _m.Map, _m.Place, BotParley.ArriveTiles, $"the meeting at {_m.Town?.Name}", ref _recall) is { } cast)
        {
            return cast;
        }

        return BotDoing.Walk(_m.Map, _m.Place, BotArrival.Within(Math.Max(1, BotParley.ArriveTiles - 1)), $"to the meeting at {_m.Town?.Name ?? "the seat"}");
    }

    public override bool Bend(IBotWilful bot)
    {
        _unreached = true;
        Unreached++;

        return false;
    }

    public override void Drop(IBotWilful bot)
    {
        if (bot?.Self is BotMobile body)
        {
            BotParley.Let(_m, body, _host, _unreached);
        }
    }

    public static string Describe() =>
        $"envoys: {Began} set out and {Hosts} went to answer, {Arrived} reached the meeting, {Finished} saw the word given, {Lapsed} saw it lapse, {Unreached} could not get there, {Replaced} were replaced";

    public static void Forget()
    {
        Began = 0;
        Hosts = 0;
        Arrived = 0;
        Finished = 0;
        Lapsed = 0;
        Unreached = 0;
        Replaced = 0;
    }
}
