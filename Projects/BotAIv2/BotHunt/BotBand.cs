using System;
using System.Collections.Generic;
using Server.Mobiles;

namespace Server.BotAI.V2;

/// <summary>
/// Calling a company together for something one bot cannot take, and seeing it through.
///
/// <para>
/// <b>The squad machinery has been complete and unused since it was written.</b> Formation, stations,
/// scouting, the share-out, yielding a tile to whoever outranks you on it — all of it works, and
/// <c>BotSquads.Form</c> had no caller at all. Its own note says so in as many words: joining will be an
/// undertaking once somebody proposes one. This is that undertaking, and what it proposes on is the one
/// fact that makes a company worth its cost — a creature the arithmetic says one bot must refuse and four
/// bots may have.
/// </para>
///
/// <para>
/// <b>It calls, and then it stops giving orders.</b> Everything after the moment the company agrees on a
/// target belongs to the squad's own beat: where each member stands, who is anchored on whom, who swings,
/// and how the corpse is divided. This undertaking holds on only so that the fight has an owner — somebody
/// whose ledger the takings land in, and who can say the word when it is over. It sends the bot nowhere,
/// which is the whole reason it is allowed to run alongside a squad at all; see
/// <see cref="BotDeed.Alongside"/>.
/// </para>
/// </summary>
public sealed class BotBand : BotDeed
{
    public const string Trade = "band";

    public static double Prior { get; set; } = 30.0;

    public static double WorkMinutes { get; set; } = 4.0;

    private readonly BaseCreature _quarry;

    private readonly Map _map;

    private readonly Point3D _found;

    private BotSquad _squad;

    private int _called;

    private bool _engaged;

    private readonly double _together;

    public BotBand(BaseCreature quarry, double together = 0.0)
    {
        _quarry = quarry;
        _map = quarry.Map;
        _found = quarry.Location;
        _together = together;
    }

    public override double Brings(Mobile body) => System.Math.Max(BotQuad.Strength(body), _together);

    public override string Kind => Trade;

    public override bool Braves => true;

    public override bool Resumes => true;

    public override Mobile Foe => _quarry;

    public override void Taken(IBotWilful bot) => BotQuarry.Claim(bot?.Self, _quarry);

    public override Map Map => _map;

    public override Point3D Where => _found;

    public override double Expects => Prior;

    public override double Minutes => WorkMinutes;

    public override SkillName? Trains => null;

    public override int Outlay => 0;

    public override double Coin => 1.0;

    public override int Made => 0;

    public override bool Alongside => true;

    public override bool Pressing(IBotWilful bot)
    {
        var body = bot?.Self;

        return body != null && Standing() && body.InRange(_quarry.Location, BotMuster.Reach);
    }

    public override string Stage
    {
        get
        {
            if (!_engaged)
            {
                return _squad == null
                    ? $"calling a company against {_quarry?.Name ?? "something"}"
                    : $"bringing {_called} of us up to {_quarry?.Name ?? "something"}";
            }

            return $"{_called} of us on {_quarry?.Name ?? "something"}";
        }
    }

    public override BotDoing Advance(IBotWilful bot)
    {
        var body = bot?.Self;

        if (body == null || _map == null || _map == Map.Internal)
        {
            return BotDoing.Failed("no body");
        }

        if (bot is not IBotSquadMember member)
        {
            return BotDoing.Failed("not the sort of thing that joins companies");
        }

        if (!_engaged)
        {
            return _squad == null ? Calling(member, body) : Gathering(member, body);
        }

        return Fighting(member);
    }

