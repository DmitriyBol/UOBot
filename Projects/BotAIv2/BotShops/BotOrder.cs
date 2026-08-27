using Server.Logging;

namespace Server.BotAI.V2;

/// <summary>
/// Putting an order on the board, and going back for it when somebody has filled it.
///
/// <para>
/// <b>Ordering only, and the collecting half used to live here.</b> It was a second shape of this same
/// undertaking — built with no kind, offered by <see cref="BotUpkeep"/> ahead of any fresh order, and it
/// picked up whatever the board was holding. The reasoning was that from the bot's side the two are one
/// thing, and the reasoning was right about that and wrong about where to put it: collecting costs nothing,
/// takes no time and moves the bot nowhere, so weighed against a rescue at a hundred and forty gold a minute
/// it lost every auction it was ever in. One bot on the shard collected anything in twenty-six minutes on
/// 26.08.2026, and it won only because it had nothing else on the board at all. Collecting is now a reflex
/// on the population's beat — see <c>BotAuction.Fetch</c> — where nothing has to be bid against.
/// </para>
/// </summary>
public sealed class BotOrder : BotDeed
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotOrder));

    /// <summary>The ledger's key.</summary>
    public const string Trade = "order";

    private readonly Map _map;

    private readonly Point3D _where;

    private readonly IBotWilful _buyer;

    private readonly System.Type _kind;

    private readonly int _offer;

    private readonly int _units;

    private bool _posted;

    /// <summary>Putting a fresh order on the board.</summary>
    public BotOrder(Map map, Point3D where, IBotWilful buyer, System.Type kind, int offer, int units = 1)
    {
        _map = map;
        _where = where;
        _buyer = buyer;
        _kind = kind;
        _offer = offer;
        _units = System.Math.Max(1, units);
    }

    public override string Kind => Trade;

    public override Map Map => _map;

    public override Point3D Where => _where;

    public override double Expects => BotUpkeep.Prior;

    /// <summary>Short. Neither half of this is work; it is the paperwork around somebody else's.</summary>
    public override double Minutes => 0.5;

    /// <summary>What it costs is the order itself, and the market takes it at the moment of asking.</summary>
    public override int Outlay => _offer * System.Math.Max(1, _units);

    public override string Stage => $"ordering {_units} {_kind.Name}";

    public override BotDoing Advance(IBotWilful bot)
    {
        var body = bot?.Self;

        if (body == null || _map == null || _map == Map.Internal)
        {
            return BotDoing.Failed("no body");
        }

        if (_posted)
        {
            // The want stands on its own from here: it raises its own offer, finds its own supplier and holds
            // the goods until they are fetched. Nothing about it needs this bot to stand still.
            return BotDoing.Done($"{_kind.Name} is on the board");
        }

        _posted = true;

        var want = BotAuction.Ask(_buyer ?? bot, _kind, System.Math.Max(1, _units), _offer);

        if (want == null)
        {
            return BotDoing.Failed($"the board would not take an order for {_kind.Name}");
        }

        logger.Information(
            "{Name} has asked the population for {Units} {Item} at {Offer}gp each",
            body.Name,
            System.Math.Max(1, _units),
            _kind.Name,
            want.Offer
        );

        return BotDoing.Done($"{System.Math.Max(1, _units)} {_kind.Name} ordered at {want.Offer}gp each");
    }
}
