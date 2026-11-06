using System;
using Server.Logging;

namespace Server.BotAI.V2;

public sealed class BotResettle : BotDeed
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotResettle));

    public const string Trade = "resettle";

    public static double Prior { get; set; } = 10.0;

    public static long Began { get; private set; }

    public static long Arrived { get; private set; }

    public static long Unreached { get; private set; }

    public static long BySettling { get; private set; }

    public static long ByPoor { get; private set; }

    public static long ByDeaths { get; private set; }

    private readonly BotTravel _road;

    private readonly BotTowns.Town _town;

    private readonly string _from;

    private readonly string _why;

    private readonly BotRelocate.Cause _cause;

    private Point3D _at;

    public BotResettle(Map map, BotTowns.Town town, string from, Point3D at, BotRelocate.Cause cause, string why)
    {
        _road = new BotTravel(map, town, from);
        _town = town;
        _from = from;
        _at = at;
        _cause = cause;
        _why = why;
    }

    public override string Kind => Trade;

    public override Map Map => _road.Map;

    public override Point3D Where => _road.Where;

    public override double Expects => Prior;

    public override bool Unpaid => true;

    public override bool Steadfast => true;

    public override SkillName? Trains => null;

    public override double Minutes =>
        Math.Max(0.2, Math.Sqrt(Math.Pow(Where.X - _at.X, 2) + Math.Pow(Where.Y - _at.Y, 2)) * BotWalk.StepDelayMs(BotMobile.Runs) / 60000.0);

    public override string Stage => $"moving house from {_from} to {_town?.Name}: {_why}";

    public override void Taken(IBotWilful bot)
    {
        _road.Taken(bot);

        if (bot?.Self is not BotMobile body || _town == null)
        {
            return;
        }

        var rec = BotResidence.Adopt(body);
        var left = BotResidence.TownOf(rec)?.Name;

        rec = BotResidence.File(body, _town);
        rec.Pending = null;
        rec.Left = left;
        rec.Moves++;
        rec.Deaths.Clear();

        rec.Watched = false;

        Began++;

        switch (_cause)
        {
            case BotRelocate.Cause.Settling:
                {
                    BySettling++;

                    break;
                }
            case BotRelocate.Cause.Poor:
                {
                    ByPoor++;

                    break;
                }
            default:
                {
                    ByDeaths++;

                    break;
                }
        }

        logger.Information(
            "{Name} the {Class} is moving house from {From} to {Town}: {Why}; {Residents} live there now",
            body.Name,
            body.Class?.Name,
            left ?? _from,
            _town.Name,
            _why,
            BotResidence.Residents(_town)
        );

        BotEvents.Post("residence", body, $"moving from {left ?? _from} to {_town.Name}: {_why}");
    }

    public override void Resumed(IBotWilful bot) => _road.Resumed(bot);

    public override BotDoing Advance(IBotWilful bot)
    {
        if (bot?.Self is { } body)
        {
            _at = body.Location;
        }

        var doing = _road.Advance(bot);

        if (doing.Kind == BotDoingKind.Done)
        {
            Arrived++;
        }
        else if (doing.Kind == BotDoingKind.Failed)
        {
            Unreached++;
        }

        return doing;
    }

    public static string Describe() =>
        $"{Began} moves set out on ({BySettling} settling, {ByPoor} for a poor living, {ByDeaths} for deaths), {Arrived} arrived, {Unreached} gave up on the road";

    public static void Forget()
    {
        Began = 0;
        Arrived = 0;
        Unreached = 0;
        BySettling = 0;
        ByPoor = 0;
        ByDeaths = 0;
    }
}