    private BotDoing Calling(IBotSquadMember member, Mobile body)
    {
        if (!Standing())
        {
            return BotDoing.Failed("it went before anybody could be called");
        }

        if (!body.InRange(_quarry.Location, BotMuster.Reach))
        {
            return BotDoing.Failed($"{_quarry.Name} moved off before the company formed");
        }

        _squad = member.Squad ?? BotSquads.Form(member);

        if (_squad == null)
        {
            return BotDoing.Failed("could not call one together");
        }

        _near.Clear();

        foreach (var mobile in _map.GetMobilesInRange<Mobile>(body.Location, BotMuster.Reach))
        {
            if (mobile == body || mobile is not IBotSquadMember { Squad: null })
            {
                continue;
            }

            if (mobile is not IBotAlly { AbleToFight: true })
            {
                continue;
            }

            _near.Add(mobile);
        }

        _near.Sort((a, b) => body.GetDistanceToSqrt(a).CompareTo(body.GetDistanceToSqrt(b)));

        var asked = Asked(body);
        var strength = BotQuad.Strength(body);

        for (var i = 0; i < _near.Count && !BotSquads.Enough(_squad, strength, asked); i++)
        {
            if (_near[i] is not IBotSquadMember other)
            {
                continue;
            }

            var past = _squad.Count >= BotSquad.Usual;

            if (BotSquads.Enrol(_squad, other, _map, _quarry.Location, $"called to {body.Name}'s company against {_quarry.Name}"))
            {
                strength += BotThreat.Power(_near[i]);

                if (past)
                {
                    BotSquads.Past();
                }
            }
        }

        _near.Clear();

        _called = _squad.Count;

        if (_called < 2)
        {
            BotSquads.Leave(member);
            _squad = null;

            return BotDoing.Failed($"nobody was free to come at {_quarry.Name}");
        }

        if (LevyMedic(_squad, body, _map, _quarry.Location, $"called to {body.Name}'s company against {_quarry.Name}, as its medic"))
        {
            _called = _squad.Count;
        }

        var brought = Brought(_squad);

        var beyond = _squad.Beyond(_quarry);

        if (Rejudged && brought * BotThreat.Tolerance < asked || beyond)
        {
            Short++;
            BotQuarry.Shun(_quarry, beyond ? BotQuarry.HopelessMs : BotQuarry.ShunMs);

            BotSquads.Disband(_squad, $"only {_called} came against {_quarry.Name}, short of the fight");
            _squad = null;

            return BotDoing.Failed(
                beyond
                    ? $"only {_called} came against {_quarry.Name}: the company prices it at {BotSquad.Reckon(_quarry):F0}, past what {brought:F0} of strength can beat"
                    : $"only {_called} came against {_quarry.Name}: {brought:F0} of strength to the {asked:F0} it asks"
            );
        }

        BotQuarry.Claim(body, _quarry);

        if (Gathers)
        {
            _gatherTick = Core.TickCount;
            _squad.Charged = true;
            _squad.OnTheMarch = true;
            _squad.Quarry = Nothing;

            return BotDoing.Work($"bringing {_called} of us up to {_quarry.Name}");
        }

        _squad.Engage(_quarry, member);
        Engaged(_squad);

        return BotDoing.Work($"{_called} of us on {_quarry.Name}");
    }

    private BotDoing Gathering(IBotSquadMember member, Mobile body)
    {
        var squad = member.Squad;

        if (squad == null || !ReferenceEquals(squad, _squad))
        {
            Unfinished++;

            return BotDoing.Failed($"the company broke up before it came up to {_quarry?.Name ?? "it"}");
        }

        if (!Standing())
        {
            WentBefore++;

            return BotDoing.Done($"{_quarry?.Name ?? "it"} went before {_called} of us came up to it");
        }

        if (squad.Focus is { Deleted: false, Alive: true })
        {
            SetUpon++;
            Engaged(squad);

            return BotDoing.Work($"{_called} of us on {squad.Focus.Name}, {_quarry.Name} after");
        }

        if (!body.InRange(_quarry.Location, BotMuster.Reach))
        {
            MovedOff++;
            BotSquads.Disband(_squad, $"{_quarry.Name} moved off before the company came up to it");
            _squad = null;

            return BotDoing.Failed($"{_quarry.Name} moved off before the company came up to it");
        }

        if (!body.InRange(_quarry.Location, EngageTiles))
        {
            if (!_walkedUp)
            {
                _walkedUp = true;
                WalkedUp++;
            }

            return BotDoing.Walk(_map, _quarry, BotArrival.Within(EngageTiles), $"up to {_quarry.Name} with the company");
        }

        var alive = 0;
        var near = 0;
        var members = squad.Members;

        for (var i = 0; i < members.Count; i++)
        {
            if (ReferenceEquals(members[i], member) || members[i]?.Self is not { Deleted: false, Alive: true } other)
            {
                continue;
            }

            alive++;

            if (other.Map == _map && other.InRange(body.Location, GatherTiles))
            {
                near++;
            }
        }

        if (near < alive && Core.TickCount - _gatherTick < GatherMs)
        {
            return BotDoing.Work($"waiting for {alive - near} of {_called} to come up to {_quarry.Name}");
        }

        if (near >= alive)
        {
            Gathered++;
        }
        else
        {
            GatheredLate++;

            var afield = squad.Afield(body.Location, BotSquad.FieldReach);
            var reckoned = BotSquad.Reckon(_quarry);

            if (reckoned > 0.0 && reckoned > afield * BotSquad.ChoiceOdds)
            {
                GatheredShort++;
                BotQuarry.Shun(_quarry, BotQuarry.ShunMs);
                BotSquads.Disband(_squad, $"only {afield:F0} of strength came up to {_quarry.Name} in {GatherMs / 1000}s");
                _squad = null;

                return BotDoing.Failed($"only {afield:F0} of strength came up to {_quarry.Name} in {GatherMs / 1000}s, which the company prices at {reckoned:F0}");
            }
        }

        squad.Engage(_quarry, member);
        Engaged(squad);

        return BotDoing.Work($"{_called} of us on {_quarry.Name}");
    }

