using System.Collections.Generic;
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

    public const string Trade = "chop";

    public static double Prior { get; set; } = 60.0;

    public static double WorkMinutes { get; set; } = 2.0;

    public static int SwingMs { get; set; } = 2000;

    public static int StallMs { get; set; } = 30000;

    public static long Spoken { get; private set; }

    public static long Silent { get; private set; }

    public static long Unreached { get; private set; }

    public static int AdriftMost { get; set; } = 4;

    public static int QuietSwings { get; set; } = 20;

    public static int GiveUpSwings { get; set; } = 80;

    public static long Fruitless { get; private set; }

    private int _grewSwings;

    private readonly Map _map;

    private readonly Point3D _where;

    private readonly int _want;

    private IPoint3D _tree;

    private readonly HashSet<(int X, int Y)> _shunned = [];

    private int _adrift;

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

    public override bool Steadfast => true;

    public override void Resumed(IBotWilful bot)
    {
        _swungTick = 0;
        _grewTick = 0;
        _grewSwings = _swings;
        _adrift = 0;
        _counting = false;
    }

    public override Map Map => _map;

    public override Point3D Where => _where;

    public override double Expects => Prior;

    public override double Minutes => WorkMinutes;

    public override SkillName? Trains => SkillName.Lumberjacking;

    public override int Outlay => 0;

    public override double Coin => 0.0;

    public override int Made => _cut * BotTimber.Worth;

    public override string Stage =>
        _tree == null
            ? $"out to the woods near ({_where.X}, {_where.Y})"
            : $"cutting wood ({_cut} logs in {_swings} swings)";

    private int _had;

    private bool _counting;

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

        if (!Wield(body, tool))
        {
            return BotDoing.Failed("it cannot get the axe into its hand");
        }

        if (_cut >= _want)
        {
            Sheathe(body);

            var (ordered, listed) = BotTimber.Store(bot);

            return BotDoing.Done($"{_cut} logs in {_swings} swings, {ordered} to order and {listed} put out to sell");
        }

        _tree ??= BotTimber.Find(body, _shunned);

        if (_tree == null)
        {
            Sheathe(body);

            if (_cut <= 0)
            {
                return BotDoing.Failed("no tree within reach");
            }

            var (ordered, listed) = BotTimber.Store(bot);

            return BotDoing.Done(
                $"{_cut} logs in {_swings} swings, {ordered} to order and {listed} put out to sell — no tree left within reach"
            );
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
            _grewSwings = _swings;
        }
        else if (have < _had)
        {
            _had = have;
        }

        if (_cut <= 0 && _swings >= GiveUpSwings)
        {
            Fruitless++;
            Sheathe(body);

            return BotDoing.Failed($"{_swings} swings and not one log");
        }

        var word = BotHeard.Last(body, Server.Engines.Harvest.Lumberjacking.System?.GetDefinition(), out _);

        if (word != BotHeard.Word.Adrift)
        {
            _adrift = 0;
        }

        switch (word)
        {
            case BotHeard.Word.Empty:
            case BotHeard.Word.Taken:
                {
                    BotHeard.Clear(body);
                    Spoken++;

                    if (_tree != null)
                    {
                        _shunned.Add((_tree.X, _tree.Y));
                    }

                    _tree = null;
                    _grewTick = 0;
                    _grewSwings = _swings;

                    return BotDoing.Work(
                        _cut > 0 ? $"moving to the next tree, {_cut} logs so far" : "looking for another tree"
                    );
                }

            case BotHeard.Word.Broken:
                {
                    BotHeard.Clear(body);

                    return BotDoing.Failed("the axe wore out");
                }

            case BotHeard.Word.Full:
                {
                    BotHeard.Clear(body);
                    Sheathe(body);

                    var (ordered, listed) = BotTimber.Store(bot);

                    return BotDoing.Done(
                        $"{_cut} logs in {_swings} swings, {ordered} to order and {listed} put out to sell — the pack would hold no more"
                    );
                }

            case BotHeard.Word.Adrift:
                {
                    BotHeard.Clear(body);
                    _grewTick = now;

                    if (++_adrift >= AdriftMost && _tree != null)
                    {
                        _shunned.Add((_tree.X, _tree.Y));
                        Unreached++;
                        _tree = null;
                        _adrift = 0;
                        _grewSwings = _swings;

                        return BotDoing.Work(
                            _cut > 0 ? $"the tree was out of reach from here, {_cut} logs so far; looking for another" : "the tree was out of reach from here; looking for another"
                        );
                    }

                    break;
                }
        }

        _swungTick = now;
        _swings++;

        BotTimber.Swing(body, tool, _tree);

        if (_grewTick == 0)
        {
            _grewTick = now;
            _grewSwings = _swings;

            return BotDoing.Work("cutting wood");
        }

        if (now - _grewTick < StallMs && _swings - _grewSwings < QuietSwings)
        {
            return BotDoing.Work("cutting wood");
        }

        Silent++;

        if (_tree != null)
        {
            _shunned.Add((_tree.X, _tree.Y));
        }

        _tree = null;
        _grewTick = 0;
        _grewSwings = _swings;

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
