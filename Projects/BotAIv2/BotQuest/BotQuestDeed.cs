using System;
using System.Collections.Generic;
using Server.Mobiles;

namespace Server.BotAI.V2;

public sealed class BotQuestDeed : BotDeed
{
    public const string Trade = "errand";

    public static double KillMinutes { get; set; } = 2.0;

    public static double StandMinutes { get; set; } = 2.0;

    public static int LookMs { get; set; } = 10000;

    public static int LairWithin { get; set; } = 6;

    public static long Resumes { get; private set; }

    public static long Hops { get; private set; }

    public static long Emptied { get; private set; }

    private readonly BotQuest _quest;

    private readonly SkillName _trains;

    private readonly bool _lairs;

    private readonly double _claim;

    private List<Point3D> _passed;

    private Point3D _place;

    private int _roam;

    private BotSlay _slay;

    private BaseCreature _target;

    private long _lookedTick;

    private long _emptyTick;

    private bool _arrived;

    private long _pausedTick;

    private bool _lost;

    private int _doneAtTake;

    private string _failed;

    public BotQuestDeed(BotQuest quest, SkillName trains, Point3D from, Point3D place, int roam = 0)
    {
        _roam = roam;
        _quest = quest;
        _trains = trains;
        _lairs = quest.Kind == BotQuestKind.Kill && quest.Where == Point3D.Zero;
        _place = place != Point3D.Zero ? place : quest.Where != Point3D.Zero ? quest.Where : from;
        _claim = Claim(quest, from, _place);
        _doneAtTake = quest.Done;

        _lookedTick = Core.TickCount - LookMs;
        _emptyTick = Core.TickCount;
        _pausedTick = Core.TickCount;
    }

    internal static double Work(BotQuest quest) =>
        quest.Kind == BotQuestKind.Kill ? Math.Max(1, quest.Amount - quest.Done) * KillMinutes : StandMinutes;

    internal static double Claim(BotQuest quest, Point3D from, Point3D place) =>
        quest.Held / Math.Max(1.0, BotAppraisal.Travel(from, place) + Work(quest));

    public override string Kind => Trade;

    public override Map Map => _quest.Map;

    public override Point3D Where => _place;

    public override double Expects => _claim;

    public override double Minutes => Work(_quest);

    public override SkillName? Trains => _quest.Kind == BotQuestKind.Kill ? _trains : null;

    public override int Outlay => 0;

    public override double Coin => 1.0;

    public override int Made => 0;

    public override bool Braves => _quest.Kind == BotQuestKind.Kill;

    public override bool Steadfast => true;

    public override bool Posted => true;

    public override Mobile Foe => _slay?.Foe;

    public override string Stage =>
        _quest.Kind switch
        {
            BotQuestKind.Kill => _target is { Deleted: false, Alive: true }
                ? $"setting on {_target.Name} for the board, {_quest.Done} of {_quest.Amount} {_quest.What} down"
                : $"looking for {_quest.What} near ({_place.X}, {_place.Y}), {_quest.Done} of {_quest.Amount} down",
            BotQuestKind.Gather => $"bringing {_quest.Amount} {_quest.What} to ({_quest.Where.X}, {_quest.Where.Y})",
            _ => $"going to look at ({_quest.Where.X}, {_quest.Where.Y})"
        };

    public override void Taken(IBotWilful bot)
    {
        if (!BotQuests.Take(_quest, bot?.Self as BotMobile))
        {
            _lost = true;

            return;
        }

        _doneAtTake = _quest.Done;
    }

