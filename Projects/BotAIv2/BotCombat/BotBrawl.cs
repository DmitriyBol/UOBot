using System;
using Server.Mobiles;

namespace Server.BotAI.V2;

/// <summary>
/// A fight with one of our own — a duel, a robbery, a manhunt — as a piece of work: close, hit, and stop when told.
///
/// <para>
/// <b>Every fight on this shard until 15.09.2026 was against a creature.</b> <c>BotSlay</c> takes a
/// <c>BaseCreature</c>, <c>BotThreat</c> counts only creatures, and a bot hit by another bot swung back by reflex
/// and otherwise carried on with its errand. Patrick's orders for the night — a championship between the strongest
/// bots, murderers who kill and rob, a Baron's patrol that catches them — all need a bot to go after a bot on
/// purpose, and this is the one undertaking that does it. The fighting itself is still the engine's: <c>Combatant</c>
/// and <c>Warmode</c> start its swing timer, and a caster throws what <c>BotStrike</c> says it can.
/// </para>
///
/// <para>
/// <b>It ends when whoever set it up says so, not when the other side is dead.</b> A duel is over at a share of
/// health; a robbery at a corpse; a manhunt at a catch. So the caller hands in the rule — a function answering with
/// the reason the fight is over, or nothing while it goes on — and the deed asks it every beat before it swings.
/// Nothing here decides whether a bot may fight another; that is the caller's, and the engine's notoriety rules
/// underneath (see <c>BotMobile.IsHarmfulCriminal</c>).
/// </para>
///
/// <para>
/// Committed and summoning: it is taken by being pressed on a bot rather than won at auction, and no better offer
/// takes the bot off it. Unpaid, so the earnings veto lets it through; braves lethal ground, because the duel's ring
/// and the robber's victim are wherever they are.
/// </para>
/// </summary>
public sealed class BotBrawl : BotDeed
{
    public static double Prior { get; set; } = 200.0;

    public static double WorkMinutes { get; set; } = 3.0;

    public static int CapMs { get; set; } = 300000;

    public static int LostMs { get; set; } = 30000;

    public static int ChaseMs { get; set; } = 90000;

    public static int HuntMs { get; set; } = 600000;

    public static long Duels { get; private set; }

    public static long Robberies { get; private set; }

    public static long Manhunts { get; private set; }

    public static long Defences { get; private set; }

    public static long Capped { get; private set; }

    public static long Lost { get; private set; }

    public const string Duel = "duel";

    public const string Robbery = "rob";

    public const string Manhunt = "manhunt";

    public const string Defence = "defend";

    private readonly Mobile _foe;

    private readonly Map _map;

    private readonly string _kind;

    private readonly Func<IBotWilful, BotBrawl, string> _over;

    private readonly SkillName _trains;

    private readonly long _began;

    private long _seenTick;

    private bool _aiming;

    private bool _cast;

    private long _castTick;

    private int _casts;

    private Point3D _found;

    private Point3D _aim;

    public static int Restride { get; set; } = 6;

    public BotBrawl(Mobile foe, string kind, SkillName trains, Func<IBotWilful, BotBrawl, string> over)
    {
        _foe = foe;
        _map = foe?.Map;
        _kind = kind ?? Duel;
        _trains = trains;
        _over = over;
        _began = Core.TickCount;
        _seenTick = _began;
        _found = foe?.Location ?? Point3D.Zero;
    }

    private bool _counted;

    private void Count()
    {
        if (_counted)
        {
            return;
        }

        _counted = true;

        switch (_kind)
        {
            case Duel:
                Duels++;

                break;

            case Robbery:
                Robberies++;

                break;

            case Manhunt:
                Manhunts++;

                break;

            default:
                Defences++;

                break;
        }
    }

    public long Elapsed => Core.TickCount - _began;

    public int Casts => _casts;

    public override string Kind => _kind;

    public override bool Committed => true;

    public override bool Steadfast => true;

    public override bool Summons => true;

    public override bool Braves => true;

    public override bool Unpaid => true;

    public override Mobile Foe => _foe;

    public override Map Map => _map;

