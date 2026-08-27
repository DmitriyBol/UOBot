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
    /// <summary>The ledger's key.</summary>
    public const string Trade = "band";

    /// <summary>
    /// What fighting something in company is reckoned at per minute before experience corrects it.
    ///
    /// <para>
    /// Above a lone hunt's sixty, because the whole point of the thing is that it is bigger game: what a
    /// company can take is what a bot has been walking past all evening, and a creature that needs four
    /// bots carries a purse that reflects it. Not far above, though — the takings are divided, and a prior
    /// that promised each member a whole ogre would have the population forming companies for the arithmetic
    /// rather than for the ogre. The ledger settles it either way within a few fights, from what actually
    /// arrives in the pack.
    /// </para>
    /// </summary>
    public static double Prior { get; set; } = 90.0;

    /// <summary>How long one of these is expected to take, calling and fighting together.</summary>
    public static double WorkMinutes { get; set; } = 4.0;

    private readonly BaseCreature _quarry;

    private readonly Map _map;

    private readonly Point3D _found;

    private BotSquad _squad;

    private int _called;

    private bool _engaged;

    public BotBand(BaseCreature quarry)
    {
        _quarry = quarry;
        _map = quarry.Map;
        _found = quarry.Location;
    }

    public override string Kind => Trade;

    public override Map Map => _map;

    /// <summary>Where it was found, so the ledger learns about the ground rather than about the chase.</summary>
    public override Point3D Where => _found;

    public override double Expects => Prior;

    public override double Minutes => WorkMinutes;

    /// <summary>
    /// Nothing, and that is deliberate.
    ///
    /// The blows a member lands are the engine's and train whatever it swings, exactly as they would on any
    /// other fight — that credit is real and arrives without anybody claiming it here. Naming a skill on
    /// this as well would pay the caller twice for one fight and make calling companies a way of training.
    /// </summary>
    public override SkillName? Trains => null;

    public override int Outlay => 0;

    /// <summary>Mostly coin: a share of what came off the corpse.</summary>
    public override double Coin => 1.0;

    public override int Made => 0;

    /// <summary>The one piece of work in the project that goes on while its bot is in a squad.</summary>
    public override bool Alongside => true;

    /// <summary>
    /// A company is worth calling now or not at all: the creature is standing there, and the bots who would
    /// come are standing there too. Half a minute later it is one bot's problem again.
    /// </summary>
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

    /// <summary>
    /// The whole of the calling, in one beat.
    ///
    /// <para>
    /// <b>It has to be one beat, and that is not tidiness.</b> The instant a squad exists its founder is on
    /// the <c>Bound</c> rung, where the squad owns the bot — so a calling spread over several beats would be
    /// frozen by the company it had half finished assembling. Everybody who is going to come is standing
    /// within sight already, which is the condition the proposer offered this on, so there is nothing to
    /// wait for.
    /// </para>
    /// </summary>
    private BotDoing Calling(IBotSquadMember member, Mobile body)
    {
        if (!Standing())
        {
            return BotDoing.Failed("it went before anybody could be called");
        }

        // It wandered off while the auction was being run. Chasing it would be a lone bot walking at
        // something it has already worked out it cannot take.
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

            // Only those who could actually take part. A bot on its last two hit points is not
            // reinforcement, and counting it is how the first version formed companies that disbanded in the
            // tick they were announced.
            if (mobile is not IBotAlly { AbleToFight: true })
            {
                continue;
            }

            BotSquads.Join(_squad, other);
        }

        _called = _squad.Count;

        if (_called < 2)
        {
            // Nobody came. The squad would disband by itself on its own beat, but leaving the bot standing in
            // a company of one until then is a bot on the Bound rung for no reason at all.
            BotSquads.Leave(member);
            _squad = null;

            return BotDoing.Failed($"nobody was free to come at {_quarry.Name}");
        }

        _squad.Engage(_quarry, member);

        // Nobody else goes for it while the company has it. The claim is the same one a lone hunter makes,
        // and it is renewed for as long as this undertaking is held.
        BotQuarry.Claim(body, _quarry);

        _engaged = true;

        return BotDoing.Work($"{_called} of us on {_quarry.Name}");
    }

    /// <summary>
    /// Watching it happen, which is all this does.
    ///
    /// The squad's own beat moves everybody, points them at the focus and divides what comes off it. What is
    /// left here is knowing when it is over — and being the undertaking the takings are counted against,
    /// which is measured from the bot's own purse and needs no help from this file.
    /// </summary>
    private BotDoing Fighting(IBotSquadMember member)
    {
        var squad = member.Squad;

        if (squad == null || !ReferenceEquals(squad, _squad))
        {
            // Disbanded under it, or the bot was taken out of it. Finished rather than failed: the fight
            // happened, whatever came of it is already counted, and nothing about this patch of ground was
            // proved bad.
            return BotDoing.Done($"the company broke up around {_quarry?.Name ?? "it"}");
        }

        if (!Standing())
        {
            return BotDoing.Done($"{_quarry?.Name ?? "it"} went down to {_called} of us");
        }

        // Given up on by the squad's own judgement — four minutes, or health that will not move. That is the
        // company deciding, not this, and there is nothing left for the caller to hold on to.
        if (!ReferenceEquals(squad.Focus, _quarry))
        {
            return BotDoing.Done($"the company broke off {_quarry?.Name ?? "it"}");
        }

        BotQuarry.Claim(member.Self, _quarry);

        return BotDoing.Work($"{_called} of us on {_quarry.Name}");
    }

    /// <summary>Over, however it ended. The claim goes back; the squad lives its own life from here.</summary>
    public override void Drop(IBotWilful bot) => BotQuarry.Release(_quarry);

    /// <summary>Whether the quarry is still something to fight.</summary>
    private bool Standing() =>
        _quarry is { Deleted: false, Alive: true } && _quarry.Map == _map;
}