    public override BotDoing Advance(IBotWilful bot)
    {
        var body = bot?.Self;
        var map = _quest.Map;

        if (body == null || map == null || map == Map.Internal)
        {
            return BotDoing.Failed("no body");
        }

        if (_lost)
        {
            return BotDoing.Failed("somebody else took the errand first");
        }

        if (!BotQuests.Standing(_quest))
        {
            return BotDoing.Failed("the errand came off the board");
        }

        if (!ReferenceEquals(_quest.Taker, body))
        {
            return BotDoing.Failed("the board gave the errand to somebody else");
        }

        if (_quest.Fulfilled)
        {
            BotQuests.Finish(_quest, body as BotMobile);

            return BotDoing.Done($"the errand is done: {_quest.Tell()}");
        }

        return _quest.Kind switch
        {
            BotQuestKind.Kill => Kill(bot, body, map),
            BotQuestKind.Gather => Gather(body, map),
            _ => Scout(body, map)
        };
    }

    private BotDoing Kill(IBotWilful bot, Mobile body, Map map)
    {
        var now = Core.TickCount;

        if (_slay != null)
        {
            var doing = _slay.Advance(bot);

            if (doing.Kind is not (BotDoingKind.Done or BotDoingKind.Failed))
            {
                return doing;
            }

            var dead = _target is { Deleted: true } or { Alive: false };

            _slay.Drop(bot);
            _slay = null;
            _target = null;

            if (dead)
            {
                BotQuests.Progress(_quest, 1);
                _emptyTick = now;

                if (_quest.Fulfilled)
                {
                    BotQuests.Finish(_quest, body as BotMobile);

                    return BotDoing.Done($"killed the last of {_quest.Amount} {_quest.What} for the board");
                }
            }

            return BotDoing.Work($"{_quest.Done} of {_quest.Amount} {_quest.What} down, looking for the next");
        }

        var seek = _lairs ? Math.Max(BotQuests.LookRange, _roam + LairWithin) : BotQuests.KillRange;
        var arrive = _lairs ? LairWithin : Math.Max(4, BotQuests.KillRange / 2);

        if (now - _lookedTick >= LookMs)
        {
            _lookedTick = now;

            var near = body.InRange(_place, seek);
            var found = near || _lairs ? Nearest(body, map, near ? _place : body.Location, seek) : null;

            if (found != null)
            {
                _target = found;
                _slay = new BotSlay(found, _trains);
                _slay.Taken(bot);

                return BotDoing.Work($"setting on {found.Name}, {_quest.Done} of {_quest.Amount} down");
            }
        }

        if (!body.InRange(_place, arrive))
        {

            if (!_arrived)
            {
                _emptyTick = now;
            }

            return BotDoing.Walk(map, _place, BotArrival.Within(arrive), _lairs ? "to where they are kept" : "to the errand's ground");
        }

        _arrived = true;

        if (now - _emptyTick < BotQuests.EmptyMs)
        {
            return BotDoing.Work($"looking for {_quest.What} near ({_place.X}, {_place.Y}), {_quest.Done} of {_quest.Amount} down");
        }

        if (NextLair(body, map, out var was))
        {
            return BotDoing.Work($"no {_quest.What} at ({was.X}, {was.Y}); on to ({_place.X}, {_place.Y})");
        }

        Emptied++;

        return Fail(
            _lairs
                ? $"saw no {_quest.What} at the {_passed?.Count ?? 1} nearest places on the island that keep them"
                : $"saw no {_quest.What} within {seek} tiles of ({_place.X}, {_place.Y}) for {BotQuests.EmptyMs / 60000} minutes"
        );
    }

    private bool NextLair(Mobile body, Map map, out Point3D was)
    {
        was = _place;

        if (!_lairs)
        {
            return false;
        }

        _passed ??= [];
        _passed.Add(_place);

        if (_passed.Count >= BotQuests.MostLairs)
        {
            return false;
        }

        var next = BotLairs.Nearest(map, _quest.Type, body.Location, _passed, out var roam);

        if (next == Point3D.Zero)
        {
            return false;
        }

        _place = next;
        _roam = roam;
        _arrived = false;
        _emptyTick = Core.TickCount;
        _lookedTick = Core.TickCount - LookMs;
        Hops++;

        return true;
    }

