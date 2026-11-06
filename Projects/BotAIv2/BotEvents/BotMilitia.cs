using Server.Logging;

namespace Server.BotAI.V2;

public sealed class BotMilitiaCall : IBotProposer
{
    public string Name => "Militia";

    public BotStanding Rung => BotStanding.Free;

    public static int CountEveryMs { get; set; } = 30000;

    private static long _countedTick;

    private static bool _counted;

    private static int _free;

    public BotDeed Propose(IBotWilful bot)
    {
        if (!BotSeasons.Running || !BotInvasion.Running || BotInvasion.Calling is not { } raid)
        {
            return null;
        }

        var body = bot?.Self;

        if (body == null || body.Map != raid.Map || bot is not IBotSquadMember { Squad: null } || !BotSeasons.Fighter(body)
            || !body.InRange(raid.Town.Square, BotInvasion.TownReach))
        {
            return null;
        }

        var now = Core.TickCount;

        if (!BotInvasion.Offers(raid, now))
        {
            return null;
        }

        if (!_counted || now - _countedTick >= CountEveryMs)
        {
            _counted = true;
            _countedTick = now;
            _free = Free(raid);
        }

        if (_free < BotInvasion.LeastMilitia)
        {
            return null;
        }

        if (!BotGates.Joined(raid.Map, body.Location, raid.Site)
            || BotReach.Ask(raid.Map, body.Location, raid.Site, BotArrival.Within(BotPeril.Side / 3)) == BotReachVerdict.Sealed)
        {
            return null;
        }

        BotInvasion.Offer();

        return new BotMilitia(raid);
    }

    private static int Free(BotInvasion.Raid raid)
    {
        var free = 0;
        var bots = BotPopulation.Bots;

        for (var i = 0; i < bots.Count; i++)
        {
            var b = bots[i];

            if (BotSeasons.Fighter(b) && b.Map == raid.Map && b is IBotSquadMember { Squad: null } && b.InRange(raid.Town.Square, BotInvasion.TownReach))
            {
                free++;
            }
        }

        return free;
    }
}

public sealed class BotMilitia : BotDeed
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotMilitia));

    public const string Trade = "militia";

    private readonly BotInvasion.Raid _raid;

    private readonly BotSweep _sweep;

    public BotMilitia(BotInvasion.Raid raid)
    {
        _raid = raid;
        _sweep = new BotSweep(raid.Map, raid.Site, BotPeril.Reading(raid.Map, raid.Site));
    }

    public override string Kind => Trade;

    public override Map Map => _sweep.Map;

    public override Point3D Where => _sweep.Where;

    public override double Expects => BotInvasion.MilitiaPrior;

    public override double Minutes => _sweep.Minutes;

    public override SkillName? Trains => null;

    public override bool Braves => true;

    public override bool Alongside => true;

    public override string Stage => $"{_raid.Town?.Name}'s militia: {_sweep.Stage}";

    public override void Taken(IBotWilful bot) => BotInvasion.Took(_raid, bot?.Self);

    public override BotDoing Advance(IBotWilful bot)
    {
        var member = bot as IBotSquadMember;

        if (_raid.Over && member?.Squad is not { Stance: BotSquadStance.Fighting })
        {
            _sweep.Drop(bot);

            var done = BotDoing.Done($"the invasion of {_raid.Town?.Name} is over");

            BotInvasion.Ended(_raid, bot?.Self, done);

            return done;
        }

        var doing = _sweep.Advance(bot);

        if (member?.Squad is { } squad)
        {
            BotInvasion.Mustered(_raid, squad);
        }

        if (doing.Kind is BotDoingKind.Done or BotDoingKind.Failed)
        {
            BotInvasion.Ended(_raid, bot?.Self, doing);
        }

        return doing;
    }

    public override bool Bend(IBotWilful bot) => _sweep.Bend(bot);

    public override void Drop(IBotWilful bot)
    {
        _sweep.Drop(bot);

        logger.Information("{Name} let {Town}'s militia go", bot?.Self?.Name, _raid.Town?.Name);
    }
}
