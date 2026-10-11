using Server.Logging;

namespace Server.BotAI.V2;

/// <summary>
/// Saying aloud what the bot wants and what it will pay: a want on the market, spoken in the trade chat.
///
/// <para>
/// <b>Ordering only, and it holds no gold any more (01.10.2026).</b> An order used to put its money down in escrow on the
/// auction's board and wait for a supplier to fill it from anywhere, the goods then fetched by a reflex. Since Patrick's order of
/// 30.09 every order is hand to hand: this says the want (<see cref="BotMarket.Ask"/>), the buyer keeps its gold, and the thing
/// arrives with a carrier who walks it over and is paid at the meeting (<see cref="BotDeliver"/>), or the buyer walks to a shop
/// that holds it (<see cref="BotPatron"/>). The buyer must be able to pay what it asks for when it asks — the market refuses
/// a want the purse could not honour.
/// </para>
/// </summary>
public sealed class BotOrder : BotDeed
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotOrder));

    public const string Trade = "order";

    private readonly Map _map;

    private readonly Point3D _where;

    private readonly IBotWilful _buyer;

    private readonly System.Type _kind;

    private readonly int _offer;

    private readonly int _units;

    private bool _posted;

    public static BotOrder For(Map map, Point3D where, IBotWilful buyer, System.Type kind, int offer, int units = 1) =>
        BotMarket.Full || !BotMarket.Running ? null : new BotOrder(map, where, buyer, kind, offer, units);

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

    public override double Minutes => 0.5;

    public override bool Paperwork => true;

    public override int Outlay => _offer * System.Math.Max(1, _units);

    public override int Made => 0;

    public override string Stage => $"ordering {_units} {_kind.Name}";

    public System.Type Wanted => _kind;

    public int Units => _units;

    public int Price => _offer;

    private static string Held(Mobile body)
    {
        var held = BotMarket.Reserved(body);

        return held > 0 ? $", with {held}gp of its {BotYield.Wealth(body)}gp held for its other wants" : "";
    }

    public override BotDoing Advance(IBotWilful bot)
    {
        var body = bot?.Self;

        if (body == null || _map == null || _map == Map.Internal)
        {
            return BotDoing.Failed("no body");
        }

        if (_posted)
        {
            return BotDoing.Done($"{_kind.Name} is asked for");
        }

        _posted = true;

        var want = BotMarket.Ask(_buyer ?? bot, _kind, System.Math.Max(1, _units), _offer);

        if (want == null)
        {
            return BotDoing.Failed(
                BotMarket.Full
                    ? $"the market already holds {BotMarket.MaxWants} wants"
                    : $"could not pay for {System.Math.Max(1, _units)} {_kind.Name} at {_offer}gp if they came{Held(body)}"
            );
        }

        logger.Information(
            "{Name} has asked the island for {Units} {Item} at {Offer}gp each",
            body.Name,
            System.Math.Max(1, _units),
            _kind.Name,
            want.Offer
        );

        return BotDoing.Done($"{System.Math.Max(1, _units)} {_kind.Name} asked for at {want.Offer}gp each");
    }
}