    private void Engaged(BotSquad squad)
    {
        _killsAtCall = squad.Kills;
        _engaged = true;
        Release(squad);
    }

    private static void Release(BotSquad squad)
    {
        if (squad == null)
        {
            return;
        }

        squad.Charged = false;
        squad.OnTheMarch = false;

        if (ReferenceEquals(squad.Quarry, Nothing))
        {
            squad.Quarry = null;
        }
    }

    private static readonly Func<Mobile, BaseCreature> Nothing = _ => null;

    public static bool Gathers { get; set; } = true;

    public static int EngageTiles { get; set; } = 10;

    public static int GatherTiles { get; set; } = 12;

    public static int GatherMs { get; set; } = 30000;

    private long _gatherTick;

    private bool _walkedUp;

    public static long WalkedUp { get; private set; }

    public static long Gathered { get; private set; }

    public static long GatheredLate { get; private set; }

    public static long GatheredShort { get; private set; }

    public static long SetUpon { get; private set; }

    public static long WentBefore { get; private set; }

    public static long MovedOff { get; private set; }

    private double Asked(Mobile body) => Asked(_map, _quarry, body);

    public static double Asked(Map map, BaseCreature quarry, Mobile body)
    {
        if (map == null || quarry == null)
        {
            return 0.0;
        }

        var total = BotThreat.Power(quarry);

        foreach (var creature in map.GetMobilesInRange<BaseCreature>(quarry.Location, AskedReach))
        {
            if (!ReferenceEquals(creature, quarry) && BotThreat.Menacing(body, creature))
            {
                total += BotThreat.Power(creature) * BotThreat.Secondary;
            }
        }

        return total;
    }

    public static int MedicReach { get; set; } = 150;

    public static bool Medics { get; set; } = true;

    public static long MedicsLevied { get; private set; }

    public static long MedicsWanting { get; private set; }

    public static bool LevyMedic(BotSquad squad, Mobile body, Map map, Point3D where, string why)
    {
        if (!Medics || squad == null || body == null || map == null || squad.Count >= squad.Ceiling)
        {
            return false;
        }

        var members = squad.Members;

        for (var i = 0; i < members.Count; i++)
        {
            if (BotFormation.RoleOf(members[i]) == BotRole.Medic)
            {
                return false;
            }
        }

        IBotSquadMember medic = null;
        var nearest = double.MaxValue;

        foreach (var mobile in map.GetMobilesInRange<Mobile>(body.Location, MedicReach))
        {
            if (mobile == body || mobile is not IBotSquadMember { Squad: null, Class.Role: BotRole.Medic } other
                || mobile is not IBotAlly { AbleToFight: true } || mobile is not { Deleted: false, Alive: true })
            {
                continue;
            }

            if (BotInfirmaries.Keeping(mobile))
            {
                continue;
            }

            var distance = body.GetDistanceToSqrt(mobile);

            if (distance < nearest)
            {
                nearest = distance;
                medic = other;
            }
        }

        if (medic != null && BotSquads.Enrol(squad, medic, map, where, why))
        {
            MedicsLevied++;

            return true;
        }

        MedicsWanting++;

        return false;
    }

    public static int AskedReach { get; set; } = 8;

    public static bool Rejudged { get; set; } = true;

    public static long Short { get; private set; }

    private static double Brought(BotSquad squad)
    {
        var sum = 0.0;
        var members = squad.Members;

        for (var i = 0; i < members.Count; i++)
        {
            if (members[i]?.Self is { Deleted: false, Alive: true } body)
            {
                sum += BotThreat.Power(body);
            }
        }

        return sum;
    }

    private static readonly List<Mobile> _near = [];

    private int _killsAtCall;

