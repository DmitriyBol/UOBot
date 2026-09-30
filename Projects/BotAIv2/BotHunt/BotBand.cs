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
                return $"calling a company against {_quarry?.Name ?? "something"}";
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
            return Calling(member, body);
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

        foreach (var mobile in _map.GetMobilesInRange<Mobile>(body.Location, BotMuster.Reach))
        {
            if (_squad.Count >= _squad.Ceiling)
            {
                break;
            }

            if (mobile == body || mobile is not IBotSquadMember { Squad: null } other)
            {
                continue;
            }

            if (mobile is not IBotAlly { AbleToFight: true })
            {
                continue;
            }

            BotSquads.Join(_squad, other);
        }

        _called = _squad.Count;

        if (_called < 2)
        {
            BotSquads.Leave(member);
            _squad = null;

            return BotDoing.Failed($"nobody was free to come at {_quarry.Name}");
        }

        _squad.Engage(_quarry, member);

        BotQuarry.Claim(body, _quarry);

        _engaged = true;

        return BotDoing.Work($"{_called} of us on {_quarry.Name}");
    }

    private BotDoing Fighting(IBotSquadMember member)
    {
        var squad = member.Squad;

        if (squad == null || !ReferenceEquals(squad, _squad))
        {
            return BotDoing.Done($"the company broke up around {_quarry?.Name ?? "it"}");
        }

        if (!Standing())
        {
            return BotDoing.Done($"{_quarry?.Name ?? "it"} went down to {_called} of us");
        }

        if (!ReferenceEquals(squad.Focus, _quarry))
        {
            return BotDoing.Done($"the company broke off {_quarry?.Name ?? "it"}");
        }

        BotQuarry.Claim(member.Self, _quarry);

        return BotDoing.Work($"{_called} of us on {_quarry.Name}");
    }

    public override void Drop(IBotWilful bot) => BotQuarry.Release(_quarry);

    private bool Standing() =>
        _quarry is { Deleted: false, Alive: true } && _quarry.Map == _map;
}
