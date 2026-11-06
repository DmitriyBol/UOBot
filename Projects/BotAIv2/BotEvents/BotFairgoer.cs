namespace Server.BotAI.V2;

public sealed class BotFairCall : IBotProposer
{
    public string Name => "Fair";

    public BotStanding Rung => BotStanding.Free;

    public static int LeastLeftMs { get; set; } = 1800000;

    public BotDeed Propose(IBotWilful bot)
    {
        if (!BotSeasons.Running || !BotFair.Open || bot?.Self is not BotMobile body || body.Map != BotPopulation.Home)
        {
            return null;
        }

        if (!BotTrading.Trader(body) && !BotCraftFirst.Crafter(bot) || BotFair.Came(body))
        {
            return null;
        }

        var town = BotFair.Town;

        if (BotFair.ClosesTick - Core.TickCount < LeastLeftMs || !body.InRange(town.Square, BotFair.Reach) || town.Bounds.Contains(body.Location))
        {
            return null;
        }

        var map = body.Map;

        if (!BotGates.Joined(map, body.Location, town.Square)
            || BotReach.Ask(map, body.Location, town.Square, BotArrival.Within(6)) == BotReachVerdict.Sealed
            || BotDanger.Road(body, map, body.Location, town.Square, BotQuad.Strength(body), out _, out _, out _, BotFairgoer.Trade))
        {
            return null;
        }

        BotFair.Offer();

        return new BotFairgoer(town);
    }
}

public sealed class BotFairgoer : BotDeed
{
    public const string Trade = "fair";

    private readonly BotTowns.Town _town;

    private readonly Map _map;

    private long _arrivedTick;

    private bool _arrived;

    public BotFairgoer(BotTowns.Town town)
    {
        _town = town;
        _map = BotPopulation.Home;
    }

    public override string Kind => Trade;

    public override Map Map => _map;

    public override Point3D Where => _town.Square;

    public override double Expects => BotFair.Prior;

    public override double Minutes => BotFair.StayMs / 60000.0;

    public override string Stage => _arrived ? $"at the fair in {_town.Name}" : $"walking to the fair in {_town.Name}";

    public override void Taken(IBotWilful bot) => BotFair.Going(bot?.Self);

    public override BotDoing Advance(IBotWilful bot)
    {
        var body = bot?.Self;

        if (body == null)
        {
            return BotDoing.Failed("no body");
        }

        if (!BotFair.Open || !ReferenceEquals(BotFair.Town, _town))
        {
            if (_arrived)
            {
                return BotDoing.Done($"the fair in {_town.Name} closed while I was there");
            }

            BotFair.Late();

            return BotDoing.Failed($"the fair in {_town.Name} closed before I got there");
        }

        if (!_arrived)
        {
            if (!body.InRange(_town.Square, 6))
            {
                return BotDoing.Walk(_map, _town.Square, BotArrival.Within(6), $"walking to the fair in {_town.Name}");
            }

            _arrived = true;
            _arrivedTick = Core.TickCount;
            BotFair.Reached(body);
        }

        if (Core.TickCount - _arrivedTick >= BotFair.StayMs)
        {
            return BotDoing.Done($"spent {BotFair.StayMs / 60000} minutes at the fair in {_town.Name}");
        }

        return BotDoing.Work($"at the fair in {_town.Name}");
    }
}