    private BaseCreature Nearest(Mobile body, Map map, Point3D centre, int range)
    {
        BaseCreature best = null;
        var closest = int.MaxValue;

        foreach (var creature in map.GetMobilesInRange<BaseCreature>(centre, range))
        {
            if (creature is not { Deleted: false, Alive: true, Controlled: false } || !_quest.Type.IsInstanceOfType(creature))
            {
                continue;
            }

            if (!BotQuarry.Ours(body, creature) || BotQuarry.Shunned(creature))
            {
                continue;
            }

            var away = Math.Max(Math.Abs(creature.X - body.X), Math.Abs(creature.Y - body.Y));

            if (away >= closest)
            {
                continue;
            }

            closest = away;
            best = creature;
        }

        return best;
    }

    private BotDoing Gather(Mobile body, Map map)
    {
        if (!body.InRange(_quest.Where, BotQuests.DeliverReach))
        {
            return BotDoing.Walk(map, _quest.Where, BotArrival.Within(BotQuests.DeliverReach), "to hand the goods over");
        }

        var pack = body.Backpack;

        if (pack == null || pack.GetAmount(_quest.Type) < _quest.Amount)
        {
            return Fail($"no longer has {_quest.Amount} {_quest.What} to hand over");
        }

        if (!pack.ConsumeTotal(_quest.Type, _quest.Amount))
        {
            return Fail($"could not hand {_quest.Amount} {_quest.What} over");
        }

        BotQuests.Progress(_quest, _quest.Amount);
        BotQuests.Finish(_quest, body as BotMobile);

        return BotDoing.Done($"handed over {_quest.Amount} {_quest.What} for the board");
    }

    private BotDoing Scout(Mobile body, Map map)
    {
        if (!body.InRange(_quest.Where, 2))
        {
            return BotDoing.Walk(map, _quest.Where, BotArrival.Within(2), "to look at the place");
        }

        BotQuests.Progress(_quest, 1);
        BotQuests.Finish(_quest, body as BotMobile);

        return BotDoing.Done($"stood at ({_quest.Where.X}, {_quest.Where.Y}) for the board");
    }

    private BotDoing Fail(string why)
    {
        _failed = why;

        return BotDoing.Failed(why);
    }

    public override bool Bend(IBotWilful bot)
    {
        var body = bot?.Self;

        if (_slay != null)
        {
            _slay.Bend(bot);
            _slay.Drop(bot);
            _slay = null;
            _target = null;

            return true;
        }

        if (body != null && _quest.Kind == BotQuestKind.Kill && NextLair(body, _quest.Map, out _))
        {
            return true;
        }

        _failed = $"could not get to ({_place.X}, {_place.Y})";

        return false;
    }

    public override void Paused(IBotWilful bot)
    {
        _pausedTick = Core.TickCount;
        _slay?.Drop(bot);
        _slay = null;
        _target = null;
    }

    public override void Resumed(IBotWilful bot)
    {
        var now = Core.TickCount;

        _lookedTick = now - LookMs;
        _emptyTick += now - _pausedTick;
        Resumes++;

        BotQuests.Touch(_quest, bot?.Self);
    }

    public override void Drop(IBotWilful bot)
    {
        _slay?.Drop(bot);
        _slay = null;
        _target = null;

        var body = bot?.Self;

        if (_lost || body == null || !ReferenceEquals(_quest.Taker, body) || _quest.Fulfilled || !BotQuests.Standing(_quest))
        {
            return;
        }

        BotQuests.Release(_quest, _failed ?? "the taker let it go", body.Alive && _quest.Done == _doneAtTake);
    }

    public static string Describe() =>
        $"{Resumes} errands taken up again after being put down, {Hops} empty lairs left for the next, {Emptied} places given up with nothing seen";

    public static void Forget()
    {
        Resumes = 0;
        Hops = 0;
        Emptied = 0;
    }
}
