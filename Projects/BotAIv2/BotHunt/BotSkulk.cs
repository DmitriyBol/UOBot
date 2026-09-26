using System;
using System.Collections.Generic;
using Server.Logging;

namespace Server.BotAI.V2;

/// <summary>
/// Practising being unseen: hiding again and again where it stands, and once it hides well enough for the engine to allow
/// it, moving a few quiet steps at a time.
///
/// <para>
/// <b>Patrick's order of 17.09.2026: criminals train their stealth.</b> A fighter tempted to rob that cannot hide yet
/// (<see cref="BotShadow.RobHiding"/>) is pressed into this instead of a robbery — his choice, over robbing openly — and a
/// bot that has ever practised is pressed back into it every <see cref="EveryMs"/>. Its Hiding above nought is the only mark
/// a crook carries that outlives a restart, because <see cref="BotProgress"/> keeps every skill a bot has any of. The rate is
/// the engine's own: a use every ten seconds, a tenth of a point when the gain roll comes up, which is some six or seven hours
/// of practice from nothing to the eighty that Stealth wants.
/// </para>
/// </summary>
public sealed class BotSkulk : BotDeed
{
    private static readonly ILogger logger = LogFactory.GetLogger(typeof(BotSkulk));

    public const string Trade = "skulk";

    public static int StintMs { get; set; } = 300000;

    public static int EveryMs { get; set; } = 1800000;

    public static int Apprentices { get; set; }

    public static int EagerEveryMs { get; set; } = 300000;

    private const int MostApprentices = 32;

    public static int Wander { get; set; } = 4;

    public static double Prior { get; set; } = 400.0;

    public static long Stints { get; private set; }

    public static long Ended { get; private set; }

    public static long Urged { get; private set; }

    public static long Eager { get; private set; }

    public static int WaitMs { get; set; } = 600000;

    public static long Waited { get; private set; }

    private static readonly HashSet<Serial> _waiting = [];

    public static double HidingGained { get; private set; }

    public static double StealthGained { get; private set; }

    private static readonly Dictionary<Serial, long> _urgedTick = [];

    private static readonly HashSet<Serial> _apprentices = [];

    private static readonly Serial[] _best = new Serial[MostApprentices];

    private static readonly double[] _bestAt = new double[MostApprentices];

    private readonly Map _map;

    private readonly Point3D _where;

    private bool _started;

    private long _began;

    private double _hiding;

    private double _stealth;

    private Point3D _aim = Point3D.Zero;

    public BotSkulk(Map map, Point3D where)
    {
        _map = map;
        _where = where;
    }

    public override string Kind => Trade;

    public override bool Committed => true;

    public override bool Unpaid => true;

    public override bool Still => true;

    public override bool Repeats(BotDeed other) => other is BotSkulk;

    public override bool Hurries => false;

    public override bool Afoot => true;

    public override Map Map => _map;

    public override Point3D Where => _where;

    public override double Expects => Prior;

    public override double Minutes => StintMs / 60000.0;

    public override SkillName? Trains => SkillName.Hiding;

    public override int Outlay => 0;

    public override double Coin => 0.0;

    public override int Made => 0;

    public override string Stage => "practising hiding";

    public override BotDoing Advance(IBotWilful bot)
    {
        var body = bot?.Self;

        if (body == null || _map == null || body.Map != _map)
        {
            return BotDoing.Failed("no body");
        }

        var now = Core.TickCount;

        if (!_started && !body.InRange(_where, 8))
        {
            return BotDoing.Walk(_map, _where, BotArrival.Within(4), "to practise out of sight");
        }

        if (!_started)
        {
            _started = true;
            _began = now;
            _hiding = body.Skills.Hiding.Base;
            _stealth = body.Skills.Stealth.Base;
            _urgedTick[body.Serial] = now;
            Stints++;
        }

        if (now - _began >= StintMs)
        {
            return Finish(body);
        }

        var quietly = BotShadow.MayStealth(body);

        if (quietly && body.Mounted)
        {
            BotStable.Alight(body);
        }

        if (quietly && body.Hidden && body.AllowedStealthSteps > 0)
        {
            if (_aim == Point3D.Zero || body.InRange(_aim, 0))
            {
                _aim = Aim();
            }

            if (_aim != Point3D.Zero)
            {
                return BotDoing.Walk(_map, _aim, BotArrival.Within(0), "moving quietly");
            }
        }

        if (!BotShadow.Ready(body))
        {
            return BotDoing.Work("keeping still");
        }

        if (quietly && body.Hidden)
        {
            BotShadow.Quiet(body);
            _aim = Point3D.Zero;

            return BotDoing.Work("trying to move quietly");
        }

        BotShadow.Hide(body);

        return BotDoing.Work("trying to hide");
    }

    public override void Drop(IBotWilful bot)
    {
        if (bot?.Self is { Hidden: true } body)
        {
            body.RevealingAction();
        }
    }

    private BotDoing Finish(Mobile body)
    {
        Ended++;

        var hiding = body.Skills.Hiding.Base;
        var stealth = body.Skills.Stealth.Base;

        HidingGained += Math.Max(0.0, hiding - _hiding);
        StealthGained += Math.Max(0.0, stealth - _stealth);

        if (body.Hidden)
        {
            body.RevealingAction();
        }

        logger.Information(
            "{Name} practised hiding for {Minutes} minutes: Hiding {From:F1} to {To:F1}, Stealth {StealthFrom:F1} to {StealthTo:F1}",
            body.Name,
            StintMs / 60000,
            _hiding,
            hiding,
            _stealth,
            stealth
        );

        return BotDoing.Done($"practised hiding: Hiding {hiding:F1}, Stealth {stealth:F1}");
    }