    public override Point3D Where => _foe is { Deleted: false } ? _foe.Location : _found;

    public override double Expects => Prior;

    public override double Minutes => WorkMinutes;

    public override SkillName? Trains => _trains;

    public override int Outlay => 0;

    public override double Coin => 0.0;

    public override int Made => 0;

    public override string Stage => $"{_kind}: fighting {_foe?.Name ?? "somebody"} ({_casts} casts, {Elapsed / 1000}s)";

    public override BotDoing Advance(IBotWilful bot)
    {
        var body = bot?.Self;

        if (body == null || _map == null || _map == Map.Internal)
        {
            return BotDoing.Failed("no body");
        }

        Count();

        if (_foe is not { Deleted: false } || _foe.Map != _map)
        {
            Quiet(body);

            return BotDoing.Done($"{_foe?.Name ?? "the foe"} is gone");
        }

        var verdict = _over?.Invoke(bot, this);

        if (verdict != null)
        {
            Quiet(body);

            return BotDoing.Done(verdict);
        }

        if (!_foe.Alive)
        {
            Quiet(body);

            return BotDoing.Done($"{_foe.Name} is down");
        }

        var cap = _kind == Manhunt ? HuntMs : CapMs;

        if (Core.TickCount - _began >= cap)
        {
            Capped++;
            Quiet(body);

            return BotDoing.Done($"the {_kind} ran to its cap");
        }

        _found = _foe.Location;

        var casting = BotStrike.Can(body);
        var armed = casting && BotStrike.Best(body) >= 0;
        var reach = Math.Max(body.Weapon?.MaxRange ?? 1, armed ? BotStrike.Range : 0);

        if (!body.InRange(_foe.Location, reach) || !body.InLOS(_foe))
        {
            var lost = _kind == Manhunt ? HuntMs : _kind == Robbery ? ChaseMs : LostMs;

            if (Core.TickCount - _seenTick >= lost)
            {
                Lost++;
                Quiet(body);

                return BotDoing.Done($"{_foe.Name} could not be reached for {lost / 1000}s");
            }

            if (_aim == Point3D.Zero || !Utility.InRange(_foe.Location, _aim, Restride))
            {
                _aim = _foe.Location;
            }

            return BotDoing.Walk(_map, _aim, BotArrival.Within(Math.Max(1, reach - 1)), $"after {_foe.Name}");
        }

        _seenTick = Core.TickCount;

        body.Warmode = true;
        body.Combatant = _foe;

        if (!casting)
        {
            return BotDoing.Work($"fighting {_foe.Name}");
        }

        if (_aiming && body.Target != null)
        {
            _aiming = false;

            if (BotStrike.Aim(body, _foe))
            {
                _casts++;
            }

            return BotDoing.Work($"casting at {_foe.Name}");
        }

        if (body.Spell != null)
        {
            return BotDoing.Work("casting");
        }

        if (_cast && Core.TickCount - _castTick < BotStrike.CastMs)
        {
            return BotDoing.Work($"between spells at {_foe.Name}");
        }

        var spell = BotStrike.Best(body);

        if (spell >= 0 && BotStrike.Begin(body, spell))
        {
            _aiming = true;
            _cast = true;
            _castTick = Core.TickCount;

            return BotDoing.Work($"casting at {_foe.Name}");
        }

        return BotDoing.Work($"fighting {_foe.Name}");
    }

    private void Quiet(Mobile body)
    {
        if (body != null && ReferenceEquals(body.Combatant, _foe))
        {
            body.Combatant = null;
            body.Warmode = false;
        }
    }

    public override void Drop(IBotWilful bot) => Quiet(bot?.Self);

    public static string Describe() =>
        Duels + Robberies + Manhunts + Defences == 0
            ? "no bot has fought another on purpose"
            : $"{Duels} duels, {Robberies} robberies, {Manhunts} manhunts and {Defences} defences fought bot against bot, {Capped} ran to the cap and {Lost} lost their foe";

    public static void Forget()
    {
        Duels = 0;
        Robberies = 0;
        Manhunts = 0;
        Defences = 0;
        Capped = 0;
        Lost = 0;
    }
}