    private BotDoing Fighting(IBotSquadMember member)
    {
        var squad = member.Squad;
        var kills = _squad == null ? 0 : _squad.Kills - _killsAtCall;

        if (squad == null || !ReferenceEquals(squad, _squad))
        {
            if (kills > 0 || !Standing())
            {
                return BotDoing.Done($"the company broke up with {Down(kills)}");
            }

            Unfinished++;

            return BotDoing.Failed($"the company broke up round {_quarry?.Name ?? "it"} with nothing down");
        }

        if (!Standing())
        {
            return BotDoing.Done($"{_quarry?.Name ?? "it"} went down to {_called} of us{(kills > 1 ? $" ({kills} down in all)" : "")}");
        }

        if (ReferenceEquals(squad.Focus, _quarry))
        {
            BotQuarry.Claim(member.Self, _quarry);

            return BotDoing.Work($"{_called} of us on {_quarry.Name}");
        }

        if (ReferenceEquals(squad.Broken, _quarry))
        {
            Unfinished++;

            return BotDoing.Failed($"the company broke off {_quarry.Name}: {squad.BrokenWhy}{(kills > 0 ? $" ({Down(kills)})" : "")}");
        }

        if (squad.Bleeding(_quarry))
        {
            PulledBack++;
            BotQuarry.Shun(_quarry, BotQuarry.ShunMs);

            return BotDoing.Failed($"the company pulled back from {_quarry.Name} after losing {BotSquad.BleedDeaths} inside a minute{(kills > 0 ? $" ({Down(kills)})" : "")}");
        }

        if (squad.Focus is { Deleted: false, Alive: true } other)
        {
            if (!ReferenceEquals(_aside, other))
            {
                _aside = other;
                Turned++;
            }

            BotQuarry.Claim(member.Self, _quarry);

            return BotDoing.Work($"{_called} of us on {other.Name}, {_quarry.Name} after");
        }

        if (member.Self is { } body && body.InRange(_quarry.Location, BotMuster.Reach) && !BotQuarry.Shunned(_quarry))
        {
            Returned++;
            squad.Engage(_quarry, member, true);
            BotQuarry.Claim(body, _quarry);

            return BotDoing.Work($"{_called} of us back on {_quarry.Name}");
        }

        if (kills > 0)
        {
            return BotDoing.Done($"{Down(kills)}; {_quarry.Name} got away");
        }

        Unfinished++;

        return BotDoing.Failed($"{_quarry.Name} got away with nothing down");
    }

    private static string Down(int kills) => kills == 1 ? "1 down" : $"{kills} down";

    private Mobile _aside;

    public static long Turned { get; private set; }

    public static long Returned { get; private set; }

    public static long Unfinished { get; private set; }

    public static long PulledBack { get; private set; }

    public static string Describe() =>
        $"bands: {Turned} turns of a band's company to something else attacking it while the band held, {Returned} turned back to their quarry after, {Unfinished} ended with their quarry standing and nothing down, {PulledBack} ended because the company pulled back from the quarry after losing {BotSquad.BleedDeaths} inside a minute, {Short} not engaged because the company that came fell short of the fight ({(Rejudged ? "judged after the levy" : "off")}); "
        + $"{WalkedUp} companies walked up to their quarry before engaging ({(Gathers ? "on" : "off")}): {Gathered} engaged with everybody within {GatherTiles} tiles of the leader, {GatheredLate} on the {GatherMs / 1000}s clock with members still behind ({GatheredShort} of them let go for the strength come up being short at ×{BotSquad.ChoiceOdds:F1}), {SetUpon} set upon on the way, {WentBefore} found the quarry gone and {MovedOff} gone off past {BotMuster.Reach} tiles; "
        + $"{MedicsLevied} medics levied into a company that had none and {MedicsWanting} companies that found no free healer within {MedicReach} tiles";

    public static void Forget()
    {
        Turned = 0;
        Returned = 0;
        Unfinished = 0;
        Short = 0;
        WalkedUp = 0;
        Gathered = 0;
        GatheredLate = 0;
        GatheredShort = 0;
        PulledBack = 0;
        SetUpon = 0;
        WentBefore = 0;
        MovedOff = 0;
        MedicsLevied = 0;
        MedicsWanting = 0;
    }

    public override void Drop(IBotWilful bot)
    {
        BotQuarry.Release(_quarry);

        if (_squad is { Disbanded: false })
        {
            Release(_squad);
        }
    }

    private bool Standing() =>
        _quarry is { Deleted: false, Alive: true } && _quarry.Map == _map;
}
