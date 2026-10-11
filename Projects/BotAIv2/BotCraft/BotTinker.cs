using System;
using Server.Engines.Craft;
using Server.Logging;

namespace Server.BotAI.V2;

/// <summary>
/// Making a tool out of iron, wherever the bot is standing, and handing it to whoever asked for it or to the
/// market.
///
/// <para>
/// <b>No place.</b> Tinkering wants no forge, no anvil and no fire — the tinker's tools work in a pack in the
/// road — so this is the one iron trade with no walk in it, which is also why a tinker never fails "no anvil
/// the engine will accept". The shape is the forge's otherwise: count the pack before the next swing, stop
/// when the batch is made, the swings are spent or the iron is gone, and hand over.
/// </para>
///
/// <para>
/// <b>A tool the bot itself uses is kept back.</b> The output is gathered by kind, and the bot's own pickaxe
/// is of that kind. Bound things are left out, and for a tool the class is issued one is left in the pack
/// whatever its binding, because selling one's own pickaxe to make room for the ones just made is the kind of
/// thing this project has done before.
/// </para>
/// </summary>
public sealed class BotTinker : BotDeed
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotTinker));

    public const string Trade = "tinker";

    public static double Prior { get; set; } = 90.0;

    public static double WorkMinutes { get; set; } = 4.0;

    public static int Batch { get; set; } = 3;

    public static int MaxSwings { get; set; } = 12;

    public static int SwingMs { get; set; } = 3000;

    public static int Guess { get; set; } = 25;

    public static long Stints { get; private set; }

    public static long Wrought { get; private set; }

    public static long Pieces { get; private set; }

    public static long RanOut { get; private set; }

    public static long Fruitless { get; private set; }

    public static long ToOrder { get; private set; }

    public static long Listed { get; private set; }

    private readonly Map _map;

    private readonly Point3D _where;

    private readonly BotWant _order;

    public BotWant Order => _order;

    private readonly Type _kind;

    private CraftItem _recipe;

    private int _swings;

    private int _made;

    private int _had = -1;

    private bool _swung;

    private long _swungTick;

    public BotTinker(Map map, Point3D where, Type kind, BotWant order = null)
    {
        _map = map;
        _where = where;
        _kind = kind;
        _order = order;
    }

    public override string Kind => Trade;

    public override Map Map => _map;

    public override Point3D Where => _where;

    public override double Expects => _order != null ? Prior * 1.6 : BotGlut.Claim(_kind, Prior);

    public override double Minutes => WorkMinutes;

    public override SkillName? Trains => BotTinkering.Skill;

    public override double Coin => _order == null ? 0.4 : 1.0;

    public override int Made => BotGlut.Yield(_kind, _made, Guess);

    public override string Stage => $"tinkering {_kind?.Name ?? "a tool"} ({_swings} attempts, {_made} made)";

    public override BotDoing Advance(IBotWilful bot)
    {
        var body = bot?.Self;

        if (body == null || _map == null || _map == Map.Internal || _kind == null)
        {
            return BotDoing.Failed("no body");
        }

        var tool = BotTinkering.Kit(body);

        if (tool == null)
        {
            return _made > 0 ? Handing(bot, body, "the tinker's tools wore out") : BotDoing.Failed("no tinker's tools");
        }

        _recipe ??= BotTinkering.Recipe(body, _kind);

        if (_recipe == null)
        {
            return BotDoing.Failed($"cannot make {_kind.Name} at {BotTinkering.Able(body):F0} tinkering");
        }

        if (_had < 0)
        {
            _had = BotCraftwork.Made(body, _kind);
        }

        var have = BotCraftwork.Made(body, _kind);

        if (have > _had)
        {
            BotCraftwork.Produced(_kind, have - _had);
            _made += have - _had;
            _had = have;
        }

        if (_made >= Math.Max(1, Batch) || _swings >= MaxSwings)
        {
            if (_made > 0)
            {
                return Handing(bot, body, null);
            }

            Stints++;
            Fruitless++;

            return BotDoing.Failed($"nothing came of the iron in {_swings} attempts{BotCraftEar.Why(body)}");
        }

        var cost = BotCraftwork.Cost(_recipe);

        if (BotTinkering.Ingots(body) < cost && BotMarket.FromBank(bot, BotTinkering.Metal, cost * (Math.Max(1, Batch) + 1)) > 0)
        {
            logger.Information("{Name} took its own iron out of its bank box to make {Item}", body.Name, _kind.Name);
        }

        if (BotTinkering.Ingots(body) < cost)
        {
            if (_made > 0)
            {
                return Handing(bot, body, "out of iron");
            }

            Stints++;
            RanOut++;

            return BotDoing.Failed("out of iron" + BotCraftEar.Why(body));
        }

        if (_swung && Core.TickCount - _swungTick < SwingMs)
        {
            return BotDoing.Work($"tinkering {_kind.Name}");
        }

        _swung = true;
        _swungTick = Core.TickCount;
        _swings++;

        BotCraftwork.Swing(body, BotTinkering.System, _recipe, BotTinkering.Metal, tool);

        return BotDoing.Work($"tinkering {_kind.Name}");
    }

    private BotDoing Handing(IBotWilful bot, Mobile body, string because)
    {
        var goods = BotCraftwork.Gather(body, _kind);

        goods.RemoveAll(item => BotBinding.IsBound(item, bot?.Bond));

        if (goods.Count > 0 && BotOutfit.ToolsFor((body as BotMobile)?.Class).Contains(_kind))
        {
            goods.RemoveAt(0);
        }

        if (goods.Count == 0)
        {
            Stints++;
            Fruitless++;

            return BotDoing.Done($"{_swings} attempts at {_kind.Name} and nothing to show");
        }

        var want = _order ?? BotMarket.Demand(bot, _kind);
        var made = 0;

        for (var i = 0; i < goods.Count; i++)
        {
            made += Math.Max(1, goods[i].Amount);
        }

        var filled = want == null ? 0 : Math.Min(made, want.Payable);
        var listed = made - filled;

        logger.Information(
            "{Name} tinkered {Made} {Item} in {Swings} attempts: {Filled} for somebody's order, to be carried to them, and {Listed} kept, worth {Ask}gp each{Because}",
            body.Name,
            _made,
            _kind.Name,
            _swings,
            filled,
            listed,
            Ask(bot, _kind),
            because == null ? "" : $"; stopped because {because}"
        );

        Stints++;
        Wrought++;
        Pieces += _made;
        ToOrder += filled;
        Listed += listed;

        return BotDoing.Done($"{_made} {_kind.Name} made, {filled} for an order and {listed} kept");
    }

    public static int Ask(IBotWilful bot, Type kind)
    {
        var worth = BotMarket.Worth(kind, Guess);
        var shelf = BotShops.Shelf(bot, kind, worth);

        return Math.Max(BotMarket.Floor, Math.Min(worth, shelf));
    }

    public static string Describe() =>
        Stints == 0
            ? "no tinkering has ended yet"
            : $"{Stints} tinkering stints ended: {Wrought} with {Pieces} tools made, {ToOrder} for an order and {Listed} kept for the shop by the bank, {RanOut} out of iron, {Fruitless} that made nothing";

    public static void Forget()
    {
        Stints = 0;
        Wrought = 0;
        Pieces = 0;
        RanOut = 0;
        Fruitless = 0;
        ToOrder = 0;
        Listed = 0;
    }
}
