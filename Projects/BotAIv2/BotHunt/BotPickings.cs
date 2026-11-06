using Server.Items;
using Server.Logging;

namespace Server.BotAI.V2;

public sealed class BotPickings : BotDeed
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotPickings));

    public const string Trade = "pickings";

    public static double Prior { get; set; } = 90.0;

    public static double WorkMinutes { get; set; } = 0.5;

    public static int Reach => BotQuarry.LootReach;

    public static int Range { get; set; } = 30;

    public static long Barren { get; private set; }

    public static void Forget() => Barren = 0;

    private readonly Map _map;

    private readonly Corpse _corpse;

    private int _taken;

    private int _coins;

    private int _hides;

    public BotPickings(Map map, Corpse corpse)
    {
        _map = map;
        _corpse = corpse;
    }

    public override string Kind => Trade;

    public override bool Braves => true;

    public override Map Map => _map;

    public override Point3D Where => _corpse?.GetWorldLocation() ?? Point3D.Zero;

    public override double Expects => Prior;

    public override double Minutes => WorkMinutes;

    public override SkillName? Trains => null;

    public override int Outlay => 0;

    public override double Coin => 1.0;

    public override bool Bend(IBotWilful bot)
    {
        var where = _corpse?.GetWorldLocation() ?? Point3D.Zero;

        if (where != Point3D.Zero)
        {
            bot?.Resolve?.Ledger?.Beware(Trade, _map, where);
        }

        return false;
    }

    public override int Made => _made;

    private int _made;

    public override string Stage =>
        _taken > 0 || _coins > 0
            ? $"took {_taken} things and {_coins}gp off what it killed"
            : $"going through {_corpse?.Owner?.Name ?? "what it killed"}";

    public override BotDoing Advance(IBotWilful bot)
    {
        var body = bot?.Self;

        if (body == null || _map == null || _map == Map.Internal)
        {
            return BotDoing.Failed("no body");
        }

        if (_corpse is not { Deleted: false })
        {
            return BotDoing.Done("it was gone by the time it got there");
        }

        var where = _corpse.GetWorldLocation();

        if (!body.InRange(where, Reach))
        {
            return BotDoing.Walk(_map, where, BotArrival.Within(Reach), "back to what it killed");
        }

        _hides = BotSlay.Skin(body, _corpse);

        var (taken, coins, made) = BotSlay.Rifle(bot, body, _corpse);

        _taken = taken;
        _coins = coins;
        _made = made;

        if (_corpse.Owner is BotMobile { Guild: Guilds.Guild theirs } && body.Guild is Guilds.Guild ours &&
            ours != theirs)
        {
            BotWar.Looted(ours.Name, theirs.Name, coins + made);
        }

        if (taken == 0 && coins == 0 && _hides == 0)
        {

            Barren++;

            _corpse.Looters?.Add(body);

            return BotDoing.Done("there was nothing on it");
        }

        logger.Information(
            "{Name} went back for what it killed and took {Things} things, {Coins}gp and {Hides} leather",
            body.Name,
            taken,
            coins,
            _hides
        );

        return BotDoing.Done($"{taken} things, {coins}gp and {_hides} leather off {_corpse.Owner?.Name ?? "it"}");
    }
}

public sealed class BotPicker : IBotProposer
{
    public static long Asked { get; private set; }

    public static long Sworn { get; private set; }

    public static long NothingDead { get; private set; }

    public static long Picked { get; private set; }

    public static long Offered { get; private set; }

    public static long Sealed { get; private set; }

    public static long Baulked { get; private set; }

    public string Name => "Picker";

    public BotStanding Rung => BotStanding.Free;

    public BotDeed Propose(IBotWilful bot)
    {
        var body = bot?.Self;
        var map = body?.Map;

        if (map == null || map == Map.Internal || !body.Alive)
        {
            return null;
        }

        if (body is BotMobile { Class.Scavenges: false })
        {
            Sworn++;

            return null;
        }

        Asked++;

        Corpse found = null;

        foreach (var item in map.GetItemsInRange(body.Location, BotPickings.Range))
        {
            if (item is not Corpse corpse || corpse.Deleted)
            {
                continue;
            }

            if (corpse.Killer != body)
            {
                continue;
            }

            if (corpse.Items.Count == 0)
            {
                Picked++;

                continue;
            }

            if (corpse.Looters?.Contains(body) == true)
            {
                Picked++;

                continue;
            }

            found = corpse;

            break;
        }

        if (found == null)
        {
            NothingDead++;

            return null;
        }

        var lies = found.GetWorldLocation();

        if (BotReach.Ask(map, body.Location, lies, BotArrival.Within(BotPickings.Reach))
            == BotReachVerdict.Sealed)
        {
            Sealed++;

            return null;
        }

        if (bot.Resolve?.Ledger?.Cautious(BotPickings.Trade, map, lies) == true)
        {
            Baulked++;

            return null;
        }

        Offered++;

        return new BotPickings(map, found);
    }

    public static string Describe() =>
        Asked == 0
            ? "nobody has been offered anything they killed"
            : $"{Asked} looks for its own kills ({Sworn} answers went to classes that never stoop): {Offered} bodies offered, {Picked} already been through, {NothingDead} had nothing of theirs lying about; {BotPickings.Barren} held nothing worth carrying, {Sealed} lay where nothing can walk, {Baulked} had refused this bot a road before, and {BotGleaner.Sealed} spent arrows lay the same way ({BotGleaner.Baulked} of them already refused)";

    public static void Forget()
    {
        Asked = 0;
        Sworn = 0;
        NothingDead = 0;
        Picked = 0;
        Offered = 0;
        BotPickings.Forget();
    }
}