    private Point3D Aim()
    {
        for (var i = 0; i < 8; i++)
        {
            var x = _where.X + Utility.RandomMinMax(-Wander, Wander);
            var y = _where.Y + Utility.RandomMinMax(-Wander, Wander);
            var z = _map.GetAverageZ(x, y);

            if (_map.CanSpawnMobile(x, y, z))
            {
                return new Point3D(x, y, z);
            }
        }

        return Point3D.Zero;
    }

    public static void Urge(BotMobile body, long now)
    {
        if (!BotShadow.Running || EveryMs <= 0 || body is not { Deleted: false, Alive: true, Fallen: false } || body.Skills.Hiding.Base <= 0.0)
        {
            return;
        }

        if (BotFence.Is(body))
        {
            return;
        }

        var hiding = body.Skills.Hiding;
        var stealth = body.Skills.Stealth;

        if (hiding.Base >= hiding.Cap && (!BotShadow.MayStealth(body) || stealth.Base >= stealth.Cap))
        {
            return;
        }

        if (!_urgedTick.TryGetValue(body.Serial, out var last))
        {
            _urgedTick[body.Serial] = now;

            return;
        }

        var apprentice = _apprentices.Contains(body.Serial);
        var every = EveryMs;

        if (BotUnderworld.Member(body))
        {
            every = BotUnderworld.MemberPracticeEveryMs;
        }
        else if (apprentice)
        {
            every = EagerEveryMs;
        }

        if (now - last < every)
        {
            return;
        }

        if (body.Resolve?.Deed != null && now - last < every + WaitMs)
        {
            _waiting.Add(body.Serial);

            return;
        }

        _urgedTick[body.Serial] = now;

        if (body.Map is not { } map || map == Map.Internal || body.Squad != null || BotDuel.Duelling(body) || BotOutlaw.Jailed(body)
            || body.Resolve?.Deed is BotSkulk or BotRob or BotLieLow or BotBrawl or BotManhunt or BotSentence)
        {
            return;
        }

        if (BotLadder.Standing(body) is not (BotStanding.Free or BotStanding.Busy))
        {
            return;
        }

        var where = BotUnderworld.Member(body) && BotUnderworld.Hideout != Point3D.Zero ? BotUnderworld.Hideout : body.Location;

        var waited = _waiting.Remove(body.Serial) && body.Resolve?.Deed == null;

        if (BotWill.Press(body, new BotSkulk(map, where), apprentice ? "practising in earnest" : "the itch to practise hiding"))
        {
            Urged++;

            if (waited)
            {
                Waited++;
            }

            if (apprentice)
            {
                Eager++;
            }
        }
    }

    public static void Rank(IReadOnlyList<BotMobile> bots)
    {
        if (bots == null)
        {
            return;
        }

        _apprentices.Clear();

        var want = Math.Clamp(Apprentices, 0, MostApprentices);

        if (!BotShadow.Running || want == 0)
        {
            return;
        }

        var held = 0;

        for (var i = 0; i < bots.Count; i++)
        {
            var body = bots[i];

            if (!Fits(body))
            {
                continue;
            }

            var hiding = body.Skills.Hiding.Base;

            if (hiding <= 0.0 || hiding >= BotShadow.RobHiding)
            {
                continue;
            }

            int at;

            if (held < want)
            {
                at = held++;
            }
            else if (hiding > _bestAt[want - 1])
            {
                at = want - 1;
            }
            else
            {
                continue;
            }

            while (at > 0 && _bestAt[at - 1] < hiding)
            {
                _bestAt[at] = _bestAt[at - 1];
                _best[at] = _best[at - 1];
                at--;
            }

            _bestAt[at] = hiding;
            _best[at] = body.Serial;
        }

        for (var i = 0; i < held; i++)
        {
            _apprentices.Add(_best[i]);
        }
    }

    private static bool Fits(BotMobile body)
    {
        if (body is not { Deleted: false, Alive: true, Fallen: false } || body.Class is not { } klass)
        {
            return false;
        }

        if (klass.Role is BotRole.Producer or BotRole.Medic || klass.Unpaid || klass.Leads)
        {
            return false;
        }

        return !BotOutlaw.Jailed(body) && !BotUnderworld.Member(body);
    }

    public static string Describe() =>
        $"{Stints} stints of practising hiding ({Urged} of them from the itch: {Waited} of those begun between two pieces of work after waiting for the one in hand, {Eager} an apprentice's; {_waiting.Count} itches waiting now, {_apprentices.Count} apprentices named), {Ended} run to the end, gaining {HidingGained:F1} Hiding and {StealthGained:F1} Stealth between them";

    public static void Forget()
    {
        Stints = 0;
        Ended = 0;
        Urged = 0;
        Waited = 0;
        _waiting.Clear();
        Eager = 0;
        HidingGained = 0;
        StealthGained = 0;
        _urgedTick.Clear();
        _apprentices.Clear();
    }
}
