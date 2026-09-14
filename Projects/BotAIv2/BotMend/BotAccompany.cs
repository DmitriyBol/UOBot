using System.Collections.Generic;
using Server.Mobiles;

namespace Server.BotAI.V2;

/// <summary>
/// A healer standing by one of ours who is fighting, close enough to bind a wound the moment there is one.
///
/// <para>
/// <b>What a healer does while nobody is hurt yet.</b> Measured on the morning of 14.09.2026: healers spent 26% of
/// their working minutes looking for a fight and 5% in one, against 10% on their own trade. A medic that goes
/// looking for fights is a fifth fighter with a green staff, and one that stays at home is a healer nobody can reach.
/// The mending itself was never missing — <c>BotSurgeon</c> offers a wound to whoever can see it — what was missing
/// was a healer anywhere near the wounds. Games that ship a support role give it a place beside somebody rather than a
/// quarry of its own: Horizon Zero Dawn's herds hand out roles, Halo's squads hand out styles. This is the same shape
/// without an order in it: the healer weighs standing by a hunter against everything else it could do.
/// </para>
///
/// <para>
/// <b>Put down for the wound, taken up again after it.</b> Steadfast, so a salve — a summons — pauses this instead of
/// ending it, and when the wound is bound the healer walks back to its fighter. A support role is a string of
/// interruptions, and that is exactly the case the pause was built for: each wound must not be a new decision about
/// where to be.
/// </para>
///
/// <para>
/// Paid nothing by itself — the skill arrives through the mending it makes possible and is booked there — so it is
/// unpaid, and its proposer is its only gate: a healer, fit, with cloth or a heal and the herbs for it, a fighter of
/// ours actually engaged within reach, and nobody else already standing by that fighter.
/// </para>
/// </summary>
public sealed class BotAccompany : BotDeed
{
    public const string Trade = "escort";

    public static double Prior { get; set; } = 45.0;

    public static int LeaseMs { get; set; } = 300000;

    public static int Stay { get; set; } = 5;

    public static int GraceMs { get; set; } = 20000;

    public static int ClaimMs { get; set; } = 15000;

    public static int MeansEveryMs { get; set; } = 5000;

    public static int KeepUp { get; set; } = 6;

    public static long Stood { get; private set; }

    public static long Over { get; private set; }

    public static long Leased { get; private set; }

    public static long Lost { get; private set; }

    public static long Spent { get; private set; }

    public static long Lagged { get; private set; }

    private static readonly Dictionary<Serial, (Serial Healer, long Tick)> _escorted = [];

    private readonly BotMobile _companion;

    private readonly Map _map;

    private readonly long _began;

    private long _quietTick;

    private long _meansTick;

    private int _lagged;

    private bool _standing;

    public BotAccompany(BotMobile companion, Map map)
    {
        _companion = companion;
        _map = map;
        _began = Core.TickCount;
        _quietTick = _began;

        _meansTick = _began - MeansEveryMs;
    }

    public override string Kind => Trade;

    public override bool Steadfast => true;

    public override bool Unpaid => true;

    public override bool Still => _standing;

    public override Map Map => _map;

    public override Point3D Where => _companion?.Location ?? Point3D.Zero;

    public override double Expects => Prior;

    public override double Minutes => LeaseMs / 60000.0;

    public override SkillName? Trains => SkillName.Healing;

    public override int Outlay => 0;

    public override double Coin => 0.0;

    public override int Made => 0;

    public override string Stage =>
        _standing ? $"standing by {_companion?.Name}" : $"on the way to stand by {_companion?.Name}";

    public static bool Engaged(BotMobile m) =>
        m is { Deleted: false, Alive: true }
        && (m.Combatant is { Deleted: false, Alive: true }
            || m.Squad is { Stance: BotSquadStance.Fighting }
            || m.Resolve?.Deed?.Kind is BotSlay.Trade or BotRescue.Trade or BotRally.Trade or BotQuarrel.Trade);

    public static bool Means(Mobile healer) =>
        BotMend.Cloth(healer) > 0 || BotMend.Herbs(healer, BotArsenal.SpellHeal);

    public static bool Taken(Mobile companion, Mobile healer)
    {
        if (companion == null || !_escorted.TryGetValue(companion.Serial, out var claim))
        {
            return false;
        }

        if (Core.TickCount - claim.Tick >= ClaimMs)
        {
            _escorted.Remove(companion.Serial);

            return false;
        }

        return claim.Healer != healer.Serial;
    }

