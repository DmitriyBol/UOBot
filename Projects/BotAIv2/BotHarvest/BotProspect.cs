using Server.Logging;

namespace Server.BotAI.V2;

public sealed class BotProspect : BotDeed
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotProspect));

    public const string Trade = "prospect";

    public static double Prior { get; set; } = 30.0;

    public static double WorkMinutes { get; set; } = 4.0;

    private readonly Map _map;

    private readonly Point3D _where;

    private readonly int _knew;

    private int _found;

    private bool _looked;

    public BotProspect(Map map, Point3D where, int knew)
    {
        _map = map;
        _where = where;
        _knew = knew;
    }

    public override string Kind => Trade;

    public override Map Map => _map;

    public override Point3D Where => _where;

    public override double Expects => Prior;

    public override double Minutes => WorkMinutes;

    public override SkillName? Trains => null;

    public override int Outlay => 0;

    public override double Coin => 0.0;

    public override int Made => _found * BotOre.WorthSmelting;

    public override string Stage =>
        _looked
            ? $"prospected ({_found} new seams)"
            : $"prospecting out towards ({_where.X}, {_where.Y})";

    public override bool Bend(IBotWilful bot)
    {
        bot?.Resolve?.Ledger?.Beware(Trade, _map, _where);

        return false;
    }

    public override BotDoing Advance(IBotWilful bot)
    {
        var body = bot?.Self;

        if (body == null || _map == null || _map == Map.Internal || !body.Alive)
        {
            return BotDoing.Failed("no body");
        }

        if (!body.InRange(_where, BotGround.Reach / 2))
        {
            return BotDoing.Walk(_map, _where, BotArrival.Within(BotGround.Reach / 2), $"prospecting towards ({_where.X}, {_where.Y})");
        }

        BotGround.Survey(_map, body.Location);

        if (BotGround.Sweeping(_map, body.Location))
        {
            return BotDoing.Work("waiting for the ground here to be swept");
        }

        _looked = true;
        _found = System.Math.Max(0, BotGround.Seams.Count - _knew);

        if (_found > 0)
        {
            BotGround.Found(_found);

            logger.Information(
                "{Name} prospected out to ({X}, {Y}) and put {Found} new seams on the board, which now holds {All}",
                body.Name,
                body.X,
                body.Y,
                _found,
                BotGround.Seams
            );

            return BotDoing.Done($"{_found} new seams out at ({body.X}, {body.Y})");
        }

        BotGround.FoundNothing();

        return BotDoing.Done($"nothing in the ground out at ({body.X}, {body.Y})");
    }
}
