using Server.Items;
using Server.Logging;
using Server.Targeting;

namespace Server.BotAI.V2;

/// <summary>
/// Cutting wood: walk to the nearest tree and swing until the pack has enough or the tree has nothing left.
///
/// <para>
/// <b>Deliberately the smallest harvest on the shard.</b> The miner has veins worth different money, a lode
/// to walk to, a forge to smelt at and a bank to leave metal in; a woodcutter has trees, and every tree is
/// the same tree. So there is no survey, no ledger of good ground and no second leg — the errand is "cut
/// until the axe stops paying", and the walk that brought the bot into the woods is the walk the auction
/// already priced.
/// </para>
/// </summary>
public sealed class BotChop : BotDeed
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotChop));

    /// <summary>The ledger's key.</summary>
    public const string Trade = "chop";

    /// <summary>What a chopping trip is reckoned at before the ledger knows better.</summary>
    public static double Prior { get; set; } = 60.0;

    /// <summary>How long one is expected to take.</summary>
    public static double WorkMinutes { get; set; } = 2.0;

    /// <summary>How often the axe comes round. The engine holds its own delay; this only stops us asking faster.</summary>
    public static int SwingMs { get; set; } = 2000;

    /// <summary>
    /// How long the axe may go without producing a log before the trip is given up.
    ///
    /// <para>
    /// Half a minute, which is a dozen swings. A tree that has been cut out answers every swing with nothing
    /// and looks exactly like a tree that is simply unlucky, and the engine says which only by silence — the
    /// message it would send goes to a client this bot has not got.
    /// </para>
    /// </summary>
    public static int StallMs { get; set; } = 30000;

    private readonly Map _map;

    private readonly Point3D _where;

    private readonly int _want;

    private IPoint3D _tree;

    private int _cut;

    private int _swings;

    private long _swungTick;

    private long _grewTick;

    public BotChop(Map map, Point3D where, int want)
    {
        _map = map;
        _where = where;
        _want = want;
    }

    public override string Kind => Trade;

    public override Map Map => _map;

    public override Point3D Where => _where;

    public override double Expects => Prior;

    public override double Minutes => WorkMinutes;

    public override SkillName? Trains => SkillName.Lumberjacking;

    public override int Outlay => 0;

    /// <summary>Nothing here is coin. Wood is goods, and what it is worth is what the shard pays for a log.</summary>
    public override double Coin => 0.0;

    public override int Made => _cut * BotTimber.Worth;

    public override string Stage =>
        _tree == null
            ? $"out to the woods near ({_where.X}, {_where.Y})"
            : $"cutting wood ({_cut} logs in {_swings} swings)";

    /// <summary>Logs in the pack when the trip began, so wood it was already carrying is not counted as cut.</summary>
    private int _had;

    /// <summary>Seeded on the first swing, because the pack is not empty when the trip starts.</summary>
    private bool _counting;

    /// <summary>
    /// Puts the axe in the hand, freeing it first if something else is in it.
    ///
    /// <para>
    /// <c>Mobile.EquipItem</c> refuses a busy layer outright rather than swapping, so whatever the bot is
    /// holding goes into the pack first. It is put back by <see cref="Sheathe"/> when the trip ends, and by
    /// <c>BotMobile.Rearm</c> on its own clock if the trip never does.
    /// </para>
    /// </summary>
    private static bool Wield(Mobile body, Item tool)
    {
        if (tool.Parent == body)
        {
            return true;
        }

        var held = body.FindItemOnLayer(Layer.TwoHanded) ?? body.FindItemOnLayer(Layer.OneHanded);

        if (held != null && held != tool)
        {
            body.AddToBackpack(held);
        }

        return body.EquipItem(tool);
    }

    /// <summary>The axe back into the pack and the bot's own weapon back into its hand.</summary>
    private static void Sheathe(Mobile body)
    {
        var tool = body?.FindItemOnLayer(Layer.OneHanded) ?? body?.FindItemOnLayer(Layer.TwoHanded);

        if (tool is not Hatchet and not BaseAxe)
        {
            return;
        }

        body.AddToBackpack(tool);

        (body as BotMobile)?.Rearm();
    }

    /// <summary>
    /// Given up on. The axe goes back in the pack — a woodcutter that walks off to a fight holding a hatchet
    /// is a bot that has quietly swapped its own weapon for a tool, and the shopper will then buy it another
    /// of the weapon it is standing on.
    /// </summary>
    public override void Drop(IBotWilful bot)
    {
        base.Drop(bot);

        Sheathe(bot?.Self);
    }

    public override BotDoing Advance(IBotWilful bot)
    {
        var body = bot?.Self;

        if (body == null || _map == null || _map == Map.Internal)
        {
            return BotDoing.Failed("no body");
        }

        var tool = BotTimber.Tool(body);

        if (tool == null)
        {
            return BotDoing.Failed("nothing to cut with");
        }

        // <b>The axe has to be in the hand, and this one line is why not one log has ever been cut on this
        // shard.</b> Lumberjacking is the only harvest with this rule and it enforces it twice:
        // <c>Lumberjacking.CheckHarvest</c> refuses outright when <c>tool.Parent != from</c> and sends
        // "The axe must be equipped for any serious wood chopping" — to a client a bot does not have. Mining
        // has no such rule, which is exactly why the pickaxe worked from the pack and the hatchet never did.
        // Thirty honest swings at a real tree, nought logs, and the engine explaining itself to nobody:
        // 0 logs in 30 swings at 14:01 on 04.09.2026, and the same reading every half hour before it.
        if (!Wield(body, tool))
        {
            return BotDoing.Failed("it cannot get the axe into its hand");
        }

        // Enough. Said as done rather than pressed on with: wood is worth the same by the log, so a
        // woodcutter that keeps going past what it came for is a woodcutter carrying its own weight limit.
        if (_cut >= _want)
        {
            Sheathe(body);

            return BotDoing.Done($"{_cut} logs in {_swings} swings");
        }

        // Looked for again every time, because a tree that has been cut out stops being a tree to the
        // engine — and the next one along is usually one tile away.
        _tree ??= BotTimber.Find(body);

        if (_tree == null)
        {
            Sheathe(body);

            return _cut > 0
                ? BotDoing.Done($"{_cut} logs in {_swings} swings — no tree left within reach")
                : BotDoing.Failed("no tree within reach");
        }

        var trunk = new Point3D(_tree.X, _tree.Y, _tree.Z);

        if (!body.InRange(trunk, BotTimber.SwingReach))
        {
            return BotDoing.Walk(_map, trunk, BotArrival.Within(BotTimber.SwingReach), "to a tree");
        }

        var now = Core.TickCount;

        if (_swungTick != 0 && now - _swungTick < SwingMs)
        {
            return BotDoing.Work("cutting wood");
        }

        // <b>Counted before the next swing, not after the last one.</b> Harvesting is asynchronous — the
        // engine starts a timer and the wood appears a moment later — so reading the pack in the same breath
        // as the swing reads it too early, every time. It showed as "dropped chop: cutting wood (0 logs in
        // 29 swings)" at 13:31 on 04.09.2026: twenty-nine honest swings at a real tree, nought recorded, the
        // errand never able to reach its own finish, and the whole arrow chain waiting behind it. The same
        // fault as BotBrew and BotFletch had, on the harvest side of the house.
        if (!_counting)
        {
            _counting = true;
            _had = BotTimber.Logs(body);
        }

        var have = BotTimber.Logs(body);

        if (have > _had)
        {
            _cut += have - _had;
            _had = have;
            _grewTick = now;
        }

        _swungTick = now;
        _swings++;

        BotTimber.Swing(body, tool, _tree);

        // From here the original fence stands unchanged, and now it means what it says: _grewTick moves
        // whenever wood actually arrives, so "this tree has stopped giving" is a fact rather than a
        // certainty produced by reading the pack a second too soon.
        if (_grewTick == 0)
        {
            _grewTick = now;

            return BotDoing.Work("cutting wood");
        }

        if (now - _grewTick < StallMs)
        {
            return BotDoing.Work("cutting wood");
        }

        // This one is finished. Not the trip — the next ring out almost always holds another.
        _tree = null;
        _grewTick = 0;

        if (_cut > 0)
        {
            return BotDoing.Work($"moving to the next tree, {_cut} logs so far");
        }

        logger.Information(
            "{Name} cut at a tree {Swings} times and got nothing; trying another",
            body.Name,
            _swings
        );

        return BotDoing.Work("looking for another tree");
    }
}