    public override BotDoing Advance(IBotWilful bot)
    {
        var body = bot?.Self;

        if (body == null || _map == null || _map == Map.Internal)
        {
            return BotDoing.Failed("no body");
        }

        var name = _companion?.Name ?? "the fighter";

        if (_companion is not { Deleted: false, Alive: true } || _companion.Map != _map)
        {
            Lost++;

            return BotDoing.Done($"{name} is gone");
        }

        var now = Core.TickCount;

        if (now - _began >= LeaseMs)
        {
            Leased++;

            return BotDoing.Done($"stood by {name} for {LeaseMs / 60000} minutes");
        }

        if (now - _meansTick >= MeansEveryMs)
        {
            _meansTick = now;

            if (!Means(body))
            {
                Spent++;

                return BotDoing.Failed("nothing to heal with");
            }
        }

        _escorted[_companion.Serial] = (body.Serial, now);

        if (Engaged(_companion))
        {
            _quietTick = now;
        }
        else if (now - _quietTick >= GraceMs)
        {
            Over++;

            return BotDoing.Done($"{name}'s fight is over");
        }

        if (!body.InRange(_companion, Stay))
        {
            _standing = false;

            return BotDoing.Walk(_map, _companion, BotArrival.Within(Stay - 1), $"to stand by {name}");
        }

        _standing = true;
        Stood++;

        return BotDoing.Work($"standing by {name}");
    }

    public override bool Bend(IBotWilful bot)
    {
        if (++_lagged > KeepUp)
        {
            return false;
        }

        Lagged++;
        _standing = false;

        return true;
    }

    public override void Resumed(IBotWilful bot)
    {
        _quietTick = Core.TickCount;
        _standing = false;
    }

    public override void Drop(IBotWilful bot)
    {
        if (_companion != null && bot?.Self is { } body
            && _escorted.TryGetValue(_companion.Serial, out var claim) && claim.Healer == body.Serial)
        {
            _escorted.Remove(_companion.Serial);
        }
    }

    public static void Forget()
    {
        Stood = 0;
        Over = 0;
        Leased = 0;
        Lost = 0;
        Spent = 0;
        Lagged = 0;
        _escorted.Clear();
    }
}

/// <summary>
/// Offers a healer the nearest fighter of ours who is in a fight and has nobody standing by it.
///
/// <para>
/// <b>Healers only, and counted only for healers.</b> Every other bot is turned away in one comparison and is not
/// tallied, because a tally of seventy refusals a review per healer would bury the eleven questions that mean
/// something. The rest of the gates are the errand's own reasons for ending, asked first so that the offer is never
/// an errand that fails on its first beat.
/// </para>
/// </summary>
public sealed class BotAttendant : IBotProposer
{
    public static int Reach { get; set; } = 60;

    public static long Asked { get; private set; }

    public static long Offered { get; private set; }

    public static long Held { get; private set; }

    public static long Unfit { get; private set; }

    public static long Pressed { get; private set; }

    public static long Bare { get; private set; }

    public static long Nobody { get; private set; }

    public string Name => "Attendant";

    public BotStanding Rung => BotStanding.Free;

    public BotDeed Propose(IBotWilful bot)
    {
        if (bot?.Self is not BotMobile { Deleted: false, Alive: true } body || body.Class?.Role != BotRole.Medic)
        {
            return null;
        }

        var map = body.Map;

        if (map == null || map == Map.Internal)
        {
            return null;
        }

        Asked++;

        if (body.Squad != null)
        {
            Held++;

            return null;
        }

        if (body.HitsMax <= 0 || body.Hits < body.HitsMax * BotHunter.FitAt)
        {
            Unfit++;

            return null;
        }

        if (BotThreat.Hunter(body, BotDefender.Reach) != null)
        {
            Pressed++;

            return null;
        }

        if (!BotAccompany.Means(body))
        {
            Bare++;

            return null;
        }

        var companion = Nearest(body, map);

        if (companion == null)
        {
            Nobody++;

            return null;
        }

        Offered++;

        return new BotAccompany(companion, map);
    }

    private static BotMobile Nearest(BotMobile body, Map map)
    {
        BotMobile best = null;
        var bestAway = double.MaxValue;

        foreach (var m in map.GetMobilesInRange<BotMobile>(body.Location, Reach))
        {
            if (m == body || m.Class?.Role == BotRole.Medic || !BotAccompany.Engaged(m) || BotAccompany.Taken(m, body)
                || BotRegard.AtWar(body, m))
            {
                continue;
            }

            var away = body.GetDistanceToSqrt(m);

            if (away >= bestAway)
            {
                continue;
            }

            if (BotReach.Ask(map, body.Location, m.Location, BotArrival.Within(BotAccompany.Stay)) == BotReachVerdict.Sealed)
            {
                continue;
            }

            best = m;
            bestAway = away;
        }

        return best;
    }

    public static string Describe() =>
        Asked == 0
            ? "no healer has been offered anybody to stand by"
            : $"{Asked} times a healer was asked: {Offered} sent to stand by a fighter, {Held} in a company, {Unfit} too hurt, {Pressed} with something on it, {Bare} with nothing to heal with, {Nobody} with nobody of ours fighting within {Reach} tiles; {BotAccompany.Stood} beats stood by, {BotAccompany.Lagged} times a healer fell behind and walked on; stints ended {BotAccompany.Over} when the fight was over, {BotAccompany.Leased} at the end of the lease, {BotAccompany.Lost} when the fighter was gone, {BotAccompany.Spent} with nothing left to heal with";

    public static void Forget()
    {
        Asked = 0;
        Offered = 0;
        Held = 0;
        Unfit = 0;
        Pressed = 0;
        Bare = 0;
        Nobody = 0;

        BotAccompany.Forget();
    }
}
